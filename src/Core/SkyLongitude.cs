using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's longitude for the sky, for other mods (a sun, moon or clock that follows the meridians): degrees east at
    /// a world X, or null where Earth has no answer (Earth not active in this world, or X not a number). Uses
    /// EarthGeometry.SkyLonAt, the X column's meridian, so one X column has one solar time.
    /// The layout is set once per world and never changed in place, so At is safe on any thread.
    /// </summary>
    public sealed class SkyLongitude
    {
        volatile EarthGeometry geometry;

        /// <summary>The world's layout, or null when Earth is not active in it. A layout that holds no Earth counts as none.</summary>
        public void Set(EarthGeometry geometry)
        {
            this.geometry = geometry != null && EarthGeometry.IsUsableLayout(geometry.RingLength, geometry.Gutter, geometry.BlocksPerDegreeZ, geometry.EquatorZ)
                ? geometry : null;
        }

        public bool Active => geometry != null;

        public double? At(double x)
        {
            var g = geometry;
            if (g == null || !double.IsFinite(x)) return null;
            double lon = g.SkyLonAt(x);
            return double.IsFinite(lon) ? lon : null;
        }
    }
}
