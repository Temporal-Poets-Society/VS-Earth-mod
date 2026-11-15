using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EarthMap.Core;

static partial class P
{
    static EarthData data;
    static EarthGeometry geo;
    static WeatherGround ground;

    static WeatherModel New(int rules, long key = 12345, Action<WeatherSettings> tweak = null)
    {
        var s = new WeatherSettings { DaysPerYear = 108, HoursPerDay = 24, Rules = rules };
        tweak?.Invoke(s);
        var m = new WeatherModel(data.Weather, geo, s, key);
        m.Ground = ground;
        return m;
    }

    static long B(double d) => BitConverter.DoubleToInt64Bits(d);

    // every output of the model at one query, as bits
    static void Eval(WeatherModel m, double x, double z, double h, double metres, List<long> o)
    {
        var s = m.Sample(x, z, h);
        o.Add(B(s.Cloudness)); o.Add(B(s.CloudCover)); o.Add(B(s.Pressure)); o.Add(B(s.Wind.X)); o.Add(B(s.Wind.Z)); o.Add(B(s.Thunder)); o.Add(B(s.RainShare));
        o.Add(B(m.Cloudness(x, z, h)));
        var w = m.WindAt(x, z, h, true); o.Add(B(w.X)); o.Add(B(w.Z));
        o.Add(B(m.ThunderAt(x, z, h)));
        o.Add(B(m.HailAt(x, z, h, metres)));
        o.Add(B(m.TemperatureAnomaly(x, z, h, metres)));
        var f = m.FogAt(x, z, h, metres); o.Add(B(f.Ground)); o.Add(B(f.Air)); o.Add(f.Sea ? 1 : 0);
        o.Add(B(m.CloudCover(x, z, h)));
        o.Add(B(WeatherModel.RainMmPerHour(m.RainSample(x, z, h))));
    }

    static (double x, double z, double h, double metres)[] Queries(int n, int seed, double hourSpan)
    {
        var r = new Random(seed);
        var q = new (double x, double z, double h, double metres)[n];
        for (int i = 0; i < n; i++)
        {
            double x = geo.RingStart + r.NextDouble() * geo.RingLength, z = geo.EquatorZ + (r.NextDouble() * 2 - 1) * 88 * geo.BlocksPerDegreeZ;
            // a third of the queries near the one before (the anomaly's cells), a third at nearly the same hour
            if (i > 0 && i % 3 == 1) { x = q[i - 1].x + r.Next(-40, 40); z = q[i - 1].z + r.Next(-40, 40); }
            double h = i > 0 && i % 3 == 2 ? q[i - 1].h + r.NextDouble() * 0.3 : r.NextDouble() * hourSpan - 2000;
            q[i] = (x, z, h, r.Next(0, 4) * 900.0);
        }
        return q;
    }

    static int Determinism()
    {
        int bad = 0;
        foreach (int rules in new[] { 5, 6, 7, 8, 9 })
        {
            // hours over 20,000: 1,667 storm slots, over the caches (512 alive, 1024 formed): trims happen
            var q = Queries(4500, 7 + rules, 20000);
            var a = new List<long>[q.Length];
            var m1 = New(rules);
            for (int i = 0; i < q.Length; i++) { a[i] = new List<long>(20); Eval(m1, q[i].x, q[i].z, q[i].h, q[i].metres, a[i]); }
            // 2: a fresh model, the reverse order
            var m2 = New(rules);
            int d2 = 0;
            for (int i = q.Length - 1; i >= 0; i--) { var o = new List<long>(20); Eval(m2, q[i].x, q[i].z, q[i].h, q[i].metres, o); if (!o.SequenceEqual(a[i])) d2++; }
            // 3: the first model again, caches warm, a shuffled order
            int d3 = 0;
            var order = Enumerable.Range(0, q.Length).OrderBy(i => (i * 2654435761L) % 1000003).ToArray();
            foreach (int i in order) { var o = new List<long>(20); Eval(m1, q[i].x, q[i].z, q[i].h, q[i].metres, o); if (!o.SequenceEqual(a[i])) d3++; }
            // 4: 16 threads on one fresh model, interleaved slices
            var m4 = New(rules);
            int d4 = 0;
            Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
            {
                int local = 0;
                for (int i = t; i < q.Length; i += 16) { var o = new List<long>(20); Eval(m4, q[i].x, q[i].z, q[i].h, q[i].metres, o); if (!o.SequenceEqual(a[i])) local++; }
                Interlocked.Add(ref d4, local);
            });
            // 5: two models alternating on one thread (per-thread caches shared by a process's models)
            var m5 = New(rules); var m6 = New(rules, 999);
            int d5 = 0;
            for (int i = 0; i < q.Length; i++)
            {
                var o = new List<long>(20); Eval(m5, q[i].x, q[i].z, q[i].h, q[i].metres, o); if (!o.SequenceEqual(a[i])) d5++;
                var junk = new List<long>(20); Eval(m6, q[i].x, q[i].z, q[i].h, q[i].metres, junk);
            }
            Console.WriteLine($"rules {rules}: {q.Length} queries x 18 outputs; differing: reverse/fresh {d2}, warm/shuffled {d3}, 16 threads {d4}, two models interleaved {d5}");
            bad += d2 + d3 + d4 + d5;
        }
        return bad;
    }

    // the time the game hands the rain hook (totalDays * HoursPerDay, totalDays = TotalHours / (double)24f) against TotalHours
    static void TimeWays()
    {
        var m = New(9);
        var r = new Random(3);
        int bitDiff = 0, n = 200000, hourDiff = 0, rainFlip = 0;
        double maxAbs = 0;
        for (int i = 0; i < n; i++)
        {
            double x = geo.RingStart + r.NextDouble() * geo.RingLength, z = geo.EquatorZ + (r.NextDouble() * 2 - 1) * 80 * geo.BlocksPerDegreeZ;
            long ticks = (long)(r.NextDouble() * 3.6e13);
            double totalHours = TimeSpan.FromTicks(ticks).TotalHours;
            double totalDays = totalHours / (double)24f;
            double h2 = totalDays * 24;
            if (B(h2) != B(totalHours)) hourDiff++;
            double a = m.Cloudness(x, z, totalHours), b = m.Cloudness(x, z, h2);
            if (B(a) != B(b)) bitDiff++;
            if ((a > 0.5) != (b > 0.5)) rainFlip++;
            maxAbs = Math.Max(maxAbs, Math.Abs(a - b));
        }
        Console.WriteLine($"time two ways: of {n}, hours differ in bits {hourDiff}; cloudness bits differ {bitDiff}, largest difference {maxAbs:E2}, rain on/off flips {rainFlip}");
    }

    static void Nan()
    {
        int bad = 0, n = 0;
        var tweaks = new (string, Action<WeatherSettings>)[]
        {
            ("default", s => { }), ("rain 0", s => s.RainFrequency = 0), ("rain 10", s => s.RainFrequency = 10), ("storms 0", s => s.StormFrequency = 0),
            ("storms 20", s => s.StormFrequency = 20), ("pace 0.05", s => s.Pace = 0.05), ("pace 20", s => s.Pace = 20), ("hours/day 2.4", s => s.HoursPerDay = 2.4),
            ("days/year 12", s => s.DaysPerYear = 12), ("days/year 720", s => s.DaysPerYear = 720), ("gusts 5", s => s.Gusts = 5), ("variability 5", s => s.Variability = 5),
            ("rain 0.001", s => s.RainFrequency = 0.001), ("rain -1", s => s.RainFrequency = -1), ("storms -1", s => s.StormFrequency = -1),
        };
        foreach (int rules in new[] { 5, 9 })
            foreach (var (name, tw) in tweaks)
            {
                var m = New(rules, 4242, tw);
                var r = new Random(11);
                int localBad = 0;
                double maxCloud = 0, maxWind = 0, maxAnom = 0, minAnom = 0, maxMm = 0;
                for (int i = 0; i < 12000; i++)
                {
                    double x = geo.RingStart + (r.NextDouble() * 3 - 1) * geo.RingLength, z = geo.EquatorZ + (r.NextDouble() * 2 - 1) * 100 * geo.BlocksPerDegreeZ;
                    double h = i % 5 == 0 ? -r.NextDouble() * 1500 : i % 5 == 1 ? 1e7 + r.NextDouble() * 1500 : r.NextDouble() * 3000;
                    if (i % 97 == 0) z = geo.EquatorZ - 90 * geo.BlocksPerDegreeZ;
                    if (i % 89 == 0) z = geo.EquatorZ;
                    double metres = (i % 4) * 2500.0;
                    var s = m.Sample(x, z, h);
                    var w = m.WindAt(x, z, h, true);
                    double th = m.ThunderAt(x, z, h), ha = m.HailAt(x, z, h, metres), an = m.TemperatureAnomaly(x, z, h, metres), mm = WeatherModel.RainMmPerHour(m.RainSample(x, z, h));
                    var f = m.FogAt(x, z, h, metres);
                    n++;
                    bool ok = double.IsFinite(s.Cloudness) && s.Cloudness >= 0 && s.Cloudness <= 1.6 && double.IsFinite(s.CloudCover) && s.CloudCover >= 0 && s.CloudCover <= 1
                        && double.IsFinite(s.Pressure) && double.IsFinite(w.X) && double.IsFinite(w.Z) && w.Strength <= WeatherModel.MaxStrength + 1e-9 && s.Wind.Strength <= WeatherModel.MaxStrength + 1e-9
                        && th >= 0 && th <= 1 && ha >= 0 && ha <= 1 && double.IsFinite(an) && f.Ground >= 0 && f.Ground <= 1 && f.Air >= 0 && f.Air <= 1 && double.IsFinite(mm) && mm >= 0
                        && double.IsFinite(s.RainShare) && s.RainShare >= 0;
                    if (!ok) { localBad++; if (localBad <= 3) Console.WriteLine($"   BAD rules {rules} {name}: x {x:0} z {z:0} h {h:0.00}: cloudness {s.Cloudness} cover {s.CloudCover} p {s.Pressure} wind {w.X},{w.Z} thunder {th} hail {ha} anomaly {an} fog {f.Ground}/{f.Air} mm {mm} share {s.RainShare}"); }
                    maxCloud = Math.Max(maxCloud, s.Cloudness); maxWind = Math.Max(maxWind, w.Strength); maxAnom = Math.Max(maxAnom, an); minAnom = Math.Min(minAnom, an); maxMm = Math.Max(maxMm, mm);
                }
                Console.WriteLine($"rules {rules} {name,-14}: bad {localBad}; cloudness <= {maxCloud:0.000}, wind <= {maxWind:0.000}, anomaly {minAnom:0.0}..{maxAnom:0.0} C, rain <= {maxMm:0.0} mm/h");
                bad += localBad;
            }
        Console.WriteLine($"nan/range scan: {n} samples, {bad} bad");
    }

    delegate double F(WeatherModel m, double x, double z, double h);

    static readonly (string name, F f)[] Fields =
    {
        ("cloudness", (m, x, z, h) => m.Cloudness(x, z, h)),
        ("cover", (m, x, z, h) => m.CloudCover(x, z, h)),
        ("pressure hPa", (m, x, z, h) => m.Sample(x, z, h).Pressure),
        ("wind X", (m, x, z, h) => m.WindAt(x, z, h, true).X),
        ("wind Z", (m, x, z, h) => m.WindAt(x, z, h, true).Z),
        ("thunder", (m, x, z, h) => m.ThunderAt(x, z, h)),
        ("hail", (m, x, z, h) => m.HailAt(x, z, h, 800)),
        ("anomaly C", (m, x, z, h) => m.TemperatureAnomaly(x, z, h, 300)),
        ("fog ground", (m, x, z, h) => m.FogAt(x, z, h, 100).Ground),
        ("mm/h", (m, x, z, h) => WeatherModel.RainMmPerHour(m.RainSample(x, z, h))),
    };

    // jumps across moments: f(t - eps) against f(t + eps) at many places, against the same step at an ordinary moment
    static void TimeEdges(int rules)
    {
        var m = New(rules);
        var r = new Random(5);
        var places = new (double x, double z)[3000];
        for (int i = 0; i < places.Length; i++) places[i] = (geo.RingStart + r.NextDouble() * geo.RingLength, geo.EquatorZ + (r.NextDouble() * 2 - 1) * 75 * geo.BlocksPerDegreeZ);
        const double eps = 1e-6;
        double year = 108 * 24;
        var moments = new (string, double)[]
        {
            ("ordinary moment", 5000.37), ("shower restart (4 h)", 5000), ("shower half restart (2 h)", 5002), ("front restart (24 h)", 5016), ("front half (12 h)", 5028),
            ("spell restart (96 h)", 5088), ("spell half (48 h)", 5040), ("storm slot (12 h)", 5004),
            ("year wrap", 2 * year), ("month edge", 2 * year + 9 * 24), ("mid-month", 2 * year + 4.5 * 24), ("time 0", 0), ("negative time", -1000.0), ("anomaly step (1/6 h)", 5000 + 7.0 / 6),
        };
        Console.WriteLine($"time edges, rules {rules}: the largest |f(t+eps) - f(t-eps)| over {places.Length} places, eps {eps} h");
        Console.Write($"{"",-28}");
        foreach (var (n, _) in Fields) Console.Write($"{n,13}");
        Console.WriteLine();
        foreach (var (name, t) in moments)
        {
            Console.Write($"{name,-28}");
            foreach (var (_, f) in Fields)
            {
                double mx = 0;
                foreach (var (x, z) in places) mx = Math.Max(mx, Math.Abs(f(m, x, z, t + eps) - f(m, x, z, t - eps)));
                Console.Write($"{mx,13:E2}");
            }
            Console.WriteLine();
        }
    }

    // jumps across lines in space
    static void SpaceEdges(int rules)
    {
        var m = New(rules);
        const double eps = 1e-4;
        double ringEnd = geo.RingStart + geo.RingLength;
        double Zr(Random q) => geo.EquatorZ + (q.NextDouble() * 2 - 1) * 75 * geo.BlocksPerDegreeZ;
        double Xr(Random q) => geo.RingStart + q.NextDouble() * geo.RingLength;
        var lines = new (string, Func<Random, (double x1, double z1, double x2, double z2)>)[]
        {
            ("ordinary place (2 eps apart)", q => { double x = Xr(q), z = Zr(q); return (x - eps, z, x + eps, z); }),
            ("the ring's jump line", q => { double z = Zr(q); return (ringEnd - eps, z, geo.RingStart + eps, z); }),
            ("x and x + ring length", q => { double x = Xr(q), z = Zr(q); return (x, z, x + geo.RingLength, z); }),
            ("lon 180", q => { geo.FromGeo(0, 180, out double x, out _); double z = Zr(q); return (x - eps, z, x + eps, z); }),
            ("lon 0", q => { geo.FromGeo(0, 0, out double x, out _); double z = Zr(q); return (x - eps, z, x + eps, z); }),
            ("the equator", q => { double x = Xr(q); return (x, geo.EquatorZ - eps, x, geo.EquatorZ + eps); }),
            ("the north pole (z beyond)", q => { double x = Xr(q), zp = geo.EquatorZ - 90 * geo.BlocksPerDegreeZ; return (x, zp - eps, x, zp + eps); }),
            ("lat 70 (storms' edge)", q => { double x = Xr(q), zp = geo.EquatorZ - 70 * geo.BlocksPerDegreeZ; return (x, zp - eps, x, zp + eps); }),
            ("lat 4 (cyclones' edge)", q => { double x = Xr(q), zp = geo.EquatorZ - 4 * geo.BlocksPerDegreeZ; return (x, zp - eps, x, zp + eps); }),
            ("a 32-block cell edge (x)", q => { double x = Math.Floor(Xr(q) / 32) * 32, z = Zr(q); return (x - eps, z, x + eps, z); }),
            ("a 600-block lattice edge", q => { double x = geo.RingStart + Math.Floor(q.NextDouble() * geo.RingLength / 600) * 600, z = Math.Floor(Zr(q) / 600) * 600; return (x - eps, z - eps, x + eps, z + eps); }),
        };
        Console.WriteLine($"space edges, rules {rules}: the largest |f(a) - f(b)| over 3000 pairs and 3 hours each");
        Console.Write($"{"",-30}");
        foreach (var (n, _) in Fields) Console.Write($"{n,13}");
        Console.WriteLine();
        foreach (var (name, pair) in lines)
        {
            var q = new Random(77);
            var pairs = Enumerable.Range(0, 3000).Select(_ => pair(q)).ToArray();
            Console.Write($"{name,-30}");
            foreach (var (_, f) in Fields)
            {
                double mx = 0;
                foreach (var (x1, z1, x2, z2) in pairs)
                    foreach (double h in new[] { 5000.37, 7013.2, 9411.9 })
                        mx = Math.Max(mx, Math.Abs(f(m, x1, z1, h) - f(m, x2, z2, h)));
                Console.Write($"{mx,13:E2}");
            }
            Console.WriteLine();
        }
    }

    // a storm's outer edge: its rain boost just inside against just outside (0)
    static void StormEdge(int rules)
    {
        var m = New(rules);
        double worstBoost = 0, worstCloud = 0, worstCover = 0; string where = "", whereCloud = "";
        int storms = 0, edges = 0, rainEdges = 0, cloudEdges = 0;
        var hist = new int[8];
        for (double h = 3000; h < 3000 + 108 * 24; h += 6)
        {
            double wh = h * m.Settings.Pace;
            foreach (var s in m.Storms.AliveIn(wh))
            {
                if (!s.AliveAt(wh)) continue;
                s.At(wh, out double la, out double lo, out double inten);
                if (inten < 0.2 || Math.Abs(la) > 80) continue;
                storms++;
                for (int k = 0; k < 24; k++)
                {
                    double a = k * Math.PI / 12, R = s.RadiusKm;
                    double dn = R * Math.Sin(a), lat2 = la + dn / 111.32;
                    double de = R * Math.Cos(a), lon2 = lo + de / (111.32 * PortableMath.Cos((lat2 + la) * Math.PI / 360));
                    if (Math.Abs(lat2) > 85) continue;
                    double lat1 = la + (lat2 - la) * 0.9995, lon1 = lo + (lon2 - lo) * 0.9995, lat3 = la + (lat2 - la) * 1.0005, lon3 = lo + (lon2 - lo) * 1.0005;
                    var inside = m.Storms.Influence(lat1, Geo.Wrap180(lon1), h); var outside = m.Storms.Influence(lat3, Geo.Wrap180(lon3), h);
                    double jump = inside.RainBoost - outside.RainBoost;
                    geo.FromGeo(lat1, Geo.Wrap180(lon1), out double x1, out double z1); geo.FromGeo(lat3, Geo.Wrap180(lon3), out double x3, out double z3);
                    double c1 = m.Cloudness(x1, z1, h), c3 = m.Cloudness(x3, z3, h);
                    double v1 = m.CloudCover(x1, z1, h), v3 = m.CloudCover(x3, z3, h);
                    edges++;
                    hist[Math.Min(7, (int)(Math.Abs(jump) * 10))]++;
                    if ((c1 > 0.5) != (c3 > 0.5)) rainEdges++;
                    if ((c1 > 0) != (c3 > 0)) cloudEdges++;
                    string desc = $"{s.Name} ({(s.Tropical ? "tropical" : "low")}, R {R:0} km, strength {inten:0.00}) centre {la:0.0},{lo:0.0}, bearing {k * 15} deg from east, hour {h:0}: boost {inside.RainBoost:0.000} -> {outside.RainBoost:0.000}, cloudness {c1:0.000} -> {c3:0.000}, cover {v1:0.000} -> {v3:0.000} over {Math.Sqrt((x3 - x1) * (x3 - x1) + (z3 - z1) * (z3 - z1)):0.0} blocks";
                    if (Math.Abs(jump) > worstBoost) { worstBoost = Math.Abs(jump); where = desc; }
                    if (Math.Abs(c1 - c3) > worstCloud) { worstCloud = Math.Abs(c1 - c3); whereCloud = desc; }
                    worstCover = Math.Max(worstCover, Math.Abs(v1 - v3));
                }
            }
        }
        Console.WriteLine($"storm edges, rules {rules}: {storms} storm-moments, {edges} edge points (0.05 % of the radius inside against outside)");
        Console.WriteLine($"  rain boost jump histogram (0.1 sd bins, last 0.7+): {string.Join(" ", hist)}");
        Console.WriteLine($"  largest boost jump {worstBoost:0.000} sd; largest cloudness jump {worstCloud:0.000}; largest cover jump {worstCover:0.000}; rain on one side only at {rainEdges}, any rain cloud on one side only at {cloudEdges} of {edges}");
        Console.WriteLine($"  worst boost: {where}");
        Console.WriteLine($"  worst cloudness: {whereCloud}");
    }

    // the cold-sea deck at the land 0.5 contour: cover and sea fog along a line across the coast
    static void Deck(int rules)
    {
        var m = New(rules);
        foreach (var (name, lat, lon0, lon1) in new[] { ("Peru 12 S", -12.0, -79.0, -75.5), ("Namibia 23 S", -23.0, 12.5, 16.0), ("California 36 N", 36.0, -124.0, -120.0) })
        {
            double worstCover = 0, worstFog = 0, wcLon = 0, wfLon = 0; double coverA = 0, coverB = 0, fogA = 0, fogB = 0, landAt = 0, wfH = 0;
            for (int month = 0; month < 12; month++)
                for (int day = 0; day < 9; day += 2)
                {
                    double h = (108 * 2 + month * 9 + day) * 24 + 7;
                    double pc = double.NaN, pf = double.NaN;
                    for (double lon = lon0; lon <= lon1; lon += 0.001)
                    {
                        geo.FromGeo(lat, lon, out double x, out double z);
                        double c = m.MonthCover(x, z, h), f = m.FogAt(x, z, h, 0).Ground;
                        if (!double.IsNaN(pc))
                        {
                            if (Math.Abs(c - pc) > worstCover) { worstCover = Math.Abs(c - pc); wcLon = lon; coverA = pc; coverB = c; landAt = data.Weather.Sample(lat, lon, m.YearRel(h)).Land; }
                            if (Math.Abs(f - pf) > worstFog) { worstFog = Math.Abs(f - pf); wfLon = lon; fogA = pf; fogB = f; wfH = h; }
                        }
                        pc = c; pf = f;
                    }
                }
            double blocks = 0.001 * geo.BlocksPerDegreeX;
            Console.WriteLine($"deck {name}, rules {rules}: largest step between places {blocks:0.00} blocks apart: month cover {coverA:0.000} -> {coverB:0.000} at lon {wcLon:0.000} (land share {landAt:0.000}); sea fog {fogA:0.000} -> {fogB:0.000} at lon {wfLon:0.000}, hour {wfH:0}");
        }
    }

    // the dry-month line: precipitation 0.01 mm/day, where the rain chance steps from 0
    static void DryLine(int rules)
    {
        var m = New(rules);
        var r = new Random(9);
        int found = 0; double worst = 0; string where = "";
        int flips = 0, pairs = 0;
        for (int i = 0; i < 2000000 && found < 3000; i++)
        {
            double lat = (r.NextDouble() * 2 - 1) * 40, lon = r.NextDouble() * 360 - 180, yr = r.NextDouble();
            double hours0 = yr * 108 * 24;
            geo.FromGeo(lat, lon, out double x0, out double z0); geo.FromGeo(lat, lon + 0.02, out double x1, out double z1);
            double f0 = m.RainSample(x0, z0, hours0).RainChance, f1 = m.RainSample(x1, z1, hours0).RainChance;
            if ((f0 > 0) == (f1 > 0)) continue;
            double a = x0, b = x1;
            for (int k = 0; k < 40; k++) { double mid = (a + b) / 2; if ((m.RainSample(mid, z0, hours0).RainChance > 0) == (f0 > 0)) a = mid; else b = mid; }
            found++;
            for (int year = 0; year < 8; year++)
                for (int d = 0; d < 3; d++)
                {
                    double h = hours0 + year * 108 * 24;
                    double ca = m.Cloudness(a, z0, h), cb = m.Cloudness(b, z0, h);
                    pairs++;
                    if ((ca > 0) != (cb > 0)) flips++;
                    if (Math.Abs(ca - cb) > worst) { worst = Math.Abs(ca - cb); where = $"lat {lat:0.00} lon {lon:0.00} yearRel {yr:0.000} year {year}: cloudness {ca:0.000} against {cb:0.000} ({Math.Abs(b - a):E1} blocks apart), rain chance {m.RainSample(a, z0, h).RainChance:E2} against {m.RainSample(b, z0, h).RainChance:E2}, storm boost {m.Storms.Influence(lat, lon, h).RainBoost:0.00}"; }
                }
        }
        Console.WriteLine($"dry line (rain chance 0 against above 0), rules {rules}: {found} crossings, {pairs} pairs a hair apart; largest cloudness step {worst:0.000}; rain clouds on one side only {flips} ({100.0 * flips / Math.Max(1, pairs):0.00} %)");
        Console.WriteLine($"  worst: {where}");
    }

    static int Main(string[] args)
    {
        string root = args[0];
        data = EarthData.LoadFromFolder(root, false);
        ground = data.WeatherGround;
        var inp = new LayoutInput { WorldWidth = 256000, CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = 51200, CwPoleDistance = 51200 - 48, CwBandInner = 608, Gutter = false };
        geo = EarthLayout.Build(inp, data, out _);
        Console.WriteLine($"geometry: ring {geo.RingStart}+{geo.RingLength}, wraps {geo.Wraps}, gutter {geo.Gutter}, seam {geo.SeamLon0}, {geo.BlocksPerDegreeX:0.00} x {geo.BlocksPerDegreeZ:0.00} blocks per degree");
        string mode = args.Length > 1 ? args[1] : "all";
        int rc = 0;
        if (mode == "determinism" || mode == "all") rc += Determinism();
        if (mode == "timeways" || mode == "all") TimeWays();
        if (mode == "nan" || mode == "all") Nan();
        if (mode == "time" || mode == "all") { TimeEdges(5); TimeEdges(9); }
        if (mode == "space" || mode == "all") { SpaceEdges(5); SpaceEdges(9); }
        if (mode == "storm" || mode == "all") { StormEdge(5); StormEdge(9); }
        if (mode == "deck" || mode == "all") { Deck(9); }
        if (mode == "dry" || mode == "all") { DryLine(9); }
        if (mode == "equator") { Equator(5); Equator(9); }
        if (mode == "slots") Slots();
        if (mode == "badlayout") BadLayout();
        if (mode == "eco") Eco(args);
        return rc == 0 ? 0 : 1;
    }

    static partial void Eco(string[] args);
}
