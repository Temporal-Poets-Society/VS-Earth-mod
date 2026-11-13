using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's one geography for ranges: the WGSRPD level-3 botanical countries of treeregions.json, used by trees, ground
    /// plants, the placement catalogue, the fauna packs and the vanilla animals' 1318 ranges (ranges proposal,
    /// docs/research/ranges-2026-11-07). Immutable after construction and shared between threads; one instance per regions
    /// file (<see cref="Shared"/>).
    /// <para>Lookups by surface (<see cref="At"/>): every <b>land</b> point of the Earth terrain has a region. A point inside
    /// a polygon (or within <see cref="CoastTolerance"/> of one) takes it; land the polygons miss (their coast is coarser than
    /// the terrain's, e.g. half of Antarctica's coast, deltas, small islands) takes the first region met on rings of growing
    /// radius up to <see cref="LandFillDegrees"/>. Fresh water and shore water use the same search up to
    /// <see cref="WaterSearchDegrees"/>; open sea keeps only the coast tolerance. No region is decided by a latitude.</para>
    /// <para>Point lookup: a 1° cell grid of candidate polygons and per-ring latitude bands of edges, so a lookup tests only
    /// the few edges crossing the point's latitude; allocation-free.</para>
    /// </summary>
    public sealed class BotanicalRegions
    {
        /// <summary>Limited allowance for differing terrain and shoreline resolutions (degrees).</summary>
        public const double CoastTolerance = .08;
        /// <summary>Land outside every polygon takes the nearest region within this many degrees.</summary>
        public const double LandFillDegrees = 5;
        /// <summary>Aquatic plants and shore water: nearest region within this many degrees (was RegionalPlantCatalog's).</summary>
        public const double WaterSearchDegrees = 2;

        /// <summary>What lies at the point being looked up; decides how far a point outside every polygon may reach.</summary>
        public enum Surface { Land, Water, Sea }

        public sealed class Region { public string Code { get; set; } public string Name { get; set; } public double[][][][] Polygons { get; set; } }
        public sealed class RegionFile { public int Version { get; set; } public Region[] Regions { get; set; } }

        const double BandHeight = .05;
        sealed class Ring
        {
            public readonly double[] X, Y;
            public readonly double South;
            public readonly int[][] Bands;
            public Ring(double[][] points)
            {
                int n = points.Length;
                bool closed = points[0][0] == points[n - 1][0] && points[0][1] == points[n - 1][1];
                int count = closed ? n : n + 1;
                X = new double[count]; Y = new double[count];
                for (int i = 0; i < count; i++) { var p = points[i % n]; X[i] = p[0]; Y[i] = p[1]; }
                South = Y.Min();
                int bands = (int)Math.Floor((Y.Max() - South) / BandHeight) + 1;
                var lists = new List<int>[bands];
                for (int i = 0; i + 1 < count; i++)
                {
                    int b0 = Band(Math.Min(Y[i], Y[i + 1])), b1 = Band(Math.Max(Y[i], Y[i + 1]));
                    for (int b = Math.Max(b0, 0); b <= Math.Min(b1, bands - 1); b++) (lists[b] ??= new List<int>()).Add(i);
                }
                Bands = lists.Select(l => l?.ToArray() ?? Array.Empty<int>()).ToArray();
            }
            public int Band(double y) => (int)Math.Floor((y - South) / BandHeight);
            public bool Contains(double x, double y)
            {
                int b = Band(y);
                if (b < 0 || b >= Bands.Length) return false;
                var edges = Bands[b];
                bool inside = false;
                for (int k = 0; k < edges.Length; k++)
                {
                    int i = edges[k];
                    double ay = Y[i], by = Y[i + 1];
                    if ((ay > y) != (by > y) && x < (X[i + 1] - X[i]) * (y - ay) / (by - ay) + X[i]) inside = !inside;
                }
                return inside;
            }
            /// <summary>Smallest squared distance (degrees²) to an edge, if below best.</summary>
            public double Distance2(double x, double y, double best)
            {
                int b0 = Math.Max(Band(y - CoastTolerance), 0), b1 = Math.Min(Band(y + CoastTolerance), Bands.Length - 1);
                for (int b = b0; b <= b1; b++)
                {
                    var edges = Bands[b];
                    for (int k = 0; k < edges.Length; k++)
                    {
                        int i = edges[k];
                        double ax = X[i], ay = Y[i], dx = X[i + 1] - ax, dy = Y[i + 1] - ay;
                        double len = dx * dx + dy * dy, t = len == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len, 0, 1);
                        double ex = x - ax - t * dx, ey = y - ay - t * dy, d = ex * ex + ey * ey;
                        if (d < best) best = d;
                    }
                }
                return best;
            }
        }
        sealed class Polygon
        {
            public int Region;
            public Ring Outer;
            public Ring[] Holes;
            public double West, East, South, North;
        }

        readonly Polygon[] polygons;
        readonly int[][] cells = new int[360 * 180][];
        readonly string[] codes, names;
        readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
        static readonly double[] SearchRadii = { .15, .3, .5, .75, 1, 1.5, 2, 3, 4, 5 };
        static readonly double[] DirX = Enumerable.Range(0, 16).Select(i => Math.Cos(i * Math.PI / 8)).ToArray();
        static readonly double[] DirY = Enumerable.Range(0, 16).Select(i => Math.Sin(i * Math.PI / 8)).ToArray();

        public int Count => codes.Length;

        public BotanicalRegions(string json)
        {
            var data = JsonSerializer.Deserialize<RegionFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data?.Version != 1 || data.Regions == null) throw new ArgumentException("Unsupported botanical boundaries");
            codes = new string[data.Regions.Length];
            names = new string[data.Regions.Length];
            var list = new List<Polygon>();
            for (int r = 0; r < data.Regions.Length; r++)
            {
                var region = data.Regions[r];
                if (region?.Code == null) throw new ArgumentException("Invalid botanical region");
                codes[r] = region.Code;
                names[r] = region.Name;
                if (!index.TryAdd(region.Code, r)) throw new ArgumentException("Duplicate botanical region " + region.Code);
                foreach (var rings in region.Polygons ?? Array.Empty<double[][][]>())
                {
                    if (rings.Length == 0 || rings[0].Length < 4) throw new ArgumentException("Invalid botanical polygon");
                    var outer = new Ring(rings[0]);
                    list.Add(new Polygon
                    {
                        Region = r, Outer = outer, Holes = rings.Skip(1).Where(h => h.Length >= 4).Select(h => new Ring(h)).ToArray(),
                        West = outer.X.Min(), East = outer.X.Max(), South = outer.South, North = outer.Y.Max(),
                    });
                }
            }
            polygons = list.ToArray();
            var cellLists = new List<int>[cells.Length];
            for (int p = 0; p < polygons.Length; p++)
            {
                var poly = polygons[p];
                for (int z = CellZ(poly.South - CoastTolerance); z <= CellZ(poly.North + CoastTolerance); z++)
                for (int x = CellX(poly.West - CoastTolerance); x <= CellX(poly.East + CoastTolerance); x++)
                    (cellLists[z * 360 + x] ??= new List<int>()).Add(p);
            }
            for (int i = 0; i < cells.Length; i++) cells[i] = cellLists[i]?.ToArray();
        }

        // ------------------------------------------------------------------ one instance per regions file

        static readonly object sharedLock = new();
        static BotanicalRegions sharedInstance;
        static int sharedLength;
        static ulong sharedHash;

        /// <summary>
        /// The instance for this regions file, built once and reused by every catalogue (trees, plants, placement, fauna,
        /// vanilla animals). Assets come back as new strings each time, so the file is recognised by its content.
        /// </summary>
        public static BotanicalRegions Shared(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            ulong hash = WeatherNoise.Fnv1a(json);
            lock (sharedLock)
            {
                if (sharedInstance != null && sharedLength == json.Length && sharedHash == hash) return sharedInstance;
                var built = new BotanicalRegions(json);
                sharedInstance = built; sharedLength = json.Length; sharedHash = hash;
                return built;
            }
        }

        // ------------------------------------------------------------------ codes and masks

        static int CellX(double lon) => Math.Clamp((int)Math.Floor(lon + 180), 0, 359);
        static int CellZ(double lat) => Math.Clamp((int)Math.Floor(lat + 90), 0, 179);
        static double WrapLon(double lon) => ((lon + 180) % 360 + 360) % 360 - 180;

        public int IndexOf(string code) => code != null && index.TryGetValue(code, out int i) ? i : -1;
        public bool ContainsCode(string code) => code != null && index.ContainsKey(code);
        public string Code(int region) => (uint)region < (uint)codes.Length ? codes[region] : null;
        public string Name(int region) => (uint)region < (uint)names.Length ? names[region] : "open ocean / unmapped";
        public string Name(string code) => Name(IndexOf(code));

        /// <summary>
        /// The set of these region codes. What an unknown code means is the caller's: <paramref name="unknown"/> gets each one
        /// and may throw (the plant and placement catalogues and the region ranges call it a format error; the fauna catalogue
        /// holds its rule back before it gets here); without it, unknown codes are left out (the tree catalogue warns first).
        /// </summary>
        public RegionSet Set(IEnumerable<string> regionCodes, Action<string> unknown = null)
        {
            var set = new RegionSet(codes.Length);
            foreach (string c in regionCodes)
            {
                int i = IndexOf(c);
                if (i >= 0) set.Add(i);
                else unknown?.Invoke(c);
            }
            return set;
        }
        /// <summary>An empty set of this index's regions (to fill with <see cref="RegionSet.UnionWith"/>).</summary>
        public RegionSet EmptySet() => new RegionSet(codes.Length);

        // ------------------------------------------------------------------ lookups

        /// <summary>Region index containing the point, else (coastal) the nearest within CoastTolerance degrees; -1 none.</summary>
        public int Find(double lat, double lon, bool coastal = true)
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90) return -1;
            lon = WrapLon(lon);
            var list = cells[CellZ(lat) * 360 + CellX(lon)];
            if (list == null) return -1;
            for (int k = 0; k < list.Length; k++)
            {
                var p = polygons[list[k]];
                if (lon < p.West || lon > p.East || lat < p.South || lat > p.North || !p.Outer.Contains(lon, lat)) continue;
                bool hole = false;
                for (int h = 0; h < p.Holes.Length && !hole; h++) hole = p.Holes[h].Contains(lon, lat);
                if (!hole) return p.Region;
            }
            if (!coastal) return -1;
            double best = CoastTolerance * CoastTolerance;
            int found = -1;
            for (int k = 0; k < list.Length; k++)
            {
                var p = polygons[list[k]];
                if (lon < p.West - CoastTolerance || lon > p.East + CoastTolerance || lat < p.South - CoastTolerance || lat > p.North + CoastTolerance) continue;
                double d = p.Outer.Distance2(lon, lat, best);
                if (d < best) { best = d; found = p.Region; }
            }
            return found;
        }

        /// <summary>Code of <see cref="Find"/> (null for none).</summary>
        public string CodeAt(double lat, double lon, bool coastal = true) => Code(Find(lat, lon, coastal));

        /// <summary>The region at a point of the given surface (see the class summary); -1 none.</summary>
        public int At(double lat, double lon, Surface surface) => surface switch
        {
            Surface.Land => FindLand(lat, lon),
            Surface.Water => FindNear(lat, lon, WaterSearchDegrees),
            _ => Find(lat, lon),
        };

        /// <summary>A land point's region: <see cref="Find"/>, else the first region met within <see cref="LandFillDegrees"/>
        /// of its <see cref="BlendCell"/> cell's centre (<see cref="LandFill"/>). Misses are cached per thread on that grid (land
        /// the polygons miss comes in patches).</summary>
        public int FindLand(double lat, double lon)
        {
            int r = Find(lat, lon);
            if (r >= 0 || !double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90) return r;
            lon = WrapLon(lon);
            int ci = (int)Math.Floor(lat / BlendCell), cj = (int)Math.Floor(lon / BlendCell);
            long key = ((long)ci << 32) ^ (uint)cj;
            var cache = landCache ??= new SmallCache();
            if (cache.Owner != this) { cache.Owner = this; Array.Fill(cache.Keys, long.MinValue); }
            for (int k = 0; k < SmallCache.Size; k++)
                if (cache.Keys[k] == key) return cache.Values[k];
            r = LandFill(ci, cj);
            int slot = cache.Next;
            cache.Next = (slot + 1) % SmallCache.Size;
            cache.Keys[slot] = key;
            cache.Values[slot] = r;
            return r;
        }

        /// <summary>
        /// The region land outside every polygon takes: the first met within <see cref="LandFillDegrees"/> of the centre of its
        /// <see cref="BlendCell"/> cell (ci, cj), as <see cref="FindBlended"/> does, so every point of the cell gets the same one
        /// whatever order chunks are generated in (before Earth 2.0.0 the search started from whichever point of the cell a
        /// thread asked first).
        /// </summary>
        int LandFill(int ci, int cj) => FindNear(Math.Clamp((ci + 0.5) * BlendCell, -90, 90), (cj + 0.5) * BlendCell, LandFillDegrees);

        /// <summary>How a land point got its region, for /earth where.</summary>
        public string DescribeLand(double lat, double lon)
        {
            int inside = Find(lat, lon, false);
            if (inside >= 0) return Name(inside) + " (" + Code(inside) + ")";
            int coast = Find(lat, lon);
            if (coast >= 0) return Name(coast) + " (" + Code(coast) + "; within " + CoastTolerance + "° of its outline)";
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90) return "none (outside the Earth)";
            int fill = LandFill((int)Math.Floor(lat / BlendCell), (int)Math.Floor(WrapLon(lon) / BlendCell));
            return fill >= 0 ? Name(fill) + " (" + Code(fill) + "; outside every outline, nearest region within " + LandFillDegrees + "°)"
                : "none (no region within " + LandFillDegrees + "°)";
        }

        /// <summary>
        /// Find, or for points outside every polygon the first region met on rings of growing radius (16 directions, up to
        /// maxDegrees). Deterministic and allocation-free; cheap far from land (empty grid cells).
        /// </summary>
        public int FindNear(double lat, double lon, double maxDegrees)
        {
            int r = Find(lat, lon);
            if (r >= 0 || maxDegrees <= 0 || !double.IsFinite(lat) || !double.IsFinite(lon)) return r;
            double c = Math.Max(Math.Cos(Math.Clamp(lat, -90, 90) * Math.PI / 180), .2);
            if (!AnyCellNear(lat, lon, maxDegrees, maxDegrees / c)) return -1;
            for (int i = 0; i < SearchRadii.Length && SearchRadii[i] <= maxDegrees + 1e-9; i++)
            {
                double radius = SearchRadii[i];
                for (int d = 0; d < 16; d++)
                {
                    double y = lat + radius * DirY[d];
                    if (y < -90 || y > 90) continue;
                    r = Find(y, lon + radius * DirX[d] / c);
                    if (r >= 0) return r;
                }
            }
            return -1;
        }

        bool AnyCellNear(double lat, double lon, double dLat, double dLon)
        {
            int z0 = CellZ(lat - dLat), z1 = CellZ(lat + dLat);
            int span = (int)Math.Ceiling(dLon) + 1;
            if (span >= 180) span = 180;
            int x0 = CellX(WrapLon(lon));
            for (int z = z0; z <= z1; z++)
            for (int dx = -span; dx <= span; dx++)
                if (cells[z * 360 + ((x0 + dx) % 360 + 360) % 360] != null) return true;
            return false;
        }

        // ------------------------------------------------------------------ blended borders (trees)

        /// <summary>Lookup grid for the blended lookup (degrees): positions within one cell share its result.</summary>
        public const double BlendCell = 0.02;

        /// <summary>
        /// Botanical country for natural vegetation on land: borders between countries are blended by looking up a point
        /// displaced by smooth, deterministic noise (about 0.3° typical, at most 1°), so a political line such as 31°N
        /// becomes an irregular transition zone instead of a straight cut. A displaced point off the land falls back to
        /// the undisplaced cell centre, then to the exact point's land region (<see cref="FindLand"/>), so shores and small
        /// islands keep their own region. Pure function of the position; cached per thread on a <see cref="BlendCell"/> grid.
        /// </summary>
        public int FindBlended(double lat, double lon)
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90) return -1;
            lon = WrapLon(lon);
            int ci = (int)Math.Floor(lat / BlendCell), cj = (int)Math.Floor(lon / BlendCell);
            long key = ((long)ci << 32) ^ (uint)cj;
            var cache = blendCache ??= new SmallCache();
            if (cache.Owner != this) { cache.Owner = this; Array.Fill(cache.Keys, long.MinValue); }
            for (int k = 0; k < SmallCache.Size; k++)
                if (cache.Keys[k] == key) return cache.Values[k] != -2 ? cache.Values[k] : FindLand(lat, lon);
            double clat = (ci + 0.5) * BlendCell, clon = (cj + 0.5) * BlendCell;
            BorderOffset(clat, clon, out double dLat, out double dLon);
            int r = Find(Math.Clamp(clat + dLat, -90, 90), clon + dLon);
            if (r < 0) r = Find(clat, clon);
            int slot = cache.Next;
            cache.Next = (slot + 1) % SmallCache.Size;
            cache.Keys[slot] = key;
            cache.Values[slot] = r < 0 ? -2 : r;
            return r >= 0 ? r : FindLand(lat, lon);
        }

        sealed class SmallCache
        {
            public const int Size = 8;
            public BotanicalRegions Owner;
            public readonly long[] Keys = new long[Size];
            public readonly int[] Values = new int[Size];
            public int Next;
        }
        [ThreadStatic] static SmallCache blendCache;
        [ThreadStatic] static SmallCache landCache;

        /// <summary>Smooth deterministic displacement (degrees) for blending botanical borders; periodic in longitude.</summary>
        public static void BorderOffset(double lat, double lon, out double dLat, out double dLon)
        {
            double a = 0.7 * Value(lat, lon, 1.5, 11) + 0.3 * Value(lat, lon, 0.5, 23);
            double b = 0.7 * Value(lat, lon, 1.5, 37) + 0.3 * Value(lat, lon, 0.5, 51);
            dLat = a;
            dLon = b / Math.Max(0.3, Math.Cos(lat * Math.PI / 180));
        }

        // value noise on a lat/lon lattice, -1..1, smooth, period 360° in longitude
        static double Value(double lat, double lon, double spacing, int seed)
        {
            double x = (lon + 180) / spacing, y = (lat + 90) / spacing;
            int period = (int)Math.Round(360 / spacing);
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
            double v00 = Hash(xa, y0, seed), v10 = Hash(xb, y0, seed), v01 = Hash(xa, y0 + 1, seed), v11 = Hash(xb, y0 + 1, seed);
            return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
        }

        static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h / (double)uint.MaxValue * 2 - 1;
            }
        }
    }

    /// <summary>
    /// A set of regions of one <see cref="BotanicalRegions"/> index, as bits: every range file's region list (plant and
    /// placement taxa, trees and their analogues, fauna, vanilla animals, people) is one of these
    /// (<see cref="BotanicalRegions.Set"/>). Filled while its catalogue is built, read-only after; a region outside the index
    /// (-1: none) is never in it.
    /// </summary>
    public sealed class RegionSet
    {
        readonly ulong[] bits;

        internal RegionSet(int count) { bits = new ulong[(count + 63) >> 6]; }

        internal void Add(int region) => bits[region >> 6] |= 1UL << (region & 63);

        /// <summary>Adds another set of the same index (a patch of several taxa grows where any of them does).</summary>
        internal void UnionWith(RegionSet other)
        {
            for (int i = 0; i < bits.Length; i++) bits[i] |= other.bits[i];
        }

        public bool Contains(int region) => (uint)region < (uint)(bits.Length << 6) && (bits[region >> 6] & (1UL << (region & 63))) != 0;

        public bool this[int region] => Contains(region);
    }

    /// <summary>
    /// A box [west, south, east, north] in degrees, as the range files write them (tree and fauna envelopes, 1318 losses).
    /// West greater than east crosses the 180° line. Read and checked in one place: four finite numbers, south not above north.
    /// </summary>
    public readonly struct GeoBox
    {
        public readonly double West, South, East, North;

        public GeoBox(double west, double south, double east, double north) { West = west; South = south; East = east; North = north; }

        /// <summary>The box of a range file's four numbers, or false when they are not four finite numbers with south not
        /// above north.</summary>
        public static bool TryFrom(double[] b, out GeoBox box)
        {
            box = default;
            if (b == null || b.Length != 4 || !double.IsFinite(b[0]) || !double.IsFinite(b[1]) || !double.IsFinite(b[2]) || !double.IsFinite(b[3]) || b[1] > b[3]) return false;
            box = new GeoBox(b[0], b[1], b[2], b[3]);
            return true;
        }

        /// <summary>The boxes of a list (null for none), or null and false when one is not a box.</summary>
        public static bool TryFrom(double[][] list, out GeoBox[] boxes)
        {
            boxes = null;
            if (list == null) return true;
            var result = new GeoBox[list.Length];
            for (int i = 0; i < list.Length; i++) if (!TryFrom(list[i], out result[i])) return false;
            boxes = result;
            return true;
        }

        public bool Contains(double lat, double lon) => lat >= South && lat <= North && (West <= East ? lon >= West && lon <= East : lon >= West || lon <= East);

        /// <summary>Whether any of the boxes holds the point (false for none).</summary>
        public static bool Any(GeoBox[] boxes, double lat, double lon)
        {
            if (boxes == null) return false;
            foreach (var b in boxes) if (b.Contains(lat, lon)) return true;
            return false;
        }
    }
}
