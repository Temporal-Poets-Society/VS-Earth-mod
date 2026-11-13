using System.Collections.Concurrent;

namespace EarthMap.Core
{
    /// <summary>FineElevation needs Geo.Wrap180 and Geo.MetresPerDegree from EarthData.cs, which this tool doesn't link.</summary>
    public static class Geo
    {
        public const double MetresPerDegree = 111320;

        public static double Wrap180(double lon)
        {
            lon %= 360;
            if (lon >= 180) lon -= 360;
            if (lon < -180) lon += 360;
            return lon;
        }
    }

    /// <summary>FineElevation can lower dammed reservoirs (DamRemoval.cs, which needs the river network); this tool reads
    /// GLO-90 as it is, so it never sets FineElevation.Dams.</summary>
    public sealed class DamRemovalData
    {
        public void ApplyTo(DemTiles.Tile tile) { }
    }
}

namespace Glo90Coarse
{
    /// <summary>
    /// Area means of a GLO-90 tile on a coarser grid. Point i of n stands for [i, i + 1) in units of the source spacing
    /// (the half-point shift in CoarseElevation's grid); coarse cell c of m covers [c n/m, (c + 1) n/m), and each point
    /// counts with the length it shares with the cell. Only land points count: GLO-90 has the sea at exactly 0 m.
    /// </summary>
    static class Coarsen
    {
        public sealed class Weights
        {
            public int[] Start;
            public double[][] W;
        }

        static readonly ConcurrentDictionary<(int, int), Weights> cache = new();

        public static Weights For(int n, int m) => cache.GetOrAdd((n, m), k => Make(k.Item1, k.Item2));

        static Weights Make(int n, int m)
        {
            var res = new Weights { Start = new int[m], W = new double[m][] };
            for (int c = 0; c < m; c++)
            {
                // exact rational edges: c n / m
                long an = (long)c * n, bn = (long)(c + 1) * n;
                int i0 = (int)(an / m), i1 = (int)((bn + m - 1) / m) - 1;
                i1 = Math.Min(i1, n - 1);
                var w = new double[i1 - i0 + 1];
                for (int i = i0; i <= i1; i++)
                {
                    double lo = Math.Max(i, (double)an / m), hi = Math.Min(i + 1, (double)bn / m);
                    w[i - i0] = Math.Max(0, hi - lo);
                }
                res.Start[c] = i0; res.W[c] = w;
            }
            return res;
        }

        public static bool IsLand(float v) => v != 0f && float.IsFinite(v);

        /// <summary>Land-only means (NaN without land) and land shares of each cell of an R × C grid over a rows × cols tile.</summary>
        public static void Means(float[] h, int rows, int cols, int R, int C, double[] mean, double[] share)
        {
            var wr = For(rows, R);
            var wc = For(cols, C);
            var S = new double[cols];
            var W = new double[cols];
            double cellArea = (double)rows / R * cols / C;
            for (int r = 0; r < R; r++)
            {
                Array.Clear(S); Array.Clear(W);
                var w = wr.W[r];
                for (int a = 0; a < w.Length; a++)
                {
                    double wt = w[a];
                    if (wt <= 0) continue;
                    int off = (wr.Start[r] + a) * cols;
                    for (int j = 0; j < cols; j++)
                    {
                        float v = h[off + j];
                        if (v != 0f && float.IsFinite(v)) { S[j] += wt * v; W[j] += wt; }
                    }
                }
                for (int c = 0; c < C; c++)
                {
                    var w2 = wc.W[c];
                    int j0 = wc.Start[c];
                    double s = 0, ww = 0;
                    for (int b = 0; b < w2.Length; b++) { s += w2[b] * S[j0 + b]; ww += w2[b] * W[j0 + b]; }
                    mean[r * C + c] = ww > 0 ? s / ww : double.NaN;
                    if (share != null) share[r * C + c] = ww / cellArea;
                }
            }
        }

        /// <summary>
        /// The spread of the land points in each cell of an R × C grid around a plane fitted to them (root mean square, m);
        /// points go to the cell holding their centre. A measure-only stand-in for the residual around the interpolated grid.
        /// </summary>
        public static double[] PlaneSpread(float[] h, int rows, int cols, int R, int C)
        {
            int n = R * C;
            var acc = new double[n * 10];
            for (int i = 0; i < rows; i++)
            {
                int r = (int)((i + 0.5) * R / rows);
                double y = (i + 0.5) - (r + 0.5) * rows / (double)R;
                for (int j = 0; j < cols; j++)
                {
                    float v = h[i * cols + j];
                    if (!(v != 0f && float.IsFinite(v))) continue;
                    int c = (int)((j + 0.5) * C / cols);
                    double x = (j + 0.5) - (c + 0.5) * cols / (double)C;
                    int k = (r * C + c) * 10;
                    acc[k] += 1; acc[k + 1] += x; acc[k + 2] += y; acc[k + 3] += v;
                    acc[k + 4] += x * x; acc[k + 5] += y * y; acc[k + 6] += x * y;
                    acc[k + 7] += x * v; acc[k + 8] += y * v; acc[k + 9] += (double)v * v;
                }
            }
            var res = new double[n];
            for (int q = 0; q < n; q++)
            {
                int k = q * 10;
                double N = acc[k];
                if (N < 1) { res[q] = 0; continue; }
                double Sx = acc[k + 1], Sy = acc[k + 2], Sh = acc[k + 3], Sxx = acc[k + 4], Syy = acc[k + 5], Sxy = acc[k + 6], Sxh = acc[k + 7], Syh = acc[k + 8], Shh = acc[k + 9];
                double ss = Shh - Sh * Sh / N;
                if (N >= 4)
                {
                    // centred normal equations for the slope
                    double cxx = Sxx - Sx * Sx / N, cyy = Syy - Sy * Sy / N, cxy = Sxy - Sx * Sy / N;
                    double cxh = Sxh - Sx * Sh / N, cyh = Syh - Sy * Sh / N;
                    double det = cxx * cyy - cxy * cxy;
                    if (det > 1e-9 * Math.Max(1, cxx * cyy))
                    {
                        double b = (cxh * cyy - cyh * cxy) / det, c = (cyh * cxx - cxh * cxy) / det;
                        ss -= b * cxh + c * cyh;
                    }
                }
                res[q] = Math.Sqrt(Math.Max(0, ss / N));
            }
            return res;
        }
    }
}
