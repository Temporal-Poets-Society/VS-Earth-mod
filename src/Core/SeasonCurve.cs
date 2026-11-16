using System;

namespace EarthMap.Core
{
    /// <summary>
    /// (Decision 0086, seasons rules 1) The live temperature's shape through the year and the day, from one cell's twelve
    /// monthly means, day-night ranges and day-to-day spreads. Pure arithmetic, the same on every side and operating system.
    /// </summary>
    public static partial class SeasonCurve
    {
        public const int Months = 12, StepsPerMonth = 32, TableSteps = Months * StepsPerMonth;

        /// <summary>The month's curve at <paramref name="yearRel"/> (0..1 from the year's start, the game's month the real
        /// one): a periodic cubic spline through the month middles whose average over each month is that month's mean
        /// (tools/season_curve.py; exact to 3e-4 of a degree per degree of mean). <paramref name="means"/> has 12 entries.</summary>
        public static double Mean(ReadOnlySpan<float> means, double yearRel)
        {
            double pos = Wrap(yearRel) * TableSteps;
            int i0 = (int)pos;
            if (i0 >= TableSteps) i0 = TableSteps - 1;
            double f = pos - i0;
            int i1 = i0 + 1 == TableSteps ? 0 : i0 + 1;
            double a = 0, b = 0;
            int r0 = i0 * Months, r1 = i1 * Months;
            for (int j = 0; j < Months; j++)
            {
                a += Kernel[r0 + j] * means[j];
                b += Kernel[r1 + j] * means[j];
            }
            return a + (b - a) * f;
        }

        /// <summary>A month's quantity that must stay positive (the day-night range, the spread) at <paramref name="yearRel"/>:
        /// linear between the months' middles, periodic.</summary>
        public static double Linear(ReadOnlySpan<float> perMonth, double yearRel)
        {
            double m = Wrap(yearRel) * Months - 0.5;
            if (m < 0) m += Months;
            int j0 = (int)m;
            if (j0 >= Months) j0 = Months - 1;
            int j1 = j0 + 1 == Months ? 0 : j0 + 1;
            double f = m - j0;
            return perMonth[j0] + (perMonth[j1] - perMonth[j0]) * f;
        }

        /// <summary>The twelve means' average (the yearly mean the curve swings around).</summary>
        public static double YearMean(ReadOnlySpan<float> means)
        {
            double s = 0;
            for (int j = 0; j < Months; j++) s += means[j];
            return s / Months;
        }

        static double Wrap(double x)
        {
            x -= Math.Floor(x);
            return x >= 1 ? 0 : x;
        }
    }

    /// <summary>(Decision 0086) One place's monthly temperature layer: per month the mean (°C, at sea level), the day-night
    /// range and the day-to-day spread (°C). One per thread: the reader fills it.</summary>
    public sealed class SeasonCell
    {
        public readonly float[] Means = new float[SeasonCurve.Months], Ranges = new float[SeasonCurve.Months], Spreads = new float[SeasonCurve.Months];
    }

    /// <summary>(Decision 0086) The monthly temperature layer as the model reads it (the data side's reader): the cell at a
    /// place, bilinear over the 2' grid; false where it isn't there (no file, or a client whose tile hasn't arrived).
    /// Thread-safe.</summary>
    public interface ISeasonLayer
    {
        bool Sample(double lat, double lon, SeasonCell into);
    }

    /// <summary>(Decision 0086) The data side's reader (<see cref="SeasonData"/>, seasons-1.bin) as the model reads it:
    /// each month's four cells around the place, bilinear between the cell centres as FineTemperature. False where a tile
    /// isn't here (a player's game before the server has sent it).</summary>
    public sealed class SeasonDataLayer : ISeasonLayer
    {
        public readonly SeasonData Data;
        public SeasonDataLayer(SeasonData data) { Data = data; }

        public bool Sample(double lat, double lon, SeasonCell into)
        {
            double fr = (90 - lat) * SeasonData.Res - 0.5, fc = (lon + 180) * SeasonData.Res - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            float wy = (float)(fr - r0), wx = (float)(fc - c0);
            // (Fable's F2) all twelve months of the four cells at once: four tile lookups a reading instead of 48
            Span<float> m = stackalloc float[48], r = stackalloc float[48], s = stackalloc float[48];
            if (!Data.TryMonths4(r0, c0, m, r, s)) return false;
            for (int month = 0; month < SeasonCurve.Months; month++)
            {
                into.Means[month] = Bilinear(m, month, wx, wy);
                into.Ranges[month] = Bilinear(r, month, wx, wy);
                into.Spreads[month] = Bilinear(s, month, wx, wy);
            }
            return true;
        }

        /// <summary>A month's value between the four cells (TryMonths4's layout: cell k's month at k * 12 + month).</summary>
        static float Bilinear(Span<float> v, int month, float wx, float wy)
        {
            float v0 = v[month], v1 = v[12 + month], v2 = v[24 + month], v3 = v[36 + month];
            float top = v0 + (v1 - v0) * wx, bottom = v2 + (v3 - v2) * wx;
            return top + (bottom - top) * wy;
        }
    }

    /// <summary>(Decision 0086, seasons rules 1) The live temperature's year and day at one place.</summary>
    public static class SeasonTemperature
    {
        /// <summary>How far the season and the hour put the temperature from the place's yearly mean (°C): the month's curve
        /// at <paramref name="yearRel"/> less the twelve means' average, plus the day's range times the day's shape at
        /// <paramref name="hour"/> (24-hour clock, the sun's own: up from <paramref name="sunrise"/> to
        /// <paramref name="sunset"/>). <paramref name="spread"/>: the day-to-day spread now (°C), for the spells.</summary>
        public static double Deviation(SeasonCell cell, double yearRel, double hour, double sunrise, double sunset, double noon, out double spread)
        {
            spread = SeasonCurve.Linear(cell.Spreads, yearRel);
            double range = SeasonCurve.Linear(cell.Ranges, yearRel);
            return SeasonCurve.Mean(cell.Means, yearRel) - SeasonCurve.YearMean(cell.Means) + range * DayShape.At(hour, sunrise, sunset, noon);
        }
    }

    /// <summary>
    /// (Decision 0086) The day's temperature shape, Parton and Logan's (1981) as fitted at 22 METAR stations
    /// (tools/fit_day_shape.py, 264 station-months: rms 0.066 of the scaled cycle against vanilla's 0.165): coldest
    /// <see cref="MinAfterSunrise"/> hours after sunrise, a sine to its top <see cref="WarmestAfterNoon"/> (2.09) hours after
    /// the solar noon, an exponential fall after sunset (<see cref="NightDecay"/>) to the next morning's minimum; blended into
    /// a plain cosine as the day or the night gets short (Fable's review F1). <see cref="At"/> gives
    /// the deviation from the day's mean as a share of the day's range: its 24-hour mean is 0 and its maximum minus minimum 1.
    /// </summary>
    public static class DayShape
    {
        /// <summary>Parton and Logan's parameters as fitted: the minimum <see cref="MinAfterSunrise"/> hours after sunrise,
        /// the sine's lag <see cref="TopLag"/> (its half-period is the day's length + 2 × TopLag), the night's decay. The
        /// warmest hour then comes <see cref="WarmestAfterNoon"/> (2.09) hours after the solar noon, whatever the day's
        /// length (sunrise + MinAfterSunrise + (length + 2 × TopLag) / 2).</summary>
        public const double MinAfterSunrise = 0.49, TopLag = 1.60, NightDecay = 2.24;
        public const double WarmestAfterNoon = MinAfterSunrise + TopLag;
        /// <summary>(Fable's review F1) Short days and short nights: the sun's part of the day's range fades, and Parton and
        /// Logan's shape is blended by a smooth step of the shorter of day and night (0 below <see cref="BlendFrom"/> hours,
        /// 1 from <see cref="BlendTo"/>) with a plain cosine, warmest at the same hour; so the shape changes continuously
        /// with the day's length (no switch, no noon spike in the weeks around the polar night). Day lengths are tabulated
        /// every <see cref="LengthStep"/> hours.</summary>
        const double LengthStep = 0.05, BlendFrom = 0.75, BlendTo = 4.0;
        const int Samples = 192;
        static readonly double[] plMean, plRange, mean, range;

        static DayShape()
        {
            int n = (int)Math.Round(24 / LengthStep) + 1;
            plMean = new double[n]; plRange = new double[n]; mean = new double[n]; range = new double[n];
            for (int i = 0; i < n; i++)
            {
                double length = i * LengthStep, s = 0, lo = double.MaxValue, hi = double.MinValue;
                for (int k = 0; k < Samples; k++)
                {
                    double v = Raw((k + 0.5) * 24.0 / Samples, length);
                    s += v; lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                }
                plMean[i] = s / Samples; plRange[i] = Math.Max(hi - lo, 1e-9);
                // the blend's own mean and range (192 samples a day: the extremes within 0.2 % of the formula's)
                s = 0; lo = double.MaxValue; hi = double.MinValue;
                for (int k = 0; k < Samples; k++)
                {
                    double v = Blended((k + 0.5) * 24.0 / Samples, i);
                    s += v; lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                }
                mean[i] = s / Samples; range[i] = Math.Max(hi - lo, 1e-9);
            }
        }

        /// <summary>The deviation from the day's mean at <paramref name="hour"/> (0..24, the same clock as the sun's times)
        /// for a day with the sun up from <paramref name="sunrise"/> to <paramref name="sunset"/> hours, as a share of the
        /// day's range. A sun that doesn't set or rise is passed as sunset - sunrise of 24 or 0, with
        /// <paramref name="noon"/> the hour of its highest point (else the middle of the two is taken).</summary>
        public static double At(double hour, double sunrise, double sunset, double noon = double.NaN)
        {
            double length = sunset - sunrise;
            if (length < 0) length += 24;
            if (double.IsNaN(noon) || (length > 0.001 && length < 23.999)) noon = sunrise + length / 2;
            double t = hour - (noon - 12);       // into the shape's own clock, the solar noon at 12
            t -= 24 * Math.Floor(t / 24);
            int i = (int)Math.Round(length / LengthStep);
            return (Blended(t, i) - mean[i]) / range[i];
        }

        static double Weight(double length)
        {
            double x = Math.Clamp((Math.Min(length, 24 - length) - BlendFrom) / (BlendTo - BlendFrom), 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>The blend at solar hour <paramref name="t"/> for table entry <paramref name="i"/> (before its own
        /// mean and range are taken out): Parton and Logan's shape scaled to mean 0 and range 1, and a cosine of range 1
        /// warmest at noon + WarmestAfterNoon, weighted by the day's and night's length.</summary>
        static double Blended(double t, int i)
        {
            double length = i * LengthStep, w = Weight(length);
            double cosine = 0.5 * Math.Cos(2 * Math.PI * (t - 12 - WarmestAfterNoon) / 24);
            if (w <= 0) return cosine;
            double pl = (Raw(t, length) - plMean[i]) / plRange[i];
            return w * pl + (1 - w) * cosine;
        }

        /// <summary>The raw shape at solar hour <paramref name="t"/> (noon at 12) for a day of <paramref name="length"/>
        /// hours: 0 at its minimum, 1 at the sine's top.</summary>
        static double Raw(double t, double length)
        {
            double rise = 12 - length / 2, set = 12 + length / 2, night = 24 - length;
            double start = rise + MinAfterSunrise, span = length + 2 * TopLag;
            if (t >= start && t <= set) return Math.Sin(Math.PI * (t - start) / span);
            double atSet = Math.Sin(Math.PI * (set - start) / span);
            double since = t > set ? t - set : t + 24 - set;
            double total = night + MinAfterSunrise, e = Math.Exp(-NightDecay);
            return atSet * (Math.Exp(-NightDecay * since / total) - e) / (1 - e);
        }
    }
}
