using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0048, soil rules 3: the natural flood of the rivers that keep water through the dry season, as the level (m)
    /// it reaches, on a 15" grid (240 cells per degree), baked by tools/FloodplainBake from Earth's own river courses and
    /// GLO-90 heights. From every course with 10 m³/s or more, at least 10 m³/s of it from moist catchments
    /// (AridHydrology.WetSourceFlow), the flood stands 1.2·Q^0.3 m above the river's water (12.6 m on the Nile, 8.7 m on the
    /// Euphrates, 4.8 m at 100 m³/s), falls 0.15 m per km of the way out and covers the land it reaches without rising, so
    /// closed basins beside a valley (Wadi Natrun) stay dry. It never stands above the top of the river nearest in a
    /// straight line, nor covers ground more than one flood height below that river's water (the Salton basin beside the
    /// Colorado). Only rivers through dry land are flooded (the grid is for arid ground). A cell holds the highest flood
    /// reaching it; the cells next to the flood hold it as well, a little lower, so ground decides where the flood ends,
    /// not the grid: in the game a column is in the flood where its own height is at or below the level here
    /// (<see cref="Floods"/>), and the edge follows the valley's foot.
    /// Earth ships this grid (assets/earth/config/floodplains.bin). File: "EFP1", version, cells per degree, block size and
    /// block count (ints), then one raw deflate stream: each block's key (block row from the north x blocks across + block
    /// column from 180° W, int), then each block's cells (rows from the north) as unsigned 16-bit decimetres above
    /// <see cref="Base"/> + 1 (0: no flood), each stored as its difference from the cell before it in the block. Only blocks
    /// with a flooded cell are stored.
    /// </summary>
    public sealed class FloodplainGrid
    {
        public const string Asset = "earth:config/floodplains.bin";
        public const int FileVersion = 1;
        public const int CellsPerDegree = 240, Block = 16;
        const string Magic = "EFP1";
        const int HeaderBytes = 20;
        public const int Cols = 360 * CellsPerDegree, Rows = 180 * CellsPerDegree;
        public const int BlockCols = Cols / Block, BlockRows = Rows / Block;
        /// <summary>Levels are stored as decimetres above this (m): the Dead Sea's shore (-430 m) to 5500 m fit.</summary>
        public const double Base = -1000;

        readonly Dictionary<int, ushort[]> blocks;

        public FloodplainGrid(Dictionary<int, ushort[]> blocks) { this.blocks = blocks ?? new Dictionary<int, ushort[]>(); }

        public int BlockCount => blocks.Count;
        public IReadOnlyDictionary<int, ushort[]> Blocks => blocks;

        public static ushort Encode(double level) => (ushort)Math.Clamp(Math.Round((level - Base) * 10) + 1, 1, 65535);
        public static double Decode(ushort v) => v == 0 ? double.NaN : (v - 1) / 10.0 + Base;

        /// <summary>The level stored for a cell (row from the north, column from 180° W), NaN where no flood reaches.</summary>
        public double CellLevel(int row, int col)
        {
            if (row < 0 || row >= Rows) return double.NaN;
            col = (col % Cols + Cols) % Cols;
            return blocks.TryGetValue(row / Block * BlockCols + col / Block, out var b) ? Decode(b[row % Block * Block + col % Block]) : double.NaN;
        }

        /// <summary>
        /// The flood's level (m) at a place, NaN where no flood reaches: bilinear between the cell centres, and a corner the
        /// flood does not reach takes the mean of those it does.
        /// </summary>
        public double Level(double lat, double lon)
        {
            double fy = (90 - lat) * CellsPerDegree - .5, fx = (lon + 180) * CellsPerDegree - .5;
            int r = (int)Math.Floor(fy), c = (int)Math.Floor(fx);
            double ty = fy - r, tx = fx - c;
            double a = CellLevel(r, c), b = CellLevel(r, c + 1), d = CellLevel(r + 1, c), e = CellLevel(r + 1, c + 1);
            int n = 0;
            double sum = 0;
            if (!double.IsNaN(a)) { sum += a; n++; }
            if (!double.IsNaN(b)) { sum += b; n++; }
            if (!double.IsNaN(d)) { sum += d; n++; }
            if (!double.IsNaN(e)) { sum += e; n++; }
            if (n == 0) return double.NaN;
            if (n < 4)
            {
                double m = sum / n;
                if (double.IsNaN(a)) a = m;
                if (double.IsNaN(b)) b = m;
                if (double.IsNaN(d)) d = m;
                if (double.IsNaN(e)) e = m;
            }
            return (a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty;
        }

        /// <summary>Whether ground at <paramref name="elev"/> (m, the column's real height) lies in the flood here.</summary>
        public bool Floods(double lat, double lon, double elev)
        {
            double level = Level(lat, lon);
            return !double.IsNaN(level) && elev <= level;
        }

        public byte[] Write()
        {
            var keys = blocks.Keys.OrderBy(k => k).ToArray();
            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(Magic));
            ms.Write(BitConverter.GetBytes(FileVersion));
            ms.Write(BitConverter.GetBytes(CellsPerDegree));
            ms.Write(BitConverter.GetBytes(Block));
            ms.Write(BitConverter.GetBytes(keys.Length));
            using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, true))
            {
                foreach (int k in keys) z.Write(BitConverter.GetBytes(k));
                var buf = new byte[Block * Block * 2];
                foreach (int k in keys)
                {
                    var b = blocks[k];
                    ushort prev = 0;
                    for (int i = 0; i < b.Length; i++)
                    {
                        ushort d = unchecked((ushort)(b[i] - prev));
                        buf[2 * i] = (byte)d;
                        buf[2 * i + 1] = (byte)(d >> 8);
                        prev = b[i];
                    }
                    z.Write(buf);
                }
            }
            return ms.ToArray();
        }

        public static FloodplainGrid Read(byte[] file)
        {
            if (file == null || file.Length < HeaderBytes || Encoding.ASCII.GetString(file, 0, 4) != Magic) throw new FormatException("not a floodplain grid (EFP1)");
            int version = BitConverter.ToInt32(file, 4), cpd = BitConverter.ToInt32(file, 8), block = BitConverter.ToInt32(file, 12), count = BitConverter.ToInt32(file, 16);
            if (version != FileVersion || cpd != CellsPerDegree || block != Block || count < 0 || count > BlockRows * BlockCols)
                throw new FormatException($"floodplain grid has a bad header (version {version}, {cpd} cells per degree, blocks of {block}, {count} blocks)");
            using var z = new DeflateStream(new MemoryStream(file, HeaderBytes, file.Length - HeaderBytes), CompressionMode.Decompress);
            var keyBytes = ReadExactly(z, count * 4);
            var blocks = new Dictionary<int, ushort[]>(count);
            var buf = new byte[Block * Block * 2];
            int last = -1;
            for (int n = 0; n < count; n++)
            {
                int k = BitConverter.ToInt32(keyBytes, 4 * n);
                if (k <= last || k >= BlockRows * BlockCols) throw new FormatException("floodplain grid has a bad block key");
                last = k;
                Fill(z, buf);
                var cells = new ushort[Block * Block];
                ushort prev = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    prev = unchecked((ushort)(prev + (buf[2 * i] | buf[2 * i + 1] << 8)));
                    cells[i] = prev;
                }
                blocks[k] = cells;
            }
            if (z.ReadByte() != -1) throw new FormatException("floodplain grid has data after its cells");
            return new FloodplainGrid(blocks);
        }

        static byte[] ReadExactly(Stream s, int n)
        {
            var b = new byte[n];
            Fill(s, b);
            return b;
        }

        static void Fill(Stream s, byte[] b)
        {
            int o = 0;
            while (o < b.Length)
            {
                int r = s.Read(b, o, b.Length - o);
                if (r <= 0) throw new FormatException("floodplain grid is cut short");
                o += r;
            }
        }
    }
}
