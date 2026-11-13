// The 30" ground sets' downloader (Core.GroundDownloader): local folders as the source; the transport under it and every
// other Earth download (Core.DataTransport) over HTTP on a local server that misbehaves on request; then
// (GROUND_DOWNLOAD_ONLINE=1) the real release egts-v1 on GitHub, including a resumed download.
//   dotnet run -c Release --project tests/GroundDownloadTests
using System.Security.Cryptography;
using EarthMap.Core;

int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failures++; }

string root = Path.Combine(Path.GetTempPath(), "earth-ground-download-test-" + Guid.NewGuid().ToString("N")[..8]);
string source = Path.Combine(root, "source"), data = Path.Combine(root, "data");
Directory.CreateDirectory(source);
var rng = new Random(7);
GroundDownloader.Set Make(string name, int bytes)
{
    var b = new byte[bytes];
    rng.NextBytes(b);
    File.WriteAllBytes(Path.Combine(source, name), b);
    return new GroundDownloader.Set(name, bytes, Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant());
}
var sets = new[] { Make("a-30s.egts", 3_000_000), Make("b-30s.egts", 1_234_567), Make("c-30s.egts", 10) };
var log = new List<string>();
bool Ensure(string src, IEnumerable<GroundDownloader.Set> s = null) => GroundDownloader.Ensure(data, src, m => log.Add(m), default, s ?? sets);
bool NoParts() => !Directory.Exists(Path.Combine(data, "ground")) || Directory.GetFiles(Path.Combine(data, "ground"), "*.part").Length == 0;

// shipped table
Check(GroundDownloader.Sets.Length == 3 && GroundDownloader.TotalBytes() == 172_251_299, "release egts-v1: three sets, 172,251,299 bytes");
Check(GroundDownloader.Sets.All(s => s.Sha256.Length == 64), "every shipped set has a SHA-256");

// nothing here, no source
Check(GroundDownloader.Missing(data, false, sets).Count == 3, "all three missing in an empty folder");
Check(!Ensure(""), "no source: not fetched");

// a local folder as the source
Check(Ensure(source), "fetched from a local folder");
Check(GroundDownloader.Missing(data, true, sets).Count == 0, "all three present and checked");
Check(NoParts(), "no .part files left");
Check(File.ReadAllBytes(GroundDownloader.PathOf(data, sets[1])).SequenceEqual(File.ReadAllBytes(Path.Combine(source, sets[1].Name))), "copied bytes are the source's");
log.Clear();
Check(Ensure(source) && log.Count == 0, "already here: nothing fetched");

// a file of the right size but other bytes: found by the hash, replaced
var bad = File.ReadAllBytes(GroundDownloader.PathOf(data, sets[0]));
bad[12345] ^= 0xFF;
File.WriteAllBytes(GroundDownloader.PathOf(data, sets[0]), bad);
Check(GroundDownloader.Missing(data, false, sets).Count == 0, "size check alone passes a changed file");
Check(GroundDownloader.Missing(data, true, sets).Select(s => s.Name).SequenceEqual(new[] { sets[0].Name }), "the hash finds the changed file");
Check(Ensure(source) && GroundDownloader.Missing(data, true, sets).Count == 0, "the changed file is fetched again");

// resume: half a .part is completed, not restarted
File.Delete(GroundDownloader.PathOf(data, sets[0]));
var whole = File.ReadAllBytes(Path.Combine(source, sets[0].Name));
File.WriteAllBytes(GroundDownloader.PathOf(data, sets[0]) + ".part", whole[..1_500_000]);
Check(Ensure(source) && GroundDownloader.Missing(data, true, sets).Count == 0 && NoParts(), "a broken download resumes and checks out");

// a source whose bytes don't match the release: discarded, nothing put in place
var wrong = new GroundDownloader.Set("c-30s.egts", 10, new string('0', 64));
File.Delete(GroundDownloader.PathOf(data, sets[2]));
log.Clear();
Check(!Ensure(source, new[] { wrong }), "wrong bytes: not accepted");
Check(!File.Exists(GroundDownloader.PathOf(data, wrong)) && NoParts(), "wrong bytes: no file and no .part left");
Check(log.Any(l => l.Contains("SHA-256")), "wrong bytes: logged");

// a source that lacks a file
File.Delete(Path.Combine(source, sets[2].Name));
Check(!Ensure(source), "missing in the source: not fetched");
Check(!File.Exists(GroundDownloader.PathOf(data, sets[2])), "missing in the source: nothing put in place");

// ------------------------------------------------------------------ review A4/A9: servers sharing ModData/earth take turns
// (a single-player game and a dedicated server on one machine share VintagestoryData); the turn's lock file stays, and a
// wait that runs out says what happened
{
    string shared = Path.Combine(root, "shared"), sharedGround = Path.Combine(shared, GroundDownloader.Subfolder);
    var two = sets.Take(2).ToArray();
    var held = await DataTransport.TakeTurnAsync(sharedGround, null);
    var waitLog = new List<string>();
    var second = Task.Run(() => GroundDownloader.Ensure(shared, source, m => { lock (waitLog) waitLog.Add(m); }, default, two));
    await Task.Delay(2500);
    bool waited = !second.IsCompleted && !Directory.EnumerateFiles(sharedGround, "*.part").Any();
    // the first server puts the sets in place and gives the turn back
    foreach (var s in two) File.Copy(Path.Combine(source, s.Name), GroundDownloader.PathOf(shared, s));
    held.Dispose();
    bool got = second.Wait(TimeSpan.FromSeconds(30)) && second.Result;
    Check(waited && got && waitLog.Any(l => l.Contains("another server is fetching")) && !waitLog.Any(l => l.StartsWith("fetching")),
        "A4: a second server waits for the first's turn, then finds the sets in place and fetches nothing (" + string.Join(" | ", waitLog) + ")");
    Check(File.Exists(Path.Combine(sharedGround, DataTransport.TurnFile)), "A9: the turn's lock file stays in the folder (no delete on close)");
    using (await DataTransport.TakeTurnAsync(sharedGround, null))
    {
        string why = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { using var _ = await DataTransport.TakeTurnAsync(sharedGround, null, default, TimeSpan.FromSeconds(1.5)); }
        catch (IOException e) { why = e.Message; }
        Check(why != null && why.Contains("another Earth server has been fetching into") && why.Contains("still holds") && sw.Elapsed.TotalSeconds < 10,
            "A9: a wait that runs out says another server holds the turn, not the raw sharing error (" + why + ")");
    }
    // a lock file an older Earth deleted on close, still being deleted while something (a virus scanner) holds it open:
    // Windows refuses to open it ("access denied") until then; the turn waits instead of failing
    string pendingFolder = Path.Combine(root, "pending"), pending = Path.Combine(pendingFolder, DataTransport.TurnFile);
    Directory.CreateDirectory(pendingFolder);
    var old = new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
    var scanner = new FileStream(pending, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    old.Dispose();
    string raw = "opens";
    try { new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1).Dispose(); }
    catch (Exception e) { raw = e.GetType().Name; }
    _ = Task.Delay(1500).ContinueWith(_ => scanner.Dispose());
    bool turned;
    try { using var t = await DataTransport.TakeTurnAsync(pendingFolder, null, default, TimeSpan.FromSeconds(15)); turned = true; }
    catch (Exception) { turned = false; }
    Check(turned, $"A9: a lock file pending deletion is waited for (a plain open there: {raw})");
}

// review A9: why the store's files could not be had, and a full disk named as such
{
    string store = Path.Combine(root, "store");
    var lakes = new EarthDataStore.File("lakes-test.bin", 10, new string('0', 64));
    Check(!EarthDataStore.Ensure(store, null, "", null, out string none, default, new[] { lakes }) && none.Contains("no data source is set"), "A9: no source: says so (" + none + ")");
    Check(!EarthDataStore.Ensure(store, null, source, null, out string lacking, default, new[] { lakes }) && lacking.StartsWith("lakes-test.bin: "), "A9: a source without the file: names it (" + lacking + ")");
    Check(DataTransport.DiskShortfall(store, long.MaxValue / 4) is string full && full.Contains("MB free") && DataTransport.DiskShortfall(store, 1) == null,
        "A9: a fetch that can't fit names the disk");
}

// ------------------------------------------------------------------ the transport over HTTP (DataTransport), on a local
// server that misbehaves on request: every Earth download goes through it (GLO-90 squares and parts, the release files,
// these sets)
string web = Path.Combine(root, "web"), into = Path.Combine(root, "into");
Directory.CreateDirectory(web);
var big = new byte[2_500_000];
rng.NextBytes(big);
File.WriteAllBytes(Path.Combine(web, "big.bin"), big);
string bigSha = Convert.ToHexString(SHA256.HashData(big)).ToLowerInvariant();
string Target(string n) => Path.Combine(into, n);
using (var server = new TestServer(web))
using (var transport = new DataTransport(TimeSpan.FromSeconds(30)) { ReadTimeout = TimeSpan.FromSeconds(1) })
{
    var tlog = new List<string>();
    async Task<(bool Ok, string Error, bool NotThere)> Fetch(string name, int tries = 4) => await transport.FileAsync(server.Url, name, Target(name), big.Length, bigSha, tries, tlog.Add);
    bool Whole(string name) => File.Exists(Target(name)) && File.ReadAllBytes(Target(name)).AsSpan().SequenceEqual(big) && !File.Exists(Target(name) + ".part");

    Check((await Fetch("big.bin")).Ok && Whole("big.bin"), "HTTP: a whole file, checked and in place");

    // a broken download's .part resumes with a range request from where it broke
    File.Delete(Target("big.bin"));
    File.WriteAllBytes(Target("big.bin") + ".part", big[..1_000_000]);
    server.Requests.Clear();
    Check((await Fetch("big.bin")).Ok && Whole("big.bin") && server.Requests.Count == 1 && server.Requests[0].From == 1_000_000,
        "HTTP: a .part resumes from its end (" + string.Join(", ", server.Requests.Select(r => r.From)) + ")");

    // a connection cut short: the next try resumes where it broke
    File.Delete(Target("big.bin"));
    server.Requests.Clear();
    server.CutAfter = 700_000;
    server.Plan = (_, n) => n == 0 ? TestServer.Mode.Cut : TestServer.Mode.Normal;
    tlog.Clear();
    var cut = await Fetch("big.bin");
    Check(cut.Ok && Whole("big.bin") && server.Requests.Count == 2 && server.Requests[1].From > 0 && tlog.Any(l => l.Contains("(try 1 of 4)")),
        "HTTP: a cut connection resumes on the next try (from " + string.Join(", ", server.Requests.Select(r => r.From)) + ")");

    // a server that ignores the range sends the whole file: the .part starts again and checks out
    File.Delete(Target("big.bin"));
    File.WriteAllBytes(Target("big.bin") + ".part", big[..500_000]);
    server.Plan = (_, _) => TestServer.Mode.IgnoreRange;
    Check((await Fetch("big.bin")).Ok && Whole("big.bin"), "HTTP: a range ignored starts the file again");

    // a .part with other bytes than the release's: discarded on the SHA-256, fetched again whole
    File.Delete(Target("big.bin"));
    var wrongPart = (byte[])big[..1_200_000].Clone();
    wrongPart[10] ^= 0xFF;
    File.WriteAllBytes(Target("big.bin") + ".part", wrongPart);
    server.Plan = (_, _) => TestServer.Mode.Normal;
    tlog.Clear();
    Check((await Fetch("big.bin")).Ok && Whole("big.bin") && tlog.Any(l => l.Contains("SHA-256")), "HTTP: a .part with other bytes is discarded and fetched again");

    // Codex correctness #5: a body that stalls after its headers gives up after the read timeout instead of holding the
    // thread (a generation thread waits for GLO-90 squares) for ever
    server.Plan = (_, _) => TestServer.Mode.Stall;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    bool timedOut = false;
    try { await transport.RangeAsync(server.Url, "big.bin", 1000, 5000); }
    catch (IOException e) when (e.Message.StartsWith("no data for")) { timedOut = true; }
    Check(timedOut && sw.Elapsed.TotalSeconds < 10, $"HTTP: a range body that stalls times out ({sw.Elapsed.TotalSeconds:F1} s)");
    File.Delete(Target("big.bin"));
    sw.Restart();
    var stalled = await Fetch("big.bin", 2);
    Check(!stalled.Ok && !File.Exists(Target("big.bin")) && sw.Elapsed.TotalSeconds < 15, $"HTTP: a whole file that stalls gives up after its tries ({sw.Elapsed.TotalSeconds:F1} s: {stalled.Error})");

    Check(!stalled.NotThere, "A3: a download that stalls is a failure that may pass, not the source's answer");

    // review A3: the source's answer that it doesn't have a file (HTTP 404, a folder without it) is told from a failure,
    // and not tried again
    server.Plan = (_, _) => TestServer.Mode.Normal;
    server.Requests.Clear();
    var gone = await Fetch("nothing.bin");
    Check(!gone.Ok && gone.NotThere && server.Requests.Count == 1 && gone.Error.Contains("404"), $"A3: a 404 is the source's answer, asked once ({server.Requests.Count} requests: {gone.Error})");
    var gone2 = await transport.FileAsync(web, "nothing.bin", Target("nothing.bin"), 10, new string('0', 64), 4);
    Check(!gone2.Ok && gone2.NotThere, "A3: a local source folder without the file answers the same");
    server.Plan = (_, _) => TestServer.Mode.Cut;
    var cutAll = await Fetch("big.bin", 2);
    Check(!cutAll.Ok && !cutAll.NotThere, "A3: a connection that keeps breaking is a failure that may pass");
    File.Delete(Target("big.bin") + ".part");
    server.Plan = (_, _) => TestServer.Mode.Normal;
    // the ground sets: none set or not in the source decide for good (Lacking); a failure doesn't (Failed)
    string a3 = Path.Combine(root, "a3");
    Check(GroundDownloader.Get(a3, "", null, default, sets.Take(1)) == GroundDownloader.Result.Lacking, "A3: no ground source set: Lacking");
    Check(GroundDownloader.Get(a3, server.Url, null, default, sets.Take(1)) == GroundDownloader.Result.Lacking, "A3: a source without the set (404): Lacking");
    File.Copy(Path.Combine(source, sets[0].Name), Path.Combine(web, sets[0].Name), true);
    var corrupt = new GroundDownloader.Set(sets[0].Name, sets[0].Bytes, new string('0', 64));
    Check(GroundDownloader.Get(a3, server.Url, null, default, new[] { corrupt }) == GroundDownloader.Result.Failed && !File.Exists(GroundDownloader.PathOf(a3, corrupt)),
        "A3: a download that never checks out: Failed, nothing in place");

    // ranges and whole reads
    server.Plan = (_, _) => TestServer.Mode.Normal;
    var range = await transport.RangeAsync(server.Url, "big.bin", 123_456, 7_890);
    Check(range.AsSpan().SequenceEqual(big.AsSpan(123_456, 7_890)), "HTTP: a range read is the file's bytes there");
    server.Plan = (_, _) => TestServer.Mode.IgnoreRange;
    bool refused = false;
    try { await transport.RangeAsync(server.Url, "big.bin", 0, 100); }
    catch (HttpRequestException) { refused = true; }
    Check(refused, "HTTP: a range answered with the whole file is not read");
    server.Plan = (_, _) => TestServer.Mode.Normal;
    var got = await transport.GetAsync(server.Url + "big.bin");
    var none = await transport.GetAsync(server.Url + "nothing.bin");
    Check(got.Found && got.Data.AsSpan().SequenceEqual(big) && !none.Found, "HTTP: a whole read, and a missing file (404) is not found");
    var local = await transport.GetAsync(Path.Combine(web, "nothing.bin"));
    Check(!local.Found && (await transport.RangeAsync(web, "big.bin", 5, 10)).AsSpan().SequenceEqual(big.AsSpan(5, 10)), "a local folder works as a source too");

    // retries: false is an answer (no retry), a transient failure is retried, the caller's cancellation ends it
    int calls = 0;
    Check(!await DataTransport.RetryAsync(3, _ => { calls++; return Task.FromResult(false); }, _ => TimeSpan.Zero, null) && calls == 1, "retry: false ends it at once");
    calls = 0;
    Check(await DataTransport.RetryAsync(3, _ => ++calls < 3 ? throw new IOException("x") : Task.FromResult(true), _ => TimeSpan.Zero, null) && calls == 3, "retry: transient failures are tried again");
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    bool cancelled = false;
    try { await DataTransport.RetryAsync(3, _ => throw new OperationCanceledException(cancel.Token), _ => TimeSpan.Zero, null, cancel.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "retry: the caller's cancellation is not swallowed");

    // the ground sets over HTTP, as a new world fetches them
    string viaHttp = Path.Combine(root, "data-http");
    foreach (var s in sets.Take(2)) File.Copy(GroundDownloader.PathOf(data, s), Path.Combine(web, s.Name), true);
    Check(GroundDownloader.Ensure(viaHttp, server.Url, _ => { }, default, sets.Take(2)) && GroundDownloader.Missing(viaHttp, true, sets.Take(2)).Count == 0,
        "the ground sets from a URL");
}

if (Environment.GetEnvironmentVariable("GROUND_DOWNLOAD_ONLINE") == "1")
{
    // the real release: soil groups resumed over HTTP from a 1 MB .part (GROUND_LOCAL_30S: a folder with the baked files,
    // to cut the .part from), then the other two in full
    string online = Path.Combine(root, "online");
    string local = Environment.GetEnvironmentVariable("GROUND_LOCAL_30S");
    var soil = GroundDownloader.Sets[0];
    if (local != null && File.Exists(Path.Combine(local, soil.Name)))
    {
        Directory.CreateDirectory(Path.Combine(online, "ground"));
        using var fs = File.OpenRead(Path.Combine(local, soil.Name));
        var head = new byte[1_000_000];
        fs.ReadExactly(head);
        File.WriteAllBytes(GroundDownloader.PathOf(online, soil) + ".part", head);
    }
    var t = DateTime.UtcNow;
    var lines = new List<string>();
    bool ok = GroundDownloader.Ensure(online, GroundDownloader.Release, m => { lines.Add(m); Console.WriteLine("  " + m); });
    Check(ok, $"release egts-v1 fetched from GitHub in {(DateTime.UtcNow - t).TotalSeconds:F0} s");
    Check(GroundDownloader.Missing(online, true).Count == 0, "release egts-v1: every set checked");
    if (local != null) Check(!lines.Any(l => l.Contains("try ")), "release egts-v1: the soil groups resumed from 1 MB without errors");
}
else Console.WriteLine("skip the online check (GROUND_DOWNLOAD_ONLINE=1 runs it, 172 MB)");

try { Directory.Delete(root, true); } catch (IOException) { }
Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
return failures == 0 ? 0 : 1;
