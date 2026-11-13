using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// The share of each grid cell (0.1 degree, 84 N to 60 S) covered by seven groups of wetland vegetation, aggregated by
    /// tools/bake_wetlands.py from the Global Lakes and Wetlands Database v2 (Lehner et al. 2025, CC BY 4.0; see
    /// config/wetland-attribution.txt): open peatland (bog), wooded peatland, flooded swamp forest, flooded open marsh and
    /// delta, mangrove, saltmarsh, and damp ground (seasonally saturated or ephemeral, whose open or wooded state in GLWD
    /// is mostly today's farming). Lakes, rivers, salt pans and rice paddies are not in it. Each 15" GLWD pixel's wetland
    /// area counts for its dominant class, and sea counts as dry, so a coastal cell's share is of the whole cell.
    /// Earth ships this grid (assets/earth/config/wetlands.bin, about 2 MB); WetlandTests checks it against known wetlands.
    /// File: "EWL1", west, north, step (doubles), columns, rows, layer count (ints), each layer's name (a byte length, then
    /// ASCII), then every layer's cells (layer-major, rows from the north) as shares 0..250, deflated in one stream.
    /// Cells are kept in 16 x 16 blocks and blocks without any wetland are not stored (a few MB in memory, not the full grid).
    /// </summary>
    public sealed class WetlandGrid
    {
        public const string Asset = "earth:config/wetlands.bin";
        const string Magic = "EWL1";
        const int HeaderBytes = 40;
        const int B = 16;
        public const int Bog = 0, PeatForest = 1, SwampForest = 2, Marsh = 3, Mangrove = 4, SaltMarsh = 5, Damp = 6;
        public double West { get; private set; }
        public double North { get; private set; }
        public double Step { get; private set; }
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        /// <summary>The layer names in file order ("bog", "peatforest", "swampforest", "marsh", "mangrove", "saltmarsh", "damp").</summary>
        public string[] Layers { get; private set; }
        byte[][][] blocks;    // [layer][block row * blockCols + block col]: B x B shares, null where all are 0
        int blockCols;
        bool global;          // the columns span 360 degrees and wrap

        WetlandGrid() { }

        public static WetlandGrid Read(byte[] file)
        {
            if (file == null || file.Length < HeaderBytes || Encoding.ASCII.GetString(file, 0, 4) != Magic) throw new FormatException("not a wetland grid (EWL1)");
            var g = new WetlandGrid
            {
                West = BitConverter.ToDouble(file, 4), North = BitConverter.ToDouble(file, 12), Step = BitConverter.ToDouble(file, 20),
                Cols = BitConverter.ToInt32(file, 28), Rows = BitConverter.ToInt32(file, 32),
            };
            int layers = BitConverter.ToInt32(file, 36);
            if (g.Cols <= 0 || g.Rows <= 0 || layers <= 0 || layers > 64 || !(g.Step > 0) || !double.IsFinite(g.Step)
                || !double.IsFinite(g.West) || !double.IsFinite(g.North) || (long)g.Cols * g.Rows > 50_000_000)
                throw new FormatException("wetland grid has a bad header");
            int o = HeaderBytes;
            g.Layers = new string[layers];
            for (int l = 0; l < layers; l++)
            {
                if (o >= file.Length || file[o] == 0 || o + 1 + file[o] > file.Length) throw new FormatException("wetland grid has a bad layer name");
                int n = file[o];
                for (int i = 0; i < n; i++) if (file[o + 1 + i] < 32 || file[o + 1 + i] > 126) throw new FormatException("wetland grid has a bad layer name");
                g.Layers[l] = Encoding.ASCII.GetString(file, o + 1, n);
                o += 1 + n;
            }
            g.global = Math.Abs(g.Cols * g.Step - 360) < 1e-6;
            g.blockCols = (g.Cols + B - 1) / B;
            int blockRows = (g.Rows + B - 1) / B;
            g.blocks = new byte[layers][][];
            var band = new byte[B * g.Cols];
            try
            {
                using var z = new DeflateStream(new MemoryStream(file, o, file.Length - o), CompressionMode.Decompress);
                for (int l = 0; l < layers; l++)
                {
                    var lb = g.blocks[l] = new byte[blockRows * g.blockCols][];
                    for (int br = 0; br < blockRows; br++)
                    {
                        int h = Math.Min(B, g.Rows - br * B), need = h * g.Cols, got = 0, n;
                        while (got < need && (n = z.Read(band, got, need - got)) > 0) got += n;
                        if (got != need) throw new FormatException("wetland grid is truncated");
                        if (band.AsSpan(0, need).IndexOfAnyInRange((byte)251, (byte)255) >= 0) throw new FormatException("wetland grid has a share above 250");
                        for (int bc = 0; bc < g.blockCols; bc++)
                        {
                            int c0 = bc * B, w = Math.Min(B, g.Cols - c0);
                            byte[] blk = null;
                            for (int y = 0; y < h; y++)
                            {
                                var row = band.AsSpan(y * g.Cols + c0, w);
                                if (row.IndexOfAnyExcept((byte)0) < 0) continue;
                                blk ??= new byte[B * B];
                                row.CopyTo(blk.AsSpan(y * B, w));
                            }
                            lb[br * g.blockCols + bc] = blk;
                        }
                    }
                }
                if (z.Read(band, 0, 1) != 0) throw new FormatException("wetland grid has data after its cells");
            }
            catch (InvalidDataException e) { throw new FormatException("wetland grid is corrupt: " + e.Message, e); }
            return g;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(Magic));
            ms.Write(BitConverter.GetBytes(West)); ms.Write(BitConverter.GetBytes(North)); ms.Write(BitConverter.GetBytes(Step));
            ms.Write(BitConverter.GetBytes(Cols)); ms.Write(BitConverter.GetBytes(Rows)); ms.Write(BitConverter.GetBytes(Layers.Length));
            foreach (var name in Layers) { ms.WriteByte((byte)name.Length); ms.Write(Encoding.ASCII.GetBytes(name)); }
            using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, true))
            {
                var row = new byte[Cols];
                for (int l = 0; l < Layers.Length; l++)
                    for (int r = 0; r < Rows; r++)
                    {
                        for (int c = 0; c < Cols; c++) row[c] = Cell(l, r, c);
                        z.Write(row);
                    }
            }
            return ms.ToArray();
        }

        /// <summary>0..1: the share of the layer's wetland here, bilinear between cell centres; 0 outside the grid.</summary>
        public double Share(int layer, double lat, double lon)
        {
            var lb = blocks[layer];
            if (!Locate(lat, lon, out int x0, out int x1, out int y0, out int y1, out double tx, out double ty)) return 0;
            return Bilinear(lb, x0, x1, y0, y1, tx, ty);
        }

        /// <summary>0..1: the share of all layers together here (clamped to 1).</summary>
        public double Total(double lat, double lon)
        {
            if (!Locate(lat, lon, out int x0, out int x1, out int y0, out int y1, out double tx, out double ty)) return 0;
            double sum = 0;
            foreach (var lb in blocks) sum += Bilinear(lb, x0, x1, y0, y1, tx, ty);
            return Math.Min(1, sum);
        }

        /// <summary>Version of the rules that open the forest over bog, marsh and saltmarsh (EarthParams.WetlandRules).</summary>
        public const int RulesVersion = 1;

        /// <summary>0..1: the open wetland here, where trees do not stand: bog, flooded marsh and delta, saltmarsh. Wooded peat,
        /// swamp forest, mangrove and damp ground keep their trees.</summary>
        public double OpenShare(double lat, double lon)
        {
            if (!Locate(lat, lon, out int x0, out int x1, out int y0, out int y1, out double tx, out double ty)) return 0;
            double sum = Bilinear(blocks[Bog], x0, x1, y0, y1, tx, ty) + Bilinear(blocks[Marsh], x0, x1, y0, y1, tx, ty)
                + Bilinear(blocks[SaltMarsh], x0, x1, y0, y1, tx, ty);
            return Math.Min(1, sum);
        }

        /// <summary>The pond rules that keep vanilla's surface ponds to the mapped wetlands (EarthParams.PondRules, the Earth tab's
        /// "wetlands"; EarthParams.PondRulesWetlands).</summary>
        public const int PondRulesVersion = 1;

        /// <summary>
        /// 0..1: the share of vanilla's surface pond tries kept here (pond rules 1; the author, 8 November 2026: "Wetlands
        /// only"): the mapped wetland, every group whole and damp ground at half, as the water table weighs them
        /// (water-table-v1.json). Lakes, rivers and their ponds are not in the grid, so none here keeps any.
        /// </summary>
        public double PondShare(double lat, double lon)
        {
            if (!Locate(lat, lon, out int x0, out int x1, out int y0, out int y1, out double tx, out double ty)) return 0;
            double sum = 0;
            for (int l = 0; l < blocks.Length; l++)
                sum += (l == Damp ? 0.5 : 1) * Bilinear(blocks[l], x0, x1, y0, y1, tx, ty);
            return Math.Min(1, sum);
        }

        /// <summary>Whether a surface pond try starting at block column (x, z) is kept where <paramref name="share"/> of them
        /// are: a frozen hash of the column (GeologyTransitions.Hash), so every layer vanilla adds to one pond gets the same
        /// answer and a world gets the same ponds each time.</summary>
        public static bool KeepsPond(double share, long seed, int x, int z)
            => share > 0 && (share >= 1 || GeologyTransitions.Hash(seed, x, 0, z, PondSalt) < share);

        const int PondSalt = 0x504F4E44;

        /// <summary>The stored byte (share x 250) of a cell, rows from the north, columns from the west.</summary>
        public byte Cell(int layer, int row, int col)
        {
            if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols) throw new ArgumentOutOfRangeException(nameof(row), "outside the wetland grid");
            return Get(blocks[layer], col, row);
        }

        bool Locate(double lat, double lon, out int x0, out int x1, out int y0, out int y1, out double tx, out double ty)
        {
            x0 = x1 = y0 = y1 = 0; tx = ty = 0;
            double fx = (lon - West) / Step - 0.5, fy = (North - lat) / Step - 0.5;
            if (!double.IsFinite(fx) || !(fy >= -0.5 && fy <= Rows - 0.5)) return false;
            fy = Math.Clamp(fy, 0, Rows - 1);
            y0 = Math.Min((int)fy, Rows - 1); y1 = Math.Min(y0 + 1, Rows - 1); ty = fy - y0;
            if (global)
            {
                fx -= Math.Floor(fx / Cols) * Cols;
                x0 = Math.Min((int)fx, Cols - 1); x1 = (x0 + 1) % Cols; tx = fx - x0;
            }
            else
            {
                if (fx < -0.5 || fx > Cols - 0.5) return false;
                fx = Math.Clamp(fx, 0, Cols - 1);
                x0 = Math.Min((int)fx, Cols - 1); x1 = Math.Min(x0 + 1, Cols - 1); tx = fx - x0;
            }
            return true;
        }

        double Bilinear(byte[][] lb, int x0, int x1, int y0, int y1, double tx, double ty) =>
            (Get(lb, x0, y0) * (1 - tx) * (1 - ty) + Get(lb, x1, y0) * tx * (1 - ty) + Get(lb, x0, y1) * (1 - tx) * ty + Get(lb, x1, y1) * tx * ty) / 250.0;

        byte Get(byte[][] lb, int x, int y)
        {
            var blk = lb[(y / B) * blockCols + x / B];
            return blk == null ? (byte)0 : blk[(y % B) * B + x % B];
        }
    }
}
