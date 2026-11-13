using System;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// ModConfig/earth.json (EarthMapConfig): read at every server start, moved to the current defaults where a setting's
    /// meaning changed, checked before anything is frozen into a save (an invalid value must never stop a world from
    /// loading), and written back; also written when one-shot UpgradeExistingWorld* flags are reset (once the world that took
    /// them is saved).
    /// </summary>
    static class EarthConfigStore
    {
        public const string FileName = "earth.json";

        /// <summary>The config, migrated and checked, written back to the file.</summary>
        public static EarthMapConfig Load(ICoreServerAPI api, ILogger log)
        {
            EarthMapConfig cfg = null;
            try { cfg = api.LoadModConfig<EarthMapConfig>(FileName); }
            catch (Exception e) { log.Error("Failed to read {0}, using defaults: {1}", FileName, e.Message); }
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
            try { api.StoreModConfig(cfg, FileName); }
            catch (Exception e) { log.Warning("Could not write {0} back ({1}); the settings read are used, the file is left as it was.", FileName, e.Message); }
            return cfg;
        }

        /// <summary>
        /// Resets one-shot flags (WorldUpgrades) in the file as it is now: read again, so that what an admin changed in it while
        /// the world ran is kept, the flags set to false and written back (<paramref name="session"/>, the config read at start,
        /// where the file is gone). An error is logged, never thrown: the world goes on, and the flags have to be set back by
        /// hand (a file that can't be read is left as it is).
        /// </summary>
        public static void ResetFlags(ICoreServerAPI api, EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log)
        {
            try
            {
                var file = api.LoadModConfig<EarthMapConfig>(FileName) ?? session;
                reset(file);
                api.StoreModConfig(file, FileName);
                log.Notification("{0}: the world is saved with {1}; the flag(s) are reset.", FileName, names);
            }
            catch (Exception e) { log.Error("Could not reset {0} in {1}: {2}. Set them back to false by hand.", names, FileName, e.Message); }
        }
    }
}
