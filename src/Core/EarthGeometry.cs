using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Where the Earth lies in the world. Equirectangular: x runs along longitude, z along latitude, both linear.
    ///
    /// X: the ring (RingStart .. RingStart + RingLength) holds 360° of longitude. In Earth worlds it is Cylinder World's
    /// ring between the jump lines and wraps; a geometry that doesn't wrap is left only for offline tests. A strip of open sea
    /// (the gutter, Gutter blocks wide) is inserted where the ring's ends meet, so the jump line always has open sea
    /// around it. The Earth is cut along the straight meridian SeamLon0 (decision 0019), so every meridian is a
    /// straight X column.
    ///
    /// Z: latitude = (EquatorZ - z) / BlocksPerDegreeZ, north = smaller z (Cylinder World's layout).
    /// </summary>
    public sealed class EarthGeometry
    {
        public double RingStart;
        public double RingLength;
        public bool Wraps;
        public double Gutter;
        public double EquatorZ;
        public double BlocksPerDegreeZ;
        /// <summary>The meridian the Earth is cut along (the jump line).</summary>
        public double SeamLon0;

        public double BlocksPerDegreeX => (RingLength - Gutter) / 360.0;

        /// <summary>Whether a layout can place the Earth: finite positive scales and a gutter that leaves part of the ring.</summary>
        public static bool IsUsableLayout(double ringLength, double gutter, double blocksPerDegreeZ, double equatorZ)
            => double.IsFinite(ringLength) && double.IsFinite(gutter) && gutter >= 0 && ringLength - gutter > 0
               && double.IsFinite(blocksPerDegreeZ) && blocksPerDegreeZ > 0 && double.IsFinite(equatorZ);

        /// <summary>Longitude at the middle of the ring (the world's centre meridian).</summary>
        public double CenterLon => Geo.Wrap180(SeamLon0 + 180);

        public struct GeoPos
        {
            public double Lat, Lon;
            /// <summary>Inside the inserted sea strip at the ring's ends (or outside the ring on a non-wrapping map).</summary>
            public bool InGutter;
            /// <summary>In the gutter: blocks from its edge (where the Earth ends) into it.</summary>
            public double GutterDepth;
            /// <summary>0..1 along the Earth's 360° (0 and 1 = the seam).</summary>
            public double U;
            /// <summary>Beyond ±90° (only on maps longer than the Earth needs).</summary>
            public bool BeyondPole;
        }

        public GeoPos ToGeo(double x, double z)
        {
            var g = new GeoPos();
            double lat = (EquatorZ - z) / BlocksPerDegreeZ;
            if (lat > 90 || lat < -90)
            {
                g.BeyondPole = true;
                lat = Math.Clamp(lat, -90, 90);
            }
            g.Lat = lat;
            double u = UAt(x, out g.InGutter, out g.GutterDepth);
            g.U = u;
            g.Lon = LonAt(u);
            return g;
        }

        /// <summary>0..1 along the Earth's 360° at x (0 and 1 = the seam). In the gutter, or outside the ring on a
        /// non-wrapping map, u is clamped to the nearer end.</summary>
        public double UAt(double x, out bool inGutter, out double gutterDepth)
        {
            double pos = x - RingStart;
            if (Wraps)
            {
                pos %= RingLength;
                if (pos < 0) pos += RingLength;
            }
            double half = Gutter / 2;
            inGutter = false;
            gutterDepth = 0;
            if (pos < half)
            {
                inGutter = true;
                gutterDepth = half - pos;
                return 0;
            }
            if (pos > RingLength - half)
            {
                inGutter = true;
                gutterDepth = pos - (RingLength - half);
                return 1;
            }
            return (pos - half) / (RingLength - Gutter);
        }

        /// <summary>
        /// The longitude of the X column x, for the sky: the same as ToGeo's at every latitude, so one X column has one
        /// solar time. The gutter has the seam's meridian (u = 0 and u = 1 are the same), so the sky is continuous across
        /// the jump line.
        /// </summary>
        public double SkyLonAt(double x) => LonAt(UAt(x, out _, out _));

        public double LonAt(double u) => Geo.Wrap180(SeamLon0 + u * 360);

        /// <summary>World position of a latitude/longitude (x within the ring).</summary>
        public void FromGeo(double lat, double lon, out double x, out double z)
        {
            z = EquatorZ - lat * BlocksPerDegreeZ;
            double east = (lon - SeamLon0) % 360;
            if (east < 0) east += 360;
            x = RingStart + Gutter / 2 + east / 360 * (RingLength - Gutter);
        }

        /// <summary>
        /// Whether a straight piece between two positions from FromGeo jumps across the seam (from one end of the ring to the
        /// other). Real pieces of Earth's lines are far shorter than half the Earth's width, whatever the world's size.
        /// </summary>
        public bool AcrossSeam(double x1, double x2) => Math.Abs(x2 - x1) > (RingLength - Gutter) / 2;

        /// <summary>x moved into the ring (only for a wrapping world).</summary>
        public double WrapX(double x)
        {
            if (!Wraps) return x;
            double pos = (x - RingStart) % RingLength;
            if (pos < 0) pos += RingLength;
            return RingStart + pos;
        }

        /// <summary>Blocks along x from x to the nearest ring end (the jump line / map edge).</summary>
        public double DistToRingEnd(double x)
        {
            double pos = x - RingStart;
            if (Wraps)
            {
                pos %= RingLength;
                if (pos < 0) pos += RingLength;
            }
            return Math.Min(Math.Abs(pos), Math.Abs(RingLength - pos));
        }
    }
}
