using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// outlets.bin: the lakes' tongues past their outlet as land (worlds with EarthParams.LakeOutlets). Some Natural Earth
    /// lakes run on down their outflow river past the lake's outlet: Lake Erie's polygon holds the whole upper Niagara River
    /// and ends below the falls, over the gorge. In the bake (tools/bake_earth.py outlet_tongues) the part past the
    /// HydroLAKES pour point along the HydroRIVERS outflow, when it is river-shaped (3.5 km wide or less on average) and
    /// not a chain of lakes, is land; the river runs through it. The file holds the grid cells that differ from terrain.bin (as
    /// islands.bin).
    /// </summary>
    public sealed class OutletData
    {
        public const int FileVersion = 1;

        public IslandData.Patch[] Patches;

        public static OutletData Read(Stream s, int gridWidth)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMOT", 16, "outlets.bin");
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8), nPatch = BitConverter.ToInt32(file, 12);
            if (version != FileVersion || w != gridWidth) throw new InvalidDataException("outlets.bin: layout mismatch");
            byte[] b = EarthBinary.UnbrotliAll(file, 16, "outlets.bin");
            int o = 0;
            return new OutletData { Patches = IslandData.ReadPatches(b, ref o, nPatch, w) };
        }

        /// <summary>
        /// With the outlets overlay on, river points in a lake's tongue that is land now are no longer held at the lake's level
        /// (the river data marked them as in the lake): they keep the lake's level as their valley level, and the river's
        /// pools, rapids and falls are laid out through them like anywhere else. The reaches concerned are copied; the shared
        /// river data stays as it is.
        /// </summary>
        public static List<RiverReach> ReleaseRiverPoints(EarthData data, List<RiverReach> source)
        {
            if (source == null || !data.OutletOverlay) return source;
            List<RiverReach> copy = null;
            for (int r = 0; r < source.Count; r++)
            {
                var re = source[r];
                if (re.Fixed == null) continue;
                bool[] fixedNew = null;
                for (int k = 0; k < re.Fixed.Length; k++)
                {
                    if (!re.Fixed[k] || re.Level[k] == 0) continue; // the sea stays fixed
                    int row = (int)Math.Floor((90 - re.Lat[k]) * data.TerrainRes), col = (int)Math.Floor((re.Lon[k] + 180) * data.TerrainRes);
                    if (!data.IsOutletLandCell(row, col)) continue;
                    fixedNew ??= (bool[])re.Fixed.Clone();
                    fixedNew[k] = false;
                }
                if (fixedNew == null) continue;
                copy ??= new List<RiverReach>(source);
                copy[r] = new RiverReach { Q = re.Q, Next = re.Next, Lon = re.Lon, Lat = re.Lat, Level = re.Level, Fixed = fixedNew, Width = re.Width };
            }
            return copy ?? source;
        }
    }
}
