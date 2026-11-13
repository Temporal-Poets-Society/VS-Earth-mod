using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Terrain forced to one height (story locations), blended into the natural terrain around it: everything within
    /// Radius of the box X±HalfX, Z±HalfZ (a disc when both are 0, as in saves before 0.14.8), then Blend more.
    /// </summary>
    public sealed class FlatZone
    {
        public string Code;
        public double X, Z, Radius, Blend, HalfX, HalfZ;
        public int TargetTop;

        /// <summary>Distance from the box to a point at offset (dx, dz) from the centre.</summary>
        public double Distance(double dx, double dz)
        {
            double ex = Math.Max(0, Math.Abs(dx) - HalfX), ez = Math.Max(0, Math.Abs(dz) - HalfZ);
            return Math.Sqrt(ex * ex + ez * ez);
        }
    }

    /// <summary>The story locations' terrain (TerrainModel.FlatZones): a pure function of the zones, the layout and a column.</summary>
    public static class StoryTerrain
    {
        /// <summary>A column with the story locations near it applied (TerrainModel.GetColumn, last before the pads).</summary>
        public static void Apply(List<FlatZone> zones, EarthGeometry geo, int seaLevel, double x, double z, ref TerrainModel.Column c)
        {
            foreach (var zone in zones)
            {
                double dx = x - zone.X, dz = z - zone.Z;
                if (geo.Wraps)
                {
                    double L = geo.RingLength;
                    dx = ((dx % L) + L + L / 2) % L - L / 2;
                }
                double d = zone.Distance(dx, dz);
                if (d >= zone.Radius + zone.Blend) continue;
                // never over a river (sites keep clear of them; this protects worlds made before they did)
                if (c.River) continue;
                double w = d <= zone.Radius ? 1 : 1 - Geo.SmoothStep((d - zone.Radius) / zone.Blend);
                int natural = c.Top;
                int top = (int)Math.Round(natural + (zone.TargetTop - natural) * w);
                if (c.Water != TerrainModel.WaterNone && w < 0.5)
                {
                    // a lake or the sea in the blend stays water (only its bed follows the site a little); before, any
                    // water above sea level there was drained, even where the site barely changed the ground
                    c.Top = Math.Min(top, c.WaterTop - 1);
                    continue;
                }
                if (c.Water != TerrainModel.WaterNone)
                {
                    // inside the site: filled in, at least up to sea level
                    top = Math.Max(top, seaLevel);
                    c.Water = TerrainModel.WaterNone;
                    c.WaterTop = -1;
                }
                c.Top = top;
                c.IceBottom = int.MaxValue;
            }
        }

        /// <summary>Whether a box (blocks) comes within a story location's radius and blend.</summary>
        public static bool Intersects(List<FlatZone> zones, EarthGeometry geo, double minX, double minZ, double maxX, double maxZ)
        {
            // in a wrapping world a zone holds on every lap of the ring, as Apply wraps its distance
            int laps = geo.Wraps ? 1 : 0;
            foreach (var zone in zones)
            {
                for (int lap = -laps; lap <= laps; lap++)
                {
                    double zx = zone.X + lap * geo.RingLength;
                    double gx = Math.Max(0, Math.Max(minX - (zx + zone.HalfX), (zx - zone.HalfX) - maxX));
                    double gz = Math.Max(0, Math.Max(minZ - (zone.Z + zone.HalfZ), (zone.Z - zone.HalfZ) - maxZ));
                    double r = zone.Radius + zone.Blend;
                    if (gx * gx + gz * gz <= r * r) return true;
                }
            }
            return false;
        }
    }
}
