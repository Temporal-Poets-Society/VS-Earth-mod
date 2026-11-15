using System;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>The type of one plane's cells as a file stores them.</summary>
    public enum PlaneType : byte { U8, I8, I16, U16 }

    /// <summary>
    /// Predicted planes (EA-0077, code review 14 November 2026 ER-02): the coding of the client climate files from 2.0.0-rc.14
    /// on (rain.bin, aridity.bin, temperature.bin, climate.bin, weather.bin), the bytes of a header version with
    /// <see cref="Coding"/> in its high 16 bits. Brotli has no model for a smooth 2-D field; a spatial predictor in front of it
    /// does what PNG's filters do, so the same values take about a third less room. Exact integer steps throughout: a file
    /// decodes to the very values of the plain brotli file it was made from (tools/planecoding.py writes and checks it), so
    /// no rules version.
    ///
    /// A block (after brotli) is, for each of its planes, three bytes: the predictor (<see cref="Predictor"/>), a step (the
    /// plane's values are multiples of it: 0.5 °C in temperature.bin's tenths) and a reference plane (0 for none, else 1 + the
    /// index of an earlier plane of the block whose value at the cell is subtracted first: climate.bin's coldest and warmest
    /// month from the mean). Then each plane's residuals, row by row: one byte a cell for an 8-bit plane, for a 16-bit plane
    /// the low bytes of all its cells and then the high bytes, each residual zigzag-coded.
    ///
    /// A cell's value v, in its type's range, is d = v - ref (wrapped to the type), q = d / step; the predictor guesses q
    /// from the cells already decoded (west, north, north-west; the first row from the west, the first column from the
    /// north, the first cell 0) and the residual is q minus the guess, modulo 2^bits. Decoding reverses each step.
    /// </summary>
    public static class PlaneCoding
    {
        /// <summary>The coding in a header version's high 16 bits: plain brotli of the planes (every file before rc.14, and
        /// the data store's grids: summits.bin, glaciers.bin).</summary>
        public const int Plain = 0;
        /// <summary>The coding in a header version's high 16 bits: predicted planes, as here.</summary>
        public const int Predicted = 1;

        /// <summary>The content version of a header version (the low 16 bits).</summary>
        public static int ContentVersion(int headerVersion) => headerVersion & 0xFFFF;
        /// <summary>The coding of a header version (the high 16 bits).</summary>
        public static int Coding(int headerVersion) => (int)((uint)headerVersion >> 16);

        /// <summary>The predictors a plane can name.</summary>
        public enum Predictor : byte { None = 0, Left = 1, Up = 2, Med = 3, Average = 4, Planar = 5 }

        public static int Bytes(PlaneType t) => t is PlaneType.I16 or PlaneType.U16 ? 2 : 1;

        /// <summary>The decompressed size of a block of these planes over <paramref name="cells"/> cells.</summary>
        public static int BlockSize(ReadOnlySpan<PlaneType> types, int cells)
        {
            long size = 3L * types.Length;
            for (int p = 0; p < types.Length; p++) size += (long)Bytes(types[p]) * cells;
            if (size > int.MaxValue) throw new InvalidDataException("predicted planes: too large");
            return (int)size;
        }

        /// <summary>The bytes a plain file holds for these planes: each plane's cells row by row, little-endian at its type's
        /// width, the planes one after the other.</summary>
        public static int RawSize(ReadOnlySpan<PlaneType> types, int cells)
        {
            long size = 0;
            for (int p = 0; p < types.Length; p++) size += (long)Bytes(types[p]) * cells;
            return checked((int)size);
        }

        /// <summary>
        /// Brotli of a block of <paramref name="types"/>.Length planes of rows x cols cells in <paramref name="file"/> at
        /// [offset, offset + length), decoded to the layout a plain file's brotli holds (<see cref="RawSize"/>). Throws an
        /// InvalidDataException naming <paramref name="what"/> when the block is damaged.
        /// </summary>
        public static byte[] Decode(byte[] file, int offset, int length, int rows, int cols, ReadOnlySpan<PlaneType> types, string what)
        {
            int cells = checked(rows * cols);
            byte[] block = EarthBinary.Unbrotli(file, offset, length, BlockSize(types, cells), what);
            var raw = new byte[RawSize(types, cells)];
            Decode(block, rows, cols, types, raw, what);
            return raw;
        }

        /// <summary>A decompressed block into the plain layout <paramref name="raw"/>.</summary>
        public static void Decode(ReadOnlySpan<byte> block, int rows, int cols, ReadOnlySpan<PlaneType> types, Span<byte> raw, string what)
        {
            int planes = types.Length, cells = rows * cols;
            if (block.Length != BlockSize(types, cells) || raw.Length != RawSize(types, cells))
                throw new InvalidDataException(what + ": predicted planes of the wrong size");
            Span<int> rawAt = planes <= 64 ? stackalloc int[planes] : new int[planes];
            for (int p = 0, at = 0; p < planes; p++) { rawAt[p] = at; at += Bytes(types[p]) * cells; }
            int res = 3 * planes;
            // one scratch array per thread for tiles (32,400 ints would be large-object garbage on every cache miss, L-5); a
            // block above a tile's size (climate.bin, weather.bin: once per load) gets its own, so no thread keeps 4 MB
            int[] q;
            if (cells > ScratchCells) q = new int[cells];
            else if ((q = scratch) == null) scratch = q = new int[ScratchCells];
            for (int p = 0; p < planes; p++)
            {
                var type = types[p];
                var pred = (Predictor)block[3 * p];
                int step = block[3 * p + 1], refPlane = block[3 * p + 2] - 1;
                if (pred > Predictor.Planar || step == 0 || refPlane >= p || refPlane >= 0 && types[refPlane] != type)
                    throw new InvalidDataException(what + ": plane " + p + " has an unknown coding");
                int width = Bytes(type);
                Residuals(block.Slice(res, width * cells), type, pred, rows, cols, q);
                res += width * cells;
                var dst = raw.Slice(rawAt[p], width * cells);
                if (refPlane < 0 && step == 1)
                {
                    Store(q, type, dst);
                    continue;
                }
                if (refPlane < 0)
                {
                    // values in steps (temperature.bin's 0.5 °C): q * step stays in the type's range
                    for (int i = 0; i < cells; i++) q[i] *= step;
                }
                else
                {
                    var refRaw = raw.Slice(rawAt[refPlane], width * cells);
                    for (int i = 0; i < cells; i++) q[i] = Wrap(q[i] * step + Load(refRaw, type, i), type);
                }
                Store(q, type, dst);
            }
        }

        [ThreadStatic] static int[] scratch;
        /// <summary>The scratch a thread keeps: a 256 x 256 tile's cells (the client grids' tiles are 180 x 180).</summary>
        const int ScratchCells = 65536;

        /// <summary>A value wrapped into its type's range (modulo 2^bits).</summary>
        internal static int Wrap(int v, PlaneType t) => t switch
        {
            PlaneType.U8 => v & 0xFF,
            PlaneType.I8 => (sbyte)v,
            PlaneType.I16 => (short)v,
            _ => v & 0xFFFF,
        };

        /// <summary>Cell i of a plane in the plain layout.</summary>
        internal static int Load(ReadOnlySpan<byte> raw, PlaneType t, int i) => t switch
        {
            PlaneType.U8 => raw[i],
            PlaneType.I8 => (sbyte)raw[i],
            PlaneType.I16 => (short)(raw[2 * i] | raw[2 * i + 1] << 8),
            _ => raw[2 * i] | raw[2 * i + 1] << 8,
        };

        /// <summary>A 16-bit zigzag code back to its signed number.</summary>
        internal static int UnZigzag(int z) => (z >> 1) ^ -(z & 1);

        /// <summary>Stores v at cell i of a plane in the plain layout.</summary>
        internal static void Put(Span<byte> raw, PlaneType t, int i, int v)
        {
            if (Bytes(t) == 1) raw[i] = (byte)v;
            else { raw[2 * i] = (byte)v; raw[2 * i + 1] = (byte)(v >> 8); }
        }

        /// <summary>The plane's values (the first dst.Length / width of q, which may be a larger scratch array) into dst.</summary>
        static void Store(int[] q, PlaneType t, Span<byte> dst)
        {
            if (Bytes(t) == 1)
                for (int i = 0; i < dst.Length; i++) dst[i] = (byte)q[i];
            else
                for (int i = 0, n = dst.Length / 2; i < n; i++) { dst[2 * i] = (byte)q[i]; dst[2 * i + 1] = (byte)(q[i] >> 8); }
        }

        /// <summary>
        /// One plane's residuals back to its values q (after the step, before the reference plane): the residuals first
        /// (they become the values in place, each read before its cell is written), then one loop per predictor. Wrapping
        /// to the type is ((x + bias) &amp; mask) - bias, the same as <see cref="Wrap"/>.
        /// </summary>
        static void Residuals(ReadOnlySpan<byte> res, PlaneType t, Predictor pred, int rows, int cols, int[] q)
        {
            int cells = rows * cols;
            int mask = Bytes(t) == 2 ? 0xFFFF : 0xFF, bias = t is PlaneType.I8 or PlaneType.I16 ? (mask + 1) >> 1 : 0;
            var d = q.AsSpan(0, cells);
            if (mask == 0xFFFF)
            {
                var lo = res.Slice(0, cells);
                var hi = res.Slice(cells, cells);
                for (int i = 0; i < d.Length; i++) d[i] = UnZigzag(lo[i] | hi[i] << 8);
            }
            else
            {
                var b = res.Slice(0, cells);
                for (int i = 0; i < d.Length; i++) d[i] = b[i];
            }
            if (pred == Predictor.None)
            {
                for (int i = 0; i < d.Length; i++) d[i] = ((d[i] + bias) & mask) - bias;
                return;
            }
            // the first row from the west (its first cell from 0), the first column from the north
            d[0] = ((d[0] + bias) & mask) - bias;
            for (int c = 1; c < cols; c++) d[c] = ((d[c - 1] + d[c] + bias) & mask) - bias;
            for (int r = 1; r < rows; r++)
            {
                int row = r * cols, up = row - cols;
                d[row] = ((d[up] + d[row] + bias) & mask) - bias;
                switch (pred)
                {
                    case Predictor.Left:
                        for (int i = row + 1; i < row + cols; i++) d[i] = ((d[i - 1] + d[i] + bias) & mask) - bias;
                        break;
                    case Predictor.Up:
                        for (int i = row + 1; i < row + cols; i++) d[i] = ((d[i - cols] + d[i] + bias) & mask) - bias;
                        break;
                    case Predictor.Med:
                        for (int i = row + 1; i < row + cols; i++)
                        {
                            int w = d[i - 1], n = d[i - cols], nw = d[i - cols - 1];
                            int min = Math.Min(w, n), max = Math.Max(w, n);
                            int guess = nw >= max ? min : nw <= min ? max : w + n - nw;
                            d[i] = ((guess + d[i] + bias) & mask) - bias;
                        }
                        break;
                    case Predictor.Average:
                        for (int i = row + 1; i < row + cols; i++) d[i] = ((((d[i - 1] + d[i - cols]) >> 1) + d[i] + bias) & mask) - bias;
                        break;
                    default:
                        for (int i = row + 1; i < row + cols; i++) d[i] = ((d[i - 1] + d[i - cols] - d[i - cols - 1] + d[i] + bias) & mask) - bias;
                        break;
                }
            }
        }
    }
}
