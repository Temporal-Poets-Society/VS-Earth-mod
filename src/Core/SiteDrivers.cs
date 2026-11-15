using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace EarthMap.Core
{
    // Site drivers (habitat proposal, docs/research/habitats-2026-11-06, sections 2, 3a and 4; EA-0054 step 1): the numbers
    // that decide a habitat at one place, game-independent. Heat, light, water supply, soil richness, wetness from the water
    // table (0059) and disturbance. Nothing here changes the world; /earth site prints them and tests/SiteDriversTests
    // checks them. Step 2 (species by site) will read them behind its own rules version.

    /// <summary>
    /// Potential (clear-sky) solar radiation on a sloping surface, FAO-56 conventions (Allen et al. 1998): solar constant
    /// 0.0820 MJ m-2 min-1, declination and inverse relative Earth-Sun distance by eqs 23-24, clear-sky radiation
    /// Rso = (0.75 + 2e-5 z) Ra (eq 37). The tilted surface's extraterrestrial radiation is integrated numerically over the
    /// day: the sun vector against the surface normal, clipped at zero and while the sun is below the horizon. Applying the
    /// eq 37 factor to tilted Ra treats all clear-sky radiation as direct beam (no diffuse or reflected share), which
    /// overstates the contrast between sunny and shady slopes somewhat; the index vs flat ground is the ratio of the two.
    /// Aspect: the direction the slope faces downhill, radians clockwise from north (0 north, pi/2 east, pi south).
    /// </summary>
    public static class SolarRadiation
    {
        public const double Gsc = 0.0820;            // MJ m-2 min-1
        public const int DaysPerYear = 365;
        public const int DefaultSteps = 288;         // 5-minute steps

        public static double InverseDistance(int day) => 1 + 0.033 * Math.Cos(2 * Math.PI * day / DaysPerYear);           // eq 23
        public static double Declination(int day) => 0.409 * Math.Sin(2 * Math.PI * day / DaysPerYear - 1.39);           // eq 24

        /// <summary>Sunset hour angle (eq 25), clamped for polar day (pi) and polar night (0).</summary>
        public static double SunsetHourAngle(double latRad, double decl) => Math.Acos(Math.Clamp(-Math.Tan(latRad) * Math.Tan(decl), -1, 1));

        /// <summary>FAO-56 eq 21: daily extraterrestrial radiation on flat ground (MJ m-2 day-1).</summary>
        public static double DailyRaFlat(double latDeg, int day)
        {
            double phi = latDeg * Math.PI / 180, d = Declination(day), ws = SunsetHourAngle(phi, d);
            return 24 * 60 / Math.PI * Gsc * InverseDistance(day) * (ws * Math.Sin(phi) * Math.Sin(d) + Math.Cos(phi) * Math.Cos(d) * Math.Sin(ws));
        }

        public static double ClearSkyFactor(double elevationM) => 0.75 + 2e-5 * Math.Clamp(elevationM, -500, 9000);

        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (double[] Cos, double[] Sin)> stepTables = new();

        static void Steps(int steps, out double[] cos, out double[] sin)
        {
            if (steps < 4) throw new ArgumentOutOfRangeException(nameof(steps));
            var t = stepTables.GetOrAdd(steps, n =>
            {
                var c = new double[n]; var s = new double[n];
                for (int k = 0; k < n; k++)
                {
                    double w = -Math.PI + (k + .5) * 2 * Math.PI / n;   // hour angle at step midpoints; negative = morning
                    c[k] = Math.Cos(w); s[k] = Math.Sin(w);
                }
                return (c, s);
            });
            cos = t.Cos; sin = t.Sin;
        }

        /// <summary>
        /// Daily extraterrestrial radiation on a surface (MJ m-2 day-1) by midpoint integration over the hour angle.
        /// Local frame east, north, up: sun = (-cos d sin w, sin d cos phi - cos d sin phi cos w, sin d sin phi + cos d cos phi cos w),
        /// normal = (sin s sin a, sin s cos a, cos s).
        /// </summary>
        public static double DailyRa(double latDeg, double slopeRad, double aspectRad, int day, int steps = DefaultSteps)
        {
            Steps(steps, out var cw, out var sw);
            return DailyRa(latDeg * Math.PI / 180, Math.Sin(slopeRad) * Math.Sin(aspectRad), Math.Sin(slopeRad) * Math.Cos(aspectRad), Math.Cos(slopeRad), day, cw, sw);
        }

        static double DailyRa(double phi, double ne, double nn, double nu, int day, double[] cw, double[] sw)
        {
            double d = Declination(day), sd = Math.Sin(d), cd = Math.Cos(d), sp = Math.Sin(phi), cp = Math.Cos(phi);
            double upA = sd * sp, upB = cd * cp, noA = sd * cp, noB = -cd * sp;
            double sum = 0;
            for (int k = 0; k < cw.Length; k++)
            {
                double up = upA + upB * cw[k];
                if (up <= 0) continue;                                   // sun below the horizon
                double ct = ne * (-cd * sw[k]) + nn * (noA + noB * cw[k]) + nu * up;
                if (ct > 0) sum += ct;                                   // surface faces away from the sun
            }
            // minutes per radian of hour angle: 1440 / (2 pi)
            return sum * (2 * Math.PI / cw.Length) * (1440 / (2 * Math.PI)) * Gsc * InverseDistance(day);
        }

        public struct Result
        {
            /// <summary>Clear-sky radiation on the surface (MJ m-2): the year, and the days above 5 °C.</summary>
            public double Annual, Growing;
            /// <summary>The same on flat ground at this latitude and elevation.</summary>
            public double AnnualFlat, GrowingFlat;
            /// <summary>Days counted as growing season.</summary>
            public int GrowingDays;
            public double Index => AnnualFlat > 0 ? Annual / AnnualFlat : 1;
            public double GrowingIndex => GrowingFlat > 0 ? Growing / GrowingFlat : (Growing > 0 ? double.PositiveInfinity : 1);
        }

        /// <summary>
        /// Exact path: every day of the year, <paramref name="steps"/> steps a day, for the surface and for flat ground.
        /// <paramref name="dayTemperature"/> (day 1..365 -> mean daily °C) decides the growing season (above 5 °C); null
        /// counts no growing season. About 2 x 365 x 288 sun positions (~1 ms).
        /// </summary>
        public static Result Clear(double latDeg, double slopeRad, double aspectRad, double elevationM, Func<int, double> dayTemperature, int steps = DefaultSteps)
        {
            Steps(steps, out var cw, out var sw);
            double phi = latDeg * Math.PI / 180, f = ClearSkyFactor(elevationM);
            double ne = Math.Sin(slopeRad) * Math.Sin(aspectRad), nn = Math.Sin(slopeRad) * Math.Cos(aspectRad), nu = Math.Cos(slopeRad);
            var r = new Result();
            for (int day = 1; day <= DaysPerYear; day++)
            {
                double s = f * DailyRa(phi, ne, nn, nu, day, cw, sw), flat = f * DailyRa(phi, 0, 0, 1, day, cw, sw);
                r.Annual += s; r.AnnualFlat += flat;
                if (dayTemperature != null && dayTemperature(day) > Heat.GrowingBase) { r.Growing += s; r.GrowingFlat += flat; r.GrowingDays++; }
            }
            return r;
        }
    }

    /// <summary>
    /// Cheap path for per-column use: a lazily filled table per integer latitude (-90..90) of daily Ra at 24 representative
    /// days (the middle of each half-month), slopes 0-60° every 5° and 36 aspects (10°), 10-minute steps. Lookups
    /// interpolate linearly in latitude, slope and aspect (cyclic); slopes beyond 60° use 60°. The growing season counts
    /// each day above 5 °C exactly and gives it its half-month's Ra. Each latitude costs ~1.5 M sun positions once (a few
    /// ms) and 45 KB. Accuracy against <see cref="SolarRadiation.Clear"/>, checked by tests/SiteDriversTests: annual and
    /// growing-season sums within 0.4 % and the index within 0.002 for |lat| &lt; 75 and slopes up to 45°; within 1 % at
    /// polar latitudes (66-89°). 16 aspects (22.5°) gave up to 2 % on steep shaded slopes.
    /// </summary>
    public static class SolarTable
    {
        public const int Days = 24, Slopes = 13, Aspects = 36, Steps = 144;
        public const double SlopeStepDeg = 5;
        static readonly float[][] rows = new float[181][];
        static readonly int[] repDay = new int[Days];
        static readonly int[] firstDay = new int[Days + 1];

        static SolarTable()
        {
            for (int k = 0; k <= Days; k++) firstDay[k] = 1 + (int)Math.Round(k * (double)SolarRadiation.DaysPerYear / Days);
            for (int k = 0; k < Days; k++) repDay[k] = (firstDay[k] + firstDay[k + 1] - 1) / 2;
        }

        static float[] Row(int lat)
        {
            int i = lat + 90;
            var row = Volatile.Read(ref rows[i]);
            if (row != null) return row;
            row = new float[Slopes * Aspects * Days];
            for (int s = 0; s < Slopes; s++)
                for (int a = 0; a < Aspects; a++)
                    for (int d = 0; d < Days; d++)
                    {
                        if (s == 0 && a > 0) { row[(0 * Aspects + a) * Days + d] = row[d]; continue; }
                        row[(s * Aspects + a) * Days + d] = (float)SolarRadiation.DailyRa(lat, s * SlopeStepDeg * Math.PI / 180, a * 2 * Math.PI / Aspects, repDay[d], Steps);
                    }
            Interlocked.CompareExchange(ref rows[i], row, null);
            return rows[i];
        }

        /// <summary>Interpolated daily Ra (MJ m-2 day-1) for representative day <paramref name="d"/> (0..23).</summary>
        static void Daily(double latDeg, double slopeRad, double aspectRad, double[] into)
        {
            latDeg = Math.Clamp(latDeg, -90, 90);
            int l0 = (int)Math.Floor(latDeg), l1 = Math.Min(90, l0 + 1);
            double tl = latDeg - l0;
            double sd = Math.Clamp(slopeRad * 180 / Math.PI / SlopeStepDeg, 0, Slopes - 1);
            int s0 = Math.Min((int)sd, Slopes - 2); double ts = sd - s0;
            double ad = (aspectRad / (2 * Math.PI) % 1 + 1) % 1 * Aspects;
            int a0 = (int)ad % Aspects, a1 = (a0 + 1) % Aspects; double ta = ad - Math.Floor(ad);
            var r0 = Row(l0); var r1 = Row(l1);
            for (int d = 0; d < Days; d++)
            {
                double V(float[] r, int s, int a) => r[(s * Aspects + a) * Days + d];
                double Lat(float[] r) =>
                    (1 - ts) * ((1 - ta) * V(r, s0, a0) + ta * V(r, s0, a1)) + ts * ((1 - ta) * V(r, s0 + 1, a0) + ta * V(r, s0 + 1, a1));
                into[d] = (1 - tl) * Lat(r0) + tl * Lat(r1);
            }
        }

        /// <summary>Same result as <see cref="SolarRadiation.Clear"/>, from the table.</summary>
        public static SolarRadiation.Result Clear(double latDeg, double slopeRad, double aspectRad, double elevationM, Func<int, double> dayTemperature)
        {
            var s = new double[Days]; var flat = new double[Days];
            Daily(latDeg, slopeRad, aspectRad, s);
            Daily(latDeg, 0, 0, flat);
            double f = SolarRadiation.ClearSkyFactor(elevationM);
            var r = new SolarRadiation.Result();
            for (int d = 0; d < Days; d++)
            {
                int n = firstDay[d + 1] - firstDay[d], warm = 0;
                if (dayTemperature != null)
                    for (int day = firstDay[d]; day < firstDay[d + 1]; day++) if (dayTemperature(day) > Heat.GrowingBase) warm++;
                r.Annual += f * s[d] * n; r.AnnualFlat += f * flat[d] * n;
                r.Growing += f * s[d] * warm; r.GrowingFlat += f * flat[d] * warm; r.GrowingDays += warm;
            }
            return r;
        }
    }

    /// <summary>
    /// Heat. The annual cycle is a sinusoid around the yearly mean, peaking on day 200 in the north and day 20 in the
    /// south, with an amplitude of half the warmest-minus-coldest-month range divided by 0.9886 (the mean of a sinusoid
    /// over its warmest month is 0.9886 of its peak). Daily weather spread is ignored, which slightly understates degree
    /// days where the mean is near the 5 °C base. Real cycles are asymmetric (long flat winters, sharper summers); this
    /// keeps the yearly mean exact and the seasonal range close.
    /// </summary>
    public static class Heat
    {
        public const double GrowingBase = 5;
        const double MonthMean = 0.98862;   // sin(pi/12) / (pi/12)

        public static int WarmestDay(double latDeg) => latDeg >= 0 ? 200 : 20;

        public static double DayTemperature(double mean, double cold, double warm, double latDeg, int day)
            => mean + (warm - cold) / 2 / MonthMean * Math.Cos(2 * Math.PI * (day - WarmestDay(latDeg)) / SolarRadiation.DaysPerYear);

        public static Func<int, double> Cycle(double mean, double cold, double warm, double latDeg)
            => day => DayTemperature(mean, cold, warm, latDeg, day);

        /// <summary>Growing degree days above <paramref name="baseC"/>: the daily sum over the year.</summary>
        public static double DegreeDays(double mean, double cold, double warm, double latDeg, double baseC = GrowingBase)
        {
            double sum = 0;
            for (int day = 1; day <= SolarRadiation.DaysPerYear; day++) sum += Math.Max(0, DayTemperature(mean, cold, warm, latDeg, day) - baseC);
            return sum;
        }
    }

    /// <summary>Soil drivers from a ground-column sample (WRB group and cover recipe of ground-rules-v1), the habitat
    /// prototype's rules (prototype/habitat_proto.py, GROUND).</summary>
    public static class SiteSoil
    {
        public enum Texture { Sand, Loam, Clay, Peat, ShallowOverRock }
        public static readonly string[] TextureNames = { "sand", "loam", "clay", "peat", "shallow over rock" };
        public static readonly string[] RichnessNames = { "A poor", "B moderately poor", "C fertile", "D rich" };
        public static readonly string[] PermeabilityNames = { "tight", "medium", "free-draining" };

        /// <summary>Cover recipes (ground-rules-v1 cover.recipes) of sorted sand: outwash, dunes, sand sheets, beach and marine sand.</summary>
        public static readonly string[] SandCovers = { "outwash", "dune", "dune-on-till", "sand-sheet", "sand-on-saprolite", "marine", "marine-on-till", "coastal", "carbonate-shelf" };
        /// <summary>Wind-blown sand (the prototype's dune cover): very dry where it stands high above the drainage base.</summary>
        public static readonly string[] DuneCovers = { "dune", "dune-on-till", "sand-sheet" };
        public static readonly string[] PeatCovers = { "peat", "peat-on-till" };
        public static readonly string[] ClayCovers = { "lakeclay", "lake", "playa", "pelagic-clay" };

        /// <summary>The prototype took the lighter 40 % of Albeluvisols by clay inside Latvia as B; a fixed stand-in here.</summary>
        public const double AlbeluvisolLightClay = 17;

        /// <summary>A ground-column sample: WRB group (SoilGrids name, null unknown) and cover recipe (null unknown), clay %,
        /// pH (units), Quaternary thickness in metres (NaN unknown).</summary>
        public struct Input
        {
            public bool Known;
            public string Group, Cover;
            public double Clay, Ph, QuaternaryMetres;
            public string GroupName => Group ?? "unknown soil";
            public string CoverName => Cover ?? "unknown";
            public static Input Unknown => new() { Known = false, Clay = double.NaN, Ph = double.NaN, QuaternaryMetres = double.NaN };
            public static Input FromSample(in GroundColumns.Sample s) => new Input
            {
                Known = s.Soil != null || s.Cover != null, Group = s.Soil, Cover = s.Cover,
                Clay = s.Clay, Ph = s.Ph, QuaternaryMetres = s.QuaternaryMetres,
            };
            public bool Is(params string[] groups) => Group != null && Array.IndexOf(groups, Group) >= 0;
            public bool CoverIn(string[] covers) => Cover != null && Array.IndexOf(covers, Cover) >= 0;
        }

        public struct Result
        {
            public bool Known;
            public Texture Texture;
            /// <summary>0 A poor .. 3 D rich.</summary>
            public int Richness;
            /// <summary>0 tight, 1 medium, 2 free-draining.</summary>
            public int Permeability;
            /// <summary>Sandy topsoil for the moisture scale (the prototype's sandy_top: Arenosols, Podzols, Regosols, dune or marine cover).</summary>
            public bool SandyTop;
            public string Describe() => !Known ? "unknown (loam, C fertile, medium permeability assumed)" : string.Format(CultureInfo.InvariantCulture,
                "{0}, richness {1}, {2}", TextureNames[(int)Texture], RichnessNames[Richness], PermeabilityNames[Permeability]);
        }

        public static Result Classify(in Input s)
        {
            if (!s.Known) return new Result { Known = false, Texture = Texture.Loam, Richness = 2, Permeability = 1 };
            double clay = double.IsNaN(s.Clay) ? 0 : s.Clay, ph = double.IsNaN(s.Ph) ? 0 : s.Ph;
            var r = new Result { Known = true };

            // a sandy soil group over a clay cover (Podzols on lake clay in the Baltic) is not free-draining
            bool sandy = s.Is("Arenosols", "Podzols") && !s.CoverIn(ClayCovers) && clay < 25 || s.CoverIn(SandCovers);
            bool tight = s.Is("Stagnosols", "Planosols", "Vertisols", "Solonetz") || s.CoverIn(ClayCovers) || clay >= 30;
            r.Permeability = sandy ? 2 : tight ? 0 : 1;
            r.SandyTop = sandy && (s.Is("Arenosols", "Podzols", "Regosols") || s.CoverIn(DuneCovers) || s.Cover is "marine" or "marine-on-till" or "coastal");

            // richness: the prototype's Baltic rules, widened to the other WRB groups by their base status (A leached and
            // acid, B poor, C moderate, D base-rich)
            r.Richness = 2;
            if (s.Is("Arenosols", "Podzols", "Ferralsols", "Acrisols", "Plinthosols")) r.Richness = 0;
            else if (s.Is("Regosols", "Planosols", "Stagnosols", "Umbrisols", "Alisols", "Lixisols", "Leptosols", "Cryosols", "Durisols", "Solonetz", "Solonchaks", "Gypsisols")) r.Richness = 1;
            else if (s.Is("Albeluvisols")) r.Richness = clay <= AlbeluvisolLightClay ? 1 : 2;
            if (s.Is("Phaeozems", "Chernozems", "Kastanozems", "Fluvisols", "Vertisols", "Nitisols", "Andosols") || s.Is("Luvisols", "Cambisols", "Gleysols", "Calcisols") && (ph >= 6.3 || clay >= 27)) r.Richness = 3;

            // Peat: bog (A) below pH 5, poor fen or transition mire (B) below 6, fen (C) above. Peat holds water whatever
            // lies under it (the cover class can say outwash under a bog), so it is never free-draining (tested in game at Teiči).
            if (s.Is("Histosols") || s.CoverIn(PeatCovers)) { r.Texture = Texture.Peat; r.Richness = ph < 5 ? 0 : ph < 6 ? 1 : 2; r.Permeability = 0; }
            else if (s.QuaternaryMetres < 0.5) r.Texture = Texture.ShallowOverRock;
            else if (sandy) r.Texture = Texture.Sand;
            else if (clay >= 30 || s.Is("Vertisols") || s.CoverIn(ClayCovers)) r.Texture = Texture.Clay;
            else r.Texture = Texture.Loam;
            return r;
        }
    }

    /// <summary>Site wetness: 0 very dry, 1 dry, 2 fresh, 3 moist, 4 wet, 5 waterlogged, 6 flooded, 7 open water.</summary>
    public static class SiteWetness
    {
        public static readonly string[] Names = { "very dry", "dry", "fresh", "moist", "wet", "waterlogged", "flooded", "open water" };
        public const int OpenWater = 7;

        public struct Result
        {
            /// <summary>-1 unknown.</summary>
            public int Class;
            /// <summary>Only a lower bound (from the soil group, with no water table).</summary>
            public bool AtLeast;
            public string Source;
            public string Describe() => Class < 0 ? "unknown (" + Source + ")"
                : (AtLeast ? "at least " : "") + Class + " " + Names[Class] + " (" + Source + ")";
        }

        /// <summary>
        /// The prototype's rules. <paramref name="tableDepthM"/>: the water table's depth below the surface in metres
        /// (negative = above it; NaN = unknown: outside the Earth or the table is off); <paramref name="heightAboveDrainageM"/>
        /// (NaN unknown): the ground's height above its drainage base, which only separates very dry dunes and eskers from dry sand.
        /// </summary>
        public static Result FromTable(double tableDepthM, double heightAboveDrainageM, in SiteSoil.Input soil, in SiteSoil.Result s, bool openWater)
            => FromTable(tableDepthM, heightAboveDrainageM, soil, s, openWater, double.NaN);

        /// <summary>
        /// The same, outside the prototype's humid Baltic: where the table is out of the roots' reach (2 m and deeper) the
        /// climate's water supply sets the ceiling, by the aridity index (UNEP classes): hyper-arid and arid ground is very
        /// dry, semi-arid ground dry (NaN: humid, the prototype's rule alone).
        /// </summary>
        public static Result FromTable(double tableDepthM, double heightAboveDrainageM, in SiteSoil.Input soil, in SiteSoil.Result s, bool openWater, double aridityIndex)
        {
            if (openWater) return new Result { Class = OpenWater, Source = "open water" };
            bool gley = soil.Is("Gleysols");
            bool histo = soil.Is("Histosols") || soil.CoverIn(SiteSoil.PeatCovers);
            if (double.IsNaN(tableDepthM))
            {
                if (histo) return new Result { Class = 5, AtLeast = true, Source = "Histosol or peat; no water table here" };
                if (gley) return new Result { Class = 3, AtLeast = true, Source = "Gleysol; no water table here" };
                return new Result { Class = -1, Source = "no water table here" };
            }
            double d = tableDepthM, hand = heightAboveDrainageM;
            bool veryDry = (soil.Is("Arenosols") || soil.CoverIn(SiteSoil.DuneCovers)) && hand > 3 || soil.Is("Podzols") && hand > 20;
            int m = 2;
            if (d >= 2 && s.SandyTop) m = 1;
            if (d >= 2 && veryDry) m = 0;
            if (d >= 2 && aridityIndex < 0.5) m = Math.Min(m, aridityIndex < 0.2 ? 0 : 1);
            if (d < 2) m = 3;
            if (d < 1) m = 4;
            if (d < 0.3) m = 5;
            if (d < 0) m = 6;
            if (gley) m = Math.Max(m, d < 1.5 ? 4 : 3);
            if (histo) m = Math.Max(m, 5);
            return new Result { Class = m, Source = string.Format(CultureInfo.InvariantCulture, "water table {0:0.0} m down{1}", d,
                double.IsNaN(hand) ? "" : string.Format(CultureInfo.InvariantCulture, ", {0:0.#} m above its drainage base", hand)) };
        }
    }

    /// <summary>Water supply: Earth's VS rainfall (0..255 from the yearly aridity, lowered up to 25 % for dry summers).</summary>
    public static class WaterSupply
    {
        public static readonly string[] AridityNames = { "hyper-arid", "arid", "semi-arid", "dry sub-humid", "humid", "very humid" };
        // tools/bake_earth.py: ln(aridity index) -> VS rain, before the summer-drought and humid-tropics adjustments
        static readonly double[] ai = { 0.01, 0.05, 0.2, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 5.0 };
        static readonly double[] rain = { 0, 15, 50, 95, 115, 130, 150, 170, 200, 235 };

        /// <summary>Approximate aridity index (P / PET) from the rainfall value, undoing the summer-drought reduction. The
        /// humid-tropics raise (up to +60) is not undone, so very wet tropical cells read too humid.</summary>
        public static double AridityIndex(double vsRain, double drySummer)
        {
            double r = vsRain / (1 - 0.25 * Math.Clamp(drySummer, 0, 1));
            if (r <= rain[0]) return ai[0];
            for (int i = 1; i < rain.Length; i++)
                if (r <= rain[i])
                {
                    double t = (r - rain[i - 1]) / (rain[i] - rain[i - 1]);
                    return Math.Exp(Math.Log(ai[i - 1]) + t * (Math.Log(ai[i]) - Math.Log(ai[i - 1])));
                }
            return ai[^1];
        }

        /// <summary>UNEP classes by aridity index: &lt;0.05, &lt;0.2, &lt;0.5, &lt;0.65, &lt;1.5, else very humid.</summary>
        public static int AridityClass(double aridityIndex)
            => aridityIndex < 0.05 ? 0 : aridityIndex < 0.2 ? 1 : aridityIndex < 0.5 ? 2 : aridityIndex < 0.65 ? 3 : aridityIndex < 1.5 ? 4 : 5;
    }

    /// <summary>
    /// Disturbance, coarse. Coast: distance to the sea (shore &lt; 250 m: salt spray and blowing sand, coastal &lt; 2 km,
    /// near the coast &lt; 10 km), exposure exp(-d / 2 km). Flood: height above the nearest river's water, floodplain below
    /// 1.5 m (the prototype's meadow rule), occasional floods below 3 m. Fire: summer drought x low rain x fuel x soil,
    /// 0..1: (0.25 + 0.75 drySummer) x (1 - smoothstep((rain - 60) / 120)) x smoothstep((rain - 20) / 40) x soil
    /// (sand 1, shallow 0.8, loam 0.6, unknown 0.6, clay 0.5, peat 0.4); low &lt; 0.15, moderate &lt; 0.35, else high.
    /// </summary>
    public static class Disturbance
    {
        public struct Result
        {
            public double SeaDistanceM, CoastExposure, HeightAboveRiverM, RiverDistanceM, Fire;
            /// <summary>-1 unknown, 0 none, 1 occasional, 2 floodplain.</summary>
            public int Flood;
            public string CoastClass => double.IsNaN(SeaDistanceM) ? "unknown" : SeaDistanceM < 250 ? "shore" : SeaDistanceM < 2000 ? "coastal" : SeaDistanceM < 10000 ? "near the coast" : "inland";
            public string FloodClass => Flood switch { 2 => "floodplain", 1 => "occasional floods", 0 => "none", _ => "unknown" };
            public string FireClass => Fire < 0.15 ? "low" : Fire < 0.35 ? "moderate" : "high";
        }

        static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

        public static double FireIndex(double vsRain, double drySummer, in SiteSoil.Result soil)
        {
            double s = !soil.Known ? .6 : soil.Texture switch
            {
                SiteSoil.Texture.Sand => 1, SiteSoil.Texture.ShallowOverRock => .8, SiteSoil.Texture.Clay => .5, SiteSoil.Texture.Peat => .4, _ => .6,
            };
            return (0.25 + 0.75 * Math.Clamp(drySummer, 0, 1)) * (1 - Smooth((vsRain - 60) / 120)) * Smooth((vsRain - 20) / 40) * s;
        }

        /// <param name="seaDistanceM">NaN unknown.</param>
        /// <param name="heightAboveRiverM">Height of the surface above the nearest river's water (NaN: no river near).</param>
        public static Result Compute(double seaDistanceM, double heightAboveRiverM, double riverDistanceM, double vsRain, double drySummer, in SiteSoil.Result soil)
        {
            var r = new Result { SeaDistanceM = seaDistanceM, HeightAboveRiverM = heightAboveRiverM, RiverDistanceM = riverDistanceM };
            r.CoastExposure = double.IsNaN(seaDistanceM) ? double.NaN : Math.Exp(-Math.Max(0, seaDistanceM) / 2000);
            r.Flood = double.IsNaN(heightAboveRiverM) ? 0 : heightAboveRiverM < 1.5 ? 2 : heightAboveRiverM < 3 ? 1 : 0;
            r.Fire = FireIndex(vsRain, drySummer, soil);
            return r;
        }
    }

    /// <summary>Everything the site drivers need at one position. NaN marks unknown values.</summary>
    public struct SiteInput
    {
        public double Lat, Lon, ElevationM;
        /// <summary>Yearly mean, coldest and warmest month (°C) at the surface (lapse already applied).</summary>
        public double TMean, TCold, TWarm;
        public double Rain, DrySummer;
        /// <summary>The climate's aridity index (P / PET with Earth's Penman factor); NaN: read back from <see cref="Rain"/>.</summary>
        public double AridityIndex;
        public double SlopeRad, AspectRad;
        public SiteSoil.Input Soil;
        public double WaterTableDepthM, HeightAboveDrainageM;
        public bool OpenWater;
        /// <summary>The mapped wetlands' weighted share here (GLWD v2 through the water table's weights; 0 none, NaN unknown).</summary>
        public double WetlandShare;
        public double SeaDistanceM, HeightAboveRiverM, RiverDistanceM;
        /// <summary>Use <see cref="SolarTable"/> instead of the exact daily integration.</summary>
        public bool FastLight;

        public static SiteInput Create(double lat, double lon) => new()
        {
            Lat = lat, Lon = lon, Soil = SiteSoil.Input.Unknown, AridityIndex = double.NaN, WaterTableDepthM = double.NaN, HeightAboveDrainageM = double.NaN,
            WetlandShare = double.NaN, SeaDistanceM = double.NaN, HeightAboveRiverM = double.NaN, RiverDistanceM = double.NaN,
        };
    }

    /// <summary>The site drivers at one position, as plain numbers.</summary>
    public struct SiteDrivers
    {
        public SiteInput Input;
        public double Gdd5;
        public SolarRadiation.Result Light;
        public double AridityIndex;
        public int AridityClass;
        public SiteSoil.Result Soil;
        public SiteWetness.Result Wetness;
        public Disturbance.Result Disturbance;

        /// <summary>
        /// The site type from wetness and richness, the grid of the Baltic forest-site types (the habitat prototype's
        /// GROUND table): dry poor heath to rich swamp. Open water and unknown wetness have their own names.
        /// </summary>
        public string SiteName => SiteTypeName(Wetness.Class, Soil.Richness, AridityIndex, Gdd5);

        static readonly string[,] siteNames =
        {
            // A poor, B moderately poor, C fertile, D rich
            { "lichen heath", "dry heath", "dry grassland", "dry steppe or calcareous grassland" },         // 0 very dry
            { "dry heath", "dry pine forest (heath)", "dry mixed forest", "dry rich forest" },               // 1 dry
            { "poor fresh forest", "fresh mixed forest", "fresh rich forest", "broadleaf forest" },         // 2 fresh
            { "moist poor forest", "moist mixed forest", "moist rich forest", "moist broadleaf forest" },  // 3 moist
            { "wet heath or bog woodland", "wet poor forest", "wet rich forest (alder, ash)", "rich swamp forest" },  // 4 wet
            { "raised bog", "transition mire", "fen", "rich fen or alder carr" },                            // 5 waterlogged
            { "bog pool", "poor marsh", "marsh", "reed bed" },                                              // 6 flooded
        };

        /// <summary>The grid's names are those of the boreal and temperate forest zone. Where the climate allows no closed
        /// forest the dry and fresh sites are named by the climate instead: below 0.2 aridity desert, below 0.5 steppe or
        /// dry savanna, below 400 degree days above 5 °C tundra (wet sites keep their mire and marsh names).</summary>
        public static string SiteTypeName(int wetness, int richness, double aridityIndex = double.NaN, double gdd5 = double.NaN)
        {
            if (wetness == SiteWetness.OpenWater) return "open water";
            if (wetness < 0 || wetness > 6) return "unknown wetness";
            int rich = Math.Clamp(richness, 0, 3);
            if (wetness <= 3)
            {
                if (aridityIndex < 0.2) return wetness <= 1 ? "desert" : "desert oasis ground";
                if (aridityIndex < 0.5) return wetness <= 1 ? (rich >= 2 ? "steppe or dry savanna" : "dry shrubland") : "moist steppe or savanna woodland";
                if (gdd5 < 400) return wetness <= 1 ? "dry tundra" : "tundra";
            }
            return siteNames[wetness, rich];
        }

        public static SiteDrivers Compute(in SiteInput i)
        {
            var r = new SiteDrivers { Input = i };
            r.Gdd5 = Heat.DegreeDays(i.TMean, i.TCold, i.TWarm, i.Lat);
            var cycle = Heat.Cycle(i.TMean, i.TCold, i.TWarm, i.Lat);
            double elev = double.IsNaN(i.ElevationM) ? 0 : i.ElevationM;
            r.Light = i.FastLight ? SolarTable.Clear(i.Lat, i.SlopeRad, i.AspectRad, elev, cycle) : SolarRadiation.Clear(i.Lat, i.SlopeRad, i.AspectRad, elev, cycle);
            // the climate's own aridity (EarthData.ClimateSample.Aridity, the value soil by aridity, 0048, uses) where given;
            // else read back from the rain value (offline tests)
            r.AridityIndex = double.IsFinite(i.AridityIndex) && i.AridityIndex >= 0 ? i.AridityIndex : WaterSupply.AridityIndex(i.Rain, i.DrySummer);
            r.AridityClass = WaterSupply.AridityClass(r.AridityIndex);
            r.Soil = SiteSoil.Classify(i.Soil);
            r.Wetness = SiteWetness.FromTable(i.WaterTableDepthM, i.HeightAboveDrainageM, i.Soil, r.Soil, i.OpenWater, r.AridityIndex);
            r.Disturbance = Core.Disturbance.Compute(i.SeaDistanceM, i.HeightAboveRiverM, i.RiverDistanceM, i.Rain, i.DrySummer, r.Soil);
            return r;
        }

        static string Dir(double aspectRad)
        {
            string[] n = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return n[(int)Math.Round((aspectRad / (2 * Math.PI) % 1 + 1) % 1 * 8) % 8];
        }

        /// <summary>Multi-line text for chat.</summary>
        public string Describe()
        {
            var i = Input; var c = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            sb.AppendLine(string.Format(c, "Heat: mean {0:0.0} °C, coldest month {1:0.0}, warmest {2:0.0}; GDD>5 {3:0} (sinusoid)", i.TMean, i.TCold, i.TWarm, Gdd5));
            double slopeDeg = i.SlopeRad * 180 / Math.PI;
            sb.AppendLine(string.Format(c, "Light: slope {0:0.0}°{1}; clear-sky {2:0} MJ/m²/yr ({3:0.00} x flat {4:0}), growing season {5:0} MJ/m² over {6} days ({7:0.00} x flat)",
                slopeDeg, slopeDeg >= 0.5 ? string.Format(c, " facing {0} ({1:0}°)", Dir(i.AspectRad), i.AspectRad * 180 / Math.PI) : "",
                Light.Annual, Light.Index, Light.AnnualFlat, Light.Growing, Light.GrowingDays, Light.GrowingDays > 0 ? Light.GrowingIndex : 1));
            sb.AppendLine(string.Format(c, "Water supply: VS rain {0:0}, dry summer {1:0.00}; aridity index ~{2:0.00} ({3})", i.Rain, i.DrySummer, AridityIndex, WaterSupply.AridityNames[AridityClass]));
            sb.AppendLine("Soil: " + (i.Soil.Known ? string.Format(c, "{0} on {1} cover ({2:0.#} m), clay {3:0} %, pH {4:0.0} -> ", i.Soil.GroupName, i.Soil.CoverName, i.Soil.QuaternaryMetres, i.Soil.Clay, i.Soil.Ph) : "") + Soil.Describe());
            sb.AppendLine("Wetness: " + Wetness.Describe() + (i.WetlandShare > 0.005 ? string.Format(c, "; mapped wetland {0:0%}", i.WetlandShare) : ""));
            sb.AppendLine(string.Format(c, "Site: {0} x {1} ({2})", Wetness.Class < 0 ? "?" : Wetness.Class.ToString(c), "ABCD"[Soil.Richness], SiteName));
            if (Wetness.Class >= 0 && Wetness.Class < SiteWetness.OpenWater)
                sb.AppendLine(string.Format(c, "For plants (site rules 1): EIVE moisture {0:0.0}, nutrients {1:0.0}{2}; tree size x{3:0.00}",
                    SiteNiche.Moisture(Wetness.Class, AridityIndex), SiteNiche.Nutrients(Soil.Richness),
                    i.Soil.Known && double.IsFinite(i.Soil.Ph) ? string.Format(c, ", reaction {0:0.0}", SoilNiche.Reaction(i.Soil.Ph)) : "",
                    SiteNiche.Productivity(Wetness.Class, Soil.Richness)));
            var d = Disturbance;
            sb.Append(string.Format(c, "Disturbance: coast {0}{1}; flood {2}{3}; fire {4} ({5:0.00})",
                d.CoastClass, double.IsNaN(d.SeaDistanceM) || double.IsInfinity(d.SeaDistanceM) ? (double.IsInfinity(d.SeaDistanceM) ? " (no sea within 10 km)" : "") : string.Format(c, " (sea ~{0:0} m)", d.SeaDistanceM),
                d.FloodClass, double.IsNaN(d.HeightAboveRiverM) ? "" : string.Format(c, " ({0:0.0} m above a river {1:0} m away)", d.HeightAboveRiverM, d.RiverDistanceM),
                d.FireClass, d.Fire));
            return sb.ToString();
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// EA-0054 step 2, site rules 1 (decision 0080): one column's site as a plant sees it, on the scales of the Ecological
    /// Indicator Values for Europe (EIVE 1.0, 0..10) that the tree catalogue's niches use (<see cref="SoilNiche"/>), and the
    /// productivity that sets how tall a tree grows there. Calibrated on EIVE's own values: beech and oak stand at moisture
    /// 4.6-4.8 (fresh), downy birch 6.3, alder 7.2, the bog plants (cotton grass, Labrador tea, cranberry) 6.9-7.1 and
    /// sedge fens 7.7; nutrients from heather 1.3 and pine 2.7 over beech 4.9 to ash 7.0.
    /// </summary>
    public static class SiteNiche
    {
        /// <summary>Saved in new worlds (EarthParams.SiteRules).</summary>
        public const int RulesVersion = 1;

        /// <summary>EIVE moisture by wetness class (0 very dry .. 6 flooded).</summary>
        public static readonly float[] MoistureByClass = { 2.5f, 3.5f, 4.6f, 5.6f, 6.5f, 7.2f, 8.0f };
        /// <summary>EIVE nutrients by richness (A poor .. D rich).</summary>
        public static readonly float[] NutrientsByRichness = { 2.0f, 3.5f, 5.0f, 6.5f };

        /// <summary>
        /// EIVE moisture of a column. Where the water table is out of the roots' reach (classes 0-2) the climate still
        /// shifts it, by the same 2 units per e-fold of the aridity index as <see cref="SoilNiche.DrainedMoisture"/>,
        /// limited to -2.5 .. +1 (a very humid climate keeps fresh ground moist, but the table decides wet ground).
        /// </summary>
        public static float Moisture(int wetness, double aridityIndex)
        {
            if (wetness < 0) return float.NaN;
            int c = Math.Min(wetness, MoistureByClass.Length - 1);
            float m = MoistureByClass[c];
            // (0: no rain at all, the driest; NaN: unknown, no shift)
            if (c <= 2 && aridityIndex >= 0) m += (float)Math.Clamp(2 * Math.Log(Math.Max(aridityIndex, 0.01)), -2.5, 1.0);
            return Math.Clamp(m, 0.5f, 9.5f);
        }

        /// <summary>From this wetness class (wet: the table within 1 m) a column counts as wet ground for the floodplain
        /// trees (alder, willows), which otherwise need a river or lake beside them.</summary>
        public const int WetGround = 4;

        public static float Nutrients(int richness) => NutrientsByRichness[Math.Clamp(richness, 0, 3)];

        /// <summary>Tree size factor (site index): poor and dry ground grows smaller trees, waterlogged ground stunted ones
        /// (bog pines), fresh to moist rich ground full size.</summary>
        public static float Productivity(int wetness, int richness)
        {
            float w = wetness switch { 0 => 0.75f, 1 => 0.88f, 2 or 3 => 1f, 4 => 0.9f, 5 => 0.65f, 6 => 0.55f, _ => 1f };
            float r = richness switch { 0 => 0.8f, 1 => 0.9f, _ => 1f };
            return w * r;
        }

        /// <summary>One column's site for the plant pickers; <see cref="Ok"/> false where it is unknown.</summary>
        public struct Column
        {
            public bool Ok, OpenWater;
            public sbyte Wetness, Richness;
            public float Moisture, Nutrients, Ph, Productivity;
        }

        /// <summary>The site from the water table and the ground column at a column (NaN depth: unknown).</summary>
        public static Column From(double tableDepthM, double heightAboveBaseM, bool openWater, in SiteSoil.Input soil, double aridityIndex)
        {
            if (double.IsNaN(tableDepthM) && !openWater) return default;
            var s = SiteSoil.Classify(soil);
            var w = SiteWetness.FromTable(tableDepthM, heightAboveBaseM, soil, s, openWater, aridityIndex);
            if (w.Class < 0) return default;
            var c = new Column { Ok = true, OpenWater = w.Class == SiteWetness.OpenWater, Wetness = (sbyte)w.Class, Richness = (sbyte)s.Richness };
            c.Moisture = Moisture(Math.Min(w.Class, 6), aridityIndex);
            c.Nutrients = Nutrients(s.Richness);
            c.Ph = soil.Known && double.IsFinite(soil.Ph) ? (float)soil.Ph : float.NaN;
            c.Productivity = Productivity(Math.Min(w.Class, 6), s.Richness);
            return c;
        }
    }
}
