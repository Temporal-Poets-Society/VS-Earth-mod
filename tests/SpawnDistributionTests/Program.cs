using EarthMap.Core;
using System.Globalization;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
string root=Path.GetFullPath(args.Length>0?args[0]:".");
string mode=args.Length>1?args[1]:"before";
string output=Path.Combine(root,"tests/out/earth-playstyle");
Directory.CreateDirectory(output);
var data=EarthData.LoadFromFolder(root,true);
data.SetClimateFix(true); data.SetFineTemperature(true); data.SetFineRain(true);
data.SetIslandOverlay(true); data.SetLakeOverlay(true); data.SetShoreOverlay(true);
data.SetSummits(true); data.SetGlaciers(true); data.SetReservoirExclusions(true);
const int W=512000,L=204800;
var geo=EarthLayout.Build(new LayoutInput {WorldWidth=W,CwRingStart=W/10,CwRingLength=W*4/5,
    CwEquatorZ=L/2,CwPoleDistance=L/2-48,CwBandInner=608,Gutter=false},data,out _);
var vertical=new VerticalTerrainOptions().Build(512,110,.0625,200,20,.43);
var m=new TerrainModel(data,geo,vertical,12345) {ClimateRules=TerrainModel.RealRainRules,
    LocalAlpineEcology=true,AlpineSurfaceRules=MountainEcology.SurfaceVersion,Summits=true,SummitRules=KnownSummits.Version,
    Islands=new IslandLayer(data.Islands,geo,4),LakeShores=true,GutterSeabed=TerrainModel.RealSeamVersion,
    MinSeaDepth=TerrainModel.ShipSeaDepth};
// Hydrological overlays use the same creation settings; river carving is intentionally included in measurements.
var ends=RiverEnds.Prepare(data,data.Rivers);
var reaches=RiverDeltas.Apply(ends.Reaches).Reaches;
m.Rivers=new RiverNetwork(reaches,geo,100,1,m.RiverWaterTop,"ends",20,2,0.65,0.5,
    falls:RiverFalls.All,continueDownstream:true,fadeEnds:ends.Fade,geometrySeams:true,
    widthRules:RiverNetwork.WidthRulesVersion,poolLayout:RiverNetwork.PoolLayoutVersion);
var counts=new SortedDictionary<string,int>();
var stages=new SortedDictionary<string,long>();
string Region(double lat,double lon) => SpawnSearch.StartRegion(lat,lon);
string Hemi(double lat) => lat<0?"South":"North";
if(mode=="filters")
{
    var totals=new SortedDictionary<string,int>();
    void Hit(string climate,string region,string stage){string key=$"{climate},{region},{stage}";totals[key]=totals.GetValueOrDefault(key)+1;}
    var random=new Random(380038);
    for(int i=0;i<200000;i++)
    {
        double lat=Math.Asin(Math.Sin(-60*Math.PI/180)+(Math.Sin(80*Math.PI/180)-Math.Sin(-60*Math.PI/180))*random.NextDouble())*180/Math.PI;
        double lon=random.NextDouble()*360-180;
        string region=Region(lat,lon);
        m.ClimateAt(lat,lon,out double t,out _,out _);
        foreach(var band in SpawnSearch.ClimateBands)
        {
            Hit(band.climate,region,"area");if(t<band.min || t>band.max)continue;
            Hit(band.climate,region,"climate");geo.FromGeo(lat,lon,out double x,out double z);var c=m.GetColumn(x,z);
            if(c.Water!=TerrainModel.WaterNone || c.Top<110 || c.Top>170)continue;Hit(band.climate,region,"lowland");
            if(!LegacySpawnSearch.Spawnable(m,W,L,x,z,60))continue;Hit(band.climate,region,"gentleIceFree");
            int land=0;for(int k=0;k<8;k++){var d=m.GetColumn(x+256*Math.Cos(k*Math.PI/4),z+256*Math.Sin(k*Math.PI/4));if(d.Water==TerrainModel.WaterNone && !d.Gutter)land++;}
            if(land==8)Hit(band.climate,region,"oldInterior");
            if(SpawnSearch.RandomSpawnable(m,W,L,x,z))Hit(band.climate,region,"newCoast");
        }
    }
    File.WriteAllLines(Path.Combine(output,"spawn-filter-survey.csv"),new[]{"climate,region,stage,count"}.Concat(totals.Select(k=>$"{k.Key},{k.Value}")));
    double Correlation(double[] a,double[] b){double aa=a.Average(),bb=b.Average();return a.Zip(b,(x,y)=>(x-aa)*(y-bb)).Sum()/Math.Sqrt(a.Sum(x=>(x-aa)*(x-aa))*b.Sum(x=>(x-bb)*(x-bb)));}
    var first=Enumerable.Range(0,500).Select(s=>new Random(unchecked(s*31+7919)).NextDouble()).ToArray();
    Console.WriteLine($"Legacy first latitude draw lag-one correlation, sequential seeds 0..499: {Correlation(first[..^1],first[1..]):F6}");
    Console.WriteLine("200000-point filter survey complete.");return 0;
}
if(mode=="probe")
{
    var stagesProbe=new Dictionary<string,int>();
    void Hit(string s)=>stagesProbe[s]=stagesProbe.GetValueOrDefault(s)+1;
    using var probe=new StreamWriter(Path.Combine(output,"southern-cool-probe.csv"));
    probe.WriteLine("latitude,longitude,temperature,height,ice,gentle,coast");
    for(double la=-84.95;la<0;la+=.1)for(double lo=-179.95;lo<180;lo+=.1)
    {
        var source=data.SampleTerrain(la,lo);if(source.Ocean>=.5)continue;
        m.ClimateAt(la,lo,out double t,out _,out _);if(t < -5 || t > 1)continue;Hit("climateLand");
        geo.FromGeo(la,lo,out double x,out double z);var c=m.GetColumn(x,z);
        if(c.Water!=TerrainModel.WaterNone || c.Top<110 || c.Top>170)continue;Hit("lowland");
        bool ice=c.IceBottom<=c.Top;if(!ice)Hit("iceFree");
        bool gentle=SpawnSearch.Spawnable(m,W,L,x,z,60),coast=SpawnSearch.RandomSpawnable(m,W,L,x,z);
        if(gentle)
        {
            Hit("gentle");
            foreach(int radius in new[]{64,128,256,512})
            {
                int land=0;for(int k=0;k<8;k++){var d=m.GetColumn(x+radius*Math.Cos(k*Math.PI/4),z+radius*Math.Sin(k*Math.PI/4));if(d.Water==TerrainModel.WaterNone && !d.Gutter)land++;}
                Console.WriteLine($"cool candidate {la:F3},{lo:F3} radius {radius}: {land}/8 land");
            }
        }
        if(coast)Hit("coast");
        probe.WriteLine($"{la:F3},{lo:F3},{t:F3},{c.Top},{ice},{gentle},{coast}");
    }
    foreach(var kv in stagesProbe)Console.WriteLine($"{kv.Key}: {kv.Value}");return 0;
}
void Count(string key){counts[key]=counts.GetValueOrDefault(key)+1;}
string current="";
LegacySpawnSearch.Observe=(stage,lat,lon)=>{
    string key=$"{current},{Region(lat,lon)},{stage}";
    stages[key]=stages.GetValueOrDefault(key)+1;
};
var rng=new Random(380036);
int[] randomSeeds=Enumerable.Range(0,500).Select(_=>rng.Next(int.MinValue,int.MaxValue)).ToArray();
using var rows=new StreamWriter(Path.Combine(output,$"spawn-{mode}.csv"));
rows.WriteLine("climate,seedSet,seed,latitude,longitude,temperature,region,hemisphere,latitudeBand");
int failed=0;
foreach(var band in SpawnSearch.ClimateBands)
{
    current=band.climate;
    foreach(string set in new[]{"random","sequential"})
    foreach(int seed in set=="random"?randomSeeds:Enumerable.Range(0,500))
    {
        double lat,lon,t;
        bool ok=mode=="before"?LegacySpawnSearch.FindClimatePlace(m,W,L,seed,current,out lat,out lon,out t)
            :SpawnSearch.FindClimatePlace(m,W,L,seed,current,out lat,out lon,out t);
        if(!ok){failed++;continue;}
        string region=Region(lat,lon),hemisphere=Hemi(lat),latitudeBand=$"{Math.Floor(lat/15)*15:0} to {Math.Floor(lat/15)*15+15:0}";
        rows.WriteLine($"{current},{set},{seed},{lat:F5},{lon:F5},{t:F3},{region},{hemisphere},{latitudeBand}");
        Count($"{current},{set},region,{region}"); Count($"{current},{set},hemisphere,{hemisphere}");Count($"{current},{set},latitude,{latitudeBand}");
        if(t<band.min || t>band.max)throw new Exception("Climate band violated");
        if(mode=="after" && seed==0)
        {
            bool again=SpawnSearch.FindClimatePlace(m,W,L,seed,current,out double lat2,out double lon2,out double t2);
            if(!again || lat!=lat2 || lon!=lon2 || t!=t2)throw new Exception("Seed is not deterministic");
            geo.FromGeo(lat,lon,out double x,out double z);
            if(!SpawnSearch.RandomSpawnable(m,W,L,x,z))throw new Exception("Unsafe start");
        }
    }
    Console.WriteLine($"{mode} {current}: done");
}
rows.Dispose();
File.WriteAllLines(Path.Combine(output,$"spawn-{mode}-summary.csv"),new[]{"climate,seedSet,dimension,value,count"}.Concat(counts.Select(k=>$"{k.Key},{k.Value}")));
File.WriteAllLines(Path.Combine(output,$"spawn-{mode}-filters.csv"),new[]{"climate,region,stage,count"}.Concat(stages.Select(k=>$"{k.Key},{k.Value}")));
Console.WriteLine($"{mode}: {failed} searches failed; reports in {output}");
if(mode=="after")
{
    foreach(var band in SpawnSearch.ClimateBands)
    foreach(string set in new[]{"random","sequential"})
    {
        foreach(string hemisphere in new[]{"North","South"})
            if(counts.GetValueOrDefault($"{band.climate},{set},hemisphere,{hemisphere}")<20)
                throw new Exception($"Distribution collapsed: {band.climate}/{set}/{hemisphere}");
        var regions=counts.Where(k=>k.Key.StartsWith($"{band.climate},{set},region,")).ToArray();
        if(regions.Length<4 || regions.Any(k=>k.Value<20 || k.Value>200))
            throw new Exception($"Region distribution collapsed: {band.climate}/{set}");
    }
    foreach(string set in new[]{"random","sequential"})
    foreach(string region in new[]{"Eastern North America","Western North America","South America","Western Europe / Mediterranean","Eastern Europe","East Asia","Australia / New Zealand"})
        if(counts.GetValueOrDefault($"temperate,{set},region,{region}")<30)
            throw new Exception("Temperate region disappeared: "+region);
    var all=File.ReadAllLines(Path.Combine(output,"spawn-after.csv")).Skip(1).Select(l=>l.Split(',')).ToArray();
    foreach(string set in new[]{"random","sequential"})
    {
        var temperate=all.Where(r=>r[0]=="temperate" && r[1]==set).ToArray();
        int west=temperate.Count(r=>double.Parse(r[3])>40 && double.Parse(r[4])>=-12 && double.Parse(r[4])<12);
        int eastNA=temperate.Count(r=>double.Parse(r[3])>25 && double.Parse(r[3])<55 && double.Parse(r[4])>-95 && double.Parse(r[4])<-55);
        if(west<15 || eastNA<15)throw new Exception("Western Europe or eastern North America disappeared");
    }
    Console.WriteLine("Distribution regression, both hemispheres, regional coverage and determinism passed.");
}
return failed==0?0:1;
