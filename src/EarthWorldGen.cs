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
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, CheckTerrainBeforeRunning);
            api.Event.MapRegionGeneration(OnMapRegionGen, "standard");
            // after vanilla's block layers (GenBlockLayers, 0.4) in the same pass, before any vegetation
            api.Event.ChunkColumnGeneration(BareRock, EnumWorldGenPass.Terrain, "standard");
            // after vanilla's caves (0.3), which mark their hot springs in the same pass, before GenHotSprings (TerrainFeatures)
            api.Event.ChunkColumnGeneration(KnownHotSprings, EnumWorldGenPass.Terrain, "standard");
            // after caves and ores (TerrainFeatures), before sunlight
            api.Event.ChunkColumnGeneration(FillRockFloor, EnumWorldGenPass.Vegetation, "standard");
            // decision 0070: after every plant pass (block patches scatter round a centre the patch check sees alone)
            api.Event.ChunkColumnGeneration(ClearShorePlants, EnumWorldGenPass.PreDone, "standard");
            Started = true;
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
            patches.Apply(EarthMap.PatchVanilla.Table(sapi.Event.GetRegisteredWorldGenHandlers("standard")));
            TerrainReservationPatchGuardReady = patches.On(EarthMap.PatchVanilla.Feature.PatchReservations);
        }

        /// <summary>PatchVanilla's second way for Earth's terrain, where another mod has wrapped GenTerra's handler before Earth
        /// could: a prefix on GenTerra's own method (which that wrapper calls), the same as <see cref="InPlaceOfGenTerra"/>.</summary>
        internal static bool GenTerraPrefix([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var gen = Instance;
            gen?.ThrowIfRefused(request);
            if (gen == null || !gen.Active) return true;
            gen.MakeTerrain(request);
            return false;
        }

        /// <summary>Set by <see cref="Refuse"/>: this Earth world is stopping and nothing more is generated in it.</summary>
        volatile bool refused;

        /// <summary>
        /// A chunk asked for after a refusal (another generation thread, in the seconds before the server stops): Active is off
        /// by then, and vanilla's terrain would make it. It is noted for the next start and its terrain pass throws instead.
        /// </summary>
        void ThrowIfRefused(IChunkColumnGenerateRequest request)
        {
            if (!refused) return;
            NoteFailedChunk(request.ChunkX, request.ChunkZ);
            throw new InvalidOperationException("Earth refused this world; chunk " + request.ChunkX + "," + request.ChunkZ + " is made at the next start.");
        }

        /// <summary>
        /// Earth's terrain for one chunk column. A throw is a refusal (CR-G2): the game catches it, skips the terrain pass's
        /// other handlers (the surface pass's check among them) and still saves the chunk past that pass, so the chunk is noted
        /// for deletion at the next start (<see cref="NoteFailedChunk"/>) and the server stops.
        /// </summary>
        void MakeTerrain(IChunkColumnGenerateRequest request)
        {
            try { terrain.Generate(request); }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth's terrain for chunk {0},{1} failed: {2}", request.ChunkX, request.ChunkZ, e);
                NoteFailedChunk(request.ChunkX, request.ChunkZ);
                Refuse("its terrain for chunk " + request.ChunkX + "," + request.ChunkZ + " could not be made (" + e.Message + "); that chunk is made again at the next start");
            }
        }

        /// <summary>The save's list of chunk columns Earth refused or could not make the terrain of (pairs of ints, chunk x then z).</summary>
        const string FailedChunksKey = "earth-failed-chunks";
        static readonly object failedChunksLock = new object();

        /// <summary>
        /// Notes a chunk column the game will save although Earth's terrain didn't make it (CR-G2): the game advances a chunk past
        /// a pass that threw and saves it, so without this the chunk would stay in the save for good. The next start that sets
        /// this world's generation up deletes it (<see cref="DeleteFailedChunks"/>) before any chunk generates. Never throws.
        /// </summary>
        void NoteFailedChunk(int cx, int cz)
        {
            try
            {
                lock (failedChunksLock)
                {
                    var save = sapi.WorldManager.SaveGame;
                    var chunks = FailedChunks(save.GetData(FailedChunksKey));
                    if (!chunks.Contains((cx, cz))) chunks.Add((cx, cz));
                    save.StoreData(FailedChunksKey, FailedChunksBytes(chunks));
                }
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth could not note chunk column {0},{1} for deletion ({2}): it keeps what was saved of it; delete or regenerate that chunk column by hand before the world is played.", cx, cz, e.Message);
            }
        }

        /// <summary>The chunk columns <see cref="NoteFailedChunk"/> noted in earlier starts, deleted (the game queues it and deletes them
        /// on its chunk thread once it runs, also where the spawn area loaded one; they generate again when asked for). Called once
        /// this world's generation is set up; the list is emptied (a start that stops before the game runs saves nothing, so it
        /// keeps the list).</summary>
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
                Mod.Logger.Warning("Earth: {0} chunk column(s) whose terrain Earth refused or could not make in an earlier start are deleted and generate again: {1}.",
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
            throw new InvalidOperationException(message);
        }

        /// <summary>The terrain check at the last moment before the game runs: a mod that changed Earth's terrain handler after
        /// Earth's set-up is caught here.</summary>
        void CheckTerrainBeforeRunning()
        {
            if (Active) CheckTerrain();
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
            if (!saidUnverified && patches.Unverified(EarthMap.PatchVanilla.Feature.Terrain) is string note)
            {
                saidUnverified = true;
                Mod.Logger.Warning("Earth's terrain: {0}. Earth can't tell from here whether that mod still calls Earth's terrain writer, so every " +
                    "chunk is checked as it generates: the first one made without it stops the server before Earth's other passes run on it.", note);
            }
        }

        /// <summary>Vanilla's terrain pass handler (GenTerra's), wrapped in the terrain pass's handler list by PatchVanilla: in an
        /// active Earth world <see cref="TerrainChunkWriter"/> makes the chunk instead, otherwise vanilla does.</summary>
        internal static ChunkColumnGenerationDelegate InPlaceOfGenTerra(ChunkColumnGenerationDelegate genTerra) => request =>
        {
            var gen = Instance;
            gen?.ThrowIfRefused(request);
            if (gen == null || !gen.Active) genTerra(request);
            else gen.MakeTerrain(request);
        };

        // ------------------------------------------------------------------------------------------ the passes, in order

        /// <summary>The surface pass, after vanilla's block layers in the terrain pass: the river ramps, then each surface
        /// column by what it is.</summary>
        void BareRock(IChunkColumnGenerateRequest request)
        {
            if (!Active || surface == null) return;
            var s = surface.Begin(request);
            // this chunk's terrain pass is running now: without the terrain pass's handoff, Earth's terrain did not make it
            if (!s.MadeByEarth)
            {
                // the game saves this chunk anyway (past the pass that threw): noted, it is deleted at the next start (CR-G2)
                NoteFailedChunk(request.ChunkX, request.ChunkZ);
                Refuse("vanilla's terrain pass made chunk " + request.ChunkX + "," + request.ChunkZ + " without Earth's terrain (a mod replaced Earth's terrain handler)");
            }
            rivers.PlaceRamps(request.Chunks, request.ChunkX, request.ChunkZ);
            surface.Columns(s);
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
            if (!Active || model == null || model.ShoreRules < 1) return;
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
                    var c = ColumnStore.At(model, cx + lx, cz + lz);
                    if (!c.Beach || c.Shore == ShoreMaterial.None || c.Water != TerrainModel.WaterNone) continue;
                    data.SetBlockUnsafe(idx, 0);
                }
        }

        void OnMapRegionGen(IMapRegion region, int regionX, int regionZ, ITreeAttribute chunkGenParams)
        {
            // decision 0075: vanilla biomes keep vanilla's forest and shrub maps (its noise, thinned by Earth's climate as vanilla thins it)
            if (!Active || model.VanillaBiomes) return;
            RegionMaps.ForestAndShrubs(sapi, model, region, regionX, regionZ);
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

        /// <summary>The rock floor's top under a column (0: none), for prospecting (RockFloorPass.Top).</summary>
        internal static int RockFloorTop(int x, int z) => RockFloorPass.Top(x, z);

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

        public override void Dispose()
        {
            if (Instance == this && model?.PondRules >= 1) Mod?.Logger.Notification("Earth pond rules this session: {0}.", PondGuardReport());
            if (Instance == this && core?.Params?.SpringRules >= EarthParams.SpringRulesCaves) Mod?.Logger.Notification("Earth springs rules this session: {0}.", SpringGuardReport());
            if (ColumnStore.Of(model) is { } columns)
            {
                Mod?.Logger.Notification("Earth column store this session: {0}.", columns.Report());
                ColumnStore.Detach(model);
            }
            patches?.Release();
            harmony?.UnpatchAll(EarthMap.PatchVanilla.HarmonyId);
            WorldGenerationContext.Withdraw(context);
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
            WorldGenerationContext.Withdraw(context);
            context = null;
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
                WorldGenerationContext.Publish(context);
                Active = true;
                DeleteFailedChunks();
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
            RegionMaps.Install(sapi, model, Mod.Logger);
            PlaceStories(p);
            PlanTerrain(p);
            context = new WorldGenerationContext(sapi, model, p, worldRules);
        }

        /// <summary>The story locations moved onto real land once (a new world), their heights set at every start.</summary>
        void PlaceStories(EarthParams p)
        {
            if (!p.StoryPlaced)
            {
                if (core.Config.MoveStoryLocations)
                {
                    try
                    {
                        StoryPlacer.Place(sapi, core, Mod.Logger);
                    }
                    catch (Exception e)
                    {
                        Mod.Logger.Error("Placing the story locations failed, vanilla's positions are kept: {0}", e);
                    }
                }
                p.StoryPlaced = true;
                core.SaveParams();
            }
            try
            {
                StoryPlacer.ApplyHeights(sapi, core);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Setting the story locations' heights failed, vanilla's are kept: {0}", e);
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
