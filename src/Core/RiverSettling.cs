using System;
using System.Collections.Generic;
using static EarthMap.Core.TerrainModel;

namespace EarthMap.Core
{
    /// <summary>Which of the settling's rules a world's river pool layout has (RiverNetwork.PoolLayout; RiverSettling.Settle).</summary>
    public readonly struct SettleRules
    {
        /// <summary>Pool layout 1 (decision 0024): dry land next to a waterfall's curtain is raised to the curtain's top.</summary>
        public readonly bool CurtainBanks;
        /// <summary>Pool layout 4: banks next to any lake water too.</summary>
        public readonly bool LakeBanks;
        /// <summary>Pool layout 5: ramps from the settling alone, a sill under each drop, the upper edges marked.</summary>
        public readonly bool Sills;
        /// <summary>Pool layout 6: which 1-block steps are rapid comes from Hydrology's rapids window (EdgeRapid).</summary>
        public readonly bool RapidRule;
        /// <summary>Pool layout 7 and later: the window alone, rapids spreading from any feeding cell, spills over curtains.</summary>
        public readonly bool WhiteWater;
        /// <summary>Pool layout 8: no sills (Hydrology knows natural river water from Earth's query).</summary>
        public readonly bool NoSills;
        /// <summary>Pool layout 10: no spills; a ramp falls evenly to a step within its length.</summary>
        public readonly bool NoSpills;

        public SettleRules(bool curtainBanks, bool lakeBanks, bool sills, bool rapidRule, bool whiteWater, bool noSills, bool noSpills)
        {
            CurtainBanks = curtainBanks; LakeBanks = lakeBanks; Sills = sills; RapidRule = rapidRule;
            WhiteWater = whiteWater; NoSills = noSills; NoSpills = noSpills;
        }

        /// <summary>The rules of a pool layout (0 without rivers: none of them).</summary>
        public static SettleRules ForPoolLayout(int poolLayout) => new(poolLayout >= 1, poolLayout >= 4, poolLayout >= 5, poolLayout >= 6,
            poolLayout >= 7, poolLayout >= 8, poolLayout >= 10);
    }

    /// <summary>
    /// Settling the water of an area of columns after the terrain model made them (ramps, banks, sills, rapids): a pure
    /// function of the columns, no model state. Per-thread scratch space, so it allocates nothing once warm.
    /// </summary>
    public static class RiverSettling
    {
        /// <summary>
        /// Settles the water of an area of columns (row-major, w x h) the way VS's own liquid spreading would: where a
        /// column's fresh water is a block lower than a neighbour's, flowing water spreads over it from there (height 6,
        /// one less per block), and dry land right next to river water is raised to hold it in. This fills in the few
        /// steps the river layout leaves without a ramp (inner edges of tight bends, meander loops that touch, tributary
        /// mouths, rivers meeting lakes). Columns near the edges of the area may miss sources outside it, so callers pass
        /// a margin of at least 8 columns around the ones they use. With curtainBanks (worlds with pool layout 1, decision
        /// 0024) dry land next to a waterfall's curtain is raised to the curtain's top, the level of the water above it.
        /// </summary>
        public static void Settle(Column[] cols, int w, int h, in SettleRules rules)
        {
            bool curtainBanks = rules.CurtainBanks, lakeBanks = rules.LakeBanks, sills = rules.Sills, rapidRule = rules.RapidRule,
                whiteWater = rules.WhiteWater, noSills = rules.NoSills, noSpills = rules.NoSpills;
            // per-thread scratch (called for every chunk near a river or lake step), cleared to the zeros fresh arrays had
            int n0 = w * h;
            if (settleScratch == null || settleScratch.Length < 3 * n0) settleScratch = new int[3 * n0];
            Array.Clear(settleScratch, 0, 3 * n0);
            var scratch = moreScratch ??= new Scratch();
            var level = new Span<int>(settleScratch, 0, n0);
            var from = new Span<int>(settleScratch, n0, n0);
            var fall = new Span<int>(settleScratch, 2 * n0, n0);
            Span<int> nb = stackalloc int[4];
            int Neighbours(int k, Span<int> o)
            {
                int x = k % w, z = k / w, n = 0;
                if (x > 0) o[n++] = k - 1;
                if (x < w - 1) o[n++] = k + 1;
                if (z > 0) o[n++] = k - w;
                if (z < h - 1) o[n++] = k + w;
                return n;
            }
            // pool layout 5: ramps come only from the settling below, one column per height from the step (6, 5, 4, ...).
            // The river model's own ramp heights follow the distance along the river, so a height repeated over 2-3 columns
            // across a diagonal or wide river, and vanilla's first update rebuilt such a ramp (rule study, Earth 0.16.8 test)
            if (sills)
                for (int k = 0; k < cols.Length; k++)
                    if (cols[k].Water == WaterLake) cols[k].Ramp = 0;
            for (int k = 0; k < cols.Length; k++)
            {
                ref var c = ref cols[k];
                if (c.Water != WaterLake || c.WaterTop < 0) continue;
                int n = Neighbours(k, nb);
                for (int i = 0; i < n; i++)
                {
                    ref var d = ref cols[nb[i]];
                    if (d.Water == WaterNone || d.WaterTop <= c.WaterTop) continue;
                    // 2 or more blocks higher: a waterfall (a lake above a fall, rivers touching at different levels),
                    // also over a cliff where the upper water's bed lies above this pool
                    if (d.WaterTop - c.WaterTop >= 2)
                    {
                        fall[k] = Math.Max(fall[k], d.WaterTop - c.WaterTop);
                        level[k] = 6;
                        from[k] = nb[i];
                        continue;
                    }
                    // water in the neighbour right above this column's water surface
                    if (d.Top > c.WaterTop) continue;
                    level[k] = 6;
                    from[k] = nb[i];
                }
            }
            for (int l = 6; l > 1; l--)
            {
                for (int k = 0; k < cols.Length; k++)
                {
                    if (level[k] != l) continue;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        int m = nb[i];
                        if (cols[m].Water != WaterLake || cols[m].WaterTop != cols[k].WaterTop || level[m] >= l - 1) continue;
                        level[m] = l - 1;
                        from[m] = k;
                    }
                }
            }
            // pool layout 10 (with noSpills): where the next step down comes within a ramp's length (two steps' lines
            // converging, mostly on the inside of a wide river's bend), the ramp falls evenly to it instead of meeting it at
            // height 3-6: the surface goes from the upper edge (7/8 above the upper water) to the lower edge (7/8 above the
            // lower water) in equal parts, and the lower edge carries no ramp. Before, the ramp was cut off there, a face of
            // up to 7/8 of a block, with a spill of falling water standing on it.
            if (noSpills)
            {
                var down = Grow(ref scratch.Down, n0);
                Array.Fill(down, int.MaxValue, 0, n0);
                var q = scratch.Queue;
                q.Clear();
                for (int k = 0; k < n0; k++)
                {
                    if (level[k] == 0 || fall[k] > 0) continue;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        ref var d = ref cols[nb[i]];
                        if (d.Water == WaterNone || d.WaterTop >= cols[k].WaterTop) continue;
                        down[k] = 0;
                        q.Enqueue(k);
                        break;
                    }
                }
                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        int m = nb[i];
                        if (level[m] == 0 || fall[m] > 0 || cols[m].WaterTop != cols[k].WaterTop || down[m] <= down[k] + 1) continue;
                        down[m] = down[k] + 1;
                        q.Enqueue(m);
                    }
                }
                for (int k = 0; k < n0; k++)
                {
                    if (level[k] == 0 || down[k] == int.MaxValue) continue;
                    int up = 7 - level[k], gap = up + down[k];
                    if (gap >= 7) continue;
                    level[k] = Math.Clamp((int)Math.Round(7 - 8.0 * up / gap), 0, level[k]);
                }
            }
            for (int k = 0; k < cols.Length; k++)
            {
                if (fall[k] > 0) cols[k].Fall = (byte)Math.Min(fall[k], 250);
                if (level[k] <= cols[k].Ramp) continue;
                ref var c = ref cols[k];
                c.Ramp = (byte)level[k];
                if (!c.River || (c.FlowX == 0 && c.FlowZ == 0))
                {
                    int f = from[k];
                    c.FlowX = k % w - f % w;
                    c.FlowZ = k / w - f / w;
                }
            }
            // pool layout 5 (decision 0024 step 3, the rule study's shape that stays as generated under Hydrology's rules):
            // - a sill under each drop: the upper water's edge column has its bed at least at the lower pool's surface (at a
            //   1-block step it holds water only in its top block; else its lower layers touch the lower pool, and Hydrology's
            //   communicating vessels fill the ramp, or a curtain's foot, up to the upper water);
            // - the upper water's edges marked for their sources: labelled with the flow at a step, rapid at a chute's step
            //   (its ramp is rapid), rapid down to the curtain's foot at a curtain's lip;
            // - the spill: where a chute's ramp (3 or more high) ends beside the next step down, falling rapid water one
            //   lower above that step's first ramp cell.
            if (sills)
            {
                for (int k = 0; k < cols.Length; k++)
                {
                    ref var c = ref cols[k];
                    // fresh water, or salt lakes beside each other (the vessels fill either, never across liquids)
                    if ((c.Water != WaterLake && c.Water != WaterSaltLake) || c.WaterTop < 0) continue;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        int m = nb[i];
                        ref var d = ref cols[m];
                        if (d.Water != c.Water || d.WaterTop <= c.WaterTop) continue;
                        int up = d.WaterTop - c.WaterTop;
                        // (pool layout 8: no sills - Hydrology knows natural river water from Earth's query, decision 0027)
                        if (d.Top < c.WaterTop && !noSills) d.Top = c.WaterTop;
                        if (up == 1)
                        {
                            if (level[k] == 6 && d.Edge != EdgeLip) d.Edge = Math.Max(d.Edge, c.Rapid ? EdgeChute : EdgeStep);
                            if (!rapidRule && c.Rapid && c.Ramp > 0 && d.Ramp >= 3) c.Spill = (byte)Math.Max(c.Spill, d.Ramp - 1);
                        }
                        else if (fall[k] > 0)
                        {
                            d.Edge = EdgeLip;
                            d.EdgeDepth = (byte)Math.Max(d.EdgeDepth, Math.Min(up, 250));
                        }
                        else continue;
                        // a lake's edge has no flow of its own: toward the drop
                        if (!d.River && d.FlowX == 0 && d.FlowZ == 0)
                        {
                            d.FlowX = k % w - m % w;
                            d.FlowZ = k / w - m / w;
                        }
                    }
                }
            }
            // banks: dry land next to river water is at least as high as its surface (one more beside a ramp; with
            // curtainBanks as high as the top of a waterfall's curtain beside it; with lakeBanks, pool layout 4, next to any
            // lake water too)
            for (int k = 0; k < cols.Length; k++)
            {
                ref var c = ref cols[k];
                if (c.Water != WaterNone) continue;
                int need = int.MinValue;
                int n = Neighbours(k, nb);
                for (int i = 0; i < n; i++)
                {
                    ref var d = ref cols[nb[i]];
                    if (d.Water == WaterNone || (!d.River && d.Ramp == 0 && !(lakeBanks && d.Water != WaterSea))) continue;
                    need = Math.Max(need, d.WaterTop + Math.Max(d.Ramp > 0 ? 1 : 0, curtainBanks ? d.Fall : 0));
                }
                if (need > c.Top) c.Top = need;
            }
            // pool layout 6 (rule study 2026-10-24, Hydrology tools/watersim/src/RapidRule.cs): which 1-block steps are rapid
            // comes from Hydrology's rapids window (H4) evaluated on these columns, step by step, not from the river model's
            // whole reach (which marked a chute's first step rapid where H4 turns it ordinary on the first touch):
            // - an upper edge is rapid when the river model has rapids there (the ramp below it is rapid) and the water falls 2
            //   or more within H4's window (EdgeRapid). The window alone also marked long single steps rapid (the Congo at
            //   256k), where H4 keeps either but rows with a water plant on the edge went back to ordinary water: the rule
            //   only takes rapids away;
            // - the ramp below a step is rapid when an edge of that step is (it follows the BFS back to its seed);
            // - a spill sits above the next step's ramp where a rapid ramp cell beside it is 2 or more high (was 3);
            // - the ramp below a curtain is rapid (a drop of 2 or more is always within the window).
            // After the banks: the window reads their final heights.
            if (sills && rapidRule)
            {
                var rapidOf = Grow(ref scratch.RapidOf, n0);
                var stamp = Grow(ref scratch.Stamp, n0);
                var queue = scratch.Edges;
                Array.Clear(rapidOf, 0, n0);
                Array.Clear(stamp, 0, n0);
                queue.Clear();
                // per upper edge: 1 an edge of some step, 2 rapid for at least one of its lower pools (whatever the order)
                var edgeOf = Grow(ref scratch.EdgeOf, n0);
                Array.Clear(edgeOf, 0, n0);
                int pass = 0;
                for (int k = 0; k < cols.Length; k++)
                {
                    ref var c = ref cols[k];
                    if (c.Water != WaterLake || c.WaterTop < 0 || level[k] != 6) continue;
                    if (fall[k] > 0) rapidOf[k] = true;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        int m = nb[i];
                        ref var d = ref cols[m];
                        if (d.Water != WaterLake || d.WaterTop != c.WaterTop + 1) continue;
                        // (pool layout 7: the window alone, as the rule study has it; Hydrology 0.1.6 no longer reads water
                        // plants under a ramp as ground, which made layout 6 keep the river model's rapids as a gate)
                        // (pool layout 8: an edge in a rapids reach is rapid whatever the window says)
                        bool rapid = d.RapidsReach || (c.Rapid || whiteWater) && EdgeRapid(cols, w, h, m, stamp, ref pass, queue);
                        if (rapid) rapidOf[k] = true;
                        edgeOf[m] = Math.Max(edgeOf[m], rapid ? (byte)2 : (byte)1);
                    }
                }
                for (int m = 0; m < cols.Length; m++)
                    if (edgeOf[m] > 0 && cols[m].Edge != EdgeLip) cols[m].Edge = edgeOf[m] == 2 ? EdgeChute : EdgeStep;
                // the ramp cells, highest first, take their seed's answer
                for (int l = 5; l >= 1; l--)
                    for (int k = 0; k < cols.Length; k++)
                    {
                        if (level[k] != l || from[k] < 0 || from[k] >= cols.Length) continue;
                        rapidOf[k] = rapidOf[from[k]];
                        // (pool layout 7: rapid when any cell one higher that feeds it is, as Hydrology's rapids spread; not only
                        // the one the settling came from)
                        if (whiteWater && !rapidOf[k])
                        {
                            int n2 = Neighbours(k, nb);
                            for (int i = 0; i < n2; i++)
                                if (level[nb[i]] == l + 1 && cols[nb[i]].WaterTop == cols[k].WaterTop && rapidOf[nb[i]]) { rapidOf[k] = true; break; }
                        }
                    }
                for (int k = 0; k < cols.Length; k++)
                {
                    ref var c = ref cols[k];
                    if (c.Water == WaterLake && c.Ramp > 0 && fall[k] == 0) c.Rapid = rapidOf[k];
                }
                // (pool layout 10: no spills. Where two steps' lines come within a ramp's length of each other, mostly on the
                // inside of a wide river's bend, the spill stood as a wall of falling water a block above the ramp: the
                // play-test's "vertical water face" at the Congo. Now the upper ramp just ends at the lower step's edge)
                for (int k = 0; k < cols.Length && !noSpills; k++)
                {
                    ref var c = ref cols[k];
                    // (pool layout 7: also above a curtain, where two steps meet on neighbouring rows of a diagonal river and
                    // the upper step's ramp spills over the curtain's top)
                    if (c.Water != WaterLake || c.Ramp == 0 || (fall[k] > 0 && !whiteWater)) continue;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        ref var d = ref cols[nb[i]];
                        if (d.Water == WaterLake && d.WaterTop == c.WaterTop + Math.Max(1, fall[k]) && d.Rapid && d.Ramp >= 2 && fall[nb[i]] == 0)
                            c.Spill = (byte)Math.Max(c.Spill, d.Ramp - 1);
                    }
                }
            }
        }

        /// <summary>
        /// Pool layout 6: Hydrology's rapids window (H4: the water falls 2 or more within 6 column steps) at the upper edge k
        /// of a 1-block step, on settled columns (a port of the rule study's RapidRule.EdgeRapid). Downstream, a breadth-first
        /// search over 4-neighbours up to 6 steps records where water from the edge lands at each distance (a water top, or
        /// one above a dry bed, where it stops); upstream, up to 5 steps through water that reaches the current level, the
        /// highest surface (a ramp counts one higher). Rapid when an upstream surface within i steps is 2 or more above a
        /// landing within j steps, i + j &lt;= 6, j &gt;= 1. stamp/pass/queue are scratch space shared by the calls.
        /// </summary>
        static bool EdgeRapid(Column[] cols, int w, int h, int k, int[] stamp, ref int pass, List<int> queue, int reach = 6, int drop = 2)
        {
            int y0 = cols[k].WaterTop;
            bool Water(int m) => cols[m].Water != WaterNone;
            Span<int> nbs = stackalloc int[4];
            int Nb(int m, Span<int> o)
            {
                int x = m % w, z = m / w, n = 0;
                if (z > 0) o[n++] = m - w;
                if (x < w - 1) o[n++] = m + 1;
                if (z < h - 1) o[n++] = m + w;
                if (x > 0) o[n++] = m - 1;
                return n;
            }
            Span<int> down = stackalloc int[reach + 1];
            down.Fill(int.MaxValue);
            int mark = ++pass;
            stamp[k] = mark;
            // queue entries: column, level, wet (0/1), distance, packed 4 ints per entry
            queue.Clear();
            int cnt = Nb(k, nbs);
            for (int i = 0; i < cnt; i++)
            {
                int n = nbs[i];
                if (cols[n].Top >= y0) continue;                          // a bank: no open side
                if (Water(n) && cols[n].WaterTop >= y0) continue;         // a source beside it
                stamp[n] = mark;
                if (Water(n)) { queue.Add(n); queue.Add(cols[n].WaterTop); queue.Add(1); queue.Add(1); }
                else { queue.Add(n); queue.Add(cols[n].Top + 1); queue.Add(cols[n].Top + 1 == y0 ? 1 : 0); queue.Add(1); }
            }
            if (queue.Count == 0) return false;
            for (int head = 0; head < queue.Count; head += 4)
            {
                int m = queue[head], lvl = queue[head + 1], dist = queue[head + 3];
                bool wet = queue[head + 2] != 0;
                down[dist] = Math.Min(down[dist], lvl);
                if (dist == reach || !wet) continue;
                cnt = Nb(m, nbs);
                for (int i = 0; i < cnt; i++)
                {
                    int n = nbs[i];
                    if (stamp[n] == mark || cols[n].Top >= lvl) continue;
                    stamp[n] = mark;
                    queue.Add(n);
                    if (Water(n) && cols[n].WaterTop >= lvl) { queue.Add(lvl); queue.Add(1); }
                    else if (Water(n)) { queue.Add(cols[n].WaterTop); queue.Add(1); }
                    else { queue.Add(cols[n].Top + 1); queue.Add(cols[n].Top + 1 == lvl ? 1 : 0); }
                    queue.Add(dist + 1);
                }
            }
            for (int d = 2; d <= reach; d++) down[d] = Math.Min(down[d], down[d - 1]);
            if (y0 - down[1] >= drop) return true;

            Span<int> up = stackalloc int[reach];
            up.Fill(y0);
            mark = ++pass;
            stamp[k] = mark;
            queue.Clear();
            queue.Add(k); queue.Add(y0); queue.Add(0);
            for (int head = 0; head < queue.Count; head += 3)
            {
                int m = queue[head], lvl = queue[head + 1], dist = queue[head + 2];
                up[dist] = Math.Max(up[dist], lvl);
                if (dist == reach - 1) continue;
                cnt = Nb(m, nbs);
                for (int i = 0; i < cnt; i++)
                {
                    int n = nbs[i];
                    if (stamp[n] == mark || !Water(n)) continue;
                    int bottom = cols[n].Top + 1, surface = cols[n].WaterTop + (cols[n].Ramp > 0 ? 1 : 0);
                    if (surface < lvl || bottom > lvl + drop) continue;
                    stamp[n] = mark;
                    queue.Add(n); queue.Add(surface); queue.Add(dist + 1);
                }
            }
            for (int d = 1; d < reach; d++) up[d] = Math.Max(up[d], up[d - 1]);
            for (int i = 0; i < reach; i++)
                for (int j = 1; i + j <= reach; j++)
                    if (up[i] - down[j] >= drop) return true;
            return false;
        }

        // per-thread scratch: level, from and fall (3 n each), and the arrays and queues of the no-spills ramps and the rapid
        // rule; each use clears its part to what a fresh one held, so the results don't depend on what ran before
        [ThreadStatic] static int[] settleScratch;
        [ThreadStatic] static Scratch moreScratch;

        sealed class Scratch
        {
            public int[] Down, Stamp;
            public bool[] RapidOf;
            public byte[] EdgeOf;
            public readonly Queue<int> Queue = new();
            public readonly List<int> Edges = new();
        }

        /// <summary>The array, grown to at least n (not cleared).</summary>
        static T[] Grow<T>(ref T[] a, int n) => a != null && a.Length >= n ? a : a = new T[n];
    }
}
