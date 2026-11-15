using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Springs rules 2, "streams" (decision 0063 rule 5): where springs rise, decided from the terrain model and the data
    /// alone, never from blocks of neighbouring chunks, so a chunk's springs don't depend on which chunks were made first.
    /// Two kinds, both one source block at the surface that vanilla's liquid rules then run downhill:
    /// - Stream heads (<see cref="StreamHeads"/>): the head of every perennial HydroRIVERS stream, moved onto the valley floor
    ///   near it within <see cref="SnapMetres"/> (the head is a 15" cell, about 460 m, and the model's valley floor is where
    ///   the water collects): the lowest ground counting each metre of distance as <see cref="SnapPenalty"/> m of height, so
    ///   a head steps down a valley's side but doesn't slide down the valley to the radius' rim. Rapid water where the ground down-valley is steep for the stream's discharge by the rivers' own
    ///   rapids rule (<see cref="RiverNetwork.RapidsByPower"/>), else still water.
    /// - Seeps (decision 0059's water table): where the table at a column stands above a lower neighbouring column's
    ///   ground, so it comes out of the slope's face. Tried at a few hashed columns per chunk, more where the climate leaves
    ///   more water over (<see cref="SeepTries"/>); none in arid climates or open water.
    /// </summary>
    public static class StreamSprings
    {
        /// <summary>How far (m) a head may move onto the valley floor, about one HydroSHEDS cell.</summary>
        public const double SnapMetres = 450;
        /// <summary>Height (m) each metre of distance from the head counts for in the snap: steeper than a stream's floor
        /// near its head (mostly under 10 %), gentler than the valley sides it steps down (mostly 20 % and more), so the
        /// spring moves across the valley onto its floor, not along it.</summary>
        public const double SnapPenalty = 0.15;
        /// <summary>The snap radius in blocks: SnapMetres at the world's scale, at least a block, at most MaxSnapBlocks (the
        /// finest Earth size is about 6 m per block).</summary>
        public static int SnapRadius(double metresPerBlock) => Math.Clamp((int)Math.Round(SnapMetres / metresPerBlock), 1, MaxSnapBlocks);
        public const int MaxSnapBlocks = 80;
        /// <summary>The steepness that decides rapid water is the real fall (m/m) over about this far down-valley (m), at
        /// least <see cref="SlopeMinBlocks"/> blocks.</summary>
        public const double SlopeMetres = 150;
        public const int SlopeMinBlocks = 3, SlopeMaxBlocks = 24;
        public static int SlopeRun(double metresPerBlock) => Math.Clamp((int)Math.Round(SlopeMetres / metresPerBlock), SlopeMinBlocks, SlopeMaxBlocks);

        /// <summary>Why a head gets no spring (from the model; the game adds structures and craters).</summary>
        public enum Skip : byte { None, Gutter, DrawnRiver, Water, NoLand, Frozen }
        public static readonly string[] SkipNames = { "placed", "outside the Earth", "a drawn river within the snap radius", "in or beside drawn water", "no land within the snap radius", "frozen ground" };

        /// <summary>Open water in a model column: sea, lake, salt lake, river water, the water mask's rivers.</summary>
        public static bool IsWater(in TerrainModel.Column c)
            => c.Water != TerrainModel.WaterNone || (c.River && !c.DryRiver) || c.RiverOutline == TerrainModel.RiverInOutline;

        /// <summary>Ground a spring can rise from: not water, not glacier ice, not outside the Earth.</summary>
        public static bool IsLand(in TerrainModel.Column c) => !c.Gutter && !IsWater(c) && c.IceBottom > c.Top;

        static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };
        /// <summary>The four directions (0 east, 1 west, 2 south, 3 north): x and z steps.</summary>
        public static int DirX(int d) => Dx[d];
        public static int DirZ(int d) => Dz[d];

        // ------------------------------------------------------------------ stream heads

        /// <summary>
        /// The heads whose cell centres lie in a block box, with their block positions in the box's lap of a wrapping
        /// world. Gutter parts of the box hold none.
        /// </summary>
        public static void HeadsNear(StreamHeads heads, EarthGeometry g, double x0, double z0, double x1, double z1, List<(StreamHeads.Head Head, double X, double Z)> into, List<StreamHeads.Head> scratch)
        {
            if (heads == null) return;
            double latN = (g.EquatorZ - z0) / g.BlocksPerDegreeZ, latS = (g.EquatorZ - z1) / g.BlocksPerDegreeZ;
            double span = g.RingLength - g.Gutter, half = g.Gutter / 2;
            double a = x0 - g.RingStart, b = x1 - g.RingStart, lap = 0;
            if (g.Wraps)
            {
                lap = Math.Floor(a / g.RingLength) * g.RingLength;
                a -= lap; b -= lap;
            }
            Piece(a, Math.Min(b, g.RingLength), lap);
            if (g.Wraps && b > g.RingLength) Piece(0, b - g.RingLength, lap + g.RingLength);

            void Piece(double pa, double pb, double offset)
            {
                if (pb < half || pa > g.RingLength - half || span <= 0) return;
                double lonW = g.SeamLon0 + Math.Clamp((pa - half) / span, 0, 1) * 360, lonE = g.SeamLon0 + Math.Clamp((pb - half) / span, 0, 1) * 360;
                scratch.Clear();
                heads.Find(latS, latN, lonW, lonE, scratch);
                foreach (var h in scratch)
                {
                    double x = g.RingStart + offset + half + (h.Lon - g.SeamLon0) / 360 * span, z = g.EquatorZ - h.Lat * g.BlocksPerDegreeZ;
                    if (x >= x0 && x < x1 && z >= z0 && z < z1) into.Add((h, x, z));
                }
            }
        }

        /// <summary>A head's spring as the model places it.</summary>
        public struct HeadPlan
        {
            public int HeadId;
            /// <summary>Q10 (m³/s) of the head.</summary>
            public double Q;
            public Skip Skip;
            /// <summary>The head's own column, and the spring's (on the valley floor within the snap radius: the lowest ground
            /// counting distance as SnapPenalty of height).</summary>
            public int RawX, RawZ, X, Z;
            /// <summary>The model's top block at the spring; the source's y; whether it is set into the ground with a lower
            /// neighbour on side Dir (else it sits on the ground, a block above Top).</summary>
            public int Top, Y, Dir;
            public bool Face;
            /// <summary>Real fall (m/m) down-valley over <see cref="SlopeRun"/> blocks, and rapid water.</summary>
            public double Slope;
            public bool Rapid;
            /// <summary>Blocks the spring moved from the head's cell centre.</summary>
            public double Moved;
        }

        /// <summary>
        /// The model's column of block (bx, bz) as the planners read it. Generation samples a block at its corner
        /// (ColumnStore: GetColumn(x, z) for block x, z), and placing a spring compares its planned top with the generated
        /// one: with TerrainModel.GenFixRules 1 the plans read the same column (CR-R1); older worlds keep their plans from
        /// the block's centre, half a block off, where slopes and banks dropped many springs as "ground".
        /// </summary>
        static TerrainModel.Column BlockColumn(TerrainModel model, int bx, int bz)
            => model.Rules.CornerColumns ? model.GetColumn(bx, bz) : model.GetColumn(bx + .5, bz + .5);

        /// <summary>
        /// Plans a head's spring from the model alone: the same answer in every chunk and at any time. The head is at block
        /// position (x, z); q its Q10; rapids false where the world turned rapids off (earthRiverRapids).
        /// </summary>
        public static HeadPlan PlanHead(TerrainModel model, int headId, double x, double z, double q, bool rapids)
        {
            var plan = new HeadPlan { HeadId = headId, Q = q, RawX = (int)Math.Floor(x), RawZ = (int)Math.Floor(z), Dir = -1 };
            double mpb = model.MetresPerBlock;
            int r = SnapRadius(mpb);
            var cache = new Dictionary<long, TerrainModel.Column>();
            TerrainModel.Column At(int bx, int bz)
            {
                long key = (long)bx << 32 | (uint)bz;
                if (!cache.TryGetValue(key, out var c)) cache[key] = c = BlockColumn(model, bx, bz);
                return c;
            }
            var raw = At(plan.RawX, plan.RawZ);
            if (raw.Gutter) { plan.Skip = Skip.Gutter; return plan; }
            // a river the world draws this close is the head's own stream or swallows it at once
            if (model.Rivers != null && model.Rivers.DistanceToWater(plan.RawX + .5, plan.RawZ + .5, r) <= r) { plan.Skip = Skip.DrawnRiver; return plan; }
            if (IsWater(raw)) { plan.Skip = Skip.Water; return plan; }
            // the valley floor near the head: the lowest land within the radius counting distance as SnapPenalty of height;
            // a lattice, then down to the best column near the lattice's best
            int step = Math.Max(1, r / 8);
            int bxBest = 0, bzBest = 0; double best = double.PositiveInfinity, bestD = 0;
            double Score(double elev, double d2) => elev + SnapPenalty * mpb * Math.Sqrt(d2);
            bool Better(double elev, double d2) { double s = Score(elev, d2); return s < best || (s == best && d2 < bestD); }
            for (int dz = -(r / step) * step; dz <= r; dz += step)
                for (int dx = -(r / step) * step; dx <= r; dx += step)
                {
                    double d2 = dx * dx + dz * dz;
                    if (d2 > (double)r * r) continue;
                    var c = At(plan.RawX + dx, plan.RawZ + dz);
                    if (!IsLand(c) || !Better(c.Elev, d2)) continue;
                    best = Score(c.Elev, d2); bestD = d2; bxBest = dx; bzBest = dz;
                }
            if (double.IsPositiveInfinity(best)) { plan.Skip = Skip.NoLand; return plan; }
            for (int guard = 0; guard < 4 * r; guard++)
            {
                int nx = bxBest, nz = bzBest; bool moved = false;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int tx = bxBest + dx, tz = bzBest + dz;
                        double d2 = tx * tx + tz * tz;
                        if (d2 > (double)r * r) continue;
                        var c = At(plan.RawX + tx, plan.RawZ + tz);
                        if (!IsLand(c) || !Better(c.Elev, d2)) continue;
                        best = Score(c.Elev, d2); bestD = d2; nx = tx; nz = tz; moved = true;
                    }
                if (!moved) break;
                bxBest = nx; bzBest = nz;
            }
            plan.X = plan.RawX + bxBest; plan.Z = plan.RawZ + bzBest;
            plan.Moved = Math.Sqrt(bxBest * bxBest + bzBest * bzBest);
            var s = At(plan.X, plan.Z);
            plan.Top = s.Top;
            // no spring in or right beside drawn water: its water would only be the lake's or river's edge
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (IsWater(At(plan.X + dx, plan.Z + dz))) { plan.Skip = Skip.Water; return plan; }
            if (model.FrozenAllYear(plan.X + .5, plan.Z + .5, s.Top)) { plan.Skip = Skip.Frozen; return plan; }
            // set into the ground where a neighbour is lower (the water comes out of that side), else on the ground
            int low = s.Top;
            for (int d = 0; d < 4; d++)
            {
                var n = At(plan.X + Dx[d], plan.Z + Dz[d]);
                if (IsLand(n) && n.Top < low) { low = n.Top; plan.Dir = d; }
            }
            plan.Face = plan.Dir >= 0;
            plan.Y = plan.Face ? s.Top : s.Top + 1;
            plan.Slope = FallDownValley(At, plan.X, plan.Z, SlopeRun(mpb), mpb);
            plan.Rapid = rapids && RiverNetwork.RapidsByPower(plan.Slope * 1000, q);
            return plan;
        }

        /// <summary>The real fall (m/m) from a column along the steepest way down over land, for up to <paramref name="run"/>
        /// blocks; 0 in a hollow.</summary>
        static double FallDownValley(Func<int, int, TerrainModel.Column> at, int x, int z, int run, double mpb)
        {
            var start = at(x, z);
            double elev = start.Elev, dist = 0;
            int cx = x, cz = z;
            for (int i = 0; i < run; i++)
            {
                int bx = 0, bz = 0; double drop = 0, bestRate = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var c = at(cx + dx, cz + dz);
                        if (c.Gutter) continue;
                        double d = dx != 0 && dz != 0 ? Math.Sqrt(2) : 1;
                        double e = IsWater(c) ? double.NaN : c.Elev;
                        if (double.IsNaN(e)) continue;
                        double rate = (elev - e) / d;
                        if (rate > bestRate) { bestRate = rate; bx = dx; bz = dz; drop = d; }
                    }
                if (bestRate <= 0) break;
                cx += bx; cz += bz; dist += drop;
                elev = at(cx, cz).Elev;
            }
            return dist > 0 ? Math.Max(0, start.Elev - elev) / (dist * mpb) : 0;
        }

        // ------------------------------------------------------------------ seeps (the water table at a slope)

        /// <summary>Tries per chunk where the water surplus is full (<see cref="WaterTable.SurplusFullMm"/> a year or more):
        /// each a hashed column of the chunk where the table may come out of a slope. Seeps, not a sieve: in the offline probe
        /// (decision 0063) a seep every 30 chunks in Latvia and every 50-200 in the Alps and Norway at 1:10 (0.05-0.3 per
        /// km²), every 3 chunks in Latvia at 1:100. The count scales with this.</summary>
        public const double SeepTriesFull = 4;
        /// <summary>No seeps under this aridity index (precipitation over potential evaporation): arid and hyper-arid
        /// climates (UNEP's classes).</summary>
        public const double SeepMinAridity = 0.2;
        /// <summary>Least distance (blocks) between two seeps of a chunk.</summary>
        public const int SeepSpacing = 8;

        public static ulong ChunkHash(long seed, int chunkX, int chunkZ) => WeatherNoise.Hash(seed ^ 0x5EE9_5EE9L, chunkX, chunkZ, 0x0063);

        /// <summary>The share of the full tries a climate gets: the yearly water surplus (Budyko, the water table's rules)
        /// over its full value; 0 in arid climates.</summary>
        public static double SeepShare(WaterTable rules, double precipMm, double aridity)
        {
            if (rules == null || double.IsNaN(aridity) || aridity < SeepMinAridity) return 0;
            return Math.Clamp(rules.Surplus(precipMm, aridity) / rules.SurplusFullMm, 0, 1);
        }

        /// <summary>How many columns a chunk tries: SeepTriesFull × share, the fraction by the chunk's hash.</summary>
        public static int SeepTries(double share, ulong chunkHash)
        {
            double n = SeepTriesFull * Math.Clamp(share, 0, 1);
            int whole = (int)Math.Floor(n);
            return whole + (WeatherNoise.Unit(chunkHash, 0) < n - whole ? 1 : 0);
        }

        /// <summary>The i-th try's column in the chunk (0..31 each way).</summary>
        public static void SeepColumn(ulong chunkHash, int i, out int lx, out int lz)
        {
            ulong h = WeatherNoise.Mix(chunkHash + (ulong)(i + 1) * 0x9E3779B97F4A7C15UL);
            lx = (int)(h & 31); lz = (int)((h >> 5) & 31);
        }

        /// <summary>
        /// Where the table at a land column comes out of its face: at y = min(table, top), towards the lowest land
        /// neighbour whose ground is below that y (the block there is open). nTop/nLand: the four neighbours (DirX/DirZ).
        /// False where the table stays inside the ground.
        /// </summary>
        public static bool SeepFace(int top, int tableY, int[] nTop, bool[] nLand, out int y, out int dir)
        {
            y = Math.Min(tableY, top);
            dir = -1;
            for (int d = 0; d < 4; d++)
                if (nLand[d] && nTop[d] < y && (dir < 0 || nTop[d] < nTop[dir])) dir = d;
            return dir >= 0;
        }

        /// <summary>The water table at a column: its block y and whether it is open water; false outside the Earth.</summary>
        public delegate bool TableAt(int x, int z, out int tableY, out bool openWater);

        /// <summary>Why a seep try found no seep (<see cref="PlanSeep"/>).</summary>
        public enum SeepResult : byte { Seep, NotLand, NoLowerNeighbour, NearWater, NoTable, TableInside, Frozen }

        /// <summary>
        /// Whether the water table comes out of a slope at column (x, z), from the model and the table alone: the column is
        /// land with a lower land neighbour, no open water within 2 blocks, and the table stands above that neighbour's
        /// ground (<see cref="SeepFace"/>), on ground that isn't frozen all year.
        /// </summary>
        public static SeepResult PlanSeep(TerrainModel model, int x, int z, TableAt table, out int top, out int y, out int dir)
        {
            y = 0; dir = -1;
            var c = BlockColumn(model, x, z);
            top = c.Top;
            if (!IsLand(c)) return SeepResult.NotLand;
            var nTop = new int[4];
            var nLand = new bool[4];
            bool lower = false;
            for (int d = 0; d < 4; d++)
            {
                var n = BlockColumn(model, x + Dx[d], z + Dz[d]);
                nTop[d] = n.Top; nLand[d] = IsLand(n);
                if (nLand[d] && n.Top < c.Top) lower = true;
            }
            if (!lower) return SeepResult.NoLowerNeighbour;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (IsWater(BlockColumn(model, x + dx, z + dz))) return SeepResult.NearWater;
            if (!table(x, z, out int tableY, out bool open) || open) return SeepResult.NoTable;
            if (!SeepFace(c.Top, tableY, nTop, nLand, out y, out dir)) return SeepResult.TableInside;
            if (model.FrozenAllYear(x + .5, z + .5, y)) return SeepResult.Frozen;
            return SeepResult.Seep;
        }
    }
}
