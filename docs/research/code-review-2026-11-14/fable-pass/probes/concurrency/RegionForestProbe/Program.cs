using System;
using System.IO;
using System.Threading;
using EarthMap.Core;

// Probe: a map region's forest potential (EarthWorldGen.OnMapRegionGen -> RegionMaps.ForestAndShrubs -> TerrainModel.ForestPotential)
// on a thread that may not fetch GLO-90 tiles (the state of the generating thread when the first region group of a session is made)
// against a thread that may (every later region). Same model, same positions.
static class Probe
{
    static int Main(string[] args)
    {
        string root = args[0], store = args[1];
        var data = EarthData.LoadFromFolder(Path.Combine(root, "earthdata"), true);
        // the test suite's standard layout (about 1:200)
        var geo = EarthLayout.Build(new LayoutInput { WorldWidth = 256000, CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = 51200, CwPoleDistance = 51152, CwBandInner = 608, GutterMargin = 512 }, data, out _);
        var vertical = VerticalScale.ForWorld(384, 165);
        string folder = Path.Combine(Path.GetTempPath(), "earth-probe-forest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        byte[] list = File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin"));
        int fetched = 0;
        TerrainModel Make()
        {
            var m = new TerrainModel(data, geo, vertical, 290142474) { LocalAlpineEcology = true };
            string zones = Path.Combine(root, "assets", "earth", "config", "zones.json");
            if (File.Exists(zones)) m.Zones = new ClimateZones(File.ReadAllText(zones), true);
            var fine = new FineElevation(folder, list);
            fine.Fetch = (latS, lonW) =>
            {
                string name = DemTiles.ShortName(latS, lonW) + FineElevation.FileExtension, from = Path.Combine(store, name);
                if (!File.Exists(from)) return false;
                File.Copy(from, Path.Combine(folder, name), true);
                Interlocked.Increment(ref fetched);
                return true;
            };
            m.Fine = fine; m.FineRules = 5;
            return m;
        }
        foreach (var (name, lat, lon) in new[] { ("Mont Blanc", 45.83, 6.86), ("Tatra", 49.2, 20.0), ("Riga (flat)", 56.95, 24.1), ("Caucasus", 43.0, 43.0) })
        {
            foreach (var f in Directory.GetFiles(folder)) File.Delete(f);
            geo.FromGeo(lat, lon, out double cx, out double cz);
            int regionX = (int)Math.Floor(cx / 512), regionZ = (int)Math.Floor(cz / 512);
            const int size = 33;          // a 512-block region sampled every 16 blocks, as a forest map's pixels
            double[] Potentials(bool fetch, out int missed)
            {
                var m = Make();
                var p = new double[size * size];
                int miss = 0;
                var t = new Thread(() =>
                {
                    FineElevation.FetchAllowed = fetch;
                    int before = FineElevation.MissedReads;
                    for (int j = 0; j < size; j++)
                        for (int i = 0; i < size; i++)
                            p[j * size + i] = m.ForestPotential(regionX * 512 + i * 16, regionZ * 512 + j * 16);
                    miss = FineElevation.MissedReads - before;
                });
                t.Start(); t.Join();
                missed = miss;
                return p;
            }
            // 1. the first region group of a session: the generating thread has run no terrain pass yet (FetchAllowed false),
            //    the square's tile is not on this machine
            var first = Potentials(false, out int missedFirst);
            // 2. any later region: the thread's FetchAllowed is on since its first terrain pass
            var later = Potentials(true, out int missedLater);
            int differ = 0, bytes = 0, big = 0; double max = 0;
            for (int k = 0; k < first.Length; k++)
            {
                double d = Math.Abs(first[k] - later[k]);
                if (d > 1e-9) differ++;
                max = Math.Max(max, d);
                // the forest map's byte as ForestAndShrubs writes it, vanilla's noise value taken as 128
                int a = (int)Math.Clamp(first[k] * (110 + 0.6 * 128), 0, 255), b = (int)Math.Clamp(later[k] * (110 + 0.6 * 128), 0, 255);
                if (a != b) bytes++;
                if (Math.Abs(a - b) >= 32) big++;
            }
            Console.WriteLine("{0}: region ({1},{2}), {3} pixels: no-fetch thread missed {4} reads, fetching thread {5} (tiles fetched so far {6}); " +
                "{7} potentials differ (largest {8:0.000}), {9} forest map bytes differ, {10} by 32 or more of 255",
                name, regionX, regionZ, first.Length, missedFirst, missedLater, fetched, differ, max, bytes, big);
        }
        try { Directory.Delete(folder, true); } catch (IOException) { }
        return 0;
    }
}
