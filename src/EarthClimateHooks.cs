using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using EarthMap.Core;

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
            // (decision 0086) seasons rules 1: the monthly temperature layer replaces the season, the day and the spells below;
            // RealSeasons false (earth.json, read at every start) keeps vanilla's season and day in every world (Fable's F3)
            if (p.SeasonRules >= 1 && p.RealSeasons && mode != EnumGetClimateMode.WorldGenValues && LayerTemperature(climate, pos, totalDays)) return;
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
        [ThreadStatic] static SeasonCell seasonCell;
        [ThreadStatic] static SunEntry[] suns;
        // (Fable's re-review N1) keyed by the world's model id (TerrainModel.CacheId, a number from 1), not its calendar: a
        // thread that outlives a world then keeps nothing of it reachable
        struct SunEntry { public long Model, Day, Zx, Zz; public double Rise, Set, Noon; }
        // 256 (about 12 kB a thread): vanilla's catch-up loops read many places and days in turn, and at 32 the table missed
        // 20.8 % of readings with 1 player and 54.5 % with 13; at 256, 1.2 % and 7.6 % (the data session's measurement, 16 November)
        const int SunEntries = 256;
        Vintagestory.GameContent.ModTemperature vanillaTemperature;
        bool vanillaTemperatureLooked;

        /// <summary>
        /// (Decision 0086, seasons rules 1) The live temperature from the monthly temperature layer: the place's own monthly
        /// curve and day-night range on a day shaped by the sun (<see cref="SeasonTemperature"/>), its spells sized by its own
        /// day-to-day spread and the rain's day-night part (<see cref="WeatherModel.SeasonWeather"/>), in place of vanilla's
        /// season, day and noise terms (computed as vanilla's ModTemperature does and taken out, so another mod's change to the
        /// reading stays). False where the layer has no cell here (no file; a client before its tile arrives): the caller then
        /// applies rules 0's shape, as before.
        /// </summary>
        bool LayerTemperature(ClimateCondition climate, BlockPos pos, double totalDays)
        {
            var layer = core.SeasonLayer;
            var api = core.AnyApi;
            var cal = api?.World?.Calendar;
            if (layer == null || cal == null || cal.OnGetLatitude == null) return false;
            var g = core.Model.Geometry.ToGeo(pos.X, pos.Z);
            var cell = seasonCell ??= new SeasonCell();
            if (!layer.Sample(g.Lat, g.Lon, cell)) return false;
            double yearRel = totalDays / cal.DaysPerYear % 1.0;
            if (yearRel < 0) yearRel += 1;
            float? over = cal.SeasonOverride;
            double seasonRel = over.HasValue ? over.Value - Math.Floor(over.Value) : yearRel;
            double dayRel = totalDays - Math.Floor(totalDays);
            SunTimes(cal, core.Model.CacheId, pos.X, pos.Z, totalDays, (float)yearRel, out double rise, out double set, out double noon);
            double target = SeasonTemperature.Deviation(cell, seasonRel, dayRel * 24, rise, set, noon, out double spread);
            var side = api.Side;
            var w = EarthWeather.For(side)?.Model;
            if (w != null && !EarthWeather.PrecipitationOverridden(api))
            {
                try
                {
                    target += w.SeasonWeather(pos.X, pos.Z, totalDays * w.Settings.HoursPerDay, EarthWeather.GroundMetres(api, pos.Y), out double spell);
                    target += spread * spell;
                }
                catch (Exception e) { EarthWeather.ReportHookFailure(side, "the weather's temperature", e); }
            }
            climate.Temperature += (float)(target - VanillaSeasonDay(climate, pos, totalDays, cal));
            return true;
        }

        /// <summary>(Decision 0086) What seasons rules 1 reads here now, for /earthweather: the month's curve against the
        /// place's yearly mean, the day-night range and its shape at this hour, the sun's times, the spread, and what it takes
        /// out of vanilla's reading; or why it falls back to rules 0's shape.</summary>
        // (bench, 16 November 2026) every signed number has a third, zero section: without it .NET writes a value that rounds
        // to zero from below as "-+0.00"
        internal string Report(BlockPos pos, double totalDays)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var p = core.Params;
            if (p == null || p.SeasonRules < 1) return "Seasons rules 0 (the season shape from the place's coldest and warmest month).";
            if (!p.RealSeasons) return "Seasons rules " + p.SeasonRules + ", but RealSeasons is false in earth.json: vanilla's seasons and day.";
            var layer = core.SeasonLayer;
            var api = core.AnyApi;
            var cal = api?.World?.Calendar;
            if (layer == null) return "Seasons rules 1, but the monthly layer isn't here this session: rules 0's shape.";
            if (cal == null || cal.OnGetLatitude == null) return "Seasons rules 1: no calendar yet.";
            var g = core.Model.Geometry.ToGeo(pos.X, pos.Z);
            var cell = new SeasonCell();
            if (!layer.Sample(g.Lat, g.Lon, cell)) return "Seasons rules 1: this place's tile hasn't arrived: rules 0's shape.";
            double yearRel = totalDays / cal.DaysPerYear % 1.0;
            if (yearRel < 0) yearRel += 1;
            double dayRel = totalDays - Math.Floor(totalDays);
            SunTimes(cal, core.Model.CacheId, pos.X, pos.Z, totalDays, (float)yearRel, out double rise, out double set, out double noon);
            double curve = SeasonCurve.Mean(cell.Means, yearRel), year = SeasonCurve.YearMean(cell.Means);
            double range = SeasonCurve.Linear(cell.Ranges, yearRel), spread = SeasonCurve.Linear(cell.Spreads, yearRel);
            double shape = DayShape.At(dayRel * 24, rise, set, noon);
            var climate = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            double vanilla = climate == null ? double.NaN : VanillaSeasonDay(climate, pos, totalDays, cal);
            double spell = 0, rain = 0;
            var w = EarthWeather.For(api.Side)?.Model;
            if (w != null) rain = w.SeasonWeather(pos.X, pos.Z, totalDays * w.Settings.HoursPerDay, EarthWeather.GroundMetres(api, pos.Y), out spell);
            return string.Format(ci, "Seasons rules 1 (the monthly layer): the year's mean {0:0.0} °C at sea level, the curve now {1:+0.0;-0.0;+0.0} against it; " +
                "day-night range {2:0.0} °C, the hour's place in it {3:+0.00;-0.00;+0.00} ({4:+0.0;-0.0;+0.0} °C); sun up {5:0.0} h to {6:0.0} h; day-to-day spread {7:0.0} °C, " +
                "the spell now {8:+0.00;-0.00;+0.00} ({9:+0.0;-0.0;+0.0} °C), the rain's day part {10:+0.0;-0.0;+0.0} °C; vanilla's season, day and noise taken out: {11:+0.0;-0.0;+0.0} °C; " +
                "the live temperature less the year's mean at the ground and these parts: {12:+0.00;-0.00;+0.00} °C (0 when the handlers add up).",
                year, curve - year, range, shape, range * shape, rise, set, spread, spell, spread * spell, rain, vanilla, Leftover());
            double Leftover()
            {
                // the live reading, less its base (the year's mean at the ground, as world generation reads it) and the parts
                var live = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
                if (live == null || climate == null) return double.NaN;
                bool overridden = EarthWeather.PrecipitationOverridden(api);
                double parts = curve - year + range * shape + (overridden ? 0 : spread * spell + rain);
                return live.Temperature - live.WorldGenTemperature - parts;
            }
        }

        /// <summary>What vanilla's ModTemperature added to the base for the season, the hour and its two date-only noises
        /// (ModTemperature.updateTemperature, game 1.22.7); 0 where ModTemperature isn't loaded. Right because Earth's
        /// handler runs after ModTemperature's (vanilla subscribes in Start, Earth at server and client start), so the
        /// reading Earth adds to is vanilla's whole; /earthweather's seasons line shows what is left over (0 when it adds
        /// up).</summary>
        double VanillaSeasonDay(ClimateCondition climate, BlockPos pos, double totalDays, IGameCalendar cal)
        {
            if (!vanillaTemperatureLooked)
            {
                vanillaTemperature = core.AnyApi?.ModLoader.GetModSystem<Vintagestory.GameContent.ModTemperature>();
                vanillaTemperatureLooked = true;
            }
            var mt = vanillaTemperature;
            if (mt == null) return 0;
            double yearRel = totalDays / cal.DaysPerYear % 1.0;
            double hourOfDay = totalDays % 1.0 * cal.HoursPerDay;
            double lat = cal.OnGetLatitude(pos.Z);
            double swing = Math.Abs(lat) * 65.0, v = -swing / 2;
            float? over = cal.SeasonOverride;
            if (over.HasValue) v += swing * GameMath.Smootherstep(Math.Abs(GameMath.CyclicValueDistance(0.5f, over.Value * 12f, 12f) / 6f));
            else v += swing * GameMath.Smootherstep(Math.Abs(GameMath.CyclicValueDistance(lat > 0 ? 0.5 : 6.5, yearRel * 12.0, 12.0) / 6.0));
            // (Fable's F5) the rainfall vanilla's day term read: the climate map's, which GetClimateAt puts into Rainfall
            // before the handlers run (ServerWorldMap/ClientWorldMap) and keeps in WorldgenRainfall; the weather system's
            // handler later replaces Rainfall with the precipitation now, so reading WorldgenRainfall gives the same term
            // whether Earth's handler runs before or after the weather system's (the one overload that leaves it 0, the
            // GetClimateAt(pos, climate int) of a chunk's climate, falls back to Rainfall)
            float rainfall = climate.WorldgenRainfall > 0 ? climate.WorldgenRainfall : climate.Rainfall;
            double dayRange = 18f - rainfall * 13f;
            v += (GameMath.SmoothStep(Math.Abs(GameMath.CyclicValueDistance(4.0, hourOfDay, 24.0) / 12.0)) - 0.5) * dayRange;
            if (mt.YearlyTemperatureNoise != null) v += mt.YearlyTemperatureNoise.Noise(totalDays, 0.0) * 3.0;
            if (mt.DailyTemperatureNoise != null) v += mt.DailyTemperatureNoise.Noise(totalDays, 0.0);
            return v;
        }

        /// <summary>Sunrise and sunset (hours, 24-hour clock of the game day) where the player is, from the calendar's own sun
        /// (OnGetSolarSphericalCoords, which AstraTerra replaces with its longitude-aware one): the zenith angle sampled every
        /// quarter hour, the crossings of the horizon interpolated, and <paramref name="noon"/> the hour of the highest
        /// sampled sun (for a sun that doesn't set or rise, on AstraTerra's local clock too). A sun that never sets: 0 to
        /// 24; one that never rises: 12 to 12. Kept per thread in a table of <see cref="SunEntries"/> (256) entries, by a hash of the game day and an
        /// area of 2048 x 256 blocks (rows of 1,024 would put tens of minutes of sunrise into one row near 60 degrees at the
        /// solstice) and keyed also by the world's model id, so readings near players far apart don't evict each other and
        /// another world's times never answer (Fable's review F2 and F5, re-review N1).</summary>
        static void SunTimes(IGameCalendar cal, long model, double x, double z, double totalDays, float yearRel, out double rise, out double set, out double noon)
        {
            long day = (long)Math.Floor(totalDays), zx = (long)Math.Floor(x / 2048), zz = (long)Math.Floor(z / 256);
            var table = suns ??= new SunEntry[SunEntries];
            ulong hash = (ulong)day * 0x9E3779B97F4A7C15UL ^ (ulong)zx * 0xC2B2AE3D27D4EB4FUL ^ (ulong)zz * 0x165667B19E3779F9UL;
            ref var e = ref table[(int)((hash ^ (hash >> 29)) % SunEntries)];
            if (e.Model == model && e.Day == day && e.Zx == zx && e.Zz == zz) { rise = e.Rise; set = e.Set; noon = e.Noon; return; }
            const int steps = 96;
            double cx = (zx + 0.5) * 2048, cz = (zz + 0.5) * 256;
            // the sun's height as the calendar's sun vector has it (GameCalendar.GetSunPosition: up = cos of the "zenith
            // angle", which vanilla's survival sun gives as 2 pi - acos(sin of the altitude)): above 0 while the sun is up
            double Altitude(int k) => Math.Cos(cal.OnGetSolarSphericalCoords(cx, cz, yearRel, (float)(k / (double)steps)).ZenithAngle);
            double prev = Altitude(0), up = double.NaN, down = double.NaN, highest = prev;
            int above = prev > 0 ? 1 : 0, top = 0;
            for (int k = 1; k <= steps; k++)
            {
                double a = Altitude(k);
                if (a > 0) above++;
                if (a > highest) { highest = a; top = k; }
                if (prev <= 0 && a > 0 && double.IsNaN(up)) up = (k - 1 + prev / (prev - a)) * 24.0 / steps;
                if (prev > 0 && a <= 0 && double.IsNaN(down)) down = (k - 1 + prev / (prev - a)) * 24.0 / steps;
                prev = a;
            }
            if (double.IsNaN(up) || double.IsNaN(down))
            {
                if (above > steps / 2) { rise = 0; set = 24; }
                else { rise = 12; set = 12; }
            }
            else { rise = up; set = down; }
            noon = top * 24.0 / steps;
            e = new SunEntry { Model = model, Day = day, Zx = zx, Zz = zz, Rise = rise, Set = set, Noon = noon };
        }

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
