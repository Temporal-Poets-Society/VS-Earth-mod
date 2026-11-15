using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// islands.bin (tools/bake_earth.py): the islands smaller than the grid's big land (1000 km²) as GSHHG full-resolution
    /// outlines, and the 2' grid cells that change when their land is taken out of the grid. In the grid those islands were
    /// ETOPO's 1' blobs (a 0.05 km² cay as a 15 km², 50 m hill) and ETOPO had false islands on reef rims besides (the Great
    /// Bahama Bank's southern rim, the Tongue of the Ocean); with the patches those are shallow reef, and the islands come
    /// back from their outlines where they are wide enough in blocks for the world's scale (IslandLayer).
    /// </summary>
    public sealed class IslandData
    {
        public const int FileVersion = 1;

        public sealed class Island
        {
            public float AreaKm2;
            /// <summary>Highest point (m): ETOPO's, at most 3 + 100·sqrt(area).</summary>
            public float Height;
            /// <summary>Twice the distance (km) from the coast to the innermost point.</summary>
            public float WidthKm;
            public double[] Lon, Lat;
            public double MinLon, MaxLon, MinLat, MaxLat;
        }

        public struct Patch
        {
            public int Row, Col;
            public short ElevCode, Level;
            public byte Flags;
        }

        public Island[] Islands { get; private set; }
        public Patch[] Patches { get; private set; }

        /// <summary>Island data from its parts (SeaRefereeData.ApplyTo).</summary>
        internal static IslandData Create(Island[] islands, Patch[] patches) => new IslandData { Islands = islands, Patches = patches };

        /// <summary>Grid patches as the baker writes them (islands.bin, lakes.bin): cell index deltas (varint), elevation codes
        /// (i16), levels (i16), flags (u8).</summary>
        internal static Patch[] ReadPatches(byte[] b, ref int o, int nPatch, int w)
        {
            var patches = new Patch[nPatch];
            long cell = 0;
            for (int i = 0; i < nPatch; i++)
            {
                cell += (long)EarthBinary.ReadVarint(b, ref o);
                patches[i].Row = (int)(cell / w);
                patches[i].Col = (int)(cell % w);
            }
            for (int i = 0; i < nPatch; i++) patches[i].ElevCode = BitConverter.ToInt16(b, o + 2 * i);
            o += 2 * nPatch;
            for (int i = 0; i < nPatch; i++) patches[i].Level = BitConverter.ToInt16(b, o + 2 * i);
            o += 2 * nPatch;
            for (int i = 0; i < nPatch; i++) patches[i].Flags = b[o + i];
            o += nPatch;
            return patches;
        }

        public static IslandData Read(Stream s, int gridWidth)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMIS", 20, "islands.bin");
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8);
            int nPatch = BitConverter.ToInt32(file, 12), nIsland = BitConverter.ToInt32(file, 16);
            if (version != FileVersion || w != gridWidth) throw new InvalidDataException("islands.bin: layout mismatch");
            byte[] b = EarthBinary.UnbrotliAll(file, 20, "islands.bin");
            int o = 0;
            var d = new IslandData { Patches = ReadPatches(b, ref o, nPatch, w), Islands = new Island[nIsland] };

            for (int i = 0; i < nIsland; i++) d.Islands[i] = new Island { AreaKm2 = BitConverter.ToSingle(b, o + 4 * i) };
            o += 4 * nIsland;
            for (int i = 0; i < nIsland; i++) d.Islands[i].Height = BitConverter.ToSingle(b, o + 4 * i);
            o += 4 * nIsland;
            for (int i = 0; i < nIsland; i++) d.Islands[i].WidthKm = BitConverter.ToSingle(b, o + 4 * i);
            o += 4 * nIsland;
            var count = new int[nIsland];
            for (int i = 0; i < nIsland; i++) count[i] = BitConverter.ToInt32(b, o + 4 * i);
            o += 4 * nIsland;
            long qx = 0, qy = 0;
            for (int i = 0; i < nIsland; i++)
            {
                var isl = d.Islands[i];
                int n = count[i];
                isl.Lon = new double[n];
                isl.Lat = new double[n];
                isl.MinLon = isl.MinLat = double.MaxValue;
                isl.MaxLon = isl.MaxLat = double.MinValue;
                for (int k = 0; k < n; k++)
                {
                    qx += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                    qy += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                    double lon = qx * 1e-5, lat = qy * 1e-5;
                    isl.Lon[k] = lon;
                    isl.Lat[k] = lat;
                    if (lon < isl.MinLon) isl.MinLon = lon;
                    if (lon > isl.MaxLon) isl.MaxLon = lon;
                    if (lat < isl.MinLat) isl.MinLat = lat;
                    if (lat > isl.MaxLat) isl.MaxLat = lat;
                }
            }
            return d;
        }
    }

    /// <summary>
    /// The islands of one world: those at least MinWidthBlocks wide at its scale (a 100 m cay is 10 blocks in a world with a
    /// 4 million block ring, half a block with a 200 000 block one), with their distance to the coast for any point.
    /// Works in the world's own block metric, where x follows longitude (BlocksPerDegreeX, the same at every latitude) and
    /// z latitude, so outlines keep the shape they get in the world. Thread-safe; an island's edge index is built the first
    /// time something comes near it.
    /// </summary>
    public sealed class IslandLayer
    {
        /// <summary>How far out (blocks) the sea floor rises to an island's shore.</summary>
        public const double ApronBlocks = 24;
        /// <summary>The island rules a world is frozen with (EarthParams.Islands). 1: the outlines as GSHHG has them (0.14.0).
        /// 2: their corners rounded off (Smooth). 3: islands in the sea stay whole where a river's centre line or a river
        /// exit channel crosses them (TerrainModel.Column.IslandOverSea; before, the channel cut a straight strip through).
        /// 4 (EA-0110, Fable pass FP-20): an island crossing 180° (GSHHG stores it as two halves cut there: Taveuni, Rabi)
        /// has no coast along the cut, so its profile runs on across the meridian instead of dropping to a sea-level beach
        /// in a notch along it.</summary>
        public const int RulesVersion = 4;
        /// <summary>From these rules the edges along 180° are no coast.</summary>
        public const int SeamlessMeridianRules = 4;
        /// <summary>Corner cutting as CoastLayer and tools/bake_earth.py smooth_ring do it for the big land: at most this far
        /// (degrees, ~170 m) back from a corner, this many passes.</summary>
        public const double SmoothCut = 0.0015;
        public const int SmoothPasses = 2;
        const double BucketDeg = 0.25;

        public readonly double MinWidthBlocks;
        public readonly int Rules;
        readonly double bpdX, bpdZ;
        readonly Shape[] shapes;
        readonly Dictionary<int, int[]> buckets = new Dictionary<int, int[]>();
        readonly int bucketsX = (int)Math.Round(360 / BucketDeg);

        public int Count => shapes.Length;
        public int Total { get; }

        sealed class Shape
        {
            public IslandData.Island Src;
            public double LonRef, LatRef;
            public double MinX, MaxX, MinZ, MaxZ;
            public double Height, ProfileBlocks;
            public Edges Edges;
        }

        sealed class Edges
        {
            public float[] X, Z;
            /// <summary>The edge from point i lies on 180° (both ends there): GSHHG's cut, not a coast. Null: none.</summary>
            public bool[] OnMeridian;
            public int[][] Strips;
            public double StripH;
            /// <summary>EA-0113: a grid of square cells over the edges (each edge in every cell its box touches), and for each
            /// cell how many rings of cells around it hold no edge; built with the edges. Used for points inside the island,
            /// whose search reaches half the island's width.</summary>
            public int[][] Cells;
            public int[] EmptyRings;
            public int CellsX, CellsZ;
            public double CellSize, GridX0, GridZ0;
        }

        public struct Hit
        {
            public bool Inside;
            /// <summary>Distance (blocks) to the coast, up to the island's profile width inside or ApronBlocks outside.</summary>
            public double Dist;
            /// <summary>Height (m) of the island's top, and the distance (blocks) from the coast over which it rises to it.</summary>
            public double Height, ProfileBlocks;
        }

        public IslandLayer(IslandData data, EarthGeometry geo, double minWidthBlocks, int rules = RulesVersion)
        {
            MinWidthBlocks = minWidthBlocks;
            Rules = rules;
            bpdX = geo.BlocksPerDegreeX;
            bpdZ = geo.BlocksPerDegreeZ;
            Total = data.Islands.Length;
            var list = new List<Shape>();
            var lists = new Dictionary<int, List<int>>();
            foreach (var isl in data.Islands)
            {
                double lat = 0.5 * (isl.MinLat + isl.MaxLat);
                double kmX = bpdX / (111.32 * Math.Max(Math.Cos(lat * Math.PI / 180), 0.02)), kmZ = bpdZ / 111.32;
                if (isl.WidthKm * Math.Min(kmX, kmZ) < minWidthBlocks) continue;
                var sh = new Shape
                {
                    Src = isl, LonRef = isl.MinLon, LatRef = isl.MinLat,
                    MinX = 0, MaxX = (isl.MaxLon - isl.MinLon) * bpdX, MinZ = 0, MaxZ = (isl.MaxLat - isl.MinLat) * bpdZ,
                    Height = isl.Height,
                    ProfileBlocks = Math.Max(1, 0.5 * isl.WidthKm * Math.Sqrt(kmX * kmZ)),
                };
                int id = list.Count;
                list.Add(sh);
                double padLon = ApronBlocks / bpdX, padLat = ApronBlocks / bpdZ;
                int r0 = (int)Math.Floor((isl.MinLat - padLat + 90) / BucketDeg), r1 = (int)Math.Floor((isl.MaxLat + padLat + 90) / BucketDeg);
                int c0 = (int)Math.Floor((isl.MinLon - padLon + 180) / BucketDeg), c1 = (int)Math.Floor((isl.MaxLon + padLon + 180) / BucketDeg);
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        int key = r * bucketsX + ((c % bucketsX) + bucketsX) % bucketsX;
                        if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<int>();
                        l.Add(id);
                    }
                }
            }
            shapes = list.ToArray();
            foreach (var kv in lists) buckets[kv.Key] = kv.Value.ToArray();
        }

        // EA-0113 (the Fable pass's FP-23): a column asks for the same point twice (the island profile, then the shore strip):
        // the last answer on this thread is kept
        // (Fable's re-review of c5b4564) by the layer's number, not the layer: a pool thread must not keep a closed world's
        // islands, their edges and cell grids alive (as ER-22 / EA-0091 did elsewhere)
        [ThreadStatic] static long lastLayer;
        static long layers;
        readonly long layerId = Interlocked.Increment(ref layers);
        [ThreadStatic] static long lastLat, lastLon;
        [ThreadStatic] static bool lastFound;
        [ThreadStatic] static Hit lastHit;

        /// <summary>The island at or nearest to a point (within ApronBlocks of its coast), if any.</summary>
        public bool Query(double lat, double lon, out Hit hit)
        {
            long bLat = BitConverter.DoubleToInt64Bits(lat), bLon = BitConverter.DoubleToInt64Bits(lon);
            if (lastLayer == layerId && lastLat == bLat && lastLon == bLon) { hit = lastHit; return lastFound; }
            bool found = QueryAt(lat, lon, out hit);
            lastLayer = layerId; lastLat = bLat; lastLon = bLon; lastHit = hit; lastFound = found;
            return found;
        }

        bool QueryAt(double lat, double lon, out Hit hit)
        {
            hit = default;
            int key = (int)Math.Floor((lat + 90) / BucketDeg) * bucketsX + (((int)Math.Floor((lon + 180) / BucketDeg) % bucketsX) + bucketsX) % bucketsX;
            if (!buckets.TryGetValue(key, out var ids)) return false;
            bool found = false;
            foreach (int id in ids)
            {
                var sh = shapes[id];
                double px = Geo.Wrap180(lon - sh.LonRef) * bpdX, pz = (lat - sh.LatRef) * bpdZ;
                if (px < sh.MinX - ApronBlocks || px > sh.MaxX + ApronBlocks || pz < sh.MinZ - ApronBlocks || pz > sh.MaxZ + ApronBlocks) continue;
                var e = sh.Edges ?? BuildEdges(sh);
                bool inside = px >= sh.MinX && px <= sh.MaxX && pz >= sh.MinZ && pz <= sh.MaxZ && Inside(e, px, pz);
                double cap = inside ? sh.ProfileBlocks : ApronBlocks;
                double d = inside ? DistanceInside(e, px, pz, cap, Rules >= SeamlessMeridianRules) : Distance(e, px, pz, cap, Rules >= SeamlessMeridianRules);
                if (!inside && d >= ApronBlocks) continue;
                if (inside || !found || (!hit.Inside && d < hit.Dist))
                {
                    hit = new Hit { Inside = inside, Dist = d, Height = sh.Height, ProfileBlocks = sh.ProfileBlocks };
                    found = true;
                    if (inside) return true;
                }
            }
            return found;
        }

        /// <summary>Is the point on one of the world's islands?</summary>
        public bool IsLand(double lat, double lon) => Query(lat, lon, out var hit) && hit.Inside;

        Edges BuildEdges(Shape sh)
        {
            var isl = sh.Src;
            double[] lon = isl.Lon, lat = isl.Lat;
            if (Rules >= 2) Smooth(ref lon, ref lat);
            int n = lon.Length;
            var e = new Edges { X = new float[n], Z = new float[n] };
            for (int i = 0; i < n; i++)
            {
                e.X[i] = (float)(Geo.Wrap180(lon[i] - sh.LonRef) * bpdX);
                e.Z[i] = (float)((lat[i] - sh.LatRef) * bpdZ);
            }
            for (int i = 0; i < n; i++)
            {
                if (!OnCut(lon[i]) || !OnCut(lon[(i + 1) % n])) continue;
                e.OnMeridian ??= new bool[n];
                e.OnMeridian[i] = true;
            }
            int k = Math.Clamp(n / 6, 1, 512);
            e.StripH = Math.Max(sh.MaxZ / k, 1e-6);
            var strips = new List<int>[k];
            for (int i = 0; i < k; i++) strips[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int s0 = Math.Clamp((int)(Math.Min(e.Z[i], e.Z[j]) / e.StripH), 0, k - 1);
                int s1 = Math.Clamp((int)(Math.Max(e.Z[i], e.Z[j]) / e.StripH), 0, k - 1);
                for (int s = s0; s <= s1; s++) strips[s].Add(i);
            }
            e.Strips = new int[k][];
            for (int i = 0; i < k; i++) e.Strips[i] = strips[i].ToArray();
            BuildCells(e);
            Interlocked.CompareExchange(ref sh.Edges, e, null);
            return sh.Edges;
        }

        /// <summary>
        /// Rounds off a closed ring (degrees, without its closing point) as tools/bake_earth.py smooth_ring does for the big
        /// land. Much of GSHHG's full resolution is traced from a raster: straight runs of ~550 m with 45° cuts, staircases
        /// ~50 blocks a step with a 4 million block ring (Čiovo off Trogir). Each pass drops repeated points, then puts each
        /// edge's points a quarter from its ends (at most SmoothCut; Chaikin's corner cutting); points on 180° stay. Below
        /// ~200 m per block the cuts are smaller than a block.
        /// </summary>
        static void Smooth(ref double[] lon, ref double[] lat)
        {
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                int m = lon.Length, n = 0;
                var pl = new double[m];
                var pz = new double[m];
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    if (j != i && lon[i] == lon[j] && lat[i] == lat[j]) continue;
                    pl[n] = lon[i];
                    pz[n] = lat[i];
                    n++;
                }
                if (n < 3) return;
                var nl = new double[2 * n];
                var nz = new double[2 * n];
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double ax = pl[i], az = pz[i], dx = Geo.Wrap180(pl[j] - ax), dz = pz[j] - az;
                    double k = Math.Cos(az * Math.PI / 180);
                    double len = Math.Sqrt(dx * k * (dx * k) + dz * dz);
                    double f = len > 0 ? Math.Min(0.25, SmoothCut / len) : 0.25;
                    if (OnCut(ax) || OnCut(pl[j])) f = 0;
                    nl[2 * i] = ax + f * dx; nz[2 * i] = az + f * dz;
                    // uncut: the far end exactly (it stays on 180° for the next pass)
                    nl[2 * i + 1] = f > 0 ? ax + (1 - f) * dx : pl[j]; nz[2 * i + 1] = f > 0 ? az + (1 - f) * dz : pz[j];
                }
                lon = nl;
                lat = nz;
            }
        }

        /// <summary>On 180° (GSHHG's cut there).</summary>
        static bool OnCut(double lon) => Math.Abs(Math.Abs(lon) - 180) < 1e-9;

        static bool Inside(Edges e, double px, double pz)
        {
            int k = e.Strips.Length, n = e.X.Length;
            var strip = e.Strips[Math.Clamp((int)(pz / e.StripH), 0, k - 1)];
            bool inside = false;
            foreach (int i in strip)
            {
                int j = (i + 1) % n;
                double zi = e.Z[i], zj = e.Z[j];
                if ((zi > pz) == (zj > pz)) continue;
                double x = e.X[i] + (pz - zi) / (zj - zi) * (e.X[j] - e.X[i]);
                if (px < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>EA-0113: the cell grid of <see cref="Edges.Cells"/>: at most 128 cells a side, none under 8 blocks.</summary>
        static void BuildCells(Edges e)
        {
            int n = e.X.Length;
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                x0 = Math.Min(x0, e.X[i]); x1 = Math.Max(x1, e.X[i]);
                z0 = Math.Min(z0, e.Z[i]); z1 = Math.Max(z1, e.Z[i]);
            }
            double size = Math.Max(8, Math.Max(x1 - x0, z1 - z0) / 128);
            int nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / size) + 1), nz = Math.Max(1, (int)Math.Ceiling((z1 - z0) / size) + 1);
            var lists = new List<int>[nx * nz];
            const double pad = 1e-3;   // an edge on a cell's border is in the cells on both sides
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int cx0 = Math.Clamp((int)Math.Floor((Math.Min(e.X[i], e.X[j]) - pad - x0) / size), 0, nx - 1);
                int cx1 = Math.Clamp((int)Math.Floor((Math.Max(e.X[i], e.X[j]) + pad - x0) / size), 0, nx - 1);
                int cz0 = Math.Clamp((int)Math.Floor((Math.Min(e.Z[i], e.Z[j]) - pad - z0) / size), 0, nz - 1);
                int cz1 = Math.Clamp((int)Math.Floor((Math.Max(e.Z[i], e.Z[j]) + pad - z0) / size), 0, nz - 1);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        (lists[cz * nx + cx] ??= new List<int>()).Add(i);
            }
            var cells = new int[nx * nz][];
            var rings = new int[nx * nz];
            const int far = int.MaxValue / 4;
            for (int c = 0; c < cells.Length; c++)
            {
                cells[c] = lists[c]?.ToArray() ?? Array.Empty<int>();
                rings[c] = cells[c].Length > 0 ? 0 : far;
            }
            // the chessboard distance to the nearest cell holding an edge, in two passes
            for (int cz = 0; cz < nz; cz++)
                for (int cx = 0; cx < nx; cx++)
                {
                    int c = cz * nx + cx, v = rings[c];
                    if (cx > 0) v = Math.Min(v, rings[c - 1] + 1);
                    if (cz > 0)
                    {
                        v = Math.Min(v, rings[c - nx] + 1);
                        if (cx > 0) v = Math.Min(v, rings[c - nx - 1] + 1);
                        if (cx < nx - 1) v = Math.Min(v, rings[c - nx + 1] + 1);
                    }
                    rings[c] = v;
                }
            for (int cz = nz - 1; cz >= 0; cz--)
                for (int cx = nx - 1; cx >= 0; cx--)
                {
                    int c = cz * nx + cx, v = rings[c];
                    if (cx < nx - 1) v = Math.Min(v, rings[c + 1] + 1);
                    if (cz < nz - 1)
                    {
                        v = Math.Min(v, rings[c + nx] + 1);
                        if (cx < nx - 1) v = Math.Min(v, rings[c + nx + 1] + 1);
                        if (cx > 0) v = Math.Min(v, rings[c + nx - 1] + 1);
                    }
                    rings[c] = v;
                }
            e.Cells = cells;
            e.EmptyRings = rings;
            e.CellsX = nx; e.CellsZ = nz;
            e.CellSize = size; e.GridX0 = x0; e.GridZ0 = z0;
        }

        /// <summary>
        /// EA-0113 (the Fable pass's FP-23): <see cref="Distance"/> for a point inside the island, whose cap is half the
        /// island's width: the edges are searched ring by ring of cells around the point's cell, from the first ring that holds
        /// any, until no edge farther out can be nearer than the best so far. Every edge nearer than the result is in a ring
        /// searched, each distance is worked out as Distance does and the minimum over them is the same number in any order,
        /// so the result is Distance's to the bit.
        /// </summary>
        static double DistanceInside(Edges e, double px, double pz, double cap, bool seamless)
        {
            double size = e.CellSize;
            double fx = (px - e.GridX0) / size, fz = (pz - e.GridZ0) / size;
            int ci = (int)Math.Floor(fx), cj = (int)Math.Floor(fz);
            if (ci < 0 || cj < 0 || ci >= e.CellsX || cj >= e.CellsZ) return Distance(e, px, pz, cap, seamless);
            var meridian = seamless ? e.OnMeridian : null;
            int n = e.X.Length;
            double best = cap * cap;
            int maxRing = Math.Max(Math.Max(ci, e.CellsX - 1 - ci), Math.Max(cj, e.CellsZ - 1 - cj));
            for (int r = e.EmptyRings[cj * e.CellsX + ci]; r <= maxRing; r++)
            {
                // a cell r rings out lies at least r - 1 cells from the point (a little less, against rounding)
                if (r >= 1)
                {
                    double lb = (r - 1) * size * 0.999;
                    if (lb * lb >= best) break;
                }
                for (int dz = -r; dz <= r; dz++)
                {
                    int cz = cj + dz;
                    if (cz < 0 || cz >= e.CellsZ) continue;
                    bool edgeRow = dz == -r || dz == r;
                    for (int dx = -r; dx <= r; dx += edgeRow ? 1 : Math.Max(1, 2 * r))
                    {
                        int cx = ci + dx;
                        if (cx < 0 || cx >= e.CellsX) continue;
                        foreach (int i in e.Cells[cz * e.CellsX + cx])
                        {
                            if (meridian != null && meridian[i]) continue;
                            int j = (i + 1) % n;
                            double ax = e.X[i], az = e.Z[i], bx = e.X[j], bz = e.Z[j];
                            if (Math.Min(ax, bx) - cap > px || Math.Max(ax, bx) + cap < px) continue;
                            double ddx = bx - ax, ddz = bz - az, len = ddx * ddx + ddz * ddz;
                            double t = len > 0 ? Math.Clamp(((px - ax) * ddx + (pz - az) * ddz) / len, 0, 1) : 0;
                            double ex = ax + t * ddx - px, ez = az + t * ddz - pz, d2 = ex * ex + ez * ez;
                            if (d2 < best) best = d2;
                        }
                    }
                }
            }
            return Math.Sqrt(best);
        }

        /// <summary>The distance to the coast within <paramref name="cap"/>; with <paramref name="seamless"/> the edges along
        /// 180° do not count (island rules 4).</summary>
        static double Distance(Edges e, double px, double pz, double cap, bool seamless)
        {
            var meridian = seamless ? e.OnMeridian : null;
            int k = e.Strips.Length, n = e.X.Length;
            int s0 = Math.Clamp((int)Math.Floor((pz - cap) / e.StripH), 0, k - 1), s1 = Math.Clamp((int)Math.Floor((pz + cap) / e.StripH), 0, k - 1);
            double best = cap * cap;
            for (int s = s0; s <= s1; s++)
            {
                foreach (int i in e.Strips[s])
                {
                    if (meridian != null && meridian[i]) continue;
                    int j = (i + 1) % n;
                    double ax = e.X[i], az = e.Z[i], bx = e.X[j], bz = e.Z[j];
                    if (Math.Min(ax, bx) - cap > px || Math.Max(ax, bx) + cap < px) continue;
                    double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
                    double t = len > 0 ? Math.Clamp(((px - ax) * dx + (pz - az) * dz) / len, 0, 1) : 0;
                    double ex = ax + t * dx - px, ez = az + t * dz - pz, d2 = ex * ex + ez * ez;
                    if (d2 < best) best = d2;
                }
            }
            return Math.Sqrt(best);
        }
    }
}
