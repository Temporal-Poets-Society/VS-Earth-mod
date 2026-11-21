using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using EarthMap.Core;
using RiverSnap;

// Rivers in GLO-90's valleys (docs/research/river-snap-2026-11-05.md).
//   RiverSnap <rivers.bin> <data/glo90-tiles.bin> <GLO-90 store> <HydroLAKES_points_v10.dbf> <output.bin> [key=value ...]
// Reads rivers.bin v2, snaps every reach's course to the valley floor of the converted GLO-90 store (FineElevation's
// folder), takes the levels from GLO-90 by bake_rivers.py's rules, simplifies the lines and writes a new rivers.bin v2
// (never over an existing file). Keys: see Params and the switch below; box=latMin,latMax,lonMin,lonMax (repeatable)
// snaps only the chains reaching into the boxes and copies the rest unchanged (for tuning).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 5)
{
    Console.WriteLine("usage: RiverSnap <rivers.bin> <glo90-tiles.bin> <store folder> <HydroLAKES_points_v10.dbf|-> <output.bin> [key=value ...]");
    return 2;
}
var total = Stopwatch.StartNew();
void Log(string s) => Console.WriteLine($"[{total.Elapsed.TotalSeconds,7:F1}s] {s}");
var P = new Params();
int threads = Environment.ProcessorCount, cacheTiles = 2500, quality = 11, window = 24;
double tol = 20, vtol = 2, maxSeg = 5000, carveWeight = 0.5, quant = 0;
int smoothIters = 60;
bool selfTest = true;
string fit = "l1";
string dumpPath = null;
int frozenFrom = int.MaxValue;
var variants = new List<(double Tol, double Vtol, double MaxSeg, double Quant)>();
var boxes = new List<(double La0, double La1, double Lo0, double Lo1)>();
foreach (var a in args.Skip(5))
{
    var kv = a.Split('=', 2);
    if (kv.Length != 2) { Console.WriteLine("bad argument " + a); return 2; }
    double D() => double.Parse(kv[1], CultureInfo.InvariantCulture);
    switch (kv[0].ToLowerInvariant())
    {
        case "threads": threads = (int)D(); break;
        case "cache": cacheTiles = (int)D(); break;
        case "quality": quality = (int)D(); break;
        case "window": window = (int)D(); break;
        case "tol": tol = D(); break;
        case "vtol": vtol = D(); break;
        case "maxseg": maxSeg = D(); break;
        case "carve": carveWeight = D(); break;
        case "fit": fit = kv[1].ToLowerInvariant(); if (fit != "l1" && fit != "avg") { Console.WriteLine("fit=l1|avg"); return 2; } break;
        case "barriercut": P.BarrierCut = D(); break;
        case "barrierpad": P.BarrierPad = (int)D(); break;
        case "smooth": smoothIters = (int)D(); break;
        case "quant": quant = D(); break;
        case "selftest": selfTest = D() != 0; break;
        case "dump": dumpPath = kv[1]; break;
        case "variants":
            // tol:vtol:maxseg:quant per variant, comma-separated: sizes logged, not written
            foreach (var vs in kv[1].Split(','))
            {
                var v = vs.Split(':').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                variants.Add((v[0], v[1], v[2], v[3]));
            }
            break;
        case "debug":
            var dbg = kv[1].Split(',');
            Worker.DebugAt = (double.Parse(dbg[0], CultureInfo.InvariantCulture), double.Parse(dbg[1], CultureInfo.InvariantCulture), dbg[2]);
            break;
        case "ds": P.Ds = D(); break;
        case "hmax": P.HMax = D(); break;
        case "lambda": P.Lambda = D(); break;
        case "lambdawidth": P.LambdaWidth = D(); break;
        case "pull": P.Pull = D(); break;
        case "slope": P.Slope = D(); break;
        case "othershare": P.OtherShare = D(); break;
        case "junctionfree": P.JunctionFree = D(); break;
        case "passes":
            // H0:Hq:Dl:LambdaMul:RefSigma:DirSigma per pass, comma-separated
            P.Passes = kv[1].Split(',').Select(s =>
            {
                var v = s.Split(':').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                return new Pass { H0 = v[0], Hq = v[1], Dl = v[2], LambdaMul = v[3], RefSigma = v[4], DirSigma = v[5] };
            }).ToList();
            break;
        case "offsigma": P.OffSigma = D(); break;
        case "floormin": P.FloorMin = D(); break;
        case "floorhalfwidth": P.FloorHalfWidth = D(); break;
        case "openk": P.OpenK = (int)D(); break;
        // (EA-0131) reaches from this index on are rivers already laid out: held where they are, the rest end on them
        case "frozenfrom": frozenFrom = (int)D(); break;
        case "box":
            var b = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            boxes.Add((b[0], b[1], b[2], b[3]));
            break;
        default: Console.WriteLine("unknown key " + kv[0]); return 2;
    }
}
string outPath = args[4];
if (File.Exists(outPath)) { Console.WriteLine(outPath + " exists; not replaced"); return 1; }
Log($"parameters: ds {P.Ds}; passes {string.Join(", ", P.Passes)} (H max {P.HMax}); lambda {P.Lambda} (width {P.LambdaWidth}) pull {P.Pull} slope {P.Slope} " +
    $"other {P.OtherShare} (junctions {P.JunctionFree}) offsigma {P.OffSigma} floor {P.FloorMin}/{P.FloorHalfWidth} open {P.OpenK}; " +
    $"barriers {P.BarrierCut} m/{P.BarrierPad}; fit {fit}" + (fit == "avg" ? $" (carve {carveWeight})" : "") + $", smooth {smoothIters}; tol {tol} vtol {vtol} maxseg {maxSeg} quant {quant}; threads {threads} cache {cacheTiles} tiles");

var reaches = RiverFile.Read(args[0]);
long vin = reaches.Sum(r => (long)r.Lon.Length);
Log($"{args[0]}: {reaches.Count} reaches, {vin} vertices, {new FileInfo(args[0]).Length / 1e6:F2} MB");
var net = new Network(reaches, frozenFrom);
if (frozenFrom < reaches.Count) Log($"reaches from {frozenFrom} on frozen ({reaches.Count - frozenFrom})");
Log($"{net.Chains.Count} chains, deepest tributary level {net.MaxDepth}");

var fine = new FineElevation(args[2], File.ReadAllBytes(args[1])) { Capacity = 8 };
var store = new TileStore(fine, cacheTiles);
if (selfTest)
{
    // the sampler must give FineElevation's heights (the game's): random points in a few squares, across edges
    var rnd = new Random(1);
    var smp = new Sampler(store);
    var squares = new[] { (50, 7), (36, -113), (28, 83), (-4, -61), (57, 24), (59, 10), (49, 7) };
    int checkedPts = 0;
    double worst = 0;
    foreach (var (la, lo) in squares)
    {
        if (!fine.SourceHas(la, lo) || !fine.IsHere(la, lo)) continue;
        for (int i = 0; i < 4000; i++)
        {
            double lat = la + (i < 200 ? rnd.NextDouble() * 0.002 : rnd.NextDouble()), lon = lo + (i % 3 == 0 ? 0.9995 + rnd.NextDouble() * 0.001 : rnd.NextDouble());
            if (!fine.TryHeight(lat, lon, out double h0)) continue;
            double h1 = smp.Height(lat, lon);
            worst = Math.Max(worst, Math.Abs(h0 - h1));
            checkedPts++;
        }
    }
    Log($"self-test: {checkedPts} points against FineElevation.TryHeight, largest difference {worst:G3} m");
    if (worst > 1e-6) { Console.WriteLine("self-test failed"); return 1; }
}

// chains to snap
var chains = net.Chains;
bool InBoxes(Chain c)
{
    if (boxes.Count == 0 || c.Frozen) return true;
    foreach (int r in c.Reaches)
    {
        var re = reaches[r];
        for (int k = 0; k < re.Lon.Length; k++)
            foreach (var bx in boxes)
                if (re.Lat[k] >= bx.La0 && re.Lat[k] <= bx.La1 && re.Lon[k] >= bx.Lo0 && re.Lon[k] <= bx.Lo1) return true;
    }
    return false;
}
var sel = chains.Where(InBoxes).ToList();
// layout (station counts), then the station numbering: deepest tributaries first (flow order)
Parallel.ForEach(Partitioner.Create(0, sel.Count, 256), new ParallelOptions { MaxDegreeOfParallelism = threads },
    () => new Worker(net, P, null, store), (range, _, w) => { for (int i = range.Item1; i < range.Item2; i++) w.Layout(sel[i]); return w; }, _ => { });
long nst = 0;
foreach (var c in sel.OrderByDescending(c => c.Depth).ThenBy(c => c.Index))
{
    c.StationStart = (int)nst;
    nst += c.StationCount;
}
if (nst > int.MaxValue) throw new InvalidOperationException("too many stations");
Log($"{sel.Count} chains to snap, {nst} stations ({P.Ds} m)");
foreach (var c in sel)
{
    var re = reaches[c.Reaches[c.Reaches.Length / 2]];
    int k = re.Lon.Length / 2;
    uint x = (uint)Math.Clamp((re.Lon[k] + 180) / 360 * 65535, 0, 65535), y = (uint)Math.Clamp((re.Lat[k] + 90) / 180 * 65535, 0, 65535);
    long m = 0;
    for (int bit = 0; bit < 16; bit++) m |= (long)((x >> bit) & 1) << (2 * bit) | (long)((y >> bit) & 1) << (2 * bit + 1);
    c.Morton = m;
}
var G = new Stations((int)nst);
// snap, parents before their tributaries (a tributary ends at its main river's snapped junction)
long fallbacks = 0, nans = 0, overshoot = 0, samples = 0, done = 0, otherLimited = 0, bendLimited = 0, barrierStations = 0, barrierChains = 0;
var snapTime = Stopwatch.StartNew();
using (var timer = new Timer(_ =>
{
    long d = Interlocked.Read(ref done);
    Log($"  {d} of {nst} stations ({100.0 * d / Math.Max(1, nst):F1} %), tiles loaded {store.Loads} (evicted {store.Evictions}, cached {store.Cached}), " +
        $"{GC.GetTotalMemory(false) / 1e9:F1} GB managed");
}, null, 30000, 30000))
{
    for (int depth = 0; depth <= net.MaxDepth; depth++)
    {
        var level = sel.Where(c => c.Depth == depth).ToList();
        if (level.Count == 0) continue;
        // long rivers first (they take a thread for a while), then the rest in Morton order: the threads work on
        // neighbouring rivers, sharing tiles
        var order = level.Where(c => c.StationCount > 20000).OrderByDescending(c => c.StationCount)
            .Concat(level.Where(c => c.StationCount <= 20000).OrderBy(c => c.Morton)).ToList();
        Parallel.ForEach(Partitioner.Create(order, EnumerablePartitionerOptions.NoBuffering), new ParallelOptions { MaxDegreeOfParallelism = threads },
            () => new Worker(net, P, G, store),
            (c, _, w) =>
            {
                long before = w.StationsDone;
                w.Process(c);
                Interlocked.Add(ref done, w.StationsDone - before);
                return w;
            },
            w =>
            {
                Interlocked.Add(ref fallbacks, w.Fallbacks);
                Interlocked.Add(ref nans, w.NanSamples);
                Interlocked.Add(ref overshoot, w.Overshoot);
                Interlocked.Add(ref samples, w.Samples);
                Interlocked.Add(ref otherLimited, w.OtherLimited);
                Interlocked.Add(ref bendLimited, w.BendLimited);
                Interlocked.Add(ref barrierStations, w.BarrierStations);
                Interlocked.Add(ref barrierChains, w.BarrierChains);
            });
    }
}
Log($"snapped in {snapTime.Elapsed.TotalSeconds:F1} s: {samples / 1e9:F2} G samples, {store.Loads} tile loads ({store.BytesRead / 1e9:F2} GB read, " +
    $"{store.Evictions} evicted, {store.MissingTiles} missing), {nans} samples without data, {fallbacks} constraint fallbacks, " +
    $"{overshoot} tributary ends pulled back; profiles shortened by bends {bendLimited}, by other rivers {otherLimited} (station-passes); " +
    $"{barrierStations} stations in {barrierChains} chains back towards the original line at barriers");

// levels
var res = args[3] == "-" ? null : HydroLakes.Reservoirs(args[3]);
var lt = Stopwatch.StartNew();
var rep = Levels.Run(G, res, carveWeight, smoothIters, P.Ds, fit == "l1" ? sel : null);
Log($"levels in {lt.Elapsed.TotalSeconds:F1} s: de-dammed {rep.Dedammed} reservoir stretches ({rep.DedammedStations} stations) of {rep.Reservoirs} reservoirs; " +
    $"{rep.Conflicts} lake/sea stations lower than one downstream; {rep.NoFloor} stations without a floor (original level)");

if (dumpPath != null)
{
    // per station: lon, lat (float64), floor, original level, level (float32), next (int32), fixed (uint8), Q (float32)
    using var f = new BinaryWriter(new FileStream(dumpPath, FileMode.CreateNew));
    for (int v = 0; v < G.Count; v++)
    {
        f.Write(G.Lon[v]); f.Write(G.Lat[v]); f.Write(G.Floor[v]); f.Write(G.OrigLev[v]); f.Write((float)G.Level[v]);
        f.Write(G.Next[v]); f.Write((byte)(G.Fixed[v] ? 1 : 0)); f.Write(G.Q[v]);
    }
    Log($"stations written to {dumpPath}");
}
// station statistics
{
    var shift = new List<float>();
    var dl = new List<float>();
    var dOrig = new List<float>();
    for (int v = 0; v < G.Count; v += 7)
    {
        if (G.Fixed[v]) continue;
        shift.Add(G.Shift[v]);
        if (!float.IsNaN(G.Floor[v])) dl.Add((float)(G.Level[v] - G.Floor[v]));
        dOrig.Add((float)(G.Level[v] - G.OrigLev[v]));
    }
    string Pct(List<float> l) { l.Sort(); return string.Join(" ", new[] { 1, 10, 50, 90, 99 }.Select(p => $"p{p} {l[(int)((l.Count - 1) * p / 100.0)]:F1}")); }
    Log($"course moved (m): {Pct(shift)}");
    Log($"level - floor (m): {Pct(dl)}");
    Log($"level - original level (m): {Pct(dOrig)}");
}

// simplify each reach between its anchors (ends, lake and sea vertices)
const double Mdeg = 111195.0;
List<Reach> Simplify(double tol, double vtol, double maxSeg, double quant)
{
var outReaches = new Reach[reaches.Count];
float Qz(double v) => quant > 0 ? (float)(Math.Round(v / quant) * quant) : (float)v;
Parallel.For(0, reaches.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => (new List<int>(), new bool[16], new double[16], new double[16]), (r, _, local) =>
{
    var re = reaches[r];
    var c = chains[net.ChainOf[r]];
    if (!c.Processed || c.Frozen) { outReaches[r] = re; return local; }
    int m = net.PosInChain[r];
    int a = c.StationStart + c.ReachStart[m];
    int b = m + 1 < c.Reaches.Length ? c.StationStart + c.ReachStart[m + 1] : c.StationStart + c.StationCount - 1;
    int cnt = b - a + 1;
    var (outIdx, keep, xs, ys) = local;
    if (keep.Length < cnt) { keep = new bool[cnt * 2]; xs = new double[cnt * 2]; ys = new double[cnt * 2]; }
    // local metres (longitude unwrapped along the reach)
    double lat0 = G.Lat[a], cl = Math.Cos(lat0 * Math.PI / 180), lon = G.Lon[a];
    for (int i = 0; i < cnt; i++)
    {
        int v = a + i;
        if (i > 0) lon += ((G.Lon[v] - G.Lon[v - 1]) % 360 + 540) % 360 - 180;
        xs[i] = (lon - G.Lon[a]) * cl * Mdeg;
        ys[i] = (G.Lat[v] - lat0) * Mdeg;
        keep[i] = i == 0 || i == cnt - 1 || G.Anchor[v] && G.Fixed[v];
    }
    var stack = new Stack<(int, int)>();
    int prev = 0;
    for (int i = 1; i < cnt; i++)
    {
        if (!keep[i]) continue;
        stack.Push((prev, i));
        prev = i;
    }
    while (stack.Count > 0)
    {
        var (i0, i1) = stack.Pop();
        if (i1 <= i0 + 1) continue;
        double ax = xs[i0], ay = ys[i0], dx = xs[i1] - ax, dy = ys[i1] - ay, l2 = dx * dx + dy * dy;
        double la = G.Level[a + i0], lb = G.Level[a + i1];
        int best = -1;
        double bestE = 1;
        for (int i = i0 + 1; i < i1; i++)
        {
            double px = xs[i] - ax, py = ys[i] - ay;
            double t = l2 > 0 ? Math.Clamp((px * dx + py * dy) / l2, 0, 1) : 0;
            double ex = px - t * dx, ey = py - t * dy;
            double e = Math.Max(Math.Sqrt(ex * ex + ey * ey) / tol, Math.Abs(G.Level[a + i] - (la + t * (lb - la))) / vtol);
            if (e > bestE) { bestE = e; best = i; }
        }
        if (best < 0 && l2 > maxSeg * maxSeg) best = (i0 + i1) / 2;
        if (best < 0) continue;
        keep[best] = true;
        stack.Push((i0, best));
        stack.Push((best, i1));
    }
    outIdx.Clear();
    for (int i = 0; i < cnt; i++) if (keep[i]) outIdx.Add(a + i);
    var o = new Reach { Q = re.Q, Next = re.Next, Lon = new float[outIdx.Count], Lat = new float[outIdx.Count], Level = new short[outIdx.Count], Fixed = new bool[outIdx.Count] };
    for (int i = 0; i < outIdx.Count; i++)
    {
        int v = outIdx[i];
        bool fx = G.Fixed[v] && G.Anchor[v];
        o.Lon[i] = fx ? (float)G.Lon[v] : Qz(G.Lon[v]);
        o.Lat[i] = fx ? (float)G.Lat[v] : Qz(G.Lat[v]);
        o.Level[i] = (short)Math.Clamp(Math.Round(G.Level[v]), -1000, 9000);
        o.Fixed[i] = G.Fixed[v];
    }
    outReaches[r] = o;
    local.Item2 = keep; local.Item3 = xs; local.Item4 = ys;
    return local;
}, _ => { });
return outReaches.ToList();
}
if (variants.Count > 0)
{
    // simplified one after the other (each in parallel), compressed side by side (Brotli runs on one thread)
    var sw = Stopwatch.StartNew();
    var lists = variants.Select(vr => Simplify(vr.Tol, vr.Vtol, vr.MaxSeg, vr.Quant)).ToList();
    var sizes = new long[variants.Count];
    Parallel.For(0, variants.Count, i => sizes[i] = RiverFile.Size(lists[i], quality, window));
    for (int i = 0; i < variants.Count; i++)
    {
        var vr = variants[i];
        long vv = lists[i].Sum(r => (long)r.Lon.Length);
        Log($"variant tol {vr.Tol} vtol {vr.Vtol} maxseg {vr.MaxSeg} quant {vr.Quant}: {vv} vertices (x{(double)vv / vin:F2}), {sizes[i] / 1e6:F2} MB (brotli {quality}/{window})");
    }
    Log($"variants in {sw.Elapsed.TotalSeconds:F0} s");
}
var outList = Simplify(tol, vtol, maxSeg, quant);
long vout = outList.Sum(r => (long)r.Lon.Length);

// checks: joints, levels never rising, lake and sea vertices kept
int jointMismatch = 0, risesIn = 0, risesInFixed = 0, risesJoint = 0, fixedLost = 0, mixedJoints = 0;
for (int r = 0; r < outList.Count; r++)
{
    var o = outList[r];
    for (int k = 1; k < o.Level.Length; k++)
        if (o.Level[k] > o.Level[k - 1]) { if (o.Fixed[k] || o.Fixed[k - 1]) risesInFixed++; else risesIn++; }
    if (o.Next >= 0)
    {
        var nx = outList[o.Next];
        // (with boxes, a joint between a snapped and an unsnapped reach is not comparable)
        if (ReferenceEquals(o, reaches[r]) != ReferenceEquals(nx, reaches[o.Next])) { mixedJoints++; continue; }
        if (o.Lon[^1] != nx.Lon[0] || o.Lat[^1] != nx.Lat[0]) jointMismatch++;
        if (nx.Level[0] > o.Level[^1]) risesJoint++;
    }
    var re = reaches[r];
    if (ReferenceEquals(re, o)) continue;
    int j = 0;
    for (int k = 0; k < re.Lon.Length; k++)
    {
        if (!re.Fixed[k]) continue;
        // (a joint's first vertex was merged with the reach before; its flag may come from either)
        bool found = false;
        for (int t = 0; t < o.Lon.Length && !found; t++) found = o.Fixed[t] && o.Lon[t] == re.Lon[k] && o.Lat[t] == re.Lat[k];
        if (!found) fixedLost++;
        j++;
    }
}
Log($"checks: {jointMismatch} joints apart, {risesIn} rises within reaches (+{risesInFixed} at lake/sea vertices), {risesJoint} rises at joints, " +
    $"{fixedLost} lake/sea vertices not kept" + (mixedJoints > 0 ? $"; {mixedJoints} joints between snapped and unsnapped reaches not compared" : ""));
int fixedRisesOrig = 0;
foreach (var re in reaches) for (int k = 1; k < re.Level.Length; k++) if (re.Level[k] > re.Level[k - 1]) fixedRisesOrig++;
Log($"(input: {fixedRisesOrig} rises within reaches)");

var wt = Stopwatch.StartNew();
long size = RiverFile.Write(outPath, outList, quality, window);
Log($"{outPath}: {outList.Count} reaches, {vout} vertices (input {vin}, x{(double)vout / vin:F2}), {size / 1e6:F2} MB (brotli {quality}/{window}, {wt.Elapsed.TotalSeconds:F0} s)");
Log($"peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:F1} GB; total {total.Elapsed.TotalSeconds:F0} s");
return 0;
