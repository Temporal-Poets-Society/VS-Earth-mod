using System;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// A global latitude/longitude grid in brotli tiles, the container of temperature.bin (FineTemperature) and decision
    /// 0031's data: rain.bin (CHELSA rainfall, 2'), summits.bin (ETOPO 2022 30" elevation in mountain tiles) and glaciers.bin
    /// (RGI 6 glacier coverage, 30"); aridity.bin too. Header: magic, version, width, height, tile, tile count, then (offset,
    /// length) per tile from the north-west, row by row; a tile of length 0 holds no data. Cell (row, col) is centred at
    /// latitude 90 - (row + 0.5) / Res and longitude -180 + (col + 0.5) / Res. Tiles are decoded on demand and kept in a
    /// TileCache. Thread-safe.
    ///
    /// The header's version carries the coding in its high 16 bits (PlaneCoding): 0, a tile is brotli of its planes; 1 (the
    /// mod's own grids from rc.14, EA-0077), a block of predicted planes that decodes to the same bytes. <see cref="Version"/>
    /// is the content version in the low 16 bits.
    /// </summary>
    public sealed class TiledGrid
    {
        public readonly int Width, Height, Res, Tile, Version;
        /// <summary>How the tiles are coded: <see cref="PlaneCoding.Plain"/> or <see cref="PlaneCoding.Predicted"/>.</summary>
        public readonly int Coding;
        readonly int tilesX, bytesPerCell;
        readonly PlaneType[] types;
        readonly string name;
        readonly byte[] file;
        readonly int[] offset, length;
        readonly TileCache<byte[]> tiles;
        static readonly byte[] Empty = new byte[0];

        /// <summary>bytesPerCell: the bytes all planes of one cell take after decompression. <paramref name="name"/>: the
        /// file's name in error messages (the magic when not given). <paramref name="types"/>: the planes' types for a
        /// predicted file (their bytes add up to bytesPerCell); by default bytesPerCell planes of bytes.</summary>
        public TiledGrid(byte[] file, string magic, int bytesPerCell, int maxCachedTiles = 48, string name = null, PlaneType[] types = null)
        {
            this.file = file;
            this.bytesPerCell = bytesPerCell;
            this.name = name ?? magic;
            if (types == null)
            {
                types = new PlaneType[bytesPerCell];
                Array.Fill(types, PlaneType.U8);
            }
            if (PlaneCoding.RawSize(types, 1) != bytesPerCell) throw new ArgumentException(this.name + ": plane types of another size");
            this.types = types;
            EarthBinary.Expect(file, magic, 24, this.name);
            int headerVersion = BitConverter.ToInt32(file, 4);
            Version = PlaneCoding.ContentVersion(headerVersion);
            Coding = PlaneCoding.Coding(headerVersion);
            if (Coding != PlaneCoding.Plain && Coding != PlaneCoding.Predicted) throw new InvalidDataException(this.name + ": unknown coding " + Coding);
            Width = BitConverter.ToInt32(file, 8);
            Height = BitConverter.ToInt32(file, 12);
            Tile = BitConverter.ToInt32(file, 16);
            int count = BitConverter.ToInt32(file, 20);
            if (Tile <= 0 || Width % Tile != 0 || Height % Tile != 0 || count != (Width / Tile) * (Height / Tile) || Width != 2 * Height || Width % 360 != 0)
                throw new InvalidDataException(this.name + ": layout mismatch");
            Res = Width / 360;
            tilesX = Width / Tile;
            EarthBinary.TileIndex(file, 24, count, this.name, out offset, out length);
            tiles = new TileCache<byte[]>(Decode, maxCachedTiles);
        }

        public static byte[] ReadAll(Stream s) => EarthBinary.ReadAll(s);

        /// <summary>The fractional (row, col) of a place: integer values are cell centres.</summary>
        public void Position(double lat, double lon, out double row, out double col)
        {
            row = (90 - lat) * Res - 0.5;
            col = (lon + 180) * Res - 0.5;
        }

        /// <summary>The tile key (row of tiles * tiles across + column) and the index in the tile of a cell. Rows are clamped
        /// at the poles, columns wrap.</summary>
        public int KeyOf(int row, int col, out int index)
        {
            row = Math.Clamp(row, 0, Height - 1);
            col %= Width;
            if (col < 0) col += Width;
            index = (row % Tile) * Tile + col % Tile;
            return row / Tile * tilesX + col / Tile;
        }

        /// <summary>A cell's decoded tile (all planes, plane by plane) and its index in it; null where the tile holds no data.
        /// Rows are clamped at the poles, columns wrap.</summary>
        public byte[] TileOf(int row, int col, out int index)
        {
            // (TileCache keeps each thread's last tile of each grid: rain, aridity, summits and glaciers are read in turn)
            var t = tiles.Get(KeyOf(row, col, out index));
            return t.Length == 0 ? null : t;
        }

        /// <summary>Cells in one tile (the size of each plane).</summary>
        public int CellsPerTile => Tile * Tile;

        /// <summary>A tile's bytes, all planes, freshly decompressed (not cached); empty for a tile without data.</summary>
        public byte[] Decode(int key)
        {
            if (length[key] == 0) return Empty;
            if (Coding == PlaneCoding.Predicted) return PlaneCoding.Decode(file, offset[key], length[key], Tile, Tile, types, name + ": tile " + key);
            return EarthBinary.Unbrotli(file, offset[key], length[key], Tile * Tile * bytesPerCell, name + ": tile " + key);
        }

        /// <summary>The first plane's bytes at a place, bilinear between cell centres (a tile without data counts 0).</summary>
        public double Bilinear(double lat, double lon)
        {
            Position(lat, lon, out double fr, out double fc);
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0, sum = 0;
            for (int k = 0; k < 4; k++)
            {
                int dr = k >> 1, dc = k & 1;
                var t = TileOf(r0 + dr, c0 + dc, out int i);
                if (t != null) sum += (dr == 0 ? 1 - tr : tr) * (dc == 0 ? 1 - tc : tc) * t[i];
            }
            return sum;
        }

        /// <summary>The first plane's bytes at a place, Catmull-Rom over the 4 x 4 cells around it (a tile without data counts
        /// 0): no kinks along the cells, unlike <see cref="Bilinear"/>.</summary>
        public double CatmullRom(double lat, double lon)
        {
            Position(lat, lon, out double fr, out double fc);
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            Span<double> wr = stackalloc double[4], wc = stackalloc double[4];
            CatmullRomWeights(tr, wr);
            CatmullRomWeights(tc, wc);
            double sum = 0;
            for (int dr = -1; dr <= 2; dr++)
            {
                double row = 0;
                for (int dc = -1; dc <= 2; dc++)
                {
                    var t = TileOf(r0 + dr, c0 + dc, out int i);
                    if (t != null) row += wc[dc + 1] * t[i];
                }
                sum += wr[dr + 1] * row;
            }
            return sum;
        }

        static void CatmullRomWeights(double t, Span<double> w)
        {
            double t2 = t * t, t3 = t2 * t;
            w[0] = 0.5 * (-t3 + 2 * t2 - t);
            w[1] = 0.5 * (3 * t3 - 5 * t2 + 2);
            w[2] = 0.5 * (-3 * t3 + 4 * t2 + t);
            w[3] = 0.5 * (t3 - t2);
        }
    }
}
