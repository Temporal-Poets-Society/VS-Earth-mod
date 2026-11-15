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
    /// EA-0100 (Fable pass FP-05): no tile is fetched or written while its disk has less than <see cref="Reserve"/> free
    /// beyond it, a tile that can't be written into the store (a full disk, no permission) is not fetched again from
    /// another source, and "download all" doesn't start without room for what is missing and stops at the first such tile.
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
        /// <summary>ER-26 (A5, code review 14 November 2026): egl-v1's manifest.json, size and SHA-256 (the data repo's copy and
        /// two stores' fetched copies agree, 14 November 2026). Checked where the source is <see cref="Glo90Repo"/>: every tile
        /// is checked against the manifest, and the manifest now against this (release assets are never replaced). A mirror
        /// set by hand (ElevationRepo) stays unchecked.</summary>
        public const long Glo90ManifestBytes = 2_597_215;
        public const string Glo90ManifestSha256 = "04112a18e2c391e9a27da2300ce3e2d208db5f3f63d34909268a26df109e7e50";

        static bool IsDefaultRelease(string repo) => repo != null && string.Equals(repo.Trim().TrimEnd('/', '\\'), Glo90Repo.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether a manifest may be this source's: anything for a mirror, egl-v1's own bytes for the default release.</summary>
        bool ManifestFits(byte[] data) => !IsDefaultRelease(Repo)
            || data.LongLength == Glo90ManifestBytes && Convert.ToHexString(SHA256.HashData(data)).Equals(Glo90ManifestSha256, StringComparison.OrdinalIgnoreCase);
        /// <summary>The release's manifest, kept beside the tiles once fetched (for tile format 1).</summary>
        public const string ManifestCache = "manifest-egl1.json";

        // EA-0106 (the Fable pass's FP-17): no body or entry is taken larger than these
        /// <summary>The most a mirror's manifest may be (the default release's is pinned at <see cref="Glo90ManifestBytes"/>).</summary>
        public const long MaxMirrorManifestBytes = 16L << 20;
        /// <summary>The most a manifest may give for one tile (egl-v1's largest is 1.18 MB).</summary>
        public const int MaxTileBytes = 8 << 20;
        /// <summary>The most a Copernicus GeoTIFF may be (GLO-90's largest is 6.1 MB).</summary>
        public const long MaxSourceBytes = 64L << 20;
        long ManifestLimit => IsDefaultRelease(Repo) ? Glo90ManifestBytes : MaxMirrorManifestBytes;

        /// <summary>
        /// EA-0106 (Fable's re-review of 255baa7): the release's heights checksum for each square (data/glo90-heights.bin,
        /// embedded in the mod): a conversion from Copernicus and a tile kept here are the release's exactly when their
        /// heights' checksum is the table's, on any runtime. Comparing file bytes did not hold: they depend on the runtime's
        /// Brotli (on .NET 10, the game's runtime, 341 of 602 release tiles encode to other bytes than on .NET 8). Null: no
        /// table; tiles are not judged (as before EA-0106).
        /// </summary>
        public Glo90Heights ReleaseHeights;

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
        /// <summary>EA-0106: tiles here or converted whose heights were not the release's (replaced at the next start, or
        /// not kept).</summary>
        public long TilesDiffering;
        public long TilesStored => Interlocked.Read(ref TilesConverted) + Interlocked.Read(ref TilesFetched);

        /// <summary>EA-0100: the bytes no download takes from the store's disk (1 GB): a world's saves and the game need room.</summary>
        public const long DefaultReserve = 1_000_000_000L;
        /// <summary>(Fable's re-review of 1926d8f) The room a square fetched for chunk generation leaves free: with EA-0099 a
        /// square refused for room leaves its chunks unmade, so a single tile (about 330 kB) is not held to the reserve meant
        /// for "download all".</summary>
        public const long GenerationReserve = 100_000_000L;
        public long Reserve = DefaultReserve;
        /// <summary>EA-0100: free bytes on the store's disk (DataTransport.FreeBytes of the store's folder by default; tests set their own).</summary>
        public Func<long> FreeSpace;
        /// <summary>A converted tile's size on average (the release's 8.6 GB over its 26,475 squares), for a fetch from Copernicus
        /// before its size is known.</summary>
        public const long AverageTileBytes = Glo90StoredBytes / 26475;

        /// <summary>
        /// EA-0100: a tile could not be written into the store (a full disk, no permission, a store under a file), or the disk has
        /// no room for it. Not a network failure: it isn't tried again, nor from the other source, and it stops "download all".
        /// </summary>
        public sealed class StoreException : Exception
        {
            public StoreException(string message, Exception inner = null) : base(message, inner) { }
        }

        /// <summary>EA-0100: why the store last refused a tile (null before any): "download all" reports it.</summary>
        public string StoreError => Volatile.Read(ref storeError);
        string storeError;
        long storeFailures;

        // the last free-space reading and the bytes stored since (a reading costs a walk of the drives: not one per tile)
        readonly object roomGate = new();
        long roomFree = -1, roomStoredAt, roomAt;

        /// <summary>
        /// Throws <see cref="StoreException"/> unless the store's disk has <paramref name="bytes"/> free beyond <see cref="Reserve"/>.
        /// Read again every 64 MB stored, every 10 s, and whenever the last reading less what was stored since comes near.
        /// </summary>
        void EnsureRoom(long bytes, bool batch = true)
        {
            long reserve = batch ? Reserve : Math.Min(Reserve, GenerationReserve);
            long free;
            lock (roomGate)
            {
                long stored = Interlocked.Read(ref BytesStored), now = Environment.TickCount64;
                long estimate = roomFree - (stored - roomStoredAt);
                if (roomFree < 0 || stored - roomStoredAt > 64L << 20 || now - roomAt > 10_000 || estimate - bytes < reserve + (256L << 20))
                {
                    roomFree = ReadFree();
                    roomStoredAt = stored;
                    roomAt = now;
                    estimate = roomFree;
                }
                free = estimate;
            }
            if (free - bytes < reserve) throw StoreFailed(RoomMessage(free, bytes, reserve), null);
        }

        long ReadFree()
        {
            try { return FreeSpace != null ? FreeSpace() : DataTransport.FreeBytes(Target.Folder); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                // a disk whose free space can't be read: the writes themselves say when it is full
                return long.MaxValue;
            }
        }

        string RoomMessage(long free, long bytes, long reserve = -1) =>
            $"only {Size(Math.Max(0, free))} free on the disk holding {Target.Folder}: {Size(bytes)} are needed and Earth leaves {Size(reserve < 0 ? Reserve : reserve)} free";

        static string Size(long bytes) => bytes >= 1_000_000_000
            ? (bytes / 1e9).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB"
            : (bytes / 1e6).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

        StoreException StoreFailed(string message, Exception inner)
        {
            Volatile.Write(ref storeError, message);
            Interlocked.Increment(ref storeFailures);
            return new StoreException(message, inner);
        }

        /// <summary>EA-0100: null when the store's disk has room for <paramref name="bytes"/> beyond the reserve, else why not.</summary>
        public string RoomFor(long bytes)
        {
            long free = ReadFree();
            return free - bytes >= Reserve ? null : RoomMessage(free, bytes);
        }

        /// <summary>(EA-0116 D-L4) ElevationRepo or ElevationSource can't be used as set (a malformed address or path), or null.</summary>
        public readonly string SettingProblem;

        public DemDownloader(FineElevation target, string source = Glo90Bucket, int parallel = 3, string repo = null)
        {
            Target = target;
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo.Trim();
            // (EA-0116 D-L4) said once, naming the setting; the fetches then fail as ones that may pass (never "lacking")
            SettingProblem = DataTransport.SourceProblem(Repo) is string r ? "ElevationRepo in earth.json: " + r
                : DataTransport.SourceProblem(Source) is string s ? "ElevationSource in earth.json: " + s : null;
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
        /// Every wait, the unbounded one too, ends with <see cref="ServerStoppingException"/> once <see cref="Stopping"/> says
        /// so (FP-01).
        /// </summary>
        public bool AwaitForGeneration(int latS, int lonW, Task<bool> fetch, TimeSpan budget, long since = 0)
        {
            if (fetch.IsCompleted) return fetch.GetAwaiter().GetResult();
            if (budget == Timeout.InfiniteTimeSpan)
            {
                // the spawn chunks wait as long as the download takes, but not past a stop (C-L2 of the Fable pass)
                while (!fetch.IsCompleted)
                {
                    ThrowIfStopping(latS, lonW);
                    if (Stopping == null) break;
                    Task.WhenAny(fetch, Task.Delay(StopPollMs)).GetAwaiter().GetResult();
                }
                return fetch.GetAwaiter().GetResult();
            }
            int key = FineElevation.Key(latS, lonW);
            long called = Environment.TickCount64, budgetMs = (long)budget.TotalMilliseconds;
            long from = since > 0 ? since : called, backstop = from + budgetMs * BackstopBudgets;
            while (!fetch.IsCompleted)
            {
                ThrowIfStopping(latS, lonW);
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
                if (Stopping != null) wait = Math.Min(wait, StopPollMs);
                Task.WhenAny(fetch, Task.Delay(TimeSpan.FromMilliseconds(wait))).GetAwaiter().GetResult();
            }
            return fetch.GetAwaiter().GetResult();
        }

        /// <summary>
        /// (FP-01, the Fable pass of the 14 November 2026 code review) Whether the server is stopping; null: never asked. A chunk
        /// generation thread waiting for a square looks at it every <see cref="StopPollMs"/> and ends its wait with
        /// <see cref="ServerStoppingException"/>: the game pauses its threads before it disposes the mods, and a thread pauses
        /// only between chunks, so a wait that went on past the game's 10 seconds had Earth disposed under it, and the chunks
        /// that thread made next (built-in heights, then vanilla's terrain) were saved.
        /// </summary>
        public Func<bool> Stopping;

        /// <summary>How often a generation thread waiting for a square looks at <see cref="Stopping"/>, in milliseconds.</summary>
        public const int StopPollMs = 250;

        void ThrowIfStopping(int latS, int lonW)
        {
            if (Stopping?.Invoke() == true)
                throw new ServerStoppingException("the server is stopping while chunk generation waits for GLO-90 tile " + DemTiles.ShortName(latS, lonW));
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
        /// <param name="token">ER-16: cancelled when the server stops (the wait for another server's turn and the download).</param>
        public bool FetchFile(string name, string target, long bytes, string sha256, out string error, out bool lacking, CancellationToken token = default)
        {
            error = null;
            lacking = false;
            if (Repo == null) { error = "no data release set"; lacking = true; return false; }
            // (EA-0092 A4) the one protocol for a set of published files (DataTransport.EnsureFilesAsync); servers sharing the
            // folder take turns (review A4), and the one that waited may find the file fetched
            var (result, why) = Task.Run(() => DataTransport.EnsureFilesAsync(Path.GetDirectoryName(Path.GetFullPath(target)),
                new[] { new DataTransport.FileSpec(name, target, bytes, sha256) }, Repo, "ElevationRepo", "data release", "release file(s)", Log, token,
                received: n => Interlocked.Add(ref BytesDownloaded, n), transport: transport), token).GetAwaiter().GetResult();
            if (result == DataTransport.SetResult.Here) return true;
            error = why;
            lacking = result == DataTransport.SetResult.Lacking;
            return false;
        }

        /// <summary>The squares whose last fetch found that no source has them (review A3), as FineElevation.Key.</summary>
        readonly ConcurrentDictionary<int, byte> lacking = new();

        /// <summary>(EA-0121) Each square's last failure, for whoever waited for it (/earth tp says it); cleared when the square
        /// is fetched.</summary>
        readonly ConcurrentDictionary<int, string> failures = new();

        /// <summary>(EA-0121) Why the last fetch of a square failed, as far as the downloader knows; null when none did.</summary>
        public string WhyFailed(int latS, int lonW) => failures.TryGetValue(FineElevation.Key(latS, lonW), out string why) ? why : null;

        void Failed(int latS, int lonW, string why) => failures[FineElevation.Key(latS, lonW)] = why;

        /// <summary>The last reason the release's manifest could not be had (EA-0121's message), or null.</summary>
        volatile string manifestError;

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
        public Task<bool> FetchAsync(int latS, int lonW, bool copernicus = false) => FetchAsync(latS, lonW, copernicus, false);

        /// <summary><see cref="FetchAsync(int, int, bool)"/>; <paramref name="batch"/>: for "download all", held to the full
        /// <see cref="Reserve"/> (a fetch for generation only to <see cref="GenerationReserve"/>).</summary>
        Task<bool> FetchAsync(int latS, int lonW, bool copernicus, bool batch)
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
                try { ok = await Fetch(latS, lonW, copernicus, batch).ConfigureAwait(false); }
                catch (Exception e) { Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": " + e.Message); Failed(latS, lonW, e.Message); }
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

        async Task<bool> Fetch(int latS, int lonW, bool copernicus, bool batch)
        {
            int key = FineElevation.Key(latS, lonW);
            // whether each origin answered that it doesn't have the square (an origin that isn't set has none)
            bool releaseLacks = Repo == null, copernicusLacks = Source == null;
            // the release's manifest: whether the release has the square, and its part. (Fable's overview low 8, 14 November
            // 2026) A fetch from Copernicus doesn't wait for it first: its conversion is judged by the heights table shipped in
            // the mod (ReleaseHeights, since 0f88041), so the manifest is asked for only when Copernicus doesn't give the
            // square (the release's tile instead, and whether both lack it). Before, with no copy of the manifest in the store
            // and the release down, the first fetch of each minute waited for the manifest's attempt.
            Manifest m = null;
            Entry e = default;
            bool listed = false, releaseTried = false, differs = false;
            if (Repo != null && !copernicus) (m, e, listed) = await Listing(key).ConfigureAwait(false);
            // (EA-0121) a release whose manifest can't be had: the reason for whoever waits for the square
            if (Repo != null && !copernicus && m == null) Failed(latS, lonW, ManifestFailure());
            if (!copernicus && listed)
            {
                releaseTried = true;
                if (await FetchFromRepo(latS, lonW, m, e, batch).ConfigureAwait(false)) return Fetched(key);
                if (Source != null) Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": not from the release; trying Copernicus");
            }
            if (Source != null)
            {
                // (Fable's re-review of 90692fe) compared only where this runtime encodes as the release's did
                ulong? want = ReleaseHeights != null && ReleaseHeights.TryGet(latS, lonW, out ulong sum) ? sum : null;
                var (ok, notFound, other) = await FetchFromCopernicus(latS, lonW, want, batch).ConfigureAwait(false);
                if (ok) return Fetched(key);
                copernicusLacks = notFound;
                differs = other;
            }
            if (Repo != null && copernicus)
            {
                (m, e, listed) = await Listing(key).ConfigureAwait(false);
                if (m == null && Source == null) Failed(latS, lonW, ManifestFailure());
            }
            if (m != null && !listed) releaseLacks = true;
            // EA-0106 (FP-15): Copernicus's file is not the one the release was made from: the release's tile is preferred
            if (differs && !releaseTried && listed)
            {
                Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": the release's tile instead");
                if (await FetchFromRepo(latS, lonW, m, e, batch).ConfigureAwait(false)) return Fetched(key);
            }
            if (releaseLacks && copernicusLacks) lacking[key] = 0;
            else lacking.TryRemove(key, out _);
            return false;
        }

        /// <summary>A square fetched: neither lacking nor failed any more.</summary>
        bool Fetched(int key)
        {
            lacking.TryRemove(key, out _);
            failures.TryRemove(key, out _);
            return true;
        }

        string ManifestFailure() => "the release's manifest could not be had from " + Repo + (manifestError != null ? ": " + manifestError : "");

        /// <summary>The release's manifest (null when it can't be had) and the square's entry in it.</summary>
        async Task<(Manifest M, Entry E, bool Listed)> Listing(int key)
        {
            var m = await GetManifest().ConfigureAwait(false);
            Entry e = default;
            bool listed = m != null && m.Tiles.TryGetValue(key, out e);
            return (m, e, listed);
        }

        /// <summary>The pause before try n + 1 of a square.</summary>
        static TimeSpan Pause(int attempt) => TimeSpan.FromMilliseconds(2000 * attempt * attempt);

        /// <summary>
        /// A square from Copernicus, converted here; NotFound: the bucket (or folder) answered that it doesn't have it.
        /// EA-0106 (the Fable pass's FP-15 and L8; Fable's re-review of 255baa7): the conversion must have GLO-90's dimensions,
        /// and where the release has the square (<paramref name="expectHeights"/>, from <see cref="ReleaseHeights"/>) its
        /// heights must be the release's: every machine then makes the same heights, whichever way its tiles came. Differs:
        /// they were not (a source file issued again since egl-v1, or another converter); it is not kept. Without the table
        /// the conversion is kept as before, and that is said once.
        /// </summary>
        async Task<(bool Ok, bool NotFound, bool Differs)> FetchFromCopernicus(int latS, int lonW, ulong? expectHeights, bool batch = true)
        {
            string name = DemTiles.SourceName(latS, lonW);
            string url = LocalSource ? Path.Combine(Source, name + ".tif") : Source.TrimEnd('/') + "/" + name + "/" + name + ".tif";
            bool notFound = false, differs = false;
            if (ReleaseHeights == null && Interlocked.Exchange(ref uncomparedSaid, 1) == 0)
                Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": the release's heights table isn't here, so tiles converted from Copernicus can't be " +
                    "compared with the release's; they are kept as converted (said once)");
            bool ok = await DataTransport.RetryAsync(Retries, async attempt =>
            {
                EnsureRoom(AverageTileBytes, batch);
                await slots.WaitAsync(closing.Token).ConfigureAwait(false);
                started.TryAdd(FineElevation.Key(latS, lonW), Environment.TickCount64);   // CR-D2: the generation wait counts from here
                try
                {
                    var (found, data, etag) = await transport.GetAsync(url, MaxSourceBytes).ConfigureAwait(false);
                    if (!found)
                    {
                        notFound = true;
                        Log?.Invoke(name + (LocalSource ? ": not in " + Source : ": not in the bucket (404)"));
                        Failed(latS, lonW, "Copernicus doesn't have it" + (LocalSource ? " (not in " + Source + ")" : " (404)"));
                        return false;
                    }
                    Interlocked.Add(ref BytesDownloaded, data.Length);
                    var tile = DemTiles.FromGrid(DemTiles.ReadGeoTiff(data), latS, lonW);
                    // EA-0106 (L8): the readers index rows and columns by GLO-90's grid; another product (ElevationSource set to the
                    // 30 m bucket) was kept, then refused at every read and fetched again. The same file every try: not retried.
                    if (tile.Rows != DemTiles.Glo90Rows || tile.Cols != DemTiles.Glo90Cols(latS))
                    {
                        Log?.Invoke($"{name}: {tile.Cols} x {tile.Rows} cells, not GLO-90's {DemTiles.Glo90Cols(latS)} x {DemTiles.Glo90Rows}; not kept");
                        Failed(latS, lonW, $"Copernicus's file has {tile.Cols} x {tile.Rows} cells, not GLO-90's");
                        return false;
                    }
                    tile.SourceBytes = data.Length;
                    tile.SourceETag = etag;
                    byte[] file = DemTiles.Encode(tile);
                    // read back before keeping it: a converter fault must not become a world's terrain
                    var back = DemTiles.Decode(file);
                    if (back.Cols != tile.Cols || back.Rows != tile.Rows) throw new InvalidDataException("tile did not read back");
                    if (expectHeights is ulong want && DemTiles.Checksum(tile.Heights) != want)
                    {
                        differs = true;
                        Interlocked.Increment(ref TilesDiffering);
                        Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": converted from Copernicus it is not the release's tile (a source file issued " +
                            "again since egl-v1, or another converter); not kept, so that every machine makes the same heights");
                        Failed(latS, lonW, "converted from Copernicus it is not the release's tile");
                        return false;
                    }
                    Write(latS, lonW, file, batch);
                    Interlocked.Increment(ref TilesConverted);
                    return true;
                }
                finally { slots.Release(); }
            }, Pause, (attempt, e) =>
            {
                Log?.Invoke($"{name}: attempt {attempt} of {Retries} failed: {e.Message}");
                Failed(latS, lonW, "from Copernicus: " + e.Message);
            }, closing.Token).ConfigureAwait(false);
            return (ok, !ok && notFound, !ok && differs);
        }

        int uncomparedSaid;

        /// <summary>Whether a tile file is the one the manifest lists: its length and SHA-256.</summary>
        static bool Matches(byte[] file, Entry e) => file.Length == e.Length && SHA256.HashData(file).AsSpan().SequenceEqual(e.Sha);

        /// <summary>
        /// EA-0106 (the Fable pass's FP-15; Fable's re-review of 255baa7): a tile this machine keeps, at FineElevation's first
        /// read of the square in a session (<see cref="FineElevation.Verify"/>), judged by its heights' checksum against
        /// <see cref="ReleaseHeights"/>, which needs neither the release's manifest nor encoding the tile again. True: the
        /// release's heights (whatever the file's bytes), or nothing to judge by (no table, or the release has no tile there).
        /// False: other heights (another source file, converter or source): the tile stays in use this session and the
        /// release's is fetched beside it (<see cref="FetchReplacement"/>).
        /// </summary>
        public bool? KeptMatches(int latS, int lonW, byte[] file, DemTiles.Tile tile)
        {
            if (ReleaseHeights == null || !ReleaseHeights.TryGet(latS, lonW, out ulong want)) return true;
            ulong have = tile.HeightsChecksum != 0 ? tile.HeightsChecksum : DemTiles.Checksum(tile.Heights);
            if (have == want) return true;
            Interlocked.Increment(ref TilesDiffering);
            Log?.Invoke(DemTiles.ShortName(latS, lonW) + ": the tile here has other heights than the release's (another source file, converter or " +
                "source); it stays in use this session, and the release's is fetched beside it to take its place at the next start");
            return false;
        }



        /// <summary>EA-0106: starts loading the release's manifest (from the copy here, else the release), so that the first
        /// reads of tiles here can be compared with it.</summary>
        public void LoadManifestSoon()
        {
            if (Repo != null) GetManifest();
        }

        /// <summary>A square from the release: its bytes by a range request on its part (with the transport's read timeout, so
        /// a stalled body can't hold the generation thread waiting for it), kept when they are the manifest's.</summary>
        Task<bool> FetchFromRepo(int latS, int lonW, Manifest m, Entry e, bool batch = true)
        {
            string name = DemTiles.ShortName(latS, lonW);
            return DataTransport.RetryAsync(Retries, async attempt =>
            {
                EnsureRoom(e.Length, batch);
                await slots.WaitAsync(closing.Token).ConfigureAwait(false);
                started.TryAdd(FineElevation.Key(latS, lonW), Environment.TickCount64);   // CR-D2: the generation wait counts from here
                try
                {
                    byte[] data = await transport.RangeAsync(Repo, m.Parts[e.Part - 1], e.Offset, e.Length).ConfigureAwait(false);
                    Interlocked.Add(ref BytesDownloaded, data.Length);
                    Keep(latS, lonW, e, data, batch);
                    Interlocked.Increment(ref TilesFetched);
                    return true;
                }
                finally { slots.Release(); }
            }, Pause, (attempt, x) =>
            {
                Log?.Invoke($"{name}: attempt {attempt} of {Retries} from the release failed: {x.Message}");
                Failed(latS, lonW, "from the release: " + x.Message);
            }, closing.Token);
        }

        /// <summary>A square from the release, kept only when it is the manifest's file for that square.</summary>
        void Keep(int latS, int lonW, Entry e, byte[] data, bool batch = true)
        {
            if (!Matches(data, e))
                throw new InvalidDataException("differs from the release's manifest");
            if (data.Length < 12 || data[0] != (byte)'E' || data[1] != (byte)'G' || data[2] != (byte)'L' || data[3] != (byte)'O'
                || data[4] != DemTiles.FormatVersion || BitConverter.ToInt16(data, 8) != latS || BitConverter.ToInt16(data, 10) != lonW)
                throw new InvalidDataException("not this square's tile");
            Write(latS, lonW, data, batch);
        }

        void Write(int latS, int lonW, byte[] file, bool batch = true)
        {
            WriteAs(latS, lonW, file, Target.PathOf(latS, lonW), batch);
            Interlocked.Add(ref BytesStored, file.Length);
            Target.TileArrived(latS, lonW);
        }

        /// <summary>A checked tile's bytes into <paramref name="path"/> through a temporary name of its own.</summary>
        void WriteAs(int latS, int lonW, byte[] file, string path, bool batch)
        {
            EnsureRoom(file.Length, batch);
            for (int attempt = 1; ; attempt++)
            {
                string tmp = UniquePart(path);
                try
                {
                    Directory.CreateDirectory(Target.Folder);
                    File.WriteAllBytes(tmp, file);
                    File.Move(tmp, path, true);
                    return;
                }
                // (Fable's re-review of 1926d8f) a write refused for a moment (a virus scanner holding the new file) is tried
                // again, twice; a full disk is final at once
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 3 && !DiskFull(e))
                {
                    Log?.Invoke($"{DemTiles.ShortName(latS, lonW)}: writing into {Target.Folder} failed ({e.Message}); trying again");
                    Thread.Sleep(250 * attempt);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // EA-0100: the store's own fault, not the source's (the bytes were checked): never fetched again elsewhere
                    throw StoreFailed($"{DemTiles.ShortName(latS, lonW)} could not be written into {Target.Folder}: {e.Message}", e);
                }
                finally { TryDelete(tmp); }
            }
        }


        /// <summary>A disk full (ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL; ENOSPC on Linux).</summary>
        static bool DiskFull(Exception e) => (e.HResult & 0xFFFF) is 0x70 or 0x27 || e.HResult == 28;

        /// <summary>
        /// (Fable's re-review of 90692fe) The release's tile for a square whose tile here is not the release's, fetched in the
        /// background into FineElevation.ReplacementExtension beside the one in use (which stays in use this session); it takes
        /// that one's place at the next start (FineElevation.ApplyReplacements). Nothing changes when the release or its
        /// manifest can't be had: the tile here goes on, and the next session asks again.
        /// </summary>
        public void FetchReplacement(int latS, int lonW)
        {
            if (Repo == null) return;
            string name = DemTiles.ShortName(latS, lonW);
            _ = Task.Run(async () =>
            {
                try
                {
                    var m = await GetManifest().ConfigureAwait(false);
                    if (m == null || !m.Tiles.TryGetValue(FineElevation.Key(latS, lonW), out var e)) return;
                    byte[] data;
                    await slots.WaitAsync(closing.Token).ConfigureAwait(false);
                    try { data = await transport.RangeAsync(Repo, m.Parts[e.Part - 1], e.Offset, e.Length).ConfigureAwait(false); }
                    finally { slots.Release(); }
                    Interlocked.Add(ref BytesDownloaded, data.Length);
                    if (!Matches(data, e)) throw new InvalidDataException("differs from the release's manifest");
                    WriteAs(latS, lonW, data, Target.PathOf(latS, lonW) + FineElevation.ReplacementExtension, false);
                    Log?.Invoke(name + ": the release's tile is here beside the one in use; it takes that one's place at the next start");
                }
                catch (Exception x)
                {
                    Log?.Invoke(name + ": the release's tile could not be fetched to replace the one here (" + x.Message + "); asked again next session");
                }
            });
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
                {
                    // EA-0106: not read past what a manifest may be
                    if (new FileInfo(cache).Length > ManifestLimit) Log?.Invoke("the release's manifest here is larger than one can be; fetching it again");
                    else
                    {
                        byte[] kept = await File.ReadAllBytesAsync(cache).ConfigureAwait(false);
                        if (ManifestFits(kept)) return ParseManifest(kept);
                        Log?.Invoke("the release's manifest here is not egl-v1's; fetching it again");
                    }
                }
            }
            catch (Exception e)
            {
                Log?.Invoke("the release's manifest here is unreadable; fetching it again: " + e.Message);
            }
            try
            {
                // EA-0106 (FP-17): the default release's manifest is read no further than its pinned size, a mirror's than 16 MB
                var (found, data, _) = await transport.GetAsync(RepoPath(ManifestName), ManifestLimit).ConfigureAwait(false);
                if (!found) throw new FileNotFoundException(ManifestName + " is not there");
                if (!ManifestFits(data)) throw new InvalidDataException(ManifestName + " from " + Repo + " is not egl-v1's (" + data.Length + " bytes)");
                var m = ParseManifest(data);
                // L7 of the Fable pass: a copy that can't be kept (the store can't be written) doesn't lose the manifest
                try
                {
                    Directory.CreateDirectory(Target.Folder);
                    string tmp = UniquePart(cache);
                    try
                    {
                        await File.WriteAllBytesAsync(tmp, data).ConfigureAwait(false);
                        File.Move(tmp, cache, true);
                        await File.WriteAllTextAsync(from, Repo ?? "").ConfigureAwait(false);
                    }
                    finally { TryDelete(tmp); }
                }
                catch (Exception x) when (x is IOException or UnauthorizedAccessException)
                {
                    Log?.Invoke($"the release's manifest could not be kept in {Target.Folder} ({x.Message}); it is used for this session");
                }
                Log?.Invoke($"the release's manifest: {m.Tiles.Count} squares in {m.Parts.Length} parts, from {Repo}");
                return m;
            }
            catch (Exception e)
            {
                manifestFailedAt = DateTime.UtcNow;
                manifestError = e.Message;
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
                long bytes = p.GetProperty("bytes").GetInt64();
                if (bytes < 0) throw new InvalidDataException("part " + name + " of " + bytes + " bytes");
                m.Parts[i] = name;
                m.PartBytes[i++] = bytes;
            }
            foreach (var t in root.GetProperty("tiles").EnumerateObject())
            {
                if (!TryParseShortName(t.Name, out int latS, out int lonW)) throw new InvalidDataException("square " + t.Name);
                var a = t.Value;
                int part = a[0].GetInt32(), len = a[2].GetInt32();
                long off = a[1].GetInt64();
                byte[] sha = Convert.FromHexString(a[3].GetString());
                // EA-0106 (FP-17, L2): no entry asks for more than a tile may be (a mirror's asked for 1.5 GB a square), and its end
                // is found without overflow
                if (part < 1 || part > m.Parts.Length || off < 0 || len <= 0 || len > MaxTileBytes || off > m.PartBytes[part - 1] - len || sha.Length != 32)
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
            Volatile.Write(ref storeError, null);
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
                    else if (RoomFor(squares.Count * AverageTileBytes) is string no) b.Error = no;
                    else await EachSquare(squares, b, workers, true).ConfigureAwait(false);
                }
                catch (StoreException e)
                {
                    b.Error = e.Message;
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
            // EA-0100: the store refusing a tile (here or in a generation fetch meanwhile) ends the batch: every square would follow
            long failuresAtStart = Interlocked.Read(ref storeFailures);
            var tasks = new List<Task>();
            for (int w = 0; w < workers; w++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (!b.Cancel.IsCancellationRequested && Interlocked.Read(ref storeFailures) == failuresAtStart)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= squares.Count) break;
                        var (latS, lonW) = squares[i];
                        bool ok = await FetchAsync(latS, lonW, copernicus, true).ConfigureAwait(false);
                        if (ok) Interlocked.Increment(ref b.Done); else Interlocked.Increment(ref b.Failed);
                    }
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            if (Interlocked.Read(ref storeFailures) != failuresAtStart) b.Error ??= StoreError;
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
            // EA-0100: room for every missing square (the manifest's sizes; Copernicus's conversions at the average) beyond the reserve
            long toStore = byPart.Sum(p => p.Sum(t => (long)t.e.Length));
            foreach (var (latS, lonW) in squares)
                if (!m.Tiles.ContainsKey(FineElevation.Key(latS, lonW))) toStore += AverageTileBytes;
            if (RoomFor(toStore) is string no)
            {
                b.Error = no;
                return;
            }
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
                        // (a StoreException goes on up: the batch ends with it, no other square of the part is read)
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

        /// <summary>(EA-0092 A10) Cancelled at Dispose: a fetch waiting for a slot or between tries ends then, instead of a try
        /// or two later on a disposed transport, and nothing is logged through the mod's logger after it is gone.</summary>
        readonly CancellationTokenSource closing = new();

        public void Dispose()
        {
            CancelAll();
            closing.Cancel();
            Log = null;
            transport.Dispose();
        }
    }
}
