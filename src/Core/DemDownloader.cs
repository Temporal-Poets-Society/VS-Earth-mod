using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// Fetches GLO-90 tiles into the store (decision 0051). Two origins: Earth's data release (the tiles already converted,
    /// 8.6 GB in five stored zips, any one square by an HTTP range request on its part) and Copernicus's own bucket (the
    /// original GeoTIFFs, 71 GB, converted here, only the result kept). On demand a square comes from the release, and from
    /// Copernicus where the release can't supply it. A square already being fetched is waited for, not fetched twice.
    /// "Download all" takes the chosen origin: from the release it streams each part that is mostly missing and fetches
    /// the squares of the others one by one; it can be stopped and started again later, and squares here are skipped.
    /// Every request goes through DataTransport (read timeouts, range reads, whole files resumed and checked).
    /// </summary>
    public sealed class DemDownloader : IDisposable
    {
        /// <summary>Earth's data release (github.com/Temporal-Poets-Society/earth-glo90, tag egl-v1). The tag names the tile
        /// format, not a mod version, so every Earth that reads format 1 uses these files.</summary>
        public const string Glo90Repo = "https://github.com/Temporal-Poets-Society/earth-glo90/releases/download/egl-v1/";
        /// <summary>The public bucket (no account). A local folder (holding the published names) also works, for tests.</summary>
        public const string Glo90Bucket = "https://copernicus-dem-90m.s3.amazonaws.com/";
        /// <summary>The whole source as listed in the bucket on 3 November 2026: 26,475 tiles, 71.1 GB of DEM files.</summary>
        public const long Glo90SourceBytes = 71_100_000_000L;
        /// <summary>Converted size of all of it, measured 4 November 2026 (tools/Glo90Convert over all 26,475 tiles).</summary>
        public const long Glo90StoredBytes = 8_614_000_000L;
        /// <summary>The release's five parts together (5 November 2026): the tiles plus each part's notices.</summary>
        public const long Glo90RepoBytes = 8_617_283_489L;
        public const string ManifestName = "manifest.json";
        /// <summary>The release's manifest, kept beside the tiles once fetched (for tile format 1).</summary>
        public const string ManifestCache = "manifest-egl1.json";

        public enum Origin { Repo, Copernicus }

        public readonly FineElevation Target;
        /// <summary>Copernicus's bucket, or a local folder with the published .tif files; null for none.</summary>
        public string Source;
        /// <summary>Earth's data release, or a local folder with its manifest and parts; null for none.</summary>
        public string Repo;
        public int Retries = 3;
        public Action<string> Log;
        /// <summary>Every request goes through it: body read timeouts, range reads, whole files resumed and checked.</summary>
        readonly DataTransport transport;
        readonly SemaphoreSlim slots;
        readonly ConcurrentDictionary<int, Task<bool>> inFlight = new();

        /// <summary>Bytes received, bytes written, squares converted from Copernicus, squares taken from the release.</summary>
        public long BytesDownloaded, BytesStored, TilesConverted, TilesFetched, TilesFailed;
        public long TilesStored => Interlocked.Read(ref TilesConverted) + Interlocked.Read(ref TilesFetched);

        public DemDownloader(FineElevation target, string source = Glo90Bucket, int parallel = 3, string repo = null)
        {
            Target = target;
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo.Trim();
            slots = new SemaphoreSlim(Math.Clamp(parallel, 1, 8));
            transport = new DataTransport(TimeSpan.FromMinutes(3));
        }

        /// <summary>How long a read of any body may wait for data (the transport's; tests shorten it).</summary>
        public TimeSpan ReadTimeout { get => transport.ReadTimeout; set => transport.ReadTimeout = value; }

        bool LocalSource => !DataTransport.IsUrl(Source);
        string RepoPath(string name) => DataTransport.Locate(Repo, name);

        /// <summary>Blocking fetch for chunk generation: true when the tile is on this machine afterwards.</summary>
        public bool FetchBlocking(int latS, int lonW) => FetchAsync(latS, lonW).GetAwaiter().GetResult();

        /// <summary>
        /// A fetch's result for chunk generation, waiting at most <paramref name="budget"/> of its download (Timeout.InfiniteTimeSpan:
        /// as long as it takes): false when it failed or is still running then. Each try is bounded only per read, so a server that
        /// accepts and stalls held the generation thread, which also loads saved chunks, for half an hour a square (CR-D2).
        /// The budget runs from when the square's download first gets a slot (or from this call, if it had one before): time
        /// queued behind other squares' downloads isn't the square's own, and on a slow link a square could run out without
        /// having started. Queued or not, the wait ends <see cref="BackstopBudgets"/> budgets after the call, or after
        /// <paramref name="since"/> (Environment.TickCount64) when given: a caller waiting for several squares in turn passes
        /// one start for all, so that they share one backstop instead of each adding its own.
        /// A fetch that runs out goes on in the background; the caller treats the square as a failed fetch for this session.
        /// </summary>
        public bool AwaitForGeneration(int latS, int lonW, Task<bool> fetch, TimeSpan budget, long since = 0)
        {
            if (budget == Timeout.InfiniteTimeSpan || fetch.IsCompleted) return fetch.GetAwaiter().GetResult();
            int key = FineElevation.Key(latS, lonW);
            long called = Environment.TickCount64, budgetMs = (long)budget.TotalMilliseconds;
            long from = since > 0 ? since : called, backstop = from + budgetMs * BackstopBudgets;
            while (!fetch.IsCompleted)
            {
                long now = Environment.TickCount64;
                bool began = started.TryGetValue(key, out long at);
                long ownEnd = began ? Math.Max(called, at) + budgetMs : long.MaxValue, deadline = Math.Min(ownEnd, backstop);
                if (now >= deadline)
                {
                    string square = DemTiles.ShortName(latS, lonW);
                    Log?.Invoke(now >= ownEnd
                        ? square + $": not here {budget.TotalSeconds:0} s after its download began; chunk generation goes on without it (the download goes on)"
                        : square + $": not here after {(now - from) / 1000.0:0} s of waiting in all (the limit of {BackstopBudgets} times {budget.TotalSeconds:0} s), " +
                          (began ? $"its download running for {(now - at) / 1000.0:0} s" : "its download still waiting for a slot behind other squares") +
                          "; chunk generation goes on without it (the download goes on)");
                    return false;
                }
                // until the download has a slot, look again each second (nothing signals the moment it gets one)
                long wait = began ? deadline - now : Math.Min(deadline - now, 1000);
                Task.WhenAny(fetch, Task.Delay(TimeSpan.FromMilliseconds(wait))).GetAwaiter().GetResult();
            }
            return fetch.GetAwaiter().GetResult();
        }

        /// <summary>
        /// The longest chunk generation waits for a square in all, queued or downloading, in budgets from the call (CR-D2): with
        /// the running game's two minutes, ten. A queue that has brought no slot to the square in that time is stalled, not slow
        /// (a try ahead of it gives up within the transport's 3-minute wait for headers or 1-minute wait for data, and there are
        /// several slots), and the generation thread, which also loads saved chunks, mustn't be held without bound by it.
        /// </summary>
        public const int BackstopBudgets = 5;

        /// <summary>When each square being fetched first got a download slot (Environment.TickCount64), for AwaitForGeneration.
        /// Set by the first try that gets one, removed when the fetch ends.</summary>
        readonly ConcurrentDictionary<int, long> started = new();

        /// <summary>
        /// One whole file of the release (the lake and river outlines and the dam file, 0053 items 17-19) into
        /// <paramref name="target"/>, blocking: resumed from its .part, checked against its size and SHA-256 before it is moved
        /// into place, up to DataTransport.FileTries tries; false (with the last reason) when it can't be had.
        /// <paramref name="lacking"/>: whether that is for good (review A3): no release is set, or it answered that it doesn't
        /// have the file; not after a network error, a download that didn't check out, or another server's turn that ran out.
        /// </summary>
        public bool FetchFile(string name, string target, long bytes, string sha256, out string error, out bool lacking)
        {
            error = null;
            lacking = false;
            if (Repo == null) { error = "no data release set"; lacking = true; return false; }
            bool ok, notThere; string why;
            try
            {
                (ok, why, notThere) = Task.Run(async () =>
                {
                    // servers sharing the folder take turns (review A4); the one that waited may find the file fetched
                    using var turn = await DataTransport.TakeTurnAsync(Path.GetDirectoryName(Path.GetFullPath(target)), Log).ConfigureAwait(false);
                    if (DataTransport.FileMatches(target, bytes, sha256)) return (true, null, false);
                    return await transport.FileAsync(Repo, name, target, bytes, sha256, DataTransport.FileTries, Log,
                        n => Interlocked.Add(ref BytesDownloaded, n)).ConfigureAwait(false);
                }).GetAwaiter().GetResult();
            }
            // a source that isn't a usable URL or path (ElevationRepo set by hand): the caller's "can't be had" path, but not for
            // good: a setting can be corrected
            catch (Exception e) when (e is UriFormatException or ArgumentException or NotSupportedException) { ok = false; why = "ElevationRepo isn't a usable URL or folder: " + e.Message; notThere = false; }
            catch (IOException e) { ok = false; why = e.Message; notThere = false; }   // another server's turn that ran out
            if (ok) Log?.Invoke($"{name}: {bytes / 1e6:F1} MB from {Repo}");
            else { error = why; lacking = notThere; }
            return ok;
        }

        /// <summary>The squares whose last fetch found that no source has them (review A3), as FineElevation.Key.</summary>
        readonly ConcurrentDictionary<int, byte> lacking = new();

        /// <summary>
        /// Whether the square's last fetch ended with the sources' answer that they don't have it, rather than a failure that
        /// may pass (review A3): the release's manifest doesn't list it (or no release is set) and Copernicus answered 404 (or
        /// none is set). Only then may a world keep the square on the built-in heights for good.
        /// </summary>
        public bool Lacks(int latS, int lonW) => lacking.ContainsKey(FineElevation.Key(latS, lonW));

        /// <summary>
        /// The square's tile on this machine: at once if it is here, false where the source has none. Once started a fetch
        /// isn't cancelled (generation may be waiting for it). <paramref name="copernicus"/>: from Copernicus only.
        /// </summary>
        public Task<bool> FetchAsync(int latS, int lonW, bool copernicus = false)
        {
            if (!Target.SourceHas(latS, lonW)) return Task.FromResult(false);
            if (Target.IsHere(latS, lonW)) return Task.FromResult(true);
            int key = FineElevation.Key(latS, lonW);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = inFlight.GetOrAdd(key, tcs.Task);
            if (running != tcs.Task) return running;
            Task.Run(async () =>
            {
                bool ok = false;
                try { ok = await Fetch(latS, lonW, copernicus).ConfigureAwait(false); }
                catch (Exception e) { Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": " + e.Message); }
                finally
                {
                    // before inFlight: a later fetch of the square, possible only once this one is out of inFlight, keeps its own start
                    started.TryRemove(key, out _);
                    inFlight.TryRemove(key, out _);
                    if (!ok) Interlocked.Increment(ref TilesFailed);
                    tcs.SetResult(ok);
                }
            });
            return tcs.Task;
        }

        async Task<bool> Fetch(int latS, int lonW, bool copernicus)
        {
            int key = FineElevation.Key(latS, lonW);
            // whether each origin answered that it doesn't have the square (an origin that isn't set has none)
            bool releaseLacks = Repo == null, copernicusLacks = Source == null;
            if (!copernicus && Repo != null)
            {
                var m = await GetManifest().ConfigureAwait(false);
                if (m != null && m.Tiles.TryGetValue(key, out var e))
                {
                    if (await FetchFromRepo(latS, lonW, m, e).ConfigureAwait(false)) { lacking.TryRemove(key, out _); return true; }
                    if (Source != null) Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": not from the release; trying Copernicus");
                }
                else if (m != null) releaseLacks = true;
            }
            if (Source != null)
            {
                var (ok, notFound) = await FetchFromCopernicus(latS, lonW).ConfigureAwait(false);
                if (ok) { lacking.TryRemove(key, out _); return true; }
                copernicusLacks = notFound;
            }
            if (releaseLacks && copernicusLacks) lacking[key] = 0;
            else lacking.TryRemove(key, out _);
            return false;
        }

        /// <summary>The pause before try n + 1 of a square.</summary>
        static TimeSpan Pause(int attempt) => TimeSpan.FromMilliseconds(2000 * attempt * attempt);

        /// <summary>A square from Copernicus, converted here; NotFound: the bucket (or folder) answered that it doesn't have it.</summary>
        async Task<(bool Ok, bool NotFound)> FetchFromCopernicus(int latS, int lonW)
        {
            string name = DemTiles.SourceName(latS, lonW);
            string url = LocalSource ? Path.Combine(Source, name + ".tif") : Source.TrimEnd('/') + "/" + name + "/" + name + ".tif";
            bool notFound = false;
            bool ok = await DataTransport.RetryAsync(Retries, async attempt =>
            {
                await slots.WaitAsync().ConfigureAwait(false);
                started.TryAdd(FineElevation.Key(latS, lonW), Environment.TickCount64);   // CR-D2: the generation wait counts from here
                try
                {
                    var (found, data, etag) = await transport.GetAsync(url).ConfigureAwait(false);
                    if (!found) { notFound = true; Log?.Invoke(name + (LocalSource ? ": not in " + Source : ": not in the bucket (404)")); return false; }
                    Interlocked.Add(ref BytesDownloaded, data.Length);
                    var tile = DemTiles.FromGrid(DemTiles.ReadGeoTiff(data), latS, lonW);
                    tile.SourceBytes = data.Length;
                    tile.SourceETag = etag;
                    byte[] file = DemTiles.Encode(tile);
                    // read back before keeping it: a converter fault must not become a world's terrain
                    var back = DemTiles.Decode(file);
                    if (back.Cols != tile.Cols || back.Rows != tile.Rows) throw new InvalidDataException("tile did not read back");
                    Write(latS, lonW, file);
                    Interlocked.Increment(ref TilesConverted);
                    return true;
                }
                finally { slots.Release(); }
            }, Pause, (attempt, e) => Log?.Invoke($"{name}: attempt {attempt} of {Retries} failed: {e.Message}")).ConfigureAwait(false);
            return (ok, !ok && notFound);
        }

        /// <summary>A square from the release: its bytes by a range request on its part (with the transport's read timeout, so
        /// a stalled body can't hold the generation thread waiting for it), kept when they are the manifest's.</summary>
        Task<bool> FetchFromRepo(int latS, int lonW, Manifest m, Entry e)
        {
            string name = DemTiles.ShortName(latS, lonW);
            return DataTransport.RetryAsync(Retries, async attempt =>
            {
                await slots.WaitAsync().ConfigureAwait(false);
                started.TryAdd(FineElevation.Key(latS, lonW), Environment.TickCount64);   // CR-D2: the generation wait counts from here
                try
                {
                    byte[] data = await transport.RangeAsync(Repo, m.Parts[e.Part - 1], e.Offset, e.Length).ConfigureAwait(false);
                    Interlocked.Add(ref BytesDownloaded, data.Length);
                    Keep(latS, lonW, e, data);
                    Interlocked.Increment(ref TilesFetched);
                    return true;
                }
                finally { slots.Release(); }
            }, Pause, (attempt, x) => Log?.Invoke($"{name}: attempt {attempt} of {Retries} from the release failed: {x.Message}"));
        }

        /// <summary>A square from the release, kept only when it is the manifest's file for that square.</summary>
        void Keep(int latS, int lonW, Entry e, byte[] data)
        {
            if (data.Length != e.Length || !SHA256.HashData(data).AsSpan().SequenceEqual(e.Sha))
                throw new InvalidDataException("differs from the release's manifest");
            if (data.Length < 12 || data[0] != (byte)'E' || data[1] != (byte)'G' || data[2] != (byte)'L' || data[3] != (byte)'O'
                || data[4] != DemTiles.FormatVersion || BitConverter.ToInt16(data, 8) != latS || BitConverter.ToInt16(data, 10) != lonW)
                throw new InvalidDataException("not this square's tile");
            Write(latS, lonW, data);
        }

        void Write(int latS, int lonW, byte[] file)
        {
            Directory.CreateDirectory(Target.Folder);
            string path = Target.PathOf(latS, lonW), tmp = UniquePart(path);
            try
            {
                File.WriteAllBytes(tmp, file);
                File.Move(tmp, path, true);
            }
            finally { TryDelete(tmp); }
            Interlocked.Add(ref BytesStored, file.Length);
            Target.TileArrived(latS, lonW);
        }

        /// <summary>A temporary name beside <paramref name="path"/> no other writer uses (review A4: two servers on one store fetching
        /// the same square wrote one name.part and broke each other's write). The whole file then replaces the target at once;
        /// two writers of the same square write the same checked bytes.</summary>
        static string UniquePart(string path) => path + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".part";

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // ------------------------------------------------------------------------------------------ the release's manifest

        readonly struct Entry
        {
            public readonly int Part, Length;
            public readonly long Offset;
            public readonly byte[] Sha;
            public Entry(int part, long offset, int length, byte[] sha) { Part = part; Offset = offset; Length = length; Sha = sha; }
        }

        sealed class Manifest
        {
            public string[] Parts;
            public long[] PartBytes;
            public readonly Dictionary<int, Entry> Tiles = new();
        }

        readonly object manifestGate = new();
        Task<Manifest> manifest;
        DateTime manifestFailedAt;

        /// <summary>The manifest: from the store folder, else from the release (kept then); null when it can't be had (tried
        /// again a minute later).</summary>
        Task<Manifest> GetManifest()
        {
            lock (manifestGate)
            {
                if (manifest == null || manifest.IsCompleted && manifest.Result == null && (DateTime.UtcNow - manifestFailedAt).TotalSeconds > 60)
                    manifest = Task.Run(LoadManifest);
                return manifest;
            }
        }

        async Task<Manifest> LoadManifest()
        {
            string cache = Path.Combine(Target.Folder, ManifestCache);
            // the release the copy came from, beside it: a copy is used only for that release, so that a mirror packed
            // differently (ElevationRepo changed) doesn't fail every tile's hash against the old parts (CR-D8). A copy kept
            // before this record is the default release's.
            string from = cache + ".repo";
            try
            {
                if (File.Exists(cache) && (File.Exists(from) ? File.ReadAllText(from).Trim() == (Repo ?? "") : Repo == Glo90Repo))
                    return ParseManifest(await File.ReadAllBytesAsync(cache).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                Log?.Invoke("the release's manifest here is unreadable; fetching it again: " + e.Message);
            }
            try
            {
                var (found, data, _) = await transport.GetAsync(RepoPath(ManifestName)).ConfigureAwait(false);
                if (!found) throw new FileNotFoundException(ManifestName + " is not there");
                var m = ParseManifest(data);
                Directory.CreateDirectory(Target.Folder);
                string tmp = UniquePart(cache);
                try
                {
                    await File.WriteAllBytesAsync(tmp, data).ConfigureAwait(false);
                    File.Move(tmp, cache, true);
                    await File.WriteAllTextAsync(from, Repo ?? "").ConfigureAwait(false);
                }
                finally { TryDelete(tmp); }
                Log?.Invoke($"the release's manifest: {m.Tiles.Count} squares in {m.Parts.Length} parts, from {Repo}");
                return m;
            }
            catch (Exception e)
            {
                manifestFailedAt = DateTime.UtcNow;
                Log?.Invoke("the release's manifest could not be had from " + Repo + ": " + e.Message);
                return null;
            }
        }

        static Manifest ParseManifest(byte[] json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("format").GetString() != "egl" || root.GetProperty("formatVersion").GetInt32() != DemTiles.FormatVersion)
                throw new InvalidDataException("a manifest for another tile format");
            var parts = root.GetProperty("parts");
            var m = new Manifest { Parts = new string[parts.GetArrayLength()], PartBytes = new long[parts.GetArrayLength()] };
            int i = 0;
            foreach (var p in parts.EnumerateArray())
            {
                string name = p.GetProperty("name").GetString();
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains("..")) throw new InvalidDataException("part name " + name);
                m.Parts[i] = name;
                m.PartBytes[i++] = p.GetProperty("bytes").GetInt64();
            }
            foreach (var t in root.GetProperty("tiles").EnumerateObject())
            {
                if (!TryParseShortName(t.Name, out int latS, out int lonW)) throw new InvalidDataException("square " + t.Name);
                var a = t.Value;
                int part = a[0].GetInt32(), len = a[2].GetInt32();
                long off = a[1].GetInt64();
                byte[] sha = Convert.FromHexString(a[3].GetString());
                if (part < 1 || part > m.Parts.Length || off < 0 || len <= 0 || off + len > m.PartBytes[part - 1] || sha.Length != 32)
                    throw new InvalidDataException("entry " + t.Name);
                m.Tiles[FineElevation.Key(latS, lonW)] = new Entry(part, off, len, sha);
            }
            if (m.Tiles.Count == 0) throw new InvalidDataException("no squares");
            return m;
        }

        /// <summary>N60E006, S01W075 (DemTiles.ShortName) back to the square.</summary>
        public static bool TryParseShortName(string s, out int latS, out int lonW)
        {
            latS = lonW = 0;
            if (s == null || s.Length != 7 || !int.TryParse(s.AsSpan(1, 2), out latS) || !int.TryParse(s.AsSpan(4, 3), out lonW)) return false;
            if (s[0] == 'S') latS = -latS; else if (s[0] != 'N') return false;
            if (s[3] == 'W') lonW = -lonW; else if (s[3] != 'E') return false;
            return latS >= -90 && latS < 90 && lonW >= -180 && lonW < 180;
        }

        // ------------------------------------------------------------------------------------------ download all

        public sealed class Batch
        {
            public int Total, Done, Skipped, Failed;
            public long DownloadedAtStart, StoredAtStart, Expected;
            public Origin From;
            public DateTime Started = DateTime.UtcNow, Ended;
            public bool Running, Cancelled;
            public string Error;
            internal CancellationTokenSource Cancel = new();
        }

        public Batch All { get; private set; }

        /// <summary>Starts fetching every square the source has that isn't here yet; false when one is already running.</summary>
        public bool StartAll(int workers = 3, Origin from = Origin.Repo)
        {
            var current = All;
            if (current != null && current.Running) return false;
            if (from == Origin.Repo && Repo == null || from == Origin.Copernicus && Source == null) return false;
            var squares = new List<(int, int)>();
            int skipped = 0;
            foreach (var sq in Target.SourceSquares())
            {
                if (Target.IsHere(sq.latS, sq.lonW)) skipped++;
                else squares.Add(sq);
            }
            var b = new Batch
            {
                Total = squares.Count + skipped, Skipped = skipped, Running = true, From = from,
                DownloadedAtStart = Interlocked.Read(ref BytesDownloaded), StoredAtStart = Interlocked.Read(ref BytesStored),
                Expected = from == Origin.Copernicus ? (long)(Glo90SourceBytes * (squares.Count / (double)Math.Max(1, squares.Count + skipped))) : 0,
            };
            All = b;
            workers = Math.Clamp(workers, 1, 8);
            Task.Run(async () =>
            {
                try
                {
                    if (from == Origin.Repo) await AllFromRepo(squares, b, workers).ConfigureAwait(false);
                    else await EachSquare(squares, b, workers, true).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (!b.Cancel.IsCancellationRequested)
                    {
                        b.Error = e.Message;
                        Log?.Invoke("download all: " + e);
                    }
                }
                finally
                {
                    b.Ended = DateTime.UtcNow;
                    b.Cancelled = b.Cancel.IsCancellationRequested;
                    b.Running = false;
                    Log?.Invoke($"download all from {(from == Origin.Repo ? "the release" : "Copernicus")} {(b.Cancelled ? "stopped" : b.Error != null ? "failed" : "finished")}: " +
                        $"{b.Done} fetched, {b.Failed} failed, {b.Skipped} already here; {(Interlocked.Read(ref BytesDownloaded) - b.DownloadedAtStart) / 1e9:F2} GB downloaded");
                }
            });
            return true;
        }

        public void CancelAll() => All?.Cancel.Cancel();

        async Task EachSquare(List<(int latS, int lonW)> squares, Batch b, int workers, bool copernicus)
        {
            int next = -1;
            var tasks = new List<Task>();
            for (int w = 0; w < workers; w++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (!b.Cancel.IsCancellationRequested)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= squares.Count) break;
                        var (latS, lonW) = squares[i];
                        bool ok = await FetchAsync(latS, lonW, copernicus).ConfigureAwait(false);
                        if (ok) Interlocked.Increment(ref b.Done); else Interlocked.Increment(ref b.Failed);
                    }
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>A part with more than this share of its bytes missing is streamed whole, otherwise its squares one by one.</summary>
        const double StreamShare = 0.25;

        async Task AllFromRepo(List<(int latS, int lonW)> squares, Batch b, int workers)
        {
            var m = await GetManifest().ConfigureAwait(false);
            if (m == null)
            {
                b.Error = "the release can't be reached (" + Repo + ")";
                return;
            }
            var byPart = new List<(int latS, int lonW, Entry e)>[m.Parts.Length];
            for (int p = 0; p < byPart.Length; p++) byPart[p] = new List<(int, int, Entry)>();
            var single = new List<(int, int)>();
            foreach (var (latS, lonW) in squares)
            {
                if (m.Tiles.TryGetValue(FineElevation.Key(latS, lonW), out var e)) byPart[e.Part - 1].Add((latS, lonW, e));
                else single.Add((latS, lonW));
            }
            var streamed = new List<int>();
            long expected = 0;
            for (int p = 0; p < byPart.Length; p++)
            {
                if (byPart[p].Count == 0) continue;
                byPart[p].Sort((x, y) => x.e.Offset.CompareTo(y.e.Offset));
                long missing = byPart[p].Sum(t => (long)t.e.Length);
                if (missing > StreamShare * m.PartBytes[p])
                {
                    streamed.Add(p);
                    var last = byPart[p][^1].e;
                    expected += last.Offset + last.Length;
                }
                else
                {
                    single.AddRange(byPart[p].Select(t => (t.latS, t.lonW)));
                    expected += missing;
                }
            }
            b.Expected = expected;
            var leftovers = new ConcurrentQueue<(int, int)>();
            var gate = new SemaphoreSlim(Math.Min(workers, 5));
            await Task.WhenAll(streamed.Select(p => Task.Run(async () =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { await StreamPart(m, p + 1, byPart[p], b, leftovers).ConfigureAwait(false); }
                finally { gate.Release(); }
            }))).ConfigureAwait(false);
            if (b.Cancel.IsCancellationRequested) return;
            // squares the stream didn't keep, and those of parts mostly here: one by one (from Copernicus where the release fails)
            single.AddRange(leftovers);
            await EachSquare(single, b, workers, false).ConfigureAwait(false);
        }

        /// <summary>A part read front to back once, its wanted squares kept as they pass; a broken connection resumes where it
        /// broke. Squares that can't be kept go to <paramref name="leftovers"/>.</summary>
        async Task StreamPart(Manifest m, int part, List<(int latS, int lonW, Entry e)> tiles, Batch b, ConcurrentQueue<(int, int)> leftovers)
        {
            string name = m.Parts[part - 1];
            var token = b.Cancel.Token;
            var buffer = new byte[1 << 20];
            int next = 0, attempt = 0;
            long pos = 0;
            while (next < tiles.Count && !token.IsCancellationRequested)
            {
                try
                {
                    using var opened = await transport.OpenAsync(Repo, name, pos, token).ConfigureAwait(false);
                    // a source that sent the whole part instead of the rest of it: not read (resumed again, or given up)
                    if (opened.From != pos) throw new HttpRequestException($"{name}: the whole part instead of its bytes from {pos:N0}");
                    var stream = opened.Stream;
                    for (; next < tiles.Count; next++)
                    {
                        if (token.IsCancellationRequested) return;
                        var (latS, lonW, e) = tiles[next];
                        while (pos < e.Offset)
                        {
                            int n = await transport.ReadAsync(stream, buffer.AsMemory(0, (int)Math.Min(buffer.Length, e.Offset - pos)), token).ConfigureAwait(false);
                            if (n == 0) throw new EndOfStreamException(name + " ended early");
                            pos += n;
                            Interlocked.Add(ref BytesDownloaded, n);
                        }
                        var data = new byte[e.Length];
                        await transport.ReadExactlyAsync(stream, data, name, token).ConfigureAwait(false);
                        pos += data.Length;
                        Interlocked.Add(ref BytesDownloaded, data.Length);
                        attempt = 0;
                        if (Target.IsHere(latS, lonW)) { Interlocked.Increment(ref b.Done); continue; }
                        // the square is being fetched on its own (generation): it is counted when that ends
                        int key = FineElevation.Key(latS, lonW);
                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (inFlight.GetOrAdd(key, tcs.Task) != tcs.Task) { leftovers.Enqueue((latS, lonW)); continue; }
                        bool ok = false;
                        try
                        {
                            Keep(latS, lonW, e, data);
                            Interlocked.Increment(ref TilesFetched);
                            ok = true;
                        }
                        catch (Exception x) when (x is IOException or InvalidDataException or UnauthorizedAccessException)
                        {
                            Log?.Invoke(DemTiles.ShortName(latS, lonW) + " in " + name + ": " + x.Message);
                        }
                        finally
                        {
                            inFlight.TryRemove(key, out _);
                            tcs.SetResult(ok);
                        }
                        if (ok) Interlocked.Increment(ref b.Done); else leftovers.Enqueue((latS, lonW));
                    }
                    return;
                }
                catch (Exception x) when (!token.IsCancellationRequested && x is HttpRequestException or IOException or OperationCanceledException)
                {
                    if (++attempt > Retries)
                    {
                        Log?.Invoke($"{name}: gave up at byte {pos:N0} ({x.Message}); its other squares come one by one");
                        for (; next < tiles.Count; next++) leftovers.Enqueue((tiles[next].latS, tiles[next].lonW));
                        return;
                    }
                    Log?.Invoke($"{name}: broke at byte {pos:N0} ({x.Message}); resuming, attempt {attempt} of {Retries}");
                    await Task.Delay(2000 * attempt * attempt, token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>How many squares the source has, and how many of them are here (counts files, so a few ms).</summary>
        public (int source, int here, long bytesHere) Census()
        {
            int source = 0, here = 0;
            long bytes = 0;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Target.Folder))
                foreach (var f in new DirectoryInfo(Target.Folder).EnumerateFiles("*" + FineElevation.FileExtension))
                {
                    present.Add(Path.GetFileNameWithoutExtension(f.Name));
                    bytes += f.Length;
                }
            foreach (var (latS, lonW) in Target.SourceSquares())
            {
                source++;
                if (present.Contains(DemTiles.ShortName(latS, lonW))) here++;
            }
            return (source, here, bytes);
        }

        public void Dispose()
        {
            CancelAll();
            transport.Dispose();
        }
    }
}
