using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Real elevation (m) to block height. The vertical scale is independent of the horizontal one and follows the
    /// world height: Mount Everest (8849 m) reaches TopY, the deepest trench (~11 km) reaches SeaLevel - MaxDepth.
    /// Both curves are logarithmic, so lowlands and shelves get the most exaggeration and high mountains are
    /// compressed:  y = sea + S·ln(1 + h/H0)  on land,  y = sea - 1 - D·ln(1 + depth/D0)  under water.
    /// A 384-high world (sea 165, top 360): 100 m → +21, 500 m → +64, 1000 m → +92, 4000 m → +156, Everest +195;
    /// 26 m deep → −10, 200 m → −28, 4000 m → −60, 11 km → −71.
    /// </summary>
    public sealed class VerticalScale
    {
        public const double Everest = 8849, Trench = 11000;

        public readonly int SeaLevel;
        public readonly bool LinearLand;
        public readonly int MapHeight;
        public readonly double TopY, LandH0, MaxDepth, SeaD0;
        readonly double s, d;

        public VerticalScale(int mapHeight, int seaLevel, double topY, double landH0, double maxDepth, double seaD0, bool linearLand = false)
        {
            MapHeight = mapHeight;
            LinearLand = linearLand;
            SeaLevel = seaLevel;
            TopY = topY;
            LandH0 = landH0;
            MaxDepth = maxDepth;
            SeaD0 = seaD0;
            s = (TopY - SeaLevel) / Math.Log(1 + Everest / LandH0);
            d = MaxDepth / Math.Log(1 + Trench / SeaD0);
        }

        /// <summary>
        /// The curve settings (EarthMapConfig LandCurve, SeaCurve, TopMargin, MaxSeaDepth) made usable: a curve that isn't a
        /// finite positive number (0 gives NaN heights), a top margin outside 0..0.5 or a sea depth outside 0.05..0.95 takes its
        /// default. Returns the settings that were replaced (empty when all were fine).
        /// </summary>
        public static string Sanitize(ref double landH0, ref double seaD0, ref double topMarginFraction, ref double maxDepthFraction)
        {
            var bad = new System.Collections.Generic.List<string>();
            if (!(double.IsFinite(landH0) && landH0 > 0)) { bad.Add("LandCurve " + landH0); landH0 = 200; }
            if (!(double.IsFinite(seaD0) && seaD0 > 0)) { bad.Add("SeaCurve " + seaD0); seaD0 = 20; }
            if (!(double.IsFinite(topMarginFraction) && topMarginFraction >= 0 && topMarginFraction < .5)) { bad.Add("TopMargin " + topMarginFraction); topMarginFraction = .0625; }
            if (!(double.IsFinite(maxDepthFraction) && maxDepthFraction >= .05 && maxDepthFraction <= .95)) { bad.Add("MaxSeaDepth " + maxDepthFraction); maxDepthFraction = .43; }
            return string.Join(", ", bad);
        }

        /// <summary>Whether this scale gives finite heights both ways (a world saved with a broken curve doesn't).</summary>
        public bool IsUsable => double.IsFinite(s) && s > 0 && double.IsFinite(d) && d > 0 && LandH0 > 0 && SeaD0 > 0 && TopY > SeaLevel && MaxDepth > 0;

        /// <summary>Defaults for a world height: top 6% below the build limit, sea floor down to 60% of the sea level's height.</summary>
        public static VerticalScale ForWorld(int mapHeight, int seaLevel, double topMarginFraction = 0.0625, double landH0 = 200, double seaD0 = 20, double maxDepthFraction = 0.43)
        {
            double top = mapHeight - Math.Max(12, Math.Round(mapHeight * topMarginFraction));
            double depth = Math.Min(seaLevel - 12, Math.Round(seaLevel * maxDepthFraction));
            return new VerticalScale(mapHeight, seaLevel, top, landH0, depth, seaD0);
        }

        /// <summary>Fractional block height of a real elevation; the top solid block is floor() of this.</summary>
        public double Y(double h)
        {
            if (h >= 0) return SeaLevel + (LinearLand ? (TopY - SeaLevel) * h / Everest : s * Math.Log(1 + h / LandH0));
            return SeaLevel - 1 - d * Math.Log(1 - h / SeaD0);
        }

        /// <summary>Inverse of Y (real elevation for a block height).</summary>
        public double Elevation(double y)
        {
            if (y >= SeaLevel) return LinearLand ? (y - SeaLevel) * Everest / (TopY - SeaLevel) : LandH0 * (Math.Exp((y - SeaLevel) / s) - 1);
            if (y >= SeaLevel - 1) return 0;
            return -SeaD0 * (Math.Exp((SeaLevel - 1 - y) / d) - 1);
        }

        /// <summary>Blocks per metre at elevation h (land).</summary>
        public double Slope(double h) => h >= 0 ? (LinearLand ? (TopY - SeaLevel) / Everest : s / (LandH0 + h)) : d / (SeaD0 - h);
    }
}
