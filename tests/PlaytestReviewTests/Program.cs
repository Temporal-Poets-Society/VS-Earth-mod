using EarthMap.Core;
using System.Globalization;
using System.Text;
using System.Text.Json;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
string root=Path.GetFullPath(args.Length>0?args[0]:".");
string output=Path.Combine(root,"tests/out/playtest-issues/review");Directory.CreateDirectory(output);
var data=EarthData.LoadFromFolder(root,true);
data.SetClimateFix(true);data.SetFineTemperature(true);data.SetFineRain(true);data.SetSummits(true);
data.SetIslandOverlay(true);data.SetLakeOverlay(true);data.SetShoreOverlay(true);
var reaches=data.RiverReaches();
using(var csv=new StreamWriter(Path.Combine(output,"arid-reaches.csv")))
{
    csv.WriteLine("lat,lon,Q,rain,warm,elev,next");
    foreach(var r in reaches)
    {
        int i=r.Lat.Length/2; double lat=r.Lat[i],lon=r.Lon[i];
        if(lat<12||lat>33||lon<35||lon>60) continue;
        var cs=data.SampleClimate(lat,lon);double elev=data.SampleTerrain(lat,lon).Elev;
        csv.WriteLine($"{lat},{lon},{r.Q},{cs.Rain:F2},{cs.TWarm-.0065*elev:F2},{elev:F1},{r.Next}");
    }
}
// Read the local Natural Earth source metadata; no downloads or Python dependencies.
string raw=Path.GetFullPath(Path.Combine(root,"../../../rawdata/ne_lakes/ne_10m_lakes"));
foreach(string suffix in new[]{"","_europe","_north_america"})
{
using(var db=new BinaryReader(File.OpenRead(raw+suffix+".dbf")))
using(var shp=new BinaryReader(File.OpenRead(raw+suffix+".shp")))
using(var csv=new StreamWriter(Path.Combine(output,"iraq-lake-source"+suffix+".csv")))
{
    db.BaseStream.Position=4;int count=db.ReadInt32();int header=db.ReadUInt16(),record=db.ReadUInt16();
    var fields=new List<(string Name,int Offset,int Length)>();int offset=1;
    for(int at=32;at<header-1;at+=32)
    {
        db.BaseStream.Position=at;var descriptor=db.ReadBytes(32);
        string name=Encoding.ASCII.GetString(descriptor,0,11).TrimEnd('\0');
        int len=descriptor[16];fields.Add((name,offset,len));offset+=len;
    }
    csv.WriteLine("index,name,featurecla,wikidata,minLon,minLat,maxLon,maxLat");
    shp.BaseStream.Position=100;
    for(int n=0;n<count;n++)
    {
        shp.ReadInt32();int bytes=System.Net.IPAddress.NetworkToHostOrder(shp.ReadInt32())*2;long end=shp.BaseStream.Position+bytes;
        int type=shp.ReadInt32();if(type!=5){shp.BaseStream.Position=end;continue;}
        double x0=shp.ReadDouble(),y0=shp.ReadDouble(),x1=shp.ReadDouble(),y1=shp.ReadDouble();shp.BaseStream.Position=end;
        if(x1<38||x0>49||y1<29||y0>38) continue;
        db.BaseStream.Position=header+n*record;var row=db.ReadBytes(record);
        string F(string name){var f=fields.FirstOrDefault(f=>f.Name.Equals(name,StringComparison.OrdinalIgnoreCase));return f.Length==0?"":Encoding.UTF8.GetString(row,f.Offset,f.Length).Trim().Replace(",",";");}
        csv.WriteLine($"{n},{F("name")},{F("featurecla")},{F("wikidataid")},{x0},{y0},{x1},{y1}");
    }
}
}
Console.WriteLine("Wrote arid reach and Iraqi lake source audits.");
LakeAudit.Write(root,output);
using(var csv=new StreamWriter(Path.Combine(output,"iraq-water-components.csv")))
{
    csv.WriteLine("cells,lat,lon,minLat,maxLat,minLon,maxLon,level,salt");
    var seen=new HashSet<(int,int)>();var exclusions=new HashSet<(int,int)>();
    for(int r=1620;r<1830;r++)for(int c=6660;c<6870;c++)
    {
        if(seen.Contains((r,c)) || (data.GetCell(r,c).Flags&EarthData.FlagLake)==0)continue;
        var cells=new List<(int R,int C)>();var queue=new Queue<(int,int)>();queue.Enqueue((r,c));seen.Add((r,c));
        while(queue.Count>0)
        {
            var p=queue.Dequeue();cells.Add(p);
            for(int dr=-1;dr<=1;dr++)for(int dc=-1;dc<=1;dc++)
            {
                var k=(p.Item1+dr,p.Item2+dc);
                if(k.Item1<1500||k.Item1>1900||k.Item2<6600||k.Item2>7000||seen.Contains(k))continue;
                if((data.GetCell(k.Item1,k.Item2).Flags&EarthData.FlagLake)==0)continue;
                seen.Add(k);queue.Enqueue(k);
            }
        }
        double lat=90-(cells.Average(p=>p.R)+.5)/30,lon=-180+(cells.Average(p=>p.C)+.5)/30;
        var cell=data.GetCell(r,c);
        csv.WriteLine($"{cells.Count},{lat:F4},{lon:F4},{90-(cells.Max(p=>p.R)+1)/30.0:F4},{90-cells.Min(p=>p.R)/30.0:F4},{-180+cells.Min(p=>p.C)/30.0:F4},{-180+(cells.Max(p=>p.C)+1)/30.0:F4},{cell.Level},{(cell.Flags&EarthData.FlagSalt)!=0}");
        if(lat>32.4&&lat<33.2&&lon>43.4&&lon<44)
            foreach(var p in cells)for(int dr=-3;dr<=3;dr++)for(int dc=-3;dc<=3;dc++)exclusions.Add((p.R+dr,p.C+dc));
    }
    if(exclusions.Count>0 && args.Contains("--bake-reservoirs"))
        File.WriteAllText(Path.Combine(root,"earthdata/reservoirs-v1.json"),JsonSerializer.Serialize(new{version=1,source="Natural Earth Q2007652 Lake Razazah; NASA artificial diversion reservoir; cells plus 3-cell shore ring",cells=exclusions.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).Select(p=>new[]{p.Item1,p.Item2})})+"\n");
}
Console.WriteLine("Routing moist-source discharge...");
var supported=AridHydrology.WetSourceFlow(data,reaches);
using(var csv=new StreamWriter(Path.Combine(output,"arabia-flow-support.csv")))
{
    csv.WriteLine("lat,lon,Q,wetSourceFlow,rain,dry");
    for(int i=0;i<reaches.Count;i++)
    {
        var r=reaches[i];int mid=r.Lat.Length/2;double lat=r.Lat[mid],lon=r.Lon[mid];
        if(lat<19||lat>29||lon<36||lon>56)continue;
        var cs=data.SampleClimate(lat,lon);double warm=cs.TWarm-.0065*data.SampleTerrain(lat,lon).Elev;
        csv.WriteLine($"{lat},{lon},{r.Q},{supported[i]:F2},{cs.Rain:F2},{AridHydrology.Dry(cs.Rain,warm,r.Q,supported[i])}");
    }
}
var geo=new EarthGeometry{RingStart=51200,RingLength=409600,Wraps=true,EquatorZ=102400,BlocksPerDegreeZ=204800.0/180,SeamLon0=-169};
var vertical=new VerticalTerrainOptions().Build(512,110,.0625,200,20,.43);
var model=new TerrainModel(data,geo,vertical,386217){Summits=true,SummitRules=KnownSummits.Version,LocalAlpineEcology=true,AlpineSurfaceRules=1,ClimateRules=3,DetailScale=1};
model.Islands=new IslandLayer(data.Islands,geo,4,IslandLayer.RulesVersion);model.Coasts=new CoastLayer(data.Coasts,geo,4);model.LakeShores=true;
int checks=0,failures=0;
void Check(bool ok,string name){checks++;if(!ok){failures++;Console.Error.WriteLine("FAIL "+name);}}
using(var csv=new StreamWriter(Path.Combine(output,"perennial-and-wadi-columns.csv")))
{
    csv.WriteLine("site,lat,lon,Q,wetSourceFlow,rain,beforeWater,afterWater,dryBed,fanCount,riparianWeight");
    foreach(var p in new[]{("Arabian wadi",20.41,44.98),("Nile",25.5,32.7),("Euphrates",33.0,44.3),("Tigris",33.5,44.3),("Indus",26.0,68.3),("Colorado",36.1,-113.3),("Orange",-28.6,17.6)})
    {
        int index=-1;double nearest=double.MaxValue;
        for(int i=0;i<reaches.Count;i++)
        {
            var re=reaches[i];int mid=re.Lat.Length/2;
            double d=Math.Pow(re.Lat[mid]-p.Item2,2)+Math.Pow((re.Lon[mid]-p.Item3)*Math.Cos(p.Item2*Math.PI/180),2);
            if(d<nearest){nearest=d;index=i;}
        }
        var r=reaches[index];int middle=r.Lat.Length/2;double lat=r.Lat[middle],lon=r.Lon[middle];
        var isolated=new RiverReach{Q=r.Q,Next=-1,Lat=r.Lat,Lon=r.Lon,Level=r.Level,Fixed=r.Fixed};
        model.Rivers=new RiverNetwork(new List<RiverReach>{isolated},geo,100,1,model.RiverWaterTop,"all",20,2,widthRules:1,poolLayout:9);
        model.WetSourceFlow=new[]{supported[index]};geo.FromGeo(lat,lon,out double x,out double z);
        model.AridRiverRules=0;var old=model.GetColumn(x,z);model.AridRiverRules=1;model.DryFans=new WadiFans(model);var current=model.GetColumn(x,z);
        var cs=data.SampleClimate(lat,lon);double riparian=model.RiparianWeight(x,z,cs.Rain);
        csv.WriteLine($"{p.Item1},{lat},{lon},{r.Q},{supported[index]:F2},{cs.Rain:F2},{old.Water},{current.Water},{current.DryRiver},{model.DryFans.Sites.Count},{riparian:F3}");
        Check(p.Item1=="Arabian wadi"? current.Water==0 && current.DryRiver && riparian==0 : current.Water>0 && !current.DryRiver,p.Item1+" generated channel water");
        model.DryFans=null;
    }
    model.Rivers=null;model.AridRiverRules=0;model.WetSourceFlow=null;
}
string Record(int id,string code,string elevation,string dem,double lat=10,double lon=10,string feature="T")
{var c=Enumerable.Repeat("",19).ToArray();c[0]=id.ToString();c[1]="Fixture "+id;c[4]=lat.ToString();c[5]=lon.ToString();c[6]=feature;c[7]=code;c[15]=elevation;c[16]=dem;return string.Join('\t',c);}
string inputFixture=string.Join('\n',Record(1,"PK","5000","8999"),Record(2,"MT","","8800",11,11),Record(3,"VLC","3776","0",35.36,138.73),Record(4,"PKS","4000","0",-20,-60),Record(5,"MT","6100","0",40,50),Record(6,"HLL","6000","0",30,30),Record(7,"PK","6000","0",40,40,"P"),Record(8,"PK","9001","0",20,20),Record(9,"PK","4900","0",10.001,10));
File.WriteAllText(Path.Combine(output,"allCountries-fixture.txt"),inputFixture);
byte[] baked=GeoNamesBake.Bake(new StringReader(inputFixture),out var bakeStats);
File.WriteAllBytes(Path.Combine(output,"fixture-peaks.json"),baked);
var bakedCatalog=new PeakCatalog(baked);
Check(bakedCatalog.Count==4,"GeoNames explicit elevations, classes, codes and spacing");
Check(bakedCatalog.Near(10,10).Single().Height==5000,"GeoNames elevation column; DEM 8999 ignored");
Check(!bakedCatalog.Near(11,11).Any(p=>p.Lat==11),"DEM-only summit omitted");
Check(baked.SequenceEqual(GeoNamesBake.Bake(new StringReader(inputFixture),out _)),"Deterministic peak bake");
File.WriteAllText(Path.Combine(output,"peak-bake-fixture-stats.json"),JsonSerializer.Serialize(bakeStats));
var harness=new Harness(root,false);
using(var csv=new StreamWriter(Path.Combine(output,"global-desert-trees.csv")))
{
    csv.WriteLine("site,pack,rain,warm,drySummer,hotDesert,eligibleTrees,pineShare,acaciaShare");
    foreach(var config in new[]{harness.Make("vanilla",Array.Empty<string>(),true),harness.Make("full",harness.AllProviders,true),harness.Make("unregional",Array.Empty<string>(),false)})
    foreach(var p in new[]{("Hoggar",23.3,5.8),("Arabia",23.3,45.0),("Namib",-23.5,15.5),("Thar",26.9,70.9),("Australia",-25.5,134.0),("Atacama",-23.5,-68.5),("Santorini",36.4,25.4)})
    {
        var cs=data.SampleClimate(p.Item2,p.Item3);double elev=data.SampleTerrain(p.Item2,p.Item3).Elev;
        var site=new TreeSite{Lat=p.Item2,Lon=p.Item3,Elevation=elev,Region=config.Catalog?.Geography.Find(p.Item2,p.Item3)??-1,ColdMonth=(float)(cs.TCold-.0065*elev),Factors=Enumerable.Repeat(1f,RegionalTreePicker.Groups).ToArray()};site.AmbientRain=(float)cs.Rain;site.WarmMonth=(float)(cs.TWarm-.0065*elev);site.DrySummer=(float)cs.DrySummer;
        site.Rain=120;site.Temp=24;site.Fert=70;site.Forest=40;site.HeightRel=.75f;site.Wet=true;
        float[] shares=new float[config.TreeRegs.Length];config.Trees.Expected(site,shares);
        if(config.Catalog==null)
        {
            var ranges=harness.Make("native ranges",Array.Empty<string>(),true).Catalog;
            for(int i=0;i<shares.Length;i++)if(MountainEcology.HotDesert(cs.Rain,site.WarmMonth,cs.DrySummer)&&!ranges.NativeAt(config.TreeRegs[i].Generator,site))shares[i]=0;
        }
        double pine=shares.Where((v,i)=>config.TreeRegs[i].Generator.Contains("pine")).Sum();
        double acacia=shares.Where((v,i)=>config.TreeRegs[i].Generator.Contains("acacia")).Sum();
        bool desert=MountainEcology.HotDesert(cs.Rain,site.WarmMonth,cs.DrySummer);
        if(desert)Check(pine==0,p.Item1+" "+config.Name+" no pine despite wet riparian boost");
        csv.WriteLine($"{p.Item1},{config.Name},{cs.Rain:F2},{site.WarmMonth:F2},{cs.DrySummer:F3},{desert},{shares.Sum():F4},{pine:F4},{acacia:F4}");
    }
}
var references=KnownSummits.Peaks.Concat(new[]{new KnownSummits.Peak("Kangchenjunga",27.7025,88.1475,8586),new("Cho Oyu",28.0942,86.6608,8188),new("Nanga Parbat",35.2372,74.5892,8126),new("Matterhorn",45.9764,7.6586,4478),new("Elbrus",43.355,42.4392,5642),new("Chimborazo",-1.4692,-78.8175,6263)}).ToArray();
// Deliberately synthetic catalogue: validates the reader and shaping while the real download is pending.
byte[] fixture=JsonSerializer.SerializeToUtf8Bytes(new{version=1,peaks=references.Select(p=>new object[]{p.Name,p.Lat,p.Lon,p.Height})});
var fixtureCatalog=new PeakCatalog(fixture);
geo.FromGeo(27.9881,86.925,out double perfX,out double perfZ);double sink=0;
for(int i=0;i<1000;i++){sink+=KnownSummits.Restore(geo,perfX,perfZ,8000,fixtureCatalog);sink+=MountainEcology.LooseVolcanicGround(35.36,138.73,2500);}
long allocation=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++)sink+=KnownSummits.Restore(geo,perfX,perfZ,8000,fixtureCatalog);
long peakBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;
allocation=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++)sink+=MountainEcology.LooseVolcanicGround(35.36,138.73,2500);
long coneBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;
Check(peakBytes==0,"Summit catalogue lookup/cap allocates zero bytes per column");
Check(coneBytes==0,"Global volcanic ecology allocates zero bytes per column");
Console.WriteLine($"Hot paths: peak {peakBytes} bytes / 10000, volcanic {coneBytes} bytes / 10000; sink {sink:F0}");
var productionCatalog=data.ReadPeaks();
File.WriteAllText(Path.Combine(output,"production-catalogue.txt"),productionCatalog==null?"ABSENT: nine fallback peaks only. Fixture results are not production acceptance.\n":$"{productionCatalog.Count} peaks; SHA256 {productionCatalog.Signature}\n");
using(var csv=new StreamWriter(Path.Combine(output,"summit-catalogue-handoff.csv")))
using(var profiles=new StreamWriter(Path.Combine(output,"catalogue-slope-profiles.csv")))
using(var productionProfiles=new StreamWriter(Path.Combine(output,"production-slope-profiles.csv")))
{
    csv.WriteLine("peak,targetY,fallbackY,syntheticCatalogueY,productionCatalogueY,maxCorrectionChangePer100m,productionPeak,productionElevation");
    profiles.WriteLine("peak,axis,distanceM,oldM,catalogueM,correctionM");
    productionProfiles.WriteLine("peak,axis,distanceM,oldM,catalogueM,correctionM");
    foreach(var p in references)
    {
        geo.FromGeo(p.Lat,p.Lon,out double x,out double z);x=Math.Round(x);z=Math.Round(z);
        model.Peaks=null;int fallback=model.GetColumn(x,z).Top;model.Peaks=fixtureCatalog;var top=model.GetColumn(x,z);
        double largest=0;
        foreach(bool east in new[]{true,false})
        {
            double metres=east?111320*Math.Cos(p.Lat*Math.PI/180)/geo.BlocksPerDegreeX:111320/geo.BlocksPerDegreeZ,last=0;
            for(int d=-6000;d<=6000;d+=100)
            {
                double px=x+(east?d/metres:0),pz=z+(east?0:d/metres);
                model.SummitRules=1;var old=model.GetColumn(px,pz);model.SummitRules=KnownSummits.Version;var current=model.GetColumn(px,pz);
                double correction=current.Elev-old.Elev;
                if(d>-6000)largest=Math.Max(largest,Math.Abs(correction-last));last=correction;
                profiles.WriteLine($"{p.Name},{(east?"east":"north")},{d},{old.Elev:F3},{current.Elev:F3},{correction:F3}");
                if(productionCatalog!=null)
                {
                    model.Peaks=productionCatalog;var actual=model.GetColumn(px,pz);model.Peaks=fixtureCatalog;
                    productionProfiles.WriteLine($"{p.Name},{(east?"east":"north")},{d},{old.Elev:F3},{actual.Elev:F3},{actual.Elev-old.Elev:F3}");
                }
            }
        }
        string actualY="pending download",actualName="",actualHeight="";
        if(productionCatalog!=null)
        {
            double Distance(KnownSummits.Peak q)=>Math.Sqrt(Math.Pow((q.Lat-p.Lat)*111320,2)+Math.Pow(Geo.Wrap180(q.Lon-p.Lon)*111320*Math.Cos(p.Lat*Math.PI/180),2));
            var actual=productionCatalog.Near(p.Lat,p.Lon).Where(q=>Distance(q)<1500).OrderBy(Distance).FirstOrDefault();
            if(actual==null)actualY="missing within 1.5 km";
            else
            {
                geo.FromGeo(actual.Lat,actual.Lon,out double ax,out double az);model.Peaks=productionCatalog;
                int y=model.GetColumn(Math.Round(ax),Math.Round(az)).Top;actualY=y.ToString();actualName=actual.Name.Replace(",",";");actualHeight=actual.Height.ToString();
                // the terrain must reach the catalogue's own survey; the catalogue may differ slightly from the
                // reference (GeoNames keeps Everest's older 8848 m against 2020's 8849 m)
                Check(y==(int)Math.Floor(vertical.Y(actual.Height)),p.Name+" production catalogue target");
                Check(Math.Abs(actual.Height-p.Height)<=Math.Max(30,0.01*p.Height),p.Name+" production catalogue height near reference");
            }
            Check(actual!=null,p.Name+" production catalogue coverage");
            model.Peaks=fixtureCatalog;
        }
        csv.WriteLine($"{p.Name},{(int)Math.Floor(vertical.Y(p.Height))},{fallback},{top.Top},{actualY},{largest:F3},{actualName},{actualHeight}");
        Check(top.Top==(int)Math.Floor(vertical.Y(p.Height)),p.Name+" synthetic catalogue target");
        // The lift must not make a pillar: next to the summit it may steepen the ground by at most the slope of a steep
        // horn face (50 degrees: 1.2 m per metre), measured in metres so block heights at every altitude compare alike.
        var nbs=new[]{(x-1,z),(x+1,z),(x,z-1),(x,z+1)};
        var around=nbs.Select(c=>model.GetColumn(c.Item1,c.Item2)).ToArray();
        int rulesNow=model.SummitRules;model.SummitRules=1;var baseTop=model.GetColumn(x,z);
        var baseAround=nbs.Select(c=>model.GetColumn(c.Item1,c.Item2)).ToArray();model.SummitRules=rulesNow;
        double columnM=111320/geo.BlocksPerDegreeZ;
        double added=Enumerable.Range(0,4).Max(i=>(top.Elev-around[i].Elev)-(baseTop.Elev-baseAround[i].Elev));
        Console.WriteLine($"{p.Name}: top {top.Top}, around {string.Join('/',around.Select(c=>c.Top))}; before {baseTop.Top}; lift steepens by at most {added/columnM:F2} m/m");
        Check(added<=1.2*columnM,p.Name+" no single-column summit spike");
        Check(largest<250,p.Name+" synthetic catalogue smooth correction");
    }
    if(productionCatalog!=null)
    {
        // Low named hills (play-test, Burgas 42.1 N 27.5 E): summit caps must never dig moats round them.
        model.Peaks=productionCatalog;int lowered2=0,lowered3=0,samples=0;double worst2=0,worst3=0;
        for(double lat=42.0;lat<=42.7;lat+=.005)for(double lon=26.8;lon<=27.9;lon+=.005)
        {
            geo.FromGeo(lat,lon,out double sx,out double sz);
            model.SummitRules=1;double before=model.GetColumn(sx,sz).Elev;
            model.SummitRules=2;double v2=model.GetColumn(sx,sz).Elev;
            model.SummitRules=KnownSummits.Version;double v3=model.GetColumn(sx,sz).Elev;
            samples++;if(v2<before-1e-6){lowered2++;worst2=Math.Min(worst2,v2-before);}if(v3<before-1e-6){lowered3++;worst3=Math.Min(worst3,v3-before);}
        }
        Console.WriteLine($"Burgas low hills: {samples} columns; rules 2 lowered {lowered2} (worst {worst2:F0} m), rules {KnownSummits.Version} lowered {lowered3} (worst {worst3:F0} m, only ground above a surveyed summit)");
        Check(worst3>-40 && lowered3*10<lowered2,"summit caps no longer dig moats round low hills");
        model.SummitRules=KnownSummits.Version;
    }
    model.Peaks=null;
}
using(var csv=new StreamWriter(Path.Combine(output,"volcanic-cones.csv")))
{
    csv.WriteLine("name,lat,lon,referenceM,fullLooseGroundFromM,looseAtTwoThirds,looseAtHalf,beforeFujiOnly");
    foreach(var p in HoloceneCones.Peaks)
    {
        double upper=MountainEcology.LooseVolcanicGround(p.Lat,p.Lon,p.Height*.67),lower=MountainEcology.LooseVolcanicGround(p.Lat,p.Lon,p.Height*.5);
        Check(upper>.99,p.Name+" upper cone");
        csv.WriteLine($"{p.Name.Replace(",",";")},{p.Lat},{p.Lon},{p.Height},{p.Height*.66:F0},{upper:F3},{lower:F3},{(p.Name=="Fuji"?1:0)}");
    }
    Check(MountainEcology.LooseVolcanicGround(35.36,138.73,2500)>.99,"Fuji bare at 2500m");
    Check(MountainEcology.LooseVolcanicGround(35.36,138.73,2000)<.001,"Fuji lower forest");
    Check(MountainEcology.LooseVolcanicGround(45.83,6.87,3000)==0,"Nonvolcanic Alps unaffected by volcanic rule");
}
using(var csv=new StreamWriter(Path.Combine(output,"coral-habitat.csv")))
{
    csv.WriteLine("site,waterKind,warm,mean,oldLiquidOnly,newMarineWarm");
    foreach(var p in new[]{("Hammar reported",30.8,47.3),("Hammar basin",30.75,47.1),("Red Sea",26.8,34.5),("Gulf",26.5,51.5),("Great Barrier Reef",-18.325,147.35),("Salt lake",31.5,35.45),("Cold sea",61.5,3.0)})
    {
        geo.FromGeo(p.Item2,p.Item3,out double x,out double z);var c=model.GetColumn(x,z);var cs=data.SampleClimate(p.Item2,p.Item3);
        bool allowed=MarineEcology.CoralHabitat(c,cs);csv.WriteLine($"{p.Item1},{c.Water},{cs.TWarm:F2},{cs.TMean:F2},{c.Water!=0},{allowed}");
        Check(p.Item1 is "Red Sea" or "Gulf" or "Great Barrier Reef" ? allowed : !allowed,p.Item1+" coral eligibility");
    }
    foreach(byte kind in new byte[]{1,2,3})
    {
        var c=new TerrainModel.Column{Top=90,WaterTop=109,Water=kind,IceBottom=int.MaxValue};
        var cs=new EarthData.ClimateSample{TMean=25,TWarm=35};bool allowed=MarineEcology.CoralHabitat(c,cs);
        Check(allowed==(kind==1),"Warm water kind "+kind+" coral guard");
        csv.WriteLine($"synthetic warm kind {kind},{kind},35,25,True,{allowed}");
    }
}
using(var csv=new StreamWriter(Path.Combine(output,"reservoir-before-after.csv")))
{
    csv.WriteLine("site,lat,lon,beforeWater,afterWater,beforeY,afterY");
    foreach(var p in new[]{("Razzaza",32.76,43.62),("Habbaniyah",33.30,43.43),("Tharthar",34.03,43.27),("Hammar",30.8,47.3)})
    {
        geo.FromGeo(p.Item2,p.Item3,out double x,out double z);data.SetReservoirExclusions(false);var old=model.GetColumn(x,z);
        data.SetReservoirExclusions(true);var current=model.GetColumn(x,z);
        csv.WriteLine($"{p.Item1},{p.Item2},{p.Item3},{old.Water},{current.Water},{old.Top},{current.Top}");
        Check(p.Item1=="Hammar" ? old.Water==current.Water : current.Water==0,p.Item1+" reservoir audit");
    }
    data.SetReservoirExclusions(false);
}
using(var csv=new StreamWriter(Path.Combine(output,"strait-clearance.csv")))
{
    var navigation=new NavigableStraits(geo,model);
    csv.WriteLine("strait,beforeMinWaterWidth,afterMinWaterWidth,beforeMinShipWidth,afterMinShipWidth,beforeMinDepth,afterMinDepth,maxNaturalCentreElevation,sections");
    foreach(var strait in NavigableStraits.Courses)
    {
        var course=navigation.WorldCourses[Array.IndexOf(NavigableStraits.Courses,strait)];
        int oldWidth=999,newWidth=999,oldShip=999,newShip=999,oldDepth=999,newDepth=999,sections=0;double maxElev=-999;
        for(int k=1;k<course.Length;k++)
        {
            var a=course[k-1];var b=course[k];double ax=a.X,az=a.Z,bx=b.X,bz=b.Z;
            double dx=bx-ax,dz=bz-az,len=Math.Sqrt(dx*dx+dz*dz);int steps=(int)Math.Ceiling(len/2);
            if(steps==0)continue;
            for(int t=0;t<=steps;t++)
            {
                double x=ax+dx*t/steps,z=az+dz*t/steps;
                (int Water,int Ship,int Depth) Measure(bool upgraded)
                {
                    model.Straits=upgraded?navigation:null;
                    var centre=model.GetColumn(x,z);if(!upgraded)maxElev=Math.Max(maxElev,centre.Water==1?0:vertical.Elevation(centre.Top));
                    int depth=centre.Water==1?centre.WaterTop-centre.Top:0;
                    int Width(int minDepth)
                    {
                        if(depth<minDepth)return 0;int count=1;
                        foreach(int sign in new[]{-1,1})for(int d=1;d<=80;d++)
                        {var c=model.GetColumn(x-sign*dz/len*d,z+sign*dx/len*d);if(c.Water!=1||c.WaterTop-c.Top<minDepth)break;count++;}
                        return count;
                    }
                    return(Width(1),Width(6),depth);
                }
                var old=Measure(false);var current=Measure(true);sections++;
                oldWidth=Math.Min(oldWidth,old.Water);newWidth=Math.Min(newWidth,current.Water);oldShip=Math.Min(oldShip,old.Ship);newShip=Math.Min(newShip,current.Ship);
                oldDepth=Math.Min(oldDepth,old.Depth);newDepth=Math.Min(newDepth,current.Depth);
                Check(current.Ship>=23 && current.Depth>=6,strait.Name+" ship clearance");
            }
        }
        csv.WriteLine($"{strait.Name},{oldWidth},{newWidth},{oldShip},{newShip},{oldDepth},{newDepth},{maxElev:F1},{sections}");
        Console.WriteLine($"{strait.Name}: ship width {oldShip}->{newShip}; depth {oldDepth}->{newDepth}; max source elevation {maxElev:F1}");
        double minX=course.Min(p=>p.X)-30,minZ=course.Min(p=>p.Z)-30,maxX=course.Max(p=>p.X)+30,maxZ=course.Max(p=>p.Z)+30;
        double scale=Math.Max(1,Math.Max(maxX-minX,maxZ-minZ)/400);int width=(int)Math.Ceiling((maxX-minX)/scale),height=(int)Math.Ceiling((maxZ-minZ)/scale);
        foreach(bool upgraded in new[]{false,true})
        {
            model.Straits=upgraded?navigation:null;byte[] rgb=new byte[width*height*3];
            for(int j=0;j<height;j++)for(int i=0;i<width;i++)
            {
                var c=model.GetColumn(minX+i*scale,minZ+j*scale);int at=(j*width+i)*3;
                if(c.Water==1){int d=c.WaterTop-c.Top;rgb[at]=(byte)(d>=6?25:75);rgb[at+1]=(byte)(d>=6?90:155);rgb[at+2]=190;}
                else{int shade=(int)Math.Clamp(115+vertical.Elevation(c.Top)*.15,115,210);rgb[at]=(byte)shade;rgb[at+1]=(byte)(shade*.85);rgb[at+2]=(byte)(shade*.6);}
            }
            Png.WriteRgb(Path.Combine(output,strait.Name+(upgraded?"-after.png":"-before.png")),width,height,rgb);
        }
    }
    model.Straits=null;
}
Console.WriteLine($"Review checks: {checks}, failures: {failures}");
var tags=typeof(EarthMap.EarthParams).GetFields().Select(f=>f.GetCustomAttributes(typeof(ProtoBuf.ProtoMemberAttribute),false).Cast<ProtoBuf.ProtoMemberAttribute>().FirstOrDefault()?.Tag).Where(t=>t!=null).ToArray();
Check(tags.Distinct().Count()==tags.Length,"Unique protobuf field numbers");
var legacy=new EarthMap.EarthParams();
Check(legacy.AridRiverRules==0&&legacy.StraitRules==0&&legacy.ReservoirRules==0&&legacy.PeakCatalogSignature==null,"Missing save fields keep legacy terrain");
Console.WriteLine($"Final review checks: {checks}, failures: {failures}");
Environment.ExitCode=failures==0?0:1;
