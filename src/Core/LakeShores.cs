using System;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// shores.bin (tools/bake_earth.py bake_shores): the land around lakes, for worlds with EarthParams.LakeShores. terrain.bin
    /// raises all dry land within 3 grid cells (about 11 km) of a lake to the lake's level. Where the ground falls away from
    /// a lake (its outlet valley, the far side of a crater rim) that made a flat plateau at the water's level ending in a
    /// cliff up to 10 blocks high along the cells where the ring stops (south of Lake Bolsena and Lake Bracciano). With the
    /// patches only the land touching the lake keeps the raise; further out it eases off to the real ground. The file holds
    /// the grid cells whose elevation differs from terrain.bin (as islands.bin; levels and flags are unchanged), and how
    /// near a lake each cell of those rings is, which TerrainModel.LakeShores fades the old rules out with.
    /// </summary>
    public sealed class ShoreData
    {
        public const int FileVersion = 1;

        public IslandData.Patch[] Patches;
        /// <summary>The cells of the lakes' 3-cell rings less than fully near a lake (grid cell index, row * width + col) and their
        /// nearness (0..254 of 255): 1 at the cells touching a lake, easing to 0 where the ring ends. Every other cell is 255.</summary>
        public long[] NearCells;
        public byte[] Near;

        public static ShoreData Read(Stream s, int gridWidth)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMSH", 20, "shores.bin");
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8), nPatch = BitConverter.ToInt32(file, 12);
            int nNear = BitConverter.ToInt32(file, 16);
            if (version != FileVersion || w != gridWidth) throw new InvalidDataException("shores.bin: layout mismatch");
            byte[] b = EarthBinary.UnbrotliAll(file, 20, "shores.bin");
            int o = 0;
            var d = new ShoreData { Patches = IslandData.ReadPatches(b, ref o, nPatch, w), NearCells = new long[nNear], Near = new byte[nNear] };
            long cell = 0;
            for (int i = 0; i < nNear; i++)
            {
                cell += (long)EarthBinary.ReadVarint(b, ref o);
                d.NearCells[i] = cell;
            }
            Buffer.BlockCopy(b, o, d.Near, 0, nNear);
            return d;
        }
    }
}
