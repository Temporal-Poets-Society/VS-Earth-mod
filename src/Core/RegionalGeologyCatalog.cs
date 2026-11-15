using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EarthMap.Core
{
    /// <summary>
    /// Immutable offline geology. Source classes are categorical; layers are explicit game-scale hypotheses.
    /// One instance serves one rules version: v1 (0.12 saves, frozen bit-for-bit) or v2 (new worlds).
    /// </summary>
    public sealed class RegionalGeologyCatalog
    {
        /// <summary>Rules version stamped into new worlds.</summary>
        public const int RulesVersion = 2;
        /// <summary>Oldest rules version this code still reproduces exactly.</summary>
        public const int OldestRulesVersion = 1;
        public static bool IsSupportedRulesVersion(int version) => version >= OldestRulesVersion && version <= RulesVersion;
        /// <summary>Shared categorical GLiM/GUM grid (unchanged since v1; v2 derives its coastal fill at load).</summary>
        public const string GridAsset = "earth:config/geology-grids-v1.json";
        public static string RulesAsset(int version) => "earth:config/geology-rules-v" + version.ToString(CultureInfo.InvariantCulture) + ".json";
        public const int MaxBeds = 16;

        public sealed class GridFile
        {
            public int Version { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public double CellSize { get; set; }
            public double West { get; set; }
            public double North { get; set; }
            public byte[] Lithology { get; set; }
            public byte[] Sediment { get; set; }
        }
        public sealed class Material
        {
            public string Id { get; set; }
            public string Vanilla { get; set; }
            public string Provider { get; set; }
            public string Asset { get; set; }
            public string Note { get; set; }
        }
        public sealed class Bed
        {
            public string Material { get; set; }
            public double Fraction { get; set; }
        }
        public sealed class Profile
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public Bed[] Beds { get; set; }
            public string Basement { get; set; }
            /// <summary>Material index (into <see cref="Materials"/>) of each bed; resolved at load.</summary>
            [JsonIgnore] public int[] BedIndex { get; internal set; }
            [JsonIgnore] public int BasementIndex { get; internal set; }
        }
        public sealed class Province
        {
            public string Id { get; set; }
            public string Profile { get; set; }
            public string Source { get; set; }
            public string Note { get; set; }
            public double[][] Polygon { get; set; }
            /// <summary>v2: GLiM classes the province may override (null: any, as in v1).</summary>
            public int[] Lithology { get; set; }
        }
        /// <summary>v2 generation parameters (absent in v1 files).</summary>
        public sealed class GenerationSettings
        {
            /// <summary>No-data GLiM classes (ocean 0, water 11, no data 15, ice 16) filled from the nearest mapped cell within this radius.</summary>
            public double FillRadiusCells { get; set; } = 2;
            public int[] FillLithology { get; set; } = { 0, 11, 15, 16 };
            /// <summary>Jitter of the Voronoi seeds that replace the square 0.5-degree cells (cell units, below 0.5).</summary>
            public double VoronoiJitter { get; set; } = 0.45;
            public double WarpLowWavelengthCells { get; set; } = 0.9;
            public double WarpLowAmplitudeCells { get; set; } = 0.35;
            /// <summary>Stratigraphic thickness scale in blocks per block of sea-level height (world-height independent).</summary>
            public double StrataThicknessPerSeaLevel { get; set; } = 0.6;
            /// <summary>Radius of the low-passed reference surface the bed boundaries follow.</summary>
            public double DatumRadiusCells { get; set; } = 0.6;
            /// <summary>Gentle regional dip/undulation of the beds as a share of the thickness scale.</summary>
            public double DatumUndulation { get; set; } = 0.3;
            public double ThicknessVariation { get; set; } = 0.15;
            /// <summary>Share of coarse-sediment ground (GUM class) carrying a gravel lens.</summary>
            public Dictionary<string, double> GravelCoverage { get; set; } = new() { ["2"] = 0.5, ["15"] = 0.45, ["19"] = 0.25 };
        }
        public sealed class RuleFile
        {
            public int Version { get; set; }
            public Material[] Materials { get; set; }
            public Profile[] Profiles { get; set; }
            public Dictionary<string,string> Classes { get; set; }
            public Province[] Provinces { get; set; }
            public SaltSite[] SaltSites { get; set; }
            /// <summary>Block code paths the strata pass never replaces (v1: bauxite only).</summary>
            public string[] Preserve { get; set; }
            public GenerationSettings Generation { get; set; }
        }
        public sealed class SaltSite
        {
            public string Id { get; set; }
            public double Lat { get; set; }
            public double Lon { get; set; }
            public double RadiusKm { get; set; }
            public string Source { get; set; }
            public string Note { get; set; }
        }
        public readonly struct Sample
        {
            public readonly byte Lithology, Sediment;
            public readonly Profile Profile;
            public readonly string Province, Source;
            public readonly bool Known, OutsideEarth;
            /// <summary>v2: category borrowed from the nearest mapped cell (coastal/water/ice no-data cell).</summary>
            public readonly bool Filled;
            public Sample(byte rock, byte cover, Profile profile, string province, string source, bool outside=false, bool filled=false)
            { Lithology=rock; Sediment=cover; Profile=profile; Province=province; Source=source;
              Known=rock>0 && rock!=11 && rock!=15 && rock!=16; OutsideEarth=outside; Filled=filled; }
            public bool MappedPeat => Sediment==24;
            public bool MappedSalt => Sediment==28;
            public bool FineSediment => Sediment is 3 or 10 or 12 or 13 or 16 or 22 or 23 or 24 or 34 or 35 or 36 or 37;
            public bool Alluvium => Sediment is >=1 and <=8;
            public bool Glacial => Sediment is >=15 and <=20;
            public bool Cover => Sediment>0 && Sediment is not (9 or 31 or 32 or 39);
            public bool Sedimentary => Lithology is 1 or 3 or 5 or 6 or 14;
            public bool HasRegionalBedrock => !OutsideEarth && Profile.Id is not ("unknown" or "covered");
        }
        /// <summary>Physical setting of a deposit attempt. Relief is measured in real metres so it does not tighten with world height.</summary>
        public readonly struct Site
        {
            public readonly bool Marine, Saline, Ice, Underwater, NearFreshWater;
            /// <summary>Temperature (°C, true surface), precipitation (mm/yr), relief (blocks, diagnostic only).</summary>
            public readonly double Temperature, Precipitation, Relief;
            /// <summary>Relief (m) of the water/ground surface across the sampled footprint, and that footprint's span (km).</summary>
            public readonly double ReliefMetres, SpanKm;
            /// <summary>0..1 moisture index from precipitation versus potential evaporation (Vintage Story rainfall / 255).</summary>
            public readonly double Wetness;
            public double Slope => SpanKm > 0 ? ReliefMetres / SpanKm : 0;
            /// <summary>Legacy fixture constructor: relief in blocks (~4 m each near sea level over a ~2.4 km footprint), wetness from precipitation.</summary>
            public Site(bool marine, bool saline, bool ice, bool underwater, bool nearFreshWater, double temperature, double precipitation, double relief)
                : this(marine, saline, ice, underwater, nearFreshWater, temperature, precipitation, relief, relief * 4, 2.4, Math.Clamp(precipitation / 900.0, 0, 1)) { }
            public Site(bool marine, bool saline, bool ice, bool underwater, bool nearFreshWater, double temperature, double precipitation, double relief,
                double reliefMetres, double spanKm, double wetness)
            { Marine=marine; Saline=saline; Ice=ice; Underwater=underwater; NearFreshWater=nearFreshWater;
              Temperature=temperature; Precipitation=precipitation; Relief=relief; ReliefMetres=reliefMetres; SpanKm=spanKm; Wetness=wetness; }
        }
        public enum SaltPermission { Deny, Native, Favoured }

        readonly GridFile grid;
        readonly byte[] lithology, sediment;   // v2: coastal/no-data fill applied; v1: the grid arrays
        readonly bool[] filled;
        readonly Dictionary<string,Profile> profiles;
        readonly Profile[] classProfile = new Profile[256];
        readonly Province[] provinces;
        readonly SaltSite[] saltSites;
        readonly Profile unknown;
        public int Version { get; }
        public IReadOnlyList<Material> Materials { get; }
        public IReadOnlyList<Province> Provinces => provinces;
        public IReadOnlyList<SaltSite> SaltSites => saltSites;
        public IReadOnlyList<string> Preserve { get; }
        public GenerationSettings Generation { get; }
        /// <summary>SHA-256 of generation-relevant content only (grid bytes, parsed rules; not notes, URLs, labels or whitespace).</summary>
        public string Fingerprint { get; }
        public int MaterialIndex(string id) { for (int i = 0; i < Materials.Count; i++) if (Materials[i].Id == id) return i; return -1; }
        // for the tests: profiles by id, and all of them
        public Profile ProfileById(string id)=>profiles.TryGetValue(id??"",out var p)?p:null;
        public IEnumerable<Profile> AllProfiles=>profiles.Values;
        static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true};

        public RegionalGeologyCatalog(string gridJson,string rulesJson)
        {
            RuleFile rules;
            try
            {
                grid=JsonSerializer.Deserialize<GridFile>(gridJson,Options);
                rules=JsonSerializer.Deserialize<RuleFile>(rulesJson,Options);
            }
            catch(Exception e) when (e is JsonException or FormatException or NotSupportedException)
            {throw new ArgumentException("Unreadable regional geology dataset: "+e.Message,e);}
            if(grid==null || rules==null || grid.Version!=1 || !IsSupportedRulesVersion(rules.Version) || grid.Width!=720 || grid.Height!=360 || grid.CellSize!=.5 || grid.West!=-180 || grid.North!=90 || grid.Lithology?.Length!=259200 || grid.Sediment?.Length!=259200)
                throw new ArgumentException("Unsupported or incomplete regional geology dataset");
            Version=rules.Version;
            Materials=rules.Materials ?? throw new ArgumentException("No geology material mappings");
            if(Materials.Any(m=>m==null||string.IsNullOrWhiteSpace(m.Id)||string.IsNullOrWhiteSpace(m.Vanilla)))throw new ArgumentException("Incomplete geology material mapping");
            var ids=new HashSet<string>(Materials.Select(m=>m.Id));
            if(ids.Count!=Materials.Count) throw new ArgumentException("Duplicate geological material");
            if(rules.Profiles==null || rules.Classes==null)throw new ArgumentException("No geology profiles/classes");
            profiles=rules.Profiles.ToDictionary(p=>p.Id,StringComparer.Ordinal);
            foreach(var p in profiles.Values)
            {
                if(!ids.Contains(p.Basement) || p.Beds==null || p.Beds.Length>MaxBeds || p.Beds.Any(b=>b==null||!ids.Contains(b.Material)||!(b.Fraction>0)) || p.Beds.Sum(b=>b.Fraction)>.95)
                    throw new ArgumentException("Invalid geological profile: "+p.Id);
                p.BedIndex=p.Beds.Select(b=>MaterialIndex(b.Material)).ToArray();
                p.BasementIndex=MaterialIndex(p.Basement);
            }
            var classes=rules.Classes.ToDictionary(p=>byte.Parse(p.Key,CultureInfo.InvariantCulture),p=>p.Value);
            provinces=rules.Provinces ?? Array.Empty<Province>();
            saltSites=rules.SaltSites ?? Array.Empty<SaltSite>();
            if(saltSites.Any(s=>s==null || s.RadiusKm<=0 || s.RadiusKm>10 || Math.Abs(s.Lat)>90 || Math.Abs(s.Lon)>180 || string.IsNullOrWhiteSpace(s.Source)))
                throw new ArgumentException("Invalid salt occurrence kernel");
            if(!profiles.TryGetValue("unknown",out unknown) || classes.Values.Any(p=>!profiles.ContainsKey(p)))
                throw new ArgumentException("Unknown profile or unreferenced province");
            foreach(var p in provinces)
                if(p==null || !profiles.ContainsKey(p.Profile ?? "") || string.IsNullOrWhiteSpace(p.Source) || p.Polygon==null || p.Polygon.Length<3
                   || p.Polygon.Any(v=>v==null||v.Length<2||!double.IsFinite(v[0])||!double.IsFinite(v[1])||Math.Abs(v[1])>90||Math.Abs(v[0])>360))
                    throw new ArgumentException("Invalid geological province: "+(p?.Id??"(null)"));
            foreach(var kv in classes)classProfile[kv.Key]=profiles[kv.Value];
            Preserve=rules.Preserve ?? new[]{"rock-bauxite"};
            if(GeologyFacts.GenerationBlock(Version))
            {
                Generation=rules.Generation ?? new GenerationSettings();
                var gs=Generation;
                if(!(gs.FillRadiusCells>=0 && gs.FillRadiusCells<=6) || !(gs.VoronoiJitter>=0 && gs.VoronoiJitter<.5) || !(gs.WarpLowWavelengthCells>0) || !(gs.WarpLowAmplitudeCells>=0 && gs.WarpLowAmplitudeCells<=1)
                   || !(gs.StrataThicknessPerSeaLevel>0 && gs.StrataThicknessPerSeaLevel<=2) || !(gs.DatumRadiusCells>0 && gs.DatumRadiusCells<=3) || !(Math.Abs(gs.DatumUndulation)<=1) || !(gs.ThicknessVariation>=0 && gs.ThicknessVariation<.9))
                    throw new ArgumentException("Invalid v2 geology generation settings");
                (lithology,sediment,filled)=Fill(grid.Lithology,grid.Sediment,gs.FillRadiusCells,gs.FillLithology??Array.Empty<int>());
                foreach(var s in gs.GravelCoverage?.Keys ?? Enumerable.Empty<string>())
                    if(!byte.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out byte b)||b>=gravelCoverage.Length)throw new ArgumentException("Invalid gravel coverage class");
                    else gravelCoverage[b]=Math.Clamp(gs.GravelCoverage[s],0,1);
            }
            else {lithology=grid.Lithology;sediment=grid.Sediment;filled=null;}
            Fingerprint=ComputeFingerprint(rules);
        }
        readonly double[] gravelCoverage=new double[64];
        /// <summary>v2: share of ground carrying a discontinuous gravel lens for a GUM class (0 for others).</summary>
        public double GravelCoverage(byte sedimentClass)=>sedimentClass<gravelCoverage.Length?gravelCoverage[sedimentClass]:0;

        static (byte[],byte[],bool[]) Fill(byte[] lith,byte[] sed,double radius,int[] fillCodes)
        {
            var l=(byte[])lith.Clone();var s=(byte[])sed.Clone();var f=new bool[l.Length];
            var fill=new bool[256];foreach(int c in fillCodes)if(c>=0&&c<256)fill[c]=true;
            int r=(int)Math.Floor(radius);
            // nearest first, deterministic tie order
            var offsets=new List<(int dx,int dz,double d)>();
            for(int dz=-r;dz<=r;dz++)for(int dx=-r;dx<=r;dx++){double d=Math.Sqrt(dx*dx+dz*dz);if(d>0&&d<=radius+1e-9)offsets.Add((dx,dz,d));}
            offsets.Sort((a,b)=>a.d!=b.d?a.d.CompareTo(b.d):a.dz!=b.dz?a.dz.CompareTo(b.dz):a.dx.CompareTo(b.dx));
            for(int z=0;z<360;z++)for(int x=0;x<720;x++)
            {
                int i=z*720+x;
                if(!fill[lith[i]])continue;
                foreach(var o in offsets)
                {
                    int zz=z+o.dz;if(zz<0||zz>=360)continue;
                    int j=zz*720+((x+o.dx)%720+720)%720;
                    if(fill[lith[j]])continue;
                    l[i]=lith[j];f[i]=true;
                    if(sed[i]==0)s[i]=sed[j];
                    break;
                }
            }
            return (l,s,f);
        }

        string ComputeFingerprint(RuleFile rules)
        {
            var sb=new StringBuilder();
            string N(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
            sb.Append("v=").Append(rules.Version).Append('\n');
            sb.Append("lith=").Append(Convert.ToHexString(SHA256.HashData(grid.Lithology))).Append('\n');
            sb.Append("sed=").Append(Convert.ToHexString(SHA256.HashData(grid.Sediment))).Append('\n');
            foreach(var m in Materials)sb.Append("m=").Append(m.Id).Append('|').Append(m.Vanilla).Append('|').Append(m.Provider).Append('|').Append(m.Asset).Append('\n');
            foreach(var p in profiles.Values.OrderBy(p=>p.Id,StringComparer.Ordinal))
            {
                sb.Append("p=").Append(p.Id).Append('|').Append(p.Basement);
                foreach(var b in p.Beds)sb.Append('|').Append(b.Material).Append(':').Append(N(b.Fraction));
                sb.Append('\n');
            }
            foreach(var kv in rules.Classes.OrderBy(k=>k.Key,StringComparer.Ordinal))sb.Append("c=").Append(kv.Key).Append(':').Append(kv.Value).Append('\n');
            foreach(var p in provinces)
            {
                sb.Append("v=").Append(p.Id).Append('|').Append(p.Profile).Append('|');
                foreach(var v in p.Polygon)sb.Append(N(v[0])).Append(',').Append(N(v[1])).Append(';');
                if(p.Lithology!=null)sb.Append('|').Append(string.Join(",",p.Lithology));
                sb.Append('\n');
            }
            foreach(var s in saltSites)sb.Append("s=").Append(s.Id).Append('|').Append(N(s.Lat)).Append('|').Append(N(s.Lon)).Append('|').Append(N(s.RadiusKm)).Append('\n');
            sb.Append("keep=").Append(string.Join(",",Preserve)).Append('\n');
            if(Generation!=null)
            {
                var g=Generation;
                sb.Append("g=").Append(N(g.FillRadiusCells)).Append('|').Append(string.Join(",",g.FillLithology??Array.Empty<int>())).Append('|').Append(N(g.VoronoiJitter)).Append('|')
                  .Append(N(g.WarpLowWavelengthCells)).Append('|').Append(N(g.WarpLowAmplitudeCells)).Append('|').Append(N(g.StrataThicknessPerSeaLevel)).Append('|')
                  .Append(N(g.DatumRadiusCells)).Append('|').Append(N(g.DatumUndulation)).Append('|').Append(N(g.ThicknessVariation)).Append('|');
                foreach(var kv in (g.GravelCoverage??new()).OrderBy(k=>k.Key,StringComparer.Ordinal))sb.Append(kv.Key).Append(':').Append(N(kv.Value)).Append(',');
                sb.Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public Sample At(double lat,double lon,bool outsideEarth=false)
        {
            if(outsideEarth || !double.IsFinite(lat) || !double.IsFinite(lon) || lat<-90 || lat>90)
                return new Sample(0,0,unknown,null,"Outside mapped Earth",true);
            lon=Wrap(lon);
            int x,z;
            if(GeologyFacts.CellContacts(Version))VoronoiCell(lat,lon,out x,out z);
            else
            {
                x=Math.Clamp((int)Math.Floor((lon+180)*2),0,719);
                z=Math.Clamp((int)Math.Floor((90-lat)*2),0,359);
            }
            int i=z*720+x;
            byte rock=lithology[i],cover=sediment[i];
            bool borrowed=filled!=null && filled[i];
            foreach(var p in provinces)
                if((p.Lithology==null || Array.IndexOf(p.Lithology,(int)rock)>=0) && Contains(p.Polygon,lon,lat))
                    return new Sample(rock,cover,profiles[p.Profile],p.Id,p.Source,false,borrowed);
            var profile=classProfile[rock]??unknown;
            return new Sample(rock,cover,profile,null,borrowed?"GLiM/GUM 0.5 degree, nearest mapped cell (coast/no-data fill); subsurface synthesized":"GLiM/GUM 0.5 degree; subsurface synthesized",false,borrowed);
        }

        /// <summary>
        /// v2: the cell owning a point is the nearest jittered seed (one per 0.5-degree cell), so category contacts run
        /// along irregular polygon edges instead of the source grid lines. Every value still comes from a real cell.
        /// </summary>
        void VoronoiCell(double lat,double lon,out int cellX,out int cellZ)
        {
            double u=(lon+180)*2,v=(90-lat)*2;
            int ix=(int)Math.Floor(u),iz=Math.Clamp((int)Math.Floor(v),0,359);
            double jitter=Generation.VoronoiJitter,best=double.MaxValue;
            cellX=Math.Clamp(ix,0,719);cellZ=iz;
            for(int dz=-2;dz<=2;dz++)
            {
                int cz=iz+dz;if(cz<0||cz>=360)continue;
                for(int dx=-2;dx<=2;dx++)
                {
                    int cx=ix+dx,wx=(cx%720+720)%720;
                    double sx=cx+.5+jitter*(2*GeologyTransitions.Hash(0,wx,cz,0,6151)-1);
                    double sz=cz+.5+jitter*(2*GeologyTransitions.Hash(0,wx,cz,0,6373)-1);
                    double d=(sx-u)*(sx-u)+(sz-v)*(sz-v);
                    if(d<best){best=d;cellX=wx;cellZ=cz;}
                }
            }
        }

        /// <summary>v1 game-scale depth, not a borehole prediction. Coherent broad folding avoids per-block random strata. Frozen for v1 saves.
        /// Generation uses its per-column form (<see cref="ColumnV1"/>, <see cref="BedV1"/>); this stays as the tests' oracle.</summary>
        public string RockAt(Sample sample,int depth,int rockHeight,double lat,double lon,long seed)
        {
            var p=sample.Profile;
            double phase=seed%997/997.0;
            double fold=Math.Sin(lon*.7+phase)*Math.Cos(lat*.9-phase)*.12;
            double scale=Math.Max(24,rockHeight)*(1+fold);
            int boundary=0;
            foreach(var bed in p.Beds)
            {
                boundary+=Math.Max(2,(int)Math.Round(bed.Fraction*scale));
                if(depth<boundary)return bed.Material;
            }
            return p.Basement;
        }

        /// <summary>
        /// v1 per-column form of <see cref="RockAt"/>: cumulative depth boundaries (bed i holds depths below boundary i).
        /// Identical results, one sin/cos per column instead of per block.
        /// </summary>
        public static int ColumnV1(Profile p,int rockHeight,double lat,double lon,long seed,Span<int> depthBoundaries)
        {
            double phase=seed%997/997.0;
            double fold=Math.Sin(lon*.7+phase)*Math.Cos(lat*.9-phase)*.12;
            double scale=Math.Max(24,rockHeight)*(1+fold);
            int boundary=0,n=p.Beds.Length;
            for(int i=0;i<n;i++)
            {
                boundary+=Math.Max(2,(int)Math.Round(p.Beds[i].Fraction*scale));
                depthBoundaries[i]=boundary;
            }
            return n;
        }

        /// <summary>
        /// v2 bed boundaries at absolute heights below a smoothed regional reference surface (<paramref name="datumY"/>):
        /// bed i holds blocks with y above boundary i (and not above boundary i-1); valleys and cliffs cut through
        /// lower beds and thickness does not follow local column height. Below the last boundary lies the basement.
        /// </summary>
        public static int ColumnV2(Profile p,double datumY,double thickness,Span<int> yBoundaries)
        {
            double cumulative=0;int n=p.Beds.Length;
            for(int i=0;i<n;i++)
            {
                cumulative+=Math.Max(2,p.Beds[i].Fraction*thickness);
                yBoundaries[i]=(int)Math.Floor(datumY-cumulative);
            }
            return n;
        }
        /// <summary>v1 bed for a depth (boundaries from <see cref="ColumnV1"/>), searching forward from <paramref name="bed"/> as depth only grows down a column.</summary>
        public static int BedV1(int depth,ReadOnlySpan<int> depthBoundaries,int beds,int bed)
        {
            while(bed<beds && depth>=depthBoundaries[bed])bed++;
            return bed;
        }
        /// <summary>v2 bed for a block height (boundaries from <see cref="ColumnV2"/>), searching forward as y decreases.</summary>
        public static int BedV2(int y,ReadOnlySpan<int> yBoundaries,int beds,int bed)
        {
            while(bed<beds && y<=yBoundaries[bed])bed++;
            return bed;
        }
        /// <summary>Material index of a bed number (the basement below the last bed).</summary>
        public static int MaterialOfBed(Profile p,int bed)=>bed<p.BedIndex.Length?p.BedIndex[bed]:p.BasementIndex;
        /// <summary>v2 material index at a block height, from <see cref="ColumnV2"/>. With <see cref="RockAtV2"/> the tests'
        /// oracle for <see cref="BedV2"/>, which generation uses.</summary>
        public static int MaterialIndexV2(Profile p,int y,ReadOnlySpan<int> yBoundaries)
        {
            for(int i=0;i<p.Beds.Length;i++)if(y>yBoundaries[i])return p.BedIndex[i];
            return p.BasementIndex;
        }
        public string RockAtV2(Sample s,int y,double datumY,double thickness)
        {
            Span<int> b=stackalloc int[MaxBeds];
            ColumnV2(s.Profile,datumY,thickness,b);
            return Materials[MaterialIndexV2(s.Profile,y,b)].Id;
        }

        // ------------------------------------------------------------------ surface resources (all rules versions)

        static double Ramp(double v,double lo,double hi)=>v<=lo?0:v>=hi?1:(v-lo)/(hi-lo);

        /// <summary>
        /// Ordinary clay frequency multiplier (accept probability = min(1, factor)). Never denies on rock type alone:
        /// weathered basalt and crystalline rock form clay-rich soils. Fine sediments, alluvium, glacial clays and
        /// fresh water favour clay; coarse sand/gravel and steep ground reduce it. Physical exclusions: sea, salt lake,
        /// glacier ice, outside Earth.
        /// </summary>
        public static double ClayFactor(Sample s,Site site)
        {
            if(s.OutsideEarth || site.Marine || site.Saline || site.Ice)return 0;
            double f;
            if(s.Sediment is 16 or 22 or 23 or 36 or 37)f=1.3;                 // glaciolacustrine, lacustrine, marine and volcanic-lake clays
            else if(s.FineSediment || s.Sediment is 19 or 31 or 32)f=1.2;   // fine cover, till matrix, laterite/residual saprolite
            else if(s.Alluvium && s.Sediment!=2)f=1.2;
            else if(s.Sediment is 2 or 11 or 15)f=.65;                      // fans, dune sand, glaciofluvial gravel
            else if(s.Lithology is 2 or 4)f=1.1;                            // mafic rock weathers to smectite-rich soils (Deccan)
            else if(s.Sedimentary)f=1.05;
            else f=.95;                                                     // crystalline, volcanic or unknown
            if(site.NearFreshWater)f*=1.2;
            // gentle ground keeps its weathering mantle; steep slopes thin it
            f*=1-.6*Ramp(site.Slope,40,320);
            if(site.Underwater)f*=.85;
            return f;
        }
        public static bool AllowsClay(Sample s,Site site)=>ClayFactor(s,site)>0;   // tests only, as AllowsPeat and AllowsSalt

        /// <summary>
        /// Peat frequency multiplier. Mapped peat bypasses the climate wetness rule; elsewhere a humid moisture index,
        /// gentle relief and a waterlogging-prone substrate are needed. Boreal/subarctic permafrost peat is allowed to
        /// about -12 °C mean temperature and tropical peat has no upper temperature limit.
        /// </summary>
        public static double PeatFactor(Sample s,Site site)
        {
            if(s.OutsideEarth || site.Marine || site.Saline || site.Ice || site.Underwater)return 0;
            bool mapped=s.MappedPeat;
            // permafrost peat plateaus and polygon mires: mapped peat is accepted in colder ground than inferred peat
            double t=mapped?Ramp(site.Temperature,-17,-9):Ramp(site.Temperature,-12.5,-5);
            if(t<=0)return 0;
            // Wetness is precipitation against potential evaporation (VS rainfall/255: deserts ~.25, humid temperate ~.5,
            // rainforest ~.78), so cold low-precipitation lowlands still count as wet. Mapped peat bypasses it.
            double w=site.Wetness;
            double wet=mapped?Math.Max(.75,Ramp(w,.3,.45)):Ramp(w,.36,.5);
            if(wet<=0)return 0;
            // hyper-oceanic climates carry blanket bog onto moderate slopes (Ireland, Scotland, Norway)
            double maxSlope=mapped?160:60+140*Ramp(w,.56,.7);
            double relief=1-Ramp(site.Slope,12,maxSlope);
            if(relief<=0)return 0;
            double substrate=mapped?1:s.Sediment is 16 or 22 or 23 or 36 or 37?.9:s.Glacial||s.FineSediment||s.Alluvium?.75:.5;
            if(site.NearFreshWater)substrate=Math.Max(substrate,.85);
            // low evaporation and frozen subsoil favour waterlogging (boreal paludification)
            if(!mapped && site.Temperature<6)substrate=Math.Min(1,substrate*1.3);
            return t*wet*relief*substrate;
        }
        public static bool AllowsPeat(Sample s,Site site)=>PeatFactor(s,site)>0;

        /// <summary>Unknown salt is not absence: native attempts stay possible on Earth ground outside ice.</summary>
        public static bool AllowsSalt(Sample s,Site site,bool permissiveEvaporiteBasin)=>SaltDecision(s,site,permissiveEvaporiteBasin,false)!=SaltPermission.Deny;
        /// <summary>
        /// Native: run the native generator with its own host/chance/climate checks. Favoured: positive surface salt
        /// evidence (GUM salt cell or a reviewed occurrence kernel) lets an exposed surface-bed attempt skip the native
        /// warm-desert climate gate (cold salt flats such as the Altiplano). Missing evidence never denies.
        /// </summary>
        public static SaltPermission SaltDecision(Sample s,Site site,bool positiveEvidence,bool surfaceBed)
        {
            if(s.OutsideEarth || site.Ice)return SaltPermission.Deny;
            if(surfaceBed && (site.Marine || site.Underwater))return SaltPermission.Deny;
            return surfaceBed && positiveEvidence ? SaltPermission.Favoured : SaltPermission.Native;
        }
        /// <summary>Keep native map values unchanged on Earth, including unmapped ground.</summary>
        public static int SaltMapValue(int nativeValue,bool outsideEarth)=>outsideEarth?0:nativeValue;
        public SaltSite SaltOccurrence(double lat,double lon)
        {
            if(!double.IsFinite(lat)||!double.IsFinite(lon)||Math.Abs(lat)>90)return null;
            foreach(var s in saltSites)
            {
                double dy=(lat-s.Lat)*111.195;
                double dx=Wrap(lon-s.Lon)*111.195*Math.Cos((lat+s.Lat)*Math.PI/360);
                if(dx*dx+dy*dy<=s.RadiusKm*s.RadiusKm)return s;
            }
            return null;
        }
        /// <summary>Diagnostic positive-evidence intersection only (the tests); false means unknown, never salt absence.</summary>
        public bool SaltEvidenceIntersects(double lat,double lon,double halfLat,double halfLon)
        {
            if(!double.IsFinite(lat)||!double.IsFinite(lon)||!double.IsFinite(halfLat)||!double.IsFinite(halfLon)||Math.Abs(lat)>90||halfLat<0||halfLon<0)return false;
            halfLat=Math.Min(90,halfLat);halfLon=Math.Min(180,halfLon);lon=Wrap(lon);
            int north=Math.Clamp((int)Math.Floor((90-(lat+halfLat))*2),0,359);
            int south=Math.Clamp((int)Math.Floor((90-(lat-halfLat))*2),0,359);
            int west=(int)Math.Floor((lon-halfLon+180)*2),east=(int)Math.Floor((lon+halfLon+180)*2);
            for(int y=north;y<=south;y++)for(int x=west;x<=east;x++)
                if(grid.Sediment[y*720+((x%720+720)%720)]==28)return true;
            foreach(var s in saltSites)
            {
                double dy=Math.Max(0,Math.Abs(lat-s.Lat)-halfLat)*111.195;
                double dx=Math.Max(0,Math.Abs(Wrap(lon-s.Lon))-halfLon)*111.195*Math.Cos((lat+s.Lat)*Math.PI/360);
                if(dx*dx+dy*dy<=s.RadiusKm*s.RadiusKm)return true;
            }
            return false;
        }
        public static string ClayInterpretation(Sample s,Site site)
        {
            double f=ClayFactor(s,site);
            if(f<=0)return "no ordinary clay here (sea, salt lake, ice or outside Earth)";
            string kind=s.FineSediment || site.NearFreshWater ? "ordinary clay; reducing/waterlogged colour possible" : "ordinary weathered/sedimentary clay";
            return string.Format(CultureInfo.InvariantCulture,"{0}; frequency x{1:0.00}; refractory grade unproven",kind,Math.Min(1,f));
        }
        public static bool Contains(double[][] ring,double x,double y)
        {
            bool inside=false;
            for(int i=0,j=ring.Length-1;i<ring.Length;j=i++)
            {
                var a=ring[i];var b=ring[j];
                if((a[1]>y)!=(b[1]>y) && x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0])inside=!inside;
            }
            return inside;
        }
        static double Wrap(double lon)=>((lon+180)%360+360)%360-180;
    }
}
