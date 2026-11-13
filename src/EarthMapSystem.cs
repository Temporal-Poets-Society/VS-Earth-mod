using System;
using System.Globalization;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace EarthMap
{
    /// <summary>
    /// Core of the mod, both sides: config, the per-world parameters (frozen into the savegame at world creation),
    /// the Earth data, spawn, network sync, live temperatures beyond vanilla's range, latitude, and /earth.
    /// </summary>
    public class EarthMapSystem : ModSystem
    {
        public const string SaveKey = "earth-params";
        /// <summary>Set in a new Earth world's save at StartPre (EarthVerticalBootstrap; the game writes it at AssetsFinalize),
        /// cleared when its params are stored. Other mods may read it as "this world is still being created" (Cylinder World,
        /// Earth's companions), since SaveGame.IsNew is false on a second start after a failed first one, but only while the
        /// world has no Cylinder World record (<see cref="IsNewWorld(ISaveGame)"/>).</summary>
        public const string CreatingKey = "earth-creating";

        /// <summary>Cylinder World's record of a world's seam (CylinderWorldCore's save key "cylinderworld-seam", in every
        /// Cylinder World Earth accepts): stored at its WorldReady step, it reaches the disk only once the world runs.</summary>
        public const string CylinderRecordKey = "cylinderworld-seam";

        /// <summary>
        /// A world being created: new, or a new Earth world whose first start stopped before its params were stored (its data
        /// could not be had). The game wrote the save on that first start, so SaveGame.IsNew is false on the second; every
        /// "is this a new world" test before the params exist uses this instead (Hydrology review C10: the providers check
        /// skipped such a world). The marker counts only while the world has no Cylinder World record, as Cylinder World
        /// counts it (1.1.0-rc.2): that record is saved once the world runs, so a marker left in a world that has run (Earth
        /// removed after a failed first start, the world played without it, Earth added back) never makes it new again
        /// (Cylinder World review). Asked before WorldReady: Cylinder World stores its record for a new world in its
        /// WorldReady step, so from then on <see cref="NewWorldAtStart"/> answers.
        /// </summary>
        public static bool IsNewWorld(ISaveGame save) => save.IsNew || IsNewWorld(save.GetData(CreatingKey), save.GetData(CylinderRecordKey));

        /// <summary><see cref="IsNewWorld(ISaveGame)"/> for a save that isn't new, from its marker and its Cylinder World
        /// record as stored.</summary>
        public static bool IsNewWorld(byte[] creating, byte[] cylinderRecord) => creating is { Length: > 0 } && cylinderRecord == null;

        /// <summary>Server: <see cref="IsNewWorld(ISaveGame)"/> as this start found the save (at StartServerSide, before any mod's
        /// WorldReady step stored anything in it). From WorldReady on this, not IsNewWorld: by Earth's WorldReady step
        /// Cylinder World has stored its record for a world it sets up as new.</summary>
        public bool NewWorldAtStart { get; private set; }
        const string ChannelName = "earth";
        const string ConfigFile = EarthConfigStore.FileName;

        /// <summary>The parameters saved in this world (SaveKey), or null when it has none (a new world, or one without Earth).</summary>
        public static EarthParams Saved(ISaveGame save)
        {
            byte[] stored = save.GetData(SaveKey);
            return stored != null ? SerializerUtil.Deserialize<EarthParams>(stored) : null;
        }

        /// <summary>The config's Enabled before StartServerSide loads it (a config that can't be read means the defaults).</summary>
        public static bool ConfigEnabled(ICoreServerAPI api)
        {
            try { return api.LoadModConfig<EarthMapConfig>(ConfigFile)?.Enabled ?? true; }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Whether this start's save is an Earth world, asked where Earth's own start-up may not have run (CR-L1): the save holds
        /// enabled params (or params that can't be read), or it has none and is the new world of an Earth play style. A
        /// generation step that finds this true without <see cref="Params"/> stops the server rather than let vanilla generate.
        /// </summary>
        internal static bool ExpectsEarth(ICoreServerAPI api, bool newWorldAtStart)
        {
            var save = api.WorldManager.SaveGame;
            byte[] stored = save.GetData(SaveKey);
            if (stored != null)
            {
                try { return SerializerUtil.Deserialize<EarthParams>(stored)?.Enabled != false; }
                catch (Exception) { return true; }
            }
            return EarthWorldOptions.CreatesEarth(newWorldAtStart, save.WorldType, save.PlayStyle, ConfigEnabled(api));
        }

        public EarthMapConfig Config { get; private set; }
        public EarthParams Params { get; private set; }
        /// <summary>Server: whether Earth created this world's params in this start (ResolveParams' fresh world), set at
        /// WorldReady. Unlike SaveGame.IsNew also true on the second start of a new world whose first start stopped before
        /// its params were stored: every "decided once for a new world" choice after WorldReady asks this (review A1).</summary>
        public bool CreatedThisStart { get; private set; }
        public EarthData Data { get; private set; }
        /// <summary>Server: full terrain model. Client: climate only (no terrain data loaded).</summary>
        public TerrainModel Model { get; private set; }
        public RegionalTreeCatalog RegionalTrees { get; private set; }
        /// <summary>Server: /earth, and the diagnostics the self-test runs too.</summary>
        internal EarthCommands Commands { get; private set; }

        readonly SkyLongitude sky = new();
        readonly AstraTerraLongitude astraTerra = new();
        readonly AstraTerraLook astraTerraLook = new();

        /// <summary>
        /// Degrees east (-180..180) of the sky's meridian at world X, or null when Earth Map is not active in this world.
        /// Both sides, any thread. The meridian of the X column (EarthGeometry.SkyLonAt), the map's longitude at every
        /// latitude. Latitude stays the calendar's
        /// (OnGetLatitude); a position's map latitude and longitude are Model.Geometry.ToGeo.
        /// </summary>
        public double? LongitudeAt(double x) => sky.At(x);

        /// <summary>
        /// The river flow query's version in this world (decision 0027): 1 where the world's pool layout is 8 (steps without
        /// sills, rapids reaches marked), 2 where it is 9 or newer (also RiverFlowAt: the rapids reaches' sources carry the
        /// river's flow), 3 where it is 11 or newer (decision 0046: every natural river column's top source carries the river's
        /// flow, calm ones as ordinary water), 0 otherwise. Hydrology uses its query rules only when this is 1 or more.
        /// </summary>
        public int RiverQueryVersion => Params?.Enabled == true && Params.RiverPoolLayout >= 8 && Model?.Rivers != null
            ? Params.RiverPoolLayout >= 11 ? 3 : Params.RiverPoolLayout >= 9 ? 2 : 1 : 0;

        /// <summary>
        /// The river flow query (decision 0027, 0022 part A): whether the column is natural river water as generated (not a
        /// bank, land, a lake, the sea, an exit channel or anything dug later: it describes the river as generated), and if so
        /// whether its reach is a rapids reach (pool layout 11: whether the generator drew its top source as rapid water, at a rapids
        /// reach's drops and a curtain's lip), the water's fall
        /// per block along it and the river's discharge (m³/s). Server side, any thread; false when Earth is not active or
        /// RiverQueryVersion is 0.
        /// Every answer is the generator's, the same for the world's whole life, so a caller may keep it (Hydrology keeps
        /// each per column). Where this thread can't have that answer yet, it throws EarthMap.Core.RiverQueryNotReadyException
        /// instead of a guess: the column, or (pool layout 11) its chunk's settling, reads a GLO-90 tile not yet on this
        /// machine (generation fetches it; a query thread may not), the world's generation is not set up yet, or that
        /// chunk's settling failed a moment ago (RiverQuery.RetryMs). Nothing about the column follows from a call that
        /// threw; ask again later. Hydrology 0.6.2-rc.1 to rc.3 (and rc.4 as it stands) catch any exception from it, keep
        /// nothing and ask again at the column's next water update, logging the first failure once per world. (Before
        /// 2.0.0-rc.3 the model's rough guess was answered there, and Hydrology kept it.) RiverFlowAt and RiverSpanAt read
        /// the same column as this, so they can't miss a tile where this didn't: ask this first.
        /// </summary>
        public bool RiverAt(int x, int z, out bool rapids, out double slope, out double discharge)
        {
            rapids = false; slope = 0; discharge = 0;
            if (RiverQueryVersion < 1) return false;
            var model = Model;
            int missed = FineElevation.MissedReads;
            var c = model.GetColumn(x, z);
            // (pool layout 11: rapids only where the generator drew the top source as rapid water, at a rapids reach's drops and
            // a curtain's lip, so that Hydrology keeps the calm pools between them ordinary water: from the columns as the
            // generator settled them and the ramp words it drew from them; before, a rapids reach's whole length)
            if (c.River) rapids = Params.RiverPoolLayout < 11 ? c.RapidsReach : riverQuery.DrawnRapid(x, z, c, modelColumn);
            // the columns read here (this one; pool layout 11 its neighbours too) are the generator's only where this thread
            // took no built-in height in place of a GLO-90 tile still to come (FineElevation.MissedReads): else no answer
            if (FineElevation.MissedReads != missed)
                throw new RiverQueryNotReadyException(string.Format(CultureInfo.InvariantCulture,
                    "Earth's river query has no answer for column ({0}, {1}) yet: a GLO-90 tile it reads is not on this machine yet; ask again later", x, z));
            if (!c.River) return false;
            // (the river query at the terrain's own source position: a companion's terrain expansion moves it)
            model.ProjectTerrain(x, z, out double sx, out double sz);
            if (!model.Rivers.Nearest(sx, sz, out var hit)) return true;
            slope = model.Rivers.ReachSlope(hit.Reach);
            discharge = hit.Q;
            return true;
        }

        // Pool layout 11's drawn rapid sources per chunk (EarthWorldGen.DrawnRapidSources, through RiverQuery), and the
        // model's column at a block position for it; made at StartServerSide
        RiverQuery riverQuery;
        System.Func<int, int, TerrainModel.Column> modelColumn;
        EarthWorldGen worldGen;
        int riverQueryFailed;

        /// <summary>
        /// The water a natural river column holds as the model has it: its bed (the top of the ground under the water) and its
        /// water surface (the top source; a ramp below a step lies one block above it). For telling the river's own water
        /// from other water in the same column (a channel on a bridge over the river, a cave pool under its bed): Hydrology
        /// applies its river rules only between them. Both are what the generator draws: in the pool layouts this answers for
        /// (8 on, no sills) settling never changes a river column's bed or surface (RiverSettling). Above the surface the
        /// surface pass adds the ramp below a step (one block), in pool layouts 8 and 9 a spill over the next step (one
        /// more; Hydrology's allowance of 2 covers both), and at a waterfall the curtain up to the upper water's surface.
        /// False where the column is no river, the query is off, or the column holds no water (a rock
        /// breaking a chute's surface: RiverQuery.Span; before 2.0.0-rc.3 its bed came back above its surface). Server
        /// side, any thread.
        /// </summary>
        public bool RiverSpanAt(int x, int z, out int bed, out int waterTop)
        {
            bed = waterTop = -1;
            if (RiverQueryVersion < 1) return false;
            return RiverQuery.Span(Model.GetColumn(x, z), out bed, out waterTop);
        }

        /// <summary>
        /// Query version 2 (pool layout 9): the river's flow at a natural river column, as the generator labels its sources
        /// (a rapids reach's; from version 3, pool layout 11, every river column's)
        /// (0 = north (-z), clockwise in 45° steps to 7 = north-west, the order of vanilla's flow variants n, ne, e, se, s,
        /// sw, w, nw); -1 where the column is no river or the version is below 2. Server side, any thread.
        /// </summary>
        public int RiverFlowAt(int x, int z)
        {
            if (RiverQueryVersion < 2) return -1;
            var c = Model.GetColumn(x, z);
            // (decision 0069 rule 4: still river water reports no flow, as its blocks carry none)
            return c.River && !c.Still ? EarthWorldGen.FlowDirIndex(c.FlowX, c.FlowZ) : -1;
        }

        /// <summary>Whether this side's sun follows Earth's meridians (AstraTerra took them and its LongitudeAwareSun is on),
        /// so local solar time is the calendar's hour plus the longitude / 15.</summary>
        public bool SkyFollowsLongitude => astraTerra.SunFollowsLongitude;

        ICoreServerAPI sapi;
        ICoreClientAPI capi;
        IServerNetworkChannel serverChannel;

        // after Cylinder World's core (0.01), so its geometry is ready at WorldReady
        public override double ExecuteOrder() => 0.05;

        ICoreAPI anyApi;

        public override void Start(ICoreAPI api)
        {
            anyApi = api;
            // climate rules v2 (decision 0009): the prefixes go in before anything compiles vanilla's altitude adjustment into
            // its own code; they do nothing until a world with those rules is loaded (ClimateLapse.Use)
            ClimateLapse.Patch(api, Mod);
        }

        // ------------------------------------------------------------------------------------------ server

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            NewWorldAtStart = IsNewWorld(api.WorldManager.SaveGame);
            // the river query's drawn rapids: not ready (null) while EarthWorldGen isn't there; a chunk whose settling throws
            // is said once a session (its next tries are RiverQuery.RetryMs apart)
            riverQuery = new RiverQuery((cx, cz) => (worldGen ??= sapi.ModLoader.GetModSystem<EarthWorldGen>())?.DrawnRapidSources(cx, cz),
                failed: (cx, cz, e) =>
                {
                    if (System.Threading.Interlocked.Exchange(ref riverQueryFailed, 1) == 0)
                        Mod.Logger.Warning("Earth's river query: the drawn rapids of chunk ({0}, {1}) could not be had; such a chunk is asked again {2} s later (said once): {3}",
                            cx, cz, RiverQuery.RetryMs / 1000, e);
                });
            // (a neighbouring chunk's stored columns where they are there: the generator's own, ColumnStore)
            modelColumn = (x, z) => ColumnStore.Near(Model, x, z);
            Config = EarthConfigStore.Load(api, Mod.Logger);
            RequiredMods.EnsureForWorld(api, Config.Enabled);
            var fauna = api.ModLoader.GetModSystem<EarthFaunaSystem>();
            if (fauna != null) fauna.RangeProfile = Config.FaunaRangeProfile;
            serverChannel = api.Network.RegisterChannel(ChannelName).RegisterMessageType<EarthParams>();
            var save = api.WorldManager.SaveGame;
            if (save.GetData(SaveKey) == null && EarthWorldOptions.CreatesEarth(NewWorldAtStart, save.WorldType, save.PlayStyle, Config.Enabled))
            {
                // the Earth tab's size, before the game builds the map from the save (decision 0023)
                try { Mod.Logger.Notification(EarthWorldOptions.ApplySize(api)); }
                catch (Exception e) { Mod.Logger.Error("Could not set the world size from the Earth tab ({0}); this world keeps the World generation tab's size.", e.Message); }
            }
            // WorldReady: the real map size is known and Cylinder World has set up its geometry; worldgen starts after this
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, InitWorld);
            api.Event.PlayerJoin += plr => { if (Params != null) serverChannel.SendPacket(Params, plr); };
            Commands = new EarthCommands(this, api, Mod.Logger);
            Commands.Register(api);
            // after every mod's Start(): vanilla's season handler (ModTemperature) subscribes there and overwrites the live temperature
            api.Event.OnGetClimate += (climateHooks ??= new EarthClimateHooks(this)).OnGetClimate;
        }

        /// <summary>The coast outlines (EarthParams.Coasts) a world with this geometry gets by the config: none when they are off,
        /// without the islands, or where the grid's cells are under CoastMinCellBlocks wide.</summary>
        internal int CoastsFor(EarthGeometry geo, bool islands) =>
            Config.Coasts && islands && CoastLayer.CellBlocks(geo) >= Config.CoastMinCellBlocks ? CoastData.FileVersion : 0;

        /// <summary>
        /// The Earth data (decision 0066). A client reads only the climate files inside this mod. The server (with
        /// <paramref name="terrain"/>) first makes sure the data store's files are in VintagestoryData/ModData/earth/data
        /// (EarthDataStore: from an Earth 1.0 data pack still installed, else from the data release): every one for a new
        /// world, the ones its saved rules read for a saved <paramref name="world"/> (<see cref="StoreFilesRead"/>), then
        /// reads each file from this mod if it carries it, else from the store. A server that can't get them stops with
        /// instructions instead of generating a world with parts of its data missing.
        /// </summary>
        EarthData LoadData(bool terrain, EarthParams world = null)
        {
            string src = Mod.SourcePath;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EarthData data;
            try
            {
                string store = EarthGround.DataFolder;
                if (terrain) EnsureServerData(store, world);
                data = EarthData.Load(name => EarthData.OpenDataFile(src, name) ?? (terrain ? EarthDataStore.Open(store, name) : null), terrain);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth data cannot load: {0}", e.Message);
                sapi?.Server.ShutDown();
                throw;
            }
            Mod.Logger.Notification("Earth data loaded from {0}{1} in {2} ms", src, terrain ? " and " + EarthDataStore.Folder(EarthGround.DataFolder) : "", sw.ElapsedMilliseconds);
            if (data.WeatherError != null) Mod.Logger.Error("Earth weather data could not be read; vanilla weather is used. {0}", data.WeatherError);
            return data;
        }

        /// <summary>The data store's files this world needs in place (all for a new world, or while a one-shot upgrade is asked
        /// for: it moves the world to this build's rules), or an exception saying which are missing and how to get them.</summary>
        void EnsureServerData(string store, EarthParams world)
        {
            var local = new System.Collections.Generic.List<System.Func<string, System.IO.Stream>>();
            foreach (string id in EarthDataStore.LegacyPacks)
                if (sapi.ModLoader.IsModEnabled(id) && sapi.ModLoader.GetMod(id)?.SourcePath is string pack) local.Add(EarthDataStore.FromPack(pack));
            // a development build or an unpacked mod folder that still carries a file
            local.Add(EarthDataStore.FromPack(Mod.SourcePath));
            EarthDataStore.File[] files = EarthDataStore.Files;
            if (world != null && !WorldUpgrades.AnyAsked(Config))
            {
                var read = StoreFilesRead(world);
                files = EarthDataStore.Files.Where(f => read.Contains(f.Name)).ToArray();
                if (files.Length < EarthDataStore.Files.Length)
                    Mod.Logger.Notification("Earth data: this world's saved rules read {0} of the data store's {1} files.", files.Length, EarthDataStore.Files.Length);
            }
            if (EarthDataStore.Ensure(store, local, Config?.DataRepo, m => Mod.Logger.Notification("Earth data: {0}", m), out string why, default, files)) return;
            var missing = EarthDataStore.Missing(store, files);
            long needed = EarthDataStore.TotalBytes(missing);
            // what happened, not always "the network" (review A9): a full disk says so
            string disk = DataTransport.DiskShortfall(EarthDataStore.Folder(store), needed);
            string repo = string.IsNullOrWhiteSpace(Config?.DataRepo) ? null : Config.DataRepo;
            throw new System.IO.FileNotFoundException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} of Earth's data files ({1:F0} MB) are missing from {2} and could not be had ({3}): {4}. {5}",
                missing.Count, needed / 1e6, EarthDataStore.Folder(store),
                string.Join(", ", missing.Take(6).Select(f => f.Name)) + (missing.Count > 6 ? ", …" : ""),
                disk ?? why ?? "not fetched",
                disk != null ? "Free space on that disk, then start again."
                : repo == null ? "Set DataRepo in earth.json (Earth's data release is " + EarthDataStore.Release + "), or put the files from that release into this folder by hand, then start again."
                : "Let the server reach " + repo + ", or put the files from that release into this folder by hand, then start again."));
        }

        /// <summary>The server's Earth data, loaded the first time it is needed (with the terrain): the store files a saved
        /// <paramref name="world"/>'s rules read in place, or all of them.</summary>
        internal EarthData LoadedData(EarthParams world = null) => Data ??= LoadData(true, world);

        /// <summary>
        /// The data store's files (EarthDataStore.Files) a saved world's rules read (review A5: decision 0066 promises that an
        /// offline server keeps working with an Earth 1.0 world's data packs, which carry 9 of the 38 files). Read by every
        /// world: the 2' terrain grid and rivers.bin, and the soil sites and wetlands (2.0 reads both for every world: the
        /// regional trees' soil fit, the fire, pond and wetland rules and the water table; kept for all, to be safe). The
        /// rest only where a saved rule reads it (EarthParams.BuildModel, RiverSourceRepository.RiverReaches, WorldGenRules,
        /// EarthGround, EarthSprings). A file left out is one this world's rules never read, so its absence can't change what
        /// the world generates; a new world, or one asked to take a one-shot upgrade, needs them all.
        /// </summary>
        public static System.Collections.Generic.HashSet<string> StoreFilesRead(EarthParams p)
        {
            var files = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "terrain.bin", "rivers.bin", "soil-site.bin", "wetlands.bin" };
            void If(bool rule, System.Collections.Generic.IEnumerable<string> names) { if (rule) files.UnionWith(names); }
            void One(bool rule, string name) { if (rule) files.Add(name); }
            // the grid's overlays (BuildModel); the sea referee is checked against the grid, the islands and the coasts
            One(p.Islands > 0 || p.Coasts > 0 || p.SeaReferee >= 1, "islands.bin");
            One(p.Coasts > 0 || p.SeaReferee >= 1, "coasts.bin");
            One(p.SeaReferee >= 1, "sea-referee.bin");
            One(p.RiverEnds > 0, "lakes.bin");
            One(p.LakeShores > 0, "shores.bin");
            // decision 0070's beaches by OSM's material (TerrainModel.ShoreData)
            One(p.ShoreRules >= 1, ShoreMaterialData.FileName);
            // decision 0031's mountains, and the included GLO-90 heights
            One(p.Summits >= 1, "summits.bin");
            One(p.Summits >= 2, "peaks.json");
            One(p.Glaciers >= 1, "glaciers.bin");
            If(p.CoarseElevation > 0, EarthDataStore.Files.Select(f => f.Name).Where(n => n.StartsWith(CoarseElevation.Prefix + "-", StringComparison.Ordinal)));
            // the river courses on GLO-90 and their overlays
            One(p.RiverCourses >= 1, RiverSourceRepository.Glo90RiversFile);
            One(p.RiverCourses >= RiverNetwork.Glo90SnapCourses, "rivers-glo90-snap.bin");
            One(p.RiverCourses >= RiverNetwork.Glo90ArmsCourses, "rivers-glo90-arms.bin");
            If(p.RiverCourses >= RiverNetwork.Glo90WidthCourses, new[] { "rivers-glo90-snap-2.bin", "rivers-glo90-arms-2.bin" });
            One(p.SpringRules >= EarthParams.SpringRulesStreams, StreamHeads.FileName);
            // the ground columns' 2' sets (ground-rules-v1.json), the floodplains they read, the ice sheets' ground
            If(p.GroundColumns > 0, EarthDataStore.Files.Select(f => f.Name).Where(n => n.EndsWith(".egt", StringComparison.Ordinal) || n.EndsWith(".egts", StringComparison.Ordinal)));
            One(p.GroundColumns > 0 && p.SoilRules >= SoilFertility.FloodplainRules, "floodplains.bin");
            One(p.AlpineSurfaceRules >= 2, "global-glaciation.egts");
            return files;
        }

        /// <summary>
        /// WorldReady (the real map size is known and Cylinder World has set up its geometry; world generation starts after
        /// this): the world's parameters (a new world's computed and frozen, a saved world's repaired where they can't make
        /// terrain), the settings applied at every start, the Earth data, the one-shot upgrades asked for in earth.json, then
        /// the model and what hangs on it.
        /// </summary>
        void InitWorld()
        {
            // (the required providers were checked in StartServerSide: a refusal there stops the server before this phase)
            try { string fixedSize = DungeonMapSize.Fix(sapi, Mod); if (fixedSize != null) Mod.Logger.Notification(fixedSize); }
            catch (Exception e) { Mod.Logger.Warning("Tiled dungeons: could not update their map size ({0}).", e.Message); }
            var save = sapi.WorldManager.SaveGame;
            // Earth needs Cylinder World (decision 0023); its geometry is set up by now (WorldReady, after its core)
            var cw = CylinderCompat.TryGet(sapi);
            EarthParams p;
            bool fresh;
            try
            {
                p = ResolveParams(save, cw, out fresh);
                RepairSaved(p, save, cw);
            }
            catch (MissingMemberException e)
            {
                // CylinderCompat's getters throw this when Cylinder World renamed a member Earth reads: one named line and the
                // server stops (it crashed here before without saying why; CR-W10)
                Refuse("the installed Cylinder World no longer has a member Earth reads (" + e.Message + "); install a Cylinder World version this Earth Map supports");
                return;
            }
            CreatedThisStart = fresh;
            ApplyRuntime(p);
            Params = p;
            if (!p.Enabled) return;
            // CR-L1: Earth's generation registers its set-up in its own StartServerSide; where that threw (the game's mod loader
            // logs it and goes on), this world's generation is never set up and vanilla's terrain would fill an Earth world
            if (sapi.ModLoader.GetModSystem<EarthWorldGen>()?.Started != true)
                Refuse("Earth's world generation did not start (its error is logged above)");
            AnnounceSavedRules(p, save);
            // the data first (it reads no params): a start that can't have it stops before any one-shot is applied, and the
            // one-shots that read it (the climate fix, the summits) find it loaded (Codex round 3 item 2, review A2); a saved
            // world needs the store files its rules read (review A5)
            LoadedData(fresh ? null : p);
            var upgrades = new WorldUpgrades(this, sapi, Mod.Logger);
            upgrades.Layout(p);
            ReportDataMismatch(p);
            // Terrain versions must be upgraded before constructing the model (regional upgrades run later).
            upgrades.Terrain(p);
            if (!BuildAndAttachModel(p, out long modelMs)) return;
            upgrades.Regional(p);
            LoadRegionalTrees(p);
            if (Model.Rivers != null) Mod.Logger.Notification("Earth Map: {0} river segments (navigable from {1} m³/s, streams: {2} down to {3} m³/s) indexed in {4} ms",
                Model.Rivers.SegmentCount, Model.Rivers.MinQ, Model.Rivers.StreamMode, Model.Rivers.StreamMinQ, modelMs);
            if (Model.Rivers?.UnconvergedBasins > 0)
                Mod.Logger.Warning("Earth rivers: the pool layout of {0} river basin(s) had not settled when its rounds ran out; a tributary may stand above its main river at a confluence there (CR-R7).",
                    Model.Rivers.UnconvergedBasins);
            // what only start-up read (the reaches, the laid-out courses): ~200 MB and ~4 M objects less for every full collection
            // while the world runs (decision 0025); the river audit builds them again when asked
            Model.Rivers?.ReleaseStartupData();
            Data.ReleaseRivers();

            if (p.ExtendTemperature)
            {
                // Earth Map extends temperatures from the real climate; Cylinder World's own extension would add its curve on top
                if (!cw.TrySet("ExtendTemperature", false)) Mod.Logger.Warning("Could not turn off Cylinder World's temperature extension.");
            }

            if (fresh) FinishNewWorld(p, save, cw);
            Mod.Logger.Notification("Earth Map: spawn at {0} (x={1} z={2}); world centre meridian {3}; wrapping with Cylinder World, seam through the Bering Strait",
                FormatLatLon(p.SpawnLat, p.SpawnLon), p.SpawnX, p.SpawnZ, FormatLon(Model.Geometry.CenterLon));
            if (!sapi.ModLoader.IsModEnabled("wmap"))
                Mod.Logger.Notification("Earth Map: Wmap is not installed. Earth works without it, but Wmap's charts and far terrain are recommended (decision 0023).");
        }

        /// <summary>
        /// The saved parameters; without them (a new world, or Earth added to an existing one) a new world's, computed and
        /// frozen, or a disabled set that keeps the world vanilla (stored either way). A world saved with Earth whose Cylinder
        /// World isn't there is refused (the server stops), its save left as it is. <paramref name="fresh"/>: a new Earth world.
        /// </summary>
        EarthParams ResolveParams(ISaveGame save, CylinderCompat cw, out bool fresh)
        {
            fresh = false;
            var p = Saved(save);
            if (p == null)
            {
                // a new Earth world whose first start stopped before its params were stored (its data could not be had): the
                // game had already written the save, with the marker EarthVerticalBootstrap.StartPre set, so IsNewWorld still
                // counts it as new; resumed: this is that second start (as the save was at this start: Cylinder World has
                // stored its new record by now)
                bool resumed = !save.IsNew && NewWorldAtStart;
                if (!Config.Enabled)
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map is disabled in {0}; this world keeps vanilla terrain.", ConfigFile);
                }
                else if (!NewWorldAtStart)
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Warning("Earth Map was added to an existing world. It only generates new worlds; this one keeps its terrain.");
                }
                else if (save.WorldType != "standard")
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map only generates 'standard' worlds (this one is '{0}').", save.WorldType);
                }
                else if (!EarthWorldOptions.IsEarthStyle(save.PlayStyle))
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map is inactive for play style '{0}'; new Earth worlds use the 'earth' play style. This world keeps vanilla generation.", save.PlayStyle ?? "(missing)");
                }
                else if (CylinderWorldMissing(cw) is string missing)
                {
                    // like a failed startup step (decision 0008): no half-working Earth, the world keeps vanilla terrain
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Error("Earth Map needs Cylinder World's planet layout (decision 0023), but {0}. This new world keeps vanilla terrain; " +
                        "fix ModConfig/cylinderworld.json and create a new world for the Earth.", missing);
                }
                else
                {
                    // (the marker is set and saved before this, in StartPre; the game saves nothing written here until it
                    // runs, so this start's own failure below leaves the world marked for the next one; cleared below)
                    if (resumed) Mod.Logger.Notification("Earth Map: this world's first start did not finish; it is created now.");
                    var data = LoadedData();
                    p = ComputeParams(data, cw, out string report);
                    EarthWorldOptions.Freeze(save.WorldConfiguration, Config, p, data);
                    // 0072: the world's own equator-to-pole distance for everything that reads polarEquatorDistance
                    string polar = EarthWorldOptions.ApplyPolarEquatorDistance(save.WorldConfiguration, p.BlocksPerDegreeZ);
                    if (polar != null) Mod.Logger.Notification(polar);
                    else Mod.Logger.Warning("Earth: this world's layout gives no equator-to-pole distance; polarEquatorDistance stays {0}.",
                        save.WorldConfiguration.GetAsString(EarthWorldOptions.PolarEquatorDistanceKey, "unset"));
                    Mod.Logger.Notification("Earth land heights: {0}; rivers {1}; detail noise rules {2} ({3:0.#} m per block across).",
                        p.FineElevation > 0 ? "Copernicus GLO-90, full (downloaded as needed)" + (p.CoarseElevation > 0 ? ", included GLO-90 where a square can't be" : "")
                        : p.CoarseElevation > 0 && data.Coarse != null ? "Copernicus GLO-90, included"
                        : "built-in" + (data.CoarseError != null ? " (included GLO-90 not readable: " + data.CoarseError + ")" : ""),
                        (p.RiverCourses >= RiverNetwork.Glo90WidthCourses ? "on GLO-90, every reach from 20 m³/s moved onto the water mask's rivers and smoothed, with the mask's widths and the side arms"
                            : p.RiverCourses >= RiverNetwork.Glo90ArmsCourses ? "on GLO-90, moved into the water mask's rivers, with their side arms"
                            : p.RiverCourses >= RiverNetwork.Glo90SnapCourses ? "on GLO-90, moved into the water mask's rivers" : p.RiverCourses > 0 ? "on GLO-90" : "from rivers.bin")
                        + (p.SeaReferee > 0 ? "; sea refereed by the water mask" : "") + (p.LakeOutlines > 0 ? "; lakes from the water mask's outlines" : "")
                        + (p.RiverOutlines > 0 ? "; rivers' water from the water mask's outlines" : "")
                        + (p.ChannelRules > 0 ? "; channels dredged along the courses (channel rules " + p.ChannelRules + ")" : ""),
                        p.DetailRules, EarthWorldOptions.MetresPerBlock(save.WorldConfiguration));
                    if (data.Glo90RiversError != null)
                        Mod.Logger.Warning("Earth: the rivers on GLO-90 could not be read ({0}); this new world's rivers come from rivers.bin.", data.Glo90RiversError);
                    string badSettings = p.RepairSettings(new EarthMapConfig());
                    if (badSettings.Length > 0) Mod.Logger.Warning("{0}: {1} can't make terrain; this new world uses the defaults for them (it saves them, so fix the config before creating the next one).", ConfigFile, badSettings);
                    Mod.Logger.Notification(report);
                    fresh = true;
                }
                save.StoreData(SaveKey, SerializerUtil.Serialize(p));
                save.StoreData(CreatingKey, System.Array.Empty<byte>());
            }
            else if (p.Enabled && (!p.Cylinder || cw == null))
            {
                // Earth runs only with Cylinder World (decision 0023). An Earth world is never opened without it (Codex round 3
                // item 1): vanilla's terrain in new chunks beside the Earth's would stay in the save for good. Refused like
                // RequiredMods and the terrain check: one error, the server stops, the saved params stay as they are (nothing
                // is saved before the game runs), so the world comes back as it was once Cylinder World is active again.
                Refuse(p.Cylinder
                    ? "this world was made with Cylinder World, but " + CylinderWorldMissing(cw) + "; restart once it is active (the world is unchanged)"
                    : "this world was made without Cylinder World, which Earth no longer supports (decision 0023); open it with the Earth Map " +
                      "version that made it");
            }
            return p;
        }

        /// <summary>An Earth world this server can't open as it was made: one error line, the server stops (the RequiredMods
        /// pattern: the exception also ends the rest of this WorldReady step, and nothing of the world is saved before the
        /// game runs).</summary>
        void Refuse(string why)
        {
            string message = "Earth cannot load this world: " + why + ". The server stops, so that this Earth world never generates " +
                "chunks without Earth's terrain.";
            Mod.Logger.Error("{0}", message);
            sapi.Server.ShutDown();
            throw new InvalidOperationException(message);
        }

        /// <summary>An Earth world's saved settings, layout and vertical scale made usable where they can't make terrain: every
        /// chunk they made is broken anyway, so the repair is saved and happens once.</summary>
        void RepairSaved(EarthParams p, ISaveGame save, CylinderCompat cw)
        {
            if (p.Enabled)
            {
                // settings frozen with a broken value (NaN detail, a negative width) make broken terrain for good: repaired once
                string broken = p.RepairSettings(new EarthMapConfig());
                if (broken.Length > 0)
                {
                    Mod.Logger.Error("Earth Map: this world's saved settings can't make terrain ({0}); they now use the defaults. Chunks generated before keep what they got.", broken);
                    save.StoreData(SaveKey, SerializerUtil.Serialize(p));
                }
            }
            if (p.Enabled && !EarthGeometry.IsUsableLayout(p.RingLength, p.Gutter, p.BlocksPerDegreeZ, p.EquatorZ))
            {
                // a layout saved from settings that leave no Earth (no chunk of it can have generated properly): laid out again
                var relaid = ComputeParams(LoadedData(), cw, out _);
                Mod.Logger.Error("Earth Map: this world's saved layout can't place the Earth (ring {0}, gutter {1}, {2} blocks per degree north-south); it is laid out again with usable settings.",
                    p.RingLength, p.Gutter, p.BlocksPerDegreeZ);
                p.RingStart = relaid.RingStart; p.RingLength = relaid.RingLength; p.Wraps = relaid.Wraps; p.Gutter = relaid.Gutter;
                p.EquatorZ = relaid.EquatorZ; p.BlocksPerDegreeZ = relaid.BlocksPerDegreeZ; p.SeamLon0 = relaid.SeamLon0;
                // a layout without a gutter needs the real seam's repeating terrain (decision 0030)
                if (p.Gutter == 0) p.GutterSeabed = relaid.GutterSeabed;
                save.StoreData(SaveKey, SerializerUtil.Serialize(p));
            }
            if (p.Enabled && !p.BuildVertical().IsUsable)
            {
                // a world saved with a broken curve (0 gives NaN heights): every chunk it made is broken anyway, so the
                // defaults for its height let it generate terrain again; saved, so this happens once
                var fixedScale = VerticalScale.ForWorld(p.MapHeight, p.SeaLevel);
                Mod.Logger.Error("Earth Map: this world's saved vertical scale can't make terrain (land curve {0}, sea curve {1}, top y {2}, depth {3}); it now uses the defaults for its height. Chunks generated before keep their broken terrain.",
                    p.LandH0, p.SeaD0, p.TopY, p.MaxDepth);
                p.TopY = fixedScale.TopY; p.LandH0 = fixedScale.LandH0; p.MaxDepth = fixedScale.MaxDepth; p.SeaD0 = fixedScale.SeaD0;
                save.StoreData(SaveKey, SerializerUtil.Serialize(p));
            }
        }

        /// <summary>What every start takes from earth.json and this build, saved or not (the handshake fields clients read);
        /// the temperature range only in worlds made before it was frozen (EarthParams.TemperatureRangeFrozen).</summary>
        void ApplyRuntime(EarthParams p)
        {
            if (p.TemperatureRangeFrozen == 0) p.ExtendTemperature = Config.ExtendTemperatureRange;
            p.RealSeasons = Config.RealSeasons;
            EarthWorldOptions.Runtime(Config, p);
            p.WeatherRules = EarthWeather.RulesVersion;
        }

        /// <summary>An active world's saved rules: warnings for rules newer than this build, Hydrology's vessels setting, the
        /// sky's longitude. (A world's layout Version is not compared: every rule is frozen in a field of its own, and saves
        /// from before the last layout change, Earth Map 0.12, are refused anyway for lacking Cylinder World.)</summary>
        void AnnounceSavedRules(EarthParams p, ISaveGame save)
        {
            if (p.Summits > KnownSummits.Version || p.AlpineSurfaceRules > MountainEcology.SurfaceVersion)
                Mod.Logger.Warning("Earth mountain rules are newer than this build (summits {0}, alpine surface {1}); current rules are used this session and saved versions are retained.", p.Summits, p.AlpineSurfaceRules);
            if(p.AridRiverRules>AridHydrology.Version || p.StraitRules>NavigableStraits.Version || p.ReservoirRules>EarthParams.ReservoirRulesVersion || p.LakeOutlets>OutletData.FileVersion
                || p.SeaReferee>SeaRefereeData.FileVersion || p.RiverCourses>RiverNetwork.Glo90CoursesVersion || p.LakeOutlines>LakeOutlineData.FileVersion
                || p.RiverOutlines>RiverOutlineData.FileVersion || p.DamRules>DamRemovalData.NewestRules || p.ChannelRules>TerrainModel.ChannelRulesVersion)
                Mod.Logger.Warning("Earth water terrain rules are newer than this build; current rules are used this session and saved versions are retained.");
            if (p.GeothermalRules > Geothermal.RulesVersion)
                Mod.Logger.Warning("Earth geothermal rules {0} are newer than this build ({1}); current rules are used this session and the saved version is retained.", p.GeothermalRules, Geothermal.RulesVersion);
            if (p.DetailRules > TerrainModel.DetailRulesVersion)
                Mod.Logger.Warning("Earth detail noise rules {0} are newer than this build ({1}); this build's are used this session and the saved version is retained.", p.DetailRules, TerrainModel.DetailRulesVersion);
            // pool layout 5 draws a sill under every 1-block river step, so Hydrology's communicating vessels (deep canals fill
            // layer by layer) can't flood the steps: Hydrology reads this world setting (decision 0024)
            // (pool layout 8 has no sills: only a Hydrology that reads the river flow query may fill vessels there, so the world
            // asks under a key older Hydrology versions don't read; decision 0027)
            string vesselsKey = p.RiverPoolLayout >= 8 ? "hydrologyFillVesselsWithRiverQuery" : "hydrologyFillVessels";
            if (p.RiverPoolLayout >= 5 && !save.WorldConfiguration.GetBool(vesselsKey)) save.WorldConfiguration.SetBool(vesselsKey, true);
            UseSkyLongitude(p, sapi);
        }

        /// <summary>A world whose data its rules read isn't in this build: what new chunks get instead.</summary>
        void ReportDataMismatch(EarthParams p)
        {
            if (p.SeaReferee >= 1 && Data.SeaRefereeAvailable == 0)
                Mod.Logger.Warning("This world's sea is refereed by GLO-90's water mask (sea-referee.bin), but {0}; chunks generated now follow the installed coasts and may meet the older ones with a step.",
                    Data.SeaReferee == null ? "this Earth build has no sea-referee.bin" : "the installed terrain and water data packs aren't the ones it was made for");
            else if (p.SeaReferee >= 1 && p.SeaReferee < SeaRefereeData.FileVersion)
                Mod.Logger.Warning("This world's sea was refereed by an earlier test build (sea referee {0}); this build's data (sea referee {1}) also moves the coastline to the water mask's where GSHHG's differs, so chunks generated now may meet older ones with a step there.",
                    p.SeaReferee, SeaRefereeData.FileVersion);
            if (p.Works1318 >= 1 && Data.Works1318 == null)
                Mod.Logger.Warning("This world's coasts and waters are as of 1318 (decision 0064), but this Earth build has no {0}; chunks generated now have today's and may meet the older ones with a step.", Works1318Data.FileName);
            else if (p.Works1318 > Works1318Data.RulesVersion)
                Mod.Logger.Warning("This world's coasts and waters as of 1318 are rules {0}, newer than this build's ({1}); this build's are used.", p.Works1318, Works1318Data.RulesVersion);
            if (p.PerennialRules >= 1 && p.AridRiverRules >= 1 && Data.NonPerennial == null)
                Mod.Logger.Warning("This world's dry rivers follow GIRES' non-perennial rivers (decision 0069), but this Earth build has no {0}; " +
                    "chunks generated now dry rivers by their catchments' rainfall and may meet the older ones with a step.", NonPerennialData.FileName);
        }

        /// <summary>
        /// The world's terrain model, with the dam file's course levels, GLO-90's heights and this side's climate rules; false
        /// when it can't be built (logged, the server stopping). <paramref name="modelMs"/>: how long it took.
        /// </summary>
        bool BuildAndAttachModel(EarthParams p, out long modelMs)
        {
            modelMs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 0053 item 19 (proposed): the dam file, whose river course levels go into the model (its heights to GLO-90's tiles)
            var elevation = sapi.ModLoader.GetModSystem<EarthElevationSystem>();
            ModelCache cache = null;
            try
            {
                var dams = p.DamRules >= 1 ? elevation?.LoadDams(p) : null;
                // decision 0069: the river flow file, whose rerouted mouths go into the courses
                var flow = p.FlowRules >= 1 ? elevation?.LoadFlow(p) : null;
                cache = OpenModelCache(p, dams != null, flow != null);
                Model = p.BuildModel(Data, dams, cache, flow);
                if (flow != null)
                    Mod.Logger.Notification(p.FlowReroutes >= 0
                        ? "Earth rivers: the flow of the water mask's rivers from {0} (decision 0069); {1} mouth reaches rerouted through their own water."
                        : "Earth rivers: {0} was solved on another course list; its flow is used, its rerouted mouths are not.", RiverFlowData.FileName, p.FlowReroutes);
            }
            catch (Exception e) when (DataProblem(e) is Exception data)
            {
                // data this world's rules read is missing or not the one it was made with (a changed peaks.json): thrown here,
                // at WorldReady, it would crash the server. Stopped now, before world generation starts, the save stays as it is.
                // (A step that runs on several cores throws it wrapped in an AggregateException: DataProblem unwraps it.)
                // As Refuse: the shutdown is asked for first, then the step ends with an exception, so that the WorldReady steps
                // after this one (fauna, placement, plants, geology, ores) don't run on a world with params and no model (CR-L3).
                string message = "Earth Map can't build this world's terrain: " + data.Message + ". The server stops before generating anything; fix this and restart.";
                Mod.Logger.Error("{0}", message);
                sapi.Server.ShutDown();
                throw new InvalidOperationException(message, data);
            }
            if (cache != null) SaveModelCache(cache);
            // decision 0051 (proposed): GLO-90's land heights for a world that chose them
            elevation?.AttachServer(Model, p);
            UseClimateRules(p, Model);
            // what the weather needs every side to agree on (decision 0017): the data and whether the sun follows longitude
            p.WeatherDataSignature = Data.Weather?.Signature;
            p.WeatherLocalSun = SkyFollowsLongitude;
            // A6: world generation's vegetation zones and fire rules read weather.bin too, and the world froze the file it was
            // made with; another file changes them in chunks generated from now on (said once, the world goes on)
            if (p.WeatherDataMade != null && p.WeatherDataMade != p.WeatherDataSignature)
                Mod.Logger.Warning("Earth weather data: this world was created with weather.bin {0}, but this Earth has {1}. The vegetation zones and " +
                    "fire rules of chunks generated from now on follow the installed file and may meet the older chunks with a seam; use the Earth " +
                    "version the world was made with to keep them.", p.WeatherDataMade, p.WeatherDataSignature ?? ("none (" + (Data.WeatherError ?? "missing") + ")"));
            ReportMissingClimateData(p, "new chunks get the coarser climate grid's temperatures",
                "new chunks get an older climate (v" + Data.ClimateFixApplied + ")");
            modelMs = sw.ElapsedMilliseconds;
            return true;
        }

        /// <summary>The data problem (missing or wrong data: IOException, InvalidDataException) an exception is or carries
        /// (the parallel steps' AggregateException holds the workers' exceptions), else null.</summary>
        static Exception DataProblem(Exception e)
        {
            if (e is IOException or InvalidDataException) return e;
            if (e is AggregateException a)
                foreach (var inner in a.Flatten().InnerExceptions)
                    if (inner is IOException or InvalidDataException) return inner;
            return null;
        }

        /// <summary>The model cache's files: beside the data store, shared by the worlds of this data folder.</summary>
        static string ModelCacheFolder => Path.Combine(GamePaths.DataPath, "ModData", "earth", "cache");
        /// <summary>The layout of <see cref="ModelCacheInputs"/> (first in it, so WhatChanged reads only its own layout).</summary>
        const string ModelCacheInputsLayout = "earth-model-cache-inputs-4";
        /// <summary>The save's record of the model cache key it used last (so a start that can't use it says why).</summary>
        const string ModelCacheKeyKey = "earthModelCacheKey";

        /// <summary>
        /// What the cached steps of a world's model build read, for the model cache's key (Core.ModelCache): this Earth build
        /// (its DLL's module id: any other build, even of the same version, builds afresh, so a code change can never meet
        /// results of older code; it also pins the data, whose files the store checks against the sizes and SHA-256 compiled
        /// into it), whether the dam file is there, and the world's parameters, without those set after the build at a start
        /// or while it runs: the spawn, the story placement and its flat zones (EarthParams.BuildModel adds the zones to the
        /// model last, after the straits and the sea channels), the ore, geology and terrain-plan signatures, the weather
        /// handshake, the ground and deposit sets. None of them reaches the cached steps, and keeping them made the second
        /// start of a new world miss. Any other field that changes (a config edit, an upgrade) only makes the next start
        /// build afresh. (The names of the data files opened were part of the key at first: a new world opens some of them
        /// before the build that a later start opens only during it, so they made the second start miss too.)
        /// </summary>
        byte[] ModelCacheInputs(EarthParams p, bool dams, bool flow = false)
        {
            var copy = SerializerUtil.Deserialize<EarthParams>(SerializerUtil.Serialize(p));
            copy.SpawnLat = copy.SpawnLon = 0;
            copy.SpawnX = copy.SpawnZ = 0;
            copy.StoryPlaced = false;
            copy.Zones = null;
            copy.GeologyProviderSignature = copy.OreDataSignature = copy.TerrainPlanSignature = null;
            copy.WeatherDataSignature = copy.WeatherDataMade = null;
            copy.WeatherLocalSun = false;
            copy.GroundData = 0;
            copy.DepositSet = 0;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(ModelCacheInputsLayout);
                w.Write(typeof(EarthParams).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
                w.Write(Mod.Info.Version ?? "");
                // the data files this mod itself carries (they win over the store's, and a few have no hash compiled in)
                w.Write(EarthDataset.DataIdentity(Mod.SourcePath));
                // the runtime and platform the searches' arithmetic (Math.Pow, Sin, Cos) ran on, for a data folder shared or
                // kept across a game update that changes the runtime
                w.Write(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " " +
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + " " +
                    (OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other"));
                w.Write(dams);
                w.Write(flow);
                byte[] prm = SerializerUtil.Serialize(copy);
                w.Write(prm.Length);
                w.Write(prm);
            }
            return ms.ToArray();
        }

        /// <summary>The model cache for this world's build (null when the config turns it off or its key can't be made: the
        /// model is then built as before).</summary>
        ModelCache OpenModelCache(EarthParams p, bool dams, bool flow = false)
        {
            if (Config?.ModelCache == false) return null;
            try { return ModelCache.Open(ModelCacheFolder, ModelCacheInputs(p, dams, flow)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProtoBuf.ProtoException)
            {
                Mod.Logger.Warning("Earth model cache: not used this start ({0}); the model is built in full.", e.Message);
                return null;
            }
        }

        /// <summary>Says what the cache gave (or why it gave nothing although the world had one), saves what was built, and
        /// records the key in the save. The model is built by now and doesn't depend on any of this: whatever goes wrong here
        /// is said in one line and the start goes on (Codex review of 2.0.0-rc.4: a damaged older file made the explanation
        /// throw).</summary>
        void SaveModelCache(ModelCache cache)
        {
            try { ReportAndSaveModelCache(cache); }
            catch (Exception e)
            {
                Mod.Logger.Warning("Earth model cache: could not be saved or explained ({0}: {1}); the world goes on, the next start builds what it needs.", e.GetType().Name, e.Message);
            }
        }

        void ReportAndSaveModelCache(ModelCache cache)
        {
            var save = sapi.WorldManager.SaveGame;
            string last = save.GetData(ModelCacheKeyKey) is byte[] b ? System.Text.Encoding.ASCII.GetString(b) : null;
            if (cache.Built.Count == 0 && cache.Reused.Count == 0)
            {
                // no straits and no sea channels in this world: nothing to keep, no file
                Mod.Logger.Notification("Earth model cache: nothing to keep for this world (no straits or sea channels).");
                return;
            }
            if (cache.Built.Count == 0)
                Mod.Logger.Notification("Earth model cache: {0} from model-{1}.bin.", string.Join(", ", cache.Reused), cache.Key);
            else if (cache.Problem != null)
                Mod.Logger.Warning("Earth model cache: model-{0}.bin could not be used ({1}); built again: {2}.", cache.Key, cache.Problem, string.Join(", ", cache.Built));
            else if (last != null && last != cache.Key)
                Mod.Logger.Notification("Earth model cache: built {0} again, as {1} changed since this world's last start.", string.Join(", ", cache.Built),
                    WhatChanged(ModelCache.InputsOf(ModelCacheFolder, last), cache.Inputs));
            else
                Mod.Logger.Notification("Earth model cache: built {0}; kept for later starts.", string.Join(", ", cache.Built));
            string why = cache.Save();
            if (why != null) Mod.Logger.Warning("Earth model cache: could not write {0} ({1}); the next start builds it again.", ModelCacheFolder, why);
            save.StoreData(ModelCacheKeyKey, System.Text.Encoding.ASCII.GetBytes(cache.Key));
        }

        /// <summary>What differs between two sets of model cache inputs, in words (the Earth build, the data, the dam file, or
        /// the world's settings by name).</summary>
        static string WhatChanged(byte[] before, byte[] now)
        {
            if (before == null) return "the world's settings or this Earth (the earlier cache file is gone)";
            try
            {
                static (Guid Build, string Version, string Data, string Runtime, bool Dams, bool Flow, byte[] Params) Read(byte[] bytes)
                {
                    using var r = new BinaryReader(new MemoryStream(bytes));
                    if (r.ReadString() != ModelCacheInputsLayout) return default;
                    var build = new Guid(r.ReadBytes(16));
                    string version = r.ReadString(), data = r.ReadString(), runtime = r.ReadString();
                    bool dams = r.ReadBoolean(), flow = r.ReadBoolean();
                    return (build, version, data, runtime, dams, flow, r.ReadBytes(r.ReadInt32()));
                }
                var a = Read(before);
                var b = Read(now);
                if (a.Params == null) return "this Earth (the world's last start was made by a build with other cache inputs)";
                var what = new System.Collections.Generic.List<string>();
                if (a.Build != b.Build) what.Add(a.Version == b.Version ? "the Earth build" : "the Earth version (" + a.Version + " to " + b.Version + ")");
                if (a.Data != b.Data) what.Add("the mod's data files");
                if (a.Runtime != b.Runtime) what.Add("the runtime (" + a.Runtime + " to " + b.Runtime + ")");
                if (a.Dams != b.Dams) what.Add("the dam file");
                if (a.Flow != b.Flow) what.Add("the river flow file");
                var pa = SerializerUtil.Deserialize<EarthParams>(a.Params);
                var pb = SerializerUtil.Deserialize<EarthParams>(b.Params);
                var fields = typeof(EarthParams).GetFields()
                    .Where(f => f.IsDefined(typeof(ProtoBuf.ProtoMemberAttribute), false))
                    .Where(f => !Equals(f.GetValue(pa), f.GetValue(pb)) && !(f.GetValue(pa) is System.Collections.IEnumerable ea && f.GetValue(pb) is System.Collections.IEnumerable eb && f.FieldType != typeof(string) && ea.Cast<object>().SequenceEqual(eb.Cast<object>())))
                    .Select(f => f.Name).ToList();
                if (fields.Count > 0) what.Add("the world's settings (" + string.Join(", ", fields.Take(12)) + (fields.Count > 12 ? ", …" : "") + ")");
                return what.Count > 0 ? string.Join(" and ", what) : "nothing that can be named";
            }
            // (only a log line's words: whatever an older or odd file makes the readers throw, the cache is saved all the same)
            catch (Exception) { return "something that can't be read back"; }
        }

        /// <summary>The climate data this world's rules read (EarthParams.ConfigureClimateData) that this Earth doesn't have,
        /// with what that means on this side.</summary>
        void ReportMissingClimateData(EarthParams p, string withoutFineTemperatures, string withoutClimateFix)
        {
            if (p.ClimateRules >= TerrainModel.RealLapseRules && !Data.FineTemperatures)
                Mod.Logger.Error("Earth Map: this world uses CHELSA's temperatures (earthdata/temperature.bin), which this Earth Map doesn't have{0}: {1}. Reinstall the mod.",
                    Data.FineTemperatureError != null ? " (" + Data.FineTemperatureError + ")" : "", withoutFineTemperatures);
            if (p.ClimateData > 0 && Data.ClimateFixApplied != p.ClimateData)
                Mod.Logger.Error("Earth Map: this world uses the climate fix v{0} (earthdata/climatefix{1}.bin), which this Earth Map doesn't have: {2}. Reinstall the mod.",
                    p.ClimateData, p.ClimateData == 1 ? "" : p.ClimateData.ToString(), withoutClimateFix);
        }

        /// <summary>A new world's spawn, handed to Cylinder World so vanilla's story chain starts there too.</summary>
        void FinishNewWorld(EarthParams p, ISaveGame save, CylinderCompat cw)
        {
            if (sapi.WorldManager.MapSizeY < 384)
                Mod.Logger.Notification("Earth Map: this world is {0} blocks high; Earth's heights are tuned for 384 (World height on the world creation screen).", sapi.WorldManager.MapSizeY);
            EarthSpawn.Choose(sapi, Model, Config, p, Mod.Logger);
            // Cylinder World picked its own spawn (map centre, starting-climate latitude) and moves vanilla's story
            // chain there; make it ours, so the chain and its vanilla spots (the devastation area's) start from here. Also on
            // the second start of a world whose first start did not finish (SaveGame.IsNew is false then; only a world
            // Earth has just made gets here).
            if (!CylinderCompat.SetSpawn(sapi, p.SpawnX, p.SpawnZ))
                Mod.Logger.Warning("Cylinder World didn't take Earth's spawn: vanilla's story chain starts from its spawn at x={0} z={1}.", cw.SpawnX, cw.SpawnZ);
            save.StoreData(SaveKey, SerializerUtil.Serialize(p));
        }

        public void SaveParams()
        {
            // only an active Earth world: a vanilla world (Earth off, or added to an existing world) gets no Earth params
            if (sapi != null && Params?.Enabled == true) sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(Params));
        }

        /// <summary>Never stops the world from loading: without the hooks or the data, forests fall back to the earlier picks.</summary>
        public RegionalTreeCatalog DesertTreeRanges { get; private set; }
        void LoadRegionalTrees(EarthParams p)
        {
            RegionalTrees = null;
            DesertTreeRanges=null;
            if (!p.RegionalTrees)
            {
                DesertTreeRanges=RegionalTreeCatalog.TryLoad(
                    sapi.Assets.TryGet(new AssetLocation("earth:config/regionaltrees.json"))?.ToText(),
                    sapi.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"))?.ToText(),
                    sapi.ModLoader.Mods.Select(m=>m.Info.ModID),out string desertError);
                if(DesertTreeRanges==null)Mod.Logger.Warning("Earth desert native ranges unavailable ({0}); legacy hot-desert tree attempts will be skipped.",desertError);
                return;
            }
            if (!EarthTrees.Patched)
            {
                Mod.Logger.Warning("Earth regional trees: the tree hooks are unavailable in this game version ({0}); forests keep vanilla's species pick.",
                    EarthTrees.PatchError ?? "not patched");
                return;
            }
            string error;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RegionalTrees = RegionalTreeCatalog.TryLoad(
                    sapi.Assets.Get(new AssetLocation("earth:config/regionaltrees.json"))?.ToText(),
                    sapi.Assets.Get(new AssetLocation("earth:config/treeregions.json"))?.ToText(),
                    sapi.ModLoader.Mods.Select(m => m.Info.ModID), out error);
                if (RegionalTrees != null)
                    Mod.Logger.Notification("Earth regional trees v{0}: {1} rules; providers: {2} ({3} ms)", RegionalTreeCatalog.RulesVersion,
                        RegionalTrees.Rules.Count, string.Join(", ", RegionalTrees.Providers), sw.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                RegionalTrees = null;
                error = e.Message;
            }
            DesertTreeRanges=RegionalTrees;
            if (RegionalTrees == null)
                Mod.Logger.Error("Earth regional trees: could not load the tree data ({0}); forests use the earlier forest-type species mix.", error ?? "unknown error");
        }

        /// <summary>Why Cylinder World can't carry a new Earth world (decision 0023), or null when it can: it must be active here
        /// with its planet layout (layout v3+, polar walls and polar climate), so its latitudes, seasons and day length are the
        /// Earth's.</summary>
        static string CylinderWorldMissing(CylinderCompat cw)
        {
            if (cw == null) return "Cylinder World is not active in this world (set \"Enabled\": true in ModConfig/cylinderworld.json; " +
                "it also turns itself off on a map too narrow for its seam, see its log line)";
            var missing = new System.Collections.Generic.List<string>();
            if (cw.Version < 3) missing.Add("layout v3 or newer (it has v" + cw.Version + ")");
            if (!cw.HasWalls) missing.Add("\"PolarWalls\": true (a map too short for the walls turns them off, see its log)");
            if (!cw.PolarClimate) missing.Add("\"PolarClimate\": true");
            return missing.Count == 0 ? null : "Cylinder World is active without " + string.Join(", ", missing);
        }

        /// <summary>
        /// Whether Cylinder World keeps Earth's own terrain in its seam band (decision 0030): only then can the real Earth
        /// run across the jump line. That is its Earth polar mode in a new world (PolarIceMode "earth": SeamParams.EarthPolarIce)
        /// with layout v10 or later, where it keeps Earth's seabed and land (CW-D26, CW-D31). Otherwise its own sea band would
        /// cut the Earth along the jump line, so the world keeps the gutter.
        /// </summary>
        bool CylinderKeepsEarthSeam(CylinderCompat cw)
        {
            bool earthMode, layout10;
            try { earthMode = cw.GetBool("EarthPolarIce"); layout10 = cw.Get("Version") >= 10; }
            catch (MissingMemberException) { earthMode = layout10 = false; }
            if (earthMode && layout10) return true;
            Mod.Logger.Notification("Earth Map: Cylinder World is not in its Earth polar mode (PolarIceMode \"earth\") in this new world, so its sea band " +
                "would cut the Earth at the jump line; the world keeps the seam gutter of open sea (decision 0030).");
            return false;
        }

        EarthParams ComputeParams(EarthData data, CylinderCompat cw, out string report)
        {
            int W = sapi.WorldManager.MapSizeX, H = sapi.WorldManager.MapSizeY;
            int sea = sapi.World.SeaLevel;
            bool realSeam = !Config.SeamGutter && CylinderKeepsEarthSeam(cw);
            var inp = new LayoutInput
            {
                WorldWidth = W,
                GutterMargin = Config.GutterMargin,
                Gutter = !realSeam,
                CwRingStart = cw.JumpInset,
                CwRingLength = cw.Circumference,
                CwBandInner = cw.SeamHalfWidth - cw.JumpInset,
                CwEquatorZ = cw.EquatorZ,
                CwPoleDistance = cw.PoleDistance,
            };
            int gutterSeabed = cw.SeabedY;
            string badLayout = EarthLayout.Sanitize(inp);
            if (badLayout.Length > 0) Mod.Logger.Warning("{0}: {1} can't place the Earth; this world uses the defaults for them (it saves the layout, so fix the config before creating the next one).", ConfigFile, badLayout);
            var geo = EarthLayout.Build(inp, data, out report);
            double landCurve = Config.LandCurve, seaCurve = Config.SeaCurve, topMargin = Config.TopMargin, seaDepth = Config.MaxSeaDepth;
            string badCurves = VerticalScale.Sanitize(ref landCurve, ref seaCurve, ref topMargin, ref seaDepth);
            if (badCurves.Length > 0) Mod.Logger.Warning("{0}: {1} can't make terrain; this new world uses the defaults for them (it saves them, so fix the config before creating the next one).", ConfigFile, badCurves);
            var terrain = EarthWorldOptions.Terrain(sapi.WorldManager.SaveGame.WorldConfiguration, H);
            var vs = terrain.Build(H, sea, topMargin, landCurve, seaCurve, seaDepth);
            report += string.Format(" World height {0}, sea level {1}: Everest at y={2:0}, deepest sea floor at y={3:0}.", H, sea, vs.TopY, sea - 1 - vs.MaxDepth);
            int hydrologyQuery = RequiredMods.QueryVersion(sapi);
            return new EarthParams
            {
                Version = EarthParams.CurrentVersion,
                Enabled = true,
                RegionalTrees = Config.RegionalTrees,
                TreeRulesVersion = Config.RegionalTrees ? RegionalTreeCatalog.RulesVersion : 0,
                RegionalGeology = Config.RegionalGeology,
                RegionalOres = Config.RegionalOres,
                OreRulesVersion = Config.RegionalOres ? RegionalOreCatalog.Version : 0,
                OreSettingsJson = System.Text.Json.JsonSerializer.Serialize(Config.Ores ?? new OreTuning()),
                GeologyRulesVersion = Config.RegionalGeology ? RegionalGeologyCatalog.RulesVersion : 0,
                GeologyTransitionBlocks = Config.GeologyTransitionBlocks,
                RegionalFauna = Config.RegionalFauna,
                FaunaRulesVersion = Config.RegionalFauna ? RegionalFaunaCatalog.RulesVersion : 0,
                IncludeExtinctAnimals = Config.IncludeExtinctAnimals,
                // A04: it reaches world generation's temperatures, so the world keeps the value it was made with
                ExtendTemperature = Config.ExtendTemperatureRange,
                TemperatureRangeFrozen = 1,
                RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
                EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
                Cylinder = true,
                GutterBridges = true,
                GutterSeabed = realSeam ? TerrainModel.RealSeamVersion : 2,
                RiverFalls = Config.Rivers ? RiverFalls.Version : 0,
                RiverChannels = Config.Rivers ? SeaChannels.Version : 0,
                RiverEnds = Config.Rivers ? Core.RiverEnds.Version : 0,
                RiverDeltas = Config.Rivers ? Core.RiverDeltas.Version : 0,
                RiverWidths = Config.Rivers ? RiverNetwork.WidthRulesVersion : 0,
                // pool layout 8 (steps without sills) only with a Hydrology that reads the river flow query (decision 0027): without
                // it, sill-less steps are not safe from its communicating vessels. Pool layouts 9 and 10 (flow-labelled rapid
                // sources) only with one that reads RiverFlowAt and keeps those labels when the water updates (its query version 2).
                // Pool layout 11 (flow-labelled calm rivers, decision 0046) only with one that keeps them in every river column (3).
                // RequiredMods refuses a new Earth world before this without query version 3 (RiverQueryMinimum, Hydrology
                // 0.6.2), so every new world gets layout 11; the fallback to 10 below is kept for a Hydrology that reports less.
                RiverPoolLayout = !Config.Rivers ? 0 : hydrologyQuery >= 3 ? RiverNetwork.PoolLayoutVersion : Math.Min(10, RiverNetwork.PoolLayoutVersion),
                MinSeaDepth = TerrainModel.ShipSeaDepth,
                LakeShores = ShoreData.FileVersion,
                // the test profiles of decision 0031 also store the real rainfall (rules 3); the defaults keep rules 2
                ClimateRules = terrain.Experimental ? TerrainModel.RealRainRules : TerrainModel.RealLapseRules,
                LinearLand = vs.LinearLand, LocalAlpineEcology = terrain.LocalEcology, PhysicalRockExposure = terrain.PhysicalRock,
                TerrainProfile = terrain.Experimental ? terrain.Budget + "/" + terrain.Curve + "/" + terrain.Detail : null,
                VanillaFaunaRanges = terrain.Experimental ? VanillaFaunaRanges.Version : 0,
                FineRain = terrain.Experimental && DataHas(data, d => { d.SetFineRain(true); return d.FineRain; }, d => d.FineRainError) ? 1 : 0,
                Summits = terrain.Experimental && DataHas(data, d => { d.SetSummits(true); return d.HasSummits; }, d => d.SummitsError) ? KnownSummits.Version : 0,
                AridRiverRules = AridHydrology.Version,
                StraitRules = NavigableStraits.Version,
                ReservoirRules = EarthParams.ReservoirRulesVersion,
                LakeOutlets = data?.Outlets != null ? OutletData.FileVersion : 0,
                SeaReferee = data?.SeaRefereeAvailable ?? 0,
                // decision 0064: coasts and waters as of 1318 where people changed them later
                Works1318 = data?.Works1318 != null ? Works1318Data.RulesVersion : 0,
                PerennialRules = data?.NonPerennial != null ? NonPerennialData.RulesVersion : 0,
                AlpineSurfaceRules = terrain.LocalEcology ? MountainEcology.SurfaceVersion : 0,
                SoilRules = SoilFertility.RulesVersion,
                GroundColumns = Config.RegionalGeology && Config.GroundColumnsPrototype ? EarthMap.Core.GroundColumns.RulesVersion : 0,
                OreLayers = Config.RegionalGeology && Config.GroundColumnsPrototype ? EarthMap.Core.OreLayers.RulesVersion : 0,
                // 0065: rules v4 needs the ore layers (bodies keep to their layers)
                OreRules = Config.RegionalOres && Config.RegionalGeology && Config.GroundColumnsPrototype ? OreBackground.CurrentRules : 0,
                GroundCoverRules = GroundCover.RulesVersion,
                ZoneRules = ClimateZones.RulesVersion,
                MoistureRules = GroundMoisture.RulesVersion,
                GeothermalRules = Geothermal.RulesVersion,
                GrassWinterRules = GrassWinter.RulesVersion,
                FireRules = FireRegime.RulesVersion,
                WetlandRules = WetlandGrid.RulesVersion,
                AridityRules = data != null && DataHas(data, d => { d.SetPenmanAridity(true); return d.PenmanAridity; }, d => d.PenmanError) ? EarthData.PenmanAridityVersion : 0,
                WaterTableRules = WaterTable.RulesVersion,
                PeakCatalogSignature = terrain.Experimental && data?.HasSummits == true ? data.ReadPeaks()?.Signature : null,
                Glaciers = terrain.Experimental && DataHas(data, d => { d.SetGlaciers(true); return d.HasGlaciers; }, d => d.GlaciersError) ? 1 : 0,
                ClimateData = data?.ClimateFixAvailable ?? 0,
                // A6: the zones and fire rules read weather.bin's monthly normals; a later start compares (BuildAndAttachModel)
                WeatherDataMade = data?.Weather?.Signature,
                Canals = EarthWorldOptions.CanalCodes(sapi.WorldManager.SaveGame.WorldConfiguration),
                Islands = Config.Islands ? IslandLayer.RulesVersion : 0,
                Coasts = CoastsFor(geo, Config.Islands),
                MinIslandBlocks = Config.MinIslandBlocks,
                MapHeight = H, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
                GutterSeabedY = Math.Max(gutterSeabed, (int)(sea - 1 - vs.MaxDepth)),
                NoiseSeed = sapi.WorldManager.Seed,
                DetailScale = Config.DetailScale * terrain.DetailMultiplier,
                Rivers = Config.Rivers,
                RiverMinDischarge = Config.RiverMinDischarge,
                RiverWidthScale = Config.RiverWidthScale,
                StreamMode = EarthWorldOptions.StreamsFor(Config.Streams, geo.RingLength),
                StreamMinDischarge = Config.StreamMinDischarge,
                RiverStraighten = Config.RiverStraighten,
                RiverSmoothWidths = Config.RiverSmoothWidths,
                RiverRelief = Config.RiverRelief,
            };
        }

        // ------------------------------------------------------------------------------------------ client

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<EarthParams>()
                .SetMessageHandler<EarthParams>(OnParams);
            api.Event.OnGetClimate += (climateHooks ??= new EarthClimateHooks(this)).OnGetClimate;
        }

        void OnParams(EarthParams p)
        {
            // (no provider check here: the required providers are server-side, RequiredMods, and the server checked them)
            Params = p;
            if (!p.Enabled)
            {
                Model = null;
                ClimateLapse.Use(this, null);
                astraTerra.Clear();
                sky.Set(null);
                RestoreSeaLevel();
                return;
            }
            UseSkyLongitude(p, capi);
            astraTerraLook.Suggest(capi, Mod.Logger);
            try
            {
                Data ??= LoadData(false);
                p.ConfigureClimateData(Data);
                ReportMissingClimateData(p, "the live temperatures here differ from the server's", "the live temperatures here differ from the server's");
                Model = new TerrainModel(Data, p.BuildGeometry(), p.BuildVertical(), p.NoiseSeed) { ClimateRules = p.ClimateRules, GeothermalRules = p.GeothermalRules, ClientClimate = true };
                // vanilla's client never sets this (its GetRainFall reads it): the world's own sea level, also a lowered one;
                // the value before is put back when this client leaves (CR-L4: a client that joins a non-Earth server next, in the
                // same process, would keep Earth's level, as vanilla resets it only on a server)
                seaLevelBefore ??= Climate.Sealevel;
                Climate.Sealevel = p.SeaLevel;
                UseClimateRules(p, Model);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Could not load the Earth climate data: {0}", e.Message);
            }
        }

        /// <summary>Whether an optional data file of decision 0031 is in this Earth Map (a new world records it only then); logs why not.</summary>
        bool DataHas(EarthData data, System.Func<EarthData, bool> load, System.Func<EarthData, string> error)
        {
            if (load(data)) return true;
            Mod.Logger.Error("Earth Map: {0}; this new world goes without it. Reinstall the mod.", error(data) ?? "data missing");
            return false;
        }

        /// <summary>This side's claim on vanilla's patched climate adjustments (ClimateLapse): the real lapse from climate rules 2,
        /// also the real rainfall from rules 3 (decision 0031's test profiles).</summary>
        void UseClimateRules(EarthParams p, TerrainModel model)
            => ClimateLapse.Use(this, p.ClimateRules >= TerrainModel.RealLapseRules ? model.Vertical : null, p.ClimateRules >= TerrainModel.RealRainRules);

        /// <summary>This side's sky longitude (LongitudeAt) from the world's layout, and AstraTerra told about it if it can take it.</summary>
        void UseSkyLongitude(EarthParams p, ICoreAPI api)
        {
            sky.Set(p.BuildGeometry());
            astraTerra.Bind(api, LongitudeAt, Mod.Logger);
        }

        /// <summary>Client: the sea level vanilla's climate had before this client set the world's own (null while unset).</summary>
        int? seaLevelBefore;

        void RestoreSeaLevel()
        {
            if (seaLevelBefore is int before) Climate.Sealevel = before;
            seaLevelBefore = null;
        }

        public override void Dispose()
        {
            ClimateLapse.Use(this, null);
            astraTerra.Clear();
            sky.Set(null);
            RestoreSeaLevel();
            base.Dispose();
        }

        // ------------------------------------------------------------------------------------------ temperature

        /// <summary>Set (this thread) while a caller asks GetClimateAt with Earth's real temperature, clamped to -20..40 °C, as
        /// the base instead of vanilla's map reading (EarthFauna's runtime spawn check): only the range extension applies
        /// (EarthClimateHooks).</summary>
        [ThreadStatic] internal static bool SuppliedEarthTemperature;

        /// <summary>The climate hook (both sides): temperatures beyond vanilla's range and each place's real seasons.</summary>
        EarthClimateHooks climateHooks;
        /// <summary>For the climate hook: the side this system started on.</summary>
        internal ICoreAPI AnyApi => anyApi;

        // ------------------------------------------------------------------------------------------ formatting (also /earth's, EarthCommands)

        public static string FormatLat(double lat) => string.Format(CultureInfo.InvariantCulture, "{0:0.00}°{1}", Math.Abs(lat), lat >= 0 ? "N" : "S");
        public static string FormatLon(double lon) => string.Format(CultureInfo.InvariantCulture, "{0:0.00}°{1}", Math.Abs(lon), lon >= 0 ? "E" : "W");
        public static string FormatLatLon(double lat, double lon) => FormatLat(lat) + " " + FormatLon(lon);
    }
}
