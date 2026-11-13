using System;
using System.IO;
using System.IO.Compression;

namespace EarthMap.Core
{
    /// <summary>Minimal RGB PNG writer (no dependencies).</summary>
    public static class Png
    {
        public static void WriteRgb(string path, int w, int h, byte[] rgb)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, w);
            WriteBE(ihdr, 4, h);
            ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
            Chunk(fs, "IHDR", ihdr);
            // the compressed image goes out in IDAT chunks as it is made (no second copy of a large image in memory)
            using (var idat = new IdatStream(fs))
            using (var z = new ZLibStream(idat, CompressionLevel.Fastest, true))
            {
                for (int y = 0; y < h; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgb, y * w * 3, w * 3);
                }
            }
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        /// <summary>Writes what it is given as IDAT chunks of up to 256 KiB.</summary>
        sealed class IdatStream : Stream
        {
            readonly Stream target;
            readonly byte[] buffer = new byte[256 * 1024];
            int used;
            public IdatStream(Stream target) { this.target = target; }
            public override void Write(byte[] data, int offset, int count) => Write(data.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> data)
            {
                while (data.Length > 0)
                {
                    int n = Math.Min(data.Length, buffer.Length - used);
                    data.Slice(0, n).CopyTo(buffer.AsSpan(used));
                    used += n;
                    data = data.Slice(n);
                    if (used == buffer.Length) Flush();
                }
            }
            public override void Flush()
            {
                if (used == 0) return;
                Chunk(target, "IDAT", buffer.AsSpan(0, used));
                used = 0;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) Flush();
                base.Dispose(disposing);
            }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
        }

        static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
        {
            var len = new byte[4];
            WriteBE(len, 0, data.Length);
            s.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t);
            s.Write(data);
            uint crc = Crc(t, 0xFFFFFFFF);
            crc = Crc(data, crc) ^ 0xFFFFFFFF;
            var c = new byte[4];
            WriteBE(c, 0, (int)crc);
            s.Write(c);
        }

        static void WriteBE(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        static uint[] table;

        static uint Crc(ReadOnlySpan<byte> data, uint crc)
        {
            if (table == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                table = t;
            }
            for (int i = 0; i < data.Length; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }

    /// <summary>Colours for map renders: height tint for land, depth for water, white for ice.</summary>
    public static class MapColors
    {
        // green lowlands -> yellow-brown hills -> grey-white peaks
        static readonly double[] LandStops = { 0, 0.08, 0.25, 0.5, 0.75, 1 };
        static readonly double[,] LandColours = { { 80, 130, 65 }, { 120, 155, 80 }, { 175, 160, 100 }, { 150, 120, 90 }, { 185, 180, 175 }, { 255, 255, 255 } };

        public static void Column(in TerrainModel.Column c, int sea, int mapHeight, out byte r, out byte g, out byte b)
        {
            if (c.Water != TerrainModel.WaterNone)
            {
                double depth = Math.Clamp((c.WaterTop - c.Top) / 60.0, 0, 1);
                if (c.Water == TerrainModel.WaterSea)
                {
                    r = (byte)(120 - 105 * depth); g = (byte)(170 - 130 * depth); b = (byte)(225 - 120 * depth);
                }
                else
                {
                    r = (byte)(70 - 40 * depth); g = (byte)(175 - 80 * depth); b = (byte)(190 - 60 * depth);
                }
                if (c.Gutter) { r = (byte)(r * 0.8); g = (byte)(g * 0.8); }
                return;
            }
            if (c.IceBottom <= c.Top)
            {
                r = 236; g = 242; b = 250;
                return;
            }
            double t = Math.Clamp((c.Top - sea) / (double)Math.Max(1, mapHeight - sea - 16), 0, 1);
            var xs = LandStops;
            var cs = LandColours;
            int i = 0;
            while (i < xs.Length - 2 && t > xs[i + 1]) i++;
            double f = (t - xs[i]) / (xs[i + 1] - xs[i]);
            r = (byte)(cs[i, 0] + (cs[i + 1, 0] - cs[i, 0]) * f);
            g = (byte)(cs[i, 1] + (cs[i + 1, 1] - cs[i, 1]) * f);
            b = (byte)(cs[i, 2] + (cs[i + 1, 2] - cs[i, 2]) * f);
        }

        /// <summary>Bytes Render needs for an image: the RGB result (the rest is two rows of heights per thread).</summary>
        public static long RenderBytes(long w, long h) => 3 * w * h;

        /// <summary>
        /// Renders an area of columns (step blocks per pixel) with hill shading. Bands of rows run in parallel, each keeping only
        /// its current and previous row of heights (it samples the row above it again), so memory is the RGB result alone.
        /// overlay (pixel i, j) may paint a pixel over the map (the tests draw the coast outlines with it).
        /// A sweep of the whole world: the bands run on a few workers (ParallelWork), each reading the built-in heights
        /// (FineElevation.BuiltInOnly, put back after), so that the picture decodes no GLO-90 tiles, keeps none on pool threads
        /// and doesn't push the generation thread's tiles out of the shared cache (CR-T1; at its 16 or more blocks a pixel the
        /// picture is the same).
        /// </summary>
        public static byte[] Render(TerrainModel model, double x0, double z0, int w, int h, double step, Func<int, int, (byte, byte, byte)?> overlay = null)
        {
            var rgb = new byte[RenderBytes(w, h)];
            int sea = model.SeaLevel, mh = model.Vertical.MapHeight;
            const int band = 64;
            ParallelWork.For((h + band - 1) / band,
                () => { bool own = FineElevation.BuiltInOnly; FineElevation.BuiltInOnly = true; return own; },
                (bi, _) =>
            {
                int j0 = bi * band, j1 = Math.Min(h, j0 + band);
                var up = new int[w];
                var row = new int[w];
                if (j0 > 0)
                    for (int i = 0; i < w; i++) up[i] = model.GetColumn(x0 + i * step, z0 + (j0 - 1) * step).Top;
                for (int j = j0; j < j1; j++)
                {
                    for (int i = 0; i < w; i++)
                    {
                        var c = model.GetColumn(x0 + i * step, z0 + j * step);
                        int top = row[i] = c.Top;
                        Column(c, sea, mh, out byte r, out byte g, out byte b);
                        if (c.Water == TerrainModel.WaterNone)
                        {
                            int left = i > 0 ? row[i - 1] : top;
                            int above = j > 0 ? up[i] : top;
                            double shade = Math.Clamp(1 + ((top - left) - (top - above)) * 0.12 / Math.Max(1, step / 4), 0.55, 1.35);
                            r = (byte)Math.Clamp(r * shade, 0, 255); g = (byte)Math.Clamp(g * shade, 0, 255); b = (byte)Math.Clamp(b * shade, 0, 255);
                        }
                        var o = overlay?.Invoke(i, j);
                        if (o.HasValue) (r, g, b) = o.Value;
                        long k = 3L * ((long)j * w + i);
                        rgb[k] = r; rgb[k + 1] = g; rgb[k + 2] = b;
                    }
                    (up, row) = (row, up);
                }
            }, own => FineElevation.BuiltInOnly = own);
            return rgb;
        }
    }
}
