using System.Reflection;
using EarthMap;
using EarthMap.Core;
using ProtoBuf;
using Vintagestory.API.Common;

/// <summary>
/// (EA-0088, D-R5 of the Fable pass) The one-shot opt-ins of earth.json (WorldUpgrades) without the game: applied to an older
/// world and stored, skipped by a world made in this start (ER-07), each flag off for the session at once and in earth.json
/// only at the first save once the game runs (FP-09), an admin's later edits kept; a one-shot that throws keeps its flag; the
/// rule fixes adopted without a flag (never lowered, S-R1).
/// </summary>
static class WorldUpgradeChecks
{
    sealed class Host : IUpgradeHost
    {
        public EarthMapConfig Config { get; set; }
        public string Folder;
        public readonly List<EarthParams> Stored = new();
        public int SaveParamsCalls, PeakReads;
        public string Canals = "", Peaks;
        public int ClimateFixAvailable { get; set; }
        public System.Func<EarthGeometry, bool, int> Coasts = (_, _) => 0;
        public readonly List<Action> RunGame = new(), SaveHandlers = new();
        public void Store(EarthParams p) => Stored.Add(p);
        public void SaveParams() => SaveParamsCalls++;
        public string CanalCodes() => Canals;
        public string PeakCatalogSignature() { PeakReads++; return Peaks; }
        public int CoastsFor(EarthGeometry geo, bool islands) => Coasts(geo, islands);
        public void OnRunGame(Action handler) => RunGame.Add(handler);
        public void AddSaveHandler(Action handler) => SaveHandlers.Add(handler);
        public void RemoveSaveHandler(Action handler) => SaveHandlers.Remove(handler);
        public void ResetFlags(EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log)
            => EarthConfigStore.ResetFlags(Folder, session, reset, names, log);
        public void Running() { foreach (var h in RunGame.ToArray()) h(); }
        public void Saving() { foreach (var h in SaveHandlers.ToArray()) h(); }
    }

    static readonly FieldInfo[] Flags = typeof(EarthMapConfig).GetFields()
        .Where(f => f.FieldType == typeof(bool) && f.Name.StartsWith("UpgradeExistingWorld", StringComparison.Ordinal)).ToArray();

    static byte[] Bytes(EarthParams p) { using var s = new MemoryStream(); Serializer.Serialize(s, p); return s.ToArray(); }

    public static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "earth-world-upgrades-" + Environment.ProcessId);
        try
        {
            // the names: each one-shot's log and reset line names its flag by its Apply method (ApplyCoasts: UpgradeExistingWorldCoasts)
            var applies = typeof(WorldUpgrades).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Apply", StringComparison.Ordinal)).Select(m => m.Name.Replace("Apply", "UpgradeExistingWorld")).ToHashSet();
            check(Flags.Length > 0 && applies.SetEquals(Flags.Select(f => f.Name)), "world upgrades: every UpgradeExistingWorld flag has its Apply method, and no Apply names a missing flag");
            check(!WorldUpgrades.AnyAsked(null) && !WorldUpgrades.AnyAsked(new EarthMapConfig()) && WorldUpgrades.AnyAsked(new EarthMapConfig { UpgradeExistingWorldCanals = true }),
                "world upgrades: AnyAsked sees a set flag, and only one");

            // a session whose earth.json asks for the given flags (written there, read back as a start reads it)
            Host Start(params string[] asked)
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                var file = EarthConfigStore.Load(folder, TestLog.Create().Logger, out _);
                foreach (var name in asked) typeof(EarthMapConfig).GetField(name).SetValue(file, true);
                EarthConfigStore.Store(file, folder);
                return new Host { Config = EarthConfigStore.Load(folder, TestLog.Create().Logger, out _), Folder = folder };
            }
            bool InFile(string flag) => (bool)typeof(EarthMapConfig).GetField(flag).GetValue(EarthConfigStore.Read(folder));
            bool InSession(Host h, string flag) => (bool)typeof(EarthMapConfig).GetField(flag).GetValue(h.Config);

            // an older world: applied, stored, the flags off for the session; earth.json keeps them until a save once the game runs
            var host = Start("UpgradeExistingWorldRiverFalls", "UpgradeExistingWorldCanals", "UpgradeExistingWorldGutterBridges");
            host.Canals = "suez";
            var p = new EarthParams { Enabled = true, Rivers = true, RiverFalls = RiverFalls.Version, Canals = "" };
            var (logger, log) = TestLog.Create();
            var upgrades = new WorldUpgrades(host, logger);
            upgrades.Layout(p);
            check(p.Canals == "suez" && p.GutterBridges, "world upgrades: the canals and the gutter bridges are applied to an older world");
            check(host.Stored.Count == 2 && host.Stored.All(s => s == p), "world upgrades: the params are stored after each one-shot that changed them (the falls had nothing to do)");
            check(log.Says(EnumLogType.Notification, "UpgradeExistingWorldRiverFalls: nothing to do"), "world upgrades: a one-shot with nothing to do says so");
            check(!InSession(host, "UpgradeExistingWorldRiverFalls") && !InSession(host, "UpgradeExistingWorldCanals") && !InSession(host, "UpgradeExistingWorldGutterBridges"),
                "world upgrades: each asked flag is off for the session at once");
            check(host.RunGame.Count == 1 && host.SaveHandlers.Count == 1, "world upgrades: the run phase and the save event are hooked once");
            upgrades.Terrain(p); upgrades.Regional(p);
            check(host.RunGame.Count == 1 && host.SaveHandlers.Count == 1, "world upgrades: the later groups hook nothing more");
            host.Saving();
            check(InFile("UpgradeExistingWorldCanals") && InFile("UpgradeExistingWorldGutterBridges"), "world upgrades: a save before the game runs writes nothing, so earth.json keeps the flags");
            var edited = EarthConfigStore.Read(folder);
            edited.WeatherPace = 3; edited.UpgradeExistingWorldIslands = true;
            EarthConfigStore.Store(edited, folder);
            (logger, log) = TestLog.Create();
            var later = new WorldUpgrades(host, logger);   // the same session's flags are off: nothing runs again
            later.Layout(p);
            check(host.Stored.Count == 2, "world upgrades: a flag off for the session never runs twice in it");
            host.Running(); host.Saving();
            check(!InFile("UpgradeExistingWorldRiverFalls") && !InFile("UpgradeExistingWorldCanals") && !InFile("UpgradeExistingWorldGutterBridges"),
                "world upgrades: the first save once the game runs resets the flags in earth.json");
            var after = EarthConfigStore.Read(folder);
            check(after.WeatherPace == 3 && after.UpgradeExistingWorldIslands, "world upgrades: what an admin changed in earth.json while the world ran is kept (a flag set for the next start too)");
            check(host.SaveHandlers.Count == 0, "world upgrades: the save handler goes once the flags are reset");

            // a world made in this start (ER-07, FP-09): nothing applied or stored, the flags named in a warning and reset at its save
            host = Start("UpgradeExistingWorldCanals", "UpgradeExistingWorldIslands", "UpgradeExistingWorldOreRules");
            host.Canals = "suez,panama";
            p = new EarthParams { Enabled = true, Islands = 1, Canals = "" };
            byte[] before = Bytes(p);
            (logger, log) = TestLog.Create();
            upgrades = new WorldUpgrades(host, logger, fresh: true);
            upgrades.Layout(p); upgrades.Terrain(p); upgrades.Regional(p);
            check(Bytes(p).AsSpan().SequenceEqual(before) && host.Stored.Count == 0, "world upgrades: a new world's params are not touched by a one-shot");
            check(log.Says(EnumLogType.Warning, "UpgradeExistingWorldCanals, UpgradeExistingWorldIslands") && log.Says(EnumLogType.Warning, "UpgradeExistingWorldOreRules"),
                "world upgrades: a new world names the flags it skips");
            check(!log.Says(EnumLogType.Notification, "keeps its saved island rules"), "world upgrades: a new world says nothing of the island rules it didn't keep");
            host.Running(); host.Saving();
            check(!InFile("UpgradeExistingWorldCanals") && !InFile("UpgradeExistingWorldIslands") && !InFile("UpgradeExistingWorldOreRules"),
                "world upgrades: the flags a new world skipped are reset at its first save (FP-09)");

            // every flag at once: each is in one of the three groups' tables
            host = Start(Flags.Select(f => f.Name).ToArray());
            upgrades = new WorldUpgrades(host, TestLog.Create().Logger);
            p = new EarthParams { Enabled = true };
            upgrades.Layout(p); upgrades.Terrain(p); upgrades.Regional(p);
            check(Flags.All(f => !(bool)f.GetValue(host.Config)), "world upgrades: every flag belongs to a group (Layout, Terrain or Regional)");
            host.Running(); host.Saving();
            check(Flags.All(f => !InFile(f.Name)), "world upgrades: every flag is reset in earth.json");

            // a one-shot that throws keeps its flag (and those after it in its group), so it runs again once the cause is fixed
            host = Start("UpgradeExistingWorldIslands", "UpgradeExistingWorldCoasts", "UpgradeExistingWorldGutterBridges");
            host.Coasts = (_, _) => throw new InvalidOperationException("no coast data");
            upgrades = new WorldUpgrades(host, TestLog.Create().Logger);
            bool threw = false;
            try { upgrades.Layout(new EarthParams { Enabled = true }); } catch (InvalidOperationException) { threw = true; }
            check(threw && !InSession(host, "UpgradeExistingWorldIslands") && InSession(host, "UpgradeExistingWorldCoasts") && InSession(host, "UpgradeExistingWorldGutterBridges"),
                "world upgrades: a one-shot that throws keeps its flag, and so do the ones after it");
            host.Running(); host.Saving();
            check(!InFile("UpgradeExistingWorldIslands") && InFile("UpgradeExistingWorldCoasts") && InFile("UpgradeExistingWorldGutterBridges"),
                "world upgrades: only the applied flags are reset in earth.json");

            // the climate fix and the summits read the loaded data only when they need it
            host = Start("UpgradeExistingWorldClimateData", "UpgradeExistingWorldMountainTerrain");
            host.ClimateFixAvailable = 4; host.Peaks = "peaks-v3";
            p = new EarthParams { Enabled = true, ClimateData = 2, Summits = 0 };
            upgrades = new WorldUpgrades(host, TestLog.Create().Logger);
            upgrades.Layout(p); upgrades.Terrain(p);
            check(p.ClimateData == 4 && host.PeakReads == 0 && p.PeakCatalogSignature == null, "world upgrades: the climate fix moves to the newest; a world without summits reads no peak catalogue");
            host = Start("UpgradeExistingWorldClimateData", "UpgradeExistingWorldMountainTerrain");
            host.Peaks = "peaks-v3";
            p = new EarthParams { Enabled = true, ClimateData = 0, Summits = 2 };
            (logger, log) = TestLog.Create();
            upgrades = new WorldUpgrades(host, logger);
            upgrades.Layout(p); upgrades.Terrain(p);
            check(p.ClimateData == 0 && log.Says(EnumLogType.Notification, "climatefix4.bin are missing"), "world upgrades: without a climate fix in the data the world keeps its climate, and says why");
            check(p.Summits == KnownSummits.Version && p.PeakCatalogSignature == "peaks-v3", "world upgrades: the summits move to this build's catalogue and its signature");

            // the ore rules follow the config, and the crafting rules follow where Earth decides bauxite (decision 0077)
            host = Start("UpgradeExistingWorldOreRules");
            host.Config.RegionalOres = false;
            p = new EarthParams { Enabled = true, RegionalOres = true, OreRulesVersion = 1, CraftingRules = EarthCrafting.RulesVersion };
            new WorldUpgrades(host, TestLog.Create().Logger).Regional(p);
            check(!p.RegionalOres && p.CraftingRules == EarthCrafting.RulesFor(p.GroundColumns, false), "world upgrades: ore maps off takes the crafting rules with them");
            host = Start("UpgradeExistingWorldOreRules");
            p = new EarthParams { Enabled = true, OreLayers = 1, OreDataSignature = "old" };
            new WorldUpgrades(host, TestLog.Create().Logger).Regional(p);
            check(p.RegionalOres == host.Config.RegionalOres && (!p.RegionalOres || (p.OreRulesVersion == RegionalOreCatalog.Version && p.OreRules == OreBackground.CurrentRules
                && p.OreDataSignature == null && p.OreSettingsJson != null)), "world upgrades: ore maps on take this build's ore rules, the real-size deposits in a ground-column world");

            // the rule fixes without a flag: adopted when on and older, never lowered (S-R1), stored only when something changed
            host = Start();
            var cfgNow = host.Config;
            p = new EarthParams { Enabled = true, RegionalTrees = true, TreeRulesVersion = RegionalTreeCatalog.RulesVersion - 1,
                RegionalFauna = true, FaunaRulesVersion = RegionalFaunaCatalog.RulesVersion + 1, IncludeExtinctAnimals = cfgNow.IncludeExtinctAnimals,
                RegionalOres = false, OreRulesVersion = 1, PlacementRules = 0 };
            (logger, log) = TestLog.Create();
            upgrades = new WorldUpgrades(host, logger);
            upgrades.Regional(p);
            check(p.TreeRulesVersion == RegionalTreeCatalog.RulesVersion && host.SaveParamsCalls == 1, "world upgrades: older tree rules are adopted and the params saved");
            check(p.FaunaRulesVersion == RegionalFaunaCatalog.RulesVersion + 1, "world upgrades: a saved rules number above this build's is never lowered (S-R1)");
            check(p.OreRulesVersion == 1 && p.PlacementRules == 0, "world upgrades: rules of a system that is off are left as they are");
            check(host.SaveHandlers.Count == 0, "world upgrades: without a flag nothing is hooked");
            upgrades.Regional(p);
            check(host.SaveParamsCalls == 1, "world upgrades: nothing to adopt, nothing saved");
            p.IncludeExtinctAnimals = !cfgNow.IncludeExtinctAnimals;
            upgrades.Regional(p);
            check(p.IncludeExtinctAnimals == cfgNow.IncludeExtinctAnimals && host.SaveParamsCalls == 2, "world upgrades: a world from before the creation options follows the config's extinct animals");
            p.IncludeExtinctAnimals = !cfgNow.IncludeExtinctAnimals; p.CreationOptionsVersion = 1;
            upgrades.Regional(p);
            check(p.IncludeExtinctAnimals != cfgNow.IncludeExtinctAnimals && host.SaveParamsCalls == 2, "world upgrades: a world with creation options keeps its own");

            // an older world without the flag keeps its island rules, and says so
            if (IslandLayer.RulesVersion > 1)
            {
                host = Start();
                (logger, log) = TestLog.Create();
                new WorldUpgrades(host, logger).Layout(new EarthParams { Enabled = true, Islands = 1 });
                check(log.Says(EnumLogType.Notification, "keeps its saved island rules v1"), "world upgrades: an older world's island rules are kept, with a note");
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
