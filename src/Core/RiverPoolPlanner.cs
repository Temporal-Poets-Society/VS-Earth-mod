using System;
using System.Collections.Generic;
using System.Linq;
using static EarthMap.Core.RiverNetwork;

namespace EarthMap.Core
{
    /// <summary>
    /// The pool layout's generations (RiverNetwork.PoolLayoutVersion, EarthParams.RiverPoolLayout) by what each rule does,
    /// so a branch reads as its rule. A world keeps the layout it was made with; each rule holds from its version on.
    /// </summary>
    internal readonly struct RiverPoolRules
    {
        public readonly int Layout;
        public RiverPoolRules(int layout) { Layout = layout; }
        /// <summary>1: each pool at least MinPool long and a drop as high as the valley has fallen; a listed fall is a drop
        /// like any other (as high as what lies below allows; the river holds its level also with the lip right here);
        /// rapids where a drop is 2 blocks or more, carried into the next reach. 0: one block per step.</summary>
        public bool ValleyDrops => Layout >= 1;
        /// <summary>2: a drop of 2–4 blocks is a chute of 1-block steps, 5 or more (and every listed fall) a curtain with a lip
        /// and a plunge pool; every step but a lone 1-block one is rapids.</summary>
        public bool Chutes => Layout >= 2;
        /// <summary>3: the bed eases into and out of chutes, lips and plunge pools (queries); just above a listed fall the
        /// river takes no forced drop, the fall takes it at its lip.</summary>
        public bool EasedBeds => Layout >= 3;
        /// <summary>7 only: white water - chutes and gorge sections of steps WhiteWaterSpacing apart, rapids runs.</summary>
        public bool Sections => Layout == 7;
        /// <summary>8: rapids reaches, by the real valley's slope around each reach.</summary>
        public bool ReachRapids => Layout >= 8;
        /// <summary>9: the river steps down a block at a time as soon as the valley is a block lower, StepGap apart.</summary>
        public bool StepByStep => Layout >= 9;
        /// <summary>10: the steps are led by the valley ahead (StepLead).</summary>
        public bool LeadsValley => Layout >= 10;
        /// <summary>11: a rapids reach also needs a strong current for its size (RapidsByPower), none where the world turned
        /// rapids off.</summary>
        public bool RapidsByPower => Layout >= 11;
    }

    /// <summary>
    /// The second stage of a river network's build: the pools on the prepared courses. Places the listed waterfalls, then
    /// lays out each reach's water as a staircase of pools by the world's pool layout (RiverPoolRules): backward bounds from
    /// the levels the river must meet downstream, a forward pass placing the steps, both repeated so tributaries come down to
    /// the level the main river has at the confluence; then marks which steps and reaches are rapids. The network keeps the
    /// pools, the falls as placed and the rapids reaches; the rest is dropped once it is built.
    /// </summary>
    internal sealed class RiverPoolPlanner
    {
        /// <summary>How often the layout runs at most: a tributary left above the main river at a confluence comes down to it
        /// in the next run.</summary>
        const int MaxRounds = 4;

        readonly RiverNetwork net;
        readonly RiverPoolRules rules;
        readonly EarthGeometry geo;
        readonly Func<double, int> waterTop;
        readonly IReadOnlyList<RiverFall> falls;
        readonly List<RiverReach> reaches;
        readonly int nr;
        readonly bool[] keep;
        readonly double[][] xs, zs, lev, lens;
        readonly bool[][] fix;
        readonly List<int>[] inflows;

        /// <summary>A waterfall on a reach: the piece of the course (vertex Piece to Piece + 1), the distance along it, the
        /// drop (blocks).</summary>
        struct FallSite
        {
            public int Piece, Drop;
            /// <summary>Distance along the piece, and along the reach.</summary>
            public double Along, S;
            /// <summary>Lowest water top the river may drop to here (a lake, the sea or a bigger river downstream); index in
            /// the placed falls.</summary>
            public int Floor, Index;
        }

        /// <summary>
        /// The water as the forward pass goes along a river: the pool's top, the distance since the last step, and (pool
        /// layout 7) whether a rapids run goes on (its next drop comes WhiteWaterSpacing after the last, not MinPool), how many
        /// steps of a gorge section are still to come and whether a run follows them (a chute's, not a gorge section's). A
        /// reach's water at its end carries on into the reach downstream (HydroRIVERS' reaches can be only a few blocks long).
        /// </summary>
        struct Water
        {
            public int P;
            public double Dsd;
            public bool Run;
            public int GorgeLeft;
            public bool RunAfter;
        }

        // falls: per reach the falls on it (null: none), the distance below its end to the next one, the drops they got
        List<FallSite>[] fallsOn;
        double[] fallAhead;
        int[] fallDrops;
        // per reach: its length; per vertex the backward bound and (pool layout 10) the valley's lead; the cap a confluence
        // sets, how far into the reach a lake's level still holds; the water at its end
        double[] reachLen;
        double[][] bound, lead;
        double[] junctionCap, holdAtStart;
        Water[] ends;
        Pools[] pools;
        int gorgeSections, gorgeSteps;
        bool[] reachRapids;
        float[] reachSlope, reachValley, reachQ;

        public RiverPoolPlanner(RiverNetwork net, EarthGeometry geo, RiverCourses courses, IReadOnlyList<RiverFall> falls, Func<double, int> waterTop)
        {
            this.net = net;
            rules = net.PoolRules;
            this.geo = geo;
            this.waterTop = waterTop;
            this.falls = falls;
            reaches = courses.Reaches;
            nr = courses.Count;
            keep = courses.Keep;
            xs = courses.X;
            zs = courses.Z;
            lev = courses.Level;
            fix = courses.Fixed;
            lens = courses.Lengths;
            inflows = courses.Inflows;
        }

        /// <summary>Per reach its pools (null where not kept).</summary>
        public Pools[] ReachPools => pools;
        /// <summary>The falls as placed, with the drops they got.</summary>
        public List<PlacedFall> PlacedFalls { get; private set; }
        /// <summary>For DebugReach: per reach the cap its confluence set and its backward bound at its start (NaN where not kept).</summary>
        public double[] JunctionCaps => junctionCap;
        public double[] FirstBounds()
        {
            var b0 = new double[nr];
            for (int r = 0; r < nr; r++) b0[r] = bound[r] != null ? bound[r][0] : double.NaN;
            return b0;
        }
        /// <summary>Pool layout 7: the gorge sections the layout made and their steps.</summary>
        public int GorgeSections => gorgeSections;
        public int GorgeSteps => gorgeSteps;
        /// <summary>Basins whose junction caps were still being lowered when the rounds (MaxRounds) ran out (CR-R7).</summary>
        public int UnconvergedBasins => unconvergedBasins;
        int unconvergedBasins;
        /// <summary>Pool layout 8, per reach: rapids or not, the water's fall per block, the valley's fall (m/km), the
        /// discharge (null before layout 8).</summary>
        public bool[] ReachRapids => reachRapids;
        public float[] ReachSlope => reachSlope;
        public float[] ReachValley => reachValley;
        public float[] ReachQ => reachQ;

        /// <summary>How far above a fall the river keeps its level (blocks): a tenth of a degree, about the reach of the 5'
        /// window the levels were taken in.</summary>
        double FallHold => 0.1 * geo.BlocksPerDegreeX;
        int MinPool => net.MinPool;
        static double Sq(double v) => v * v;

        /// <summary>Lays out the pools (once per planner).</summary>
        public void Plan()
        {
            // waterfalls: per reach, the piece of its course they are on, the distance along that piece, the drop (blocks)
            PlacedFalls = new List<PlacedFall>();
            fallsOn = PlaceFalls(PlacedFalls);
            // per reach: its length (every kept reach's, also without falls: pool layout 8's rapids reaches average over them)
            reachLen = new double[nr];
            for (int r = 0; r < nr; r++) if (keep[r]) foreach (double l in lens[r]) reachLen[r] += l;
            if (fallsOn != null) FallsBelow();
            fallDrops = new int[PlacedFalls.Count];
            LayOutPools();
            // the drops the falls got (less than their height where a lake or a bigger river lies close below)
            for (int i = 0; i < PlacedFalls.Count; i++)
            {
                var pf = PlacedFalls[i];
                pf.Drop = fallDrops[i];
                PlacedFalls[i] = pf;
            }
            MarkRapidSteps();
            if (rules.Chutes) CurtainHeights();
            if (rules.ValleyDrops) RapidsIntoNextReach();
            if (rules.ReachRapids) FindRapidsReaches();
        }

        /// <summary>
        /// Puts each waterfall on the river it belongs to: the kept course nearest to its lip, preferring the bigger river
        /// within reach (a tributary may join just above or below a fall). The drop is the fall's height on the scale of the
        /// lowlands (as tall as a cliff of that height by the sea), at least one block. Falls with no river near them are left out.
        /// </summary>
        List<FallSite>[] PlaceFalls(List<PlacedFall> placed)
        {
            if (falls == null || falls.Count == 0) return null;
            var on = new List<FallSite>[nr];
            var reachOf = new double[nr];
            var box = new (double X0, double Z0, double X1, double Z1)[nr];
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                reachOf[r] = net.HalfWidth(reaches[r].Q) + 32 * net.SizeScale;
                var x = xs[r]; var z = zs[r];
                box[r] = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                for (int k = 0; k < x.Length; k++)
                    box[r] = (Math.Min(box[r].X0, x[k]), Math.Min(box[r].Z0, z[k]), Math.Max(box[r].X1, x[k]), Math.Max(box[r].Z1, z[k]));
            }
            foreach (var fall in falls)
            {
                geo.FromGeo(fall.Lat, fall.Lon, out double fx, out double fz);
                int bestR = -1, bestK = 0;
                double bestQ = -1, bestD = double.MaxValue, bestT = 0;
                for (int r = 0; r < nr; r++)
                {
                    if (!keep[r]) continue;
                    double q = reaches[r].Q, reach = reachOf[r];
                    if (box[r].X0 - reach > fx || box[r].X1 + reach < fx || box[r].Z0 - reach > fz || box[r].Z1 + reach < fz) continue;
                    var x = xs[r]; var z = zs[r];
                    for (int k = 0; k + 1 < x.Length; k++)
                    {
                        if (lens[r][k] <= 1) continue; // across the seam
                        double x1 = x[k], z1 = z[k], dx = x[k + 1] - x1, dz = z[k + 1] - z1;
                        if (Math.Min(x1, x1 + dx) - reach > fx || Math.Max(x1, x1 + dx) + reach < fx
                            || Math.Min(z1, z1 + dz) - reach > fz || Math.Max(z1, z1 + dz) + reach < fz) continue;
                        double l2 = dx * dx + dz * dz;
                        double t = l2 > 0 ? Math.Clamp(((fx - x1) * dx + (fz - z1) * dz) / l2, 0, 1) : 0;
                        double d = Math.Sqrt(Sq(fx - x1 - t * dx) + Sq(fz - z1 - t * dz));
                        if (d > reach) continue;
                        if (q > bestQ * 1.5 || (q * 1.5 >= bestQ && d < bestD))
                        {
                            bestR = r; bestK = k; bestQ = Math.Max(q, bestQ); bestD = d; bestT = t;
                        }
                    }
                }
                if (bestR < 0) continue;
                int drop = Math.Max(1, waterTop(fall.Height) - waterTop(0));
                double along = bestT * lens[bestR][bestK], start = 0;
                for (int k = 0; k < bestK; k++) start += lens[bestR][k];
                (on[bestR] ??= new List<FallSite>()).Add(new FallSite { Piece = bestK, Along = along, S = start + along, Drop = drop, Floor = int.MinValue, Index = placed.Count });
                on[bestR].Sort((a, b) => a.Piece != b.Piece ? a.Piece.CompareTo(b.Piece) : a.Along.CompareTo(b.Along));
                placed.Add(new PlacedFall
                {
                    Name = fall.Name, Reach = bestR, Q = reaches[bestR].Q, Drop = drop,
                    X = xs[bestR][bestK] + bestT * (xs[bestR][bestK + 1] - xs[bestR][bestK]),
                    Z = zs[bestR][bestK] + bestT * (zs[bestR][bestK + 1] - zs[bestR][bestK]),
                });
            }
            return on;
        }

        /// <summary>
        /// Per reach how far below its end the next fall is (downstream reaches come later), and per fall how low it may take
        /// its river: never below a lake or the sea it runs into, nor below the bigger river it joins (Murchison Falls above
        /// Lake Albert, Iguazu above the Paraná), else the river would run uphill into them. Lakes within FallHold below the
        /// lip don't count: water standing at the lip's level right below a fall is a reservoir fed from above it (the power
        /// reservoirs beside the Niagara gorge).
        /// </summary>
        void FallsBelow()
        {
            fallAhead = new double[nr];
            for (int r = nr - 1; r >= 0; r--)
            {
                fallAhead[r] = double.PositiveInfinity;
                if (!keep[r]) continue;
                int nx = reaches[r].Next;
                if (nx < 0 || nx >= nr || !keep[nx]) continue;
                fallAhead[r] = fallsOn[nx] != null ? fallsOn[nx][0].S : reachLen[nx] + fallAhead[nx];
            }
            var floorStart = new int[nr];
            for (int r = nr - 1; r >= 0; r--)
            {
                if (!keep[r]) continue;
                int nx = reaches[r].Next, n = lev[r].Length;
                int end = nx < 0 || nx >= nr || !keep[nx] ? waterTop(lev[r][n - 1])
                    : reaches[nx].Q > 1.5 * reaches[r].Q ? waterTop(lev[nx][0]) : floorStart[nx];
                int floor = end;
                for (int k = n - 1; k >= 0; k--) if (fix[r][k]) floor = Math.Max(floor, waterTop(lev[r][k]));
                floorStart[r] = floor;
                if (fallsOn[r] == null) continue;
                for (int i = 0; i < fallsOn[r].Count; i++)
                {
                    var site = fallsOn[r][i];
                    int f = end;
                    double d = lens[r][site.Piece] - site.Along;
                    for (int k = site.Piece + 1; k < n; k++)
                    {
                        if (fix[r][k] && d > FallHold) f = Math.Max(f, waterTop(lev[r][k]));
                        if (k < n - 1) d += lens[r][k];
                    }
                    site.Floor = f;
                    fallsOn[r][i] = site;
                }
            }
        }

        /// <summary>
        /// The rounds of the layout: each river system (basin) on its own, backward bounds then the forward pass, until no
        /// tributary's cap at a confluence changes (at most MaxRounds). River systems are independent: no reach reads another
        /// basin's bounds, pools or levels (inflows, the reach downstream and the valley ahead all stay in one basin), so the
        /// basins run in parallel with the same result as one after another, each its reaches in their order.
        /// </summary>
        void LayOutPools()
        {
            bound = new double[nr][];
            lead = rules.LeadsValley ? new double[nr][] : null;
            ends = new Water[nr];
            junctionCap = new double[nr];
            holdAtStart = new double[nr];
            for (int r = 0; r < nr; r++) junctionCap[r] = double.PositiveInfinity;
            pools = new Pools[nr];
            var basins = Basins(reaches, keep);
            // pool layout 7's diagnostics: the gorge sections of each basin's last run (they are its final layout's)
            var basinGorge = new (int Sections, int Steps)[basins.Count];
            // A basin's round reads only its own reaches and caps, so a round that leaves its caps as they were would lay it
            // out the same again: only the basins whose caps changed run on, and the rounds end where none did, as when every
            // basin ran every round. A basin with a reach that doesn't flow into a later one (the river files keep them in
            // flow order) would read its own previous round's inflows: it runs every round as before.
            var unordered = basins.Select(m => !InFlowOrder(m)).ToArray();
            var lowered = new bool[basins.Count];
            var run = Enumerable.Range(0, basins.Count).ToList();
            for (int iter = 0; iter < MaxRounds; iter++)
            {
                // (ER-25: on ParallelWork's few long-lived workers, each with one Worker, one basin at a time)
                var thisRound = run;
                ParallelWork.For(thisRound.Count, () => new Worker(), (k, wk) =>
                {
                    int b = thisRound[k];
                    lowered[b] = RunBasin(basins[b], wk);
                    basinGorge[b] = (wk.GorgeSections, wk.GorgeSteps);
                });
                if (!run.Any(b => lowered[b])) break;
                run = Enumerable.Range(0, basins.Count).Where(b => lowered[b] || unordered[b]).ToList();
                // the rounds ran out with caps still lowered: those basins keep a tributary above its main river at a
                // confluence; counted so the start says so (CR-R7; more rounds would change the layout of existing worlds)
                if (iter == MaxRounds - 1) unconvergedBasins = run.Count(b => lowered[b]);
            }
            foreach (var (sections, steps) in basinGorge)
            {
                gorgeSections += sections;
                gorgeSteps += steps;
            }
        }

        /// <summary>A worker's lists for the forward pass, and the gorge sections its current basin's run made.</summary>
        sealed class Worker
        {
            public readonly List<float> Drops = new List<float>();
            public readonly List<byte> Shapes = new List<byte>();
            public int GorgeSections, GorgeSteps;
        }

        /// <summary>Whether every reach of a basin flows into a later one (or into none it keeps).</summary>
        bool InFlowOrder(int[] members)
        {
            foreach (int r in members)
            {
                int nx = reaches[r].Next;
                if (nx >= 0 && nx < nr && keep[nx] && nx <= r) return false;
            }
            return true;
        }

        /// <summary>One round of a basin: backward bounds, the forward pass, the caps its confluences set for the next round.
        /// Whether a cap changed.</summary>
        bool RunBasin(int[] members, Worker wk)
        {
            wk.GorgeSections = wk.GorgeSteps = 0;
            // backward: reaches are in topological order (sources first), so walk from the end
            for (int m = members.Length - 1; m >= 0; m--) if (keep[members[m]]) BackwardBounds(members[m]);
            // forward, one block at a time along each reach
            for (int m = 0; m < members.Length; m++) if (keep[members[m]]) LayOutReach(members[m], wk);
            return LowerJunctionCaps(members);
        }

        /// <summary>
        /// A reach's backward bounds: per vertex how high its pool may still be to reach the levels downstream with a step
        /// every RampLength blocks (a lake's or the sea's, the reach below's, the main river's at a confluence: its cap), a
        /// listed fall downstream taking its whole drop at once; and (pool layout 10) the lowest level the water must come
        /// down to so it meets the valley downstream with steps StepSpacing apart (+infinity where nothing ahead asks for it).
        /// </summary>
        void BackwardBounds(int r)
        {
            var re = reaches[r];
            int n = xs[r].Length;
            var bd = bound[r] ??= new double[n];
            int nx = re.Next;
            double b = junctionCap[r];
            if (nx >= 0 && nx < nr && keep[nx] && bound[nx] != null) b = Math.Min(b, bound[nx][0]);
            // arrive at a lake's or the sea's level a little before the first vertex in it (the drawn shore can be up to a data
            // cell ahead of it)
            double hold = nx >= 0 && nx < nr && keep[nx] ? holdAtStart[nx] : 0, margin = 16 + 2 * net.HalfWidth(re.Q);
            for (int k = n - 1; k >= 0; k--)
            {
                if (k < n - 1)
                {
                    double len = lens[r][k], held = Math.Min(hold, len);
                    hold -= held;
                    b += (len - held) / RampLength;
                    // a waterfall downstream takes its whole drop at once
                    if (fallsOn?[r] != null) foreach (var f in fallsOn[r]) if (f.Piece == k) b += f.Drop;
                }
                if (fix[r][k])
                {
                    // a lake or the sea has its own level: what lies downstream (a fall out of it) never lowers it
                    b = waterTop(lev[r][k]);
                    hold = margin;
                }
                // (EA-0165) a streams layer's network comes down in short falls as soon as it lags (MaxLag): its pools are never
                // held under their own level to reach the levels downstream a step every RampLength blocks. A stream falling
                // faster than that for long (1 in 3.5 from a lake at 28.37° N 84.78° E) had its pools laid out up to 120 blocks
                // under its levels from the bottom up: a trench below the lake. Not at a reach's end, where its confluence's cap
                // holds.
                // (EA-0179: not a coarse world's one-block streams, whose levels are not fitted to the land: their water would
                // rise to a level the land drawn at 1:50 can lie under)
                else if (net.MaxLag < RiverNetwork.ChuteMaxDrop && k < n - 1) b = Math.Max(b, waterTop(lev[r][k]));
                bd[k] = b;
            }
            holdAtStart[r] = hold;
            if (lead != null)
            {
                // the valley downstream, reached at one block per StepSpacing; not across a listed fall
                var ld = lead[r] ??= new double[n];
                double a = nx >= 0 && nx < nr && keep[nx] && lead[nx] != null ? lead[nx][0] : double.PositiveInfinity;
                for (int k = n - 1; k >= 0; k--)
                {
                    if (k < n - 1)
                    {
                        a += lens[r][k] / StepSpacing;
                        if (fallsOn?[r] != null) foreach (var f in fallsOn[r]) if (f.Piece == k) a = double.PositiveInfinity;
                    }
                    a = Math.Min(a, waterTop(lev[r][k]));
                    ld[k] = a;
                }
            }
        }

        /// <summary>Tributaries whose last pool is above the main river at the confluence descend to it next round: their
        /// caps. Whether one changed.</summary>
        bool LowerJunctionCaps(int[] members)
        {
            bool lowered = false;
            for (int m = 0; m < members.Length; m++)
            {
                var ins = inflows[members[m]];
                if (ins == null || ins.Count < 2) continue;
                int pm = int.MaxValue;
                foreach (int i in ins) pm = Math.Min(pm, ends[i].P);
                foreach (int i in ins)
                {
                    if (ends[i].P > pm && junctionCap[i] > pm) { junctionCap[i] = pm; lowered = true; }
                }
            }
            return lowered;
        }

        /// <summary>The water a reach starts with: the lowest inflow's at its end (of equals, the biggest river's: its main
        /// inflow), else at its source as high as its valley but no higher than what lies downstream allows; never above a
        /// lake it starts in.</summary>
        Water StartWater(int r, out int mainIn)
        {
            var ins = inflows[r];
            mainIn = -1;
            Water w;
            if (ins != null && ins.Count > 0)
            {
                // the lowest inflow sets the level (the others step down to it); of equals, the biggest river
                int best = ins[0];
                foreach (int i in ins)
                {
                    if (ends[i].P < ends[best].P || (ends[i].P == ends[best].P && reaches[i].Q > reaches[best].Q)) best = i;
                }
                w = ends[best];
                mainIn = best;
            }
            else w = new Water { P = (int)Math.Min(waterTop(lev[r][0]), Math.Floor(bound[r][0])), Dsd = 1e6 };
            if (fix[r][0] && w.P > waterTop(lev[r][0]))
            {
                w.P = waterTop(lev[r][0]);
                if (rules.Sections) { w.GorgeLeft = 0; w.Run = false; }
            }
            return w;
        }

        /// <summary>The forward pass along one reach, one block at a time: its pools (start level, steps and their shapes,
        /// length) and the water at its end.</summary>
        void LayOutReach(int r, Worker wk)
        {
            int n = xs[r].Length;
            var w = StartWater(r, out int mainIn);
            var pl = pools[r] ??= new Pools();
            int next = reaches[r].Next;
            pl.MainIn = mainIn;
            pl.Next = next >= 0 && next < nr && keep[next] ? next : -1;
            pl.P0 = w.P;
            pl.D0 = (float)Math.Min(w.Dsd, 1e6);
            wk.Drops.Clear();
            wk.Shapes.Clear();
            // (the reach's length as summed for reachLen: the same sum in the same order)
            double reachLength = reachLen[r];
            double s0 = 0;
            var reachFalls = fallsOn?[r];
            // the first fall further downstream, as a distance along this reach (holding the level may begin here)
            double nextFall = fallAhead != null ? reachLen[r] + fallAhead[r] : double.PositiveInfinity;
            for (int k = 1; k < n; k++)
            {
                double L = lens[r][k - 1];
                int steps = Math.Max(1, (int)Math.Ceiling(L));
                for (int u = 1; u <= steps; u++)
                {
                    double f = (double)u / steps, s = s0 + f * L;
                    w.Dsd += L / steps;
                    bool aboveFall = nextFall - s <= FallHold;
                    if (reachFalls != null && TakeFalls(r, k, u, steps, f, L, s, s0, reachFalls, ref w, wk)) aboveFall = true;
                    if (rules.Sections && w.GorgeLeft > 0) ContinueSection(aboveFall, s, ref w, wk);
                    // the valley floor here, and how high the pool may be to still meet the levels downstream
                    int valley = waterTop(lev[r][k - 1] + (lev[r][k] - lev[r][k - 1]) * f);
                    double cap = Math.Floor(bound[r][k] + (1 - f) * L / RampLength); // may be infinite
                    if (rules.ValleyDrops) DropToValley(r, k, u, f, s, nextFall, reachLength, valley, cap, aboveFall, ref w, wk);
                    else StepLayout0(valley, cap, aboveFall, s, ref w, wk);
                }
                // a lake or the sea: exactly its level
                if (fix[r][k]) DownToLevel(waterTop(lev[r][k]), (float)(s0 + L), ref w, wk);
                s0 += L;
            }
            pl.Drops = wk.Drops.ToArray();
            pl.Shape = wk.Shapes.ToArray();
            // a chute's later steps lie ahead of where it began: a fall or a lake may come before its end
            if (rules.Chutes) Array.Sort(pl.Drops, pl.Shape);
            pl.Len = s0;
            ends[r] = w;
        }

        /// <summary>The listed falls whose lip lies in this sub-step u of piece k - 1: each takes its whole drop at its lip
        /// (pool layout 1: as high as the river must come down here, to the bigger river or the lake just below, never below
        /// its floor). Whether a fall of this reach lies within FallHold ahead (pool layout 1: also with its lip right here,
        /// else the valley's own drop lands on the lip and adds to the fall).</summary>
        bool TakeFalls(int r, int k, int u, int steps, double f, double L, double s, double s0, List<FallSite> reachFalls,
            ref Water w, Worker wk)
        {
            bool aboveFall = false;
            foreach (var fl in reachFalls)
            {
                if ((fl.S > s || (rules.ValleyDrops && fl.S == s)) && fl.S - s <= FallHold) aboveFall = true;
                if (fl.Piece != k - 1 || fl.Along > f * L || (u > 1 && fl.Along <= (double)(u - 1) / steps * L)) continue;
                int drop = Math.Min(fl.Drop, w.P - fl.Floor);
                if (rules.ValleyDrops)
                {
                    double capHere = Math.Floor(bound[r][k] + (1 - f) * L / RampLength);
                    if (w.P - drop > capHere) drop = (int)Math.Min(w.P - capHere, w.P - fl.Floor);
                }
                fallDrops[fl.Index] = Math.Max(0, drop);
                if (drop <= 0) continue;
                for (int i = 0; i < drop; i++) { wk.Drops.Add((float)(s0 + fl.Along)); wk.Shapes.Add(ShapeCurtain); }
                w.P -= drop;
                w.Dsd = 0;
                w.Run = false;
                w.GorgeLeft = 0;
            }
            return aboveFall;
        }

        /// <summary>Pool layout 7: the next step of a gorge section, WhiteWaterSpacing after the last; never into a fall's hold
        /// (the section ends there).</summary>
        static void ContinueSection(bool aboveFall, double s, ref Water w, Worker wk)
        {
            if (aboveFall) w.GorgeLeft = 0;
            else if (w.Dsd >= WhiteWaterSpacing)
            {
                w.P--; w.Dsd = 0; w.GorgeLeft--;
                wk.Drops.Add((float)s); wk.Shapes.Add(ShapeChute);
                // (a chute's last step: a rapids run carries on while the valley keeps falling)
                if (w.GorgeLeft == 0) w.Run = w.RunAfter;
            }
        }

        /// <summary>Pool layout 0: one block per step, at once where what lies downstream needs it lower (a lake, the sea, the
        /// main river ahead), else once the valley is lower after MinPool blocks (RampLength where it is 2 or more lower); not
        /// just above a fall (the levels there come down early, from the gorge below in their 5' window).</summary>
        void StepLayout0(int valley, double cap, bool aboveFall, double s, ref Water w, Worker wk)
        {
            if (w.P > cap && w.Dsd >= 1)
            {
                w.P--; w.Dsd = 0; w.Run = false; wk.Drops.Add((float)s); wk.Shapes.Add(ShapeStep); // forced
            }
            else if (!aboveFall && w.P > valley && (w.Dsd >= MinPool || (w.P > valley + 1 && w.Dsd >= RampLength)))
            {
                w.P--; w.Dsd = 0; wk.Drops.Add((float)s); wk.Shapes.Add(ShapeStep);
            }
        }

        /// <summary>Pool layout 1 and later: one drop down to where the river must be: at once where a lake, the sea or the
        /// main river ahead needs it lower (forced), else down to where the valley asks for it (ValleyLevel); pool layout 7
        /// lays a drop of 2 blocks or more out as a white-water section (StartSection).</summary>
        void DropToValley(int r, int k, int u, double f, double s, double nextFall, double reachLength, int valley, double cap, bool aboveFall,
            ref Water w, Worker wk)
        {
            int to = w.P;
            // (pool layout 3: not just above a listed fall either; the fall takes what is still needed at its lip)
            if (w.P > cap && w.Dsd >= 1 && !(rules.EasedBeds && aboveFall)) to = (int)cap;
            bool inRun = rules.Sections && w.Run;
            bool forced = to < w.P;
            if (forced) w.GorgeLeft = 0;
            to = ValleyLevel(r, k, f, valley, aboveFall, inRun, in w, to);
            bool gorge = rules.Sections && !forced && StartSection(r, k, u, s, nextFall, inRun, ref w, ref to, wk);
            if (to < w.P) LayDrop(to, inRun, gorge, s, reachLength, net.LagOf(r), ref w, wk);
            // a run ends where the valley no longer falls a block within WhiteWaterSpacing of the last step
            else if (inRun && w.Dsd > WhiteWaterSpacing) w.Run = false;
        }

        /// <summary>How far down the valley asks the water to go here (to: where it is to go already). Pool layouts 1-8: to
        /// the valley floor after a pool of at least MinPool (WhiteWaterSpacing inside a rapids run). 9: a block at a time,
        /// StepGap after the last step. 10: also where the valley ahead asks for it, StepSpacing apart, at most StepLead below
        /// the valley here. From 9 a lag beyond ChuteMaxDrop goes down at once. Never just above a fall.</summary>
        int ValleyLevel(int r, int k, double f, int valley, bool aboveFall, bool inRun, in Water w, int to)
        {
            int P = w.P;
            double dsd = w.Dsd;
            if (rules.LeadsValley)
            {
                double ahead = lead[r][k - 1] + (lead[r][k] - lead[r][k - 1]) * f;
                int target = double.IsInfinity(ahead) ? valley : Math.Max(valley - StepLead, Math.Min(valley, (int)Math.Floor(ahead)));
                if (!aboveFall && P > target && dsd >= StepGap(Math.Max(1, P - valley))) to = Math.Min(to, P - 1);
                if (!aboveFall && P - valley > net.LagOf(r) && dsd >= ChuteSpacing) to = Math.Min(to, valley);
            }
            else if (rules.StepByStep)
            {
                if (!aboveFall && P > valley && dsd >= StepGap(P - valley)) to = Math.Min(to, P - 1);
                if (!aboveFall && P - valley > net.LagOf(r) && dsd >= ChuteSpacing) to = Math.Min(to, valley);
            }
            else if (!aboveFall && P > valley && dsd >= (inRun ? WhiteWaterSpacing : MinPool)) to = Math.Min(to, valley);
            return to;
        }

        /// <summary>
        /// Pool layout 7, a drop of 2 blocks or more that is not forced: a white-water section, its first step now and the
        /// rest WhiteWaterSpacing apart, on into the next reaches, if they all come before a fall's hold or a lake. A gorge
        /// section where the valley ahead keeps falling at least GorgeMinSteps blocks (the river takes up to GorgeMaxSteps of
        /// them early and runs calm after it until the valley has caught up); else a chute of 2–4 blocks laid out the same way
        /// (instead of squeezed before a short reach's end), with a rapids run after it. Whether it is one (to is then a block
        /// down).
        /// </summary>
        bool StartSection(int r, int k, int u, double s, double nextFall, bool inRun, ref Water w, ref int to, Worker wk)
        {
            int P = w.P;
            bool gorge = false;
            // (the valley has fallen 2 blocks or more since the last step: rapids, not a gentle river's step)
            if (!inRun && w.GorgeLeft == 0 && P - to >= 2 && P - to < GorgeMinSteps)
            {
                Ahead(r, k, u, s, nextFall, GorgeMinSteps * GorgeSpan * MinPool, GorgeMaxSteps * GorgeSpan * MinPool,
                    out int lowestNear, out int lowestFar, out double stop);
                int fit = StepsBefore(stop);
                int take = Math.Min(Math.Min(GorgeMaxSteps, P - lowestFar), fit);
                if (P - lowestNear >= GorgeMinSteps && take >= GorgeMinSteps)
                {
                    to = P - 1;
                    w.GorgeLeft = take - 1;
                    gorge = true;
                    w.RunAfter = false;
                    wk.GorgeSections++;
                    wk.GorgeSteps += take;
                }
            }
            if (!gorge && w.GorgeLeft == 0 && P - to >= 2 && P - to <= ChuteMaxDrop)
            {
                double reach = (P - to - 1) * WhiteWaterSpacing + 2;
                Ahead(r, k, u, s, nextFall, reach, reach, out _, out _, out double stop);
                if (StepsBefore(stop) >= P - to)
                {
                    w.GorgeLeft = P - to - 1;
                    to = P - 1;
                    gorge = true;
                    w.RunAfter = true;
                }
            }
            return gorge;
        }

        /// <summary>Takes the water down to level to here. A 1-block step (inside a rapids run or a section: a chute's step,
        /// rapid); from pool layout 2 a drop of 2–4 blocks a chute of 1-block steps ChuteGap apart (squeezed before the
        /// reach's end, a short curtain where there is no room; the next drop waits for its end), 5 or more a curtain.</summary>
        void LayDrop(int to, bool inRun, bool gorge, double s, double reachLength, int maxLag, ref Water w, Worker wk)
        {
            int h = w.P - to;
            double spacing = rules.Chutes && h >= 2 && h <= maxLag ? Math.Min(net.ChuteGap, (reachLength - s) / (h - 1)) : 0;
            byte shape = h == 1 ? (inRun || gorge ? ShapeChute : ShapeStep) : !rules.Chutes ? ShapeStep : spacing >= 1 ? ShapeChute : h > maxLag ? ShapeCurtain : ShapeShortCurtain;
            for (int i = 0; w.P > to; w.P--, i++)
            {
                wk.Drops.Add((float)(s + (shape == ShapeChute ? i * spacing : 0)));
                wk.Shapes.Add(shape);
            }
            w.Dsd = shape == ShapeChute ? -(h - 1) * spacing : 0;
            // (after a gorge section the river runs calm until the valley has caught up)
            w.Run = rules.Sections && shape == ShapeChute && !gorge;
        }

        /// <summary>A lake's or the sea's level t: the water comes down to exactly it (pool layout 2: a short curtain where that
        /// is 2 blocks or more).</summary>
        void DownToLevel(int t, float at, ref Water w, Worker wk)
        {
            byte shape = rules.Chutes && w.P - t >= 2 ? ShapeShortCurtain : ShapeStep;
            while (w.P > t)
            {
                w.P--; w.Dsd = 0; w.Run = false; w.GorgeLeft = 0; wk.Drops.Add(at); wk.Shapes.Add(shape);
            }
        }

        /// <summary>Pool layout 7: how many steps WhiteWaterSpacing apart fit before a stop that far ahead (none ahead:
        /// infinity).</summary>
        static int StepsBefore(double stop) => stop < 1 ? 0 : (int)Math.Min(GorgeMaxSteps + 1, Math.Floor((stop - 1) / WhiteWaterSpacing) + 1);

        /// <summary>Pool layout 7: the valley floor ahead along the river from piece k, sub-step u0 of reach r, through the
        /// reaches downstream: its lowest level within near and within far blocks, and the distance to the first place a
        /// gorge section must end before (a listed fall's hold, a lake's or the sea's vertex).</summary>
        void Ahead(int r, int k, int u0, double sHere, double nextFallHere, double near, double far, out int lowNear, out int lowFar, out double stop)
        {
            lowNear = lowFar = int.MaxValue;
            stop = nextFallHere - FallHold - sHere;
            if (fallsOn?[r] != null) foreach (var fl in fallsOn[r]) if (fl.S >= sHere) stop = Math.Min(stop, fl.S - FallHold - sHere);
            double dist = 0;
            int rr = r, kk = k, uStart = u0;
            for (int guard = 0; guard < 256 && dist <= far; guard++)
            {
                int nn = xs[rr].Length;
                for (; kk < nn; kk++, uStart = 1)
                {
                    double L = lens[rr][kk - 1];
                    int steps = Math.Max(1, (int)Math.Ceiling(L));
                    for (int u = uStart; u <= steps; u++)
                    {
                        if (rr != r || kk != k || u != u0) dist += L / steps;
                        if (dist > far || dist >= stop) return;
                        int v = waterTop(lev[rr][kk - 1] + (lev[rr][kk] - lev[rr][kk - 1]) * u / steps);
                        lowFar = Math.Min(lowFar, v);
                        if (dist <= near) lowNear = Math.Min(lowNear, v);
                    }
                    if (fix[rr][kk]) { stop = Math.Min(stop, dist); return; }
                }
                int nx = reaches[rr].Next;
                if (nx < 0 || nx >= nr || !keep[nx]) return;
                // (not on into a much bigger river: a tributary's section must not take the main valley's fall)
                if (reaches[nx].Q > 1.5 * reaches[rr].Q) return;
                rr = nx; kk = 1; uStart = 1;
            }
        }

        /// <summary>Which steps are rapids. Pool layout 2: every step but a lone 1-block one (chutes, curtains). 1: the steps
        /// of a drop of 2 blocks or more (they share one place). 0: steps closer together than three quarters of a normal
        /// pool.</summary>
        void MarkRapidSteps()
        {
            for (int r = 0; r < nr; r++)
            {
                var pl = pools[r];
                if (pl == null) continue;
                var d = pl.Drops;
                pl.Rapid = new bool[d.Length];
                for (int i = 0; i < d.Length; i++)
                {
                    if (rules.Chutes)
                    {
                        pl.Rapid[i] = pl.Shape[i] != ShapeStep;
                        continue;
                    }
                    if (rules.ValleyDrops)
                    {
                        pl.Rapid[i] = (i > 0 && d[i - 1] == d[i]) || (i + 1 < d.Length && d[i + 1] == d[i]);
                        continue;
                    }
                    double gapUp = i > 0 ? d[i] - d[i - 1] : pl.D0 + d[i];
                    double gapDown = i + 1 < d.Length ? d[i + 1] - d[i] : double.MaxValue;
                    pl.Rapid[i] = Math.Min(gapUp, gapDown) < MinPool * 0.75;
                }
            }
        }

        /// <summary>Pool layout 2: each curtain's height (its steps share one place).</summary>
        void CurtainHeights()
        {
            for (int r = 0; r < nr; r++)
            {
                var pl = pools[r];
                if (pl == null) continue;
                pl.Height = new byte[pl.Drops.Length];
                for (int i = 0; i < pl.Drops.Length;)
                {
                    int j = i;
                    while (j < pl.Drops.Length && pl.Drops[j] == pl.Drops[i]) j++;
                    for (int q = i; q < j; q++) pl.Height[q] = (byte)Math.Min(j - i, 255);
                    i = j;
                }
            }
        }

        /// <summary>Pool layout 1: a drop at the end of a reach (into a confluence, at a lake's edge): the water below it, in
        /// the next reach, is still its rapids (reaches are in topological order, sources first).</summary>
        void RapidsIntoNextReach()
        {
            for (int r = 0; r < nr; r++)
            {
                var pl = pools[r];
                if (pl == null || pl.MainIn < 0 || pools[pl.MainIn] == null) continue;
                var up = pools[pl.MainIn];
                pl.RapidIn = up.Drops.Length > 0 ? up.Rapid[up.Drops.Length - 1] : up.RapidIn;
            }
        }

        /// <summary>Pool layout 8: rapids reaches. The real valley's fall (m/km) and the water's fall (blocks per block),
        /// averaged over RapidsWindow each way along the main river from the middle of the reach: constant along a reach (a
        /// window sliding along it put rapids into the middle of calm pools in the rule study's simulator).</summary>
        void FindRapidsReaches()
        {
            reachRapids = new bool[nr];
            reachSlope = new float[nr];
            reachValley = new float[nr];
            reachQ = new float[nr];
            double mpb = net.MetresPerBlock > 0 ? net.MetresPerBlock : geo != null && geo.BlocksPerDegreeZ > 0 ? Geo.MetresPerDegree / geo.BlocksPerDegreeZ : 0;
            double window = RapidsWindow * net.SizeScale;
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r] || pools[r] == null || lev[r] == null || lev[r].Length < 2) continue;
                double up = reachLen[r] / 2, down = reachLen[r] / 2;
                double levUp = lev[r][0], levDown = lev[r][lev[r].Length - 1];
                int waterUp = pools[r].P0, waterDown = ends[r].P;
                for (int rr = r, guard = 0; up < window && guard < 256; guard++)
                {
                    int m = pools[rr].MainIn;
                    if (m < 0 || pools[m] == null || lev[m] == null || lev[m].Length < 2) break;
                    rr = m; up += reachLen[rr]; levUp = lev[rr][0]; waterUp = pools[rr].P0;
                }
                for (int rr = r, guard = 0; down < window && guard < 256; guard++)
                {
                    int m = pools[rr].Next;
                    if (m < 0 || pools[m] == null || lev[m] == null || lev[m].Length < 2) break;
                    rr = m; down += reachLen[rr]; levDown = lev[rr][lev[rr].Length - 1]; waterDown = ends[rr].P;
                }
                double dist = up + down;
                if (dist <= 0) continue;
                reachSlope[r] = (float)Math.Max(0, (waterUp - waterDown) / dist);
                reachQ[r] = reaches[r].Q;
                reachValley[r] = mpb > 0 ? (float)((levUp - levDown) / (dist * mpb / 1000)) : 0;
                reachRapids[r] = mpb > 0 && (rules.RapidsByPower ? net.RapidsReaches && RiverNetwork.RapidsByPower(reachValley[r], reaches[r].Q) : reachValley[r] >= RapidsSlope);
            }
        }

        /// <summary>The kept reaches grouped by river system (joined through Next), each in ascending order, biggest first.</summary>
        static List<int[]> Basins(List<RiverReach> reaches, bool[] keep)
        {
            int nr = reaches.Count;
            var parent = new int[nr];
            for (int r = 0; r < nr; r++) parent[r] = r;
            int Root(int r)
            {
                while (parent[r] != r) r = parent[r] = parent[parent[r]];
                return r;
            }
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int nx = reaches[r].Next;
                if (nx < 0 || nx >= nr || !keep[nx]) continue;
                int a = Root(r), b = Root(nx);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
            var members = new Dictionary<int, List<int>>();
            for (int r = 0; r < nr; r++)
            {
                if (!keep[r]) continue;
                int root = Root(r);
                if (!members.TryGetValue(root, out var l)) members[root] = l = new List<int>();
                l.Add(r);
            }
            var basins = members.Values.Select(l => l.ToArray()).ToList();
            basins.Sort((a, b) => b.Length.CompareTo(a.Length));
            return basins;
        }
    }
}
