using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The terrain parts of /earth site (EarthCommands.CmdSite, EA-0088): the slope and aspect of the ground, and how far the
    /// sea is. Off the game API so tests/ServerToolsTests can run them on heights of its own.
    /// </summary>
    public static class SiteTerrain
    {
        /// <summary>
        /// Slope and aspect from a least-squares plane through 5 x 5 heights <paramref name="d"/> blocks apart around (x, z):
        /// a plain two-point difference reads single blocks and forest edges of the surface model as slopes.
        /// <paramref name="metresX"/>, <paramref name="metresZ"/>: metres per block east and south. North is -z. The slope in
        /// radians; the aspect the way downhill, clockwise from north, 0 to 2π (a flat plane says north).
        /// </summary>
        public static void PlaneSlope(Func<double, double, double> height, double x, double z, int d, double metresX, double metresZ,
            out double slopeRad, out double aspectRad)
        {
            double sxh = 0, szh = 0, sxx = 0, szz = 0;
            for (int j = -2; j <= 2; j++)
                for (int i = -2; i <= 2; i++)
                {
                    double h = height(x + i * d, z + j * d), mx = i * d * metresX, mz = -j * d * metresZ;   // north is -z
                    sxh += mx * h; szh += mz * h; sxx += mx * mx; szz += mz * mz;
                }
            double east = sxh / sxx, north = szh / szz;
            slopeRad = Math.Atan(Math.Sqrt(east * east + north * north));
            double aspect = Math.Atan2(-east, -north);                        // downhill, clockwise from north
            aspectRad = aspect < 0 ? aspect + 2 * Math.PI : aspect;
        }

        /// <summary>
        /// Metres to the nearest sea column: 0 on the sea; else a coarse search in 32 directions on rings 15 % apart, from 25 m
        /// (at least 2 blocks) out to <paramref name="rangeM"/>; +∞ when there is none within it.
        /// </summary>
        public static double SeaDistance(Func<double, double, bool> sea, double x, double z, double metresPerBlock, double rangeM)
        {
            if (sea(x, z)) return 0;
            for (double r = Math.Max(2, 25 / metresPerBlock); r * metresPerBlock <= rangeM; r *= 1.15)
                for (int k = 0; k < 32; k++)
                {
                    double a = k * Math.PI / 16;
                    if (sea(x + r * Math.Cos(a), z + r * Math.Sin(a))) return r * metresPerBlock;
                }
            return double.PositiveInfinity;
        }
    }
}
