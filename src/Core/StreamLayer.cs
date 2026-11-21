using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0131 (decision 0061): the streams layer of a world at 1:25 and finer (EarthParams.StreamRules), from StreamData,
    /// loaded by area as chunks generate. The world's own river network (TerrainModel.Rivers) is left as it is; each 1° tile's
    /// stream systems (those whose outlet lies in it) become a small RiverNetwork of their own when a column first asks
    /// near them, with the world's river rules (pools, steps, chutes and falls with their lips and plunge pools, rapids,
    /// banks held at the water) and every reach kept at the smallest stream's width, depth, bank and valley
    /// (RiverBuildOptions.KeepAll). A column looks at its own tile's systems and its visitors' (the home tiles whose systems
    /// reach into it), the nearest stream winning.
    /// <para>Joins follow the water as drawn, not a baked copy of it: a system's outlet ends at the level of the water the
    /// world draws at its last vertex without the streams (a river, a lake or the sea: TerrainModel.MainColumn), and a system
    /// ending at a large river whose drawn course lies off its last vertex goes on to that course's nearest point (within
    /// JoinSearchMetres); the stream's levels are raised to that water's where lower (it never runs uphill into it).
    /// </para>
    /// <para>Deterministic whatever order chunks come in: a tile's network is a function of its systems and of the world's
    /// drawn water without the streams; the systems are independent basins (RiverPoolPlanner lays each out on its own); a
    /// column's answer is the nearest of the networks its tile lists. Thread-safe; a build that fails (a GLO-90 square not
    /// here yet) is not kept, so it is tried again.</para>
    /// <para>The land a tile is fitted to is GLO-90's, as chunk generation reads it, whichever thread asks first (Langholm,
    /// 18 November 2026: World Map's world chart, on a thread that takes the built-in heights, asked about the Border Esk's
    /// tile first; fitted to the coarse land, the Esk's narrow valley stood more than RidgeCutMetres above its water, two of
    /// its reaches were cut there, and the layer kept that tile for the session, the chunks generated after it included). A
    /// thread that takes the built-in heights (FineElevation.BuiltInOnly: a picture of the whole world) gets no streams and
    /// builds no tile; a tile built while one of its reads took the built-in heights where generation reads GLO-90 (a square
    /// not on this machine, asked by a thread that may not fetch it: FineElevation.MissedReads) is never kept and gives that
    /// ask no streams there; until a GLO-90 square arrives, threads that may not fetch don't build it again (a thread that
    /// may, chunk generation, always does).</para>
    /// </summary>
    public sealed class StreamLayer
    {
        /// <summary>How far (m) a system ending at a large river looks for that river's drawn course from its last vertex.</summary>
        public const double JoinSearchMetres = 1000;
        /// <summary>How far (m) back from its end a system's outlet reach is looked along, a block at a time, for where it comes
        /// into its outlet's drawn water.</summary>
        public const double JoinScanMetres = 1000;
        /// <summary>The rules this build draws (EarthParams.StreamRules): 1 = this layer as described.</summary>
        public const int RulesVersion = 1;
        /// <summary>How far (m) a stream's water may lie under the land beneath its course before the course is taken to cross a
        /// ridge it couldn't have cut (FitToLand).</summary>
        public const double RidgeCutMetres = 120;
        /// <summary>How far downstream (blocks) a vertex's fall is measured for the room its pools need (LagAllowance).</summary>
        public const double StepWindow = 24;
        /// <summary>FitToLand's sampling of the land along a course: every LandStep blocks, and LandSide blocks to either side.</summary>
        public const double LandStep = 3, LandSide = 0;
        /// <summary>FitToLand's vertices added where a piece's water line stands above the land, or more than HoldBlocks under it
        /// (EA-0165): every RefineStep blocks.</summary>
        public const double RefineStep = 9;
        public const int HoldBlocks = 2;
        /// <summary>How far (blocks) a stream's water (a ramp's flowing layer counting) may stand above the land as drawn without
        /// it, and how often a tile's network is laid out again to get there (LowerUnderPools).</summary>
        public const int PoolHold = 1, SettleRounds = 4;
        /// <summary>(diagnostics) told of each ridge cut with the reach's land and levels, and with DebugLat/DebugLon set, of the
        /// levels after each of FitToLand's passes of the reaches with a vertex within 40 m of that place.</summary>
        internal static Action<string> DebugLog;
        internal static double DebugLat = double.NaN, DebugLon = double.NaN;

        readonly StreamData data;
        readonly TerrainModel model;
        readonly RiverBuildOptions options;
        readonly ConcurrentDictionary<int, Lazy<Built>> homes = new();

        /// <summary>A home tile's network as built: whether it was fitted to the land generation reads (no read on the building
        /// thread took the built-in heights where generation reads GLO-90), and the thread that built it.</summary>
        sealed class Built
        {
            public RiverNetwork Net;
            public bool OnGlo90;
            public int Thread;
        }

        /// <summary>Tiles whose last build lacked a GLO-90 square, with the reader's arrivals then (FineElevation.Arrivals).</summary>
        readonly ConcurrentDictionary<int, long> lacking = new();
        readonly ConcurrentDictionary<int, int[]> sources = new();
        long built, reachesBuilt, buildTicks, joinTicks, fitTicks, lowered, ridges, refined, resettled, merged, raised;
        /// <summary>Vertices raised towards the land under them (FitToLand's first pass, EA-0165).</summary>
        public long VerticesRaised => Interlocked.Read(ref raised);
        /// <summary>Junctions moved up to where their inflows first crossed (MergeCrossingInflows, EA-0164).</summary>
        public long JunctionsMoved => Interlocked.Read(ref merged);

        public StreamData Data => data;
        /// <summary>Home tiles built so far, their reaches, and the time spent building them (ms).</summary>
        public long TilesBuilt => Interlocked.Read(ref built);
        public long ReachesBuilt => Interlocked.Read(ref reachesBuilt);
        public double BuildMs => Interlocked.Read(ref buildTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        /// <summary>Of BuildMs, the time spent fitting the levels to the land (ms), and the vertices lowered and the reaches
        /// cut at a ridge by that fit (FitToLand).</summary>
        public double FitMs => Interlocked.Read(ref fitTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public long VerticesLowered => Interlocked.Read(ref lowered);
        public long RidgeCuts => Interlocked.Read(ref ridges);
        /// <summary>Reaches given vertices to follow the land (FitToLand, Refine).</summary>
        public long ReachesRefined => Interlocked.Read(ref refined);
        /// <summary>Tiles' networks laid out again with vertices lowered under their pools (LowerUnderPools).</summary>
        public long Relayouts => Interlocked.Read(ref resettled);
        /// <summary>Of BuildMs, the time spent joining the outlets to the drawn water (ms).</summary>
        public double JoinMs => Interlocked.Read(ref joinTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <param name="worldOptions">The world's own river network's options (EarthParams.RiverOptions): the layer's networks
        /// take its rules and widths, without its falls, canals, ends and fades, and keep every reach.</param>
        /// <summary>(EA-0200, decision 0098) The smallest stream drawn, m³/s (EarthParams.StreamLayerMinDischarge, the Earth tab's
        /// Smallest stream): a system's streams below it are left out (<see cref="KeepFrom"/>). 1 or less: every stream of the
        /// file, as before.</summary>
        public double SmallestQ { get; init; }

        public StreamLayer(StreamData data, TerrainModel model, RiverBuildOptions worldOptions)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            options = worldOptions with
            {
                StreamMode = "all", Falls = null, SeaLevelPiece = null, ContinueDownstream = false, FadeEnds = null, KeepAll = true,
            };
        }

        /// <summary>The stream nearest to (x, z) (source coordinates; lat, lon its place) whose water, bank or valley reaches
        /// it, and the network it is in.</summary>
        public bool Nearest(double x, double z, double lat, double lon, out RiverNetwork.Hit hit, out RiverNetwork net)
        {
            hit = default;
            net = null;
            // a picture of the whole world on the built-in heights: no streams (the layer is fitted to GLO-90, which this thread
            // doesn't read)
            if (FineElevation.BuiltInOnly) return false;
            int tile = data.TileAt(lat, lon);
            if (tile < 0) return false;
            var nets = Networks(tile);
            double best = double.MaxValue;
            for (int i = 0; i < nets.Length; i++)
            {
                var n = nets[i];
                if (!n.Nearest(x, z, out var h)) continue;
                // (as RiverNetwork.Nearest chooses within one network: the water nearest beyond its half width)
                double score = h.Dist - h.HalfWidth;
                if (score >= best) continue;
                best = score;
                hit = h;
                net = n;
            }
            return net != null;
        }

        // per thread: the last tile asked about and its networks (a chunk's columns ask the same tile 1024 times)
        [ThreadStatic] static StreamLayer lastLayer;
        [ThreadStatic] static int lastTile;
        [ThreadStatic] static RiverNetwork[] lastNets;

        /// <summary>The networks a tile's columns look at: its home tiles' (Sources) that have streams.</summary>
        RiverNetwork[] Networks(int tile)
        {
            if (ReferenceEquals(lastLayer, this) && lastTile == tile) return lastNets;
            var list = Sources(tile);
            var nets = new List<RiverNetwork>(list.Length);
            bool kept = true;
            foreach (int h in list)
            {
                var n = Home(h, out bool k);
                kept &= k;
                if (n != null) nets.Add(n);
            }
            var arr = nets.ToArray();
            // (a network not kept is not remembered for the thread's next ask either)
            if (kept) { lastLayer = this; lastTile = tile; lastNets = arr; }
            return arr;
        }

        /// <summary>The home tiles a tile's columns look at: its own (when it has systems) and its visitors'.</summary>
        int[] Sources(int tile)
        {
            if (sources.TryGetValue(tile, out var s)) return s;
            var list = new List<int>();
            if (data.State(tile) == StreamData.HasData)
            {
                var t = data.Decode(tile);
                if (t.Systems.Count > 0) list.Add(tile);
                foreach (var (dx, dy) in t.Visitors)
                {
                    int h = data.Step(tile, dx, dy);
                    if (h >= 0 && !list.Contains(h)) list.Add(h);
                }
            }
            s = list.ToArray();
            return sources.GetOrAdd(tile, s);
        }

        /// <summary>(tests) A home tile's network as a column would have it built (null when the tile has no systems).</summary>
        internal RiverNetwork HomeNetwork(int tile) => Home(tile, out _);

        /// <summary>(tests) The home tiles a place's columns look at.</summary>
        internal int[] HomesAt(double lat, double lon)
        {
            int t = data.TileAt(lat, lon);
            return t < 0 ? Array.Empty<int>() : Sources(t);
        }

        /// <summary>(tests) A home tile's network built on this thread and not kept (on a thread taking the built-in heights: as
        /// the layer built it there before the Langholm fix).</summary>
        internal RiverNetwork BuildDetached(int tile) => Build(tile);

        /// <summary>A home tile's network, built at the first ask (null when the tile has no systems); kept: whether the layer
        /// keeps it (built on the land generation reads).</summary>
        RiverNetwork Home(int tile, out bool kept)
        {
            kept = false;
            int me = Environment.CurrentManagedThreadId;
            for (int round = 0; round < 8; round++)
            {
                if (!homes.TryGetValue(tile, out var lazy))
                {
                    // lacking a square at the last build: a thread that may not fetch builds again only once one has arrived
                    if (!FineElevation.FetchAllowed && lacking.TryGetValue(tile, out long seen) && seen == Arrivals()) return null;
                    lazy = homes.GetOrAdd(tile, new Lazy<Built>(() => BuildOn(tile), LazyThreadSafetyMode.ExecutionAndPublication));
                }
                Built b;
                try { b = lazy.Value; }
                catch
                {
                    // not kept: the next ask builds it again (a GLO-90 square that couldn't be had now)
                    homes.TryRemove(new KeyValuePair<int, Lazy<Built>>(tile, lazy));
                    throw;
                }
                if (b.OnGlo90)
                {
                    kept = true;
                    return b.Net;
                }
                // fitted to land that wasn't all GLO-90 as generation reads it: never kept, and no streams for the thread that
                // built it; another thread (chunk generation may fetch the square) builds its own
                homes.TryRemove(new KeyValuePair<int, Lazy<Built>>(tile, lazy));
                if (b.Thread == me) return null;
            }
            // (other threads that couldn't read the land kept getting in first) built here, used only if on GLO-90
            var own = BuildOn(tile);
            return own.OnGlo90 ? own.Net : null;
        }

        long Arrivals() => model.Fine?.Arrivals ?? 0;

        /// <summary>Build on this thread, noting whether every read took the land generation reads.</summary>
        Built BuildOn(int tile)
        {
            long arrived = Arrivals();
            int missed = FineElevation.MissedReads;
            var net = Build(tile);
            bool onGlo90 = FineElevation.MissedReads == missed;
            if (onGlo90) lacking.TryRemove(tile, out _);
            else lacking[tile] = arrived;
            return new Built { Net = net, OnGlo90 = onGlo90, Thread = Environment.CurrentManagedThreadId };
        }

        RiverNetwork Build(int tile)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var t = data.Decode(tile);
            if (t.Systems.Count == 0) return null;
            var reaches = new List<RiverReach>();
            var lands = new List<(int[] Land, List<(double T, int Top, double X, double Z)>[] Along)>();
            foreach (var sys in t.Systems)
            {
                // (EA-0200) the Earth tab's Smallest stream: the system's headwaters below it go
                if (SmallestQ > 1)
                {
                    sys.Reaches = KeepFrom(sys.Reaches, SmallestQ);
                    if (sys.Reaches.Count == 0) continue;
                }
                MergeCrossingInflows(sys);
                long f0 = System.Diagnostics.Stopwatch.GetTimestamp();
                lands.AddRange(FitToLand(sys));
                Interlocked.Add(ref fitTicks, System.Diagnostics.Stopwatch.GetTimestamp() - f0);
                if (sys.Reaches.Count == 0) continue;
                int first = reaches.Count;
                foreach (var re in sys.Reaches)
                {
                    if (re.Next >= 0) re.Next += first;
                    reaches.Add(re);
                }
                long j0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Join(sys, reaches, first);
                Interlocked.Add(ref joinTicks, System.Diagnostics.Stopwatch.GetTimestamp() - j0);
            }
            var net = new RiverNetwork(reaches, model.Geometry, model.RiverWaterTop, options);
            // the pools as laid out: where one stands more than PoolHold blocks above the land, its vertex comes down by that much
            // and the network is laid out again (the pool layout's lag on a steep stream is more than LagAllowance foresees)
            for (int round = 0; round < SettleRounds && LowerUnderPools(net, reaches, lands); round++)
            {
                Interlocked.Increment(ref resettled);
                net = new RiverNetwork(reaches, model.Geometry, model.RiverWaterTop, options);
            }
            Interlocked.Increment(ref built);
            Interlocked.Add(ref reachesBuilt, reaches.Count);
            Interlocked.Add(ref buildTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            return net;
        }

        /// <summary>
        /// (EA-0200, decision 0098) A system's reaches without those below <paramref name="minQ"/> m³/s (flow order kept, Next
        /// renumbered): a reach stays when its flow is at least minQ, or when a reach that stays flows into it, so every stream
        /// left reaches the system's outlet even where its flow drops downstream (a losing stream). Flow grows downstream, so what
        /// goes is headwaters. The reaches themselves are the same objects.
        /// </summary>
        internal static List<RiverReach> KeepFrom(List<RiverReach> reaches, double minQ)
        {
            int n = reaches.Count;
            var keep = new bool[n];
            var fed = new bool[n];
            for (int r = 0; r < n; r++)
            {
                keep[r] = reaches[r].Q >= minQ || fed[r];
                int nx = reaches[r].Next;
                if (keep[r] && nx > r && nx < n) fed[nx] = true;
            }
            var index = new int[n];
            var kept = new List<RiverReach>(n);
            for (int r = 0; r < n; r++)
            {
                index[r] = keep[r] ? kept.Count : -1;
                if (keep[r]) kept.Add(reaches[r]);
            }
            foreach (var re in kept)
                if (re.Next >= 0) re.Next = re.Next < n ? index[re.Next] : -1;
            return kept;
        }

        /// <summary>
        /// EA-0164: two inflows of one junction whose courses cross before it meet where they first cross (the first along the
        /// larger): both end there, and the larger's course on from there goes to the reach they flow into, ahead of its own
        /// vertices. The snap lays each stream on its own valley line; near a junction a tributary's could run over the other
        /// inflow's, an X and then two channels side by side down to the junction, the water stepping across the other's
        /// (Skjolden, 61.53° N 7.60° E). Not where they cross within 3 blocks of both their ends (the junction itself).
        /// </summary>
        void MergeCrossingInflows(StreamData.StreamSystem sys)
        {
            var rs = sys.Reaches;
            for (int round = 0; round < 4; round++)
            {
                bool any = false;
                for (int a = 0; a < rs.Count; a++)
                    for (int b = a + 1; b < rs.Count; b++)
                    {
                        int next = rs[a].Next;
                        if (next < 0 || rs[b].Next != next) continue;
                        bool aBig = rs[a].Q >= rs[b].Q;
                        var big = aBig ? rs[a] : rs[b];
                        var small = aBig ? rs[b] : rs[a];
                        if (!FirstCrossing(big, small, out int kb, out double tb, out int ks, out double ts, out double plat, out double plon)) continue;
                        var nx = rs[next];
                        // the larger's course on from the crossing, ahead of the next reach's (its last vertex is the junction, the
                        // next reach's first)
                        var lon = new List<float>(); var lat = new List<float>(); var lev = new List<short>(); var fix = new List<bool>();
                        lon.Add((float)plon); lat.Add((float)plat); lev.Add(LevelOn(big, kb, tb)); fix.Add(false);
                        for (int k = kb + 1; k < big.Lon.Length - 1; k++) { lon.Add(big.Lon[k]); lat.Add(big.Lat[k]); lev.Add(big.Level[k]); fix.Add(big.Fixed[k]); }
                        lon.AddRange(nx.Lon); lat.AddRange(nx.Lat); lev.AddRange(nx.Level); fix.AddRange(nx.Fixed);
                        nx.Lon = lon.ToArray(); nx.Lat = lat.ToArray(); nx.Level = lev.ToArray(); nx.Fixed = fix.ToArray();
                        nx.Width = null;
                        EndAt(big, kb, tb, plat, plon);
                        EndAt(small, ks, ts, plat, plon);
                        Interlocked.Increment(ref merged);
                        any = true;
                    }
                if (!any) break;
            }
        }

        /// <summary>A reach's level at piece k (from vertex k to k + 1), share t along it.</summary>
        static short LevelOn(RiverReach re, int k, double t) => (short)Math.Round(re.Level[k] + t * (re.Level[k + 1] - re.Level[k]));

        /// <summary>A reach cut to end at piece k's share t (a new last vertex there).</summary>
        static void EndAt(RiverReach re, int k, double t, double lat, double lon)
        {
            int n = k + 2;
            short level = LevelOn(re, k, t);
            var lo = new float[n]; var la = new float[n]; var lv = new short[n]; var fx = new bool[n];
            Array.Copy(re.Lon, lo, k + 1); Array.Copy(re.Lat, la, k + 1); Array.Copy(re.Level, lv, k + 1); Array.Copy(re.Fixed, fx, k + 1);
            lo[k + 1] = (float)lon; la[k + 1] = (float)lat; lv[k + 1] = level; fx[k + 1] = false;
            re.Lon = lo; re.Lat = la; re.Level = lv; re.Fixed = fx;
            re.Width = null;
        }

        /// <summary>
        /// Where two reaches' courses first cross, along the first (pieces kA and kB, shares tA and tB, and its place), not
        /// within 3 blocks of both their last vertices.
        /// </summary>
        bool FirstCrossing(RiverReach a, RiverReach b, out int kA, out double tA, out int kB, out double tB, out double lat, out double lon)
        {
            kA = kB = -1; tA = tB = 0; lat = lon = 0;
            if (a.Lon.Length < 2 || b.Lon.Length < 2) return false;
            Positions(a, out var ax, out var az);
            Positions(b, out var bx, out var bz);
            // b in a's frame across the ring's seam
            double shift = Wrapped(bx[0] - ax[0]) - (bx[0] - ax[0]);
            for (int i = 0; i < bx.Length; i++) bx[i] += shift;
            double best = double.MaxValue;
            for (int i = 0; i + 1 < ax.Length; i++)
                for (int j = 0; j + 1 < bx.Length; j++)
                {
                    double px = ax[i], pz = az[i], rx = ax[i + 1] - px, rz = az[i + 1] - pz;
                    double qx = bx[j], qz = bz[j], sx = bx[j + 1] - qx, sz = bz[j + 1] - qz;
                    double den = rx * sz - rz * sx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double t = ((qx - px) * sz - (qz - pz) * sx) / den, u = ((qx - px) * rz - (qz - pz) * rx) / den;
                    if (t < 0 || t > 1 || u < 0 || u > 1 || i + t >= best) continue;
                    double cx = px + t * rx, cz = pz + t * rz;
                    double ea = (ax[^1] - cx) * (ax[^1] - cx) + (az[^1] - cz) * (az[^1] - cz);
                    double eb = (bx[^1] - cx) * (bx[^1] - cx) + (bz[^1] - cz) * (bz[^1] - cz);
                    if (ea <= 9 && eb <= 9) continue;
                    best = i + t;
                    kA = i; tA = t; kB = j; tB = u;
                    var g = model.Geometry.ToGeo(model.Geometry.WrapX(cx), cz);
                    lat = g.Lat; lon = g.Lon;
                }
            return kA >= 0;
        }

        /// <summary>A reach's vertices in world blocks, continuous across the ring's seam.</summary>
        void Positions(RiverReach re, out double[] x, out double[] z)
        {
            var geo = model.Geometry;
            int n = re.Lon.Length;
            x = new double[n]; z = new double[n];
            for (int k = 0; k < n; k++)
            {
                geo.FromGeo(re.Lat[k], re.Lon[k], out x[k], out z[k]);
                if (k > 0) x[k] = x[k - 1] + Wrapped(x[k] - x[k - 1]);
            }
        }

        /// <summary>
        /// A system's levels fitted to the land as the world draws it without the streams (TerrainModel.MainColumn at each
        /// vertex), so a stream never stands on a dam and never runs through a ridge: the snapped courses are right in their
        /// valleys, but a stream's head can start in the next valley over and cross the ridge between (the Dolomites at
        /// 46.42° N 11.87° E: a head 477 m below the level its fit gave it, then a ridge 200 m above it).
        /// <list type="number">
        /// <item>No dams: every vertex's level at most the land's under it (the highest whole metre whose water top is the
        /// land's top), and never rising downstream (into a confluence, no higher than the lowest inflow's end); where the stream
        /// falls faster than a block in 7, lower by as much as its pools lag above their level on such a slope (LagAllowance):
        /// the first bench showed the Dolomites stream's pools on a wall of rock 2-5 blocks above the grass.</item>
        /// <item>No ridges: where the water would still be more than RidgeCutMetres below the land under it, the course crosses
        /// high ground the stream couldn't have cut: the reach starts after it (its part upstream of the ridge dropped, the
        /// reaches that flowed into that part ending where they are); a reach that is all ridge goes, and so its inflows end.</item>
        /// </list>
        /// Vertices in water the world draws (a lake, a river) are left as they are.
        /// </summary>
        List<(int[] Land, List<(double T, int Top, double X, double Z)>[] Along)> FitToLand(StreamData.StreamSystem sys)
        {
            var rs = sys.Reaches;
            int nr = rs.Count;
            // the land under each vertex, and along each piece between two vertices (sampled LandStep blocks apart, LandSide blocks
            // to either side of the line too)
            var land = new int[nr][];
            var along = new List<(double T, int Top, double X, double Z)>[nr][];
            for (int r = 0; r < nr; r++) LandAlong(rs[r], out land[r], out along[r]);
            var snapped = DebugLog != null ? rs.ConvertAll(x => (short[])x.Level.Clone()) : null;
            void Stage(string what)
            {
                if (DebugLog == null || double.IsNaN(DebugLat)) return;
                for (int r = 0; r < nr; r++)
                {
                    var re = rs[r];
                    int near = -1;
                    double best = 40.0 * 40.0;
                    for (int k = 0; k < re.Lon.Length; k++)
                    {
                        double dy = (re.Lat[k] - DebugLat) * Geo.MetresPerDegree, dx = (re.Lon[k] - DebugLon) * Geo.MetresPerDegree * Math.Cos(DebugLat * Math.PI / 180);
                        if (dx * dx + dy * dy < best) { best = dx * dx + dy * dy; near = k; }
                    }
                    if (near < 0) continue;
                    var sb = new System.Text.StringBuilder($"stage {what}: reach Q {re.Q:F2} vertex {near} of {re.Lon.Length}:");
                    for (int k = Math.Max(0, near - 6); k < Math.Min(re.Lon.Length, near + 7); k++)
                        sb.Append($" [{k} land {(land[r][k] == int.MaxValue ? double.NaN : model.Vertical.Elevation(land[r][k])):F0} lev {re.Level[k]}]");
                    DebugLog(sb.ToString());
                }
            }
            Stage("baked");
            // 0. (EA-0165) up to the land: the baked levels fall smoothly from a lake's outlet to the foot of the valley's step
            // below, under the flat ground before the step (Skjolden, 61.55° N 7.58° E: 60 m under the meadow 300 m from the
            // lake, a slot through it); each vertex is raised towards the land under it as far as the level upstream allows
            Raise(rs, land);
            Stage("raised");
            // 1. no dams: at the land under each vertex; then, where the stream falls fast, as far under it as its pools may lag;
            // then each piece's water line under the land along it (both its ends lowered by what it stands above the land
            // most), the levels falling downstream again after each round
            Cap(rs, land, null);
            // pieces whose water line stands above the land along them follow it: a vertex every RefineStep blocks, at that land
            // (a plateau stream holds its level to the cliff's edge and falls there, where a straight line from the plateau to the
            // foot would pull the plateau's level down by the cliff)
            for (int r = 0; r < nr; r++) Refine(rs[r], ref land[r], ref along[r]);
            Cap(rs, land, null);
            Stage("refined");
            var lag = LagAllowance(rs, land);
            Cap(rs, land, lag);
            Stage("lagged");
            for (int round = 0; round < 3; round++)
            {
                bool lowered = false;
                for (int r = 0; r < nr; r++) lowered |= UnderLandAlong(rs[r], along[r], lag[r]);
                if (!lowered) break;
                Cap(rs, land, lag);
                Stage("under land " + round);
            }
            // 2. no ridges: each reach starts after the last vertex where its water lies deeper than RidgeCutMetres under the land
            var start = new int[nr];
            var gone = new bool[nr];
            for (int r = 0; r < nr; r++)
            {
                var lv = rs[r].Level;
                int last = -1;
                for (int k = 0; k < lv.Length; k++)
                    if (land[r][k] != int.MaxValue && model.Vertical.Elevation(land[r][k]) - lv[k] > RidgeCutMetres) last = k;
                if (last < 0) continue;
                Interlocked.Increment(ref ridges);
                if (DebugLog != null)
                {
                    var sb = new System.Text.StringBuilder($"ridge: reach Q {rs[r].Q:F2} at {rs[r].Lat[last]:F4},{rs[r].Lon[last]:F4}, vertex {last} of {lv.Length}:");
                    for (int k = 0; k < lv.Length; k++)
                        sb.Append($" [{k} land {(land[r][k] == int.MaxValue ? double.NaN : model.Vertical.Elevation(land[r][k])):F0} lev {lv[k]} snap {(snapped[r].Length == lv.Length ? snapped[r][k].ToString() : "-")}]");
                    DebugLog(sb.ToString());
                }
                if (last >= lv.Length - 2) gone[r] = true;
                else start[r] = last + 1;
            }
            if (Array.IndexOf(gone, true) < 0 && Array.TrueForAll(start, s => s == 0))
            {
                var all = new List<(int[], List<(double T, int Top, double X, double Z)>[])>(nr);
                for (int r = 0; r < nr; r++) all.Add((land[r], along[r]));
                return all;
            }
            // the reaches that flowed into a cut or gone part end where they are
            var index = new int[nr];
            var kept = new List<RiverReach>(nr);
            for (int r = 0; r < nr; r++)
            {
                index[r] = gone[r] ? -1 : kept.Count;
                if (!gone[r]) kept.Add(rs[r]);
            }
            for (int r = 0; r < nr; r++)
            {
                if (gone[r]) continue;
                var re = rs[r];
                int nx = re.Next;
                re.Next = nx >= 0 && !gone[nx] && start[nx] == 0 ? index[nx] : -1;
                if (start[r] > 0)
                {
                    int s = start[r], n = re.Lon.Length - s;
                    re.Lon = re.Lon[s..]; re.Lat = re.Lat[s..]; re.Level = re.Level[s..]; re.Fixed = re.Fixed[s..];
                    if (re.Width != null) re.Width = re.Width[s..];
                    land[r] = land[r][s..];
                    along[r] = along[r][Math.Min(s, along[r].Length)..];
                }
            }
            // a system whose outlet reach went ends where its data now ends
            if (gone[nr - 1]) sys.EndKind = StreamData.EndFree;
            sys.Reaches = kept;
            var lands = new List<(int[], List<(double T, int Top, double X, double Z)>[])>(kept.Count);
            for (int r = 0; r < nr; r++) if (!gone[r]) lands.Add((land[r], along[r]));
            return lands;
        }

        /// <summary>
        /// A built network's pools against the land as drawn without the streams (each reach's vertices and samples from
        /// FitToLand; an outlet's vertices past them, added by Join, aren't looked at): a vertex whose own water, or the
        /// water of the samples nearer it than its neighbour, stands more than PoolHold blocks above the land (a ramp's
        /// flowing layer counting as a block) is lowered by that much, and the levels fall downstream again (a fixed vertex,
        /// a join, stays). Whether any was.
        /// </summary>
        bool LowerUnderPools(RiverNetwork net, List<RiverReach> reaches, List<(int[] Land, List<(double T, int Top, double X, double Z)>[] Along)> lands)
        {
            var geo = model.Geometry;
            bool any = false;
            int Over(int r, double x, double z, int top)
            {
                if (top == int.MaxValue || !net.Nearest(geo.WrapX(x), z, out var hit) || hit.Reach != r || hit.Dist > hit.HalfWidth) return 0;
                return hit.WaterTop + (hit.Ramp > 0 ? 1 : 0) - top - PoolHold;
            }
            for (int r = 0; r < reaches.Count && r < lands.Count; r++)
            {
                var re = reaches[r];
                var (land, along) = lands[r];
                int n = Math.Min(re.Lon.Length, land.Length);
                var need = new int[n];
                for (int k = 0; k < n; k++)
                {
                    geo.FromGeo(re.Lat[k], re.Lon[k], out double x, out double z);
                    need[k] = Math.Max(need[k], Over(r, x, z, land[k]));
                }
                for (int k = 0; k + 1 < n && k < along.Length; k++)
                {
                    if (along[k] == null) continue;
                    foreach (var (t, top, x, z) in along[k])
                    {
                        int j = t < 0.5 ? k : k + 1;
                        need[j] = Math.Max(need[j], Over(r, x, z, top));
                    }
                }
                for (int k = 0; k < n; k++)
                {
                    if (need[k] <= 0 || re.Fixed[k]) continue;
                    re.Level[k] = (short)Math.Min(re.Level[k], LevelAtMost(model.RiverWaterTop(re.Level[k]) - need[k]));
                    any = true;
                }
            }
            if (!any) return false;
            // falling downstream again (into a confluence, no higher than the lowest inflow's end)
            var inLevel = new int[reaches.Count];
            Array.Fill(inLevel, int.MaxValue);
            for (int r = 0; r < reaches.Count; r++)
            {
                var lv = reaches[r].Level;
                int prev = inLevel[r];
                for (int k = 0; k < lv.Length; k++)
                {
                    if (!reaches[r].Fixed[k] && lv[k] > prev) lv[k] = (short)prev;
                    prev = lv[k];
                }
                int nx = reaches[r].Next;
                if (nx >= 0 && lv.Length > 0) inLevel[nx] = Math.Min(inLevel[nx], lv[^1]);
            }
            return true;
        }

        /// <summary>
        /// A reach's land as drawn without the streams (int.MaxValue where the world draws water, the gutter): under each vertex,
        /// and along each piece between two vertices, the lowest of the line and LandSide blocks to either side of it every
        /// LandStep blocks (the land between vertices 9 blocks apart in a V-shaped valley, and the lower side of a course
        /// along a slope, count too).
        /// </summary>
        void LandAlong(RiverReach re, out int[] land, out List<(double T, int Top, double X, double Z)>[] along)
        {
            var geo = model.Geometry;
            int n = re.Lon.Length;
            land = new int[n];
            along = new List<(double, int, double, double)>[Math.Max(0, n - 1)];
            var x = new double[n];
            var z = new double[n];
            for (int k = 0; k < n; k++)
            {
                geo.FromGeo(re.Lat[k], re.Lon[k], out x[k], out z[k]);
                if (k > 0) x[k] = x[k - 1] + Wrapped(x[k] - x[k - 1]);
                land[k] = LandTop(x[k], z[k]);
            }
            for (int k = 0; k + 1 < n; k++)
            {
                var list = along[k] = new List<(double, int, double, double)>();
                double dx = x[k + 1] - x[k], dz = z[k + 1] - z[k], len = Math.Sqrt(dx * dx + dz * dz);
                if (len <= 0) continue;
                double ux = dx / len, uz = dz / len;
                int steps = Math.Max(1, (int)Math.Ceiling(len / LandStep));
                for (int t = 1; t < steps; t++)
                {
                    double px = x[k] + dx * t / steps, pz = z[k] + dz * t / steps;
                    int top = LandTop(px, pz);
                    if (LandSide > 0)
                        for (int side = -1; side <= 1; side += 2)
                            top = Math.Min(top, LandTop(px - uz * side * LandSide, pz + ux * side * LandSide));
                    if (top != int.MaxValue) list.Add(((double)t / steps, top, px, pz));
                }
            }
        }

        /// <summary>
        /// A reach's pieces with their water line (the level between their ends) above the land along them, less the lag room
        /// (the larger of their ends'): both ends of such a piece lowered by the most it stands above. Whether any was.
        /// </summary>
        bool UnderLandAlong(RiverReach re, List<(double T, int Top, double X, double Z)>[] along, int[] lag)
        {
            var lv = re.Level;
            bool any = false;
            for (int k = 0; k + 1 < lv.Length; k++)
            {
                if (along[k] == null) continue;
                int room = Math.Max(lag[k], lag[k + 1]);
                double worst = 0;
                foreach (var (t, top, _, _) in along[k])
                {
                    double level = lv[k] + t * (lv[k + 1] - lv[k]);
                    double over = level - LevelAtMost(top - room);
                    if (over > worst) worst = over;
                }
                if (worst <= 0) continue;
                int d = (int)Math.Ceiling(worst);
                lv[k] = (short)(lv[k] - d);
                lv[k + 1] = (short)(lv[k + 1] - d);
                any = true;
                Interlocked.Increment(ref lowered);
            }
            return any;
        }

        /// <summary>
        /// FitToLand: a reach's pieces whose water line (the level between their ends) stands above the land sampled along them,
        /// or runs more than HoldBlocks under it (EA-0165), get a vertex every RefineStep blocks of samples, so the course's
        /// levels follow the land; the pieces' samples are split among the new pieces. A new vertex's level: the land's where the
        /// line stands above it; elsewhere the land's as far as the piece's level so far allows (never rising downstream), never
        /// under the line. So a stream holds its level across flat ground and falls where the land falls: before, a piece from
        /// a lake's outlet to a vertex 900 m on below the valley's step ran straight, 11 blocks under the meadow (Skjolden,
        /// 61.55° N 7.58° E: a slot through flat ground).
        /// </summary>
        void Refine(RiverReach re, ref int[] land, ref List<(double T, int Top, double X, double Z)>[] along)
        {
            int n = re.Lon.Length;
            if (n < 2) return;
            var lon = new List<float>(n); var lat = new List<float>(n); var lev = new List<short>(n); var fix = new List<bool>(n);
            var lnd = new List<int>(n);
            var al = new List<List<(double, int, double, double)>>(n);
            bool changed = false;
            int every = Math.Max(1, (int)Math.Round(RefineStep / LandStep));
            for (int k = 0; k < n; k++)
            {
                lon.Add(re.Lon[k]); lat.Add(re.Lat[k]); lev.Add(re.Level[k]); fix.Add(re.Fixed[k]); lnd.Add(land[k]);
                if (k + 1 >= n) break;
                var samples = along[k] ?? new List<(double, int, double, double)>();
                bool off = false;
                foreach (var (t, top, _, _) in samples)
                {
                    double line = re.Level[k] + t * (re.Level[k + 1] - re.Level[k]);
                    if (line > LevelAtMost(top) || line < LevelAtMost(top - HoldBlocks)) { off = true; break; }
                }
                if (!off) { al.Add(samples); continue; }
                changed = true;
                var piece = new List<(double, int, double, double)>();
                double t0 = 0;
                double held = re.Level[k];
                for (int i = 0; i < samples.Count; i++)
                {
                    var (t, top, x, z) = samples[i];
                    if ((i + 1) % every != 0 || i == samples.Count - 1)
                    {
                        piece.Add(((t - t0) / Math.Max(1e-9, 1 - t0), top, x, z));
                        continue;
                    }
                    // a new vertex here: the samples so far are its piece's (their T rescaled to it)
                    double span = t - t0;
                    for (int j = 0; j < piece.Count; j++) piece[j] = (piece[j].Item1 * (1 - t0) / span, piece[j].Item2, piece[j].Item3, piece[j].Item4);
                    al.Add(piece);
                    piece = new List<(double, int, double, double)>();
                    var g = model.Geometry.ToGeo(model.Geometry.WrapX(x), z);
                    double level = re.Level[k] + t * (re.Level[k + 1] - re.Level[k]);
                    int at = LevelAtMost(top);
                    // above the land: down to it; under it: up to it as far as the level held so far, never under the line
                    double v = at < level ? at : Math.Max(level, Math.Min(held, at));
                    held = Math.Min(held, v);
                    lon.Add((float)g.Lon); lat.Add((float)g.Lat);
                    lev.Add((short)Math.Floor(v));
                    fix.Add(false); lnd.Add(top);
                    t0 = t;
                }
                al.Add(piece);
            }
            if (!changed) return;
            re.Lon = lon.ToArray(); re.Lat = lat.ToArray(); re.Level = lev.ToArray(); re.Fixed = fix.ToArray();
            land = lnd.ToArray();
            along = al.ToArray();
            Interlocked.Increment(ref refined);
        }

        /// <summary>The land's top at (x, z) as drawn without the streams, int.MaxValue where the world draws water there.</summary>
        int LandTop(double x, double z)
        {
            var c = model.MainColumn(model.Geometry.WrapX(x), z);
            return c.Water != TerrainModel.WaterNone || c.Gutter ? int.MaxValue : c.Top;
        }

        /// <summary>
        /// FitToLand's first pass (EA-0165): each vertex's level raised to the land's under it where that is higher (in water the
        /// world draws, to that water's level), as far as the level upstream allows (the vertex before it, or at a reach's first
        /// vertex the lowest of its inflows' ends): never rising downstream, never under the baked level.
        /// </summary>
        void Raise(List<RiverReach> rs, int[][] land)
        {
            int nr = rs.Count;
            var inLevel = new int[nr];
            Array.Fill(inLevel, int.MaxValue);
            for (int r = 0; r < nr; r++)
            {
                var lv = rs[r].Level;
                int prev = inLevel[r];
                for (int k = 0; k < lv.Length; k++)
                {
                    int at;
                    if (land[r][k] != int.MaxValue) at = LevelAtMost(land[r][k]);
                    else
                    {
                        // in water the world draws (a lake the stream leaves, passes through or ends in): that water's level (the
                        // bake's smoothing had a lake's outlet stream 35 m under the lake by its shore, Skjolden)
                        model.Geometry.FromGeo(rs[r].Lat[k], rs[r].Lon[k], out double x, out double z);
                        var c = model.MainColumn(model.Geometry.WrapX(x), z);
                        at = c.Water != TerrainModel.WaterNone && !c.Gutter ? LevelAtMost(c.WaterTop) : int.MinValue;
                    }
                    int up = Math.Min(prev, at);
                    if (up > lv[k] && up <= short.MaxValue) { lv[k] = (short)up; Interlocked.Increment(ref raised); }
                    prev = Math.Min(prev, lv[k]);
                }
                int nx = rs[r].Next;
                if (nx >= 0 && nx < nr && lv.Length > 0) inLevel[nx] = Math.Min(inLevel[nx], lv[^1]);
            }
        }

        /// <summary>
        /// FitToLand's no-dams pass: each vertex's level at most the land's under it less <paramref name="lag"/> blocks (null:
        /// none), and never rising downstream (into a confluence, no higher than the lowest inflow's end).
        /// </summary>
        void Cap(List<RiverReach> rs, int[][] land, int[][] lag)
        {
            int nr = rs.Count;
            var inLevel = new int[nr];
            Array.Fill(inLevel, int.MaxValue);
            for (int r = 0; r < nr; r++)
            {
                var lv = rs[r].Level;
                int prev = inLevel[r];
                for (int k = 0; k < lv.Length; k++)
                {
                    int cap = land[r][k] == int.MaxValue ? int.MaxValue : LevelAtMost(land[r][k] - (lag?[r][k] ?? 0));
                    int v = Math.Min(lv[k], Math.Min(cap, prev));
                    if (v < lv[k]) Interlocked.Increment(ref lowered);
                    lv[k] = (short)v;
                    prev = v;
                }
                int nx = rs[r].Next;
                if (nx >= 0 && lv.Length > 0) inLevel[nx] = Math.Min(inLevel[nx], lv[^1]);
            }
        }

        /// <summary>
        /// Per vertex, how far (blocks) a stream's pools may stand above its level where it falls fast: the river network's
        /// pool layout steps down a block at a time no closer than RiverNetwork.StepGap(lag) apart (7 blocks with the water at
        /// most a block above the valley), and a stream's takes a lag of more than RiverNetwork.StreamMaxLag at once as a fall,
        /// no sooner than ChuteSpacing after its last step. So on a valley falling a block in 7 or less the water stays within a
        /// block of it; steeper, it lags StreamMaxLag and what the valley falls in ChuteSpacing blocks (at most ChuteMaxDrop).
        /// Measured over StepWindow blocks downstream from each vertex (on into the reaches below), on the levels as capped at
        /// the land; 0 in water the world draws.
        /// </summary>
        int[][] LagAllowance(List<RiverReach> rs, int[][] land)
        {
            int nr = rs.Count;
            var top = new int[nr][];
            var s = new double[nr][];
            var a = new int[nr][];
            for (int r = 0; r < nr; r++)
            {
                var re = rs[r];
                int n = re.Lon.Length;
                top[r] = new int[n];
                s[r] = new double[n];
                a[r] = new int[n];
                double px = 0, pz = 0;
                for (int k = 0; k < n; k++)
                {
                    model.Geometry.FromGeo(re.Lat[k], re.Lon[k], out double x, out double z);
                    if (k > 0)
                    {
                        x = px + Wrapped(x - px);
                        s[r][k] = s[r][k - 1] + Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                    }
                    px = x; pz = z;
                    top[r][k] = model.RiverWaterTop(re.Level[k]);
                }
            }
            for (int r = 0; r < nr; r++)
            {
                for (int k = 0; k < top[r].Length; k++)
                {
                    if (land[r][k] == int.MaxValue) continue;
                    // StepWindow blocks downstream, on into the reaches below (a reach's last vertex is the next one's first)
                    int rr = r, j = k;
                    double run = 0;
                    while (run < StepWindow)
                    {
                        if (j + 1 < top[rr].Length) { run += s[rr][j + 1] - s[rr][j]; j++; continue; }
                        int nx = rs[rr].Next;
                        if (nx < 0 || nx >= nr || top[nx].Length < 2) break;
                        rr = nx; j = 0;
                    }
                    double slope = run > 0 ? (top[r][k] - top[rr][j]) / Math.Max(1, run) : 0;
                    a[r][k] = slope <= 1.0 / RiverNetwork.StepSpacing ? 0
                        : Math.Min(RiverNetwork.StreamMaxLag + (int)Math.Ceiling(RiverNetwork.ChuteSpacing * slope), RiverNetwork.ChuteMaxDrop);
                }
            }
            return a;
        }

        /// <summary>The highest whole-metre level whose river water top (TerrainModel.RiverWaterTop) is at most
        /// <paramref name="top"/>.</summary>
        int LevelAtMost(int top)
        {
            int lo = -1000, hi = 9000;
            if (model.RiverWaterTop(lo) > top) return lo;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (model.RiverWaterTop(mid) <= top) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>
        /// A system's outlet joined to the water the world draws there without the streams: its last vertex at that water's
        /// level (fixed), or, ending at a large river whose drawn course lies off it, a vertex added on that course's nearest
        /// point; levels below that water's raised to it. A free end (the data's end, a sink, the sea's shore on land) keeps
        /// its snapped levels.
        /// </summary>
        void Join(StreamData.StreamSystem sys, List<RiverReach> reaches, int first)
        {
            // (the system's outlet is its last reach; FitToLand may have left others ending at a ridge)
            int outlet = reaches.Count - 1;
            if (outlet < first || reaches[outlet].Next >= 0) return;
            var re = reaches[outlet];
            int n = re.Lon.Length;
            if (n == 0) return;
            int top = -1;
            // the stream ends where it first comes into the drawn water of its outlet's kind (a river's for a river, the sea's
            // for the sea; never a lake it passes through) along its last JoinScan vertices: the rest of its line, baked on
            // the river's course as it was, would run on beside the drawn water or hook back to it
            if (sys.EndKind != StreamData.EndFree) top = CutAtEntry(sys.EndKind, re, ref n);
            model.Geometry.FromGeo(re.Lat[n - 1], re.Lon[n - 1], out double x, out double z);
            if (top >= 0) { }
            else if (model.MainColumn(x, z) is var c && c.Water != TerrainModel.WaterNone && c.WaterTop >= 0) top = c.WaterTop;
            else if (sys.EndKind == StreamData.EndRiver && model.Rivers != null)
            {
                double within = JoinSearchMetres * model.Geometry.BlocksPerDegreeZ / Geo.MetresPerDegree;
                if (model.Rivers.Nearest(x, z, out var hit) || model.Rivers.NearestCourse(x, z, within, out hit))
                {
                    top = hit.WaterTop;
                    double dx = hit.CentreX - x, dz = hit.CentreZ - z;
                    if (dx * dx + dz * dz > 0.25)
                    {
                        // the stream's last vertices that lie past the course (each no nearer it than the one before) give
                        // way to the course's point, so the stream turns into the river instead of hooking back to it
                        double Away(int k)
                        {
                            model.Geometry.FromGeo(re.Lat[k], re.Lon[k], out double vx, out double vz);
                            vx = model.Geometry.WrapX(vx);
                            double ex = model.Geometry.WrapX(hit.CentreX) - vx, ez = hit.CentreZ - vz;
                            return ex * ex + ez * ez;
                        }
                        while (n > 2 && Away(n - 2) <= Away(n - 1)) n--;
                        var g = model.Geometry.ToGeo(hit.CentreX, hit.CentreZ);
                        short endLevel = re.Level[n - 1];
                        Array.Resize(ref re.Lon, n + 1); Array.Resize(ref re.Lat, n + 1);
                        Array.Resize(ref re.Level, n + 1); Array.Resize(ref re.Fixed, n + 1);
                        re.Lon[n] = (float)g.Lon; re.Lat[n] = (float)g.Lat; re.Level[n] = endLevel;
                        n++;
                    }
                }
            }
            if (top < 0) return;
            short level = LevelFor(top, re.Level[n - 1]);
            // never below the water it flows into: what lies lower is raised to it (the levels fall downstream, so this is
            // the system's last stretch)
            for (int r = first; r < reaches.Count; r++)
            {
                var lv = reaches[r].Level;
                for (int k = 0; k < lv.Length; k++) if (lv[k] < level) lv[k] = level;
            }
            re.Level[n - 1] = level;
            re.Fixed[n - 1] = true;
        }

        /// <summary>
        /// The water top where an outlet reach first comes into the drawn water of its outlet's kind (river water or the sea for a
        /// river, the sea for the sea), sampled a block apart along its last JoinScanMetres; the reach then ends there (its vertex
        /// count <paramref name="n"/> and arrays cut, the entry point its last vertex). -1 where it doesn't.
        /// </summary>
        int CutAtEntry(byte endKind, RiverReach re, ref int n)
        {
            var geo = model.Geometry;
            double scan = JoinScanMetres * geo.BlocksPerDegreeZ / Geo.MetresPerDegree, along = 0;
            int k0 = n - 1;
            // the first vertex of the stretch to look along: JoinScanMetres back from the end
            var px = new double[n];
            var pz = new double[n];
            for (int k = 0; k < n; k++)
            {
                geo.FromGeo(re.Lat[k], re.Lon[k], out px[k], out pz[k]);
                if (k > 0) px[k] = px[k - 1] + Wrapped(px[k] - px[k - 1]);
            }
            while (k0 > 0 && along < scan)
            {
                along += Math.Sqrt((px[k0] - px[k0 - 1]) * (px[k0] - px[k0 - 1]) + (pz[k0] - pz[k0 - 1]) * (pz[k0] - pz[k0 - 1]));
                k0--;
            }
            for (int k = k0; k < n - 1; k++)
            {
                double dx = px[k + 1] - px[k], dz = pz[k + 1] - pz[k], len = Math.Sqrt(dx * dx + dz * dz);
                int steps = Math.Max(1, (int)Math.Ceiling(len));
                for (int s = k == k0 ? 0 : 1; s <= steps; s++)
                {
                    double f = (double)s / steps, x = px[k] + f * dx, z = pz[k] + f * dz;
                    var w = model.MainColumn(geo.WrapX(x), z);
                    // (a stream bound for a river ends at the sea too where the world draws that river's end short of the
                    // stream's junction: river ends rules 2, EA-0160, cut 1,038 reaches at the sea and leave 283 out past it)
                    bool kind = endKind == StreamData.EndRiver ? w.River || w.Water == TerrainModel.WaterSea : w.Water == TerrainModel.WaterSea;
                    if (w.Water == TerrainModel.WaterNone || w.WaterTop < 0 || !kind) continue;
                    // the entry point is the reach's new end
                    var g = geo.ToGeo(geo.WrapX(x), z);
                    short lev = (short)Math.Round(re.Level[k] + f * (re.Level[k + 1] - re.Level[k]));
                    n = k + 2;
                    Array.Resize(ref re.Lon, n); Array.Resize(ref re.Lat, n); Array.Resize(ref re.Level, n); Array.Resize(ref re.Fixed, n);
                    re.Lon[n - 1] = (float)g.Lon; re.Lat[n - 1] = (float)g.Lat; re.Level[n - 1] = lev; re.Fixed[n - 1] = false;
                    return w.WaterTop;
                }
            }
            return -1;
        }

        /// <summary>A step east along the ring the short way round (across the seam on a ring that wraps).</summary>
        double Wrapped(double dx)
        {
            double ring = model.Geometry.RingLength;
            if (ring <= 0) return dx;
            dx %= ring;
            if (dx > ring / 2) dx -= ring;
            if (dx < -ring / 2) dx += ring;
            return dx;
        }

        /// <summary>A whole-metre level whose river water top (TerrainModel.RiverWaterTop) is <paramref name="top"/>, the
        /// nearest to <paramref name="near"/> (the lowest above it where none is exactly it).</summary>
        short LevelFor(int top, short near)
        {
            int lo = -1000, hi = 9000;
            // the lowest level whose water top is at least top
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (model.RiverWaterTop(mid) >= top) hi = mid; else lo = mid + 1;
            }
            int a = lo;
            if (model.RiverWaterTop(a) != top) return (short)a;
            // of the levels giving exactly top, the one nearest the snapped level
            int b = a;
            while (b + 1 <= 9000 && model.RiverWaterTop(b + 1) == top) b++;
            return (short)Math.Clamp(near, a, b);
        }
    }
}
