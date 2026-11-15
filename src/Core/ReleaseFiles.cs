using System;
using System.Collections.Concurrent;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// How a world uses one of the release files that come with the GLO-90 heights (0053 items 17-19, decision 0069): its save
    /// keys and what it has without the file. A world whose source answered that it lacks the file when the world first needed
    /// it goes without it for good (OffKey), so its chunks match each other; any other failure, or a world that has generated
    /// with it (OnKey), stops the server (review A3, <see cref="ReleaseFiles.Unavailable"/>).
    /// </summary>
    public sealed record ReleaseFileUse(string Label, string OnKey, string OffKey, string With, string Without);

    /// <summary>
    /// The start-up policy for the release files (EarthElevationSystem; EA-0088, D-R5 of the 14 November 2026 code review's
    /// Fable pass): whether a world uses a file, goes without it for good, or must stop, and fetching it when it is missing or
    /// not the one this build reads. Off the game API: the save's data, the fetch and the log lines are given to it, so
    /// tests/ServerToolsTests runs every branch.
    /// </summary>
    public sealed class ReleaseFiles
    {
        /// <summary>Whether a file's bytes are the ones this build reads (each data class's IsExpected), and if not why.</summary>
        public delegate bool FileCheck(byte[] file, out string why);
        /// <summary>The release's file to <paramref name="path"/> (DemDownloader.FetchFile); <paramref name="lacking"/>: the
        /// release answered that it doesn't have it.</summary>
        public delegate bool Fetch(string name, string path, long bytes, string sha256, out string error, out bool lacking);

        readonly Func<string, byte[]> getData;
        readonly Action<string, byte[]> storeData;
        readonly string folder;
        readonly Func<string> repo;
        readonly Fetch fetch;
        readonly Action<string> notify, warn;

        /// <param name="getData">The save's data by key (null: none).</param>
        /// <param name="folder">The GLO-90 folder the files are kept in.</param>
        /// <param name="repo">The data release files are fetched from (null: none set).</param>
        public ReleaseFiles(Func<string, byte[]> getData, Action<string, byte[]> storeData, string folder, Func<string> repo, Fetch fetch,
            Action<string> notify, Action<string> warn)
        {
            this.getData = getData; this.storeData = storeData; this.folder = folder; this.repo = repo; this.fetch = fetch;
            this.notify = notify; this.warn = warn;
        }

        /// <summary>Whether this world goes without the file for good (said).</summary>
        public bool KeptWithout(ReleaseFileUse use, string name)
        {
            if (getData(use.OffKey) == null) return false;
            notify(string.Format("{0}: this world keeps {1} ({2} couldn't be used when it first needed it).", use.Label, use.Without, name));
            return true;
        }

        /// <summary>
        /// A release file from the folder, fetched from the data release (blocking: before any chunk) when it is missing or not
        /// the one this build reads; null, with the reason, when it can't be had. <paramref name="lacking"/>: whether that is
        /// for good (review A3): no release is set, the release answered that it doesn't have the file, or the file it has
        /// isn't one this build reads; not a network error or a download that didn't check out.
        /// </summary>
        public byte[] Obtain(ReleaseFileUse use, string name, long bytes, string sha256, FileCheck check, out string why, out bool lacking)
        {
            lacking = false;
            string path = Path.Combine(folder, name);
            byte[] file = null;
            try { if (File.Exists(path)) file = File.ReadAllBytes(path); } catch (Exception e) { warn(string.Format("{0}: {1}: {2}", use.Label, path, e.Message)); }
            if (check(file, out why)) return file;
            if (file != null) warn(string.Format("{0}: {1} isn't the file this build reads ({2}); fetching it again.", use.Label, path, why));
            string from = repo();
            if (from == null) { why = "no data release set (ElevationRepo)"; lacking = true; return null; }
            notify(string.Format("{0}: fetching {1} ({2:F1} MB) from {3}.", use.Label, name, bytes / 1e6, from));
            if (!fetch(name, path, bytes, sha256, out string error, out lacking)) { why = error; return null; }
            try { file = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = e.Message; return null; }
            if (check(file, out why)) return file;
            lacking = true;   // fetched and checked against the release, yet not a file this build reads
            return null;
        }

        /// <summary>
        /// The file can't be had or used. A world that never generated with it goes without it for good only when that is the
        /// source's answer (<paramref name="lacking"/>; its chunks then all match): stored, said, and null returned. Otherwise
        /// the message the server stops with (the caller stops it, nothing saved): a failure that may pass (the network) must
        /// not become the world's choice for good, and a world that has generated with the file must not go on without it
        /// (its new chunks would differ from the rest).
        /// </summary>
        /// <param name="repoName">The release named in the message.</param>
        public string Unavailable(ReleaseFileUse use, string name, string why, bool lacking, string repoName)
        {
            bool used = getData(use.OnKey) != null;
            if (!used && lacking)
            {
                storeData(use.OffKey, System.Text.Encoding.UTF8.GetBytes(why ?? "missing"));
                warn(string.Format("{0}: {1} can't be used ({2}); this world keeps {3} for good, so its chunks match.", use.Label, name, why, use.Without));
                return null;
            }
            return used
                ? string.Format("{0}: {1}, but {2} can't be had now ({3}). The server stops, so that no chunk of this world gets {4}; put the file in {5} " +
                    "(from {6}), or let the server reach it, and start again.", use.Label, use.With, name, why, use.Without, folder, repoName)
                : string.Format("{0}: {1} could not be fetched ({2}) while the world starts. The server stops (nothing is saved), so that a failure " +
                    "that may pass doesn't leave this world with {3} for good; start again once the server reaches {4}, or put the file in {5}.",
                    use.Label, name, why, use.Without, repoName, folder);
        }

        /// <summary>The world generates with the file from now on.</summary>
        public void Used(ReleaseFileUse use)
        {
            if (getData(use.OnKey) == null) storeData(use.OnKey, new byte[] { 1 });
        }
    }

    /// <summary>
    /// EA-0099 (the Fable pass's FP-03): the GLO-90 squares that couldn't be fetched while the game runs. Their chunks are not
    /// made; a square is tried again for a chunk once <see cref="RetryAfter"/> has passed since a fetch failed (a chunk that
    /// only found it recently failed doesn't push that out), and the players are told of a square at most every
    /// <see cref="TellEvery"/>. Times are milliseconds (Environment.TickCount64). Off the game API (EA-0088).
    /// </summary>
    public sealed class UnavailableSquares
    {
        public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan TellEvery = TimeSpan.FromMinutes(10);
        // square key -> when it may be fetched again for a chunk; and when the players were last told of it
        readonly ConcurrentDictionary<int, long> until = new(), toldAt = new();

        /// <summary>Whether the square's fetch failed less than <see cref="RetryAfter"/> ago.</summary>
        public bool Recently(int key, long now) => until.TryGetValue(key, out long u) && now < u;

        /// <summary>The square can't be had now: its retry wait starts, unless one is running; true when the players are to be
        /// told (the first time, and again after <see cref="TellEvery"/>).</summary>
        public bool Failed(int key, long now)
        {
            if (!Recently(key, now)) until[key] = now + (long)RetryAfter.TotalMilliseconds;
            if (toldAt.TryGetValue(key, out long told) && now - told < (long)TellEvery.TotalMilliseconds) return false;
            toldAt[key] = now;
            return true;
        }
    }
}
