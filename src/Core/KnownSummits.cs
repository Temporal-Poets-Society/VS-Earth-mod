using System;

namespace EarthMap.Core
{
    /// <summary>Decision 0032: bounded smooth summit caps for peaks still lost in the 30 arc-second means.</summary>
    public static class KnownSummits
    {
        public const int Version = 3;   // 2: pre.9's round caps (domes, moats round low hills); 3: the ground lifted to the survey, sharpest at the top
        public sealed class Peak
        {
            public readonly string Name;
            public readonly double Lat, Lon, Height, Radius;
            public Peak(string name, double lat, double lon, double height, double radius = 3000)
            { Name=name; Lat=lat; Lon=lon; Height=height; Radius=radius; }
        }
        public static readonly Peak[] Peaks = {
            new Peak("Everest",27.9881,86.9250,8849,1400), new Peak("Lhotse",27.9617,86.9330,8516,1400),
            new Peak("Makalu",27.8897,87.0883,8485), new Peak("K2",35.8808,76.5158,8611),
            new Peak("Mont Blanc",45.8326,6.8652,4808), new Peak("Fuji",35.3606,138.7274,3776),
            new Peak("Kilimanjaro",-3.0674,37.3556,5895), new Peak("Denali",63.0695,-151.0074,6190),
            new Peak("Aconcagua",-32.6532,-70.0109,6961)
        };

        // Quintic blend has zero first and second derivatives at each end.
        static double Smooth(double t) { t=Math.Clamp(t,0,1); return t*t*t*(10+t*(-15+6*t)); }

        public static double Restore(EarthGeometry geo, double x, double z, double ground, PeakCatalog catalog = null, int rules = Version,
            Func<double,double,double> baseGround = null)
            => Restore(geo, x, z, ground, 0, catalog, rules, baseGround);

        /// <summary>Each peak's playable top column and support radius (RestorePeak) for one world's geometry, per catalogue and
        /// summit cutoff, so the columns near a peak don't scan the catalogue round it again. Pure, so every thread gets the
        /// same values. A terrain model keeps one.</summary>
        public sealed class SupportCache
        {
            internal readonly System.Collections.Concurrent.ConcurrentDictionary<(Peak, PeakCatalog, int), (double X, double Z, double Radius)> Map = new();
        }

        public static double Restore(EarthGeometry geo, double x, double z, double ground, int minElevation,
            PeakCatalog catalog = null, int rules = Version, Func<double,double,double> baseGround = null, SupportCache supports = null)
        {
            if (minElevation < 0) return ground;
            var g=geo.ToGeo(x,z);
            if(catalog==null) foreach(var p in Peaks) { if(p.Height>=minElevation) ground=RestorePeak(geo,x,z,g.Lat,g.Lon,ground,p,null,rules,baseGround,minElevation,supports); }
            else foreach(var p in catalog.Near(g.Lat,g.Lon)) { if(p.Height>=minElevation) ground=RestorePeak(geo,x,z,g.Lat,g.Lon,ground,p,catalog,rules,baseGround,minElevation,supports); }
            return ground;
        }
        static double RestorePeak(EarthGeometry geo,double x,double z,double lat,double lon,double ground,Peak p,PeakCatalog catalog,int rules,
            Func<double,double,double> baseGround,int minElevation,SupportCache supports)
        {
            if(Math.Abs(lat-p.Lat)>.06 || Math.Abs(Geo.Wrap180(lon-p.Lon))*Math.Cos(p.Lat*Math.PI/180)>.06) return ground;
            double px,pz,radius;
            if(supports!=null && supports.Map.TryGetValue((p,catalog,minElevation),out var known)) { px=known.X; pz=known.Z; radius=known.Radius; }
            else
            {
                geo.FromGeo(p.Lat,p.Lon,out px,out pz);
                // Anchor the top on a playable column, rather than between columns (which loses the top block).
                px=Math.Round(px); pz=Math.Round(pz);
                radius=p.Radius;
                if(catalog!=null) foreach(var other in catalog.Near(p.Lat,p.Lon))
                {
                    if(ReferenceEquals(p,other) || other.Height<minElevation) continue;
                    double ox=Geo.Wrap180(p.Lon-other.Lon)*Geo.MetresPerDegree*Math.Cos(p.Lat*Math.PI/180), oz=(p.Lat-other.Lat)*Geo.MetresPerDegree;
                    radius=Math.Min(radius,Math.Sqrt(ox*ox+oz*oz)*.45);
                }
                supports?.Map.TryAdd((p,catalog,minElevation),(px,pz,radius));
            }
            double dx=(x-px)*Geo.MetresPerDegree*Math.Cos(p.Lat*Math.PI/180)/geo.BlocksPerDegreeX;
            double dz=(z-pz)*Geo.MetresPerDegree/geo.BlocksPerDegreeZ;
            double r=Math.Sqrt(dx*dx+dz*dz)/Math.Max(1,radius);
            if(r>=1) return ground;
            if(rules>=3 && baseGround!=null)
            {
                // Rules 3: lift the ground's own shape by what the grid lost at the summit, the lift sharpest at the
                // top and gone at the radius, so ridges keep their shape and the top comes to a point (rules 2's round
                // caps made domes with flat tops, and dug moats round low hills). Bounded so no column becomes a spike.
                double deficit=Math.Clamp(p.Height + .000001 - baseGround(px,pz),-0.3*radius,1.0*radius);
                double f=(1-r)*(1-r);
                return ground + deficit*f;
            }
            // Rules 2 (pre.9 saves): a rounded cap. The outer 75% joins the original slopes with C2 continuity.
            double cap=p.Height + .000001 - 1200*r*r;
            double weight=1-Smooth((r-.25)/.75);
            ground += weight*(cap-ground);
            return ground;
        }
    }
}
