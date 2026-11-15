using System.Reflection;
using EarthMap;
using Newtonsoft.Json;
using Vintagestory.API.Common;

/// <summary>An ILogger that keeps what it is told, each line by the method's name (Error, Warning, Notification...). A proxy,
/// not a LoggerBase: LoggerBase's static constructor reads its own source path from the API's .pdb, which a copy of the game
/// may not have.</summary>
public class TestLog : DispatchProxy
{
    public readonly List<(EnumLogType Type, string Text)> Lines = new();
    public static (ILogger Logger, TestLog Log) Create() { var logger = DispatchProxy.Create<ILogger, TestLog>(); return (logger, (TestLog)(object)logger); }
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (Enum.TryParse(method.Name, out EnumLogType type) && args.Length >= 1 && args[0] is string format)
            Lines.Add((type, args.Length > 1 && args[1] is object[] values && values.Length > 0 ? string.Format(format, values) : format));
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public int Count(EnumLogType type) => Lines.Count(l => l.Type == type);
    public bool Says(EnumLogType type, string text) => Lines.Any(l => l.Type == type && l.Text.Contains(text));
}

/// <summary>
/// (EA-0088, D-R5 of the Fable pass) ModConfig/earth.json as EarthConfigStore reads, migrates, checks and writes it, in a
/// folder of its own: a missing file, one that can't be read (FP-16: never written over, a copy kept), an old ConfigVersion,
/// invalid values, a file that can't be written back (CR-L1), and the one-shot flags reset over an admin's later edits.
/// </summary>
static class ConfigStoreChecks
{
    public static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "earth-config-store-" + Environment.ProcessId);
        string file = Path.Combine(folder, EarthConfigStore.FileName);
        void Fresh() { if (Directory.Exists(folder)) Directory.Delete(folder, true); Directory.CreateDirectory(folder); }
        string Written(EarthMapConfig c) => JsonConvert.SerializeObject(c, Formatting.Indented);
        bool NoTemp() => !File.Exists(file + ".tmp");
        try
        {
            // a missing file: the defaults, at the current version, written as the game writes a mod config
            Fresh();
            var (logger, log) = TestLog.Create();
            var cfg = EarthConfigStore.Load(folder, logger, out string unreadable);
            check(unreadable == null && cfg.ConfigVersion == EarthMapConfig.CurrentConfigVersion, "config store: a missing file gives the defaults at the current version");
            check(File.Exists(file) && File.ReadAllText(file) == Written(cfg) && NoTemp(), "config store: a missing file is written with the defaults (indented JSON, no temporary file left)");
            check(log.Count(EnumLogType.Error) == 0 && log.Count(EnumLogType.Warning) == 0, "config store: the defaults log no error");

            // FP-16: a file that can't be read is left as it is, a copy kept beside it, the session on the defaults
            Fresh();
            const string broken = "{\n  \"Enabled\": false,\n  \"DataRepo\": \"mirror/earth-data\"\n  \"ConfigVersion\": 5\n}";
            File.WriteAllText(file, broken);
            (logger, log) = TestLog.Create();
            cfg = EarthConfigStore.Load(folder, logger, out unreadable);
            check(unreadable != null, "config store: an unreadable file says so");
            check(File.ReadAllText(file) == broken && NoTemp(), "config store: an unreadable file is never written over (FP-16)");
            var copies = Directory.GetFiles(folder, EarthConfigStore.FileName + ".bad-*");
            check(copies.Length == 1 && File.ReadAllText(copies[0]) == broken, "config store: a copy of an unreadable file is kept beside it");
            check(cfg.Enabled && cfg.ConfigVersion == EarthMapConfig.CurrentConfigVersion, "config store: an unreadable file's session runs on the defaults");
            check(log.Says(EnumLogType.Error, Path.GetFileName(copies[0])), "config store: the error names the copy");

            // an old file: the settings whose meaning changed move to the current defaults, the rest is kept
            Fresh();
            var defaults = new EarthMapConfig();
            File.WriteAllText(file, JsonConvert.SerializeObject(new { ConfigVersion = 2, Enabled = false, LandCurve = 99.0, SeaCurve = 9.0, MaxSeaDepth = 0.9,
                Streams = "ends", RiverMinDischarge = 1.0, WeatherPace = 2.0 }));
            (logger, log) = TestLog.Create();
            cfg = EarthConfigStore.Load(folder, logger, out unreadable);
            check(unreadable == null && cfg.ConfigVersion == EarthMapConfig.CurrentConfigVersion, "config store: a version 2 file is moved to the current version");
            check(cfg.LandCurve == defaults.LandCurve && cfg.SeaCurve == defaults.SeaCurve && cfg.MaxSeaDepth == defaults.MaxSeaDepth, "config store: version 2's height curves move to the 0.3 defaults");
            check(cfg.RiverMinDischarge == defaults.RiverMinDischarge && cfg.Streams == defaults.Streams, "config store: version 2's rivers move to the 0.7 defaults");
            check(!cfg.Enabled && cfg.WeatherPace == 2, "config store: the settings whose meaning didn't change are kept");
            check(File.ReadAllText(file) == Written(cfg) && log.Count(EnumLogType.Notification) >= 2, "config store: the moved file is written back, each step noted");
            // version 4's "ends" is version 5's "auto"; a version 5 "ends" is the admin's choice
            File.WriteAllText(file, JsonConvert.SerializeObject(new { ConfigVersion = 4, Streams = "ends" }));
            check(EarthConfigStore.Load(folder, TestLog.Create().Logger, out _).Streams == "auto", "config store: version 4's Streams \"ends\" (the old default) becomes \"auto\"");
            File.WriteAllText(file, JsonConvert.SerializeObject(new { ConfigVersion = 5, Streams = "ends" }));
            check(EarthConfigStore.Load(folder, TestLog.Create().Logger, out _).Streams == "ends", "config store: a current file's Streams \"ends\" is kept");

            // invalid values are corrected before anything is frozen into a save, each with an error
            Fresh();
            File.WriteAllText(file, JsonConvert.SerializeObject(new { ConfigVersion = 5, GeologyTransitionBlocks = -5.0, CoastMinCellBlocks = 4.0, MinIslandBlocks = 100.0,
                Streams = " All ", FaunaRangeProfile = "Everywhere", WeatherPace = 100.0, RainFrequency = -1.0, StormFrequency = 2.5 }));
            (logger, log) = TestLog.Create();
            cfg = EarthConfigStore.Load(folder, logger, out unreadable);
            check(cfg.GeologyTransitionBlocks == defaults.GeologyTransitionBlocks && cfg.CoastMinCellBlocks == defaults.CoastMinCellBlocks && cfg.MinIslandBlocks == defaults.MinIslandBlocks,
                "config store: geology and coast values out of range fall back to the defaults");
            check(cfg.Streams == "all", "config store: Streams is trimmed and lower-cased (\"All\" would freeze streams off)");
            check(cfg.FaunaRangeProfile == "natural" && log.Says(EnumLogType.Warning, "FaunaRangeProfile"), "config store: an unknown fauna profile is natural, with a warning");
            check(cfg.WeatherPace == defaults.WeatherPace && cfg.RainFrequency == defaults.RainFrequency && cfg.StormFrequency == 2.5, "config store: weather values out of range fall back, valid ones are kept");
            check(log.Count(EnumLogType.Error) == 5 && File.ReadAllText(file) == Written(cfg), "config store: each invalid value logs an error, the corrected file is written back");
            File.WriteAllText(file, "{ \"ConfigVersion\": 5, \"FaunaRangeProfile\": \"1318\", \"GeologyTransitionBlocks\": NaN }");
            cfg = EarthConfigStore.Load(folder, TestLog.Create().Logger, out _);
            check(cfg.FaunaRangeProfile == "1318" && cfg.GeologyTransitionBlocks == defaults.GeologyTransitionBlocks, "config store: NaN is out of range, 1318 a fauna profile");

            // CR-L1: a file that can't be written back is no reason to stop
            Fresh();
            File.WriteAllText(file, JsonConvert.SerializeObject(new { ConfigVersion = 5, WeatherPace = 2.0 }));
            string before = File.ReadAllText(file);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                (logger, log) = TestLog.Create();
                cfg = EarthConfigStore.Load(folder, logger, out unreadable);
                check(unreadable == null && cfg.WeatherPace == 2 && log.Says(EnumLogType.Warning, "Could not write"), "config store: a read-only file is read and used, with a warning (CR-L1)");
                check(File.ReadAllText(file) == before, "config store: a read-only file is left as it was");
            }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
            check(NoTemp(), "config store: a write that fails leaves no temporary file");

            // the one-shot flags are reset in the file as it is now: what an admin changed while the world ran is kept
            Fresh();
            var session = EarthConfigStore.Load(folder, TestLog.Create().Logger, out _);
            var edited = EarthConfigStore.Read(folder);
            edited.UpgradeExistingWorldCoasts = true; edited.UpgradeExistingWorldIslands = true; edited.WeatherPace = 3;
            EarthConfigStore.Store(edited, folder);
            session.UpgradeExistingWorldCoasts = true;
            (logger, log) = TestLog.Create();
            EarthConfigStore.ResetFlags(folder, session, c => c.UpgradeExistingWorldCoasts = false, "UpgradeExistingWorldCoasts", logger);
            var after = EarthConfigStore.Read(folder);
            check(!after.UpgradeExistingWorldCoasts && after.UpgradeExistingWorldIslands && after.WeatherPace == 3 && NoTemp(), "config store: a reset flag is written, the admin's later edits kept");
            check(log.Says(EnumLogType.Notification, "UpgradeExistingWorldCoasts"), "config store: the reset is noted");
            // the file gone: the session's config, the flag reset, written
            File.Delete(file);
            EarthConfigStore.ResetFlags(folder, session, c => c.UpgradeExistingWorldCoasts = false, "UpgradeExistingWorldCoasts", TestLog.Create().Logger);
            after = EarthConfigStore.Read(folder);
            check(after != null && !after.UpgradeExistingWorldCoasts && after.WeatherPace == session.WeatherPace, "config store: with the file gone the session's config is written, the flag reset");
            // a file that can't be read: an error, the file as it was
            File.WriteAllText(file, broken);
            (logger, log) = TestLog.Create();
            EarthConfigStore.ResetFlags(folder, session, c => c.UpgradeExistingWorldCoasts = false, "UpgradeExistingWorldCoasts", logger);
            check(File.ReadAllText(file) == broken && log.Says(EnumLogType.Error, "by hand"), "config store: a reset never writes over a file that can't be read");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
