using EarthMap.Core;

/// <summary>
/// (EA-0088) EarthElevationSystem's start-up policy for the release files (Core.ReleaseFiles): a file used as it is, fetched
/// again when it isn't the one this build reads, gone without for good only when the source answers that it lacks it and the
/// world never generated with it, the server stopped otherwise; and EA-0099's wait before a square that couldn't be had is
/// tried again (Core.UnavailableSquares).
/// </summary>
static class ReleaseFileChecks
{
    public static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "earth-release-files-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        try { Files(folder, check); }
        finally { Directory.Delete(folder, true); }
        Squares(check);
    }

    static void Files(string folder, Action<bool, string> check)
    {
        var use = new ReleaseFileUse("Earth lakes", "lakes-on", "lakes-off", "this world's lakes come from the outlines", "the grid's lakes");
        var save = new Dictionary<string, byte[]>();
        var notes = new List<string>();
        var warnings = new List<string>();
        string repo = "owner/earth-data";
        int fetches = 0;
        ReleaseFiles With(ReleaseFiles.Fetch fetch) => new(k => save.TryGetValue(k, out var v) ? v : null, (k, v) => save[k] = v, folder, () => repo, fetch, notes.Add, warnings.Add);
        // a file this build reads is 3 bytes long
        static bool Good(byte[] f, out string why) { why = f == null ? "missing" : f.Length == 3 ? null : "not this build's"; return f?.Length == 3; }
        ReleaseFiles.Fetch Writes(byte[] content) => (string name, string path, long bytes, string sha256, out string error, out bool lacking) =>
        {
            fetches++; File.WriteAllBytes(path, content); error = null; lacking = false; return true;
        };
        ReleaseFiles.Fetch Fails(bool lack) => (string name, string path, long bytes, string sha256, out string error, out bool lacking) =>
        {
            fetches++; error = lack ? "not in the release" : "the network is down"; lacking = lack; return false;
        };

        File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[3]);
        var got = With(Fails(false)).Obtain(use, "a.bin", 3, "", Good, out string why, out bool lacking);
        check(got?.Length == 3 && fetches == 0, "release files: a file this build reads is used as it is, nothing fetched");
        File.WriteAllBytes(Path.Combine(folder, "b.bin"), new byte[5]);
        got = With(Writes(new byte[3])).Obtain(use, "b.bin", 3, "", Good, out why, out lacking);
        check(got?.Length == 3 && fetches == 1 && warnings.Any(w => w.Contains("isn't the file this build reads")), "release files: a file that isn't this build's is fetched again, said");
        repo = null;
        got = With(Writes(new byte[3])).Obtain(use, "c.bin", 3, "", Good, out why, out lacking);
        check(got == null && lacking && why.Contains("ElevationRepo") && fetches == 1, "release files: without a release set the file is lacking for good, nothing fetched");
        repo = "owner/earth-data";
        got = With(Fails(true)).Obtain(use, "d.bin", 3, "", Good, out why, out lacking);
        check(got == null && lacking && why == "not in the release", "release files: the release's answer that it hasn't the file is lacking for good");
        got = With(Fails(false)).Obtain(use, "e.bin", 3, "", Good, out why, out lacking);
        check(got == null && !lacking && why == "the network is down", "release files: a network error is not lacking (it may pass)");
        got = With(Writes(new byte[4])).Obtain(use, "f.bin", 3, "", Good, out why, out lacking);
        check(got == null && lacking, "release files: a fetched file that still isn't this build's is lacking for good");

        var files = With(Fails(false));
        check(!files.KeptWithout(use, "g.bin"), "release files: a world without the off key isn't kept without the file");
        string stop = files.Unavailable(use, "g.bin", "the network is down", false, repo);
        check(stop != null && stop.Contains("could not be fetched") && !save.ContainsKey("lakes-off"), "release files: a failure that may pass stops the server, nothing saved");
        stop = files.Unavailable(use, "g.bin", "not in the release", true, repo);
        check(stop == null && save.TryGetValue("lakes-off", out var off) && System.Text.Encoding.UTF8.GetString(off) == "not in the release"
            && warnings.Any(w => w.Contains("for good")), "release files: a world that never used the file goes without it for good when the source lacks it");
        notes.Clear();
        check(files.KeptWithout(use, "g.bin") && notes.Any(n => n.Contains("the grid's lakes")), "release files: from then on the world is kept without it, said");

        save.Clear();
        files.Used(use);
        check(save.TryGetValue("lakes-on", out var on) && on.SequenceEqual(new byte[] { 1 }), "release files: a world that generates with the file is marked");
        save["lakes-on"] = new byte[] { 9 };
        files.Used(use);
        check(save["lakes-on"][0] == 9, "release files: the mark is written once");
        stop = files.Unavailable(use, "g.bin", "not in the release", true, repo);
        check(stop != null && stop.Contains("can't be had now") && stop.Contains(folder) && !save.ContainsKey("lakes-off"),
            "release files: a world that generated with the file stops when it lacks it now (its new chunks would differ)");
        stop = files.Unavailable(use, "g.bin", "the network is down", false, repo);
        check(stop != null && !save.ContainsKey("lakes-off"), "release files: and when the network fails");
    }

    static void Squares(Action<bool, string> check)
    {
        var squares = new UnavailableSquares();
        long t0 = 1_000_000, minute = 60_000;
        check(!squares.Recently(7, t0), "squares: one that never failed is tried");
        check(squares.Failed(7, t0) && squares.Recently(7, t0 + 4 * minute) && !squares.Recently(7, t0 + 5 * minute),
            "squares: the first failure is told, and the square waits 5 minutes");
        check(!squares.Failed(7, t0 + 2 * minute) && !squares.Recently(7, t0 + 5 * minute),
            "squares: a chunk failing within the wait neither pushes it out nor is told again");
        check(!squares.Failed(7, t0 + 6 * minute) && squares.Recently(7, t0 + 10 * minute) && !squares.Recently(7, t0 + 11 * minute),
            "squares: a failure after the wait starts a new one, untold within 10 minutes");
        check(squares.Failed(7, t0 + 10 * minute), "squares: told again after 10 minutes");
        check(!squares.Recently(8, t0 + minute) && squares.Failed(8, t0 + minute), "squares: each square on its own");
        check(UnavailableSquares.RetryAfter == TimeSpan.FromMinutes(5) && UnavailableSquares.TellEvery == TimeSpan.FromMinutes(10),
            "squares: 5 minutes before a retry, 10 between tellings (EA-0099 as decided)");
    }
}
