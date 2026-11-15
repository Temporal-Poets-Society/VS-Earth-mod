using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Geothermal rules (EarthParams.GeothermalRules, decision 0057; the author: "Hot springs should be only in places with
    /// hot springs", and of lava: "now sometimes it spawns out of mountain side or randomly inside rock"). Vanilla draws one
    /// random "geologic activity" byte per chunk on its climate map and makes from it:
    /// - hot springs above a deep, wide cave where it is over 128 (GenCaves, GenHotSprings);
    /// - lava springs in cliffs and cave walls, more of them the higher it is, up to y = activity / 2 x map height / 256
    ///   + 10 (y 520 in a 1024-high world; GenRivulets);
    /// - lava at cave bottoms below y 12, up to y = activity x 16 / 128, basalt above it (GenCaves).
    /// Earth's climate map passed that noise through, so hot springs and lava came anywhere.
    ///
    /// From rules 1:
    /// - The byte is <see cref="Activity"/>: heat under the GVP's Holocene volcanoes, 0 everywhere else. It is never over
    ///   128, so no hot spring comes from it: over 128 vanilla also floods every deep cave floor with lava, which would put
    ///   magma under every hot spring (Bath, Budapest).
    /// - Hot springs are the known ones: each OpenStreetMap or GeoNames hot spring and geyser is placed as a vanilla hot
    ///   spring where its ground allows one (EarthWorldGen.KnownHotSprings).
    /// - Vanilla's lava springs are off (EarthWorldGen's spring guard); open lava waits for real volcanoes (0057 part 2).
    ///
    /// From rules 2, volcanoes get bodies: magma chambers, conduits, craters and lava lakes (Volcanoes.cs, EarthVolcanoes).
    /// From rules 3 (decision 0078, EA-0067), their interiors in real proportions: the cone's own layered rock, a lens-shaped
    /// chamber, a conduit with a cupola and a funnel, dykes and a sill, no caves next to them (VolcanoInterior.cs).
    /// </summary>
    public static partial class Geothermal
    {
        public const int RulesVersion = 3;

        /// <summary>The kinds of site (tools/bake_geothermal_sites.py writes these numbers). Volcanoes by the GVP's evidence:
        /// Active, an eruption dated in the Common Era; Holocene, dated before it or credible without a date; Uncertain.</summary>
        public enum SiteKind : byte { Active, Holocene, Uncertain, HotSpring, Geyser }

        public sealed class Site
        {
            public readonly string Name;
            public readonly double Lat, Lon;
            public readonly SiteKind Kind;
            public Site(string name, double lat, double lon, SiteKind kind) { Name = name; Lat = lat; Lon = lon; Kind = kind; }
            public bool IsSpring => Kind is SiteKind.HotSpring or SiteKind.Geyser;
        }

        /// <summary>The most activity any place gets: vanilla's hot springs need more.</summary>
        public const int MaxActivity = 128;

        /// <summary>
        /// A volcano's heat (0..<see cref="MaxActivity"/>) <paramref name="km"/> from it, falling linearly to 0. At 128 every
        /// cave cell below y 12 is lava, at 64 those up to y 8, so the deep lava thins out away from the vent: within about
        /// 29 km of an active volcano, 19 km of an older Holocene one and 9 km of an uncertain one. Springs give none.
        /// </summary>
        public static double Heat(SiteKind kind, double km)
        {
            var (peak, reach) = kind switch
            {
                SiteKind.Active => (128.0, 30.0),
                SiteKind.Holocene => (128.0, 20.0),
                SiteKind.Uncertain => (96.0, 10.0),
                _ => (0.0, 0.0),
            };
            return km >= reach ? 0 : peak * (reach - km) / reach;
        }

        /// <summary>The farthest any site's heat reaches (km).</summary>
        const double MaxReach = 30;
        const double KmPerDegree = 111.32;
        static readonly Site[][] volcanoRows, springRows;

        // In the static constructor, not field initializers: the site arrays live in the generated parts of this partial
        // class, and the order of field initializers across parts is not defined.
        static Geothermal()
        {
            var volcanoes = new List<Site>();
            for (int i = 0; i < GvpKind.Length; i++)
                volcanoes.Add(new Site(GvpName[i], GvpLatLon[2 * i], GvpLatLon[2 * i + 1], (SiteKind)GvpKind[i]));
            var springs = new List<Site>();
            void AddSprings(float[] latLon, byte[] kind)
            {
                for (int i = 0; i < kind.Length; i++) springs.Add(new Site(null, latLon[2 * i], latLon[2 * i + 1], (SiteKind)kind[i]));
            }
            AddSprings(OsmSpringLatLon, OsmSpringKind);
            AddSprings(GeoNamesSpringLatLon, GeoNamesSpringKind);
            volcanoRows = Rows(volcanoes);
            springRows = Rows(springs);
            VolcanoCount = volcanoes.Count;
            SpringCount = springs.Count;
            bodies = BuildBodies();
        }

        static int Row(double lat) => Math.Clamp((int)Math.Floor(lat + 90), 0, 179);

        static Site[][] Rows(List<Site> sites)
        {
            var lists = new List<Site>[180];
            for (int i = 0; i < 180; i++) lists[i] = new();
            foreach (var s in sites) lists[Row(s.Lat)].Add(s);
            var result = new Site[180][];
            for (int i = 0; i < 180; i++) result[i] = lists[i].ToArray();
            return result;
        }

        /// <summary>Sites (for tests and the log).</summary>
        public static readonly int VolcanoCount, SpringCount;

        /// <summary>The hot springs and geysers between two latitudes.</summary>
        public static IEnumerable<Site> SpringsBetween(double latMin, double latMax)
        {
            for (int r = Row(latMin); r <= Row(latMax); r++)
                foreach (var s in springRows[r])
                    if (s.Lat >= latMin && s.Lat <= latMax) yield return s;
        }

        /// <summary>The volcanoes between two latitudes.</summary>
        public static IEnumerable<Site> VolcanoesBetween(double latMin, double latMax)
        {
            for (int r = Row(latMin); r <= Row(latMax); r++)
                foreach (var s in volcanoRows[r])
                    if (s.Lat >= latMin && s.Lat <= latMax) yield return s;
        }

        /// <summary>Earth's geologic activity (0..<see cref="MaxActivity"/>) at a place: the most any volcano gives there.</summary>
        public static int Activity(double lat, double lon)
        {
            int row = Row(lat);
            double coslat = Math.Max(Math.Cos(lat * Math.PI / 180), 0.01);
            double best = 0;
            for (int r = Math.Max(0, row - 1); r <= Math.Min(179, row + 1); r++)
            {
                foreach (var s in volcanoRows[r])
                {
                    double dLat = (s.Lat - lat) * KmPerDegree;
                    if (Math.Abs(dLat) >= MaxReach) continue;
                    double dLon = s.Lon - lon;
                    if (dLon > 180) dLon -= 360; else if (dLon < -180) dLon += 360;
                    dLon *= KmPerDegree * coslat;
                    if (Math.Abs(dLon) >= MaxReach) continue;
                    double heat = Heat(s.Kind, Math.Sqrt(dLat * dLat + dLon * dLon));
                    if (heat > best) best = heat;
                }
            }
            return Math.Min(MaxActivity, (int)Math.Round(best));
        }
    }
}
