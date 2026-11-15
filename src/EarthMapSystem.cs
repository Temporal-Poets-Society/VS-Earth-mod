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
    public partial class EarthMapSystem : ModSystem
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

        /// <summary>World-config attributes Hydrology reads (its H2Filling.WorldVesselsKey and WorldVesselsWithQueryKey): Earth sets
        /// one on worlds whose river steps have sills, so Hydrology's communicating vessels may fill them (decision 0024); the
        /// second on worlds with the river query (pool layout 8 and later), which older Hydrology versions don't read
        /// (decision 0027). Pinned by tests/ReflectionContractTests.</summary>
        public const string HydrologyVesselsKey = "hydrologyFillVessels", HydrologyVesselsWithQueryKey = "hydrologyFillVesselsWithRiverQuery";

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
        public int RiverQueryVersion => Params?.Enabled == true && PoolLayoutFacts.RapidsReaches(Params.RiverPoolLayout) && Model?.Rivers != null
            ? PoolLayoutFacts.RiverQueryVersion(Params.RiverPoolLayout) : 0;

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
            if (c.River) rapids = !PoolLayoutFacts.DrawnRapids(Params.RiverPoolLayout) ? c.RapidsReach : riverQuery.DrawnRapid(x, z, c, modelColumn);
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

        /// <summary>ER-16 (code review 14 November 2026): cancelled when this server stops (its shutdown phase, which a stop from
        /// another thread enters while this one still waits) or this system is disposed. The first start's downloads (the
        /// data store, the 30" ground sets, the GLO-90 release files) take it, so leaving a world that is still loading no
        /// longer waits for some hundred MB; a cancelled download leaves its .part, which the next start resumes.</summary>
        readonly System.Threading.CancellationTokenSource stopping = new();
        internal System.Threading.CancellationToken Stopping => stopping.Token;

        /// <summary>
        /// ER-18 (code review 14 November 2026): this side's <see cref="Params"/> are set, on the main thread: the server's at
        /// the end of its WorldReady step (an Earth world's once its data and model are there; a vanilla world's disabled
        /// ones), a client's when the server's arrive. The systems that build from them (the weather, thunder, grass seasons
        /// and winter, ground moisture, the snow relight) act on it at once; before, each found them by a 0.5-5 s poll, so the
        /// weather was vanilla's until its first poll and a client could reload its shaders a second time.
        /// </summary>
        public event System.Action<EarthParams> ParamsReady;

        void RaiseParamsReady(EarthParams p)
        {
            var handlers = ParamsReady;
            if (handlers == null || p == null) return;
            foreach (System.Action<EarthParams> h in handlers.GetInvocationList())
            {
                try { h(p); }
                catch (Exception e) { Mod.Logger.Error("Earth Map: a system failed when the world's parameters were ready: {0}", e); }
            }
        }

        void StopDownloads()
        {
            try { stopping.Cancel(); } catch (ObjectDisposedException) { }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, StopDownloads);
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
            Config = EarthConfigStore.Load(api, Mod.Logger, out configUnreadable);
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
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () =>
            {
                // (ER-16, Fable's re-review) a first-start download the server's stop cancelled ends the step quietly: the server is
                // stopping anyway, nothing is saved, and the .part resumes at the next start
                try { InitWorld(); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    Mod.Logger.Notification("Earth: a download stopped with the server; it resumes at the next start (the world is unchanged).");
                }
            });
            api.Event.PlayerJoin += plr => { if (Params != null) serverChannel.SendPacket(Params, plr); };
            Commands = new EarthCommands(this, api, Mod.Logger);
            Commands.Register(api);
            // after every mod's Start(): vanilla's season handler (ModTemperature) subscribes there and overwrites the live temperature
            api.Event.OnGetClimate += (climateHooks ??= new EarthClimateHooks(this)).OnGetClimate;
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
            // (the Fable reviewer's note on 854af7b) an Earth world never runs on the defaults of an earth.json that can't be
            // read: a new one would freeze them for good, a saved one could fetch its data from GitHub instead of the file's
            // mirror; a vanilla world goes on
            if (p.Enabled && configUnreadable != null)
            {
                Refuse("earth.json can't be read (" + configUnreadable + "); fix it, or delete it to start from the defaults (the world is unchanged)");
                return;
            }
            if (p.Enabled && !fresh && (p.RulesNewerThanBuild() is { Count: > 0 } || p.HasUnknownFields))
            {
                // ER-05: an older build would make its own (older) rules' chunks in this world for good; FP-04: a newer Earth's
                // new rules come as fields this build doesn't know, which no saved number shows
                var newer = p.RulesNewerThanBuild();
                string by = p.LastSavedWith != null && p.LastSavedWith != Mod.Info.Version ? p.LastSavedWith : "a version after " + Mod.Info.Version;
                string what = newer.Count > 0 ? "its " + string.Join(", ", newer) + " are newer than this build's (" + Mod.Info.Version + ")"
                    : "it holds settings this build (" + Mod.Info.Version + ") doesn't know";
                Refuse("this world was saved by a newer Earth (" + by + "): " + what + "; open it with that version or newer (the world is unchanged)");
                return;
            }
            if (p.Enabled)
            {
                // record only (ER-05): which Earth made the world, which last started it
                bool changed = false;
                if (fresh && p.CreatedWith == null) { p.CreatedWith = Mod.Info.Version; changed = true; }
                if (p.LastSavedWith != Mod.Info.Version) { p.LastSavedWith = Mod.Info.Version; changed = true; }
                if (changed && !fresh) save.StoreData(SaveKey, SerializerUtil.Serialize(p));
            }
            ApplyRuntime(p);
            Params = p;
            // decision 0077: this world's crafting rules, before the server builds the asset packet its clients get
            EarthCrafting.Apply(sapi, p, Mod.Logger);
            if (!p.Enabled) { RaiseParamsReady(p); return; }
            // CR-L1: Earth's generation registers its set-up in its own StartServerSide; where that threw (the game's mod loader
            // logs it and goes on), this world's generation is never set up and vanilla's terrain would fill an Earth world
            if (sapi.ModLoader.GetModSystem<EarthWorldGen>()?.Started != true)
                Refuse("Earth's world generation did not start (its error is logged above)");
            AnnounceSavedRules(p, save);
            // the data first (it reads no params): a start that can't have it stops before any one-shot is applied, and the
            // one-shots that read it (the climate fix, the summits) find it loaded (Codex round 3 item 2, review A2); a saved
            // world needs the store files its rules read (review A5)
            LoadedData(fresh ? null : p);
            // ER-07: a world created in this start takes no one-shot (they are for older worlds)
            // (FP-09: the flags a new world skips are reset in earth.json with its first save, so its later starts don't apply them)
            var upgrades = new WorldUpgrades(new GameUpgradeHost(this, sapi), Mod.Logger, fresh);
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
            RaiseParamsReady(p);
        }

        /// <summary>An Earth world this server can't open as it was made: one error line, the server stops (the RequiredMods
        /// pattern: the exception also ends the rest of this WorldReady step, and nothing of the world is saved before the
        /// game runs).</summary>
        /// <summary>The parser's error when earth.json is there but can't be read (EarthConfigStore.Load), else null.</summary>
        string configUnreadable;

        void Refuse(string why)
        {
            string message = "Earth cannot load this world: " + why + ". The server stops, so that this Earth world never generates " +
                "chunks without Earth's terrain.";
            Mod.Logger.Error("{0}", message);
            sapi.Server.ShutDown();
            throw new InvalidOperationException(message);
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

        public override void Dispose()
        {
            StopDownloads();
            ParamsReady = null;
            // (Fable's final pass before 2.0.0) the store's repair hook holds this system, and with it a closed single-player
            // world's data and model, until the next world sets it
            EarthDataStore.Repair = null;
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
