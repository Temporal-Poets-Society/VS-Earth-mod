using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>
    /// Vanilla draws each crafted stone block (cobblestone, ashlar, drystone, polished rock ...) as one shared template per
    /// block type, recoloured for every rock: granite's and basalt's cobblestone are the same image in two palettes. Earth's
    /// own rocks follow that: their crafted textures are the nearest vanilla rock's template moved into the Earth rock's
    /// palette (EarthStoneTextures does it in the client when assets load, so no vanilla pixels ship with Earth).
    /// Pure functions on ARGB pixel arrays (0xAARRGGBB, as BitmapRef.Pixels gives them).
    /// </summary>
    public static class StoneRecolour
    {
        /// <summary>Vanilla 1.22 texture folders under block/stone/ drawn from a shared template (checked against the
        /// game's files: cobblestone 1.00 correlation between rocks, ashlar and drystone 0.7-0.98). Rock, gravel, plain
        /// sand and termite mounds are drawn per rock in vanilla and stay Earth's own.</summary>
        public static readonly string[] TemplateDirs =
        {
            "cobblestone", "cobblestone/fanned", "cobblestoneslab", "cobblestonestairs", "cobbleskull", "brick", "agedbrick",
            "crackedbrick", "drystone", "drystonefence", "polishedrock", "polishedrockslab", "polishedrock-old/cobbled",
            "polishedrock-old/full", "polishedrock-old/inside", "sand/wavy"
        };

        /// <summary>Folders whose palette is the rock's sand rather than the rock itself.</summary>
        public static bool SandPalette(string dir) => dir.StartsWith("sand", StringComparison.Ordinal);

        /// <summary>Mean luma and chroma (YCbCr) and the luma spread of a set of images.</summary>
        public readonly struct Palette
        {
            public readonly double Y, Cb, Cr, SpreadY;
            public Palette(double y, double cb, double cr, double spreadY) { Y = y; Cb = cb; Cr = cr; SpreadY = spreadY; }
            public override string ToString() => FormattableString.Invariant($"Y {Y:0} Cb {Cb:0.0} Cr {Cr:0.0} sY {SpreadY:0.0}");
        }

        static void Ycc(int argb, out double y, out double cb, out double cr)
        {
            double r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            y = .299 * r + .587 * g + .114 * b;
            cb = (b - y) * .564;
            cr = (r - y) * .713;
        }

        public static Palette Measure(IEnumerable<int[]> images)
        {
            double sy = 0, sy2 = 0, scb = 0, scr = 0;
            long n = 0;
            foreach (var img in images)
                foreach (int p in img)
                {
                    if (((p >> 24) & 255) < 128) continue;
                    Ycc(p, out double y, out double cb, out double cr);
                    sy += y; sy2 += y * y; scb += cb; scr += cr; n++;
                }
            if (n == 0) return new Palette(128, 0, 0, 1);
            double my = sy / n;
            return new Palette(my, scb / n, scr / n, Math.Sqrt(Math.Max(1e-6, sy2 / n - my * my)));
        }

        /// <summary>The reference palette closest to ours (chroma weighted 1.5).</summary>
        public static string Nearest(IReadOnlyDictionary<string, Palette> references, Palette ours)
            => references.OrderBy(kv => Sq(kv.Value.Y - ours.Y) + Sq(1.5 * (kv.Value.Cb - ours.Cb)) + Sq(1.5 * (kv.Value.Cr - ours.Cr)))
                         .ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).FirstOrDefault();

        static double Sq(double v) => v * v;

        /// <summary>
        /// Moves an image from one rock's palette into another's, keeping its shading: luma shifted to the new mean with the
        /// spread scaled by the ratio of the rocks' spreads (held to 0.85-1.2); chroma rotated from the old hue to the new
        /// and scaled by the ratio of saturations (held to 0.4-2.5), or shifted when the old palette is nearly grey.
        /// Alpha is kept.
        /// </summary>
        public static int[] Transfer(int[] argb, Palette from, Palette to)
        {
            double g = Math.Clamp(to.SpreadY / Math.Max(from.SpreadY, 1e-3), .85, 1.2);
            double mf = Math.Sqrt(from.Cb * from.Cb + from.Cr * from.Cr), mt = Math.Sqrt(to.Cb * to.Cb + to.Cr * to.Cr);
            bool rotate = mf >= 3;
            double t = rotate ? Math.Atan2(to.Cr, to.Cb) - Math.Atan2(from.Cr, from.Cb) : 0;
            double k = rotate ? Math.Clamp(mt / mf, .4, 2.5) : 1, cos = Math.Cos(t) * k, sin = Math.Sin(t) * k;
            var outp = new int[argb.Length];
            for (int i = 0; i < argb.Length; i++)
            {
                int p = argb[i];
                Ycc(p, out double y, out double cb, out double cr);
                y = to.Y + g * (y - from.Y);
                double ncb, ncr;
                if (rotate) { ncb = cb * cos - cr * sin; ncr = cb * sin + cr * cos; }
                else { ncb = cb + to.Cb - from.Cb; ncr = cr + to.Cr - from.Cr; }
                double r = y + ncr / .713, b = y + ncb / .564, gg = (y - .299 * r - .114 * b) / .587;
                outp[i] = (p & unchecked((int)0xFF000000)) | (Byte(r) << 16) | (Byte(gg) << 8) | Byte(b);
            }
            return outp;
        }

        static int Byte(double v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)Math.Round(v);

        /// <summary>RGBA PNG (8 bits per channel, no filtering) of an ARGB image.</summary>
        public static byte[] EncodePng(int[] argb, int width, int height)
        {
            if (argb.Length != width * height) throw new ArgumentException("pixel count does not match the size");
            var raw = new byte[height * (width * 4 + 1)];
            for (int y = 0, o = 0; y < height; y++)
            {
                raw[o++] = 0;
                for (int x = 0; x < width; x++)
                {
                    int p = argb[y * width + x];
                    raw[o++] = (byte)(p >> 16); raw[o++] = (byte)(p >> 8); raw[o++] = (byte)p; raw[o++] = (byte)(p >> 24);
                }
            }
            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                idat = ms.ToArray();
            }
            using var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, width); BigEndian(ihdr, 4, height);
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", idat);
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static void BigEndian(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            BigEndian(head, 0, data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, 0, data.Length);
            uint crc = Crc(Crc(0xFFFFFFFFu, head, 4, 4), data, 0, data.Length) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            BigEndian(c, 0, unchecked((int)crc));
            s.Write(c, 0, 4);
        }

        static uint[] crcTable;
        static uint Crc(uint crc, byte[] buf, int start, int len)
        {
            var table = crcTable ??= Enumerable.Range(0, 256).Select(n =>
            {
                uint c = (uint)n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                return c;
            }).ToArray();
            for (int i = start; i < start + len; i++) crc = table[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
