using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace EarthMap.Core
{
    /// <summary>
    /// lakes-glo90-1.bin (tools/bake_lake_outlines.py; 0053 item 17, proposed): the lakes of the Copernicus GLO-90 water body
    /// mask north of 60°S as outlines, each with its level (the median GLO-90 height of its largest piece) and flags, in 1°
    /// tiles. Left out: lakes Earth's sea has, reservoirs, lakes on glaciers, and the lakes on Earth's own salt and terminal
    /// lakes (the grid keeps those at their 1318 extent). Each tile holds its own pieces of the lakes, cut at the tile's
    /// edges: rings are closed, and an edge with both ends on the same side of the tile is that cut, not a shore. About 58 MB,
    /// too large for the mod's zip: it comes with the GLO-90 heights (EarthElevationSystem).
    /// </summary>
    public sealed class LakeOutlineData
    {
        /// <summary>The newest version this build reads. 2 (decision 0069 rule 9): the same rules with Global Dam Watch's
        /// reservoirs left out too (tools/reservoir_list.py; the Seimare Dam's reservoir of 2013 among them). 3 (EA-0147):
        /// version 2's lakes, levels and flags, each tile outlined with its neighbours' facing mask cells
        /// (tools/recontour_lake_outlines.py), so a lake's pieces meet where its shore crosses a tile's edge: version 2 drew
        /// each tile alone, and its pieces stepped by a whole mask cell, or were notched by half of one, along every whole
        /// degree a lake crosses (straight seams on World Map's chart along 27°E at 1:10).</summary>
        public const int FileVersion = 3;
        /// <summary>Version 1's file name in the GLO-90 folder and on Earth's data release; a new version gets a new name.</summary>
        public const string FileName = "lakes-glo90-1.bin";
        /// <summary>The version 1 file this build reads (from the bake of 7 November 2026); any other is refused.</summary>
        public const long FileBytes = 58_106_553;
        public const string FileSha256 = "8f0075ac42d15589682333a500a20a63bfb7bdacfceec9ea52ad2d9979236384";
        /// <summary>Version 2's file (the bake of 12 November 2026).</summary>
        public const string FileName2 = "lakes-glo90-2.bin";
        public const long FileBytes2 = 57_734_593;
        public const string FileSha2562 = "51e2a4b63a6a42343a50c77a5bc9a15dfda1f7e97d5e3e96cadf17ed130f53d6";
        /// <summary>Version 3's file (EA-0147, re-contoured from version 2 on 16 November 2026).</summary>
        public const string FileName3 = "lakes-glo90-3.bin";
        public const long FileBytes3 = 57_742_609;
        public const string FileSha2563 = "ea3d4cd1b690192fe9613cf9ccafbf4bc4f15413bcb68b88b9bac3ffa18d163c";
        public static string FileNameFor(int version) => version >= 3 ? FileName3 : version >= 2 ? FileName2 : FileName;
        public static long FileBytesFor(int version) => version >= 3 ? FileBytes3 : version >= 2 ? FileBytes2 : FileBytes;
        public static string FileSha256For(int version) => version >= 3 ? FileSha2563 : version >= 2 ? FileSha2562 : FileSha256;
        /// <summary>Whether these bytes are this version's file this build reads (size and SHA-256).</summary>
        public static bool IsExpected(int version, byte[] bytes, out string why) => Matches(bytes, FileBytesFor(version), FileSha256For(version), out why);
        public const byte NoData = 255, NoLakes = 0, HasLakes = 1;
        public const byte FlagSalt = 1, FlagRaised = 2, FlagMissing = 4;
        const int HeaderBytes = 24;

        /// <summary>The file's version (its header's).</summary>
        public int Version { get; private set; }
        /// <summary>Vertex units per degree, tile size (degrees), tiles across and down.</summary>
        public int Unit { get; private set; }
        public int TileDeg { get; private set; }
        public int TilesX { get; private set; }
        public int TilesY { get; private set; }
        byte[] file, states;
        int[] offset, length;
        /// <summary>The file's name in error messages.</summary>
        string name;

        /// <summary>Whether these bytes are the file this build was made for (size and SHA-256).</summary>
        public static bool IsExpected(byte[] bytes, out string why) => Matches(bytes, FileBytes, FileSha256, out why);

        internal static bool Matches(byte[] bytes, long size, string sha256, out string why)
        {
            why = null;
            if (bytes == null) { why = "missing"; return false; }
            if (bytes.Length != size) { why = $"{bytes.Length:N0} bytes, not {size:N0}"; return false; }
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (sha != sha256) { why = "SHA-256 " + sha + ", not " + sha256; return false; }
            return true;
        }

        /// <summary>A lake outlines file of version 1 to 3, by its header.</summary>
        public static LakeOutlineData Read(byte[] file)
        {
            int v = file != null && file.Length >= 8 ? BitConverter.ToInt32(file, 4) : 0;
            return Read(file, FileNameFor(v), "EMLO", v is >= 1 and <= FileVersion ? v : FileVersion);
        }

        /// <summary>A file of this layout (also RiverOutlineData's, with its own magic).</summary>
        internal static LakeOutlineData Read(byte[] file, string name, string magic, int version)
        {
            EarthBinary.Expect(file, magic, HeaderBytes, name);
            if (BitConverter.ToInt32(file, 4) != version) throw new InvalidDataException(name + ": version mismatch");
            var d = new LakeOutlineData
            {
                file = file, name = name, Version = version, Unit = BitConverter.ToInt32(file, 8), TileDeg = BitConverter.ToInt32(file, 12),
                TilesX = BitConverter.ToInt32(file, 16), TilesY = BitConverter.ToInt32(file, 20),
            };
            if (d.Unit <= 0 || d.TileDeg <= 0 || d.TilesX * d.TileDeg != 360 || d.TilesY * d.TileDeg != 180) throw new InvalidDataException(name + ": layout mismatch");
            int n = d.TilesX * d.TilesY;
            if (file.Length < HeaderBytes + n) throw new InvalidDataException(name + ": cut short");
            d.states = new byte[n];
            Buffer.BlockCopy(file, HeaderBytes, d.states, 0, n);
            d.offset = new int[n];
            d.length = new int[n];
            int o = HeaderBytes + n;
            for (int t = 0; t < n; t++)
            {
                if (d.states[t] != HasLakes) continue;
                if (o + 8 > file.Length) throw new InvalidDataException(name + ": cut short");
                d.offset[t] = BitConverter.ToInt32(file, o);
                d.length[t] = BitConverter.ToInt32(file, o + 4);
                if (d.offset[t] < 0 || d.length[t] <= 0 || (long)d.offset[t] + d.length[t] > file.Length) throw new InvalidDataException(name + ": bad tile index");
                o += 8;
            }
            return d;
        }

        /// <summary>The tile holding a point (row 0 at 90°S), or -1 outside the grid, and the point's offset (degrees) from the
        /// tile's south-west corner (as CoastData.TileAt).</summary>
        public int TileAt(double lat, double lon, out double dLat, out double dLon)
        {
            double u = Geo.Wrap180(lon) + 180, v = lat + 90;
            int c = Math.Clamp((int)Math.Floor(u / TileDeg), 0, TilesX - 1), r = (int)Math.Floor(v / TileDeg);
            dLon = Math.Max(0, u - c * TileDeg);
            dLat = Math.Max(0, v - r * TileDeg);
            return r < 0 || r >= TilesY ? -1 : r * TilesX + c;
        }

        public byte State(int tile) => tile < 0 || tile >= states.Length ? NoData : states[tile];

        /// <summary>
        /// A tile with lakes: each lake's level (m) and flags, and its rings (lake, vertex count), with their vertices in
        /// units from the tile's south-west corner.
        /// </summary>
        public void Decode(int tile, out float[] level, out byte[] flags, out int[] ringLake, out int[] ringLength, out int[] vx, out int[] vy)
        {
            byte[] b = EarthBinary.UnbrotliAll(file, offset[tile], length[tile], name + ": tile " + tile);
            int o = 0;
            int lakes = (int)EarthBinary.ReadVarint(b, ref o);
            level = new float[lakes];
            flags = new byte[lakes];
            var rl = new List<int>();
            var rn = new List<int>();
            var xs = new List<int>();
            var ys = new List<int>();
            for (int k = 0; k < lakes; k++)
            {
                level[k] = EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o)) / 10f;
                flags[k] = (byte)EarthBinary.ReadVarint(b, ref o);
                int rings = (int)EarthBinary.ReadVarint(b, ref o);
                long x = 0, y = 0;
                for (int r = 0; r < rings; r++)
                {
                    int n = (int)EarthBinary.ReadVarint(b, ref o);
                    rl.Add(k);
                    rn.Add(n);
                    for (int j = 0; j < n; j++)
                    {
                        x += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                        y += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                        xs.Add((int)x);
                        ys.Add((int)y);
                    }
                }
            }
            ringLake = rl.ToArray();
            ringLength = rn.ToArray();
            vx = xs.ToArray();
            vy = ys.ToArray();
        }
    }

    /// <summary>
    /// The water mask's lakes in one world (EarthParams.LakeOutlines): for any point, whether it lies in a lake and which,
    /// how far (blocks, up to Reach) it is from the nearest shore, and that shore's lake. Works in the world's own block
    /// metric, like CoastLayer. A tile is decoded the first time something comes into it and kept in a small cache: its
    /// rings in blocks, a grid of cells with the edges touching each, and the lake at each cell's centre (counting each
    /// lake's crossings from outside the tile, so a lake on an island in another lake comes out right). Near a tile's
    /// edge the neighbouring tiles' shores count too. With UnionShores (river outlines 3), pieces that overlap are one
    /// water: the shores are the edges with land on one side. Thread-safe.
    /// </summary>
    public sealed class LakeOutlineLayer
    {
        public readonly LakeOutlineData Data;
        /// <summary>How far (blocks) distances are measured: about 800 m, 6..64 blocks (the bed's slope from the shore runs
        /// out there).</summary>
        public readonly double Reach;
        /// <summary>Land this near (blocks) a lake holds it in: the highest such lake is in Hit.HoldLevel.</summary>
        public const double HoldReach = 1.5;
        readonly double bpdX, bpdZ, smoothCut;
        readonly int smoothPasses;
        /// <summary>Decoded tiles kept.</summary>
        public int MaxCachedTiles { get => tiles.Max; set => tiles.Max = value; }
        readonly TileCache<Tile> tiles;

        sealed class Tile
        {
            public float[] X, Z;
            /// <summary>The edge from vertex v runs to Next[v]; Lake[v] is its lake; Cut[v]: it runs along the tile's edge.</summary>
            public int[] Next, Lake;
            public bool[] Cut;
            /// <summary>UnionShores: the edge has water on both sides (it lies in another piece), so it is no shore; null
            /// without.</summary>
            public bool[] Interior;
            public float[] Level;
            public byte[] Flags;
            /// <summary>Each lake's area in this tile (blocks²).</summary>
            public float[] Area;
            /// <summary>Each lake's sides of the tile its outline runs along (SideW, SideE, SideS, SideN): where the lake
            /// goes on into the next tile.</summary>
            public byte[] Sides;
            /// <summary>Where each lake's outline runs along each side of the tile (blocks along that side, from its west or
            /// south end): [4 * lake + side] with side 0 west, 1 east, 2 south, 3 north; min > max where it doesn't.</summary>
            public float[] SpanMin, SpanMax;
            /// <summary>GenFixRules 5 (EA-0112): each lake's area with its pieces in the tiles next door, filled when first
            /// asked (NaN until then).</summary>
            public float[] Whole;
            public int Nx, Nz;
            public double Cell;
            /// <summary>Each cell's edges (CellStart, CellEdges) and the lake at its centre (CellLake: -1 none; with
            /// UnionShores, -2 - k where the centre lies in several pieces, CellMany[k]).</summary>
            public int[] CellStart, CellEdges, CellLake;
            public int[][] CellMany;
        }

        /// <summary>What the outlines say at a point.</summary>
        public struct Hit
        {
            /// <summary>The outlines decide here (a tile the file covers); false: the grid's lakes, as before.</summary>
            public bool Covered;
            /// <summary>In a lake: Level and Flags are its own, Dist the distance to its shore.</summary>
            public bool Inside;
            /// <summary>Inside: the lake's level (m); outside: the nearest lake's within Reach, NaN when there is none.</summary>
            public double Level;
            public byte Flags;
            /// <summary>Distance (blocks) to the nearest shore, up to Reach.</summary>
            public double Dist;
            /// <summary>That lake's area (blocks², its part in the tile the shore is in): a pond smaller than a block or two
            /// may hold no block's centre and show no water.</summary>
            public double Area;
            /// <summary>The tile (key + 1, 0: none) and lake index the hit's Area belongs to, for
            /// <see cref="LakeOutlineLayer.WholeArea"/>.</summary>
            internal int TileKey1, LakeIndex;
            /// <summary>The layer that made this hit (its tile and lake numbers are that layer's): the water mask's, the works
            /// of 1318 or the river outlines' each number their lakes per tile on their own.</summary>
            internal LakeOutlineLayer Owner;
            /// <summary>Outside: the highest level (m) of the lakes within HoldReach, NaN when there is none.</summary>
            public double HoldLevel;
        }

        /// <summary>The smallest cell of a tile's edge index (blocks; at least Reach).</summary>
        readonly double minCell;

        /// <summary>Pieces with any of these flags are left out when a tile is decoded, as if absent (0063 channel rules 1: the
        /// rivers' narrow pieces, RiverOutlineData.FlagNarrow, where the courses draw the streams).</summary>
        public readonly byte SkipFlags;

        /// <summary>With SkipFlags, where given: a flagged piece is left out where at least half of its outline lies where
        /// this says so (latitude, longitude; 0063 channel rules 1: by a river course the world draws,
        /// TerrainModel.CourseDrawsAt), or where it runs into the tile's unflagged water in one place at most (Skipped). A
        /// narrow piece away from the courses that joins the wide water in two places (a side arm, a channel between two
        /// wide waters) stays. Null: every flagged piece is left out.</summary>
        public readonly Func<double, double, bool> SkipOnlyWhere;

        /// <summary>With SkipOnlyWhere (0063 channel rules 4): a flagged piece away from the courses stays where this says so at
        /// a third or more of the points sampled along its rings (TerrainModel.FlowCarriesAt: the flow file has the river's
        /// flow passing through it), however many places it joins the wide water at (a side channel broken into pieces).
        /// Null: as before.</summary>
        public readonly Func<double, double, bool> KeepWhere;

        /// <summary>
        /// Pieces that overlap count as one water (river outlines 3, whose narrow pieces reach a little into the wide water
        /// they run into): where an edge crosses another piece's it is split, and an edge with water on both sides (a narrow
        /// piece's end in the wide water, the wide water's edge across its mouth) is no shore, for points in the water and
        /// out of it. A cell centre in several pieces keeps them all. Without it (versions 1 and 2, the lakes) every edge but
        /// the cuts is a shore, as these worlds were made.
        /// </summary>
        public readonly bool UnionShores;

        /// <summary>The lakes (reach: ReachFor), or with another reach and a smallest index cell (the rivers: RiverOutlineData).</summary>
        public LakeOutlineLayer(LakeOutlineData data, EarthGeometry geo, double reach = 0, double minCell = 16, byte skipFlags = 0, bool unionShores = false,
            Func<double, double, bool> skipOnlyWhere = null, Func<double, double, bool> keepWhere = null)
        {
            Data = data;
            SkipFlags = skipFlags;
            SkipOnlyWhere = skipOnlyWhere;
            KeepWhere = keepWhere;
            UnionShores = unionShores;
            bpdX = geo.BlocksPerDegreeX;
            bpdZ = geo.BlocksPerDegreeZ;
            double mpb = Geo.MetresPerDegree / geo.BlocksPerDegreeZ;
            Reach = reach > 0 ? reach : ReachFor(geo);
            this.minCell = minCell;
            // the outlines follow the mask's 3" pixels (45° cuts at their corners): where a pixel is a few blocks or more,
            // corners are rounded off by at most half a pixel
            smoothCut = 0.5 * FineElevation.CellMetres / mpb;
            smoothPasses = mpb < 40 ? 2 : 0;
            tiles = new TileCache<Tile>(Build, 24);
        }

        public static double ReachFor(EarthGeometry geo) => Math.Clamp(Math.Round(800 / (Geo.MetresPerDegree / geo.BlocksPerDegreeZ)), 6, 64);

        /// <summary>
        /// The distance (blocks) from a point to the nearest side of its tile. Inside a piece, <see cref="Hit.Dist"/> counts a
        /// tile side as no shore wherever any piece goes on across it (whatever its flags); a disc that must hold only the
        /// very pieces of its centre also stops at the sides (TerrainModel.CoastDistanceAt).
        /// </summary>
        public double TileSideDistance(double lat, double lon)
        {
            Data.TileAt(lat, lon, out double dLat, out double dLon);
            double w = Data.TileDeg * bpdX, ht = Data.TileDeg * bpdZ;
            double px = Math.Min(dLon * bpdX, w), pz = Math.Min(dLat * bpdZ, ht);
            return Math.Min(Math.Min(px, w - px), Math.Min(pz, ht - pz));
        }

        public Hit At(double lat, double lon)
        {
            var h = new Hit { Level = double.NaN, HoldLevel = double.NaN, Dist = Reach };
            int t = Data.TileAt(lat, lon, out double dLat, out double dLon);
            byte st = Data.State(t);
            if (st == LakeOutlineData.NoData) return h;
            h.Covered = true;
            double w = Data.TileDeg * bpdX, ht = Data.TileDeg * bpdZ;
            double px = Math.Min(dLon * bpdX, w), pz = Math.Min(dLat * bpdZ, ht);
            Tile own = st == LakeOutlineData.HasLakes ? GetTile(t) : null;
            int lake = own != null ? Inside(own, px, pz) : -1;
            if (lake >= 0)
            {
                h.Inside = true;
                h.Level = own.Level[lake];
                h.Flags = own.Flags[lake];
                h.Area = own.Area[lake];
                h.TileKey1 = t + 1; h.LakeIndex = lake; h.Owner = this;
            }
            double best = Reach * Reach;
            if (own != null) Scan(t, own, px, pz, ref best, ref h);
            int col = t % Data.TilesX, row = t / Data.TilesX;
            for (int dr = -1; dr <= 1; dr++)
            {
                if (dr < 0 && pz >= Reach || dr > 0 && ht - pz >= Reach) continue;
                int r2 = row + dr;
                if (r2 < 0 || r2 >= Data.TilesY) continue;
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    if (dc < 0 && px >= Reach || dc > 0 && w - px >= Reach) continue;
                    int t2 = r2 * Data.TilesX + (col + dc + Data.TilesX) % Data.TilesX;
                    byte s2 = Data.State(t2);
                    Tile next = s2 == LakeOutlineData.HasLakes ? GetTile(t2) : null;
                    // in a lake: the tile's side is a shore where the lake doesn't go on across it
                    if (h.Inside && (dr == 0) != (dc == 0))
                    {
                        double qx = dc < 0 ? w - 0.01 : dc > 0 ? 0.01 : px, qz = dr < 0 ? ht - 0.01 : dr > 0 ? 0.01 : pz;
                        if (next == null || Inside(next, qx, qz) < 0)
                        {
                            double ds = dc < 0 ? px : dc > 0 ? w - px : dr < 0 ? pz : ht - pz;
                            if (ds * ds < best) best = ds * ds;
                        }
                    }
                    if (next != null) Scan(t2, next, px - dc * w, pz - dr * ht, ref best, ref h);
                }
            }
            h.Dist = Math.Sqrt(best);
            return h;
        }

        /// <summary>The nearest edge within Reach: in a lake its shores only (not the cuts along the tile's edge, which have
        /// the lake on both sides); outside, any edge, since every lake lies wholly inside its tiles' rings. With UnionShores
        /// neither counts the edges with water on both sides.</summary>
        void Scan(int key, Tile t, double px, double pz, ref double best, ref Hit h)
        {
            double cap = Reach, hold2 = HoldReach * HoldReach;
            int c0 = Math.Clamp((int)Math.Floor((px - cap) / t.Cell), 0, t.Nx - 1), c1 = Math.Clamp((int)Math.Floor((px + cap) / t.Cell), 0, t.Nx - 1);
            int r0 = Math.Clamp((int)Math.Floor((pz - cap) / t.Cell), 0, t.Nz - 1), r1 = Math.Clamp((int)Math.Floor((pz + cap) / t.Cell), 0, t.Nz - 1);
            bool[] interior = t.Interior;
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    int cell = r * t.Nx + c;
                    for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1]; k++)
                    {
                        int a = t.CellEdges[k];
                        if (h.Inside && t.Cut[a] || interior != null && interior[a]) continue;
                        int b = t.Next[a];
                        double ax = t.X[a], az = t.Z[a], bx = t.X[b], bz = t.Z[b];
                        if (Math.Min(ax, bx) - px > cap || px - Math.Max(ax, bx) > cap || Math.Min(az, bz) - pz > cap || pz - Math.Max(az, bz) > cap) continue;
                        double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
                        double u = len > 0 ? Math.Clamp(((px - ax) * dx + (pz - az) * dz) / len, 0, 1) : 0;
                        double ex = ax + u * dx - px, ez = az + u * dz - pz, d2 = ex * ex + ez * ez;
                        if (h.Inside)
                        {
                            if (d2 < best) best = d2;
                            continue;
                        }
                        int lake = t.Lake[a];
                        if (d2 < best)
                        {
                            best = d2;
                            h.Level = t.Level[lake];
                            h.Flags = t.Flags[lake];
                            h.Area = t.Area[lake];
                            h.TileKey1 = key + 1; h.LakeIndex = lake; h.Owner = this;
                        }
                        if (d2 <= hold2 && !(t.Level[lake] <= h.HoldLevel)) h.HoldLevel = t.Level[lake];
                    }
                }
            }
        }

        /// <summary>The lake (index in the tile) a point of the tile lies in, -1 for none (the last of OddAt's).</summary>
        static int Inside(Tile t, double px, double pz)
        {
            var odd = oddScratch ??= new List<int>();
            OddAt(t, px, pz, odd);
            return odd.Count == 0 ? -1 : odd[odd.Count - 1];
        }

        /// <summary>The lakes a point of the tile lies in: those at its cell's centre, and each lake whose edges lie between
        /// them an odd number of times toggled.</summary>
        static void OddAt(Tile t, double px, double pz, List<int> odd)
        {
            int c = Math.Clamp((int)Math.Floor(px / t.Cell), 0, t.Nx - 1), r = Math.Clamp((int)Math.Floor(pz / t.Cell), 0, t.Nz - 1);
            int cell = r * t.Nx + c;
            odd.Clear();
            int centre = t.CellLake[cell];
            if (centre >= 0) odd.Add(centre);
            else if (centre <= -2) odd.AddRange(t.CellMany[-2 - centre]);
            double xr = (c + 0.5) * t.Cell, zr = (r + 0.5) * t.Cell;
            // along the row of the cell's centre to the point's x, then up or down to the point
            for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1]; k++)
            {
                int a = t.CellEdges[k], b = t.Next[a];
                double x0 = t.X[a], z0 = t.Z[a], x1 = t.X[b], z1 = t.Z[b];
                if ((z0 > zr) != (z1 > zr))
                {
                    double x = x0 + (zr - z0) / (z1 - z0) * (x1 - x0);
                    if (x > Math.Min(xr, px) && x < Math.Max(xr, px)) Toggle(odd, t.Lake[a]);
                }
                if ((x0 > px) != (x1 > px))
                {
                    double z = z0 + (px - x0) / (x1 - x0) * (z1 - z0);
                    if (z > Math.Min(zr, pz) && z < Math.Max(zr, pz)) Toggle(odd, t.Lake[a]);
                }
            }
        }

        [ThreadStatic] static List<int> oddScratch;

        static void Toggle(List<int> odd, int lake)
        {
            int i = odd.IndexOf(lake);
            if (i >= 0) odd.RemoveAt(i);
            else odd.Add(lake);
        }

        Tile GetTile(int key) => tiles.Get(key);

        /// <summary>
        /// GenFixRules 5 (EA-0112): the area (blocks²) of the lake a hit found, with its pieces across the tile's edges: the
        /// lake as a whole, so the rules that read the area (the shore strip, a pool joining its river) do not change along a
        /// whole degree line where one tile holds only a sliver of it. Asked only by worlds with those rules (older worlds
        /// never touch the neighbouring tiles). One step into the next tiles: a lake over three tiles in a row counts the
        /// middle one's pieces from either end.
        /// </summary>
        public double WholeArea(in Hit h)
        {
            if (h.TileKey1 <= 0 || h.Owner == null) return h.Area;
            // asked of another layer (the works of 1318's hit in the water mask's layer): the hit's own layer answers
            if (h.Owner != this) return h.Owner.WholeArea(h);
            return WholeArea(h.TileKey1 - 1, GetTile(h.TileKey1 - 1), h.LakeIndex);
        }

        /// <summary>
        /// A lake's area with the pieces of the same lake in the neighbouring tiles: those whose outline runs along the
        /// shared edge at the same level where this lake's does (the spans along that edge overlap within a 3" pixel), so two
        /// lakes at one level crossing the same degree line (a lake district, sea-level lagoons) don't lend each other their
        /// area. Cached in the tile (a race only computes it twice).
        /// </summary>
        double WholeArea(int key, Tile tile, int lake)
        {
            var whole = tile.Whole;
            if (whole == null)
            {
                whole = new float[tile.Area.Length];
                Array.Fill(whole, float.NaN);
                tile.Whole = whole;
            }
            float cached = whole[lake];
            if (!float.IsNaN(cached)) return cached;
            double a = tile.Area[lake];
            byte s = tile.Sides == null ? (byte)0 : tile.Sides[lake];
            int col = key % Data.TilesX, row = key / Data.TilesX;
            // one 3" pixel of the water mask, in blocks, as the slack between the two tiles' outlines along their edge
            double slack = Math.Max(1, 3.0 / 3600 * Math.Max(bpdX, bpdZ));
            foreach (var (side, si, dc, dr, opposite, oi) in new (byte, int, int, int, byte, int)[] { (SideW, 0, -1, 0, SideE, 1), (SideE, 1, 1, 0, SideW, 0), (SideS, 2, 0, -1, SideN, 3), (SideN, 3, 0, 1, SideS, 2) })
            {
                if ((s & side) == 0) continue;
                float lo = tile.SpanMin[4 * lake + si], hi = tile.SpanMax[4 * lake + si];
                int r2 = row + dr, c2 = ((col + dc) % Data.TilesX + Data.TilesX) % Data.TilesX;
                if (r2 < 0 || r2 >= Data.TilesY) continue;
                int k2 = r2 * Data.TilesX + c2;
                if (Data.State(k2) != LakeOutlineData.HasLakes) continue;
                var n = GetTile(k2);
                for (int m = 0; m < n.Area.Length; m++)
                    if (n.Sides != null && (n.Sides[m] & opposite) != 0 && n.Level[m] == tile.Level[lake]
                        && n.SpanMin[4 * m + oi] <= hi + slack && n.SpanMax[4 * m + oi] >= lo - slack) a += n.Area[m];
            }
            // the stored float, also on this first call: an answer must not depend on whether the tile was cached
            return whole[lake] = (float)a;
        }

        const byte SideW = 1, SideE = 2, SideS = 4, SideN = 8;

        Tile Build(int key)
        {
            Data.Decode(key, out float[] level, out byte[] flags, out int[] ringLake, out int[] ringLength, out int[] vx, out int[] vy);
            bool[] skip = SkipFlags != 0 ? Skipped(key, level, flags, ringLake, ringLength, vx, vy) : null;
            return Assemble(level, flags, ringLake, ringLength, vx, vy, skip, UnionShores);
        }

        /// <summary>A tile from its decoded pieces, but those skipped: the rings in blocks, the edge index and the lake at each
        /// cell's centre.</summary>
        Tile Assemble(float[] level, byte[] flags, int[] ringLake, int[] ringLength, int[] vx, int[] vy, bool[] skip, bool union)
        {
            int full = Data.Unit * Data.TileDeg;
            double sx = bpdX / Data.Unit, sz = bpdZ / Data.Unit;
            var xs = new List<float>(vx.Length);
            var zs = new List<float>(vx.Length);
            var next = new List<int>(vx.Length);
            var lakeOf = new List<int>(vx.Length);
            var cut = new List<bool>(vx.Length);
            var rx = new List<double>();
            var rz = new List<double>();
            var rs = new List<byte>();
            // signed: a lake's islands wind the other way round from its outer ring
            var area = new double[level.Length];
            var sides = new byte[level.Length];
            var spanMin = new float[4 * level.Length];
            var spanMax = new float[4 * level.Length];
            Array.Fill(spanMin, float.MaxValue);
            Array.Fill(spanMax, float.MinValue);
            for (int ring = 0, v = 0; ring < ringLength.Length; v += ringLength[ring++])
            {
                if (skip != null && skip[ringLake[ring]]) continue;
                rx.Clear(); rz.Clear(); rs.Clear();
                for (int j = 0; j < ringLength[ring]; j++)
                {
                    int ux = vx[v + j], uy = vy[v + j];
                    rx.Add(ux * sx);
                    rz.Add(uy * sz);
                    rs.Add((byte)((ux == 0 ? SideW : 0) | (ux == full ? SideE : 0) | (uy == 0 ? SideS : 0) | (uy == full ? SideN : 0)));
                }
                if (smoothPasses > 0) Smooth(rx, rz, rs, smoothCut, smoothPasses);
                int n = rx.Count;
                if (n < 3) continue;
                double twice = 0;
                for (int j = 0; j < n; j++) { int k = (j + 1) % n; twice += rx[j] * rz[k] - rx[k] * rz[j]; }
                area[ringLake[ring]] += 0.5 * twice;
                int first = xs.Count;
                for (int j = 0; j < n; j++)
                {
                    int k = (j + 1) % n;
                    xs.Add((float)rx[j]);
                    zs.Add((float)rz[j]);
                    next.Add(first + k);
                    lakeOf.Add(ringLake[ring]);
                    cut.Add((rs[j] & rs[k]) != 0);
                    byte on = (byte)(rs[j] & rs[k]);
                    sides[ringLake[ring]] |= on;
                    for (int si = 0; si < 4; si++)
                    {
                        if ((on & (1 << si)) == 0) continue;
                        // along the side: north-south (z) on the west and east sides, west-east (x) on the south and north
                        double p0 = si < 2 ? rz[j] : rx[j], p1 = si < 2 ? rz[k] : rx[k];
                        int at = 4 * ringLake[ring] + si;
                        spanMin[at] = (float)Math.Min(spanMin[at], Math.Min(p0, p1));
                        spanMax[at] = (float)Math.Max(spanMax[at], Math.Max(p0, p1));
                    }
                }
            }
            var t = new Tile
            {
                X = xs.ToArray(), Z = zs.ToArray(), Next = next.ToArray(), Lake = lakeOf.ToArray(), Cut = cut.ToArray(),
                Level = level, Flags = flags, Area = Array.ConvertAll(area, a => (float)Math.Abs(a)), Sides = sides, SpanMin = spanMin, SpanMax = spanMax,
            };

            double w = Data.TileDeg * bpdX, h = Data.TileDeg * bpdZ;
            t.Cell = Math.Max(Reach, minCell);
            t.Nx = Math.Max(1, (int)Math.Ceiling(w / t.Cell));
            t.Nz = Math.Max(1, (int)Math.Ceiling(h / t.Cell));
            IndexEdges(t);
            if (union && SplitAtCrossings(t)) IndexEdges(t);
            CellLakes(t, union);
            if (union) MarkInterior(t);
            return t;
        }

        /// <summary>How far apart (mask pixels) the places where a piece runs into other water must be to be two: more than
        /// a narrow piece's width (under three pixels) and its reach into the water it runs into.</summary>
        public const double JoinsApartPixels = 5;

        /// <summary>
        /// The pieces of a tile left out (SkipFlags). With SkipOnlyWhere: those where at least half of the points sampled
        /// along their rings (up to 64 a ring) lie where it says so, and of the others those that run into the tile's
        /// unflagged water (the wide water) in at most one place: a piece joining it in two places JoinsApartPixels apart or
        /// more (a side arm, a channel between two wide waters) stays, one that only leaves it (a canal from a river or a
        /// reservoir) or meets none goes.
        /// </summary>
        bool[] Skipped(int key, float[] level, byte[] flags, int[] ringLake, int[] ringLength, int[] vx, int[] vy)
        {
            int lakes = level.Length;
            var skip = new bool[lakes];
            for (int k = 0; k < lakes; k++) skip[k] = (flags[k] & SkipFlags) != 0;
            if (SkipOnlyWhere == null) return skip;
            var near = new int[lakes];
            var seen = new int[lakes];
            var carries = new int[lakes];
            double lon0 = key % Data.TilesX * Data.TileDeg - 180, lat0 = key / Data.TilesX * Data.TileDeg - 90, unit = Data.Unit;
            for (int ring = 0, v = 0; ring < ringLength.Length; v += ringLength[ring++])
            {
                int lake = ringLake[ring];
                if (!skip[lake]) continue;
                int n = ringLength[ring], stride = Math.Max(1, n / 64);
                for (int j = 0; j < n; j += stride)
                {
                    seen[lake]++;
                    double plat = lat0 + vy[v + j] / unit, plon = lon0 + vx[v + j] / unit;
                    if (SkipOnlyWhere(plat, plon)) near[lake]++;
                    else if (KeepWhere != null && KeepWhere(plat, plon)) carries[lake]++;
                }
            }
            bool any = false;
            var off = new bool[lakes];
            for (int k = 0; k < lakes; k++)
                if (skip[k] && !(seen[k] > 0 && 2 * near[k] >= seen[k]))
                {
                    // (channel rules 4: a piece the flow passes through stays)
                    if (KeepWhere != null && 3 * carries[k] >= seen[k]) { skip[k] = false; continue; }
                    any = off[k] = true;
                }
            if (!any) return skip;
            // where the pieces off the courses run into the wide water: the spread of their points inside it
            var wide = Assemble(level, flags, ringLake, ringLength, vx, vy, skip, false);
            double sx = bpdX / Data.Unit, sz = bpdZ / Data.Unit;
            var x0 = new double[lakes];
            var x1 = new double[lakes];
            var z0 = new double[lakes];
            var z1 = new double[lakes];
            Array.Fill(x0, double.MaxValue); Array.Fill(z0, double.MaxValue);
            Array.Fill(x1, double.MinValue); Array.Fill(z1, double.MinValue);
            var odd = new List<int>();
            for (int ring = 0, v = 0; ring < ringLength.Length; v += ringLength[ring++])
            {
                int lake = ringLake[ring];
                if (!off[lake]) continue;
                for (int j = 0; j < ringLength[ring]; j++)
                {
                    double px = vx[v + j] * sx, pz = vy[v + j] * sz;
                    OddAt(wide, px, pz, odd);
                    if (odd.Count == 0) continue;
                    x0[lake] = Math.Min(x0[lake], px); x1[lake] = Math.Max(x1[lake], px);
                    z0[lake] = Math.Min(z0[lake], pz); z1[lake] = Math.Max(z1[lake], pz);
                }
            }
            double apart = JoinsApartPixels * FineElevation.CellMetres * bpdZ / Geo.MetresPerDegree;
            for (int k = 0; k < lakes; k++)
                if (off[k] && x1[k] >= x0[k] && (x1[k] - x0[k]) * (x1[k] - x0[k]) + (z1[k] - z0[k]) * (z1[k] - z0[k]) >= apart * apart) skip[k] = false;
            return skip;
        }

        /// <summary>The cells an edge's bounding box touches.</summary>
        static void Span(Tile t, int e, out int c0, out int c1, out int r0, out int r1)
        {
            int b = t.Next[e];
            c0 = Math.Clamp((int)Math.Floor(Math.Min(t.X[e], t.X[b]) / t.Cell), 0, t.Nx - 1);
            c1 = Math.Clamp((int)Math.Floor(Math.Max(t.X[e], t.X[b]) / t.Cell), 0, t.Nx - 1);
            r0 = Math.Clamp((int)Math.Floor(Math.Min(t.Z[e], t.Z[b]) / t.Cell), 0, t.Nz - 1);
            r1 = Math.Clamp((int)Math.Floor(Math.Max(t.Z[e], t.Z[b]) / t.Cell), 0, t.Nz - 1);
        }

        /// <summary>Each cell's edges: those whose bounding box touches it.</summary>
        static void IndexEdges(Tile t)
        {
            int nc = t.Nx * t.Nz, ne = t.X.Length;
            var count = new int[nc + 1];
            for (int e = 0; e < ne; e++)
            {
                Span(t, e, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) count[r * t.Nx + c + 1]++;
            }
            for (int i = 0; i < nc; i++) count[i + 1] += count[i];
            t.CellStart = (int[])count.Clone();
            t.CellEdges = new int[count[nc]];
            for (int e = 0; e < ne; e++)
            {
                Span(t, e, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t.CellEdges[count[r * t.Nx + c]++] = e;
            }
        }

        /// <summary>A crossing this near an edge's end (a fraction of the edge) is at the end: no split.</summary>
        const double SplitEnd = 1e-6;
        /// <summary>How far (blocks) to either side of an edge's middle MarkInterior looks for water.</summary>
        const double SideStep = 0.01;

        /// <summary>
        /// UnionShores: splits each edge (but the cuts) where it crosses another piece's, so each part lies wholly in or out
        /// of the other pieces. False when no edges cross.
        /// </summary>
        static bool SplitAtCrossings(Tile t)
        {
            int ne = t.X.Length;
            List<(int e, double u)> at = null;
            for (int e = 0; e < ne; e++)
            {
                if (t.Cut[e]) continue;
                int b = t.Next[e];
                double ax = t.X[e], az = t.Z[e], rx = t.X[b] - ax, rz = t.Z[b] - az;
                Span(t, e, out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        int cell = r * t.Nx + c;
                        for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1]; k++)
                        {
                            int f = t.CellEdges[k];
                            if (t.Lake[f] == t.Lake[e] || t.Cut[f]) continue;
                            int d = t.Next[f];
                            double cx = t.X[f], cz = t.Z[f], sx = t.X[d] - cx, sz = t.Z[d] - cz;
                            double den = rx * sz - rz * sx;
                            if (den == 0) continue;
                            double qx = cx - ax, qz = cz - az;
                            double u = (qx * sz - qz * sx) / den, v = (qx * rz - qz * rx) / den;
                            if (u <= SplitEnd || u >= 1 - SplitEnd || v < 0 || v > 1) continue;
                            // once, in the cell holding the crossing (among those both edges touch)
                            Span(t, f, out int fc0, out int fc1, out int fr0, out int fr1);
                            int ic = Math.Clamp((int)Math.Floor((ax + u * rx) / t.Cell), Math.Max(c0, fc0), Math.Min(c1, fc1));
                            int ir = Math.Clamp((int)Math.Floor((az + u * rz) / t.Cell), Math.Max(r0, fr0), Math.Min(r1, fr1));
                            if (ic == c && ir == r) (at ??= new List<(int, double)>()).Add((e, u));
                        }
                    }
                }
            }
            if (at == null) return false;
            at.Sort();
            var xs = new List<float>(t.X);
            var zs = new List<float>(t.Z);
            var next = new List<int>(t.Next);
            var lake = new List<int>(t.Lake);
            var cut = new List<bool>(t.Cut);
            for (int i = 0; i < at.Count;)
            {
                int e = at[i].e, end = t.Next[e], prev = e;
                double ax = t.X[e], az = t.Z[e], rx = t.X[end] - ax, rz = t.Z[end] - az, last = 0;
                for (; i < at.Count && at[i].e == e; i++)
                {
                    double u = at[i].u;
                    if (u - last <= SplitEnd) continue;
                    last = u;
                    int n = xs.Count;
                    xs.Add((float)(ax + u * rx));
                    zs.Add((float)(az + u * rz));
                    next.Add(end);
                    lake.Add(t.Lake[e]);
                    cut.Add(false);
                    next[prev] = n;
                    prev = n;
                }
            }
            t.X = xs.ToArray();
            t.Z = zs.ToArray();
            t.Next = next.ToArray();
            t.Lake = lake.ToArray();
            t.Cut = cut.ToArray();
            return true;
        }

        /// <summary>
        /// UnionShores: marks the edges (but the cuts) with water on both sides, its own piece's on one and another's on the
        /// other, a little to either side of the edge's middle. Only edges in cells where pieces meet (edges of two pieces
        /// or more, or a centre in another piece) can be.
        /// </summary>
        static void MarkInterior(Tile t)
        {
            int nc = t.Nx * t.Nz, ne = t.X.Length;
            t.Interior = new bool[ne];
            var meet = new bool[nc];
            bool any = false;
            for (int cell = 0; cell < nc; cell++)
            {
                int centre = t.CellLake[cell], one = centre;
                bool mixed = centre <= -2;
                for (int k = t.CellStart[cell]; k < t.CellStart[cell + 1] && !mixed; k++)
                {
                    int l = t.Lake[t.CellEdges[k]];
                    if (one < 0) one = l;
                    else mixed = l != one;
                }
                meet[cell] = mixed;
                any |= mixed;
            }
            if (!any) return;
            var odd = new List<int>();
            for (int e = 0; e < ne; e++)
            {
                if (t.Cut[e]) continue;
                Span(t, e, out int c0, out int c1, out int r0, out int r1);
                bool near = false;
                for (int r = r0; r <= r1 && !near; r++) for (int c = c0; c <= c1 && !near; c++) near = meet[r * t.Nx + c];
                if (!near) continue;
                int b = t.Next[e];
                double dx = t.X[b] - t.X[e], dz = t.Z[b] - t.Z[e], len = Math.Sqrt(dx * dx + dz * dz);
                if (len <= 2 * SideStep) continue;
                double mx = t.X[e] + 0.5 * dx, mz = t.Z[e] + 0.5 * dz, ox = -dz / len * SideStep, oz = dx / len * SideStep;
                OddAt(t, mx + ox, mz + oz, odd);
                if (odd.Count == 0) continue;
                OddAt(t, mx - ox, mz - oz, odd);
                t.Interior[e] = odd.Count > 0;
            }
        }

        /// <summary>The lake at each cell's centre (with several, UnionShores: all of them, CellMany): along its row from
        /// west of the tile (outside every ring), each lake's crossings toggling it.</summary>
        static void CellLakes(Tile t, bool many)
        {
            int nc = t.Nx * t.Nz, ne = t.X.Length;
            var rows = new List<(double x, int lake)>[t.Nz];
            for (int r = 0; r < t.Nz; r++) rows[r] = new List<(double, int)>();
            for (int e = 0; e < ne; e++)
            {
                int b = t.Next[e];
                double x0 = t.X[e], z0 = t.Z[e], x1 = t.X[b], z1 = t.Z[b];
                int ra = Math.Max(0, (int)Math.Floor(Math.Min(z0, z1) / t.Cell - 0.5)), rb = Math.Min(t.Nz - 1, (int)Math.Ceiling(Math.Max(z0, z1) / t.Cell));
                for (int r = ra; r <= rb; r++)
                {
                    double zr = (r + 0.5) * t.Cell;
                    if ((z0 > zr) != (z1 > zr)) rows[r].Add((x0 + (zr - z0) / (z1 - z0) * (x1 - x0), t.Lake[e]));
                }
            }
            t.CellLake = new int[nc];
            List<int[]> several = null;
            var odd = new List<int>();
            for (int r = 0; r < t.Nz; r++)
            {
                var cross = rows[r];
                cross.Sort((p, q) => p.x.CompareTo(q.x));
                odd.Clear();
                int p = 0;
                for (int c = 0; c < t.Nx; c++)
                {
                    double xr = (c + 0.5) * t.Cell;
                    while (p < cross.Count && cross[p].x < xr) Toggle(odd, cross[p++].lake);
                    int cell = r * t.Nx + c;
                    if (odd.Count > 1 && many)
                    {
                        (several ??= new List<int[]>()).Add(odd.ToArray());
                        t.CellLake[cell] = -1 - several.Count;
                    }
                    else t.CellLake[cell] = odd.Count == 0 ? -1 : odd[odd.Count - 1];
                }
            }
            t.CellMany = several?.ToArray();
        }

        /// <summary>
        /// Rounds off a closed ring (blocks; side flags per vertex): each pass puts each edge's points a quarter from its ends
        /// (at most <paramref name="cut"/>; Chaikin's corner cutting), keeping the vertices on the tile's edge where they are
        /// and the cuts along it straight, so neighbouring tiles still meet.
        /// </summary>
        static void Smooth(List<double> x, List<double> z, List<byte> side, double cut, int passes)
        {
            var nx = new List<double>();
            var nz = new List<double>();
            var ns = new List<byte>();
            for (int pass = 0; pass < passes; pass++)
            {
                int n = x.Count;
                if (n < 3) return;
                int s0 = side.FindIndex(s => s != 0);
                if (s0 > 0)
                {
                    Rotate(x, s0); Rotate(z, s0); Rotate(side, s0);
                }
                nx.Clear(); nz.Clear(); ns.Clear();
                if (s0 >= 0) { nx.Add(x[0]); nz.Add(z[0]); ns.Add(side[0]); }
                for (int i = 0; i < n; i++)
                {
                    int b = (i + 1) % n;
                    if ((side[i] & side[b]) != 0)
                    {
                        if (b != 0) { nx.Add(x[b]); nz.Add(z[b]); ns.Add(side[b]); }
                        continue;
                    }
                    double dx = x[b] - x[i], dz = z[b] - z[i], len = Math.Sqrt(dx * dx + dz * dz);
                    double f = len > 0 ? Math.Min(0.25, cut / len) : 0.25;
                    nx.Add(x[i] + f * dx); nz.Add(z[i] + f * dz); ns.Add(0);
                    nx.Add(x[b] - f * dx); nz.Add(z[b] - f * dz); ns.Add(0);
                    if (side[b] != 0 && b != 0) { nx.Add(x[b]); nz.Add(z[b]); ns.Add(side[b]); }
                }
                x.Clear(); x.AddRange(nx);
                z.Clear(); z.AddRange(nz);
                side.Clear(); side.AddRange(ns);
            }
        }

        static void Rotate<T>(List<T> list, int k)
        {
            var head = list.GetRange(0, k);
            list.RemoveRange(0, k);
            list.AddRange(head);
        }
    }
}
