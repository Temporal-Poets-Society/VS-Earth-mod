using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Geothermal rules 2 (decision 0057 part 2; the author: "We need to make proper volcanoes, with magma chamber etc. Ash and
    /// active volcanoes could have lava lake inside", then "download what is needed and build it"). The GVP's Holocene
    /// volcanoes that are mountains (composite cones, shields and calderas; not volcanic fields or minor vents) get a body:
    /// - A magma chamber: a lens of lava deep under the vent, in a shell of basalt. Volcanoes active in the game's time
    ///   (the game is some 100-200 years after 1318: a confirmed eruption starting 1300-1700, or a busy one since) have a
    ///   full lens; older Holocene ones a smaller lava core in a body of basalt.
    /// - A conduit from the chamber to the vent: lava under a thin basalt crust in active volcanoes, solid basalt in the others.
    /// - In active cones a summit crater, at least a few blocks across where the heights are too coarse to show one.
    /// - A lava lake in the crater of the eight volcanoes with persistent lava lakes in the historical record.
    /// - Fresh ash (Earth's tephra) over the ground around an active vent on land, thickest at the vent.
    /// Sizes are real (metres) and kept to a few blocks at coarse scales. Mining into a chamber is dangerous by design.
    /// </summary>
    public static partial class Geothermal
    {
        /// <summary>The GVP's volcanic landform (tools/bake_geothermal_sites.py writes these numbers; its three Minor ones are one).</summary>
        public enum Landform : byte { Composite, Shield, Caldera, Cluster, Minor }

        /// <summary>A persistent lava lake (lake and crater radius, crater depth in metres). The vent is searched for within
        /// <see cref="SearchM"/> of the GVP's place, or of <see cref="Lat"/>/<see cref="Lon"/> where the lake is elsewhere.</summary>
        public sealed class LavaLakeSite
        {
            public readonly int Number;
            public readonly string Name;
            public readonly double Lat, Lon, RadiusM, CraterRadiusM, CraterDepthM, SearchM;
            public LavaLakeSite(int number, string name, double lat, double lon, double radiusM, double craterRadiusM, double craterDepthM, double searchM)
            {
                Number = number; Name = name; Lat = lat; Lon = lon; RadiusM = radiusM; CraterRadiusM = craterRadiusM; CraterDepthM = craterDepthM; SearchM = searchM;
            }
        }

        /// <summary>The volcanoes with persistent lava lakes (0057 part 2), by GVP number. Kilauea's is Halemaumau, in the
        /// southwest of its caldera; Ambrym's Marum.</summary>
        public static readonly LavaLakeSite[] LavaLakes =
        {
            new(221080, "Erta Ale", double.NaN, double.NaN, 50, 150, 80, 600),
            new(223030, "Nyiragongo", double.NaN, double.NaN, 120, 600, 250, 1000),
            new(332010, "Kilauea", 19.4069, -155.2834, 150, 450, 85, 200),
            new(390020, "Erebus", double.NaN, double.NaN, 20, 125, 100, 800),
            new(344100, "Masaya", double.NaN, double.NaN, 25, 250, 150, 200),
            new(357120, "Villarrica", double.NaN, double.NaN, 15, 100, 120, 800),
            new(257040, "Ambrym", double.NaN, double.NaN, 30, 400, 250, 1500),
            new(223020, "Nyamulagira", double.NaN, double.NaN, 50, 500, 150, 1200),
        };

        public sealed class Volcano
        {
            public readonly int Number;
            public readonly string Name;
            public readonly double Lat, Lon;
            public readonly SiteKind Kind;
            public readonly Landform Form;
            /// <summary>The GVP's summit elevation (m).</summary>
            public readonly int ElevationM;
            /// <summary>Erupting in the game's time (see the class notes).</summary>
            public readonly bool Era;
            public readonly LavaLakeSite Lake;

            public Volcano(int number, string name, double lat, double lon, SiteKind kind, Landform form, int elevationM, bool era, LavaLakeSite lake)
            {
                Number = number; Name = name; Lat = lat; Lon = lon; Kind = kind; Form = form; ElevationM = elevationM; Era = era; Lake = lake;
            }

            public bool Active => Era || Lake != null;
            public bool HasBody => Lake != null || Kind != SiteKind.Uncertain && Form is Landform.Composite or Landform.Shield or Landform.Caldera;
            /// <summary>A summit crater is carved (where the vent is dry land).</summary>
            public bool HasCrater => Lake != null || Active && Form is Landform.Composite or Landform.Shield;
            public double VentLat => Lake != null && !double.IsNaN(Lake.Lat) ? Lake.Lat : Lat;
            public double VentLon => Lake != null && !double.IsNaN(Lake.Lon) ? Lake.Lon : Lon;
        }

        static Volcano[] bodies;

        /// <summary>The volcanoes that get a body, in the baked order.</summary>
        public static IReadOnlyList<Volcano> Bodies => bodies;

        static Volcano[] BuildBodies()
        {
            var lakes = new Dictionary<int, LavaLakeSite>();
            foreach (var l in LavaLakes) lakes[l.Number] = l;
            var list = new List<Volcano>();
            for (int i = 0; i < GvpNumber.Length; i++)
            {
                lakes.TryGetValue(GvpNumber[i], out var lake);
                var v = new Volcano(GvpNumber[i], GvpName[i], GvpLatLon[2 * i], GvpLatLon[2 * i + 1], (SiteKind)GvpKind[i], (Landform)GvpForm[i],
                    GvpElevation[i], GvpEra[i], lake);
                if (v.HasBody) list.Add(v);
            }
            return list.ToArray();
        }

        /// <summary>The GVP's volcano of this name, whether it gets a body or not (for tests and the log); null when there is none.</summary>
        public static Volcano FindVolcano(string name)
        {
            for (int i = 0; i < GvpNumber.Length; i++)
            {
                if (GvpName[i] != name) continue;
                LavaLakeSite lake = null;
                foreach (var l in LavaLakes) if (l.Number == GvpNumber[i]) lake = l;
                return new Volcano(GvpNumber[i], GvpName[i], GvpLatLon[2 * i], GvpLatLon[2 * i + 1], (SiteKind)GvpKind[i], (Landform)GvpForm[i],
                    GvpElevation[i], GvpEra[i], lake);
            }
            return null;
        }
    }

    /// <summary>
    /// Where one volcano's body goes in one world (geothermal rules 2), from the world's own terrain model, so every chunk
    /// that holds a part of it agrees. Block coordinates; y is the block's height.
    /// </summary>
    public sealed class VolcanoPlan
    {
        /// <summary>A crater of an active cone without a lava lake: radius and depth (m).</summary>
        public const double CraterRadiusM = 200, CraterDepthM = 100;
        /// <summary>How far from the GVP's place the vent is looked for (m): the summit's middle, the centre of the highest
        /// ground (a crater's rim, or a peak).</summary>
        public const double SearchM = 1200;
        /// <summary>Chamber half-widths (m): active, dormant; calderas have wider ones.</summary>
        public const double ActiveHalfWidthM = 800, DormantHalfWidthM = 500, CalderaWidening = 1.5;
        /// <summary>Chamber half-heights (m): active, dormant.</summary>
        public const double ActiveHalfHeightM = 300, DormantHalfHeightM = 200;
        /// <summary>A dormant chamber's lava core, as a share of the chamber.</summary>
        public const double DormantCore = 0.6;
        /// <summary>The conduit's radius (m), at least one block.</summary>
        public const double ConduitRadiusM = 20;
        /// <summary>Depth of a lava lake's lava (m), at least two blocks.</summary>
        public const double LakeDepthM = 15;
        /// <summary>Fresh ash around an active volcano's vent on land: how far (m, at least 6 blocks) and how thick at the vent
        /// (blocks), thinning outwards to a ragged edge.</summary>
        public const double AshRadiusM = 2500;
        public const int AshMaxBlocks = 3;
        /// <summary>Basalt shell around a chamber's lava (blocks).</summary>
        public const int Shell = 2;
        /// <summary>Basalt crust over an active conduit's lava (blocks), the vent's surface block included.</summary>
        public const int Crust = 3;
        /// <summary>How far a crater's edge strays from a circle, as a share of its wall's width, so its walls aren't rings of
        /// perfect circles. A lava lake and the rock next to it stay round.</summary>
        public const double CraterWobble = 0.15;

        public readonly Geothermal.Volcano Volcano;
        public readonly int VentX, VentZ;
        /// <summary>The highest ground near the vent, and the vent's surface after its crater or lake.</summary>
        public readonly int SummitY, VentTop;
        public readonly bool Submarine;

        public readonly bool Crater, Lake;
        /// <summary>The crater's radius where its edge neither reaches out nor pulls in, and the lake's.</summary>
        public readonly double CraterR, LakeR;
        public readonly int RimY, FloorY, LakeY, LakeFloorY;
        /// <summary>Phases of the crater edge's three waves around the vent, from the volcano's number.</summary>
        readonly double phase2, phase3, phase5;

        public readonly int ChamberY;
        /// <summary>The chamber's lava (half-width, half-height) and its whole body with the shell (blocks).</summary>
        public readonly double LavaA, LavaB, BodyA, BodyB;

        public readonly double ConduitR;
        /// <summary>The conduit's lava reaches up to ConduitLavaTop (none when below ChamberY); basalt above it up to ConduitTop.</summary>
        public readonly int ConduitLavaTop, ConduitTop;

        /// <summary>The fresh ash's reach (blocks; 0 for none).</summary>
        public readonly double AshR;

        /// <summary>How far from the vent any part reaches (blocks).</summary>
        public readonly double Reach;

        /// <summary>The farthest any volcano's body can reach in a world at this scale (blocks), for indexing before planning.</summary>
        public static double MaxReach(double metresPerBlock)
        {
            double a = Math.Max(4, ActiveHalfWidthM * CalderaWidening / metresPerBlock) + Shell + 1;
            double crater = 0;
            foreach (var l in Geothermal.LavaLakes) crater = Math.Max(crater, l.CraterRadiusM);
            crater = Math.Max(crater, CraterRadiusM) / metresPerBlock * (1 + CraterWobble) + 8;
            double ash = Math.Max(6, AshRadiusM / metresPerBlock) + 1;
            return Math.Max(Math.Max(a, crater), ash);
        }

        /// <summary>Blocks per metre underground: the land's at sea level, at most 10 km between sea level and the bottom.</summary>
        public static double BlocksPerMetreUnder(VerticalScale vs) => Math.Min(vs.Slope(0), (vs.SeaLevel - 16) / 10000.0);

        public VolcanoPlan(Geothermal.Volcano v, TerrainModel model)
        {
            Volcano = v;
            var geo = model.Geometry;
            var vs = model.Vertical;
            double mpb = model.MetresPerBlock;
            double under = BlocksPerMetreUnder(vs);
            var lake = v.Lake;
            double craterRM = lake?.CraterRadiusM ?? CraterRadiusM, craterDM = lake?.CraterDepthM ?? CraterDepthM, searchM = lake?.SearchM ?? SearchM;
            geo.FromGeo(v.VentLat, v.VentLon, out double gx, out double gz);
            int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);

            // the vent: the middle of the highest ground near the GVP's place (a crater's rim around it, or a peak)
            int search = (int)Math.Max(1, Math.Round(searchM / mpb));
            int step = Math.Max(1, search / 16);
            var samples = new List<(int X, int Z, int Top)>();
            int summit = int.MinValue;
            for (int dz = -search; dz <= search; dz += step)
            {
                for (int dx = -search; dx <= search; dx += step)
                {
                    if (dx * dx + dz * dz > search * search) continue;
                    int top = model.GetColumn(x0 + dx, z0 + dz).Top;
                    samples.Add((x0 + dx, z0 + dz, top));
                    summit = Math.Max(summit, top);
                }
            }
            double summitElev = vs.Elevation(summit);
            double perM = vs.Slope(Math.Max(0, summitElev));
            int craterDepth = Math.Max(3, (int)Math.Round(craterDM * perM));
            double sx = 0, sz = 0;
            int n = 0;
            foreach (var s in samples)
            {
                if (s.Top < summit - Math.Max(2, craterDepth)) continue;
                sx += s.X; sz += s.Z; n++;
            }
            VentX = (int)Math.Round(sx / n);
            VentZ = (int)Math.Round(sz / n);
            SummitY = summit;
            var vent = model.GetColumn(VentX, VentZ);
            Submarine = vent.Water == TerrainModel.WaterSea || vent.Top < model.SeaLevel - 1;
            bool wet = vent.Water != TerrainModel.WaterNone || vent.WaterTop > vent.Top;
            int ventTop = vent.Top;

            // the crater, and the lava lake in it
            if (v.HasCrater && !wet)
            {
                Crater = true;
                Lake = lake != null;
                LakeR = Lake ? Math.Max(2, lake.RadiusM / mpb) : 0;
                CraterR = Math.Max(Lake ? LakeR + 3 : 3, craterRM / mpb);
                phase2 = 2 * Math.PI * Hash01(v.Number, 2);
                phase3 = 2 * Math.PI * Hash01(v.Number, 3);
                phase5 = 2 * Math.PI * Hash01(v.Number, 5);
                // the rim: the lowest ground along the crater's edge
                int rim = int.MaxValue;
                int points = Math.Max(24, (int)Math.Ceiling(2 * Math.PI * CraterR * (1 + CraterWobble)));
                for (int k = 0; k < points; k++)
                {
                    double a = 2 * Math.PI * k / points, r = EdgeRadius(a);
                    rim = Math.Min(rim, model.GetColumn(VentX + (int)Math.Round(r * Math.Cos(a)), VentZ + (int)Math.Round(r * Math.Sin(a))).Top);
                }
                RimY = rim;
                if (Lake)
                {
                    // the lava stands below every column next to it, so it never spills
                    int near = int.MaxValue;
                    int box = (int)Math.Ceiling(LakeR + 2);
                    for (int dz = -box; dz <= box; dz++)
                    {
                        for (int dx = -box; dx <= box; dx++)
                        {
                            double d = Math.Sqrt(dx * dx + dz * dz);
                            if (d > LakeR && d <= LakeR + 1.5) near = Math.Min(near, model.GetColumn(VentX + dx, VentZ + dz).Top);
                        }
                    }
                    LakeY = Math.Min(RimY - craterDepth, near - 1);
                    LakeFloorY = LakeY - Math.Max(2, (int)Math.Round(LakeDepthM * perM));
                    FloorY = LakeY + 1;
                    ventTop = LakeFloorY;
                }
                else
                {
                    FloorY = RimY - craterDepth;
                    ventTop = Math.Min(ventTop, FloorY);
                }
            }
            VentTop = ventTop;

            // the chamber, from the summit's real elevation down; above sea level on the land's scale, below it on the
            // underground one
            double depthM = lake != null || v.Form == Geothermal.Landform.Shield ? 2500 : v.Form == Geothermal.Landform.Caldera ? 4000 : 5000;
            double e = summitElev - depthM;
            double halfW = (v.Active ? ActiveHalfWidthM : DormantHalfWidthM) * (v.Form == Geothermal.Landform.Caldera ? CalderaWidening : 1);
            double a0 = Math.Max(4, halfW / mpb);
            double b0 = Math.Max(3, (v.Active ? ActiveHalfHeightM : DormantHalfHeightM) * under);
            double y = e >= 0 ? vs.Y(e) : vs.SeaLevel + e * under;
            // rock over it and under it: at least 8 blocks over the shell, 3 under
            double highest = VentTop - 8 - Shell - b0, lowest = 3 + Shell + b0;
            if (highest < lowest)
            {
                b0 = Math.Max(1, (VentTop - 11 - 2 * Shell) / 2.0);
                highest = VentTop - 8 - Shell - b0;
                lowest = Math.Min(highest, 3 + Shell + b0);
            }
            ChamberY = (int)Math.Round(Math.Clamp(y, lowest, Math.Max(lowest, highest)));
            double core = v.Active ? 1 : DormantCore;
            LavaA = a0 * core;
            LavaB = b0 * core;
            BodyA = a0 + Shell;
            BodyB = b0 + Shell;

            // the conduit
            ConduitR = Math.Max(1, ConduitRadiusM / mpb);
            if (Lake)
            {
                ConduitLavaTop = LakeFloorY;
                ConduitTop = LakeFloorY;
            }
            else if (v.Active)
            {
                ConduitLavaTop = VentTop - Crust;
                ConduitTop = VentTop;
            }
            else
            {
                ConduitLavaTop = ChamberY - 1;
                ConduitTop = VentTop - 4;
            }
            AshR = v.Active && !Submarine ? Math.Max(6, AshRadiusM / mpb) : 0;
            Reach = Math.Max(Math.Max(BodyA + 1, Crater ? CraterReach + 3 : 0), AshR + 1);
        }

        /// <summary>How far from the vent the crater's edge reaches at its farthest (blocks; 0 without a crater).</summary>
        public double CraterReach => !Crater ? 0 : Lake ? LakeR + (CraterR - LakeR) * (1 + CraterWobble) : CraterR * (1 + CraterWobble);

        /// <summary>The crater edge's share of reaching out (positive) or pulling in at an angle around the vent.</summary>
        double Wobble(double a) => CraterWobble * (0.55 * Math.Sin(2 * a + phase2) + 0.3 * Math.Sin(3 * a + phase3) + 0.15 * Math.Sin(5 * a + phase5));

        /// <summary>The crater's edge at an angle around the vent (blocks from it).</summary>
        double EdgeRadius(double a) => Lake ? LakeR + (CraterR - LakeR) * (1 + Wobble(a)) : CraterR * (1 + Wobble(a));

        /// <summary>Whether a column is in the crater or on its rim (in any crater, active by its nature): no pond or spring
        /// there, the ground is hot.</summary>
        public bool InCrater(int x, int z) => Crater && Dist(x, z) <= CraterReach + 2 && CraterDist(x, z) <= CraterR + 2;

        /// <summary>A column's distance from the vent on the crater's own measure, where its edge is at CraterR all round: the
        /// wall's distance shrunk where the edge reaches out and stretched where it pulls in; the lake keeps its own.</summary>
        double CraterDist(int x, int z)
        {
            double d = Dist(x, z);
            if (d <= LakeR || d == 0) return d;
            double w = 1 + Wobble(Math.Atan2(z - VentZ, x - VentX));
            return Lake ? LakeR + (d - LakeR) / w : d / w;
        }

        /// <summary>How many blocks of fresh ash a column gets (0 outside it): AshMaxBlocks at the vent, thinning with the
        /// square of the distance; a hash of the column dithers the thickness, so the edge is ragged, not a circle.</summary>
        public int AshDepth(int x, int z, double d)
        {
            if (d >= AshR) return 0;
            double f = 1 - d / AshR;
            return Math.Min(AshMaxBlocks, (int)Math.Floor(AshMaxBlocks * f * f + Hash01(x, z)));
        }

        static double Hash01(int x, int z) => StableHash.Unit24(x, z);

        /// <summary>Horizontal distance (blocks) of a column from the vent.</summary>
        public double Dist(int x, int z) => Math.Sqrt((double)(x - VentX) * (x - VentX) + (double)(z - VentZ) * (z - VentZ));

        /// <summary>The crater's surface at a column (int.MaxValue outside it): the lake's floor, a steep wall from just above
        /// the lava up to the rim, or a flat floor and a wall rising to the rim.</summary>
        public int CraterSurface(int x, int z)
        {
            if (!Crater || Dist(x, z) > CraterReach) return int.MaxValue;
            double d = CraterDist(x, z);
            if (d > CraterR) return int.MaxValue;
            if (Lake)
            {
                if (d <= LakeR) return LakeFloorY;
                return FloorY + (int)Math.Floor((RimY - FloorY) * SmoothStep((d - LakeR) / (CraterR - LakeR)));
            }
            double flat = 0.35 * CraterR;
            if (d <= flat) return FloorY;
            return FloorY + (int)Math.Floor((RimY - FloorY) * SmoothStep((d - flat) / (CraterR - flat)));
        }

        /// <summary>A lake column (lava over the floor).</summary>
        public bool InLake(double d) => Lake && d <= LakeR;

        /// <summary>Next to the lake: its rock is made whole from the lake's floor to the lava's top.</summary>
        public bool LakeRim(double d) => Lake && d > LakeR && d <= LakeR + 2;

        /// <summary>The chamber's vertical half-extent (blocks) at a distance from the vent: of its lava, and of the whole body
        /// with the shell (negative where none).</summary>
        public double LavaHalf(double d) => d >= LavaA ? -1 : LavaB * Math.Sqrt(1 - d * d / (LavaA * LavaA));
        public double BodyHalf(double d) => d >= BodyA ? -1 : BodyB * Math.Sqrt(1 - d * d / (BodyA * BodyA));

        /// <summary>The conduit's core (lava or plug), and its basalt sleeve.</summary>
        public bool InConduit(double d) => d * d <= ConduitR * ConduitR + 0.25;
        public bool ConduitSleeve(double d) => !InConduit(d) && d <= ConduitR + 1.3;

        static double SmoothStep(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }
    }
}
