using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EarthMap.Core;

static class RealDatasetBenchmark
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // Compile a typed accessor once: inspecting four million value-type segments
    // must not allocate a boxed segment and four boxed integers for every row.
    static (long Entries, int Segments, int LargestCellRectangle) LegacyEntryCount<T>(T[] values)
    {
        var s = Expression.Parameter(typeof(T), "s");
        var ctor = typeof(ValueTuple<int,int,int,int>).GetConstructor(new[]{typeof(int),typeof(int),typeof(int),typeof(int)});
        var accessor = Expression.Lambda<Func<T,(int,int,int,int)>>(Expression.New(ctor,
            new[]{"CellX0","CellX1","CellZ0","CellZ1"}.Select(name=>Expression.Field(s,name))),s).Compile();
        long entries=0; int largest=0;
        foreach(var value in values)
        {
            var (x0,x1,z0,z1)=accessor(value);
            long area=(long)(x1-x0+1)*(z1-z0+1);
            entries+=area;largest=Math.Max(largest,(int)Math.Min(int.MaxValue,area));
        }
        return(entries,values.Length,largest);
    }
    public static void Run(string archive, string output)
    {
        const int worldWidth=4096000,worldLength=2048000,mapHeight=384,seaLevel=165;
        var data=EarthData.LoadFromZip(archive,false);
        List<RiverReach> reaches;
        using(var zip=System.IO.Compression.ZipFile.OpenRead(archive))
        using(var stream=zip.GetEntry("earthdata/rivers.bin").Open())reaches=RiverData.Read(stream);
        // Cylinder World on this map (twice as wide as long, so no room for EarthProportions): its smallest jump inset, 512
        var geo=EarthLayout.Build(new LayoutInput{WorldWidth=worldWidth,GutterMargin=512,
            CwRingStart=512,CwRingLength=worldWidth-1024,CwEquatorZ=worldLength/2.0,CwPoleDistance=worldLength/2.0-48,CwBandInner=608},data,out var layoutReport);
        var model=new TerrainModel(data,geo,VerticalScale.ForWorld(mapHeight,seaLevel),386217){RiverRelief=1};
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long beforeAllocated=GC.GetTotalAllocatedBytes(true),beforeManaged=GC.GetTotalMemory(false);
        var watch=Stopwatch.StartNew();
        var network=new RiverNetwork(reaches,geo,100,1.5,model.RiverWaterTop,"ends",20,
            geo.RingLength/204800.0,.5,2);
        watch.Stop();
        long allocated=GC.GetTotalAllocatedBytes(true)-beforeAllocated;
        long managedBeforeCollection=GC.GetTotalMemory(false);
        long peakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64;
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long managedRetained=GC.GetTotalMemory(false)-beforeManaged;
        var grid=(Dictionary<long,int[]>)typeof(RiverNetwork).GetField("grid",Private).GetValue(network);
        var segs=(Array)typeof(RiverNetwork).GetField("segs",Private).GetValue(network);
        var inspect=typeof(RealDatasetBenchmark).GetMethod(nameof(LegacyEntryCount),BindingFlags.Static|BindingFlags.NonPublic)
            .MakeGenericMethod(segs.GetType().GetElementType());
        var legacy=((long Entries,int Segments,int LargestCellRectangle))inspect.Invoke(null,new object[]{segs});
        long compactEntries=grid.Values.Sum(a=>(long)a.Length);
        var queryWatch=Stopwatch.StartNew();int hits=0;
        var rng=new Random(23823);
        // Whole-world representative samples plus densely sampled Rhine, Nile,
        // Amazon and Yangtze areas. Query timing is informational, not a gate.
        foreach(var anchor in new[]{(49d,8d),(30d,31d),(-3d,-60d),(30d,112d)})
        {
            geo.FromGeo(anchor.Item1,anchor.Item2,out double x,out double z);
            for(int i=0;i<10000;i++)if(network.Nearest(x+rng.NextDouble()*10000-5000,z+rng.NextDouble()*10000-5000,out _))hits++;
        }
        for(int i=0;i<10000;i++)if(network.Nearest(rng.NextDouble()*worldWidth,rng.NextDouble()*worldLength,out _))hits++;
        queryWatch.Stop();
        var result=new{status="PASS",archive,archiveSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant(),
            worldWidth,worldLength,mapHeight,seaLevel,layoutReport,sizeScale=network.SizeScale,
            segments=network.SegmentCount,buildMilliseconds=watch.Elapsed.TotalMilliseconds,
            allocatedBytes=allocated,managedBytesBeforeCollection=managedBeforeCollection,retainedManagedBytes=managedRetained,
            peakProcessWorkingSetBytes=peakWorkingSet,compactBuckets=grid.Count,compactEntries,
            calculatedLegacyEntries=legacy.Entries,legacyEntryCountMethod="Exact sum of legacy128-cell membership rectangles; legacy large grid is not allocated.",
            largestLegacyRectangle=legacy.LargestCellRectangle,entryReductionFactor=(double)legacy.Entries/compactEntries,
            queryCount=50000,queryHits=hits,queryMilliseconds=queryWatch.Elapsed.TotalMilliseconds,
            note="Focused river-only process: source dataset is original0.11 ZIP, settings match4096000x2048000 native inspection. Native baseline350459ms includes full Earth initialization and is not a like-for-like process-memory comparison."};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        GC.KeepAlive(network);
    }
}
