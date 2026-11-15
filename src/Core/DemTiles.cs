using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace EarthMap.Core
{
    /// <summary>
    /// Copernicus DEM tiles (decision 0051): names, the published GeoTIFF read without native libraries, and Earth's own
    /// compressed tile file. One tile covers one degree: latitude LatS..LatS+1, longitude LonW..LonW+1. Its rows run from
    /// north to south, 1200 of them for GLO-90; its columns get fewer towards the poles (1200 below 50°, 800, 600, 400,
    /// 240, 120). Height (r, c) is the point at latitude LatS + 1 - r / Rows and longitude LonW + c / Cols (Copernicus's
    /// PixelIsPoint): the tile's north and west edges are its own, its south and east edges the next tiles' first row and column.
    /// </summary>
    public static class DemTiles
    {
        /// <summary>Earth's tile format (EGLO): see <see cref="Encode"/>.</summary>
        public const int FormatVersion = 1;
        /// <summary>Converter rules: how the source's heights become the stored ones (1: rounded to whole metres).</summary>
        public const int ConverterVersion = 1;
        public const byte SourceGlo90 = 1;
        public const int Glo90Rows = 1200;
        /// <summary>Most cells a tile may declare (GLO-90's are at most 1200 x 1200).</summary>
        const long MaxCells = 4096L * 4096;
        static readonly byte[] Magic = { (byte)'E', (byte)'G', (byte)'L', (byte)'O' };

        /// <summary>GLO-90's columns per tile at a tile's latitude (its south edge), from the product handbook's spacing.</summary>
        public static int Glo90Cols(int latS)
        {
            int a = latS >= 0 ? latS : -latS - 1; // the band of the tile's equator-side edge
            return a < 50 ? 1200 : a < 60 ? 800 : a < 70 ? 600 : a < 80 ? 400 : a < 85 ? 240 : 120;
        }

        /// <summary>Short name of a square, as Earth's files are named: N60E006, S01W075.</summary>
        public static string ShortName(int latS, int lonW) =>
            (latS >= 0 ? "N" : "S") + Math.Abs(latS).ToString("00") + (lonW >= 0 ? "E" : "W") + Math.Abs(lonW).ToString("000");

        /// <summary>The published name: Copernicus_DSM_COG_30_N60_00_E006_00_DEM (30 for GLO-90's 3″ grid).</summary>
        public static string SourceName(int latS, int lonW) =>
            "Copernicus_DSM_COG_30_" + (latS >= 0 ? "N" : "S") + Math.Abs(latS).ToString("00") + "_00_" +
            (lonW >= 0 ? "E" : "W") + Math.Abs(lonW).ToString("000") + "_00_DEM";

        /// <summary>The square of a published name (as in the bucket's listing), or false.</summary>
        public static bool TryParseSourceName(string name, out int latS, out int lonW)
        {
            latS = lonW = 0;
            // Copernicus_DSM_COG_30_N60_00_E006_00_DEM
            var parts = name.Trim().Split('_');
            if (parts.Length < 9) return false;
            string la = parts[4], lo = parts[6];
            if (la.Length != 3 || lo.Length != 4) return false;
            if (!int.TryParse(la.AsSpan(1), out latS) || !int.TryParse(lo.AsSpan(1), out lonW)) return false;
            if (la[0] == 'S') latS = -latS; else if (la[0] != 'N') return false;
            if (lo[0] == 'W') lonW = -lonW; else if (lo[0] != 'E') return false;
            return latS >= -90 && latS < 90 && lonW >= -180 && lonW < 180;
        }

        /// <summary>Index of a square in the 360 × 180 tile bitmap (row 0 is 89..90°N, column 0 is 180..179°W).</summary>
        public static int SquareIndex(int latS, int lonW) => (89 - latS) * 360 + (lonW + 180);

        // ------------------------------------------------------------------------------------------ the published GeoTIFF

        /// <summary>A height grid as read from a source file.</summary>
        public sealed class Grid
        {
            public int Cols, Rows;
            /// <summary>Row-major, row 0 the northernmost.</summary>
            public float[] Heights;
            /// <summary>Longitude and latitude of point (0, 0), and the spacing in degrees.</summary>
            public double Lon0, Lat0, DLon, DLat;
        }

        /// <summary>EA-0106 (the Fable pass's FP-17, L3): the most columns, rows, tile columns and tile rows a GeoTIFF may have
        /// here (GLO-90's are at most 1200, its tiles 1024), and the most values one directory entry may hold.</summary>
        const int MaxTiffSide = 4096, MaxTiffValues = 1 << 16;

        /// <summary>
        /// Reads the full-resolution image of a Copernicus COG: a classic little-endian TIFF with one float32 band in tiles,
        /// deflate or no compression, and no or the floating-point predictor (3). Other TIFFs throw. EA-0106: every count,
        /// offset and size is checked against the file and the limits above before anything is allocated from it (a damaged
        /// directory allocated up to 4 GB), so that a damaged or foreign file throws InvalidDataException and nothing else.
        /// </summary>
        public static Grid ReadGeoTiff(byte[] f)
        {
            if (f.Length < 8 || f[0] != 'I' || f[1] != 'I') throw new InvalidDataException("not a little-endian TIFF");
            if (BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(2)) != 42) throw new InvalidDataException("not a classic TIFF (BigTIFF is not read)");
            long ifd = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(4));
            if (ifd < 8 || ifd > f.Length - 2) throw new InvalidDataException("TIFF directory outside the file");
            int n = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan((int)ifd));
            if (ifd + 2 + 12L * n > f.Length) throw new InvalidDataException("TIFF directory runs past the file");
            int width = 0, height = 0, bps = 0, compression = 1, predictor = 1, format = 1, spp = 1, planar = 1, tw = 0, th = 0;
            long[] offsets = null, counts = null;
            double[] scale = null, tie = null;
            for (int i = 0; i < n; i++)
            {
                int e = (int)ifd + 2 + 12 * i;
                int tag = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(e));
                int type = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(e + 2));
                long count = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(e + 4));
                switch (tag)
                {
                    case 256: width = (int)Value(f, e, type, count, 0); break;
                    case 257: height = (int)Value(f, e, type, count, 0); break;
                    case 258: bps = (int)Value(f, e, type, count, 0); break;
                    case 259: compression = (int)Value(f, e, type, count, 0); break;
                    case 277: spp = (int)Value(f, e, type, count, 0); break;
                    case 284: planar = (int)Value(f, e, type, count, 0); break;
                    case 317: predictor = (int)Value(f, e, type, count, 0); break;
                    case 322: tw = (int)Value(f, e, type, count, 0); break;
                    case 323: th = (int)Value(f, e, type, count, 0); break;
                    case 324: offsets = Values(f, e, type, count); break;
                    case 325: counts = Values(f, e, type, count); break;
                    case 339: format = (int)Value(f, e, type, count, 0); break;
                    case 33550: scale = Doubles(f, e, type, count); break;
                    case 33922: tie = Doubles(f, e, type, count); break;
                }
            }
            if (bps != 32 || format != 3 || spp != 1) throw new InvalidDataException($"not one float32 band (bits {bps}, format {format}, samples {spp})");
            if (tw <= 0 || th <= 0 || offsets == null || counts == null) throw new InvalidDataException("not a tiled TIFF");
            if (compression != 1 && compression != 8 && compression != 32946) throw new InvalidDataException("compression " + compression);
            if (predictor != 1 && predictor != 3) throw new InvalidDataException("predictor " + predictor);
            if (planar != 1 && spp > 1) throw new InvalidDataException("planar configuration " + planar);
            if (scale == null || tie == null || scale.Length < 2 || tie.Length < 6) throw new InvalidDataException("no georeference");
            if (width < 1 || height < 1 || width > MaxTiffSide || height > MaxTiffSide || tw > MaxTiffSide || th > MaxTiffSide)
                throw new InvalidDataException($"{width} x {height} cells in tiles of {tw} x {th}: larger than a GLO-90 tile can be");
            long tiles = (long)((width + tw - 1) / tw) * ((height + th - 1) / th);
            int across = (width + tw - 1) / tw, down = (height + th - 1) / th;
            if (offsets.Length < tiles || counts.Length < tiles) throw new InvalidDataException("tile tables too short");

            var heights = new float[width * height];
            var raw = new byte[tw * th * 4];
            var row = new byte[tw * 4];
            for (int ty = 0; ty < down; ty++)
            {
                for (int tx = 0; tx < across; tx++)
                {
                    int k = ty * across + tx;
                    long off = offsets[k], len = counts[k];
                    if (off < 0 || len < 0 || off + len > f.Length) throw new InvalidDataException("tile outside the file");
                    if (compression == 1)
                    {
                        if (len < raw.Length) throw new InvalidDataException("short tile");
                        Buffer.BlockCopy(f, (int)off, raw, 0, raw.Length);
                    }
                    else
                    {
                        using var z = new ZLibStream(new MemoryStream(f, (int)off, (int)len), CompressionMode.Decompress);
                        int got = 0, r;
                        while (got < raw.Length && (r = z.Read(raw, got, raw.Length - got)) > 0) got += r;
                        if (got != raw.Length) throw new InvalidDataException("short tile");
                    }
                    int w = Math.Min(tw, width - tx * tw), h = Math.Min(th, height - ty * th);
                    for (int y = 0; y < th; y++)
                    {
                        var src = raw.AsSpan(y * tw * 4, tw * 4);
                        if (predictor == 3)
                        {
                            // floating-point predictor: bytes differenced along the row, then stored as byte planes, the most
                            // significant first (libtiff's fpAcc)
                            for (int i = 1; i < src.Length; i++) src[i] += src[i - 1];
                            for (int c = 0; c < tw; c++)
                            {
                                row[4 * c + 3] = src[c];
                                row[4 * c + 2] = src[tw + c];
                                row[4 * c + 1] = src[2 * tw + c];
                                row[4 * c] = src[3 * tw + c];
                            }
                        }
                        else src.CopyTo(row);
                        if (y >= h) continue;
                        int dst = (ty * th + y) * width + tx * tw;
                        for (int c = 0; c < w; c++) heights[dst + c] = BinaryPrimitives.ReadSingleLittleEndian(row.AsSpan(4 * c));
                    }
                }
            }
            return new Grid { Cols = width, Rows = height, Heights = heights, Lon0 = tie[3], Lat0 = tie[4], DLon = scale[0], DLat = scale[1] };
        }

        static int Size(int type) => type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 or 16 or 17 => 8, _ => throw new InvalidDataException("TIFF type " + type) };

        /// <summary>Where an entry's values are: in the entry, or at its offset; EA-0106: all of them within the file, and not
        /// more than <see cref="MaxTiffValues"/>.</summary>
        static int Where(byte[] f, int e, int type, long count)
        {
            long bytes = Size(type) * count;
            if (bytes <= 4) return e + 8;
            long at = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(e + 8));
            if (count > MaxTiffValues || at > f.Length - bytes) throw new InvalidDataException($"TIFF entry of {count} values at {at}: outside the file or too many");
            return (int)at;
        }

        static long Value(byte[] f, int e, int type, long count, int i)
        {
            if (i >= count) throw new InvalidDataException("TIFF entry without a value");
            int at = Where(f, e, type, count) + i * Size(type);
            return type switch
            {
                3 => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(at)),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(at)),
                16 => (long)BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(at)),
                1 => f[at],
                _ => throw new InvalidDataException("TIFF integer type " + type),
            };
        }

        static long[] Values(byte[] f, int e, int type, long count)
        {
            Where(f, e, type, count);   // EA-0106: the values are in the file before the array is made
            var v = new long[count];
            for (int i = 0; i < count; i++) v[i] = Value(f, e, type, count, i);
            return v;
        }

        static double[] Doubles(byte[] f, int e, int type, long count)
        {
            if (type != 12) throw new InvalidDataException("TIFF double type " + type);
            int at = Where(f, e, type, count);
            var v = new double[count];
            for (int i = 0; i < count; i++) v[i] = BinaryPrimitives.ReadDoubleLittleEndian(f.AsSpan(at + 8 * i));
            return v;
        }

        // ------------------------------------------------------------------------------------------ Earth's tile file

        /// <summary>One tile in whole metres, as Earth keeps it.</summary>
        public sealed class Tile
        {
            public int LatS, LonW, Cols, Rows;
            /// <summary>Row-major, row 0 the northernmost (latitude LatS + 1).</summary>
            public short[] Heights;
            public short Min, Max;
            public byte Source = SourceGlo90;
            public int Converter = ConverterVersion;
            public long SourceBytes;
            public string SourceETag = "";
            /// <summary>(EA-0106) The heights' checksum (<see cref="Checksum"/>) as the file's header gives it and
            /// <see cref="Decode"/> verified; 0 for a tile not read from a file.</summary>
            public ulong HeightsChecksum;

            /// <summary>Height at a point of the grid; the column is clamped (the caller keeps it within the tile).</summary>
            public short At(int r, int c) => Heights[r * Cols + Math.Clamp(c, 0, Cols - 1)];
        }

        /// <summary>
        /// A source grid as a tile: heights rounded to whole metres (NaN, the source's no-data, becomes 0, as GLO-90 has the
        /// sea). Checks that the grid is the square it should be.
        /// </summary>
        public static Tile FromGrid(Grid g, int latS, int lonW)
        {
            // (EA-0106) written so that NaN fails too
            if (!(Math.Abs(g.Lat0 - (latS + 1)) <= 1e-6) || !(Math.Abs(g.Lon0 - lonW) <= 1e-6))
                throw new InvalidDataException($"tile {ShortName(latS, lonW)} starts at {g.Lat0}, {g.Lon0}");
            if (!(Math.Abs(g.DLat * g.Rows - 1) <= 1e-6) || !(Math.Abs(g.DLon * g.Cols - 1) <= 1e-6))
                throw new InvalidDataException($"tile {ShortName(latS, lonW)} isn't one degree ({g.Cols} × {g.Rows}, {g.DLon} × {g.DLat})");
            var t = new Tile { LatS = latS, LonW = lonW, Cols = g.Cols, Rows = g.Rows, Heights = new short[g.Heights.Length] };
            short lo = short.MaxValue, hi = short.MinValue;
            for (int i = 0; i < g.Heights.Length; i++)
            {
                float v = g.Heights[i];
                short h = float.IsFinite(v) ? (short)Math.Clamp(Math.Round(v), -12000, 12000) : (short)0;
                t.Heights[i] = h;
                if (h < lo) lo = h;
                if (h > hi) hi = h;
            }
            t.Min = lo; t.Max = hi;
            return t;
        }

        /// <summary>FNV-1a over the heights (little-endian int16).</summary>
        public static ulong Checksum(short[] h)
        {
            ulong x = 14695981039346656037UL;
            foreach (short v in h)
            {
                x = (x ^ (byte)v) * 1099511628211UL;
                x = (x ^ (byte)(v >> 8)) * 1099511628211UL;
            }
            return x;
        }

        /// <summary>
        /// Earth's tile file: "EGLO", format, source, converter, LatS, LonW, Cols, Rows, Min, Max (little-endian), the source
        /// file's byte size, its ETag (length-prefixed ASCII), the heights' checksum, then the payload's length and the payload:
        /// each height minus the median edge prediction from its west, north and north-west neighbours (0 outside), zigzag
        /// coded to 16 bits, all low bytes then all high bytes, Brotli quality 9.
        /// </summary>
        public static byte[] Encode(Tile t)
        {
            int n = t.Cols * t.Rows;
            var planes = new byte[2 * n];
            for (int r = 0; r < t.Rows; r++)
            {
                for (int c = 0; c < t.Cols; c++)
                {
                    int i = r * t.Cols + c;
                    int z = Zig(t.Heights[i] - Predict(t.Heights, t.Cols, r, c));
                    planes[i] = (byte)z;
                    planes[n + i] = (byte)(z >> 8);
                }
            }
            var packed = new byte[BrotliEncoder.GetMaxCompressedLength(planes.Length)];
            using (var enc = new BrotliEncoder(9, 22))
            {
                if (enc.Compress(planes, packed, out int used, out int written, true) != System.Buffers.OperationStatus.Done || used != planes.Length)
                    throw new InvalidOperationException("Brotli could not compress the tile");
                Array.Resize(ref packed, written);
            }
            using var ms = new MemoryStream(packed.Length + 64);
            using var w = new BinaryWriter(ms);
            w.Write(Magic);
            w.Write((byte)FormatVersion);
            w.Write(t.Source);
            w.Write((ushort)t.Converter);
            w.Write((short)t.LatS); w.Write((short)t.LonW);
            w.Write((ushort)t.Cols); w.Write((ushort)t.Rows);
            w.Write(t.Min); w.Write(t.Max);
            w.Write(t.SourceBytes);
            var etag = System.Text.Encoding.ASCII.GetBytes(t.SourceETag ?? "");
            if (etag.Length > 255) Array.Resize(ref etag, 255);
            w.Write((byte)etag.Length); w.Write(etag);
            w.Write(Checksum(t.Heights));
            w.Write(packed.Length);
            w.Write(packed);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Reads a tile file back; damaged or foreign files throw InvalidDataException.</summary>
        public static Tile Decode(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var rd = new BinaryReader(ms);
            var magic = rd.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("not an Earth elevation tile");
            int format = rd.ReadByte();
            if (format != FormatVersion) throw new InvalidDataException("tile format " + format);
            var t = new Tile { Source = rd.ReadByte(), Converter = rd.ReadUInt16(), LatS = rd.ReadInt16(), LonW = rd.ReadInt16() };
            t.Cols = rd.ReadUInt16(); t.Rows = rd.ReadUInt16();
            t.Min = rd.ReadInt16(); t.Max = rd.ReadInt16();
            t.SourceBytes = rd.ReadInt64();
            t.SourceETag = System.Text.Encoding.ASCII.GetString(rd.ReadBytes(rd.ReadByte()));
            ulong sum = rd.ReadUInt64();
            int len = rd.ReadInt32();
            if (t.Cols <= 0 || t.Rows <= 0 || len < 0 || len > data.Length - ms.Position) throw new InvalidDataException("tile header damaged");
            // a damaged size with valid magic: an InvalidDataException like the rest (int overflow and huge arrays threw others,
            // which the readers' damaged-file filter doesn't catch; CR-D9). GLO-90's largest tile is 1200 x 1200.
            if ((long)t.Cols * t.Rows > MaxCells) throw new InvalidDataException("tile header damaged (" + t.Cols + " x " + t.Rows + " cells)");
            int n = t.Cols * t.Rows;
            var planes = new byte[2 * n];
            if (!BrotliDecoder.TryDecompress(new ReadOnlySpan<byte>(data, (int)ms.Position, len), planes, out int written) || written != planes.Length)
                throw new InvalidDataException("tile data damaged");
            t.Heights = new short[n];
            for (int r = 0; r < t.Rows; r++)
            {
                for (int c = 0; c < t.Cols; c++)
                {
                    int i = r * t.Cols + c;
                    int z = planes[i] | planes[n + i] << 8;
                    t.Heights[i] = (short)(Predict(t.Heights, t.Cols, r, c) + Unzig(z));
                }
            }
            if (Checksum(t.Heights) != sum) throw new InvalidDataException("tile checksum differs");
            t.HeightsChecksum = sum;
            return t;
        }

        /// <summary>LOCO-I's median edge detector from the west, north and north-west heights (0 outside the tile).</summary>
        static int Predict(short[] h, int cols, int r, int c)
        {
            int w = c > 0 ? h[r * cols + c - 1] : 0;
            int n = r > 0 ? h[(r - 1) * cols + c] : 0;
            int nw = r > 0 && c > 0 ? h[(r - 1) * cols + c - 1] : 0;
            int lo = Math.Min(w, n), hi = Math.Max(w, n);
            return nw >= hi ? lo : nw <= lo ? hi : w + n - nw;
        }

        static int Zig(int r) => (r << 1) ^ (r >> 31);
        static int Unzig(int z) => (z >> 1) ^ -(z & 1);
    }
}
