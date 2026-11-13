using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarthMap.Core;

// Golden output of the BASELINE (0.12.2, commit db80766) v1 geology code paths.
// Emulates EarthGeology.SampleColumn / ApplyStrata / RegionalRock / ApplyCover exactly as written in the baseline.
static class Baseline
{
    public static int Main(string[] args)
    {
        string root = args[0];
        string outFile = args[1];
        var data = EarthData.LoadFromFolder(root, true);
        string grid = File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-grids-v1.json"));
        string rules = File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-rules-v1.json"));
        var cat = new RegionalGeologyCatalog(grid, rules);
        var result = new Dictionary<string, object>();
        // all cell centres
        using (var h = new Hasher())
        {
            for (int z = 0; z < 360; z++) for (int x = 0; x < 720; x++)
            {
                var s = cat.At(89.75 - z * .5, -179.75 + x * .5);
                h.Sample(s);
            }
            result["cells"] = h.Done();
        }
        foreach (var w in GoldenWorlds.All)
        {
            var model = w.Build(data);
            var geo = model.Geometry;
            int mapHeight = w.MapHeight;
            var hw = new Hasher(); var hs = new Hasher(); var hst = new Hasher(); var hr = new Hasher(); var hc = new Hasher();
            int regional = 0, strataBlocks = 0, covers = 0, ice = 0;
            for (int i = 0; i < w.Count; i++)
            {
                GoldenWorlds.Position(w, i, out int x, out int z);
                var warp = GeologyTransitions.Warp(x, z, w.Seed, w.Width);
                hw.D(warp.X); hw.D(warp.Z);
                // SampleColumn (baseline)
                var original = geo.ToGeo(x, z);
                RegionalGeologyCatalog.Sample sample;
                if (original.InGutter || original.BeyondPole) sample = cat.At(original.Lat, original.Lon, true);
                else
                {
                    var blended = geo.ToGeo(warp.X, warp.Z);
                    sample = blended.InGutter || blended.BeyondPole ? cat.At(original.Lat, original.Lon) : cat.At(blended.Lat, blended.Lon);
                }
                hs.I(x); hs.I(z); hs.Sample(sample);
                var c = model.GetColumn(x, z);
                int heightmap = Math.Clamp(c.Top, 1, mapHeight - 2);
                // ApplyStrata (baseline)
                if (sample.HasRegionalBedrock)
                {
                    int top = Math.Min(heightmap, c.IceBottom - 1);
                    top = Math.Min(top, mapHeight - 1);
                    if (top >= 1)
                    {
                        regional++;
                        if (c.IceBottom <= c.Top) ice++;
                        hst.I(x); hst.I(z); hst.I(top);
                        for (int y = top; y > 0; y--) { hst.S(cat.RockAt(sample, top - y, top, original.Lat, original.Lon, w.Seed)); strataBlocks++; }
                    }
                    // RegionalRock (baseline) for ice-free columns, caller passes heightmap top
                    if (c.IceBottom > c.Top)
                    {
                        hr.I(x); hr.I(z);
                        foreach (int depth in GoldenWorlds.Depths)
                        {
                            if (depth > heightmap - 1) continue;
                            hr.I(depth); hr.S(cat.RockAt(sample, depth, heightmap, original.Lat, original.Lon, w.Seed));
                        }
                    }
                }
                // ApplyCover (baseline), assuming a valid top rock below the soil
                int kind = 0, cap = -1;
                if (!sample.OutsideEarth)
                {
                    bool dune = sample.Sediment == 11;
                    bool coarse = sample.Sediment is 2 or 15 or 19;
                    if ((dune || coarse) && heightmap >= 4 && !(c.IceBottom <= c.Top || c.Water != TerrainModel.WaterNone || c.Beach))
                    {
                        var climate = model.Data.SampleClimate(original.Lat, original.Lon);
                        cap = dune && climate.Precip < 600 ? 0 : 2;
                        kind = dune ? 1 : 2;
                        covers++;
                    }
                }
                hc.I(x); hc.I(z); hc.I(kind); hc.I(cap);
            }
            result[w.Name] = new Dictionary<string, object>
            {
                ["warp"] = hw.Done(), ["sample"] = hs.Done(), ["strata"] = hst.Done(), ["regionalRock"] = hr.Done(), ["cover"] = hc.Done(),
                ["regionalColumns"] = regional, ["strataBlocks"] = strataBlocks, ["coverColumns"] = covers, ["iceColumns"] = ice,
            };
            Console.WriteLine($"{w.Name}: regional={regional} blocks={strataBlocks} covers={covers} ice={ice}");
        }
        File.WriteAllText(outFile, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine(File.ReadAllText(outFile));
        return 0;
    }
}
