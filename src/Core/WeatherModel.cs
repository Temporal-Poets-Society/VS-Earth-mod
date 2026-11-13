using System;

namespace EarthMap.Core
{
    /// <summary>Wind at a place: the air moves toward (X, Z) in world axes (+X east, +Z south); the length is the strength in
    /// vanilla's wind units (0 still, 0.15 light breeze, 0.3 medium, 0.6 strong breeze, 1 storm, 1.5 at most). Sea's convention.</summary>
    public readonly struct WeatherWind
    {
        public readonly double X, Z;

        public WeatherWind(double x, double z) { X = x; Z = z; }

        public double Strength => Math.Sqrt(X * X + Z * Z);

        /// <summary>Compass bearing the wind blows from: 0 north, 90 east, 180 south, 270 west.</summary>
        public double FromDegrees => Bearing(-X, -Z);

        public WeatherWind WithStrength(double s)
        {
            double l = Strength;
            return l < 1e-9 ? new WeatherWind(s, 0) : new WeatherWind(X / l * s, Z / l * s);
        }

        /// <summary>Positive = clockwise seen from above (compass direction increases).</summary>
        public WeatherWind Rotated(double radians)
        {
            double c = PortableMath.Cos(radians), s = PortableMath.Sin(radians);
            return new WeatherWind(X * c - Z * s, X * s + Z * c);
        }

        public static WeatherWind operator +(WeatherWind a, WeatherWind b) => new WeatherWind(a.X + b.X, a.Z + b.Z);
        public static WeatherWind operator *(WeatherWind a, double k) => new WeatherWind(a.X * k, a.Z * k);

        /// <summary>Compass bearing of a direction in world axes (north = -Z).</summary>
        public static double Bearing(double x, double z)
        {
            if (x * x + z * z < 1e-18) return 0;
            double deg = PortableMath.Atan2(x, -z) * 180 / Math.PI;
            return deg < 0 ? deg + 360 : deg;
        }

        static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        public static string CompassName(double bearing) => Points[(int)Math.Round((bearing % 360 + 360) % 360 / 22.5) % 16];

        public override string ToString() => $"{Strength:0.00} from {CompassName(FromDegrees)}";
    }

    /// <summary>The weather's settings for one world (from the config; the same on every side).</summary>
    public sealed class WeatherSettings
    {
        /// <summary>Weather hours per game hour: 1 = fronts and storms move and change at their real speed (km/h, with the map's
        /// own kilometres per block), so a rainy morning lasts a morning. Higher: more weather per season, faster changes.</summary>
        public double Pace = 1;
        /// <summary>Multiplies how often it rains everywhere (the real monthly rain chance at 1).</summary>
        public double RainFrequency = 1;
        /// <summary>Multiplies how often storms form (real yearly counts at 1).</summary>
        public double StormFrequency = 1;
        /// <summary>Strength of the day-to-day wind changes on top of the prevailing winds.</summary>
        public double Variability = 1;
        /// <summary>Strength of the gusts.</summary>
        public double Gusts = 1;
        public double DaysPerYear = 108;
        public double HoursPerDay = 24;
        /// <summary>The world's weather rules (EarthParams.WeatherModelRules; EarthWeather.RulesVersion is the newest): what
        /// the model does beyond rules 5. 6 (new worlds from 13 November 2026): the low cloud decks over cold upwelling seas
        /// (EA-0023), hail from Earth's thunderstorms (EA-0019) and the weather's own warm and cold spells, cloud and rain in
        /// the temperature (EA-0025). Worlds made before keep 5.</summary>
        public int Rules = Newest;
        /// <summary>The newest weather rules this build has (EarthWeather.RulesVersion).</summary>
        public const int Newest = 6;
    }

    /// <summary>Everything about the weather at one place and moment (WeatherModel.Sample).</summary>
    public struct WeatherSample
    {
        public double Lat, Lon, YearRel;
        /// <summary>The month's normal precipitation here, mm/day.</summary>
        public double NormalPrecip;
        /// <summary>The month's normal temperature (sea-level equivalent), °C.</summary>
        public double NormalTemp;
        /// <summary>0..1: how much of the time it rains here this month (the climate's rain chance).</summary>
        public double RainChance;
        /// <summary>0..1: share of showers and thunderstorms (vs. long frontal rain).</summary>
        public double Convective;
        /// <summary>Vanilla's "rain cloudness": above 0.5 it rains (precipitation = cloudness - 0.5); 0..0.5 the rain clouds
        /// gather before it starts (WeatherSystemBase.GetRainCloudness).</summary>
        public double Cloudness;
        /// <summary>0..1: how much of the sky has clouds (for the cloud patterns).</summary>
        public double CloudCover;
        /// <summary>Air pressure at sea level, hPa.</summary>
        public double Pressure;
        public WeatherWind Wind, Prevailing;
        /// <summary>The storm whose winds are strongest here, or null.</summary>
        public WeatherStorms.Storm Storm;
        /// <summary>0..1: how strongly a thunderstorm is overhead (0 none; WeatherModel.ThunderAt).</summary>
        public double Thunder;

        public double Precipitation => Math.Max(0, Cloudness - 0.5);
    }

    /// <summary>
    /// The weather of an Earth world as a pure function of (key, world layout, position, game time), the same on the
    /// server and every client, with nothing saved or sent (decision 0017):
    ///
    ///  - The climate: the month's real precipitation, temperature and land (WeatherData) at the place's real latitude
    ///    and longitude.
    ///  - Prevailing winds: the month's mean 10 m wind from the NCEP/NCAR reanalysis (weather.bin v2); without it, laid
    ///    out around the tropical rain band (the ITCZ): trade winds blowing toward it, turning into monsoon westerlies
    ///    where it has moved far from the equator (India, West Africa, northern Australia in their summers), the westerlies
    ///    and polar easterlies beyond the subtropical ridges, weaker over land. Weather systems are steered by the ITCZ
    ///    layout's winds aloft either way.
    ///  - Rain: random fields carried along by the steering winds (fronts and rain areas hundreds of kilometres wide, and
    ///    showers sized for the player, a few hundred blocks, mixed by how showery the climate is), on top of a slow field
    ///    of wet and dry spells 2000 km across that lasts days, so rain comes in runs of wet days with dry weeks between
    ///    them (thunderstorms too, where the air is humid). Each is mapped to a normal distribution, so a threshold taken from the month's rain chance makes it rain
    ///    exactly that share of the time, everywhere: the monsoon's wet summer and dry winter, the Mediterranean's dry
    ///    summer, the desert's rare shower. The rain chance is the month's precipitation over how hard it falls, fitted to
    ///    airport reports: light snow for long hours in frost, short downpours in the heat. Showers over land peak in the
    ///    afternoon. Clouds gather before the rain starts.
    ///  - Storms (WeatherStorms): tropical cyclones and mid-latitude lows that form where and when they do on the Earth,
    ///    travel along the steering winds, and bring their own winds and rain.
    ///
    /// Distances: the large-scale fields and storms are real kilometres converted with the map's blocks per degree; the
    /// showers and gusts are fixed sizes in blocks. Time: weather hours = game hours × Pace.
    /// </summary>
    public sealed class WeatherModel
    {
        public readonly WeatherData Data;
        public readonly EarthGeometry Geo;
        public readonly WeatherSettings Settings;
        public readonly WeatherStorms Storms;
        readonly long key;

        /// <summary>Blocks per real kilometre, north-south.</summary>
        public readonly double BlocksPerKm;

        // the rain fields: lattice cell size (blocks), how fast they change (weather hours per lattice step) and how
        // often their advection restarts (weather hours)
        const double SynopticKm = 800, SynopticEvolve = 40, SynopticReset = 24;
        const double ShowerBlocks = 600, ShowerEvolve = 3, ShowerReset = 4;
        const double DriftKm = 1000, DriftEvolve = 30;
        const double GustBlocks = 300, GustEvolve = 0.12;
        // the wet and dry spells (weather regimes: a blocking high's dry week, a run of wet days under a trough): a slow field
        // 2000 km across that changes over days and drifts at half the steering wind
        const double SpellKm = 2000, SpellEvolve = 120, SpellReset = 96, SpellDrift = 0.5;
        /// <summary>How closely thunderstorms keep to the wet spells where the month's air is humid (Thunder.RainReach 1);
        /// where it is dry they come on their own, the season's dry lightning.</summary>
        public const double ThunderTie = 0.6;
        readonly double synScale, driftScale, spellScale;
        readonly long synCells, showerCells, driftCells, gustCells, synCells2, showerCells2, spellCells, spellCells2;

        static WeatherNoise.Gaussianizer gauss;
        static readonly object gaussLock = new object();

        /// <summary>Hours to add to the calendar's hour of day at x for the local solar time (the sky's longitude when the
        /// sun follows it, AstraTerra), or null: everyone has the calendar's hour.</summary>
        public volatile Func<double, double> LocalHourOffset;

        public const double MaxStrength = 1.5;

        public WeatherModel(WeatherData data, EarthGeometry geo, WeatherSettings settings, long key)
        {
            Data = data;
            Geo = geo;
            Settings = settings;
            this.key = key ^ 0x3EA7E5L;
            BlocksPerKm = geo.BlocksPerDegreeZ / 111.32;
            synScale = SynopticKm * BlocksPerKm;
            driftScale = DriftKm * BlocksPerKm;
            synCells = Cells(synScale);
            synCells2 = synCells * 2;
            showerCells = Cells(ShowerBlocks);
            showerCells2 = showerCells * 2;
            driftCells = Cells(driftScale);
            gustCells = Cells(GustBlocks);
            spellScale = SpellKm * BlocksPerKm;
            spellCells = Cells(spellScale);
            spellCells2 = spellCells * 2;
            lock (gaussLock) gauss ??= new WeatherNoise.Gaussianizer((k, x, z, t) => Fbm(k, x, z, t, 0, 0));
            Storms = new WeatherStorms(this, key);
        }

        // ------------------------------------------------------------------------------------------ place and time

        bool Wraps => Geo.Wraps && Geo.RingLength > 0;

        long Cells(double scale) => Wraps ? Math.Max(1, (long)Math.Round(Geo.RingLength / scale)) : 0;

        double LatticeX(double x, double scale, long cells) => Wraps ? (x - Geo.RingStart) / (Geo.RingLength / cells) : x / scale;

        /// <summary>0..1 through the calendar year (0 = start of January), as the game's calendar counts it.</summary>
        public double YearRel(double hours)
        {
            double days = hours / Settings.HoursPerDay, y = Settings.DaysPerYear;
            return (days - y * Math.Floor(days / y)) / y;
        }

        /// <summary>The place's latitude, longitude and normals at a game time.</summary>
        public void Place(double x, double z, double hours, out double lat, out double lon, out double yearRel, out WeatherData.Normals n)
        {
            var g = Geo.ToGeo(x, z);
            lat = g.Lat;
            lon = g.Lon;
            yearRel = YearRel(hours);
            n = Data.Sample(lat, lon, yearRel);
        }

        // ------------------------------------------------------------------------------------------ prevailing winds

        /// <summary>
        /// Sea's belts beyond the subtropical ridges, by |latitude| (degrees): (latitude, eastward, poleward) in wind units.
        /// Westerlies from the south-west, polar easterlies from the north-east; calm at the ridges and the polar front.
        /// </summary>
        static readonly double[,] Belts =
        {
            { 0, -0.10, 0.00 }, { 5, -0.20, -0.10 }, { 10, -0.30, -0.15 }, { 15, -0.33, -0.14 }, { 22, -0.28, -0.10 },
            { 28, -0.12, -0.03 }, { 32, 0.00, 0.00 }, { 38, 0.20, 0.06 }, { 45, 0.34, 0.10 }, { 52, 0.36, 0.08 },
            { 58, 0.30, 0.06 }, { 64, 0.16, 0.03 }, { 70, 0.00, 0.00 }, { 76, -0.12, -0.04 }, { 84, -0.18, -0.04 }, { 90, -0.10, 0.00 },
        };

        static WeatherWind Belt(double lat)
        {
            double a = Math.Min(90, Math.Abs(lat)), hs = lat >= 0 ? 1 : -1;
            int i = 0;
            while (i < Belts.GetLength(0) - 2 && a > Belts[i + 1, 0]) i++;
            double t = WeatherNoise.SmoothStep(Belts[i, 0], Belts[i + 1, 0], a);
            double east = WeatherNoise.Lerp(Belts[i, 1], Belts[i + 1, 1], t);
            double pole = WeatherNoise.Lerp(Belts[i, 2], Belts[i + 1, 2], t);
            if (hs < 0)
            {
                double forties = 1 + 0.25 * WeatherNoise.Band(a, 36, 42, 55, 62);   // the roaring forties
                east *= forties;
                pole *= forties;
            }
            return new WeatherWind(east, -pole * hs);
        }

        /// <summary>Degrees the extratropical belts and the subtropical ridges sit north of their yearly place: they follow
        /// the ITCZ (the sun) part of the way.</summary>
        static double BeltShift(double itcz) => Math.Clamp(0.5 * (itcz - 4), -8, 8);

        /// <summary>0..1: how much of the tropical (trade wind and monsoon) regime holds at a latitude.</summary>
        static double TropicalWeight(double lat, double itcz)
        {
            double s = BeltShift(itcz), ridgeN = 30 + s, ridgeS = -30 + s;
            return (1 - WeatherNoise.SmoothStep(ridgeN - 4, ridgeN + 3, lat)) * WeatherNoise.SmoothStep(ridgeS - 3, ridgeS + 4, lat);
        }

        /// <summary>
        /// The month's prevailing wind at a latitude, given the ITCZ's latitude there and the land fraction (friction).
        /// Between the subtropical ridges the air flows toward the ITCZ: where that is toward the equator it's turned west
        /// (the trade winds, NE in the north and SE in the south); where the ITCZ has moved far from the equator and the
        /// air flows away from the equator to reach it, the earth's turning makes it a westerly: the SW monsoon of India and
        /// West Africa, the NW monsoon of northern Australia.
        /// </summary>
        public static WeatherWind PrevailingAt(double lat, double itcz, double land)
        {
            double wTrop = TropicalWeight(lat, itcz);
            WeatherWind w = default;
            if (wTrop > 0)
            {
                double s = BeltShift(itcz), d = lat - itcz;
                double ridge = d >= 0 ? 30 + s : -30 + s;
                double span = Math.Max(4, Math.Abs(ridge - itcz));
                double m = 0.31 * WeatherNoise.SmoothStep(0, 6, Math.Abs(d)) * (1 - WeatherNoise.SmoothStep(0.55, 1.0, Math.Abs(d) / span));
                double flowNorth = d >= 0 ? -1 : 1;
                bool equatorward = flowNorth * lat <= 0;
                double east = equatorward ? -0.9 * m : 1.05 * m * WeatherNoise.SmoothStep(0, 6, Math.Abs(lat));
                double north = flowNorth * 0.45 * m;
                w = new WeatherWind(east, -north) * wTrop;
            }
            if (wTrop < 1) w += Belt(lat - 0.5 * BeltShift(itcz)) * (1 - wTrop);
            return w * (1 - 0.35 * Math.Clamp(land, 0, 1));
        }

        /// <summary>The month's prevailing wind at a place (what the wind is on average).</summary>
        public WeatherWind Prevailing(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            return PrevailingFor(lat, Data.ItczLat(lon, yr), n);
        }

        /// <summary>
        /// The month's prevailing wind from the normals: the reanalysis' mean 10 m wind where weather.bin has it (it knows
        /// the East Asian winter monsoon, the winds round the oceans' highs, the katabatic winds off the ice sheets),
        /// otherwise the winds laid out around the ITCZ (<see cref="PrevailingAt"/>).
        /// </summary>
        WeatherWind PrevailingFor(double lat, double itcz, in WeatherData.Normals n) =>
            Data.HasWindsAndClouds ? new WeatherWind(n.WindE / MetersPerSecondPerUnit, -n.WindN / MetersPerSecondPerUnit) : PrevailingAt(lat, itcz, n.Land);

        /// <summary>Metres per second for one vanilla wind unit (vanilla's "storm" ≈ Beaufort 10, Sea's Beaufort scale).</summary>
        public const double MetersPerSecondPerUnit = 25;

        /// <summary>
        /// How fast weather systems move here, km/h (east, north): the winds a few kilometres up, which are the prevailing
        /// winds without the ground's friction, stronger and more westerly in the mid-latitudes.
        /// </summary>
        public static void SteeringKmh(double lat, double itcz, out double east, out double north)
        {
            var p = PrevailingAt(lat, itcz, 0);
            double trop = TropicalWeight(lat, itcz);
            double k = 100 * (1 - 0.3 * trop);
            east = p.X * k + 14 * WeatherNoise.Band(Math.Abs(lat), 26, 36, 62, 72);
            north = -p.Z * k * (1 - 0.85 * trop);
        }

        /// <summary>The steering wind in blocks per weather hour (world axes).</summary>
        void SteeringBlocks(double lat, double itcz, out double vx, out double vz)
        {
            SteeringKmh(lat, itcz, out double east, out double north);
            double cos = Math.Max(0.25, PortableMath.Cos(lat * Math.PI / 180));
            vx = east / (111.32 * cos) * Geo.BlocksPerDegreeX;
            vz = -north / 111.32 * Geo.BlocksPerDegreeZ;
        }

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

        /// <summary>0..1: how much of the sky has clouds at a place and game time (WeatherSample.CloudCover, without the wind).</summary>
        public double CloudCover(double x, double z, double hours)
        {
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, true, out _, out _);
            return s.CloudCover;
        }

        void Rain(double x, double z, double hours, ref WeatherSample s, bool full, out WeatherData.Normals n, out WeatherStorms.Effect st)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out n);
            double itcz = Data.ItczLat(lon, yr);
            s.Lat = lat; s.Lon = lon; s.YearRel = yr;
            s.NormalPrecip = n.Precip;
            s.NormalTemp = n.Temp;
            double f = RainChanceFor(n.Precip, n.Temp, n.Cloud, Settings.RainFrequency);
            double cv = ConvectiveFor(n.Temp);
            s.RainChance = f;
            s.Convective = cv;

            double wh = hours * Settings.Pace;
            SteeringBlocks(lat, itcz, out double vx, out double vz);
            double gs = Advected(key + 1, x, z, wh, vx, vz, synScale, synCells, synCells2, SynopticEvolve, SynopticReset);
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            // the wet and dry spells: rain comes in runs of wet days with dry weeks between them (a dry month's few rainy days
            // together, not a shower every few days), the rain chance unchanged
            double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
            double sw = SpellWeight(n.Temp), rest = Math.Sqrt(1 - sw * sw);
            double g = rest * (Math.Sqrt(1 - cv * cv) * gs + cv * gm) + sw * sp;
            // the large-scale weather: fronts and spells (cloudier and lower pressure in a wet spell)
            double large = rest * gs + sw * sp;

            // showers over land peak in the mid-afternoon
            double hourOfDay = HourOfDay(x, hours);
            double diurnal = 0.45 * cv * Math.Clamp(n.Land, 0, 1) * PortableMath.Cos(2 * Math.PI * (hourOfDay - 15) / 24);

            st = Storms.Influence(lat, lon, hours);
            double theta = f > 0 ? WeatherNoise.InverseCdf(1 - f) - diurnal : double.PositiveInfinity;
            g += st.RainBoost;
            // a storm brings rain even where the month is dry, unless rain is switched off (RainFrequency 0)
            if (st.RainBoost > 0 && f <= 0 && Settings.RainFrequency > 0) theta = 2.5;
            s.Cloudness = CloudnessFor(g, theta, n.Precip, f);

            // a thunderstorm, the top of the shower cells (in a humid month, of the wet spells too): it rains hard under it,
            // unless the air below is so dry that its rain evaporates on the way down (dry lightning), when only its dark
            // cloud is overhead
            double wet = Thunder.RainReach(n.Temp, n.Precip), tie = ThunderTie * wet;
            double gt = tie > 0 ? Math.Sqrt(1 - tie * tie) * gm + tie * sp : gm;
            s.Thunder = gt > 0 ? ThunderFrom(gt, ThunderThreshold(hourOfDay, n, vx, vz, tie)) : 0;
            if (s.Thunder > 0)
            {
                double reach = WeatherNoise.SmoothStep(1 - wet, 1.3 - wet, s.Thunder);
                s.Cloudness = Math.Max(s.Cloudness, reach > 0 ? 0.5 + reach : 0.45);
            }
            if (!full) return;

            // the month's mean cloud cover from the reanalysis (the low cloud decks over cold coasts too), else from its rain
            double baseCover = double.IsNaN(n.Cloud) ? MeanCloudFor(n.Precip) : n.Cloud;
            if (Settings.Rules >= 6) baseCover = LowCloudDeck(baseCover, lat, lon, yr, n);
            double cover = Math.Clamp(baseCover + 0.28 * large + 0.1 * gm + 0.25 * st.RainBoost, 0, 1);
            if (s.Cloudness > 0) cover = Math.Max(cover, Math.Min(1, 0.55 + 0.9 * s.Cloudness));
            s.CloudCover = cover;
            s.Pressure = 1013 - 7 * large - st.PressureDrop;
            s.Storm = st.Storm;
            s.Prevailing = PrevailingFor(lat, itcz, n);
        }

        // (rules 6, EA-0023) the cold-water low cloud decks: how many degrees below the latitude's open-ocean mean the sea
        // must be before a deck forms, how much of the sky it covers for each degree beyond, and the most it covers
        const double DeckStart = 1.0, DeckPerDegree = 0.28, DeckMost = 0.8;

        /// <summary>
        /// (Weather rules 6, EA-0023) The month's cloud cover with the low stratocumulus decks over cold upwelling seas, which
        /// the NCEP/NCAR reanalysis Earth's cover comes from has far too little of (its low cloud off Peru and Namibia 20-27 %
        /// in July where satellites see about 70 %; its total cover there 50-55 % at the deck's peak, near the coast less). A
        /// deck forms where the sea is colder than the open ocean's mean at its latitude (<see cref="WeatherData.ColdWater"/>,
        /// a stand-in for the stable air over a cold sea that Klein and Hartmann (1993) tie the decks to), between about 10°
        /// and 45° of latitude, and the sky is at least that cloudy: 28 % for each degree beyond the first, at most 80 %.
        /// Checked against the satellites' total cover at the decks' seasonal peak (ISCCP, MODIS; about 80 % off Peru and
        /// Namibia, 75 % off California, 60 % off the Canaries, 55 % off Western Australia), WeatherTests lowcloud.
        /// </summary>
        double LowCloudDeck(double cover, double lat, double lon, double yr, in WeatherData.Normals n)
        {
            double cold = Data.ColdWater(lat, lon, yr, n.Temp, n.Land);
            if (!(cold > DeckStart)) return cover;
            double band = WeatherNoise.Band(Math.Abs(lat), 6, 12, 40, 48);
            double deck = Math.Min(DeckMost, DeckPerDegree * (cold - DeckStart)) * band;
            return Math.Max(cover, deck);
        }

        /// <summary>The month's mean cloud cover at a place (the reanalysis', with the cold-water decks from rules 6): what
        /// a sample's cover moves around.</summary>
        public double MonthCover(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double c = double.IsNaN(n.Cloud) ? MeanCloudFor(n.Precip) : n.Cloud;
            return Settings.Rules >= 6 ? LowCloudDeck(c, lat, lon, yr, n) : c;
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
            return rate * p / MeanIntensity(rate);
        }

        // ------------------------------------------------------------------------------------------ thunder

        /// <summary>
        /// Thunder at a place and game time, 0..1: how strongly a thunderstorm is overhead (0 none). Thunderstorms are the
        /// strongest of the shower cells, the same field that brings the showers, so they move with the steering wind. Each
        /// month a place gets the thunder days of its climate (Core.Thunder: warm, wet and over land, fitted to the WWLLN
        /// lightning climatology), more in the afternoon over land. Under a thunderstorm it rains hard, unless the month's air
        /// is so dry that the rain evaporates on the way down (dry lightning, Core.Thunder.RainReach). The same as
        /// WeatherSample.Thunder, without the rest of the weather.
        /// </summary>
        public double ThunderAt(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            SteeringBlocks(lat, Data.ItczLat(lon, yr), out double vx, out double vz);
            double tie = ThunderTie * Thunder.RainReach(n.Temp, n.Precip);
            double theta = ThunderThreshold(HourOfDay(x, hours), n, vx, vz, tie);
            if (double.IsPositiveInfinity(theta)) return 0;
            double wh = hours * Settings.Pace;
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            if (tie > 0)
            {
                double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
                gm = Math.Sqrt(1 - tie * tie) * gm + tie * sp;
            }
            return ThunderFrom(gm, theta);
        }

        /// <summary>
        /// (Weather rules 6, EA-0019) Hail at a place and game time, 0..1 (0 none; 0 in worlds before rules 6, which keep
        /// vanilla's random hail events). Hail falls from the strongest part of a thunderstorm: the share
        /// <see cref="HailShare"/> of its time with the thunder field furthest above its threshold (the field's normal tail),
        /// so it moves with the storms and comes where and when they are strongest. <paramref name="metres"/>: the ground's
        /// height above the sea (the freezing level is over the ground: the High Plains and the Highveld get much hail).
        /// </summary>
        public double HailAt(double x, double z, double hours, double metres = 0)
        {
            if (Settings.Rules < 6) return 0;
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double share = HailShare(n.Temp - GroundLapse * Math.Max(0, metres));
            if (share <= 0) return 0;
            SteeringBlocks(lat, Data.ItczLat(lon, yr), out double vx, out double vz);
            double tie = ThunderTie * Thunder.RainReach(n.Temp, n.Precip);
            double theta = ThunderThreshold(HourOfDay(x, hours), n, vx, vz, tie);
            if (double.IsPositiveInfinity(theta)) return 0;
            double wh = hours * Settings.Pace;
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            if (tie > 0)
            {
                double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
                gm = Math.Sqrt(1 - tie * tie) * gm + tie * sp;
            }
            if (gm <= theta) return 0;
            // the level above which the field spends the hail's share of the time it spends above the thunder threshold
            double level = WeatherNoise.InverseCdf(1 - share * (1 - WeatherNoise.Cdf(theta)));
            return gm <= level ? 0 : WeatherNoise.SmoothStep(level, level + HailCore, gm);
        }

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
            if (Settings.Rules < 6) return 0;
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, false, out var n, out _);
            double range = Data.AnnualRange(s.Lat, s.Lon), ground = n.Temp - GroundLapse * Math.Max(0, metres);
            double sd = SpellSdBase + SpellSdRange * (1 - PortableMath.Exp(-range / SpellSdScale)) * (1 - SpellSdSummer * WeatherNoise.SmoothStep(0, 20, ground));
            SteeringBlocks(s.Lat, Data.ItczLat(s.Lon, s.YearRel), out double vx, out double vz);
            double spell = Advected(key + 53, x, z, hours * Settings.Pace, vx, vz, synScale, synCells, synCells2, TempSpellEvolve, TempSpellReset);
            double wet = WeatherNoise.SmoothStep(0.45, 0.6, s.Cloudness);
            double rain = 0;
            if (wet > 0)
            {
                double day = 0.5 * (1 + PortableMath.Cos(2 * Math.PI * (HourOfDay(x, hours) - 14) / 24));
                double high = Interpolate(WetT, WetHigh, ground), low = Interpolate(WetT, WetLow, ground);
                rain = wet * (low + (high - low) * day);
            }
            return sd * spell + rain;
        }

        static double Interpolate(double[] xs, double[] ys, double x)
        {
            if (!(x > xs[0])) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[^1];
        }

        /// <summary>How far above its own level the field must be for the heaviest hail (standard deviations).</summary>
        const double HailCore = 0.3;

        /// <summary>
        /// (Rules 6) 0..0.09: the share of a thunderstorm's time with hail reaching the ground, by the month's mean temperature
        /// at the ground, a stand-in for how high the freezing level is above it: none in cold months (no storms tall
        /// enough), most at 10-19 °C (the mid-latitude spring and summer: the Great Plains, the Alpine foreland, the Pampas,
        /// the Highveld, northern India before the monsoon), none from 26 °C (the tropics' freezing level near 5 km melts the
        /// hail on the way down). Not fitted to hail records (none on this PC): of the order of the share of thunderstorm
        /// days with hail reports in the mid-latitudes (WeatherTests rules6 lists hail days at stations).
        /// </summary>
        public static double HailShare(double groundTemp) => 0.09 * WeatherNoise.SmoothStep(4, 10, groundTemp) * (1 - WeatherNoise.SmoothStep(19, 26, groundTemp));

        /// <summary>°C the month's mean falls per metre up (the weather's normals are at sea level).</summary>
        public const double GroundLapse = 0.0065;

        /// <summary>0..1: the share of days with thunder at a place in the month of this game time (the climate's, with the
        /// rain frequency).</summary>
        public double ThunderDays(double x, double z, double hours)
        {
            Place(x, z, hours, out _, out _, out _, out var n);
            return 1 - PortableMath.Exp(-Thunder.SpellsPerDay(n.Temp, n.Precip, n.Land) * Settings.RainFrequency);
        }

        /// <summary>How far above its threshold the shower field must be for full thunder (standard deviations).</summary>
        const double ThunderCore = 0.6;

        static double ThunderFrom(double gm, double theta) => gm <= theta ? 0 : WeatherNoise.SmoothStep(theta, theta + ThunderCore, gm);

        /// <summary>The level the thunder field (the shower field, tied to the wet spells by <paramref name="tie"/>) must pass
        /// for thunder at this hour of the day (infinity for none): from the spells a day the month's thunder days need, more
        /// in the afternoon over land, and how fast the field changes here. Storms tied to the wet spells come in runs and
        /// share days, so they need more spells for the same thunder days (1 + tie², measured in WeatherTests thunder).</summary>
        double ThunderThreshold(double hourOfDay, in WeatherData.Normals n, double vx, double vz, double tie)
        {
            double perDay = Thunder.SpellsPerDay(n.Temp, n.Precip, n.Land) * Settings.RainFrequency;
            if (!(perDay > 1e-6)) return double.PositiveInfinity;
            double perHour = (1 + tie * tie) * perDay / Settings.HoursPerDay * (1 + Thunder.Diurnal * Thunder.LandWeight(n.Land) * PortableMath.Cos(2 * Math.PI * (hourOfDay - 16) / 24));
            return Thunder.Threshold(perHour, ShowerRate(vx, vz) * Math.Sqrt(1 - tie * tie));
        }

        /// <summary>The hour of the day (0..24, local solar time when the sky follows the longitude) at x and a game time.</summary>
        double HourOfDay(double x, double hours)
        {
            double h = (hours % Settings.HoursPerDay + Settings.HoursPerDay) % Settings.HoursPerDay * 24 / Settings.HoursPerDay;
            var off = LocalHourOffset;
            return off != null ? h + off(x) : h;
        }

        // how fast the shower field changes at a place (Rice's ω², per weather hour²): from being carried across its cells
        // (cells per hour) and from its own change (lattice steps per hour), measured in WeatherTests (thunderrate)
        const double ShowerAcross = 6.6, ShowerChange = 11.7;

        /// <summary>How fast the shower field changes at a place, radians per game hour (Rice's ω: a smooth field crosses a
        /// level θ upward (ω / 2π)·e^(−θ²/2) times an hour): faster where the steering wind carries the cells by quickly (the
        /// bigger the world, the more blocks it covers an hour) and with the weather's pace.</summary>
        double ShowerRate(double vx, double vz)
        {
            double across = Math.Sqrt(vx * vx + vz * vz) / ShowerBlocks;
            return Settings.Pace * Math.Sqrt(ShowerAcross * across * across + ShowerChange / (ShowerEvolve * ShowerEvolve));
        }

        /// <summary>The shower field (standard normal) at a place and game time, which thunderstorms are the top of, and how
        /// many of its cells the steering wind carries past an hour there. For tests.</summary>
        public double ShowerField(double x, double z, double hours, out double cellsPerHour)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out _);
            double itcz = Data.ItczLat(lon, yr);
            SteeringBlocks(lat, itcz, out double vx, out double vz);
            cellsPerHour = Math.Sqrt(vx * vx + vz * vz) / ShowerBlocks;
            return Advected(key + 2, x, z, hours * Settings.Pace, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
        }

        /// <summary>The weather hours per lattice step of the shower field (WeatherTests fits <see cref="ShowerRate"/>).</summary>
        public const double ShowerEvolveHours = ShowerEvolve;

        // ------------------------------------------------------------------------------------------ wind

        /// <summary>The wind at a place and game time; without gusts it's the steady wind the sea follows.</summary>
        public WeatherWind WindAt(double x, double z, double hours, bool gusts)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double itcz = Data.ItczLat(lon, yr);
            return Wind(x, z, hours, lat, n, PrevailingFor(lat, itcz, n), Storms.Influence(lat, lon, hours), gusts);
        }

        /// <summary>The wind from the place's latitude, normals, prevailing wind and storms (<see cref="Sample"/> has them
        /// from the rain already).</summary>
        WeatherWind Wind(double x, double z, double hours, double lat, in WeatherData.Normals n, WeatherWind prevailing, in WeatherStorms.Effect st, bool gusts)
        {
            var w = prevailing;
            double wh = hours * Settings.Pace;
            w += Drift(x, z, wh, lat) * (1 - 0.35 * Math.Clamp(n.Land, 0, 1));
            if (st.Wind.Strength > 0)
            {
                // a storm's own circulation takes over from the background wind near it
                double take = Math.Min(1, st.Wind.Strength / 0.6) * 0.6;
                w = w * (1 - take) + st.Wind * (1 - 0.3 * Math.Clamp(n.Land, 0, 1));
            }
            if (gusts) w = Gust(w, x, z, wh);
            double l = w.Strength;
            return l > MaxStrength ? w.WithStrength(MaxStrength) : w;
        }

        /// <summary>Day-to-day drift: a smooth random wind over ~1000 km and ~30 weather hours, weaker in the trades.</summary>
        WeatherWind Drift(double x, double z, double wh, double lat)
        {
            if (Settings.Variability <= 0) return default;
            double lx = LatticeX(x, driftScale, driftCells), lz = z / driftScale, lt = wh / DriftEvolve;
            // the monthly mean winds are vectors averaged over the month, weaker than the wind usually is where it turns a lot
            // (the mid-latitudes); the drift is what turns it
            double amp = Settings.Variability * WeatherNoise.Lerp(0.16, 0.46, WeatherNoise.SmoothStep(18, 38, Math.Abs(lat)));
            return new WeatherWind(WeatherNoise.Fbm(key + 11, lx, lz, lt, driftCells) * amp, WeatherNoise.Fbm(key + 12, lx, lz, lt, driftCells) * amp);
        }

        /// <summary>Gusts: ±~20% strength and ±~12° direction, changing over tens of seconds (sized in blocks).</summary>
        WeatherWind Gust(WeatherWind w, double x, double z, double wh)
        {
            if (Settings.Gusts <= 0) return w;
            double lx = LatticeX(x, GustBlocks, gustCells), lz = z / GustBlocks, lt = wh / GustEvolve;
            double gs = WeatherNoise.Fbm(key + 21, lx, lz, lt, gustCells), ga = WeatherNoise.Fbm(key + 22, lx, lz, lt, gustCells);
            double s = w.Strength * (1 + 0.45 * gs * Settings.Gusts);
            return w.WithStrength(Math.Max(0, s)).Rotated(0.35 * ga * Settings.Gusts);
        }
    }
}
