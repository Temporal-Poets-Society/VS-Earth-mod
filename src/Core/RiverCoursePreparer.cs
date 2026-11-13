using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// The courses a river network's pools and pieces are laid out on, per reach (indices as in the reach list; null or
    /// empty for reaches the world doesn't show): world positions, levels and lake/sea flags per vertex as straightened, each
    /// piece's length, and the kept inflows. Build data only: the network keeps the lines (ReachLine), the rest is dropped
    /// once it is built.
    /// </summary>
    internal sealed class RiverCourses
    {
        public List<RiverReach> Reaches;
        public int Count;
        /// <summary>Whether a reach is part of this world's rivers (navigable, or a stream).</summary>
        public bool[] Keep;
        public double[][] X, Z, Level;
        public bool[][] Fixed;
        /// <summary>Channel rules 1: the river's measured half width at each vertex (blocks, 0 where it has none); null without
        /// any.</summary>
        public double[][] MeasuredHalfWidth;
        /// <summary>Per reach, the length of each piece (vertex k to k + 1; 1 for a piece across the seam).</summary>
        public double[][] Lengths;
        /// <summary>Per reach, its kept inflows in reach order (null for none).</summary>
        public List<int>[] Inflows;
        /// <summary>Reaches smoothed along the way (channel rules 1).</summary>
        public int Smoothed;
    }

    /// <summary>
    /// The first stage of a river network's build: which reaches the world shows, their courses in world coordinates, the
    /// measured widths (channel rules 1), straightened for the widths (RiverStraighten), brought down to the sea's level where
    /// they cross a sea-level cut, and measured.
    /// </summary>
    internal static class RiverCoursePreparer
    {
        /// <param name="net">The network being built: its settings and width rules are set, nothing else.</param>
        /// <param name="blockMetres">Metres per block north-south (0 without a ring scale).</param>
        public static RiverCourses Prepare(RiverNetwork net, EarthGeometry geo, List<RiverReach> reaches, RiverBuildOptions options, double blockMetres)
        {
            int channelRules = options.ChannelRules;
            double landCellBlocks = net.LandCellBlocks;
            int nr = reaches.Count;
            var keep = SelectReaches(reaches, options.MinQ, net.StreamMode, net.StreamMinQ, options.ContinueDownstream);
            // world positions, levels and lake/sea flags per reach
            var xs = new double[nr][];
            var zs = new double[nr][];
            var lev = new double[nr][];
            var fix = new bool[nr][];
            // each reach on its own (6 M vertices at 1:10: on several cores from 2.0.0-rc.4, ParallelWork)
            ParallelWork.For(nr, r =>
            {
                var re = reaches[r];
                if (!keep[r]) return;
                int n = re.Lon.Length;
                xs[r] = new double[n]; zs[r] = new double[n]; lev[r] = new double[n]; fix[r] = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    geo.FromGeo(re.Lat[k], re.Lon[k], out xs[r][k], out zs[r][k]);
                    lev[r][k] = re.Level[k];
                    fix[r][k] = re.Fixed[k];
                }
            }, chunk: 4096);
            // channel rules 1 (0063): the river's measured half width at each vertex (courses rules 4's RiverReach.Width, 5 m units)
            // as a share of the real width typical of its flow (RealWidthMetres), times the half width the game draws that flow
            // at (HalfWidth): the drawn river narrows where the real one is narrower than its flow's usual, by the same ratio,
            // at any scale. Taken in blocks as measured, it was under the drawn width wherever the game draws rivers wider than
            // real ones (every river at 1:100, most at 1:10), and every such river came out half as wide and twice as deep. 0
            // where the mask has none, or 255 (that wide or more: no cap)
            double[][] mw = null;
            if (channelRules >= 1 && blockMetres > 0)
            {
                for (int r = 0; r < nr; r++)
                {
                    var w = reaches[r].Width;
                    if (!keep[r] || w == null || w.Length != xs[r].Length) continue;
                    var m = new double[w.Length];
                    bool any = false;
                    double q = reaches[r].Q, perMetre = net.HalfWidth(q) / Math.Max(1, 0.5 * RiverNetwork.RealWidthMetres(q));
                    for (int k = 0; k < w.Length; k++)
                    {
                        if (w[k] == 0 || w[k] == 255) continue;
                        m[k] = Math.Max(RiverNetwork.MinHalfWidth, w[k] * RiverReach.WidthUnit * perMetre);
                        any = true;
                    }
                    if (any) (mw ??= new double[nr][])[r] = m;
                }
            }
            // straighter courses for the exaggerated widths (loop cut-offs, smoothing)
            // the courses are laid out with the widths before width rules 1 (grown with the world): narrower water on a big map
            // keeps the same smooth centre line instead of showing the river data's 15" staircase
            // 0053: courses snapped to GLO-90's valley floors keep their bends; only loops tighter than the water's own width
            // are cut (the water would overlap itself), not those of the widths grown with the world; no smoothing, and
            // chains without a cut keep their own vertices
            Func<double, double> courseWidth = landCellBlocks > 0 ? net.HalfWidth : net.CourseHalfWidth;
            // channel rules 1 (0063): every course runs in eight directions: courses rules 4 moved a reach onto the mask's water
            // along an 8-connected pixel path smoothed by one 90 m pixel, and left the rest on HydroRIVERS' 15" grid. At 1:10 that
            // shows as straight legs of 50 to 150 blocks with 45° and 90° corners (the Tigris below Amarah). The courses are
            // smoothed along the way by a Gaussian of CourseSmoothMetres (a corner moves in by up to about a quarter of that, a
            // 45° one by about a sixth), held where the mask measured the river to its real half width there, so the course
            // stays in its water: a pixel path's corner is cut, a real meander keeps its swing (the Gauja). Not where the
            // sigma is under the course's 4-block sampling (scales from about 1:50), where the legs are no wider than the water
            double[] cornerSigma = null;
            double[][] hold = null;
            int smoothed = 0;
            if (channelRules >= 1 && landCellBlocks > 0 && blockMetres > 0)
            {
                double sigma = RiverNetwork.CourseSmoothMetres / blockMetres;
                if (sigma >= RiverNetwork.CourseSmoothMinBlocks)
                {
                    cornerSigma = new double[nr];
                    hold = new double[nr][];
                    for (int r = 0; r < nr; r++)
                    {
                        if (!keep[r] || xs[r] == null) continue;
                        cornerSigma[r] = sigma;
                        smoothed++;
                        var w = reaches[r].Width;
                        if (w == null || w.Length != xs[r].Length) continue;
                        var h = new double[w.Length];
                        for (int k = 0; k < w.Length; k++) h[k] = w[k] == 0 || w[k] == 255 ? 0 : w[k] * RiverReach.WidthUnit / blockMetres;
                        hold[r] = h;
                    }
                }
            }
            RiverStraighten.Apply(reaches, keep, xs, zs, lev, fix, courseWidth, net.Straighten, landCellBlocks > 0 ? 0 : Math.Max(0, options.SmoothWidths),
                options.GeometrySeams ? geo : null, cutWidths: landCellBlocks > 0 ? 1 : 0, wid: mw, cornerSigma: cornerSigma, hold: hold);
            // pieces crossing sea-level water cut through the land (canals and their lakes): the river comes down to the sea's
            // level there, as at a lake (else it would stand above the cut in a wall of water)
            var seaLevelPiece = options.SeaLevelPiece;
            if (seaLevelPiece != null)
            {
                for (int r = 0; r < nr; r++)
                {
                    if (!keep[r]) continue;
                    for (int k = 0; k + 1 < xs[r].Length; k++)
                    {
                        if (!seaLevelPiece(xs[r][k], zs[r][k], xs[r][k + 1], zs[r][k + 1])) continue;
                        lev[r][k] = lev[r][k + 1] = 0;
                        fix[r][k] = fix[r][k + 1] = true;
                    }
                }
            }
            var lens = new double[nr][];
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int n = xs[r].Length;
                lens[r] = new double[Math.Max(0, n - 1)];
                for (int k = 1; k < n; k++)
                {
                    double len = Math.Sqrt(Sq(xs[r][k] - xs[r][k - 1]) + Sq(zs[r][k] - zs[r][k - 1]));
                    lens[r][k - 1] = net.AcrossSeam(xs[r][k - 1], xs[r][k], len) ? 1 : len;
                }
            }
            // channel rules 5 (decision 0074): one width along a river. The mask measures a river at 90 m, so one 180 m wide
            // is 2 pixels here and 3 there by how it lies on the grid; its measured width went up and down by half along it,
            // and the channel with it (wide pieces and narrow ones by turns, the Lielupe's and the Oder's "ponds"). Each
            // reach's measured half widths are averaged along it (OneWidth), so a widening shorter than a few widths is gone
            // and a longer one stays.
            if (mw != null && channelRules >= RiverNetwork.PondChannelRules)
                for (int r = 0; r < nr; r++)
                    if (keep[r] && mw[r] != null && lens[r] != null && mw[r].Length == lens[r].Length + 1)
                        mw[r] = OneWidth(mw[r], lens[r], RiverNetwork.OneWidthSigma * 2 * net.HalfWidth(reaches[r].Q));
            var inflows = new List<int>[nr];
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int nx = reaches[r].Next;
                if (nx >= 0 && nx < nr && keep[nx]) (inflows[nx] ??= new List<int>()).Add(r);
            }
            return new RiverCourses
            {
                Reaches = reaches, Count = nr, Keep = keep, X = xs, Z = zs, Level = lev, Fixed = fix, MeasuredHalfWidth = mw,
                Lengths = lens, Inflows = inflows, Smoothed = smoothed,
            };
        }

        static double Sq(double v) => v * v;

        /// <summary>Channel rules 5: a reach's measured half widths (0: none there) averaged along it, each over the measured
        /// ones within 3 sigma (blocks along the course) weighted by a Gaussian of sigma; a vertex without one keeps none.</summary>
        internal static double[] OneWidth(double[] m, double[] lens, double sigma)
        {
            int n = m.Length;
            var s = new double[n];
            for (int k = 1; k < n; k++) s[k] = s[k - 1] + lens[k - 1];
            var o = new double[n];
            double far = 3 * sigma, inv = 1 / (2 * sigma * sigma);
            int lo = 0;
            for (int k = 0; k < n; k++)
            {
                if (m[k] <= 0) continue;
                while (s[lo] < s[k] - far) lo++;
                double sum = 0, wsum = 0;
                for (int j = lo; j < n && s[j] <= s[k] + far; j++)
                {
                    if (m[j] <= 0) continue;
                    double d = s[j] - s[k], wt = Math.Exp(-d * d * inv);
                    sum += wt * m[j];
                    wsum += wt;
                }
                o[k] = sum / wsum;
            }
            return o;
        }

        /// <summary>
        /// The reaches this world shows: the navigable rivers (discharge from minQ), and streams by mode: "all" down to
        /// streamMinQ, "ends" only the continuation of each river upstream from where it drops below minQ, along its
        /// biggest branch, down to streamMinQ. With continueDownstream also everything below a shown reach, to the river's
        /// end: a river losing water downstream (the Darling, the Rio Grande, wadis) narrows instead of stopping where its
        /// discharge drops below minQ, and comes back as one river where it grows again (the Niger, the Zambezi).
        /// </summary>
        static bool[] SelectReaches(List<RiverReach> reaches, double minQ, string mode, double streamMinQ, bool continueDownstream = false)
        {
            int nr = reaches.Count;
            var keep = new bool[nr];
            for (int r = 0; r < nr; r++)
            {
                double q = reaches[r].Q;
                keep[r] = q >= minQ || (mode == "all" && q >= streamMinQ);
            }
            if (mode != "ends")
            {
                if (continueDownstream) ContinueDownstream(reaches, keep);
                return keep;
            }
            // the biggest inflow of each reach
            var bigIn = new int[nr];
            var bigQ = new double[nr];
            for (int r = 0; r < nr; r++) bigIn[r] = -1;
            for (int r = 0; r < nr; r++)
            {
                int nx = reaches[r].Next;
                if (nx < 0 || nx >= nr) continue;
                if (reaches[r].Q > bigQ[nx]) { bigQ[nx] = reaches[r].Q; bigIn[nx] = r; }
            }
            for (int r = 0; r < nr; r++)
            {
                // the upper end of a navigable river: its biggest inflow is below the navigable size
                if (reaches[r].Q < minQ) continue;
                int up = bigIn[r];
                if (up < 0 || reaches[up].Q >= minQ) continue;
                for (int guard = 0; up >= 0 && guard < 100000 && reaches[up].Q >= streamMinQ && !keep[up]; guard++)
                {
                    keep[up] = true;
                    up = bigIn[up];
                }
            }
            if (continueDownstream) ContinueDownstream(reaches, keep);
            return keep;
        }

        static void ContinueDownstream(List<RiverReach> reaches, bool[] keep)
        {
            int nr = reaches.Count;
            // reaches are in topological order (sources first): one pass carries "shown" down every course
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int nx = reaches[r].Next;
                if (nx >= 0 && nx < nr) keep[nx] = true;
            }
        }
    }
}
