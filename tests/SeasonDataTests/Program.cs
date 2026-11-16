// EA-0144: the monthly temperature layer's reader (src/Core/SeasonData.cs) against the synthetic fixture that
// tools/write_seasons.py --synthetic writes (tests/fixtures/seasons-synthetic.bin): four 90-cell tiles in a 2 x 2 block,
// every other tile empty, each value from the formula below (the writer's SYNTHETIC). The file from its bytes, from disk,
// and a player's layer filled tile by tile as the server sends them.
using System.Diagnostics;
using EarthMap.Core;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    if (!ok) failures++;
}

// --cost=<file>: Fable's F2, what live readings cost with players far apart (a measurement, no checks). A 1:100 world's
// block grid (1,203 blocks a degree east-west, 1,112 north-south); each player 1,500 readings a second over the loaded land
// around them (±512 blocks), and once a second a chunk loading with vanilla's catch-up (30 crops, each read at its block
// for each of 30 past days). The tile cache at 12 (before) and sized for the players (after); the twelve TryCells4
// lookups of a reading (before) against TryMonths4 (after); the sun's times kept in one entry a thread (before) and in
// the weather session's table (after: 32 entries, keyed by the day and areas of 2,048 x 256 blocks, hashed as
// EarthClimateHooks.SunTimes hashes them). One thread, 60 game seconds.
if (args.FirstOrDefault(a => a.StartsWith("--cost=")) is string costArg)
{
    const double bx = 433152 / 360.0, bz = 200192 / 180.0;
    var scenarios = new (string Name, (double Lat, double Lon)[] Players)[]
    {
        ("1 player (Riga)", new[] { (56.95, 24.10) }),
        ("3 players far apart (Riga, Mumbai, Los Angeles)", new[] { (56.95, 24.10), (19.08, 72.88), (34.05, -118.25) }),
        ("4 players each at a tile corner", new[] { (51.0, 0.0), (30.0, 33.0), (-12.0, -69.0), (3.0, 102.0) }),
        ("13 players spread over the world", Enumerable.Range(0, 13).Select(i => (-50.0 + i * 8.7, -170.0 + i * 26.0)).ToArray()),
    };
    // --sun-slots=32,128,...: only the after case, once for each size of the sun table
    var slotSizes = args.FirstOrDefault(a => a.StartsWith("--sun-slots=")) is string slotArg
        ? slotArg.Substring(12).Split(',').Select(int.Parse).ToArray() : null;
    var runs = slotSizes == null ? new[] { (false, 32), (true, 32) } : slotSizes.Select(n => (true, n)).ToArray();
    foreach (var (name, players) in scenarios)
    {
        foreach (var (after, slots) in runs)
        {
            using var data = SeasonData.Open(costArg.Substring(7));
            if (after) data.CachedTiles = SeasonData.TilesFor(players.Length);
            var layer = new SeasonDataLayer(data);
            var cell = new SeasonCell();
            var rnd = new Random(1);
            float[] m4 = new float[4], r4 = new float[4], s4 = new float[4];
            // the sun's times: before, one entry; after, the table (a world number of its own each run)
            long oldKeyDay = long.MinValue, oldZx = 0, oldZz = 0, sunMisses = 0;
            var suns = new (bool Valid, long Day, long Zx, long Zz)[slots];
            long today = 10_000;
            void Read(double x, double z, long day)
            {
                double lat = 90 - z / bz, lon = x / bx - 180;
                if (after) layer.Sample(lat, lon, cell);
                else
                {
                    // the branch's reading before F2: twelve TryCells4, each with its own tile lookup
                    double fr = (90 - lat) * SeasonData.Res - 0.5, fc = (lon + 180) * SeasonData.Res - 0.5;
                    int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
                    for (int month = 0; month < 12; month++) data.TryCells4(r0, c0, month, m4, r4, s4);
                }
                long zx = (long)Math.Floor(x / 2048);
                if (after)
                {
                    long zz = (long)Math.Floor(z / 256);
                    ulong hash = (ulong)day * 0x9E3779B97F4A7C15UL ^ (ulong)zx * 0xC2B2AE3D27D4EB4FUL ^ (ulong)zz * 0x165667B19E3779F9UL;
                    ref var e = ref suns[(int)((hash ^ (hash >> 29)) % (ulong)slots)];
                    if (!(e.Valid && e.Day == day && e.Zx == zx && e.Zz == zz)) { sunMisses++; e = (true, day, zx, zz); }
                }
                else
                {
                    long zz = (long)Math.Floor(z / 256);
                    if (!(oldKeyDay == day && oldZx == zx && oldZz == zz)) { sunMisses++; oldKeyDay = day; oldZx = zx; oldZz = zz; }
                }
            }
            long decodesBefore = data.Decodes;
            var clock = Stopwatch.StartNew();
            long readings = 0;
            for (int second = 0; second < 60; second++)
            {
                // the players' readings come interleaved, as the server's ticks serve them all
                for (int k = 0; k < 1500; k++)
                    foreach (var (plat, plon) in players)
                    {
                        double px = (plon + 180) * bx, pz = (90 - plat) * bz;
                        Read(px + rnd.Next(-512, 513), pz + rnd.Next(-512, 513), today);
                        readings++;
                    }
                // a chunk loading at the edge of each player's land: 30 crops caught up over 30 past days
                foreach (var (plat, plon) in players)
                {
                    double px = (plon + 180) * bx + rnd.Next(-512, 513), pz = (90 - plat) * bz + rnd.Next(-512, 513);
                    for (int crop = 0; crop < 30; crop++)
                    {
                        double cx = px + rnd.Next(0, 32), cz = pz + rnd.Next(0, 32);
                        for (int d = 30; d >= 1; d--) { Read(cx, cz, today - d); readings++; }
                    }
                }
            }
            double ms = clock.Elapsed.TotalMilliseconds;
            long decodes = data.Decodes - decodesBefore;
            long sun = sunMisses;
            Console.WriteLine($"{name}, {(after ? "after" : "before")}{(slotSizes != null ? $" (sun table {slots})" : "")}: {readings:N0} readings in 60 s, {ms / readings * 1000:F2} µs a reading (layer only); " +
                $"{decodes:N0} tile decodes ({data.CachedTiles} kept); {sun:N0} sun-time misses ({(double)sun / readings * 100:F2} % of readings, 97 samples of the sun each)");
        }
    }
    return 0;
}

// --sun-cost: what one sun-time miss costs, with a stand-in for vanilla's sun (declination and hour angle, the zenith's
// cosine; about the trigonometry of GameCalendar's solar coordinates) sampled 97 times, as EarthClimateHooks.SunTimes does
if (args.Contains("--sun-cost"))
{
    Func<double, double, double, double> zenith = (latRad, yearRel, hourRel) =>
    {
        double decl = -23.44 * Math.PI / 180 * Math.Cos(2 * Math.PI * (yearRel + 10 / 365.0));
        double cosZ = Math.Sin(latRad) * Math.Sin(decl) + Math.Cos(latRad) * Math.Cos(decl) * Math.Cos(2 * Math.PI * (hourRel - 0.5));
        return Math.Acos(Math.Clamp(cosZ, -1, 1));
    };
    double sink = 0;
    var sunClock = Stopwatch.StartNew();
    const int misses = 200_000;
    for (int i = 0; i < misses; i++)
    {
        double lat = (i % 160 - 80) * Math.PI / 180, yr = i % 108 / 108.0;
        for (int k = 0; k <= 96; k++) sink += Math.Cos(zenith(lat, yr, k / 96.0));
    }
    Console.WriteLine($"one sun-time miss (97 samples of a stand-in sun): {sunClock.Elapsed.TotalMilliseconds * 1000 / misses:F2} µs ({sink:F0})");
    return 0;
}

// --timing=<file>: decode time, tile sizes and memory on a real seasons file (a measurement, no checks)
if (args.FirstOrDefault(a => a.StartsWith("--timing=")) is string timingArg)
{
    var real = SeasonData.Open(timingArg.Substring(9));
    var times = new List<double>();
    var sizes = new List<int>();
    long memBefore = GC.GetTotalMemory(true);
    for (int key = 0; key < real.TileCount; key++)
    {
        int len = real.TileBlob(key).Length;
        if (len == 0) continue;
        sizes.Add(len);
        int tr = key / real.TilesAcross * real.Tile, tc = key % real.TilesAcross * real.Tile;
        var clock = Stopwatch.StartNew();
        real.TryCell(tr, tc, 0, out _, out _, out _);
        times.Add(clock.Elapsed.TotalMilliseconds);
    }
    long memAfter = GC.GetTotalMemory(true);
    times.Sort();
    sizes.Sort();
    Console.WriteLine($"{times.Count} tiles with data: decode (blob read from disk, brotli, unpredict) median {times[times.Count / 2]:F2} ms, " +
        $"90 % {times[(int)(times.Count * 0.9)]:F2} ms, max {times[^1]:F2} ms; blob median {sizes[sizes.Count / 2]:N0} bytes, max {sizes[^1]:N0}; " +
        $"managed memory with the cache full ({SeasonData.MaxCachedTiles} tiles) {(memAfter - memBefore) / 1e6:F1} MB");
    // --cells=row:col,...: January and July as the reader gives them, in the bake's units (to compare with the .npz)
    if (args.FirstOrDefault(a => a.StartsWith("--cells=")) is string cellsArg)
        foreach (var cell in cellsArg.Substring(8).Split(','))
        {
            var rc = cell.Split(':');
            int row = int.Parse(rc[0]), col = int.Parse(rc[1]);
            real.TryCell(row, col, 0, out float cm0, out float cr0, out float cs0);
            real.TryCell(row, col, 6, out float cm6, out float cr6, out float cs6);
            real.TryProvenance(row, col, out byte prov);
            Console.WriteLine($"{row} {col} jan {Math.Round(cm0 * 10)} {Math.Round(cr0 / 0.2)} {Math.Round(cs0 * 10)} jul {Math.Round(cm6 * 10)} {Math.Round(cr6 / 0.2)} {Math.Round(cs6 * 10)} prov {prov}");
        }
    real.Dispose();
    return 0;
}

string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string fixture = Path.Combine(root, "tests", "fixtures", "seasons-synthetic.bin");
byte[] file = File.ReadAllBytes(fixture);
const int T = 90;
(int ty, int tx)[] dataTiles = { (10, 20), (10, 21), (11, 20), (11, 21) };

int Mean(int m, int r, int c) => 5 * ((r * 7 + c * 3 + m * 11) % 400 - 200);
int Range(int m, int r, int c) => (r + c + 3 * m) % 200;
int Spread(int m, int r, int c) => (2 * r + c + m) % 100;
int Prov(int r, int c) => (r + c) % 8;

bool Same(float a, float b) => Math.Abs(a - b) < 1e-4f;

// every cell of every month in the four tiles, as the formula says
bool CheckAll(SeasonData s, out string first)
{
    first = null;
    foreach (var (ty, tx) in dataTiles)
        for (int r = ty * T; r < (ty + 1) * T; r++)
            for (int c = tx * T; c < (tx + 1) * T; c++)
            {
                for (int m = 0; m < 12; m++)
                {
                    if (!s.TryCell(r, c, m, out float mean, out float range, out float spread)
                        || !Same(mean, Mean(m, r, c) * 0.1f) || !Same(range, Range(m, r, c) * 0.2f) || !Same(spread, Spread(m, r, c) * 0.1f))
                    {
                        first = $"row {r}, col {c}, month {m}: {mean}, {range}, {spread}";
                        return false;
                    }
                }
                if (!s.TryProvenance(r, c, out byte bits) || bits != Prov(r, c))
                {
                    first = $"row {r}, col {c}: provenance {bits}";
                    return false;
                }
            }
    return true;
}

// 1. the file from its bytes
var sw = Stopwatch.StartNew();
var fromBytes = SeasonData.Read(file);
Check(fromBytes.Tile == T && fromBytes.TileCount == (10800 / T) * (5400 / T), $"the fixture: {fromBytes.TileCount} tiles of {fromBytes.Tile} cells");
Check(CheckAll(fromBytes, out string bad), $"every cell and month of the four tiles reads as baked ({sw.ElapsedMilliseconds} ms){(bad == null ? "" : ": " + bad)}");
Check(!fromBytes.TryCell(0, 0, 0, out float m0, out _, out _) && float.IsNaN(m0), "a tile without data answers false, NaN");

// 2. four cells at once: inside a tile (one lookup) and across a tile's corner (four), the same as one by one
bool Quad(SeasonData s, int r0, int c0, int month)
{
    Span<float> mean = stackalloc float[4], range = stackalloc float[4], spread = stackalloc float[4];
    if (!s.TryCells4(r0, c0, month, mean, range, spread)) return false;
    for (int k = 0; k < 4; k++)
    {
        s.TryCell(r0 + (k >> 1), c0 + (k & 1), month, out float a, out float b, out float c);
        if (!Same(a, mean[k]) || !Same(b, range[k]) || !Same(c, spread[k])) return false;
    }
    return true;
}
Check(Quad(fromBytes, 10 * T + 5, 20 * T + 7, 3) && Quad(fromBytes, 10 * T + T - 1, 20 * T + T - 1, 11),
    "four cells inside a tile and across a tile's corner match the cells one by one");
Span<float> qm = stackalloc float[4], qr = stackalloc float[4], qs = stackalloc float[4];
Check(!fromBytes.TryCells4(10 * T - 1, 20 * T, 0, qm, qr, qs) && float.IsNaN(qm[0]) && float.IsNaN(qs[3]),
    "four cells reaching into a tile without data answer false, all NaN");
// (Fable's F2) all twelve months of the four cells at once, the same as TryCells4 month by month: inside a tile, across a
// tile's corner, and false with NaN reaching into a tile without data
bool Months(SeasonData s, int r0, int c0)
{
    Span<float> mm = stackalloc float[48], rr = stackalloc float[48], ss = stackalloc float[48];
    Span<float> m1 = stackalloc float[4], r1 = stackalloc float[4], s1 = stackalloc float[4];
    if (!s.TryMonths4(r0, c0, mm, rr, ss)) return false;
    for (int month = 0; month < 12; month++)
    {
        if (!s.TryCells4(r0, c0, month, m1, r1, s1)) return false;
        for (int k = 0; k < 4; k++)
            if (!Same(mm[k * 12 + month], m1[k]) || !Same(rr[k * 12 + month], r1[k]) || !Same(ss[k * 12 + month], s1[k])) return false;
    }
    return true;
}
Span<float> miss48 = stackalloc float[48], rmiss48 = stackalloc float[48], smiss48 = stackalloc float[48];
Check(Months(fromBytes, 10 * T + 5, 20 * T + 7) && Months(fromBytes, 10 * T + T - 1, 20 * T + T - 1)
    && !fromBytes.TryMonths4(10 * T - 1, 20 * T, miss48, rmiss48, smiss48) && float.IsNaN(miss48[0]) && float.IsNaN(smiss48[47]),
    "all twelve months at once match TryCells4 month by month, inside a tile and across a corner; false with NaN into a tile without data");
Check(SeasonData.TilesFor(0) == 12 && SeasonData.TilesFor(1) == 12 && SeasonData.TilesFor(3) == 20 && SeasonData.TilesFor(13) == 60
    && SeasonData.TilesFor(1000) == SeasonData.MaxCachedTilesBound, "the decoded tiles for the players online: 12 at least, four a player and eight, 256 at most");
Check(SeasonData.TilesPerPlayer(3600, 3600, 12) == 4 && SeasonData.TilesPerPlayer(1950, 1950, 30) == 4
    && SeasonData.TilesPerPlayer(1950, 1950, 32) == 9 && SeasonData.TilesPerPlayer(1950, 1950, 64) == 16
    && SeasonData.TilesFor(3, SeasonData.TilesPerPlayer(1950, 1950, 32)) == 35,
    "a player's loaded land touches four tiles at the usual view distances, nine at 1:200 past a radius of about 30 chunks");
fromBytes.CachedTiles = 5;
int keptLow = fromBytes.CachedTiles;
fromBytes.CachedTiles = 100;
Check(keptLow == 12 && fromBytes.CachedTiles == 100, "the cache's size is kept within its bounds");
bool threw = false;
try { fromBytes.TryCell(10 * T, 20 * T, 12, out _, out _, out _); } catch (ArgumentOutOfRangeException) { threw = true; }
Check(threw, "month 12 is refused");

// 3. the server's way: the file on disk, tiles read as needed; a tile's bytes as the file holds them
string copy = Path.Combine(Path.GetTempPath(), "seasons-test-" + Environment.ProcessId + ".bin");
File.WriteAllBytes(copy, file);
using (var onDisk = SeasonData.Open(copy))
{
    Check(CheckAll(onDisk, out bad), "the file on disk reads the same" + (bad == null ? "" : ": " + bad));
    int key = onDisk.KeyOf(10 * T, 21 * T, out _);
    Check(onDisk.TileBlob(key).AsSpan().SequenceEqual(fromBytes.TileBlob(key)) && onDisk.TileBlob(0).Length == 0,
        $"a tile's bytes from disk are the file's ({onDisk.TileBlob(key).Length:N0} bytes); an empty tile has none");
}
File.Delete(copy);

// 4. a player's game: nothing until the server's tiles arrive, each missing tile reported once while it waits
var client = SeasonData.ForTiles(T);
var reported = new List<int>();
client.Missing = reported.Add;
int k1 = client.KeyOf(10 * T, 20 * T, out _);
bool before = client.TryCell(10 * T, 20 * T, 0, out _, out _, out _);
client.TryCell(10 * T + 1, 20 * T + 1, 5, out _, out _, out _);
Check(!before && reported.Count == 1 && reported[0] == k1, $"a player's layer without the tile answers false and reports the tile once ({reported.Count})");
client.AskAgainMs = 0;
client.TryCell(10 * T, 20 * T, 0, out _, out _, out _);
Check(reported.Count == 2, "and again after the wait");
foreach (var (ty, tx) in dataTiles)
{
    int key = ty * (10800 / T) + tx;
    client.AddTile(key, fromBytes.TileBlob(key));
}
Check(CheckAll(client, out bad), "with the four tiles the server sent, the player's layer reads as the server's" + (bad == null ? "" : ": " + bad));
// a damaged packet: dropped when read, reported again, fine once sent again
var damaged = fromBytes.TileBlob(k1);
damaged[damaged.Length / 2] ^= 0x5A;
var fresh = SeasonData.ForTiles(T);
var again = new List<int>();
fresh.Missing = again.Add;
fresh.AddTile(k1, damaged);
bool readDamaged = fresh.TryCell(10 * T, 20 * T, 0, out _, out _, out _);
fresh.AddTile(k1, fromBytes.TileBlob(k1));
bool readGood = fresh.TryCell(10 * T + 3, 20 * T + 4, 7, out float g, out _, out _) && Same(g, Mean(7, 10 * T + 3, 20 * T + 4) * 0.1f);
Check(!readDamaged && again.Count == 1 && again[0] == k1 && readGood, "a damaged tile from the network is dropped and asked for again; sent again, it reads");
fresh.AddTile(-1, new byte[1]);
fresh.AddTile(fresh.TileCount, new byte[1]);
Check(!fresh.HasTile(-1) && !fresh.HasTile(fresh.TileCount), "keys outside the grid are ignored");

// 5. layout checks
var wrong = (byte[])file.Clone();
wrong[0] = (byte)'X';
threw = false;
try { SeasonData.Read(wrong); } catch (InvalidDataException) { threw = true; }
bool threwTile = false;
var wrongTile = (byte[])file.Clone();
BitConverter.TryWriteBytes(wrongTile.AsSpan(16), 180);
try { SeasonData.Read(wrongTile); } catch (InvalidDataException) { threwTile = true; }
Check(threw && threwTile, "another magic or a tile size that doesn't fit the index is refused");

// 6. the download (the one protocol of the data store and the ground sets): a source folder holding the file, one without
// it, none set, and a copy that doesn't check out
string work = Path.Combine(Path.GetTempPath(), "seasons-ensure-" + Environment.ProcessId);
if (Directory.Exists(work)) Directory.Delete(work, true);
string repo = Path.Combine(work, "repo"), earth = Path.Combine(work, "earth"), emptyRepo = Path.Combine(work, "empty");
Directory.CreateDirectory(repo);
Directory.CreateDirectory(emptyRepo);
File.WriteAllBytes(Path.Combine(repo, SeasonData.FileName), file);
string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant();
var spec = new DataTransport.FileSpec(SeasonData.FileName, SeasonData.PathIn(earth), file.Length, sha);
var log = new List<string>();
var (got, why) = SeasonData.EnsureAsync(earth, repo, log.Add, default, spec).GetAwaiter().GetResult();
bool placed = File.Exists(SeasonData.PathIn(earth)) && File.ReadAllBytes(SeasonData.PathIn(earth)).AsSpan().SequenceEqual(file);
using (var opened = placed ? SeasonData.Open(SeasonData.PathIn(earth)) : null)
    Check(got == DataTransport.SetResult.Here && placed && opened != null && opened.TryCell(10 * T, 20 * T, 4, out float e4, out _, out _) && Same(e4, Mean(4, 10 * T, 20 * T) * 0.1f),
        $"fetched from a source folder into ModData/earth/data, checked, and read ({got})");
var (again2, _) = SeasonData.EnsureAsync(earth, emptyRepo, log.Add, default, spec).GetAwaiter().GetResult();
Check(again2 == DataTransport.SetResult.Here, "a file already here and checked isn't fetched again (whatever the source)");
File.Delete(SeasonData.PathIn(earth));
var (lacking, whyLacking) = SeasonData.EnsureAsync(earth, emptyRepo, log.Add, default, spec).GetAwaiter().GetResult();
var (none, _) = SeasonData.EnsureAsync(earth, "", log.Add, default, spec).GetAwaiter().GetResult();
Check(lacking == DataTransport.SetResult.Lacking && none == DataTransport.SetResult.Lacking && !File.Exists(SeasonData.PathIn(earth)),
    $"a source without the file, and no source, answer Lacking ({whyLacking})");
var wrongSpec = spec with { Sha256 = new string('0', 64) };
var (failed, whyFailed) = SeasonData.EnsureAsync(earth, repo, log.Add, default, wrongSpec).GetAwaiter().GetResult();
Check(failed == DataTransport.SetResult.Failed && !File.Exists(SeasonData.PathIn(earth)), $"a copy that doesn't check out isn't kept: Failed ({whyFailed})");
Check(SeasonData.Published && SeasonData.FileBytes == 58_168_345 && SeasonData.FileSha256.Length == 64
    && SeasonData.Spec(earth).Target == SeasonData.PathIn(earth) && SeasonData.Spec(earth).Bytes == SeasonData.FileBytes,
    "the published file's size and SHA-256 are in the build (Earth 2.1.0's seasons-1.bin)");
Directory.Delete(work, true);

// 7. KeyAt: a place's tile
Check(fromBytes.KeyAt(90 - (10 * T + 0.5) / 30.0, -180 + (20 * T + 0.5) / 30.0) == k1, "a place's tile key");

Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
return failures == 0 ? 0 : 1;
