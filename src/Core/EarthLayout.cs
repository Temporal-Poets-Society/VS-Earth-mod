using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Places the Earth on Cylinder World's ring and latitudes (Earth needs Cylinder World, decision 0023), so its
    /// longitude and latitude match the real ones: the ring between the jump lines holds 360°, the equator is
    /// Cylinder World's and its poles are at the walls.
    /// </summary>
    public sealed class LayoutInput
    {
        /// <summary>The world's width; only bounds the gutter margin.</summary>
        public int WorldWidth;

        public double CwRingStart, CwRingLength, CwEquatorZ, CwPoleDistance;
        /// <summary>How far Cylinder World's flat seam sea reaches in from each jump line.</summary>
        public double CwBandInner;

        /// <summary>Extra open sea beyond the seam band on each side of the jump line.</summary>
        public double GutterMargin = 512;

        /// <summary>Insert the seam gutter (open sea around the jump line). False (new Earth worlds, decision 0030): no gutter,
        /// the real Earth runs across the jump line.</summary>
        public bool Gutter = true;
    }

    public static class EarthLayout
    {
        /// <summary>
        /// The layout settings made usable before a world freezes them: the gutter margin finite, not negative and at most
        /// an eighth of the world's width (wider leaves no Earth: the scale becomes 0 and every coordinate NaN). Returns the
        /// settings replaced (empty when fine).
        /// </summary>
        public static string Sanitize(LayoutInput inp)
        {
            var bad = new System.Collections.Generic.List<string>();
            double eighth = Math.Max(0, inp.WorldWidth / 8.0);
            if (!(double.IsFinite(inp.GutterMargin) && inp.GutterMargin >= 0 && inp.GutterMargin <= eighth)) { bad.Add("GutterMargin " + inp.GutterMargin); inp.GutterMargin = Math.Min(512, eighth); }
            return string.Join(", ", bad);
        }

        public static EarthGeometry Build(LayoutInput inp, EarthData data, out string report)
        {
            Sanitize(inp);
            var g = new EarthGeometry
            {
                SeamLon0 = SeamLon,
                RingStart = inp.CwRingStart,
                RingLength = inp.CwRingLength,
                Wraps = true,
                Gutter = inp.Gutter ? Math.Ceiling(2 * (inp.CwBandInner + inp.GutterMargin) / 64) * 64 : 0,
                EquatorZ = inp.CwEquatorZ,
                BlocksPerDegreeZ = inp.CwPoleDistance / 90.0,
            };

            double stretch = g.BlocksPerDegreeZ / g.BlocksPerDegreeX;
            double latSpan = Math.Min(90, g.EquatorZ / g.BlocksPerDegreeZ);
            report = string.Format(
                "Earth layout: {0:0.#} blocks per degree east-west ({1:0} m per block at the equator), {2:0.#} north-south (stretch {3:0.00}), " +
                "latitudes shown ±{4:0.#}°, seam gutter {5:0} blocks, world centre at {6:0.0}°{7}.",
                g.BlocksPerDegreeX, 40075016.0 / 360 / g.BlocksPerDegreeX, g.BlocksPerDegreeZ, stretch, latSpan, g.Gutter,
                Math.Abs(g.CenterLon), g.CenterLon >= 0 ? "E" : "W");
            if (stretch > 1.3 || stretch < 0.77)
            {
                // Cylinder World's ring is 2 x the length only with its EarthProportions, on a map at least twice as wide as long
                report += string.Format(" Warning: the Earth is stretched {0:0.00}x north-south. For Earth's proportions use a world at least twice as wide as long " +
                    "(e.g. 256000 x 102400 or 512000 x 204800) with Cylinder World's EarthProportions.", stretch);
            }
            return g;
        }

        /// <summary>
        /// The meridian the Earth is cut along (decision 0019). On the 2' grid it crosses only St. Lawrence Island
        /// (63.2-63.3°N) and the Antarctic plateau, and north of 60°S passes at least 0.1° from all other land: west of Big
        /// Diomede in the Bering Strait, east of the Pribilofs, of Samoa's Manu'a group and of Niue.
        /// </summary>
        public const double SeamLon = -169.25;
    }
}
