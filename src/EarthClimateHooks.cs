using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>
    /// Earth's climate hook on both sides (EarthMapSystem subscribes it at server and client start, after every mod's Start(),
    /// so it runs after vanilla's handlers). It reads the world's parameters and model as EarthMapSystem holds them.
    /// </summary>
    sealed class EarthClimateHooks
    {
        readonly EarthMapSystem core;

        public EarthClimateHooks(EarthMapSystem core) { this.core = core; }

        /// <summary>
        ///  1. Vanilla's climate map stores -20..40 °C. Where the real climate is colder or hotter (Antarctica, Greenland),
        ///     add the difference to the worldgen and live temperature.
        ///  2. Seasons: vanilla swings the temperature by 65 °C × |latitude| between winter and summer everywhere. Replace
        ///     that with each place's real swing between its coldest and warmest month: mild maritime winters (Britain,
        ///     Norway with the Gulf Stream), mild Mediterranean winters, extreme continental ones (Siberia).
        /// </summary>
        public void OnGetClimate(ref ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode, double totalDays)
        {
            if (!ClimateCost.On) { Adjust(climate, pos, mode, totalDays); return; }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Adjust(climate, pos, mode, totalDays);
            ClimateCost.Count(pos, mode, totalDays, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }

        void Adjust(ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode, double totalDays)
        {
            var p = core.Params;
            var m = core.Model;
            if (p == null || !p.Enabled || m == null || pos == null) return;
            if (p.ExtendTemperature)
            {
                // what vanilla's reading lacks: both its cuts to -20..40 °C (the stored byte, the final value) undone;
                // a caller that supplied Earth's own temperature as the base (regional fauna) gets the range extension only
                double lost;
                if (EarthMapSystem.SuppliedEarthTemperature)
                {
                    double t = m.TrueTemperature(pos.X, pos.Z, pos.Y);
                    lost = t - Math.Clamp(t, -20, 40);
                }
                else lost = m.ClimateCorrection(pos.X, pos.Z, pos.Y);
                double worldgen = lost;
                if (p.ClimateRules < 1 || EarthMapSystem.SuppliedEarthTemperature)
                {
                    // older worlds keep their world generation (new chunks must match): the correction from before 0.14.1 (the
                    // client undoes its own -50 cut)
                    double t = m.TrueTemperature(pos.X, pos.Z, pos.Y);
                    worldgen = t - Math.Clamp(t, m.VanillaFloor, 40);
                }
                Shift(climate, worldgen, lost);
            }
            if (p.RealSeasons && mode != EnumGetClimateMode.WorldGenValues) climate.Temperature += (float)SeasonCorrection(pos, totalDays);
            // (weather rules 6, EA-0025) the weather's warm and cold spells and the rain's cooling by day and warmth by night,
            // on the live temperature only (world generation reads the climate alone)
            if (mode != EnumGetClimateMode.WorldGenValues)
            {
                var side = core.AnyApi?.Side;
                var w = side == null ? null : EarthWeather.For(side.Value)?.Model;
                if (w != null && w.Settings.Has.TemperatureAnomaly && !EarthWeather.PrecipitationOverridden(core.AnyApi))
                {
                    long start = ClimateCost.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    try { climate.Temperature += (float)w.TemperatureAnomaly(pos.X, pos.Z, totalDays * w.Settings.HoursPerDay, EarthWeather.GroundMetres(core.AnyApi, pos.Y)); }
                    catch (Exception e) { EarthWeather.ReportHookFailure(side.Value, "the weather's temperature", e); }
                    if (start != 0) ClimateCost.CountWeather(System.Diagnostics.Stopwatch.GetTimestamp() - start);
                }
            }
        }

        /// <summary>The reading this thread shifted last, the base it left in it and the shift it gave that base (see Shift).</summary>
        [ThreadStatic] static ClimateCondition lastShifted;
        [ThreadStatic] static float lastBase, lastBaseShift;

        /// <summary>
        /// The range extension's shift of one reading, once: <paramref name="worldgen"/> to its base (world generation's
        /// temperature), <paramref name="lost"/> to its live temperature, each where it is 0.05 °C or more.
        /// Vanilla reuses readings: GetClimateAt(pos, baseClimate, mode, totalDays) sets the live temperature back to the base
        /// and raises the event again on the same object, many times in a row in its catch-up loops (snow accumulation,
        /// farmland, berry bushes, fruit trees, transient blocks, harvestable animals), each on one thread with nothing else
        /// read in between. Adding the shift every time moved a reading's base by it again on each pass (before 2.0.0-rc.3;
        /// Cylinder World's own correction had the same bug, its CC2, and is made idempotent the same way). The object back
        /// with the base this shift left in it is that reuse: its base keeps the shift it has, and its live temperature,
        /// set back to that base (vanilla's seasons then work from it), gets only what <paramref name="lost"/> differs from
        /// it by (nothing in worlds whose two shifts are the same). (Its value alone can't tell: the map's own jitter overlaps
        /// shifts that small.) World generation never reuses a reading (the reuse overload is the running game's), so what it
        /// reads is unchanged.
        /// </summary>
        internal static void Shift(ClimateCondition climate, double worldgen, double lost)
        {
            float toBase = Math.Abs(worldgen) >= 0.05 ? (float)worldgen : 0, toLive = Math.Abs(lost) >= 0.05 ? (float)lost : 0;
            if (ReferenceEquals(climate, lastShifted) && climate.WorldGenTemperature == lastBase)
            {
                if (toLive != lastBaseShift) climate.Temperature += toLive - lastBaseShift;
                return;
            }
            if (toBase != 0) climate.WorldGenTemperature += toBase;
            if (toLive != 0) climate.Temperature += toLive;
            // (a reading whose base got no shift is right as it is when reused: base and live temperature unshifted)
            if (toBase != 0)
            {
                lastShifted = climate;
                lastBase = climate.WorldGenTemperature;
                lastBaseShift = toBase;
            }
        }

        /// <summary>Real seasonal temperature minus vanilla's (same curve shape and timing as vanilla's ModTemperature).</summary>
        double SeasonCorrection(BlockPos pos, double totalDays)
        {
            var world = core.AnyApi?.World;
            var cal = world?.Calendar;
            if (cal?.OnGetLatitude == null) return 0;
            double lat = cal.OnGetLatitude(pos.Z);
            double vanillaSwing = Math.Abs(lat) * 65.0;
            double s;
            float? over = cal.SeasonOverride;
            if (over.HasValue)
            {
                s = GameMath.Smootherstep(Math.Abs(GameMath.CyclicValueDistance(0.5f, over.Value * 12f, 12f) / 6f));
            }
            else
            {
                double yearRel = totalDays / cal.DaysPerYear % 1.0;
                s = GameMath.Smootherstep(Math.Abs(GameMath.CyclicValueDistance(lat > 0 ? 0.5 : 6.5, yearRel * 12.0, 12.0) / 6.0));
            }
            var g = core.Model.Geometry.ToGeo(pos.X, pos.Z);
            var cs = core.Data.SampleClimate(g.Lat, g.Lon);
            double realSwing = cs.TWarm - cs.TCold;
            double midOffset = (cs.TWarm + cs.TCold) / 2 - cs.TMean;
            return (realSwing - vanillaSwing) * (s - 0.5) + midOffset;
        }
    }
}
