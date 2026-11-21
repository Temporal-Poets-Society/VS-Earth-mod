using System.Net;
using System.Net.Http.Headers;
using EarthMap.Core;

/// <summary>
/// EA-0191 (decision 0103): every data file this build pins is on the data release it is fetched from, with the pinned size.
/// A file the build reads that its release lacks makes a new world stop (ReleaseFiles.Unavailable); before 2.2.0-rc.9 it made
/// the world go without it for good. Asks each file's download address as a server does (DataTransport.Locate on the
/// release's download folder), for its first byte only, and reads the whole size from the answer.
///
/// Run before a release: <c>dotnet run -c Release --project tests\DataReleaseCheck</c>. Exit 0: every file is there with
/// its size; 1: a file is missing or has another size; 2: an address could not be asked (the network). The GLO-90 tiles
/// themselves are not asked one by one: egl-v1's manifest, which lists them, is (its size and SHA-256 are pinned).
/// </summary>
static class Program
{
    sealed record Pinned(string Release, string Name, long Bytes, string What);

    static async Task<int> Main()
    {
        var files = Pins();
        Console.WriteLine($"Earth data release check: {files.Count} pinned files on {files.Select(f => f.Release).Distinct().Count()} releases.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("earth-data-release-check/1");
        using var gate = new SemaphoreSlim(6);
        var answers = await Task.WhenAll(files.Select(async f =>
        {
            await gate.WaitAsync();
            try { return (f, await Ask(http, DataTransport.Locate(f.Release, f.Name))); }
            finally { gate.Release(); }
        }));
        int missing = 0, wrong = 0, failed = 0;
        foreach (var group in answers.GroupBy(a => a.f.Release))
        {
            Console.WriteLine();
            Console.WriteLine(group.Key);
            foreach (var (f, (status, bytes, error)) in group)
            {
                string verdict = status switch
                {
                    Status.There when bytes == f.Bytes => "ok",
                    Status.There => $"OTHER SIZE: {bytes:N0} on the release",
                    Status.Missing => "MISSING from the release",
                    _ => "NOT ASKED: " + error,
                };
                if (status == Status.There && bytes != f.Bytes) wrong++;
                else if (status == Status.Missing) missing++;
                else if (status == Status.Failed) failed++;
                Console.WriteLine($"  {f.Name,-34} {f.Bytes,14:N0}  {verdict,-30}  {f.What}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"{files.Count - missing - wrong - failed} of {files.Count} as pinned; {missing} missing, {wrong} with another size, {failed} not asked.");
        return missing + wrong > 0 ? 1 : failed > 0 ? 2 : 0;
    }

    /// <summary>The files this build pins, each with the release a server fetches it from (earth.json's defaults).</summary>
    static List<Pinned> Pins()
    {
        var list = new List<Pinned>();
        foreach (var f in EarthDataStore.Files) list.Add(new(EarthDataStore.Release, f.Name, f.Bytes, "data store (ModData/earth/data)"));
        if (SeasonData.Published) list.Add(new(EarthDataStore.Release, SeasonData.FileName, SeasonData.FileBytes, "monthly temperatures (data)"));
        foreach (var s in GroundDownloader.Sets) list.Add(new(GroundDownloader.Release, s.Name, s.Bytes, "30″ ground set"));
        foreach (var s in GroundDownloader.RockSets) list.Add(new(GroundDownloader.Release, s.Name, s.Bytes, "30″ rock set (geology rules 4)"));
        string glo = DemDownloader.Glo90Repo;
        list.Add(new(glo, DemDownloader.ManifestName, DemDownloader.Glo90ManifestBytes, "GLO-90 tiles' manifest"));
        // every version a world may read (older worlds keep theirs)
        for (int v = 1; v <= LakeOutlineData.FileVersion; v++) list.Add(new(glo, LakeOutlineData.FileNameFor(v), LakeOutlineData.FileBytesFor(v), $"lake outlines {v}"));
        for (int v = 1; v <= RiverOutlineData.FileVersion; v++) list.Add(new(glo, RiverOutlineData.FileName(v), RiverOutlineData.FileBytes(v), $"river outlines {v}"));
        for (int r = 1; r <= RiverFlowData.FileVersion; r++) list.Add(new(glo, RiverFlowData.FileNameFor(r), RiverFlowData.FileBytesFor(r), $"river flow rules {r}"));
        for (int r = 1; r <= DamRemovalData.NewestRules; r++) list.Add(new(glo, DamRemovalData.FileNameFor(r), DamRemovalData.FileBytesFor(r), $"dam rules {r}"));
        list.Add(new(glo, StreamData.FileName, StreamData.FileBytes, "streams layer"));
        // one line per file (versions that read the same file share it)
        return list.GroupBy(p => (p.Release, p.Name)).Select(g => g.First() with { What = string.Join(", ", g.Select(p => p.What)) }).ToList();
    }

    enum Status { There, Missing, Failed }

    /// <summary>The first byte of the file at <paramref name="url"/>: its whole size from Content-Range (or Content-Length when the
    /// server sends it all), Missing on 404.</summary>
    static async Task<(Status, long, string)> Ask(HttpClient http, string url)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, 0);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (resp.StatusCode == HttpStatusCode.NotFound) return (Status.Missing, -1, null);
                if (resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange?.Length is long total) return (Status.There, total, null);
                if (resp.StatusCode == HttpStatusCode.OK && resp.Content.Headers.ContentLength is long whole) return (Status.There, whole, null);
                if (attempt >= 3) return (Status.Failed, -1, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (attempt >= 3) return (Status.Failed, -1, e.Message);
            }
            await Task.Delay(2000 * attempt);
        }
    }
}
