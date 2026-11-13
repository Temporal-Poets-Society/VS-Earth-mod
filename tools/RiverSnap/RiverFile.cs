using System.IO.Compression;

namespace RiverSnap
{
    /// <summary>One reach of rivers.bin v2 (as RiverNetwork.cs's RiverReach).</summary>
    sealed class Reach
    {
        public float Q;
        public int Next = -1;
        public float[] Lon, Lat;
        public short[] Level;
        public bool[] Fixed;
    }

    /// <summary>
    /// rivers.bin v2 (tools/bake_rivers.py): "EMRV", int32 version, int32 reach count, brotli( per reach: float32 Q, int32
    /// next reach index (-1), int32 n, n x (float32 lon, float32 lat, int16 level, uint8 fixed) ), reaches in topological
    /// order (sources first).
    /// </summary>
    static class RiverFile
    {
        public static List<Reach> Read(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 12 || file[0] != 'E' || file[1] != 'M' || file[2] != 'R' || file[3] != 'V') throw new InvalidDataException(path + ": bad header");
            int version = BitConverter.ToInt32(file, 4), count = BitConverter.ToInt32(file, 8);
            if (version != 2) throw new InvalidDataException(path + ": version " + version + ", expected 2");
            using var dec = new BrotliStream(new MemoryStream(file, 12, file.Length - 12), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            dec.CopyTo(raw);
            byte[] b = raw.GetBuffer();
            var list = new List<Reach>(count);
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                var r = new Reach { Q = BitConverter.ToSingle(b, o), Next = BitConverter.ToInt32(b, o + 4) };
                int n = BitConverter.ToInt32(b, o + 8);
                o += 12;
                r.Lon = new float[n]; r.Lat = new float[n]; r.Level = new short[n]; r.Fixed = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    r.Lon[k] = BitConverter.ToSingle(b, o);
                    r.Lat[k] = BitConverter.ToSingle(b, o + 4);
                    r.Level[k] = BitConverter.ToInt16(b, o + 8);
                    r.Fixed[k] = b[o + 10] != 0;
                    o += 11;
                }
                list.Add(r);
            }
            return list;
        }

        /// <summary>Writes a v2 file; never replaces an existing one. Returns its size in bytes.</summary>
        public static long Write(string path, List<Reach> reaches, int quality, int window)
        {
            var (packed, written) = Encode(reaches, quality, window);
            using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            f.Write("EMRV"u8);
            f.Write(BitConverter.GetBytes(2));
            f.Write(BitConverter.GetBytes(reaches.Count));
            f.Write(packed, 0, written);
            return f.Length;
        }

        /// <summary>The size a v2 file of these reaches would have, in bytes.</summary>
        public static long Size(List<Reach> reaches, int quality, int window) => 12 + Encode(reaches, quality, window).Length;

        static (byte[] Packed, int Length) Encode(List<Reach> reaches, int quality, int window)
        {
            long vertices = 0;
            foreach (var r in reaches) vertices += r.Lon.Length;
            long rawLen = 12L * reaches.Count + 11 * vertices;
            var raw = new byte[rawLen];
            int o = 0;
            foreach (var r in reaches)
            {
                BitConverter.TryWriteBytes(raw.AsSpan(o), r.Q);
                BitConverter.TryWriteBytes(raw.AsSpan(o + 4), r.Next);
                BitConverter.TryWriteBytes(raw.AsSpan(o + 8), r.Lon.Length);
                o += 12;
                for (int k = 0; k < r.Lon.Length; k++)
                {
                    BitConverter.TryWriteBytes(raw.AsSpan(o), r.Lon[k]);
                    BitConverter.TryWriteBytes(raw.AsSpan(o + 4), r.Lat[k]);
                    BitConverter.TryWriteBytes(raw.AsSpan(o + 8), r.Level[k]);
                    raw[o + 10] = (byte)(r.Fixed[k] ? 1 : 0);
                    o += 11;
                }
            }
            var packed = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
            if (!BrotliEncoder.TryCompress(raw, packed, out int written, quality, window)) throw new InvalidOperationException("Brotli could not compress the rivers");
            return (packed, written);
        }
    }
}
