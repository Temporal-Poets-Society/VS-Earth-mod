using EarthMap.Core;

// Decision 0051: every GLO-90 tile in a local folder (the published Copernicus_DSM_COG_30_*.tif names, e.g. from
// tools/fetch_glo90.py) converted into Earth's tile store, as the game's "Download all" would from the bucket. Tiles
// already in the store are skipped, so it can be stopped and run again.
//   Glo90Convert <source folder> <data/glo90-tiles.bin> <store, e.g. %APPDATA%\VintagestoryData\ModData\earth\glo90> [workers]
if (args.Length < 3)
{
    Console.WriteLine("usage: Glo90Convert <source folder> <glo90-tiles.bin> <store folder> [workers]");
    return 2;
}
int workers = args.Length > 3 ? int.Parse(args[3]) : Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
var fine = new FineElevation(args[2], File.ReadAllBytes(args[1]));
using var dl = new DemDownloader(fine, args[0], workers) { Log = s => Console.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + s) };
var started = DateTime.UtcNow;
dl.StartAll(workers);
var b = dl.All;
Console.WriteLine($"{b.Total} source tiles, {b.Skipped} already in {args[2]}; converting with {workers} workers");
while (b.Running)
{
    Thread.Sleep(30000);
    double min = (DateTime.UtcNow - started).TotalMinutes;
    int left = b.Total - b.Skipped - b.Done - b.Failed;
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} {b.Done} converted, {b.Failed} failed, {left} to go; read {dl.BytesDownloaded / 1e9:F2} GB, " +
        $"kept {dl.BytesStored / 1e9:F2} GB ({(dl.BytesDownloaded > 0 ? 100.0 * dl.BytesStored / dl.BytesDownloaded : 0):F1} %); {min:F1} min");
}
Thread.Sleep(500);
Console.WriteLine($"done: {b.Done} converted, {b.Failed} failed, {b.Skipped} skipped; read {dl.BytesDownloaded / 1e9:F2} GB, kept {dl.BytesStored / 1e9:F2} GB " +
    $"in {(DateTime.UtcNow - started).TotalMinutes:F1} min");
return b.Failed > 0 ? 1 : 0;
