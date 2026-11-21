using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EarthMap;
using EarthMap.Core;

// EA-0194: what a sweep of the whole world (World Map's chart and climate raster, Earth's own map picture) leaves in memory
// and allocates, on a new 1:10 world of this build ("earth-10" play style, made as SavedWorldDigestTests --make makes its
// 1:100 world) with the data store's GLO-90 files.
//   1. Island edges: every island queried on an 8192 x 4096 grid over the globe on all cores, as World Map's chart samples
//      it; the live heap and the edge cache after it, with the cache unbounded (as before EA-0194) and with its budget.
//   2. The same answers: the islands' answers with the budget, and with a budget so small that it evicts all the time, equal
//      the unbounded ones bit for bit, on that grid and on dense patches in archipelagos.
//   3. Bytes allocated per sample by ForestPotential and ClimatePixelForVanillaLapse on the built-in heights (World Map's
//      climate raster), and by their parts, on 4,000 land samples.
// Needs the data store (EARTH_STORE, else the player's or the AgentBridge bench's ModData\earth; exit 3 without it).
//   4. World Map's climate raster (1024 x 512 texels of ForestPotential and the climate pixel) by rows, as World Map 0.5.5
//      bakes it, and by blocks of 8 to 64 texels: the same texels, and the bytes allocated and the time of each.
// Usage: dotnet run -c Release --project tests\SweepMemoryTests -- <repo root> [islands|same|alloc|raster ...]
static class Sweep
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        var parts = args.Length > 1 ? args.Skip(1).ToHashSet() : new HashSet<string> { "islands", "same", "alloc", "raster" };
        string store = FindStore();
        if (store == null) { Console.WriteLine("skip: a 1:10 GLO-90 world needs the data store (EARTH_STORE or ModData\\earth)"); return 3; }
        var sw = Stopwatch.StartNew();
        var data = LoadData(root, store);
        var p = NewWorldParams(root, data, "10");
        var model = Build(root, store, data, p);
        Console.WriteLine($"1:10 world: {model.Geometry.BlocksPerDegreeX:0} blocks a degree, islands {model.Islands?.Count ?? 0} of {model.Islands?.Total ?? 0}, built in {sw.Elapsed.TotalSeconds:0} s; data store {store}");
        if (model.Islands == null) { Console.WriteLine("FAIL this world has no island layer"); return 1; }
        if (parts.Contains("islands")) Islands(data, p, model);
        if (parts.Contains("same")) Same(data, p, model);
        if (parts.Contains("alloc")) Alloc(model);
        if (parts.Contains("raster")) Raster(model);
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 1. island edges after a whole-world sweep

    const int GridW = 8192, GridH = 4096;

    static void Islands(EarthData data, EarthParams p, TerrainModel model)
    {
        foreach (long budget in new[] { long.MaxValue, IslandLayer.DefaultEdgeBudgetBytes })
        {
            var layer = new IslandLayer(data.Islands, model.Geometry, model.Islands.MinWidthBlocks, model.Islands.Rules) { EdgeBudgetBytes = budget };
            long before = Live();
            var sw = Stopwatch.StartNew();
            ulong h = GridHash(layer);
            double secs = sw.Elapsed.TotalSeconds;
            long after = Live();
            string label = budget == long.MaxValue ? "unbounded" : $"budget {budget >> 20} MB";
            Console.WriteLine($"islands, {label}: {GridW} x {GridH} grid in {secs:0.0} s; {layer.BuiltEdges} edge sets held, {layer.EdgeBytes / 1048576.0:0} MB by their count; " +
                              $"live heap {before / 1048576.0:0} -> {after / 1048576.0:0} MB (+{(after - before) / 1048576.0:0}); {layer.EdgesBuilt} built in all; hash {h:X16}");
            if (budget != long.MaxValue)
            {
                Check(layer.EdgeBytes <= budget, $"islands: the edges held after the sweep stay within the budget ({layer.EdgeBytes >> 20} MB of {budget >> 20})");
                Check(after - before < budget + (64L << 20), $"islands: the sweep leaves under the budget plus 64 MB live (+{(after - before) >> 20} MB)");
            }
            GC.KeepAlive(layer);
        }
    }

    /// <summary>Live managed memory after a full blocking, compacting collection.</summary>
    static long Live()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        return GC.GetTotalMemory(false);
    }

    /// <summary>Every grid point's island answer, hashed row by row on all cores and combined in row order.</summary>
    static ulong GridHash(IslandLayer layer)
    {
        var rows = new ulong[GridH];
        Parallel.For(0, GridH, j =>
        {
            ulong h = 14695981039346656037UL;
            double lat = 90 - (j + 0.5) * 180.0 / GridH;
            for (int i = 0; i < GridW; i++)
            {
                double lon = -180 + (i + 0.5) * 360.0 / GridW;
                h = Mix(h, layer, lat, lon);
            }
            rows[j] = h;
        });
        ulong all = 14695981039346656037UL;
        foreach (ulong r in rows) all = Add(all, r);
        return all;
    }

    static ulong Mix(ulong h, IslandLayer layer, double lat, double lon)
    {
        bool found = layer.Query(lat, lon, out var hit);
        h = Add(h, found ? 1UL : 0UL);
        if (!found) return h;
        h = Add(h, hit.Inside ? 1UL : 0UL);
        h = Add(h, (ulong)BitConverter.DoubleToInt64Bits(hit.Dist));
        h = Add(h, (ulong)BitConverter.DoubleToInt64Bits(hit.Height));
        return Add(h, (ulong)BitConverter.DoubleToInt64Bits(hit.ProfileBlocks));
    }

    static ulong Add(ulong h, ulong v)
    {
        for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; }
        return h;
    }

    // ------------------------------------------------------------------ 2. the same answers with eviction

    static readonly (string Name, double Lat0, double Lon0, double Lat1, double Lon1)[] Patches =
    {
        ("Aegean", 36.0, 23.0, 39.5, 28.0), ("Stockholm archipelago", 59.0, 18.0, 60.0, 19.5), ("Philippines", 9.0, 122.0, 12.5, 126.0),
        ("Bahamas", 22.5, -78.5, 26.5, -74.0), ("Dalmatia", 42.8, 15.0, 44.5, 17.5), ("Fiji across 180", -18.0, 177.0, -15.5, -179.0),
        ("Canadian Arctic", 72.0, -100.0, 76.0, -90.0), ("Maldives", 2.5, 72.5, 6.0, 74.0),
    };

    static void Same(EarthData data, EarthParams p, TerrainModel model)
    {
        var geo = model.Geometry;
        ulong[] Run(long budget, out long built)
        {
            var layer = new IslandLayer(data.Islands, geo, model.Islands.MinWidthBlocks, model.Islands.Rules) { EdgeBudgetBytes = budget };
            var hashes = new ulong[Patches.Length + 1];
            hashes[0] = GridHash(layer);
            // each patch: 512 x 512 points, on all cores (threads race to build and evict the same islands)
            for (int k = 0; k < Patches.Length; k++)
            {
                var pt = Patches[k];
                double lonSpan = Geo.Wrap180(pt.Lon1 - pt.Lon0); if (lonSpan <= 0) lonSpan += 360;
                var rows = new ulong[512];
                Parallel.For(0, 512, j =>
                {
                    ulong h = 14695981039346656037UL;
                    double lat = pt.Lat0 + (j + 0.5) * (pt.Lat1 - pt.Lat0) / 512;
                    for (int i = 0; i < 512; i++) h = Mix(h, layer, lat, Geo.Wrap180(pt.Lon0 + (i + 0.5) * lonSpan / 512));
                    rows[j] = h;
                });
                ulong all = 14695981039346656037UL;
                foreach (ulong r in rows) all = Add(all, r);
                hashes[k + 1] = all;
            }
            built = layer.EdgesBuilt;
            return hashes;
        }
        var want = Run(long.MaxValue, out long b0);
        var budgeted = Run(IslandLayer.DefaultEdgeBudgetBytes, out long b1);
        var tiny = Run(256 << 10, out long b2);
        Console.WriteLine($"same: edge sets built: unbounded {b0}, budget {b1}, 256 KB {b2}");
        Check(b2 > b0, "same: the 256 KB budget evicted and rebuilt edges");
        for (int k = 0; k < want.Length; k++)
        {
            string name = k == 0 ? "the 8192 x 4096 grid" : Patches[k - 1].Name;
            Check(budgeted[k] == want[k] && tiny[k] == want[k], $"same: {name}: the answers with the budget and with 256 KB equal the unbounded ones ({want[k]:X16})");
        }
    }

    // ------------------------------------------------------------------ 3. allocation per sample on the built-in heights

    static void Alloc(TerrainModel model)
    {
        var rnd = new Random(194);
        var pts = new List<(double X, double Z)>();
        while (pts.Count < 4000)
        {
            double lat = -56 + 130 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
            { var ts = model.Data.SampleTerrain(lat, lon); if (ts.Ocean >= 0.5 || ts.Lake >= 0.5) continue; }
            model.Geometry.FromGeo(lat, lon, out double x, out double z);
            pts.Add((x, z));
        }
        bool own = FineElevation.BuiltInOnly;
        FineElevation.BuiltInOnly = true;
        try
        {
            var probes = new (string Name, Func<double, double, double> F)[]
            {
                ("ForestPotential", (x, z) => model.ForestPotential(x, z)),
                ("ClimatePixelForVanillaLapse", (x, z) => model.ClimatePixelForVanillaLapse(x, z, 0, 1f, 1f)),
                ("GetSurfaceColumn", (x, z) => model.GetSurfaceColumn(x, z).Top),
                ("ZoneDriversAt", (x, z) => model.ZoneDriversAt(x, z).Aridity),
                ("RiparianWeight(rain 60)", (x, z) => model.RiparianWeight(x, z, 60)),
                ("WarmMonthAtGround", (x, z) => model.WarmMonthAtGround(x, z)),
                ("GetColumn", (x, z) => model.GetColumn(x, z).Elev),
                ("TerrainGeoAt", (x, z) => model.TerrainGeoAt(x, z).Lat),
                ("Data.SampleClimate", (x, z) => { var g = model.TerrainGeoAt(x, z); return model.Data.SampleClimate(g.Lat, g.Lon).TMean; }),
                ("Data.SampleTerrain", (x, z) => { var g = model.TerrainGeoAt(x, z); return model.Data.SampleTerrain(g.Lat, g.Lon).Elev; }),
                ("ClimateAt", (x, z) => { var g = model.TerrainGeoAt(x, z); model.ClimateAt(g.Lat, g.Lon, out double t, out _, out _); return t; }),
                ("Geothermal.Activity", (x, z) => { var g = model.TerrainGeoAt(x, z); return Geothermal.Activity(g.Lat, g.Lon); }),
            };
            foreach (var pr in probes)
            {
                double sink = 0;
                for (int i = 0; i < 200; i++) sink += pr.F(pts[i].X, pts[i].Z);   // warm up (first-use caches)
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                foreach (var (x, z) in pts) sink += pr.F(x, z);
                double us = sw.Elapsed.TotalMilliseconds * 1000 / pts.Count;
                long bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / pts.Count;
                Console.WriteLine($"alloc: {pr.Name,-28} {bytes,9:N0} bytes and {us,7:0.0} µs a sample ({(sink == 12345 ? "" : "")}on the built-in heights)");
            }
        }
        finally { FineElevation.BuiltInOnly = own; }
    }

    // ------------------------------------------------------------------ 4. World Map's climate raster, by rows and by blocks

    /// <summary>World Map's climate raster at 1:10 (its 8192 x 4096 chart / 8): ForestPotential and the climate pixel of
    /// every texel on the built-in heights, on all cores, by rows as World Map 0.5.5 bakes it and by 16 x 16 blocks; the
    /// bytes allocated, the time and the answers (which must be the same).</summary>
    static void Raster(TerrainModel model)
    {
        const int W = 1024, H = 512;
        (double X, double Z) At(int i, int j)
        {
            model.Geometry.FromGeo(90 - (j + 0.5) * 180.0 / H, -180 + (i + 0.5) * 360.0 / W, out double x, out double z);
            return (x, z);
        }
        int Texel(int i, int j)
        {
            var (x, z) = At(i, j);
            bool own = FineElevation.BuiltInOnly;
            FineElevation.BuiltInOnly = true;
            try { return (int)Math.Round(model.ForestPotential(x, z) * 255) << 24 ^ model.ClimatePixelForVanillaLapse(x, z, 0, 1f, 1f); }
            finally { FineElevation.BuiltInOnly = own; }
        }
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        ulong Hash(int[] px) { ulong h = 14695981039346656037UL; foreach (int v in px) h = Add(h, (uint)v); return h; }
        var byRows = new int[W * H];
        Live();
        long a0 = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        Parallel.For(0, H, opts, j => { for (int i = 0; i < W; i++) byRows[j * W + i] = Texel(i, j); });
        double rowSecs = sw.Elapsed.TotalSeconds;
        long rowBytes = GC.GetTotalAllocatedBytes(true) - a0;
        Console.WriteLine($"raster {W} x {H} on {opts.MaxDegreeOfParallelism} threads: by rows {rowSecs:0.0} s, {rowBytes / 1e9:0.0} GB allocated");
        foreach (int B in new[] { 8, 16, 32, 64 })
        {
            var byBlocks = new int[W * H];
            Live();
            a0 = GC.GetTotalAllocatedBytes(true);
            sw.Restart();
            Parallel.For(0, (W / B) * (H / B), opts, b =>
            {
                int bi = b % (W / B) * B, bj = b / (W / B) * B;
                for (int j = bj; j < bj + B; j++) for (int i = bi; i < bi + B; i++) byBlocks[j * W + i] = Texel(i, j);
            });
            double blockSecs = sw.Elapsed.TotalSeconds;
            long blockBytes = GC.GetTotalAllocatedBytes(true) - a0;
            Console.WriteLine($"raster by {B} x {B} blocks: {blockSecs:0.0} s, {blockBytes / 1e9:0.0} GB allocated");
            Check(Hash(byRows) == Hash(byBlocks), $"raster: the same texels by rows and by {B} x {B} blocks ({Hash(byBlocks):X16})");
        }
    }

    // ------------------------------------------------------------------ the world (SavedWorldDigestTests' Make and Build)

    static string FindStore()
    {
        string env = Environment.GetEnvironmentVariable("EARTH_STORE");
        if (env == "none") return null;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (string s in new[] { env, Path.Combine(appData, "VintagestoryData", "ModData", "earth"), Path.Combine(appData, "VintagestoryData-agentbridge", "ModData", "earth") })
            if (!string.IsNullOrEmpty(s) && Directory.Exists(Path.Combine(s, "data")) && Directory.Exists(Path.Combine(s, "glo90"))) return s;
        return null;
    }

    static EarthData LoadData(string root, string store) => EarthData.Load(name =>
    {
        string a = Path.Combine(root, "earthdata", name);
        if (File.Exists(a)) return File.OpenRead(a);
        string b = Path.Combine(store, "data", name);
        return File.Exists(b) ? File.OpenRead(b) : null;
    }, true);

    /// <summary>A new world of the "earth-&lt;scale&gt;" play style with this build's defaults, seed 386217.</summary>
    static EarthParams NewWorldParams(string root, EarthData data, string scale)
    {
        var wc = new Vintagestory.API.Datastructures.TreeAttribute();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json"))))
            foreach (var kv in doc.RootElement.GetProperty("playStyles").EnumerateArray().First(s => s.GetProperty("code").GetString() == "earth-" + scale).GetProperty("worldConfig").EnumerateObject())
                switch (kv.Value.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.True: case System.Text.Json.JsonValueKind.False: wc.SetBool(kv.Name, kv.Value.GetBoolean()); break;
                    case System.Text.Json.JsonValueKind.Number: wc.SetInt(kv.Name, kv.Value.GetInt32()); break;
                    default: wc.SetString(kv.Name, kv.Value.GetString()); break;
                }
        SpawnSearch.SizeFor(scale, out int W, out int L, out _);
        int H = wc.GetInt("earthWorldHeight", 384);
        var w = new NewWorld
        {
            MapSizeX = W, MapSizeY = H, SeaLevel = EarthWorldOptions.Terrain(wc, H).SeaLevel(H, 165), Seed = 386217, WorldConfig = wc, HydrologyQuery = 3,
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 672,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 70, CylinderKeepsEarthSeam = () => true,
        };
        var log = DispatchProxy.Create<Vintagestory.API.Common.ILogger, QuietLog>();
        var p = WorldParamsBuilder.Build(w, new EarthMapConfig(), data, log, out _, out string bad);
        if (bad.Length > 0) throw new InvalidOperationException("this build's defaults can't make terrain: " + bad);
        return p;
    }

    static TerrainModel Build(string root, string store, EarthData data, EarthParams p)
    {
        if (p.FineElevation < 1) return p.BuildModel(data);
        string g = Path.Combine(store, "glo90");
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(File.ReadAllBytes(Path.Combine(g, DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules))))) : null;
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(File.ReadAllBytes(Path.Combine(g, RiverFlowData.FileName))) : null;
        var model = p.BuildModel(data, dams, null, flow);
        var fine = new FineElevation(g, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
        fine.SetFallback(Array.Empty<int>());
        model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
        model.Fine = fine;
        if (dams != null && p.DamCourseError == null) fine.Dams = dams;
        if (p.LakeOutlines >= 1)
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(g, LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion))))), model.Geometry);
        if (p.RiverOutlines >= 1)
        {
            int v = RiverOutlineData.Usable(p.RiverOutlines);
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(g, RiverOutlineData.FileName(v)))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
        }
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
        return model;
    }
}

/// <summary>An ILogger that says nothing.</summary>
public class QuietLog : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
        => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
}
