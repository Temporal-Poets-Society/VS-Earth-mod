using System;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// ModConfig/earth.json (EarthMapConfig): read at every server start, moved to the current defaults where a setting's
    /// meaning changed, checked before anything is frozen into a save (an invalid value must never stop a world from
    /// loading), and written back; also written when one-shot UpgradeExistingWorld* flags are reset (once the world that took
    /// them is saved). A file that can't be read is never written over (FP-16, the Fable pass of the 14 November 2026 code
    /// review): before, its defaults replaced it, and one missing comma lost every setting (Enabled, a mirror's DataRepo or
    /// ElevationRepo, every one-shot flag). Writes go through a temporary file, so a crash or a full disk mid-write leaves the
    /// file as it was.
    /// </summary>
    static class EarthConfigStore
    {
        public const string FileName = "earth.json";

        /// <summary>The config, migrated and checked, written back to the file. <paramref name="unreadable"/>: the parser's
        /// error when the file is there but can't be read (the defaults are returned, the file left as it was).</summary>
        public static EarthMapConfig Load(ICoreServerAPI api, ILogger log) => Load(api, log, out _);

        public static EarthMapConfig Load(ICoreServerAPI api, ILogger log, out string unreadable) => Load(GamePaths.ModConfig, log, out unreadable);

        /// <summary>(EA-0088, D-R5 of the Fable pass) <see cref="Load(ICoreServerAPI, ILogger, out string)"/> on the earth.json
        /// in <paramref name="folder"/>, read as the game reads a mod config (LoadModConfig: Newtonsoft over the file's text):
        /// what tests/EarthWorldOptionsTests checks, without the game.</summary>
        internal static EarthMapConfig Load(string folder, ILogger log, out string unreadable)
        {
            unreadable = null;
            EarthMapConfig cfg = null;
            try { cfg = Read(folder); }
            catch (Exception e)
            {
                // (FP-16) the admin's file stays as it is, with a copy beside it; this session runs on the defaults
                unreadable = e.Message;
                string copy = KeepCopy(folder);
                log.Error("{0} can't be read: {1}. Earth does not write the file{2}; an Earth world is not started on the defaults " +
                    "(it could freeze them, or fetch data from GitHub instead of a mirror the file names): fix the place the error names, or " +
                    "delete the file to start from the defaults.", FileName, e.Message, copy != null ? " (a copy of it is kept as " + copy + ")" : "");
            }
            if (cfg == null)
            {
                cfg = new EarthMapConfig { ConfigVersion = EarthMapConfig.CurrentConfigVersion };
            }
            else if (cfg.ConfigVersion < EarthMapConfig.CurrentConfigVersion)
            {
                // 0.3: stronger height curves (deeper shallow seas, compressed mountains), tuned for world height 384
                var d = new EarthMapConfig();
                if (cfg.ConfigVersion < 3)
                {
                    cfg.LandCurve = d.LandCurve;
                    cfg.SeaCurve = d.SeaCurve;
                    cfg.MaxSeaDepth = d.MaxSeaDepth;
                    log.Notification("{0} updated to the 0.3 defaults for LandCurve, SeaCurve and MaxSeaDepth.", FileName);
                }
                if (cfg.ConfigVersion < 4)
                {
                    // 0.7: wider rivers, navigable from 100 m³/s, ending upstream in streams
                    cfg.RiverMinDischarge = d.RiverMinDischarge;
                    cfg.RiverWidthScale = d.RiverWidthScale;
                    cfg.Streams = d.Streams;
                    cfg.StreamMinDischarge = d.StreamMinDischarge;
                    cfg.RiverStraighten = d.RiverStraighten;
                    cfg.RiverSmoothWidths = d.RiverSmoothWidths;
                    log.Notification("{0} updated to the 0.7 river defaults (RiverMinDischarge, RiverWidthScale, Streams).", FileName);
                }
                if (cfg.ConfigVersion < 5 && string.Equals(cfg.Streams, "ends", StringComparison.OrdinalIgnoreCase))
                {
                    // 0061's quick step: the old default "ends" becomes "auto" (every stream at 1:25 and finer, "ends" coarser)
                    cfg.Streams = d.Streams;
                    log.Notification("{0}: Streams \"ends\" (the earlier default) is now \"auto\": new worlds at 1:25 and finer draw every stream the river data has.", FileName);
                }
                cfg.ConfigVersion = EarthMapConfig.CurrentConfigVersion;
            }
            // Validate before anything is frozen into a save: an invalid value must never stop a world from loading.
            var defaults = new EarthMapConfig();
            if (!double.IsFinite(cfg.GeologyTransitionBlocks) || cfg.GeologyTransitionBlocks < 0 || cfg.GeologyTransitionBlocks > 512)
            {
                log.Error("{0}: GeologyTransitionBlocks must be 0..512 (was {1}); using {2}.", FileName, cfg.GeologyTransitionBlocks, defaults.GeologyTransitionBlocks);
                cfg.GeologyTransitionBlocks = defaults.GeologyTransitionBlocks;
            }
            if (!double.IsFinite(cfg.CoastMinCellBlocks) || cfg.CoastMinCellBlocks < 8 || cfg.CoastMinCellBlocks > 1000)
            {
                log.Error("{0}: CoastMinCellBlocks must be 8..1000 (was {1}); using {2}.", FileName, cfg.CoastMinCellBlocks, defaults.CoastMinCellBlocks);
                cfg.CoastMinCellBlocks = defaults.CoastMinCellBlocks;
            }
            if (!double.IsFinite(cfg.MinIslandBlocks) || cfg.MinIslandBlocks < 1 || cfg.MinIslandBlocks > 64)
            {
                log.Error("{0}: MinIslandBlocks must be 1..64 (was {1}); using {2}.", FileName, cfg.MinIslandBlocks, defaults.MinIslandBlocks);
                cfg.MinIslandBlocks = defaults.MinIslandBlocks;
            }
            cfg.Ores ??= new OreTuning();
            if (!cfg.Ores.TryValidate(cfg.Ores.Resources?.Keys ?? Enumerable.Empty<string>(), out string oreError))
            {
                log.Error("{0}: invalid Ores settings ({1}); using the default ore tuning.", FileName, oreError);
                cfg.Ores = new OreTuning();
            }
            string profile = (cfg.FaunaRangeProfile ?? "").Trim().ToLowerInvariant();
            if (profile != "natural" && profile != "current" && profile != "1318")
            {
                log.Warning("{0}: FaunaRangeProfile must be \"natural\", \"current\" or \"1318\" (was \"{1}\"); using \"natural\".", FileName, cfg.FaunaRangeProfile);
                profile = "natural";
            }
            cfg.FaunaRangeProfile = profile;
            // a new world freezes the resolved value and the river network compares it as it is ("all", "ends"): "All" would
            // freeze streams off for good
            string streams = (cfg.Streams ?? "").Trim().ToLowerInvariant();
            if (streams != "auto" && streams != "all" && streams != "ends" && streams != "none")
            {
                log.Error("{0}: Streams must be \"auto\", \"all\", \"ends\" or \"none\" (was \"{1}\"); using \"{2}\".", FileName, cfg.Streams, defaults.Streams);
                streams = defaults.Streams;
            }
            cfg.Streams = streams;
            double Range(string name, double value, double min, double max, double fallback)
            {
                if (double.IsFinite(value) && value >= min && value <= max) return value;
                log.Error("{0}: {1} must be {2}..{3} (was {4}); using {5}.", FileName, name, min, max, value, fallback);
                return fallback;
            }
            cfg.WeatherPace = Range(nameof(cfg.WeatherPace), cfg.WeatherPace, 0.25, 8, defaults.WeatherPace);
            cfg.RainFrequency = Range(nameof(cfg.RainFrequency), cfg.RainFrequency, 0, 3, defaults.RainFrequency);
            cfg.StormFrequency = Range(nameof(cfg.StormFrequency), cfg.StormFrequency, 0, 5, defaults.StormFrequency);
            // a file that can't be written back (read-only, locked by an editor or a backup tool, a full disk) is no reason to stop:
            // thrown here it would end StartServerSide before the world's set-up is registered, and the game's mod loader would
            // go on with vanilla's terrain in an Earth world (code review 2026-11-11, CR-L1)
            if (unreadable != null) return cfg;
            try { Store(cfg, folder); }
            catch (Exception e) { log.Warning("Could not write {0} back ({1}); the settings read are used, the file is left as it was.", FileName, e.Message); }
            return cfg;
        }

        static string ConfigPath(string folder) => Path.Combine(folder, FileName);

        /// <summary>The earth.json in <paramref name="folder"/> as the game's LoadModConfig reads it (null when there is none;
        /// throws when it can't be parsed).</summary>
        internal static EarthMapConfig Read(string folder)
        {
            string path = ConfigPath(folder);
            return File.Exists(path) ? JsonConvert.DeserializeObject<EarthMapConfig>(File.ReadAllText(path)) : null;
        }

        /// <summary>(FP-16) The config written as the game writes a mod config (StoreModConfig: indented JSON), through a
        /// temporary file moved over the old one.</summary>
        internal static void Store(EarthMapConfig cfg, string folder)
        {
            string path = ConfigPath(folder), temp = path + ".tmp";
            Directory.CreateDirectory(folder);
            File.WriteAllText(temp, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            try { File.Move(temp, path, true); }
            catch (Exception) { try { File.Delete(temp); } catch (Exception) { } throw; }
        }

        /// <summary>(FP-16) A copy of an unreadable earth.json beside it (earth.json.bad-yyyyMMdd-HHmmss); null when it can't be
        /// made.</summary>
        static string KeepCopy(string folder)
        {
            try
            {
                string copy = ConfigPath(folder) + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                File.Copy(ConfigPath(folder), copy, true);
                return Path.GetFileName(copy);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Resets one-shot flags (WorldUpgrades) in the file as it is now: read again, so that what an admin changed in it while
        /// the world ran is kept, the flags set to false and written back (<paramref name="session"/>, the config read at start,
        /// where the file is gone). An error is logged, never thrown: the world goes on, and the flags have to be set back by
        /// hand (a file that can't be read is left as it is).
        /// </summary>
        public static void ResetFlags(ICoreServerAPI api, EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log)
            => ResetFlags(GamePaths.ModConfig, session, reset, names, log);

        internal static void ResetFlags(string folder, EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log)
        {
            try
            {
                var file = Read(folder) ?? session;
                reset(file);
                Store(file, folder);
                log.Notification("{0}: {1} reset, now that the world that took or skipped them is saved.", FileName, names);
            }
            catch (Exception e) { log.Error("Could not reset {0} in {1}: {2}. Set them back to false by hand.", names, FileName, e.Message); }
        }
    }
}
