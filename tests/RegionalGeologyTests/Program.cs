using EarthMap.Core;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Regional geology tests on the real shipped data and real Earth terrain.
//   dotnet run -c Release -- [repoRoot] [quick]
// "quick" skips the terrain-model sections (golden v1 output, eligibility rates, exposure, anti-grid).
static class Program
{
    static int passed, failed;
    static void Check(bool ok, string msg)
    {
        if (ok) passed++;
        else { failed++; Console.WriteLine("  FAIL " + msg); }
    }
    static void Info(string s) => Console.WriteLine("  " + s);
    static string root;
    static RegionalGeologyCatalog cat1, cat2;
    static string gridText, rules1Text, rules2Text;
    static EarthData data;

    static int Main(string[] args)
    {
        root = args.Length > 0 && !args[0].Equals("quick", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(args[0]) : FindRoot();
        bool quick = args.Any(a => a.Equals("quick", StringComparison.OrdinalIgnoreCase));
        string config = Path.Combine(root, "assets/earth/config");
        gridText = File.ReadAllText(Path.Combine(config, "geology-grids-v1.json"));
        rules1Text = File.ReadAllText(Path.Combine(config, "geology-rules-v1.json"));
        rules2Text = File.ReadAllText(Path.Combine(config, "geology-rules-v2.json"));
        cat1 = new RegionalGeologyCatalog(gridText, rules1Text);
        cat2 = new RegionalGeologyCatalog(gridText, rules2Text);
        var sw = Stopwatch.StartNew();
        Section("Data, versions and save safety", DataAndSaveSafety);
        Section("Material codes against installed vanilla blocks", VanillaMaterials);
        Section("Rules v2 categories (coastal fill, pyroclastics, chalk, preserved rocks)", V2Categories);
        Section("Surface resource rules (unit)", ResourceUnits);
        Section("v1 strata/cover per-column equivalence (synthetic)", V1ColumnEquivalence);
        if (!quick)
        {
            data = EarthData.LoadFromFolder(root, true);
            Section("v1 golden output against 0.12.2 baseline", V1Golden);
            Section("Clay/peat eligibility on real terrain", Eligibility);
            Section("v2 strata exposure and reference surface", V2Exposure);
            Section("v2 anti-grid and warp continuity", AntiGrid);
            Section("Threaded determinism", Threads);
            Section("Performance", Performance);
        }
        Console.WriteLine(failed == 0 ? $"PASS {passed} checks in {sw.Elapsed.TotalSeconds:0.0}s" : $"FAILED {failed} of {passed + failed} checks");
        return failed == 0 ? 0 : 1;
    }
    static void Section(string name, Action body)
    {
        Console.WriteLine("== " + name);
        try { body(); }
        catch (Exception e) { failed++; Console.WriteLine("  FAIL exception: " + e); }
    }
    static string FindRoot()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d != null && !File.Exists(Path.Combine(d.FullName, "EarthMap.csproj"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    // ------------------------------------------------------------------------------------------------ data

    static void DataAndSaveSafety()
    {
        Check(cat1.Version == 1 && cat2.Version == 2 && RegionalGeologyCatalog.RulesVersion == 2, "v1 and v2 rule files load as their own versions; new worlds get v2");
        // 0.12 saves fingerprint SHA-256(grid text + v1 rules text): keep the v1 files byte-identical so it is recognised.
        string legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gridText + rules1Text)));
        Check(legacy == "18D3663E10314B0A73CF0BFAB59283034A6DA9494DE89859AF98E9CD4169F300", "v1 grid+rules files are byte-identical to 0.12 (legacy save fingerprint " + legacy.Substring(0, 8) + ")");
        var source = JsonSerializer.Deserialize<RegionalGeologyCatalog.GridFile>(gridText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Check(source.Lithology.Count(b => b == 2) == 2560 && source.Sediment.Count(b => b == 24) == 439 && source.Sediment.Count(b => b == 28) == 125, "GLiM/GUM class counts preserved by the bake");
        // fingerprint: generation-relevant content only
        string noteEdited = rules1Text.Replace("Vanilla carbonate-category proxy; not chemical equivalence.", "Edited wording.").Replace("https://whc.unesco.org/en/list/32/maps/", "https://example.org/");
        Check(noteEdited != rules1Text && new RegionalGeologyCatalog(gridText, noteEdited).Fingerprint == cat1.Fingerprint, "Notes/URLs do not change the data fingerprint");
        string reformatted = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(rules1Text), new JsonSerializerOptions { WriteIndented = false });
        Check(new RegionalGeologyCatalog(gridText, reformatted).Fingerprint == cat1.Fingerprint, "Whitespace/formatting does not change the data fingerprint");
        string fractionEdited = rules1Text.Replace("{\"material\":\"sandstone\",\"fraction\":0.24}", "{\"material\":\"sandstone\",\"fraction\":0.25}");
        Check(fractionEdited != rules1Text && new RegionalGeologyCatalog(gridText, fractionEdited).Fingerprint != cat1.Fingerprint, "A bed thickness change changes the data fingerprint");
        Check(cat1.Fingerprint != cat2.Fingerprint, "v1 and v2 fingerprints differ");
        // validation
        Check(Throws(() => new RegionalGeologyCatalog(gridText, rules1Text.Replace("\"version\": 1", "\"version\": 999"))), "Unsupported rules version rejected (caller logs and disables, never crashes)");
        Check(Throws(() => new RegionalGeologyCatalog(gridText, rules2Text.Replace("\"polygon\":[[22.5,56.2]", "\"polygonX\":[[22.5,56.2]"))), "Province without polygon rejected");
        Check(Throws(() => new RegionalGeologyCatalog(gridText, rules2Text.Replace("[[22.5,56.2],[24.0,55.9],[27.8,56.1],[28.2,57.4],[26.0,57.9],[24.1,57.6],[22.5,56.2]]", "[[22.5,56.2],[24.0,55.9]]"))), "Degenerate province polygon rejected");
        Check(Throws(() => new RegionalGeologyCatalog(gridText, "{not json")), "Corrupt rules rejected as ArgumentException-family");
        Check(Throws(() => new RegionalGeologyCatalog(gridText.Substring(0, gridText.Length / 2), rules1Text)), "Truncated grid rejected");
        Check(RegionalGeologyCatalog.IsSupportedRulesVersion(1) && RegionalGeologyCatalog.IsSupportedRulesVersion(2) && !RegionalGeologyCatalog.IsSupportedRulesVersion(0) && !RegionalGeologyCatalog.IsSupportedRulesVersion(3), "Supported rules versions are exactly 1..2");
        Check(GeologyTransitions.IsValidWidth(96) && !GeologyTransitions.IsValidWidth(double.NaN) && !GeologyTransitions.IsValidWidth(600), "Contact width validation");
        // GA child-grade adapter helper
        Check(RegionalGeologyCompatibility.PadChildGrades<string>(null, 3).Length == 0 && RegionalGeologyCompatibility.PadChildGrades(new[] { "poor", "medium" }, 3).SequenceEqual(new[] { "poor", "medium", "medium" }), "Child grades clamp like the native parent generator");
        // basics retained from v1
        Check(cat1.At(57, 25).Province == "baltic-devonian-interior-pilot" && cat2.At(57, 25).Province == "baltic-devonian-interior-pilot", "Latvia pilot province in both versions");
        Check(!cat1.At(57, 25, true).HasRegionalBedrock && cat2.At(91, 25).OutsideEarth && cat2.At(double.NaN, 25).OutsideEarth, "Outside-Earth samples never borrow real rock");
        Check(cat1.SaltOccurrence(49.9791667, 20.0638889)?.Id == "wieliczka" && cat1.SaltOccurrence(57, 25) == null, "Salt kernels are local");
    }
    static bool Throws(Action a)
    {
        try { a(); return false; } catch (ArgumentException) { return true; }
    }

    static void VanillaMaterials()
    {
        string assets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory", "assets", "survival", "worldproperties", "block");
        string withDeposit = Path.Combine(assets, "rockwithdeposit.json"), plain = Path.Combine(assets, "rock.json");
        if (!File.Exists(withDeposit) || !File.Exists(plain)) { Info("installed game assets not found; skipped"); return; }
        var rockCodes = Regex.Matches(File.ReadAllText(withDeposit), "\"?[Cc]ode\"?\\s*:\\s*\"([a-z]+)\"").Select(m => "game:rock-" + m.Groups[1].Value).ToHashSet();
        var looseCodes = Regex.Matches(File.ReadAllText(plain), "\"?[Cc]ode\"?\\s*:\\s*\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var cat in new[] { cat1, cat2 })
            foreach (var m in cat.Materials)
                Check(rockCodes.Contains(m.Vanilla), $"v{cat.Version} {m.Id} -> {m.Vanilla} exists in the installed game");
        foreach (string p in cat2.Preserve) Check(rockCodes.Contains("game:" + p), "preserved " + p + " is a real rock block");
        Check(looseCodes.Contains("chalk") && looseCodes.Contains("chert") && !looseCodes.Contains("tuff"), "chalk/chert have sand+gravel variants; tuff has none (never a top bed)");
        Check(cat2.Materials.All(m => m.Asset != "game:rock-mudstone"), "GA mudstone (bauxite recipe) never selected");
    }

    // ------------------------------------------------------------------------------------------------ v2 categories

    static void V2Categories()
    {
        int known1 = 0, known2 = 0, filled = 0;
        for (int z = 0; z < 360; z++) for (int x = 0; x < 720; x++)
        {
            double lat = 89.75 - z * .5, lon = -179.75 + x * .5;
            if (cat1.At(lat, lon).HasRegionalBedrock) known1++;
            var s = cat2.At(lat, lon);
            if (s.HasRegionalBedrock) known2++;
            if (s.Filled) filled++;
        }
        Info($"cells with regional bedrock: v1 {known1}, v2 {known2} ({filled} v2 cells borrow a neighbour's category)");
        Check(known1 == 46707, "v1 regional-bedrock cell count unchanged (46707)");
        Check(known2 > known1 + 5000, "v2 fills coastal/no-data and pyroclastic cells");
        foreach (var (name, lat, lon) in new[] { ("Bergen", 60.39, 5.32), ("Hawaii Hilo", 19.72, -155.08), ("Hawaii Kona", 19.64, -155.99) })
        {
            int v1 = 0, v2 = 0, n = 0;
            for (double dy = -.06; dy <= .061; dy += .03) for (double dx = -.06; dx <= .061; dx += .03)
            { n++; if (cat1.At(lat + dy, lon + dx).HasRegionalBedrock) v1++; if (cat2.At(lat + dy, lon + dx).HasRegionalBedrock) v2++; }
            Info($"{name}: regional bedrock at {v1}/{n} (v1) -> {v2}/{n} (v2)");
            Check(v2 == n, name + " coast gets regional bedrock in v2");
        }
        foreach (var (name, lat, lon) in new[] { ("Ecuador Andes (py cell)", -0.25, -78.75), ("Kyushu (py cell)", 32.75, 131.25), ("Kamchatka (py cell)", 58.25, 159.25), ("Taupo NZ (py cell)", -38.25, 176.25), ("Sierra Madre Occidental (py cell)", 22.75, -104.25) })
        {
            var s1 = cat1.At(lat, lon); var s2 = cat2.At(lat, lon, false);
            // use the exact centre of a known pyroclastic cell; the Voronoi owner may be a neighbour, so search the cell
            bool any = false;
            for (double dy = -.2; dy <= .2; dy += .1) for (double dx = -.2; dx <= .2; dx += .1)
            { var t = cat2.At(lat + dy, lon + dx); if (t.Lithology == 12 && t.Profile.Id == "pyroclastic") any = true; }
            Info($"{name}: v1 lith={s1.Lithology} profile={s1.Profile.Id}; v2 profile={s2.Profile.Id}");
            Check(s1.Lithology != 12 || !s1.HasRegionalBedrock, name + " was unknown in v1");
            Check(any, name + " maps to the pyroclastic (andesite/tuff) profile in v2");
        }
        foreach (var (name, lat, lon) in new[] { ("Dover cliffs", 51.13, 1.32), ("Champagne (Reims)", 49.25, 4.03), ("Salisbury Plain", 51.2, -1.8), ("Stevns Klint", 55.3, 12.4), ("Austin chalk", 30.3, -97.75), ("Selma chalk", 32.4, -87.0) })
        {
            var s = cat2.At(lat, lon);
            Info($"{name}: v2 profile={s.Profile.Id} province={s.Province} lith={s.Lithology}");
            Check(s.Profile.Id == "chalk-cretaceous" && s.Profile.Beds[0].Material == "chalk" && s.Profile.Beds.Any(b => b.Material == "chert"), name + " has a chalk-with-flint sequence in v2");
        }
        Check(cat2.At(48.0, -3.5).Profile.Id != "chalk-cretaceous", "Armorican crystalline Brittany is not chalk");
        Check(cat2.ProfileById("carbonate").Beds.Any(b => b.Material == "chert"), "Carbonate sequence carries chert bands");
        Check(cat2.Preserve.Contains("rock-kimberlite") && cat2.Preserve.Contains("rock-suevite") && cat2.Preserve.Contains("rock-bauxite"), "v2 keeps vanilla kimberlite/suevite (diamond hosts) and bauxite");
        Check(cat1.Preserve.SequenceEqual(new[] { "rock-bauxite" }), "v1 replacement set unchanged (bauxite only preserved)");
        foreach (var p in cat2.AllProfiles) if (p.Beds.Length > 0) Check(p.Beds[0].Material != "tuff", p.Id + " never starts with tuff (no tuff sand/gravel)");
    }

    // ------------------------------------------------------------------------------------------------ resources (unit)

    static void ResourceUnits()
    {
        var basalt = new RegionalGeologyCatalog.Sample(2, 0, cat2.ProfileById("mafic-volcanic"), null, "fixture");
        var granite = new RegionalGeologyCatalog.Sample(9, 0, cat2.ProfileById("felsic-plutonic"), null, "fixture");
        var lake = new RegionalGeologyCatalog.Sample(3, 22, cat2.ProfileById("siliciclastic"), null, "fixture");
        var dune = new RegionalGeologyCatalog.Sample(3, 11, cat2.ProfileById("siliciclastic"), null, "fixture");
        var peat = new RegionalGeologyCatalog.Sample(1, 24, cat2.ProfileById("covered"), null, "fixture");
        RegionalGeologyCatalog.Site S(double slope, double t = 12, double wet = .6, bool fresh = false, bool marine = false, bool ice = false, bool under = false)
            => new(marine, false, ice, under || marine, fresh, t, 800, 0, slope * 2.4, 2.4, wet);
        Check(RegionalGeologyCatalog.ClayFactor(basalt, S(5)) >= 1, "Weathered basalt keeps full clay frequency (Deccan black soils)");
        Check(RegionalGeologyCatalog.ClayFactor(granite, S(5, 25, .8)) >= .9, "Tropical crystalline saprolite keeps clay");
        Check(RegionalGeologyCatalog.ClayFactor(granite, S(500)) is > 0 and < .6, "Steep ground reduces but never vetoes clay");
        Check(RegionalGeologyCatalog.ClayFactor(lake, S(5)) > RegionalGeologyCatalog.ClayFactor(dune, S(5)), "Lacustrine clay favoured over dune sand");
        Check(RegionalGeologyCatalog.ClayFactor(lake, S(5, marine: true)) == 0 && RegionalGeologyCatalog.ClayFactor(lake, S(5, ice: true)) == 0, "No clay on the sea floor or glaciers");
        Check(RegionalGeologyCatalog.ClayFactor(lake, S(5, fresh: true, under: true)) > .9, "River/lake-bed centres keep clay (bank patches)");
        // peat
        Check(RegionalGeologyCatalog.PeatFactor(peat, S(3, -3.7, .5)) > 0, "Mapped Hudson Bay Lowland peat allowed (cold, modest precipitation)");
        Check(RegionalGeologyCatalog.PeatFactor(peat, S(3, -9, .55)) > 0, "Permafrost peat plateau allowed (-9 C)");
        Check(RegionalGeologyCatalog.PeatFactor(peat, S(3, -19, .6)) == 0 && RegionalGeologyCatalog.PeatFactor(granite, S(3, -14, .6)) == 0, "Polar desert has no peat");
        Check(RegionalGeologyCatalog.PeatFactor(peat, S(3, 27, .9)) > 0, "Tropical peat swamp allowed (no 18 C ceiling)");
        Check(RegionalGeologyCatalog.PeatFactor(granite, S(3, 10, .2)) == 0, "Dry unmapped ground has no peat");
        Check(RegionalGeologyCatalog.PeatFactor(granite, S(3, 6, .7)) > 0, "Wet flat boreal rock (Shield) can hold peat");
        Check(RegionalGeologyCatalog.PeatFactor(granite, S(300, 6, .7)) == 0 && RegionalGeologyCatalog.PeatFactor(granite, S(120, 9, .97)) > 0, "No peat cliffs; hyper-oceanic blanket bog on moderate slopes");
        Check(RegionalGeologyCatalog.PeatFactor(peat, S(3, 10, .9, under: true)) == 0, "No peat centred under water");
        // salt
        var salt = new RegionalGeologyCatalog.Sample(14, 28, cat2.ProfileById("evaporitic"), null, "fixture");
        var none = new RegionalGeologyCatalog.Sample(3, 0, cat2.ProfileById("siliciclastic"), null, "fixture");
        var dry = S(2, 20, .1);
        Check(RegionalGeologyCatalog.SaltDecision(salt, dry, true, true) == RegionalGeologyCatalog.SaltPermission.Favoured, "Evidence-positive surface salt flat favoured (skips the warm-desert gate)");
        Check(RegionalGeologyCatalog.SaltDecision(none, dry, false, true) == RegionalGeologyCatalog.SaltPermission.Native, "Missing evidence keeps native surface attempts");
        Check(RegionalGeologyCatalog.SaltDecision(none, dry, false, false) == RegionalGeologyCatalog.SaltPermission.Native && RegionalGeologyCatalog.SaltDecision(salt, dry, true, false) == RegionalGeologyCatalog.SaltPermission.Native, "Buried salt keeps native behaviour with or without evidence");
        Check(RegionalGeologyCatalog.SaltDecision(salt, S(2, 20, .1, marine: true), true, true) == RegionalGeologyCatalog.SaltPermission.Deny && RegionalGeologyCatalog.SaltDecision(salt, S(2, -20, .1, ice: true), true, false) == RegionalGeologyCatalog.SaltPermission.Deny, "No salt beds under the sea or ice even with evidence");
        Check(RegionalGeologyCatalog.SaltMapValue(1234, false) == 1234 && RegionalGeologyCatalog.SaltMapValue(1234, true) == 0, "Native salt map values survive on Earth");
    }

    // ------------------------------------------------------------------------------------------------ v1 equivalence

    static void V1ColumnEquivalence()
    {
        // ColumnV1/BedV1 (per column) must equal the frozen per-block RockAt for every profile, height and seed.
        var rnd = new Random(5);
        Span<int> b = stackalloc int[RegionalGeologyCatalog.MaxBeds];
        int mismatches = 0, n = 0;
        foreach (var p in cat1.AllProfiles)
        {
            var s = new RegionalGeologyCatalog.Sample(3, 0, p, null, "fixture");
            for (int t = 0; t < 300; t++)
            {
                int top = rnd.Next(0, 384); double lat = rnd.NextDouble() * 180 - 90, lon = rnd.NextDouble() * 360 - 180;
                long seed = rnd.Next(int.MinValue, int.MaxValue);
                int beds = RegionalGeologyCatalog.ColumnV1(p, top, lat, lon, seed, b), bed = 0;
                for (int y = top; y > 0; y--)
                {
                    bed = RegionalGeologyCatalog.BedV1(top - y, b, beds, bed);
                    n++;
                    if (cat1.Materials[RegionalGeologyCatalog.MaterialOfBed(p, bed)].Id != cat1.RockAt(s, top - y, top, lat, lon, seed)) mismatches++;
                }
            }
        }
        Info($"per-column v1 strata vs per-block RockAt: {n} blocks, {mismatches} mismatches");
        Check(mismatches == 0, $"per-column v1 strata equal per-block RockAt ({n} blocks, {mismatches} mismatches)");
    }

    // ------------------------------------------------------------------------------------------------ golden

    /// <summary>
    /// The v1 code paths frozen as 0.12.2 wrote them. The world positions depend on Earth's layout, so an agreed layout
    /// change (decision 0019: the straight seam) rewrites the per-world hashes with GOLDEN_V1_REWRITE=1, after this
    /// test passed on the old layout. The cell lookup does not depend on the layout and is never rewritten.
    /// </summary>
    static void V1Golden()
    {
        string goldenPath = Path.Combine(root, "tests/RegionalGeologyTests/golden-v1.json");
        var golden = JsonDocument.Parse(File.ReadAllText(goldenPath)).RootElement;
        var rewrite = Environment.GetEnvironmentVariable("GOLDEN_V1_REWRITE") == "1"
            ? new JsonObject { ["cells"] = golden.GetProperty("cells").GetString() } : null;
        using (var h = new Hasher())
        {
            for (int z = 0; z < 360; z++) for (int x = 0; x < 720; x++) h.Sample(cat1.At(89.75 - z * .5, -179.75 + x * .5));
            Check(h.Done() == golden.GetProperty("cells").GetString(), "v1 categorical lookup at all 259200 cell centres unchanged");
        }
        Span<int> bounds = stackalloc int[RegionalGeologyCatalog.MaxBeds];
        foreach (var w in GoldenWorlds.All)
        {
            var model = Model(w);
            var geo = model.Geometry;
            double cellBlocks = geo.BlocksPerDegreeZ * .5;
            var hw = new Hasher(); var hs = new Hasher(); var hst = new Hasher(); var hr = new Hasher(); var hc = new Hasher();
            int regional = 0, blocks = 0, covers = 0, ice = 0, rrNonRegional = 0;
            for (int i = 0; i < w.Count; i++)
            {
                GoldenWorlds.Position(w, i, out int x, out int z);
                var warp = GeologyTransitions.Warp(x, z, w.Seed, w.Width);
                hw.D(warp.X); hw.D(warp.Z);
                var sample = RegionalGeologyWorld.Sample(cat1,geo, x, z, w.Seed, w.Width, cellBlocks, out var original);
                hs.I(x); hs.I(z); hs.Sample(sample);
                var c = model.GetColumn(x, z);
                int heightmap = Math.Clamp(c.Top, 1, w.MapHeight - 2);
                int mapTop = w.MapHeight - 1;
                if (sample.HasRegionalBedrock)
                {
                    // strata pass: top below glacier ice (the pass finds it from the ice blocks; IceBottom-1 by construction)
                    int top = Math.Min(Math.Min(heightmap, c.IceBottom - 1), mapTop);
                    if (top >= 1)
                    {
                        regional++;
                        if (c.IceBottom <= c.Top) ice++;
                        hst.I(x); hst.I(z); hst.I(top);
                        int beds = RegionalGeologyCatalog.ColumnV1(sample.Profile, top, original.Lat, original.Lon, w.Seed, bounds), bed = 0;
                        for (int y = top; y > 0; y--)
                        {
                            bed = RegionalGeologyCatalog.BedV1(top - y, bounds, beds, bed);
                            hst.S(cat1.Materials[RegionalGeologyCatalog.MaterialOfBed(sample.Profile, bed)].Id); blocks++;
                        }
                    }
                    if (c.IceBottom > c.Top)
                    {
                        // RegionalRock for an ice-free column (EarthGeology.RegionalRock, v1 branch)
                        hr.I(x); hr.I(z);
                        int strataTop = Math.Min(Math.Min(heightmap, c.IceBottom - 1), mapTop);
                        int beds = RegionalGeologyCatalog.ColumnV1(sample.Profile, strataTop, original.Lat, original.Lon, w.Seed, bounds);
                        foreach (int depth in GoldenWorlds.Depths)
                        {
                            if (depth > heightmap - 1) continue;
                            int y = heightmap - depth;
                            int bed = RegionalGeologyCatalog.BedV1(strataTop - y, bounds, beds, 0);
                            hr.I(depth); hr.S(cat1.Materials[RegionalGeologyCatalog.MaterialOfBed(sample.Profile, bed)].Id);
                        }
                    }
                }
                else rrNonRegional++;
                // ApplyCover v1 decision (EarthGeology.ApplyCover, rules==1)
                int kind = 0, cap = -1;
                if (!sample.OutsideEarth)
                {
                    bool dune = sample.Sediment == 11, coarse = sample.Sediment is 2 or 15 or 19;
                    if ((dune || coarse) && heightmap >= 4 && !(c.IceBottom <= c.Top || c.Water != TerrainModel.WaterNone || c.Beach))
                    {
                        cap = dune && model.Data.SampleClimate(original.Lat, original.Lon).Precip < 600 ? 0 : 2;
                        kind = dune ? 1 : 2; covers++;
                    }
                }
                hc.I(x); hc.I(z); hc.I(kind); hc.I(cap);
            }
            if (rewrite != null)
            {
                rewrite[w.Name] = new JsonObject
                {
                    ["warp"] = hw.Done(), ["sample"] = hs.Done(), ["strata"] = hst.Done(), ["regionalRock"] = hr.Done(), ["cover"] = hc.Done(),
                    ["regionalColumns"] = regional, ["strataBlocks"] = blocks, ["coverColumns"] = covers, ["iceColumns"] = ice,
                };
                Info($"{w.Name}: golden rewritten ({regional} regional columns, {blocks} strata blocks, {covers} cover columns, {ice} under ice)");
                continue;
            }
            var g = golden.GetProperty(w.Name);
            Check(hw.Done() == g.GetProperty("warp").GetString(), w.Name + ": v1 contact warp bit-identical");
            Check(hs.Done() == g.GetProperty("sample").GetString(), w.Name + ": v1 sample/profile selection identical");
            Check(hst.Done() == g.GetProperty("strata").GetString() && blocks == g.GetProperty("strataBlocks").GetInt32(), w.Name + $": v1 strata identical ({regional} columns, {blocks} blocks, {ice} under ice)");
            Check(hr.Done() == g.GetProperty("regionalRock").GetString(), w.Name + ": v1 RegionalRock identical where a regional profile applies (ice-free)");
            Check(hc.Done() == g.GetProperty("cover").GetString() && covers == g.GetProperty("coverColumns").GetInt32(), w.Name + $": v1 sand/gravel cover decisions identical ({covers} columns)");
            Info($"{w.Name}: {rrNonRegional} sampled columns without a regional profile now return RegionalRock=0 (baseline returned granite there)");
        }
        if (rewrite != null)
            File.WriteAllText(goldenPath, rewrite.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
    }

    // ------------------------------------------------------------------------------------------------ eligibility

    static readonly Dictionary<string, TerrainModel> models = new();
    /// <summary>Terrain models are expensive (river index); share them between sections.</summary>
    static TerrainModel Model(GoldenWorld w)
    {
        string key = string.Join(",", w.W, w.L, w.MapHeight, w.Sea, w.RingStart, w.Ring, w.Seed);
        return models.TryGetValue(key, out var m) ? m : models[key] = w.Build(data);
    }
    static GoldenWorld UserWorld(int height, long seed = 12345) => new GoldenWorld
    {
        Name = "user-h" + height, W = 512000, L = 204800, MapHeight = height, Sea = (int)(0.4313725490196078 * height), RingStart = 51200, Ring = 409600, Seed = seed, Width = 96
    };

    static void Eligibility()
    {
        var means = new List<double>();
        foreach (int h in new[] { 256, 384, 1024 })
        {
            var w = UserWorld(h);
            var model = Model(w);
            double cellBlocks = model.Geometry.BlocksPerDegreeZ * .5;
            var rnd = new Random(7);
            int eligible = 0, allowed = 0, nearFresh = 0, freshAllowed = 0; double sum = 0, sum2 = 0; int allowed2 = 0;
            while (eligible < 12000)
            {
                double x = rnd.NextDouble() * w.W, z = rnd.NextDouble() * w.L;
                var c = model.GetColumn(x, z);
                if (c.Gutter || c.Water != 0 || c.IceBottom <= c.Top) continue;
                var cl = data.SampleClimate(c.Lat, c.Lon);
                if (cl.Rain / 255.0 < 0.27 || cl.TMean - 0.0065 * Math.Max(0, c.Elev) < -10) continue;
                var s = RegionalGeologyWorld.Sample(cat1,model.Geometry, x, z, w.Seed, w.Width, cellBlocks, out _);
                if (s.OutsideEarth) continue;
                eligible++;
                var site = RegionalGeologyWorld.SiteAt(model, x, z, out _);
                double f = RegionalGeologyCatalog.ClayFactor(s, site);
                if (f > 0) allowed++;
                sum += Math.Min(1, f);
                var s2 = RegionalGeologyWorld.Sample(cat2,model.Geometry, x, z, w.Seed, w.Width, cellBlocks, out _);
                double f2 = RegionalGeologyCatalog.ClayFactor(s2, site);
                if (f2 > 0) allowed2++;
                sum2 += Math.Min(1, f2);
                if (site.NearFreshWater) { nearFresh++; if (f > 0) freshAllowed++; }
            }
            double mean = sum / eligible;
            means.Add(mean);
            Info($"h{h}: clay allowed on {100.0 * allowed / eligible:0.0}% (v1 sample) / {100.0 * allowed2 / eligible:0.0}% (v2) of {eligible} vanilla-eligible land columns; mean multiplier {mean:0.000} / {sum2 / eligible:0.000}; near fresh water {freshAllowed}/{nearFresh} allowed  [baseline 0.12.2: 64.5% / 57.1% / 33.1% at h256/384/1024, near-water 63/408 .. 25/408]");
            Check(allowed >= .85 * eligible && allowed2 >= .85 * eligible, $"h{h}: clay attempts allowed on >= 85% of vanilla-eligible land");
            Check(mean >= .8 && sum2 / eligible >= .8, $"h{h}: clay multiplier averages >= 0.8");
            Check(freshAllowed == nearFresh, $"h{h}: river/lake water depth never vetoes clay");
            if (h == 384) PeatChecks(w, model, cellBlocks);
        }
        Check(means.Max() - means.Min() < .05, $"clay frequency independent of world height (means {string.Join(" / ", means.Select(m => m.ToString("0.000", CultureInfo.InvariantCulture)))})");
    }

    static void PeatChecks(GoldenWorld w, TerrainModel model, double cellBlocks)
    {
        var g = JsonSerializer.Deserialize<RegionalGeologyCatalog.GridFile>(gridText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        int cells = 0, land = 0, ok = 0; double sum = 0;
        var failures = new List<string>();
        for (int zc = 0; zc < 360; zc++) for (int xc = 0; xc < 720; xc++)
        {
            if (g.Sediment[zc * 720 + xc] != 24) continue;
            cells++;
            double lat = 89.75 - zc * .5, lon = -179.75 + xc * .5;
            model.Geometry.FromGeo(lat, lon, out double px, out double pz);
            var site = RegionalGeologyWorld.SiteAt(model, px, pz, out var c);
            if (site.Underwater || site.Ice || site.Marine) continue;
            land++;
            var s = RegionalGeologyWorld.Sample(cat1,model.Geometry, px, pz, w.Seed, w.Width, cellBlocks, out _);
            double f = RegionalGeologyCatalog.PeatFactor(s, site);
            sum += f;
            if (f > 0) ok++; else if (failures.Count < 6) failures.Add($"{lat},{lon} T={site.Temperature:0.0} wet={site.Wetness:0.00} slope={site.Slope:0} sed={s.Sediment}");
        }
        Info($"mapped peat cells: {cells}, dry-land centres {land}, peat eligible at {ok} ({100.0 * ok / land:0.0}%), mean factor {sum / land:0.00} [baseline 0.12.2: 139/415 = 33.5%]" + (failures.Count > 0 ? "; e.g. not eligible: " + string.Join("; ", failures) : ""));
        Check(ok >= .9 * land, "mapped peat is eligible on >= 90% of its dry-land cells");
        foreach (var (name, lat, lon) in new[] { ("Latvia", 57.0, 25.0), ("Lithuania", 55.5, 24.0), ("Finland", 63.0, 26.0), ("West Siberian Lowland", 61.0, 75.0), ("Hudson Bay Lowlands", 54.0, -84.0), ("Ireland midlands", 53.3, -7.8), ("Scotland Flow Country", 58.4, -3.8), ("Central Kalimantan", -2.5, 114.0), ("Riau, Sumatra", 0.5, 102.0) })
        {
            int n = 0, eligible = 0; double mean = 0;
            for (double dy = -.3; dy <= .31; dy += .15) for (double dx = -.3; dx <= .31; dx += .15)
            {
                model.Geometry.FromGeo(lat + dy, lon + dx, out double px, out double pz);
                var site = RegionalGeologyWorld.SiteAt(model, px, pz, out _);
                if (site.Underwater || site.Marine) continue;
                n++;
                double f = RegionalGeologyCatalog.PeatFactor(RegionalGeologyWorld.Sample(cat1,model.Geometry, px, pz, w.Seed, w.Width, cellBlocks, out _), site);
                if (f > 0) eligible++;
                mean += f;
            }
            Info($"{name}: peat eligible at {eligible}/{n} land points, mean factor {(n > 0 ? mean / n : 0):0.00}");
            Check(n > 0 && eligible >= .6 * n, name + ": peat eligible across the region");
        }
    }

    // ------------------------------------------------------------------------------------------------ v2 strata

    static void V2Exposure()
    {
        var w = UserWorld(384);
        var model = Model(w);
        var geo = model.Geometry;
        double cellBlocks = geo.BlocksPerDegreeZ * .5;
        var gs = cat2.Generation;
        var reference = new GeologyReference(model, w.Seed, cellBlocks, gs.DatumRadiusCells, gs.StrataThicknessPerSeaLevel, gs.DatumUndulation, gs.ThicknessVariation);
        Info($"thickness scale {reference.ThicknessScale:0} blocks at height 384 (sea {model.SeaLevel})");
        Span<int> b = stackalloc int[RegionalGeologyCatalog.MaxBeds];
        // canyon/valley transects: distinct surface rocks within one profile
        foreach (var (name, lat, lon0, lon1) in new[] { ("Grand Canyon", 36.10, -112.45, -111.85), ("Rhine gorge (Middle Rhine)", 50.15, 7.45, 7.85), ("Alps, Rhone valley (Valais)", 46.25, 7.0, 7.7), ("Dover cliffs coast", 51.13, 1.0, 1.45) })
        {
            var byProfile = new Dictionary<string, HashSet<string>>();
            var byProfileV1 = new Dictionary<string, HashSet<string>>();
            int steps = 400;
            for (int i = 0; i <= steps; i++)
            {
                geo.FromGeo(lat, lon0 + (lon1 - lon0) * i / steps, out double x, out double z);
                var c = model.GetColumn(x, z);
                var s = RegionalGeologyWorld.Sample(cat2,geo, x, z, w.Seed, w.Width, cellBlocks, out _);
                if (s.HasRegionalBedrock)
                {
                    int top = Math.Min(c.Top, c.IceBottom - 1);
                    reference.At(x, z, out double datum, out double thick);
                    int beds = RegionalGeologyCatalog.ColumnV2(s.Profile, datum, thick, b);
                    string m = cat2.Materials[RegionalGeologyCatalog.MaterialOfBed(s.Profile, RegionalGeologyCatalog.BedV2(top, b, beds, 0))].Id;
                    (byProfile.TryGetValue(s.Profile.Id, out var set) ? set : byProfile[s.Profile.Id] = new()).Add(m);
                }
                var s1 = RegionalGeologyWorld.Sample(cat1,geo, x, z, w.Seed, w.Width, cellBlocks, out var o1);
                if (s1.HasRegionalBedrock)
                {
                    int top = Math.Min(c.Top, c.IceBottom - 1);
                    string m = cat1.RockAt(s1, 0, top, o1.Lat, o1.Lon, w.Seed);
                    (byProfileV1.TryGetValue(s1.Profile.Id, out var set) ? set : byProfileV1[s1.Profile.Id] = new()).Add(m);
                }
            }
            int best = byProfile.Count == 0 ? 0 : byProfile.Values.Max(v => v.Count);
            int bestV1 = byProfileV1.Count == 0 ? 0 : byProfileV1.Values.Max(v => v.Count);
            Info($"{name}: surface beds per profile v2 {{{string.Join("; ", byProfile.Select(p => p.Key + ": " + string.Join(",", p.Value)))}}}; v1 max {bestV1}");
            if (name != "Dover cliffs coast") Check(best >= 2, name + ": v2 valley/canyon exposes >= 2 beds of one sequence");
            else Check(byProfile.TryGetValue("chalk-cretaceous", out var chalk) && chalk.Contains("chalk"), "Dover: chalk at the surface");
            Check(bestV1 <= 1, name + ": v1 (frozen) shows only the first bed at the surface");
        }
        // global statistics over random regional land columns
        var rnd = new Random(11);
        int n = 0, deeper = 0, basement = 0; var thickRatio = new List<double>();
        var mountain = new List<double>(); var lowland = new List<double>();
        while (n < 6000)
        {
            double x = rnd.NextDouble() * w.W, z = rnd.NextDouble() * w.L;
            var c = model.GetColumn(x, z);
            if (c.Gutter || c.Water != 0 || c.IceBottom <= c.Top) continue;
            var s = RegionalGeologyWorld.Sample(cat2,geo, x, z, w.Seed, w.Width, cellBlocks, out _);
            if (!s.HasRegionalBedrock || s.Profile.Beds.Length < 2) continue;
            n++;
            reference.At(x, z, out double datum, out double thick);
            int beds = RegionalGeologyCatalog.ColumnV2(s.Profile, datum, thick, b);
            int bed = RegionalGeologyCatalog.BedV2(c.Top, b, beds, 0);
            if (bed > 0) deeper++;
            if (bed >= beds) basement++;
            // first-bed thickness does not grow with local height
            (c.Top - model.SeaLevel > 90 ? mountain : lowland).Add(s.Profile.Beds[0].Fraction * thick);
        }
        double mMean = mountain.Count > 0 ? mountain.Average() / 1 : 0, lMean = lowland.Count > 0 ? lowland.Average() : 0;
        Info($"v2: {100.0 * deeper / n:0.0}% of regional land columns expose a bed below the first ({100.0 * basement / n:0.0}% basement); v1: 0% by construction. Normalised first-bed thickness/fraction: mountains {mMean:0.0} vs lowlands {lMean:0.0} blocks");
        Check(deeper > n * .1 && deeper < n * .8, "v2 surface shows lower beds in a minority-to-majority of columns, not everywhere");
        Check(mountain.Count == 0 || Math.Abs(mMean - lMean) < .25 * lMean, "v2 bed thickness does not scale with local column height");
    }

    // ------------------------------------------------------------------------------------------------ anti-grid

    static void AntiGrid()
    {
        var w = UserWorld(384);
        var model = Model(w);
        var geo = model.Geometry;
        double cellBlocks = geo.BlocksPerDegreeZ * .5;
        var gs = cat2.Generation;
        // displacement stats and continuity
        var rnd = new Random(3);
        var d1 = new List<double>(); var d2 = new List<double>(); double maxJump = 0, maxJumpV1 = 0, maxRate = 0;
        (double X, double Z) V2(double x, double z) => GeologyTransitions.WarpV2(x, z, w.Seed, w.Width, cellBlocks, gs.WarpLowWavelengthCells, gs.WarpLowAmplitudeCells);
        double Step((double X, double Z) a, (double X, double Z) b, double dx, double dz) => Math.Abs(b.X - a.X - dx) + Math.Abs(b.Z - a.Z - dz);
        for (int i = 0; i < 100000; i++)
        {
            double x = rnd.NextDouble() * w.W, z = rnd.NextDouble() * w.L;
            var a = GeologyTransitions.Warp(x, z, w.Seed, w.Width);
            var v = V2(x, z);
            d1.Add(Math.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z)));
            d2.Add(Math.Sqrt((v.X - x) * (v.X - x) + (v.Z - z) * (v.Z - z)));
            maxJump = Math.Max(maxJump, Math.Max(Step(v, V2(x + 1, z), 1, 0), Step(v, V2(x, z + 1), 0, 1)));
            maxJumpV1 = Math.Max(maxJumpV1, Math.Max(Step(a, GeologyTransitions.Warp(x + 1, z, w.Seed, w.Width), 1, 0), Step(a, GeologyTransitions.Warp(x, z + 1, w.Seed, w.Width), 0, 1)));
            // continuity: the change over a tiny step shrinks with the step (no discontinuity anywhere)
            maxRate = Math.Max(maxRate, Math.Max(Step(v, V2(x + 1e-3, z), 1e-3, 0), Step(v, V2(x, z + 1e-3), 0, 1e-3)) / 1e-3);
        }
        d1.Sort(); d2.Sort();
        Info($"cell = {cellBlocks:0} blocks; warp displacement v1 median {d1[d1.Count / 2]:0} p90 {d1[d1.Count * 9 / 10]:0} max {d1[^1]:0}; v2 median {d2[d2.Count / 2]:0} p90 {d2[d2.Count * 9 / 10]:0} max {d2[^1]:0}; largest change per block step v1 {maxJumpV1:0.00}, v2 {maxJump:0.00}; largest local rate {maxRate:0.00}");
        Check(maxRate < 1.25 * maxJump + .5 && maxJump < maxJumpV1 + 2.5, "v2 warp is continuous and only modestly steeper than v1");
        // boundary offsets from the source grid lines, along east-west and north-south transects
        double M1 = GridOffset(cat1, geo, w, cellBlocks, out int b1), M2 = GridOffset(cat2, geo, w, cellBlocks, out int b2);
        Info($"category contacts: mean distance to the nearest 0.5-degree grid line = {M1:0.000} cell (v1, {b1} contacts) vs {M2:0.000} cell (v2, {b2} contacts); 0 = pure grid, 0.25 = no grid trace");
        Check(M2 >= .18 && M2 > M1 + .05, "v2 contacts no longer follow the 0.5-degree grid");
    }
    static double GridOffset(RegionalGeologyCatalog cat, EarthGeometry geo, GoldenWorld w, double cellBlocks, out int count)
    {
        double sum = 0; count = 0;
        var rnd = new Random(19);
        for (int t = 0; t < 120; t++)
        {
            bool eastWest = t % 2 == 0;
            double lat = -50 + rnd.NextDouble() * 115, lon = -170 + rnd.NextDouble() * 330;
            geo.FromGeo(lat, lon, out double x0, out double z0);
            RegionalGeologyCatalog.Sample prev = default; bool has = false;
            for (int i = 0; i < 6000; i += 2)
            {
                double x = eastWest ? x0 + i : x0, z = eastWest ? z0 : z0 + i;
                var s = RegionalGeologyWorld.Sample(cat,geo, x, z, w.Seed, w.Width, cellBlocks, out var o);
                if (o.InGutter) break;
                if (has && (s.Lithology != prev.Lithology || s.Sediment != prev.Sediment) && s.Lithology != 0 && prev.Lithology != 0)
                {
                    // where is this contact relative to the source grid lines (in cell units)?
                    double coord = eastWest ? (o.Lon + 180) * 2 : (90 - o.Lat) * 2;
                    double frac = coord - Math.Floor(coord);
                    sum += Math.Min(frac, 1 - frac);
                    count++;
                }
                prev = s; has = true;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    // ------------------------------------------------------------------------------------------------ threads

    static void Threads()
    {
        var w = UserWorld(384);
        var model = Model(w);
        var geo = model.Geometry;
        double cellBlocks = geo.BlocksPerDegreeZ * .5;
        var gs = cat2.Generation;
        var reference = new GeologyReference(model, w.Seed, cellBlocks, gs.DatumRadiusCells, gs.StrataThicknessPerSeaLevel, gs.DatumUndulation, gs.ThicknessVariation);
        int count = 20000;
        var xs = new double[count]; var zs = new double[count];
        var rnd = new Random(23);
        for (int i = 0; i < count; i++) { geo.FromGeo(rnd.NextDouble() * 120 - 55, rnd.NextDouble() * 340 - 170, out xs[i], out zs[i]); xs[i] = Math.Floor(xs[i]); zs[i] = Math.Floor(zs[i]); }
        string Eval(int i)
        {
            Span<int> b = stackalloc int[RegionalGeologyCatalog.MaxBeds];
            var s = RegionalGeologyWorld.Sample(cat2,geo, xs[i], zs[i], w.Seed, w.Width, cellBlocks, out _);
            reference.At(xs[i], zs[i], out double datum, out double thick);
            int beds = RegionalGeologyCatalog.ColumnV2(s.Profile, datum, thick, b);
            return s.Profile.Id + ":" + datum.ToString("R", CultureInfo.InvariantCulture) + ":" + string.Join(",", b.Slice(0, beds).ToArray());
        }
        var sequential = new string[count];
        for (int i = 0; i < count; i++) sequential[i] = Eval(i);
        var parallel = new string[count];
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i => parallel[i] = Eval(i));
        // shuffled order across threads exercises the per-thread lattice cache
        var order = Enumerable.Range(0, count).OrderBy(i => (i * 7919) % count).ToArray();
        var shuffled = new string[count];
        Parallel.ForEach(order, i => shuffled[i] = Eval(i));
        Check(sequential.SequenceEqual(parallel) && sequential.SequenceEqual(shuffled), $"v2 samples/reference/boundaries identical across {Environment.ProcessorCount} threads and evaluation orders");
    }

    // ------------------------------------------------------------------------------------------------ performance

    static void Performance()
    {
        var w = UserWorld(384);
        var model = Model(w);
        var geo = model.Geometry;
        double cellBlocks = geo.BlocksPerDegreeZ * .5;
        var gs = cat2.Generation;
        var reference = new GeologyReference(model, w.Seed, cellBlocks, gs.DatumRadiusCells, gs.StrataThicknessPerSeaLevel, gs.DatumUndulation, gs.ThicknessVariation);
        Span<int> b = stackalloc int[RegionalGeologyCatalog.MaxBeds];
        int chunks = 40; long sink = 0;
        double[] cxs = new double[chunks], czs = new double[chunks];
        for (int k = 0; k < chunks; k++) { geo.FromGeo(45 + k * .3, 5 + k * .4, out double x, out double z); cxs[k] = Math.Floor(x / 32) * 32; czs[k] = Math.Floor(z / 32) * 32; }
        // warm caches
        for (int k = 0; k < chunks; k++) model.GetColumn(cxs[k], czs[k]);
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < chunks; k++) for (int zz = 0; zz < 32; zz++) for (int xx = 0; xx < 32; xx++) sink += model.GetColumn(cxs[k] + xx, czs[k] + zz).Top;
        double getColumn = sw.Elapsed.TotalMilliseconds / chunks;
        sw.Restart();
        for (int k = 0; k < chunks; k++) for (int zz = 0; zz < 32; zz++) for (int xx = 0; xx < 32; xx++)
        {
            var s = RegionalGeologyWorld.Sample(cat1,geo, cxs[k] + xx, czs[k] + zz, w.Seed, w.Width, cellBlocks, out var o);
            for (int y = 200; y > 0; y--) sink += cat1.RockAt(s, 200 - y, 200, o.Lat, o.Lon, w.Seed).Length;
        }
        double oldStrata = sw.Elapsed.TotalMilliseconds / chunks;
        sw.Restart();
        for (int k = 0; k < chunks; k++) for (int zz = 0; zz < 32; zz++) for (int xx = 0; xx < 32; xx++)
        {
            var s = RegionalGeologyWorld.Sample(cat1,geo, cxs[k] + xx, czs[k] + zz, w.Seed, w.Width, cellBlocks, out var o);
            int beds = RegionalGeologyCatalog.ColumnV1(s.Profile, 200, o.Lat, o.Lon, w.Seed, b), bed = 0;
            for (int y = 200; y > 0; y--) { bed = RegionalGeologyCatalog.BedV1(200 - y, b, beds, bed); sink += RegionalGeologyCatalog.MaterialOfBed(s.Profile, bed); }
        }
        double newStrata = sw.Elapsed.TotalMilliseconds / chunks;
        sw.Restart();
        for (int k = 0; k < chunks; k++) for (int zz = 0; zz < 32; zz++) for (int xx = 0; xx < 32; xx++)
        {
            var s = RegionalGeologyWorld.Sample(cat2,geo, cxs[k] + xx, czs[k] + zz, w.Seed, w.Width, cellBlocks, out _);
            reference.At(cxs[k] + xx, czs[k] + zz, out double datum, out double thick);
            int beds = RegionalGeologyCatalog.ColumnV2(s.Profile, datum, thick, b), bed = 0;
            for (int y = 200; y > 0; y--) { bed = RegionalGeologyCatalog.BedV2(y, b, beds, bed); sink += RegionalGeologyCatalog.MaterialOfBed(s.Profile, bed); }
        }
        double v2Strata = sw.Elapsed.TotalMilliseconds / chunks;
        Info($"per 32x32 chunk, 200 rock blocks per column: GetColumn alone {getColumn:0.00} ms (0.12.2 called it 2-3x per column in the geology passes; now only for cover candidates); v1 strata old per-block RockAt {oldStrata:0.00} ms -> per-column {newStrata:0.00} ms; v2 strata {v2Strata:0.00} ms (sink {sink % 7})");
        Check(newStrata < oldStrata * .6, "v1 per-column strata is substantially faster than per-block RockAt");
        // gravel lens coverage
        foreach (byte cls in new byte[] { 2, 15, 19 })
        {
            double cov = cat2.GravelCoverage(cls), t = GeologyTransitions.GravelThreshold(cov), core = GeologyTransitions.GravelThreshold(cov * .4);
            int hit = 0, two = 0, total = 40000; var r = new Random(cls);
            for (int i = 0; i < total; i++) { double m = GeologyTransitions.GravelMask(r.NextDouble() * 1e6, r.NextDouble() * 1e6, w.Seed); if (m >= t) { hit++; if (m >= core) two++; } }
            Info($"GUM class {cls}: v2 gravel lenses on {100.0 * hit / total:0.0}% of columns (target {100 * cov:0}%), 2 blocks thick on {100.0 * two / total:0.0}%; v1: continuous 2-block sheet on 100%");
            Check(Math.Abs((double)hit / total - cov) < .05, $"gravel coverage for class {cls} near its target");
        }
    }
}
