using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, rain: the rain chance and rate, the advected fields, the samples, cloudness
    // and millimetres; moved unchanged from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ rain

        /// <summary>0..0.85: the share of the time it rains or snows, the month's precipitation (mm/day) over how hard it
        /// falls while it does (<see cref="RainRate"/>): over a quarter of the time in a snowy month of 1.5 mm/day at -5 °C
        /// with half the sky clouded, a twelfth in a temperate month of 2 mm/day at 12 °C, a ninth in a tropical month of
        /// 6 mm/day, a third in a monsoon month of 25 mm/day, almost never in a desert's summer.</summary>
        public static double RainChanceFor(double precip, double temp, double cloud, double frequency)
        {
            if (precip <= 0.01) return 0;
            return Math.Clamp(frequency * precip / (24 * RainRate(precip, temp, cloud)), 0, 0.85);
        }

        /// <summary>
        /// mm per hour while it rains or snows, from the month's precipitation (mm/day), sea-level temperature (°C) and mean
        /// cloud cover (0..1; NaN without the reanalysis, then from the precipitation as for the sky): light snow for hours in
        /// frost, harder rain in warm air (9% more per degree up to 10 °C, 5% above; between -20 and 30 °C), harder in wet
        /// months, and lighter under a cloudy climate's grey skies than as a sunny climate's rare downpours. Fitted (least
        /// squares of its logarithm) to the share of routine METAR reports with precipitation falling at the airports next to
        /// 39 GHCN-Daily stations below 1000 m, 2017-2023 (tools/iem_metar_precip.py): 0.22 mm/h at -5 °C and 1.5 mm/day under
        /// half cloud, 1 mm/h at 12 °C and 2 mm/day, 2.3 mm/h at 26 °C and 6 mm/day under two-thirds cloud.
        /// </summary>
        public static double RainRate(double precip, double temp, double cloud)
        {
            if (double.IsNaN(cloud)) cloud = MeanCloudFor(precip);
            double t = Math.Clamp(temp, -20, 30);
            return PortableMath.Exp(RateCold * Math.Min(t, 10) + RateWarm * Math.Max(t - 10, 0) + RatePrecip * PortableMath.Log(precip) - RateCloud * cloud);
        }

        const double RateCold = 0.085, RateWarm = 0.0525, RatePrecip = 0.413, RateCloud = 2.47;

        /// <summary>A month's mean cloud cover from its precipitation alone, where weather.bin has no reanalysis clouds.</summary>
        static double MeanCloudFor(double precip) => 0.08 + 0.8 * (1 - PortableMath.Exp(-precip / 2.5));

        /// <summary>
        /// 0.57..0.9: the wet and dry spells' weight in the rain field (the rest is fronts and showers), by the month's
        /// sea-level temperature: 0.9 in frost (a winter's long snowy and long dry weeks under its weather regimes), 0.57 in
        /// mild months, 0.74 in hot months. With <see cref="RainRate"/> fixing the hours of rain, fitted to the wet days, wet
        /// spells and ten-day dry spells of the GHCN-Daily stations (tools/ghcnd_dryspells.py; WeatherTests rainfreq at the
        /// METAR hours, dryspells): a stronger weight puts the same hours of rain on fewer days.
        /// </summary>
        public static double SpellWeight(double temp) =>
            0.57 + 0.33 * (1 - WeatherNoise.SmoothStep(-2, 9, temp)) + 0.17 * WeatherNoise.SmoothStep(12, 22, temp);

        /// <summary>
        /// (Rules 8, EA-0020) The weather rules from which the rain comes as at the airports and the stations: in events of a
        /// few hours (<see cref="FastShare"/>), through the whole day (<see cref="DiurnalAmplitude"/>), and in warm months as
        /// a few downpours and much light rain (<see cref="RainSkew"/>). Fitted to the routine METAR reports of 61 airports,
        /// 2017-2023 (tools/iem_metar_events.py: the share of the hours and days with precipitation, how long it lasts, when
        /// in the day it falls; WeatherTests events and eventfit) and the wet days (1 mm or more) of the 48 GHCN-Daily stations
        /// with full records (WeatherTests dryspells). The rain hours are rules 5's (the threshold is unchanged).
        /// </summary>

        /// <summary>(Rules 8) The least weight of the shower field (hours) in the rain field; the fronts and wet spells
        /// (days) keep their ratio in the rest. Rules 5-7 gave cool months 0.1-0.2, so their rain fell in events of 5-12 hours
        /// where the airports report 2-4 (log ratio of event length +0.65 below 5 °C, +0.61 at 8-14 °C; warm months, whose
        /// showers had 0.5 already, +0.15). 0.3 is the best for the days with rain (any amount: rms 10.0 → 9.5 points over
        /// 520 airport-months): the airports' events are shorter still (0.4 matches them, +0.0), but in the model shorter
        /// events fall on more days (+9 points at 5-15 °C), where the real ones cluster in a day's showers.</summary>
        public const double FastShare = 0.3;

        /// <summary>(Rules 8) The showers' afternoon peak: how far the rain threshold drops at 15:00 over land for a fully
        /// convective month (rules 5-7: 0.45). The airports report precipitation almost evenly through the day (each quarter
        /// 23-28 % of it, nights included; the afternoon peak only in Florida); with 0.45 the model put 34-39 % of warm months'
        /// rain into the afternoon and 11-14 % into the night; with 0.1, 2-3 points off.</summary>
        public const double DiurnalAmplitude = 0.1;

        /// <summary>For fitting (WeatherTests events): a fixed shower weight and afternoon peak instead of rules 8's (NaN: off).</summary>
        internal double FastOverride = double.NaN, DiurnalOverride = double.NaN;

        /// <summary>0.25..0.75: share of the rain variance in showers rather than fronts; more in warm months and places.</summary>
        static double ConvectiveFor(double temp) => 0.25 + 0.5 * WeatherNoise.SmoothStep(12, 26, temp);

        /// <summary>
        /// A random field carried along by the steering wind, with a standard normal distribution at every place: two copies
        /// half a restart period apart, each carried from its last restart and faded in and out over its period (the copy
        /// restarting is the one that is faded out), each with its own noise every period.
        /// </summary>
        double Advected(long k, double x, double z, double wh, double vx, double vz, double scale, long cells, long cells2, double evolve, double reset)
        {
            double sum = 0, w2 = 0;
            for (int phase = 0; phase < 2; phase++)
            {
                double s = wh / reset + phase * 0.5, window = Math.Floor(s), tau = s - window;
                double w = 1 - Math.Abs(2 * tau - 1);
                if (w <= 1e-6) continue;
                double age = tau * reset;
                double px = x - vx * age, pz = z - vz * age;
                // a fresh field per window and phase, hashed so no key repeats another's (Fbm's second octave uses key + 7919)
                ulong h = WeatherNoise.Hash(k, (long)window, phase, 0x5EED);
                long kk = (long)h;
                // and at its own phase of the lattice: value noise spreads less between the lattice's nodes than at them, so
                // where the wind barely moves the field a place would keep one spread (more or fewer heavy showers and storms
                // than its climate) instead of the field's
                double lx = LatticeX(px, scale, cells) + WeatherNoise.Unit(h, 1), lz = pz / scale + WeatherNoise.Unit(h, 2);
                double n = gauss.Map(Fbm(kk, lx, lz, wh / evolve, cells, cells2));
                sum += w * n;
                w2 += w * w;
            }
            return w2 > 0 ? sum / Math.Sqrt(w2) : 0;
        }

        /// <summary>Two octaves in space, one speed in time (a second octave changing twice as fast made rain flicker on and
        /// off where the field hovers at the threshold). Not WeatherNoise.Fbm, whose weights and time octave differ: the rain
        /// fields' numbers are fixed by the weather rules.</summary>
        static double Fbm(long key, double x, double z, double t, long periodX, long periodX2)
        {
            double a = WeatherNoise.Value(key, x, z, t, periodX);
            double b = WeatherNoise.Value(key + 7919, x * 2 + 0.37, z * 2 + 0.61, t + 0.5, periodX2);
            return (a + 0.4 * b) / 1.4;
        }

        /// <summary>Vanilla's rain cloudness at a place and game time (see WeatherSample.Cloudness). The fast path for the game.</summary>
        public double Cloudness(double x, double z, double hours)
        {
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, false, out _, out _);
            return s.Cloudness;
        }

        /// <summary>The rain at a place and game time: a sample with the month's normals, rain chance, showers, cloudness and
        /// thunder, without the cloud cover, pressure, storm and winds (left 0). All <see cref="RainMmPerHour"/> reads.</summary>
        public WeatherSample RainSample(double x, double z, double hours)
        {
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, false, out _, out _);
            return s;
        }

        /// <summary>The month's normal temperature (sea-level equivalent, °C) at a place and game time: WeatherSample.NormalTemp
        /// without the rest of the weather.</summary>
        public double NormalTemp(double x, double z, double hours)
        {
            Place(x, z, hours, out _, out _, out _, out var n);
            return n.Temp;
        }

        /// <summary>Everything at a place and game time.</summary>
        public WeatherSample Sample(double x, double z, double hours)
        {
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, true, out var n, out var st);
            // the wind from the same place, normals, prevailing wind and storms as the rain (WindAt without gusts)
            s.Wind = Wind(x, z, hours, s.Lat, n, s.Prevailing, st, false);
            return s;
        }

        /// <summary>
        /// Rain cloudness from the field value g (standard normal), the threshold theta where rain starts, and how hard it
        /// rains on average when it does (from the month's mm/day over its rain chance): the clouds gather over the last 0.9
        /// of a standard deviation before the threshold (0..0.5), then precipitation grows with how far g is above it, so
        /// that its mean while raining is that intensity.
        /// </summary>
        static double CloudnessFor(double g, double theta, double precip, double f)
        {
            const double lead = 0.9;
            if (double.IsPositiveInfinity(theta) || g <= theta - lead) return 0;
            if (g < theta) return 0.5 * WeatherNoise.SmoothStep(theta - lead, theta, g);
            double intensity = MeanIntensity(f > 0 ? precip / (24 * f) : 1);
            double tail = 1 - WeatherNoise.Cdf(theta);
            double meanExcess = tail > 1e-9 ? WeatherNoise.Pdf(theta) / tail - theta : 0.3;
            double k = intensity / Math.Max(0.05, meanExcess);
            return Math.Min(1.6, 0.5 + k * (g - theta));
        }

        /// <summary>
        /// (Rules 8) <see cref="CloudnessFor(double, double, double, double)"/> with the rain skewed: the excess x over the
        /// threshold (in its mean, m) taken as (1 - w) x + w x²/E[x²] for the drawn intensity (capped at 1.1 over the threshold's
        /// 0.5), a blend of rules 5's linear rain and its square over the square's mean (both of mean 1 over the normal field's
        /// tail above the threshold). <paramref name="share"/>: this moment's millimetres over the month's mean rate
        /// (<see cref="RainMmPerHour"/>): (1 - w) of rules 7's (the linear share cut at the cap, c = 1.1 over the mean
        /// intensity, mean 1 - e^(-c)) and w of the square's, uncut and scaled to the same mean: the skew moves the rain
        /// between moments, not the month's millimetres, and a downpour's millimetres aren't cut where its drawing is. With w = 0
        /// the same as rules 7.
        /// </summary>
        static double CloudnessFor(double g, double theta, double precip, double f, double w, out double share)
        {
            const double lead = 0.9;
            share = 0;
            if (double.IsPositiveInfinity(theta) || g <= theta - lead) return 0;
            if (g < theta) return 0.5 * WeatherNoise.SmoothStep(theta - lead, theta, g);
            double intensity = MeanIntensity(f > 0 ? precip / (24 * f) : 1);
            double tail = 1 - WeatherNoise.Cdf(theta);
            double meanExcess = tail > 1e-9 ? WeatherNoise.Pdf(theta) / tail - theta : 0.3;
            double x = (g - theta) / Math.Max(0.05, meanExcess);
            double c = 1.1 / intensity;
            if (w < 1e-6)
            {
                share = Math.Min(c, x);
                return Math.Min(1.6, 0.5 + intensity * x);
            }
            // the square over its mean over a normal field's tail above theta: E[(g - theta)²] = 1 - theta·λ + theta² with
            // λ = pdf/tail the tail's mean (2 m² for an exponential tail; 1.65 m² at theta 0.5, where it rains a third of the time)
            double lambda = meanExcess + theta, m = Math.Max(0.05, meanExcess);
            double m2 = Math.Max(0.05 * m * m, 1 - theta * lambda + theta * theta);
            double sq = x * x * m * m / m2;
            double sx = (1 - w) * x + w * sq;
            // the millimetres: rules 7's capped linear share, and the square's cut at SquareMost times its mean (thunderstorms
            // and storms push the field past the normal tail) and scaled by the cut square's mean over that tail, so that both
            // have rules 7's mean, 1 - e^(-c). Over the tail: ∫(g - θ)²φ from θ to u = Φ(u) - Φ(θ) + θφ(θ) - uφ(u)
            // - 2θ(φ(θ) - φ(u)) + θ²(Φ(u) - Φ(θ)), with u where the square reaches the cut.
            double u = theta + Math.Sqrt(SquareMost * m2), cu = WeatherNoise.Cdf(u), ct = WeatherNoise.Cdf(theta);
            double pu = WeatherNoise.Pdf(u), pt = WeatherNoise.Pdf(theta);
            double inner = (cu - ct) * (1 + theta * theta) + theta * pt - u * pu - 2 * theta * (pt - pu);
            double cutMean = tail > 1e-9 ? (inner + SquareMost * m2 * (1 - cu)) / (m2 * tail) : 1;
            share = (1 - w) * Math.Min(c, x) + w * (1 - PortableMath.Exp(-c)) * Math.Min(SquareMost, sq) / Math.Max(0.2, cutMean);
            return Math.Min(1.6, 0.5 + intensity * sx);
        }

        /// <summary>(Rules 8, EA-0020) How skewed the rain is while it rains, by the rain rate's temperature (°C): the weight w of
        /// the excess's square in <see cref="CloudnessFor(double, double, double, double, double, out double)"/>, 0 up to
        /// 18 °C (as before), RainSkewWarm from 26 °C. Warm months' rain comes as a few downpours and much light rain, so
        /// fewer of their rainy days reach 1 mm. The share of the days with any rain (the airports' METAR) that reach 1 mm
        /// (GHCN-Daily) was 0.83 in the model at 24 °C and over against 0.70 (Singapore, Tokyo, Kansas City, Oklahoma City
        /// 0.74-0.85 against 0.61-0.68); with w = 1 there 0.75 (0.5: 0.80); below 18 °C it was right and is kept. The hours
        /// of rain, when it starts and the month's millimetres are rules 7's.</summary>
        public static double RainSkew(double temp) => RainSkewWarm * WeatherNoise.SmoothStep(SkewFrom, SkewFull, temp);
        public const double RainSkewWarm = 1;
        /// <summary>(Rules 8) The most a moment's squared share counts for in the millimetres, in its mean over the normal
        /// tail: thunderstorms and storms push the field past that tail, and uncut they doubled Kansas City's July (3 still
        /// gave +18 %); with 2 the months' millimetres stay within 11 % of rules 7's at Kano, Singapore, Mumbai, Kansas City,
        /// London and Tampa (WeatherTests moisture).</summary>
        const double SquareMost = 2;
        const double SkewFrom = 18, SkewFull = 26;

        /// <summary>Vanilla's precipitation (cloudness - 0.5) on average while it rains, from the month's mm per hour while
        /// it rains.</summary>
        static double MeanIntensity(double mmPerHour) => Math.Clamp(0.15 + 0.22 * mmPerHour, 0.15, 0.95);

        /// <summary>mm per hour falling in a sample of the weather: the month's rate while it rains (its mm/day over its
        /// rain chance), scaled by how hard this moment's rain is against the month's mean, so that a month's samples add up
        /// to its precipitation. A storm's rain in a month that has none counts as 1 mm/h at the mean intensity.</summary>
        public static double RainMmPerHour(in WeatherSample s)
        {
            double p = s.Precipitation;
            if (p <= 0) return 0;
            double rate = s.RainChance > 0 ? s.NormalPrecip / (24 * s.RainChance) : 1;
            // (rules 8) the skewed share, not cut by the drawn intensity's cap
            if (s.HasRainShare) return rate * s.RainShare;
            return rate * p / MeanIntensity(rate);
        }
    }
}
