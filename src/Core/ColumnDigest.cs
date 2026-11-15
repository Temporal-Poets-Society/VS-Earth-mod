using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// SelfTest's digest of a chunk column as a generation pass left it (its DIGEST lines, EA-0088). Every digest recorded on
    /// a bench depends on this format, so tests/ServerToolsTests pins it: a change to it makes every earlier run look
    /// different. Per chunk, bottom up: per block index (0 to 32767) the block id (as <c>canon</c> maps it) and the fluid
    /// id, 4 bytes each, little-endian, into one SHA-256 for the column, and the chunk's own SHA-256 (its first 8 hex digits)
    /// in <see cref="Chunks"/>; then the map chunk's height maps, 2 bytes a value.
    /// </summary>
    public sealed class ColumnDigest : IDisposable
    {
        public const int ChunkBlocks = 32 * 32 * 32;
        readonly IncrementalHash column = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        readonly byte[] buffer = new byte[ChunkBlocks * 8];
        readonly StringBuilder chunks = new();
        readonly int[] canon;

        /// <param name="canon">The id hashed for each block id (SelfTest: every loot vessel as the first one); an id beyond
        /// it is hashed as it is.</param>
        public ColumnDigest(int[] canon) { this.canon = canon ?? Array.Empty<int>(); }

        public void AddChunk(Func<int, int> block, Func<int, int> fluid)
        {
            for (int i = 0; i < ChunkBlocks; i++)
            {
                int id = block(i);
                BitConverter.TryWriteBytes(buffer.AsSpan(i * 8), (uint)id < (uint)canon.Length ? canon[id] : id);
                BitConverter.TryWriteBytes(buffer.AsSpan(i * 8 + 4), fluid(i));
            }
            column.AppendData(buffer);
            chunks.Append(' ').Append(Convert.ToHexString(SHA256.HashData(buffer))[..8]);
        }

        /// <summary>A height map (none: nothing is added).</summary>
        public void AddHeights(ushort[] heights)
        {
            if (heights != null) foreach (ushort v in heights) column.AppendData(BitConverter.GetBytes(v));
        }

        /// <summary>The chunks' short hashes so far, each after a space.</summary>
        public string Chunks => chunks.ToString();

        /// <summary>The column's hash (and the digest starts over).</summary>
        public byte[] Finish() => column.GetHashAndReset();

        public void Dispose() => column.Dispose();

        /// <summary>The "Meta" digest beside the blocks: the top rock per column (4 bytes a value), then each piece of mod data
        /// in order, its length first (-1 for none, 4 bytes) and its bytes.</summary>
        public static byte[] Meta(int[] topRock, IEnumerable<byte[]> data)
        {
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (topRock != null) foreach (int v in topRock) h.AppendData(BitConverter.GetBytes(v));
            foreach (var b in data)
            {
                h.AppendData(BitConverter.GetBytes(b?.Length ?? -1));
                if (b != null) h.AppendData(b);
            }
            return h.GetHashAndReset();
        }
    }
}
