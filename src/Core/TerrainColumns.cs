namespace EarthMap.Core
{
    /// <summary>Pure helpers over terrain columns (TerrainModel.Column).</summary>
    internal static class TerrainColumns
    {
        /// <summary>A fixed pseudo-random number per column (the rocks in rapids).</summary>
        internal static uint Hash(int x, int z)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ 0x52a91du;
                h ^= h >> 13; h *= 0x85ebca6bu; h ^= h >> 16;
                return h;
            }
        }

        /// <summary>The glacier ice on a column (blocks), 0 without.</summary>
        internal static double IceThickness(in TerrainModel.Column c) => c.IceBottom <= c.Top ? c.Top - c.IceBottom + 1 : 0;
    }
}
