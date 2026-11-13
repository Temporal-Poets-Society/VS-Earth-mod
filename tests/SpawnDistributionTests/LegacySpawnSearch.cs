using System;
using System.Globalization;

namespace EarthMap.Core
{
    /// <summary>
    /// Where new players start (decision 0023): dry, gentle land near a chosen place, or a random place at the starting
    /// climate. Also the Earth tab's world size and coordinate values.
    /// </summary>
    public static class LegacySpawnSearch
    {
        public static Action<string,double,double> Observe;
        /// <summary>Vanilla's starting climates as yearly mean temperatures (Ã‚Â°C), from its world creation screen.</summary>
        public static readonly (string climate, double min, double max)[] ClimateBands =
        {
            ("hot", 28, 32), ("warm", 19, 23), ("temperate", 6, 14), ("cool", -5, 1), ("icy", -15, -10),
        };

        /// <summary>Highest ground (blocks above sea level) a random spawn takes: lowland, as the former fixed places were.</summary>
        public const int LowlandAboveSea = 60;
        /// <summary>Blocks a spawn keeps from the map's edges.</summary>
        public const double Margin = 64;

        public static bool OnMap(int mapX, int mapZ, double x, double z, double margin)
            => x >= margin && z >= margin && x <= mapX - margin && z <= mapZ - margin;

        /// <summary>Dry, ice-free land on the map at most maxAboveSea blocks above sea level, within 3 blocks of the height
        /// 12 blocks out on each side, with no water there.</summary>
        public static bool Spawnable(TerrainModel m, int mapX, int mapZ, double x, double z, int maxAboveSea)
        {
            if (!OnMap(mapX, mapZ, x, z, Margin)) return false;
            int sea = m.SeaLevel;
            var c = m.GetColumn(x, z);
            if (c.Water != TerrainModel.WaterNone || c.Gutter || c.IceBottom <= c.Top || c.Top < sea || c.Top > sea + maxAboveSea) return false;
            foreach (var (dx, dz) in new[] { (12, 0), (-12, 0), (0, 12), (0, -12) })
            {
                var d = m.GetColumn(x + dx, z + dz);
                if (d.Water != TerrainModel.WaterNone || Math.Abs(d.Top - c.Top) > 3) return false;
            }
            return true;
        }

        /// <summary>Nearest spawnable land to (x, z), searching outwards in 32-block steps up to radius.</summary>
        public static bool FindDryLand(TerrainModel m, int mapX, int mapZ, double x, double z, double radius, int maxAboveSea, out int bx, out int bz)
        {
            for (double r = 0; r <= radius; r += 32)
            {
                int n = r == 0 ? 1 : Math.Max(8, (int)(2 * Math.PI * r / 32));
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    double cx = x + r * Math.Cos(a), cz = z + r * Math.Sin(a);
                    if (!Spawnable(m, mapX, mapZ, cx, cz, maxAboveSea)) continue;
                    bx = (int)Math.Floor(cx); bz = (int)Math.Floor(cz);
                    return true;
                }
            }
            bx = (int)x; bz = (int)z;
            return false;
        }

        /// <summary>
        /// A random place, from the seed, whose real yearly mean surface temperature is in the starting climate's band:
        /// spawnable lowland with land all around 256 blocks out (no speck of an island), between 60Ã‚Â°S and 80Ã‚Â°N. Points are
        /// drawn evenly by area, so a climate with more land is found sooner.
        /// </summary>
        public static bool FindClimatePlace(TerrainModel m, int mapX, int mapZ, int seed, string climate, out double lat, out double lon, out double tMean)
        {
            var band = Array.Find(ClimateBands, b => b.climate == climate);
            if (band.climate == null) band = ClimateBands[2];
            var rnd = new Random(unchecked(seed * 31 + 7919));
            double s0 = Math.Sin(-60 * Math.PI / 180), s1 = Math.Sin(80 * Math.PI / 180);
            for (int i = 0; i < 20000; i++)
            {
                double la = Math.Asin(s0 + (s1 - s0) * rnd.NextDouble()) * 180 / Math.PI, lo = rnd.NextDouble() * 360 - 180;
                Observe?.Invoke("draw",la,lo);
                m.ClimateAt(la, lo, out double t, out _, out _);
                if (t < band.min || t > band.max) continue;
                Observe?.Invoke("climate",la,lo);
                m.Geometry.FromGeo(la, lo, out double x, out double z);
                var top=m.GetColumn(x,z);
                if(top.Water==TerrainModel.WaterNone && top.Top>=m.SeaLevel && top.Top<=m.SeaLevel+LowlandAboveSea) Observe?.Invoke("lowland",la,lo);
                if (!Spawnable(m, mapX, mapZ, x, z, LowlandAboveSea)) continue;
                Observe?.Invoke("gentle",la,lo);
                int land = 0;
                for (int k = 0; k < 8; k++)
                {
                    var c = m.GetColumn(x + 256 * Math.Cos(k * Math.PI / 4), z + 256 * Math.Sin(k * Math.PI / 4));
                    if (c.Water == TerrainModel.WaterNone && !c.Gutter) land++;
                }
                if (land < 8) continue;
                Observe?.Invoke("interior",la,lo);
                lat = la; lon = lo; tMean = t;
                return true;
            }
            lat = lon = tMean = double.NaN;
            return false;
        }

        /// <summary>"lat, lon" in degrees (also "lat lon" or "lat;lon").</summary>
        public static bool TryParseLatLon(string s, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            var parts = (s ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
                && double.IsFinite(lat) && double.IsFinite(lon) && Math.Abs(lat) <= 90;
        }

        public const int DefaultWidth = 512000;
        /// <summary>World length per block of width: Cylinder World's Earth proportions then make a ring of 0.8 Ãƒâ€” the width
        /// (2 Ãƒâ€” the length) with a tenth of the width as hidden seam ocean at each side.</summary>
        public const double LengthPerWidth = 0.4;

        /// <summary>The world's width and length for the Earth tab's size value (a width in blocks); an unusable value gives
        /// the default and bad = true. Both are whole 512-block map regions, as Cylinder World's ring wants.</summary>
        public static void SizeFor(string value, out int width, out int length, out bool bad)
        {
            bad = !int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) || width < 64000 || width > 8192000;
            if (bad) width = DefaultWidth;
            width = width / 512 * 512;
            length = (int)Math.Round(width * LengthPerWidth / 512) * 512;
        }
    }
}
