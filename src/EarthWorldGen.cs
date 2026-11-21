using System;
using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Server worldgen for Earth worlds: the wiring of Earth's passes into vanilla's world generation, and their order.
    ///  1. Terrain pass: vanilla's GenTerra handler is replaced (in the pass's public handler list) and every chunk column is
    ///     built from the terrain model instead (<see cref="TerrainChunkWriter"/>). After vanilla's block layers in the same pass, the surface pass: the
    ///     river ramps (<see cref="RiverBlockPass"/>), then each surface column (<see cref="AlpineSurfacePass"/>), both from what
    ///     the terrain pass handed over (<see cref="TerrainHandoff"/>); then the known hot springs (<see cref="SpringsAndPonds"/>).
    ///  2. Vegetation pass, after caves and ores: the rock floor (<see cref="RockFloorPass"/>).
    ///  3. Map regions: the climate map gets the real temperature and rainfall, the ocean map the real sea, the forest and shrub
    ///     maps the real climate's potentials (<see cref="RegionMaps"/>).
    ///  4. Story locations: moved onto suitable real land near where vanilla would put them (see StoryPlacer).
    ///  5. Vanilla's own generators, guarded by patches: block layers (<see cref="BlockLayerAdjustments"/>), structures
    ///     (<see cref="StructurePlacementGuards"/>), ponds and springs (<see cref="SpringsAndPonds"/>), creatures
    ///     (<see cref="SpawnRangeGuards"/>), prospecting (<see cref="RockFloorPass"/>), trees (EarthTrees), corals (EarthCorals).
    ///     They read the world being generated from <see cref="WorldGenerationContext"/>.
    /// ExecuteOrder 0.7: after vanilla (GenMaps 0.1, stories 0.2) and Cylinder World's worldgen (0.6), so our
    /// climate layer wraps theirs and the story locations have already been shifted by Cylinder World.
    /// </summary>
    public class EarthWorldGen : ModStdWorldGen
    {
        ICoreServerAPI sapi;
        EarthMapSystem core;
        Harmony harmony;
        PatchVanilla patches;
        public bool Active { get; private set; }
        /// <summary>Whether StartServerSide ran to its end (its set-up and passes registered): EarthMapSystem refuses an Earth
        /// world without it (CR-L1).</summary>
        internal bool Started { get; private set; }
        TerrainModel model;
        public TerrainModel Model => model;
        /// <summary>The world's noise seed (EarthParams.NoiseSeed), for position hashes outside this class (EarthTrees).</summary>
        internal long NoiseSeed => core?.Params?.NoiseSeed ?? 0;
        /// <summary>Companion planning hook, once per server start, after story locations and before chunks.
        /// Register immutable terrain pads here. Exceptions deliberately abort initialization rather than
        /// generating a save with a partially missing companion plan.</summary>
        public event Action<TerrainModel> TerrainPlanning;
        public bool TerrainReservationPatchGuardReady { get; private set; }
        public RegionalTreeCatalog RegionalTrees => core?.RegionalTrees;
        public RegionalTreeCatalog DesertTreeRanges => core?.DesertTreeRanges;
        public bool TraceTreeSelections => core?.Config?.SelfTest?.Enabled == true;
        internal ICoreServerAPI Api => sapi;
        EarthWaterTable waterTable;
        /// <summary>EA-0054 step 2: this world's water table system for the tree pick's column site (looked up once; the
        /// generator object belongs to the world, so nothing outlives it).</summary>
        internal EarthWaterTable WaterTable => waterTable ??= sapi?.ModLoader.GetModSystem<EarthWaterTable>();

        // this world's generation, set up by InitWorldGen (null before)
        WorldGenRules rules;
        WorldGenerationContext context;
        ChunkSettler settler;
        TerrainHandoff handoff;
        TerrainModel handoffModel;
        EarthElevationSystem elevation;
        /// <summary>The river query's settling scratch, per thread (not the terrain pass's).</summary>
        System.Threading.ThreadLocal<TerrainModel.Column[]> queryScratch;
        TerrainChunkWriter terrain;
        RiverBlockPass rivers;
        AlpineSurfacePass surface;
        RockFloorPass rockFloor;
        /// <summary>(EA-0162) Earth's terrain handler as put in the terrain pass's list (<see cref="InPlaceOfGenTerraHere"/>); null
        /// where Earth's terrain runs as a prefix on vanilla's generator instead (<see cref="GenTerraPrefix"/>).</summary>
        ChunkColumnGenerationDelegate terrainHandler;
        /// <summary>(EA-0162) The surface pass's handler as registered: the terrain pass's handlers before it are the ones that
        /// should have made the chunk with Earth's terrain.</summary>
        ChunkColumnGenerationDelegate surfaceHandler;
        /// <summary>(EA-0162) Chunks of other mods' own generation that reached the surface pass without Earth's terrain.</summary>
        int previewsWithoutTerrain;

        /// <summary>Habitats step 1: the vegetation zones in worlds saved with zone rules, else null.</summary>
        internal ClimateZones Zones => rules?.Zones;

        internal static int FlowDirIndex(float fx, float fz) => RampWord.FlowDirIndex(fx, fz);

        internal static EarthWorldGen Instance;

        /// <summary>The real lapse rate, °C per metre of elevation (TerrainModel.Lapse uses it with a floor at -500 m; the
        /// guards and the surface pass here take the elevation as it is).</summary>
        internal const double LapsePerMetre = 0.0065;

        public override double ExecuteOrder() => 0.7;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            Instance = this;
            // a generator from an earlier server in this process (singleplayer) no longer generates, and its guards' counts
            // aren't this session's (CR-G4)
            WorldGenerationContext.Publish(null);
            SpringsAndPonds.ResetCounters();
            StructurePlacementGuards.ResetCounters();
            harmony = new Harmony(EarthMap.PatchVanilla.HarmonyId);
            BindVanilla();
            if (api.ModLoader.IsModEnabled("watersheds"))
            {
                Mod.Logger.Error("Algernon's Watersheds is installed: it replaces the same terrain generation as Earth Map (and vanilla's maps and block layers). " +
                    "Use one or the other; Earth Map generates its own real rivers.");
            }
            api.Event.InitWorldGenerator(InitWorldGen, "standard");
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => running = true);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, CheckTerrainBeforeRunning);
            api.Event.MapRegionGeneration(OnMapRegionGen, "standard");
            // after vanilla's block layers (GenBlockLayers, 0.4) in the same pass, before any vegetation
            api.Event.ChunkColumnGeneration(surfaceHandler = Guarded("surface pass", BareRock), EnumWorldGenPass.Terrain, "standard");
            // after vanilla's caves (0.3), which mark their hot springs in the same pass, before GenHotSprings (TerrainFeatures)
            api.Event.ChunkColumnGeneration(Guarded("known hot springs", KnownHotSprings), EnumWorldGenPass.Terrain, "standard");
            // (FP-13) a terrain pass cut short before the surface pass
            api.Event.ChunkColumnGeneration(Guarded("terrain pass", SurfacePassRan), EnumWorldGenPass.TerrainFeatures, "standard");
            // after caves and ores (TerrainFeatures), before sunlight
            api.Event.ChunkColumnGeneration(Guarded("rock floor", FillRockFloor), EnumWorldGenPass.Vegetation, "standard");
            // decision 0070: after every plant pass (block patches scatter round a centre the patch check sees alone)
            api.Event.ChunkColumnGeneration(Guarded("shore strip plants", ClearShorePlants), EnumWorldGenPass.PreDone, "standard");
            Started = true;
        }

        /// <summary>
        /// (FP-13, the Fable pass of the 14 November 2026 code review) One of Earth's chunk column handlers with the net every
        /// one of them needs. After a refusal, or once this generator is closed (<see cref="Dispose"/>, FP-01), a chunk that
        /// reaches it is noted to be made again and its pass throws, so no chunk in flight finishes a pass half Earth's and is
        /// saved so. A throw of the handler is a failed pass (<see cref="FailedPass"/>: noted, the server stops), as for the
        /// geology and ore passes; before, these handlers had no catch and the game kept the chunk with half a pass. Earth's
        /// other systems register theirs through <see cref="Guard"/>.
        /// </summary>
        internal ChunkColumnGenerationDelegate Guarded(string pass, Action<IChunkColumnGenerateRequest> handler) => request =>
        {
            ThrowIfRefused(request);
            // (FP-11/FP-12) generation: this thread may wait for GLO-90 tiles for the handler's length; (EA-0099) a chunk pass,
            // which a square that can't be had leaves unmade
            using var fetch = FineElevation.Generating();
            using var inPass = FineElevation.ChunkPass();
            try { handler(request); }
            catch (Exception e) when (e is not RefusedException) { Failed(pass, request.Chunks, request.ChunkX, request.ChunkZ, e); }
        };

        /// <summary><see cref="Guarded"/> for a handler of another of Earth's systems (any start order: the mod systems exist
        /// before any of them starts).</summary>
        internal static ChunkColumnGenerationDelegate Guard(ICoreServerAPI api, string pass, Action<IChunkColumnGenerateRequest> handler)
            => api.ModLoader.GetModSystem<EarthWorldGen>() is EarthWorldGen gen ? gen.Guarded(pass, handler) : request => handler(request);

        /// <summary>What Earth throws when it refuses a world or a chunk after a refusal: a pass's catch lets it through (the
        /// chunk is noted already) instead of reporting it as the pass's own failure.</summary>
        internal sealed class RefusedException : InvalidOperationException
        {
            public RefusedException(string message, Exception inner = null) : base(message, inner) { }
        }

        /// <summary>
        /// Earth's patches on vanilla's world generation, all bound through PatchVanilla's one table: the terrain pass, block
        /// layers, tall grass, trees, corals, the structure, pond, spring and creature guards and prospecting. Each does nothing
        /// while Earth is inactive in the session; a feature whose game members are missing or changed on this game version is
        /// logged once and left to vanilla.
        /// </summary>
        void BindVanilla()
        {
            string game = sapi.ModLoader.GetMod("game")?.Info?.Version ?? Vintagestory.API.Config.GameVersion.ShortGameVersion;
            patches = new PatchVanilla(harmony, Mod.Info.Version, game, line => Mod.Logger.Warning("{0}", line), line => Mod.Logger.Error("{0}", line));
            // (FP-01) the terrain handler in this server's list asks this generator, also once Dispose has closed it
            patches.Apply(EarthMap.PatchVanilla.Table(sapi.Event.GetRegisteredWorldGenHandlers("standard"), InPlaceOfGenTerraHere));
            TerrainReservationPatchGuardReady = patches.On(EarthMap.PatchVanilla.Feature.PatchReservations);
        }

        /// <summary>PatchVanilla's second way for Earth's terrain, where another mod has wrapped GenTerra's handler before Earth
        /// could: a prefix on GenTerra's own method (which that wrapper calls), the same as <see cref="InPlaceOfGenTerra"/>.</summary>
        internal static bool GenTerraPrefix([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var gen = Instance;
            gen?.ThrowIfRefused(request);
            if (gen == null || !gen.Active) return true;
            gen.CheckFeaturesFor(request);
            gen.MakeTerrain(request);
            return false;
        }

        /// <summary>Set by <see cref="Refuse"/>, and by <see cref="Dispose"/> in an Earth world (FP-01): this Earth world is
        /// stopping and nothing more is generated in it.</summary>
        volatile bool refused;

        /// <summary>The game runs (RunGame): before, nothing of the world is saved.</summary>
        volatile bool running;

        /// <summary>
        /// (FP-01) The server stops before its game runs (a player leaving a world that is still loading its spawn area): a
        /// map region's handler that waited for a tile goes on without it, since nothing of the world is saved yet. The
        /// spawn area is made on the server's main thread, where an exception out of a region's handlers (the game guards
        /// none) ends the start as a fatal error.
        /// </summary>
        internal static bool StoppingBeforeRunning => Instance is { } gen && !gen.running;

        /// <summary>
        /// A chunk that reaches one of Earth's handlers after a refusal (the generation threads go on for the seconds before the
        /// server stops) or after <see cref="Dispose"/>: Active is off and the context withdrawn by then, so vanilla's terrain
        /// would make it, or a later pass would run with vanilla's guards. It is noted for the next start and its pass throws
        /// instead (M4 of the Fable pass: before, a later pass let it through un-noted).
        /// </summary>
        void ThrowIfRefused(IChunkColumnGenerateRequest request) => ThrowIfRefused(request.Chunks, request.ChunkX, request.ChunkZ);

        void ThrowIfRefused(IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            // EA-0099: a chunk whose terrain waits for a GLO-90 square gets none of Earth's later passes either (already noted)
            if (WaitsForHeights(chunkX, chunkZ))
                throw new RefusedException("chunk " + chunkX + "," + chunkZ + " waits for its GLO-90 heights; it is made at the next start.");
            if (!refused) return;
            NoteChunk(chunks, chunkX, chunkZ, passFailed: false);
            throw new RefusedException("Earth refused this world; chunk " + chunkX + "," + chunkZ + " is made at the next start.");
        }

        /// <summary>(FP-13) A refusal is in force: Earth's passes that are patches on vanilla's (geology, ores) check it for each
        /// chunk as the registered handlers do (<see cref="Guarded"/>), and throw for a chunk in flight.</summary>
        internal static void ThrowIfRefusedChunk(IServerChunk[] chunks, int chunkX, int chunkZ) => Instance?.ThrowIfRefused(chunks, chunkX, chunkZ);

        /// <summary>
        /// Earth's terrain for one chunk column. A throw is a refusal (CR-G2): the game catches it, skips the terrain pass's
        /// other handlers (the surface pass's check among them) and still saves the chunk past that pass, so the chunk is noted
        /// for deletion at the next start (<see cref="NoteChunk"/>) and the server stops. The server stopping while the chunk
        /// waits for a GLO-90 tile (FP-01) only notes it: the thread pauses, the game saves the chunk empty past the pass, and the
        /// next start makes it again.
        /// </summary>
        void MakeTerrain(IChunkColumnGenerateRequest request)
        {
            try
            {
                using var inPass = FineElevation.ChunkPass();
                terrain.Generate(request);
            }
            catch (TileUnavailableException e)
            {
                LeaveForNextStart(request.Chunks, request.ChunkX, request.ChunkZ, "terrain", e);
                throw;
            }
            catch (ServerStoppingException e)
            {
                NoteChunk(request.Chunks, request.ChunkX, request.ChunkZ, passFailed: false);
                Mod.Logger.Notification("Earth: chunk {0},{1} waited for a GLO-90 tile when the server stopped ({2}); it is made again at the next start.", request.ChunkX, request.ChunkZ, e.Message);
                throw;
            }
            catch (Exception e) when (e is not RefusedException)
            {
                Mod.Logger.Error("Earth's terrain for chunk {0},{1} failed: {2}", request.ChunkX, request.ChunkZ, e);
                NoteChunk(request.Chunks, request.ChunkX, request.ChunkZ, passFailed: false);
                Refuse("its terrain for chunk " + request.ChunkX + "," + request.ChunkZ + " could not be made (" + e.Message + "); that chunk is made again at the next start");
            }
        }

        /// <summary>
        /// One of Earth's later passes for a chunk column threw (rock strata, surface cover and ground, the ore layer pass, the
        /// deposit filter, Earth's ores, and every handler registered through <see cref="Guarded"/>): as for the terrain
        /// (CR-G2), the chunk is noted to be made again at the next start (<see cref="NoteChunk"/>) and the server stops, so no
        /// chunk with vanilla's blocks where Earth's belong, or with half a pass, stays in the save. Before, each pass logged
        /// three errors a session, "vanilla blocks are kept there", and went on (ER-08, code review 14 November 2026). It refuses
        /// at the first failure: a chunk noted while the game went on would be deleted at the next start with whatever players
        /// had built in it since. Called from the pass's catch; throws (the game catches it and stops the chunk's pass), except:
        /// - FP-14 (the Fable pass): a chunk that a failed pass had noted in an earlier start, deleted and made anew, which fails
        ///   again: the failure is a function of the place (a deposit of another content mod, a case of the data), and refusing
        ///   again would stop the server whenever a player comes near. It is kept as the pass left it, said loudly, and the
        ///   world goes on (the behaviour before ER-08, for that chunk only).
        /// - the server stopping during the pass (FP-01): noted, no refusal.
        /// - a chunk the game only peeks at (a static translocator's search, M2 of the Fable pass): nothing is noted, since the
        ///   coordinates are a real chunk's that this pass never made; the server still stops, as the same failure would in
        ///   that chunk's real generation.
        /// Without Earth's generation (no instance) it does nothing.
        /// </summary>
        internal static void FailedPass(string pass, IServerChunk[] chunks, int chunkX, int chunkZ, Exception e) => Instance?.Failed(pass, chunks, chunkX, chunkZ, e);

        void Failed(string pass, IServerChunk[] chunks, int chunkX, int chunkZ, Exception e)
        {
            if (e is RefusedException || refused)
            {
                NoteChunk(chunks, chunkX, chunkZ, passFailed: false);
                throw new RefusedException("Earth refused this world; chunk " + chunkX + "," + chunkZ + " is made at the next start.", e);
            }
            if (e is TileUnavailableException || e.InnerException is TileUnavailableException)
            {
                LeaveForNextStart(chunks, chunkX, chunkZ, pass, e.InnerException as TileUnavailableException ?? (TileUnavailableException)e);
                throw new RefusedException("chunk " + chunkX + "," + chunkZ + " waits for its GLO-90 heights; it is made at the next start.", e);
            }
            if (e is ServerStoppingException || e.InnerException is ServerStoppingException)
            {
                NoteChunk(chunks, chunkX, chunkZ, passFailed: false);
                Mod.Logger.Notification("Earth: chunk {0},{1} waited for a GLO-90 tile in its {2} when the server stopped; it is made again at the next start.", chunkX, chunkZ, pass);
                throw new RefusedException("the server is stopping; chunk " + chunkX + "," + chunkZ + " is made at the next start.", e);
            }
            if (!IsPeek(chunks, chunkX, chunkZ) && Retried(chunkX, chunkZ))
            {
                Mod.Logger.Error("Earth's {0} for chunk {1},{2} failed again after that chunk was made anew (an earlier start stopped at its failure there): " +
                    "it is kept as the pass left it, which can leave vanilla blocks or half a pass where Earth's belong, and the world goes on, so that " +
                    "this place can be entered. The cause is in the error: a block or deposit of another content mod (remove or update that mod), or a " +
                    "fault in Earth (report it with this log). {3}", pass, chunkX, chunkZ, e);
                return;
            }
            NoteChunk(chunks, chunkX, chunkZ, passFailed: true);
            if (!running && !IsPeek(chunks, chunkX, chunkZ)) KeepOutsideSave(chunkX, chunkZ);
            Mod.Logger.Error("Earth's {0} for chunk {1},{2} failed: {3}", pass, chunkX, chunkZ, e);
            Refuse("its " + pass + " for chunk " + chunkX + "," + chunkZ + " could not be made (" + e.Message + "); that chunk is made again at the next start. " +
                "If it fails there again, Earth keeps it as that pass left it and says so; the error above names the cause: a block or deposit of " +
                "another content mod (remove or update that mod), or a fault in Earth (report it with this log)");
        }

        /// <summary>
        /// (M2 of the Fable pass) Whether the chunks are the game's peek at a place (a static translocator's search runs the
        /// passes on throwaway chunks at real coordinates): their map chunk is a new one the game never stores, while a chunk
        /// generated for the world has the world's map chunk at its coordinates (Lib ServerSystemSupplyChunks:
        /// PeekChunkAreaLocking makes its own map chunks; GetOrCreateMapChunk stores the world's in loadedMapChunks, which
        /// WorldManager.GetMapChunk reads). Without chunks to tell by: not a peek.
        /// </summary>
        bool IsPeek(IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            var mine = chunks != null && chunks.Length > 0 ? chunks[0]?.MapChunk : null;
            if (mine == null) return false;
            try { return !ReferenceEquals(sapi.WorldManager.GetMapChunk(chunkX, chunkZ), mine); }
            catch (Exception) { return false; }
        }

        // ---- EA-0099 (the Fable pass's FP-03): chunks a GLO-90 square that can't be had leaves for the next start

        /// <summary>Chunk columns of this session whose pass threw <see cref="TileUnavailableException"/>.</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<(int x, int z), byte> waitingForHeights = new();

        bool WaitsForHeights(int chunkX, int chunkZ) => !waitingForHeights.IsEmpty && waitingForHeights.ContainsKey((chunkX, chunkZ));

        /// <summary>
        /// EA-0099, option (a), the author's decision (14 November 2026), taken knowing that the chunks left unmade are open
        /// holes a player can fall into and that anything built in them is deleted when they are made at the next start; no
        /// placeholder floor. While the game runs, a chunk whose GLO-90 square can't be fetched
        /// (the network, the release down, a full disk) is not made: it is noted (<see cref="NoteChunk"/>, no refusal: the
        /// world goes on), the game saves it empty past the pass, Earth's later passes skip it (<see cref="ThrowIfRefused"/>),
        /// and the next start deletes it so that it is made again, on the square's heights once they are here. The players
        /// are told by EarthElevationSystem. Before, it was made on the built-in heights and met later chunks with a step.
        /// </summary>
        void LeaveForNextStart(IServerChunk[] chunks, int chunkX, int chunkZ, string pass, TileUnavailableException e)
        {
            NoteChunk(chunks, chunkX, chunkZ, passFailed: false);
            if (IsPeek(chunks, chunkX, chunkZ)) return;
            if (waitingForHeights.TryAdd((chunkX, chunkZ), 0))
                Mod.Logger.Notification("Earth: chunk {0},{1} is not made in this session ({2}: {3}); it is made at the next start.", chunkX, chunkZ, pass, e.Message);
        }

        /// <summary>The save's list of chunk columns Earth refused or could not make the terrain of (pairs of ints, chunk x then z).</summary>
        const string FailedChunksKey = "earth-failed-chunks";
        /// <summary>(FP-14) Those of <see cref="FailedChunksKey"/> that a failed pass noted (same format), not a refusal elsewhere
        /// or a stop: at the next start they move to <see cref="RetriedChunksKey"/>.</summary>
        const string FailedPassChunksKey = "earth-failed-pass-chunks";
        /// <summary>(FP-14) Chunk columns made anew after a failed pass (same format): a second failure there keeps the chunk.</summary>
        const string RetriedChunksKey = "earth-retried-chunks";
        static readonly object failedChunksLock = new object();
        /// <summary>This world's <see cref="RetriedChunksKey"/>, read when its generation is set up.</summary>
        HashSet<(int x, int z)> retried = new();

        bool Retried(int chunkX, int chunkZ)
        {
            lock (failedChunksLock) return retried.Contains((chunkX, chunkZ));
        }

        /// <summary>
        /// Notes a chunk column the game will save although Earth didn't make it as the world's rules say (CR-G2): the game
        /// advances a chunk past a pass that threw and saves it, so without this the chunk would stay in the save for good. The
        /// next start deletes it once the game runs (<see cref="DeleteFailedChunks"/>). A peeked chunk is not noted
        /// (<see cref="IsPeek"/>). <paramref name="passFailed"/>: Earth's own pass failed there (FP-14's retry). Never throws.
        /// </summary>
        void NoteChunk(IServerChunk[] chunks, int cx, int cz, bool passFailed)
        {
            if (IsPeek(chunks, cx, cz)) return;
            try
            {
                lock (failedChunksLock)
                {
                    var save = sapi.WorldManager.SaveGame;
                    Add(save, FailedChunksKey, cx, cz);
                    if (passFailed) Add(save, FailedPassChunksKey, cx, cz);
                }
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth could not note chunk column {0},{1} for deletion ({2}): it keeps what was saved of it; delete or regenerate that chunk column by hand before the world is played.", cx, cz, e.Message);
            }

            static void Add(ISaveGame save, string key, int cx, int cz)
            {
                var list = FailedChunks(save.GetData(key));
                if (!list.Contains((cx, cz))) list.Add((cx, cz));
                save.StoreData(key, FailedChunksBytes(list));
            }
        }

        /// <summary>
        /// (FP-14) When this world's generation is set up: the chunks a failed pass noted in an earlier start join the retried
        /// ones (a second failure there keeps the chunk; <see cref="Failed"/>), and those are read. Saved with the world from the
        /// next save on (a start that stops before the game runs saves nothing, and leaves the lists as they were).
        /// </summary>
        void ReadRetriedChunks()
        {
            var save = sapi.WorldManager.SaveGame;
            lock (failedChunksLock)
            {
                try
                {
                    var known = FailedChunks(save.GetData(RetriedChunksKey));
                    foreach (var c in FailedChunks(save.GetData(FailedPassChunksKey))) if (!known.Contains(c)) known.Add(c);
                    // a start that stopped before its game ran (a new world's spawn area) kept its failed chunks outside the save
                    string kept = StartingFile;
                    if (kept != null && System.IO.File.Exists(kept))
                        foreach (var c in FailedChunks(System.IO.File.ReadAllBytes(kept))) if (!known.Contains(c)) known.Add(c);
                    save.StoreData(RetriedChunksKey, FailedChunksBytes(known));
                    save.StoreData(FailedPassChunksKey, Array.Empty<byte>());
                    retried = new HashSet<(int x, int z)>(known);
                }
                catch (Exception e)
                {
                    Mod.Logger.Error("Earth's list of chunks made anew after a failed pass can't be read ({0}); it is dropped.", e.Message);
                    save.StoreData(RetriedChunksKey, Array.Empty<byte>());
                    save.StoreData(FailedPassChunksKey, Array.Empty<byte>());
                    retried = new HashSet<(int x, int z)>();
                }
            }
        }

        /// <summary>The chunk columns <see cref="NoteChunk"/> noted in earlier starts, deleted (the game queues it and deletes them
        /// on its chunk thread's next tick; they generate again when asked for). Called when the game starts running (L1 of the
        /// Fable pass: called earlier, before the spawn area loaded, a noted chunk there was loaded after the unload and played as
        /// it was); the list is emptied (a start that stops before the game runs saves nothing, so it keeps the list).</summary>
        void DeleteFailedChunks()
        {
            var save = sapi.WorldManager.SaveGame;
            byte[] stored = save.GetData(FailedChunksKey);
            if (stored == null || stored.Length == 0) return;
            List<(int x, int z)> chunks;
            try { chunks = FailedChunks(stored); }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth's list of chunks to make again can't be read ({0}); it is dropped.", e.Message);
                save.StoreData(FailedChunksKey, Array.Empty<byte>());
                return;
            }
            foreach (var (x, z) in chunks) sapi.WorldManager.DeleteChunkColumn(x, z);
            save.StoreData(FailedChunksKey, Array.Empty<byte>());
            if (chunks.Count > 0)
                Mod.Logger.Warning("Earth: {0} chunk column(s) that Earth refused, could not make, or left unfinished when the server stopped in an earlier start are deleted and generate again: {1}.",
                    chunks.Count, string.Join("; ", chunks.Select(c => c.x + "," + c.z)));
        }

        static List<(int x, int z)> FailedChunks(byte[] stored)
        {
            var chunks = new List<(int x, int z)>();
            if (stored == null) return chunks;
            if (stored.Length % 8 != 0) throw new System.IO.InvalidDataException("length " + stored.Length + " is not a multiple of 8");
            for (int i = 0; i < stored.Length; i += 8) chunks.Add((BitConverter.ToInt32(stored, i), BitConverter.ToInt32(stored, i + 4)));
            return chunks;
        }

        static byte[] FailedChunksBytes(List<(int x, int z)> chunks)
        {
            var bytes = new byte[chunks.Count * 8];
            for (int i = 0; i < chunks.Count; i++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 8, 4), chunks[i].x);
                BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 8 + 4, 4), chunks[i].z);
            }
            return bytes;
        }

        /// <summary>
        /// An enabled Earth world never generates without Earth's terrain (Earth's guards, surface, geology and ores on vanilla's
        /// terrain would give an existing save chunks that never fit): when Earth's terrain is not in place (PatchVanilla could
        /// not bind it, or a mod starting later replaced it), one error line names the cause, Earth turns inactive and the server
        /// stops, as RequiredMods.Enforce does. Checked when the world's generation is set up, again when the game starts
        /// running, and by the surface pass for every chunk (it finds the terrain pass's handoff missing).
        /// </summary>
        void Refuse(string cause)
        {
            string message = "Earth cannot generate this world: " + cause + ". Earth's terrain must replace vanilla's in an Earth world " +
                "(a mod wrapping or replacing vanilla's terrain generator, or a game update, keeps it out); remove the cause and restart. " +
                "Nothing more is generated, so this world never gets Earth's other passes on vanilla terrain.";
            refused = true;
            Active = false;
            WorldGenerationContext.Withdraw(context);
            Mod.Logger.Error("{0}", message);
            sapi.Server.ShutDown();
            throw new RefusedException(message);
        }

        /// <summary><see cref="Refuse(string)"/> with its own advice in place of the terrain's (decision 0083).</summary>
        void Refuse(string cause, string advice)
        {
            string message = "Earth cannot generate this world: " + cause + ". " + advice + " Nothing more is generated in this session.";
            refused = true;
            Active = false;
            WorldGenerationContext.Withdraw(context);
            Mod.Logger.Error("{0}", message);
            sapi.Server.ShutDown();
            throw new RefusedException(message);
        }

        /// <summary>The terrain check at the last moment before the game runs: a mod that changed Earth's terrain handler after
        /// Earth's set-up is caught here. Then the chunks earlier starts noted are deleted (L1 of the Fable pass: the spawn area
        /// is loaded by now, so the unload finds a noted chunk there).</summary>
        void CheckTerrainBeforeRunning()
        {
            if (!Active) return;
            CheckTerrain();
            // (decision 0083) also for a world whose start made no chunk
            CheckFeatures();
            DeleteFailedChunks();
            // the chunks a start before this one kept outside the save are in the save's retried list from here on
            try { if (StartingFile is string kept && System.IO.File.Exists(kept)) System.IO.File.Delete(kept); }
            catch (Exception e) { Mod.Logger.Warning("Earth could not remove {0} ({1}); it is read again at the next start, which does no harm.", StartingFile, e.Message); }
        }

        /// <summary>
        /// (The Fable reviewer's note on 854af7b) Where a start before its game runs keeps the chunks a failed pass noted:
        /// nothing is saved before RunGame, so a new world whose spawn area failed at the same chunk at every start never
        /// reached the retry (<see cref="Retried"/>) and could never be made. Keyed by the save's identifier and seed; read when
        /// the next start sets generation up, removed once a start's game runs. Null where the save has no identifier.
        /// </summary>
        string StartingFile
        {
            get
            {
                var save = sapi?.WorldManager?.SaveGame;
                string id = save?.SavegameIdentifier;
                if (string.IsNullOrEmpty(id)) return null;
                var safe = new string(id.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
                return System.IO.Path.Combine(EarthGround.DataFolder, "starting-" + safe + "-" + save.Seed + ".bin");
            }
        }

        void KeepOutsideSave(int cx, int cz)
        {
            try
            {
                string file = StartingFile;
                if (file == null) return;
                lock (failedChunksLock)
                {
                    var list = System.IO.File.Exists(file) ? FailedChunks(System.IO.File.ReadAllBytes(file)) : new List<(int x, int z)>();
                    if (list.Contains((cx, cz))) return;
                    list.Add((cx, cz));
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    System.IO.File.WriteAllBytes(file, FailedChunksBytes(list));
                }
            }
            catch (Exception e) { Mod.Logger.Warning("Earth could not keep chunk {0},{1} for the next start's retry ({2}).", cx, cz, e.Message); }
        }

        bool saidUnverified;

        /// <summary>
        /// Refuses the world when Earth's terrain writer is provably not installed (PatchVanilla could not bind it, its patch was
        /// removed, or vanilla's own terrain handler is back in the list). A mod that replaced Earth's handler in the list may
        /// be calling it from its own (a wrapper): that can't be told here, so it is said once and the surface pass's check of
        /// every chunk decides (Codex round 3 item 3: such a healthy chain was refused before).
        /// </summary>
        void CheckTerrain()
        {
            if (patches.StillInPlace(EarthMap.PatchVanilla.Feature.Terrain) is string cause) Refuse(cause);
            if (saidUnverified) return;
            // (EA-0162) also where Earth's terrain runs inside vanilla's generator: a mod replaced vanilla's handler before Earth started
            string unverified = patches.Unverified(EarthMap.PatchVanilla.Feature.Terrain)
                ?? (terrainHandler == null && patches.On(EarthMap.PatchVanilla.Feature.Terrain) ? "Earth's terrain runs inside vanilla's terrain generator (GenTerra), whose handler another mod replaced or wrapped before Earth started" : null);
            if (unverified != null)
            {
                saidUnverified = true;
                Mod.Logger.Warning("Earth's terrain: {0}. Earth can't tell from here whether that mod still calls Earth's terrain writer, so every " +
                    "chunk is checked as it generates: the first one made without it stops the server before Earth's other passes run on it. Earth: {1}.",
                    unverified, TerrainPassListing());
            }
        }

        /// <summary>(EA-0162) The terrain pass's handlers in order, each named by its mod, for the log.</summary>
        string TerrainPassListing()
        {
            try
            {
                var mods = ModsByAssembly();
                var list = sapi.Event.GetRegisteredWorldGenHandlers(sapi.WorldManager.SaveGame.WorldType)?.OnChunkColumnGen?[(int)EnumWorldGenPass.Terrain]?.ToArray()
                    ?? Array.Empty<ChunkColumnGenerationDelegate>();
                return TerrainPassReport.Listing(TerrainPassReport.Read(list, terrainHandler, a => ModName(mods, a)), terrainHandler == null, list.Any(d => d?.Target is GenTerra));
            }
            catch (Exception e) { return "the terrain pass's handlers could not be read (" + e.Message + ")"; }
        }

        /// <summary>Vanilla's terrain pass handler (GenTerra's), wrapped in the terrain pass's handler list by PatchVanilla: in an
        /// active Earth world <see cref="TerrainChunkWriter"/> makes the chunk instead, otherwise vanilla does. (The table's
        /// default, for the tests; a server binds <see cref="InPlaceOfGenTerraHere"/>.)</summary>
        internal static ChunkColumnGenerationDelegate InPlaceOfGenTerra(ChunkColumnGenerationDelegate genTerra) => request =>
        {
            var gen = Instance;
            gen?.ThrowIfRefused(request);
            if (gen == null || !gen.Active) genTerra(request);
            else gen.MakeTerrain(request);
        };

        /// <summary>
        /// <see cref="InPlaceOfGenTerra"/> bound to this generator (FP-01): the handler list is this server's, and a generation
        /// thread the game could not pause before it disposed the mods still asks it. Through <see cref="Instance"/>, which
        /// Dispose clears, that thread got vanilla's terrain; this generator, closed by Dispose, notes the chunk and throws.
        /// </summary>
        ChunkColumnGenerationDelegate InPlaceOfGenTerraHere(ChunkColumnGenerationDelegate genTerra) => terrainHandler = request =>
        {
            ThrowIfRefused(request);
            if (!Active) genTerra(request);
            else
            {
                CheckFeaturesFor(request);
                MakeTerrain(request);
            }
        };

        // ------------------------------------------------------------------------------------------ the parts a world needs

        /// <summary>0: the parts of generation not checked yet this start (<see cref="CheckFeatures"/>).</summary>
        int featuresChecked;

        /// <summary><see cref="CheckFeatures"/> before a chunk's terrain; a refusal there notes the chunk (once the game runs it
        /// is saved past the pass that threw).</summary>
        void CheckFeaturesFor(IChunkColumnGenerateRequest request)
        {
            try { CheckFeatures(); }
            catch (RefusedException) { NoteChunk(request.Chunks, request.ChunkX, request.ChunkZ, passFailed: false); throw; }
        }

        /// <summary>
        /// (Decision 0083; FP-02, the Fable pass of the 14 November 2026 code review) Once a start, before the first chunk's
        /// terrain (all of the world's set-ups have run by then: some come with the world generator's set-up after this one)
        /// and when the game starts running: every part of generation this world's saved rules use is asked whether it is on.
        /// When one is off (a hook a game update changed, a competing mod, a missing or unreadable file, a block a removed mod
        /// supplied), the world is refused with all of them named; before, each part logged one line and the chunks of that
        /// session kept vanilla's blocks there for good. earth.json's AcceptMissingFeatures lets an owner generate without them.
        /// </summary>
        void CheckFeatures()
        {
            if (System.Threading.Interlocked.Exchange(ref featuresChecked, 1) != 0) return;
            var p = core?.Params;
            if (p?.Enabled != true) return;
            var off = new List<string>();
            try { FeaturesOff(p, off); }
            catch (Exception e) { off.Add("the check of the parts of generation itself (" + e.Message + ")"); }
            if (off.Count == 0) return;
            string list = string.Join("; ", off);
            if (core.Config?.AcceptMissingFeatures == true)
            {
                Mod.Logger.Warning("Earth: parts of generation this world's rules use are off this session, and earth.json's AcceptMissingFeatures lets it " +
                    "generate without them: {0}. New chunks get vanilla's blocks there and may meet other chunks with a seam.", list);
                return;
            }
            Refuse("parts of generation this world's rules use are off this session: " + list,
                "Each has its line above. Update or remove the mod that keeps one out, use an Earth version made for this game version, " +
                "reinstall Earth, or let it fetch a data file again (a file in ModData/earth that is missing or damaged); nothing of this world is changed. An owner who accepts seams where new chunks lack them sets AcceptMissingFeatures " +
                "to true in earth.json (decision 0083).");
        }

        /// <summary>What <see cref="CheckFeatures"/> finds off, each where this world's saved rules use it (decision 0083).</summary>
        void FeaturesOff(EarthParams p, List<string> off)
        {
            var mods = sapi.ModLoader;
            mods.GetModSystem<EarthGeology>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthOres>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthSprings>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthWaterTable>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthPlacement>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthPlants>()?.FeaturesOff(off);
            mods.GetModSystem<EarthVolcanoes>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthDepositDepths>()?.FeaturesOff(p, off);
            mods.GetModSystem<EarthVanillaStrata>()?.FeaturesOff(p, off);
            if (!climateMaps) off.Add("Earth's climate and ocean maps (vanilla's GenMaps was not found)");
            if (rules != null) off.AddRange(rules.Missing);
            // Earth's guards on vanilla's generation that change blocks (not: the animal range guards, whose animals move and
            // die; prospecting readings; the companions' reserved-site guard, which each companion checks itself)
            foreach (var (feature, used) in new (EarthMap.PatchVanilla.Feature, bool)[]
            {
                (EarthMap.PatchVanilla.Feature.AridBasins, true),
                (EarthMap.PatchVanilla.Feature.SoilFertility, p.SoilRules >= 1),
                (EarthMap.PatchVanilla.Feature.Topsoil, Topsoil.On(p.Topsoil)),
                (EarthMap.PatchVanilla.Feature.TallGrass, p.GroundCoverRules >= 1),
                (EarthMap.PatchVanilla.Feature.TallGrassAroundStructures, p.GroundCoverRules >= 1),
                (EarthMap.PatchVanilla.Feature.TreeSpecies, p.RegionalTrees),
                (EarthMap.PatchVanilla.Feature.CoralSalinity, true),
                (EarthMap.PatchVanilla.Feature.StructuresWherePeopleLived, true),
                (EarthMap.PatchVanilla.Feature.VillagesWherePeopleLived, true),
                (EarthMap.PatchVanilla.Feature.SurfaceRuinsOutOfWater, true),
                (EarthMap.PatchVanilla.Feature.SurfaceStructuresOutOfWater, true),
                (EarthMap.PatchVanilla.Feature.ShallowWaterStructures, true),
                (EarthMap.PatchVanilla.Feature.UndergroundStructures, true),
                (EarthMap.PatchVanilla.Feature.VillageBuildings, true),
                (EarthMap.PatchVanilla.Feature.StructureSizes, true),
                (EarthMap.PatchVanilla.Feature.Ponds, p.PondRules >= 1),
                (EarthMap.PatchVanilla.Feature.Springs, p.SpringRules >= EarthParams.SpringRulesCaves),
                (EarthMap.PatchVanilla.Feature.StorySchematics, true),
            })
                // (Fable's re-review of ad56a74) bound at start and still in place: a mod starting later may have taken it out
                if (used && patches.StillInPlace(feature) is string why) off.Add("Earth's " + feature + " guard on vanilla's generation (" + why + ")");
        }

        /// <summary>Earth's climate and ocean maps are in vanilla's map generator (<see cref="RegionMaps.Install"/>).</summary>
        bool climateMaps = true;

        // ------------------------------------------------------------------------------------------ the passes, in order

        /// <summary>
        /// The surface pass, after vanilla's block layers in the terrain pass: the river ramps, then each surface column by what
        /// it is. This chunk's terrain pass is running now: without the terrain writer's handoff for its chunks, Earth's terrain
        /// did not make it. The game alone always runs the writer first (TerrainPassReport), so another mod keeps Earth's terrain
        /// out: the world is refused, the mod named (EA-0162). Except for a chunk of another mod's own generation (its own request
        /// and its own map chunk, as a far-terrain preview runs the pass's handlers): nothing of it is saved, so it is left alone
        /// and said once.
        /// </summary>
        void BareRock(IChunkColumnGenerateRequest request)
        {
            if (!Active || surface == null) return;
            var s = surface.Begin(request);
            if (!s.MadeByEarth)
            {
                if (RequestSource(request, null) != null && IsPeek(request.Chunks, request.ChunkX, request.ChunkZ))
                {
                    if (System.Threading.Interlocked.Increment(ref previewsWithoutTerrain) == 1)
                    {
                        var f = TerrainPassFindings(request);
                        Mod.Logger.Warning("Earth: chunk {0},{1} of {2} reached Earth's surface pass without Earth's terrain. It is not the world's " +
                            "chunk (nothing of it is saved), so Earth leaves it and later ones alone; what that mod shows of them is not Earth's terrain. " +
                            "Earth: {3}.", request.ChunkX, request.ChunkZ, RequestSource(request, f.Mods), f.Listing);
                    }
                    return;
                }
                var found = TerrainPassFindings(request);
                Mod.Logger.Error("Earth: {0}.", found.Listing);
                // the game saves this chunk anyway (past the pass that threw): noted, it is deleted at the next start (CR-G2)
                NoteChunk(request.Chunks, request.ChunkX, request.ChunkZ, passFailed: false);
                Refuse(found.Cause, found.Advice);
            }
            rivers.PlaceRamps(request.Chunks, request.ChunkX, request.ChunkZ);
            surface.Columns(s);
        }

        /// <summary>
        /// (EA-0162) Why a chunk reached the surface pass without Earth's terrain, named by mod (<see cref="TerrainPassReport"/>):
        /// the terrain pass's handlers before the surface pass, how Earth's terrain is bound, where the chunk came from and other
        /// mods' Harmony patches on vanilla's terrain generator or Earth's code. Never throws (a failure to read is said instead).
        /// </summary>
        (string Cause, string Advice, string Listing, Dictionary<System.Reflection.Assembly, Mod> Mods) TerrainPassFindings(IChunkColumnGenerateRequest request)
        {
            int cx = request.ChunkX, cz = request.ChunkZ;
            try
            {
                var mods = ModsByAssembly();
                string ModOf(System.Reflection.Assembly a) => ModName(mods, a);
                var list = sapi.Event.GetRegisteredWorldGenHandlers(sapi.WorldManager.SaveGame.WorldType)?.OnChunkColumnGen?[(int)EnumWorldGenPass.Terrain]?.ToArray()
                    ?? Array.Empty<ChunkColumnGenerationDelegate>();
                var all = TerrainPassReport.Read(list, terrainHandler, ModOf);
                int surfaceAt = surfaceHandler == null ? -1 : Array.IndexOf(list, surfaceHandler);
                var before = TerrainPassReport.Read(surfaceAt >= 0 ? list.Take(surfaceAt) : list, terrainHandler, ModOf);
                bool viaGenTerra = terrainHandler == null, genTerraIn = list.Any(d => d?.Target is GenTerra);
                var (cause, advice) = TerrainPassReport.Cause(cx, cz, before, viaGenTerra, genTerraIn, RequestSource(request, mods), PatchesOnTerrain());
                return (cause, advice, TerrainPassReport.Listing(all, viaGenTerra, genTerraIn), mods);
            }
            catch (Exception e)
            {
                return ("the terrain pass made chunk " + cx + "," + cz + " without Earth's terrain (a mod keeps Earth's terrain out; the pass's handlers could not be read: " + e.Message + ")",
                    "Earth's terrain must replace vanilla's in an Earth world: remove the mod that wraps or replaces vanilla's terrain generator, then restart.",
                    "the terrain pass's handlers could not be read (" + e.Message + ")", null);
            }
        }

        /// <summary>(EA-0162) Each loaded mod by the assemblies of its mod systems.</summary>
        Dictionary<System.Reflection.Assembly, Mod> ModsByAssembly()
        {
            var mods = new Dictionary<System.Reflection.Assembly, Mod>();
            foreach (var mod in sapi.ModLoader.Mods)
                foreach (var system in mod.Systems ?? (IReadOnlyCollection<ModSystem>)Array.Empty<ModSystem>())
                    if (system != null) mods.TryAdd(system.GetType().Assembly, mod);
            return mods;
        }

        static string ModName(Dictionary<System.Reflection.Assembly, Mod> mods, System.Reflection.Assembly assembly)
        {
            if (mods != null && mods.TryGetValue(assembly, out var mod))
                return (mod.Info?.Name ?? mod.FileName) + " (" + mod.Info?.ModID + " " + mod.Info?.Version + ")";
            return "the assembly " + assembly.GetName().Name;
        }

        /// <summary>(EA-0162) Where a request comes from when it is not the game's own generation (the server's
        /// ChunkColumnLoadRequest, a peek's too): another mod's request type, named by its mod. Null for the game's.</summary>
        static string RequestSource(IChunkColumnGenerateRequest request, Dictionary<System.Reflection.Assembly, Mod> mods)
        {
            var t = request?.GetType();
            if (t == null || t.FullName == "Vintagestory.Server.ChunkColumnLoadRequest") return null;
            return ModName(mods, t.Assembly) + "'s own generation (request type " + t.FullName + ")";
        }

        /// <summary>(EA-0162) Other mods' Harmony patches on vanilla's terrain generator (GenTerra) or on Earth's own code, by
        /// Harmony id and method: a prefix there can keep Earth's terrain from running.</summary>
        static List<string> PatchesOnTerrain()
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            try
            {
                var earth = typeof(EarthWorldGen).Assembly;
                foreach (var m in Harmony.GetAllPatchedMethods())
                {
                    var t = m?.DeclaringType;
                    if (t == null || !(t == typeof(GenTerra) || t.Assembly == earth)) continue;
                    var info = Harmony.GetPatchInfo(m);
                    foreach (var owner in info?.Owners ?? Enumerable.Empty<string>())
                        if (owner != EarthMap.PatchVanilla.HarmonyId) found.Add(owner + " on " + t.Name + "." + m.Name);
                }
            }
            catch (Exception) { }
            return found.ToList();
        }

        /// <summary>
        /// (M1 of the Fable pass) The first of Earth's handlers in the pass after the terrain pass: a chunk whose terrain-writer
        /// flags are still in the handoff had its terrain pass cut short between Earth's terrain and the surface pass (a handler
        /// there threw, Earth's code inside vanilla's block layers among them, and the game skipped the rest of the pass), so it
        /// has no surface pass and no check that Earth's terrain made it: a failed pass (noted, the server stops). A peek's
        /// entries are dropped.
        /// </summary>
        void SurfacePassRan(IChunkColumnGenerateRequest request)
        {
            if (!Active || handoff == null || !handoff.Untaken(request.Chunks, request.ChunkX, request.ChunkZ)) return;
            handoff.Drop(request.Chunks);
            if (IsPeek(request.Chunks, request.ChunkX, request.ChunkZ)) return;
            throw new InvalidOperationException("the terrain pass of chunk " + request.ChunkX + "," + request.ChunkZ + " ended before Earth's surface pass (a handler between them threw; the worldgen log has its error)");
        }

        void KnownHotSprings(IChunkColumnGenerateRequest request)
        {
            if (!Active) return;
            SpringsAndPonds.KnownHotSprings(model, request);
        }

        /// <summary>A chunk whose terrain pass had a rock floor can reach this pass in a session where Earth is off (refused, or
        /// InitWorldGen failed): the block tables are Earth's own.</summary>
        void FillRockFloor(IChunkColumnGenerateRequest request)
        {
            if (!Active || rockFloor == null) return;
            rockFloor.Fill(request);
        }

        /// <summary>
        /// Decision 0070: no plant stands on the shore strip. The tall grass and block patch checks keep most off it, but a
        /// patch scatters its plants round a centre that may lie inland, so whatever plant (tall grass, fern, flower) stands
        /// on a strip column's top when the chunk is otherwise done is taken away. Water plants and reeds stay.
        /// </summary>
        void ClearShorePlants(IChunkColumnGenerateRequest request)
        {
            if (!Active || model == null || !model.Rules.ShoreStrip) return;
            var chunks = request.Chunks;
            var heights = chunks[0].MapChunk.WorldGenTerrainHeightMap;
            var all = sapi.World.Blocks;
            int cx = request.ChunkX * 32, cz = request.ChunkZ * 32, top = chunks.Length * 32 - 1;
            for (int lz = 0; lz < 32; lz++)
                for (int lx = 0; lx < 32; lx++)
                {
                    int y = heights[lz * 32 + lx] + 1;
                    if (y < 1 || y > top) continue;
                    var data = chunks[y >> 5].Data;
                    int idx = ((y & 31) * 32 + lz) * 32 + lx;
                    int id = data.GetBlockIdUnsafe(idx);
                    if (id <= 0 || id >= all.Count || all[id]?.BlockMaterial != EnumBlockMaterial.Plant) continue;
                    // reeds (cattails, cooper's reed, papyrus) keep the water's edge
                    if (all[id].Code?.Path.StartsWith("tallplant-", StringComparison.Ordinal) == true) continue;
                    // (EA-0193) from the chunk's kept shore-strip bits when its columns are no longer stored
                    if (!ColumnStore.OnShoreStrip(model, cx + lx, cz + lz)) continue;
                    // (W-L2) a plant with a block entity (a wild fruiting bush) takes its entity with it, as vanilla's GenRivulets does
                    if (all[id].EntityClass != null) chunks[y >> 5].RemoveBlockEntity(new BlockPos(cx + lx, y, cz + lz, 0));
                    data.SetBlockUnsafe(idx, 0);
                }
        }

        void OnMapRegionGen(IMapRegion region, int regionX, int regionZ, ITreeAttribute chunkGenParams)
        {
            // decision 0075: vanilla biomes keep vanilla's forest and shrub maps (its noise, thinned by Earth's climate as vanilla thins it)
            if (!Active || model.VanillaBiomes) return;
            // (FP-11) a region is made before its chunk's terrain pass: without this a thread that had made none read the
            // built-in heights where a tile wasn't on the machine, and the region's maps were saved so
            using var fetch = FineElevation.Generating();
            try { RegionMaps.ForestAndShrubs(sapi, model, region, regionX, regionZ); }
            catch (ServerStoppingException) when (!running)
            {
                // (FP-01) before the game runs nothing is saved: the start ends without this region's maps
            }
            catch (ServerStoppingException e)
            {
                // (FP-01) the game stores a region only once its handlers return: this one is made again at the next start
                // (the exception ends this thread quietly, as the server is stopping)
                Mod.Logger.Notification("Earth: map region {0},{1} waited for a GLO-90 tile when the server stopped ({2}); it is made again at the next start.", regionX, regionZ, e.Message);
                throw;
            }
        }

        // ------------------------------------------------------------------------------------------ what others ask

        /// <summary>The share (0..1) of vanilla's tall grass kept at a block column; NaN where the rule doesn't apply (the
        /// seam gutter, beyond the poles, or a world without it).</summary>
        internal double TallGrassShareAt(int wx, int wz) => BlockLayerAdjustments.TallGrassShareAt(context, wx, wz);

        /// <summary>/earth where: this world's own vanilla-animal and structure rules at a ground position.</summary>
        internal IEnumerable<string> DescribeRanges(BlockPos ground)
        {
            var w = context;
            if (w == null) yield break;
            yield return StructurePlacementGuards.Describe(w, ground);
            foreach (string line in SpawnRangeGuards.Describe(w, ground)) yield return line;
        }

        /// <summary>The rock floor's top under a column (0: none), for prospecting (RockFloorPass.Top), from the world's map
        /// chunk. World Map binds this two-argument form by its exact parameter types (docs/REFLECTION-SURFACE.md): keep it
        /// a real overload, not an optional parameter, which that lookup doesn't find.</summary>
        internal static int RockFloorTop(int x, int z) => RockFloorPass.Top(x, z, null);

        /// <summary>The rock floor's top under a column, read from the map chunk being generated (W-L4: a peeked chunk's own,
        /// not the world's at those coordinates).</summary>
        internal static int RockFloorTop(int x, int z, IMapChunk mapChunk) => RockFloorPass.Top(x, z, mapChunk);

        /// <summary>How many structures the water guards turned away since the server started (for the self-test).</summary>
        public static string StructureGuardReport() => StructurePlacementGuards.Report();
        /// <summary>Vanilla's pond tries the pond rules saw since the server started (for the self-test).</summary>
        public static string PondGuardReport() => SpringsAndPonds.PondReport();
        /// <summary>Vanilla's spring and mountain-side stream tries the springs rules turned away since the server started.</summary>
        public static string SpringGuardReport() => SpringsAndPonds.SpringReport();

        /// <summary>
        /// Which of chunk (<paramref name="cx"/>, <paramref name="cz"/>)'s 32 x 32 columns the generator draws with a rapid top
        /// source (row by row, z outer): the terrain pass's settling (ChunkSettler, the same routine) and ramp words, and the
        /// surface pass's rule for the source's kind (ChunkSettler.DrawnRapidSources). For the river query
        /// (EarthMapSystem.RiverAt), so that its "rapids" is exactly what was drawn. Any thread, with its own scratch (never
        /// the terrain pass's). Null, so that the query gives no answer (RiverQueryNotReadyException) and keeps nothing for
        /// RiverQuery.RetryMs, until the answer is the generator's:
        /// before the world's generation is fully set up (Active: the companions' terrain plan is sealed, so no expansion can
        /// still change the chunk) and when the rebuild read a built-in height where generation reads a GLO-90 tile still to
        /// come (a query thread may not fetch it; FineElevation.MissedReads counts such reads on this thread).
        /// </summary>
        internal bool[] DrawnRapidSources(int cx, int cz)
        {
            var s = settler;
            var ext = queryScratch?.Value;
            if (!Active || s == null || ext == null || model == null) return null;
            // a quick look first (the squares under the chunk and a margin), then the exact rule: the rebuild itself read no
            // built-in height where generation reads GLO-90 (projected through a companion's expansion, or reached by a summit
            // search, it may read squares beyond that margin)
            if (model.Fine != null && (elevation ??= sapi.ModLoader.GetModSystem<EarthElevationSystem>())?.ChunkReady(cx, cz) == false) return null;
            int missed = FineElevation.MissedReads;
            s.Settle(cx, cz, ext);
            var drawn = s.DrawnRapidSources(cx, cz, ext, sapi.WorldManager.MapSizeY);
            return FineElevation.MissedReads == missed ? drawn : null;
        }

        /// <summary>
        /// (FP-01, R2 of the Fable pass) The generator is closed first: in an Earth world it counts as refused from here on.
        /// The game disposes the mods once its threads have paused or after 10 seconds without; a generation thread that was
        /// still waiting (a GLO-90 tile on a slow link, before DemDownloader.Stopping) comes back after this, and its next chunks
        /// reach this generator's terrain handler and passes, which note them and throw. Vanilla's terrain handler is not put
        /// back in the list (it was, and that thread made vanilla terrain that the shutdown save kept): the list is this
        /// server's and ends with it.
        /// </summary>
        public override void Dispose()
        {
            if (Active) refused = true;
            Active = false;
            if (Instance == this && model?.PondRules >= 1) Mod?.Logger.Notification("Earth pond rules this session: {0}.", PondGuardReport());
            if (Instance == this && core?.Params?.SpringRules >= EarthParams.SpringRulesCaves) Mod?.Logger.Notification("Earth springs rules this session: {0}.", SpringGuardReport());
            if (ColumnStore.Of(model) is { } columns)
            {
                Mod?.Logger.Notification("Earth column store this session: {0}.", columns.Report());
                ColumnStore.Detach(model);
            }
            if (Instance == this && WaterSpawnDepth.Report() is string water) Mod?.Logger.Notification("Earth water depth this session: {0}.", water);
            WaterSpawnDepth.Restore();
            harmony?.UnpatchAll(EarthMap.PatchVanilla.HarmonyId);
            WorldGenerationContext.Withdraw(context);
            // (C-R2) the statics that outlived the world: the tree pickers (its catalogue) and /earth cost's switch
            EarthTrees.ReleasePickers();
            ClimateCost.On = false;
            if (Instance == this) Instance = null;
            base.Dispose();
        }

        // ------------------------------------------------------------------------------------------ set-up

        /// <summary>
        /// Once per server start, before the first chunk: the model's rule data, the block tables, the passes, the story
        /// locations and the companions' terrain plan. Earth's passes and patches run only once all of it is in place (the
        /// context is published and Active set last). Any failure stops the server (the RequiredMods pattern): vanilla catches
        /// and logs an exception here and goes on generating, which would save chunks with a part of this world's terrain plan
        /// missing.
        /// </summary>
        void InitWorldGen()
        {
            Active = false;
            // (Fable's re-review of ad56a74) /wgen regen sets the parts up again, and one can go off there: checked again at the
            // next chunk
            featuresChecked = 0;
            WorldGenerationContext.Withdraw(context);
            context = null;
            WaterSpawnDepth.Restore();
            var p = core.Params;
            // CR-L1: Earth's own start-up may have ended before this world's params were read (the game's mod loader logs a
            // StartServerSide exception and goes on): in an Earth world that would leave every new chunk to vanilla's terrain
            if (p == null && EarthMapSystem.ExpectsEarth(sapi, core.NewWorldAtStart))
                Refuse("Earth's start-up ended before this world's parameters were read (its error is logged above)");
            if (p?.Enabled != true) return;
            if (core.Model == null) Refuse("this world's terrain model was not built (its error is logged above)");
            CheckTerrain();
            try
            {
                InitWorldGen(p);
                ReadRetriedChunks();
                WorldGenerationContext.Publish(context);
                Active = true;
                // EA-0178: water creatures' height bands below their water's surface, for the sides whose patch is bound
                int water = WaterSpawnDepth.Widen(sapi, patches.On(EarthMap.PatchVanilla.Feature.WorldgenWaterDepth),
                    patches.On(EarthMap.PatchVanilla.Feature.RuntimeWaterDepth));
                if (water > 0) Mod.Logger.Notification("Earth water depth: {0} water creature types spawn by their depth below their water's surface, not their height above the sea.", water);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth world generation could not be set up: {0}. The server stops, so that this Earth world never generates " +
                    "chunks with a part of its terrain missing; fix the cause and restart.", e);
                sapi.Server.ShutDown();
                throw;
            }
        }

        void InitWorldGen(EarthParams p)
        {
            model = core.Model;
            var worldRules = WorldGenRules.Begin(sapi, Mod.Logger, p);
            rules = worldRules;
            LoadGlobalConfig(sapi);
            TopsoilSet(p);
            worldRules.Load(model, p);
            var blocks = new WorldGenBlocks(sapi.World, gcfg);
            // (kept across a later set-up of the same world, /wgen regen. Nothing is pending in it then: the terrain writer and the
            // surface pass run in the same vanilla pass of a column, and regen pauses the chunk thread, which saves only chunks
            // past that pass; keeping it is harmless. The fields swapped here are read by other threads only because regen
            // pauses the generation threads first: the river query's may pair an old settler with new scratch, on the same model.)
            if (handoff == null || handoffModel != model) { handoff = new TerrainHandoff(); handoffModel = model; }
            settler = new ChunkSettler(model);
            queryScratch = new System.Threading.ThreadLocal<TerrainModel.Column[]>(settler.NewScratch);
            terrain = new TerrainChunkWriter(sapi, model, p, gcfg, blocks, handoff, settler);
            rivers = new RiverBlockPass(sapi, model, blocks, gcfg, handoff);
            surface = new AlpineSurfacePass(sapi, model, p, blocks, worldRules.IceSheets, handoff);
            rockFloor = new RockFloorPass(blocks, gcfg);
            climateMaps = RegionMaps.Install(sapi, model, Mod.Logger);
            PlaceStories(p);
            PlanTerrain(p);
            context = new WorldGenerationContext(sapi, model, p, worldRules);
        }

        /// <summary>
        /// (Decision 0105) Whether a world with Earth soil reads the 30″ soil map (EarthParams.GroundData, as for ground columns,
        /// which decide it themselves): a new world decides once, before its first chunk. The soil-group set of release egts-v1
        /// (21 MB) is fetched from GroundRepo; a source that answers it hasn't the set (none set, or not there) leaves the world
        /// on the 2′ map for good, so its soil never mixes the two; any other failure stops the start (nothing is saved, the
        /// next start tries again). A 30″ world whose set went missing or damaged fetches it again, and stops if it can't.
        /// </summary>
        void TopsoilSet(EarthParams p)
        {
            if (!Topsoil.On(p.Topsoil) || GroundColumnFacts.Columns(p.GroundColumns)) return;
            if (p.GroundData == GroundDownloader.TwoMinuteOnly)
            {
                Mod.Logger.Notification("Earth soil: this world was made with the 2' soil map and keeps it; the 30\" soil map is not read.");
                return;
            }
            string folder = EarthGround.DataFolder, repo = core.Config?.GroundRepo;
            var sets = GroundDownloader.Sets.Where(s => s.Name == "global-soilgroup-30s.egts").ToArray();
            GroundDownloader.Result got;
            string error = null;
            try
            {
                got = GroundDownloader.Missing(folder, true, sets).Count == 0 ? GroundDownloader.Result.Here
                    : GroundDownloader.Get(folder, repo, s => { Mod.Logger.Notification("Earth soil map: " + s); Mod.Logger.StoryEvent("Earth soil map: " + s); }, core.Stopping, sets);
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or AggregateException)
            {
                if (e is OperationCanceledException && core.Stopping.IsCancellationRequested)
                    throw new InvalidOperationException("the 30\" soil map's download stopped with the server; it resumes at the next start (nothing is saved)", e);
                error = e.Message;
                got = GroundDownloader.Result.Failed;
            }
            string where = System.IO.Path.Combine(folder, GroundDownloader.Subfolder);
            if (p.GroundData == 0 && got != GroundDownloader.Result.Failed)
            {
                bool ok = got == GroundDownloader.Result.Here;
                p.GroundData = ok ? GroundDownloader.SetsVersion : GroundDownloader.TwoMinuteOnly;
                core.SaveParams();
                if (ok) Mod.Logger.Notification("Earth soil: this new world reads the 30\" soil map (release egts-v1) in {0}.", where);
                else Mod.Logger.Warning("Earth soil: the 30\" soil map can't be had ({0}); this new world uses the 2' map for good. New worlds try again.",
                    string.IsNullOrWhiteSpace(repo) ? "GroundRepo in earth.json is empty" : "the source doesn't have it, see above");
                return;
            }
            if (got == GroundDownloader.Result.Here) return;
            throw new InvalidOperationException(p.GroundData == 0
                ? "the 30\" soil map could not be fetched into " + where + " (" + (error ?? "see above") + "). Nothing is saved; start again once the server reaches "
                  + (string.IsNullOrWhiteSpace(repo) ? GroundDownloader.Release : repo) + ", or put global-soilgroup-30s.egts from " + GroundDownloader.Release + " there"
                : "this world was made with the 30\" soil map, but it is missing from " + where + " and could not be had (" + (error ?? "see above") + "); put global-soilgroup-30s.egts from "
                  + GroundDownloader.Release + " there, or let Earth fetch it (GroundRepo in earth.json), and start again");
        }

        /// <summary>
        /// The story locations moved onto real land once (a new world), their heights set at every start. (Decision 0083) A
        /// failure stops the start (InitWorldGen: nothing is generated, StoryPlaced is not saved, the next start tries again)
        /// unless earth.json accepts missing parts: before, a new world kept vanilla's sites for good, its StoryPlaced saved.
        /// </summary>
        void PlaceStories(EarthParams p)
        {
            bool accept = core.Config?.AcceptMissingFeatures == true;
            if (!p.StoryPlaced)
            {
                if (core.Config.MoveStoryLocations)
                {
                    try
                    {
                        StoryPlacer.Place(sapi, core, Mod.Logger);
                    }
                    catch (Exception e) when (accept)
                    {
                        Mod.Logger.Error("Placing the story locations failed; earth.json's AcceptMissingFeatures keeps vanilla's positions for good: {0}", e);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("placing the story locations failed (" + e.Message + "); nothing is saved, the next start tries again", e);
                    }
                }
                p.StoryPlaced = true;
                core.SaveParams();
            }
            try
            {
                StoryPlacer.ApplyHeights(sapi, core);
            }
            catch (Exception e) when (accept)
            {
                Mod.Logger.Error("Setting the story locations' heights failed; earth.json's AcceptMissingFeatures keeps vanilla's for this session: {0}", e);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("setting the story locations' heights failed (" + e.Message + ")", e);
            }
        }

        /// <summary>
        /// The companions' terrain plan (TerrainPlanning), sealed; its signature recorded, and a change since the world's last
        /// start reported. Once per model (CompanionPlan.PublishOnce): /wgen regen sets the world's generators up again
        /// (vanilla's RegenChunks calls TriggerInitWorldGen), and then the plan, the column store and the signature stay as
        /// they are; only the reloadable state before this (rule data, block tables, passes, map layers) is made again.
        /// </summary>
        void PlanTerrain(EarthParams p)
        {
            if (!CompanionPlan.PublishOnce(model, m => TerrainPlanning?.Invoke(m))) return;
            // from now on a column is a pure function of its position: the passes share them (audit B03)
            ColumnStore.Attach(model);
            string plan = model.TerrainPlanSignature();
            if (p.TerrainPlanSignature == null)
            {
                // First start with this check (or a new world): record what the companions published.
                p.TerrainPlanSignature = plan;
                core.SaveParams();
            }
            else if (p.TerrainPlanSignature != plan)
            {
                Mod.Logger.Warning("Companion terrain sites changed since this world was last started (a companion mod was added, removed or updated). " +
                    "Chunks generated from now on use the new sites and may not line up with chunks already generated near them.");
                p.TerrainPlanSignature = plan;
                core.SaveParams();
            }
        }
    }
}
