using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>
    /// River exits to the sea, deepened for ships. The terrain grid is 2' (~38 blocks in a 512k world), so an estuary's
    /// way out (the Tagus narrows at Lisbon, ~20 blocks wide) or a delta's shallows came out a block or two deep, too
    /// shallow for a carrack (4.5 deep), and a river's water ended in sea a few blocks deep.
    /// Each river that ends at the sea gets a channel from its end through the natural water (estuary, bay, strait) to
    /// the nearest open water at least as deep, found on the terrain grid preferring deeper water. Along it:
    ///  - the water is dredged to the channel depth (6 blocks for the navigable rivers, a stream's own depth for streams;
    ///    from rules version 2 at least the river's own depth at its end, so the way out is never shallower than the river),
    ///    over the river's width at its end and then widening 30° to each side (capped), so the way in from the sea is a
    ///    broad opening, not a lane to aim for; the sides ease into the natural bed; deeper natural basins stay as they are;
    ///  - low land on the way (bars, spits, beaches up to about CutHeight) is cut, but only over the river's own width; higher
    ///    ground (headlands, cliffs) is never cut. Cut land becomes sea, and land below sea level beside it is raised
    ///    to the water (no water walls).
    /// Saltwater throughout (the sea's); the river's own water stays fresh.
    /// Optionally also the sea-level canals (Suez, Panama; a gameplay option, see KnownCanals): cut through the land at
    /// any height, a towpath at sea level on each side, then a cut slope back up to the natural ground. Rivers crossing a
    /// canal come down to sea level there (RiverNetwork). Read-only after construction (thread-safe).
    /// </summary>
    public sealed class SeaChannels
    {
        /// <summary>Version of the channel rules, stored per world (EarthParams.RiverChannels; 0: none). 2: channels as deep as
        /// the river at its end (at least ShipDepth) and on to open water, past the bars outside an estuary's deep narrows
        /// (version 1 stopped at the first deep grid cell: the Tagus narrows at Lisbon, inside the bar). 3 (tall.13): low land
        /// on GLO-90 (0051) is judged on the built-in heights (LowLand). 4 (water atlas, 6 November 2026, at the author's
        /// request: dredging only in the real water, widening by erosion; tall.14): the way out follows the natural water on a
        /// grid FineCells times finer than the terrain grid, by the coast outlines and islands (RefineCourse), and is deepened
        /// only where there is water. Land is cut only to join the river's own end to the water beside it (at most MaxFineLand
        /// fine cells), never across bars, spits or polders further out (the old cuts in the Rhine–Maas delta, on the Ems,
        /// across Friesland). Near the mouth (ErodeLength river widths along the way) the low banks are eroded so the mouth is
        /// up to ErodeWidth times the river's width, in worlds without outline coasts only (from tall.15, as release/1.0's
        /// 72af35f). Both judge GLO-90 land on the built-in heights as 3 does. 5 (EA-0038, decision 0071): no river exits in
        /// worlds with outline coasts (1:100 and finer, EarthMapSystem.CoastsFor): there the coast outlines and the water
        /// mask draw a mouth's real way out, and the river's bed meets the sea's by channel rules 3 (TerrainModel.ApplyMouth);
        /// a channel dredged for ships cut a flat 7-block wedge 1.3 km out to sea at the Daugava at 1:10. Worlds without
        /// outline coasts (1:200), whose 2' grid can't draw an estuary, keep rules 4's channels; canals are as before.</summary>
        public const int Version = 5;
        /// <summary>Rules 5: no river exits where the world has outline coasts.</summary>
        const int NoExitsWithCoasts = 5;
        /// <summary>First rules that dredge in place (release/1.0's water atlas uses the same number; 0052's unmerged rules
        /// would need another one).</summary>
        const int InPlace = 4;
        /// <summary>Rules 4: fine cells per terrain cell along a channel's way, and the cost of a fine land cell against a
        /// water one (a bar is crossed rather than taking a way many times longer through the water).</summary>
        const int FineCells = 16;
        const double FineLandCost = 20;
        /// <summary>Depth of the channel for navigable rivers (blocks): a carrack draws 4.5.</summary>
        public const int ShipDepth = 6;
        /// <summary>How fast the channel widens on each side out to sea (degrees).</summary>
        public const double FanDegrees = 30;
        /// <summary>Land cells up to this high (m) can be on a channel's way (bars, spits, beaches); its columns are cut up to
        /// twice that (the detail noise lifts parts of a low bar a little higher).</summary>
        public const double CutHeight = 8;
        /// <summary>How far from a river's end a channel is looked for (terrain cells; navigable rivers further: the gulfs of the Ob
        /// and the Yenisei are hundreds of kilometres of shallow water), and how many low land cells it may cross.</summary>
        const int SearchCells = 64, ShipSearchCells = 256, MaxLandCells = 6;
        /// <summary>Rules 4: grid cells of land the way may cross (a bar), and fine cells of land the fine way may cross
        /// (about 3 km at 1:1): a river whose way out needs more (behind the Haringvliet dam, across Friesland's polders) gets
        /// its natural water deepened only.</summary>
        const int MaxLandCells4 = 2, MaxFineLand = 12;
        /// <summary>Rules 4: how far along the way from the river's end (in river half widths) low banks are eroded, and the
        /// mouth's half width there at most (in river half widths). A low bank is at most 2 blocks above the sea, so in worlds
        /// lower than 384 blocks more of a delta's land is low (the Mekong's outline check at 256: 99.4 %; 100 % at 384).</summary>
        const double ErodeLength = 8, ErodeWidth = 2;

        /// <summary>A canal: its code (config and save), name and course (lat, lon), from sea to sea, and the lakes on its way
        /// as overlapping discs (lat, lon, radius in km), all at sea level.</summary>
        public sealed class Canal
        {
            public string Code, Name;
            public (double Lat, double Lon)[] Course;
            public (double Lat, double Lon, double Km)[] Lakes = Array.Empty<(double, double, double)>();
        }

        /// <summary>
        /// The canals a world can have (EarthParams.Canals). Both are cut at sea level for sailing through, with their lakes
        /// at sea level too: Lake Timsah and the Bitter Lakes on the Suez Canal; Gatun Lake (really 26 m up, reached by locks;
        /// a reservoir, so not in the map otherwise) and Miraflores Lake on the Panama Canal.
        /// </summary>
        public static readonly IReadOnlyList<Canal> KnownCanals = new[]
        {
            new Canal
            {
                Code = "suez", Name = "Suez Canal",
                Course = new[] { (31.36, 32.38), (31.265, 32.315), (31.00, 32.315), (30.85, 32.32), (30.70, 32.33), (30.59, 32.28),
                    (30.45, 32.35), (30.33, 32.40), (30.22, 32.49), (30.08, 32.56), (29.95, 32.565), (29.85, 32.56) },
                Lakes = new[] { (30.57, 32.30, 2.0), (30.40, 32.36, 4.5), (30.34, 32.39, 5.0), (30.28, 32.43, 4.0), (30.22, 32.49, 2.5), (30.17, 32.52, 2.5) },
            },
            new Canal
            {
                Code = "panama", Name = "Panama Canal",
                Course = new[] { (9.43, -79.93), (9.38, -79.92), (9.33, -79.915), (9.27, -79.92), (9.20, -79.88), (9.13, -79.79),
                    (9.11, -79.70), (9.05, -79.66), (9.017, -79.613), (8.997, -79.592), (8.95, -79.565), (8.87, -79.52) },
                Lakes = new[] { (9.22, -79.90, 5.0), (9.17, -79.85, 5.0), (9.15, -79.95, 5.0), (9.12, -79.78, 4.0), (9.18, -80.00, 3.5),
                    (9.10, -79.88, 4.0), (9.12, -79.72, 3.0), (9.005, -79.60, 1.0) },
            },
        };

        /// <summary>
        /// 0063 channel rules 1: how far (km) from a canal's course the water mask's water is the canal's (its cut, its new
        /// channels and the sweet-water canal beside it), so not drawn (TerrainModel.NaturalColumn): with no canal option the
        /// land is as before the canals (0020), with one the canal is cut by its own rules. Its lakes' discs count LakeCatch
        /// times their radius (the Great Bitter Lake, Gatun Lake's arms).
        /// </summary>
        public const double CanalZoneKm = 3;

        /// <summary>Each known canal's zone's bounding box (lat, lon), for a cheap test first.</summary>
        static readonly (double MinLat, double MaxLat, double MinLon, double MaxLon)[] canalZoneBoxes = CanalZoneBoxes();

        static (double, double, double, double)[] CanalZoneBoxes()
        {
            var boxes = new (double, double, double, double)[KnownCanals.Count];
            for (int k = 0; k < boxes.Length; k++)
            {
                var canal = KnownCanals[k];
                double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
                void Add(double lat, double lon, double km)
                {
                    double dLat = km / 111.2, dLon = km / (111.2 * Math.Cos(lat * Math.PI / 180));
                    minLat = Math.Min(minLat, lat - dLat); maxLat = Math.Max(maxLat, lat + dLat);
                    minLon = Math.Min(minLon, lon - dLon); maxLon = Math.Max(maxLon, lon + dLon);
                }
                foreach (var p in canal.Course) Add(p.Lat, p.Lon, CanalZoneKm * 1.01);
                foreach (var l in canal.Lakes) Add(l.Lat, l.Lon, l.Km * LakeCatch * 1.01);
                boxes[k] = (minLat, maxLat, minLon, maxLon);
            }
            return boxes;
        }

        /// <summary>Channel rules 1: whether (lat, lon) is within CanalZoneKm of a known canal's course or within LakeCatch radii
        /// of one of its lakes, whichever canals the world has.</summary>
        public static bool InCanalZone(double lat, double lon)
        {
            for (int k = 0; k < canalZoneBoxes.Length; k++)
            {
                var b = canalZoneBoxes[k];
                if (lat < b.MinLat || lat > b.MaxLat || lon < b.MinLon || lon > b.MaxLon) continue;
                var canal = KnownCanals[k];
                double kx = 111.2 * Math.Cos(lat * Math.PI / 180), kz = 111.2;
                double px = lon * kx, pz = lat * kz;
                for (int i = 1; i < canal.Course.Length; i++)
                {
                    var a = canal.Course[i - 1];
                    var e = canal.Course[i];
                    if (SegDistance(a.Lon * kx, a.Lat * kz, e.Lon * kx, e.Lat * kz, px, pz, out _) <= CanalZoneKm) return true;
                }
                foreach (var l in canal.Lakes)
                {
                    double dx = (lon - l.Lon) * kx, dz = (lat - l.Lat) * kz, r = l.Km * LakeCatch;
                    if (dx * dx + dz * dz <= r * r) return true;
                }
            }
            return false;
        }
        /// <summary>Canal depth (blocks), half width at the standard 256k world (scaled with the world), towpath and cut slope
        /// (blocks out per block up), and how far the cut may reach out from the towpath (blocks).</summary>
        public const int CanalDepth = 7, LakeDepth = ShipDepth;
        public const double CanalHalfWidth = 16, TowPath = 2, CanalSlope = 1.5, CanalCutReach = 120;

        /// <summary>The canals with these codes (unknown codes are left out).</summary>
        public static List<Canal> CanalsFor(IEnumerable<string> codes)
        {
            var list = new List<Canal>();
            if (codes == null) return list;
            foreach (string code in codes)
                foreach (var k in KnownCanals)
                    if (string.Equals(k.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase) && !list.Contains(k)) list.Add(k);
            return list;
        }

        /// <summary>The courses (world coordinates) and half width of the given canals: for the river network, which brings
        /// rivers crossing them down to sea level.</summary>
        public static List<(double[] X, double[] Z, double HalfWidth, (double X, double Z, double R)[] Lakes)> CanalCourses(EarthGeometry geo, IEnumerable<string> codes)
        {
            var list = new List<(double[], double[], double, (double, double, double)[])>();
            double scale = geo.RingLength > 0 ? geo.RingLength / 204800.0 : 1;
            double blocksPerKm = geo.BlocksPerDegreeZ / 111.2;
            foreach (var canal in CanalsFor(codes))
            {
                var xs = new double[canal.Course.Length];
                var zs = new double[canal.Course.Length];
                for (int i = 0; i < xs.Length; i++) geo.FromGeo(canal.Course[i].Lat, canal.Course[i].Lon, out xs[i], out zs[i]);
                var lakes = new (double, double, double)[canal.Lakes.Length];
                for (int i = 0; i < lakes.Length; i++)
                {
                    geo.FromGeo(canal.Lakes[i].Lat, canal.Lakes[i].Lon, out double lx, out double lz);
                    lakes[i] = (lx, lz, canal.Lakes[i].Km * blocksPerKm);
                }
                list.Add((xs, zs, CanalHalfWidth * scale, lakes));
            }
            return list;
        }

        /// <summary>Does a piece of river (x1, z1)-(x2, z2) come within reach of one of these canals (its water and towpaths)?</summary>
        public static Func<double, double, double, double, bool> CanalCrossing(List<(double[] X, double[] Z, double HalfWidth, (double X, double Z, double R)[] Lakes)> courses)
        {
            if (courses == null || courses.Count == 0) return null;
            return (x1, z1, x2, z2) =>
            {
                foreach (var (cx, cz, hw, lakes) in courses)
                {
                    double reach = hw + TowPath + 8;
                    // quick test on the bounding boxes (lakes included)
                    double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    for (int i = 0; i < cx.Length; i++) { minX = Math.Min(minX, cx[i]); maxX = Math.Max(maxX, cx[i]); minZ = Math.Min(minZ, cz[i]); maxZ = Math.Max(maxZ, cz[i]); }
                    foreach (var (lx, lz, lr) in lakes) { minX = Math.Min(minX, lx - lr); maxX = Math.Max(maxX, lx + lr); minZ = Math.Min(minZ, lz - lr); maxZ = Math.Max(maxZ, lz + lr); }
                    if (Math.Max(x1, x2) < minX - reach || Math.Min(x1, x2) > maxX + reach || Math.Max(z1, z2) < minZ - reach || Math.Min(z1, z2) > maxZ + reach) continue;
                    double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
                    int n = Math.Max(1, (int)Math.Ceiling(len / 4));
                    for (int u = 0; u <= n; u++)
                    {
                        double x = x1 + (x2 - x1) * u / n, z = z1 + (z2 - z1) * u / n;
                        for (int i = 1; i < cx.Length; i++)
                            if (SegDistance(cx[i - 1], cz[i - 1], cx[i], cz[i], x, z, out _) <= reach) return true;
                        foreach (var (lx, lz, lr) in lakes)
                            if (Math.Sqrt((x - lx) * (x - lx) + (z - lz) * (z - lz)) <= lr * (1 + ShoreNoise) + TowPath + 8) return true;
                    }
                }
                return false;
            };
        }

        static double SegDistance(double x1, double z1, double x2, double z2, double x, double z, out double t)
        {
            double dx = x2 - x1, dz = z2 - z1, l2 = dx * dx + dz * dz;
            t = l2 > 0 ? Math.Clamp(((x - x1) * dx + (z - z1) * dz) / l2, 0, 1) : 0;
            double px = x1 + t * dx - x, pz = z1 + t * dz - z;
            return Math.Sqrt(px * px + pz * pz);
        }

        /// <summary>How much a canal lake's shore wanders in and out (a share of its radius).</summary>
        const double ShoreNoise = 0.2;
        static readonly Simplex2 shore = new Simplex2(4242);

        public sealed class Channel
        {
            /// <summary>A canal (Name), not a river's exit; a canal's lake (a disc: X, Z its centre, HalfWidth its radius).</summary>
            public bool Canal, Lake;
            /// <summary>A navigable river's channel (sized for ships).</summary>
            public bool Ship;
            public string Name;
            public int Reach;
            public double Q;
            /// <summary>Depth (blocks below the water surface); half width at the river's end (where land may be cut) and at most.</summary>
            public int Depth;
            public double HalfWidth, MaxHalfWidth;
            /// <summary>The course in world coordinates, from the river's end out to open water.</summary>
            public double[] X, Z;
            public double Length;
            /// <summary>For each piece of the course, whether its core may cut land (rules 4: where the way crosses a bar; before: all).</summary>
            public bool[] LandPieces;
            /// <summary>Rules 4: the course is the fine way through the natural water (else the grid route's cells).</summary>
            public bool Fine;
        }

        struct Seg
        {
            public double X1, Z1, X2, Z2, S0, Len;
            public int Channel;
            /// <summary>The piece crosses land (rules 4: a bar, the river's end; before: every piece), where the core may cut it.</summary>
            public bool Land;
        }

        /// <summary>The world's channel rules (Build's version).</summary>
        int rules = Version;

        readonly List<Channel> channels = new List<Channel>();
        Seg[] segs = Array.Empty<Seg>();
        readonly Dictionary<long, int[]> grid = new Dictionary<long, int[]>();
        const double Cell = 128;
        readonly EarthGeometry geo;
        readonly double tanFan = Math.Tan(FanDegrees * Math.PI / 180);

        public IReadOnlyList<Channel> Channels => channels;
        /// <summary>River ends at the sea already in deep enough water, and ones with no way out found.</summary>
        public int AlreadyDeep, NoWay;
        /// <summary>The river ends with no way found (reach index): enclosed shallow seas such as the Sea of Azov.</summary>
        public readonly List<int> NoWayReaches = new List<int>();

        SeaChannels(EarthGeometry geo) { this.geo = geo; }

        /// <summary>Easing of the channel's sides into the natural bed (blocks).</summary>
        static double Shoulder(int depth) => 4 + 2 * depth;

        /// <summary>The river exits (if riverExits) and the given canals of a world's terrain model, by the world's rules version.
        /// With a <paramref name="cache"/>, the exits' ways out (the slow part) come from it when it has them for these exits.</summary>
        public static SeaChannels Build(TerrainModel model, bool riverExits = true, IEnumerable<string> canals = null, int version = Version, ModelCache cache = null)
        {
            var sc = new SeaChannels(model.Geometry) { rules = version };
            var rivers = model.Rivers;
            var reaches = rivers?.Reaches;
            var data = model.Data;
            var geo = model.Geometry;
            int res = data.TerrainRes;
            var segs = new List<Seg>();
            if (rivers == null || reaches == null) riverExits = false;
            if (version >= NoExitsWithCoasts && model.Coasts != null) riverExits = false;
            // the rivers that end at the sea, and each one's way out to deep water (FindWays, or the model cache), then the
            // channels in the rivers' order
            var exits = new List<Exit>();
            for (int r = 0; riverExits && r < reaches.Count; r++)
            {
                var re = reaches[r];
                if (!rivers.Kept(r) || rivers.Kept(re.Next)) continue;
                if (!rivers.ReachLine(r, out var lx, out var lz) || lx.Length < 2) continue;
                double ex = lx[lx.Length - 1], ez = lz[lz.Length - 1];
                var g = geo.ToGeo(ex, ez);
                if (g.InGutter) continue;
                int row = (int)Math.Floor((90 - g.Lat) * res), col = (int)Math.Floor((g.Lon + 180) * res);
                // only rivers that end at the sea (not in a lake or an inland sink)
                bool sea = false;
                for (int dr = -2; dr <= 2 && !sea; dr++)
                    for (int dc = -2; dc <= 2 && !sea; dc++)
                        if ((data.GetCell(row + dr, col + dc).Flags & EarthData.FlagOcean) != 0) sea = true;
                if (!sea) continue;

                double q = re.Q;
                bool ships = q >= rivers.MinQ;
                // (the river's bed is its depth rounded to blocks at its centre)
                int depth = version >= 2 ? Math.Max(ships ? ShipDepth : 2, (int)Math.Round(rivers.Depth(q)))
                    : ships ? ShipDepth : Math.Max(2, (int)Math.Round(rivers.Depth(q)));
                double hw = rivers.HalfWidth(q);
                // (EA-0159) the ships' least width shrinks with the world's river width floor (RiverNetwork.ShipFloorFactor)
                if (ships) hw = Math.Max(hw, 12 * rivers.WidthFloorScale * rivers.ShipFloorFactor);
                int radius = ships ? ShipSearchCells : SearchCells;
                exits.Add(new Exit(r, ex, ez, q, ships, depth, hw, row, col, radius));
            }
            var ways = ModelCache.Use(cache, CachePart, () => FindWays(model, exits, version), (w, v) => v.Write(w), r => Ways.Read(r, exits));
            var paths = ways.Paths;
            var fineWays = ways.Fine;
            for (int e = 0; e < exits.Count; e++)
            {
                var (r, ex, ez, q, ships, depth, hw, _, _, _) = exits[e];
                var path = paths[e];
                if (path == null) { sc.NoWay++; sc.NoWayReaches.Add(r); continue; }
                if (path.Count <= 1) { sc.AlreadyDeep++; continue; }

                // the course: the river's end, then the centres of the cells on the way (the start cell's centre may lie inland);
                // rules 4: the way through the natural water on the fine grid, each piece marked where it crosses land
                bool fine = fineWays[e].X != null;
                var xs = fine ? fineWays[e].X : new List<double> { ex };
                var zs = fine ? fineWays[e].Z : new List<double> { ez };
                var land = fine ? fineWays[e].Land : new List<bool>();
                if (!fine)
                {
                    for (int i = 1; i < path.Count; i++)
                    {
                        int pr = path[i].r, pc = path[i].c;
                        geo.FromGeo(90 - (pr + 0.5) / res, (pc + 0.5) / res - 180, out double px, out double pz);
                        if (geo.AcrossSeam(xs[xs.Count - 1], px)) break;
                        xs.Add(px); zs.Add(pz);
                        // (rules 4 without a fine way: the natural water on the way is deepened only, and the river's end
                        // joined to it where that is no further than a bar)
                        land.Add(i == 1 && Math.Sqrt((px - ex) * (px - ex) + (pz - ez) * (pz - ez)) <= MaxFineLand * geo.BlocksPerDegreeZ / (res * FineCells));
                    }
                }
                if (xs.Count < 2) continue;
                var ch = new Channel
                {
                    Reach = r, Q = q, Depth = depth, HalfWidth = hw, Ship = ships, Fine = fine,
                    MaxHalfWidth = Math.Max(3 * hw, hw + 128 * rivers.SizeScale),
                    X = xs.ToArray(), Z = zs.ToArray(),
                };
                int index = sc.channels.Count;
                sc.channels.Add(ch);
                double s = 0;
                for (int i = 1; i < xs.Count; i++)
                {
                    double len = Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (zs[i] - zs[i - 1]) * (zs[i] - zs[i - 1]));
                    segs.Add(new Seg { X1 = xs[i - 1], Z1 = zs[i - 1], X2 = xs[i], Z2 = zs[i], S0 = s, Len = len, Channel = index,
                        Land = version < InPlace || land[i - 1] });
                    s += len;
                }
                ch.Length = s;
                ch.LandPieces = new bool[xs.Count - 1];
                for (int i = 0; i < xs.Count - 1; i++) ch.LandPieces[i] = version < InPlace || land[i];
            }
            var canalList = CanalsFor(canals);
            var courses = CanalCourses(geo, canals);
            for (int k = 0; k < courses.Count; k++)
            {
                var (cx, cz, hw, lakes) = courses[k];
                foreach (var (lx, lz, lr) in lakes)
                {
                    sc.channels.Add(new Channel { Canal = true, Lake = true, Name = canalList[k].Name, Reach = -1, Depth = LakeDepth, HalfWidth = lr, MaxHalfWidth = lr,
                        X = new[] { lx }, Z = new[] { lz } });
                    segs.Add(new Seg { X1 = lx, Z1 = lz, X2 = lx, Z2 = lz, Channel = sc.channels.Count - 1 });
                }
                var ch = new Channel { Canal = true, Name = canalList[k].Name, Reach = -1, Depth = CanalDepth, HalfWidth = hw, MaxHalfWidth = hw, X = cx, Z = cz };
                int index = sc.channels.Count;
                sc.channels.Add(ch);
                double s = 0;
                for (int i = 1; i < cx.Length; i++)
                {
                    double len = Math.Sqrt((cx[i] - cx[i - 1]) * (cx[i] - cx[i - 1]) + (cz[i] - cz[i - 1]) * (cz[i] - cz[i - 1]));
                    segs.Add(new Seg { X1 = cx[i - 1], Z1 = cz[i - 1], X2 = cx[i], Z2 = cz[i], S0 = s, Len = len, Channel = index });
                    s += len;
                }
                ch.Length = s;
            }
            sc.segs = segs.ToArray();
            var rects = new (int X0, int X1, int Z0, int Z1)[sc.segs.Length];
            for (int i = 0; i < sc.segs.Length; i++)
            {
                ref var sg = ref sc.segs[i];
                var ch = sc.channels[sg.Channel];
                // an exit's segment reaches only as far as its own widest point (the fan's width at the segment's far end, at most
                // MaxHalfWidth) and shoulder: Apply skips it beyond hw(t) + shoulder + 2, and what it gathers (the deepest, the
                // nearest, any) doesn't depend on the order or on segments it skips. Before, every segment was registered over
                // MaxHalfWidth (about 2,500 blocks at 1:10, at least 1,600 cells per segment), the most memory of a 1:10 start.
                double reach = ch.Canal ? Math.Max(ch.HalfWidth * (1 + ShoreNoise) + TowPath + CanalCutReach, ch.Lake ? LakeCatch * ch.HalfWidth : 0) + 2
                    : Math.Min(ch.HalfWidth + (sg.S0 + sg.Len) * sc.tanFan, ch.MaxHalfWidth) + Shoulder(ch.Depth) + 3;
                rects[i] = ((int)Math.Floor((Math.Min(sg.X1, sg.X2) - reach) / Cell), (int)Math.Floor((Math.Max(sg.X1, sg.X2) + reach) / Cell),
                    (int)Math.Floor((Math.Min(sg.Z1, sg.Z2) - reach) / Cell), (int)Math.Floor((Math.Max(sg.Z1, sg.Z2) + reach) / Cell));
            }
            // each cell's segments in segment order, on several cores (at 1:10 about 11 000 segments in 2.3 million cells: 2
            // s on one core before 2.0.0-rc.4)
            CellIndex.Build(rects.Length, (int i, out int x0, out int x1, out int z0, out int z1) =>
            {
                var r = rects[i];
                x0 = r.X0; x1 = r.X1; z0 = r.Z0; z1 = r.Z1;
            }, sc.grid);
            return sc;
        }

        /// <summary>The model cache's part holding the exits' ways (its layout: <see cref="Ways"/>); a new layout gets a new name.</summary>
        const string CachePart = "sea-channel-ways-1";

        /// <summary>A river that ends at the sea: its reach, its end, flow, the channel it gets (ship channel, depth, half width)
        /// and where its search starts (terrain cell, reach in cells).</summary>
        readonly record struct Exit(int R, double Ex, double Ez, double Q, bool Ships, int Depth, double Hw, int Row, int Col, int Radius);

        /// <summary>
        /// Each exit's way out: the grid route (null: none within reach; one cell: deep water already) and, under rules 4,
        /// the fine way through the natural water (X null: none was found, the grid route is used). Kept in the model cache with
        /// each exit's search start, so ways made for other exits are never used.
        /// </summary>
        sealed class Ways
        {
            public (int R, int Row, int Col, int Depth, int Radius, bool Ships)[] Starts;
            public List<(int r, int c)>[] Paths;
            public (List<double> X, List<double> Z, List<bool> Land)[] Fine;

            static (int, int, int, int, int, bool) StartOf(in Exit x) => (x.R, x.Row, x.Col, x.Depth, x.Radius, x.Ships);

            public static Ways For(List<Exit> exits) => new Ways
            {
                Starts = exits.Select(x => StartOf(x)).ToArray(),
                Paths = new List<(int r, int c)>[exits.Count],
                Fine = new (List<double> X, List<double> Z, List<bool> Land)[exits.Count],
            };

            public void Write(System.IO.BinaryWriter w)
            {
                w.Write(Starts.Length);
                for (int e = 0; e < Starts.Length; e++)
                {
                    var s = Starts[e];
                    w.Write(s.R); w.Write(s.Row); w.Write(s.Col); w.Write(s.Depth); w.Write(s.Radius); w.Write(s.Ships);
                    var p = Paths[e];
                    w.Write(p == null ? -1 : p.Count);
                    if (p != null) foreach (var (r, c) in p) { w.Write(r); w.Write(c); }
                    var f = Fine[e];
                    w.Write(f.X == null ? -1 : f.X.Count);
                    if (f.X == null) continue;
                    foreach (double v in f.X) w.Write(v);
                    w.Write(f.Z.Count);
                    foreach (double v in f.Z) w.Write(v);
                    w.Write(f.Land.Count);
                    foreach (bool v in f.Land) w.Write(v);
                }
            }

            /// <summary>The ways kept for exactly these exits, else null.</summary>
            public static Ways Read(System.IO.BinaryReader r, List<Exit> exits)
            {
                if (r.ReadInt32() != exits.Count) return null;
                var ways = For(exits);
                for (int e = 0; e < exits.Count; e++)
                {
                    var s = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean());
                    if (s != ways.Starts[e]) return null;
                    int n = r.ReadInt32();
                    if (n >= 0)
                    {
                        var p = ways.Paths[e] = new List<(int r, int c)>(n);
                        for (int i = 0; i < n; i++) p.Add((r.ReadInt32(), r.ReadInt32()));
                    }
                    int nx = r.ReadInt32();
                    if (nx < 0) continue;
                    var fx = new List<double>(nx);
                    for (int i = 0; i < nx; i++) fx.Add(r.ReadDouble());
                    int nz = r.ReadInt32();
                    var fz = new List<double>(nz);
                    for (int i = 0; i < nz; i++) fz.Add(r.ReadDouble());
                    int nl = r.ReadInt32();
                    var fl = new List<bool>(nl);
                    for (int i = 0; i < nl; i++) fl.Add(r.ReadBoolean());
                    ways.Fine[e] = (fx, fz, fl);
                }
                return ways;
            }
        }

        /// <summary>
        /// Each exit's way out to deep water. The searches run on several cores (ParallelWork: up to 8 long-lived workers, 4
        /// before 2.0.0-rc.4), each worker with its own buffers made once (a ship channel's search covers 513² cells with
        /// their land counts: about 20 MB a worker under rules 4, 40 under rules 2 and 3); each exit's way is its own search,
        /// so the ways are the same with any number of workers.
        /// </summary>
        static Ways FindWays(TerrainModel model, List<Exit> exits, int version)
        {
            var geo = model.Geometry;
            // rules 4: the grid route may cross fewer land cells, and the course is then refined on the fine grid (also in
            // the workers: each refinement is its own search)
            int maxLand = version >= InPlace ? MaxLandCells4 : MaxLandCells;
            var ways = Ways.For(exits);
            var paths = ways.Paths;
            var fineWays = ways.Fine;
            // (no exits: rivers off, or none reaching the sea; the canals are still made)
            ParallelWork.For(exits.Count, () => (Search: new SearchBuffers(), Fine: new FineSearch()), (e, local) =>
            {
                var x = exits[e];
                // version 2: out to open water; where there is none within reach (an enclosed shallow sea), the first deep cell
                var p = paths[e] = version >= 2 ? Route(model, local.Search, x.Row, x.Col, x.Depth, x.Radius, true, true, maxLand, orFirstDeep: true)
                    : Route(model, local.Search, x.Row, x.Col, x.Depth, x.Radius, false, false, maxLand);
                if (version < InPlace || p == null || p.Count <= 1) return;
                var g = geo.ToGeo(x.Ex, x.Ez);
                var fx = new List<double> { x.Ex };
                var fz = new List<double> { x.Ez };
                var fl = new List<bool>();
                if (RefineCourse(model, p, g.Lat, g.Lon, fx, fz, fl, local.Fine)) fineWays[e] = (fx, fz, fl);
            });
            return ways;
        }

        /// <summary>Water depth (blocks) the terrain gives a sea cell's centre, 0 for land.</summary>
        static int CellDepth(TerrainModel model, in EarthData.Cell cell)
        {
            if ((cell.Flags & EarthData.FlagOcean) == 0) return 0;
            int sea = model.SeaLevel;
            int top = Math.Min((int)Math.Floor(model.Vertical.Y(Math.Min(cell.Elev, -1))), sea - 1 - model.MinSeaDepth);
            return sea - 1 - top;
        }

        /// <summary>
        /// Cheapest way on the terrain grid from a river's end cell to a sea cell deeper than the channel (with open, also all
        /// its eight neighbours: open water, not the deep narrows of an estuary with a bar outside): through sea cells
        /// (shallow ones cost more, so it keeps to the deep water), across low land (bars, spits) at a high cost, never over
        /// lakes, ice or higher land. Null if there is none within reach; just the start cell if it is deep already.
        /// </summary>
        /// <param name="layered">Channel rules v2: the search state is (cell, land cells crossed so far), so a cheap arrival
        /// that used up the land budget can't hide a longer one that can still cross the last bar. v1 (older worlds) keeps
        /// one state per cell, so their channels stay as they were.</param>
        /// <param name="orFirstDeep">With open (channel rules 2 and later): where no open water is within reach (an enclosed
        /// shallow sea), the way to the first deep cell instead (just the start cell if that is deep), as a second search
        /// without open would find it. That search would run exactly as this one (the same states, costs and queue in the
        /// same order) up to the first deep cell this one takes from the queue, where it stops; that cell's way is final
        /// once taken from the queue (costs are positive), so it is kept from this search instead of searching again (audit
        /// C F9: where no cell is deep either, as in the Sea of Azov, that was a second search over all of a ship channel's
        /// 513² cells and land counts).</param>
        static List<(int r, int c)> Route(TerrainModel model, SearchBuffers search, int row0, int col0, int depth, int radius, bool open, bool layered,
            int maxLand = MaxLandCells, bool orFirstDeep = false)
        {
            var data = model.Data;
            int W = data.TerrainW;
            int side = 2 * radius + 1;
            var cells = search.Cells(side * side);
            ref readonly EarthData.Cell At(int ar, int ac)
            {
                int i = (ar + radius) * side + ac + radius;
                if (cells.Stamp[i] != cells.Search)
                {
                    var c = data.GetCell(row0 + ar, ((col0 + ac) % W + W) % W);
                    cells.Cell[i] = c; cells.Depth[i] = CellDepth(model, c); cells.Stamp[i] = cells.Search;
                }
                return ref cells.Cell[i];
            }
            bool Deep(int row, int col)
            {
                int ar = row - row0, ac = col - col0;
                if (Math.Abs(ar) <= radius && Math.Abs(ac) <= radius && row >= 0 && row < data.TerrainH) { At(ar, ac); return cells.Depth[(ar + radius) * side + ac + radius] > depth; }
                return CellDepth(model, data.GetCell(row, col)) > depth;
            }
            bool Goal(int row, int col)
            {
                if (!Deep(row, col)) return false;
                if (!open) return true;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                        if ((dr != 0 || dc != 0) && !Deep(row + dr, col + dc)) return false;
                return true;
            }
            if (Goal(row0, col0)) return new List<(int, int)> { (row0, col0) };
            bool startDeep = orFirstDeep && Deep(row0, col0);
            int L = layered ? maxLand + 1 : 1;
            // (the buffers reused across searches start as fresh arrays would: distances infinite, land counts 0)
            search.Begin(side * side * L);
            var dist = search.Dist;
            var prev = search.Prev;
            var land = search.Land;
            int Idx(int dr, int dc) => (dr + radius) * side + dc + radius;
            int State(int cell, int used) => cell * L + (layered ? used : 0);
            var pq = search.Queue;
            int s0 = State(Idx(0, 0), 0);
            search.SetDist(s0, 0); prev[s0] = -1;
            pq.Enqueue(s0, 0);
            int goal = -1, firstDeep = -1;
            while (pq.TryDequeue(out int k, out double dk))
            {
                if (dk > dist[k]) continue;
                int cell = k / L;
                int dr = cell / side - radius, dc = cell % side - radius;
                if (k != s0)
                {
                    if (Goal(row0 + dr, col0 + dc)) { goal = k; break; }
                    if (orFirstDeep && firstDeep < 0 && Deep(row0 + dr, col0 + dc)) firstDeep = k;
                }
                for (int nr = -1; nr <= 1; nr++)
                {
                    for (int nc = -1; nc <= 1; nc++)
                    {
                        if (nr == 0 && nc == 0) continue;
                        int ar = dr + nr, ac = dc + nc;
                        if (Math.Abs(ar) > radius || Math.Abs(ac) > radius) continue;
                        int row = row0 + ar;
                        if (row < 0 || row >= data.TerrainH) continue;
                        ref readonly var n = ref At(ar, ac);
                        double step = nr != 0 && nc != 0 ? Math.Sqrt(2) : 1;
                        byte onLand = land[k];
                        double cost;
                        if ((n.Flags & EarthData.FlagOcean) != 0)
                        {
                            int d = cells.Depth[(ar + radius) * side + ac + radius];
                            cost = step * (1 + 3.0 * Math.Max(0, depth + 1 - d) / (depth + 1));
                        }
                        else if ((n.Flags & (EarthData.FlagLake | EarthData.FlagIce)) == 0 && n.Elev <= CutHeight)
                        {
                            if (onLand >= maxLand) continue;
                            onLand++;
                            cost = step * 8;
                        }
                        else continue;
                        int m = State(Idx(ar, ac), onLand);
                        double nd = dk + cost;
                        if (nd >= dist[m]) continue;
                        search.SetDist(m, nd); prev[m] = k; land[m] = onLand;
                        pq.Enqueue(m, nd);
                    }
                }
            }
            if (goal < 0 && orFirstDeep)
            {
                if (startDeep) return new List<(int, int)> { (row0, col0) };
                goal = firstDeep;
            }
            if (goal < 0) return null;
            var path = new List<(int r, int c)>();
            for (int k = goal; k >= 0; k = prev[k]) path.Add((row0 + k / L / side - radius, col0 + k / L % side - radius));
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Rules 4: the way from a river's end (lat0, lon0) out to the grid route's last (open water) cell on a grid FineCells
        /// times finer, within the grid route's cells and their neighbours: through water by the coast outlines and islands
        /// (the grid decides where they don't), keeping to the deeper grid cells like Route; across low land (as Route allows)
        /// at FineLandCost, never over lakes, ice or higher land. Appends the course (world coordinates, simplified) to xs/zs
        /// and for each piece whether it crosses land. False if there is no such way (the grid route is used then).
        /// </summary>
        /// <summary>
        /// A worker's state for RefineCourse, kept from one river exit to the next (before: a hash set, three dictionaries and a
        /// queue per exit, often on the large object heap, about a thousand times per start). The corridor's terrain cells get
        /// slots in the order they are added; a fine cell's entries are at slot * FineCells² + its place in the terrain cell and
        /// count only when stamped in this search, so nothing is cleared between searches. The searches run exactly as with
        /// the dictionaries: the same states, costs and queue operations in the same order.
        /// </summary>
        sealed class FineSearch
        {
            public const byte Unknown = 255;
            public readonly Dictionary<long, int> Slots = new Dictionary<long, int>();
            public long[] SlotKey = new long[256];
            public int[] Depth = new int[256], DepthStamp = new int[256];
            public int[] Stamp = Array.Empty<int>(), Prev = Array.Empty<int>();
            public byte[] Kind = Array.Empty<byte>();
            public double[] Dist = Array.Empty<double>();
            public readonly PriorityQueue<int, double> Queue = new PriorityQueue<int, double>();
            public int Search;

            /// <summary>A new search: no corridor, an empty queue, every fine cell's entries stale.</summary>
            public void Begin()
            {
                Slots.Clear();
                Queue.Clear();
                Search++;
            }

            /// <summary>Adds a terrain cell (its key) to the corridor, once.</summary>
            public void AddCell(long key)
            {
                if (Slots.ContainsKey(key)) return;
                int slot = Slots.Count;
                Slots[key] = slot;
                if (slot == SlotKey.Length)
                {
                    Array.Resize(ref SlotKey, slot * 2);
                    Array.Resize(ref Depth, slot * 2);
                    Array.Resize(ref DepthStamp, slot * 2);
                }
                SlotKey[slot] = key;
                DepthStamp[slot] = 0;
            }

            /// <summary>Room for the corridor's fine cells (f per terrain cell side), once it is complete.</summary>
            public void Size(int f)
            {
                int n = Slots.Count * f * f;
                if (Stamp.Length >= n) return;
                n = Math.Max(n, Stamp.Length * 2);
                Stamp = new int[n];
                Prev = new int[n];
                Kind = new byte[n];
                Dist = new double[n];
            }

            /// <summary>A fine cell's index, its entries made fresh (kind unknown, distance infinite) if this search hasn't yet
            /// touched it.</summary>
            public int Touch(int i)
            {
                if (Stamp[i] != Search)
                {
                    Stamp[i] = Search;
                    Kind[i] = Unknown;
                    Dist[i] = double.PositiveInfinity;
                }
                return i;
            }
        }

        static bool RefineCourse(TerrainModel model, List<(int r, int c)> path, double lat0, double lon0, List<double> xs, List<double> zs, List<bool> landOut,
            FineSearch fs)
        {
            var data = model.Data;
            var geo = model.Geometry;
            int res = data.TerrainRes, W = data.TerrainW, F = FineCells, WF = W * F;
            double fineBlocks = geo.BlocksPerDegreeZ / (res * F);
            long Key(int r, int c) => ((long)r << 32) | (uint)(((c % W) + W) % W);
            // the corridor's terrain cells, each with a slot: its fine cells are slot * F² on
            fs.Begin();
            foreach (var (r, c) in path)
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                        if (r + dr >= 0 && r + dr < data.TerrainH) fs.AddCell(Key(r + dr, c + dc));
            var (gr, gc) = path[path.Count - 1];
            int fr0 = (int)Math.Floor((90 - lat0) * res * F), fc0 = (((int)Math.Floor((Geo.Wrap180(lon0) + 180) * res * F)) % WF + WF) % WF;
            fs.AddCell(Key(fr0 / F, fc0 / F));
            int goalSlot = fs.Slots.TryGetValue(Key(gr, gc), out int gs) ? gs : -1;
            fs.Size(F);
            // a fine cell (row, col on the whole fine grid; col wrapped) whose terrain cell is in the corridor: its index, else -1
            int Index(int fr, int fc)
            {
                if (fr < 0) return -1;
                int r = fr / F, c = fc / F;
                if (!fs.Slots.TryGetValue(Key(r, c), out int slot)) return -1;
                return fs.Touch((slot * F + fr - r * F) * F + fc - c * F);
            }
            // a fine cell (its index): 0 blocked (outside the corridor or the grid too), 1 water, 2 low land
            byte Kind(int i, int fr, int fc)
            {
                if (i < 0) return 0;
                byte b = fs.Kind[i];
                if (b != FineSearch.Unknown) return b;
                b = 0;
                int r = fr / F;
                if (r < data.TerrainH)
                {
                    var cell = data.GetCell(r, fc / F);
                    if ((cell.Flags & EarthData.FlagOcean) != 0 || (cell.Flags & (EarthData.FlagLake | EarthData.FlagIce)) == 0)
                    {
                        double lat = 90 - (fr + 0.5) / (res * F), lon = (fc + 0.5) / (res * F) - 180;
                        // (water within half a fine cell of the centre counts: channels narrower than a fine cell stay open)
                        bool water = model.CoastSideAt(lat, lon, out double side) ? side < 0.5 * fineBlocks : (cell.Flags & EarthData.FlagOcean) != 0;
                        if (water && model.Islands != null && model.Islands.IsLand(lat, lon)) water = false;
                        b = water ? (byte)1 : cell.Elev <= CutHeight ? (byte)2 : (byte)0;
                    }
                }
                return fs.Kind[i] = b;
            }
            int Depth(int i, int fr, int fc)
            {
                int slot = i / (F * F);
                if (fs.DepthStamp[slot] != fs.Search) { fs.DepthStamp[slot] = fs.Search; fs.Depth[slot] = CellDepth(model, data.GetCell(fr / F, fc / F)); }
                return fs.Depth[slot];
            }
            long FineKey(int i)
            {
                long key = fs.SlotKey[i / (F * F)];
                int fr = (int)(key >> 32) * F + i % (F * F) / F, fc = (int)(uint)key * F + i % F;
                return ((long)fr << 32) | (uint)fc;
            }
            int depthNeed = Math.Max(1, CellDepth(model, data.GetCell(gr, ((gc % W) + W) % W)) - 1);
            var dist = fs.Dist;
            var prev = fs.Prev;
            var pq = fs.Queue;
            int S = Index(fr0, fc0);
            dist[S] = 0; prev[S] = -1;
            pq.Enqueue(S, 0);
            int goal = -1;
            int visited = 0;
            while (pq.TryDequeue(out int k, out double dk))
            {
                if (dk > dist[k]) continue;
                long key = FineKey(k);
                int fr = (int)(key >> 32), fc = (int)(uint)key;
                if (k != S && Kind(k, fr, fc) == 1 && k / (F * F) == goalSlot) { goal = k; break; }
                if (++visited > 2000000) break;
                for (int nr = -1; nr <= 1; nr++)
                    for (int nc = -1; nc <= 1; nc++)
                    {
                        if (nr == 0 && nc == 0) continue;
                        int ar = fr + nr, ac = ((fc + nc) % WF + WF) % WF;
                        int m = Index(ar, ac);
                        byte b = Kind(m, ar, ac);
                        if (b == 0) continue;
                        double step = nr != 0 && nc != 0 ? Math.Sqrt(2) : 1;
                        double cost = b == 1 ? step * (1 + 3.0 * Math.Max(0, depthNeed + 1 - Depth(m, ar, ac)) / (depthNeed + 1)) : step * FineLandCost;
                        double nd = dk + cost;
                        if (nd >= dist[m]) continue;
                        dist[m] = nd; prev[m] = k;
                        pq.Enqueue(m, nd);
                    }
            }
            if (goal < 0) return false;
            var way = new List<long>();
            for (int k = goal; k != -1; k = prev[k]) way.Add(FineKey(k));
            way.Reverse();
            // (the way's cells are in the corridor: their kinds are known)
            byte KindOf(long key) => Kind(Index((int)(key >> 32), (int)(uint)key), (int)(key >> 32), (int)(uint)key);
            int landCells = 0;
            while (landCells < way.Count && KindOf(way[landCells]) != 1) landCells++;
            if (landCells > MaxFineLand) return false;
            // the river's end, then the fine cells' centres (world coordinates), and whether each is land
            int n = way.Count;
            var px = new double[n]; var pz = new double[n]; var pl = new bool[n];
            px[0] = xs[0]; pz[0] = zs[0]; pl[0] = landCells > 0;
            for (int i = 1; i < n; i++)
            {
                int fr = (int)(way[i] >> 32), fc = (int)(uint)way[i];
                geo.FromGeo(90 - (fr + 0.5) / (res * F), (fc + 0.5) / (res * F) - 180, out px[i], out pz[i]);
                pl[i] = i < landCells && KindOf(way[i]) != 1;
            }
            // simplified to within a quarter of a fine cell, keeping where the way goes from water to land and back;
            // a piece crosses land if any fine cell it stands for is land
            double tol = 0.25 * geo.BlocksPerDegreeZ / (res * F);
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            for (int i = 1; i < n; i++) if (pl[i] != pl[i - 1]) keep[i] = keep[i - 1] = true;
            var stack = new Stack<(int, int)>();
            int a0 = 0;
            for (int i = 1; i < n; i++) if (keep[i]) { stack.Push((a0, i)); a0 = i; }
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b <= a + 1) continue;
                double vx = px[b] - px[a], vz = pz[b] - pz[a], L = Math.Sqrt(vx * vx + vz * vz);
                int worst = -1; double wd = tol;
                for (int i = a + 1; i < b; i++)
                {
                    double d = L > 0 ? Math.Abs((px[i] - px[a]) * vz - (pz[i] - pz[a]) * vx) / L
                        : Math.Sqrt((px[i] - px[a]) * (px[i] - px[a]) + (pz[i] - pz[a]) * (pz[i] - pz[a]));
                    if (d > wd) { wd = d; worst = i; }
                }
                if (worst >= 0) { keep[worst] = true; stack.Push((a, worst)); stack.Push((worst, b)); }
            }
            int last = 0;
            for (int i = 1; i < n; i++)
            {
                if (!keep[i]) continue;
                if (geo.AcrossSeam(xs[xs.Count - 1], px[i])) break;
                bool crosses = false;
                for (int j = last; j <= i; j++) crosses |= pl[j];
                xs.Add(px[i]); zs.Add(pz[i]); landOut.Add(crosses);
                last = i;
            }
            return xs.Count >= 2;
        }

        /// <summary>Deepens (and where it runs over low land, cuts) a column on a channel's way. Columns elsewhere are unchanged.</summary>
        public void Apply(TerrainModel model, double x, double z, ref TerrainModel.Column c)
        {
            if (segs.Length == 0 || c.Gutter || c.IceBottom <= c.Top) return;
            x = geo.WrapX(x);
            long key = ((long)(int)Math.Floor(x / Cell) << 32) | (uint)(int)Math.Floor(z / Cell);
            if (!grid.TryGetValue(key, out var ids)) return;
            double need = 0, canalOver = double.MaxValue, lakeOver = double.MaxValue;
            bool core = false, near = false, besideCore = false, lakeCatch = false, coreLand = false, erode = false;
            foreach (int i in ids)
            {
                ref readonly var sg = ref segs[i];
                var ch = channels[sg.Channel];
                if (ch.Lake)
                {
                    double dl = Math.Sqrt((x - sg.X1) * (x - sg.X1) + (z - sg.Z1) * (z - sg.Z1));
                    double r = ch.HalfWidth * (1 + ShoreNoise * shore.Fbm(x, z, Math.Max(32, ch.HalfWidth), 3));
                    lakeOver = Math.Min(lakeOver, dl - r);
                    if (dl <= LakeCatch * ch.HalfWidth) lakeCatch = true;
                    continue;
                }
                if (ch.Canal)
                {
                    canalOver = Math.Min(canalOver, SegDistance(sg.X1, sg.Z1, sg.X2, sg.Z2, x, z, out _) - ch.HalfWidth);
                    continue;
                }
                double dx = sg.X2 - sg.X1, dz = sg.Z2 - sg.Z1, l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? Math.Clamp(((x - sg.X1) * dx + (z - sg.Z1) * dz) / l2, 0, 1) : 0;
                double px = sg.X1 + t * dx - x, pz = sg.Z1 + t * dz - z;
                double d = Math.Sqrt(px * px + pz * pz);
                double hw = Math.Min(ch.HalfWidth + (sg.S0 + t * sg.Len) * tanFan, ch.MaxHalfWidth), sh = Shoulder(ch.Depth);
                if (d > hw + sh + 2) continue;
                near = true;
                if (d <= ch.HalfWidth) { core = true; if (sg.Land) coreLand = true; }
                // rules 4: the mouth widened by eroding its low banks (on the fine way through the water, near the river's end).
                // Only in worlds without outline coasts: the small ones, where a mouth comes out too narrow. With outline
                // coasts (CoastsFor) the shore is GSHHG's; eroding it there took ~60 km² of the Mekong delta at the Bassac's
                // mouth at world height 256, where the delta stands within 2 blocks of the sea (the author: "turn off for
                // big worlds"; release/1.0 72af35f, tall.15).
                if (rules >= InPlace && model.Coasts == null && ch.Fine && !sg.Land && sg.S0 + t * sg.Len <= ErodeLength * ch.HalfWidth
                    && d <= Math.Min(hw, ErodeWidth * ch.HalfWidth)) erode = true;
                if (d <= ch.HalfWidth + 2) besideCore = true;
                double want = d <= hw ? ch.Depth : ch.Depth * (1 - Geo.SmoothStep((d - hw) / sh));
                if (want > need) need = want;
            }
            int sea = model.SeaLevel;
            if (canalOver <= TowPath + CanalCutReach || lakeOver <= TowPath + CanalCutReach)
            {
                // in a lake: shelving from its shore down to LakeDepth (the canal's course through it keeps its own depth)
                int canalDepth = canalOver <= 0 ? CanalDepth : Math.Min(LakeDepth, 1 + (int)(-lakeOver / 3));
                ApplyCanal(sea, Math.Min(canalOver, lakeOver), canalDepth, ref c);
                return;
            }
            if (lakeCatch && IsLowLake(sea, c))
            {
                // the rest of a natural lake the canal's lakes stand in (the Great Bitter Lake beyond the disc): the canal's sea
                c.WaterTop = sea - 1;
                c.Water = TerrainModel.WaterSea;
                c.Top = Math.Min(c.Top, sea - 2);
                return;
            }
            if (!near) return;
            int depth = (int)Math.Round(need);
            if (c.Water != TerrainModel.WaterNone)
            {
                // the sea, or a river or lake at sea level (the river's own end): dredged, never filled
                if (c.WaterTop == sea - 1 && depth > 0) c.Top = Math.Min(c.Top, sea - 1 - depth);
                return;
            }
            // an island in the sea stays whole from island rules v3: the channel's water goes around it
            if (c.IslandOverSea && model.Islands != null && model.Islands.Rules >= 3) return;
            // (rules 4: only where the river's end joins the water, and the eroded banks of the mouth; elsewhere the channel
            // keeps to the natural water)
            if (((core && coreLand && LowLand(model, c, sea, 2 * CutHeight, 4)) || (erode && LowLand(model, c, sea, CutHeight, 2))) && depth > 0)
            {
                // low land on the way (a bar, a spit, a beach): cut, becomes sea
                c.Top = Math.Min(c.Top, sea - 1 - depth);
                c.WaterTop = sea - 1;
                c.Water = TerrainModel.WaterSea;
                c.Beach = false;
                c.NearRiver = false;
                c.Ramp = 0;
                return;
            }
            // land below sea level beside the cut holds the water (no water walls)
            if (besideCore && c.Top < sea - 1) c.Top = sea - 1;
        }

        /// <summary>
        /// Land a channel cuts on its way (over the river's own width): up to twice CutHeight high and at most 4 blocks above
        /// the sea. From rules 3, dry land whose height is GLO-90's is judged on the built-in heights (its GridElev, and the top
        /// that height gives): GLO-90 measures the top surface, so forest canopy, dunes and dikes stood as land the cut
        /// wouldn't touch (the Amazon's banks 24 m on GLO-90, 6 m on the built-in heights; an islet left inside the Rhine–Maas
        /// cut near Rotterdam), in 137 of 983 channels, and 0051 keeps which columns are sea the built-in data's.
        /// </summary>
        bool LowLand(TerrainModel model, in TerrainModel.Column c, int sea, double maxElev, int above)
        {
            if (rules >= 3 && c.Glo90Land)
                return c.GridElev <= maxElev && (int)Math.Floor(model.Vertical.Y(c.GridElev)) <= sea + above;
            return c.Elev <= maxElev && c.Top <= sea + above;
        }

        /// <summary>How far from a canal lake's centre (in its radii) a natural lake at about sea level becomes the canal's sea
        /// (the Great Bitter Lake is wider than its disc).</summary>
        const double LakeCatch = 3;

        /// <summary>Lake water (not a river) at most a few blocks above the sea: the canal's sea takes it in.</summary>
        static bool IsLowLake(int sea, in TerrainModel.Column c) =>
            c.Water != TerrainModel.WaterNone && c.Water != TerrainModel.WaterSea && !c.River && c.WaterTop >= sea - 1 && c.WaterTop <= sea + 2;

        /// <summary>A column of a canal or its lakes (over: distance beyond their water, negative inside): sea water at sea level
        /// (depth blocks deep, or deeper where it already was), a towpath or beach at sea level, then a cut slope up to the
        /// natural ground.</summary>
        static void ApplyCanal(int sea, double over, int depth, ref TerrainModel.Column c)
        {
            if (over <= 0)
            {
                c.Top = Math.Min(c.Top, sea - 1 - depth);
                c.WaterTop = sea - 1;
                c.Water = TerrainModel.WaterSea;
                c.River = c.NearRiver = c.Beach = c.Rapid = c.RapidsReach = false;
                c.Ramp = c.Fall = 0;
                c.FlowX = c.FlowZ = 0;
                c.IceBottom = int.MaxValue;
                return;
            }
            int allowed = sea + (int)Math.Floor(Math.Max(0, over - TowPath) / CanalSlope);
            if (c.Water != TerrainModel.WaterNone)
            {
                // a lake at or just above sea level (the data has the Bitter Lakes as a salt lake at sea level, or a block up):
                // joins the sea, else it would stand above the cut's banks or be lake water in the canal's sea
                if (IsLowLake(sea, c))
                {
                    c.WaterTop = sea - 1;
                    c.Water = TerrainModel.WaterSea;
                    c.Top = Math.Min(c.Top, sea - 2);
                    return;
                }
                // water higher up in the cut is drained
                if (c.WaterTop < allowed) return;
                c.Water = TerrainModel.WaterNone;
                c.WaterTop = -1;
                c.River = c.Rapid = c.RapidsReach = false;
                c.Ramp = c.Fall = 0;
            }
            if (c.Top > allowed) { c.Top = allowed; c.IceBottom = int.MaxValue; }
            // the towpath holds the canal's water; ground below sea level in the cut is raised to the water (no water walls)
            if (over <= TowPath + 1 && c.Top < sea) c.Top = sea;
            else if (c.Top < sea - 1) c.Top = sea - 1;
            c.Beach = over <= TowPath;
        }
    }
}
