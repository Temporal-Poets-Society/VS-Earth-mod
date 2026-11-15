using EarthMap;
using EarthMap.Core;
using ProtoBuf;
using System.Text.Json;
using Vintagestory.API.Datastructures;

static class SummitChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        EarthParams Saved(EarthParams p)
        {
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, p); stream.Position = 0;
            return Serializer.Deserialize<EarthParams>(stream);
        }
        using var menu = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json")));
        var option = menu.RootElement.GetProperty("worldConfigAttributes").EnumerateArray()
            .Single(a => a.GetProperty("code").GetString() == EarthWorldOptions.SummitKey);
        string[] choices = { "off", "1000", "2000", "3000", "4000", "5000", "6000", "7000", "8000" };
        check(option.GetProperty("values").EnumerateArray().Select(v => v.GetString()).SequenceEqual(choices), "Summit menu: Off then 1 km steps");
        check(option.GetProperty("default").GetString() == "3000", "Summit menu defaults to 3000 m");
        foreach (string choice in choices)
        {
            var wc = new TreeAttribute(); wc.SetString(EarthWorldOptions.SummitKey, choice);
            var p = ParamsBuilderChecks.NewParams(wc, new EarthMapConfig());
            p = Saved(p);
            int expected = choice == "off" ? -1 : int.Parse(choice);
            check(p.SummitMinElevation == expected, "Summit choice survives protobuf: " + choice);
            wc.SetString(EarthWorldOptions.SummitKey, "8000");
            EarthWorldOptions.Runtime(new EarthMapConfig(), p);
            check(p.SummitMinElevation == expected, "Summit choice stays frozen after creation: " + choice);
        }
        foreach (string invalid in new[] { "", "bad", "-1", "0", "999", "3001", "3000.5", "9000" })
        {
            var wc = new TreeAttribute(); wc.SetString(EarthWorldOptions.SummitKey, invalid);
            check(EarthWorldOptions.SummitMinElevation(wc) == 3000, "Invalid summit choice uses 3000 m: " + invalid);
        }
        check(EarthWorldOptions.SummitMinElevation(null) == 3000, "Missing creation option defaults to 3000 m");
        var legacy = Saved(new EarthParams { Summits = KnownSummits.Version });
        EarthWorldOptions.Runtime(new EarthMapConfig(), legacy);
        check(legacy.SummitMinElevation == 0, "Older saves without field 89 retain all surveyed peaks");

        var geo = new EarthGeometry { RingLength = 360000, BlocksPerDegreeZ = 1000, EquatorZ = 90000, SeamLon0 = -180 };
        geo.FromGeo(0, 0, out double x, out double z);
        PeakCatalog Catalog(params object[][] peaks) => new(JsonSerializer.SerializeToUtf8Bytes(new { version = 1, peaks }));
        var boundary = Catalog(new object[] { "At cutoff", 0, 0, 3000 });
        var below = Catalog(new object[] { "Below cutoff", 0, 0, 2999 });
        check(KnownSummits.Restore(geo, x, z, 1000, 3000, boundary, baseGround: (_, _) => 1000) > 2999.9,
            "A surveyed summit exactly at 3000 m is included");
        check(KnownSummits.Restore(geo, x, z, 1000, 3000, below, baseGround: (_, _) => 1000) == 1000,
            "A surveyed summit at 2999 m is excluded");
        check(KnownSummits.Restore(geo, x, z, 1000, -1, boundary, baseGround: (_, _) => throw new Exception("Off sampled peak base")) == 1000,
            "Off bypasses surveyed correction entirely");
        var neighbours = Catalog(new object[] { "Mountain", 0, 0, 4000 }, new object[] { "Hill", 0, .02, 2000 });
        check(KnownSummits.Restore(geo, x - 18, z, 1000, neighbours, baseGround: (_, _) => 1000) == 1000,
            "Legacy neighbouring hill narrows the mountain's support");
        check(KnownSummits.Restore(geo, x - 18, z, 1000, 3000, neighbours, baseGround: (_, _) => 1000) > 1000,
            "Excluded hills do not narrow a qualifying mountain's support");
        foreach (int rules in new[] { 2, 3 })
            check(KnownSummits.Restore(geo, x, z, 1000, neighbours, rules, (_, _) => 1000)
                == KnownSummits.Restore(geo, x, z, 1000, 0, neighbours, rules, (_, _) => 1000),
                "Existing Restore entry point retains rules " + rules);

        var data = EarthData.LoadFromFolder(root, true);
        var catalog = data.ReadPeaks();
        check(catalog != null && catalog.Count > 150000, "Real production catalogue used for terrain regression");
        TerrainModel Model(int minimum)
        {
            var p = Saved(new EarthParams
            {
                MapHeight = 512, SeaLevel = 110, TopY = 510, LandH0 = 1000, MaxDepth = 47, SeaD0 = 20,
                RingStart = 51200, RingLength = 409600, Wraps = true, EquatorZ = 102400,
                BlocksPerDegreeZ = 204800.0 / 180, SeamLon0 = -169.25, NoiseSeed = 386217, DetailScale = 1,
                Summits = KnownSummits.Version, SummitMinElevation = minimum, PeakCatalogSignature = catalog.Signature
            });
            var model = p.BuildModel(data);
            check(model.SummitMinElevation == minimum && model.Summits && data.HasSummits,
                "BuildModel retains threshold and finer terrain layer: " + minimum);
            return model;
        }
        var all = Model(0); var off = Model(-1); var at1k = Model(1000); var at3k = Model(3000); var at4k = Model(4000); var at8k = Model(8000);
        (double x, double z) Column(double lat, double lon)
        {
            all.Geometry.FromGeo(lat, lon, out double px, out double pz);
            return (Math.Round(px), Math.Round(pz));
        }
        foreach (var hill in new[] { ("Mount Wanniassa", -35.39114, 149.11641), ("Mount Taylor", -35.36511, 149.08454), ("Tuggeranong Hill", -35.45679, 149.1099) })
        {
            var c = Column(hill.Item2, hill.Item3);
            double natural = off.GetColumn(c.x, c.z).Elev, before = all.GetColumn(c.x, c.z).Elev;
            check(Math.Abs(before - natural) > 1, hill.Item1 + " reproduces existing surveyed correction");
            for (int dx = -12; dx <= 12; dx += 3) for (int dz = -12; dz <= 12; dz += 3)
            {
                double baseline = off.GetColumn(c.x + dx, c.z + dz).Elev;
                check(at3k.GetColumn(c.x + dx, c.z + dz).Elev == baseline && at1k.GetColumn(c.x + dx, c.z + dz).Elev == baseline,
                    hill.Item1 + " and surrounding slopes receive no below-cutoff correction");
            }
            Console.WriteLine($"{hill.Item1}: legacy {before:F1} m; correction off / 1000 / 3000 = {natural:F1} m");
        }
        var fuji = catalog.Near(35.3607, 138.72773).Single(p => p.Name == "Fujiyama");
        var f = Column(fuji.Lat, fuji.Lon);
        double fujiDefault = at3k.GetColumn(f.x, f.z).Elev, fujiOff = off.GetColumn(f.x, f.z).Elev;
        check(Math.Abs(fujiDefault - fuji.Height) < .01 && fujiDefault == all.GetColumn(f.x, f.z).Elev, "Fuji retains its 3776 m surveyed top at default cutoff");
        check(Math.Abs(fujiDefault - fujiOff) > 1 && at4k.GetColumn(f.x, f.z).Elev == fujiOff, "Fuji correction switches off at 4000 m");
        var everest = catalog.Near(27.98791, 86.92529).Single(p => p.Name == "Mount Everest");
        var e = Column(everest.Lat, everest.Lon);
        check(Math.Abs(at8k.GetColumn(e.x, e.z).Elev - everest.Height) < .01, "Everest remains corrected at 8000 m cutoff");
        Console.WriteLine($"Fuji: 3000 default {fujiDefault:F1} m; correction off / 4000 = {fujiOff:F1} m; Everest at 8000 cutoff = {at8k.GetColumn(e.x, e.z).Elev:F1} m");
    }
}
