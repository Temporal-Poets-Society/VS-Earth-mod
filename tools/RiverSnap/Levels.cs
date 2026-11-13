namespace RiverSnap
{
    /// <summary>
    /// Water levels on the snapped stations, by bake_rivers.py's rules with GLO-90's floor in place of ETOPO's:
    ///  1. the floor at each station (Worker: lowest GLO-90 near the course, opened along it); lake and sea stations keep
    ///     the original level;
    ///  2. dams: each HydroLAKES reservoir stretch of the main stem becomes a straight slope from its tail to the river
    ///     below the dam (bake_rivers' algorithm, its step limits scaled to the denser stations);
    ///  3. a monotone fit per chain, main rivers before their tributaries: the least-absolute-deviation fit that never rises
    ///     downstream (L1Fit), between the lake or sea levels above and below each stretch and not below the main river's
    ///     level at the junction; a bump is cut through where that moves fewer metres than filling up to it, a pit filled
    ///     where that does. (fit=avg: the first version, CarveWeight × the floor carved down plus the rest × the floor
    ///     filled up, which let one ridge the course crossed raise a river for 100 km.)
    ///  4. bake_rivers' order: raised to the lake or sea level downstream, made non-increasing downstream, smoothed
    ///     (SmoothIters rounds of the mean with the downstream and the mean upstream neighbour, lake and sea stations fixed),
    ///     raised and made non-increasing again. A tributary's last station flows into its main river's junction station,
    ///     so it never ends below it.
    /// </summary>
    static class Levels
    {
        public sealed class Report
        {
            public int Dedammed, Reservoirs, Conflicts, NoFloor;
            public long DedammedStations;
        }

        public static Report Run(Stations G, List<(double Lon, double Lat, double Elev)> reservoirs, double carveWeight, int smoothIters, double ds,
            List<Chain> chains = null)
        {
            var rep = new Report { Reservoirs = reservoirs?.Count ?? 0 };
            int N = G.Count;
            var lev = G.Level;
            var fixd = G.Fixed;
            var next = G.Next;
            for (int v = 0; v < N; v++)
            {
                if (fixd[v]) lev[v] = G.OrigLev[v];
                else if (float.IsNaN(G.Floor[v])) { lev[v] = G.OrigLev[v]; rep.NoFloor++; }
                else lev[v] = G.Floor[v];
            }
            // inflows (CSR) and the main inflow of each station (most water; the first of equals)
            var inCount = new int[N + 1];
            for (int v = 0; v < N; v++) if (next[v] >= 0) inCount[next[v] + 1]++;
            for (int v = 0; v < N; v++) inCount[v + 1] += inCount[v];
            var inList = new int[inCount[N]];
            var fillAt = (int[])inCount.Clone();
            var prevMain = new int[N];
            Array.Fill(prevMain, -1);
            for (int v = 0; v < N; v++)
            {
                int w = next[v];
                if (w < 0) continue;
                inList[fillAt[w]++] = v;
                if (prevMain[w] < 0 || G.Q[v] > G.Q[prevMain[w]]) prevMain[w] = v;
            }

            if (reservoirs != null) Dedam(G, reservoirs, prevMain, ds, rep);

            if (chains != null) FitChains(G, chains);
            else AverageFit(G, carveWeight);

            RaiseToDownstream(G);
            Monotone(G);
            // spread the remaining drops out (lake and sea stations fixed)
            var nw = new double[N];
            for (int it = 0; it < smoothIters; it++)
            {
                Parallel.For(0, N, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, v =>
                {
                    if (fixd[v]) { nw[v] = lev[v]; return; }
                    int a = inCount[v], b = inCount[v + 1];
                    double up = lev[v];
                    if (b > a)
                    {
                        double s = 0;
                        for (int k = a; k < b; k++) s += lev[inList[k]];
                        up = s / (b - a);
                    }
                    double down = next[v] >= 0 ? lev[next[v]] : lev[v];
                    nw[v] = (lev[v] + up + down) / 3;
                });
                Array.Copy(nw, lev, N);
            }
            rep.Conflicts = RaiseToDownstream(G);
            Monotone(G);
            return rep;
        }

        /// <summary>The first fit (fit=avg): CarveWeight × the floor carved down plus the rest × the floor filled up.</summary>
        static void AverageFit(Stations G, double carveWeight)
        {
            int N = G.Count;
            var lev = G.Level;
            var fixd = G.Fixed;
            var next = G.Next;
            var carve = (double[])lev.Clone();
            for (int v = 0; v < N; v++)
            {
                int w = next[v];
                if (w >= 0 && !fixd[w] && carve[w] > carve[v]) carve[w] = carve[v];
            }
            var fill = (double[])lev.Clone();
            for (int v = N - 1; v >= 0; v--)
            {
                int w = next[v];
                if (w >= 0 && !fixd[v] && fill[v] < fill[w]) fill[v] = fill[w];
            }
            for (int v = 0; v < N; v++) if (!fixd[v]) lev[v] = carveWeight * carve[v] + (1 - carveWeight) * fill[v];
        }

        /// <summary>
        /// The L1 monotone fit per chain, in the order of the chains' depth (main rivers first): each stretch between lake or
        /// sea stations fitted on its own, then held between the next lake or sea level downstream (or the main river's level
        /// at the junction) and the last one upstream; where those two disagree (17 rises at lakes in rivers.bin) the lower
        /// bound wins, as bake_rivers' raising did.
        /// </summary>
        static void FitChains(Stations G, List<Chain> chains)
        {
            var lev = G.Level;
            foreach (var level in chains.Where(c => c.Processed).GroupBy(c => c.Depth).OrderBy(g => g.Key))
            {
                var list = level.ToList();
                Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, list.Count, 64),
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    () => (Heap: new PriorityQueue<double, double>(), Top: new double[1024], Vals: new double[1024]),
                    (range, _, local) =>
                    {
                        for (int ci = range.Item1; ci < range.Item2; ci++)
                        {
                            var c = list[ci];
                            int a = c.StationStart, last = a + c.StationCount - 1;
                            // the main river's level at the junction (its chain is fitted: a lower depth)
                            double below = G.Next[last] >= 0 ? lev[G.Next[last]] : double.NegativeInfinity;
                            // stretches between lake and sea stations, from the mouth up
                            int end = last;
                            while (end >= a)
                            {
                                if (G.Fixed[end]) { below = lev[end]; end--; continue; }
                                int start = end;
                                while (start - 1 >= a && !G.Fixed[start - 1]) start--;
                                double upper = start - 1 >= a ? lev[start - 1] : double.PositiveInfinity;
                                int cnt = end - start + 1;
                                if (local.Vals.Length < cnt) local.Vals = new double[2 * cnt];
                                if (local.Top.Length < cnt) local.Top = new double[2 * cnt];
                                var vals = local.Vals;
                                for (int i = 0; i < cnt; i++) vals[i] = lev[start + i];
                                L1Fit.Fit(vals, 0, cnt, vals, local.Heap, local.Top);
                                for (int i = 0; i < cnt; i++)
                                {
                                    double x = vals[i];
                                    if (x > upper) x = upper;
                                    if (x < below) x = below;
                                    lev[start + i] = x;
                                }
                                below = lev[start];
                                end = start - 1;
                            }
                        }
                        return local;
                    }, _ => { });
            }
        }

        /// <summary>Every station at least the level of the lake or sea downstream of it; returns the lake and sea stations
        /// lower than one downstream (data mismatches).</summary>
        static int RaiseToDownstream(Stations G)
        {
            int N = G.Count, conflicts = 0;
            var low = new double[N];
            for (int v = N - 1; v >= 0; v--)
            {
                int w = G.Next[v];
                double b = w >= 0 ? low[w] : -1e9;
                if (G.Fixed[v])
                {
                    if (G.Level[v] < b - 0.5) conflicts++;
                    b = G.Level[v];
                }
                else if (G.Level[v] < b) G.Level[v] = b;
                low[v] = b;
            }
            return conflicts;
        }

        /// <summary>Never rising downstream; lake and sea stations keep their level.</summary>
        static void Monotone(Stations G)
        {
            for (int v = 0; v < G.Count; v++)
            {
                int w = G.Next[v];
                if (w >= 0 && !G.Fixed[w] && G.Level[w] > G.Level[v]) G.Level[w] = G.Level[v];
            }
        }

        /// <summary>bake_rivers.py's de-damming on the stations: below the dam the first station clearly lower than the
        /// reservoir, up the main stem while at the reservoir's level, a straight slope between (never through a lake).</summary>
        static void Dedam(Stations G, List<(double Lon, double Lat, double Elev)> res, int[] prevMain, double ds, Report rep)
        {
            int N = G.Count;
            const double R = 0.03;
            // bake_rivers walked 60 vertices down and 3000 up at ~0.9 km per vertex; the same distances in stations
            int maxDown = (int)Math.Ceiling(60 * 900 / ds), maxUp = (int)Math.Ceiling(3000 * 900 / ds);
            var keys = new long[N];
            var idx = new int[N];
            static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;
            for (int v = 0; v < N; v++)
            {
                double x = G.Lon[v] * Math.Cos(G.Lat[v] * Math.PI / 180), y = G.Lat[v];
                keys[v] = Key((int)Math.Floor(x / R), (int)Math.Floor(y / R));
                idx[v] = v;
            }
            Array.Sort(keys, idx);
            var touched = new bool[N];
            var lev = G.Level;
            var cands = new List<int>();
            var path = new List<int>();
            foreach (var (lon, lat, elev) in res)
            {
                double px = lon * Math.Cos(lat * Math.PI / 180), py = lat;
                int cx = (int)Math.Floor(px / R), cy = (int)Math.Floor(py / R);
                cands.Clear();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long key = Key(cx + dx, cy + dy);
                        int at = Array.BinarySearch(keys, key);
                        if (at < 0) continue;
                        while (at > 0 && keys[at - 1] == key) at--;
                        for (; at < N && keys[at] == key; at++)
                        {
                            int v = idx[at];
                            double x = G.Lon[v] * Math.Cos(G.Lat[v] * Math.PI / 180), y = G.Lat[v];
                            if ((x - px) * (x - px) + (y - py) * (y - py) > R * R) continue;
                            if (lev[v] <= elev + 5 && !touched[v]) cands.Add(v);
                        }
                    }
                }
                if (cands.Count == 0) continue;
                int best = -1;
                double bd = double.MaxValue;
                foreach (int c in cands)
                {
                    double dd = (G.Lon[c] - lon) * (G.Lon[c] - lon) + (G.Lat[c] - lat) * (G.Lat[c] - lat);
                    if (dd < bd || (dd == bd && c < best)) { bd = dd; best = c; }
                }
                int w = best, steps = 0;
                while (w >= 0 && lev[w] > elev - 3 && steps < maxDown) { w = G.Next[w]; steps++; }
                if (w < 0 || lev[w] > elev - 3) continue;
                path.Clear();
                int u = prevMain[w];
                while (u >= 0 && lev[u] <= elev + 2 && path.Count < maxUp) { path.Add(u); u = prevMain[u]; }
                if (u < 0 || path.Count < 2) continue;
                bool lake = false;
                foreach (int p in path) if (G.Fixed[p]) { lake = true; break; }
                if (lake) continue;
                // seq = u, path reversed, w
                int count = path.Count + 2;
                var seq = new int[count];
                seq[0] = u;
                for (int k = 0; k < path.Count; k++) seq[1 + k] = path[path.Count - 1 - k];
                seq[count - 1] = w;
                var d = new double[count];
                for (int k = 1; k < count; k++)
                {
                    double x0 = G.Lon[seq[k - 1]] * Math.Cos(G.Lat[seq[k - 1]] * Math.PI / 180), x1 = G.Lon[seq[k]] * Math.Cos(G.Lat[seq[k]] * Math.PI / 180);
                    d[k] = d[k - 1] + Math.Sqrt((x1 - x0) * (x1 - x0) + (G.Lat[seq[k]] - G.Lat[seq[k - 1]]) * (G.Lat[seq[k]] - G.Lat[seq[k - 1]]));
                }
                if (d[count - 1] <= 0) continue;
                double l0 = lev[u], l1 = lev[w];
                for (int k = 1; k < count - 1; k++)
                {
                    lev[seq[k]] = l0 + (l1 - l0) * d[k] / d[count - 1];
                    touched[seq[k]] = true;
                }
                rep.Dedammed++;
                rep.DedammedStations += count - 2;
            }
        }
    }
}
