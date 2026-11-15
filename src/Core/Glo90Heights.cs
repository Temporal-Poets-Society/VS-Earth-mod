using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0106 (Fable's re-review of 255baa7): the heights checksum (DemTiles.Checksum, FNV-1a over the decoded heights) of
    /// every GLO-90 square of Earth's data release egl-v1, shipped with the mod (data/glo90-heights.bin, baked by
    /// tools/bake_glo90_heights.py from the release's tiles). Whether a tile is the release's is judged by its heights, on any
    /// runtime, OS or CPU: its file's bytes depend on the runtime's Brotli encoder (on .NET 10, 341 of 602 release tiles
    /// encode to other bytes than on .NET 8, the same heights).
    /// </summary>
    public sealed class Glo90Heights
    {
        public const string FileName = "glo90-heights.bin";
        /// <summary>The embedded resource's name in the mod's assembly.</summary>
        public const string ResourceName = "EarthMap.glo90-heights.bin";

        // by DemTiles.SquareIndex; 0 where the release has no tile (a checksum of 0 is as good as impossible)
        readonly ulong[] bySquare;
        public int Count { get; }

        /// <summary>A table read from the file, for the squares of <paramref name="tileList"/> (data/glo90-tiles.bin) in its
        /// bit order; the file names the list it was made for (the first 8 bytes of its SHA-256).</summary>
        public static Glo90Heights Read(byte[] file, byte[] tileList)
        {
            if (file == null || file.Length < 20 || file[0] != 'E' || file[1] != 'G' || file[2] != 'L' || file[3] != 'H')
                throw new InvalidDataException(FileName + ": not a GLO-90 heights table");
            if (BitConverter.ToUInt32(file, 4) != 1) throw new InvalidDataException(FileName + ": version " + BitConverter.ToUInt32(file, 4));
            long count = BitConverter.ToUInt32(file, 8);
            if (20 + 8 * count != file.Length) throw new InvalidDataException(FileName + ": " + file.Length + " bytes for " + count + " squares");
            var listHash = SHA256.HashData(tileList);
            for (int i = 0; i < 8; i++)
                if (file[12 + i] != listHash[i]) throw new InvalidDataException(FileName + " was made for another tile list");
            var table = new ulong[360 * 180];
            int k = 0;
            for (int ix = 0; ix < 360 * 180; ix++)
            {
                if ((tileList[ix >> 3] & (0x80 >> (ix & 7))) == 0) continue;
                if (k >= count) throw new InvalidDataException(FileName + ": fewer squares than the tile list");
                table[ix] = BitConverter.ToUInt64(file, 20 + 8 * k++);
            }
            if (k != count) throw new InvalidDataException(FileName + ": more squares than the tile list");
            return new Glo90Heights(table, k);
        }

        /// <summary>A table from (latS, lonW) to heights checksum (tests).</summary>
        public Glo90Heights(IReadOnlyDictionary<(int LatS, int LonW), ulong> sums)
        {
            bySquare = new ulong[360 * 180];
            foreach (var kv in sums) bySquare[DemTiles.SquareIndex(kv.Key.LatS, kv.Key.LonW)] = kv.Value;
            Count = sums.Count;
        }

        Glo90Heights(ulong[] table, int count)
        {
            bySquare = table;
            Count = count;
        }

        /// <summary>The release's heights checksum for a square, or false where the release has no tile.</summary>
        public bool TryGet(int latS, int lonW, out ulong checksum)
        {
            checksum = 0;
            if (latS < -90 || latS > 89 || lonW < -180 || lonW > 179) return false;
            checksum = bySquare[DemTiles.SquareIndex(latS, lonW)];
            return checksum != 0;
        }
    }
}
