using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EarthMap.Core;

// Probe: what a generation thread that waits for a GLO-90 tile gets when the downloader is disposed under it
// (EarthElevationSystem.Dispose -> DemDownloader.Dispose, which the game runs while the chunk thread may still be inside
// PrepareChunk / FineElevation.TileAt). A local server accepts the connection and never answers: a stalled download.
static class Probe
{
    static int Main(string[] args)
    {
        string root = args[0];
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var held = new System.Collections.Generic.List<TcpClient>();
        var accept = new Thread(() => { try { while (true) lock (held) held.Add(listener.AcceptTcpClient()); } catch (Exception) { } }) { IsBackground = true };
        accept.Start();

        string folder = Path.Combine(Path.GetTempPath(), "earth-probe-dispose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var fine = new FineElevation(folder, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var clock = Stopwatch.StartNew();
        var downloader = new DemDownloader(fine, "http://127.0.0.1:" + port, 3, null) { Log = s => log.Enqueue($"  [{clock.Elapsed.TotalSeconds:0.0} s] downloader: {s}") };
        // as EarthElevationSystem.AttachServer wires it for a running game (GenerationWait 2 minutes)
        fine.Fetch = (latS, lonW) => downloader.AwaitForGeneration(latS, lonW, downloader.FetchAsync(latS, lonW), TimeSpan.FromMinutes(2));
        fine.Lacks = downloader.Lacks;
        string fellBack = null;
        fine.FellBack = (a, o) => fellBack = "for good";
        fine.FellBackForSession = (a, o) => fellBack = "for this session";

        bool got = true; double height = double.NaN, returnedAt = 0;
        var generation = new Thread(() =>
        {
            FineElevation.FetchAllowed = true;                 // a thread that has run a terrain pass
            got = fine.TryHeight(45.83, 6.86, out height);     // Mont Blanc's square, not on this machine
            returnedAt = clock.Elapsed.TotalSeconds;
        });
        generation.Start();
        Thread.Sleep(3000);
        Console.WriteLine("[{0:0.0} s] the generation thread is {1} for the tile; the downloader is disposed now (the mod's Dispose)", clock.Elapsed.TotalSeconds,
            generation.IsAlive ? "still waiting" : "no longer waiting");
        double disposedAt = clock.Elapsed.TotalSeconds;
        downloader.Dispose();
        bool ended = generation.Join(60000);
        while (log.TryDequeue(out string line)) Console.WriteLine(line);
        Console.WriteLine(ended
            ? $"[{returnedAt:0.0} s] the wait ended {returnedAt - disposedAt:0.0} s after the dispose: TryHeight {(got ? "gave " + height : "gave no height (the caller takes the built-in one)")}; " +
              $"the square fell back {fellBack ?? "not at all"}; IsFallback {fine.IsFallback(45, 6)}"
            : "the generation thread was still waiting 60 s after the dispose");
        listener.Stop();
        try { Directory.Delete(folder, true); } catch (IOException) { }
        return 0;
    }
}
