using System.Diagnostics;
using EarthMap.Core;

// RiverNetwork start-up on a 1:10 world (ring 4 096 000, ~9.8 m per block) with rivers.bin and with rivers-glo90.bin.
//   dotnet StartupBench.dll <earthdata folder> <rivers-glo90.bin> [ring] [file:courses:straighten:smooth,...]
// Per configuration: river ends and deltas, the network (straightening, pools, index), its segments, the managed memory
// it allocates and keeps, and how far the laid-out courses lie from the reaches they were laid out from (every 10th kept
// reach; metres).
string folder = args[0], gloPath = args[1];
int ring = args.Length > 2 ? int.Parse(args[2]) : 4096000;
var data = EarthData.LoadFromFolder(folder, true);
data.SetLakeOverlay(true);
var geo = EarthLayout.Build(new LayoutInput
{
    WorldWidth = ring + 1024, GutterMargin = 512, Gutter = false, CwRingStart = 512, CwRingLength = ring,
    CwEquatorZ = ring / 4.0, CwPoleDistance = ring / 4.0 - 48, CwBandInner = 608,
}, data, out _);
var model = new TerrainModel(data, geo, VerticalScale.ForWorld(1024, 160), 386217) { RiverRelief = 1 };
double mpb = 111320.0 / geo.BlocksPerDegreeZ, sizeScale = geo.RingLength / 204800.0;
Console.WriteLine($"ring {geo.RingLength}, {mpb:F2} m per block, size scale {sizeScale:F2}; {Environment.ProcessorCount} threads");

List<RiverReach> Read(string path) { using var f = File.OpenRead(path); return RiverData.Read(f); }
var sources = new Dictionary<string, List<RiverReach>> { ["rivers.bin"] = data.RiverReaches(), ["rivers-glo90.bin"] = Read(gloPath) };
// configurations: file:courses:straighten:smooth, comma-separated (args[3]); by default the four of the note
var configs = args.Length > 3
    ? args[3].Split(',').Select(c => c.Split(':')).Select(p => (File: p[0], Courses: int.Parse(p[1]), Straighten: double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
        Smooth: double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture))).ToArray()
    : new (string File, int Courses, double Straighten, double Smooth)[]
    {
        ("rivers.bin", 0, 0.5, 2),
        ("rivers-glo90.bin", 0, 0.5, 2),
        ("rivers-glo90.bin", 1, 0.5, 2),
        ("rivers-glo90.bin", 1, 0, 0),
    };
foreach (var cfg in configs)
{
    var source = sources[cfg.File];
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long m0 = GC.GetTotalMemory(true), a0 = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    var ends = RiverEnds.Prepare(data, source);
    var reaches = RiverDeltas.Apply(ends.Reaches).Reaches;
    double tEnds = sw.Elapsed.TotalSeconds;
    sw.Restart();
    var net = new RiverNetwork(reaches, geo, 100, 1.5, model.RiverWaterTop, "ends", 20, sizeScale, cfg.Straighten, cfg.Smooth,
        falls: RiverFalls.All, continueDownstream: true, fadeEnds: ends.Fade, geometrySeams: true,
        widthRules: RiverNetwork.WidthRulesVersion, poolLayout: RiverNetwork.PoolLayoutVersion, rapids: true,
        courses: cfg.Courses, landCellMetres: cfg.Courses >= 1 ? FineElevation.CellMetres : 0);
    double tNet = sw.Elapsed.TotalSeconds;
    long allocated = GC.GetTotalAllocatedBytes(true) - a0;
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long kept = GC.GetTotalMemory(true) - m0;
    long vIn = 0, vKept = 0;
    int nKept = 0;
    for (int r = 0; r < reaches.Count; r++)
    {
        if (!net.Kept(r)) continue;
        nKept++;
        vIn += reaches[r].Lon.Length;
        if (net.ReachLine(r, out var lx, out _)) vKept += lx.Length;
    }
    // laid-out course against the reach it came from
    var dist = new List<double>();
    for (int r = 0; r < reaches.Count; r += 10)
    {
        if (!net.Kept(r) || !net.ReachLine(r, out var lx, out var lz)) continue;
        var re = reaches[r];
        int n = re.Lon.Length;
        var ix = new double[n]; var iz = new double[n];
        bool seam = false;
        for (int k = 0; k < n; k++)
        {
            geo.FromGeo(re.Lat[k], re.Lon[k], out ix[k], out iz[k]);
            if (k > 0 && Math.Abs(ix[k] - ix[k - 1]) > geo.RingLength / 2.0) seam = true;
        }
        if (seam || n < 2) continue;
        for (int i = 0; i < lx.Length; i++)
        {
            double best = double.MaxValue;
            for (int k = 0; k + 1 < n; k++)
            {
                double dx = ix[k + 1] - ix[k], dz = iz[k + 1] - iz[k], l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? Math.Clamp(((lx[i] - ix[k]) * dx + (lz[i] - iz[k]) * dz) / l2, 0, 1) : 0;
                double ex = lx[i] - ix[k] - t * dx, ez = lz[i] - iz[k] - t * dz;
                best = Math.Min(best, ex * ex + ez * ez);
            }
            dist.Add(Math.Sqrt(best) * mpb);
        }
    }
    dist.Sort();
    double P(double p) => dist.Count == 0 ? double.NaN : dist[(int)((dist.Count - 1) * p)];
    Console.WriteLine($"{cfg.File,-17} courses {cfg.Courses} straighten {cfg.Straighten} smooth {cfg.Smooth}: ends+deltas {tEnds:F1} s, network {tNet:F1} s; " +
        $"{nKept} kept reaches, {vIn} vertices in, {vKept} laid out, {net.SegmentCount} segments; allocated {allocated / 1e9:F2} GB, kept {kept / 1e6:F0} MB; " +
        $"laid-out course from the reach (m): p50 {P(0.5):F1} p90 {P(0.9):F1} p99 {P(0.99):F1} (max {P(1):F0}, {dist.Count} points)");
    GC.KeepAlive(net);
}
Console.WriteLine($"peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:F1} GB");
