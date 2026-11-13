using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EarthMap.Core;

static class Program
{
    static int checks, columns, water, settlement, flows;
    static void Check(bool ok,string description){checks++;if(!ok)throw new Exception(description);}
    static bool Bits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
    static void Same(object a,object b,bool source=false)
    {
        foreach(var f in a.GetType().GetFields())
        {
            if(source && f.Name is "Lat" or "Lon" or "FlowX" or "FlowZ")continue;
            object x=f.GetValue(a),y=b.GetType().GetField(f.Name).GetValue(b);
            bool equal=x is double d?Bits(d,(double)y):x is float v?BitConverter.SingleToInt32Bits(v)==BitConverter.SingleToInt32Bits((float)y):Equals(x,y);
            Check(equal,$"Column differs:{f.Name}:{x}/{y}");
        }
        columns++;
    }
    static void Throws(Action action,string name)
    {try{action();}catch(ArgumentException){checks++;return;}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected rejection:"+name);}
    static void Mapping()
    {
        foreach(double scale in new[]{1d,2.3,8,32,64})
        {
            var e=new TerrainExpansion("math",7,11,80,240,scale);
            double previous=-1;
            for(int i=0;i<=10000;i++)
            {
                double r=i*.03;e.Project(7+r,11,out var sx,out var sz);
                Check(sx-7>previous,"Radial map folded");previous=sx-7;
                e.Jacobian(7+r,11,out var xx,out var xz,out var zz);
                Check(xx>0&&zz>0&&xx*zz-xz*xz>0,"Non-positive Jacobian");
                e.Unproject(sx,sz,out var px,out var pz);Check(Math.Abs(px-7-r)<1e-9&&Math.Abs(pz-11)<1e-9,"Inverse did not recover physical point");
                if(r>=240||scale==1)Check(Bits(sx,7+r)&&Bits(sz,11),"Outside projection is not bit-identical");
            }
            foreach(double r in new[]{0d,79.99,80,80.01,130,200,239.99,240,240.01,1000})
            for(int a=0;a<18;a++)
            {
                double x=7+r*Math.Cos(a),z=11+r*Math.Sin(a);e.Project(x,z,out var sx,out var sz);e.Unproject(sx,sz,out var px,out var pz);
                Check(Math.Abs(px-x)<1e-9&&Math.Abs(pz-z)<1e-9,"Inverse off-axis");
                e.TransformFlow(x,z,.6f,.8f,out var fx,out var fz);
                if(!e.Contains(x,z)){Check(fx==.6f&&fz==.8f,"Outside flow changed");continue;}
                const double eps=1e-5;e.Unproject(sx+eps*.6,sz+eps*.8,out var qx,out var qz);
                double vx=qx-x,vz=qz-z,len=Math.Sqrt(vx*vx+vz*vz);
                Check(Math.Abs(fx-vx/len)<.0001&&Math.Abs(fz-vz/len)<.0001,"Flow differs from mapped tangent");flows++;
            }
        }
        Throws(()=>new TerrainExpansion("bad",0,0,10,10,2),"zero transition");
        Throws(()=>new TerrainExpansion("bad",0,0,10,20,double.NaN),"nonfinite scale");
    }
    static TerrainModel.Column[] Area(TerrainModel m,int x,int z,int w,int h)
    {var a=new TerrainModel.Column[w*h];for(int j=0;j<h;j++)for(int i=0;i<w;i++)a[j*w+i]=m.GetColumnWithoutReservations(x+i,z+j);return a;}
    static string Option(string[] args,string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
    /// <summary>The earthdata/ folder of the checkout this test runs in (searched upwards from the working directory and the binary).</summary>
    static string FindEarthData()
    {
        foreach(var start in new[]{Environment.CurrentDirectory,AppContext.BaseDirectory})
            for(var dir=new DirectoryInfo(start);dir!=null;dir=dir.Parent)
                if(File.Exists(Path.Combine(dir.FullName,"earthdata","terrain.bin")))return Path.Combine(dir.FullName,"earthdata");
        throw new FileNotFoundException("No earthdata/terrain.bin above the working directory; pass --data <earthdata folder or mod zip>");
    }
    /// <summary>
    /// Usage: [--data &lt;earthdata folder, a folder holding earthdata/, or a mod zip&gt;] [--out &lt;results.json&gt;]
    /// The data defaults to the checkout's own earthdata/ (identical to the data in release/earthmap-0.12.1.zip); a released
    /// zip is used only when named. A single positional argument (the former repository-root argument) is read as --data.
    /// Nothing is written unless --out is given.
    /// </summary>
    static void Main(string[] args)
    {
        var clock=Stopwatch.StartNew();Mapping();
        string positional=args.Length>0&&!args[0].StartsWith("--")?args[0]:null;
        string dataPath=Path.GetFullPath(Option(args,"--data")??positional??FindEarthData());
        string output=Option(args,"--out");
        bool zip=dataPath.EndsWith(".zip",StringComparison.OrdinalIgnoreCase);
        if(zip?!File.Exists(dataPath):!(File.Exists(Path.Combine(dataPath,"terrain.bin"))||File.Exists(Path.Combine(dataPath,"earthdata","terrain.bin"))))
            throw new FileNotFoundException("Earth data not found (expected a mod zip, earthdata/ or a folder holding it): "+dataPath);
        var data=zip?EarthData.LoadFromZip(dataPath,true):EarthData.LoadFromFolder(dataPath,true);
        if(data.Rivers==null)throw new FileNotFoundException("rivers.bin missing from "+dataPath);
        Console.WriteLine("Earth data: "+dataPath);
        var geo=EarthLayout.Build(new LayoutInput{WorldWidth=256000,CwRingStart=25600,CwRingLength=204800,CwEquatorZ=51200,CwPoleDistance=51152,CwBandInner=608,GutterMargin=512},data,out var layout);
        var vertical=VerticalScale.ForWorld(384,165);
        var model=new TerrainModel(data,geo,vertical,290142474){GutterSeabedY=125};
        var legacy=new LegacyTerrainModel(data,geo,vertical,290142474){GutterSeabedY=125};
        model.Rivers=new RiverNetwork(data.Rivers,geo,100,1.5,model.RiverWaterTop,"ends",20,1,.5,2);legacy.Rivers=model.Rivers;
        Console.WriteLine($"Built {model.Rivers.SegmentCount} canonical river segments; {layout}");
        var rng=new Random(59171);
        for(int i=0;i<12000;i++)
        {
            double x=rng.NextDouble()*256000,z=rng.NextDouble()*102400;
            Same(legacy.GetColumnWithoutReservations(x,z),model.GetColumnWithoutReservations(x,z));
            Check(legacy.ClimatePixel(x,z,42,1,1)==model.ClimatePixel(x,z,42,1,1),"Unmodified climate differs");
            Check(Bits(legacy.ForestPotential(x,z),model.ForestPotential(x,z)),"Unmodified forest differs");
            Check(Bits(legacy.ShrubPotential(x,z),model.ShrubPotential(x,z)),"Unmodified shrubs differ");
        }
        // the pyramids of Giza and the Colosseum at their real coordinates (the world positions move with the layout)
        geo.FromGeo(29.9792,31.1342,out var gizaX,out var gizaZ);geo.FromGeo(41.8902,12.4922,out var romeX,out var romeZ);
        int gx=(int)Math.Round(gizaX),gz=(int)Math.Round(gizaZ);
        var sites=new[]{new TerrainExpansion("giza",gx,gz,439,1317,8),new TerrainExpansion("colosseum",(int)Math.Round(romeX),(int)Math.Round(romeZ),237,711,32)};
        var wetPoints=new List<(int X,int Z)>();
        foreach(var e in sites)
        {
            int siteWetWindows=0;
            Check(model.IsTerrainExpansionAllowed(e,out _,256000,102400),"Valid site rejected");
            for(int iz=-(int)e.OuterRadius-16;iz<=e.OuterRadius+16;iz+=13)
            for(int ix=-(int)e.OuterRadius-16;ix<=e.OuterRadius+16;ix+=13)
            {
                double x=e.CenterX+ix,z=e.CenterZ+iz;e.Project(x,z,out var sx,out var sz);
                var source=legacy.GetColumnWithoutReservations(sx,sz);var physical=model.GetColumnForExpansion(e,x,z);
                Same(source,physical,true);
                var location=geo.ToGeo(x,z);Check(Bits(location.Lat,physical.Lat)&&Bits(location.Lon,physical.Lon),"Physical attribution moved");
                if(physical.Water!=0)
                {
                    water++;if(physical.River&&siteWetWindows<20&&((ix+(int)e.OuterRadius+16)/13)%7==0)
                    {wetPoints.Add(((int)x,(int)z));siteWetWindows++;}
                }
                if(physical.River)
                {
                    e.TransformFlow(x,z,source.FlowX,source.FlowZ,out var fx,out var fz);
                    Check(fx==physical.FlowX&&fz==physical.FlowZ,"Channel tangent mismatch");
                }
            }
        }
        Check(water>1000,"Insufficient real shore/channel coverage");
        var storyModel=new TerrainModel(data,geo,vertical,290142474);storyModel.FlatZones.Add(new FlatZone{X=gx,Z=gz,Radius=10,Blend=20,TargetTop=200});
        Check(!storyModel.IsTerrainExpansionAllowed(sites[0],out _),"Story conflict accepted");
        Throws(()=>model.RegisterTerrainExpansions("bad",new[]{sites[0],new TerrainExpansion("overlap",gx+47,gz,100,300,2)}),"overlapping transaction");
        Check(!model.HasTerrainExpansions,"Rejected registration partially published");
        Check(!model.IsTerrainExpansionAllowed(new TerrainExpansion("edge",26000,gz,100,300,2),out _),"Seam conflict accepted");
        model.RegisterTerrainExpansions("test",sites,256000,102400);
        Throws(()=>model.RegisterTerrainExpansions("test",Array.Empty<TerrainExpansion>()),"duplicate owner");
        foreach(var e in sites)
        for(int i=0;i<8000;i++)
        {
            double a=rng.NextDouble()*Math.PI*2,r=rng.NextDouble()*(e.OuterRadius+100),x=e.CenterX+Math.Cos(a)*r,z=e.CenterZ+Math.Sin(a)*r;
            e.Project(x,z,out var sx,out var sz);
            Same(model.GetColumnForExpansion(e,x,z),model.GetColumnWithoutReservations(x,z));
            Check(legacy.ClimatePixel(sx,sz,42,1,1)==model.ClimatePixel(x,z,42,1,1),"Mapped climate/riparian mismatch");
            Check(Bits(legacy.ForestPotential(sx,sz),model.ForestPotential(x,z)),"Mapped forest mismatch");
            Check(Bits(legacy.ShrubPotential(sx,sz),model.ShrubPotential(x,z)),"Mapped shrub mismatch");
            Check(Bits(legacy.TrueTemperature(sx,sz,180),model.TrueTemperature(x,z,180)),"Mapped temperature mismatch");
            var sg=geo.ToGeo(sx,sz);var pg=model.TerrainGeoAt(x,z);Check(Bits(sg.Lat,pg.Lat)&&Bits(sg.Lon,pg.Lon),"Mapped ocean/source geography mismatch");
        }
        foreach(var point in wetPoints)
        {
            int x=(point.X>>5)*32,z=(point.Z>>5)*32;
            var broad=Area(model,x-16,z-16,64,64);TerrainModel.SettleSteps(broad,64,64);
            var native=Area(model,x-8,z-8,48,48);TerrainModel.SettleSteps(native,48,48);
            for(int j=0;j<32;j++)for(int i=0;i<32;i++)
            {Same(broad[(j+16)*64+i+16],native[(j+8)*48+i+8]);settlement++;}
        }
        Check(settlement>=10240,"Insufficient native-margin water settlement coverage");
        model.SealTerrainReservations();Throws(()=>model.RegisterTerrainExpansions("late",Array.Empty<TerrainExpansion>()),"late mutation");
        // a world's companion plan is published once per model (CompanionPlan.PublishOnce): /wgen regen sets the generators up
        // again, and a companion registering its sites a second time would find planning closed (and stop the server)
        var planModel=new TerrainModel(data,geo,vertical,290142474);int planned=0;
        Check(!planModel.PlanSealed&&CompanionPlan.PublishOnce(planModel,m=>{planned++;m.RegisterTerrainExpansions("first",Array.Empty<TerrainExpansion>());})&&planModel.PlanSealed&&planned==1,"First set-up publishes and seals the plan");
        Check(!CompanionPlan.PublishOnce(planModel,m=>{planned++;m.RegisterTerrainExpansions("again",Array.Empty<TerrainExpansion>());})&&planned==1,"A later set-up of the same model runs no planning");
        Throws(()=>planModel.RegisterTerrainExpansions("late",Array.Empty<TerrainExpansion>()),"late mutation after publication");
        // A registered domain still has exactly zero effect elsewhere, including seams.
        for(int i=0;i<6000;i++)
        {
            double x=rng.NextDouble()*256000,z=rng.NextDouble()*102400;
            if(sites.Any(s=>s.Contains(x,z)))continue;
            Same(legacy.GetColumnWithoutReservations(x,z),model.GetColumnWithoutReservations(x,z));
        }
        // the real seam (audit B12): both laps of the ring are one column, so a pad within RealSeamKeepOut of the ring's ends
        // is refused as an expansion is, and a story zone near one end holds on the other lap too (ApplyFlatZones wraps)
        var seamModel=new TerrainModel(data,geo,vertical,290142474){GutterSeabed=TerrainModel.RealSeamVersion};
        Check(seamModel.RealSeam,"real seam layout expected");
        int ringStart=(int)geo.RingStart,ringEnd=(int)(geo.RingStart+geo.RingLength);
        Throws(()=>seamModel.RegisterTerrainReservations("seam-west",new[]{new TerrainReservation("west",ringStart+100,gz,ringStart+110,gz+10,200,8,8)}),"pad at the seam's west side");
        Throws(()=>seamModel.RegisterTerrainReservations("seam-east",new[]{new TerrainReservation("east",ringEnd-1290,gz,ringEnd-1280,gz+10,200,8,8)}),"pad at the seam's east side");
        seamModel.RegisterTerrainReservations("inland",new[]{new TerrainReservation("inland",gx+2000,gz,gx+2010,gz+10,200,8,8)});
        seamModel.FlatZones.Add(new FlatZone{X=ringEnd-50,Z=gz,Radius=10,Blend=20,TargetTop=200});
        Check(seamModel.IntersectsStoryTerrain(ringStart-60,gz-5,ringStart-40,gz+5),"story zone on the other lap missed");
        Check(!seamModel.IntersectsStoryTerrain(ringStart+200,gz-5,ringStart+220,gz+5),"story zone found where it isn't");
        var result=new{status="PASS",data=dataPath,checks,columnComparisons=columns,wetColumnComparisons=water,flowDerivativeComparisons=flows,
            nativeSettlementComparisons=settlement,canonicalSegments=model.Rivers.SegmentCount,elapsedMilliseconds=clock.ElapsedMilliseconds,
            sites=sites.Select(e=>new{e.Id,e.CenterX,e.CenterZ,e.InnerRadius,e.OuterRadius,e.Magnification}),
            limits="Offline model/settlement checks; native game generation and geometry placement are separately tested by the companion harness."};
        // e.g. --out tests/TerrainExpansionTests/results.json when deliberately refreshing the committed evidence
        if(output!=null){File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Report: "+output);}
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
