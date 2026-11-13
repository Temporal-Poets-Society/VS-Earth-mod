using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EarthMap.Core;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static long queries, distances, candidates, hits, mutantDifferences;
    static readonly List<object> measurements = new();
    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    static Dictionary<long, int[]> Grid(object network) =>
        (Dictionary<long, int[]>)network.GetType().GetField("grid", Private).GetValue(network);
    static long Entries(object network) => Grid(network).Values.Sum(a => (long)a.Length);
    static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    static int Bucket(int value, int span) => (int)Math.Floor((double)value / span);
    static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    static (T Value, double Ms, long Bytes) Measure<T>(Func<T> create)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        T value = create();
        return (value, watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    static EarthGeometry Geometry(double scale, bool wraps) => new()
    {
        RingStart = -20000 * scale, RingLength = 40000 * scale,
        EquatorZ = 0, BlocksPerDegreeZ = 200 * scale, SeamLon0 = -180, Wraps = wraps
    };
    static RiverReach Reach(EarthGeometry geo, double scale, float q, int next,
        (double X, double Z, short Level)[] points, bool mouth = false)
    {
        var result = new RiverReach { Q = q, Next = next, Lon = new float[points.Length], Lat = new float[points.Length],
            Level = new short[points.Length], Fixed = new bool[points.Length] };
        for (int i = 0; i < points.Length; i++)
        {
            var position = geo.ToGeo(points[i].X * scale, points[i].Z * scale);
            result.Lon[i] = (float)position.Lon; result.Lat[i] = (float)position.Lat;
            result.Level[i] = points[i].Level;
        }
        result.Fixed[^1] = mouth;
        return result;
    }
    static List<RiverReach> Synthetic(EarthGeometry geo, double scale)
    {
        // Tributaries arrive at a bend at different levels; duplicate centre lines
        // exercise score ties, streams exercise narrow/wide blending, and the last
        // point of the mouth forces a fixed level and prolongation.
        var list = new List<RiverReach>
        {
            Reach(geo,scale,110,3,new[]{(-180d,-85d,(short)120),(-95d,-25d,(short)98),(-25d,0d,(short)78),(0d,0d,(short)70)}),
            Reach(geo,scale,24,3,new[]{(20d,-170d,(short)190),(20d,-95d,(short)125),(0d,0d,(short)90)}),
            Reach(geo,scale,450,3,new[]{(-220d,90d,(short)110),(-135d,42d,(short)89),(-25d,15d,(short)73),(0d,0d,(short)70)}),
            Reach(geo,scale,640,4,new[]{(0d,0d,(short)70),(55d,5d,(short)64),(66d,60d,(short)55),(110d,75d,(short)51)}),
            Reach(geo,scale,700,-1,new[]{(110d,75d,(short)51),(155d,52d,(short)39),(185d,0d,(short)22),(205d,-55d,(short)0)},true),
            Reach(geo,scale,170,-1,new[]{(-330d,-300d,(short)150),(-250d,-300d,(short)125),(-250d,-245d,(short)110),(-330d,-240d,(short)100),(-340d,-290d,(short)86),(-300d,-290d,(short)70)}),
            Reach(geo,scale,170,-1,new[]{(-330d,-300d,(short)150),(-250d,-300d,(short)125),(-250d,-245d,(short)110),(-330d,-240d,(short)100),(-340d,-290d,(short)86),(-300d,-290d,(short)70)}),
            Reach(geo,scale,250,-1,new[]{(420d,-200d,(short)100),(420d,-200d,(short)100),(420d,-130d,(short)70),(455d,-85d,(short)45)}),
            // More than 4000 blocks: the existing seam guard must remain unchanged.
            Reach(geo,scale,800,-1,new[]{(-7000d,210d,(short)70),(7000d,210d,(short)45),(7010d,230d,(short)40)})
        };
        var rng = new Random(723001);
        for (int r = 0; r < 18; r++)
        {
            double x = rng.NextDouble() * 900 - 450, z = rng.NextDouble() * 900 - 450;
            var points = new (double X, double Z, short Level)[20];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = (x + i * 15, z + 45 * Math.Sin(i * .5 + r), (short)(190 - i * 8));
            }
            list.Add(Reach(geo, scale, new float[]{21,40,99,100,600,5000}[r % 6], -1, points, r % 5 == 0));
        }
        return list;
    }
    static void Compare(RiverNetwork current, LegacyRiverNetwork legacy, double x, double z, string tag)
    {
        bool a = current.Nearest(x, z, out var h), b = legacy.Nearest(x, z, out var g);
        queries++;
        Require(a == b, $"Nearest existence differs: {tag} at{x},{z}");
        if (!a) return;
        hits++;
        Require(Bits(h.Dist,g.Dist) && Bits(h.Q,g.Q) && Bits(h.DirX,g.DirX) && Bits(h.DirZ,g.DirZ)
            && h.WaterTop==g.WaterTop && h.Ramp==g.Ramp && h.Rapid==g.Rapid && h.Mouth==g.Mouth,
            $"Nearest hit differs: {tag} at{x},{z}; water{h.WaterTop}/{g.WaterTop}, ramp{h.Ramp}/{g.Ramp}");
    }
    static void RunCase(double scale, bool wraps, string streamMode, double straighten)
    {
        var geo = Geometry(scale, wraps); var reaches = Synthetic(geo, scale);
        int Water(double level) => (int)Math.Round(100 + level * .4);
        var old = Measure(() => new LegacyRiverNetwork(reaches,geo,100,1.5,Water,streamMode,20,scale,straighten,1.5));
        var now = Measure(() => new RiverNetwork(reaches,geo,100,1.5,Water,streamMode,20,scale,straighten,1.5));
        string tag = $"scale{scale}/wrap{wraps}/{streamMode}/straighten{straighten}";
        Require(now.Value.SegmentCount == old.Value.SegmentCount, "Segment count differs: " + tag);
        Require(now.Value.Drops().SequenceEqual(old.Value.Drops()), "Drop positions or levels differ: " + tag);
        for (int r = 0; r < reaches.Count; r++)
        {
            Require(now.Value.DebugReach(r) == old.Value.DebugReach(r), "Pool layout differs: " + tag);
            bool a=now.Value.ReachLine(r,out var xx,out var zz), b=old.Value.ReachLine(r,out var ox,out var oz);
            Require(a==b && (!a || (xx.SequenceEqual(ox) && zz.SequenceEqual(oz))), "Reach geometry differs: " + tag);
        }
        var segs = (Array)typeof(RiverNetwork).GetField("segs",Private).GetValue(now.Value);
        var st = segs.GetType().GetElementType();
        var bounds = new (int X0,int X1,int Z0,int Z1)[segs.Length];
        for (int i=0;i<segs.Length;i++)
        {
            object s=segs.GetValue(i);
            bounds[i]=((int)st.GetField("CellX0").GetValue(s),(int)st.GetField("CellX1").GetValue(s),
                       (int)st.GetField("CellZ0").GetValue(s),(int)st.GetField("CellZ1").GetValue(s));
        }
        int span=(int)typeof(RiverNetwork).GetField("indexSpan",Private).GetValue(now.Value);
        var grid=Grid(now.Value);var legacyGrid=Grid(old.Value);
        var points=new List<(double X,double Z)>();
        var rng=new Random(88771);
        for (int i=0;i<7000;i++)points.Add(((rng.NextDouble()*1400-700)*scale,(rng.NextDouble()*1300-650)*scale));
        // Exact fine/coarse cell boundaries and adjacent floating-point values,
        // including negative cells, are independent of the river data resolution.
        for (int c=-8;c<=8;c++)foreach(double dx in new[]{-1e-7,0,1e-7})
            for (int z=-8;z<=8;z++)points.Add((c*128*span+dx,z*128+dx));
        foreach (var drop in now.Value.Drops().Take(200))
            foreach (double d in new[]{-8d,-1,0,1,8})points.Add((drop.X+d,drop.Z-d));
        for (int p=0;p<points.Count;p++)
        {
            var point=points[p];Compare(now.Value,old.Value,point.X,point.Z,tag);
            if (wraps && p%73==0)Compare(now.Value,old.Value,point.X+geo.RingLength*2,point.Z,tag+" wrapped");
            if(p%4==0)foreach(double within in new[]{0d,.01,1,7,127.999,128,129,511})
            {
                double a=now.Value.DistanceToWater(point.X,point.Z,within),b=old.Value.DistanceToWater(point.X,point.Z,within);
                distances++;Require(Bits(a,b),$"Water distance differs: {tag}, within{within}, {a}/{b}");
            }
            if(p%17==0)
            {
                int cx=(int)Math.Floor(geo.WrapX(point.X)/128),cz=(int)Math.Floor(point.Z/128);
                grid.TryGetValue(Key(Bucket(cx,span),Bucket(cz,span)),out var ids);
                legacyGrid.TryGetValue(Key(cx,cz),out var expected);
                int[] actual=(ids??Array.Empty<int>()).Where(i=>cx>=bounds[i].X0&&cx<=bounds[i].X1&&cz>=bounds[i].Z0&&cz<=bounds[i].Z1).ToArray();
                candidates++;Require(actual.SequenceEqual(expected??Array.Empty<int>()),"Candidate membership/order differs: "+tag);
                // the per-thread cell cache Nearest actually iterates must hold exactly that list too
                candidates++;Require(now.Value.CellCandidateIds(cx,cz).SequenceEqual(expected??Array.Empty<int>()),"Cached candidate membership/order differs: "+tag);
            }
        }
        long oldEntries=Entries(old.Value),newEntries=Entries(now.Value);
        Require(newEntries<=oldEntries,"Coarse index expanded entry count: "+tag);
        if(scale>=15)Require(oldEntries>newEntries*8,"Large-scale index did not compact sufficiently: "+tag);
        // Demonstrate that the oracle exercises the subtle blend-pass regression.
        // Removing the fine membership test widens blend candidates but not the
        // physically valid nearest influence. At least one test must detect it.
        if(scale>=4)
        {
            for(int i=0;i<segs.Length;i++)
            {
                object s=segs.GetValue(i);
                st.GetField("CellX0").SetValue(s,int.MinValue);st.GetField("CellX1").SetValue(s,int.MaxValue);
                st.GetField("CellZ0").SetValue(s,int.MinValue);st.GetField("CellZ1").SetValue(s,int.MaxValue);
                segs.SetValue(s,i);
            }
            // the index is immutable in production; this deliberate mutation must not be hidden by cached cells
            RiverNetwork.ResetThreadCache();
            foreach(var point in points)
            {
                bool a=now.Value.Nearest(point.X,point.Z,out var h),b=old.Value.Nearest(point.X,point.Z,out var g);
                if(a!=b || (a&&(h.WaterTop!=g.WaterTop||h.Ramp!=g.Ramp)))mutantDifferences++;
            }
        }
        measurements.Add(new{scale,wraps,streamMode,straighten,segments=now.Value.SegmentCount,
            legacyBuckets=legacyGrid.Count,compactBuckets=grid.Count,legacyEntries=oldEntries,compactEntries=newEntries,
            legacyBuildMs=old.Ms,compactBuildMs=now.Ms,legacyAllocatedBytes=old.Bytes,compactAllocatedBytes=now.Bytes});
        Console.WriteLine($"PASS {tag}: entries{oldEntries:N0}->{newEntries:N0}; build{old.Ms:F1}->{now.Ms:F1}ms; alloc{old.Bytes:N0}->{now.Bytes:N0}");
    }
    static bool Same(RiverNetwork current, LegacyRiverNetwork legacy, double x, double z)
    {
        bool a = current.Nearest(x, z, out var h), b = legacy.Nearest(x, z, out var g);
        return a == b && (!a || (Bits(h.Dist,g.Dist) && Bits(h.Q,g.Q) && Bits(h.DirX,g.DirX) && Bits(h.DirZ,g.DirZ)
            && h.WaterTop==g.WaterTop && h.Ramp==g.Ramp && h.Rapid==g.Rapid && h.Mouth==g.Mouth));
    }
    static void CacheIsolation()
    {
        // Two live networks over the same cells (different widths and stream selection), queried alternately on one
        // thread and then chunk by chunk from several threads: the per-thread cell cache must never hand one network's
        // candidates to the other, nor one thread's to another.
        const double scale = 2;
        var geo = Geometry(scale, true); var reaches = Synthetic(geo, scale);
        int Water(double level) => (int)Math.Round(100 + level * .4);
        var a = new RiverNetwork(reaches,geo,100,1.5,Water,"all",20,scale,0,1.5);
        var b = new RiverNetwork(reaches,geo,100,3,Water,"ends",20,scale,.55,1.5);
        var la = new LegacyRiverNetwork(reaches,geo,100,1.5,Water,"all",20,scale,0,1.5);
        var lb = new LegacyRiverNetwork(reaches,geo,100,3,Water,"ends",20,scale,.55,1.5);
        var chunks = new List<(double X,double Z)[]>();
        for (int cz=-12;cz<12;cz++)for(int cx=-14;cx<14;cx++)
        {
            var chunk=new (double X,double Z)[1024];
            for(int j=0;j<32;j++)for(int i=0;i<32;i++)chunk[j*32+i]=(cx*32+i+.5,cz*32+j+.5);
            chunks.Add(chunk);
        }
        long found=0;
        foreach(var chunk in chunks)foreach(var(x,z)in chunk)
        {
            Compare(a,la,x,z,"cache isolation a");Compare(b,lb,x,z,"cache isolation b");
            if(a.Nearest(x,z,out _))found++;
        }
        Require(found>1000,"Cache isolation test found too few rivers");
        long mismatches=0;
        Parallel.For(0,chunks.Count,new ParallelOptions{MaxDegreeOfParallelism=Math.Max(4,Environment.ProcessorCount)},k=>
        {
            var(n,l)=k%2==0?(a,la):(b,lb);
            foreach(var(x,z)in chunks[k])
            {
                if(!Same(n,l,x,z))Interlocked.Increment(ref mismatches);
                if(!Same(k%2==0?b:a,k%2==0?lb:la,x+64,z))Interlocked.Increment(ref mismatches);
            }
        });
        Require(mismatches==0,$"Threaded queries differ: {mismatches}");
        Console.WriteLine($"PASS cache isolation: {chunks.Count*1024*2:N0} alternating + {chunks.Count*1024*2:N0} threaded comparisons");
    }
    static void BuildOptions()
    {
        // The positional constructor and RiverBuildOptions must build the same network for every pool layout generation.
        const double scale = 2;
        var geo = Geometry(scale, true); var reaches = Synthetic(geo, scale);
        int Water(double level) => (int)Math.Round(100 + level * .4);
        var fade = new bool[reaches.Count];
        fade[5] = true;
        int compared = 0;
        foreach (int layout in new[] { 0, 1, 2, 3, 7, 8, 9, 10, 11 })
        {
            var a = new RiverNetwork(reaches, geo, 100, 1.5, Water, "ends", 20, scale, .5, 0, 24, null, null, true, fade, true, 1, layout, layout != 9,
                0, 0, 0);
            var b = new RiverNetwork(reaches, geo, Water, new RiverBuildOptions(100, 1.5)
            {
                StreamMode = "ends", StreamMinQ = 20, SizeScale = scale, Straighten = .5, MinPool = 24, ContinueDownstream = true,
                FadeEnds = fade, GeometrySeams = true, WidthRules = 1, PoolLayout = layout, Rapids = layout != 9,
            });
            Require(a.SegmentCount == b.SegmentCount && a.Drops().SequenceEqual(b.Drops()), $"options: pieces or drops differ, layout {layout}");
            for (int r = 0; r < reaches.Count; r++)
            {
                Require(a.DebugReach(r) == b.DebugReach(r) && a.DebugShapes(r) == b.DebugShapes(r) && a.Kept(r) == b.Kept(r) && a.Fades(r) == b.Fades(r)
                    && Bits(a.ReachSlope(r), b.ReachSlope(r)) && a.ReachRapids(r) == b.ReachRapids(r), $"options: reach {r} differs, layout {layout}");
            }
            var rng = new Random(4410 + layout);
            for (int i = 0; i < 4000; i++)
            {
                double x = (rng.NextDouble() * 1400 - 700) * scale, z = (rng.NextDouble() * 1300 - 650) * scale;
                bool ha = a.Nearest(x, z, out var h), hb = b.Nearest(x, z, out var g);
                Require(ha == hb && (!ha || (Bits(h.Dist, g.Dist) && h.WaterTop == g.WaterTop && h.Ramp == g.Ramp && h.Rapid == g.Rapid
                    && h.Chute == g.Chute && Bits(h.Shoal, g.Shoal) && Bits(h.PlungeDepth, g.PlungeDepth))), $"options: hit differs at {x},{z}, layout {layout}");
                compared++;
            }
        }
        Console.WriteLine($"PASS build options: positional and record constructors equal over 9 pool layouts, {compared:N0} queries");
    }
    /// <summary>A rivers file (EMRV, record version 2) of these reaches, as tools/bake_earth.py writes it.</summary>
    static byte[] RiversFile(IList<RiverReach> reaches, int cut = 0)
    {
        var raw = new MemoryStream();
        var w = new BinaryWriter(raw);
        foreach (var re in reaches)
        {
            w.Write(re.Q); w.Write(re.Next); w.Write(re.Lon.Length);
            for (int k = 0; k < re.Lon.Length; k++) { w.Write(re.Lon[k]); w.Write(re.Lat[k]); w.Write(re.Level[k]); w.Write((byte)(re.Fixed[k] ? 1 : 0)); }
        }
        w.Flush();
        var file = new MemoryStream();
        file.Write("EMRV"u8); file.Write(BitConverter.GetBytes(2)); file.Write(BitConverter.GetBytes(reaches.Count));
        using (var enc = new System.IO.Compression.BrotliStream(file, System.IO.Compression.CompressionLevel.Optimal, true))
            enc.Write(raw.GetBuffer(), 0, (int)raw.Length - cut);
        return file.ToArray();
    }
    static RiverReach Reach(int next, int n, float lon) => new()
    {
        Q = 100 + n, Next = next, Lon = Enumerable.Range(0, n).Select(k => lon + k * .01f).ToArray(), Lat = Enumerable.Range(0, n).Select(k => 50 - k * .01f).ToArray(),
        Level = Enumerable.Range(0, n).Select(k => (short)(40 - k)).ToArray(), Fixed = Enumerable.Range(0, n).Select(k => k == n - 1).ToArray(),
    };
    static void RiversFileChecks()
    {
        // read back as written, the decoded bytes handed over without their copy to an exact array
        var reaches = new List<RiverReach> { Reach(2, 5, 10), Reach(2, 3, 11), Reach(-1, 7, 12), Reach(-1, 1, 13) };
        var back = RiverData.Read(RiversFile(reaches));
        Require(back.Count == reaches.Count, "rivers file: reach count");
        for (int r = 0; r < reaches.Count; r++)
            Require(back[r].Q == reaches[r].Q && back[r].Next == reaches[r].Next && back[r].Lon.SequenceEqual(reaches[r].Lon) && back[r].Lat.SequenceEqual(reaches[r].Lat)
                && back[r].Level.SequenceEqual(reaches[r].Level) && back[r].Fixed.SequenceEqual(reaches[r].Fixed) && back[r].Width == null, $"rivers file: reach {r} differs");
        string Fails(byte[] file, string name = "rivers.bin")
        {
            try { RiverData.Read(file, name); return null; }
            catch (InvalidDataException e) { return e.Message; }
        }
        // audit C R8: the network lays reaches out in file order, so one flowing into an earlier reach (or outside the list) is refused at load
        var backwards = new List<RiverReach>(reaches) { [2] = Reach(1, 7, 12) };
        Require(Fails(RiversFile(backwards), "rivers-glo90.bin") == "rivers-glo90.bin: reach 2 flows into reach 1, not a later one (the reaches must be in flow order)", "rivers file: a reach flowing upstream is refused");
        var itself = new List<RiverReach>(reaches) { [3] = Reach(3, 1, 13) };
        Require(Fails(RiversFile(itself))?.StartsWith("rivers.bin: reach 3 flows into reach 3") == true, "rivers file: a reach flowing into itself is refused");
        var outside = new List<RiverReach>(reaches) { [0] = Reach(4, 5, 10) };
        Require(Fails(RiversFile(outside))?.StartsWith("rivers.bin: reach 0 flows into reach 4") == true, "rivers file: a reach flowing outside the list is refused");
        // a file whose records stop early fails at load, not with zeros from the decode buffer
        Require(Fails(RiversFile(reaches, 9)) == "rivers.bin: reach 3 runs past the end of the data", "rivers file: records cut short are refused");
        // the offset-and-length decode (an outline tile) and the bound on the decoded size
        var packed = RiversFile(reaches);
        var whole = EarthBinary.UnbrotliAll(packed, 12, "rivers.bin");
        var framed = new byte[packed.Length + 7];
        Buffer.BlockCopy(packed, 12, framed, 3, packed.Length - 12);
        Require(EarthBinary.UnbrotliAll(framed, 3, packed.Length - 12, "tile").SequenceEqual(whole), "UnbrotliAll: offset and length decode the same bytes");
        var buffer = EarthBinary.UnbrotliAll(packed, 12, packed.Length - 12, "rivers.bin", out int decoded);
        Require(decoded == whole.Length && buffer.AsSpan(0, decoded).SequenceEqual(whole) && buffer.Skip(decoded).All(b => b == 0), "UnbrotliAll: the uncopied buffer holds the data, then zeros");
        Require(EarthBinary.UnbrotliAll(packed, 12, "rivers.bin", whole.Length).Length == whole.Length, "UnbrotliAll: data of exactly the bound passes");
        bool refused = false;
        try { EarthBinary.UnbrotliAll(packed, 12, "rivers.bin", whole.Length - 1); } catch (InvalidDataException) { refused = true; }
        Require(refused, "UnbrotliAll: data past the bound is refused");
        Console.WriteLine("PASS rivers file: read back, flow order and cut records refused at load; UnbrotliAll's forms agree");
    }
    static void TileCacheIsolation()
    {
        // TileCache keeps each thread's last tile per cache in a slot by the cache's number, 16 slots for all caches of all
        // kinds: with 40 caches of two kinds, slots are shared (numbers 16 apart), and a lookup must still give each cache
        // its own tile, alternately on one thread and from several, and decode each tile once.
        const int caches = 40, keys = 50;
        var decodes = new int[caches * keys];
        var named = Enumerable.Range(0, caches / 2).Select(c => new TileCache<string>(k => { Interlocked.Increment(ref decodes[c * keys + k]); return c + ":" + k; }, 1000)).ToArray();
        var pairs = Enumerable.Range(caches / 2, caches / 2).Select(c => new TileCache<int[]>(k => { Interlocked.Increment(ref decodes[c * keys + k]); return new[] { c, k }; }, 1000)).ToArray();
        bool Right(int c, int k) => c < caches / 2 ? named[c].Get(k) == c + ":" + k : pairs[c - caches / 2].Get(k) is var p && p[0] == c && p[1] == k;
        var rng = new Random(77);
        int wrong = 0;
        for (int i = 0; i < 200_000; i++)
        {
            // runs of one cache and tile (the slot's hits) between switches (its misses, other caches' writes)
            int c = rng.Next(caches), k = rng.Next(keys), run = rng.Next(1, 4);
            for (int j = 0; j < run; j++) if (!Right(c, k)) wrong++;
        }
        long threadWrong = 0;
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            var r = new Random(t);
            for (int i = 0; i < 100_000; i++)
                if (!Right(r.Next(caches), r.Next(keys))) Interlocked.Increment(ref threadWrong);
        });
        Require(wrong == 0 && threadWrong == 0, $"TileCache: {wrong} wrong tiles on one thread, {threadWrong} on several");
        Require(decodes.All(n => n == 1), $"TileCache: a tile decoded {decodes.Max()} times (once expected)");
        // a decode that fails is not kept: the next ask decodes again
        int tries = 0;
        var flaky = new TileCache<string>(k => ++tries == 1 ? throw new InvalidDataException("first") : "ok", 10);
        bool threw = false;
        try { flaky.Get(3); } catch (InvalidDataException) { threw = true; }
        Require(threw && flaky.Get(3) == "ok" && flaky.Get(3) == "ok" && tries == 2, "TileCache: a failed decode is not kept");
        Console.WriteLine($"PASS TileCache: {caches} caches sharing 16 slots, one thread and eight, each tile decoded once");
    }
    static void Main(string[] args)
    {
        if(args.Length > 0 && args[0] == "--real")
        {
            RealDatasetBenchmark.Run(args[1], args[2]);
            return;
        }
        if(args.Length > 0 && args[0] == "--bench")
        {
            RiverQueryBenchmark.Run(args.Skip(1).ToArray());
            return;
        }
        if(args.Length > 0 && args[0] == "--verify-source")
        {
            SourceBaselineAudit.Run(args[1],args[2],args[3]);
            return;
        }
        foreach(double scale in new[]{.5,1,1.01,2,2.3,4,15.625,19.53125,20,32})
        {
            RunCase(scale,false,"all",0);
            RunCase(scale,true,"ends",.55);
            GC.Collect();
        }
        RunCase(19.53125,false,"none",.5);
        CacheIsolation();
        BuildOptions();
        RiversFileChecks();
        TileCacheIsolation();
        RiverQueryChecks.Run();
        Require(hits>1000,"Insufficient actual river hits");
        Require(mutantDifferences>0,"Tests failed to detect omitted blend-pass membership filtering");
        // The report is written only where asked (e.g. tests/RiverIndexTests/results.json when refreshing the evidence).
        string output=args.FirstOrDefault();
        var result=new{status="PASS",queries,distances,candidates,hits,mutantDifferences,measurements};
        if(output!=null)File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS: {queries:N0} Nearest, {distances:N0} distance, {candidates:N0} candidate-order checks; mutant water/ramp differences{mutantDifferences}. Report: {output??"not written (pass a path)"}");
    }
}
