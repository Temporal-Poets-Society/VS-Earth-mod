using EarthMap.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Geology rules v2 pinned (EA-0166, the rocks audit's R7): worlds made with v2 keep v2's rock in their new chunks, so any
/// change to v2's data or code fails here. Like the v1 golden, on fixed layouts (today's sea level 110 at heights 256, 384
/// and 1024); a layout change agreed elsewhere rewrites the per-world hashes with GOLDEN_V2_REWRITE=1 after this test
/// passed on the old layout. The cell lookup and the rules' fingerprint do not depend on the layout and are never rewritten.
/// </summary>
static partial class Program
{
    static readonly GoldenWorld[] V2Worlds =
    {
        new GoldenWorld { Name = "v2-h384-sea110", W = 512000, L = 204800, MapHeight = 384, Sea = 110, RingStart = 51200, Ring = 409600, Seed = 12345, Width = 96, Count = 1500 },
        new GoldenWorld { Name = "v2-h1024-sea110", W = 512000, L = 204800, MapHeight = 1024, Sea = 110, RingStart = 51200, Ring = 409600, Seed = -98765, Width = 96, Count = 1000 },
        new GoldenWorld { Name = "v2-h256-sea110", W = 256000, L = 102400, MapHeight = 256, Sea = 110, RingStart = 25600, Ring = 204800, Seed = 424242, Width = 48, Count = 1500 },
    };

    static void V2Golden()
    {
        string goldenPath = Path.Combine(root, "tests/RegionalGeologyTests/golden-v2.json");
        bool write = Environment.GetEnvironmentVariable("GOLDEN_V2_REWRITE") == "1" || !File.Exists(goldenPath);
        var golden = File.Exists(goldenPath) ? JsonDocument.Parse(File.ReadAllText(goldenPath)).RootElement : default;
        string cells;
        using (var h = new Hasher())
        {
            for (int z = 0; z < 360; z++) for (int x = 0; x < 720; x++) h.Sample(cat2.At(89.75 - z * .5, -179.75 + x * .5));
            cells = h.Done();
        }
        var output = new JsonObject { ["fingerprint"] = cat2.Fingerprint, ["cells"] = cells };
        if (!write)
        {
            Check(cat2.Fingerprint == golden.GetProperty("fingerprint").GetString(), "v2 rules unchanged (parsed fingerprint)");
            Check(cells == golden.GetProperty("cells").GetString(), "v2 categorical lookup at all 259200 cell centres unchanged");
        }
        var g2 = cat2.Generation;
        Span<int> bounds = stackalloc int[RegionalGeologyCatalog.MaxBeds];
        foreach (var w in V2Worlds)
        {
            var model = Model(w);
            var geo = model.Geometry;
            double cellBlocks = geo.BlocksPerDegreeZ * .5;
            var reference = new GeologyReference(model, w.Seed, cellBlocks, g2.DatumRadiusCells, g2.StrataThicknessPerSeaLevel, g2.DatumUndulation, g2.ThicknessVariation);
            var hs = new Hasher(); var hst = new Hasher();
            int regional = 0, blocks = 0;
            for (int i = 0; i < w.Count; i++)
            {
                GoldenWorlds.Position(w, i, out int x, out int z);
                var sample = RegionalGeologyWorld.Sample(cat2, geo, x, z, w.Seed, w.Width, cellBlocks, out _);
                hs.I(x); hs.I(z); hs.Sample(sample); hs.B(sample.Filled);
                if (!sample.HasRegionalBedrock) continue;
                var c = model.GetColumn(x, z);
                int top = Math.Min(Math.Min(Math.Clamp(c.Top, 1, w.MapHeight - 2), c.IceBottom - 1), w.MapHeight - 1);
                if (top < 1) continue;
                regional++;
                reference.At(x, z, out double datum, out double thickness);
                hst.I(x); hst.I(z); hst.I(top); hst.D(datum); hst.D(thickness);
                int beds = RegionalGeologyCatalog.ColumnV2(sample.Profile, datum, thickness, bounds), bed = 0;
                for (int y = top; y > 0; y--)
                {
                    bed = RegionalGeologyCatalog.BedV2(y, bounds, beds, bed);
                    hst.S(cat2.Materials[RegionalGeologyCatalog.MaterialOfBed(sample.Profile, bed)].Id); blocks++;
                }
            }
            string sampleHash = hs.Done(), strataHash = hst.Done();
            if (write)
            {
                output[w.Name] = new JsonObject { ["sample"] = sampleHash, ["strata"] = strataHash, ["regionalColumns"] = regional, ["strataBlocks"] = blocks };
                Info($"{w.Name}: golden written ({regional} regional columns, {blocks} strata blocks)");
                continue;
            }
            var g = golden.GetProperty(w.Name);
            Check(sampleHash == g.GetProperty("sample").GetString(), w.Name + ": v2 sample, profile and coastal fill identical");
            Check(strataHash == g.GetProperty("strata").GetString() && blocks == g.GetProperty("strataBlocks").GetInt32(),
                w.Name + $": v2 strata identical ({regional} columns, {blocks} blocks)");
        }
        if (write)
        {
            if (File.Exists(goldenPath))
            {
                // a layout rewrite keeps the layout-free parts as they were pinned
                output["fingerprint"] = golden.GetProperty("fingerprint").GetString();
                output["cells"] = golden.GetProperty("cells").GetString();
            }
            File.WriteAllText(goldenPath, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
            Info("golden-v2.json written");
        }
    }
}
