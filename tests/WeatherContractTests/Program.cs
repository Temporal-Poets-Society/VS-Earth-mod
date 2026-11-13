using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Offline checks of Earth's weather as the open sea contracts' weather role (Sea's docs/open-sea-contracts.md sections
    /// 3-5, Earth decision 0017): the entry's shape and types (only .NET base types), its units (m/s, 0-1), the direction
    /// convention (+X east, +Z south, toward), the storm kinds, and the same answer for the same input on another side's
    /// model and from many threads at once. Section 5: vanilla's wind as a vector. Standard Cylinder World 256000 x 102400
    /// layout and calendar, as tests/WeatherTests.
    ///   dotnet run -c Release -- [dataFolder]   (a folder with earthdata/weather.bin, e.g. the main Earth checkout)
    /// </summary>
    static class Program
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static string FindRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "modinfo.json"))) dir = Path.GetDirectoryName(dir);
            return dir ?? Directory.GetCurrentDirectory();
        }

        const double DaysPerYear = 108, HoursPerDay = 24, YearHours = DaysPerYear * HoursPerDay;
        const double Mps = 25;

        /// <summary>Game hours at the middle of month m (0 = January) of year y.</summary>
        static double MonthHours(int y, int m, double frac = 0.5) => (y * DaysPerYear + (m + frac) / 12.0 * DaysPerYear) * HoursPerDay;

        static EarthGeometry geo;

        static int Main(string[] args)
        {
            string root = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : FindRoot();
            var sw = Stopwatch.StartNew();
            var data = EarthData.LoadFromFolder(root, false);
            Console.WriteLine($"data loaded from {root} in {sw.ElapsedMilliseconds} ms");
            if (data.Weather == null) { Console.WriteLine("FAIL no weather.bin (pass a folder with earthdata/weather.bin)"); return 1; }

            int W = 256000, L = 102400;
            var inp = new LayoutInput
            {
                WorldWidth = W,
                CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48,
                CwBandInner = 608,
            };
            geo = EarthLayout.Build(inp, data, out _);
            WeatherSettings Settings() => new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay };
            // the server's and a client's weather: each side builds its own from the same parameters (decision 0017)
            var server = new WeatherModel(data.Weather, geo, Settings(), 12345);
            if (args.Contains("hash"))
            {
                // "rules5": a world made before rules 6 (its weather must not change with rules 6's additions)
                var hashed = args.Contains("rules5") ? new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = 5 }, 12345) : server;
                int di = Array.IndexOf(args, "dump");
                return Fingerprint(hashed, di >= 0 && di + 1 < args.Length ? args[di + 1] : null);
            }
            var client = new WeatherModel(data.Weather, geo, Settings(), 12345);

            WeatherModel current = null;
            long revision = 1;
            var entry = WeatherContract.Build(() => Volatile.Read(ref current), () => Interlocked.Read(ref revision));

            CheckPortableMath();
            CheckShape(entry);
            CheckClaim(entry);
            CheckNoModel(entry);
            Volatile.Write(ref current, server);
            Interlocked.Increment(ref revision);
            Check((long)Invoke<Func<long>>(entry, "revision")() == 2, "revision: changes when the model is built (1 → 2)");
            CheckUnits(entry, server);
            CheckDirections(entry);
            CheckStorms(entry, server);
            CheckRain(entry, server);
            CheckSides(entry, client);
            CheckThreads(data, Settings);
            CheckVanillaVector(server);

            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// (EA-0029) "hash": a fingerprint of the weather's answers, bit for bit, to compare builds, processes and
        /// operating systems: wind (gusty and steady), the prevailing wind, storms, rain cloudness, cloud cover, pressure and
        /// thunder at 40,000 places and hours over the whole Earth and three years, each double's 64 bits hashed (SHA-256),
        /// with one hash per quantity so a difference names its source. Debug: build with -p:Optimize=false. "hash dump FILE"
        /// also writes every answer (a byte for the quantity, the double) to compare two runs value by value.
        /// </summary>
        static int Fingerprint(WeatherModel m, string dumpFile)
        {
            using var dump = dumpFile != null ? new BinaryWriter(File.Create(dumpFile)) : null;
            var names = new[] { "wind", "steady", "prevailing", "storm", "cloudness", "cover", "pressure", "thunder" };
            var parts = names.Select(_ => System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)).ToArray();
            var all = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buf = new byte[8];
            void Add(int i, double v)
            {
                BitConverter.TryWriteBytes(buf, BitConverter.DoubleToInt64Bits(v));
                parts[i].AppendData(buf);
                all.AppendData(buf);
                dump?.Write((byte)i);
                dump?.Write(v);
            }
            var rnd = new Random(20261113);
            int n = 40000, raining = 0, thunder = 0;
            for (int i = 0; i < n; i++)
            {
                double lat = -80 + 160 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                geo.FromGeo(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 3 * YearHours;
                var w = m.WindAt(x, z, h, true); Add(0, w.X); Add(0, w.Z);
                var s = m.Sample(x, z, h);
                Add(1, s.Wind.X); Add(1, s.Wind.Z);
                Add(2, s.Prevailing.X); Add(2, s.Prevailing.Z);
                var e = m.Storms.Influence(s.Lat, s.Lon, h); Add(3, e.Presence); Add(3, e.Wind.X); Add(3, e.Wind.Z); Add(3, e.RainBoost);
                Add(4, s.Cloudness); Add(5, s.CloudCover); Add(6, s.Pressure); Add(7, s.Thunder); Add(7, m.ThunderAt(x, z, h));
                if (s.Cloudness > 0.5) raining++;
                if (s.Thunder > 0) thunder++;
            }
            string Hex(System.Security.Cryptography.IncrementalHash ih) => Convert.ToHexString(ih.GetHashAndReset())[..16];
            Console.WriteLine($"fingerprint of {n} places and hours ({raining} raining, {thunder} under thunder) on {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, " +
                $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
                $"{(typeof(Program).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>().FirstOrDefault()?.IsJITOptimizerDisabled == true ? "Debug" : "Release")}:");
            for (int i = 0; i < names.Length; i++) Console.WriteLine($"  {names[i],-10} {Hex(parts[i])}");
            Console.WriteLine($"  ALL        {Hex(all)}");
            return 0;
        }

        static T Invoke<T>(Dictionary<string, object> e, string name) where T : class => e.TryGetValue(name, out var o) ? o as T : null;

        static Func<double, double, double, bool, (double X, double Z)> WindOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, bool, (double X, double Z)>>(e, "wind");
        static Func<double, double, double, (double X, double Z)> PrevailingOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double X, double Z)>>(e, "prevailing");
        static Func<double, double, double, (double Presence, double WindMps, string Kind)> StormOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double Presence, double WindMps, string Kind)>>(e, "storm");
        static Func<double, double, double, (double Cloudness, double Precipitation)> RainOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double Cloudness, double Precipitation)>>(e, "rain");

        static void Pos(double lat, double lon, out double x, out double z) => geo.FromGeo(lat, lon, out x, out z);

        // ------------------------------------------------------------------------------------------ shape and types

        /// <summary>True when a type and every generic argument in it come from the base class library (no mod's types).</summary>
        static bool BaseTypesOnly(Type t)
        {
            if (t.Assembly != typeof(object).Assembly) return false;
            return !t.IsGenericType || t.GetGenericArguments().All(BaseTypesOnly);
        }

        static void CheckShape(Dictionary<string, object> e)
        {
            Console.WriteLine("shape and types");
            Check(e.GetType() == typeof(Dictionary<string, object>), "the entry is a Dictionary<string, object>");
            Check(e.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(WeatherContract.Members.OrderBy(k => k, StringComparer.Ordinal)),
                "members: " + string.Join(", ", e.Keys));
            Check(e["owner"] is string o && o == "earth", "owner \"earth\" (Earth's mod ID and its SetWindProvider name)");
            Check(e["version"] is int v && v == 1, "version 1 (int)");
            Check(e["priority"] is int p && p == 100, "priority 100 (int, the world's owner)");
            Check(e["revision"] is Func<long>, "revision is Func<long>");
            // the exact delegate types a consumer tests for (Sea's SeaContracts.FollowWeather uses the wind's and prevailing's)
            Check(e["wind"] is Func<double, double, double, bool, (double X, double Z)>, "wind is Func<double, double, double, bool, (double, double)>");
            Check(e["prevailing"] is Func<double, double, double, (double X, double Z)>, "prevailing is Func<double, double, double, (double, double)>");
            Check(e["storm"] is Func<double, double, double, (double Presence, double WindMps, string Kind)>, "storm is Func<double, double, double, (double, double, string)>");
            Check(e["rain"] is Func<double, double, double, (double Cloudness, double Precipitation)>, "rain is Func<double, double, double, (double, double)>");
            Check(e["writesVanilla"] is bool wv && wv, "writesVanilla is true (bool)");
            var foreign = e.Where(kv => kv.Value == null || !BaseTypesOnly(kv.Value.GetType())).Select(kv => kv.Key).ToList();
            Check(foreign.Count == 0, "every member's type is from the base class library" + (foreign.Count > 0 ? ": not " + string.Join(", ", foreign) : ""));
        }

        /// <summary>(EA-0029) PortableMath against the system's: as accurate (relative error within a few 1e-16, Pow's a few
        /// 1e-15), over the weather's ranges and beyond, and the special values.</summary>
        static void CheckPortableMath()
        {
            Console.WriteLine("portable maths (EA-0029)");
            var r = new Random(7);
            double Rel(double a, double b) => a == b ? 0 : Math.Abs(a - b) / Math.Max(Math.Abs(b), 1e-300);
            double exp = 0, log = 0, pow = 0, sin = 0, cos = 0, atan = 0;
            for (int i = 0; i < 200000; i++)
            {
                double x = (r.NextDouble() * 2 - 1) * 700;
                exp = Math.Max(exp, Rel(PortableMath.Exp(x), Math.Exp(x)));
                double p = Math.Exp((r.NextDouble() * 2 - 1) * 700);
                log = Math.Max(log, Math.Abs(PortableMath.Log(p) - Math.Log(p)) / Math.Max(1, Math.Abs(Math.Log(p))));
                double b = r.NextDouble() * 50, e = (r.NextDouble() * 2 - 1) * 8;
                pow = Math.Max(pow, Rel(PortableMath.Pow(b, e), Math.Pow(b, e)));
                double a = (r.NextDouble() * 2 - 1) * 2000;
                sin = Math.Max(sin, Math.Abs(PortableMath.Sin(a) - Math.Sin(a)));
                cos = Math.Max(cos, Math.Abs(PortableMath.Cos(a) - Math.Cos(a)));
                double y = (r.NextDouble() * 2 - 1) * 100, xx = (r.NextDouble() * 2 - 1) * 100;
                atan = Math.Max(atan, Math.Abs(PortableMath.Atan2(y, xx) - Math.Atan2(y, xx)));
            }
            Check(exp < 5e-16, $"Exp within {exp:0.0e0} (relative) over ±700");
            Check(log < 5e-16, $"Log within {log:0.0e0} over 1e-304..1e304");
            Check(pow < 1e-14, $"Pow within {pow:0.0e0} (relative) for bases 0-50, powers ±8");
            Check(sin < 2e-15 && cos < 2e-15, $"Sin and Cos within {Math.Max(sin, cos):0.0e0} over ±2000");
            Check(atan < 5e-16, $"Atan2 within {atan:0.0e0} over all four quadrants");
            Check(PortableMath.Exp(0) == 1 && PortableMath.Log(1) == 0 && PortableMath.Pow(2, 10) == 1024 && PortableMath.Pow(-2, 3) == -8
                && PortableMath.Sin(0) == 0 && PortableMath.Cos(0) == 1 && PortableMath.Atan2(0, -1) == Math.PI && PortableMath.Atan2(1, 0) == Math.PI / 2
                && double.IsNaN(PortableMath.Log(-1)) && PortableMath.Exp(-800) == 0 && double.IsPositiveInfinity(PortableMath.Exp(800)),
                "special values (exp 0, log 1, 2^10, (−2)^3, sin 0, cos 0, atan2's axes, log −1, exp ±800)");
        }

        /// <summary>R2: Earth (priority 100) takes the role over nothing or a lower priority, never over another world owner.</summary>
        static void CheckClaim(Dictionary<string, object> earth)
        {
            Console.WriteLine("claiming the role (R2)");
            static Dictionary<string, object> Other(string owner, object priority) => new() { ["owner"] = owner, ["version"] = 1, ["priority"] = priority };
            Check(WeatherContract.MayReplace(null, out var h0) && h0 == null, "an empty slot: Earth takes it");
            Check(WeatherContract.MayReplace(Other("roughseas", 50), out _), "a mod's default (priority 50): Earth takes its place");
            Check(WeatherContract.MayReplace(Other("sea", 0), out _), "a fallback (priority 0): Earth takes its place");
            Check(WeatherContract.MayReplace(new Dictionary<string, object> { ["owner"] = "x" }, out _), "an entry without a priority counts as 0");
            Check(WeatherContract.MayReplace(Other("x", "100"), out _), "a priority that isn't an int counts as 0");
            Check(WeatherContract.MayReplace("not an entry", out _), "a value that isn't an entry is replaced");
            Check(!WeatherContract.MayReplace(Other("otherworld", 100), out var h1) && h1 == "otherworld", "another world owner (100) keeps it, and Earth knows who");
            Check(!WeatherContract.MayReplace(Other(null, 150), out var h2) && h2 == "?", "a higher priority without an owner keeps it (\"?\")");
            Check(!WeatherContract.MayReplace(earth, out _), "Earth's own entry is never asked about (it would count as another owner)");
        }

        static void CheckNoModel(Dictionary<string, object> e)
        {
            Console.WriteLine("before the weather is built");
            Pos(50, -20, out double x, out double z);
            var w = WindOf(e)(x, z, 100, false);
            var pr = PrevailingOf(e)(x, z, 100);
            var s = StormOf(e)(x, z, 100);
            var r = RainOf(e)(x, z, 100);
            Check(double.IsNaN(w.X) && double.IsNaN(w.Z) && double.IsNaN(pr.X) && double.IsNaN(s.Presence) && double.IsNaN(s.WindMps) && s.Kind == null
                && double.IsNaN(r.Cloudness) && double.IsNaN(r.Precipitation), "every answer is NaN (no answer, R7)");
            Check(Invoke<Func<long>>(e, "revision")() == 1, "revision answers");
        }

        // ------------------------------------------------------------------------------------------ units

        static void CheckUnits(Dictionary<string, object> e, WeatherModel m)
        {
            Console.WriteLine("units: m/s");
            var wind = WindOf(e);
            var prevailing = PrevailingOf(e);
            var rnd = new Random(7);
            int exact = 0, n = 0;
            double sum = 0, max = 0;
            int sea = 0;
            for (int i = 0; i < 4000; i++)
            {
                double lat = -65 + 130 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 5 * YearHours;
                bool gusts = (i & 1) == 0;
                var a = wind(x, z, h, gusts);
                var b = m.WindAt(x, z, h, gusts);
                var pa = prevailing(x, z, h);
                var pb = m.Prevailing(x, z, h);
                n++;
                if (a.X == b.X * Mps && a.Z == b.Z * Mps && pa.X == pb.X * Mps && pa.Z == pb.Z * Mps) exact++;
                double speed = Math.Sqrt(a.X * a.X + a.Z * a.Z);
                max = Math.Max(max, speed);
                m.Place(x, z, h, out _, out _, out _, out var normals);
                if (!gusts && normals.Land < 0.05) { sum += speed; sea++; }
            }
            double mean = sum / Math.Max(1, sea);
            Check(exact == n, $"wind and prevailing are Earth's wind units × 25 at {exact} of {n} places and times");
            Check(mean > 4 && mean < 12, $"steady open-sea wind {mean:0.0} m/s on average (4-12; Earth's own check: ~7)");
            Check(max <= WeatherModel.MaxStrength * Mps + 1e-9, $"never above 37.5 m/s (most {max:0.0})");
        }

        // ------------------------------------------------------------------------------------------ directions

        static void CheckDirections(Dictionary<string, object> e)
        {
            Console.WriteLine("directions: +X east, +Z south, toward where the air goes");
            Pos(10, 0, out _, out double zn);
            Pos(-10, 0, out _, out double zs);
            Pos(0, 10, out double xe, out _);
            Pos(0, -10, out double xw, out _);
            Check(zn < zs && xe > xw, "the layout: north is −Z, east is +X");
            var wind = WindOf(e);
            var prevailing = PrevailingOf(e);
            // the year's mean steady wind (vectors), from 36 times a year over three years
            (double X, double Z) Mean(double lat, double lon, bool steady)
            {
                Pos(lat, lon, out double x, out double z);
                double sx = 0, sz = 0;
                int k = 0;
                for (int y = 0; y < 3; y++)
                    for (int t = 0; t < 36; t++)
                    {
                        double h = (y + t / 36.0) * YearHours;
                        var w = steady ? wind(x, z, h, false) : prevailing(x, z, h);
                        sx += w.X; sz += w.Z; k++;
                    }
                return (sx / k, sz / k);
            }
            // the north-east trades blow toward the south-west, the south-east trades toward the north-west, the roaring
            // forties toward the east, the westerlies of the North Atlantic toward the east
            var ne = Mean(15, -140, true);
            var se = Mean(-15, -110, true);
            var forties = Mean(-45, 90, true);
            var atlantic = Mean(50, -30, true);
            var neP = Mean(15, -140, false);
            Check(ne.X < -1 && ne.Z > 0.5, $"NE trades (15°N 140°W) blow toward the south-west: ({ne.X:0.0}, {ne.Z:0.0}) m/s");
            Check(se.X < -1 && se.Z < -0.5, $"SE trades (15°S 110°W) blow toward the north-west: ({se.X:0.0}, {se.Z:0.0}) m/s");
            Check(forties.X > 3 && Math.Abs(forties.X) > Math.Abs(forties.Z), $"roaring forties (45°S 90°E) blow toward the east: ({forties.X:0.0}, {forties.Z:0.0}) m/s");
            Check(atlantic.X > 1, $"North Atlantic westerlies (50°N 30°W) blow toward the east: ({atlantic.X:0.0}, {atlantic.Z:0.0}) m/s");
            Check(neP.X < -1 && neP.Z > 0.5, $"the prevailing wind there too: ({neP.X:0.0}, {neP.Z:0.0}) m/s");
        }

        // ------------------------------------------------------------------------------------------ storms

        static void CheckStorms(Dictionary<string, object> e, WeatherModel m)
        {
            Console.WriteLine("storms: presence 0-1, wind in m/s, Rough Seas' kinds");
            var storm = StormOf(e);
            Check(WeatherContract.KindOf(null) == null, "no storm: no kind");

            // ranges over many places and times
            var rnd = new Random(11);
            int n = 0, inRange = 0, kinds = 0, consistent = 0, present = 0, matches = 0;
            var seen = new HashSet<string>();
            for (int i = 0; i < 20000; i++)
            {
                double lat = -70 + 140 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 3 * YearHours;
                var s = storm(x, z, h);
                n++;
                if (s.Presence >= 0 && s.Presence <= 1 && s.WindMps >= 0 && s.WindMps <= WeatherModel.MaxStrength * Mps + 1e-9) inRange++;
                if (s.Kind == null || s.Kind == "gale" || s.Kind == "hurricane") kinds++;
                if ((s.Presence > 0) == (s.Kind != null)) consistent++;
                if (s.Presence > 0) present++;
                if (s.Kind != null) seen.Add(s.Kind);
                // the same as Earth's own storms here: their wind, a third weaker over land, in m/s
                m.Place(x, z, h, out double la, out double lo, out _, out var nn);
                var eff = m.Storms.Influence(la, lo, h);
                double want = Math.Min(eff.Wind.Strength * (1 - 0.3 * Math.Clamp(nn.Land, 0, 1)), WeatherModel.MaxStrength) * Mps;
                if (eff.Presence <= 0 ? s.Presence == 0 && s.WindMps == 0 : s.Presence == eff.Presence && s.WindMps == want) matches++;
            }
            Check(inRange == n, $"Presence 0-1 and WindMps 0-37.5 at {inRange} of {n}");
            Check(kinds == n && !seen.Contains("squall"), $"Kind is null, gale or hurricane, never squall (seen: {string.Join(", ", seen)})");
            Check(consistent == n, $"a kind exactly where a storm is present ({consistent} of {n})");
            Check(matches == n, $"presence and wind are Earth's storms' at {matches} of {n} ({present * 100.0 / n:0.0} % of the places and times inside a storm)");
            Check(seen.Contains("gale") && seen.Contains("hurricane"), "both of Earth's storm kinds turn up");

            // one tropical cyclone and one low at their peaks: their kinds, presence in the core, the eyewall's winds
            WeatherStorms.Storm tc = null, low = null;
            for (double wh = 10 * 24; wh < 3 * YearHours && (tc == null || low == null); wh += 12)
                foreach (var s in m.Storms.AliveIn(wh))
                {
                    double mid = s.T0 + s.Life / 2;
                    if (!s.AliveAt(mid)) continue;
                    s.At(mid, out _, out _, out double inten);
                    if (inten < 0.9 * s.Peak) continue;
                    if (s.Tropical && tc == null && s.Peak >= 1.2) tc = s;
                    if (!s.Tropical && low == null && s.Peak >= 0.8) low = s;
                }
            Check(tc != null && low != null, "a strong tropical cyclone and a strong low found at their peaks");
            if (tc != null) CheckOneStorm(storm, tc, "hurricane");
            if (low != null) CheckOneStorm(storm, low, "gale");

            // presence by distance: 1 within the strongest winds near the peak, falling to 0 at the edge
            if (tc != null)
            {
                double inten = tc.Peak;
                double core = WeatherStorms.PresenceOf(tc, 0, inten), eyewall = WeatherStorms.PresenceOf(tc, tc.MaxWindKm, inten);
                double mid = WeatherStorms.PresenceOf(tc, 0.5 * tc.RadiusKm, inten), edge = WeatherStorms.PresenceOf(tc, tc.RadiusKm, inten);
                double young = WeatherStorms.PresenceOf(tc, 0, 0.3 * inten);
                Check(core == 1 && eyewall == 1 && mid > 0 && mid < eyewall && edge == 0,
                    $"presence: eye {core:0.00}, eyewall {eyewall:0.00}, half way out {mid:0.00}, edge {edge:0.00}");
                Check(Math.Abs(young - 0.3) < 1e-9, $"a storm at 30 % of its peak is 30 % present in its core ({young:0.00})");
            }
        }

        static void CheckOneStorm(Func<double, double, double, (double Presence, double WindMps, string Kind)> storm, WeatherStorms.Storm s, string kind)
        {
            double wh = s.T0 + s.Life / 2;
            double hours = wh;   // pace 1
            s.At(wh, out double lat, out double lon, out double inten);
            Pos(lat, lon, out double cx, out double cz);
            var centre = storm(cx, cz, hours);
            // its eyewall (the radius of its strongest winds), to the east
            double km = s.MaxWindKm, dLon = km / (111.32 * Math.Cos(lat * Math.PI / 180));
            Pos(lat, lon + dLon, out double ex, out double ez);
            var eyewall = storm(ex, ez, hours);
            Check(centre.Kind == kind && eyewall.Kind == kind, $"{s.Name} (peak {s.Peak * Mps:0} m/s, now {inten * Mps:0}) is a {kind}: centre {centre.Kind ?? "null"}, eyewall {eyewall.Kind ?? "null"}");
            Check(centre.Presence >= 0.9 && eyewall.Presence >= 0.9, $"  inside its core near its peak: presence {centre.Presence:0.00} at the centre, {eyewall.Presence:0.00} at {km:0} km");
            Check(eyewall.WindMps > 0.5 * inten * Mps, $"  its own wind at {km:0} km: {eyewall.WindMps:0.0} m/s (more than half its {inten * Mps:0} m/s)");
            // far outside (twice its radius north): no storm of its own there
            Pos(lat + 2 * s.RadiusKm / 111.32 * (lat >= 0 ? 1 : -1), lon, out double fx, out double fz);
            var far = storm(fx, fz, hours);
            Check(far.Presence < centre.Presence, $"  presence falls away from it ({far.Presence:0.00} at twice its radius)");
        }

        // ------------------------------------------------------------------------------------------ rain

        static void CheckRain(Dictionary<string, object> e, WeatherModel m)
        {
            Console.WriteLine("rain: vanilla's 0-1 rain-cloud overlay and precipitation");
            var rain = RainOf(e);
            var rnd = new Random(13);
            int n = 0, ok = 0, vanilla = 0, raining = 0;
            for (int i = 0; i < 20000; i++)
            {
                double lat = -70 + 140 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 3 * YearHours;
                var r = rain(x, z, h);
                double c = m.Cloudness(x, z, h);
                n++;
                if (r.Cloudness >= 0 && r.Cloudness <= 1 && r.Precipitation >= 0 && r.Precipitation <= 1 && (r.Precipitation <= 0 || r.Cloudness > 0.5)) ok++;
                // what vanilla's climate lookup makes of Earth's rain cloudness (WeatherSystemBase.Event_OnGetClimate)
                if (r.Cloudness == Math.Clamp(c, 0, 1) && r.Precipitation == Math.Clamp(c - 0.5, 0, 1)) vanilla++;
                if (r.Precipitation > 0) raining++;
            }
            Check(ok == n, $"both 0-1, and rain only under clouds above 0.5 ({ok} of {n})");
            Check(vanilla == n, $"the same as vanilla's RainCloudOverlay and Rainfall from Earth's cloudness at {vanilla} of {n}");
            Check(raining > n / 50 && raining < n / 3, $"raining at {raining * 100.0 / n:0.0} % of the places and times");
            // Mumbai's monsoon through the contract
            Pos(19.1, 72.9, out double mx, out double mz);
            int jan = 0, jul = 0, k = 300;
            for (int i = 0; i < k; i++)
            {
                if (rain(mx, mz, MonthHours(3 + i % 50, 0, 0.2 + 0.6 * rnd.NextDouble())).Precipitation > 0) jan++;
                if (rain(mx, mz, MonthHours(3 + i % 50, 6, 0.2 + 0.6 * rnd.NextDouble())).Precipitation > 0) jul++;
            }
            Check(jul > k / 4 && jan < k / 30, $"Mumbai: rain {jan * 100.0 / k:0.0} % of January, {jul * 100.0 / k:0.0} % of July");
        }

        // ------------------------------------------------------------------------------------------ the same answer everywhere

        static List<(double x, double z, double h, bool g)> Queries(int n, int seed)
        {
            var rnd = new Random(seed);
            var q = new List<(double, double, double, bool)>();
            for (int i = 0; i < n; i++)
            {
                double lat = -70 + 140 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                q.Add((x, z, rnd.NextDouble() * 4 * YearHours, rnd.Next(2) == 0));
            }
            return q;
        }

        static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

        static bool Same((double X, double Z) a, (double X, double Z) b) => Bits(a.X) == Bits(b.X) && Bits(a.Z) == Bits(b.Z);
        static bool Same((double P, double W, string K) a, (double P, double W, string K) b) => Bits(a.P) == Bits(b.P) && Bits(a.W) == Bits(b.W) && a.K == b.K;

        sealed class Answers
        {
            public (double X, double Z)[] Wind, Prevailing, Rain;
            public (double P, double W, string K)[] Storm;
        }

        static Answers Ask(Dictionary<string, object> e, List<(double x, double z, double h, bool g)> q, IEnumerable<int> order)
        {
            var a = new Answers { Wind = new (double, double)[q.Count], Prevailing = new (double, double)[q.Count], Rain = new (double, double)[q.Count], Storm = new (double, double, string)[q.Count] };
            var wind = WindOf(e); var prevailing = PrevailingOf(e); var storm = StormOf(e); var rain = RainOf(e);
            foreach (int i in order)
            {
                var (x, z, h, g) = q[i];
                a.Wind[i] = wind(x, z, h, g);
                a.Prevailing[i] = prevailing(x, z, h);
                var s = storm(x, z, h);
                a.Storm[i] = (s.Presence, s.WindMps, s.Kind);
                var r = rain(x, z, h);
                a.Rain[i] = (r.Cloudness, r.Precipitation);
            }
            return a;
        }

        static int Differences(Answers a, Answers b)
        {
            int d = 0;
            for (int i = 0; i < a.Wind.Length; i++)
                if (!Same(a.Wind[i], b.Wind[i]) || !Same(a.Prevailing[i], b.Prevailing[i]) || !Same(a.Rain[i], b.Rain[i]) || !Same(a.Storm[i], b.Storm[i])) d++;
            return d;
        }

        /// <summary>A client's entry (its own model from the same parameters) answers bit for bit as the server's.</summary>
        static void CheckSides(Dictionary<string, object> server, WeatherModel clientModel)
        {
            Console.WriteLine("the same answer on another side");
            var client = WeatherContract.Build(() => clientModel, () => 2);
            var q = Queries(3000, 21);
            var a = Ask(server, q, Enumerable.Range(0, q.Count));
            // the client asks in another order (other cache states)
            var b = Ask(client, q, Enumerable.Range(0, q.Count).Reverse());
            Check(Differences(a, b) == 0, $"a client's model answers the server's {q.Count} questions bit for bit");
        }

        /// <summary>Many threads asking a fresh model at once (its storm caches filling under them) get one thread's answers.</summary>
        static void CheckThreads(EarthData data, Func<WeatherSettings> settings)
        {
            Console.WriteLine("the same answer from several threads");
            var q = Queries(2500, 31);
            // one thread, in order (tests/WeatherTests checks the storm caches against fresh models)
            var single = new WeatherModel(data.Weather, geo, settings(), 12345);
            var one = Ask(WeatherContract.Build(() => single, () => 1), q, Enumerable.Range(0, q.Count));

            var shared = new WeatherModel(data.Weather, geo, settings(), 12345);
            var entry = WeatherContract.Build(() => shared, () => 1);
            int threads = Math.Max(8, Environment.ProcessorCount);
            var results = new Answers[threads];
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                var order = Enumerable.Range(0, q.Count).OrderBy(i => (i * 7919 + t * 104729) % q.Count).ToList();
                results[t] = Ask(entry, q, order);
            });
            int bad = results.Count(r => Differences(r, one) != 0);
            Check(bad == 0, $"{threads} threads asking one fresh model at once, each in its own order: {threads - bad} of {threads} get the single thread's answers");
        }

        // ------------------------------------------------------------------------------------------ section 5

        static void CheckVanillaVector(WeatherModel m)
        {
            Console.WriteLine("vanilla's wind as a vector (section 5)");
            var rnd = new Random(41);
            int ok = 0, n = 0;
            for (int i = 0; i < 5000; i++)
            {
                double lat = -70 + 140 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                var w = m.WindAt(x, z, rnd.NextDouble() * YearHours, true);
                double strength = w.Strength * (0.5 + rnd.NextDouble());   // as if scaled for height
                var v = WeatherContract.VanillaVector(w, strength);
                double len = Math.Sqrt(v.X * v.X + v.Z * v.Z);
                double cross = v.X * w.Z - v.Z * w.X, dot = v.X * w.X + v.Z * w.Z;
                n++;
                if (Math.Abs(len - strength) < 1e-9 && Math.Abs(cross) < 1e-9 && dot >= 0
                    && Math.Abs(WeatherContract.VanillaStrength((float)v.X, (float)v.Z) - strength) < 1e-5) ok++;
            }
            Check(ok == n, $"X east and Z south along the wind, the length its strength, and a reader of the length gets the strength back ({ok} of {n})");
            var calm = WeatherContract.VanillaVector(new WeatherWind(0, 0), 0);
            Check(calm.X == 0 && calm.Z == 0, "calm: no wind");
            // a westerly blowing toward the east (the roaring forties) has its vector's X positive, a trade wind's negative
            Pos(-45, 90, out double fx, out double fz);
            Pos(15, -140, out double tx, out double tz);
            double fe = 0, te = 0;
            for (int t = 0; t < 36; t++)
            {
                var a = m.WindAt(fx, fz, t * YearHours / 36, false);
                var b = m.WindAt(tx, tz, t * YearHours / 36, false);
                fe += WeatherContract.VanillaVector(a, a.Strength).X;
                te += WeatherContract.VanillaVector(b, b.Strength).X;
            }
            Check(fe > 0 && te < 0, $"vanilla's X: toward the east in the roaring forties ({fe / 36:0.00}), toward the west in the trades ({te / 36:0.00})");
        }
    }
}
