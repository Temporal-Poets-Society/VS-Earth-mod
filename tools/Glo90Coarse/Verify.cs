using System.Diagnostics;
using System.Text.Json;
using EarthMap.Core;

/// <summary>
/// CoarseElevation against full GLO-90 (FineElevation on the store) in eight windows, on GLO-90's own points about every
/// 100 m; the roughness layer against the actual spread there; continuity across tile, band and ±180° edges; parallel
/// lookups against serial ones; speed and memory.
///   verify <folder with glo90c files> <GLO-90 store> <glo90-tiles.bin> [out.json]
/// </summary>
static class Verify
{
    static readonly (string name, double lat0, double lat1, double lon0, double lon1)[] Windows =
    {
        ("Everest", 27.85, 28.15, 86.75, 87.10),
        ("Kali Gandaki / Dhaulagiri", 28.55, 28.85, 83.40, 83.80),
        ("Matterhorn", 45.90, 46.10, 7.55, 7.85),
        ("Grand Canyon", 36.00, 36.30, -112.30, -111.90),
        ("Middle Rhine", 50.00, 50.30, 7.55, 7.85),
        ("Gauja / Sigulda", 57.10, 57.25, 24.75, 25.00),
        ("Naeroyfjord", 60.80, 61.00, 6.80, 7.20),
        ("Netherlands coast", 51.80, 52.20, 4.00, 4.60),
    };

    public static int Run(string[] a)
    {
        string folder = a[1], store = a[2], list = a[3];
        string outPath = a.Length > 4 ? a[4] : null;
        long mem0 = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var coarse = CoarseElevation.TryOpen(name =>
        {
            string p = Path.Combine(folder, name);
            return File.Exists(p) ? File.OpenRead(p) : null;
        });
        double openMs = sw.Elapsed.TotalMilliseconds;
        if (coarse == null) { Console.WriteLine("no coarse heights in " + folder); return 1; }
        long memOpen = GC.GetTotalMemory(true) - mem0;
        Console.WriteLine($"opened in {openMs:F0} ms: {coarse.TileCount} tiles, {coarse.PayloadBytes / 1e6:F1} MB payload, managed memory +{memOpen / 1e6:F1} MB; " +
            $"grid {coarse.SpacingArcSeconds}\" ({coarse.CellMetres:F1} m), roughness {coarse.RoughnessSpacingArcSeconds}\"");
        Console.WriteLine(coarse.Note);
        // TryOpen: null when a file isn't there (null from the opener, or FileNotFoundException as EarthDataPacks throws for a
        // pack that isn't installed); the payload served as EarthDataPacks does (a read-only MemoryStream of the whole file)
        Stream Open(string name) { string p = Path.Combine(folder, name); return File.Exists(p) ? File.OpenRead(p) : null; }
        bool noIndex = CoarseElevation.TryOpen(n => n == CoarseElevation.IndexFile ? null : Open(n)) == null;
        bool noPart = CoarseElevation.TryOpen(n => n == CoarseElevation.PartFile(3) ? null : Open(n)) == null;
        bool thrown = CoarseElevation.TryOpen(n => n == CoarseElevation.PartFile(2) ? throw new FileNotFoundException("Earth requires the enabled data pack earthheightdata2") : Open(n)) == null;
        var viaMemory = CoarseElevation.TryOpen(n => { var s = Open(n); if (s == null) return null; using (s) { var m = new MemoryStream(); s.CopyTo(m); return new MemoryStream(m.ToArray(), false); } });
        bool sea = !coarse.TryHeight(0.5, -150.5, out _) && !coarse.TryRoughness(0.5, -150.5, out _);
        bool pole1 = coarse.TryHeight(-90, 0, out double poleH), pole2 = coarse.TryHeight(-89.999, 123, out double poleH2), pole = pole1 && pole2;
        bool sameMem = viaMemory != null && viaMemory.TryHeight(27.988, 86.925, out double em) && coarse.TryHeight(27.988, 86.925, out double ef) && em == ef;
        Console.WriteLine($"TryOpen: no index -> null {noIndex}; part 3 missing -> null {noPart}; part 2 throws FileNotFoundException -> null {thrown}; " +
            $"read-only MemoryStreams -> same heights {sameMem}; mid-Pacific (0.5 N 150.5 W) no height or roughness {sea}; South Pole {(pole ? $"{poleH:F0} m and {poleH2:F0} m" : "none")}");
        bool apiOk = noIndex && noPart && thrown && sameMem && sea && pole;
        viaMemory = null;
        var fine = new FineElevation(store, File.ReadAllBytes(list)) { Capacity = 64 };
        var results = new List<object>();
        var allPoints = new List<(double lat, double lon)>();

        Console.WriteLine();
        Console.WriteLine("| Window | GLO-90 land points | no coarse value | RMS (m) | mean abs (m) | mean (m) | max abs (m) | 95 % abs (m) | highest GLO-90 / coarse (m) | RMS spread / roughness (m) | corr. log | median ratio |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var w in Windows)
        {
            int rowsN = DemTiles.Glo90Rows;
            var diffs = new List<double>();
            int missing = 0;
            double maxF = double.MinValue, maxC = double.MinValue;
            var boxes = new Dictionary<long, (double e2, double r2, int n)>();
            var rg = coarse.RoughnessGrid;
            // GLO-90's own points: rows every 3"; columns at the band's spacing, strided to about 100 m
            int k0 = (int)Math.Ceiling(w.lat0 * rowsN), k1 = (int)Math.Floor(w.lat1 * rowsN);
            for (int k = k0; k <= k1; k++)
            {
                double lat = (double)k / rowsN;
                int latS = (int)Math.Ceiling(lat) - 1;
                int cols = DemTiles.Glo90Cols(latS);
                double dx = 3600.0 / cols * 30.87 * Math.Cos(lat * Math.PI / 180);
                int stride = Math.Max(1, (int)Math.Round(100 / dx));
                int c0 = (int)Math.Ceiling(w.lon0 * cols), c1 = (int)Math.Floor(w.lon1 * cols);
                for (int c = c0; c <= c1; c += stride)
                {
                    double lon = (double)c / cols;
                    if (!fine.TryHeight(lat, lon, out double f)) throw new Exception($"no GLO-90 tile at {lat}, {lon}");
                    if (f == 0) continue; // sea (and land rounded to 0 m in the store)
                    allPoints.Add((lat, lon));
                    if (!coarse.TryHeight(lat, lon, out double h)) { missing++; continue; }
                    double d = h - f;
                    diffs.Add(d);
                    maxF = Math.Max(maxF, f); maxC = Math.Max(maxC, h);
                    if (coarse.TryRoughness(lat, lon, out double rough))
                    {
                        int g = (int)Math.Floor(rg.RowY(lat) + 0.5);
                        int j = (int)Math.Floor(rg.ColX(rg.Band(g), lon) + 0.5);
                        long key = (long)g << 32 | (uint)j;
                        boxes.TryGetValue(key, out var bx);
                        boxes[key] = (bx.e2 + d * d, bx.r2 + rough * rough, bx.n + 1);
                    }
                }
            }
            var abs = diffs.Select(Math.Abs).OrderBy(x => x).ToList();
            double rms = Math.Sqrt(diffs.Average(x => x * x)), mae = abs.Average(), mean = diffs.Average(), max = abs[^1], p95 = abs[(int)(0.95 * (abs.Count - 1))];
            // roughness: per roughness cell, the actual RMS of GLO-90 minus the coarse surface against the layer's value
            var bl = boxes.Values.Where(b => b.n >= 20).Select(b => (actual: Math.Sqrt(b.e2 / b.n), layer: Math.Sqrt(b.r2 / b.n))).ToList();
            double spreadAll = Math.Sqrt(boxes.Values.Sum(b => b.e2) / boxes.Values.Sum(b => b.n));
            double layerAll = Math.Sqrt(boxes.Values.Sum(b => b.r2) / boxes.Values.Sum(b => b.n));
            double corr = Corr(bl.Select(b => Math.Log(Math.Max(0.1, b.actual))).ToArray(), bl.Select(b => Math.Log(Math.Max(0.1, b.layer))).ToArray());
            var ratios = bl.Where(b => b.layer > 0).Select(b => b.actual / b.layer).OrderBy(x => x).ToList();
            double medRatio = ratios.Count > 0 ? ratios[ratios.Count / 2] : double.NaN;
            Console.WriteLine($"| {w.name} | {diffs.Count + missing:N0} | {missing} | {rms:F1} | {mae:F1} | {mean:+0.0;-0.0} | {max:F0} | {p95:F1} | {maxF:F0} / {maxC:F0} | {spreadAll:F1} / {layerAll:F1} | {corr:F2} | {medRatio:F2} |");
            results.Add(new { w.name, w.lat0, w.lat1, w.lon0, w.lon1, points = diffs.Count + missing, missing, rms, meanAbs = mae, mean, maxAbs = max, p95Abs = p95, highestGlo90 = maxF, highestCoarse = maxC,
                spreadRms = spreadAll, roughnessRms = layerAll, roughnessCells = bl.Count, logCorrelation = corr, medianActualOverLayer = medRatio });
        }

        // continuity: across tile edges, the band edges at 50°/60° N and ±180°
        Console.WriteLine();
        var jumps = new List<object>();
        void Jump(string what, double latA, double lonA, double latB, double lonB)
        {
            bool okA = coarse.TryHeight(latA, lonA, out double ha), okB = coarse.TryHeight(latB, lonB, out double hb);
            Console.WriteLine($"continuity {what}: {(okA ? ha.ToString("F3") : "none")} / {(okB ? hb.ToString("F3") : "none")}");
            jumps.Add(new { what, a = okA ? ha : double.NaN, b = okB ? hb : double.NaN });
        }
        const double eps = 1e-9;
        Jump("tile edge 46° N at 7.7° E", 46 + eps, 7.7, 46 - eps, 7.7);
        Jump("tile edge 8° E at 45.95° N", 45.95, 8 - eps, 45.95, 8 + eps);
        Jump("band edge 50° N (1200 | 800 GLO-90 columns) at 7.7° E", 50 + eps, 7.7, 50 - eps, 7.7);
        Jump("band edge 60° N at 7.1° E", 60 + eps, 7.1, 60 - eps, 7.1);
        Jump("antimeridian at 65.5° N (Chukotka)", 65.5, 180 - eps, 65.5, -180 + eps);
        Jump("antimeridian at 65.5° N, lon 540", 65.5, 540, 65.5, 180);
        // the largest jump between points 1 m apart along lines across many tile edges
        double worst = 0; string worstAt = "";
        foreach (double lat in new[] { 46.2, 50.0 - 1e-6, 60.0 + 1e-6, 27.95 })
        {
            double step = 1.0 / 111000;
            double prev = double.NaN;
            for (double lon = 5; lon < 15; lon += step * 7)
            {
                if (!coarse.TryHeight(lat, lon, out double h)) { prev = double.NaN; continue; }
                if (!double.IsNaN(prev) && Math.Abs(h - prev) > worst) { worst = Math.Abs(h - prev); worstAt = $"{lat:F6} N {lon:F6} E"; }
                prev = h;
            }
        }
        Console.WriteLine($"largest change between neighbouring points 7 m apart along four parallels from 5° to 15° E: {worst:F2} m at {worstAt}");

        // parallel lookups against serial ones, with a fresh reader whose cache is far too small (constant eviction)
        var rnd = new Random(12345);
        var pts = new List<(double lat, double lon)>();
        for (int i = 0; i < 300000; i++) pts.Add(allPoints[rnd.Next(allPoints.Count)]);
        for (int i = 0; i < 300000; i++) pts.Add((rnd.NextDouble() * 140 - 60, rnd.NextDouble() * 360 - 180));
        var serialH = new double[pts.Count]; var serialR = new double[pts.Count]; var serialOk = new byte[pts.Count];
        coarse.ClearCache();
        sw.Restart();
        for (int i = 0; i < pts.Count; i++)
        {
            bool a1 = coarse.TryHeight(pts[i].lat, pts[i].lon, out serialH[i]);
            bool a2 = coarse.TryRoughness(pts[i].lat, pts[i].lon, out serialR[i]);
            serialOk[i] = (byte)((a1 ? 1 : 0) | (a2 ? 2 : 0));
        }
        double serialColdMs = sw.Elapsed.TotalMilliseconds;
        var other = CoarseElevation.TryOpen(name => { string p = Path.Combine(folder, name); return File.Exists(p) ? File.OpenRead(p) : null; });
        other.CacheBytes = 2L << 20;
        int diffsPar = 0;
        sw.Restart();
        Parallel.For(0, pts.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            bool a1 = other.TryHeight(pts[i].lat, pts[i].lon, out double h);
            bool a2 = other.TryRoughness(pts[i].lat, pts[i].lon, out double r);
            byte ok = (byte)((a1 ? 1 : 0) | (a2 ? 2 : 0));
            if (ok != serialOk[i] || BitConverter.DoubleToInt64Bits(h) != BitConverter.DoubleToInt64Bits(serialH[i]) || BitConverter.DoubleToInt64Bits(r) != BitConverter.DoubleToInt64Bits(serialR[i]))
                Interlocked.Increment(ref diffsPar);
        });
        double parSmallMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"parallel ({Environment.ProcessorCount} threads, 2 MB cache, {other.Decodes:N0} tile decodes) against serial: {diffsPar} of {pts.Count:N0} lookups differ");
        int diffsWarm = 0;
        Parallel.For(0, pts.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            bool a1 = coarse.TryHeight(pts[i].lat, pts[i].lon, out double h);
            if (a1 != ((serialOk[i] & 1) != 0) || BitConverter.DoubleToInt64Bits(h) != BitConverter.DoubleToInt64Bits(serialH[i])) Interlocked.Increment(ref diffsWarm);
        });
        Console.WriteLine($"parallel on the serial reader's warm cache: {diffsWarm} differ");

        // speed: warm lookups in one window (generation-like locality), serial and parallel; cold decodes
        var local = allPoints.Where(p => p.lat > 45.9 && p.lat < 46.1 && p.lon > 7.55 && p.lon < 7.85).ToList();
        coarse.ClearCache();
        foreach (var p in local) coarse.TryHeight(p.lat, p.lon, out _);
        int reps = Math.Max(1, 2000000 / local.Count);
        double sink = 0;
        sw.Restart();
        for (int r = 0; r < reps; r++) foreach (var p in local) { coarse.TryHeight(p.lat + r * 1e-7, p.lon, out double h); sink += h; }
        double nsHeight = sw.Elapsed.TotalMilliseconds * 1e6 / (reps * local.Count);
        sw.Restart();
        for (int r = 0; r < reps; r++) foreach (var p in local) { coarse.TryRoughness(p.lat + r * 1e-7, p.lon, out double h); sink += h; }
        double nsRough = sw.Elapsed.TotalMilliseconds * 1e6 / (reps * local.Count);
        var coast = allPoints.Where(p => p.lat > 51.8 && p.lat < 52.2 && p.lon > 4.0 && p.lon < 4.6).ToList();
        sw.Restart();
        for (int r = 0; r < reps; r++) foreach (var p in coast) { coarse.TryHeight(p.lat + r * 1e-7, p.lon, out double h); sink += h; }
        double nsCoast = sw.Elapsed.TotalMilliseconds * 1e6 / (reps * coast.Count);
        long n = (long)reps * local.Count;
        sw.Restart();
        Parallel.For(0, Environment.ProcessorCount, t =>
        {
            double s = 0;
            for (int r = 0; r < reps; r++) foreach (var p in local) { coarse.TryHeight(p.lat + r * 1e-7 + t * 1e-8, p.lon, out double h); s += h; }
        });
        double nsPar = sw.Elapsed.TotalMilliseconds * 1e6 / n; // each thread made n lookups in this time
        double mlpsPar = n * Environment.ProcessorCount / sw.Elapsed.TotalSeconds / 1e6;
        // cold: decode 3,000 random tiles' heights and roughness
        var keys = new List<(int, int)>();
        for (int la = -60; la < 80; la++) for (int lo = -180; lo < 180; lo++) if (coarse.HasTile(la, lo)) keys.Add((la, lo));
        var pick = keys.OrderBy(_ => rnd.Next()).Take(3000).ToList();
        coarse.ClearCache();
        long d0 = coarse.Decodes;
        sw.Restart();
        foreach (var (la, lo) in pick) { coarse.TryHeight(la + 0.5, lo + 0.5, out _); }
        double msDecodeH = sw.Elapsed.TotalMilliseconds / Math.Max(1, coarse.Decodes - d0);
        d0 = coarse.Decodes;
        sw.Restart();
        foreach (var (la, lo) in pick) { coarse.TryRoughness(la + 0.5, lo + 0.5, out _); }
        double msDecodeR = sw.Elapsed.TotalMilliseconds / Math.Max(1, coarse.Decodes - d0);
        // memory: the decoded tiles the default cache keeps, measured on the managed heap (FineElevation's tiles dropped first)
        long otherDecodes = other.Decodes;
        fine = null; other = null;
        coarse.ClearCache();
        long memEmpty = GC.GetTotalMemory(true);
        foreach (var (la, lo) in keys) { coarse.TryHeight(la + 0.5, lo + 0.5, out _); coarse.TryRoughness(la + 0.5, lo + 0.5, out _); }
        long memFull = GC.GetTotalMemory(true) - memEmpty;
        Console.WriteLine($"lookups: height {nsHeight:F0} ns (coast {nsCoast:F0} ns), roughness {nsRough:F0} ns, warm, one thread; height {nsPar:F0} ns per lookup on each of {Environment.ProcessorCount} threads at once ({mlpsPar:F0} million lookups/s in all)");
        Console.WriteLine($"first look at a tile: heights decoded in {msDecodeH:F2} ms, roughness in {msDecodeR:F2} ms (with the lookup); serial run of {pts.Count:N0} mixed lookups from a cold cache {serialColdMs:F0} ms");
        Console.WriteLine($"memory: payload {coarse.PayloadBytes / 1e6:F1} MB; after touching all {keys.Count:N0} tiles (60° S to 80° N) the cache holds {coarse.CachedBytes / 1e6:F1} MB " +
            $"(limit {coarse.CacheBytes / 1e6:F1} MB); the managed heap grew by {memFull / 1e6:F1} MB while they were decoded; payload kept after opening: +{memOpen / 1e6:F1} MB");
        var res = new
        {
            apiOk, openMs, payloadMB = coarse.PayloadBytes / 1e6, managedAfterOpenMB = memOpen / 1e6, managedWithFullCacheMB = memFull / 1e6, cacheLimitMB = coarse.CacheBytes / 1e6,
            spacingArcSec = coarse.SpacingArcSeconds, cellMetres = coarse.CellMetres, roughnessArcSec = coarse.RoughnessSpacingArcSeconds,
            windows = results, continuity = jumps, worstNeighbourChangeM = worst, worstAt,
            parallelLookups = pts.Count, parallelDiffer = diffsPar, parallelWarmDiffer = diffsWarm, parallelSmallCacheDecodes = otherDecodes,
            nsHeight, nsCoast, nsRough, nsParallelPerThread = nsPar, millionLookupsPerSecondParallel = mlpsPar, tilesTouched = keys.Count, cachedMB = coarse.CachedBytes / 1e6, msDecodeHeights = msDecodeH, msDecodeRoughness = msDecodeR, serialColdMs, sink,
        };
        if (outPath != null) File.WriteAllText(outPath, JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }));
        return diffsPar == 0 && diffsWarm == 0 && apiOk ? 0 : 1;
    }

    static double Corr(double[] x, double[] y)
    {
        if (x.Length < 3) return double.NaN;
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
        return sxy / Math.Sqrt(sxx * syy);
    }
}
