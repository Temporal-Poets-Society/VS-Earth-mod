using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>The climate drivers that separate vegetation formations, at one place and elevation.</summary>
    public struct ZoneDrivers
    {
        /// <summary>The real lapse rate, °C per metre (the standard atmosphere's 0.0065), by which the climate's sea-level
        /// temperatures cool to the ground's real elevation (TerrainModel.Lapse uses the same number).</summary>
        public const double LapseRate = 0.0065;

        /// <summary>Growing degree days above 5 °C (°C·days per year).</summary>
        public double Gdd5;
        /// <summary>Coldest and warmest monthly means (°C) at the ground.</summary>
        public double TCold, TWarm;
        /// <summary>Yearly mean temperature (°C) at the ground: where short summers and cold winters trade off, as at the
        /// border of the temperate broadleaf and hemiboreal forests, it separates them where neither month alone does.</summary>
        public double TMean;
        /// <summary>Real aridity index, precipitation / potential evapotranspiration (<see cref="SoilFertility.Aridity"/>).</summary>
        public double Aridity;
        /// <summary>0..1: how dry the summers are (1 = Mediterranean; climate.bin).</summary>
        public double DrySummer;
        /// <summary>Months of the year that are dry by Walter and Lieth's climate diagrams (precipitation in mm below twice
        /// the temperature in °C), counted smoothly; months at or below 0 °C don't count. NaN without monthly data.</summary>
        public double DryMonths;

        /// <summary>The drivers at a place: <see cref="From(EarthData.ClimateSample, double)"/> plus the dry months from the
        /// monthly normals (weather.bin) when the data has them.</summary>
        public static ZoneDrivers From(EarthData data, double lat, double lon, double lapse) => From(data, data.SampleClimate(lat, lon), lat, lon, lapse);

        /// <summary><see cref="From(EarthData, double, double, double)"/> with the place's climate already sampled.</summary>
        public static ZoneDrivers From(EarthData data, in EarthData.ClimateSample c, double lat, double lon, double lapse)
        {
            var d = From(c, lapse);
            d.DryMonths = DryMonthsAt(data.Weather, lat, lon, lapse);
            return d;
        }

        /// <summary>Smoothly counted dry months (Walter and Lieth: P mm < 2 T °C) from the monthly normals, cooled by
        /// <paramref name="lapse"/>; NaN without them. A month counts fully at P below 1.5 T and not at all above 2.5 T.</summary>
        public static double DryMonthsAt(WeatherData weather, double lat, double lon, double lapse)
        {
            if (weather == null) return double.NaN;
            // the middle of each month, bilinear in space (per-cell values would draw straight forest edges along the grid)
            Span<double> temp = stackalloc double[12], precip = stackalloc double[12];
            weather.MonthlyNormals(lat, lon, temp, precip);
            double n = 0;
            for (int m = 0; m < 12; m++)
            {
                double t = temp[m] - lapse;
                if (t <= 0) continue;
                double p = precip[m];
                n += Geo.SmoothStep((1.25 - p / (2 * t)) / 0.5);
            }
            return n;
        }

        /// <summary>
        /// The drivers from Earth's climate at sea level (<see cref="EarthData.ClimateSample"/>) cooled by <paramref name="lapse"/>
        /// (°C, the real lapse rate to the ground's elevation). Growing degree days come from a sine through the coldest and
        /// warmest months around the yearly mean, so they need no monthly data.
        /// </summary>
        public static ZoneDrivers From(EarthData.ClimateSample c, double lapse)
        {
            double mean = c.TMean - lapse, cold = c.TCold - lapse, warm = c.TWarm - lapse;
            return new ZoneDrivers
            {
                Gdd5 = GrowingDegreeDays(mean, cold, warm, 5),
                TCold = cold,
                TWarm = warm,
                TMean = mean,
                Aridity = c.Aridity,
                DrySummer = c.DrySummer,
                DryMonths = double.NaN,
            };
        }

        /// <summary>Degree days above <paramref name="baseTemp"/> over a year whose months follow a sine from the coldest to the
        /// warmest month (the yearly mean is not used: shifting the sine to it would carry the peak past the warmest month).</summary>
        public static double GrowingDegreeDays(double mean, double cold, double warm, double baseTemp)
        {
            double amplitude = Math.Max(0, (warm - cold) / 2), centre = (warm + cold) / 2;
            double sum = 0;
            for (int m = 0; m < 12; m++)
            {
                double t = centre + amplitude * Math.Cos(2 * Math.PI * (m + 0.5) / 12);
                if (t > baseTemp) sum += WeatherData.PerMonth(t - baseTemp);
            }
            return sum;
        }
    }

    /// <summary>
    /// Habitats step 1 (docs/research/habitats-2026-11-06, section 1; ranges proposal rule order): the large-scale vegetation
    /// zones from climate alone, as weights so borders blend. Each zone in zones.json is a soft envelope over the drivers
    /// (<see cref="ZoneDrivers"/>); a zone's weight is the product of a smooth step at each of its limits, normalised over
    /// all zones. Elevation needs no zones of its own: the lapse rate lowers the growing degree days and the warmest month,
    /// so mountains pass through the same zones as latitude (montane forest, subalpine forest-tundra, alpine tundra).
    /// Each zone carries densities (forest, shrubs) that replace the single rain curve of the forest and shrub potentials.
    /// </summary>
    public sealed class ClimateZones
    {
        /// <summary>Version of the zone rules (EarthParams.ZoneRules). 2 (8 November 2026, north of Crimea): trees as dense as
        /// the zones' forest says, without vanilla's floor (<see cref="TreeKeep"/>).</summary>
        public const int RulesVersion = 2;
        public const int FileVersion = 1;

        /// <summary>
        /// Vanilla's genTrees tries about 70 trees a chunk and keeps each with max(0.0025, f²), f the forest map over 255. The
        /// floor gives about 0.13 trees a chunk on any fertile ground whatever the forest map says: one every 7 to 8 chunks in
        /// the Kazakh steppe (0.008 without it), dozens in sight on open steppe at 1:10, where the Pontic steppe of 1318 had
        /// none outside its valleys.
        /// </summary>
        public const double VanillaTreeFloor = 0.0025;

        /// <summary>
        /// Zone rules 2: the share of the trees vanilla's floor lets through that a world keeps at forest map value
        /// <paramref name="forest"/> (0..255, as vanilla hands it to the species pick: truncated, so taken at its middle), so
        /// that a try keeps a tree with f² alone. 1 from about 13 up, where f² is above the floor anyway.
        /// </summary>
        public static double TreeKeep(int forest)
        {
            double f = (Math.Max(0, forest) + 0.5) / 255;
            return Math.Min(1, f * f / VanillaTreeFloor);
        }

        public sealed class Zone
        {
            public string Code, Name;
            public double Forest, Shrub;
            /// <summary>The zone's tree mix: a weight per forest-type group (<see cref="RegionalTreePicker.Groups"/>), from
            /// zones.json "treeMixes"; null = every group 1 (no forest type).</summary>
            public float[] Trees;
            /// <summary>Land people could farm (zones.json "arable", default true): the placement catalogue's wild and feral
            /// crops grow only where arable zones hold most of the weight, or in a river's green belt.</summary>
            public bool Arable = true;
            internal Limit[] Limits;
        }

        internal struct Limit
        {
            public int Driver;
            public double Min, Max, Soft;
        }

        static readonly string[] DriverNames = { "gdd5", "tCold", "tWarm", "aridity", "drySummer", "dryMonths", "tMean" };

        public readonly Zone[] Zones;
        /// <summary>Whether the aridity limits are the Penman-Monteith ones (EarthParams.AridityRules 1).</summary>
        public readonly bool Penman;

        /// <param name="penman">Worlds with EarthParams.AridityRules 1, whose aridity index has CHELSA's Penman-Monteith
        /// evapotranspiration (EarthData.ClimateSample.PenmanFactor): an aridity limit's "penman" block, where it has one,
        /// replaces its min, max and soft. Limits without one hold for both indices.</param>
        public ClimateZones(string json, bool penman = false)
        {
            Penman = penman;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            int version = root.GetProperty("version").GetInt32();
            if (version != FileVersion) throw new FormatException("zones.json: version " + version + ", expected " + FileVersion);
            // tree mixes: a weight per forest-type group, unlisted groups 1
            var mixes = new Dictionary<string, float[]>(StringComparer.Ordinal);
            if (root.TryGetProperty("treeMixes", out var tm))
                foreach (var m in tm.EnumerateObject())
                {
                    var mix = new float[RegionalTreePicker.Groups];
                    Array.Fill(mix, 1f);
                    foreach (var g in m.Value.EnumerateObject())
                    {
                        int gi = RegionalTreePicker.GroupIndex(g.Name);
                        if (gi < 0) throw new FormatException("zones.json: tree mix " + m.Name + " has unknown group " + g.Name);
                        float v = g.Value.GetSingle();
                        if (!(v >= 0)) throw new FormatException("zones.json: tree mix " + m.Name + "." + g.Name + " must be >= 0");
                        mix[gi] = v;
                    }
                    mixes[m.Name] = mix;
                }
            var zones = new List<Zone>();
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var z in root.GetProperty("zones").EnumerateArray())
            {
                var zone = new Zone
                {
                    Code = z.GetProperty("code").GetString(),
                    Name = z.GetProperty("name").GetString(),
                    Forest = z.GetProperty("forest").GetDouble(),
                    Shrub = z.GetProperty("shrub").GetDouble(),
                };
                if (z.TryGetProperty("arable", out var ar)) zone.Arable = ar.GetBoolean();
                if (z.TryGetProperty("trees", out var tr))
                {
                    string mixName = tr.GetString();
                    if (!mixes.TryGetValue(mixName, out zone.Trees)) throw new FormatException("zones.json: " + zone.Code + " names unknown tree mix " + mixName);
                }
                if (!codes.Add(zone.Code)) throw new FormatException("zones.json: zone " + zone.Code + " twice");
                if (zone.Forest < 0 || zone.Forest > 1 || zone.Shrub < 0 || zone.Shrub > 1) throw new FormatException("zones.json: " + zone.Code + " densities must be 0..1");
                var limits = new List<Limit>();
                foreach (var l in z.GetProperty("limits").EnumerateObject())
                {
                    int d = Array.IndexOf(DriverNames, l.Name);
                    if (d < 0) throw new FormatException("zones.json: " + zone.Code + " has unknown driver " + l.Name);
                    var v = l.Value;
                    var limit = new Limit
                    {
                        Driver = d,
                        Min = v.TryGetProperty("min", out var mn) ? mn.GetDouble() : double.NegativeInfinity,
                        Max = v.TryGetProperty("max", out var mx) ? mx.GetDouble() : double.PositiveInfinity,
                        Soft = v.GetProperty("soft").GetDouble(),
                    };
                    if (v.TryGetProperty("penman", out var pm))
                    {
                        if (d != 3) throw new FormatException("zones.json: " + zone.Code + "." + l.Name + ": only aridity limits have a penman block");
                        if (penman)
                        {
                            if (pm.TryGetProperty("min", out var pmn)) limit.Min = pmn.GetDouble();
                            if (pm.TryGetProperty("max", out var pmx)) limit.Max = pmx.GetDouble();
                            if (pm.TryGetProperty("soft", out var ps)) limit.Soft = ps.GetDouble();
                        }
                    }
                    if (!(limit.Soft > 0)) throw new FormatException("zones.json: " + zone.Code + "." + l.Name + " needs soft > 0");
                    if (d == 3)
                    {
                        // aridity limits are read as written (an index) and compared in log10, where "soft" is given
                        if (!double.IsInfinity(limit.Min)) limit.Min = Math.Log10(limit.Min);
                        if (!double.IsInfinity(limit.Max)) limit.Max = Math.Log10(limit.Max);
                    }
                    limits.Add(limit);
                }
                zone.Limits = limits.ToArray();
                zones.Add(zone);
            }
            if (zones.Count == 0) throw new FormatException("zones.json: no zones");
            Zones = zones.ToArray();
        }

        public int Count => Zones.Length;

        public int IndexOf(string code) => Array.FindIndex(Zones, z => z.Code == code);

        /// <param name="logAridity"><see cref="LogAridity"/> of the drivers (worked out once for all zones' limits).</param>
        static double Value(in ZoneDrivers d, int driver, double logAridity) => driver switch
        {
            0 => d.Gdd5,
            1 => d.TCold,
            2 => d.TWarm,
            3 => logAridity,
            4 => d.DrySummer,
            5 => d.DryMonths,
            _ => d.TMean,
        };

        /// <summary>The aridity limits compare in log10 (from 1e-4).</summary>
        static double LogAridity(in ZoneDrivers d) => Math.Log10(Math.Max(1e-4, d.Aridity));

        // a logistic step: 0.5 at the limit, 0.98 one "soft" inside it, 0.02 one "soft" outside
        static double Step(double t) => 1 / (1 + Math.Exp(-4 * t));

        double Raw(int i, in ZoneDrivers d, double logAridity)
        {
            double w = 1;
            foreach (var l in Zones[i].Limits)
            {
                double x = Value(d, l.Driver, logAridity);
                if (double.IsNaN(x)) continue;   // a driver the data lacks (dry months without weather.bin) doesn't limit
                if (!double.IsNegativeInfinity(l.Min)) w *= Step((x - l.Min) / l.Soft);
                if (!double.IsPositiveInfinity(l.Max)) w *= Step((l.Max - x) / l.Soft);
            }
            return w;
        }

        /// <summary>The strongest zone's weight before normalising (0..1): near 0 where the drivers fall between every zone's
        /// envelope, a gap in zones.json (tests check land has none).</summary>
        public double RawStrongest(in ZoneDrivers d)
        {
            double best = 0, logAridity = LogAridity(d);
            for (int i = 0; i < Zones.Length; i++) best = Math.Max(best, Raw(i, d, logAridity));
            return best;
        }

        /// <summary>The zones' weights (0..1, summing to 1) at these drivers. <paramref name="weights"/> needs <see cref="Count"/> entries.</summary>
        public void Weights(in ZoneDrivers d, Span<double> weights)
        {
            double sum = 0, logAridity = LogAridity(d);
            for (int i = 0; i < Zones.Length; i++)
            {
                double w = Raw(i, d, logAridity);
                weights[i] = w;
                sum += w;
            }
            if (sum > 0) for (int i = 0; i < Zones.Length; i++) weights[i] /= sum;
            else for (int i = 0; i < Zones.Length; i++) weights[i] = 1.0 / Zones.Length;
        }

        /// <summary>The zone with the largest weight.</summary>
        public int Dominant(in ZoneDrivers d)
        {
            Span<double> w = stackalloc double[Zones.Length];
            Weights(d, w);
            int best = 0;
            for (int i = 1; i < w.Length; i++) if (w[i] > w[best]) best = i;
            return best;
        }

        /// <summary>Forest and shrub density (0..1) at these drivers: the zones' densities by weight.</summary>
        public void Density(in ZoneDrivers d, out double forest, out double shrub)
        {
            Span<double> w = stackalloc double[Zones.Length];
            Weights(d, w);
            forest = shrub = 0;
            for (int i = 0; i < w.Length; i++)
            {
                forest += w[i] * Zones[i].Forest;
                shrub += w[i] * Zones[i].Shrub;
            }
        }

        /// <summary>The weight (0..1) of arable zones at these drivers.</summary>
        public double ArableShare(in ZoneDrivers d)
        {
            Span<double> w = stackalloc double[Zones.Length];
            Weights(d, w);
            double s = 0;
            for (int i = 0; i < w.Length; i++) if (Zones[i].Arable) s += w[i];
            return s;
        }

        /// <summary>
        /// The forest-type weight per tree group (<see cref="RegionalTreePicker.Groups"/> values into <paramref name="f"/>):
        /// the zones' tree mixes by weight. In zone worlds this replaces the coded climate weights of
        /// <see cref="RegionalTreePicker.ForestTypeFactors"/>, so trees and forest density follow the same zones.
        /// </summary>
        public void TreeFactors(in ZoneDrivers d, float[] f)
        {
            Span<double> w = stackalloc double[Zones.Length];
            Weights(d, w);
            Array.Clear(f, 0, RegionalTreePicker.Groups);
            for (int i = 0; i < w.Length; i++)
            {
                var mix = Zones[i].Trees;
                for (int g = 0; g < RegionalTreePicker.Groups; g++) f[g] += (float)(w[i] * (mix == null ? 1f : mix[g]));
            }
        }
    }
}
