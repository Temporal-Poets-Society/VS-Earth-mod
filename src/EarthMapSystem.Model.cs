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
    /// EarthMapSystem, the world's terrain model: built and attached at the start, kept between starts in the model cache
    /// (its key, the cache file, what changed), and the regional tree catalogue built beside it (EA-0089: split from
    /// EarthMapSystem.cs, no change).
    /// </summary>
    public partial class EarthMapSystem
    {
        /// <summary>
        /// The world's terrain model, with the dam file's course levels, GLO-90's heights and this side's climate rules; false
        /// when it can't be built (logged, the server stopping). <paramref name="modelMs"/>: how long it took.
        /// </summary>
        bool BuildAndAttachModel(EarthParams p, out long modelMs)
        {
            modelMs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 0053 item 19: the dam file, whose river course levels go into the model (its heights to GLO-90's tiles)
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
                        : "Earth rivers: {0} was solved on another course list; its flow is used, its rerouted mouths are not.", RiverFlowData.FileNameFor(p.FlowRules), p.FlowReroutes);
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
            // decision 0051: GLO-90's land heights for a world that chose them
            elevation?.AttachServer(Model, p);
            UseClimateRules(p, Model);
            // what the weather needs every side to agree on (decision 0017): the data and whether the sun follows longitude
            p.WeatherDataSignature = Data.Weather?.Signature;
            // (EA-0118) "none" where the rules read the ground and this server has none (it keeps vanilla's weather, and so
            // do its clients); null where the rules don't read it
            p.WeatherGroundSignature = WeatherRules.For(p.WeatherModelRulesOrOld).Ground ? Data.WeatherGroundSignature ?? EarthParams.NoWeatherGround : null;
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
            copy.WeatherDataSignature = copy.WeatherDataMade = copy.WeatherGroundSignature = null;
            copy.WeatherLocalSun = false;
            copy.GroundData = 0;
            copy.CreatedWith = copy.LastSavedWith = null;
            copy.DepositSet = 0;
            copy.DepositDepthRules = 0;
            copy.VanillaStrataRules = 0;
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
    }
}
