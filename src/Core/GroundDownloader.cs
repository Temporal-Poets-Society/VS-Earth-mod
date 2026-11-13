using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// The 30″ ground sets (soil groups, depth to bedrock, permafrost; 172 MB, too big for Earth's archives), published as
    /// release egts-v1 of Temporal-Poets-Society/earth-ground. They live in VintagestoryData/ModData/earth/ground, one copy per
    /// machine. Earth fetches them before a new ground-columns world generates its first chunk, so one world never mixes the
    /// 2′ and 30″ cover (global-design section 9). Each file is checked against the size and SHA-256 below before it is put in
    /// place; a broken download resumes from its .part file.
    /// </summary>
    public static class GroundDownloader
    {
        /// <summary>Where the sets come from: the release's download folder (a local folder holding the files also works).</summary>
        public const string Release = "https://github.com/Temporal-Poets-Society/earth-ground/releases/download/egts-v1/";
        /// <summary>EarthParams.GroundData of a world made with these sets; 1 = a world made with the 2′ sets only.</summary>
        public const int SetsVersion = 2, TwoMinuteOnly = 1;
        /// <summary>The folder under ModData/earth the rules list the sets in ("ground/global-…-30s.egts").</summary>
        public const string Subfolder = "ground";

        public sealed record Set(string Name, long Bytes, string Sha256);

        /// <summary>Release egts-v1 (README and manifest.json of Temporal-Poets-Society/earth-ground).</summary>
        public static readonly Set[] Sets =
        {
            new("global-soilgroup-30s.egts", 20_979_888, "b1b1f22d7c1d46a142a1ebdb86f802d4956213a4b26d61c033d0d214828089a8"),
            new("global-shangguan-30s.egts", 134_774_489, "d4c165f7b346aed9ba91f39778dda2f5c47962a857628dd81be8ee98282bc40d"),
            new("global-permafrost-30s.egts", 16_496_922, "ff9117624a5bdb6ce415832fd9634deeae1ce21ccaab2ed6dc0036dbae741739"),
        };

        public static long TotalBytes(IEnumerable<Set> sets = null) => (sets ?? Sets).Sum(s => s.Bytes);

        public static string PathOf(string dataFolder, Set s) => Path.Combine(dataFolder, Subfolder, s.Name);

        /// <summary>The sets missing from <paramref name="dataFolder"/> (ModData/earth) or of the wrong size; with
        /// <paramref name="hash"/>, also those whose SHA-256 differs (about a second for all three).</summary>
        public static List<Set> Missing(string dataFolder, bool hash, IEnumerable<Set> sets = null)
            => (sets ?? Sets).Where(s => !DataTransport.FileMatches(PathOf(dataFolder, s), s.Bytes, s.Sha256, hash)).ToList();

        /// <summary>
        /// Fetches every set that is missing or wrong from <paramref name="source"/> (a URL ending in '/' or a local folder)
        /// into <paramref name="dataFolder"/>/ground, blocking until done. True when every set is then present and checked.
        /// A file is written as name.part, resumed from where it broke (up to four tries each), checked and then moved into
        /// place, so a set is either whole or absent (DataTransport). Servers sharing the folder take turns
        /// (DataTransport.TakeTurnAsync).
        /// </summary>
        public static bool Ensure(string dataFolder, string source, Action<string> log, CancellationToken token = default, IEnumerable<Set> sets = null)
            => Get(dataFolder, source, log, token, sets) == Result.Here;

        public static async Task<bool> EnsureAsync(string dataFolder, string source, Action<string> log, CancellationToken token = default, IEnumerable<Set> sets = null)
            => await GetAsync(dataFolder, source, log, token, sets).ConfigureAwait(false) == Result.Here;

        /// <summary>
        /// How <see cref="Ensure"/> ended (review A3): Here, every set present and checked; Lacking, the source can't have them
        /// for good (none is set, or it answered that it doesn't have a set: HTTP 404, a folder without it), so a new world
        /// may decide for the 2′ sets; Failed, anything else (the network, a download that didn't check out, the disk), which
        /// may pass and must never become a world's choice for good.
        /// </summary>
        public enum Result { Here, Lacking, Failed }

        /// <summary><see cref="Ensure"/>, telling a source that lacks the sets from one that failed.</summary>
        public static Result Get(string dataFolder, string source, Action<string> log, CancellationToken token = default, IEnumerable<Set> sets = null)
            => Task.Run(() => GetAsync(dataFolder, source, log, token, sets), token).GetAwaiter().GetResult();

        public static async Task<Result> GetAsync(string dataFolder, string source, Action<string> log, CancellationToken token = default, IEnumerable<Set> sets = null)
        {
            log ??= _ => { };
            var todo = Missing(dataFolder, true, sets);
            if (todo.Count == 0) return Result.Here;
            if (string.IsNullOrWhiteSpace(source)) { log("no source set; the 30″ ground sets are not fetched."); return Result.Lacking; }
            // servers sharing the folder take turns (review A4), and look again once they have it
            using var turn = await DataTransport.TakeTurnAsync(Path.Combine(dataFolder, Subfolder), log, token).ConfigureAwait(false);
            todo = Missing(dataFolder, true, sets);
            if (todo.Count == 0) return Result.Here;
            log($"fetching {todo.Count} 30″ ground set(s), {TotalBytes(todo) / 1e6:F0} MB, from {source} into {Path.Combine(dataFolder, Subfolder)}.");
            using var transport = new DataTransport(TimeSpan.FromMinutes(2));
            bool failed = false, lacking = false;
            foreach (var s in todo)
            {
                token.ThrowIfCancellationRequested();
                var (ok, _, notThere) = await transport.FileAsync(source, s.Name, PathOf(dataFolder, s), s.Bytes, s.Sha256, DataTransport.FileTries, log, null, token).ConfigureAwait(false);
                if (ok) log($"{s.Name}: {s.Bytes / 1e6:F1} MB, checked.");
                else
                {
                    log($"{s.Name} could not be fetched{(notThere ? " (the source doesn't have it)" : "")}.");
                    if (notThere) lacking = true; else failed = true;
                }
            }
            // a set the source doesn't have can't be had however long one waits: that decides
            return lacking ? Result.Lacking : failed ? Result.Failed : Result.Here;
        }
    }
}
