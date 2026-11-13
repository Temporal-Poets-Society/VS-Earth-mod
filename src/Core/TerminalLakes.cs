using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// lakes.bin: the terminal lakes where rivers without an outlet end (worlds with EarthParams.RiverEnds). "Revived" are
    /// the lakes irrigation and dams dried up in the 20th century (the Aral Sea, Lop Nur, the Hamun lakes, Lake Chad,
    /// Owens and Tulare lakes, ...) at their level before; "natural" are salt lakes and pans that fill in wet years
    /// (Kati Thanda-Lake Eyre, Etosha, Makgadikgadi, ...) at a filled level. Each filled its basin below its level in the
    /// bake (tools/bake_earth.py TERMINAL_LAKES); the file holds the grid cells that differ from terrain.bin (as islands.bin).
    /// </summary>
    public sealed class LakeData
    {
        public const int FileVersion = 1;

        public sealed class Lake
        {
            public string Name;
            /// <summary>Water level (m), and the seed the basin was filled from.</summary>
            public double Level, Lat, Lon;
            /// <summary>How far (km) the dry land around it is greener (0: a salt pan's or a brine's barren shore).</summary>
            public double ShoreKm;
            public bool Revived, Salt;
        }

        public IslandData.Patch[] Patches;
        public Lake[] Lakes;

        public static LakeData Read(Stream s, int gridWidth)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMLK", 20, "lakes.bin");
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8);
            int nPatch = BitConverter.ToInt32(file, 12), nLake = BitConverter.ToInt32(file, 16);
            if (version != FileVersion || w != gridWidth) throw new InvalidDataException("lakes.bin: layout mismatch");
            byte[] b = EarthBinary.UnbrotliAll(file, 20, "lakes.bin");
            int o = 0;
            var d = new LakeData { Patches = IslandData.ReadPatches(b, ref o, nPatch, w), Lakes = new Lake[nLake] };
            for (int i = 0; i < nLake; i++)
            {
                var l = new Lake
                {
                    Level = BitConverter.ToSingle(b, o), Lat = BitConverter.ToSingle(b, o + 4), Lon = BitConverter.ToSingle(b, o + 8),
                    ShoreKm = BitConverter.ToSingle(b, o + 12), Revived = b[o + 16] == 1, Salt = b[o + 17] != 0,
                };
                int n = b[o + 18];
                l.Name = Encoding.UTF8.GetString(b, o + 19, n);
                o += 19 + n;
                d.Lakes[i] = l;
            }
            return d;
        }
    }
}
