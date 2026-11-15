using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// The one transport for every file Earth downloads: GLO-90 squares and the parts of its data release
    /// (DemDownloader), the release files that come with the GLO-90 heights (lake and river outlines, the dam file) and
    /// the 30″ ground sets (GroundDownloader). A source is a URL ending in '/' or a local folder holding the published
    /// names. Every body read gives up after <see cref="ReadTimeout"/> without data (HttpClient's own timeout ends with the
    /// headers, so a server that stalls after them would hold a generation thread for ever); a whole file is written as
    /// name.part, resumed from where it broke by a range request, checked against its size and SHA-256 and only then moved
    /// into place, so a file is either whole and checked or absent; transient failures are retried with a growing pause.
    /// </summary>
    public sealed class DataTransport : IDisposable
    {
        public const string UserAgent = "EarthVintageStoryMod/1.0";
        /// <summary>Tries for a whole file (<see cref="FileAsync"/>), with 1.5, 6 and 13.5 s between them.</summary>
        public const int FileTries = 4;

        readonly HttpClient http;
        /// <summary>How long one read of a body may wait for data.</summary>
        public TimeSpan ReadTimeout = TimeSpan.FromMinutes(1);

        /// <param name="headerTimeout">HttpClient's timeout: until a response's headers are in.</param>
        public DataTransport(TimeSpan headerTimeout)
        {
            // a host that never answers fails in seconds, not after the whole header timeout of each try (CR-D2)
            http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout }) { Timeout = headerTimeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        /// <summary>How long a connection may take to open.</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Free bytes on the file system holding <paramref name="folder"/>: the mounted drive whose root is the longest prefix of
        /// its full path (on Linux the path's root is "/", the root file system, not a data volume mounted under it, as in Docker;
        /// CR-D5). Throws as DriveInfo does where it can't be read.
        /// </summary>
        public static long FreeBytes(string folder)
        {
            string full = Path.GetFullPath(folder);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo best = null;
            foreach (var d in DriveInfo.GetDrives())
            {
                string root;
                try { root = d.RootDirectory.FullName; } catch (Exception) { continue; }
                string withSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
                bool holds = full.Equals(root, comparison) || full.StartsWith(withSep, comparison);
                if (holds && (best == null || root.Length > best.RootDirectory.FullName.Length)) best = d;
            }
            return (best ?? new DriveInfo(Path.GetPathRoot(full))).AvailableFreeSpace;
        }

        public void Dispose() => http.Dispose();

        public static bool IsUrl(string source) => source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// (EA-0116 D-L4) Why a source setting (DataRepo, GroundRepo, ElevationRepo, ElevationSource) can't be used, or null: an
        /// http(s) address must parse with a host; anything else is a folder, whose path must be one this system accepts. A
        /// folder that isn't there is not a fault of the setting's form (a drive may be mounted later): a fetch from it fails
        /// as one that may pass, naming the folder (<see cref="FolderThere"/>), and never counts as the source lacking the
        /// files (Fable's overview low 5). Null or blank: none set, not a problem. Before,
        /// "http://exa mple/" or "http://" ended the start with a raw UriFormatException or InvalidOperationException that
        /// didn't name the setting.
        /// </summary>
        public static string SourceProblem(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            source = source.Trim();
            if (IsUrl(source))
                return Uri.TryCreate(source, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) && !source.Contains(' ')
                    ? null : $"\"{source}\" is not a usable web address";
            try
            {
                Path.GetFullPath(source);
                return source.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ? $"\"{source}\" is not a usable folder" : null;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                return $"\"{source}\" is not a usable folder ({e.Message})";
            }
        }

        /// <summary>
        /// (Fable's overview low 5, 14 November 2026) A local source's folder that isn't there (a drive not mounted, a typo in
        /// the address, "htps://" read as a folder) throws <see cref="DirectoryNotFoundException"/> naming it: a failure that
        /// may pass, never the source's answer that it lacks a file, which a world may record for good (review A3: a new world
        /// made while the folder was away would keep the 2' ground sets for good).
        /// </summary>
        public static void FolderThere(string folder)
        {
            string full = Path.GetFullPath(folder);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"the source folder {full} isn't there");
        }

        /// <summary>Where a published name is in a source: the URL below it, or the file in the folder.</summary>
        public static string Locate(string source, string name) => IsUrl(source) ? source.TrimEnd('/') + "/" + name : Path.Combine(source, name);

        // ------------------------------------------------------------------------------------------ checks

        public static bool HashMatches(byte[] data, string sha256) => Matches(SHA256.HashData(data), sha256);
        public static bool HashMatches(Stream data, string sha256) => Matches(SHA256.HashData(data), sha256);
        static bool Matches(byte[] hash, string sha256) => sha256 != null && Convert.ToHexString(hash).Equals(sha256, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether the file at <paramref name="path"/> is there with this size and, with <paramref name="hash"/>, this
        /// SHA-256 (about a second for 170 MB); a file that can't be read is not.</summary>
        public static bool FileMatches(string path, long bytes, string sha256, bool hash = true)
        {
            try
            {
                var f = new FileInfo(path);
                if (!f.Exists || f.Length != bytes) return false;
                if (!hash) return true;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                return HashMatches(fs, sha256);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------------------------------------ turns

        /// <summary>How long a server waits for another one's turn at a folder (<see cref="TakeTurnAsync"/>).</summary>
        public static TimeSpan TurnWait = TimeSpan.FromMinutes(20);
        public const string TurnFile = "fetch.lock";

        /// <summary>
        /// This process's turn at fetching whole files into <paramref name="folder"/> (adversarial review A4): an exclusive lock
        /// on folder/fetch.lock, waited for while another server fetches into the same folder (a single-player game and a
        /// dedicated server on one machine share VintagestoryData/ModData/earth, and two writers of one .part file would
        /// break each other's download). Whoever takes a turn looks again at what is missing first: the other server may
        /// have fetched it meanwhile. Disposing gives the turn back. The lock file stays in the folder (empty): deleting it
        /// on close let two processes hold it on Linux (flock on a file unlinked while another opened it) and made a
        /// third's open fail with "access denied" on Windows while the deletion was pending (review A9). An IOException that
        /// says what happened when the wait runs out or the folder can't be written.
        /// </summary>
        public static async Task<IDisposable> TakeTurnAsync(string folder, Action<string> log, CancellationToken token = default, TimeSpan? wait = null)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, TurnFile);
            var limit = wait ?? TurnWait;
            var start = DateTime.UtcNow;
            bool said = false;
            while (true)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // IOException: held by another server. "Access denied": a lock file an older Earth deleted on close, still
                    // being deleted while something holds it open (Windows), which passes within moments; for longer it is
                    // the folder's permissions
                    var waited = DateTime.UtcNow - start;
                    if (e is UnauthorizedAccessException && waited >= TimeSpan.FromSeconds(Math.Min(30, limit.TotalSeconds)))
                        throw new IOException($"Earth can't open its lock file {path} ({e.Message}); give the server write access to {folder}", e);
                    if (waited >= limit)
                        throw new IOException($"another Earth server has been fetching into {folder} for {Span(limit)} and still holds " +
                            $"{path} ({e.Message}); let it finish or stop it, then start this one again", e);
                    if (!said && e is IOException) { log?.Invoke($"another server is fetching into {folder}; waiting for it (up to {Span(limit)})."); said = true; }
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }

        static string Span(TimeSpan t) => t.TotalMinutes >= 1 ? $"{t.TotalMinutes:0.#} minutes" : $"{t.TotalSeconds:0.#} seconds";

        /// <summary>Why <paramref name="needed"/> more bytes don't fit on the disk holding <paramref name="folder"/>, or null (when
        /// they fit, or its free space can't be read): a fetch that failed for lack of space says so, not the network.</summary>
        public static string DiskShortfall(string folder, long needed)
        {
            try
            {
                Directory.CreateDirectory(folder);
                long free = FreeBytes(folder);
                return free < needed ? $"the disk holding {folder} has {free / 1e6:F0} MB free and {needed / 1e6:F0} MB are needed" : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        }

        // ------------------------------------------------------------------------------------------ retries

        /// <summary>Failures worth another try: the network, the disk, a body that timed out or didn't check out. Not the source's
        /// answer that it doesn't have the file (<see cref="NotThereException"/>).</summary>
        public static bool IsTransient(Exception e) => e is not NotThereException
            && e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException;

        /// <summary>
        /// The source answered that it doesn't have the file: HTTP 404 or 410, or a local source folder without it. An answer, not
        /// a failure: it isn't tried again, and the caller may record it for good (review A3: only such an answer, never a
        /// network error, may make a world go without a file for good).
        /// </summary>
        public sealed class NotThereException : IOException
        {
            public NotThereException(string message, Exception inner = null) : base(message, inner) { }
        }

        /// <summary>
        /// Runs <paramref name="attempt"/> (with its number, from 1) until it returns true, at most <paramref name="tries"/>
        /// times. False is an answer (the source doesn't have it) and ends it at once; a transient exception is handed to
        /// <paramref name="failed"/> and tried again after <paramref name="pause"/>. The caller's cancellation always ends it.
        /// </summary>
        public static async Task<bool> RetryAsync(int tries, Func<int, Task<bool>> attempt, Func<int, TimeSpan> pause, Action<int, Exception> failed, CancellationToken token = default)
        {
            for (int a = 1; a <= tries; a++)
            {
                try { return await attempt(a).ConfigureAwait(false); }
                catch (Exception e) when (IsTransient(e) && !token.IsCancellationRequested) { failed?.Invoke(a, e); }
                if (a < tries) await Task.Delay(pause(a), token).ConfigureAwait(false);
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------ reads

        /// <summary>A read that gives up after <see cref="ReadTimeout"/> without data (an IOException saying so; the caller's
        /// own cancellation stays an OperationCanceledException).</summary>
        public async Task<int> ReadAsync(Stream s, Memory<byte> into, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(ReadTimeout);
            try { return await s.ReadAsync(into, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException($"no data for {ReadTimeout.TotalSeconds:0.#} s");
            }
        }

        /// <summary>Fills <paramref name="into"/> from the stream; EndOfStreamException when it ends first.</summary>
        public async Task ReadExactlyAsync(Stream s, Memory<byte> into, string what, CancellationToken token)
        {
            for (int got = 0; got < into.Length;)
            {
                int n = await ReadAsync(s, into.Slice(got), token).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException(what + " ended early");
                got += n;
            }
        }

        /// <summary>A stream on a published file from <paramref name="from"/> on (a range request), and its response.</summary>
        public sealed class Opened : IDisposable
        {
            public Stream Stream;
            public HttpResponseMessage Response;
            internal HttpRequestMessage Request;
            /// <summary>The byte the stream starts at: <c>from</c>, or 0 when the source sent the whole file instead.</summary>
            public long From;
            public void Dispose() { Stream?.Dispose(); Response?.Dispose(); Request?.Dispose(); }
        }

        public async Task<Opened> OpenAsync(string source, string name, long from, CancellationToken token)
        {
            if (!IsUrl(source))
            {
                FolderThere(source);
                FileStream fs;
                try { fs = new FileStream(Locate(source, name), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true); }
                catch (FileNotFoundException e) { throw new NotThereException($"{name} is not in {source}", e); }
                fs.Seek(Math.Min(from, fs.Length), SeekOrigin.Begin);
                return new Opened { Stream = fs, From = fs.Position };
            }
            var req = new HttpRequestMessage(HttpMethod.Get, Locate(source, name));
            if (from > 0) req.Headers.Range = new RangeHeaderValue(from, null);
            HttpResponseMessage resp = null;
            try
            {
                resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    throw new NotThereException($"{name}: HTTP {(int)resp.StatusCode}, not in {source}");
                long start = resp.StatusCode == HttpStatusCode.PartialContent ? resp.Content.Headers.ContentRange?.From ?? -1 : resp.StatusCode == HttpStatusCode.OK ? 0 : -1;
                if (start < 0 || (start != 0 && start != from)) throw new HttpRequestException($"{name}: HTTP {(int)resp.StatusCode}");
                var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                return new Opened { Request = req, Response = resp, From = start, Stream = stream };
            }
            catch
            {
                resp?.Dispose();
                req.Dispose();
                throw;
            }
        }

        /// <summary>
        /// <paramref name="length"/> bytes of a published file from <paramref name="offset"/> (a range request: GitHub
        /// redirects to its storage, which honours it), read with the read timeout. A server that ignores the range would
        /// send the whole file: it is not read.
        /// </summary>
        public async Task<byte[]> RangeAsync(string source, string name, long offset, int length, CancellationToken token = default)
        {
            var data = new byte[length];
            if (!IsUrl(source))
            {
                FolderThere(source);
                using var fs = new FileStream(Locate(source, name), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
                fs.Seek(offset, SeekOrigin.Begin);
                await fs.ReadExactlyAsync(data, token).ConfigureAwait(false);
                return data;
            }
            using var req = new HttpRequestMessage(HttpMethod.Get, Locate(source, name));
            req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.PartialContent) throw new HttpRequestException($"{name}: HTTP {(int)resp.StatusCode}, not a range");
            if (resp.Content.Headers.ContentLength is long sent && sent != length) throw new InvalidDataException($"{name}: {sent} bytes instead of {length}");
            using var body = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await ReadExactlyAsync(body, data, name, token).ConfigureAwait(false);
            return data;
        }

        /// <summary>
        /// A whole file at <paramref name="location"/> (a URL, or a path), read with the read timeout; Found false when the
        /// source doesn't have it (HTTP 404, no such file). ETag: the server's, "local" for a file. EA-0106 (the Fable pass's
        /// FP-17): at most <paramref name="maxBytes"/>; a file that is larger, or a body that says it is or turns out to be,
        /// throws <see cref="InvalidDataException"/> before more is read (a body without end from a mirror or a proxy took
        /// 1.5 GB in 3 s).
        /// </summary>
        public async Task<(bool Found, byte[] Data, string ETag)> GetAsync(string location, long maxBytes, CancellationToken token = default)
        {
            if (maxBytes <= 0 || maxBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (!IsUrl(location))
            {
                // (Fable's re-review, 14 November) a source folder that isn't there (a drive not mounted, a typo) is no answer
                // that it lacks the file, which a world may record for good (review A3): a failure that may pass
                FolderThere(Path.GetDirectoryName(Path.GetFullPath(location)));
                if (!File.Exists(location)) return (false, null, null);
                long size = new FileInfo(location).Length;
                if (size > maxBytes) throw TooLarge(location, size, maxBytes);
                return (true, await File.ReadAllBytesAsync(location, token).ConfigureAwait(false), "local");
            }
            using var resp = await http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return (false, null, null);
            resp.EnsureSuccessStatusCode();
            string etag = resp.Headers.ETag?.Tag?.Trim('"') ?? "";
            long? said = resp.Content.Headers.ContentLength;
            if (said > maxBytes) throw TooLarge(location, said.Value, maxBytes);
            using var body = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var memory = said is long n and > 0 ? new MemoryStream((int)n) : new MemoryStream();
            var buffer = new byte[1 << 16];
            int got;
            while ((got = await ReadAsync(body, buffer, token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + got > maxBytes) throw TooLarge(location, memory.Length + got, maxBytes, atLeast: true);
                memory.Write(buffer, 0, got);
            }
            return (true, memory.ToArray(), etag);
        }

        static InvalidDataException TooLarge(string location, long bytes, long maxBytes, bool atLeast = false)
            => new($"{location}: {(atLeast ? "more than " + (bytes - 1).ToString("N0") : bytes.ToString("N0"))} bytes, more than the {maxBytes:N0} expected at most; not read");


        // ------------------------------------------------------------------------------------------ a set of published files

        /// <summary>EA-0092 A4: one of a set of published files: its name in the source, where it goes, its size and SHA-256.</summary>
        public readonly record struct FileSpec(string Name, string Target, long Bytes, string Sha256);

        /// <summary>A set's answer: every file here and checked; the source answered that it lacks one (or none is set); a
        /// failure that may pass (the network, a download that didn't check out, a source setting that can't be used, another
        /// server's turn that ran out).</summary>
        public enum SetResult { Here, Lacking, Failed }

        /// <summary>
        /// EA-0092 A4 (code review 14 November 2026; CR-A11): the one protocol for a set of published files, used by Earth's
        /// data store, the 30″ ground sets and the GLO-90 release's files, which had it written three times with drifting
        /// policy. The files missing or wrong (size and SHA-256); none: Here. Then the folder's turn (servers sharing it take
        /// turns, review A4), and a second look (another server may have fetched them meanwhile); <paramref name="inTurn"/>
        /// runs with the turn held. Then <paramref name="copyIn"/> (an installed Earth 1.0 data pack carries the same bytes).
        /// Then each file from <paramref name="source"/>, stopping at the first that fails: the source is unreachable or
        /// lacks it, and the rest would only wait the same way (an offline first start retried every file for many minutes).
        /// No source set: Lacking; a source setting that can't be used (DataTransport.SourceProblem), or a local source folder
        /// that isn't there (Fable's overview low 5): Failed at once, naming <paramref name="setting"/> and the folder; all
        /// before the turn when there is nothing to copy in. The caller's cancellation
        /// ends it with OperationCanceledException, nothing left as if fetched. Why: the reason, the file's name first.
        /// </summary>
        public static async Task<(SetResult Result, string Why)> EnsureFilesAsync(string folder, IReadOnlyList<FileSpec> files, string source,
            string setting, string sourceLabel, string what, Action<string> log, CancellationToken token = default,
            bool hash = true, Func<FileSpec, bool> copyIn = null, Action inTurn = null, Action<int> received = null, DataTransport transport = null)
        {
            log ??= _ => { };
            // (EA-0092 A6) hash: false looks at the sizes only, for a caller that checks each file's hash as it reads it
            var todo = files.Where(f => !FileMatches(f.Target, f.Bytes, f.Sha256, hash)).ToList();
            if (todo.Count == 0) return (SetResult.Here, null);
            (SetResult, string)? NoSource()
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    log($"{todo.Count} {what} missing and no {sourceLabel} set ({setting} in earth.json); nothing is downloaded.");
                    return (SetResult.Lacking, $"no {sourceLabel} is set ({setting} in earth.json)");
                }
                // (EA-0116 D-L4) a setting that can't be used is named: a failure that may pass (a setting can be corrected)
                if (SourceProblem(source) is string bad)
                {
                    log($"{setting} in earth.json: {bad}; nothing is downloaded.");
                    return (SetResult.Failed, $"{setting} in earth.json: {bad}");
                }
                // (Fable's overview low 5) a folder that isn't there doesn't lack the files: a drive not mounted, a typo; the
                // setting and the folder are named, and nothing is recorded for good (review A3)
                if (!IsUrl(source))
                {
                    try { FolderThere(source); }
                    catch (DirectoryNotFoundException e)
                    {
                        log($"{setting} in earth.json: {e.Message}; nothing is downloaded.");
                        return (SetResult.Failed, $"{setting} in earth.json: {e.Message}");
                    }
                }
                return null;
            }
            if (copyIn == null && NoSource() is { } early) return early;
            IDisposable turn;
            try { turn = await TakeTurnAsync(folder, log, token).ConfigureAwait(false); }
            catch (IOException e) when (e is not NotThereException) { log(e.Message); return (SetResult.Failed, e.Message); }   // another server's turn ran out
            using (turn)
            {
                inTurn?.Invoke();
                todo = todo.Where(f => !FileMatches(f.Target, f.Bytes, f.Sha256, hash)).ToList();
                if (todo.Count == 0) return (SetResult.Here, null);
                if (copyIn != null)
                {
                    foreach (var f in todo.ToList()) if (copyIn(f)) todo.Remove(f);
                    if (todo.Count == 0) { log($"all {what} are in place (from installed data packs)."); return (SetResult.Here, null); }
                    if (NoSource() is { } late) return late;
                }
                log($"fetching {todo.Count} {what}, {todo.Sum(f => f.Bytes) / 1e6:F0} MB, from {source} into {folder}.");
                var own = transport == null ? new DataTransport(TimeSpan.FromMinutes(2)) : null;
                try
                {
                    var t = transport ?? own;
                    for (int i = 0; i < todo.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var f = todo[i];
                        (bool Ok, string Why, bool NotThere) r;
                        try { r = await t.FileAsync(source, f.Name, f.Target, f.Bytes, f.Sha256, FileTries, log, received, token).ConfigureAwait(false); }
                        catch (Exception e) when (e is UriFormatException or ArgumentException or NotSupportedException or InvalidOperationException) { r = (false, e.Message, false); }
                        if (r.Ok) { log($"{f.Name}: {f.Bytes / 1e6:F1} MB, checked."); continue; }
                        string why = r.Why ?? (r.NotThere ? "the source doesn't have it" : "not fetched");
                        log($"{f.Name} could not be fetched: {why}" + (i < todo.Count - 1 ? $"; the remaining {todo.Count - i - 1} file(s) are not tried." : "."));
                        return (r.NotThere ? SetResult.Lacking : SetResult.Failed, f.Name + ": " + why);
                    }
                }
                finally { own?.Dispose(); }
            }
            return (SetResult.Here, null);
        }

        // ------------------------------------------------------------------------------------------ whole files

        /// <summary>
        /// One try at a whole file: <paramref name="name"/> from <paramref name="source"/> into target.part (resumed from where
        /// it broke, started again when the source sends the whole file), then its size and SHA-256 checked and the file moved
        /// to <paramref name="target"/>. Throws on any failure; a .part that doesn't check out is deleted, one cut short is
        /// kept for the next try. <paramref name="progress"/>: a line every 32 MB or 15 s; <paramref name="received"/>: the
        /// bytes as they come.
        /// </summary>
        public async Task FileOnceAsync(string source, string name, string target, long bytes, string sha256, Action<string> progress = null,
            Action<int> received = null, CancellationToken token = default)
        {
            string part = target + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > bytes) { File.Delete(part); have = 0; }
            if (have < bytes)
            {
                using var opened = await OpenAsync(source, name, have, token).ConfigureAwait(false);
                if (opened.From != have) have = 0;                       // the source sent the whole file: start again
                using var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buffer = new byte[1 << 20];
                long nextNote = have + 32_000_000;
                var since = DateTime.UtcNow;
                while (have < bytes)
                {
                    int n = await ReadAsync(opened.Stream, buffer, token).ConfigureAwait(false);
                    if (n == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, (int)Math.Min(n, bytes - have)), token).ConfigureAwait(false);
                    have += n;
                    received?.Invoke(n);
                    if (progress != null && (have >= nextNote || (DateTime.UtcNow - since).TotalSeconds >= 15))
                    {
                        progress($"{name}: {have / 1e6:F0} of {bytes / 1e6:F0} MB");
                        nextNote = have + 32_000_000;
                        since = DateTime.UtcNow;
                    }
                }
                // (EA-0092 A10) on the disk before it is checked and renamed: after a power cut a renamed file could hold zeros
                output.Flush(true);
            }
            long length = new FileInfo(part).Length;
            if (length < bytes) throw new IOException($"the download stopped at {length:N0} of {bytes:N0} bytes");
            if (length != bytes || !FileMatches(part, bytes, sha256))
            {
                File.Delete(part);
                throw new InvalidDataException("the download does not match the release's SHA-256; it is discarded");
            }
            File.Move(part, target, true);
        }

        /// <summary>
        /// A whole file as <see cref="FileOnceAsync"/>, tried up to <paramref name="tries"/> times (each failure logged as
        /// "name: reason (try n of tries)"); OK, or the last failure. NotThere: the source answered that it doesn't have the
        /// file (<see cref="NotThereException"/>, not tried again); every other failure may pass (review A3).
        /// </summary>
        public async Task<(bool Ok, string Error, bool NotThere)> FileAsync(string source, string name, string target, long bytes, string sha256, int tries = FileTries,
            Action<string> log = null, Action<int> received = null, CancellationToken token = default)
        {
            string error = null;
            bool ok;
            try
            {
                ok = await RetryAsync(tries, async _ =>
                    {
                        await FileOnceAsync(source, name, target, bytes, sha256, log, received, token).ConfigureAwait(false);
                        return true;
                    }, a => TimeSpan.FromMilliseconds(1500 * a * a), (a, e) =>
                    {
                        error = e.Message;
                        log?.Invoke($"{name}: {e.Message} (try {a} of {tries})");
                    }, token).ConfigureAwait(false);
            }
            catch (NotThereException e)
            {
                log?.Invoke(e.Message);
                return (false, e.Message, true);
            }
            return (ok, ok ? null : error ?? "not fetched", false);
        }
    }
}
