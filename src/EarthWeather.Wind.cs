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
    /// EarthWeather, the wind: Earth's handler and the prefix that skips vanilla's (they share this thread's record of what was
    /// written), the regions' wind, the client's local wind and the clouds' drift (EA-0089: split from EarthWeather.cs, no change).
    /// </summary>
    public partial class EarthWeather
    {
        /// <summary>
        /// Vanilla's wind lookups (trees and plants swaying, wind chill, the vanilla water's waves, rain, snow and particles):
        /// Earth's wind here as a vector (open sea contracts section 5): X east, Z south, the length its strength scaled for
        /// height in vanilla's unit. Vanilla itself puts the strength in X alone; its readers that take X as the strength get
        /// the length through <see cref="VanillaWindVector"/>. A client where they don't gets vanilla's own form (CR-W5).
        /// </summary>
        void OnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
        {
            if (!WindHandlerPatched) { WriteWind(pos, ref windSpeed); return; }
            // (ER-04) where vanilla's handler was skipped, the prefix has already written Earth's wind for this lookup. Earth
            // keeps the last word, as when it answered after vanilla: a handler subscribed in between (another mod's) that
            // changed it gets Earth's wind written back, without working it out again
            if (!written || pos == null || writtenX != pos.X || writtenY != pos.Y || writtenZ != pos.Z) return;
            written = false;
            if (windSpeed.X != writtenWindX || windSpeed.Z != writtenWindZ) { windSpeed.X = writtenWindX; windSpeed.Z = writtenWindZ; }
        }

        // (per thread) what the prefix wrote for the lookup in progress: its place and Earth's wind
        [ThreadStatic] static bool written;
        [ThreadStatic] static double writtenX, writtenY, writtenZ, writtenWindX, writtenWindZ;

        /// <summary>Whether vanilla's wind handler is replaced by <see cref="VanillaWindHandlerPrefix"/> on this process.</summary>
        static volatile bool WindHandlerPatched;

        /// <summary>
        /// (ER-04, EA-0078) Vanilla's wind handler (WeatherSystemBase.Event_OnGetWindSpeed) runs before Earth's on every
        /// GetWindSpeedAt: four region winds and a climate reading (with Earth's rain and temperature hooks), all thrown away
        /// when Earth's handler then writes its own wind. Where Earth answers, the prefix writes Earth's wind and skips
        /// vanilla's; where it doesn't (no model, an older Sea's wind, Earth's lookup failed), vanilla's runs as before.
        /// </summary>
        static bool VanillaWindHandlerPrefix(WeatherSystemBase __instance, Vec3d pos, ref Vec3d windSpeed)
        {
            var sys = __instance?.api == null ? null : For(__instance.api.Side);
            written = false;
            if (sys == null || !sys.WriteWind(pos, ref windSpeed)) return true;
            written = true;
            writtenX = pos.X; writtenY = pos.Y; writtenZ = pos.Z;
            writtenWindX = windSpeed.X; writtenWindZ = windSpeed.Z;
            return false;
        }

        /// <summary>Earth's wind into <paramref name="windSpeed"/>; false where Earth doesn't answer here.</summary>
        bool WriteWind(Vec3d pos, ref Vec3d windSpeed)
        {
            var m = Model;
            if (m == null || !ControlsWind || pos == null || api.World?.Calendar == null) return false;
            long start = ClimateCost.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try
            {
                var w = m.WindAt(pos.X, pos.Z, HoursNow(api.World, m), true);
                double strength = AltitudeScale(w.Strength, pos.Y, api.World.SeaLevel);
                // (CR-W5) a client whose X readers were not handed the length (Earth's patch refused after a game update) gets
                // the strength in X, as vanilla writes it: with an easterly as a vector vanilla's water wave intensity
                // (0.75 + 0.9 X) goes negative and its wind wave counters run backwards. Rain then slants east, as in vanilla.
                if (capi != null && !VanillaWindVector.ReadersTakeVector)
                {
                    windSpeed.X = strength;
                    windSpeed.Z = 0;
                }
                else
                {
                    var v = WeatherContract.VanillaVector(w, strength);
                    windSpeed.X = v.X;
                    windSpeed.Z = v.Z;
                }
                if (start != 0) ClimateCost.CountWind(System.Diagnostics.Stopwatch.GetTimestamp() - start);
                return true;
            }
            catch (Exception e) { HookFailed(api.Side, "the wind lookup", e); return false; }
        }

        /// <summary>Each 512-block weather region keeps its own wind, which drives the wind sound and /weather: Earth's at its centre.</summary>
        static void RegionWindPostfix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws, double posY, ref double __result)
        {
            var api = ___ws?.api;
            var sys = api == null ? null : For(api.Side);
            var m = sys?.Model;
            if (m == null || !sys.ControlsWind) return;
            try
            {
                int rs = api.World.BlockAccessor.RegionSize;
                // (ER-12) once per region and game hour: every tick and every windmill asks the same
                double s = WeatherDrawCache.RegionWindAt(m, __instance.regionX, __instance.regionZ, (__instance.regionX + 0.5) * rs, (__instance.regionZ + 0.5) * rs,
                    HoursNow(api.World, m));
                __result = AltitudeScale(s, posY, api.World.SeaLevel);
            }
            catch (Exception e) { HookFailed(api.Side, "the weather region's wind", e); }
        }

        /// <summary>The wind at the local player for the clouds' drift: (toward x, toward z) unit direction, strength at sea
        /// level, the strength vanilla's cloud renderer adds to X (its blended reading at the player's height). Null = leave
        /// vanilla's.</summary>
        public static volatile float[] LocalWind;

        static long clientMs;
        WeatherSystemClient weatherClient;

        void UpdateLocalWind()
        {
            clientMs = capi.ElapsedMilliseconds;
            var ep = capi.World?.Player?.Entity;
            var m = Model;
            if (ep == null || m == null || !ControlsWind) { LocalWind = null; return; }
            try
            {
                var w = m.WindAt(ep.Pos.X, ep.Pos.Z, HoursNow(capi.World, m), true);
                double s = w.Strength;
                float dx = s < 1e-6 ? 1 : (float)(w.X / s), dz = s < 1e-6 ? 0 : (float)(w.Z / s);
                // (X-L9, the Fable pass) the X vanilla's CloudTick adds, 3 x its own reading: WeatherDataAtPlayer, the four
                // regions' winds blended at the player, not Earth's wind at the player itself
                var reader = (weatherClient ??= capi.ModLoader.GetModSystem<WeatherSystemClient>())?.WeatherDataAtPlayer;
                double added = reader != null ? reader.GetWindSpeed(ep.Pos.Y) : AltitudeScale(s, ep.Pos.Y, capi.World.SeaLevel);
                LocalWind = new[] { dx, dz, (float)s, (float)added };
            }
            catch (Exception e) { LocalWind = null; HookFailed(EnumAppSide.Client, "the clouds' drift", e); }
        }

        /// <summary>Each cloud renderer type's timer and target drift, as typed references (the prefix runs every frame:
        /// FieldInfo.SetValue boxed three values a frame).</summary>
        static readonly Dictionary<Type, (AccessTools.FieldRef<object, long> timer, AccessTools.FieldRef<object, float> tx, AccessTools.FieldRef<object, float> tz)> cloudFields = new();

        /// <summary>Vanilla's cloud renderers pick a random eastward drift every 20-120 s and add 3 × the wind strength to X. Keep
        /// their timer from firing and give them Earth's drift instead (they ease toward it themselves).</summary>
        static void CloudTickPrefix(object __instance)
        {
            var d = LocalWind;
            if (d == null || !cloudFields.TryGetValue(__instance.GetType(), out var f)) return;
            float speed = 1f + 6f * d[2];
            f.timer(__instance) = clientMs + 60000;
            f.tx(__instance) = d[0] * speed - 3f * d[3];
            f.tz(__instance) = d[1] * speed;
        }
    }
}
