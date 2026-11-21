using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0100 (EA-0184): each lake's real depth for lakes-glo90-5.bin's lakes (lake-depths-1.bin, in the mod;
    /// tools/bake_lake_depths.py). HydroLAKES (Messager et al. 2016, CC BY 4.0) gives every lake from 10 ha its mean depth;
    /// the bake joins lakes-glo90-5.bin's pieces into whole lakes and gives each piece of a lake that one of HydroLAKES' lakes
    /// from 1 km² lies on: its floor depth (far from the shore: the depth that, with the bed curve TerrainModel.LakeDepthAt
    /// draws, gives the lake its mean depth over its own distances to the shore), its size (area over shore length), and
    /// whether ETOPO 2022 holds its bed (then the 2′ grid's bathymetry stays). Other lakes take a depth by their area.
    /// Layout: "EMLD", int32 version, tiles across and down, int64 length and 32-byte SHA-256 of the lakes file it was made
    /// for, then a brotli block: varint tile count; per tile a varint step from the last tile index and a varint count; per
    /// entry a varint step from the last lake index, varint depth (dm), varint size (10 m), a flags byte.
    /// </summary>
    public sealed class LakeDepthData
    {
        public const string FileName = "lake-depths-1.bin";
        public const int FileVersion = 1;
        /// <summary>The file this build reads (the bake of 18 November 2026, in the mod); any other is refused.</summary>
        public const long FileBytes = 252_261;
        public const string FileSha256 = "81ba668e28ec519dce1fcfd7ff4017795417df6bbb1fd0cfcc4b1c84183575b2";

        /// <summary>Whether these bytes are the pinned file (its size and SHA-256).</summary>
        public static bool IsExpected(byte[] bytes, out string why) => LakeOutlineData.Matches(bytes, FileBytes, FileSha256, out why);
        /// <summary>Flag: ETOPO 2022 holds the lake's bed (the 2′ grid's lake bed is its bathymetry).</summary>
        public const byte FlagBathymetry = 1;

        /// <summary>One lake's depth: its floor depth (m, far from the shore), its size (m: area over shore length), whether
        /// ETOPO holds its bed.</summary>
        public readonly struct Lake
        {
            public readonly float FloorMetres, SizeMetres;
            public readonly bool Bathymetry;
            public Lake(float floor, float size, bool bathymetry) { FloorMetres = floor; SizeMetres = size; Bathymetry = bathymetry; }
        }

        readonly Dictionary<int, (int[] Index, Lake[] Lakes)> tiles = new();
        /// <summary>The lakes file this one was made for (its length and SHA-256).</summary>
        public long LakesLength { get; private set; }
        public byte[] LakesSha256 { get; private set; }
        /// <summary>Lakes (pieces) listed.</summary>
        public int Count { get; private set; }

        public static LakeDepthData Read(byte[] file)
        {
            if (file.Length < 60 || file[0] != (byte)'E' || file[1] != (byte)'M' || file[2] != (byte)'L' || file[3] != (byte)'D')
                throw new InvalidDataException(FileName + ": not a lake depth file");
            int version = BitConverter.ToInt32(file, 4);
            if (version != FileVersion) throw new InvalidDataException(FileName + ": version " + version + ", this build reads " + FileVersion);
            var d = new LakeDepthData { LakesLength = BitConverter.ToInt64(file, 16), LakesSha256 = file.AsSpan(24, 32).ToArray() };
            byte[] body;
            using (var ms = new MemoryStream(file, 56, file.Length - 56))
            using (var br = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                br.CopyTo(o);
                body = o.ToArray();
            }
            int i = 0;
            int nTiles = (int)Varint(body, ref i);
            int tile = 0;
            for (int k = 0; k < nTiles; k++)
            {
                tile += (int)Varint(body, ref i);
                int n = (int)Varint(body, ref i);
                var idx = new int[n];
                var lakes = new Lake[n];
                int lake = 0;
                for (int e = 0; e < n; e++)
                {
                    lake += (int)Varint(body, ref i);
                    float depth = Varint(body, ref i) / 10f, size = Varint(body, ref i) * 10f;
                    byte flags = body[i++];
                    idx[e] = lake;
                    lakes[e] = new Lake(depth, size, (flags & FlagBathymetry) != 0);
                }
                d.tiles[tile] = (idx, lakes);
                d.Count += n;
            }
            return d;
        }

        static long Varint(byte[] s, ref int i)
        {
            long v = 0;
            for (int sh = 0; ; sh += 7)
            {
                byte c = s[i++];
                v |= (long)(c & 0x7F) << sh;
                if (c < 0x80) return v;
            }
        }

        /// <summary>Whether this file was made for that lake outlines version's file (its pinned length and SHA-256: the file a
        /// world reads is checked against those when it is loaded).</summary>
        public bool Fits(int lakeOutlines) =>
            LakesLength == LakeOutlineData.FileBytesFor(lakeOutlines)
            && string.Equals(Convert.ToHexString(LakesSha256), LakeOutlineData.FileSha256For(lakeOutlines), StringComparison.OrdinalIgnoreCase);

        /// <summary>The depth of lake <paramref name="lake"/> of tile <paramref name="tile"/> (the lakes file's numbering), or false.</summary>
        public bool TryGet(int tile, int lake, out Lake depth)
        {
            depth = default;
            if (!tiles.TryGetValue(tile, out var t)) return false;
            int k = Array.BinarySearch(t.Index, lake);
            if (k < 0) return false;
            depth = t.Lakes[k];
            return true;
        }
    }
}
