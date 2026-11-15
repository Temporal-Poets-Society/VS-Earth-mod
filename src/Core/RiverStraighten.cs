using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Straightens rivers in world coordinates for their exaggerated width. A river several times wider than the real
    /// one folds onto itself where the real river bends tightly, leaving slivers of land and steps between its arms.
    ///  - Loop cut-offs: along each river between two confluences, where two points of the river come within about a river
    ///    width of each other (the more straightening, the farther apart), the stretch between them is cut off, as a
    ///    river cuts through a meander neck. Never across a lake (fixed vertices) or a confluence.
    ///  - Smoothing: wiggles smaller than a number of river widths are simplified away (Douglas-Peucker along the river
    ///    between confluences; joints between data reaches move onto the smoothed line).
    /// Works on per-reach vertex arrays (world x/z, level, fixed) and replaces them. A chain crossing the seam is left as it
    /// is; with seamGeo the crossing is found by the ring's geometry, without (older worlds) by a piece longer than 4000 blocks.
    /// With cutWidths (0053, rivers on GLO-90) the cut-offs are at that many water widths whatever the strength, and a chain
    /// with no cut and no smoothing keeps its own vertices.
    /// With wid (0063 channel rules 1: the river's measured half width at each vertex, blocks, 0 where it has none) the
    /// water's width at a point is the one the channel has (RiverNetwork.CappedHalfWidth), so only loops tighter than the
    /// water the course draws are cut; the widths go with the vertices (replaced along with the lines).
    /// With cornerSigma (0063 channel rules 1: per reach, blocks, 0 for none) the reaches given one are smoothed by a
    /// Gaussian of that sigma instead of smoothWidths' (which must be 0), fading out towards the chain's ends, lake points
    /// and the reaches without one, which stay where they are; with hold (per vertex, blocks, 0 for none) a point moves at
    /// most that far, so a course on the mask's water stays in it (a meander keeps its swing, a pixel path's corner is cut).
    /// </summary>
    public static class RiverStraighten
    {
        /// <summary>How far (blocks) the kept vertices of a chain smoothed with cornerSigma may lie off the smoothed line.</summary>
        public const double CornerTolerance = 1.25;

        public static void Apply(IReadOnlyList<RiverReach> reaches, bool[] keep, double[][] xs, double[][] zs, double[][] lev, bool[][] fix,
            Func<double, double> halfWidth, double strength, double smoothWidths, EarthGeometry seamGeo = null, double cutWidths = 0,
            double[][] wid = null, double[] cornerSigma = null, double[][] hold = null)
        {
            if (cornerSigma != null && smoothWidths > 0) throw new ArgumentException("cornerSigma replaces smoothWidths", nameof(cornerSigma));
            if (strength <= 0 && smoothWidths <= 0 && cutWidths <= 0 && cornerSigma == null) return;
            int nr = reaches.Count;
            // kept inflows per reach; a reach continues its upstream reach only where that is the one kept inflow
            var inCount = new int[nr];
            var onlyIn = new int[nr];
            for (int r = 0; r < nr; r++) onlyIn[r] = -1;
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int nx = reaches[r].Next;
                if (nx < 0 || nx >= nr || !keep[nx]) continue;
                inCount[nx]++;
                onlyIn[nx] = r;
            }
            // chain starts: no single kept inflow continues into this reach
            var starts = new List<int>();
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r] || xs[r] == null || xs[r].Length < 2) continue;
                if (inCount[r] == 1) continue;
                starts.Add(r);
            }
            // Each reach belongs to one chain (a reach with one kept inflow only to that inflow's), and a chain reads and
            // replaces only its own reaches' lines, so the chains run in parallel with the same result as one after another:
            // about two thirds of the river index's start-up time at the 1.1 million river pieces of a 512k world.
            // (ER-25: on ParallelWork's few long-lived workers, each with its own chain list and scratch, not a raw uncapped
            // Parallel.ForEach that made a scratch per task replica)
            ParallelWork.For(starts.Count, () => (Chain: new List<int>(), Scratch: new Scratch()), (i, local) =>
            {
                var chain = local.Chain;
                chain.Clear();
                int c = starts[i];
                while (true)
                {
                    chain.Add(c);
                    int nx = reaches[c].Next;
                    if (nx < 0 || nx >= nr || !keep[nx] || inCount[nx] != 1 || xs[nx] == null) break;
                    c = nx;
                }
                StraightenChain(chain, local.Scratch, reaches, xs, zs, lev, fix, halfWidth, strength, smoothWidths, seamGeo, cutWidths, wid, cornerSigma, hold);
            }, null, chunk: 4);
        }

        struct Pt
        {
            public double X, Z, S, Lev;
            /// <summary>The mask's half width (blocks, 0 none; only with wid).</summary>
            public double Wid;
            /// <summary>How far the smoothing may move this point (blocks, 0 no limit; only with hold).</summary>
            public double Hold;
            public bool Fixed;
            public int Reach;
            /// <summary>The first vertex of its reach (shared with the end of the reach before).</summary>
            public bool Start;
        }

        /// <summary>A worker's buffers, reused from chain to chain: a chain's own lists and arrays came to about 2 GB at the 1.1
        /// million river pieces of a 512k world, and the collections they caused held up every worker.</summary>
        sealed class Scratch
        {
            public readonly List<Pt> Pts = new List<Pt>(), Surv = new List<Pt>(), Line = new List<Pt>();
            public bool[] Removed, KeepPt;
            public double[] Hw, SAlong, Sigma, Pin, Sx, Sz, Tol;
            public int[] FixedBefore, CellPt, Count, At;
            public long[] CellKey;
            public List<(double[] X, double[] Z, double[] Lev, bool[] Fix, double[] Wid)> Lines;

            /// <summary>At least n elements; the first n cleared if asked (the rest are never read).</summary>
            public static T[] Get<T>(ref T[] buffer, int n, bool clear)
            {
                if (buffer == null || buffer.Length < n) buffer = new T[Math.Max(n, (buffer?.Length ?? 0) * 2)];
                else if (clear) Array.Clear(buffer, 0, n);
                return buffer;
            }
        }

        /// <summary>A mask width between two points (f from a to b): along it where both have one, else the nearer one's (0 is
        /// "none", not a width).</summary>
        internal static double WidthBetween(double a, double b, double f) => a > 0 && b > 0 ? a + (b - a) * f : f < 0.5 ? a : b;

        static void StraightenChain(List<int> chain, Scratch scratch, IReadOnlyList<RiverReach> reaches, double[][] xs, double[][] zs, double[][] lev, bool[][] fix,
            Func<double, double> halfWidth, double strength, double smoothWidths, EarthGeometry seamGeo, double cutWidths, double[][] wid,
            double[] cornerSigma, double[][] hold)
        {
            // the cut-off distance in water widths (both half widths); cutWidths replaces 1 + strength when given
            double cutFactor = cutWidths > 0 ? cutWidths : 1 + strength;
            bool cutting = cutWidths > 0 || strength > 0;
            // the chain as one densified line (a point every ~4 blocks, so close arms are seen between vertices)
            var pts = scratch.Pts;
            pts.Clear();
            double s = 0;
            for (int ci = 0; ci < chain.Count; ci++)
            {
                int r = chain[ci];
                var x = xs[r]; var z = zs[r];
                var w = wid?[r];
                var hd = hold?[r];
                for (int k = 0; k < x.Length; k++)
                {
                    if (k == 0)
                    {
                        if (ci == 0) pts.Add(new Pt { X = x[0], Z = z[0], S = 0, Lev = lev[r][0], Fixed = fix[r][0], Reach = r, Start = true, Wid = w != null ? w[0] : 0, Hold = hd != null ? hd[0] : 0 });
                        else
                        {
                            // the joint: already the last point of the reach before; now it starts this reach
                            var p = pts[pts.Count - 1];
                            p.Reach = r;
                            p.Start = true;
                            pts[pts.Count - 1] = p;
                        }
                        continue;
                    }
                    double dx = x[k] - x[k - 1], dz = z[k] - z[k - 1], len = Math.Sqrt(dx * dx + dz * dz);
                    if (seamGeo != null ? seamGeo.AcrossSeam(x[k - 1], x[k]) : len > 4000)
                    {
                        // across the seam: no straightening here
                        return;
                    }
                    int n = Math.Max(1, (int)Math.Ceiling(len / 4));
                    for (int u = 1; u <= n; u++)
                    {
                        double f = (double)u / n;
                        pts.Add(new Pt
                        {
                            X = x[k - 1] + dx * f, Z = z[k - 1] + dz * f, S = s + len * f,
                            Lev = lev[r][k - 1] + (lev[r][k] - lev[r][k - 1]) * f,
                            Fixed = u == n ? fix[r][k] : fix[r][k - 1] && fix[r][k],
                            Reach = r,
                            Wid = w != null ? WidthBetween(w[k - 1], w[k], f) : 0,
                            Hold = hd != null ? WidthBetween(hd[k - 1], hd[k], f) : 0,
                        });
                    }
                    s += len;
                }
            }
            int np = pts.Count;
            if (np < 3) return;

            // loop cut-offs
            var removed = Scratch.Get(ref scratch.Removed, np, true);
            double maxD = 0;
            var hw = Scratch.Get(ref scratch.Hw, np, false);
            // (a pure function of the reach's discharge: once per reach, not per point)
            int hwReach = -1;
            double reachHw = 0;
            for (int i = 0; i < np; i++)
            {
                if (pts[i].Reach != hwReach) { hwReach = pts[i].Reach; reachHw = halfWidth(reaches[hwReach].Q); }
                hw[i] = reachHw;
                if (pts[i].Wid > 0) hw[i] = RiverNetwork.CappedHalfWidth(hw[i], pts[i].Wid);
                maxD = Math.Max(maxD, 2 * hw[i] * cutFactor);
            }
            double cell = Math.Max(8, maxD);
            // the points by cell: sorted cell keys and their points, searched by key. One pair of arrays instead of a list per
            // cell (about one per two points), which kept the parallel chains waiting on the GC
            var cellKey = Scratch.Get(ref scratch.CellKey, np, false);
            var cellPt = Scratch.Get(ref scratch.CellPt, np, false);
            for (int i = 0; i < np; i++)
            {
                cellKey[i] = ((long)Math.Floor(pts[i].X / cell) << 32) | (uint)(int)Math.Floor(pts[i].Z / cell);
                cellPt[i] = i;
            }
            Array.Sort(cellKey, cellPt, 0, np);
            // prefix count of fixed points, to test "no lake inside the cut"
            var fixedBefore = Scratch.Get(ref scratch.FixedBefore, np + 1, false);
            fixedBefore[0] = 0;
            for (int i = 0; i < np; i++) fixedBefore[i + 1] = fixedBefore[i] + (pts[i].Fixed ? 1 : 0);
            int cuts = 0;
            for (int i = 0; i < np - 2; i++)
            {
                if (removed[i] || !cutting) continue;
                double d = 2 * hw[i] * cutFactor;
                double minAlong = 3 * d;
                int best = -1;
                int cx = (int)Math.Floor(pts[i].X / cell), cz = (int)Math.Floor(pts[i].Z / cell);
                for (int oz = -1; oz <= 1; oz++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        long key = ((long)(cx + ox) << 32) | (uint)(cz + oz);
                        int at = Array.BinarySearch(cellKey, 0, np, key);
                        if (at < 0) continue;
                        while (at > 0 && cellKey[at - 1] == key) at--;
                        // (the cut goes to the farthest point along that qualifies, in whatever order the points are found)
                        for (; at < np && cellKey[at] == key; at++)
                        {
                            int j = cellPt[at];
                            if (j <= best || j <= i + 1 || pts[j].S - pts[i].S < minAlong) continue;
                            double ddx = pts[j].X - pts[i].X, ddz = pts[j].Z - pts[i].Z;
                            if (ddx * ddx + ddz * ddz >= d * d) continue;
                            // not across a lake
                            if (fixedBefore[j] - fixedBefore[i + 1] > 0) continue;
                            best = j;
                        }
                    }
                }
                if (best < 0) continue;
                for (int k = i + 1; k < best; k++) removed[k] = true;
                cuts++;
                i = best - 1;
            }
            // 0053: nothing cut and nothing to smooth: the chain keeps its own vertices (no resampling)
            bool corners = false;
            if (cornerSigma != null) foreach (int r in chain) corners |= cornerSigma[r] > 0;
            if (cutWidths > 0 && cuts == 0 && smoothWidths <= 0 && !corners) return;

            // the course after the cut-offs, as points at most ~4 blocks apart (a cut-off's shortcut gets points too)
            var surv = scratch.Surv;
            surv.Clear();
            for (int i = 0; i < np; i++)
            {
                if (removed[i]) continue;
                if (surv.Count > 0)
                {
                    var a = surv[surv.Count - 1];
                    var bpt = pts[i];
                    double dx = bpt.X - a.X, dz = bpt.Z - a.Z, len = Math.Sqrt(dx * dx + dz * dz);
                    int n = (int)Math.Ceiling(len / 4);
                    for (int u = 1; u < n; u++)
                    {
                        double f = (double)u / n;
                        surv.Add(new Pt { X = a.X + dx * f, Z = a.Z + dz * f, Lev = a.Lev + (bpt.Lev - a.Lev) * f, Reach = a.Reach, Wid = WidthBetween(a.Wid, bpt.Wid, f),
                            Hold = WidthBetween(a.Hold, bpt.Hold, f) });
                    }
                }
                surv.Add(pts[i]);
            }
            int ns = surv.Count;
            // distance along the new course
            var sAlong = Scratch.Get(ref scratch.SAlong, ns, false);
            sAlong[0] = 0;
            for (int i = 1; i < ns; i++)
            {
                double dx = surv[i].X - surv[i - 1].X, dz = surv[i].Z - surv[i - 1].Z;
                sAlong[i] = sAlong[i - 1] + Math.Sqrt(dx * dx + dz * dz);
            }

            // Smoothing: a Gaussian along the course removes bends shorter than about smoothWidths river widths
            // (sigma = 0.34 wavelength damps such a bend to a tenth) and keeps longer ones, rounded. It fades out towards
            // the chain's ends (confluences, mouths) and lake points, which stay where they are.
            if ((smoothWidths > 0 || corners) && ns > 2)
            {
                var sigma = Scratch.Get(ref scratch.Sigma, ns, false);
                int sigmaReach = -1;
                double reachSigma = 0;
                for (int i = 0; i < ns; i++)
                {
                    if (corners) { sigma[i] = cornerSigma[surv[i].Reach]; continue; }
                    if (surv[i].Reach != sigmaReach) { sigmaReach = surv[i].Reach; reachSigma = Math.Max(1, 0.34 * smoothWidths * 2 * halfWidth(reaches[sigmaReach].Q)); }
                    sigma[i] = reachSigma;
                }
                // distance to the nearest pinned point (chain ends, lake points; with cornerSigma the reaches without one too)
                var pin = Scratch.Get(ref scratch.Pin, ns, false);
                double last = double.NegativeInfinity;
                for (int i = 0; i < ns; i++)
                {
                    if (i == 0 || surv[i].Fixed || sigma[i] <= 0) last = sAlong[i];
                    pin[i] = sAlong[i] - last;
                }
                last = double.PositiveInfinity;
                for (int i = ns - 1; i >= 0; i--)
                {
                    if (i == ns - 1 || surv[i].Fixed || sigma[i] <= 0) last = sAlong[i];
                    pin[i] = Math.Min(pin[i], last - sAlong[i]);
                }
                var sx = Scratch.Get(ref scratch.Sx, ns, false); var sz = Scratch.Get(ref scratch.Sz, ns, false);
                int lo = 0, hi = 0;
                for (int i = 0; i < ns; i++)
                {
                    double sg = sigma[i], reach = 3 * sg;
                    if (sg <= 0) { sx[i] = surv[i].X; sz[i] = surv[i].Z; continue; }
                    // (cornerSigma: the window may widen again after a reach without one. T-L9, code review 14 November
                    // 2026: without it the window only moves forward, so after a narrow stretch a wider sigma sums from the
                    // narrow stretch's start: a shorter tail behind the point, never more weight than it should have. The
                    // rules that have it fixed it; older rules keep their lines.)
                    if (corners)
                    {
                        while (lo > 0 && sAlong[lo - 1] >= sAlong[i] - reach) lo--;
                        if (hi < i) hi = i;
                    }
                    while (sAlong[lo] < sAlong[i] - reach) lo++;
                    while (hi + 1 < ns && sAlong[hi + 1] <= sAlong[i] + reach) hi++;
                    double wsum = 0, xsum = 0, zsum = 0;
                    for (int j = lo; j <= hi; j++)
                    {
                        double ds = sAlong[j] - sAlong[i], w = Math.Exp(-ds * ds / (2 * sg * sg));
                        wsum += w; xsum += w * surv[j].X; zsum += w * surv[j].Z;
                    }
                    double k = Geo.SmoothStep(pin[i] / (2 * sg));
                    double mx = (xsum / wsum - surv[i].X) * k, mz = (zsum / wsum - surv[i].Z) * k;
                    // hold: no further than the course may go and stay in its water
                    double lim = surv[i].Hold, moved = Math.Sqrt(mx * mx + mz * mz);
                    if (lim > 0 && moved > lim) { mx *= lim / moved; mz *= lim / moved; }
                    sx[i] = surv[i].X + mx;
                    sz[i] = surv[i].Z + mz;
                }
                for (int i = 0; i < ns; i++)
                {
                    var q = surv[i];
                    q.X = sx[i]; q.Z = sz[i];
                    surv[i] = q;
                }
            }
            for (int i = 0; i < ns; i++)
            {
                var q = surv[i];
                q.S = i == 0 ? 0 : surv[i - 1].S + Math.Sqrt((q.X - surv[i - 1].X) * (q.X - surv[i - 1].X) + (q.Z - surv[i - 1].Z) * (q.Z - surv[i - 1].Z));
                surv[i] = q;
            }
            // fewer points: within 0.75 blocks of the smoothed line (the chain's ends and lake points stay)
            var keepPt = Scratch.Get(ref scratch.KeepPt, ns, true);
            keepPt[0] = keepPt[ns - 1] = true;
            for (int i = 0; i < ns; i++) if (surv[i].Fixed) keepPt[i] = true;
            var tol = Scratch.Get(ref scratch.Tol, ns, false);
            // (cornerSigma: CornerTolerance, a turn of about 30° per vertex in the tightest bends the smoothing leaves at 1:10)
            for (int i = 0; i < ns; i++) tol[i] = corners ? CornerTolerance : 0.75;
            Simplify(surv, keepPt, tol);

            // the line as kept; reach joints that fell away move onto it (at the same distance along the river)
            var line = scratch.Line;
            line.Clear();
            int prevKept = 0;
            for (int i = 0; i < surv.Count; i++)
            {
                if (keepPt[i])
                {
                    line.Add(surv[i]);
                    prevKept = i;
                    continue;
                }
                if (surv[i].Reach == surv[i - 1].Reach) continue;
                int nextKept = i + 1;
                while (!keepPt[nextKept]) nextKept++;
                var A = surv[prevKept]; var B = surv[nextKept];
                double t = B.S > A.S ? (surv[i].S - A.S) / (B.S - A.S) : 0;
                var j = surv[i];
                j.X = A.X + (B.X - A.X) * t;
                j.Z = A.Z + (B.Z - A.Z) * t;
                line.Add(j);
            }

            // back into per-reach lines: the line runs through the chain's reaches in order; a reach ends on the first point of
            // the next (shared), and reaches cut out entirely collapse onto that point (two copies of it). Counted, then written
            // straight into the new arrays (before: a list per reach in a dictionary, grown by doubling, then copied out); a
            // reach left with fewer than two points keeps its line
            int nc = chain.Count;
            var count = Scratch.Get(ref scratch.Count, nc, true);
            WalkReaches(line, chain, count, null);
            var fill = Scratch.Get(ref scratch.At, nc, true);
            var lines = scratch.Lines ??= new List<(double[] X, double[] Z, double[] Lev, bool[] Fix, double[] Wid)>();
            lines.Clear();
            for (int ci = 0; ci < nc; ci++)
            {
                int n = count[ci], r = chain[ci];
                lines.Add(n < 2 ? default : (new double[n], new double[n], new double[n], new bool[n], wid?[r] != null ? new double[n] : null));
            }
            WalkReaches(line, chain, fill, lines);
            for (int ci = 0; ci < nc; ci++)
            {
                if (count[ci] < 2) continue;
                int r = chain[ci];
                var l = lines[ci];
                xs[r] = l.X; zs[r] = l.Z; lev[r] = l.Lev; fix[r] = l.Fix;
                if (l.Wid != null) wid[r] = l.Wid;
            }
            lines.Clear();
        }

        /// <summary>The line's points by chain position, in order (see StraightenChain's end): with into null counted into
        /// at, else written to into[position] at at[position] (only where it has arrays).</summary>
        static void WalkReaches(List<Pt> line, List<int> chain, int[] at, List<(double[] X, double[] Z, double[] Lev, bool[] Fix, double[] Wid)> into)
        {
            void Put(int ci, in Pt p)
            {
                int i = at[ci]++;
                if (into == null || into[ci].X == null) return;
                var l = into[ci];
                l.X[i] = p.X; l.Z[i] = p.Z; l.Lev[i] = p.Lev; l.Fix[i] = p.Fixed;
                if (l.Wid != null) l.Wid[i] = p.Wid;
            }
            int cur = line[0].Reach;
            int chainPos = chain.IndexOf(cur);
            Put(chainPos, line[0]);
            for (int i = 1; i < line.Count; i++)
            {
                var p = line[i];
                if (p.Reach == cur)
                {
                    Put(chainPos, p);
                    continue;
                }
                // into a later reach: this reach ends here; reaches cut out entirely collapse onto this point
                Put(chainPos, p);
                int target = chain.IndexOf(p.Reach, chainPos);
                // (the line never goes back up the chain: its points come reach by reach, each joint labelled the later one)
                if (target < 0) throw new InvalidOperationException("river chain's line goes back upstream");
                for (int ci = chainPos + 1; ci < target; ci++) { Put(ci, p); Put(ci, p); }
                Put(target, p);
                cur = p.Reach;
                chainPos = target;
            }
        }

        /// <summary>Douglas-Peucker between the points already kept (endpoints, lake points).</summary>
        static void Simplify(List<Pt> l, bool[] keep, double[] tol)
        {
            int a = 0;
            for (int b = 1; b < l.Count; b++)
            {
                if (!keep[b]) continue;
                Dp(l, keep, a, b, tol);
                a = b;
            }
        }

        /// <summary>Douglas-Peucker between kept points a and b: marks the points to keep. (T-L13, code review 14 November 2026)
        /// An explicit stack, not recursion: a chain is one point per 4 blocks, tens of thousands at 1:10, and a curve whose
        /// farthest point is always next to an end recursed once per point. Each span's choice depends only on its own ends and
        /// points, so the order the spans are taken in changes no result.</summary>
        static void Dp(List<Pt> l, bool[] keep, int a0, int b0, double[] tolAt)
        {
            var spans = new Stack<(int A, int B)>();
            spans.Push((a0, b0));
            while (spans.Count > 0)
            {
                var (a, b) = spans.Pop();
                if (b <= a + 1) continue;
                double tol = Math.Min(tolAt[a], tolAt[b]);
                double ax = l[a].X, az = l[a].Z, bx = l[b].X, bz = l[b].Z;
                double dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
                int best = -1;
                double bestD = tol;
                for (int i = a + 1; i < b; i++)
                {
                    double px = l[i].X - ax, pz = l[i].Z - az;
                    double t = len2 > 0 ? Math.Clamp((px * dx + pz * dz) / len2, 0, 1) : 0;
                    double ex = px - t * dx, ez = pz - t * dz, dd = Math.Sqrt(ex * ex + ez * ez);
                    if (dd > bestD) { bestD = dd; best = i; }
                }
                if (best < 0) continue;
                keep[best] = true;
                spans.Push((best, b));
                spans.Push((a, best));
            }
        }
    }
}
