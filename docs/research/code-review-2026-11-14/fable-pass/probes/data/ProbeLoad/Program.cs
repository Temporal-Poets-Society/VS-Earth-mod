using System.Diagnostics;
using EarthMap.Core;

static class P
{
    static long last;
    static void Line(string what, Stopwatch sw)
    {
        long ms = sw.ElapsedMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(false);
        GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true);
        long live = GC.GetTotalMemory(false);
        var info = GC.GetGCMemoryInfo();
        Console.WriteLine($"{what,-62} {ms,6} ms | live {live / 1e6,6:F0} MB ({(live - last) / 1e6,+7:+0;-0} MB) | LOH {info.GenerationInfo[3].SizeAfterBytes / 1e6,5:F0} MB | allocated in all {allocated / 1e6,6:F0} MB");
        last = live;
        sw.Restart();
    }
    static void Main(string[] args)
    {
        string root = args[0];
        var sw = Stopwatch.StartNew();
        Line("start", sw);
        if (args.Length > 1 && args[1] == "client")
        {
            var c = EarthData.LoadFromFolder(root, false);
            Line("client: load (climate.bin, 4 fixes, weather.bin, weatherground)", sw);
            c.SetClimateFix(EarthData.ClimateFixVersion); Line("client: climate fix 4 chosen (planes made)", sw);
            c.SetFineTemperature(true); Line("client: temperature.bin read", sw);
            c.SetFineRain(true); Line("client: rain.bin read", sw);
            c.SetPenmanAridity(true); Line("client: aridity.bin read", sw);
            var wg = c.WeatherGround; Line("client: weather ground made", sw);
            double s = 0;
            // a player's surroundings: 5 x 5 degrees sampled densely (the tiles a client keeps decoded)
            for (double la = 54; la <= 59; la += 0.01) for (double lo = 21; lo <= 26; lo += 0.01) s += c.SampleClimate(la, lo).Rain;
            Line("client: 250,000 climate samples over 5 x 5 degrees", sw);
            GC.KeepAlive(c); GC.KeepAlive(wg);
            return;
        }
        byte[] rivers = File.ReadAllBytes(Path.Combine(root, "earthdata", "rivers.bin"));
        Line("rivers.bin bytes read", sw);
        var list = RiverData.Read(rivers);
        long pts = 0; foreach (var r in list) pts += r.Lat.Length;
        Line($"RiverData.Read(rivers.bin): {list.Count} reaches, {pts} points", sw);
        list = null; rivers = null;
        Line("(released)", sw);
        var data = EarthData.LoadFromFolder(root, true);
        Line("server: EarthData.Load with terrain", sw);
        data.ReleaseRivers(); Line("server: rivers.bin's reaches released (EarthData.ReleaseRivers)", sw);
        data.SetClimateFix(EarthData.ClimateFixVersion); Line("server: climate fix 4", sw);
        data.SetFineTemperature(true); Line("server: temperature.bin", sw);
        data.SetFineRain(true); Line("server: rain.bin", sw);
        data.SetPenmanAridity(true); Line("server: aridity.bin", sw);
        data.SetSummits(true); Line("server: summits.bin", sw);
        data.SetGlaciers(true); Line("server: glaciers.bin", sw);
        data.SetSeaReferee(true); Line("server: sea referee", sw);
        data.SetIslandOverlay(true); Line("server: island overlay", sw);
        data.SetLakeOverlay(true); Line("server: lake overlay", sw);
        data.SetShoreOverlay(true); Line("server: shore overlay", sw);
        data.SetOutletOverlay(true); Line("server: outlet overlay", sw);
        var peaks = data.ReadPeaks(); Line($"server: peaks.json ({peaks?.Count})", sw);
        double sink = 0;
        for (double la = -88; la <= 88; la += 2.9) for (double lo = -179; lo <= 179; lo += 2.9) sink += data.SampleTerrain(la, lo).Elev;
        Line("server: terrain sampled every 2.9 degrees (tile cache full)", sw);
        GC.KeepAlive(data); GC.KeepAlive(peaks);
        var proc = Process.GetCurrentProcess();
        Console.WriteLine($"peak working set {proc.PeakWorkingSet64 / 1e6:F0} MB");
    }
}
