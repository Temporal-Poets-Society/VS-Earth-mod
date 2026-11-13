using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace EarthMap.Core
{
    /// <summary>
    /// The pieces Earth's binary data readers share: a stream read whole, the magic header, a tile index whose entries must lie
    /// in the file, brotli of a known or unknown size, the SHA-256 prefix the overlay files name their bases by, and the
    /// LEB128 varints and zigzag of the outline files. A damaged file fails here, when it is loaded, with an
    /// InvalidDataException naming it, instead of mid-chunk on a worldgen thread.
    /// </summary>
    public static class EarthBinary
    {
        /// <summary>The whole stream as bytes. A memory stream at its start whose buffer is exactly its content is given back
        /// without a copy (readers only read it).</summary>
        public static byte[] ReadAll(Stream s)
        {
            if (s is MemoryStream m && m.Position == 0 && m.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == seg.Array.Length)
                return seg.Array;
            // a file: one array of its size (a growing MemoryStream and ToArray made about three times its size in large-object
            // garbage, ~1 GB per start over the data store; CR-D4). Zip entries and the like can't seek and take the copy.
            if (s.CanSeek)
            {
                var all = new byte[s.Length - s.Position];
                s.ReadExactly(all);
                return all;
            }
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>Throws unless the file is at least <paramref name="minLength"/> bytes and starts with the four characters of
        /// <paramref name="magic"/>.</summary>
        public static void Expect(byte[] file, string magic, int minLength, string name)
        {
            if (file == null || file.Length < Math.Max(4, minLength) || file[0] != magic[0] || file[1] != magic[1] || file[2] != magic[2] || file[3] != magic[3])
                throw new InvalidDataException(name + ": bad header");
        }

        /// <summary>
        /// A tile index at <paramref name="at"/>: <paramref name="count"/> pairs of (offset, length), each a tile's bytes in the
        /// file after the index; a length of 0 is a tile without data. Throws when the index or a tile lies outside the file.
        /// </summary>
        public static void TileIndex(byte[] file, int at, int count, string name, out int[] offset, out int[] length)
        {
            if (count < 0 || at + 8L * count > file.Length) throw new InvalidDataException(name + ": tile index truncated");
            offset = new int[count];
            length = new int[count];
            for (int i = 0; i < count; i++)
            {
                offset[i] = BitConverter.ToInt32(file, at + 8 * i);
                length[i] = BitConverter.ToInt32(file, at + 4 + 8 * i);
                if (length[i] < 0 || length[i] > 0 && (offset[i] < at + 8 * count || (long)offset[i] + length[i] > file.Length))
                    throw new InvalidDataException(name + ": bad tile index");
            }
        }

        /// <summary>Throws unless the bytes [offset, offset + length) lie within the file.</summary>
        public static void InFile(byte[] file, long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > file.Length) throw new InvalidDataException(what + " lies outside the file");
        }

        /// <summary>Brotli data of a known decoded size (a tile, a grid): exactly <paramref name="size"/> bytes, or an
        /// InvalidDataException "<paramref name="what"/> failed to decompress".</summary>
        public static byte[] Unbrotli(byte[] file, int offset, int length, int size, string what)
        {
            var raw = new byte[size];
            if (!BrotliDecoder.TryDecompress(new ReadOnlySpan<byte>(file, offset, length), raw, out int written) || written != raw.Length)
                throw new InvalidDataException(what + " failed to decompress");
            return raw;
        }

        /// <summary>Brotli data from <paramref name="offset"/> to the end of the file, of a size the file doesn't state: all
        /// of it, up to <paramref name="maxBytes"/> (past that the file is taken as damaged).</summary>
        public static byte[] UnbrotliAll(byte[] file, int offset, string name, int maxBytes = 1 << 30) =>
            UnbrotliAll(file, offset, file.Length - offset, name, maxBytes);

        /// <summary>Brotli data in the bytes [offset, offset + length) of the file (a tile of an outline file), of a size the
        /// file doesn't state: all of it, up to <paramref name="maxBytes"/>.</summary>
        public static byte[] UnbrotliAll(byte[] file, int offset, int length, string name, int maxBytes = 1 << 30)
        {
            byte[] raw = UnbrotliAll(file, offset, length, name, out int decoded, maxBytes);
            if (decoded != raw.Length) Array.Resize(ref raw, decoded);
            return raw;
        }

        /// <summary>
        /// <see cref="UnbrotliAll(byte[], int, int, string, int)"/> without the last copy into an array of exactly the decoded
        /// size: the data is the first <paramref name="decoded"/> bytes of the array given back, the rest zeros. For a large
        /// file read whole (rivers.bin), where that copy is another full one on the large object heap; its reader checks
        /// that it stays within <paramref name="decoded"/>.
        /// </summary>
        public static byte[] UnbrotliAll(byte[] file, int offset, int length, string name, out int decoded, int maxBytes = 1 << 30)
        {
            InFile(file, offset, length, name);
            using var dec = new BrotliStream(new MemoryStream(file, offset, length, false), CompressionMode.Decompress);
            // straight into a buffer that doubles as it fills, first three times the packed size (Earth's files and tiles
            // mostly decode to 1.5 to 2.6 times theirs)
            var raw = new byte[(int)Math.Min(Math.Max(3L * length, 4096), maxBytes)];
            int n = 0;
            while (true)
            {
                if (n == raw.Length)
                {
                    if (n >= maxBytes)
                    {
                        if (dec.Read(new byte[1], 0, 1) > 0) throw new InvalidDataException(name + ": decompresses to more than " + maxBytes + " bytes");
                        break;
                    }
                    Array.Resize(ref raw, (int)Math.Min(2L * n, maxBytes));
                }
                int got = dec.Read(raw, n, raw.Length - n);
                if (got == 0) break;
                n += got;
            }
            decoded = n;
            return raw;
        }

        /// <summary>The first 8 bytes of a file's SHA-256 as a number, 0 for no file: how the overlay files name the files
        /// they were made for (sea-referee.bin, the river snaps and arms).</summary>
        public static ulong Sha8(byte[] file) => file == null ? 0 : BitConverter.ToUInt64(SHA256.HashData(file), 0);

        /// <summary>An unsigned LEB128 varint at o, which moves past it.</summary>
        public static ulong ReadVarint(byte[] b, ref int o)
        {
            ulong v = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte x = b[o++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return v;
            }
        }

        public static long UnZigzag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);
    }
}
