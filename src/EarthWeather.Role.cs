using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Func4 = System.Func<double, double, double, bool, System.ValueTuple<double, double>>;
using Func3 = System.Func<double, double, double, System.ValueTuple<double, double>>;

namespace EarthMap
{
    /// <summary>
    /// EarthWeather, the weather role (seacontracts:weather: claimed, re-claimed, withdrawn) and the link to Sea's wind
    /// (EA-0089: split from EarthWeather.cs, no change).
    /// </summary>
    public partial class EarthWeather
    {
        Dictionary<string, object> roleEntry;

        bool roleRefusedLogged;

        /// <summary>Earth means to hold the role on this side (claimed, not withdrawn since): what <see cref="RecheckRole"/> keeps.</summary>
        bool roleWanted;

        /// <summary>Earth's entry went in on this side at least once (so a later put-back says so in the log).</summary>
        bool roleHeldOnce;

        /// <summary>Whether this side's api.ObjectCache holds Earth's weather role now.</summary>
        public bool HoldsRole
        {
            get
            {
                if (api == null || roleEntry == null) return false;
                lock (api.ObjectCache) return api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && ReferenceEquals(o, roleEntry);
            }
        }

        /// <summary>For the log and /ew: "held", "held by &lt;owner&gt;" or "not held".</summary>
        public string RoleStatus
        {
            get
            {
                if (HoldsRole) return "held";
                if (api == null) return "not held";
                lock (api.ObjectCache)
                    return api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && o is IDictionary<string, object> d
                        ? "held by " + (d.TryGetValue("owner", out var w) ? w as string ?? "?" : "?") : "not held";
            }
        }

        /// <summary>
        /// Puts Earth's weather role in this side's api.ObjectCache (main thread), replacing an entry only of a lower priority
        /// (open sea contracts R2). A second world owner (priority 100 or more) keeps it, and Earth says so once; Earth's own
        /// weather still runs (two world owners are a setup error).
        /// </summary>
        void ClaimRole()
        {
            if (api == null) return;
            roleWanted = true;
            roleEntry ??= WeatherContract.Build(() => model, () => Interlocked.Read(ref revision));
            string other = null;
            bool lost = false;
            lock (api.ObjectCache)
            {
                api.ObjectCache.TryGetValue(WeatherContract.Key, out var o);
                if (ReferenceEquals(o, roleEntry)) return;
                if (WeatherContract.MayReplace(o, out other))
                {
                    api.ObjectCache[WeatherContract.Key] = roleEntry;
                    lost = roleHeldOnce;
                    roleHeldOnce = true;
                }
            }
            if (other != null && !roleRefusedLogged)
            {
                roleRefusedLogged = true;
                Mod.Logger.Warning("Earth weather: {0} already holds {1} as a world's owner; Earth doesn't publish its weather there.", other, WeatherContract.Key);
            }
            else if (other == null && (lost || roleRefusedLogged))
            {
                roleRefusedLogged = false;
                Mod.Logger.Notification("Earth weather: {0} was gone or another mod's; Earth publishes its weather there again.", WeatherContract.Key);
            }
        }

        /// <summary>
        /// (R2) A producer re-checks its role whenever another producer's entry may have gone: while Earth wants the role
        /// (claimed and not withdrawn), the one-second watch puts its entry back where it went missing or a lower priority
        /// took its place, and takes it where a world owner that kept Earth out has gone.
        /// </summary>
        void RecheckRole()
        {
            if (roleWanted && !HoldsRole) ClaimRole();
        }

        /// <summary>Takes Earth's weather role out of this side's api.ObjectCache, if it is still Earth's.</summary>
        void WithdrawRole()
        {
            roleWanted = false;
            if (api == null || roleEntry == null) return;
            lock (api.ObjectCache)
                if (api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && ReferenceEquals(o, roleEntry))
                    api.ObjectCache.Remove(WeatherContract.Key);
        }

        // ------------------------------------------------------------------------------------------ Sea

        object seaSystem;

        /// <summary>Sea, when installed: give it Earth's wind (SetWindProvider), or leave the wind to an older Sea that makes its own.</summary>
        void LinkSea()
        {
            ControlsWind = true;
            SeaLink = "none";
            var sea = api.ModLoader.GetModSystem("Sea.SeaSystem");
            if (sea == null) return;
            var set = RequiredMods.SeaWindSetter(sea);
            if (set == null)
            {
                ControlsWind = false;
                SeaLink = "own wind";
                Mod.Logger.Warning("Earth weather: this Sea makes its own wind (no SetWindProvider; Sea 0.4 takes Earth's). Sea keeps the wind, the clouds' drift and the rain's slant; Earth does the rain and clouds.");
                return;
            }
            var m = Model;
            Func4 windAt = (x, z, h, gusts) =>
            {
                var w = m.WindAt(x, z, h, gusts);
                return (w.X, w.Z);
            };
            Func3 prevailing = (x, z, h) =>
            {
                var w = m.Prevailing(x, z, h);
                return (w.X, w.Z);
            };
            try
            {
                bool ok = set.Invoke(sea, new object[] { SeaProviderName, windAt, prevailing }) is bool b && b;
                if (ok)
                {
                    seaSystem = sea;
                    SeaLink = "provider";
                }
                else Mod.Logger.Warning("Earth weather: Sea did not take Earth's wind.");
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Earth weather: could not give Sea the wind: {0}", e.InnerException?.Message ?? e.Message);
            }
        }

        void UnlinkSea()
        {
            if (seaSystem == null) return;
            try { RequiredMods.SeaWindClearer(seaSystem)?.Invoke(seaSystem, new object[] { SeaProviderName }); }
            catch (Exception e) { Mod.Logger.Warning("Earth weather: could not take the wind back from Sea: {0}", e.InnerException?.Message ?? e.Message); }
            seaSystem = null;
            SeaLink = "none";
        }
    }
}
