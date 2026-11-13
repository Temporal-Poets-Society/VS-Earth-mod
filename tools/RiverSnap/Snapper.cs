namespace RiverSnap
{
    /// <summary>One pass of the course search: half width across the river H0 + Hq·√Q m (at most Params.HMax), lateral
    /// sample spacing Dl m, the lateral change penalty times LambdaMul, and the Gaussian (m) that smooths the line it
    /// searches around (pass 1: the original line; later passes: the course the pass before found).</summary>
    sealed class Pass
    {
        public double H0, Hq, Dl, LambdaMul, RefSigma;
        /// <summary>The profiles' direction comes from the searched-around line smoothed this much more (m): in a bend the
        /// profiles stay nearly parallel instead of meeting a bend's radius inside it.</summary>
        public double DirSigma;
        public override string ToString() => $"{H0}+{Hq}sqrtQ/{Dl}m/x{LambdaMul}/ref{RefSigma}/dir{DirSigma}";
    }

    /// <summary>Tuning of the course and level search (Program parses them from key=value arguments).</summary>
    sealed class Params
    {
        /// <summary>Station spacing along the river (m).</summary>
        public double Ds = 90;
        public List<Pass> Passes = new()
        {
            new Pass { H0 = 2000, Hq = 8, Dl = 60, LambdaMul = 2, RefSigma = 300, DirSigma = 1500 },
            new Pass { H0 = 600, Hq = 4, Dl = 30, LambdaMul = 1, RefSigma = 200, DirSigma = 400 },
        };
        public double HMax = 4000;
        /// <summary>Lateral change penalty (m of height per (m of lateral change)² per m along), scaled up for wide rivers
        /// by (1 + width / LambdaWidth); the pull towards the searched-around line (m of height per station at 1 km off,
        /// growing with the square of the distance); the steepest lateral change between stations (m per m).</summary>
        public double Lambda = 0.1, LambdaWidth = 200, Pull = 4, Slope = 1.5;
        /// <summary>A profile reaches at most this share of the way to another river's original line (the river stays on
        /// its own side), except within JunctionFree m of a junction with that river.</summary>
        public double OtherShare = 0.5, JunctionFree = 2000;
        /// <summary>Gaussian smoothing (m along the river) of the found offsets.</summary>
        public double OffSigma = 90;
        /// <summary>The floor: lowest GLO-90 within max(FloorMin, FloorHalfWidth · half width) of the course, then a
        /// morphological opening over ±OpenK stations along it (bridges, single bumps).</summary>
        public double FloorMin = 45, FloorHalfWidth = 0.5;
        public int OpenK = 2;
        /// <summary>Barriers: where the floor stands more than BarrierCut m above the chain's own monotone fit of it, GLO-90
        /// shows no valley the course could follow (a gorge the DEM lost, or a course led over a ridge), and the course goes
        /// back to the original line there, eased in and out over BarrierPad stations. 0: off.</summary>
        public double BarrierCut = 50;
        public int BarrierPad = 10;
        /// <summary>A river's real half width (m): 4·√Q (RiverNetwork.RealWidthMetres is 8·√Q).</summary>
        public static double HalfWidth(double q) => 4 * Math.Sqrt(Math.Max(q, 0));
    }

    /// <summary>A river followed upstream along its biggest inflow: reaches in flow order. A chain ends at a mouth or where
    /// it joins a bigger river (the first vertex of reach JunctionReach, in chain Parent).</summary>
    sealed class Chain
    {
        public int Index;
        public int[] Reaches;
        public int Parent = -1, JunctionReach = -1, Depth;
        public int StationCount;
        /// <summary>Station (within the chain) of each reach's first vertex.</summary>
        public int[] ReachStart;
        public int StationStart = -1;
        public bool Processed => StationStart >= 0;
        public long Morton;
        /// <summary>The original last vertex (a tributary's junction with its parent).</summary>
        public float EndLon, EndLat;
    }

    /// <summary>The snapped network, one entry per station, numbered in flow order (a station's downstream neighbour always
    /// has a higher number): deepest tributaries first, each chain from its source.</summary>
    sealed class Stations
    {
        public double[] Lon, Lat;
        public float[] Floor, OrigLev, Shift, Q;
        public double[] Level;
        public int[] Next;
        public bool[] Fixed, Anchor;
        public Stations(int n)
        {
            Lon = new double[n]; Lat = new double[n]; Floor = new float[n]; OrigLev = new float[n]; Shift = new float[n]; Q = new float[n];
            Level = new double[n]; Next = new int[n]; Fixed = new bool[n]; Anchor = new bool[n];
        }
        public int Count => Lon.Length;
    }

    sealed class Network
    {
        public readonly List<Reach> R;
        public readonly int[] MainIn, ChainOf, PosInChain;
        public readonly List<Chain> Chains = new();
        public int MaxDepth;
        /// <summary>The original lines' pieces by 0.02° cell (piece id: SegFirst[reach] + vertex).</summary>
        public const double SegCell = 0.02;
        public readonly Dictionary<long, int[]> SegCells = new();
        public readonly int[] SegFirst, SegReach;

        public static long CellKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

        public Network(List<Reach> reaches)
        {
            R = reaches;
            int nr = R.Count;
            MainIn = new int[nr];
            Array.Fill(MainIn, -1);
            // the biggest inflow (the first of equals, as bake_rivers' prev_main)
            for (int r = 0; r < nr; r++)
            {
                int nx = R[r].Next;
                if (nx < 0 || nx >= nr) continue;
                if (MainIn[nx] < 0 || R[r].Q > R[MainIn[nx]].Q) MainIn[nx] = r;
            }
            ChainOf = new int[nr];
            PosInChain = new int[nr];
            Array.Fill(ChainOf, -1);
            var list = new List<int>();
            for (int r = 0; r < nr; r++)
            {
                if (MainIn[r] >= 0) continue; // continues its main inflow's chain
                list.Clear();
                int c = r;
                while (true)
                {
                    list.Add(c);
                    int nx = R[c].Next;
                    if (nx < 0 || nx >= nr || MainIn[nx] != c) break;
                    c = nx;
                }
                var ch = new Chain { Index = Chains.Count, Reaches = list.ToArray() };
                for (int i = 0; i < ch.Reaches.Length; i++) { ChainOf[ch.Reaches[i]] = ch.Index; PosInChain[ch.Reaches[i]] = i; }
                var lr = R[ch.Reaches[^1]];
                ch.EndLon = lr.Lon[^1]; ch.EndLat = lr.Lat[^1];
                Chains.Add(ch);
            }
            for (int r = 0; r < nr; r++) if (ChainOf[r] < 0) throw new InvalidOperationException("reach " + r + " in no chain (a cycle?)");
            foreach (var ch in Chains)
            {
                int last = ch.Reaches[^1], nx = R[last].Next;
                if (nx >= 0 && nx < nr) { ch.JunctionReach = nx; ch.Parent = ChainOf[nx]; }
            }
            // depth: a chain's parent ends at a later reach (topological order), so deepest last reach first is parents first
            var byLast = Chains.OrderByDescending(c => c.Reaches[^1]).ToList();
            foreach (var ch in byLast)
            {
                ch.Depth = ch.Parent < 0 ? 0 : Chains[ch.Parent].Depth + 1;
                MaxDepth = Math.Max(MaxDepth, ch.Depth);
            }
            // pieces by cell
            SegFirst = new int[nr + 1];
            for (int r = 0; r < nr; r++) SegFirst[r + 1] = SegFirst[r] + Math.Max(0, R[r].Lon.Length - 1);
            SegReach = new int[SegFirst[nr]];
            var cells = new Dictionary<long, List<int>>();
            for (int r = 0; r < nr; r++)
            {
                var re = R[r];
                for (int k = 0; k + 1 < re.Lon.Length; k++)
                {
                    int id = SegFirst[r] + k;
                    SegReach[id] = r;
                    if (Math.Abs(re.Lon[k + 1] - re.Lon[k]) > 180) continue; // across the antimeridian
                    int x0 = (int)Math.Floor(Math.Min(re.Lon[k], re.Lon[k + 1]) / SegCell), x1 = (int)Math.Floor(Math.Max(re.Lon[k], re.Lon[k + 1]) / SegCell);
                    int y0 = (int)Math.Floor(Math.Min(re.Lat[k], re.Lat[k + 1]) / SegCell), y1 = (int)Math.Floor(Math.Max(re.Lat[k], re.Lat[k + 1]) / SegCell);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cy = y0; cy <= y1; cy++)
                        {
                            long key = CellKey(cx, cy);
                            if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>(2);
                            l.Add(id);
                        }
                }
            }
            foreach (var kv in cells) SegCells[kv.Key] = kv.Value.ToArray();
        }
    }

    /// <summary>
    /// Snaps one chain at a time (one per thread): the chain's line densified to stations every Ds metres; per pass a line
    /// to search around (pass 1: the original, later: the course found before, each Gaussian-smoothed), GLO-90 profiles
    /// across it, and the valley floor's course as the cheapest path through the profiles (Viterbi): height, plus a
    /// penalty on lateral change between stations, plus a weak pull towards the searched-around line. A profile stops
    /// short of where the next profiles cross it (inside tight bends) and half-way to another river's original line.
    /// Lake and sea vertices and a mouth's last vertex stay where they are; a tributary ends exactly at its main river's
    /// snapped junction (the main river is snapped first).
    /// </summary>
    sealed class Worker
    {
        const double M = 111195.0; // metres per degree (sphere of 6371 km)
        readonly Network net;
        readonly Params P;
        readonly Stations G;
        readonly Sampler smp;
        public long Fallbacks, NanSamples, Overshoot, StationsDone, Samples, OtherLimited, BendLimited, BarrierStations, BarrierChains;
        readonly PriorityQueue<double, double> fitHeap = new();
        double[] bFit, bTop, bW;

        public Worker(Network net, Params p, Stations g, TileStore store)
        {
            this.net = net; P = p; G = g; smp = store != null ? new Sampler(store) : null;
        }

        // vertices of the chain
        double[] vLon = new double[64], vLat = new double[64], vLev = new double[64];
        bool[] vFix = new bool[64];
        int[] vReach = new int[64];
        int nv;
        // stations
        double[] r0Lon, r0Lat, sAl, oLev, refLon, refLat, nX, nY, hHalf, lo, hi, wgt, lam, q, off, pLon, pLat, floor, tA, tB, pinD;
        bool[] fixSt, cons, isVtxFixed;
        double[] consOff;
        int[] reachSt, J, pStart, path;
        float[] prof, cost;
        short[] back;
        double[] D0 = new double[256], D1 = new double[256];
        int n;
        double dl; // this pass's lateral spacing

        static void Grow<T>(ref T[] a, int n)
        {
            if (a == null || a.Length < n) a = new T[Math.Max(n, (a?.Length ?? 0) * 3 / 2 + 16)];
        }

        static double Wrap180(double x) { x %= 360; if (x >= 180) x -= 360; if (x < -180) x += 360; return x; }

        static double SegMetres(double lon1, double lat1, double lon2, double lat2)
        {
            double c = Math.Cos(0.5 * (lat1 + lat2) * Math.PI / 180);
            double dx = (lon2 - lon1) * c * M, dy = (lat2 - lat1) * M;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        void Gather(Chain c)
        {
            nv = 0;
            for (int m = 0; m < c.Reaches.Length; m++)
            {
                var re = net.R[c.Reaches[m]];
                for (int k = 0; k < re.Lon.Length; k++)
                {
                    if (m > 0 && k == 0)
                    {
                        // the joint: the last vertex of the reach before, which is the same point
                        vFix[nv - 1] |= re.Fixed[0];
                        vLev[nv - 1] = re.Level[0];
                        continue;
                    }
                    if (nv == vLon.Length)
                    {
                        Array.Resize(ref vLon, nv * 2); Array.Resize(ref vLat, nv * 2); Array.Resize(ref vLev, nv * 2);
                        Array.Resize(ref vFix, nv * 2); Array.Resize(ref vReach, nv * 2);
                    }
                    double lon = re.Lon[k];
                    if (nv > 0) lon = vLon[nv - 1] + Wrap180(lon - vLon[nv - 1]);
                    vLon[nv] = lon; vLat[nv] = re.Lat[k]; vLev[nv] = re.Level[k]; vFix[nv] = re.Fixed[k]; vReach[nv] = m;
                    nv++;
                }
            }
        }

        int SegCount(int k) => Math.Max(1, (int)Math.Ceiling(SegMetres(vLon[k], vLat[k], vLon[k + 1], vLat[k + 1]) / P.Ds));

        /// <summary>Station count and each reach's first station, without building the stations.</summary>
        public void Layout(Chain c)
        {
            Gather(c);
            var rs = new int[c.Reaches.Length];
            int count = 0, m = 0;
            for (int k = 0; k < nv; k++)
            {
                // vertex k starts reach vReach[k + 1] when that differs (the joint carries the upstream reach's label)
                if (k == 0) rs[0] = 0;
                else if (k < nv - 1 && vReach[k + 1] != vReach[k]) rs[++m] = count;
                if (k < nv - 1) count += SegCount(k);
                else count += 1;
            }
            if (m != c.Reaches.Length - 1) throw new InvalidOperationException($"chain {c.Index}: {m + 1} reach starts for {c.Reaches.Length} reaches");
            c.ReachStart = rs;
            c.StationCount = count;
        }

        void Densify(Chain c)
        {
            n = c.StationCount;
            Grow(ref r0Lon, n); Grow(ref r0Lat, n); Grow(ref sAl, n); Grow(ref oLev, n); Grow(ref fixSt, n); Grow(ref isVtxFixed, n);
            Grow(ref q, n); Grow(ref reachSt, n);
            int i = 0;
            double s = 0;
            for (int k = 0; k < nv - 1; k++)
            {
                int ns = SegCount(k);
                double L = SegMetres(vLon[k], vLat[k], vLon[k + 1], vLat[k + 1]);
                int reach = vReach[k + 1];
                for (int u = 0; u < ns; u++)
                {
                    double f = (double)u / ns;
                    r0Lon[i] = vLon[k] + f * (vLon[k + 1] - vLon[k]);
                    r0Lat[i] = vLat[k] + f * (vLat[k + 1] - vLat[k]);
                    sAl[i] = s + f * L;
                    oLev[i] = vLev[k] + f * (vLev[k + 1] - vLev[k]);
                    fixSt[i] = u == 0 ? vFix[k] : vFix[k] && vFix[k + 1];
                    isVtxFixed[i] = u == 0 && vFix[k];
                    reachSt[i] = reach;
                    q[i] = net.R[c.Reaches[reach]].Q;
                    i++;
                }
                s += L;
            }
            r0Lon[i] = vLon[nv - 1]; r0Lat[i] = vLat[nv - 1]; sAl[i] = s; oLev[i] = vLev[nv - 1];
            fixSt[i] = vFix[nv - 1]; isVtxFixed[i] = vFix[nv - 1]; reachSt[i] = vReach[nv - 1];
            q[i] = net.R[c.Reaches[vReach[nv - 1]]].Q;
            i++;
            if (i != n) throw new InvalidOperationException($"chain {c.Index}: {i} stations, laid out {n}");
        }

        /// <summary>Distance along the river (m) to the nearest pinned station (and the chain's ends).</summary>
        void PinDistances(bool[] pin)
        {
            Grow(ref pinD, n);
            double last = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || pin[i]) last = sAl[i];
                pinD[i] = sAl[i] - last;
            }
            last = double.PositiveInfinity;
            for (int i = n - 1; i >= 0; i--)
            {
                if (i == n - 1 || pin[i]) last = sAl[i];
                pinD[i] = Math.Min(pinD[i], last - sAl[i]);
            }
        }

        static double SmoothStep(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        /// <summary>Gaussian along the river (sigma m), fading to nothing at pinned stations and the ends (pinD set).</summary>
        void Smooth(double[] a, double[] outA, double sigma)
        {
            if (sigma <= 0) { Array.Copy(a, outA, n); return; }
            int lo_ = 0, hi_ = 0;
            double reach = 3 * sigma, inv = 1 / (2 * sigma * sigma);
            for (int i = 0; i < n; i++)
            {
                double k = SmoothStep(pinD[i] / (2 * sigma));
                if (k <= 0) { outA[i] = a[i]; continue; }
                while (sAl[lo_] < sAl[i] - reach) lo_++;
                if (hi_ < i) hi_ = i;
                while (hi_ + 1 < n && sAl[hi_ + 1] <= sAl[i] + reach) hi_++;
                double ws = 0, xs = 0;
                for (int j = lo_; j <= hi_; j++)
                {
                    double d = sAl[j] - sAl[i], w = Math.Exp(-d * d * inv);
                    ws += w; xs += w * a[j];
                }
                outA[i] = a[i] + (xs / ws - a[i]) * k;
            }
        }

        /// <summary>Gaussian along the river without pins (for directions).</summary>
        void SmoothFree(double[] a, double[] outA, double sigma)
        {
            if (sigma <= 0) { Array.Copy(a, outA, n); return; }
            int lo_ = 0, hi_ = 0;
            double reach = 3 * sigma, inv = 1 / (2 * sigma * sigma);
            for (int i = 0; i < n; i++)
            {
                while (sAl[lo_] < sAl[i] - reach) lo_++;
                if (hi_ < i) hi_ = i;
                while (hi_ + 1 < n && sAl[hi_ + 1] <= sAl[i] + reach) hi_++;
                double ws = 0, xs = 0;
                for (int j = lo_; j <= hi_; j++)
                {
                    double d = sAl[j] - sAl[i], w = Math.Exp(-d * d * inv);
                    ws += w; xs += w * a[j];
                }
                outA[i] = xs / ws;
            }
        }

        double[] dirLon, dirLat;

        void Normals(double dirSigma)
        {
            Grow(ref nX, n); Grow(ref nY, n); Grow(ref dirLon, n); Grow(ref dirLat, n);
            SmoothFree(refLon, dirLon, dirSigma);
            SmoothFree(refLat, dirLat, dirSigma);
            double px = 0, py = 1;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 2), b = Math.Min(n - 1, i + 2);
                double tx = (dirLon[b] - dirLon[a]) * Math.Cos(refLat[i] * Math.PI / 180) * M, ty = (dirLat[b] - dirLat[a]) * M;
                double len = Math.Sqrt(tx * tx + ty * ty);
                if (len > 1e-3) { px = -ty / len; py = tx / len; }
                nX[i] = px; nY[i] = py; // left of the flow
            }
        }

        /// <summary>How far each profile may reach on either side: not past the crossing with a neighbouring profile (inside
        /// a tight bend the profiles meet), nor more than OtherShare of the way to another river's original line.</summary>
        void Limits(Chain c)
        {
            Grow(ref lo, n); Grow(ref hi, n);
            var chains = net.Chains;
            for (int i = 0; i < n; i++)
            {
                double H = hHalf[i], l = -H, h = H;
                double cl = Math.Cos(refLat[i] * Math.PI / 180);
                // neighbouring profiles
                for (int j = Math.Max(0, i - 3); j <= Math.Min(n - 1, i + 3); j++)
                {
                    if (j == i) continue;
                    double dx = (refLon[j] - refLon[i]) * cl * M, dy = (refLat[j] - refLat[i]) * M;
                    double cr = nX[i] * nY[j] - nY[i] * nX[j];
                    if (Math.Abs(cr) < 1e-9) continue;
                    double a = (dx * nY[j] - dy * nX[j]) / cr;
                    if (a > 0 && a < H) h = Math.Min(h, 0.9 * a);
                    else if (a < 0 && a > -H) l = Math.Max(l, 0.9 * a);
                }
                if (h < H || l > -H) BendLimited++;
                // other rivers' original lines
                double ext = H / Math.Max(P.OtherShare, 1e-3);
                double eLon = ext * Math.Abs(nX[i]) / (M * cl), eLat = ext * Math.Abs(nY[i]) / M;
                int x0 = (int)Math.Floor(Wrap180(refLon[i] - eLon) / Network.SegCell), x1 = (int)Math.Floor(Wrap180(refLon[i] + eLon) / Network.SegCell);
                int y0 = (int)Math.Floor((refLat[i] - eLat) / Network.SegCell), y1 = (int)Math.Floor((refLat[i] + eLat) / Network.SegCell);
                if (x1 < x0) x1 = x0; // (across the antimeridian: only the first column)
                double oPlus = double.PositiveInfinity, oMinus = double.NegativeInfinity;
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cy = y0; cy <= y1; cy++)
                    {
                        if (!net.SegCells.TryGetValue(Network.CellKey(cx, cy), out var ids)) continue;
                        foreach (int id in ids)
                        {
                            int r = net.SegReach[id];
                            int ch = net.ChainOf[r];
                            if (ch == c.Index) continue;
                            var re = net.R[r];
                            int k = id - net.SegFirst[r];
                            double ax = Wrap180(re.Lon[k] - refLon[i]) * cl * M, ay = (re.Lat[k] - refLat[i]) * M;
                            double bx = Wrap180(re.Lon[k + 1] - refLon[i]) * cl * M, by = (re.Lat[k + 1] - refLat[i]) * M;
                            double ddx = bx - ax, ddy = by - ay;
                            double det = -nX[i] * ddy + ddx * nY[i];
                            if (Math.Abs(det) < 1e-9) continue;
                            double t = (nX[i] * ay - nY[i] * ax) / det;
                            if (t < 0 || t > 1) continue;
                            double o = (-ax * ddy + ddx * ay) / det;
                            if (o == 0 || Math.Abs(o) > ext) continue;
                            // not near a junction with that river (a tributary meets its main river there)
                            int other = ch;
                            float jLon = float.NaN, jLat = 0;
                            if (chains[other].Parent == c.Index) { jLon = chains[other].EndLon; jLat = chains[other].EndLat; }
                            else if (c.Parent == other) { jLon = c.EndLon; jLat = c.EndLat; }
                            if (!float.IsNaN(jLon))
                            {
                                double jx = Wrap180(jLon - refLon[i]) * cl * M - o * nX[i], jy = (jLat - refLat[i]) * M - o * nY[i];
                                if (jx * jx + jy * jy < P.JunctionFree * P.JunctionFree) continue;
                            }
                            if (o > 0) oPlus = Math.Min(oPlus, o); else oMinus = Math.Max(oMinus, o);
                        }
                    }
                }
                double h2 = Math.Min(h, P.OtherShare * oPlus), l2 = Math.Max(l, P.OtherShare * oMinus);
                if (h2 < h || l2 > l) OtherLimited++;
                lo[i] = Math.Min(0, l2);
                hi[i] = Math.Max(0, h2);
            }
        }

        public void Process(Chain c)
        {
            Gather(c);
            Densify(c);
            Grow(ref cons, n); Grow(ref consOff, n); Grow(ref refLon, n); Grow(ref refLat, n); Grow(ref hHalf, n);
            Grow(ref wgt, n); Grow(ref lam, n); Grow(ref off, n); Grow(ref pLon, n); Grow(ref pLat, n); Grow(ref floor, n);
            Grow(ref tA, n); Grow(ref tB, n); Grow(ref J, n); Grow(ref pStart, n + 1); Grow(ref path, n);
            for (int i = 0; i < n; i++)
            {
                cons[i] = fixSt[i];
                consOff[i] = 0;
                double ds0 = i > 0 ? sAl[i] - sAl[i - 1] : 0, ds1 = i < n - 1 ? sAl[i + 1] - sAl[i] : 0;
                wgt[i] = Math.Max(0.05, (i == 0 || i == n - 1 ? ds0 + ds1 : 0.5 * (ds0 + ds1)) / P.Ds);
            }
            bool tributary = c.Parent >= 0;
            var parent = tributary ? net.Chains[c.Parent] : null;
            bool parentDone = parent != null && parent.Processed;
            int jGlobal = -1;
            double tLon = r0Lon[n - 1], tLat = r0Lat[n - 1];
            if (parentDone)
            {
                jGlobal = parent.StationStart + parent.ReachStart[net.PosInChain[c.JunctionReach]];
                tLon = r0Lon[n - 1] + Wrap180(G.Lon[jGlobal] - r0Lon[n - 1]);
                tLat = G.Lat[jGlobal];
            }
            // the end: a mouth's last vertex stays, a tributary's goes to its main river's junction
            cons[n - 1] = true;
            PinDistances(cons);

            for (int pass = 0; pass < P.Passes.Count; pass++)
            {
                var ps = P.Passes[pass];
                dl = ps.Dl;
                // the line to search around: pass 1 the original, later passes the course found before; smoothed, pinned
                // at lake/sea stations and the ends
                if (pass == 0) { Smooth(r0Lon, refLon, ps.RefSigma); Smooth(r0Lat, refLat, ps.RefSigma); }
                else { Smooth(pLon, refLon, ps.RefSigma); Smooth(pLat, refLat, ps.RefSigma); }
                Normals(ps.DirSigma);
                for (int i = 0; i < n; i++)
                {
                    hHalf[i] = Math.Min(P.HMax, ps.H0 + ps.Hq * Math.Sqrt(q[i]));
                    lam[i] = ps.LambdaMul * P.Lambda * (1 + 8 * Math.Sqrt(q[i]) / P.LambdaWidth);
                }
                // the end's target as an offset across its profile
                {
                    double cl = Math.Cos(refLat[n - 1] * Math.PI / 180);
                    double ex = (tLon - refLon[n - 1]) * cl * M, ey = (tLat - refLat[n - 1]) * M;
                    consOff[n - 1] = ex * nX[n - 1] + ey * nY[n - 1];
                }
                Limits(c);
                Profiles();
                Viterbi();
                // offsets, smoothed (pinned where constrained)
                for (int i = 0; i < n; i++) tA[i] = (path[i] - J[i]) * dl;
                Smooth(tA, off, P.OffSigma);
                for (int i = 0; i < n; i++)
                {
                    if (cons[i]) off[i] = Math.Clamp(consOff[i], -J[i] * dl, J[i] * dl);
                    double cl = Math.Cos(refLat[i] * Math.PI / 180);
                    pLon[i] = refLon[i] + off[i] * nX[i] / (M * cl);
                    pLat[i] = refLat[i] + off[i] * nY[i] / M;
                }
                if (DebugAt != null) DebugDump(c, pass);
            }
            // the floor at the course: lowest GLO-90 within the floor half width of it, from the last pass's profiles
            for (int i = 0; i < n; i++)
            {
                double rf = Math.Max(P.FloorMin, P.FloorHalfWidth * Params.HalfWidth(q[i]));
                int j0 = (int)Math.Ceiling((off[i] - rf) / dl) + J[i], j1 = (int)Math.Floor((off[i] + rf) / dl) + J[i];
                j0 = Math.Clamp(j0, 0, 2 * J[i]); j1 = Math.Clamp(j1, 0, 2 * J[i]);
                if (j1 < j0) j0 = j1 = Math.Clamp((int)Math.Round(off[i] / dl) + J[i], 0, 2 * J[i]);
                double mn = double.PositiveInfinity;
                for (int j = j0; j <= j1; j++) { double v = prof[pStart[i] + j]; if (!double.IsNaN(v) && v < mn) mn = v; }
                floor[i] = double.IsPositiveInfinity(mn) ? double.NaN : mn;
            }
            // the end exactly at the target; stations that would run past it are laid on the way to it
            pLon[n - 1] = tLon; pLat[n - 1] = tLat;
            if (tributary)
            {
                int k1 = n - 1;
                for (int k = n - 2; k >= Math.Max(1, n - 60); k--)
                {
                    if (fixSt[k]) break;
                    double cl = Math.Cos(pLat[k] * Math.PI / 180);
                    int a = Math.Max(0, k - 1), b = k + 1;
                    double tx = (pLon[b] - pLon[a]) * cl, ty = pLat[b] - pLat[a];
                    double dx = (tLon - pLon[k]) * cl, dy = tLat - pLat[k];
                    double dot = (tx * dx + ty * dy) * M * M, len = Math.Sqrt(tx * tx + ty * ty) * M;
                    double along = len > 0 ? dot / len : 0;
                    if (along < 0.25 * P.Ds) k1 = k; else break;
                }
                if (k1 < n - 1)
                {
                    Overshoot++;
                    int a = k1 - 1;
                    for (int k = k1; k < n - 1; k++)
                    {
                        double f = (double)(k - a) / (n - 1 - a);
                        pLon[k] = pLon[a] + f * (tLon - pLon[a]);
                        pLat[k] = pLat[a] + f * (tLat - pLat[a]);
                    }
                }
            }
            // opening along the river: bumps narrower than the window (bridges) go, slopes stay
            int end = tributary ? n - 1 : n;
            Open(floor, end, P.OpenK);
            if (P.BarrierCut > 0) RevertBarriers(end);
            // results
            int g0 = c.StationStart;
            for (int i = 0; i < n; i++)
            {
                int g = g0 + i;
                G.Lon[g] = Wrap180(pLon[i]);
                G.Lat[g] = pLat[i];
                G.Floor[g] = (float)floor[i];
                G.OrigLev[g] = (float)oLev[i];
                G.Fixed[g] = fixSt[i];
                G.Q[g] = (float)q[i];
                G.Next[g] = i < n - 1 ? g + 1 : -1;
                G.Shift[g] = (float)SegMetres(r0Lon[i], r0Lat[i], pLon[i], pLat[i]);
                G.Anchor[g] = isVtxFixed[i] || i == 0 || i == n - 1;
            }
            foreach (int st in c.ReachStart) G.Anchor[g0 + st] = true;
            if (parentDone)
            {
                int g = g0 + n - 1;
                G.Lon[g] = G.Lon[jGlobal];
                G.Lat[g] = G.Lat[jGlobal];
                G.Floor[g] = G.Floor[jGlobal];
                G.Next[g] = jGlobal;
            }
            else if (tributary) G.Floor[g0 + n - 1] = (float)floor[n - 1];
            StationsDone += n;
        }

        /// <summary>
        /// Barrier stretches (Params.BarrierCut): the floor more than BarrierCut above the chain's own L1 monotone fit. The
        /// course there goes back to the original line, eased over BarrierPad stations each way and never at the chain's
        /// last station (a tributary's end stays on its main river); the floor there is GLO-90 at the course, which the
        /// level fit then cuts through.
        /// </summary>
        void RevertBarriers(int count)
        {
            if (count < 3) return;
            Grow(ref bFit, count); Grow(ref bTop, count); Grow(ref bW, n);
            L1Fit.Fit(floor, 0, count, bFit, fitHeap, bTop);
            int pad = Math.Max(0, P.BarrierPad);
            bool any = false;
            for (int i = 0; i < n; i++) bW[i] = 0;
            for (int i = 0; i < count; i++)
            {
                if (double.IsNaN(floor[i]) || floor[i] - bFit[i] <= P.BarrierCut) continue;
                any = true;
                for (int j = Math.Max(0, i - pad); j <= Math.Min(n - 1, i + pad); j++)
                {
                    double w = SmoothStep(1 - (double)Math.Abs(j - i) / (pad + 1));
                    if (w > bW[j]) bW[j] = w;
                }
            }
            if (!any) return;
            BarrierChains++;
            for (int i = 0; i < n; i++)
            {
                double w = bW[i] * SmoothStep((n - 1 - i) / (double)(pad + 1));
                if (w <= 0) continue;
                pLon[i] += w * (r0Lon[i] - pLon[i]);
                pLat[i] += w * (r0Lat[i] - pLat[i]);
                if (i < count)
                {
                    double h = smp.Height(pLat[i], Wrap180(pLon[i]));
                    if (!double.IsNaN(h)) floor[i] = h;
                }
                BarrierStations++;
            }
        }

        /// <summary>Debugging: (lat, lon, file prefix) of a place; the chain passing within 0.005° of it writes each pass.</summary>
        public static (double Lat, double Lon, string Prefix)? DebugAt;

        void DebugDump(Chain c, int pass)
        {
            var (dLat, dLon, prefix) = DebugAt.Value;
            bool near = false;
            for (int i = 0; i < n && !near; i++) near = Math.Abs(r0Lat[i] - dLat) < 0.005 && Math.Abs(Wrap180(r0Lon[i] - dLon)) < 0.005;
            if (!near) return;
            using var w = new StreamWriter($"{prefix}-chain{c.Index}-pass{pass}.csv");
            w.WriteLine("i,s,r0lon,r0lat,reflon,reflat,nx,ny,H,lo,hi,raw,off,plon,plat,cons,J");
            for (int i = 0; i < n; i++)
                w.WriteLine(FormattableString.Invariant($"{i},{sAl[i]:F1},{r0Lon[i]:F6},{r0Lat[i]:F6},{refLon[i]:F6},{refLat[i]:F6},{nX[i]:F4},{nY[i]:F4},{hHalf[i]:F0},{lo[i]:F0},{hi[i]:F0},{(path[i] - J[i]) * dl:F0},{off[i]:F1},{pLon[i]:F6},{pLat[i]:F6},{(cons[i] ? 1 : 0)},{J[i]}"));
            // the profiles' heights too
            using var h = new StreamWriter($"{prefix}-chain{c.Index}-pass{pass}-prof.csv");
            for (int i = 0; i < n; i++)
            {
                var sb = new System.Text.StringBuilder();
                for (int j = 0; j <= 2 * J[i]; j++) { if (j > 0) sb.Append(','); sb.Append(prof[pStart[i] + j].ToString("F0", System.Globalization.CultureInfo.InvariantCulture)); }
                h.WriteLine(sb.ToString());
            }
        }

        void Open(double[] f, int count, int k)
        {
            if (k <= 0 || count < 3) return;
            Grow(ref tA, count); Grow(ref tB, count);
            for (int i = 0; i < count; i++)
            {
                double m = double.PositiveInfinity;
                for (int j = Math.Max(0, i - k); j <= Math.Min(count - 1, i + k); j++) if (!double.IsNaN(f[j]) && f[j] < m) m = f[j];
                tA[i] = double.IsPositiveInfinity(m) ? double.NaN : m;
            }
            for (int i = 0; i < count; i++)
            {
                double m = double.NegativeInfinity;
                for (int j = Math.Max(0, i - k); j <= Math.Min(count - 1, i + k); j++) if (!double.IsNaN(tA[j]) && tA[j] > m) m = tA[j];
                if (!double.IsNaN(f[i])) f[i] = double.IsNegativeInfinity(m) ? f[i] : m;
            }
        }

        void Profiles()
        {
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                J[i] = Math.Max(1, (int)Math.Ceiling(hHalf[i] / dl));
                pStart[i] = total;
                total += 2 * J[i] + 1;
            }
            pStart[n] = total;
            Grow(ref prof, total); Grow(ref cost, total); Grow(ref back, total);
            for (int i = 0; i < n; i++)
            {
                double cl = Math.Cos(refLat[i] * Math.PI / 180);
                double dLon = nX[i] / (M * cl), dLat = nY[i] / M;
                int b = pStart[i], w = 2 * J[i] + 1;
                double hiFinite = double.NegativeInfinity;
                for (int j = 0; j < w; j++)
                {
                    double o = (j - J[i]) * dl;
                    double h = smp.Height(refLat[i] + o * dLat, refLon[i] + o * dLon);
                    prof[b + j] = (float)h;
                    if (!double.IsNaN(h)) hiFinite = Math.Max(hiFinite, h); else NanSamples++;
                }
                Samples += w;
                // the cost: the highest ground within most of the river's half width (a wide river's centre lies where its
                // whole width fits on the low ground); no data counts as the profile's highest ground
                int rc = (int)Math.Floor(0.75 * Params.HalfWidth(q[i]) / dl);
                for (int j = 0; j < w; j++)
                {
                    double m = double.NegativeInfinity;
                    for (int jj = Math.Max(0, j - rc); jj <= Math.Min(w - 1, j + rc); jj++)
                    {
                        double v = prof[b + jj];
                        if (double.IsNaN(v)) v = double.IsNegativeInfinity(hiFinite) ? 0 : hiFinite;
                        if (v > m) m = v;
                    }
                    cost[b + j] = (float)m;
                }
            }
        }

        void Viterbi()
        {
            const double Inf = double.PositiveInfinity;
            int Jmax = 0;
            for (int i = 0; i < n; i++) Jmax = Math.Max(Jmax, J[i]);
            if (D0.Length < 2 * Jmax + 1) { D0 = new double[2 * Jmax + 1]; D1 = new double[2 * Jmax + 1]; }
            double pull = P.Pull / 1e6; // per m² of offset
            for (int i = 0; i < n; i++)
            {
                int Jc = J[i], b = pStart[i];
                if (i > 0) (D0, D1) = (D1, D0);
                int jc = cons[i] ? Math.Clamp((int)Math.Round(consOff[i] / dl) + Jc, 0, 2 * Jc) : -1;
                int jlo = (int)Math.Ceiling(lo[i] / dl - 1e-9) + Jc, jhi = (int)Math.Floor(hi[i] / dl + 1e-9) + Jc;
                if (i == 0)
                {
                    for (int j = 0; j <= 2 * Jc; j++)
                    {
                        back[b + j] = -1;
                        if (jc >= 0 ? j != jc : j < jlo || j > jhi) { D1[j] = Inf; continue; }
                        double o = (j - Jc) * dl;
                        D1[j] = wgt[0] * (cost[b + j] + pull * o * o);
                    }
                    continue;
                }
                int Jp = J[i - 1];
                double ds = Math.Max(1, sAl[i] - sAl[i - 1]);
                double li = lam[i] / ds;
                int K = Math.Max(1, (int)Math.Ceiling(P.Slope * ds / dl));
                bool fellBack = false;
                for (int j = 0; j <= 2 * Jc; j++)
                {
                    if (jc >= 0 ? j != jc : j < jlo || j > jhi) { D1[j] = Inf; back[b + j] = -1; continue; }
                    int rel = j - Jc;
                    int lo_ = Math.Max(0, rel + Jp - K), hi_ = Math.Min(2 * Jp, rel + Jp + K);
                    double best = Inf;
                    int arg = -1;
                    for (int jp = lo_; jp <= hi_; jp++)
                    {
                        double d = D0[jp];
                        if (d == Inf) continue;
                        double dd = (rel - (jp - Jp)) * dl;
                        double v = d + li * dd * dd;
                        if (v < best) { best = v; arg = jp; }
                    }
                    if (arg < 0)
                    {
                        // out of reach of every state before (a constraint or a limit): any of them, at the quadratic cost
                        if (!fellBack) { Fallbacks++; fellBack = true; }
                        for (int jp = 0; jp <= 2 * Jp; jp++)
                        {
                            double d = D0[jp];
                            if (d == Inf) continue;
                            double dd = (rel - (jp - Jp)) * dl;
                            double v = d + li * dd * dd;
                            if (v < best) { best = v; arg = jp; }
                        }
                    }
                    double o = rel * dl;
                    D1[j] = best + wgt[i] * (cost[b + j] + pull * o * o);
                    back[b + j] = (short)arg;
                }
            }
            // back from the cheapest end state
            int Jl = J[n - 1], bj = Jl;
            double bv = Inf;
            for (int j = 0; j <= 2 * Jl; j++) if (D1[j] < bv) { bv = D1[j]; bj = j; }
            path[n - 1] = bj;
            for (int i = n - 1; i > 0; i--)
            {
                int pj = back[pStart[i] + path[i]];
                path[i - 1] = pj < 0 ? J[i - 1] : pj;
            }
        }
    }
}
