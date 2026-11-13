using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EarthMap.Core
{
    // CoarseElevation's writer side (the packs' encoder, index and part headers): only this tool builds the packs, so the mod
    // ships the reader alone (src/Core/CoarseElevation.cs).
    public sealed partial class CoarseElevation
    {
        public sealed partial class Grid
        {
                /// <summary>Cells about square in metres: columns per degree = rows per degree × cos(the row's middle latitude).</summary>
                public static Grid Square(int rowsPerDegree, Func<int, int> nativeCols)
                {
                    var g = new Grid { RowsPerDegree = rowsPerDegree };
                    for (int b = 0; b < 180; b++)
                    {
                        int latS = b - 90;
                        g.NativeCols[b] = (ushort)nativeCols(latS);
                        g.Cols[b] = (ushort)Math.Max(1, (int)Math.Round(rowsPerDegree * Math.Cos((latS + 0.5) * Math.PI / 180)));
                    }
                    return g;
                }

                internal void Write(BinaryWriter w)
                {
                    w.Write((ushort)RowsPerDegree);
                    for (int b = 0; b < 180; b++) { w.Write(NativeCols[b]); w.Write(Cols[b]); }
                }
        }

        public sealed partial class Steps
        {
                public Steps(float[] from, float[] step, double topMetres)
                {
                    if (from.Length == 0 || from.Length != step.Length) throw new ArgumentException("steps");
                    From = from; Step = step; Base = from[0];
                    Prepare();
                    Codes = (int)Math.Ceiling(Position(topMetres)) + 1;
                    if (Codes >= NoLand) throw new ArgumentException("too many height codes");
                }

                internal void Write(BinaryWriter w)
                {
                    w.Write((byte)From.Length);
                    for (int s = 0; s < From.Length; s++) { w.Write(From[s]); w.Write(Step[s]); }
                    w.Write((ushort)Codes);
                }
        }

        /// <summary>
        /// Roughness codes: 0 is 0 m (also cells without land), code k ≥ 1 is <paramref name="first"/> × ratio^(k - 1),
        /// ratio chosen so the last code is <paramref name="last"/>.
        /// </summary>
        public static float[] RoughnessTable(int codes, double first, double last)
        {
            var t = new float[codes];
            double ratio = Math.Pow(last / first, 1.0 / (codes - 2));
            for (int k = 1; k < codes; k++) t[k] = (float)(first * Math.Pow(ratio, k - 1));
            return t;
        }

        /// <summary>The nearest roughness code (in log terms) to a spread in metres.</summary>
        public static int RoughnessCode(float[] table, double metres)
        {
            if (!(metres > 0)) return 0;
            // below the first step: 0 or the first step, whichever is nearer in log terms around half the first
            if (metres < table[1] * 0.5) return 0;
            double l = Math.Log(metres);
            int best = 1; double bestD = Math.Abs(l - Math.Log(table[1]));
            for (int k = 2; k < table.Length; k++)
            {
                double d = Math.Abs(l - Math.Log(table[k]));
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }

        static byte[] Brotli(byte[] planes, int quality)
        {
            var packed = new byte[BrotliEncoder.GetMaxCompressedLength(planes.Length)];
            using var enc = new BrotliEncoder(quality, 22);
            if (enc.Compress(planes, packed, out int used, out int written, true) != OperationStatus.Done || used != planes.Length)
                throw new InvalidOperationException("Brotli could not compress a tile");
            Array.Resize(ref packed, written);
            return packed;
        }

        /// <summary>
        /// A binary range coder (LZMA's carry scheme) with adaptive 12-bit probabilities; integer arithmetic only, so encode
        /// and decode agree on every machine.
        /// </summary>
        public sealed class RangeEncoder
        {
            ulong low;
            uint range = 0xFFFFFFFF;
            byte cache;
            long cacheSize = 1;
            readonly MemoryStream output = new();
            public int Shift = 5;

            public void Bit(ref ushort p, int bit)
            {
                uint bound = (range >> 12) * p;
                if (bit == 0) { range = bound; p += (ushort)((4096 - p) >> Shift); }
                else { low += bound; range -= bound; p -= (ushort)(p >> Shift); }
                while (range < (1u << 24)) { range <<= 8; ShiftLow(); }
            }

            void ShiftLow()
            {
                if ((uint)low < 0xFF000000u || (low >> 32) != 0)
                {
                    byte temp = cache;
                    do { output.WriteByte((byte)(temp + (byte)(low >> 32))); temp = 0xFF; } while (--cacheSize != 0);
                    cache = (byte)((uint)low >> 24);
                }
                cacheSize++;
                low = (uint)low << 8;
            }

            public byte[] Finish()
            {
                for (int i = 0; i < 5; i++) ShiftLow();
                return output.ToArray();
            }
        }

        sealed partial class ResidualModel
        {
                public void Encode(RangeEncoder enc, int ctx, int e)
                {
                    enc.Bit(ref Zero[ctx], e == 0 ? 0 : 1);
                    if (e == 0) return;
                    enc.Bit(ref Sign[ctx], e < 0 ? 1 : 0);
                    uint m = (uint)Math.Abs(e);                 // >= 1
                    int nb = 31 - System.Numerics.BitOperations.LeadingZeroCount(m); // bits below the leading one
                    for (int i = 0; i < nb; i++) enc.Bit(ref Len[ctx * MaxBits + i], 1);
                    if (nb < MaxBits - 1) enc.Bit(ref Len[ctx * MaxBits + nb], 0);
                    for (int i = nb - 1; i >= 0; i--)
                    {
                        int bit = (int)(m >> i) & 1;
                        if (i == nb - 1) enc.Bit(ref Mant[(ctx * MaxBits + nb) * 2], bit);
                        else if (i == nb - 2) enc.Bit(ref Mant[(ctx * MaxBits + nb) * 2 + 1], bit);
                        else enc.Bit(ref Low[nb * MaxBits + i], bit);
                    }
                }
        }

        public sealed partial class Scheme
        {
                internal void Write(BinaryWriter w)
                {
                    w.Write((byte)Predictor); w.Write((byte)Context); w.Write((byte)Shift);
                    w.Write((byte)Weights.Length);
                    foreach (int k in Weights) w.Write((short)k);
                }
        }

        /// <summary>
        /// A tile's heights, arithmetic coded: a flag byte (1: every cell has land), the land mask (contexts: the four
        /// neighbours' land), then each land cell's code minus its prediction, in the context of nearby activity. A cell
        /// without land isn't coded; it takes the median edge prediction so the predictor runs on across it.
        /// </summary>
        public static byte[] EncodeHeights(int[] codes, bool[] land, int cols, int rows, Scheme s)
        {
            int n = cols * rows;
            var v = new int[n];
            var err = new int[n];
            var m = new ResidualModel();
            var enc = new RangeEncoder { Shift = s.Shift };
            bool allLand = Array.TrueForAll(land, x => x);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (!allLand)
                    {
                        int mc = (c > 0 && land[i - 1] ? 1 : 0) | (r > 0 && land[i - cols] ? 2 : 0)
                               | (r > 0 && c > 0 && land[i - cols - 1] ? 4 : 0) | (r > 0 && c + 1 < cols && land[i - cols + 1] ? 8 : 0);
                        enc.Bit(ref m.Mask[mc], land[i] ? 1 : 0);
                    }
                    // a cell without land takes the median edge prediction (bounded by its neighbours, so a run of them can't drift)
                    if (!land[i]) { v[i] = Predict(v, cols, r, c); continue; }
                    int p = PredictWith(s, v, cols, r, c);
                    int ctx = ResidualModel.Context(Activity(s, v, err, cols, r, c));
                    int e = codes[i] - p;
                    m.Encode(enc, ctx, e);
                    v[i] = codes[i];
                    err[i] = Math.Abs(e);
                }
            byte[] body = enc.Finish();
            var res = new byte[body.Length + 1];
            res[0] = (byte)(allLand ? 1 : 0);
            body.CopyTo(res, 1);
            return res;
        }

        /// <summary>A tile's roughness codes, arithmetic coded like the heights (no mask).</summary>
        public static byte[] EncodeRoughness(int[] codes, int cols, int rows, Scheme s)
        {
            int n = cols * rows;
            var err = new int[n];
            var m = new ResidualModel();
            var enc = new RangeEncoder { Shift = s.Shift };
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c, p = Predict(codes, cols, r, c);
                    int ctx = ResidualModel.Context(Activity(s, codes, err, cols, r, c));
                    int e = codes[i] - p;
                    m.Encode(enc, ctx, e);
                    err[i] = Math.Abs(e);
                }
            return enc.Finish();
        }

        /// <summary>
        /// The index: "EGCI", format version, then Brotli of: the number of parts, a note (UTF-8), the height grid, its steps,
        /// the coding scheme, the roughness grid's rows per degree (0: no layer) with its grid and table, and the tiles (latS, lonW, part,
        /// offset, height bytes, roughness bytes).
        /// </summary>
        public static byte[] WriteIndex(int parts, string note, Grid heights, Steps steps, Scheme scheme, Grid rough, float[] roughTable, IList<TileEntry> tiles)
        {
            using var body = new MemoryStream();
            using (var w = new BinaryWriter(body, Encoding.UTF8, true))
            {
                w.Write((byte)parts);
                var n = Encoding.UTF8.GetBytes(note ?? "");
                w.Write((ushort)n.Length); w.Write(n);
                heights.Write(w);
                steps.Write(w);
                scheme.Write(w);
                if (rough == null) w.Write((ushort)0);
                else
                {
                    rough.Write(w);
                    w.Write((byte)roughTable.Length);
                    foreach (float f in roughTable) w.Write(f);
                }
                w.Write(tiles.Count);
                foreach (var t in tiles)
                {
                    w.Write(t.LatS); w.Write(t.LonW); w.Write(t.Part);
                    w.Write(t.Offset); w.Write(t.HeightBytes); w.Write(t.RoughBytes);
                }
            }
            byte[] packed = Brotli(body.ToArray(), 11);
            using var ms = new MemoryStream();
            ms.Write(IndexMagic);
            ms.WriteByte(Version);
            ms.Write(packed);
            return ms.ToArray();
        }

        /// <summary>A part's header ("EGCP", format version, part number, three bytes spare); payloads follow it.</summary>
        public static byte[] PartHeader(int part) => new byte[] { PartMagic[0], PartMagic[1], PartMagic[2], PartMagic[3], Version, (byte)part, 0, 0 };
    }
}
