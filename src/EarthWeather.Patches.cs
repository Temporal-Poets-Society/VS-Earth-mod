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
    /// EarthWeather, its patches on vanilla's weather (Apply, Release) and the postfixes and prefixes they bind: the rain and
    /// cloudiness, the stopped rain, hail, fog, cloud tiles and the pattern pick (EA-0089: split from EarthWeather.cs, no change).
    /// </summary>
    public partial class EarthWeather
    {
        // ------------------------------------------------------------------------------------------ patches

        bool applied;

        void Apply()
        {
            if (applied) return;
            applied = true;
            lock (sync)
            {
                users++;
                if (harmony != null) return;
                harmony = new Harmony(HarmonyId);
                var log = Mod.Logger;
                void Try(string what, Action patch)
                {
                    try { patch(); Active.Add(what); }
                    catch (Exception e) { log.Warning("Earth weather: could not patch ({0}), that part stays vanilla: {1}", what, e.Message); }
                }
                Try("rain and rain clouds", () =>
                {
                    harmony.Patch(AccessTools.Method(typeof(WeatherSystemBase), nameof(WeatherSystemBase.GetRainCloudness))
                        ?? throw new Exception("GetRainCloudness not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(RainCloudnessSkipPrefix)),
                        postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RainCloudnessPostfix)));
                    RainPatched = true;
                });
                Try("/weather stoprain's answer", () => harmony.Patch(AccessTools.Method(typeof(WeatherSystemCommands), "RainStopFunc", new[] { typeof(IPlayer), typeof(bool) })
                    ?? throw new Exception("WeatherSystemCommands.RainStopFunc not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(RainStopPrefix))));
                Try("cloud patterns by cloud cover", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.RandomWeatherPattern))
                    ?? throw new Exception("RandomWeatherPattern not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(RandomWeatherPatternPrefix))));
                // (rules 6, EA-0019) hail from Earth's thunderstorms in place of vanilla's random hail events: the hail that
                // hurts (EntityBehaviorHealth reads GetPrecipitationState's type), and on the client the hail drawn and heard
                if (Model?.Settings.Has.Hail == true)
                {
                    Try("hail from Earth's thunderstorms", () =>
                    {
                        harmony.Patch(AccessTools.Method(typeof(WeatherSystemBase), nameof(WeatherSystemBase.GetPrecipitationState), new[] { typeof(Vec3d), typeof(double) })
                            ?? throw new Exception("GetPrecipitationState not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(PrecipitationStatePostfix)));
                        // (EA-0101) after the whole tick: vanilla's TickEvery25ms ends by setting the blended type from its own
                        // weather event, after clientUpdate, so a postfix on clientUpdate never reached the drawing
                        harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.TickEvery25ms), new[] { typeof(float) })
                            ?? throw new Exception("WeatherSimulationRegion.TickEvery25ms not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RegionClientUpdatePostfix)));
                    });
                }
                // (rules 7, EA-0026, EA-0027) the client draws Earth's fog and mist in place of the patterns' and its cloud tiles
                // as thick as Earth's cloud cover there, so cloud edges follow its rain areas
                if (Model?.Settings.Has.Fog == true)
                {
                    Try("fog and mist from Earth's weather", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.UpdateWeatherData))
                        ?? throw new Exception("WeatherSimulationRegion.UpdateWeatherData not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RegionFogPostfix))));
                    Try("cloud tiles by Earth's cloud cover", () => harmony.Patch(AccessTools.Method(typeof(WeatherDataReaderBase), "pgetBlendedCloudThicknessAt", new[] { typeof(int), typeof(int) })
                        ?? throw new Exception("WeatherDataReaderBase.pgetBlendedCloudThicknessAt not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(CloudThicknessPostfix))));
                }
                // (ER-04) vanilla's wind handler skipped where Earth's answers (its result was thrown away)
                Try("Earth's wind in place of vanilla's wind handler", () =>
                {
                    harmony.Patch(AccessTools.Method(typeof(WeatherSystemBase), "Event_OnGetWindSpeed", new[] { typeof(Vec3d), typeof(Vec3d).MakeByRefType() })
                        ?? throw new Exception("WeatherSystemBase.Event_OnGetWindSpeed not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(VanillaWindHandlerPrefix)));
                    WindHandlerPatched = true;
                });
                Try("wind sound and weather regions", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.GetWindSpeed))
                    ?? throw new Exception("GetWindSpeed not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RegionWindPostfix))));
                // (rain, snow and hail blow with the wind without a patch since the wind became a vector, 7 November 2026:
                // vanilla's weather particles take X and Z of GetWindSpeedAt as a velocity; Earth's old transpiler, which turned
                // an X-only strength to Earth's direction, would now turn it twice)
                foreach (var name in new[] { "FluffyClouds.CloudRendererMap", "Vintagestory.GameContent.CloudRenderer" })
                {
                    var t = AccessTools.TypeByName(name);
                    if (t == null) continue;
                    string what = "clouds drifting with the wind (" + name + ")";
                    // (CR-W12) looked up inside a try: an ambiguous match after a game update throws, and this runs in the
                    // one-second watch, a tick listener with no catch of its own
                    MethodInfo tick;
                    try { tick = AccessTools.Method(t, "CloudTick"); }
                    catch (Exception e) { log.Warning("Earth weather: could not patch ({0}), that part stays vanilla: {1}", what, e.Message); continue; }
                    if (PatchedBy(tick, "sea")) continue;
                    Try(what, () =>
                    {
                        Need(t, "windChangeTimer"); Need(t, "targetCloudSpeedX"); Need(t, "targetCloudSpeedZ");
                        cloudFields[t] = (AccessTools.FieldRefAccess<long>(t, "windChangeTimer"), AccessTools.FieldRefAccess<float>(t, "targetCloudSpeedX"),
                            AccessTools.FieldRefAccess<float>(t, "targetCloudSpeedZ"));
                        harmony.Patch(tick ?? throw new Exception("CloudTick not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(CloudTickPrefix)));
                    });
                }
            }
        }

        static FieldInfo Need(Type t, string name) => AccessTools.Field(t, name) ?? throw new Exception("field " + name + " not found");

        static bool PatchedBy(MethodBase m, string owner)
        {
            if (m == null) return false;
            var info = Harmony.GetPatchInfo(m);
            return info != null && info.Owners.Contains(owner);
        }

        void Release()
        {
            if (!applied) return;
            applied = false;
            lock (sync)
            {
                if (--users > 0 || harmony == null) return;
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
                Active.Clear();
                RainPatched = false;
                WindHandlerPatched = false;
                cloudFields.Clear();
            }
        }

        /// <summary>(X-R3, the Fable pass) Vanilla's rain noise is not worked out where the postfix below answers (no /weather
        /// override, a model on this side): only skips the original, the answer is still the postfix's. Where another mod's
        /// prefix (Cylinder World's) runs instead or Harmony leaves this one out, vanilla's noise is worked out and thrown away
        /// as before.</summary>
        static bool RainCloudnessSkipPrefix(WeatherSystemBase __instance) => rainHookFailed || __instance.OverridePrecipitation.HasValue || ModelFor(__instance.api) == null;

        /// <summary>(Fable's re-review of caea9b8) Earth's rain cloudness threw once: vanilla's method runs again from the next
        /// call on (the failing call answered 0, its original skipped), as HookFailed's line says. Cleared when a model is
        /// built.</summary>
        static volatile bool rainHookFailed;

        /// <summary>Vanilla's rain cloudness is Earth's (unless an admin set the precipitation with /weather). At the calendar's
        /// own time, like the winds, storms and forecasts: vanilla's RainCloudDaysOffset (it only shifts its rain noise) is
        /// left out, so a save with one doesn't get one moment's rain with another's storm winds. A postfix on purpose, not a
        /// prefix: Cylinder World prefixes the same method (SeamRainPatch.Fold, returning false in every Earth-sized world),
        /// and Harmony skips any later prefix that would also replace the result, so a prefix here would never run and the
        /// rain would be vanilla's. A postfix runs after whichever prefix answered and has the last word.</summary>
        static void RainCloudnessPostfix(WeatherSystemBase __instance, double posX, double posZ, double totalDays, ref float __result)
        {
            if (__instance.OverridePrecipitation.HasValue) return;
            var m = ModelFor(__instance.api);
            if (m == null) return;
            try
            {
                long start = ClimateCost.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                __result = (float)m.Cloudness(posX, posZ, totalDays * m.Settings.HoursPerDay);
                if (start != 0) ClimateCost.CountRain(System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }
            catch (Exception e) { rainHookFailed = true; HookFailed(__instance.api.Side, "the rain cloudness", e); }
        }

        /// <summary>
        /// Vanilla's /weather stoprain (WeatherSystemCommands.RainStopFunc with skipForward) moves vanilla's rain noise on by
        /// RainCloudDaysOffset and says the rain will stop; Earth's rain leaves that offset out (above), so nothing would
        /// change. While Earth's weather decides the rain the command says so instead and leaves the offset alone (CR-W6).
        /// /whenwillitstopraining (no skipForward) already reads Earth's rain, and an admin's set precipitation keeps
        /// vanilla's answer, so both stay vanilla's.
        /// </summary>
        static bool RainStopPrefix(bool skipForward, ref TextCommandResult __result)
        {
            var self = serverInstance;
            if (!skipForward || !RainPatched || self?.Model == null) return true;
            if (self.api.ModLoader.GetModSystem<WeatherSystemServer>()?.OverridePrecipitation.HasValue == true) return true;
            __result = TextCommandResult.Success("Earth's weather decides the rain in this world and can't be moved on: /weather stoprain changes nothing here. " +
                "/whenwillitstopraining says when the rain here stops; /weather setprecip sets the rain until /weather setprecipa.");
            return false;
        }

        /// <summary>Hail at least this strong (WeatherModel.HailAt) falls as hail.</summary>
        const double HailFrom = 0.25;

        /// <summary>(Rules 6, EA-0019) Hail where Earth's thunderstorms have it and nowhere else: what falls at a place is
        /// hail there, and vanilla's random hail event falls as rain or snow (Auto) instead.</summary>
        static void PrecipitationStatePostfix(WeatherSystemBase __instance, Vec3d pos, double totalDays, PrecipitationState __result)
        {
            if (__result == null || pos == null || __instance.OverridePrecipitation.HasValue) return;
            var m = ModelFor(__instance.api);
            if (m == null || !m.Settings.Has.Hail) return;
            bool hurts = m.Settings.Has.HailHurts;
            // (rules 6 and 7 as built: only where vanilla's own state has precipitation, its level the rain cloudness - 1)
            if (!hurts && __result.Level <= 0) return;
            try
            {
                double strength = m.HailAt(pos.X, pos.Z, totalDays * m.Settings.HoursPerDay, GroundMetres(__instance.api, pos.Y));
                bool hail = strength >= HailFrom;
                if (hail)
                {
                    __result.Type = EnumPrecipitationType.Hail;
                    // (rules 8, EA-0093) the hail hurts as vanilla's large hail does: vanilla's state subtracts the rain line
                    // twice (its level and particle size are the cloudness - 1), so its damage, which needs a particle size of
                    // 0.5 and rolls the level / 2 each second, almost never came from Earth's storms (cloudness up to 1.6).
                    // Earth's hail gives the large hail's size (0.6-1.0, vanilla's event 0.8 ± 0.2) and its strength as the level:
                    // a hit of 0.04-0.07 HP with a chance of 1/8 to 1/2 a second, about 2 HP a minute in the strongest
                    if (hurts)
                    {
                        __result.ParticleSize = Math.Max(__result.ParticleSize, HailSizeMin + (1 - HailSizeMin) * strength);
                        __result.Level = Math.Max(__result.Level, strength);
                    }
                }
                else if (__result.Type == EnumPrecipitationType.Hail) __result.Type = EnumPrecipitationType.Auto;
            }
            catch (Exception e) { HookFailed(__instance.api.Side, "the hail", e); }
        }

        /// <summary>(Rules 8) The hail's particle size at the weakest hail that falls (vanilla's damage needs 0.5).</summary>
        const double HailSizeMin = 0.6;

        /// <summary>(Rules 6, EA-0019) The client's weather regions draw and sound hail where Earth's thunderstorm over the
        /// player has it (the regions' snapshots are blended at the player), and vanilla's hail events as rain or snow. After each
        /// region's 25 ms tick, whose last line sets the blended type from vanilla's event (EA-0101; on the server it does nothing).</summary>
        static void RegionClientUpdatePostfix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws)
        {
            var api = ___ws?.api as ICoreClientAPI;
            var m = ModelFor(api);
            var wd = __instance.weatherData;
            if (m == null || !m.Settings.Has.Hail || wd == null || ___ws.OverridePrecipitation.HasValue) return;
            var pos = api.World.Player?.Entity?.Pos;
            if (pos == null) return;
            try
            {
                // (ER-12) the four regions ask the same place and hour: worked out once
                bool hail = WeatherDrawCache.HailAt(m, pos.X, pos.Z, HoursNow(api.World, m), GroundMetres(api, pos.Y)) >= HailFrom;
                if (hail) wd.BlendedPrecType = EnumPrecipitationType.Hail;
                else if (wd.BlendedPrecType == EnumPrecipitationType.Hail) wd.BlendedPrecType = EnumPrecipitationType.Auto;
            }
            catch (Exception e) { HookFailed(api.Side, "the hail drawn", e); }
        }

        // (rules 7) how the client draws Earth's fog: the flat fog's density at full ground fog (vanilla's own mist is 60 ± 20
        // over 250), how high it reaches over the sea (m) and over the land's own ground (blocks), and the distance fog added
        // at full (vanilla's patterns add 1-3, haze up to 150, over 1000)
        const double FogLayerDensity = 90, SeaFogMetres = 150, LandFogBlocks = 6, AirFogDensity = 35;

        /// <summary>
        /// (Rules 7, EA-0026) Each weather region's fog on the client is Earth's at the region's centre now
        /// (WeatherModel.FogAt) instead of its patterns' random fog and mist: a layer of flat fog on the cold seas and their
        /// coasts (from the sea up) and in the land's valleys on clear calm humid nights (from the ground at the player up),
        /// and the air thicker in fog and heavy rain. Vanilla blends the four regions around the player as before. The
        /// weight is Earth's, not vanilla's (which leaves dry places foggless whatever the weather: Lima's coastal fog).
        /// Not on the server (fog is only drawn); an admin's /weather setprecip hands it back to the patterns.
        /// </summary>
        static void RegionFogPostfix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws)
        {
            if (___ws?.api is not ICoreClientAPI api || ___ws.OverridePrecipitation.HasValue || __instance.IsDummy) return;
            var m = ModelFor(api);
            var amb = __instance.weatherData?.Ambient;
            if (m == null || amb == null || !m.Settings.Has.Fog) return;
            try
            {
                int rs = api.World.BlockAccessor.RegionSize, sea = api.World.SeaLevel;
                var pos = api.World.Player?.Entity?.Pos;
                int groundY = pos != null ? api.World.BlockAccessor.GetRainMapHeightAt((int)pos.X, (int)pos.Z) : sea;
                if (groundY <= 0) groundY = sea;
                // (ER-03) kept a moment per region: vanilla asks the four regions round the player every frame
                var fog = WeatherDrawCache.FogAt(m, __instance.regionX, __instance.regionZ, (__instance.regionX + 0.5) * rs, (__instance.regionZ + 0.5) * rs,
                    HoursNow(api.World, m), GroundMetres(api, groundY));
                var vertical = For(api.Side)?.earth?.Model?.Vertical;
                double seaTop = vertical != null ? Math.Max(3, vertical.Y(SeaFogMetres) - sea) : 8;
                double top = fog.Sea ? seaTop : Math.Max(1, groundY - sea) + LandFogBlocks;
                // a negative flat fog lies below its level (vanilla's sea-level mist); 0 clears the patterns' mist
                amb.FlatFogDensity.Set((float)(-FogLayerDensity * fog.Ground / 250), 1);
                amb.FlatFogYPos.Set((float)top);
                amb.FogDensity.Set((float)((api.Ambient.Base.FogDensity.Value + 1 + AirFogDensity * fog.Air) / 1000), 1);
            }
            catch (Exception e) { HookFailed(api.Side, "the fog", e); }
        }

        /// <summary>
        /// (Rules 7, EA-0027) A cloud tile's thickness follows Earth's cloud cover at it (WeatherModel.CloudCover: the
        /// month's, its fronts' and showers' and its storms', overcast where it rains), the pattern's own thickness scaled by
        /// it so the clouds keep their shapes (<see cref="CloudTileThickness"/>). Vanilla's tiles took their thickness from the 512-block region's pattern alone, so a cloud
        /// edge ran along the region's border rather than the rain area's. Both cloud renderers read this.
        /// </summary>
        static void CloudThicknessPostfix(WeatherSystemBase ___ws, int cloudTileX, int cloudTileZ, ref double __result)
        {
            if (___ws?.api is not ICoreClientAPI api || ___ws.OverridePrecipitation.HasValue) return;
            var m = ModelFor(api);
            if (m == null || !m.Settings.Has.CloudTiles) return;
            try
            {
                double size = ___ws.CloudTileSize;
                // (ER-12) kept a moment per tile: the cloud thread asks every tile on every pass
                double cover = WeatherDrawCache.CloudCoverAt(m, cloudTileX, cloudTileZ, (cloudTileX + 0.5) * size, (cloudTileZ + 0.5) * size, HoursNow(api.World, m));
                __result = CloudTileThickness(__result, cover);
            }
            catch (Exception e) { HookFailed(api.Side, "the cloud tiles", e); }
        }

        /// <summary>(Rules 7) A cloud tile's thickness (0..1): the pattern's own (its shapes) scaled by Earth's cloud cover
        /// there, half under a sky a third covered, one and a half times where it is four-fifths covered and up to 1.7
        /// times where it rains, none under a clear sky (cover under 5 %, fading in up to 35 %).</summary>
        internal static double CloudTileThickness(double pattern, double cover)
            => Math.Clamp(Math.Clamp(pattern, 0, 1) * (0.5 + 1.2 * cover) * WeatherNoise.SmoothStep(0.05, 0.35, cover), 0, 1);

        /// <summary>The cloud codes of vanilla's patterns by how cloudy they are: fair, mixed (neutral) and cloudy.</summary>
        internal static double CoverWeight(string code, double cover)
        {
            code ??= "";
            bool fair = code.StartsWith("clearsky") || code.StartsWith("cirro") || code.StartsWith("alto");
            bool cloudy = code.StartsWith("overcast") || code.StartsWith("stratus") || code.Contains("haze") || code.StartsWith("cumulonimbus");
            if (fair) return 0.1 + 1.9 * Math.Pow(1 - cover, 1.5);
            if (cloudy) return 0.1 + 1.9 * Math.Pow(cover, 1.5);
            return 0.6 + 0.8 * (1 - Math.Abs(cover - 0.5) * 2);
        }

        /// <summary>
        /// A weather region picks its next cloud pattern by vanilla's chances (its rainfall and temperature) times how well
        /// the pattern fits Earth's cloud cover at the region's centre now: clear skies in the desert and the dry season,
        /// overcast where the month is wet and a front is near. Uses the region's own random numbers like vanilla.
        /// </summary>
        static bool RandomWeatherPatternPrefix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws, ref WeatherPattern __result)
        {
            var m = ModelFor(___ws?.api);
            var patterns = __instance.WeatherPatterns;
            if (m == null || patterns == null || patterns.Length == 0 || __instance.Rand == null) return true;
            try
            {
                int rs = ___ws.api.World.BlockAccessor.RegionSize;
                double cover = m.CloudCover((__instance.regionX + 0.5) * rs, (__instance.regionZ + 0.5) * rs, HoursNow(___ws.api.World, m));
                var cond = __instance.weatherData.climateCond;
                double total = 0;
                var w = new double[patterns.Length];
                for (int i = 0; i < patterns.Length; i++)
                {
                    patterns[i].updateHereChance(cond?.Rainfall ?? 0, cond?.Temperature ?? 15);
                    w[i] = Math.Max(0, patterns[i].hereChance) * CoverWeight(patterns[i].config?.Code, cover);
                    total += w[i];
                }
                if (total <= 0) return true;
                double r = __instance.Rand.NextFloat() * total;
                for (int i = 0; i < patterns.Length; i++)
                {
                    r -= w[i];
                    if (r <= 0) { __result = patterns[i]; return false; }
                }
                __result = patterns[patterns.Length - 1];
                return false;
            }
            catch (Exception e) { HookFailed(___ws.api.Side, "the cloud pattern pick", e); return true; }
        }
    }
}
