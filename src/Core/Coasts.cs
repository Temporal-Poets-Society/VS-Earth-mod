using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// coasts.bin (tools/bake_earth.py bake_coasts): the GSHHG full-resolution outlines of the big land (1000 km² and more,
    /// the land the 2' grid keeps; smaller islands are in islands.bin), and of the lakes in it that the grid has as sea,
    /// in 1° tiles. Each tile holds the edges within a margin of it and says whether its centre is land; tiles without
    /// edges are all land or all sea, and south of 60°S there is no data (Antarctica isn't in GSHHG's L1).
    /// </summary>
    public sealed class CoastData
    {
        public const int FileVersion = 1;
        public const byte NoData = 255, CentreLand = 1, HasEdges = 2;

        /// <summary>Vertex units per degree, tile size (degrees), tiles across and down, margin (units).</summary>
        public int Unit { get; private set; }
        public int TileDeg { get; private set; }
        public int TilesX { get; private set; }
        public int TilesY { get; private set; }
        public int Margin { get; private set; }
        byte[] file, states;
        int[] offset, length;
        /// <summary>Tiles whose edges come from elsewhere (SeaRefereeData), decoded, or null.</summary>
        Dictionary<int, (int[] Runs, int[] X, int[] Y)> replaced;

        /// <summary>The file as read (its hash tells which coasts.bin it is).</summary>
        internal byte[] RawFile => file;

        /// <summary>A copy with some tiles' state and edges replaced (runs as Decode gives them; null runs: no edges).</summary>
        internal CoastData WithTiles(IReadOnlyDictionary<int, (byte State, int[] Runs, int[] X, int[] Y)> tiles)
        {
            var d = (CoastData)MemberwiseClone();
            d.states = (byte[])states.Clone();
            d.replaced = replaced != null ? new Dictionary<int, (int[], int[], int[])>(replaced) : new Dictionary<int, (int[], int[], int[])>();
            foreach (var kv in tiles)
            {
                d.states[kv.Key] = kv.Value.State;
                if (kv.Value.Runs != null) d.replaced[kv.Key] = (kv.Value.Runs, kv.Value.X, kv.Value.Y);
                else d.replaced.Remove(kv.Key);
            }
            return d;
        }

        public static CoastData Read(Stream s)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMCO", 28, "coasts.bin");
            if (BitConverter.ToInt32(file, 4) != FileVersion) throw new InvalidDataException("coasts.bin: version mismatch");
            var d = new CoastData
            {
                file = file, Unit = BitConverter.ToInt32(file, 8), TileDeg = BitConverter.ToInt32(file, 12),
                TilesX = BitConverter.ToInt32(file, 16), TilesY = BitConverter.ToInt32(file, 20), Margin = BitConverter.ToInt32(file, 24),
            };
            int n = d.TilesX * d.TilesY;
            if (d.TilesX * d.TileDeg != 360 || d.TilesY * d.TileDeg != 180) throw new InvalidDataException("coasts.bin: layout mismatch");
            EarthBinary.InFile(file, 28, n, "coasts.bin: the tile states");
            d.states = new byte[n];
            Buffer.BlockCopy(file, 28, d.states, 0, n);
            d.offset = new int[n];
            d.length = new int[n];
            int o = 28 + n;
            for (int t = 0; t < n; t++)
            {
                if (d.states[t] == NoData || (d.states[t] & HasEdges) == 0) continue;
                EarthBinary.InFile(file, o, 8, "coasts.bin: the tile index");
                d.offset[t] = BitConverter.ToInt32(file, o);
                d.length[t] = BitConverter.ToInt32(file, o + 4);
                o += 8;
            }
            // every tile's edges after the index and within the file, so a damaged one fails here and not on a worldgen thread
            for (int t = 0; t < n; t++)
                if (d.length[t] != 0 && (d.offset[t] < o || d.length[t] < 0 || (long)d.offset[t] + d.length[t] > file.Length))
                    throw new InvalidDataException("coasts.bin: bad tile index");
            return d;
        }

        /// <summary>The tile holding a point (row 0 at 90°S), or -1 outside the grid, and the point's offset (degrees) from the
        /// tile's south-west corner, never negative (from the same sums as the tile, so a point a rounding error west of a
        /// tile edge is at offset 0 of the tile east of it, not 360° away).</summary>
        public int TileAt(double lat, double lon, out double dLat, out double dLon)
        {
            double u = Geo.Wrap180(lon) + 180, v = lat + 90;
            int c = Math.Clamp((int)Math.Floor(u / TileDeg), 0, TilesX - 1), r = (int)Math.Floor(v / TileDeg);
            dLon = Math.Max(0, u - c * TileDeg);
            dLat = Math.Max(0, v - r * TileDeg);
            return r < 0 || r >= TilesY ? -1 : r * TilesX + c;
        }

        public byte State(int tile) => tile < 0 ? NoData : states[tile];

        /// <summary>The runs of a tile with edges: vertex count per run, and the vertices (units from the tile's south-west corner).</summary>
        public void Decode(int tile, out int[] runs, out int[] vx, out int[] vy)
        {
            if (replaced != null && replaced.TryGetValue(tile, out var r))
            {
                runs = (int[])r.Runs.Clone();
                vx = (int[])r.X.Clone();
                vy = (int[])r.Y.Clone();
                return;
            }
            byte[] b = EarthBinary.UnbrotliAll(file, offset[tile], length[tile], "coasts.bin: tile " + tile);
            int o = 0;
            int n = (int)EarthBinary.ReadVarint(b, ref o);
            runs = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++) total += runs[i] = (int)EarthBinary.ReadVarint(b, ref o);
            vx = new int[total];
            vy = new int[total];
            long x = 0, y = 0;
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                x = 0; y = 0;
                for (int j = 0; j < runs[i]; j++, k++)
                {
                    x += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                    y += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                    vx[k] = (int)x;
                    vy[k] = (int)y;
                }
            }
        }
    }

    /// <summary>
    /// The big land's coasts of one world from their outlines (EarthParams.Coasts): for any point, whether it is land and
    /// how far (blocks, up to Reach) it is from the coast. Works in the world's own block metric, where x follows longitude
    /// (BlocksPerDegreeX at every latitude) and z latitude. A tile is decoded the first time something comes into it and
    /// kept in a small cache: its edges in blocks, a grid of cells of at least Reach blocks with the edges touching each,
    /// and whether each cell's centre is land (by counting edge crossings from the tile's centre, whose land/sea the file
    /// gives). A point is land when the cell centre is and an even number of edges lie between them. Thread-safe.
    /// </summary>
    public sealed class CoastLayer
    {
        /// <summary>How far (blocks) distances are measured: the width of the coastal profile (about 640 m, 8..64 blocks).</summary>
        public readonly double Reach;
        /// <summary>Narrow sea channels the grid keeps (fjords, sounds) stay at least this wide (blocks).</summary>
        public readonly double ChannelBlocks;
        /// <summary>Size (blocks) of a 2' grid cell in this world.</summary>
        public readonly double GridCellBlocks;
        public readonly CoastData Data;
        readonly double bpdX, bpdZ;
        /// <summary>Decoded tiles kept.</summary>
        public int MaxCachedTiles { get => tiles.Max; set => tiles.Max = value; }
        readonly TileCache<Tile> tiles;

        sealed class Tile
        {
            public float[] X, Z;
            /// <summary>Edge i runs from vertex EdgeA[i] to EdgeA[i] + 1.</summary>
            public int[] EdgeA;
            public int Nx, Nz;
            public double Cell;
            public int[] CellStart, CellEdges;
            public bool[] CellLand;
        }

        /// <summary>The coasts for a world with this geometry; Reach from its scale (ReachFor).</summary>
        public CoastLayer(CoastData data, EarthGeometry geo, double channelBlocks)
        {
            Data = data;
            bpdX = geo.BlocksPerDegreeX;
            bpdZ = geo.BlocksPerDegreeZ;
            Reach = ReachFor(geo);
            ChannelBlocks = channelBlocks;
            GridCellBlocks = CellBlocks(geo);
            tiles = new TileCache<Tile>(Build, 96);
        }

        /// <summary>The coastal profile's width: about 640 m in blocks, 8..64.</summary>
        public static double ReachFor(EarthGeometry geo) => Math.Clamp(Math.Round(640 / (Geo.MetresPerDegree / geo.BlocksPerDegreeZ)), 8, 64);

        /// <summary>Size (blocks) of one 2' grid cell in a world: its outlines are worth having when this is well above a few blocks.</summary>
        public static double CellBlocks(EarthGeometry geo) => Math.Min(geo.BlocksPerDegreeX, geo.BlocksPerDegreeZ) / 30;

        /// <summary>Distance (blocks) to the coast, positive on land and negative at sea, capped at ±Reach; NaN where the outlines
        /// have no data (south of 60°S).</summary>
        public double SignedDistance(double lat, double lon)
        {
            int t = Data.TileAt(lat, lon, out double dLat, out double dLon);
            byte st = Data.State(t);
            if (st == CoastData.NoData) return double.NaN;
            if ((st & CoastData.HasEdges) == 0) return (st & CoastData.CentreLand) != 0 ? Reach : -Reach;
            var tile = GetTile(t);
            double px = dLon * bpdX, pz = dLat * bpdZ;
            bool land = Inside(tile, px, pz);
            double d = Distance(tile, px, pz, Reach);
            return land ? d : -d;
        }

        Tile GetTile(int key) => tiles.Get(key);

        Tile Build(int key)
        {
            Data.Decode(key, out int[] runs, out int[] vx, out int[] vy);
            int unit = Data.Unit;
            long lon0u = (long)(key % Data.TilesX) * Data.TileDeg * unit - 180L * unit, lat0u = (long)(key / Data.TilesX) * Data.TileDeg * unit - 90L * unit;
            var xs = new List<float>(4 * vx.Length);
            var zs = new List<float>(4 * vx.Length);
            var edges = new List<int>(4 * vx.Length);
            var lon = new List<double>();
            var lat = new List<double>();
            for (int r = 0, v = 0; r < runs.Length; v += runs[r++])
            {
                lon.Clear();
                lat.Clear();
                for (int j = 0; j < runs[r]; j++)
                {
                    lon.Add((double)(lon0u + vx[v + j]) / unit);
                    lat.Add((double)(lat0u + vy[v + j]) / unit);
                }
                Smooth(lon, lat);
                int first = xs.Count;
                for (int j = 0; j < lon.Count; j++)
                {
                    xs.Add((float)((lon[j] - lon0u / (double)unit) * bpdX));
                    zs.Add((float)((lat[j] - lat0u / (double)unit) * bpdZ));
                    if (j > 0) edges.Add(first + j - 1);
                }
            }
            var t = new Tile { X = xs.ToArray(), Z = zs.ToArray(), EdgeA = edges.ToArray() };

            double w = Data.TileDeg * bpdX, h = Data.TileDeg * bpdZ;
            t.Cell = Math.Max(Reach, 16);
            t.Nx = Math.Max(1, (int)Math.Ceiling(w / t.Cell));
            t.Nz = Math.Max(1, (int)Math.Ceiling(h / t.Cell));
            int nc = t.Nx * t.Nz;
            var count = new int[nc + 1];
            void Span(int e, out int c0, out int c1, out int r0, out int r1)
            {
                int a = t.EdgeA[e];
                c0 = Math.Clamp((int)Math.Floor(Math.Min(t.X[a], t.X[a + 1]) / t.Cell), 0, t.Nx - 1);
                c1 = Math.Clamp((int)Math.Floor(Math.Max(t.X[a], t.X[a + 1]) / t.Cell), 0, t.Nx - 1);
                r0 = Math.Clamp((int)Math.Floor(Math.Min(t.Z[a], t.Z[a + 1]) / t.Cell), 0, t.Nz - 1);
                r1 = Math.Clamp((int)Math.Floor(Math.Max(t.Z[a], t.Z[a + 1]) / t.Cell), 0, t.Nz - 1);
            }
            for (int e = 0; e < t.EdgeA.Length; e++)
            {
                Span(e, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) count[r * t.Nx + c + 1]++;
            }
            for (int i = 0; i < nc; i++) count[i + 1] += count[i];
            t.CellStart = (int[])count.Clone();
            t.CellEdges = new int[count[nc]];
            for (int e = 0; e < t.EdgeA.Length; e++)
            {
                Span(e, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t.CellEdges[count[r * t.Nx + c]++] = e;
            }

            // land at the cell centres: from the tile's centre up or down its middle to each row of centres, then along the row
            bool centre = (Data.State(key) & CoastData.CentreLand) != 0;
            double xc = 0.5 * w, zc = 0.5 * h;
            var up = new List<double>();
            var rows = new List<double>[t.Nz];
            for (int r = 0; r < t.Nz; r++) rows[r] = new List<double>();
            foreach (int a in t.EdgeA)
            {
                double x0 = t.X[a], z0 = t.Z[a], x1 = t.X[a + 1], z1 = t.Z[a + 1];
                if ((x0 > xc) != (x1 > xc)) up.Add(z0 + (xc - x0) / (x1 - x0) * (z1 - z0));
                int ra = Math.Max(0, (int)Math.Floor(Math.Min(z0, z1) / t.Cell - 0.5)), rb = Math.Min(t.Nz - 1, (int)Math.Ceiling(Math.Max(z0, z1) / t.Cell));
                for (int r = ra; r <= rb; r++)
                {
                    double zr = (r + 0.5) * t.Cell;
                    if ((z0 > zr) != (z1 > zr)) rows[r].Add(x0 + (zr - z0) / (z1 - z0) * (x1 - x0));
                }
            }
            t.CellLand = new bool[nc];
            for (int r = 0; r < t.Nz; r++)
            {
                double zr = (r + 0.5) * t.Cell;
                bool rowLand = centre;
                foreach (double z in up) if (z > Math.Min(zc, zr) && z < Math.Max(zc, zr)) rowLand = !rowLand;
                var cross = rows[r];
                cross.Sort();
                for (int c = 0; c < t.Nx; c++)
                {
                    double xr = (c + 0.5) * t.Cell;
                    bool land = rowLand;
                    foreach (double x in cross) if (x > Math.Min(xc, xr) && x < Math.Max(xc, xr)) land = !land;
                    t.CellLand[r * t.Nx + c] = land;
                }
            }
            return t;
        }

        /// <summary>The most a corner is cut per smoothing pass (degrees, ~170 m north-south), and how many passes.</summary>
        public const double SmoothCut = 0.0015;
        public const int SmoothPasses = 2;

        /// <summary>
        /// Rounds off a run of the outline (degrees), exactly as tools/bake_earth.py smooth_ring does with whole rings (the
        /// tile centres' land/sea in the file is for this shape). GSHHG's full-resolution shorelines are often traced from a
        /// raster: straight runs of ~550 m with 45° cuts (western Norway's fjords, Indonesia's mangrove coasts), staircases
        /// ~50 blocks a step with a 4 million block ring. Each pass drops repeated points, then puts each edge's points a
        /// quarter from its ends (at most SmoothCut; Chaikin's corner cutting); points on 180° stay (GSHHG's cut there). A
        /// run's two ends stay where they are: they lie well outside the tile (beyond its margin), where the whole ring's
        /// corners there would have been cut.
        /// </summary>
        static void Smooth(List<double> lon, List<double> lat)
        {
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                int n = 0;
                for (int i = 0; i < lon.Count; i++)
                {
                    if (i < lon.Count - 1 && lon[i] == lon[i + 1] && lat[i] == lat[i + 1]) continue;
                    lon[n] = lon[i];
                    lat[n] = lat[i];
                    n++;
                }
                lon.RemoveRange(n, lon.Count - n);
                lat.RemoveRange(n, lat.Count - n);
                if (n < 2) return;
                var nl = new List<double>(2 * n + 2) { lon[0] };
                var nz = new List<double>(2 * n + 2) { lat[0] };
                for (int i = 0; i < n - 1; i++)
                {
                    double ax = lon[i], az = lat[i], bx = lon[i + 1], bz = lat[i + 1];
                    double k = Math.Cos(az * Math.PI / 180);
                    double len = Math.Sqrt((bx - ax) * k * ((bx - ax) * k) + (bz - az) * (bz - az));
                    double f = len > 0 ? Math.Min(0.25, SmoothCut / len) : 0.25;
                    if (OnCut(ax) || OnCut(bx)) f = 0;
                    nl.Add(ax + f * (bx - ax)); nz.Add(az + f * (bz - az));
                    nl.Add(bx - f * (bx - ax)); nz.Add(bz - f * (bz - az));
                }
                nl.Add(lon[n - 1]);
                nz.Add(lat[n - 1]);
                lon.Clear(); lon.AddRange(nl);
                lat.Clear(); lat.AddRange(nz);
            }
        }

        /// <summary>On 180° (also as a copy shifted by 360° into a tile on the other side).</summary>
        static bool OnCut(double lon) => Math.Abs(Math.Abs(lon) - 180) < 1e-9 || Math.Abs(Math.Abs(lon) - 540) < 1e-9;

        static bool Inside(Tile t, double px, double pz)
        {
            int c = Math.Clamp((int)Math.Floor(px / t.Cell), 0, t.Nx - 1), r = Math.Clamp((int)Math.Floor(pz / t.Cell), 0, t.Nz - 1);
            int cell = r * t.Nx + c;
            bool land = t.CellLand[cell];
            double xr = (c + 0.5) * t.Cell, zr = (r + 0.5) * t.Cell;
            // along the row of the cell's centre to the point's x, then up or down to the point
            for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1]; k++)
            {
                int a = t.EdgeA[t.CellEdges[k]];
                double x0 = t.X[a], z0 = t.Z[a], x1 = t.X[a + 1], z1 = t.Z[a + 1];
                if ((z0 > zr) != (z1 > zr))
                {
                    double x = x0 + (zr - z0) / (z1 - z0) * (x1 - x0);
                    if (x > Math.Min(xr, px) && x < Math.Max(xr, px)) land = !land;
                }
                if ((x0 > px) != (x1 > px))
                {
                    double z = z0 + (px - x0) / (x1 - x0) * (z1 - z0);
                    if (z > Math.Min(zr, pz) && z < Math.Max(zr, pz)) land = !land;
                }
            }
            return land;
        }

        static double Distance(Tile t, double px, double pz, double cap)
        {
            int c0 = Math.Clamp((int)Math.Floor((px - cap) / t.Cell), 0, t.Nx - 1), c1 = Math.Clamp((int)Math.Floor((px + cap) / t.Cell), 0, t.Nx - 1);
            int r0 = Math.Clamp((int)Math.Floor((pz - cap) / t.Cell), 0, t.Nz - 1), r1 = Math.Clamp((int)Math.Floor((pz + cap) / t.Cell), 0, t.Nz - 1);
            double best = cap * cap;
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    int cell = r * t.Nx + c;
                    for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1]; k++)
                    {
                        int a = t.EdgeA[t.CellEdges[k]];
                        double ax = t.X[a], az = t.Z[a], bx = t.X[a + 1], bz = t.Z[a + 1];
                        if (Math.Min(ax, bx) - px > cap || px - Math.Max(ax, bx) > cap || Math.Min(az, bz) - pz > cap || pz - Math.Max(az, bz) > cap) continue;
                        double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
                        double u = len > 0 ? Math.Clamp(((px - ax) * dx + (pz - az) * dz) / len, 0, 1) : 0;
                        double ex = ax + u * dx - px, ez = az + u * dz - pz, d2 = ex * ex + ez * ez;
                        if (d2 < best) best = d2;
                    }
                }
            }
            return Math.Sqrt(best);
        }
    }
}
