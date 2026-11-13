using System;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Tells AstraTerra (optional) where Earth's meridians are, so its longitude-aware sun, local moon, stars and clock
    /// follow the real Earth instead of a prime meridian at the map's centre. By reflection only: Earth Map is GPL-3.0 and
    /// doesn't reference AstraTerra's AGPL-3.0 assembly. Needs AstraTerra's AstraTerraModSystem.SetLongitudeProvider
    /// (not in the 0.15.0 release); without it, or without AstraTerra, Earth logs once and does nothing.
    /// One instance per side; call Bind on both sides once that side's world has Earth's layout.
    /// </summary>
    sealed class AstraTerraLongitude
    {
        const string SystemType = "AstraTerra.AstraTerraModSystem";

        object bound;
        bool reported;
        ILogger log;
        bool unreadableReported;

        /// <summary>Whether AstraTerra took Earth's meridians on this side.</summary>
        public bool Bound => bound != null;

        /// <summary>Whether AstraTerra's sun actually follows the meridians: bound, and its LongitudeAwareSun setting on (read by
        /// reflection from its config; false when it can't be read, which is said once, apart from a setting that is off).</summary>
        public bool SunFollowsLongitude
        {
            get
            {
                if (bound == null) return false;
                try
                {
                    // (CR-W11) a private member renamed by an AstraTerra update must not read as "off" without a word: the
                    // weather would quietly put its afternoon showers on the calendar's clock
                    var field = bound.GetType().GetField("config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (field == null) return Unreadable("it has no field 'config'");
                    var cfg = field.GetValue(bound);
                    if (cfg == null) return Unreadable("its config is not loaded");
                    var setting = cfg.GetType().GetProperty("LongitudeAwareSun");
                    if (setting == null) return Unreadable("its config has no LongitudeAwareSun");
                    if (setting.GetValue(cfg) is not bool on) return Unreadable("its LongitudeAwareSun is not a true/false setting");
                    return on;
                }
                catch (Exception e) { return Unreadable(e.Message); }
            }
        }

        /// <summary>Says once that AstraTerra's LongitudeAwareSun can't be read; false (the sun taken as not following).</summary>
        bool Unreadable(string why)
        {
            if (!unreadableReported)
            {
                unreadableReported = true;
                log?.Warning("Earth Map longitude: AstraTerra's LongitudeAwareSun setting can't be read ({0}); Earth takes its sun as not following the meridians, so the weather's afternoon showers keep the calendar's clock.", why);
            }
            return false;
        }

        /// <summary>Hands the provider to AstraTerra on this side, if it is loaded and can take one.</summary>
        public void Bind(ICoreAPI api, System.Func<double, double?> provider, ILogger log)
        {
            this.log = log;
            var system = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == SystemType);
            if (system == null)
            {
                Report(log, "AstraTerra is not loaded; the sky keeps vanilla's single clock (no longitude).");
                return;
            }
            switch (LongitudeProviderBinding.TrySet(system, provider, out string error))
            {
                case LongitudeProviderBinding.Result.Bound:
                    bound = system;
                    Report(log, "AstraTerra's sky follows Earth's meridians (its longitude sun is on with LongitudeAwareSun: true).");
                    break;
                case LongitudeProviderBinding.Result.NoMethod:
                    Report(log, "this AstraTerra (" + VersionOf(api) + ") has no SetLongitudeProvider; its sky keeps its own longitude, centred on the map.");
                    break;
                default:
                    Report(log, "AstraTerra's SetLongitudeProvider failed (" + error + "); its sky keeps its own longitude.");
                    break;
            }
        }

        /// <summary>Takes the provider back (world unloading). Never throws.</summary>
        public void Clear()
        {
            if (bound == null) return;
            LongitudeProviderBinding.TrySet(bound, null, out _);
            bound = null;
        }

        void Report(ILogger log, string message)
        {
            if (reported) return;
            reported = true;
            log.Notification("Earth Map longitude: " + message);
        }

        static string VersionOf(ICoreAPI api)
        {
            try { return api.ModLoader.GetMod("astraterra")?.Info?.Version ?? "unknown version"; }
            catch (Exception) { return "unknown version"; }
        }
    }
}
