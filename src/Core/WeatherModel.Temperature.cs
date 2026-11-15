using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, the weather's own warmth and chill (rules 6); moved unchanged from
    // WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ temperature (rules 6)

        // (EA-0025) fitted to 56 GHCN-Daily stations, 1991-2020, 574 station-months (tools/fit_weather_temperature.py):
        // the spread of the daily mean around its seasonal cycle, sd = a + b (1 - e^(-range / c)) (1 - s smoothstep(0, 20, T))
        const double SpellSdBase = 0.356, SpellSdRange = 5.436, SpellSdScale = 14, SpellSdSummer = 0.40;
        const double TempSpellEvolve = 60, TempSpellReset = 48;
        // wet days (1 mm or more) minus dry days, by the month's mean temperature: the day's high and the night's low
        static readonly double[] WetT = { -10, 5, 15, 27 }, WetHigh = { 0.76, -1.75, -3.19, -2.20 }, WetLow = { 3.01, 1.52, 0.65, -0.61 };

        /// <summary>
        /// (Weather rules 6, EA-0025) How much warmer (+) or colder (-) the weather makes it here and now than the season and
        /// the hour alone (°C; 0 in worlds before rules 6): warm and cold spells, carried along with the weather as the
        /// fronts are, as strong as at real stations of that climate (from a fifth of a degree in the tropics' sea air to 6
        /// °C in a continental winter: GHCN-Daily, the daily mean's spread around its seasonal cycle, fitted to the yearly
        /// range and the month's warmth), and the rain's own: on a wet day the afternoon is cooler and the night milder than
        /// on a dry one, by what the same stations show for a month that warm (a warm month's rainy afternoon 3 °C cooler; a
        /// winter's snowy day milder, its night 3 °C). Applied to the game's live temperature on both sides. The month's
        /// warmth the fits read is at the ground, <paramref name="metres"/> above the sea.
        /// </summary>
        public double TemperatureAnomaly(double x, double z, double hours, double metres = 0)
        {
            var has = Settings.Has;
            if (!has.TemperatureAnomaly) return 0;
            AnomalyParts p;
            if (has.AnomalyCells)
            {
                // (rules 8, EA-0073) the weather's part at the cell's centre and the step's start, kept per thread: every
                // side computes the same numbers for the same cell and step, cached or not
                long cx = (long)Math.Floor(x / AnomalyCellBlocks), cz = (long)Math.Floor(z / AnomalyCellBlocks), step = (long)Math.Floor(hours / AnomalyStepHours);
                var cache = anomalyCache ??= new AnomalyEntry[AnomalyCacheSize];
                // two ways per set (the older one replaced), sets by a mixed hash of the cell and step
                ulong h = (ulong)cx * 0x9E3779B97F4A7C15UL ^ (ulong)cz * 0xC2B2AE3D27D4EB4FUL ^ (ulong)step * 0x165667B19E3779F9UL;
                h ^= h >> 31;
                h *= 0xBF58476D1CE4E5B9UL;
                int set = (int)(h >> 55) & ~1;
                ref var a = ref cache[set];
                ref var b = ref cache[set + 1];
                if (a.Owner == Id && a.Cx == cx && a.Cz == cz && a.Step == step) p = a.Parts;
                else if (b.Owner == Id && b.Cx == cx && b.Cz == cz && b.Step == step) p = b.Parts;
                else
                {
                    p = AnomalyPartsAt((cx + 0.5) * AnomalyCellBlocks, (cz + 0.5) * AnomalyCellBlocks, step * AnomalyStepHours);
                    b = a;
                    a = new AnomalyEntry { Owner = Id, Cx = cx, Cz = cz, Step = step, Parts = p };
                }
            }
            else p = AnomalyPartsAt(x, z, hours);
            double ground = p.Temp - GroundLapse * Math.Max(0, metres);
            double sd = SpellSdBase + SpellSdRange * (1 - PortableMath.Exp(-p.Range / SpellSdScale)) * (1 - SpellSdSummer * WeatherNoise.SmoothStep(0, 20, ground));
            double rain = 0;
            if (p.Wet > 0)
            {
                double high = Interpolate(WetT, WetHigh, ground), low = Interpolate(WetT, WetLow, ground);
                rain = p.Wet * (low + (high - low) * p.Day);
            }
            return sd * p.Spell + rain;
        }

        /// <summary>The parts of <see cref="TemperatureAnomaly"/> that don't depend on the ground's height: the month's
        /// sea-level normal, the yearly range, the warm or cold spell, how wet it is and the hour's place in the day.</summary>
        struct AnomalyParts { public double Temp, Range, Spell, Wet, Day; }

        struct AnomalyEntry { public long Owner; public long Cx, Cz, Step; public AnomalyParts Parts; }

        AnomalyParts AnomalyPartsAt(double x, double z, double hours)
        {
            var s = new WeatherSample();
            // (the steering the rain was carried by: the same inputs, ER-12)
            Rain(x, z, hours, ref s, false, out var n, out _, out double vx, out double vz);
            var p = new AnomalyParts
            {
                Temp = n.Temp,
                Range = Data.AnnualRange(s.Lat, s.Lon),
                Spell = Advected(key + 53, x, z, hours * Settings.Pace, vx, vz, synScale, synCells, synCells2, TempSpellEvolve, TempSpellReset),
                Wet = WeatherNoise.SmoothStep(0.45, 0.6, s.Cloudness),
            };
            if (p.Wet > 0) p.Day = 0.5 * (1 + PortableMath.Cos(2 * Math.PI * (HourOfDay(x, hours) - 14) / 24));
            return p;
        }

        /// <summary>(Rules 8, EA-0073) The weather rules from which the temperature anomaly is the same over a cell of
        /// <see cref="AnomalyCellBlocks"/> blocks and a step of <see cref="AnomalyStepHours"/> game hours (its height part
        /// excepted), so that it can be kept per thread: the climate hook reads it 1,200-1,800 times a second at many places
        /// a few blocks apart, and the full rain sample was most of the hook's cost.</summary>
        /// <summary>(Rules 8) The anomaly's cell (blocks) and time step (game hours: ten game minutes). The spells are fronts
        /// hundreds of kilometres across changing over days, and the rain's edge moves a cell in a few game minutes, so the
        /// steps are a few hundredths of a degree, at a rain's edge up to the rain's own (1-3 °C) one cell sooner.</summary>
        public const double AnomalyCellBlocks = 32, AnomalyStepHours = 1.0 / 6;
        const int AnomalyCacheSize = 512;
        [ThreadStatic] static AnomalyEntry[] anomalyCache;

        static double Interpolate(double[] xs, double[] ys, double x)
        {
            if (!(x > xs[0])) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[^1];
        }

        /// <summary>How far above its own level the field must be for the heaviest hail (standard deviations).</summary>
        const double HailCore = 0.3;
    }
}
