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

        /// <summary>The weather's ground (rules 7: weatherground.bin and climate fix 4), as the game hands it to the model.</summary>
        static WeatherGround ground;

        /// <summary>A model as the game builds it: with the ground (rules 7 reads it; older rules don't).</summary>
        static WeatherModel NewModel(WeatherData d, EarthGeometry g, WeatherSettings s, long key)
        {
            var m = new WeatherModel(d, g, s, key);
            m.Ground = ground;
            return m;
        }

        static int Main(string[] args)
        {
            string root = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : FindRoot();
            var sw = Stopwatch.StartNew();
            var data = EarthData.LoadFromFolder(root, false);
            ground = data.WeatherGround;
            if (ground == null) { Console.WriteLine("FAIL the weather's ground: " + data.WeatherGroundError); return 1; }
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
            var server = NewModel(data.Weather, geo, Settings(), 12345);
            if (args.Contains("hash"))
            {
                // "rules5": a world made before rules 6 (its weather must not change with rules 6's additions)
                // "rules6": a world made with rules 6 (likewise with rules 7's); "rules7" (likewise with rules 8's)
                int hashRules = args.Contains("rules5") ? 5 : args.Contains("rules6") ? 6 : args.Contains("rules7") ? 7 : args.Contains("rules8") ? 8 : args.Contains("rules9") ? 9 : WeatherSettings.Newest;
                var hashed = hashRules != WeatherSettings.Newest ? NewModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = hashRules }, 12345) : server;
                int di = Array.IndexOf(args, "dump");
                return Fingerprint(hashed, di >= 0 && di + 1 < args.Length ? args[di + 1] : null);
            }
            // (EA-0115) "catchup": vanilla's snow catch-up as Earth answers it, timed, and its answers fingerprinted per rules
            if (args.Contains("catchup")) return CatchUp(data, args);
            var client = NewModel(data.Weather, geo, Settings(), 12345);

            WeatherModel current = null;
            long revision = 1;
            var entry = WeatherContract.Build(() => Volatile.Read(ref current), () => Interlocked.Read(ref revision));

            CheckPortableMath();
            CheckRulesTable();
            CheckOlderRulesPinned(data);
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
            CheckPressure(entry, server);
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
        /// <summary>
        /// (ER-10, EA-0084) What each weather rules version turns on, written out from the code as it was before the rules
        /// record (the literals 6, 7 and 8 at each call site): a change to <see cref="WeatherRules"/> that moves a fact to
        /// another version fails here.
        /// </summary>
        static void CheckRulesTable()
        {
            Console.WriteLine("weather rules table");
            // version: decks, hail, anomaly, ground, dry thunder, fog, cloud tiles, rain events, anomaly cells, hail hurts, Africa thunder
            var table = new (int v, bool[] facts)[]
            {
                (0, new[] { false, false, false, false, false, false, false, false, false, false, false }),
                (5, new[] { false, false, false, false, false, false, false, false, false, false, false }),
                (6, new[] { true, true, true, false, false, false, false, false, false, false, false }),
                (7, new[] { true, true, true, true, true, true, true, false, false, false, false }),
                (8, new[] { true, true, true, true, true, true, true, true, true, true, false }),
                (9, new[] { true, true, true, true, true, true, true, true, true, true, true }),
            };
            bool ok = true;
            foreach (var (v, f) in table)
            {
                var r = WeatherRules.For(v);
                var got = new[] { r.ColdSeaDecks, r.Hail, r.TemperatureAnomaly, r.Ground, r.DryThunder, r.Fog, r.CloudTiles, r.RainEvents, r.AnomalyCells, r.HailHurts, r.AfricaThunder };
                if (!got.SequenceEqual(f)) { ok = false; Console.WriteLine($"  rules {v}: {string.Join(",", got)} where {string.Join(",", f)}"); }
            }
            Check(ok, "each weather rules version turns on what it did before the rules record (6, 7, 8)");
            Check(WeatherSettings.Newest == 9 && new WeatherSettings().Has.AfricaThunder, "new worlds get the newest weather rules (9)");
            Check(WeatherRules.For(WeatherRules.GroundFrom).Ground && !WeatherRules.For(WeatherRules.GroundFrom - 1).Ground, "the ground is read from rules 7 on");
        }

        /// <summary>
        /// (ER-10) Worlds made with older weather rules keep their weather bit for bit: the fingerprints of rules 5 to 9 as
        /// built (rules 5 and 6: 2.0.0-rc.9 to rc.12; rules 7: rc.13; rules 8 and 9: main after rc.13), with the fog of rules 7
        /// to 9 (it moved by an ulp when NightHours went to the portable maths, ER-17; pinned as it is since), and the hail at
        /// 3000 places under thunder (about 50 hailing; the random places' hail part holds two).
        /// </summary>
        static void CheckOlderRulesPinned(EarthData data)
        {
            Console.WriteLine("older weather rules, pinned");
            foreach (var (rules, all, anomaly, hail, fog, hailstorms) in new[]
            {
                (5, "18C6D3C6B8B0F4E7", (string)null, (string)null, (string)null, "151FF79F29E96D21"),
                (6, "C99BE19573CD5E22", "B15FF6CA45CE773B", "771BC10DED7B5F52", (string)null, "02258BBF92AFC0D0"),
                (7, "04AE435FBB7B4301", "4D6A17C62BD10123", "771BC10DED7B5F52", "4450DF488F399D91", "BDCE760A6CB62986"),
                (8, "4D19B0486DDC9A14", "44EFC9EA945B58DE", "771BC10DED7B5F52", "2E3EC4B7A86D4C7A", "BDCE760A6CB62986"),
                (9, "C6064590B48B6E2D", "44EFC9EA945B58DE", "771BC10DED7B5F52", "2E3EC4B7A86D4C7A", "A922D2D5A027FB7E"),
            })
            {
                var m = NewModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = rules }, 12345);
                var h = Hashes(m, null, false);
                Check(h["ALL"] == all && (anomaly == null || h["anomaly"] == anomaly) && (hail == null || h["hail"] == hail) && (fog == null || h["fog"] == fog) && h["hailstorms"] == hailstorms,
                    $"weather rules {rules} as built: ALL {h["ALL"]}, anomaly {h["anomaly"]}, hail {h["hail"]}, fog {h["fog"]}, hail under thunder {h["hailstorms"]}");
            }
        }

        /// <summary>
        /// (EA-0115) Vanilla's snow catch-up (WeatherSimulationRegion.UpdateSnowAccumulation) asks the climate at a region's
        /// 8 corners (4 places, at sea level and at the world's top) for each hour in turn, up to 480 hours a tick; with Earth's
        /// weather each reading is a rain cloudness (the rain postfix) and a temperature anomaly (the climate hook). Times a
        /// region's 8 x 480 readings, and fingerprints the readings over a whole game year at 8 places' regions for rules 5 to
        /// 9, asked as vanilla does (a corner's hours in turn) and again with the corners interleaved hour by hour: the two
        /// must be equal (a memo of the place must not change an answer), and both equal to the build before (compare the
        /// printed fingerprints).
        /// </summary>
        static int CatchUp(EarthData data, string[] args)
        {
            const int Region = 512;
            var places = new (string, double, double)[] { ("London", 51.5, -0.12), ("Kansas City", 39.1, -94.6), ("Tokyo", 35.7, 139.7),
                ("Mumbai", 19.1, 72.9), ("Sahara", 23.0, 5.5), ("Riga", 56.95, 24.1), ("Santiago", -33.45, -70.67), ("Darwin", -12.46, 130.84) };
            int hoursYear = (int)YearHours;
            foreach (int rules in new[] { 5, 6, 7, 8, 9 })
            {
                var m = NewModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = rules }, 12345);
                var corners = new List<(double x, double z, double metres)>();
                foreach (var (_, la, lo) in places)
                {
                    geo.FromGeo(la, lo, out double px, out double pz);
                    double rx = Math.Floor(px / Region) * Region, rz = Math.Floor(pz / Region) * Region;
                    for (int j = 0; j < 2; j++)
                        for (int k = 0; k < 2; k++)
                            for (int l = 0; l < 2; l++)
                                corners.Add((rx + j * (Region - 1), rz + l * (Region - 1), k == 0 ? 0 : 3000));
                }
                double Hour(int i) => 3 * YearHours + i + 0.5;
                // as vanilla asks: each corner's hours in turn
                var a = new double[corners.Count * hoursYear * 2];
                for (int c = 0; c < corners.Count; c++)
                    for (int i = 0; i < hoursYear; i++)
                    {
                        var (x, z, metres) = corners[c];
                        a[(c * hoursYear + i) * 2] = m.Cloudness(x + 0.5, z + 0.5, Hour(i));
                        a[(c * hoursYear + i) * 2 + 1] = m.TemperatureAnomaly(x, z, Hour(i), metres);
                    }
                // interleaved: every corner at an hour, then the next hour (a fresh model, so nothing is shared but the code)
                var m2 = NewModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = rules }, 12345);
                var b = new double[a.Length];
                for (int i = 0; i < hoursYear; i++)
                    for (int c = 0; c < corners.Count; c++)
                    {
                        var (x, z, metres) = corners[c];
                        b[(c * hoursYear + i) * 2] = m2.Cloudness(x + 0.5, z + 0.5, Hour(i));
                        b[(c * hoursYear + i) * 2 + 1] = m2.TemperatureAnomaly(x, z, Hour(i), metres);
                    }
                var bytes = new byte[a.Length * 8];
                Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
                string fa = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
                Buffer.BlockCopy(b, 0, bytes, 0, bytes.Length);
                string fb = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
                Check(fa == fb, $"rules {rules}: the catch-up's answers, a corner's hours in turn {fa}, corners interleaved {fb}");
                // the cost: a region's 8 corners x 480 hours, at fresh hours each run (as a returning player's region)
                double Time(Func<int, double> f, int runs)
                {
                    f(0);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    double sink = 0;
                    for (int r = 1; r <= runs; r++) sink += f(r);
                    if (sink == 12345.678) Console.Write("");
                    return sw.Elapsed.TotalMilliseconds / runs;
                }
                var cost = new List<string>();
                int variant = 0;
                foreach (var (what, rain, anomaly) in new[] { ("both", true, true), ("rain", true, false), ("anomaly", false, true) })
                {
                    // (each variant at hours of its own: the storms of an hour are built once and kept, as for every region)
                    double baseHours = (5 + 40 * variant++) * YearHours;
                    double ms = Time(r =>
                    {
                        double sum = 0;
                        var (x0, z0, _) = corners[(r * 8) % corners.Count];
                        for (int c = 0; c < 8; c++)
                        {
                            double x = x0 + (c & 1) * (Region - 1), z = z0 + ((c >> 2) & 1) * (Region - 1), metres = (c >> 1 & 1) * 3000;
                            for (int i = 0; i < 480; i++)
                            {
                                double h = baseHours + r * 480 + i + 0.5;
                                if (rain) sum += m.Cloudness(x + 0.5, z + 0.5, h);
                                if (anomaly) sum += m.TemperatureAnomaly(x, z, h, metres);
                            }
                        }
                        return sum;
                    }, 24);
                    cost.Add($"{what} {ms:0.00} ms");
                }
                Console.WriteLine($"  rules {rules}: a region's catch-up (8 corners x 480 hours): " + string.Join(", ", cost));
                if (args.Contains("parts"))
                {
                    // one corner's 480 hours, the storms of those hours already built: what a reading's parts cost (us a call)
                    var (cx0, cz0, _) = corners[0];
                    double hb = 7 * YearHours;
                    for (int i = 0; i < 480; i++) { m.Cloudness(cx0 + 0.5, cz0 + 0.5, hb + i + 0.5); m.TemperatureAnomaly(cx0, cz0, hb + i + 0.5, 0); }
                    double Us(Func<int, double> f)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew(); double sink = 0;
                        for (int rep = 0; rep < 40; rep++) for (int i = 0; i < 480; i++) sink += f(i);
                        if (sink == 12345.678) Console.Write("");
                        return sw.Elapsed.TotalMilliseconds * 1000 / (40 * 480);
                    }
                    var g0 = geo.ToGeo(cx0 + 0.5, cz0 + 0.5);
                    Console.WriteLine($"    parts (us a call): ToGeo {Us(i => geo.ToGeo(cx0 + 0.5, cz0 + 0.5).Lat):0.000}, Place {Us(i => { m.Place(cx0 + 0.5, cz0 + 0.5, hb + i + 0.5, out double la, out _, out _, out var n); return la + n.Precip; }):0.000}" +
                        $", Data.Sample {Us(i => data.Weather.Sample(g0.Lat, g0.Lon, m.YearRel(hb + i + 0.5)).Precip):0.000}" +
                        $", storms {Us(i => m.Storms.Influence(g0.Lat, g0.Lon, (hb + i + 0.5) * m.Settings.Pace).RainBoost):0.000}" +
                        $", Cloudness {Us(i => m.Cloudness(cx0 + 0.5, cz0 + 0.5, hb + i + 0.5)):0.000}, anomaly {Us(i => m.TemperatureAnomaly(cx0, cz0, hb + i + 0.5, 0)):0.000}");
                }
            }
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        static int Fingerprint(WeatherModel m, string dumpFile)
        {
            Hashes(m, dumpFile, true);
            return 0;
        }

        /// <summary>The fingerprint's parts by name (wind ... thunder, ALL, and anomaly, hail, fog outside ALL).</summary>
        static Dictionary<string, string> Hashes(WeatherModel m, string dumpFile, bool print)
        {
            using var dump = dumpFile != null ? new BinaryWriter(File.Create(dumpFile)) : null;
            var names = new[] { "wind", "steady", "prevailing", "storm", "cloudness", "cover", "pressure", "thunder" };
            var parts = names.Select(_ => System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)).ToArray();
            var all = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buf = new byte[8];
            // the later rules' parts (not in ALL, which stays as it was before them): the weather's own warmth (rules 6),
            // hail (6) and fog (7), at the ground 300 m up; and hail where it falls ("hailstorms": the random places above
            // hold almost none, Fable's re-review of 14 November)
            var extraNames = new[] { "anomaly", "hail", "fog", "hailstorms" };
            var extra = extraNames.Select(_ => System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)).ToArray();
            void AddExtra(int i, double v)
            {
                BitConverter.TryWriteBytes(buf, BitConverter.DoubleToInt64Bits(v));
                extra[i].AppendData(buf);
            }
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
                AddExtra(0, m.TemperatureAnomaly(x, z, h, 300)); AddExtra(1, m.HailAt(x, z, h, 300));
                var fg = m.FogAt(x, z, h, 300); AddExtra(2, fg.Ground); AddExtra(2, fg.Air);
                if (s.Cloudness > 0.5) raining++;
                if (s.Thunder > 0) thunder++;
            }
            // hail where it can fall: places and hours under thunder (ThunderAt above 0), until 3000 of them, the hail there
            // (0 under a storm that hails not, so where it starts and stops counts too)
            var hrnd = new Random(20261114);
            int stormy = 0, hailing = 0;
            for (int i = 0; i < 4000000 && stormy < 3000; i++)
            {
                double lat = -45 + 100 * hrnd.NextDouble(), lon = -180 + 360 * hrnd.NextDouble();
                geo.FromGeo(lat, lon, out double x, out double z);
                double h = hrnd.NextDouble() * 3 * YearHours;
                if (m.ThunderAt(x, z, h) <= 0) continue;
                stormy++;
                double hail = m.HailAt(x, z, h, 300);
                if (hail > 0) hailing++;
                AddExtra(3, hail);
            }
            string Hex(System.Security.Cryptography.IncrementalHash ih) => Convert.ToHexString(ih.GetHashAndReset())[..16];
            if (print) Console.WriteLine($"fingerprint of {n} places and hours ({raining} raining, {thunder} under thunder) on {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, " +
                $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
                $"{(typeof(Program).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>().FirstOrDefault()?.IsJITOptimizerDisabled == true ? "Debug" : "Release")}:");
            var result = new Dictionary<string, string>();
            for (int i = 0; i < names.Length; i++) result[names[i]] = Hex(parts[i]);
            result["ALL"] = Hex(all);
            for (int i = 0; i < extraNames.Length; i++) result[extraNames[i]] = Hex(extra[i]);
            if (print)
            {
                foreach (var name in names) Console.WriteLine($"  {name,-10} {result[name]}");
                Console.WriteLine($"  ALL        {result["ALL"]}");
                foreach (var name in extraNames) Console.WriteLine($"  {name,-10} {result[name]} (not in ALL)");
                Console.WriteLine($"  (hailstorms: {stormy} places under thunder, {hailing} with hail)");
            }
            return result;
        }

        static T Invoke<T>(Dictionary<string, object> e, string name) where T : class => e.TryGetValue(name, out var o) ? o as T : null;

        static Func<double, double, double, bool, (double X, double Z)> WindOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, bool, (double X, double Z)>>(e, "wind");
        static Func<double, double, double, (double X, double Z)> PrevailingOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double X, double Z)>>(e, "prevailing");
        static Func<double, double, double, (double Presence, double WindMps, string Kind)> StormOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double Presence, double WindMps, string Kind)>>(e, "storm");
        static Func<double, double, double, (double Cloudness, double Precipitation)> RainOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, (double Cloudness, double Precipitation)>>(e, "rain");
        static Func<double, double, double, double> PressureOf(Dictionary<string, object> e) => Invoke<Func<double, double, double, double>>(e, "pressure");

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
            Check(e["pressure"] is Func<double, double, double, double>, "pressure is Func<double, double, double, double> (added under R6, 15 November 2026)");
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
            double hpa = PressureOf(e)(x, z, 100);
            Check(double.IsNaN(w.X) && double.IsNaN(w.Z) && double.IsNaN(pr.X) && double.IsNaN(s.Presence) && double.IsNaN(s.WindMps) && s.Kind == null
                && double.IsNaN(r.Cloudness) && double.IsNaN(r.Precipitation) && double.IsNaN(hpa), "every answer is NaN (no answer, R7)");
            Check(Invoke<Func<long>>(e, "revision")() == 1, "revision answers");
        }

        // ------------------------------------------------------------------------------------------ pressure

        /// <summary>(15 November 2026, R6) The pressure member: Earth's model's own sea-level pressure (what World Map's
        /// barometer read from the model by reflection), in hPa, lower in storms.</summary>
        static void CheckPressure(Dictionary<string, object> e, WeatherModel m)
        {
            Console.WriteLine("pressure: hPa at sea level");
            var pressure = PressureOf(e);
            var storm = StormOf(e);
            var rnd = new Random(11);
            int exact = 0, n = 0, stormy = 0;
            double min = double.MaxValue, max = double.MinValue, calm = 0, inStorm = 0;
            int calmN = 0;
            for (int i = 0; i < 3000; i++)
            {
                double lat = -65 + 130 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                Pos(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 5 * YearHours;
                double a = pressure(x, z, h);
                n++;
                if (a == m.Sample(x, z, h).Pressure) exact++;
                min = Math.Min(min, a); max = Math.Max(max, a);
                if (storm(x, z, h).Presence > 0.5) { inStorm += a; stormy++; }
                else { calm += a; calmN++; }
            }
            Check(exact == n, $"pressure is the model's WeatherSample.Pressure at {exact} of {n} places and times");
            Check(min > 900 && max < 1050, $"between 900 and 1050 hPa ({min:0} to {max:0})");
            if (stormy > 0)
                Check(inStorm / stormy < calm / Math.Max(1, calmN), $"lower within storms ({inStorm / stormy:0} hPa at {stormy} places) than elsewhere ({calm / Math.Max(1, calmN):0})");
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
            var single = NewModel(data.Weather, geo, settings(), 12345);
            var one = Ask(WeatherContract.Build(() => single, () => 1), q, Enumerable.Range(0, q.Count));

            var shared = NewModel(data.Weather, geo, settings(), 12345);
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
