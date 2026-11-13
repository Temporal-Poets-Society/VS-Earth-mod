using System.Buffers;
using System.IO.Compression;

namespace Glo90Coarse
{
    /// <summary>
    /// The Brotli tile coding first measured (as decision 0051's tiles: median edge prediction, zigzag, byte planes, Brotli),
    /// kept only to compare sizes; the packs use CoarseElevation's arithmetic coder.
    /// </summary>
    static class BrotliCodec
    {
        static int Predict(int[] v, int cols, int r, int c)
        {
            int w = c > 0 ? v[r * cols + c - 1] : 0;
            int n = r > 0 ? v[(r - 1) * cols + c] : 0;
            int nw = r > 0 && c > 0 ? v[(r - 1) * cols + c - 1] : 0;
            int lo = Math.Min(w, n), hi = Math.Max(w, n);
            return nw >= hi ? lo : nw <= lo ? hi : w + n - nw;
        }

        static int Zig(int r) => (r << 1) ^ (r >> 31);

        static byte[] Pack(byte[] planes, int quality)
        {
            var packed = new byte[BrotliEncoder.GetMaxCompressedLength(planes.Length)];
            using var enc = new BrotliEncoder(quality, 22);
            if (enc.Compress(planes, packed, out int used, out int written, true) != OperationStatus.Done || used != planes.Length)
                throw new InvalidOperationException("Brotli");
            Array.Resize(ref packed, written);
            return packed;
        }

        /// <summary>Mask plane, then zigzag residual low and high planes; a cell without land takes its prediction.</summary>
        public static byte[] Heights(int[] codes, bool[] land, int cols, int rows, int quality)
        {
            int n = cols * rows;
            var v = new int[n];
            var planes = new byte[3 * n];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c, p = Predict(v, cols, r, c);
                    if (!land[i]) { v[i] = p; continue; }
                    v[i] = codes[i];
                    int z = Zig(codes[i] - p);
                    planes[i] = 1; planes[n + i] = (byte)z; planes[2 * n + i] = (byte)(z >> 8);
                }
            return Pack(planes, quality);
        }

        public static byte[] Roughness(int[] codes, int cols, int rows, int quality)
        {
            int n = cols * rows;
            var plane = new byte[n];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    plane[r * cols + c] = (byte)Zig(codes[r * cols + c] - Predict(codes, cols, r, c));
            return Pack(plane, quality);
        }
    }
}
