using System.Diagnostics;
using EarthMap.Core;

// Decision 0051's tile reader, codec and lookup, on published GLO-90 tiles in a local folder.
// usage: ElevationTests <folder with Copernicus_DSM_COG_30_*.tif> <tile list .bin> [<work folder>] [<whole source>]
//        [--store=<a store of .egl tiles>]: also the data release (a small one built from that store's files)
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

string storeArg = args.FirstOrDefault(a => a.StartsWith("--store="))?.Substring(8);
args = args.Where(a => !a.StartsWith("--")).ToArray();
if (args.Length < 2)
{
    // these checks read real GLO-90 tiles, which no repository folder holds
    Console.WriteLine("usage: ElevationTests <folder with Copernicus_DSM_COG_30_*.tif> <tile list .bin> [<work folder>] [<whole source>] [--store=<a store of .egl tiles>]");
    Console.WriteLine("Nothing checked: give the folder of published GLO-90 tiles and the tile list (data/glo90-tiles.bin).");
    return 0;
}
string src = args[0], list = args[1];
string work = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "earth-elevation-test");
if (Directory.Exists(work)) Directory.Delete(work, true);
Directory.CreateDirectory(work);

// names
Check(DemTiles.ShortName(60, 6) == "N60E006" && DemTiles.ShortName(-1, -75) == "S01W075", "short names");
Check(DemTiles.SourceName(60, 6) == "Copernicus_DSM_COG_30_N60_00_E006_00_DEM", "source name");
Check(DemTiles.TryParseSourceName("Copernicus_DSM_COG_30_S01_00_W075_00_DEM", out int pa, out int po) && pa == -1 && po == -75, "parse source name");

// every source tile in the folder: read, convert, round trip
long cells = 0, packed = 0, source = 0; double readMs = 0, encMs = 0, decMs = 0; int tiles = 0;
foreach (var path in Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").OrderBy(p => p))
{
    string name = Path.GetFileNameWithoutExtension(path);
    if (!DemTiles.TryParseSourceName(name, out int latS, out int lonW)) { Check(false, "name " + name); continue; }
    byte[] data = File.ReadAllBytes(path);
    var sw = Stopwatch.StartNew();
    var grid = DemTiles.ReadGeoTiff(data);
    readMs += sw.Elapsed.TotalMilliseconds;
    var tile = DemTiles.FromGrid(grid, latS, lonW);
    sw.Restart();
    byte[] file = DemTiles.Encode(tile);
    encMs += sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    var back = DemTiles.Decode(file);
    decMs += sw.Elapsed.TotalMilliseconds;
    bool same = back.Cols == tile.Cols && back.Rows == tile.Rows && back.Heights.AsSpan().SequenceEqual(tile.Heights);
    if (!same) Check(false, "round trip " + name);
    if (grid.Cols != DemTiles.Glo90Cols(latS) || grid.Rows != DemTiles.Glo90Rows)
        Check(false, $"{name}: {grid.Cols} x {grid.Rows}, expected {DemTiles.Glo90Cols(latS)} x {DemTiles.Glo90Rows}");
    File.WriteAllBytes(Path.Combine(work, DemTiles.ShortName(latS, lonW) + FineElevation.FileExtension), file);
    cells += grid.Heights.Length; packed += file.Length; source += data.Length; tiles++;
    if (tiles <= 3 || name.Contains("N60_00_E006"))
    {
        double sum = 0; foreach (float v in grid.Heights) sum += v;
        Console.WriteLine($"  {name}: {grid.Cols} x {grid.Rows} at {grid.Lat0}, {grid.Lon0}; min {tile.Min} max {tile.Max} mean {sum / grid.Heights.Length:F4}; " +
            $"h[100,200] {grid.Heights[100 * grid.Cols + 200]:R} h[last] {grid.Heights[^1]:R}");
    }
}
Check(tiles > 0, $"{tiles} tiles read and round-tripped");
Console.WriteLine($"  {cells / 1e6:F1} M cells, {source / 1e6:F0} MB published -> {packed / 1e6:F1} MB ({8.0 * packed / cells:F2} bit/cell); " +
    $"per tile: read {readMs / tiles:F0} ms, encode {encMs / tiles:F0} ms, decode {decMs / tiles:F0} ms");

// lookups through FineElevation on the converted tiles
var fine = new FineElevation(work, File.ReadAllBytes(list));
Check(fine.SourceHas(60, 6) && !fine.SourceHas(0, -150), "tile list: N60E006 yes, mid-Pacific no");
Check(fine.TryHeight(0.5, -149.5, out double sea) && sea == 0, "open sea square reads 0 m");
if (File.Exists(Path.Combine(work, "N60E006.egl")))
{
    var t = DemTiles.Decode(File.ReadAllBytes(Path.Combine(work, "N60E006.egl")));
    // a grid point exactly: row 100, column 200 -> lat 61 - 100/1200, lon 6 + 200/cols
    Check(fine.TryHeight(61 - 100 / 1200.0, 6 + 200.0 / t.Cols, out double p) && p == t.At(100, 200), $"grid point N60E006[100,200] = {p}");
    // half way between two points along a row
    fine.TryHeight(61 - 100 / 1200.0, 6 + 200.5 / t.Cols, out double mid);
    Check(Math.Abs(mid - (t.At(100, 200) + t.At(100, 201)) / 2.0) < 1e-9, $"bilinear along a row ({mid})");
    // below the last row the square to the south is needed
    bool southHere = File.Exists(Path.Combine(work, "N59E006.egl"));
    Check(fine.TryHeight(60 + 0.1 / 1200, 6.5, out _) == southHere, "a point below the last row needs the square to the south");
    Check(fine.TryHeight(60 + 1 / 1200.0, 6.5, out double last) && last == t.At(1199, (int)(0.5 * t.Cols)), "the last row itself");
    // Nærøyfjord water (60.91 N, 6.871 E) and its west wall
    fine.TryHeight(60.91, 6.871, out double fjord);
    double wall = double.MinValue;
    for (double lon = 6.84; lon < 6.871; lon += 0.0005) { fine.TryHeight(60.91, lon, out double h); wall = Math.Max(wall, h); }
    Console.WriteLine($"  Naeroyfjord at 60.91 N 6.871 E: {fjord:F1} m; highest of its west side within 1.7 km: {wall:F0} m");
    Check(fjord < 5 && wall > 900, "the fjord is water and its west wall rises over 900 m");
    // rules 3: a smooth curve through the same points
    Check(fine.TrySmoothHeight(61 - 100 / 1200.0, 6 + 200.0 / t.Cols, out double sp) && Math.Abs(sp - t.At(100, 200)) < 1e-6,
        $"smooth: a grid point reads its own height ({sp})");
    fine.TrySmoothHeight(61 - 100 / 1200.0, 6 + 200.5 / t.Cols, out double smid);
    double cr = FineElevation.CatmullRom(t.At(100, 199), t.At(100, 200), t.At(100, 201), t.At(100, 202), 0.5);
    Check(Math.Abs(smid - cr) < 1e-6, $"smooth: Catmull-Rom along a row ({smid} against {cr})");
    // rules 5 on real ground (the fjord country): never outside the four points around, and it swung outside them before
    var rnd = new Random(5);
    int outside = 0, swung = 0, n = 0;
    for (int i = 0; i < 4000; i++)
    {
        double fr = 2 + rnd.NextDouble() * 1190, fc = 2 + rnd.NextDouble() * (t.Cols - 5);
        int r0 = (int)fr, c0 = (int)fc;
        double lo = Math.Min(Math.Min(t.At(r0, c0), t.At(r0, c0 + 1)), Math.Min(t.At(r0 + 1, c0), t.At(r0 + 1, c0 + 1)));
        double hi = Math.Max(Math.Max(t.At(r0, c0), t.At(r0, c0 + 1)), Math.Max(t.At(r0 + 1, c0), t.At(r0 + 1, c0 + 1)));
        if (!fine.TrySmoothHeight(61 - fr / 1200, 6 + fc / t.Cols, true, out double b) || !fine.TrySmoothHeight(61 - fr / 1200, 6 + fc / t.Cols, out double plain)) continue;
        n++;
        if (b < lo - 1e-9 || b > hi + 1e-9) outside++;
        if (plain < lo - 1e-9 || plain > hi + 1e-9) swung++;
    }
    Check(n > 3900 && outside == 0 && swung > 0, $"rules 5: of {n} places in N60E006 the bounded curve leaves the four points around none, the plain one {swung}");
}
// fallback squares give no height
fine.MarkFallback(60, 6);
Check(!fine.TryHeight(60.5, 6.5, out _) && !fine.TrySmoothHeight(60.5, 6.5, out _), "a fallback square gives no height");

// rules 5: each piece of the curve stays between the two points it runs between. GLO-90's whole metres: flat land at
// 0 m beside a point a metre lower or higher. Plain Catmull-Rom swings to the other side of 0 m there (in two dimensions
// the product of two swings put -0.005 m at a lower point's four diagonals: a block lower, beach sand in an X near Suez)
{
    double swing = 0, bounded = 0, gap = 0;
    for (int i = 0; i <= 100; i++)
    {
        double tt = i / 100.0;
        swing = Math.Max(swing, Math.Max(FineElevation.CatmullRom(-1, 0, 0, 0, tt), -FineElevation.CatmullRom(1, 0, 0, 0, tt)));
        bounded = Math.Max(bounded, Math.Max(Math.Abs(FineElevation.CatmullRomBounded(-1, 0, 0, 0, tt)), Math.Abs(FineElevation.CatmullRomBounded(1, 0, 0, 0, tt))));
        // a steady slope has no swing: the bounded curve is Catmull-Rom itself there
        gap = Math.Max(gap, Math.Abs(FineElevation.CatmullRomBounded(0, 1, 3, 4, tt) - FineElevation.CatmullRom(0, 1, 3, 4, tt)));
    }
    Check(swing > 0.07 && bounded == 0 && gap < 1e-12,
        $"rules 5: beside a point a metre off flat land Catmull-Rom swings {swing:F3} m past 0 m, the bounded curve 0; on a steady slope they agree");
    Check(FineElevation.CatmullRomBounded(5, 2, 7, 1, 0) == 2 && FineElevation.CatmullRomBounded(5, 2, 7, 1, 1) == 7, "rules 5: the bounded curve passes through its points");
}

// the downloader from a local folder, into a fresh folder
string work2 = work + "-dl";
if (Directory.Exists(work2)) Directory.Delete(work2, true);
var fine2 = new FineElevation(work2, File.ReadAllBytes(list));
using (var dl = new DemDownloader(fine2, src) { Log = s => Console.WriteLine("  log: " + s) })
{
    var names = Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
    DemTiles.TryParseSourceName(names[0], out int la, out int lo);
    var tasks = Enumerable.Range(0, 4).Select(_ => dl.FetchAsync(la, lo)).ToArray();
    Task.WaitAll(tasks);
    Check(tasks.All(t => t.Result) && dl.TilesConverted == 1, $"four requests for {names[0]} fetch it once ({dl.TilesConverted})");
    Check(!dl.FetchBlocking(0, -150), "no fetch for open sea");
    // generation-style lazy fetch through FineElevation
    fine2.Fetch = dl.FetchBlocking;
    FineElevation.FetchAllowed = true;
    DemTiles.TryParseSourceName(names[1], out la, out lo);
    Check(fine2.TryHeight(la + 0.5, lo + 0.5, out double h2), $"lazy fetch of {names[1]}: {h2:F0} m");
    // a square the source has but this folder lacks: the fetch fails and the square falls back for good
    if (!File.Exists(Path.Combine(src, DemTiles.SourceName(27, 86) + ".tif")))
    {
        int fell = 0; fine2.FellBack = (_, _) => fell++;
        Check(!fine2.TryHeight(27.5, 86.5, out _) && fine2.IsFallback(27, 86) && fell == 1, "an unavailable tile falls back once and is recorded");
    }
    FineElevation.FetchAllowed = false;
    var (srcCount, here, bytes) = dl.Census();
    Check(srcCount == 26475 && here == 2, $"census: {srcCount} source squares, {here} here ({bytes / 1e6:F1} MB)");
}
// review A3: a square no source has (the folder lacks it) falls back for good and is saved with the world; one whose fetch
// fails (here a source file that doesn't convert, as a broken download would) falls back for this session only
{
    string bad = work + "-a3src";
    Directory.CreateDirectory(bad);
    File.WriteAllBytes(Path.Combine(bad, DemTiles.SourceName(27, 87) + ".tif"), new byte[1000]);
    var fine4 = new FineElevation(work + "-a3", File.ReadAllBytes(list));
    using var dl4 = new DemDownloader(fine4, bad) { Retries = 1, Log = s => Console.WriteLine("  log: " + s) };
    int forGood = 0, forSession = 0;
    fine4.Fetch = dl4.FetchBlocking; fine4.Lacks = dl4.Lacks;
    fine4.FellBack = (_, _) => forGood++; fine4.FellBackForSession = (_, _) => forSession++;
    FineElevation.FetchAllowed = true;
    bool h1 = fine4.TryHeight(27.5, 86.5, out _), h2 = fine4.TryHeight(27.5, 87.5, out _);
    FineElevation.FetchAllowed = false;
    Check(!h1 && !h2 && forGood == 1 && forSession == 1 && fine4.IsFallback(27, 86) && fine4.IsFallback(27, 87)
        && fine4.FallbackKeys.SequenceEqual(new[] { FineElevation.Key(27, 86) }) && dl4.Lacks(27, 86) && !dl4.Lacks(27, 87),
        $"A3: a square no source has falls back for good ({forGood}, saved: {string.Join(",", fine4.FallbackKeys)}), a failed fetch for the session only ({forSession})");
    fine4.MarkFallback(27, 87);
    Check(forGood == 2 && fine4.FallbackKeys.Length == 2, "A3: a session fallback can still become one for good");
}
// CR-D2: chunk generation's wait for a square counts the square's own download, from when it gets a download slot, not
// the time it queued behind other squares; queued or not, it ends BackstopBudgets budgets after the call. A local HTTP
// server stands in for Copernicus and holds back the answers for chosen squares; the downloader has one slot.
{
    var gates = new System.Collections.Concurrent.ConcurrentDictionary<string, ManualResetEventSlim>();
    var asked = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
    var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    server.Start();
    int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
    _ = Task.Run(async () =>
    {
        while (true)
        {
            System.Net.Sockets.TcpClient c;
            try { c = await server.AcceptTcpClientAsync(); } catch (Exception) { return; }
            _ = Task.Run(() =>
            {
                using (c)
                {
                    var s = c.GetStream();
                    var reader = new StreamReader(s);
                    string request = reader.ReadLine();   // GET /<name>/<name>.tif HTTP/1.1
                    while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                    string name = request.Split(' ')[1].Split('/')[1];
                    asked[name] = 0;
                    if (gates.TryGetValue(name, out var gate)) gate.Wait();
                    string file = Path.Combine(src, name + ".tif");
                    byte[] body = File.Exists(file) ? File.ReadAllBytes(file) : Array.Empty<byte>();
                    s.Write(System.Text.Encoding.ASCII.GetBytes((body.Length > 0 ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") +
                        $"\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                    s.Write(body);
                }
            });
        }
    });
    var logQ = new System.Collections.Concurrent.ConcurrentQueue<string>();
    if (Directory.Exists(work + "-queue")) Directory.Delete(work + "-queue", true);
    var fineQ = new FineElevation(work + "-queue", File.ReadAllBytes(list));
    using var dlQ = new DemDownloader(fineQ, $"http://127.0.0.1:{port}", 1) { Retries = 1, Log = s => { logQ.Enqueue(s); Console.WriteLine("  log: " + s); } };
    var sq = Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).Skip(2).Take(5)
        .Select(n => { DemTiles.TryParseSourceName(n, out int la, out int lo); return (name: n, latS: la, lonW: lo); }).ToArray();
    void Hold(int i) { gates[sq[i].name] = new ManualResetEventSlim(false); }
    bool AskedFor(int i) { var w = Stopwatch.StartNew(); while (!asked.ContainsKey(sq[i].name) && w.Elapsed.TotalSeconds < 10) Thread.Sleep(10); return asked.ContainsKey(sq[i].name); }
    bool Logged(int i, string what) => logQ.Any(l => l.StartsWith(DemTiles.ShortName(sq[i].latS, sq[i].lonW)) && l.Contains(what));

    // a square queued behind a held one for 4 s, with a budget of 2 s: its budget starts with its own download
    Hold(0);
    var holder = dlQ.FetchAsync(sq[0].latS, sq[0].lonW);
    bool heldAsked = AskedFor(0);
    var clock = Stopwatch.StartNew();
    var queued = Task.Run(() => dlQ.AwaitForGeneration(sq[1].latS, sq[1].lonW, dlQ.FetchAsync(sq[1].latS, sq[1].lonW), TimeSpan.FromSeconds(2)));
    Thread.Sleep(4000);
    bool stillWaiting = !queued.IsCompleted;
    gates[sq[0].name].Set();
    Check(heldAsked && stillWaiting && queued.Result && holder.Result && fineQ.IsHere(sq[1].latS, sq[1].lonW),
        $"CR-D2: a square queued {clock.Elapsed.TotalSeconds:0.0} s behind another, with a budget of 2 s, is waited for: the budget starts with its download");

    // a square whose own download stalls: false once the budget has run from its start
    Hold(2);
    clock.Restart();
    var stalled = dlQ.FetchAsync(sq[2].latS, sq[2].lonW);
    bool gotStalled = dlQ.AwaitForGeneration(sq[2].latS, sq[2].lonW, stalled, TimeSpan.FromSeconds(1));
    double stalledAfter = clock.Elapsed.TotalSeconds;
    gates[sq[2].name].Set();
    Check(!gotStalled && stalledAfter >= 0.9 && stalledAfter < 3 && Logged(2, "after its download began") && stalled.Result,
        $"CR-D2: a download that stalls ends the wait {stalledAfter:0.0} s after it began (budget 1 s), saying so; the download goes on");

    // a square queued behind one held for good: the backstop, five budgets from the call
    Hold(3);
    var holder2 = dlQ.FetchAsync(sq[3].latS, sq[3].lonW);
    bool held2Asked = AskedFor(3);
    clock.Restart();
    var behind = dlQ.FetchAsync(sq[4].latS, sq[4].lonW);
    bool gotBehind = dlQ.AwaitForGeneration(sq[4].latS, sq[4].lonW, behind, TimeSpan.FromSeconds(1));
    double behindAfter = clock.Elapsed.TotalSeconds;
    gates[sq[3].name].Set();
    Check(held2Asked && !gotBehind && behindAfter >= 4.9 && behindAfter < 7 && Logged(4, "still waiting for a slot")
        && holder2.Result && behind.Result,
        $"CR-D2: a square that never gets a slot ends the wait after {behindAfter:0.0} s, the backstop of {DemDownloader.BackstopBudgets} budgets of 1 s, saying so");
    server.Stop();
}
// rules 2's summit search, on tiles fetched lazily from a folder with the whole source (optional 4th argument). The
// expected tops are the highest raw points within 1 km found by glo90_summits.py (the sources note's folder).
if (args.Length > 3)
{
    string work3 = work + "-summits";
    if (Directory.Exists(work3)) Directory.Delete(work3, true);
    var fine3 = new FineElevation(work3, File.ReadAllBytes(list));
    using var dl3 = new DemDownloader(fine3, args[3]) { Log = s => Console.WriteLine("  log: " + s) };
    fine3.Fetch = dl3.FetchBlocking;
    FineElevation.FetchAllowed = true;
    foreach (var (name, lat, lon, real, expected) in new[] {
        ("Everest", 27.9881, 86.9250, 8849, 8726), ("K2", 35.8825, 76.5133, 8611, 8565), ("Mont Blanc", 45.8326, 6.8652, 4806, 4793),
        ("Matterhorn", 45.9763, 7.6586, 4478, 4317), ("Fuji", 35.3606, 138.7274, 3776, 3755), ("Denali", 63.0692, -151.0070, 6190, 6169) })
    {
        bool ok = fine3.TryHighest(lat, lon, FineElevation.SummitSearch, out double tLat, out double tLon, out double top, out double dist);
        bool atGrid = fine3.TryHeight(tLat, tLon, out double back) && Math.Abs(back - top) < 0.01;
        Check(ok && Math.Abs(top - expected) <= 1 && atGrid && dist <= FineElevation.SummitSearch,
            $"{name}: GLO-90 top {top:F0} m (script {expected}, survey {real}) at {tLat:F4}, {tLon:F4}, {dist:F0} m from the catalogue's point; reads back {back:F0}");
    }
    // a search that crosses a square's edge (47.0 N runs between N46 and N47)
    Check(fine3.TryHighest(47.0, 9.5, 1000, out _, out _, out double edge, out _) && fine3.IsHere(46, 9) && fine3.IsHere(47, 9),
        $"a search across 47 N reads both squares ({edge:F0} m)");
    FineElevation.FetchAllowed = false;
    Check(!fine3.TryHighest(10.5, 20.5, 1000, out _, out _, out _, out _), "no top where the tile isn't here and this thread may not fetch");
    // a whole-world map's thread takes the built-in heights; other threads read GLO-90 (Everest's square is here now)
    bool chartGot = true, chartTop = true, otherGot = false;
    var chart = new Thread(() => { chartGot = fine3.TryHeight(27.98, 86.92, out _); chartTop = fine3.TryHighest(27.9881, 86.9250, 1000, out _, out _, out _, out _); }) { Name = "Wmap world chart" };
    var other = new Thread(() => otherGot = fine3.TryHeight(27.98, 86.92, out _)) { Name = "some worker" };
    chart.Start(); other.Start(); chart.Join(); other.Join();
    Check(!chartGot && !chartTop && otherGot, "Wmap's world-chart thread takes the built-in heights, another thread GLO-90's");
    FineElevation.FetchAllowed = true;
    // rules 3 across a square's edges, a band edge (50 N: 1,200 | 800 columns) and the antimeridian; no crease at a grid line
    double Gap(double la1, double lo1, double la2, double lo2)
        => fine3.TrySmoothHeight(la1, lo1, out double a) && fine3.TrySmoothHeight(la2, lo2, out double b) ? Math.Abs(a - b) : double.NaN;
    const double eps = 1e-8;
    foreach (var (what, la1, lo1, la2, lo2) in new[] {
        ("9 E at 47.3 N", 47.3, 9.0 - eps, 47.3, 9.0 + eps), ("47 N at 9.37 E", 47.0 + eps, 9.37, 47.0 - eps, 9.37),
        ("the band edge at 50 N, 9.4 E", 50.0 + eps, 9.4, 50.0 - eps, 9.4), ("60 N at 6.5 E", 60.0 + eps, 6.5, 60.0 - eps, 6.5),
        ("the antimeridian at 65.5 N", 65.5, 180 - eps, 65.5, -180 + eps) })
    {
        double gap = Gap(la1, lo1, la2, lo2);
        Check(gap < 0.01, $"smooth: continuous across {what} ({gap:F4} m)");
    }
    Check(fine3.TryHighest(27.9881, 86.9250, 1000, out double eLat, out double eLon, out double eTop, out _)
        && fine3.TrySmoothHeight(eLat, eLon, out double eBack) && Math.Abs(eBack - eTop) < 0.01, $"smooth: Everest's GLO-90 top reads back ({eTop:F0} m)");
    {
        // slopes just west and east of a column line on Everest's south-west face: equal on the smooth curve, not on straight lines
        double la = 27.97, cols = DemTiles.Glo90Cols(27), lo = 86 + Math.Round((86.915 - 86) * cols) / cols, h = 1e-4 / cols;
        // per point spacing: the change of slope (m per point) where the column line is crossed
        double Slopes(Func<double, double> at) => Math.Abs((at(lo) - at(lo - h)) / h - (at(lo + h) - at(lo)) / h) / cols;
        double smooth = Slopes(x => fine3.TrySmoothHeight(la, x, out double v) ? v : double.NaN);
        double linear = Slopes(x => fine3.TryHeight(la, x, out double v) ? v : double.NaN);
        Check(smooth < 0.01 * linear && linear > 1, $"smooth: no crease at a grid line (slope changes by {smooth:F3} m per point, {linear:F1} on straight lines)");
        // between points: Catmull-Rom of the four points along the row (a row exactly on a grid row)
        int r = 20; double rowLat = 28 - r / 1200.0, mid = lo + 0.3 / cols;
        double Pt(int k) => fine3.TryHeight(rowLat, lo + k / cols, out double v) ? v : double.NaN;
        double want = FineElevation.CatmullRom(Pt(-1), Pt(0), Pt(1), Pt(2), 0.3);
        Check(fine3.TrySmoothHeight(rowLat, mid, out double got) && Math.Abs(got - want) < 1e-6 && Pt(0) != Pt(1),
            $"smooth: Catmull-Rom along a row through {Pt(-1)}, {Pt(0)}, {Pt(1)}, {Pt(2)} m ({got:F3} m, expected {want:F3})");
    }
    FineElevation.FetchAllowed = false;
}

// decision 0051's data release: a small one built from a store's own files in the real one's layout (parts holding the
// tiles at offsets, a manifest with each square's part, offset, length and SHA-256), fetched square by square, with one
// square broken in its part (it must come from Copernicus instead), and downloaded all at once (part 1 streamed, part 2
// mostly here so its two missing squares come one by one)
if (storeArg != null && Directory.Exists(storeArg))
{
    Check(DemDownloader.TryParseShortName("N60E006", out int pla, out int plo) && pla == 60 && plo == 6
        && DemDownloader.TryParseShortName("S01W075", out pla, out plo) && pla == -1 && plo == -75 && !DemDownloader.TryParseShortName("X01W075", out _, out _),
        "short names parse back");
    string rel = work + "-release";
    if (Directory.Exists(rel)) Directory.Delete(rel, true);
    Directory.CreateDirectory(rel);
    var srcNames = Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
    DemTiles.TryParseSourceName(srcNames[2], out int brokenLat, out int brokenLon);
    string broken = DemTiles.ShortName(brokenLat, brokenLon);
    var all = Directory.EnumerateFiles(storeArg, "*" + FineElevation.FileExtension).Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
    var picks = all.Where((n, i) => i % 600 == 0 && n != broken).Take(40).ToList();
    Check(picks.Count == 40 && File.Exists(Path.Combine(storeArg, broken + FineElevation.FileExtension)), $"40 squares and {broken} from the store");
    var part1 = picks.Take(25).Append(broken).OrderBy(n => n).ToList();
    var part2 = picks.Skip(25).ToList();
    var bitmap = new byte[360 * 180 / 8];
    var manifestTiles = new Dictionary<string, object[]>();
    var parts = new List<Dictionary<string, object>>();
    byte[] Stored(string n) => File.ReadAllBytes(Path.Combine(storeArg, n + FineElevation.FileExtension));
    foreach (var (names, p) in new[] { (part1, 1), (part2, 2) })
    {
        string partName = $"test-part{p}of2.zip";
        using (var fs = File.Create(Path.Combine(rel, partName)))
        {
            fs.Write(new byte[64]); // where a zip's local header would be
            foreach (var n in names)
            {
                fs.Write(new byte[30 + 11]); // the next local header and name
                var data = Stored(n);
                long off = fs.Position;
                manifestTiles[n] = new object[] { p, off, data.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant() };
                if (n == broken) data[data.Length / 2] ^= 0x5A; // the part holds a damaged copy
                fs.Write(data);
                DemDownloader.TryParseShortName(n, out int la, out int lo);
                int bit = DemTiles.SquareIndex(la, lo);
                bitmap[bit >> 3] |= (byte)(0x80 >> (bit & 7));
            }
            fs.Write(new byte[1000]); // the central directory
            parts.Add(new Dictionary<string, object> { ["name"] = partName, ["bytes"] = fs.Length, ["sha256"] = "", ["tiles"] = names.Count });
        }
    }
    File.WriteAllText(Path.Combine(rel, DemDownloader.ManifestName), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["format"] = "egl", ["formatVersion"] = 1, ["converterVersion"] = 1, ["tileCount"] = manifestTiles.Count, ["parts"] = parts, ["tiles"] = manifestTiles,
    }));
    string Fresh(string suffix) { string d = work + suffix; if (Directory.Exists(d)) Directory.Delete(d, true); return d; }
    bool Same(string folder, string n) => File.ReadAllBytes(Path.Combine(folder, n + FineElevation.FileExtension)).AsSpan().SequenceEqual(Stored(n));
    bool SameHeights(string folder, string n) => DemTiles.Decode(File.ReadAllBytes(Path.Combine(folder, n + FineElevation.FileExtension))).Heights
        .AsSpan().SequenceEqual(DemTiles.Decode(Stored(n)).Heights);

    // square by square from the release
    var fineA = new FineElevation(Fresh("-relA"), bitmap);
    using (var dl = new DemDownloader(fineA, null, 3, rel) { Log = s => Console.WriteLine("  log: " + s) })
    {
        var three = new[] { part1[0], part1[^1], part2[3] };
        var got = three.Select(n => { DemDownloader.TryParseShortName(n, out int la, out int lo); return dl.FetchAsync(la, lo); }).ToArray();
        Task.WaitAll(got);
        Check(got.All(t => t.Result) && dl.TilesFetched == 3 && dl.TilesConverted == 0 && three.All(n => Same(fineA.Folder, n)),
            $"three squares from the release, byte for byte the store's ({dl.TilesFetched} fetched)");
        Check(File.Exists(Path.Combine(fineA.Folder, DemDownloader.ManifestCache)), "the release's manifest is kept beside the tiles");
        Check(!dl.FetchBlocking(brokenLat, brokenLon) && !fineA.IsHere(brokenLat, brokenLon), $"{broken}: a damaged copy in the release is not kept (no Copernicus here)");
    }
    // the damaged square from Copernicus instead
    var fineB = new FineElevation(Fresh("-relB"), bitmap);
    using (var dl = new DemDownloader(fineB, src, 3, rel) { Retries = 1, Log = s => Console.WriteLine("  log: " + s) })
        Check(dl.FetchBlocking(brokenLat, brokenLon) && dl.TilesConverted == 1 && dl.TilesFetched == 0 && SameHeights(fineB.Folder, broken),
            $"{broken}: from Copernicus where the release's copy is damaged, the same heights as the store's");
    // download all from the release
    var fineC = new FineElevation(Fresh("-relC"), bitmap);
    Directory.CreateDirectory(fineC.Folder);
    foreach (var n in part2.Skip(2)) File.Copy(Path.Combine(storeArg, n + FineElevation.FileExtension), Path.Combine(fineC.Folder, n + FineElevation.FileExtension));
    using (var dl = new DemDownloader(fineC, src, 3, rel) { Retries = 1, Log = s => Console.WriteLine("  log: " + s) })
    {
        var sw = Stopwatch.StartNew();
        Check(dl.StartAll(3, DemDownloader.Origin.Repo) && !dl.StartAll(3, DemDownloader.Origin.Repo), "download all starts once");
        while (dl.All.Running && sw.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
        var b = dl.All;
        Check(!b.Running && b.Error == null && b.Total == 41 && b.Skipped == 13 && b.Done == 28 && b.Failed == 0,
            $"download all: {b.Done} kept, {b.Skipped} already here, {b.Failed} failed, error {b.Error ?? "none"} ({sw.ElapsedMilliseconds} ms)");
        Check(part1.Concat(part2).All(n => n == broken ? SameHeights(fineC.Folder, broken) : Same(fineC.Folder, n)),
            "every square is the store's (the damaged one by its heights, from Copernicus)");
        Check(dl.TilesFetched == 27 && dl.TilesConverted == 1, $"27 from the release, 1 from Copernicus ({dl.TilesFetched}, {dl.TilesConverted})");
        long part1Upto = (long)manifestTiles[part1[^1]][1] + (int)manifestTiles[part1[^1]][2];
        Check(b.Expected == part1Upto + part2.Take(2).Sum(n => (long)(int)manifestTiles[n][2]) && dl.BytesDownloaded >= b.Expected,
            $"part 1 read once up to its last square, part 2's two squares alone: {b.Expected:N0} bytes expected, {dl.BytesDownloaded:N0} read");
    }
    // a release that can't be reached
    var fineD = new FineElevation(Fresh("-relD"), bitmap);
    using (var dl = new DemDownloader(fineD, null, 3, work + "-nowhere") { Log = s => Console.WriteLine("  log: " + s) })
    {
        dl.StartAll(3, DemDownloader.Origin.Repo);
        var sw = Stopwatch.StartNew();
        while (dl.All.Running && sw.Elapsed.TotalSeconds < 30) Thread.Sleep(20);
        Check(!dl.All.Running && dl.All.Error != null && dl.All.Done == 0 && !dl.StartAll(3, DemDownloader.Origin.Copernicus),
            $"no release: download all stops with \"{dl.All.Error}\"; no Copernicus source: it doesn't start");
    }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
return failures == 0 ? 0 : 1;
