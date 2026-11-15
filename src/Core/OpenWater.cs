using System;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// /earth tp ... water (EarthCommands, EA-0088): the nearest spot with open water around it, room for a ship. Off the game
    /// API so tests/ServerToolsTests can run it on columns of its own.
    /// </summary>
    public static class OpenWater
    {
        /// <summary>Sea, lake or river at least 4 blocks deep (never the seam's gutter), with water at least 2 blocks deep and
        /// within a block of its level for 8 blocks all round (every 4 blocks).</summary>
        public static bool At(Func<double, double, TerrainModel.Column> column, double px, double pz)
        {
            var c = column(px, pz);
            if (c.Water == TerrainModel.WaterNone || c.Gutter || c.WaterTop - c.Top < 4) return false;
            for (int dz = -8; dz <= 8; dz += 4)
            {
                for (int dx = -8; dx <= 8; dx += 4)
                {
                    var n = column(px + dx, pz + dz);
                    if (n.Water == TerrainModel.WaterNone || n.WaterTop - n.Top < 2 || Math.Abs(n.WaterTop - c.WaterTop) > 1) return false;
                }
            }
            return true;
        }

        /// <summary>The first open spot (<see cref="At"/>) on rings every 4 blocks out to <paramref name="maxDist"/>, each ring
        /// walked from the east (+x) towards +z, a point every 4 blocks of its circumference; false (and the spot asked) when
        /// there is none. The search can visit a million columns where there is no water: it stops when cancelled.</summary>
        public static bool Find(Func<double, double, TerrainModel.Column> column, double x, double z, double maxDist, CancellationToken cancel, out double wx, out double wz)
        {
            for (double r = 0; r <= maxDist; r += 4)
            {
                cancel.ThrowIfCancellationRequested();
                int n = r == 0 ? 1 : (int)Math.Ceiling(2 * Math.PI * r / 4);
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    double px = x + r * Math.Cos(a), pz = z + r * Math.Sin(a);
                    if (At(column, px, pz))
                    {
                        wx = px; wz = pz;
                        return true;
                    }
                }
            }
            wx = x; wz = z;
            return false;
        }
    }
}
