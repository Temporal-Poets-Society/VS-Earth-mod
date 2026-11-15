using System;

namespace EarthMap.Core
{
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
    public sealed partial class WeatherModel
    {
        // (EA-0089, review W-19) the model is split by subject: WeatherModel.Circulation, .Rain, .Clouds, .Fog,
        // .Thunder, .Temperature and .Wind; the value types are in WeatherTypes.cs. Moved unchanged.

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

        /// <summary>(Weather rules 7) The ground's heights and the desert coasts' precipitation (Core.WeatherGround, from the
        /// mod's own files, the same on every side). Set before the model is published; without it rules 7 read as 6 there.</summary>
        public volatile WeatherGround Ground;

        public const double MaxStrength = 1.5;

        /// <summary>A number for this model, different for every model in the process: per-thread caches keep it rather than
        /// the model, so a world that is left isn't kept alive by them (CR-D10).</summary>
        public readonly long Id = System.Threading.Interlocked.Increment(ref lastId);
        static long lastId;

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
            // (rules 7, EA-0060) the desert coasts as dry as climate fix 4 has them
            var ground = Settings.Has.Ground ? Ground : null;
            if (ground != null) n.Precip *= ground.PrecipScale(lat, lon);
        }


        /// <summary>
        /// The temperature the rain rate and the wet spells go by (°C): the month's sea-level normal, and from rules 7 that
        /// at the ground, less 6.5 °C/km of the height above <see cref="RateGroundFrom"/> m its cell's temperatures were reduced
        /// from (EA-0021: the rate was fitted at airports below 1000 m with the sea-level normals, so the High Plains', the
        /// Great Basin's, Iran's and the Highveld's cool months rained and snowed on too few days: Denver's January 7 % of the
        /// days against 11, Elko's 12 against 20).
        /// </summary>
        double RateTemp(double lat, double lon, in WeatherData.Normals n)
        {
            var ground = Settings.Has.Ground ? Ground : null;
            if (ground == null) return n.Temp;
            return n.Temp - GroundLapse * Math.Max(0, ground.MetresAt(lat, lon) - RateGroundFrom);
        }

        /// <summary>For fitting: the temperature the rain rate and the spells go by at a place and game time.</summary>
        internal double RateTempAt(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out _, out var n);
            return RateTemp(lat, lon, n);
        }

        /// <summary>Height (m) from which the rain rate reads the ground's temperature (rules 7): about the height of the
        /// airports it was fitted at (three quarters below 250 m, median 70), so they keep their fit. Against GHCN-Daily's wet
        /// days at the nine stations from 850 m up (WeatherTests dryspells): rms 4.0 → 3.0 points, bias -2.7 → 0.</summary>
        public const double RateGroundFrom = 250;
    }
}
