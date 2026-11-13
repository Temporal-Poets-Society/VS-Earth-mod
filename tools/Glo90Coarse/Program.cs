using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarthMap.Core;
using Glo90Coarse;

// Copernicus GLO-90 averaged to a coarse grid for Earth's offline default heights (src/Core/CoarseElevation.cs).
//   Glo90Coarse measure <tif folder> <every Nth tile> <out.json>
//       sizes of candidate grids, height steps and roughness layers on a sample of tiles
//   Glo90Coarse build <tif folder> <work folder> [--rows 150] [--rough-rows 75] [--steps -512:1,256:2,1024:4,2560:8]
//       [--rough-codes 16] [--rough-first 1] [--rough-last 1024] [--quality 11] [--workers 32]
//       pass 1: land means -> height codes; pass 2: GLO-90 minus the interpolated grid -> roughness; payloads cached
//   Glo90Coarse split <work folder> <out folder> <part limits, bytes, comma-separated; last 0 = the rest>
//       the index and parts from the cached payloads
//   Glo90Coarse verify <folder with glo90c files> <GLO-90 store> <glo90-tiles.bin> [out.json]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 1) { Console.WriteLine("usage: Glo90Coarse measure|build|split|verify ..."); return 2; }
return args[0] switch
{
    "measure" => Tool.Measure(args),
    "build" => Tool.Build(args),
    "split" => Tool.Split(args),
    "verify" => Verify.Run(args),
    "measure2" => Measure2.Run(args),
    _ => 2,
};

static class Tool
{
    public static string Opt(string[] a, string name, string def)
    {
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static List<(int latS, int lonW, string path)> SourceTiles(string folder)
    {
        var res = new List<(int, int, string)>();
        foreach (var p in Directory.EnumerateFiles(folder, "Copernicus_DSM_COG_30_*_DEM.tif"))
            if (DemTiles.TryParseSourceName(Path.GetFileNameWithoutExtension(p), out int la, out int lo)) res.Add((la, lo, p));
        // north to south, west to east
        res.Sort((a, b) => a.Item1 != b.Item1 ? b.Item1.CompareTo(a.Item1) : a.Item2.CompareTo(b.Item2));
        return res;
    }

    public static DemTiles.Grid ReadTile(string path, int latS, int lonW)
    {
        var g = DemTiles.ReadGeoTiff(File.ReadAllBytes(path));
        if (g.Rows != DemTiles.Glo90Rows || g.Cols != DemTiles.Glo90Cols(latS) || Math.Abs(g.Lat0 - (latS + 1)) > 1e-6 || Math.Abs(g.Lon0 - lonW) > 1e-6)
            throw new InvalidDataException($"{path}: {g.Cols} x {g.Rows} at {g.Lat0}, {g.Lon0}");
        return g;
    }

    public static CoarseElevation.Steps ParseSteps(string spec, double top = 9000)
    {
        var from = new List<float>(); var step = new List<float>();
        foreach (var s in spec.Split(','))
        {
            var p = s.Split(':');
            from.Add(float.Parse(p[0], CultureInfo.InvariantCulture)); step.Add(float.Parse(p[1], CultureInfo.InvariantCulture));
        }
        return new CoarseElevation.Steps(from.ToArray(), step.ToArray(), top);
    }

    // ---------------------------------------------------------------------------------------------- measure

    sealed class Acc
    {
        public long Bytes;
        public double[] Err2 = new double[4];
        public long[] N = new long[4];
    }

    static int HClass(double h) => h < 200 ? 0 : h < 1000 ? 1 : h < 3000 ? 2 : 3;

    public static int Measure(string[] a)
    {
        string src = a[1]; int every = int.Parse(a[2]); string outPath = a[3];
        var all = SourceTiles(src);
        var sample = new List<(int latS, int lonW, string path)>();
        for (int i = 0; i < all.Count; i += every) sample.Add(all[i]);
        Console.WriteLine($"{all.Count} tiles, sampling {sample.Count}");
        var grids = new (string name, int rows, bool square)[]
        {
            ("30s-square", 120, true), ("24s-bands", 150, false), ("24s-square", 150, true), ("22.5s-square", 160, true),
            ("20s-square", 180, true), ("18s-square", 200, true),
        };
        var stepSets = new (string name, string spec)[]
        {
            ("q1", "-512:1"), ("q2", "-512:2"), ("q4", "-512:4"),
            ("c1 1m<256 2m<1024 4m<2560 8m", "-512:1,256:2,1024:4,2560:8"),
            ("c2 1m<128 2m<512 4m<1536 8m<3072 16m", "-512:1,128:2,512:4,1536:8,3072:16"),
            ("c3 2m<512 4m<2048 8m", "-512:2,512:4,2048:8"),
            ("c4 1m<512 2m<2048 4m", "-512:1,512:2,2048:4"),
            ("c5 1m<256 2m<768 4m<1792 8m<3840 16m", "-512:1,256:2,768:4,1792:8,3840:16"),
        };
        var steps = stepSets.Select(s => ParseSteps(s.spec)).ToArray();
        var rtables = new (string name, float[] table)[]
        {
            ("4-bit 1..1024 m", CoarseElevation.RoughnessTable(16, 1, 1024)),
            ("5-bit 0.5..1024 m", CoarseElevation.RoughnessTable(32, 0.5, 1024)),
        };
        var acc = new Acc[grids.Length, steps.Length];
        for (int g = 0; g < grids.Length; g++) for (int s = 0; s < steps.Length; s++) acc[g, s] = new Acc();
        var rough = new long[grids.Length, 2, rtables.Length];
        var q11 = new long[grids.Length, 2];
        var cells = new long[grids.Length]; var land = new long[grids.Length];
        long nativeLand = 0, nativeAll = 0;
        double readMs = 0;
        int done = 0;
        var sw = Stopwatch.StartNew();
        var gate = new object();
        Parallel.ForEach(sample, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, t =>
        {
            var tsw = Stopwatch.StartNew();
            var tile = ReadTile(t.path, t.latS, t.lonW);
            double ms = tsw.Elapsed.TotalMilliseconds;
            long nl = 0;
            foreach (float v in tile.Heights) if (Coarsen.IsLand(v)) nl++;
            var local = new (long bytes, double[] e2, long[] n)[grids.Length, steps.Length];
            var lr = new long[grids.Length, 2, rtables.Length];
            var lq = new long[grids.Length, 2];
            var lc = new long[grids.Length]; var ll = new long[grids.Length];
            for (int g = 0; g < grids.Length; g++)
            {
                var spec = grids[g];
                var grid = spec.square ? CoarseElevation.Grid.Square(spec.rows, DemTiles.Glo90Cols)
                                       : Bands(spec.rows);
                int band = t.latS + 90, R = grid.RowsPerDegree, C = grid.Cols[band];
                var mean = new double[R * C];
                Coarsen.Means(tile.Heights, tile.Rows, tile.Cols, R, C, mean, null);
                var isLand = new bool[R * C];
                for (int i = 0; i < mean.Length; i++) isLand[i] = !double.IsNaN(mean[i]);
                lc[g] = mean.Length; ll[g] = isLand.Count(x => x);
                for (int s = 0; s < steps.Length; s++)
                {
                    var codes = new int[mean.Length];
                    var e2 = new double[4]; var n = new long[4];
                    for (int i = 0; i < mean.Length; i++)
                    {
                        if (!isLand[i]) continue;
                        codes[i] = steps[s].Code(mean[i]);
                        double e = steps[s].Metres(codes[i]) - mean[i];
                        int hc = HClass(mean[i]);
                        e2[hc] += e * e; n[hc]++;
                    }
                    byte[] p = BrotliCodec.Heights(codes, isLand, C, R, 9);
                    local[g, s] = (p.Length, e2, n);
                    if (s == 3) lq[g, 0] = BrotliCodec.Heights(codes, isLand, C, R, 11).Length;
                    if (s == 7) lq[g, 1] = BrotliCodec.Heights(codes, isLand, C, R, 11).Length;
                }
                // roughness: on this grid and on one with half the rows
                for (int half = 0; half < 2; half++)
                {
                    var rg = half == 0 ? grid : CoarseElevation.Grid.Square(spec.rows / 2, DemTiles.Glo90Cols);
                    int RR = rg.RowsPerDegree, RC = rg.Cols[band];
                    var spread = Coarsen.PlaneSpread(tile.Heights, tile.Rows, tile.Cols, RR, RC);
                    for (int q = 0; q < rtables.Length; q++)
                    {
                        var codes = new int[spread.Length];
                        for (int i = 0; i < spread.Length; i++) codes[i] = CoarseElevation.RoughnessCode(rtables[q].table, spread[i]);
                        lr[g, half, q] = BrotliCodec.Roughness(codes, RC, RR, 9).Length;
                    }
                }
            }
            lock (gate)
            {
                readMs += ms; nativeLand += nl; nativeAll += tile.Heights.Length;
                for (int g = 0; g < grids.Length; g++)
                {
                    cells[g] += lc[g]; land[g] += ll[g];
                    q11[g, 0] += lq[g, 0]; q11[g, 1] += lq[g, 1];
                    for (int s = 0; s < steps.Length; s++)
                    {
                        acc[g, s].Bytes += local[g, s].bytes;
                        for (int c = 0; c < 4; c++) { acc[g, s].Err2[c] += local[g, s].e2[c]; acc[g, s].N[c] += local[g, s].n[c]; }
                    }
                    for (int h = 0; h < 2; h++) for (int q = 0; q < rtables.Length; q++) rough[g, h, q] += lr[g, h, q];
                }
                done++;
                if (done % 100 == 0) Console.WriteLine($"{done}/{sample.Count} {sw.Elapsed.TotalSeconds:F0} s");
            }
        });
        double scale = (double)all.Count / sample.Count;
        var res = new Dictionary<string, object>
        {
            ["tilesAll"] = all.Count, ["tilesSampled"] = sample.Count, ["seconds"] = sw.Elapsed.TotalSeconds,
            ["readMsPerTile"] = readMs / sample.Count, ["nativeLandShare"] = (double)nativeLand / nativeAll,
        };
        var rows = new List<object>();
        for (int g = 0; g < grids.Length; g++)
        {
            var st = new List<object>();
            for (int s = 0; s < steps.Length; s++)
            {
                var A = acc[g, s];
                st.Add(new
                {
                    steps = stepSets[s].name,
                    MB = A.Bytes * scale / 1e6,
                    bitsPerLandCell = A.Bytes * 8.0 / land[g],
                    rmsQuantErr_lt200_200to1000_1000to3000_gt3000 = Enumerable.Range(0, 4).Select(c => A.N[c] > 0 ? Math.Sqrt(A.Err2[c] / A.N[c]) : 0).ToArray(),
                    landCellsShareByClass = Enumerable.Range(0, 4).Select(c => (double)A.N[c] / land[g]).ToArray(),
                });
            }
            rows.Add(new
            {
                grid = grids[g].name,
                cellsM = cells[g] * scale / 1e6,
                landCellsM = land[g] * scale / 1e6,
                steps = st,
                q11_c1_MB = q11[g, 0] * scale / 1e6,
                q11_c5_MB = q11[g, 1] * scale / 1e6,
                roughness = new
                {
                    sameGrid = rtables.Select((r, q) => new { r.name, MB = rough[g, 0, q] * scale / 1e6 }).ToArray(),
                    halfRows = rtables.Select((r, q) => new { r.name, MB = rough[g, 1, q] * scale / 1e6 }).ToArray(),
                },
            });
        }
        res["grids"] = rows;
        string json = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Console.WriteLine(json);
        return 0;
    }

    /// <summary>GLO-90's own bands: the coarse grid takes every k-th column as it takes every k-th row.</summary>
    static CoarseElevation.Grid Bands(int rows)
    {
        var g = new CoarseElevation.Grid { RowsPerDegree = rows };
        for (int b = 0; b < 180; b++)
        {
            int n = DemTiles.Glo90Cols(b - 90);
            g.NativeCols[b] = (ushort)n;
            g.Cols[b] = (ushort)(rows * n / 1200);
        }
        return g;
    }

    // ---------------------------------------------------------------------------------------------- build

    sealed class Built
    {
        public int LatS, LonW;
        public byte[] Heights, Rough;
        public long LandCells, Cells, LandPoints;
        public double Res2;
    }

    public static int Build(string[] a)
    {
        string src = a[1], work = a[2];
        int rows = int.Parse(Opt(a, "--rows", "150"));
        int rrows = int.Parse(Opt(a, "--rough-rows", "50"));
        string stepSpec = Opt(a, "--steps", "-512:1,64:2,256:4,1536:8");
        int rcodes = int.Parse(Opt(a, "--rough-codes", "16"));
        double rfirst = double.Parse(Opt(a, "--rough-first", "1"), CultureInfo.InvariantCulture);
        double rlast = double.Parse(Opt(a, "--rough-last", "1024"), CultureInfo.InvariantCulture);
        var scheme = CoarseElevation.Scheme.Default;
        int workers = int.Parse(Opt(a, "--workers", Environment.ProcessorCount.ToString()));
        int limit = int.Parse(Opt(a, "--limit", "0"));
        double alpha = double.Parse(Opt(a, "--sharpen", "0.3"), CultureInfo.InvariantCulture);
        Directory.CreateDirectory(work);
        var steps = ParseSteps(stepSpec);
        var hGrid = CoarseElevation.Grid.Square(rows, DemTiles.Glo90Cols);
        var rGrid = rrows > 0 ? CoarseElevation.Grid.Square(rrows, DemTiles.Glo90Cols) : null;
        var rTable = rrows > 0 ? CoarseElevation.RoughnessTable(rcodes, rfirst, rlast) : null;
        var all = SourceTiles(src);
        if (limit > 0) all = all.Take(limit).ToList();
        Console.WriteLine($"{all.Count} tiles; grid {3600.0 / rows:0.###}\" ({rows} rows/degree), steps {stepSpec}; roughness " +
            (rGrid == null ? "none" : $"{3600.0 / rrows:0.###}\" {rcodes} codes {rfirst}..{rlast} m") + $"; sharpen {alpha}; {workers} workers");
        var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
        var built = new ConcurrentDictionary<int, Built>();
        var sw = Stopwatch.StartNew();
        int done = 0;
        // pass 1: land means of every tile
        var means = new ConcurrentDictionary<int, float[]>();
        Parallel.ForEach(all, po, t =>
        {
            var tile = ReadTile(t.path, t.latS, t.lonW);
            int band = t.latS + 90, R = hGrid.RowsPerDegree, C = hGrid.Cols[band];
            var mean = new double[R * C];
            Coarsen.Means(tile.Heights, tile.Rows, tile.Cols, R, C, mean, null);
            if (Array.TrueForAll(mean, double.IsNaN)) return;
            means[(t.latS + 90) * 360 + t.lonW + 180] = Array.ConvertAll(mean, x => (float)x);
            int d = Interlocked.Increment(ref done);
            if (d % 4000 == 0) Console.WriteLine($"pass 1: {d}/{all.Count} {sw.Elapsed.TotalSeconds:F0} s");
        });
        Console.WriteLine($"pass 1: {means.Count} tiles with land, {sw.Elapsed.TotalSeconds:F0} s");

        // a cell's mean anywhere: grid row g (0 = northernmost), column j from 180° W (wraps); NaN without land
        float MeanAt(int g, int j)
        {
            if (g < 0 || g >= hGrid.Rows) return float.NaN;
            int band = hGrid.Band(g), cols = hGrid.Cols[band];
            j %= 360 * cols; if (j < 0) j += 360 * cols;
            int lonIdx = j / cols;
            return means.TryGetValue(band * 360 + lonIdx, out var m) ? m[(g % hGrid.RowsPerDegree) * cols + j - lonIdx * cols] : float.NaN;
        }

        // pass 1b: sharpen against the land cells beside each cell (west, east; north and south nearest by longitude, across
        // tile and band edges) so the bicubic surface follows GLO-90 more closely, then height codes
        done = 0;
        Parallel.ForEach(means, po, kv =>
        {
            int key = kv.Key, band = key / 360, lonIdx = key % 360;
            int R = hGrid.RowsPerDegree, C = hGrid.Cols[band];
            var mean = kv.Value;
            var codes = new int[mean.Length];
            var isLand = new bool[mean.Length];
            long nl = 0;
            for (int r = 0; r < R; r++)
            {
                int g = (179 - band) * R + r;
                for (int c = 0; c < C; c++)
                {
                    int i = r * C + c;
                    float v = mean[i];
                    if (float.IsNaN(v)) continue;
                    double h = v;
                    if (alpha != 0)
                    {
                        int j = lonIdx * C + c;
                        double lon = hGrid.ColLon(band, j), sum = 0; int n = 0;
                        float q = MeanAt(g, j - 1); if (!float.IsNaN(q)) { sum += q; n++; }
                        q = MeanAt(g, j + 1); if (!float.IsNaN(q)) { sum += q; n++; }
                        for (int dg = -1; dg <= 1; dg += 2)
                        {
                            int g2 = g + dg;
                            if (g2 < 0 || g2 >= hGrid.Rows) continue;
                            q = MeanAt(g2, (int)Math.Floor(hGrid.ColX(hGrid.Band(g2), lon) + 0.5));
                            if (!float.IsNaN(q)) { sum += q; n++; }
                        }
                        if (n > 0) h = v + alpha * (v - sum / n);
                    }
                    isLand[i] = true; codes[i] = steps.Code(h); nl++;
                }
            }
            byte[] payload = CoarseElevation.EncodeHeights(codes, isLand, C, R, scheme);
            var back = CoarseElevation.DecodeHeights(payload, C, R, scheme);
            for (int i = 0; i < codes.Length; i++)
                if (back[i] != (isLand[i] ? codes[i] : CoarseElevation.NoLand)) throw new InvalidDataException($"tile {key}: cell {i} doesn't decode as coded");
            built[key] = new Built { LatS = band - 90, LonW = lonIdx - 180, Heights = payload, LandCells = nl, Cells = mean.Length };
            int d = Interlocked.Increment(ref done);
            if (d % 4000 == 0) Console.WriteLine($"pass 1b: {d}/{means.Count} {sw.Elapsed.TotalSeconds:F0} s");
        });
        means.Clear();
        long hBytes = built.Values.Sum(b => (long)b.Heights.Length);
        Console.WriteLine($"pass 1: {built.Count} tiles with land of {all.Count}, heights {hBytes / 1e6:F2} MB, {sw.Elapsed.TotalSeconds:F0} s");

        // the heights as the runtime reads them
        var order = built.Values.OrderByDescending(b => b.LatS).ThenBy(b => b.LonW).ToList();
        var coarse = InMemory(order, hGrid, steps, scheme, null, null, false);
        coarse.CacheBytes = 1L << 30;

        // pass 2: GLO-90 minus the interpolated grid, root mean square per roughness cell
        done = 0;
        var sw2 = Stopwatch.StartNew();
        double res2All = 0; long ptsAll = 0;
        var gate = new object();
        Parallel.ForEach(all, po, t =>
        {
            int key = (t.latS + 90) * 360 + t.lonW + 180;
            if (!built.TryGetValue(key, out var b)) return;
            var tile = ReadTile(t.path, t.latS, t.lonW);
            int band = t.latS + 90, cols = tile.Cols;
            int RR = rGrid?.RowsPerDegree ?? 1, RC = rGrid?.Cols[band] ?? 1;
            var s2 = new double[RR * RC]; var n = new long[RR * RC];
            double tot2 = 0; long pts = 0;
            for (int i = 0; i < tile.Rows; i++)
            {
                double lat = t.latS + 1 - (double)i / tile.Rows;
                int r = (int)((i + 0.5) * RR / tile.Rows);
                for (int j = 0; j < cols; j++)
                {
                    float v = tile.Heights[i * cols + j];
                    if (!Coarsen.IsLand(v)) continue;
                    double lon = t.lonW + (double)j / cols;
                    if (!coarse.TryHeight(lat, lon, out double m)) continue;
                    double e = v - m;
                    int c = (int)((j + 0.5) * RC / cols);
                    s2[r * RC + c] += e * e; n[r * RC + c]++;
                    tot2 += e * e; pts++;
                }
            }
            b.Res2 = tot2; b.LandPoints = pts;
            if (rGrid != null)
            {
                var codes = new int[RR * RC];
                for (int q = 0; q < codes.Length; q++) codes[q] = n[q] > 0 ? CoarseElevation.RoughnessCode(rTable, Math.Sqrt(s2[q] / n[q])) : 0;
                b.Rough = CoarseElevation.EncodeRoughness(codes, RC, RR, scheme);
                var back = CoarseElevation.DecodeRoughness(b.Rough, RC, RR, scheme);
                for (int q = 0; q < codes.Length; q++) if (back[q] != codes[q]) throw new InvalidDataException($"{t.path}: roughness cell {q} doesn't decode as coded");
            }
            lock (gate) { res2All += tot2; ptsAll += pts; }
            int d = Interlocked.Increment(ref done);
            if (d % 2000 == 0) Console.WriteLine($"pass 2: {d}/{built.Count} {sw2.Elapsed.TotalSeconds:F0} s");
        });
        long rBytes = order.Sum(b => (long)(b.Rough?.Length ?? 0));
        Console.WriteLine($"pass 2: {sw2.Elapsed.TotalSeconds:F0} s; roughness {rBytes / 1e6:F2} MB; GLO-90 land points {ptsAll:N0}, " +
            $"RMS difference from the grid {Math.Sqrt(res2All / ptsAll):F2} m");

        // cache the payloads
        string note = $"Copernicus GLO-90 (COP-DEM_GLO-90-DGED/2022_1, AWS copernicus-dem-90m, files of 9 May 2022) averaged by tools/Glo90Coarse: " +
            $"land-only means on {3600.0 / rows:0.###}\" rows, columns {rows}*cos(lat) per degree, sharpened by {alpha} x (mean - mean of the land cells beside it); steps (from:metres) {stepSpec}; " +
            $"arithmetic coded, linear predictor {string.Join(",", scheme.Weights)}/64" +
            (rGrid == null ? "" : $"; roughness: RMS of GLO-90 minus the interpolated grid on {3600.0 / rrows:0.###}\" cells, {rcodes} log codes {rfirst}..{rlast} m");
        using (var f = new BinaryWriter(File.Create(Path.Combine(work, "payloads.bin"))))
        {
            f.Write(note);
            f.Write(stepSpec);
            f.Write(rows); f.Write(rrows); f.Write(rcodes); f.Write(rfirst); f.Write(rlast);
            f.Write(order.Count);
            foreach (var b in order)
            {
                f.Write((short)b.LatS); f.Write((short)b.LonW);
                f.Write(b.Heights.Length); f.Write(b.Heights);
                f.Write(b.Rough?.Length ?? 0); if (b.Rough != null) f.Write(b.Rough);
                f.Write(b.LandCells); f.Write(b.Cells); f.Write(b.LandPoints); f.Write(b.Res2);
            }
        }
        var summary = new
        {
            tiles = all.Count, tilesWithLand = order.Count, sharpen = alpha, rowsPerDegree = rows, roughRowsPerDegree = rrows, steps = stepSpec, roughCodes = rcodes,
            heightMB = hBytes / 1e6, roughMB = rBytes / 1e6, landCells = order.Sum(b => b.LandCells), cells = order.Sum(b => b.Cells),
            glo90LandPoints = ptsAll, rmsDifferenceM = Math.Sqrt(res2All / ptsAll), pass1Seconds = (sw.Elapsed - sw2.Elapsed).TotalSeconds, pass2Seconds = sw2.Elapsed.TotalSeconds,
        };
        File.WriteAllText(Path.Combine(work, "build-summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(summary));
        return 0;
    }

    static CoarseElevation InMemory(List<Built> order, CoarseElevation.Grid hGrid, CoarseElevation.Steps steps, CoarseElevation.Scheme scheme, CoarseElevation.Grid rGrid, float[] rTable, bool withRough)
    {
        var part = new MemoryStream();
        part.Write(CoarseElevation.PartHeader(1));
        var entries = new List<CoarseElevation.TileEntry>();
        foreach (var b in order)
        {
            var e = new CoarseElevation.TileEntry { LatS = (short)b.LatS, LonW = (short)b.LonW, Part = 1, Offset = (int)part.Position, HeightBytes = b.Heights.Length };
            part.Write(b.Heights);
            if (withRough) { e.RoughBytes = b.Rough.Length; part.Write(b.Rough); }
            entries.Add(e);
        }
        var index = CoarseElevation.WriteIndex(1, "in memory", hGrid, steps, scheme, withRough ? rGrid : null, rTable, entries);
        return CoarseElevation.FromBytes(index, new[] { part.ToArray() });
    }

    // ---------------------------------------------------------------------------------------------- split

    public static int Split(string[] a)
    {
        string work = a[1], outDir = a[2];
        var limits = a[3].Split(',').Select(long.Parse).ToArray();
        Directory.CreateDirectory(outDir);
        using var f = new BinaryReader(File.OpenRead(Path.Combine(work, "payloads.bin")));
        string note = f.ReadString(), stepSpec = f.ReadString();
        int rows = f.ReadInt32(), rrows = f.ReadInt32(), rcodes = f.ReadInt32();
        double rfirst = f.ReadDouble(), rlast = f.ReadDouble();
        int count = f.ReadInt32();
        var tiles = new List<(short latS, short lonW, byte[] h, byte[] r)>();
        for (int i = 0; i < count; i++)
        {
            short la = f.ReadInt16(), lo = f.ReadInt16();
            var h = f.ReadBytes(f.ReadInt32());
            int rl = f.ReadInt32();
            var r = rl > 0 ? f.ReadBytes(rl) : null;
            f.ReadInt64(); f.ReadInt64(); f.ReadInt64(); f.ReadDouble();
            tiles.Add((la, lo, h, r));
        }
        var hGrid = CoarseElevation.Grid.Square(rows, DemTiles.Glo90Cols);
        var rGrid = rrows > 0 ? CoarseElevation.Grid.Square(rrows, DemTiles.Glo90Cols) : null;
        var rTable = rrows > 0 ? CoarseElevation.RoughnessTable(rcodes, rfirst, rlast) : null;
        var steps = ParseSteps(stepSpec);

        // fill parts in order (north to south, west to east) up to each limit; a last limit of 0 takes the rest
        var parts = new List<MemoryStream>();
        var entries = new List<CoarseElevation.TileEntry>();
        int p = 0;
        MemoryStream cur = null;
        foreach (var t in tiles)
        {
            long size = t.h.Length + (t.r?.Length ?? 0);
            while (cur == null || (limits[p] > 0 && cur.Length + size > limits[p]))
            {
                if (cur != null) { p++; if (p >= limits.Length) throw new InvalidOperationException("the parts' limits don't hold all tiles"); }
                cur = new MemoryStream();
                cur.Write(CoarseElevation.PartHeader(p + 1));
                parts.Add(cur);
            }
            var e = new CoarseElevation.TileEntry { LatS = t.latS, LonW = t.lonW, Part = (byte)(p + 1), Offset = (int)cur.Length, HeightBytes = t.h.Length, RoughBytes = t.r?.Length ?? 0 };
            cur.Write(t.h);
            if (t.r != null) cur.Write(t.r);
            entries.Add(e);
        }
        var index = CoarseElevation.WriteIndex(parts.Count, note, hGrid, steps, CoarseElevation.Scheme.Default, rGrid, rTable, entries);
        var outFiles = new List<(string name, byte[] data)> { (CoarseElevation.IndexFile, index) };
        for (int i = 0; i < parts.Count; i++) outFiles.Add((CoarseElevation.PartFile(i + 1), parts[i].ToArray()));
        // check the files read back as written before keeping them
        var check = CoarseElevation.FromBytes(index, outFiles.Skip(1).Select(x => x.data).ToList());
        if (check.TileCount != tiles.Count) throw new InvalidDataException("read-back tile count differs");
        var report = new List<object>();
        foreach (var (name, data) in outFiles)
        {
            File.WriteAllBytes(Path.Combine(outDir, name), data);
            string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            report.Add(new { name, bytes = data.Length, sha256 = sha, firstTile = entries.FirstOrDefault(x => CoarseElevation.PartFile(x.Part) == name) is var fe && fe.HeightBytes > 0 ? DemTiles.ShortName(fe.LatS, fe.LonW) : null,
                lastTile = entries.LastOrDefault(x => CoarseElevation.PartFile(x.Part) == name) is var le && le.HeightBytes > 0 ? DemTiles.ShortName(le.LatS, le.LonW) : null });
            Console.WriteLine($"{name}: {data.Length:N0} bytes, sha256 {sha}");
        }
        File.WriteAllText(Path.Combine(work, "split.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
