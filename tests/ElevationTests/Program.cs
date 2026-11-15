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
// EA-0106 (Fable's re-review of 255baa7): the release's heights table, shipped beside the tile list
string heightsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(list)), Glo90Heights.FileName);
var releaseHeights = File.Exists(heightsPath) ? Glo90Heights.Read(File.ReadAllBytes(heightsPath), File.ReadAllBytes(list)) : null;
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

// ER-16 (code review 14 November 2026): a GLO-90 release file (DemDownloader.FetchFile, blocking at the first start) stops
// at once on the server's stop token and leaves nothing as if fetched
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    string stopDir = work + "-stop";
    var fineStop = new FineElevation(stopDir, File.ReadAllBytes(list));
    using var dlStop = new DemDownloader(fineStop, src, 1, src);
    string target = Path.Combine(stopDir, "release-test.bin");
    var t0 = DateTime.UtcNow;
    bool stopped = false;
    try { dlStop.FetchFile("release-test.bin", target, 10, new string('0', 64), out _, out _, cancelled.Token); }
    catch (OperationCanceledException) { stopped = (DateTime.UtcNow - t0).TotalSeconds < 5; }
    Check(stopped && !File.Exists(target), "ER-16: a GLO-90 release file stops on a cancelled token, nothing fetched");
}

// FP-01 (the Fable pass of the 14 November 2026 review): chunk generation's wait for a square, bounded (the running game's)
// or not (the spawn chunks'), ends with ServerStoppingException within a poll of the server's stop, so the generation thread
// pauses before the game disposes the mods; and it still returns the download's answer when there is no stop
{
    var fineWait = new FineElevation(work + "-wait", File.ReadAllBytes(list));
    using var dlWait = new DemDownloader(fineWait, src, 1);
    var never = new TaskCompletionSource<bool>().Task;
    bool stop = false;
    dlWait.Stopping = () => stop;
    foreach (var (budget, what) in new[] { (TimeSpan.FromMinutes(2), "the running game's two minutes"), (Timeout.InfiniteTimeSpan, "the spawn chunks' unbounded wait") })
    {
        stop = false;
        var clock = Stopwatch.StartNew();
        // (caught inside: ServerStoppingException is a TaskCanceledException, so a task it ends is Canceled, not Faulted)
        var waiting = Task.Run(() =>
        {
            try { dlWait.AwaitForGeneration(10, 10, never, budget); return null; }
            catch (Exception e) { return e; }
        });
        Thread.Sleep(600);
        bool stillWaiting = !waiting.IsCompleted;
        stop = true;
        bool ended = waiting.Wait(TimeSpan.FromSeconds(5)) && waiting.Result is ServerStoppingException;
        double after = clock.Elapsed.TotalSeconds;
        Check(stillWaiting && ended && after < 0.6 + 2 * DemDownloader.StopPollMs / 1000.0 + 0.5,
            $"FP-01: {what} for a square ends with ServerStoppingException {after:0.00} s after the call, the stop said at 0.6 s");
    }
    stop = false;
    var done = new TaskCompletionSource<bool>();
    var answered = Task.Run(() => dlWait.AwaitForGeneration(10, 10, done.Task, Timeout.InfiniteTimeSpan));
    Thread.Sleep(400);
    done.SetResult(true);
    Check(answered.Wait(TimeSpan.FromSeconds(5)) && answered.Result, "FP-01: without a stop, the unbounded wait returns the download's answer");
}

// FP-11/FP-12: a generation entry point may wait for tiles for its own length, and the thread is as it was after it
{
    bool before = FineElevation.FetchAllowed;
    FineElevation.FetchAllowed = false;
    bool inner, nested;
    using (FineElevation.Generating())
    {
        inner = FineElevation.FetchAllowed;
        using (FineElevation.Generating()) { }
        nested = FineElevation.FetchAllowed;
    }
    bool after = FineElevation.FetchAllowed;
    FineElevation.FetchAllowed = true;
    using (FineElevation.Generating()) { }
    Check(inner && nested && !after && FineElevation.FetchAllowed, "FP-11: Generating() allows fetching inside its scope (nested too) and restores the thread's flag after it");
    FineElevation.FetchAllowed = before;
}

// EA-0106 (the Fable pass's FP-17, L3): a damaged GeoTIFF directory throws InvalidDataException and nothing else, and
// allocates no more than a GLO-90 tile's worth
{
    string tifPath = Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").OrderBy(p => p).First();
    byte[] tif = File.ReadAllBytes(tifPath);
    int ifd = (int)BitConverter.ToUInt32(tif, 4), entries = BitConverter.ToUInt16(tif, ifd);
    int invalid = 0, accepted = 0;
    var others = new List<string>();
    long most = 0;
    void TryRead(byte[] m, string what)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try { DemTiles.ReadGeoTiff(m); accepted++; }
        catch (InvalidDataException) { invalid++; }
        catch (Exception x) { others.Add(what + ": " + x.GetType().Name); }
        most = Math.Max(most, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    uint[] counts = { 0, 2, 0xFFFF, 0x10001, 0x7FFFFFFF, 0xFFFFFFFF };
    uint[] places = { 0, 7, (uint)tif.Length - 2, (uint)tif.Length, 0x7FFFFFF0, 0xFFFFFFFF };
    for (int i = 0; i < entries; i++)
    {
        int e = ifd + 2 + 12 * i;
        foreach (uint c in counts) { var m = (byte[])tif.Clone(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(e + 4), c); TryRead(m, $"entry {i} count {c}"); }
        foreach (uint v in places) { var m = (byte[])tif.Clone(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(e + 8), v); TryRead(m, $"entry {i} value {v}"); }
        foreach (ushort t in new ushort[] { 0, 2, 7, 13, 16 }) { var m = (byte[])tif.Clone(); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(e + 2), t); TryRead(m, $"entry {i} type {t}"); }
    }
    foreach (uint v in places) { var m = (byte[])tif.Clone(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(4), v); TryRead(m, $"directory at {v}"); }
    { var m = (byte[])tif.Clone(); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(ifd), 0xFFFF); TryRead(m, "65535 entries"); }
    Check(others.Count == 0 && most < 200_000_000,
        $"EA-0106: {invalid + accepted + others.Count} damaged GeoTIFF directories: {invalid} refused, {accepted} still read, {others.Count} other " +
        $"exceptions{(others.Count > 0 ? " (" + string.Join("; ", others.Take(5)) + ")" : "")}; at most {most / 1e6:F0} MB allocated by one");

    // (L8) a GeoTIFF of one degree but other dimensions than GLO-90's (another product) is read, but not kept as a tile
    string tifName = Path.GetFileNameWithoutExtension(tifPath);
    DemTiles.TryParseSourceName(tifName, out int oddLat, out int oddLon);
    int Entry(byte[] f, int tag)
    {
        for (int i = 0; i < entries; i++) if (BitConverter.ToUInt16(f, ifd + 2 + 12 * i) == tag) return ifd + 2 + 12 * i;
        return -1;
    }
    var odd = (byte[])tif.Clone();
    int we = Entry(odd, 256), se = Entry(odd, 33550), half = DemTiles.Glo90Cols(oddLat) / 2;
    if (BitConverter.ToUInt16(odd, we + 2) == 3) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(odd.AsSpan(we + 8), (ushort)half);
    else System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(odd.AsSpan(we + 8), (uint)half);
    int scaleAt = (int)BitConverter.ToUInt32(odd, se + 8);
    System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(odd.AsSpan(scaleAt), 2 * BitConverter.ToDouble(odd, scaleAt));
    string oddSource = work + "-odd";
    if (Directory.Exists(oddSource)) Directory.Delete(oddSource, true);
    Directory.CreateDirectory(oddSource);
    File.WriteAllBytes(Path.Combine(oddSource, tifName + ".tif"), odd);
    var oddList = new byte[360 * 180 / 8];
    int oddBit = DemTiles.SquareIndex(oddLat, oddLon);
    oddList[oddBit >> 3] |= (byte)(0x80 >> (oddBit & 7));
    var oddFine = new FineElevation(Path.Combine(oddSource, "glo90"), oddList);
    var oddLog = new List<string>();
    using (var dlOdd = new DemDownloader(oddFine, oddSource, 1, null) { Retries = 1, Log = s => { lock (oddLog) oddLog.Add(s); } })
    {
        int oddCols = DemTiles.ReadGeoTiff(odd).Cols;
        bool kept = dlOdd.FetchBlocking(oddLat, oddLon);
        Check(oddCols == half && !kept && !oddFine.IsHere(oddLat, oddLon) && oddLog.Any(l => l.Contains("not GLO-90's")),
            $"EA-0106 (L8): a GeoTIFF of {oddCols} x 1200 cells for {DemTiles.ShortName(oddLat, oddLon)} is not kept ({oddLog.LastOrDefault()})");
    }
}

// EA-0106 (FP-17, L2): a mirror's manifest larger than 16 MB, with an entry larger than a tile can be, or with one whose end
// overflows, is refused: nothing is read from its parts
{
    string hostile = work + "-hostile";
    if (Directory.Exists(hostile)) Directory.Delete(hostile, true);
    var oneSquare = new byte[360 * 180 / 8];
    int hb = DemTiles.SquareIndex(10, 20);
    oneSquare[hb >> 3] |= (byte)(0x80 >> (hb & 7));
    string sha0 = new string('0', 64);
    string Manifest(long partBytes, long off, long len, string pad = "") =>
        "{" + pad + "\"format\":\"egl\",\"formatVersion\":1,\"parts\":[{\"name\":\"p.zip\",\"bytes\":" + partBytes + "}],\"tiles\":{\"N10E020\":[1," + off + "," + len + ",\"" + sha0 + "\"]}}";
    foreach (var (what, json) in new[]
    {
        ("an entry of 9 MB", Manifest(20_000_000, 0, 9_000_000)),
        ("an entry whose end overflows", Manifest(1000, long.MaxValue - 10, 100)),
        ("a manifest of 17 MB", Manifest(1000, 0, 100, new string(' ', 17 << 20))),
    })
    {
        string repo = Path.Combine(hostile, what.Replace(' ', '-'));
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, DemDownloader.ManifestName), json);
        File.WriteAllBytes(Path.Combine(repo, "p.zip"), new byte[1000]);
        var log = new List<string>();
        using var dlH = new DemDownloader(new FineElevation(Path.Combine(repo, "glo90"), oneSquare), null, 1, repo) { Retries = 1, Log = s => { lock (log) log.Add(s); } };
        bool got = dlH.FetchBlocking(10, 20);
        string said = log.FirstOrDefault(l => l.Contains("manifest could not be had"));
        Check(!got && said != null && dlH.BytesDownloaded == 0, $"EA-0106: a mirror's manifest with {what} is refused ({said})");
    }
}

// EA-0100 (the Fable pass's FP-05): "download all" into a store that can't be written ends at the first tile, with the
// store's error, never trying Copernicus; with too little room it doesn't start, nor does a fetch for generation; with room
// it fetches every square
{
    string full = work + "-room";
    if (Directory.Exists(full)) Directory.Delete(full, true);
    string repo = Path.Combine(full, "repo"), cop = Path.Combine(full, "copernicus");
    Directory.CreateDirectory(repo); Directory.CreateDirectory(cop);
    var roomList = new byte[360 * 180 / 8];
    var roomSquares = new List<(int, int)>();
    for (int i = 0; i < 40; i++) roomSquares.Add((10 + i / 8, 20 + i % 8));
    foreach (var (la, lo) in roomSquares) { int ix = DemTiles.SquareIndex(la, lo); roomList[ix >> 3] |= (byte)(0x80 >> (ix & 7)); }
    long partBytes;
    using (var part = new FileStream(Path.Combine(repo, "part1.zip"), FileMode.Create))
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (la, lo) in roomSquares)
        {
            int cols = DemTiles.Glo90Cols(la), rows = DemTiles.Glo90Rows;
            var t = new DemTiles.Tile { LatS = la, LonW = lo, Cols = cols, Rows = rows, Heights = new short[cols * rows] };
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) t.Heights[r * cols + c] = (short)(200 + 80 * Math.Sin(r / 37.0) + 60 * Math.Cos(c / 23.0) + la);
            t.Min = t.Heights.Min(); t.Max = t.Heights.Max();
            byte[] tile = DemTiles.Encode(t);
            if (sb.Length > 0) sb.Append(',');
            sb.Append($"\"{DemTiles.ShortName(la, lo)}\":[1,{part.Position},{tile.Length},\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(tile)).ToLowerInvariant()}\"]");
            part.Write(tile);
        }
        partBytes = part.Position;
        File.WriteAllText(Path.Combine(repo, "manifest.json"), "{\"format\":\"egl\",\"formatVersion\":1,\"parts\":[{\"name\":\"part1.zip\",\"bytes\":" + partBytes + "}],\"tiles\":{" + sb + "}}");
    }
    DemDownloader.Batch RunAll(DemDownloader d)
    {
        var clock = Stopwatch.StartNew();
        Check(d.StartAll(3, DemDownloader.Origin.Repo), "EA-0100: download all starts");
        while (d.All.Running && clock.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
        return d.All;
    }

    // a store where every tile write fails (a folder stands where each tile goes, as a full disk refuses the write)
    var logFull = new List<string>();
    var fineFull = new FineElevation(Path.Combine(full, "glo90-full"), roomList);
    foreach (var (la, lo) in roomSquares) Directory.CreateDirectory(fineFull.PathOf(la, lo));
    using (var dlFull = new DemDownloader(fineFull, cop, 3, repo) { Log = s => { lock (logFull) logFull.Add(s); } })
    {
        var bFull = RunAll(dlFull);
        bool viaCopernicus;
        lock (logFull) viaCopernicus = logFull.Any(l => l.Contains("Copernicus"));
        Check(!bFull.Running && bFull.Done == 0 && bFull.Error != null && bFull.Error.Contains("could not be written") && !viaCopernicus
            && dlFull.BytesDownloaded < partBytes / 4,
            $"EA-0100: into a store that can't be written, download all ends at once with the store's error ('{bFull.Error}'), " +
            $"{dlFull.BytesDownloaded:N0} of {partBytes:N0} bytes read, Copernicus not tried; {logFull.Count} log lines");
    }

    // too little room: download all doesn't start and nothing is read; (Fable's re-review of 1926d8f) a square for generation
    // is still fetched with 0.2 GB free (its floor is GenerationReserve), and fails at once without trying Copernicus with 50 MB
    var logLow = new List<string>();
    var fineLow = new FineElevation(Path.Combine(full, "glo90-low"), roomList);
    long freeLow = 200_000_000;
    using (var dlLow = new DemDownloader(fineLow, cop, 3, repo) { FreeSpace = () => freeLow, Log = s => { lock (logLow) logLow.Add(s); } })
    {
        var bLow = RunAll(dlLow);
        Check(bLow.Done == 0 && bLow.Error != null && bLow.Error.Contains("free on the disk") && dlLow.BytesDownloaded == 0
            && !Directory.EnumerateFiles(fineLow.Folder, "*" + FineElevation.FileExtension).Any(),
            $"EA-0100: with 0.2 GB free and a 1 GB reserve download all does not start ('{bLow.Error}'), nothing read or written");
        var (la0, lo0) = roomSquares[0];
        bool gen = dlLow.FetchAsync(la0, lo0).Wait(TimeSpan.FromSeconds(30)) && dlLow.FetchAsync(la0, lo0).Result;
        Check(gen && fineLow.IsHere(la0, lo0), "EA-0100: a square for generation is still fetched with 0.2 GB free (generation's floor is 0.1 GB)");
        freeLow = 50_000_000;
        long before = dlLow.BytesDownloaded;
        var (la1, lo1) = roomSquares[1];
        bool got = dlLow.FetchAsync(la1, lo1).Wait(TimeSpan.FromSeconds(30)) && dlLow.FetchAsync(la1, lo1).Result;
        bool tried;
        lock (logLow) tried = logLow.Any(l => l.Contains("Copernicus"));
        Check(!got && dlLow.BytesDownloaded == before && !tried && dlLow.StoreError != null,
            "EA-0100: with 50 MB free a square for generation fails without a byte read or Copernicus tried");
    }

    // with room: every square, no error
    var fineOk = new FineElevation(Path.Combine(full, "glo90-ok"), roomList);
    using (var dlOk = new DemDownloader(fineOk, cop, 3, repo) { FreeSpace = () => 100_000_000_000 })
    {
        var bOk = RunAll(dlOk);
        Check(bOk.Done == roomSquares.Count && bOk.Error == null && bOk.Failed == 0,
            $"EA-0100: with room download all fetches every square ({bOk.Done} of {roomSquares.Count}, error '{bOk.Error}')");
    }
}

// EA-0116 T-L10: a tile that can't be read for a moment (another process holds it) is not set aside as damaged; it is read
// once it is free. D-R3: a killed process's leftovers are swept: tiles' unique .part files an hour old, .bad tiles 30 days
// old; a release file's own .part (to be resumed), fresh ones and .differs tiles stay
{
    string lockedDir = work + "-locked";
    if (Directory.Exists(lockedDir)) Directory.Delete(lockedDir, true);
    var fineL = new FineElevation(lockedDir, File.ReadAllBytes(list));
    Directory.CreateDirectory(fineL.Folder);
    var someTile = Directory.EnumerateFiles(work, "*" + FineElevation.FileExtension).First();
    var heldTile = DemTiles.Decode(File.ReadAllBytes(someTile));
    string heldPath = fineL.PathOf(heldTile.LatS, heldTile.LonW);
    File.Copy(someTile, heldPath);
    bool readWhileHeld, readAfter;
    using (new FileStream(heldPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        readWhileHeld = fineL.TryHeight(heldTile.LatS + 0.5, heldTile.LonW + 0.5, out _);
    fineL.TileArrived(heldTile.LatS, heldTile.LonW);
    readAfter = fineL.TryHeight(heldTile.LatS + 0.5, heldTile.LonW + 0.5, out _);
    Check(!readWhileHeld && readAfter && !File.Exists(heldPath + ".bad") && File.Exists(heldPath),
        $"EA-0116 T-L10: a tile held by another process is not set aside ({fineL.LastError}), and is read once free");

    string Touch(string name, TimeSpan age)
    {
        string p = Path.Combine(fineL.Folder, name);
        File.WriteAllBytes(p, new byte[10]);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow - age);
        return p;
    }
    var oldPart = Touch("N10E020.egl.0123456789ab.part", TimeSpan.FromHours(2));
    var newPart = Touch("N10E021.egl.0123456789ac.part", TimeSpan.FromMinutes(5));
    var resumable = Touch("lakes-glo90-2.bin.part", TimeSpan.FromDays(3));
    var oldBad = Touch("N10E022.egl.bad", TimeSpan.FromDays(40));
    var newBad = Touch("N10E023.egl.bad", TimeSpan.FromDays(2));
    var differs = Touch("N10E024.egl.differs", TimeSpan.FromDays(400));
    int swept = fineL.SweepLeftovers();
    Check(swept == 2 && !File.Exists(oldPart) && !File.Exists(oldBad) && File.Exists(newPart) && File.Exists(resumable) && File.Exists(newBad) && File.Exists(differs)
        && File.Exists(heldPath), $"EA-0116 D-R3: {swept} leftovers swept (an hour-old tile .part, a 40-day-old .bad); a release file's .part, fresh ones, .differs and tiles kept");
}

// EA-0099 (the Fable pass's FP-03): a square whose fetch failed for a reason that may pass, with the server's Unavailable
// handler set, throws for the chunk being made and is not kept on the built-in heights (no fallback marked); without the
// handler, or where the sources lack the square, it falls back as before
{
    var fineU = new FineElevation(work + "-unavailable", File.ReadAllBytes(list));
    int sessionFallbacks = 0, forGood = 0, told = 0;
    fineU.Fetch = (_, _) => false;
    fineU.Lacks = (la, _) => la == 28;
    fineU.FellBackForSession = (_, _) => sessionFallbacks++;
    fineU.FellBack = (_, _) => forGood++;
    fineU.Unavailable = (la, lo) => { told++; throw new TileUnavailableException(la, lo, "test"); };
    FineElevation.FetchAllowed = true;
    TileUnavailableException thrown = null;
    try { fineU.TryHeight(27.5, 86.5, out _); } catch (TileUnavailableException e) { thrown = e; }
    Check(thrown != null && thrown.LatS == 27 && thrown.LonW == 86 && told == 1 && !fineU.IsFallback(27, 86) && sessionFallbacks == 0,
        "EA-0099: a square that can't be had now throws TileUnavailableException for the chunk and is not kept on built-in heights");
    TileUnavailableException again = null;
    try { fineU.TryHeight(27.5, 86.5, out _); } catch (TileUnavailableException e) { again = e; }
    Check(again != null && told == 2, "EA-0099: and so it does at the next read (a later chunk), not a fallback from the first");
    bool lacked = fineU.TryHeight(28.5, 86.5, out _);
    Check(!lacked && forGood == 1 && fineU.IsFallbackForGood(28, 86) && told == 2,
        "EA-0099: a square the sources lack still keeps the built-in heights for good, Unavailable not asked");
    // outside a chunk pass (a map region): true, the built-in heights for this read, the square not given up
    int asked = 0;
    fineU.Unavailable = (_, _) => { asked++; return true; };
    bool outside1 = fineU.TryHeight(27.5, 88.5, out _), outside2 = fineU.TryHeight(27.5, 88.5, out _);
    Check(!outside1 && !outside2 && asked == 2 && !fineU.IsFallback(27, 88) && sessionFallbacks == 0,
        "EA-0099: outside a chunk pass the read takes the built-in heights, the square is not given up and is asked for again");
    fineU.Unavailable = (_, _) => false;
    bool before = fineU.TryHeight(27.5, 87.5, out _);
    Check(!before && sessionFallbacks == 1 && fineU.IsFallback(27, 87) && !fineU.IsFallbackForGood(27, 87),
        "EA-0099: when Unavailable says so (before the game runs), the square falls back for this session as before");
    bool outsideScope = FineElevation.InChunkPass, inner, afterInner;
    using (FineElevation.ChunkPass())
    {
        using (FineElevation.ChunkPass()) inner = FineElevation.InChunkPass;
        afterInner = FineElevation.InChunkPass;
    }
    Check(!outsideScope && inner && afterInner && !FineElevation.InChunkPass, "EA-0099: the chunk pass scope nests and ends");
    FineElevation.FetchAllowed = false;
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
// (Fable's overview low 8) a fetch from Copernicus doesn't wait for the release's manifest first: a release that never
// answers (a listener that takes the connection and stays silent; the transport would wait 3 minutes) doesn't hold it up
{
    var silent = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    silent.Start();
    var held = new System.Collections.Concurrent.ConcurrentBag<System.Net.Sockets.Socket>();
    _ = Task.Run(async () => { try { while (true) held.Add(await silent.AcceptSocketAsync()); } catch (Exception) { } });
    string mute = $"http://127.0.0.1:{((System.Net.IPEndPoint)silent.LocalEndpoint).Port}/";
    string muteWork = work + "-mute";
    if (Directory.Exists(muteWork)) Directory.Delete(muteWork, true);
    var fineM = new FineElevation(muteWork, File.ReadAllBytes(list));
    var dlM = new DemDownloader(fineM, src, 3, mute) { Retries = 1, Log = s => Console.WriteLine("  log: " + s) };
    string first = Directory.EnumerateFiles(src, "Copernicus_DSM_COG_30_*.tif").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).First();
    DemTiles.TryParseSourceName(first, out int laM, out int loM);
    var muteClock = Stopwatch.StartNew();
    var fetchM = dlM.FetchAsync(laM, loM, copernicus: true);
    bool doneM = fetchM.Wait(TimeSpan.FromSeconds(30));
    Check(doneM && fetchM.Result && dlM.TilesConverted == 1 && fineM.IsHere(laM, loM),
        $"low 8: from Copernicus with a release that never answers, converted at once ({muteClock.Elapsed.TotalSeconds:F1} s), the manifest not waited for");

    // EA-0121: /earth tp's wait for a square (Core.TeleportWait). The bench's case, a release that never answers and no
    // Copernicus: the wait's limit (here 1 s) ends with the fetch still running, and nobody is teleported; a release that
    // refuses the connection: the fetch fails and the message gives the manifest's reason; a square fetched, or one the
    // sources lack (built-in heights for good, its chunks are made): teleported
    string sqT = DemTiles.ShortName(laM, loM);
    var limitT = TimeSpan.FromSeconds(1);
    var dlT = new DemDownloader(new FineElevation(muteWork + "-tp", File.ReadAllBytes(list)), null, 3, mute) { Retries = 1, Log = s => Console.WriteLine("  log: " + s) };
    var hanging = dlT.FetchAsync(laM, loM);
    Task.WhenAny(hanging, Task.Delay(limitT)).Wait();
    var (goT, msgT) = TeleportWait.After(hanging, dlT.Lacks(laM, loM), dlT.WhyFailed(laM, loM), sqT, "there", limitT);
    Check(!hanging.IsCompleted && !goT && msgT.Contains("not here after") && msgT.Contains("not teleported"),
        $"EA-0121: a release that never answers: the wait ends at its limit, no teleport ({msgT})");
    dlT.Dispose();
    var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    probe.Start();
    int closedPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    using (var dlR = new DemDownloader(new FineElevation(muteWork + "-tpR", File.ReadAllBytes(list)), null, 3, $"http://127.0.0.1:{closedPort}/") { Retries = 1, Log = s => Console.WriteLine("  log: " + s) })
    {
        var refusedT = dlR.FetchAsync(laM, loM);
        bool endedR = refusedT.Wait(TimeSpan.FromSeconds(90));
        var (goR, msgR) = TeleportWait.After(refusedT, dlR.Lacks(laM, loM), dlR.WhyFailed(laM, loM), sqT, "there", TeleportWait.Limit);
        Check(endedR && !refusedT.Result && !goR && msgR.Contains("manifest could not be had") && msgR.Contains("not teleported"),
            $"EA-0121: a release that refuses the connection: the fetch fails, no teleport, the reason given ({msgR})");
    }
    Check(TeleportWait.After(Task.FromResult(true), false, null, sqT, "there", limitT).Teleport
        && TeleportWait.After(Task.FromResult(false), true, "the sources lack it", sqT, "there", limitT).Teleport,
        "EA-0121: teleported when the tile is here, or when the sources lack the square (built-in heights for good)");
    silent.Stop();
    foreach (var c in held) c.Dispose();
    if (doneM) dlM.Dispose();
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
    // EA-0106 (Fable's re-review of 255baa7): the shipped heights table is the release's: every 50th tile of the store decodes
    // to the table's checksum
    {
        int sampled = 0, agree = 0;
        foreach (var n in all.Where((n, i) => i % 50 == 0))
        {
            var t = DemTiles.Decode(Stored(n));
            sampled++;
            if (releaseHeights != null && releaseHeights.TryGet(t.LatS, t.LonW, out ulong sum) && sum == DemTiles.Checksum(t.Heights)) agree++;
        }
        Check(releaseHeights != null && releaseHeights.Count == all.Count && agree == sampled,
            $"EA-0106: data/{Glo90Heights.FileName} holds {releaseHeights?.Count} squares ({all.Count} in the store); {agree} of {sampled} sampled tiles' heights agree");
    }
    // the damaged square from Copernicus instead: its heights are the release's (EA-0106), so it is kept
    var fineB = new FineElevation(Fresh("-relB"), bitmap);
    using (var dl = new DemDownloader(fineB, src, 3, rel) { Retries = 1, ReleaseHeights = releaseHeights, Log = s => Console.WriteLine("  log: " + s) })
        Check(dl.FetchBlocking(brokenLat, brokenLon) && dl.TilesConverted == 1 && dl.TilesFetched == 0 && dl.TilesDiffering == 0 && SameHeights(fineB.Folder, broken),
            $"{broken}: from Copernicus where the release's copy is damaged, the release's heights (judged by heights, on this runtime: {Environment.Version})");

    // EA-0106 (the Fable pass's FP-15): a conversion that is not the release's tile is not kept, and the release's is preferred.
    // A one-square release whose tile for the broken square has one height changed (as if Copernicus's file had been issued
    // again since): from Copernicus only, the conversion is refused and the release's tile kept; with the release's part
    // gone, neither is kept, and the square is not one the sources lack
    {
        string rel2 = Fresh("-rel2");
        Directory.CreateDirectory(rel2);
        var other = DemTiles.Decode(Stored(broken));
        other.Heights[12345] += 7;
        other.Min = other.Heights.Min(); other.Max = other.Heights.Max();
        byte[] altered = DemTiles.Encode(other);
        File.WriteAllBytes(Path.Combine(rel2, "p.zip"), altered);
        File.WriteAllText(Path.Combine(rel2, DemDownloader.ManifestName), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "egl", ["formatVersion"] = 1,
            ["parts"] = new[] { new Dictionary<string, object> { ["name"] = "p.zip", ["bytes"] = altered.Length } },
            ["tiles"] = new Dictionary<string, object[]> { [broken] = new object[] { 1, 0, altered.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(altered)).ToLowerInvariant() } },
        }));
        var alteredHeights = new Glo90Heights(new Dictionary<(int LatS, int LonW), ulong> { [(brokenLat, brokenLon)] = DemTiles.Checksum(other.Heights) });
        var fineE = new FineElevation(Fresh("-relE"), bitmap);
        using (var dl = new DemDownloader(fineE, src, 3, rel2) { Retries = 1, ReleaseHeights = alteredHeights, Log = s => Console.WriteLine("  log: " + s) })
        {
            bool got = dl.FetchAsync(brokenLat, brokenLon, copernicus: true).GetAwaiter().GetResult();
            Check(got && dl.TilesConverted == 0 && dl.TilesFetched == 1 && dl.TilesDiffering == 1
                && File.ReadAllBytes(fineE.PathOf(brokenLat, brokenLon)).AsSpan().SequenceEqual(altered),
                $"EA-0106: {broken} from Copernicus is not the release's tile: not kept, the release's instead ({dl.TilesConverted} converted, {dl.TilesFetched} fetched)");
        }
        File.Delete(Path.Combine(rel2, "p.zip"));
        var fineF = new FineElevation(Fresh("-relF"), bitmap);
        using (var dl = new DemDownloader(fineF, src, 3, rel2) { Retries = 1, ReleaseHeights = alteredHeights, Log = s => Console.WriteLine("  log: " + s) })
            Check(!dl.FetchBlocking(brokenLat, brokenLon) && !fineF.IsHere(brokenLat, brokenLon) && !dl.Lacks(brokenLat, brokenLon) && dl.TilesDiffering == 1,
                $"EA-0106: {broken} with the release's part gone: Copernicus's conversion still not kept, and the square isn't one the sources lack");
    }

    // EA-0106 (FP-15): tiles here are judged by their heights at their first read in a session: one converted from Copernicus
    // with the bucket's ETag and the release's heights is taken as it is (whatever its bytes); one with other heights stays in
    // use while the release's is fetched beside it
    {
        var okNames = part1.Where(n => n != broken).ToList();
        var fineG = new FineElevation(Fresh("-relG"), bitmap);
        Directory.CreateDirectory(fineG.Folder);
        DemDownloader.TryParseShortName(okNames[0], out int laA, out int loA);
        DemDownloader.TryParseShortName(okNames[1], out int laB, out int loB);
        DemDownloader.TryParseShortName(okNames[2], out int laC, out int loC);
        var asBucket = DemTiles.Decode(Stored(okNames[0]));
        asBucket.SourceETag = "5d41402abc4b2a76b9719d911017c592-2";
        File.WriteAllBytes(fineG.PathOf(laA, loA), DemTiles.Encode(asBucket));
        var otherHeights = DemTiles.Decode(Stored(okNames[1]));
        otherHeights.Heights[777] += 3;
        otherHeights.Min = otherHeights.Heights.Min(); otherHeights.Max = otherHeights.Heights.Max();
        File.WriteAllBytes(fineG.PathOf(laB, loB), DemTiles.Encode(otherHeights));
        using (var dl = new DemDownloader(fineG, null, 3, rel) { Retries = 1, ReleaseHeights = releaseHeights, Log = s => Console.WriteLine("  log: " + s) })
        {
            fineG.Verify = dl.KeptMatches;
            fineG.Replace = dl.FetchReplacement;
            fineG.Fetch = (la, lo) => dl.FetchBlocking(la, lo);
            Check(dl.FetchBlocking(laC, loC), "EA-0106: a third square fetched (the manifest is here)");
            bool hA, hB;
            using (FineElevation.Generating())
            {
                hA = fineG.TryHeight(laA + 0.5, loA + 0.5, out _);
                hB = fineG.TryHeight(laB + 0.5, loB + 0.5, out _);
            }
            // (Fable's re-review of 90692fe) the differing tile stays in use; the release's arrives beside it and takes its place
            // at the next start, the old one kept as .differs
            string pathB = fineG.PathOf(laB, loB), releaseB = pathB + FineElevation.ReplacementExtension;
            var waitRelease = Stopwatch.StartNew();
            while (!File.Exists(releaseB) && waitRelease.Elapsed.TotalSeconds < 20) Thread.Sleep(50);
            bool keptInUse = File.ReadAllBytes(pathB).AsSpan().SequenceEqual(DemTiles.Encode(otherHeights)) && !File.Exists(pathB + FineElevation.DiffersExtension);
            bool bucketKept = File.ReadAllBytes(fineG.PathOf(laA, loA)).AsSpan().SequenceEqual(DemTiles.Encode(asBucket))
                && !File.Exists(fineG.PathOf(laA, loA) + FineElevation.ReplacementExtension);
            Check(hA && hB && bucketKept && keptInUse && File.Exists(releaseB) && dl.TilesDiffering == 1,
                $"EA-0106: tiles here at their first read: {okNames[0]} (the bucket's ETag, the release's heights) taken as it is, {okNames[1]} " +
                $"(other heights) still in use with the release's fetched beside it ({dl.TilesDiffering} differing)");
            var said = new List<string>();
            var (sameH, otherH) = fineG.ApplyReplacements(said.Add);
            Check(sameH == 0 && otherH == 1 && Same(fineG.Folder, okNames[1]) && File.Exists(pathB + FineElevation.DiffersExtension) && !File.Exists(releaseB)
                && said.Count == 1 && said[0].Contains(okNames[1]) && said[0].Contains("keep the old heights"),
                $"EA-0106: at the next start the release's tile takes the differing one's place, the old kept as .differs ('{said.FirstOrDefault()}')");
            // the same heights in another encoding: replaced without a .differs file or a word
            var reencoded = DemTiles.Decode(Stored(okNames[2]));
            reencoded.SourceBytes += 1;
            File.WriteAllBytes(fineG.PathOf(laC, loC), DemTiles.Encode(reencoded));
            File.WriteAllBytes(fineG.PathOf(laC, loC) + FineElevation.ReplacementExtension, Stored(okNames[2]));
            said.Clear();
            var (sameC, otherC) = fineG.ApplyReplacements(said.Add);
            Check(sameC == 1 && otherC == 0 && Same(fineG.Folder, okNames[2]) && !File.Exists(fineG.PathOf(laC, loC) + FineElevation.DiffersExtension) && said.Count == 0,
                "EA-0106: a replacement with the same heights (another encoding) takes the place silently, no .differs");
        }
    }
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
