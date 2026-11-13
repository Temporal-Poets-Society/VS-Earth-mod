using System;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EarthMap.Core
{
    /// <summary>
    /// Where new players start (decision 0023): dry, gentle land near a chosen place, or a random place at the starting
    /// climate. Also the Earth tab's world size and coordinate values.
    /// </summary>
    public static class SpawnSearch
    {
        /// <summary>Vanilla's starting climates as yearly mean temperatures (°C), from its world creation screen.</summary>
        public static readonly (string climate, double min, double max)[] ClimateBands =
        {
            ("hot", 28, 32), ("warm", 19, 23), ("temperate", 6, 14), ("cool", -5, 1), ("icy", -15, -10),
        };

        /// <summary>Highest ground (blocks above sea level) a random spawn takes: lowland, as the former fixed places were.</summary>
        public const int LowlandAboveSea = 60;
        /// <summary>Blocks a spawn keeps from the map's edges.</summary>
        public const double Margin = 64;

        public static bool OnMap(int mapX, int mapZ, double x, double z, double margin)
            => x >= margin && z >= margin && x <= mapX - margin && z <= mapZ - margin;

        /// <summary>Dry, ice-free land on the map at most maxAboveSea blocks above sea level, within 3 blocks of the height
        /// 12 blocks out on each side, with no water there.</summary>
        public static bool Spawnable(TerrainModel m, int mapX, int mapZ, double x, double z, int maxAboveSea)
        {
            if (!OnMap(mapX, mapZ, x, z, Margin)) return false;
            int sea = m.SeaLevel;
            var c = m.GetColumn(x, z);
            if (c.Water != TerrainModel.WaterNone || c.Gutter || c.IceBottom <= c.Top || c.Top < sea || c.Top > sea + maxAboveSea) return false;
            foreach (var (dx, dz) in new[] { (12, 0), (-12, 0), (0, 12), (0, -12) })
            {
                var d = m.GetColumn(x + dx, z + dz);
                if (d.Water != TerrainModel.WaterNone || Math.Abs(d.Top - c.Top) > 3) return false;
            }
            return true;
        }

        /// <summary>Nearest spawnable land to (x, z), searching outwards in 32-block steps up to radius.</summary>
        public static bool FindDryLand(TerrainModel m, int mapX, int mapZ, double x, double z, double radius, int maxAboveSea, out int bx, out int bz)
        {
            for (double r = 0; r <= radius; r += 32)
            {
                int n = r == 0 ? 1 : Math.Max(8, (int)(2 * Math.PI * r / 32));
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    double cx = x + r * Math.Cos(a), cz = z + r * Math.Sin(a);
                    if (!Spawnable(m, mapX, mapZ, cx, cz, maxAboveSea)) continue;
                    bx = (int)Math.Floor(cx); bz = (int)Math.Floor(cz);
                    return true;
                }
            }
            bx = (int)x; bz = (int)z;
            return false;
        }

        /// <summary>
        /// Decision 0037: equal chance for each qualifying major region, then area-weighted choice of a qualifying cell
        /// within it. A hashed seed selects the region, cell and local jitter. Only used when creating a new world;
        /// EarthParams saves the resulting spawn. Climate bands are exactly survival 1.22.7's startingClimate labels.
        /// The terrain check (<see cref="RandomSpawnable"/>) runs only on the cells drawn, on the world's own heights: a
        /// cell that fails is put aside and another drawn, and a region none of whose cells passes is put aside the same
        /// way. That gives each the same chance as checking every cell first, which on full GLO-90 decoded tiles across
        /// the globe (4 min 12 s at 1:10, 5 November 2026).
        /// </summary>
        public static bool FindClimatePlace(TerrainModel m, int mapX, int mapZ, int seed, string climate, out double lat, out double lon, out double tMean)
        {
            var band = Array.Find(ClimateBands, b => b.climate == climate);
            if (band.climate == null) band = ClimateBands[2];
            var cells = spawnCells.GetValue(m, _ => new SpawnCells(m, mapX, mapZ));
            var rnd = new SpawnRandom(unchecked((ulong)(long)seed));
            SpawnCell cell;
            lock (cells)
            {
                var groups = cells.For(band.climate);
                while (true)
                {
                    if (groups.Count == 0) { lat = lon = tMean = double.NaN; return false; }
                    int r = (int)(rnd.Next() * groups.Count);
                    if (cells.TryDraw(groups[r], rnd, out cell)) break;
                    groups.RemoveAt(r);
                }
            }
            for (int i = 0; i < 48; i++)
            {
                double la = cell.Lat + (rnd.Next() - .5) * cell.Size, lo = cell.Lon + (rnd.Next() - .5) * cell.Size;
                m.ClimateAt(la, lo, out double t, out _, out _);
                if (t < band.min || t > band.max) continue;
                m.Geometry.FromGeo(la, lo, out double x, out double z);
                if (StartRegion(la,lo) != StartRegion(cell.Lat,cell.Lon) || !RandomSpawnable(m,mapX,mapZ,x,z)) continue;
                lat = la; lon = lo; tMean = t;
                return true;
            }
            // The cell centre was fully validated. A coast or narrow climate band never loses its region's chance
            // just because jitter failed; there is no retry that quietly favours a large continental interior.
            lat=cell.Lat;lon=cell.Lon;tMean=cell.Temperature;return true;
        }

        /// <summary>Broad sampling regions, not country borders or a population map. These disjoint rules cover all
        /// longitudes and both hemispheres, including ice-free Antarctic coast for climates that qualify.</summary>
        public static string StartRegion(double lat,double lon)
        {
            if(lat < -60) return "Antarctica";
            if(lon < -30) return lat < 10 ? "South America" : lon < -100 ? "Western North America" : "Eastern North America";
            if(lon < 45 && lat >= 35) return lon < 15 ? "Western Europe / Mediterranean" : "Eastern Europe";
            if(lon < 60 && lat >= 7 && lat < 35) return "North Africa / West Asia";
            if(lon < 60 && lat < 7) return "Sub-Saharan Africa";
            if(lon >= 110 && lat < -12) return "Australia / New Zealand";
            if(lon >= 100 && lat >= 25) return "East Asia";
            if(lon >= 45 && lat >= 35) return "Central / North Asia";
            return "South / Southeast Asia";
        }

        /// <summary>Dry gentle lowland, with enough nearby land to rule out tiny islands while permitting coasts.
        /// Three of eight probes at 128 blocks and one at 256 must be land; the 12-block safety cross stays dry.</summary>
        public static bool RandomSpawnable(TerrainModel m,int mapX,int mapZ,double x,double z)
        {
            if(!Spawnable(m,mapX,mapZ,x,z,LowlandAboveSea)) return false;
            var geo=m.TerrainGeoAt(x,z);
            // The outline catalogue explicitly identifies islands below 1000 km2. Exclude them even when nearby
            // mainland happens to satisfy the outer probes across a strait; large islands (e.g. New Zealand) remain.
            if(m.Islands?.IsLand(geo.Lat,geo.Lon)==true)return false;
            foreach(var ring in new[]{(radius:128,minimum:3),(radius:256,minimum:1)})
            {
                int land=0;
                for(int k=0;k<8;k++)
                {
                    var c=m.GetColumn(x+ring.radius*Math.Cos(k*Math.PI/4),z+ring.radius*Math.Sin(k*Math.PI/4));
                    if(c.Water==TerrainModel.WaterNone && !c.Gutter)land++;
                }
                if(land<ring.minimum)return false;
            }
            return true;
        }

        readonly record struct SpawnCell(double Lat,double Lon,double Temperature,double Size);
        static readonly ConditionalWeakTable<TerrainModel,SpawnCells> spawnCells = new();
        /// <summary>
        /// Land cells at each starting climate by region, from the climate and land data only (no terrain), built once per
        /// creation model and shared by all five climates. Centres are merely search anchors; validated local jitter
        /// supplies the actual random point. The terrain check of a cell is done when it is first drawn and remembered,
        /// so the same seed draws the same place however many cells were checked before.
        /// </summary>
        sealed class SpawnCells
        {
            readonly TerrainModel m;
            readonly int mapX,mapZ;
            readonly Dictionary<string,Dictionary<string,List<SpawnCell>>> bands=new();
            readonly Dictionary<SpawnCell,bool> spawnable=new();
            readonly HashSet<string> refined=new();
            public SpawnCells(TerrainModel m,int mapX,int mapZ)
            {
                this.m=m;this.mapX=mapX;this.mapZ=mapZ;
                foreach(var b in ClimateBands)bands[b.climate]=new();
                for(double lat=-84.75;lat<85;lat+=.5)
                for(double lon=-179.75;lon<180;lon+=.5)
                    Add(lat,lon,.5,null,false);
            }

            void Add(double lat,double lon,double size,string only,bool south)
            {
                var source=m.Data.SampleTerrain(lat,lon);
                if(source.Ocean>=.5 || source.Elev>2000)return;
                m.ClimateAt(lat,lon,out double t,out _,out _);
                var b=Array.Find(ClimateBands,b=>t>=b.min && t<=b.max);
                if(b.climate==null || (only!=null && (b.climate!=only || (lat<0)!=south)))return;
                string region=StartRegion(lat,lon);
                if(!bands[b.climate].TryGetValue(region,out var list))bands[b.climate][region]=list=new();
                list.Add(new SpawnCell(lat,lon,t,size));
            }

            /// <summary>The climate's regions (copies of the lists, for one search to put aside); the first time, a
            /// hemisphere where no half-degree cell passes is refined.</summary>
            public List<List<SpawnCell>> For(string climate)
            {
                if(refined.Add(climate))
                foreach(bool south in new[]{false,true})
                {
                    bool found=false;
                    foreach(var list in bands[climate].Values)
                    {
                        foreach(var c in list)if((c.Lat<0)==south && Spawnable(c)){found=true;break;}
                        if(found)break;
                    }
                    if(found)continue;
                    // Narrow ice-free southern coasts can lie between coarse anchors. Refine a missing hemisphere at
                    // 0.1 degrees before declaring it unavailable; don't substitute a warmer climate or glacier spawn.
                    // Its half-degree cells all failed, so they go.
                    foreach(var list in bands[climate].Values)list.RemoveAll(c=>(c.Lat<0)==south);
                    for(double lat=south?-84.95:.05;south?lat<0:lat<85;lat+=.1)
                    for(double lon=-179.95;lon<180;lon+=.1)
                        Add(lat,lon,.1,climate,south);
                }
                var groups=new List<List<SpawnCell>>();
                foreach(var list in bands[climate].Values)if(list.Count>0)groups.Add(new List<SpawnCell>(list));
                return groups;
            }

            /// <summary>An area-weighted cell of the region that passes the terrain check, putting aside those that
            /// don't (from this search's copy); false when none does.</summary>
            public bool TryDraw(List<SpawnCell> region,SpawnRandom rnd,out SpawnCell cell)
            {
                while(region.Count>0)
                {
                    // Latitude rows have different areas. Rejection by cos(latitude) keeps area weights inside a region.
                    int i;
                    do{i=(int)(rnd.Next()*region.Count);}
                    while(rnd.Next()>Math.Cos(region[i].Lat*Math.PI/180)*region[i].Size*region[i].Size/.25);
                    if(Spawnable(region[i])){cell=region[i];return true;}
                    region[i]=region[^1];region.RemoveAt(region.Count-1);
                }
                cell=default;return false;
            }

            bool Spawnable(SpawnCell c)
            {
                if(!spawnable.TryGetValue(c,out bool ok))
                {
                    m.Geometry.FromGeo(c.Lat,c.Lon,out double x,out double z);
                    spawnable[c]=ok=RandomSpawnable(m,mapX,mapZ,x,z);
                }
                return ok;
            }
        }

        // SplitMix64: avalanche the complete signed seed, then use a reproducible full-width stream. No related
        // System.Random initial states for sequential typed seeds and no runtime-dependent RNG implementation.
        sealed class SpawnRandom
        {
            ulong state;
            public SpawnRandom(ulong seed){state=seed ^ 0x454152544853504EUL;Next();}
            public double Next()
            {
                ulong z=unchecked(state+=0x9E3779B97F4A7C15UL);
                z=unchecked((z^(z>>30))*0xBF58476D1CE4E5B9UL);
                z=unchecked((z^(z>>27))*0x94D049BB133111EBUL);
                return ((z^(z>>31))>>11)*(1.0/(1UL<<53));
            }
        }

        /// <summary>"lat, lon" in degrees (also "lat lon" or "lat;lon").</summary>
        public static bool TryParseLatLon(string s, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            var parts = (s ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
                && double.IsFinite(lat) && double.IsFinite(lon) && Math.Abs(lat) <= 90;
        }

        /// <summary>The Equator (m): the Earth tab's scale 1:S makes Cylinder World's ring (2 × the world's length) this over S.</summary>
        public const double EquatorMetres = 40_075_017;
        /// <summary>The Earth tab's default scale, 1:100.</summary>
        public const int DefaultScale = 100;
        /// <summary>The scales the size option takes: 1:5 is the finest whose world fits the width cap; 1:1000 the coarsest.</summary>
        public const int MinScale = 5, MaxScale = 1000;
        /// <summary>The widest world the size option makes (vanilla's own limit is far higher, 67 108 864).</summary>
        public const int MaxWidth = 8_192_000;
        /// <summary>
        /// Blocks of hidden seam ocean beyond each of Cylinder World's two jump lines (2.0.0-rc.5, the author through the
        /// Cylinder World session, 9 November 2026: "vanilla does not have any preset ratio … Current width is confusing. and
        /// i dont see any reason for it"). Before, the world was 2.5 × as wide as long, a tenth of the width hidden at each
        /// side (500 992 blocks at 1:10) for no purpose: nothing generates or loads there beyond about a view distance past
        /// the line. Cylinder World's Earth proportions need at least the view radius + 32, rounded up to 256 (512 at 12
        /// chunks, 1 280 at 32); 32 map regions also cover a server that raises its view distance later and far-view mods.
        /// </summary>
        public const int SeamMargin = 16_384;

        /// <summary>
        /// The world's width and length for the Earth tab's size value: a scale (its denominator: "10" is 1:10; "1:10" is
        /// read too), or, from worlds configured before 2.0.0-rc.5, a world width in blocks (64 000 and more), whose length
        /// stays as it was (0.4 × the width). The length is the Equator over twice the scale, in whole 512-block map regions
        /// (the same lengths as before for 1:200 to 1:10); the width is twice the length (Cylinder World's ring) and the
        /// seam margin at each side. An unusable value gives 1:100 and bad = true.
        /// </summary>
        public static void SizeFor(string value, out int width, out int length, out bool bad)
        {
            string v = (value ?? "").Trim();
            // a value written as a ratio is a scale and nothing else (Codex review of rc.5: "1:8192000" was read as a width)
            bool ratio = v.StartsWith("1:", StringComparison.Ordinal);
            if (ratio) v = v.Substring(2).Trim();
            bad = !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                || !(n >= MinScale && n <= MaxScale || !ratio && n >= 64_000 && n <= MaxWidth);
            if (bad) n = DefaultScale;
            length = n <= MaxScale ? LengthForScale(n) : LengthForWidth(n);
            width = 2 * length + 2 * SeamMargin;
        }

        /// <summary>The world's length at scale 1:<paramref name="scale"/> (whole map regions).</summary>
        public static int LengthForScale(int scale) => (int)Math.Round(EquatorMetres / (2.0 * scale) / 512) * 512;

        /// <summary>The length the size option gave a world width before 2.0.0-rc.5 (0.4 × the width in whole map regions);
        /// also the length the Earth reads into the World generation tab's width.</summary>
        public static int LengthForWidth(int width) => (int)Math.Round(width / 512 * 512 * 0.4 / 512) * 512;

        /// <summary>The scale denominator a world length stands for (Cylinder World's ring is twice it).</summary>
        public static double ScaleOf(int length) => EquatorMetres / (2.0 * Math.Max(512, length));
    }
}
