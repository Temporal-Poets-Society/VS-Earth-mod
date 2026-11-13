using System.Diagnostics;
using System.Text.Json;
using EarthMap.Core;

namespace Glo90Coarse
{
    /// <summary>
    /// The arithmetic coder against Brotli on a sample: predictors, contexts, adaptation; and the least-squares weights of a
    /// linear predictor (printed, to be tried with --weights).
    ///   measure2 <tif folder> <every> <out.json> [--grids 150,180] [--weights 48,-32,16,-8,-8]
    /// </summary>
    static class Measure2
    {
        public static int Run(string[] a)
        {
            string src = a[1]; int every = int.Parse(a[2]); string outPath = a[3];
            var gridRows = Tool.Opt(a, "--grids", "150,180").Split(',').Select(int.Parse).ToArray();
            var wOpt = Tool.Opt(a, "--weights", null);
            var weights = wOpt != null ? wOpt.Split(',').Select(int.Parse).ToArray() : CoarseElevation.Scheme.Default.Weights;
            var all = Tool.SourceTiles(src);
            var sample = new List<(int latS, int lonW, string path)>();
            for (int i = 0; i < all.Count; i += every) sample.Add(all[i]);
            var stepSets = Tool.Opt(a, "--steps", "q1=-512:1;q2=-512:2;q4=-512:4;c1=-512:1,256:2,1024:4,2560:8;c3=-512:2,512:4,2048:8;c6=-512:1,128:2,512:4;c7=-512:2,256:4")
                .Split(';').Select(x => (name: x.Split('=')[0], spec: x.Split('=')[1])).ToArray();
            var roughRows = Tool.Opt(a, "--rough-rows", "0").Split(',').Select(int.Parse).ToArray();
            var steps = stepSets.Select(s => Tool.ParseSteps(s.spec)).ToArray();
            var schemes = Tool.Opt(a, "--schemes", "med ctx0 s5=0,0,5;med ctx1 s5=0,1,5;med ctx1 s4=0,1,4;plane ctx1 s5=1,1,5;linear ctx1 s5=2,1,5;linear ctx1 s4=2,1,4")
                .Split(';').Select(x => { var q = x.Split('=')[1].Split(',').Select(int.Parse).ToArray(); return (name: x.Split('=')[0], s: new CoarseElevation.Scheme { Predictor = q[0], Context = q[1], Shift = q[2], Weights = weights }); }).ToArray();
            var rtable = CoarseElevation.RoughnessTable(16, 1, 1024);
            var rtable5 = CoarseElevation.RoughnessTable(8, 1, 1024);
            var extra = new long[roughRows.Length, 2];
            int G = gridRows.Length, S = steps.Length, K = schemes.Length;
            var brotli = new long[G, S];
            var ac = new long[G, S, K];
            var rough = new long[G, 2, 4]; // half, (brotli4, ac4, brotli5, ac5)
            var land = new long[G];
            // least squares of (code - W) on (N - W, NW - W, NE - W, WW - W, NN - W), on q1 codes of the first grid
            var xtx = new double[5, 5]; var xty = new double[5];
            var gate = new object();
            int done = 0;
            var sw = Stopwatch.StartNew();
            Parallel.ForEach(sample, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, t =>
            {
                var tile = Tool.ReadTile(t.path, t.latS, t.lonW);
                int band = t.latS + 90;
                var lb = new long[G, S]; var la = new long[G, S, K]; var lr = new long[G, 2, 4]; var ll = new long[G];
                var lxtx = new double[5, 5]; var lxty = new double[5];
                for (int g = 0; g < G; g++)
                {
                    var grid = CoarseElevation.Grid.Square(gridRows[g], DemTiles.Glo90Cols);
                    int R = grid.RowsPerDegree, C = grid.Cols[band];
                    var mean = new double[R * C];
                    Coarsen.Means(tile.Heights, tile.Rows, tile.Cols, R, C, mean, null);
                    var isLand = mean.Select(x => !double.IsNaN(x)).ToArray();
                    ll[g] = isLand.Count(x => x);
                    for (int s = 0; s < S; s++)
                    {
                        var codes = new int[mean.Length];
                        for (int i = 0; i < mean.Length; i++) if (isLand[i]) codes[i] = steps[s].Code(mean[i]);
                        lb[g, s] = BrotliCodec.Heights(codes, isLand, C, R, 11).Length;
                        for (int k = 0; k < K; k++)
                        {
                            var p = CoarseElevation.EncodeHeights(codes, isLand, C, R, schemes[k].s);
                            la[g, s, k] = p.Length;
                            if (s == 3 && k == 1 && t.lonW % 7 == 0)
                            {
                                // round trip
                                var back = CoarseElevation.DecodeHeights(p, C, R, schemes[k].s);
                                for (int i = 0; i < codes.Length; i++)
                                    if ((isLand[i] ? codes[i] : CoarseElevation.NoLand) != back[i]) throw new Exception($"round trip {t.path} cell {i}");
                            }
                        }
                        if (g == 0 && s == 0)
                        {
                            for (int r = 2; r < R; r++)
                                for (int c = 2; c < C - 1; c++)
                                {
                                    int i = r * C + c;
                                    if (!isLand[i] || !isLand[i - 1] || !isLand[i - 2] || !isLand[i - C] || !isLand[i - C - 1] || !isLand[i - C + 1] || !isLand[i - 2 * C]) continue;
                                    double w = codes[i - 1];
                                    double[] x = { codes[i - C] - w, codes[i - C - 1] - w, codes[i - C + 1] - w, codes[i - 2] - w, codes[i - 2 * C] - w };
                                    double y = codes[i] - w;
                                    for (int p = 0; p < 5; p++) { lxty[p] += x[p] * y; for (int q = 0; q < 5; q++) lxtx[p, q] += x[p] * x[q]; }
                                }
                        }
                    }
                    for (int half = 0; half < 2; half++)
                    {
                        var rg = half == 0 ? grid : CoarseElevation.Grid.Square(gridRows[g] / 2, DemTiles.Glo90Cols);
                        int RR = rg.RowsPerDegree, RC = rg.Cols[band];
                        var spread = Coarsen.PlaneSpread(tile.Heights, tile.Rows, tile.Cols, RR, RC);
                        var c4 = spread.Select(x => CoarseElevation.RoughnessCode(rtable, x)).ToArray();
                        var c5 = spread.Select(x => CoarseElevation.RoughnessCode(rtable5, x)).ToArray();
                        lr[g, half, 0] = BrotliCodec.Roughness(c4, RC, RR, 11).Length;
                        lr[g, half, 1] = CoarseElevation.EncodeRoughness(c4, RC, RR, schemes[1].s).Length;
                        lr[g, half, 2] = BrotliCodec.Roughness(c5, RC, RR, 11).Length;
                        lr[g, half, 3] = CoarseElevation.EncodeRoughness(c5, RC, RR, schemes[1].s).Length;
                        if (t.lonW % 7 == 0)
                        {
                            var back = CoarseElevation.DecodeRoughness(CoarseElevation.EncodeRoughness(c4, RC, RR, schemes[1].s), RC, RR, schemes[1].s);
                            for (int i = 0; i < c4.Length; i++) if (back[i] != c4[i]) throw new Exception("roughness round trip");
                        }
                    }
                }
                var le = new long[roughRows.Length, 2];
                for (int q = 0; q < roughRows.Length; q++)
                {
                    if (roughRows[q] <= 0) continue;
                    var rg = CoarseElevation.Grid.Square(roughRows[q], DemTiles.Glo90Cols);
                    int RR = rg.RowsPerDegree, RC = rg.Cols[band];
                    var spread = Coarsen.PlaneSpread(tile.Heights, tile.Rows, tile.Cols, RR, RC);
                    le[q, 0] = CoarseElevation.EncodeRoughness(spread.Select(x => CoarseElevation.RoughnessCode(rtable, x)).ToArray(), RC, RR, schemes[1].s).Length;
                    le[q, 1] = CoarseElevation.EncodeRoughness(spread.Select(x => CoarseElevation.RoughnessCode(rtable5, x)).ToArray(), RC, RR, schemes[1].s).Length;
                }
                lock (gate)
                {
                    for (int q = 0; q < roughRows.Length; q++) { extra[q, 0] += le[q, 0]; extra[q, 1] += le[q, 1]; }
                    for (int g = 0; g < G; g++)
                    {
                        land[g] += ll[g];
                        for (int s = 0; s < S; s++) { brotli[g, s] += lb[g, s]; for (int k = 0; k < K; k++) ac[g, s, k] += la[g, s, k]; }
                        for (int h = 0; h < 2; h++) for (int q = 0; q < 4; q++) rough[g, h, q] += lr[g, h, q];
                    }
                    for (int p = 0; p < 5; p++) { xty[p] += lxty[p]; for (int q = 0; q < 5; q++) xtx[p, q] += lxtx[p, q]; }
                    if (++done % 100 == 0) Console.WriteLine($"{done}/{sample.Count} {sw.Elapsed.TotalSeconds:F0} s");
                }
            });
            double scale = (double)all.Count / sample.Count;
            var coef = Solve(xtx, xty);
            var outRows = new List<object>();
            for (int g = 0; g < G; g++)
            {
                outRows.Add(new
                {
                    grid = 3600.0 / gridRows[g] + "s-square",
                    landCellsM = land[g] * scale / 1e6,
                    steps = Enumerable.Range(0, S).Select(s => new
                    {
                        steps = stepSets[s].name,
                        brotli11MB = brotli[g, s] * scale / 1e6,
                        ac = Enumerable.Range(0, K).Select(k => new { scheme = schemes[k].name, MB = ac[g, s, k] * scale / 1e6 }).ToArray(),
                    }).ToArray(),
                    roughSame = new { brotli4 = rough[g, 0, 0] * scale / 1e6, ac4 = rough[g, 0, 1] * scale / 1e6, brotli3 = rough[g, 0, 2] * scale / 1e6, ac3 = rough[g, 0, 3] * scale / 1e6 },
                    roughHalf = new { brotli4 = rough[g, 1, 0] * scale / 1e6, ac4 = rough[g, 1, 1] * scale / 1e6, brotli3 = rough[g, 1, 2] * scale / 1e6, ac3 = rough[g, 1, 3] * scale / 1e6 },
                });
            }
            var res = new { roughExtra = roughRows.Select((r, q) => new { rows = r, arcsec = r > 0 ? 3600.0 / r : 0, ac4bitMB = extra[q, 0] * scale / 1e6, ac3bitMB = extra[q, 1] * scale / 1e6 }).ToArray(), tilesSampled = sample.Count, seconds = sw.Elapsed.TotalSeconds, linearWeights = weights, leastSquares = coef, leastSquaresPer64 = coef.Select(c => Math.Round(c * 64)).ToArray(), grids = outRows };
            string json = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outPath, json);
            Console.WriteLine(json);
            return 0;
        }

        static double[] Solve(double[,] A, double[] b)
        {
            int n = b.Length;
            var m = new double[n, n + 1];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) m[i, j] = A[i, j]; m[i, n] = b[i]; }
            for (int c = 0; c < n; c++)
            {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
                for (int j = 0; j <= n; j++) (m[c, j], m[p, j]) = (m[p, j], m[c, j]);
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c] / m[c, c];
                    for (int j = c; j <= n; j++) m[r, j] -= f * m[c, j];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = m[i, n] / m[i, i];
            return x;
        }
    }
}
