using System.Diagnostics;
using System.Text.Json;
using EarthMap.Core;

/// <summary>
/// Real-data benchmark of the river lookup: build time, retained memory, the per-terrain-column Nearest query in the
/// order world generation issues it (chunk by chunk, with the settling margin), scattered queries (the cache-miss path)
/// and DistanceToWater, single- and multi-threaded, with every Hit field compared bit for bit against the first engine
/// (the frozen legacy oracle unless --no-legacy). Writes nothing unless --out is given.
///
///   dotnet run -c Release --project tests/RiverIndexTests -- --bench [--data earthdata|mod.zip]
///       [--worlds 256000x102400,512000x204800] [--passes 5] [--threads 4] [--no-legacy] [--out file.json]
///
/// A world "WxL" uses Cylinder World's standard ring (0.8 W; 256000x102400 = scale 1.0, 512000x204800 = scale 2.0).
/// Earth needs Cylinder World (decision 0023), so the fitted worlds without it are gone; a trailing "cw" is still accepted.
/// </summary>
static class RiverQueryBenchmark
{
    public readonly record struct Probe(bool Found, double Dist, double Q, double DirX, double DirZ, int WaterTop, int Ramp, bool Rapid, bool Mouth);

    public sealed class Engine
    {
        public string Name;
        public object Instance;
        public int Segments;
        public Func<double, double, Probe> Probe;
        /// <summary>Nearest over [from, to): a checksum of the hits (keeps the work observable).</summary>
        public Func<double[], double[], int, int, long> NearestRange;
        public Func<double[], double[], double, double> DistanceAll;
        public Func<double, double, double, double> Distance;
    }

    public sealed record Factory(string Name, bool Legacy, Func<List<RiverReach>, EarthGeometry, Func<double, int>, double, Engine> Build);

    /// <summary>Engines in comparison order; extra harnesses may register more (e.g. an older index kept outside the repo).</summary>
    public static readonly List<Factory> Factories = new()
    {
        new("legacy-0.11", true, (r, g, top, scale) =>
        {
            var n = new LegacyRiverNetwork(r, g, 100, 1.5, top, "ends", 20, scale, .5, 2);
            return new Engine
            {
                Instance = n, Segments = n.SegmentCount,
                Probe = (x, z) => n.Nearest(x, z, out var h) ? new Probe(true, h.Dist, h.Q, h.DirX, h.DirZ, h.WaterTop, h.Ramp, h.Rapid, h.Mouth) : default,
                NearestRange = (xs, zs, a, b) =>
                {
                    long sum = 0;
                    for (int i = a; i < b; i++) if (n.Nearest(xs[i], zs[i], out var h)) sum += 1 + h.WaterTop + h.Ramp;
                    return sum;
                },
                DistanceAll = (xs, zs, within) =>
                {
                    double sum = 0;
                    for (int i = 0; i < xs.Length; i++) { double d = n.DistanceToWater(xs[i], zs[i], within); if (!double.IsInfinity(d)) sum += d; }
                    return sum;
                },
                Distance = n.DistanceToWater,
            };
        }),
        new("current", false, (r, g, top, scale) =>
        {
            var n = new RiverNetwork(r, g, 100, 1.5, top, "ends", 20, scale, .5, 2);
            return new Engine
            {
                Instance = n, Segments = n.SegmentCount,
                Probe = (x, z) => n.Nearest(x, z, out var h) ? new Probe(true, h.Dist, h.Q, h.DirX, h.DirZ, h.WaterTop, h.Ramp, h.Rapid, h.Mouth) : default,
                NearestRange = (xs, zs, a, b) =>
                {
                    long sum = 0;
                    for (int i = a; i < b; i++) if (n.Nearest(xs[i], zs[i], out var h)) sum += 1 + h.WaterTop + h.Ramp;
                    return sum;
                },
                DistanceAll = (xs, zs, within) =>
                {
                    double sum = 0;
                    for (int i = 0; i < xs.Length; i++) { double d = n.DistanceToWater(xs[i], zs[i], within); if (!double.IsInfinity(d)) sum += d; }
                    return sum;
                },
                Distance = n.DistanceToWater,
            };
        }),
    };

    // river-dense places: Rhine/Main, Nile delta, Amazon at Manaus, Yangtze at Wuhan, Danube at Belgrade, Daugava at Riga
    static readonly (double Lat, double Lon)[] Anchors = { (50, 7.6), (30.05, 31.2), (-3.1, -60), (30.5, 114.3), (44.8, 20.4), (56.95, 24.1) };

    sealed class Pattern
    {
        public string Name;
        public double[] X, Z;
        /// <summary>Start of each work unit (a chunk) for the threaded run.</summary>
        public int[] Units;
    }

    static string Arg(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static string FindData()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "earthdata", "rivers.bin"))) return Path.Combine(dir.FullName, "earthdata");
            }
        }
        throw new FileNotFoundException("earthdata/rivers.bin not found above the working directory; pass --data <earthdata folder or mod zip>");
    }

    static (EarthData Data, List<RiverReach> Rivers) Load(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var data = EarthData.LoadFromZip(path, false);
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            using var s = zip.GetEntry("earthdata/rivers.bin").Open();
            return (data, RiverData.Read(s));
        }
        var d = EarthData.LoadFromFolder(path, false);
        string dir = Directory.Exists(Path.Combine(path, "earthdata")) ? Path.Combine(path, "earthdata") : path;
        using var f = File.OpenRead(Path.Combine(dir, "rivers.bin"));
        return (d, RiverData.Read(f));
    }

    static LayoutInput World(string spec)
    {
        var parts = spec.ToLowerInvariant().Replace("cw", "").Split('x');
        int w = int.Parse(parts[0]), l = int.Parse(parts[1]);
        // Cylinder World's standard layout (as in the terrain tests: 256000 x 102400 -> ring 204 800 from 25 600)
        return new LayoutInput
        {
            WorldWidth = w, GutterMargin = 512,
            CwRingStart = w / 10, CwRingLength = w * 4 / 5, CwEquatorZ = l / 2, CwPoleDistance = l / 2 - 48, CwBandInner = 608,
        };
    }

    static Pattern[] Patterns(EarthGeometry geo)
    {
        var chunk = new List<(double, double)>();
        var margin = new List<(double, double)>();
        var scattered = new List<(double, double)>();
        var chunkUnits = new List<int>();
        var marginUnits = new List<int>();
        var rng = new Random(4242);
        foreach (var (lat, lon) in Anchors)
        {
            geo.FromGeo(lat, lon, out double ax, out double az);
            int c0x = (int)Math.Floor(ax / 32) - 4, c0z = (int)Math.Floor(az / 32) - 4;
            for (int cz = 0; cz < 8; cz++)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    int bx = (c0x + cx) * 32, bz = (c0z + cz) * 32;
                    // a chunk's own columns, row by row (EarthWorldGen, EarthGeology, ...)
                    chunkUnits.Add(chunk.Count);
                    for (int lz = 0; lz < 32; lz++) for (int lx = 0; lx < 32; lx++) chunk.Add((bx + lx, bz + lz));
                    // ... and, next to rivers, the settling margin of 8 blocks around it
                    marginUnits.Add(margin.Count);
                    for (int lz = 0; lz < 32; lz++) for (int lx = 0; lx < 32; lx++) margin.Add((bx + lx, bz + lz));
                    for (int ez = 0; ez < 48; ez++)
                    {
                        for (int ex = 0; ex < 48; ex++)
                        {
                            if (ex >= 8 && ex < 40 && ez >= 8 && ez < 40) continue;
                            margin.Add((bx + ex - 8, bz + ez - 8));
                        }
                    }
                }
            }
            // scattered single columns (a new cell nearly every time: the cache-miss path)
            for (int i = 0; i < 32768; i++) scattered.Add((Math.Floor(ax + rng.NextDouble() * 8192 - 4096), Math.Floor(az + rng.NextDouble() * 8192 - 4096)));
        }
        Pattern Make(string name, List<(double X, double Z)> p, List<int> units) => new Pattern
        {
            Name = name, X = p.Select(q => q.X).ToArray(), Z = p.Select(q => q.Z).ToArray(),
            Units = (units ?? Enumerable.Range(0, (p.Count + 1023) / 1024).Select(i => i * 1024).ToList()).ToArray(),
        };
        return new[] { Make("chunk", chunk, chunkUnits), Make("chunk+margin", margin, marginUnits), Make("scattered", scattered, null) };
    }

    static double Median(List<double> v)
    {
        var s = v.OrderBy(a => a).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static bool Same(in Probe a, in Probe b) =>
        a.Found == b.Found && (!a.Found || (BitConverter.DoubleToInt64Bits(a.Dist) == BitConverter.DoubleToInt64Bits(b.Dist)
            && BitConverter.DoubleToInt64Bits(a.Q) == BitConverter.DoubleToInt64Bits(b.Q)
            && BitConverter.DoubleToInt64Bits(a.DirX) == BitConverter.DoubleToInt64Bits(b.DirX)
            && BitConverter.DoubleToInt64Bits(a.DirZ) == BitConverter.DoubleToInt64Bits(b.DirZ)
            && a.WaterTop == b.WaterTop && a.Ramp == b.Ramp && a.Rapid == b.Rapid && a.Mouth == b.Mouth));

    public static void Run(string[] args)
    {
        string dataPath = Arg(args, "--data", null) ?? FindData();
        string output = Arg(args, "--out", null);
        int passes = int.Parse(Arg(args, "--passes", "5"));
        int threads = int.Parse(Arg(args, "--threads", Math.Min(4, Environment.ProcessorCount).ToString()));
        bool noLegacy = args.Contains("--no-legacy");
        var worlds = Arg(args, "--worlds", "256000x102400,512000x204800").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var only = Arg(args, "--engines", null)?.Split(',');
        var factories = Factories.Where(f => !(noLegacy && f.Legacy) && (only == null || only.Contains(f.Name))).ToList();
        Console.WriteLine($"River lookup benchmark: data {dataPath}; {passes} passes; {threads} threads; engines {string.Join(", ", factories.Select(f => f.Name))}");
        var (data, rivers) = Load(dataPath);
        var report = new List<object>();
        bool allEqual = true;
        foreach (var spec in worlds)
        {
            var geo = EarthLayout.Build(World(spec), data, out var layout);
            var model = new TerrainModel(data, geo, VerticalScale.ForWorld(384, 165), 290142474) { GutterSeabedY = 125 };
            double scale = geo.RingLength / 204800.0;
            Console.WriteLine();
            Console.WriteLine($"== world {spec}: river size scale {scale:0.###} ({layout})");
            var patterns = Patterns(geo);
            var engines = new List<Engine>();
            var builds = new List<object>();
            for (int e = 0; e < factories.Count; e++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalMemory(true), allocBefore = GC.GetTotalAllocatedBytes(true);
                var watch = Stopwatch.StartNew();
                var engine = factories[e].Build(rivers, geo, model.RiverWaterTop, scale);
                watch.Stop();
                long allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
                long retained = GC.GetTotalMemory(true) - before;
                engine.Name = factories[e].Name;
                engines.Add(engine);
                builds.Add(new { engine = engine.Name, buildMs = watch.Elapsed.TotalMilliseconds, retainedBytes = retained, allocatedBytes = allocated, engine.Segments });
                Console.WriteLine($"  build {engine.Name,-12} {watch.Elapsed.TotalMilliseconds,8:F0} ms, retained {retained / 1e6,7:F1} MB, allocated {allocated / 1e6,8:F0} MB, {engine.Segments:N0} segments");
            }

            // equivalence: every Hit field, bit for bit, on every point of every pattern, and DistanceToWater
            var reference = engines[0];
            var refProbes = new Dictionary<string, Probe[]>();
            long compared = 0, found = 0, differences = 0;
            foreach (var p in patterns)
            {
                var probes = new Probe[p.X.Length];
                for (int i = 0; i < probes.Length; i++) { probes[i] = reference.Probe(p.X[i], p.Z[i]); if (probes[i].Found) found++; }
                refProbes[p.Name] = probes;
                foreach (var engine in engines.Skip(1))
                {
                    for (int i = 0; i < probes.Length; i++)
                    {
                        compared++;
                        if (!Same(engine.Probe(p.X[i], p.Z[i]), probes[i]))
                        {
                            if (differences++ < 5) Console.WriteLine($"  DIFFERENCE {engine.Name} vs {reference.Name}: {p.Name} at {p.X[i]},{p.Z[i]}");
                        }
                    }
                }
            }
            long distanceCompared = 0;
            var chunkPattern = patterns[0];
            foreach (double within in new[] { 0d, 16, 64, 300 })
            {
                for (int i = 0; i < chunkPattern.X.Length; i += 7)
                {
                    double a = reference.Distance(chunkPattern.X[i], chunkPattern.Z[i], within);
                    foreach (var engine in engines.Skip(1))
                    {
                        distanceCompared++;
                        double b = engine.Distance(chunkPattern.X[i], chunkPattern.Z[i], within);
                        if (BitConverter.DoubleToInt64Bits(a) != BitConverter.DoubleToInt64Bits(b) && differences++ < 5)
                            Console.WriteLine($"  DISTANCE DIFFERENCE {engine.Name}: within {within} at {chunkPattern.X[i]},{chunkPattern.Z[i]}: {b} / {a}");
                    }
                }
            }
            allEqual &= differences == 0;
            Console.WriteLine($"  equivalence vs {reference.Name}: {compared:N0} Nearest ({found:N0} reference hits) + {distanceCompared:N0} DistanceToWater comparisons, {differences} differences");

            // single-threaded timing: engines interleaved per pass (rotating order), two warm-up passes
            var timings = new List<object>();
            foreach (var p in patterns)
            {
                var times = engines.Select(_ => new List<double>()).ToList();
                for (int pass = -2; pass < passes; pass++)
                {
                    for (int k = 0; k < engines.Count; k++)
                    {
                        int e = (k + Math.Max(0, pass)) % engines.Count;
                        var watch = Stopwatch.StartNew();
                        long checksum = engines[e].NearestRange(p.X, p.Z, 0, p.X.Length);
                        watch.Stop();
                        GC.KeepAlive(checksum);
                        if (pass >= 0) times[e].Add(watch.Elapsed.TotalMilliseconds);
                    }
                }
                for (int e = 0; e < engines.Count; e++)
                {
                    double med = Median(times[e]), min = times[e].Min();
                    timings.Add(new { pattern = p.Name, engine = engines[e].Name, queries = p.X.Length, medianMs = med, minMs = min, nsPerQuery = med * 1e6 / p.X.Length });
                    Console.WriteLine($"  Nearest {p.Name,-13} {engines[e].Name,-12} {p.X.Length,8:N0} q: median {med,7:F1} ms, min {min,7:F1} ms = {med * 1e6 / p.X.Length,6:F0} ns/q");
                }
            }
            {
                var times = engines.Select(_ => new List<double>()).ToList();
                for (int pass = -1; pass < passes; pass++)
                {
                    for (int k = 0; k < engines.Count; k++)
                    {
                        int e = (k + Math.Max(0, pass)) % engines.Count;
                        var watch = Stopwatch.StartNew();
                        GC.KeepAlive(engines[e].DistanceAll(chunkPattern.X, chunkPattern.Z, 64));
                        watch.Stop();
                        if (pass >= 0) times[e].Add(watch.Elapsed.TotalMilliseconds);
                    }
                }
                for (int e = 0; e < engines.Count; e++)
                {
                    double med = Median(times[e]);
                    timings.Add(new { pattern = "DistanceToWater(64) chunk", engine = engines[e].Name, queries = chunkPattern.X.Length, medianMs = med, minMs = times[e].Min(), nsPerQuery = med * 1e6 / chunkPattern.X.Length });
                    Console.WriteLine($"  DistanceToWater(64)   {engines[e].Name,-12} {chunkPattern.X.Length,8:N0} q: median {med,7:F1} ms, min {times[e].Min(),7:F1} ms = {med * 1e6 / chunkPattern.X.Length,6:F0} ns/q");
                }
            }

            // multi-threaded: chunks handed out to worker threads as world generation does; results must match too
            foreach (var p in patterns.Take(2))
            {
                var probes = refProbes[p.Name];
                for (int e = 0; e < engines.Count; e++)
                {
                    var engine = engines[e];
                    var watchTimes = new List<double>();
                    long mismatches = 0;
                    for (int pass = -1; pass < passes; pass++)
                    {
                        var watch = Stopwatch.StartNew();
                        Parallel.For(0, p.Units.Length, new ParallelOptions { MaxDegreeOfParallelism = threads }, u =>
                        {
                            int a = p.Units[u], b = u + 1 < p.Units.Length ? p.Units[u + 1] : p.X.Length;
                            if (pass < 0)
                            {
                                for (int i = a; i < b; i++) if (!Same(engine.Probe(p.X[i], p.Z[i]), probes[i])) Interlocked.Increment(ref mismatches);
                            }
                            else GC.KeepAlive(engine.NearestRange(p.X, p.Z, a, b));
                        });
                        watch.Stop();
                        if (pass >= 0) watchTimes.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    if (mismatches > 0) { allEqual = false; Console.WriteLine($"  THREADED DIFFERENCES {engine.Name}: {mismatches}"); }
                    double med = Median(watchTimes);
                    timings.Add(new { pattern = p.Name + " x" + threads + " threads", engine = engine.Name, queries = p.X.Length, medianMs = med, minMs = watchTimes.Min(), nsPerQuery = med * 1e6 / p.X.Length, mismatches });
                    Console.WriteLine($"  Nearest {p.Name,-13} {engine.Name,-12} x{threads} threads: median {med,7:F1} ms ({mismatches} mismatches)");
                }
            }
            report.Add(new { world = spec, sizeScale = scale, layout, builds, equivalence = new { reference = reference.Name, compared, found, distanceCompared, differences }, timings });
            engines.Clear();
            GC.Collect();
        }
        if (output != null)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { status = allEqual ? "PASS" : "FAIL", data = dataPath, passes, threads, worlds = report }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Report: " + output);
        }
        if (!allEqual) throw new Exception("River lookup results differ between engines");
        Console.WriteLine(allEqual ? "PASS: all engines bit-identical" : "FAIL");
    }
}
