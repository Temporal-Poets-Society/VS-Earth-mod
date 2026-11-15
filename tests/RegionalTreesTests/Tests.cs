using EarthMap.Core;
using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Regional tree checks: catalogue data, the production pick (tiers, alias sharing, climate envelopes) on synthetic data,
/// and global behaviour on Earth's real baked climate with the vanilla-only and full-pack registrations
/// (tests/RegionalTreesTests/registrations.json, from tools/inspect_tree_inventory.py --fixture).
/// </summary>
static class Tests
{
    static int passed;
    static readonly List<string> failures = new();
    static void Check(bool ok, string msg) { if (ok) passed++; else { failures.Add(msg); Console.WriteLine("FAIL: " + msg); } }

    public static int Run(string root, bool quick)
    {
        var sw = Stopwatch.StartNew();
        var h = new Harness(root);
        Catalogue(h);
        Synthetic();
        Robustness(h);
        RealClimate(h, quick ? 2 : 1);
        Soil(h);
        Performance(h);
        Console.WriteLine(failures.Count == 0
            ? $"PASS {passed} regional tree checks in {sw.Elapsed.TotalSeconds:0.0} s."
            : $"FAILED {failures.Count} of {passed + failures.Count} regional tree checks.");
        return failures.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------- catalogue data
    static void Catalogue(Harness h)
    {
        string rules = h.RulesJson, regions = h.RegionsJson;
        var all = new RegionalTreeCatalog(rules, regions, h.AllProviders);
        var vanilla = new RegionalTreeCatalog(rules, regions, Array.Empty<string>());
        bool Allowed(string generator, double lat, double lon, double elevation = 50, bool underwater = false, bool marine = false, bool saline = false, int depth = 0, bool wet = false) =>
            RegionalTreeCatalog.Allows(all.Find("game:" + generator), new RegionalTreeCatalog.Site(lat, lon, elevation, all.Geography.CodeAt(lat, lon), underwater, marine, saline, depth, wet: wet));

        Check(RegionalTreeCatalog.RulesVersion == 2, "Rules version 2");
        Check(all.Warnings.Count == 0, "Shipped data loads without warnings: " + string.Join("; ", all.Warnings));
        Check(all.Rules.Sum(r => r.Generators.Length) == 195, "Inventory coverage changed; review providers");
        Check(h.Registrations.Select(r => r.Generator).Distinct().All(g => all.Find(g) != null), "Every registered generator of the tested pack is mapped");
        Check(vanilla.Providers.SequenceEqual(new[] { "vanilla" }), "Vanilla works without provider mods");
        Check(vanilla.Find("game:kauri") == null, "Absent Wildcraft cannot activate its rules");
        Check(all.Find("othermod:kauri") == null, "Asset domain must remain distinct");
        Check(RegionalTreeCatalog.Allows(null, default), "Unrelated mods keep their own rules");
        Check(Allowed("sugarmaple", 44, -73), "Sugar maple native in northeastern USA");
        Check(!Allowed("sugarmaple", 51, 10), "Introduced sugar maple must not populate Germany");
        Check(Allowed("norwaymaple", 57, 25), "Norway maple native in Latvia");
        Check(!Allowed("norwaymaple", 44, -73), "Introduced Norway maple excluded in USA");
        Check(!Allowed("crimsonkingmaple", 57, 25), "Cultivated maple excluded from natural forests");
        Check(!Allowed("pyramidalpoplar", 45, 10), "Cultivated poplar excluded from natural forests");
        Check(Allowed("kauri", -35.7, 174), "Kauri native in New Zealand");
        Check(!Allowed("kauri", -33, 151), "Kauri must not cross into Australia");
        Check(Allowed("araucariaheterophylla", -29.04, 167.95), "Norfolk Island pine stays on its native island");
        Check(!Allowed("araucariaheterophylla", -35.7, 174), "Pack named Neozeylandic does not imply mainland NZ native");
        Check(Allowed("scalesiapedunculata", -.66, -90.35), "Galapagos endemic has its own range");
        Check(!Allowed("scalesiapedunculata", 18, -66), "Caribbean pack label is not a native range");
        Check(Allowed("wiliwili", 20.8, -156.3), "Wiliwili in Hawaii");
        Check(!Allowed("wiliwili", -17.8, 178), "Hawaiian endemic absent in Fiji");
        Check(Allowed("piceamariana", 53, -78), "Black spruce native in Canada");
        Check(!Allowed("piceamariana", 65, 26), "Black spruce not native to Finland");
        Check(Allowed("baldcypressswamp", 30, -90, 1, true, false, false, 1), "Bald cypress in native freshwater");
        Check(!Allowed("baldcypressswamp", 30, -90, 1, true, true, false, 1), "Bald cypress excluded from saltwater");
        Check(Allowed("rhizophoramangle", 25.2, -80.6, 0, true, true, false, 1), "Red mangrove in shallow Florida coastal water");
        Check(!Allowed("rhizophoramangle", 25.2, -80.6, 0, true, true, false, 3), "Mangroves excluded from deep water");
        Check(!Allowed("rhizophoramangle", 25.2, -80.6, 0, true, false, true, 1), "Salt lakes are not mangrove coasts");
        Check(!Allowed("rhizophoramangle", 25.2, -80.6, 0, false, false), "Mangroves cannot populate inland Florida");
        Check(all.Geography.CodeAt(0, -130) == null, "No nearest-continent fallback in open ocean");
        Check(!Allowed("baldcypressswamp", 0, -130, 0, true, true, false, 1), "Open ocean excluded");
        Check(Allowed("redwoodpine", 40.5, -124, 100), "Coastal redwood native area");
        Check(!Allowed("redwoodpine", 36, -116, 500), "California botanical state alone must not put redwoods in Mojave");
        Check(Allowed("bristleconepine", 37.4, -118.1, 3000), "Bristlecone at native high elevation");
        Check(!Allowed("bristleconepine", 37.4, -118.1, 100), "Bristlecone excluded from desert lowland");
        Check(!Allowed("bristleconepine", 27, 31, 3000), "Bristlecone excluded from Africa");
        Check(!RegionalTreeCatalog.Allows(all.Find("game:sugarmaple"), new RegionalTreeCatalog.Site(44, -73, 50, all.Geography.CodeAt(44, -73), outsideEarth: true)), "Gutter cannot inherit a native range");
        Check(all.Geography.CodeAt(-17.8, 178) == all.Geography.CodeAt(-17.8, 538), "Wrapped longitude identical");
        // rules v2 taxa (findings 3 and 4)
        Check(Allowed("fir", 57, 25) && Allowed("fir", 67, 26.5) && Allowed("fir", 64.8, -147.7), "Generic fir includes the native spruces (Latvia, Lapland, Alaska)");
        Check(Allowed("elm", 51, 10) && Allowed("elm", 57, 25) && Allowed("elm", 40, -89), "Elm native in Germany, Latvia and the Midwest");
        Check(Allowed("alder", 57, 25, wet: true) && Allowed("alder", 52, -1, wet: true) && Allowed("alder", 60.5, 15, wet: true), "Tree alder native in the Baltics, Britain and Sweden");
        Check(!Allowed("alder", 57, 25) && !Allowed("alnusglutinosa", 57, 25) && Allowed("alnusglutinosa", 57, 25, wet: true), "Black alder only on wet sites (near rivers and lakes)");
        Check(!Allowed("tamanu", 1, 114) && RegionalTreeCatalog.Allows(all.Find("game:tamanu"), new RegionalTreeCatalog.Site(1, 109.5, 5, all.Geography.CodeAt(1, 109.5), shore: true)), "Tamanu only on coasts");
        Check(Allowed("shrubalder", 57, 25, wet: true) && Allowed("shrubalder", 46.5, 10, wet: true), "Shrub alder in the Baltics and the Alps (wet sites)");
        Check(Allowed("silverbirch", 57, 25) && !Allowed("silverbirch", -41, -71.5), "WCVP error: silver birch is not native in Patagonia");
        Check(Allowed("spruce", 57, 25) && Allowed("spruce", 50, -73), "Wildcraft spruce: regional Picea species");
        Check(Allowed("beech", 51, 10) && Allowed("beech", 44, -72.7), "Beech: European and American beech");
        var spruce = all.Find("game:spruce");
        Check(spruce.MinTemperature <= 6 && spruce.MaxTemperature >= 8, $"Spruce Earth envelope covers Latvia's real 6 °C (was Wildcraft's vanilla-scale -14..2): {spruce.MinTemperature}..{spruce.MaxTemperature}");
        var beech = all.Find("game:beech");
        Check(beech.MinTemperature <= 7 && beech.MinRain <= 120, $"Beech envelope covers central Europe: {beech.MinTemperature}..{beech.MaxTemperature}, rain {beech.MinRain}..{beech.MaxRain}");
        Check(all.Rules.Where(r => r.Group == null).All(r => r.HasEarthClimate && r.MinRain != null), "Every non-forest-type rule has an Earth climate envelope");
        Check(all.Rules.Where(r => r.Group != null).All(r => r.Climate == null), "Forest-type species keep vanilla's temperature bands (no native-range envelope)");
        Check(all.Analogues.Count >= 8 && vanilla.Analogues.Count >= 8, "Analogue rules load with and without provider mods");
        string describe = all.Describe(57, 25);
        Check(describe.Contains("(BLT)") && describe.Contains("Pinus sylvestris") && describe.Contains("Picea abies") && describe.Contains("northern"), "/earth trees description: region, natives and analogue rules");

        var geo = new EarthGeometry { RingStart = 25600, RingLength = 204800, Wraps = true, Gutter = 1024, EquatorZ = 51200, BlocksPerDegreeZ = 500, SeamLon0 = -168 };
        foreach (var p in new[] { (44d, -73d), (-35.7, 174d), (-17.8, 178d), (57d, 25d) })
        {
            geo.FromGeo(p.Item1, p.Item2, out double x, out double z);
            var roundtrip = geo.ToGeo(x + geo.RingLength, z);
            Check(all.Geography.CodeAt(p.Item1, p.Item2) == all.Geography.CodeAt(roundtrip.Lat, roundtrip.Lon), "Earth seam must preserve botanical region");
            Check(all.Geography.FindBlended(p.Item1, p.Item2) == all.Geography.FindBlended(roundtrip.Lat, roundtrip.Lon), "Earth seam must preserve the blended region");
        }
        // Country borders, polygon holes, and restricted coastal fallback independent of real-data examples.
        string sample = """{"version":1,"regions":[{"code":"A","name":"A","polygons":[[[[0,0],[1,0],[1,1],[0,1],[0,0]],[[0.4,0.4],[0.6,0.4],[0.6,0.6],[0.4,0.6],[0.4,0.4]]]]},{"code":"B","name":"B","polygons":[[[[1,0],[2,0],[2,1],[1,1],[1,0]]]]}]}""";
        var synthetic = new BotanicalRegions(sample);
        Check(synthetic.CodeAt(.5, .99) == "A" && synthetic.CodeAt(.5, 1.01) == "B", "Actual position must distinguish border within a chunk");
        Check(synthetic.CodeAt(.5, .5, false) == null, "Polygon holes preserved");
        Check(synthetic.CodeAt(.5, -.05) == "A" && synthetic.CodeAt(.5, -.2) == null, "Coastal tolerance must be bounded");
        Check(synthetic.CodeAt(double.NaN, 0) == null && synthetic.FindBlended(double.NaN, 0) == -1, "Invalid latitude excluded");
        string expectedRegion = all.Geography.CodeAt(57, 25);
        Check(expectedRegion != null, "Latvia has a botanical region");
        var sw = Stopwatch.StartNew();
        Parallel.For(0, 20000, i => { if (all.Geography.CodeAt(57, 25) != expectedRegion) throw new Exception("Concurrent lookup"); });
        Check(sw.ElapsedMilliseconds < 10000, "Regional lookup unexpectedly slow");
        // the blended lookup is a pure function of the position whatever each thread's cache holds
        var rnd = new Random(5);
        var points = Enumerable.Range(0, 4000).Select(_ => (rnd.NextDouble() * 120 - 50, rnd.NextDouble() * 360 - 180)).ToArray();
        var first = points.Select(p => all.Geography.FindBlended(p.Item1, p.Item2)).ToArray();
        int mismatches = 0;
        Parallel.For(0, points.Length * 4, i => { int k = (i * 7919) % points.Length; if (all.Geography.FindBlended(points[k].Item1, points[k].Item2) != first[k]) Interlocked.Increment(ref mismatches); });
        Check(mismatches == 0, "Blended lookup is deterministic across threads and cache states");
        int moved = 0, total = 0;
        for (int k = 0; k < points.Length; k++)
        {
            int exact = all.Geography.Find(points[k].Item1, points[k].Item2);
            if (exact < 0) continue;
            total++;
            if (first[k] != exact) moved++;
        }
        Check(moved > 0 && moved < total * 0.15, $"Blending changes the region only near borders ({moved} of {total} land points)");
        double maxOffset = 0;
        for (int k = 0; k < points.Length; k++)
        {
            BotanicalRegions.BorderOffset(points[k].Item1, points[k].Item2, out double dLat, out double dLon);
            maxOffset = Math.Max(maxOffset, Math.Max(Math.Abs(dLat), Math.Abs(dLon) * Math.Max(0.3, Math.Cos(points[k].Item1 * Math.PI / 180))));
        }
        Check(maxOffset > 0.3 && maxOffset <= 1.0, $"Border blending amplitude 0.3..1 degree (max {maxOffset:0.00})");
    }

    // ------------------------------------------------------------------------------------------- synthetic pick
    const string TwoRegions = """{"version":1,"regions":[{"code":"A","name":"A","polygons":[[[[0,0],[10,0],[10,10],[0,10],[0,0]]]]},{"code":"B","name":"B","polygons":[[[[10,0],[20,0],[20,10],[10,10],[10,0]]]]}]}""";

    static string Rules(string entries, string analogues = "[]") => "{\"version\":2,\"entries\":[" + entries + "],\"analogues\":" + analogues + "}";
    static string Rule(string provider, string gens, string taxon, string regions, string extra = "") =>
        $"{{\"provider\":\"{provider}\",\"generators\":[{string.Join(",", gens.Split(' ').Select(g => "\"game:" + g + "\""))}],\"taxa\":[\"{taxon}\"],\"taxonIds\":[\"{taxon}\"],\"regions\":[{string.Join(",", regions.Split(' ').Select(r => "\"" + r + "\""))}]{extra}}}";

    static Dictionary<string, double> Shares(RegionalTreePicker p, in TreeSite s, int n = 40000, int seed = 1)
    {
        var rnd = new TestRandom((ulong)seed);
        var counts = new Dictionary<string, double>();
        for (int i = 0; i < n; i++)
        {
            int k = p.Pick(s, ref rnd, out _, out int tier);
            string key = k < 0 ? "none" : p.Generator(k).Substring(5);
            counts[key] = counts.GetValueOrDefault(key) + 1.0 / n;
            counts["tier" + tier] = counts.GetValueOrDefault("tier" + tier) + 1.0 / n;
        }
        return counts;
    }

    static void Synthetic()
    {
        const string env = ",\"minTemperature\":0,\"maxTemperature\":20,\"minRain\":50,\"maxRain\":200";
        string json = Rules(string.Join(",",
                Rule("vanilla", "englishoak oldenglishoak", "Quercus robur", "A", ",\"group\":\"oak\""),
                Rule("vanilla", "fir", "Abies", "A", ",\"group\":\"fir\""),
                Rule("vanilla", "kapok vineykapok", "Ceiba pentandra", "A" , env),
                Rule("floralzonescaribbeanregion", "ceibapentandra", "Ceiba pentandra", "A", env),
                Rule("wildcrafttree", "beech", "Fagus sylvatica", "A", env),
                Rule("vanilla", "acacia", "Vachellia tortilis", "A", ",\"group\":\"arid\"")),
            """[{"name":"test","note":"","regions":["B"],"minTemperature":0,"maxTemperature":30,"minRain":0,"maxRain":255,"members":[{"provider":"vanilla","generators":["game:fir"],"weight":1}]}]""");
        var all = new RegionalTreeCatalog(json, TwoRegions, new[] { "floralzonescaribbeanregion", "wildcrafttree" });
        TreeVariantInfo V(string g, float w, int minT = -40, int maxT = 40, int minRain = 0, int maxRain = 255, int minForest = 0, int maxForest = 255) =>
            TreeVariantInfo.FromLimits("game:" + g, w, TreeVariantInfo.HabitatLand, minT, maxT, minRain, maxRain, 0, 255, minForest, maxForest, 0, 1);
        int a = all.Geography.IndexOf("A"), b = all.Geography.IndexOf("B");
        var site = new TreeSite { Temp = 10, Rain = 120, Fert = 120, Forest = 180, HeightRel = 0.5f, Lat = 5, Lon = 5, Region = a };

        // finding 6: one species' aliases share its weight; they are neither averaged nor multiplied
        var oak = new RegionalTreePicker(all, new[] { V("englishoak", 35, 2, 22), V("oldenglishoak", 0.5f, 2, 22), V("oldenglishoak", 0.5f, 2, 22), V("fir", 35, -10, 30) });
        var s1 = Shares(oak, site);
        double oakShare = s1.GetValueOrDefault("englishoak") + s1.GetValueOrDefault("oldenglishoak");
        Check(Math.Abs(oakShare - 0.5) < 0.02, $"Oak with two old-oak aliases keeps its intended weight (oak {oakShare:0.000}, fir {s1.GetValueOrDefault("fir"):0.000}; averaging gave 0.26)");
        Check(s1.GetValueOrDefault("oldenglishoak") > 0.005 && s1.GetValueOrDefault("oldenglishoak") < 0.03, "Old oak aliases keep their own small share");
        var kapok = new RegionalTreePicker(all, new[] { V("kapok", 90), V("vineykapok", 30), V("ceibapentandra", 80), V("ceibapentandra", 40), V("beech", 90) });
        var s2 = Shares(kapok, site);
        double ceiba = s2.GetValueOrDefault("kapok") + s2.GetValueOrDefault("vineykapok") + s2.GetValueOrDefault("ceibapentandra");
        Check(Math.Abs(ceiba - 0.5) < 0.02, $"Kapok from two providers is one species with its full weight ({ceiba:0.000})");
        Check(s2.GetValueOrDefault("ceibapentandra") > 0.12, "A second provider's copy of a species is not diluted away");

        // finding 2: Earth envelope replaces the provider's vanilla-scale envelope (Wildcraft beech 14..20 °C, rain 110..180)
        var beech = new RegionalTreePicker(all, new[] { V("beech", 1.7f, 14, 20, 110, 180, 110, 255), V("fir", 100, -10, 15) });
        var cold = site; cold.Temp = 8; cold.Rain = 100;
        Check(Shares(beech, cold).GetValueOrDefault("beech") > 0.003, "Beech at a real 8 °C / rain 100 site inside its Earth envelope");
        var hot = site; hot.Temp = 25;
        Check(Shares(beech, hot).GetValueOrDefault("beech") == 0, "Beech excluded outside its Earth envelope");

        // finding 1: tiers. Native first; analogue only where no native qualifies; climate fallback last
        var tiers = new RegionalTreePicker(all, new[] { V("englishoak", 35, 2, 22), V("fir", 100, -10, 30), V("kapok", 90, 27, 40) });
        var native = Shares(tiers, site);
        Check(native.GetValueOrDefault("tier1") > 0.99, "Native species used where available");
        var inB = site; inB.Region = b; inB.Lon = 15;
        var analogue = Shares(tiers, inB);
        Check(analogue.GetValueOrDefault("tier2") > 0.99 && analogue.GetValueOrDefault("fir") > 0.99, "Designated analogue where no native species exists");
        var none = inB; none.Temp = 35;
        var fallback = Shares(tiers, none);
        Check(fallback.GetValueOrDefault("tier3") > 0.5 && fallback.GetValueOrDefault("kapok") > 0.5, "Pre-0.9 climate pick when neither natives nor analogues qualify");
        var sea = none; sea.Underwater = true; sea.Marine = true;
        Check(Shares(tiers, sea).GetValueOrDefault("none") > 0.999, "No trees in the sea, whatever the tier");
        var expected = new double[4];
        double success = tiers.Expected(site, new float[3], expected);
        Check(Math.Abs(success - 1) < 1e-6 && expected[1] > 0.99, "Expected() agrees with Pick on tier use");

        // pre-0.9 mode without a catalogue: all registrations by climate, no regions
        var legacy = new RegionalTreePicker(null, new[] { V("englishoak", 35, 2, 22), V("kapok", 90, 27, 40) });
        var l = Shares(legacy, inB);
        Check(l.GetValueOrDefault("englishoak") > 0.99 && l.GetValueOrDefault("tier3") > 0.99, "Without regional trees the pre-0.9 climate pick is unchanged");

        // determinism under threads: same seeds, same picks
        var picks = new int[2000];
        for (int i = 0; i < picks.Length; i++) { var r = new TestRandom((ulong)i); picks[i] = kapok.Pick(site, ref r, out _, out _); }
        int diff = 0;
        Parallel.For(0, picks.Length * 8, i => { int k = i % picks.Length; var r = new TestRandom((ulong)k); if (kapok.Pick(site, ref r, out _, out _) != picks[k]) Interlocked.Increment(ref diff); });
        Check(diff == 0, "Pick is deterministic and thread-safe");
    }

    static void Robustness(Harness h)
    {
        var bad = RegionalTreeCatalog.TryLoad("{not json", h.RegionsJson, null, out string e1);
        Check(bad == null && e1 != null, "Corrupt rules: TryLoad reports instead of throwing");
        var v1 = RegionalTreeCatalog.TryLoad("{\"version\":1,\"entries\":[]}", h.RegionsJson, null, out string e2);
        Check(v1 == null && e2 != null, "Old rules version: TryLoad reports instead of throwing");
        var partial = RegionalTreeCatalog.TryLoad(Rules(Rule("vanilla", "fir", "Abies", "BLT NOWHERE") + ",{\"provider\":\"vanilla\"}"), h.RegionsJson, null, out string e3);
        Check(partial != null && e3 == null && partial.Warnings.Count == 2 && partial.Find("game:fir") != null, "Invalid rules and unknown regions are skipped with warnings");
    }

    // ------------------------------------------------------------------------------------------- real climate
    static void RealClimate(Harness h, double step)
    {
        var grid = h.ForestGrid(step);
        var legacyVanilla = h.Make("legacy-vanilla", Array.Empty<string>(), false);
        var vanilla = h.Make("regional-vanilla", Array.Empty<string>(), true);
        var full = h.Make("regional-full", h.AllProviders, true);
        var lv = Grid.Evaluate(h, legacyVanilla, grid);
        var gv = Grid.Evaluate(h, vanilla, grid);
        var gf = Grid.Evaluate(h, full, grid);
        Console.WriteLine($"  forest-capable land at {step}°: pre-0.9 vanilla treeless {Harness.Pct(lv.Treeless)} success {Harness.Pct(lv.Success)}; " +
            $"vanilla treeless {Harness.Pct(gv.Treeless)} success {Harness.Pct(gv.Success)} (native {Harness.Pct(gv.Tier[1])}, analogue {Harness.Pct(gv.Tier[2])}, fallback {Harness.Pct(gv.Tier[3])}); " +
            $"full pack treeless {Harness.Pct(gf.Treeless)} success {Harness.Pct(gf.Success)} (native {Harness.Pct(gf.Tier[1])}, analogue {Harness.Pct(gf.Tier[2])}, fallback {Harness.Pct(gf.Tier[3])})");
        Check(gv.Treeless <= Math.Min(0.10, lv.Treeless) && gv.Treeless <= 0.02, $"Vanilla only: treeless share of forest-capable land {Harness.Pct(gv.Treeless)} (pre-0.9 {Harness.Pct(lv.Treeless)}, 0.12.2 ~47%)");
        Check(gf.Treeless <= 0.005, $"Full pack: treeless share {Harness.Pct(gf.Treeless)} (0.12.2 ~35%)");
        Check(gv.Success >= lv.Success && gf.Success >= 0.95, $"Tree-attempt success at least the pre-0.9 level ({Harness.Pct(gv.Success)} / {Harness.Pct(gf.Success)} vs {Harness.Pct(lv.Success)})");
        Check(gv.Tier[3] <= 0.01 && gf.Tier[3] <= 0.01, "The pre-0.9 climate fallback is a rare last resort");
        Check(gf.Tier[1] >= 0.7 && gv.Tier[1] >= 0.45, $"Native species dominate where available; analogues fill thin pools (natives: full {Harness.Pct(gf.Tier[1])}, vanilla {Harness.Pct(gv.Tier[1])})");

        // every forest-capable botanical country has a native or designated analogue tree with vanilla only
        var weak = gv.ByRegion.Where(kv => kv.Key != "-" && kv.Value.Area > 0)
            .Where(kv => kv.Value.Fallback / kv.Value.Area > 0.5 || kv.Value.Treeless / kv.Value.Area > 0.5)
            .Select(kv => kv.Key).Except(new[] { "FAL" }).ToArray(); // Falkland Islands: no trees in reality, fallback only
        Check(weak.Length == 0, "Every forest-capable TDWG region has a native or analogue tree with vanilla only; failing: " + string.Join(" ", weak));

        // finding 2: Wildcraft's temperate species are eligible over a substantial share of their native forest
        var coverage = Grid.Coverage(h, full, step <= 1 ? h.ForestGrid(1) : grid);
        foreach (string g in new[] { "beech", "linden", "ash", "elm", "alder", "shrubalder", "spruce", "truefir", "willow", "aspen", "yew", "cedar", "brideinwhite" })
        {
            var row = coverage.FirstOrDefault(r => r.Rule.Provider == "wildcrafttree" && r.Rule.Generators.Contains("game:" + g));
            Check(row != null && row.Eligible >= 0.3, $"Wildcraft {g} eligible on {Harness.Pct(row?.Eligible ?? 0)} of its native forest (0.12.2: ~0%)");
        }

        // finding 3: Latvian mix (real: pine 34%, birch 31%, spruce 18%, alder/aspen/oak/ash the rest)
        var lat = Grid.Latvia(h, vanilla);
        Console.WriteLine("  Latvia, vanilla only: " + string.Join(", ", lat.Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
        Check(Within(lat, "pine", 0.30, 0.42) && Within(lat, "spruce/fir", 0.15, 0.27) && Within(lat, "birch", 0.22, 0.35) && Within(lat, "deciduous/other", 0.06, 0.25),
            "Latvian composition with vanilla only near pine 35-40%, spruce ~20%, birch 25-30%");
        var latFull = Grid.Latvia(h, full);
        Console.WriteLine("  Latvia, full pack: " + string.Join(", ", latFull.Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
        Check(Within(latFull, "pine", 0.2, 0.42) && Within(latFull, "spruce/fir", 0.12, 0.3) && Within(latFull, "birch", 0.15, 0.35), "Latvian composition with the full pack keeps pine, spruce and birch");

        FullPack(h, vanilla, full);

        // finding 5: botanical borders blend instead of cutting along political lines
        Border(h, vanilla, "Canada/USA 49°N (Pacific to Minnesota)", 47.6, 50.4, -122.0, -95.3, "WAS IDA MNT NDA MIN", true);
        Border(h, vanilla, "Alabama-Georgia/Florida 31°N", 29.6, 32.4, -87.4, -85.1, "FLA", false);
    }

    /// <summary>Full-pack composition (0.13 in-game findings): plausible dominant genera, riparian alders, native-only
    /// shrubs, no trees in river channels, thin native pools blended with analogues, northern oak limits.</summary>
    static void FullPack(Harness h, Harness.Config vanilla, Harness.Config full)
    {
        foreach (var p in Probes.All)
        {
            var g = Grid.Genera(h, full, p.Lat, p.Lon);
            var problems = p.Problems(g);
            Check(problems.Count == 0, $"Full pack, {p.Name}: " + string.Join(", ", g.Take(5).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))) + (problems.Count > 0 ? " -- " + string.Join("; ", problems) : ""));
        }
        // floodplain trees only near water: upland none, by rivers/lakes present
        foreach (var (name, la, lo) in new[] { ("Latvia", 57.0, 25.0), ("Upper Rhine", 48.6, 7.8), ("Finland south", 61.5, 25.0) })
        {
            var (dry, _) = Grid.Mix(h, full, la, lo);
            var (wet, _) = Grid.Mix(h, full, la, lo, wet: true);
            double alderDry = dry.GetValueOrDefault("alnusglutinosa") + dry.GetValueOrDefault("alder");
            double alderWet = wet.GetValueOrDefault("alnusglutinosa") + wet.GetValueOrDefault("alder");
            Check(alderDry == 0 && alderWet > 0.02 && alderWet <= 0.35, $"{name}: black/grey alder upland {Harness.Pct(alderDry)}, by water {Harness.Pct(alderWet)}");
            Check(dry.GetValueOrDefault("populusalba") + dry.GetValueOrDefault("oldpopulusalba") + dry.GetValueOrDefault("blackpoplar") == 0, $"{name}: no floodplain poplars upland");
        }
        // shrubs: native species only (no analogue or climate fallback shrubs: oak scrub in the savanna, dwarf birch in Kenya)
        var tiers = new double[4];
        var share = new float[full.Shrubs.Count];
        int foreign = 0, sites = 0;
        foreach (var cell in h.ForestGrid(4, 0.05))
        {
            var site = h.Site(full.Catalog, cell.Lat, cell.Lon, true);
            if (full.Shrubs.Expected(site, share, tiers) <= 0) continue;
            sites++;
            if (tiers[2] + tiers[3] > 0) foreign++;
        }
        Check(sites > 100 && foreign == 0, $"Shrubs are native only ({foreign} of {sites} sites used analogue/fallback shrubs)");
        foreach (var (name, la, lo, dwarf) in new[] { ("Latvia", 57.0, 25.0, false), ("Finland central", 64.5, 26.0, false), ("Lapland", 67.5, 26.5, true) })
        {
            var (s, _) = Grid.Mix(h, full, la, lo, shrubs: true);
            Check((s.GetValueOrDefault("dwarfbirch") > 0) == dwarf, $"{name}: dwarf birch shrubs {(dwarf ? "present (tundra/northern mires)" : "absent from boreal/hemiboreal forest floor")}");
        }
        foreach (var (name, la, lo) in new[] { ("Highveld", -28.74, 24.77), ("Kaibab", 36.4, -112.2), ("Kenya", -0.5, 36.5), ("Finland central", 64.5, 26.0) })
        {
            var (s, _) = Grid.Mix(h, full, la, lo, shrubs: true);
            var (t, _) = Grid.Mix(h, full, la, lo);
            double oak = s.GetValueOrDefault("englishoak") + t.GetValueOrDefault("englishoak") + t.GetValueOrDefault("oldenglishoak");
            Check(oak == 0 && s.GetValueOrDefault("dwarfbirch") == 0, $"{name}: no English oak or dwarf birch trees/shrubs");
        }
        var (lv, _) = Grid.Mix(h, full, 57.0, 25.0);
        Check(lv.GetValueOrDefault("quercuspetraea") == 0, "Sessile oak absent from continental central Latvia");

        // river channels and flooded sites (all tiers, trees and shrubs)
        int landInWater = 0, channel = 0, shrubInWater = 0, picks = 0;
        var rnd = new TestRandom(11);
        foreach (var (la, lo) in new[] { (30.5, 114.3), (35.0, -90.1), (-3.3, -60.0), (50.0, 8.0), (25.6, -80.7), (0.5, 20.0), (60.0, 75.0) })
            foreach (var c in new[] { vanilla, full })
            {
                var site = h.Site(c.Catalog, la, lo, false);
                site.Underwater = true;
                for (int i = 0; i < 400; i++)
                {
                    int k = c.Trees.Pick(site, ref rnd, out _, out _);
                    picks++;
                    if (k >= 0 && c.TreeRegs[k].Habitat == "land") landInWater++;
                }
                var river = site; river.RiverChannel = true;
                for (int i = 0; i < 200; i++) if (c.Trees.Pick(river, ref rnd, out _, out _) >= 0) channel++;
                var shrubSite = h.Site(c.Catalog, la, lo, true); shrubSite.Underwater = true;
                for (int i = 0; i < 200; i++) if (c.Shrubs.Pick(shrubSite, ref rnd, out _, out _) >= 0) shrubInWater++;
            }
        Check(landInWater == 0, $"Underwater attempts never pick land trees in any tier ({landInWater} of {picks})");
        Check(channel == 0, "No tree of any habitat in a river channel");
        Check(shrubInWater == 0, "No shrubs in water");
        var lake = h.Site(full.Catalog, 35.0, -90.1, false); lake.Underwater = true;
        var lakeRnd = new TestRandom(3);
        int cypress = 0;
        for (int i = 0; i < 400; i++) { int k = full.Trees.Pick(lake, ref lakeRnd, out _, out _); if (k >= 0 && full.Trees.Generator(k).StartsWith("game:baldcypress")) cypress++; }
        Check(cypress > 0, "Bald cypress still grows in freshwater swamps and lakes");

        // thin native pools: analogues fill part of a monoculture, natives keep the majority; rich pools are unchanged
        foreach (var (name, la, lo, c, max) in new[] { ("NZ (vanilla)", -40.0, 175.5, vanilla, 0.6), ("Honshu (vanilla)", 35.5, 137.0, vanilla, 0.75),
            ("SE Australia (vanilla)", -35.5, 148.5, vanilla, 0.6), ("Georgia (vanilla)", 32.5, -83.5, vanilla, 0.75) })
        {
            var (g, _) = Grid.Mix(h, c, la, lo);
            double top = g.Values.Max();
            Check(top <= max && top >= 0.4 && g.Count >= 3, $"{name}: thin native pool blended ({string.Join(", ", g.Take(4).Select(kv => kv.Key + " " + Harness.Pct(kv.Value)))})");
        }
        var rich = h.Site(full.Catalog, 57.0, 25.0, false);
        full.Trees.Expected(rich, null, tiers);
        Check(tiers[2] == 0, "Diverse native pools (Latvia) take no analogues");

        // full-pack boreal mixes (selection shares; vanilla fir is the spruce form)
        var lat = Grid.Genera(h, full, 57.0, 25.0);
        var fin = Grid.Genera(h, full, 64.5, 26.0);
        double Spruce(Dictionary<string, double> g) => g.Where(kv => Probe.Is(kv.Key, "Picea")).Sum(kv => kv.Value);
        Console.WriteLine($"  full pack: Latvia pine {Harness.Pct(lat.GetValueOrDefault("Pinus"))}, spruce/fir {Harness.Pct(Spruce(lat))}, birch {Harness.Pct(lat.GetValueOrDefault("Betula"))}; " +
            $"central Finland pine {Harness.Pct(fin.GetValueOrDefault("Pinus"))}, spruce/fir {Harness.Pct(Spruce(fin))}, birch {Harness.Pct(fin.GetValueOrDefault("Betula"))}");
        Check(lat.GetValueOrDefault("Pinus") is >= 0.3 and <= 0.45 && Spruce(lat) is >= 0.13 and <= 0.27 && lat.GetValueOrDefault("Betula") is >= 0.2 and <= 0.35,
            "Full pack, Latvia: pine ~35%, spruce ~20%, birch 25-30%");
        Check(fin.GetValueOrDefault("Pinus") is >= 0.38 and <= 0.6 && Spruce(fin) is >= 0.22 and <= 0.4 && fin.GetValueOrDefault("Betula") is >= 0.1 and <= 0.25,
            "Full pack, central Finland: pine ~50%, spruce ~30%, birch ~15%");
    }

    static bool Within(Dictionary<string, double> d, string k, double lo, double hi) => d.GetValueOrDefault(k) >= lo && d.GetValueOrDefault(k) <= hi;

    /// <summary>
    /// Border continuity along a political line between botanical countries: the exact lookup cuts along the line; the
    /// blended lookup must move the transition by a few tenths of a degree, irregularly, and mix both sides near the line.
    /// For long lines the share of the southern side per 0.1° band, and of a species that differs across the line, must
    /// change gradually.
    /// </summary>
    static void Border(Harness h, Harness.Config c, string name, double south, double north, double west, double east, string southRegions, bool longLine)
    {
        var geo = c.Catalog.Geography;
        var targets = southRegions.Split(' ').Select(geo.IndexOf).ToHashSet();
        double maxExact = 0, maxBlend = 0, maxShareJump = 0;
        double? prevExact = null, prevBlend = null, prevShare = null;
        var share = new float[c.Trees.Count];
        int near = 0, mixed = 0;
        var shift = new List<double>();
        for (double lo = west; lo <= east; lo += 0.05)
        {
            double? exactEdge = null, blendEdge = null;
            var column = new List<(double La, bool Exact, bool Blend)>();
            for (double la = south; la <= north; la += 0.02)
            {
                if (!h.IsLand(la, lo)) continue;
                bool e = targets.Contains(geo.Find(la, lo)), b = targets.Contains(geo.FindBlended(la, lo));
                if (!e && exactEdge == null) exactEdge = la;
                if (!b && blendEdge == null) blendEdge = la;
                column.Add((la, e, b));
            }
            if (exactEdge == null || blendEdge == null) continue;
            shift.Add(blendEdge.Value - exactEdge.Value);
            foreach (var p in column.Where(p => Math.Abs(p.La - exactEdge.Value) <= 0.5)) { near++; if (p.Exact != p.Blend) mixed++; }
        }
        double mean = shift.Count > 0 ? shift.Average() : 0;
        double sd = shift.Count > 1 ? Math.Sqrt(shift.Sum(s => (s - mean) * (s - mean)) / (shift.Count - 1)) : 0;
        double meanAbs = shift.Count > 0 ? shift.Average(Math.Abs) : 0;
        double mixing = near > 0 ? (double)mixed / near : 0;
        for (double la = south; la <= north && longLine; la += 0.1)
        {
            int n = 0, exact = 0, blend = 0;
            double treeShare = 0;
            for (double lo = west; lo <= east; lo += 0.05)
            {
                double l2 = la + 0.05 * Math.Sin(lo * 7.3);
                if (!h.IsLand(l2, lo)) continue;
                n++;
                if (targets.Contains(geo.Find(l2, lo))) exact++;
                var site = h.Site(c.Catalog, l2, lo, false);
                if (targets.Contains(site.Region)) blend++;
                double success = c.Trees.Expected(site, share);
                for (int i = 0; i < share.Length; i++) if (c.Trees.Generator(i) is "game:sugarmaple" or "game:silverbirch") treeShare += share[i] / Math.Max(success, 1e-9);
            }
            if (n == 0) continue;
            double fe = (double)exact / n, fb = (double)blend / n, ts = treeShare / n;
            if (prevExact != null) { maxExact = Math.Max(maxExact, Math.Abs(fe - prevExact.Value)); maxBlend = Math.Max(maxBlend, Math.Abs(fb - prevBlend.Value)); maxShareJump = Math.Max(maxShareJump, Math.Abs(ts - prevShare.Value)); }
            prevExact = fe; prevBlend = fb; prevShare = ts;
        }
        Console.WriteLine($"  {name}: transition moved by {meanAbs:0.00}° on average (irregularity sd {sd:0.00}°), {Harness.Pct(mixing)} of points within 0.5° of the line on the other side" +
            (longLine ? $"; largest change between 0.1° bands of the southern share: exact {maxExact:0.00}, blended {maxBlend:0.00}; of the sugar maple/silver birch share {maxShareJump:0.00}" : ""));
        Check(meanAbs >= 0.1 && meanAbs <= 1.0 && sd >= 0.1 && mixing >= 0.08, $"{name}: the border is blended, not a straight political line");
        if (longLine)
        {
            Check(maxExact > 0.45 && maxBlend <= 0.35, $"{name}: the southern share changes gradually (step {maxBlend:0.00} vs hard {maxExact:0.00})");
            Check(maxShareJump <= 0.35, $"{name}: species composition changes gradually across the border ({maxShareJump:0.00})");
        }
    }
    // ------------------------------------------------------------------------------------------- soil
    static void Soil(Harness h)
    {
        var soils = SoilSiteGrid.Read(File.ReadAllBytes(Path.Combine(h.Root, "assets/earth/config/soil-site.bin")));
        var full = h.Make("regional-full", h.AllProviders, true);
        // the soil grid where the ground is well known
        (string Name, double Lat, double Lon)[] places =
        {
            ("Lüneburg Heath", 53.15, 10.05), ("Hildesheim loess", 52.15, 9.95),
            ("Kharkiv chernozem", 50.0, 36.25), ("Swabian Jura", 48.45, 9.6), ("Karelian podzols", 62.5, 30.0), ("Vasyugan bogs", 57.5, 78.0),
            ("Central Amazon", -3.1, -60.0), ("Hudson Bay Lowlands", 53.5, -85.0),
        };
        var at = new Dictionary<string, (double Grass, double Wet, double Poor, double Ph)>();
        foreach (var p in places)
        {
            var v = (soils.Value(SoilSiteGrid.Grass, p.Lat, p.Lon), soils.Value(SoilSiteGrid.Wet, p.Lat, p.Lon),
                soils.Value(SoilSiteGrid.Poor, p.Lat, p.Lon), soils.Value(SoilSiteGrid.Ph, p.Lat, p.Lon));
            at[p.Name] = v;
            Console.WriteLine($"  soil {p.Name,-20} grassland {v.Item1:0.00}, waterlogged {v.Item2:0.00}, poor {v.Item3:0.00}, pH {v.Item4:0.0}");
        }
        Check(at["Lüneburg Heath"].Poor >= 0.5 && at["Lüneburg Heath"].Ph < 5.5, "Lüneburg Heath: poor, acid sands");
        Check(at["Karelian podzols"].Poor >= 0.5 && at["Karelian podzols"].Ph < 5.2, "Karelia: podzols");
        Check(at["Hildesheim loess"].Poor < 0.3 && at["Hildesheim loess"].Ph > at["Lüneburg Heath"].Ph + 0.5, "Hildesheim loess: richer and less acid than the heath");
        // SoilGrids maps the most common class per 2' cell: Polesia's fens drown in its sandy Albeluvisols and the Landes' podzols
        // show as Cambisols, so the grid knows the large peatlands, not every mire
        Check(at["Vasyugan bogs"].Wet >= 0.3, "Vasyugan: peat");
        Check(at["Hudson Bay Lowlands"].Wet >= 0.5, "Hudson Bay Lowlands: peat and gley");
        Check(at["Kharkiv chernozem"].Grass >= 0.7 && at["Kharkiv chernozem"].Ph > 6.5, "Kharkiv: black, neutral soils");
        Check(at["Swabian Jura"].Ph > 6.0, "Swabian Jura: limestone soils");
        Check(at["Central Amazon"].Poor >= 0.6 && at["Central Amazon"].Ph < 5.5, "Central Amazon: leached, acid soils");

        // one central German site on different ground: soil sorts the European species, the forest stays
        var picker = full.Trees;
        var baseSite = h.Site(full.Catalog, 51.0, 10.0, false);
        TreeSite With(float wet, float poor, float grass, float ph, float moisture, bool nearWater = false)
        {
            var s = baseSite;
            s.HasSoil = true; s.WetShare = wet; s.PoorShare = poor; s.GrassShare = grass; s.SoilPh = ph; s.SoilMoisture = moisture; s.Wet = nearWater;
            return s;
        }
        var sites = new (string Name, TreeSite Site)[]
        {
            ("any soil", baseSite),
            ("acid sand", With(0, 1, 0, 4.3f, SoilNiche.DrainedMoisture(0.9, false))),
            ("fen and gley", With(1, 0.2f, 0, 5.8f, SoilNiche.DrainedMoisture(1.2, true), true)),
            ("loess", With(0, 0, 0.2f, 7.0f, SoilNiche.DrainedMoisture(1.0, false))),
        };
        var share = new float[picker.Count];
        var mix = new Dictionary<string, Dictionary<string, double>>();
        var success = new Dictionary<string, double>();
        foreach (var (name, site) in sites)
        {
            double ok = picker.Expected(site, share);
            success[name] = ok;
            var by = new Dictionary<string, double>();
            for (int i = 0; i < share.Length; i++) if (share[i] > 0) by[picker.Generator(i)] = by.GetValueOrDefault(picker.Generator(i)) + share[i] / ok;
            mix[name] = by;
            Console.WriteLine($"  soil mix {name,-13} success {ok:0.00}: " + string.Join(", ", by.OrderByDescending(kv => kv.Value).Take(7).Select(kv => $"{kv.Key.Substring(5)} {100 * kv.Value:0}%")));
        }
        double Genus(string site, params string[] generators) => generators.Sum(g => mix[site].GetValueOrDefault("game:" + g));
        Check(Genus("acid sand", "scotspine") >= 1.5 * Genus("any soil", "scotspine") && Genus("acid sand", "scotspine") >= 1.5 * Genus("loess", "scotspine"),
            $"Scots pine takes the acid sand ({Genus("acid sand", "scotspine"):0.00} vs {Genus("any soil", "scotspine"):0.00} on any soil, {Genus("loess", "scotspine"):0.00} on loess)");
        Check(Genus("fen and gley", "alder", "alnusglutinosa") >= 2 * Genus("acid sand", "alder", "alnusglutinosa"),
            $"Alder takes the wet ground ({Genus("fen and gley", "alder", "alnusglutinosa"):0.00} vs {Genus("acid sand", "alder", "alnusglutinosa"):0.00} on acid sand)");
        Check(Genus("loess", "beech", "englishoak", "oldenglishoak", "ash", "linden") > Genus("acid sand", "beech", "englishoak", "oldenglishoak", "ash", "linden"),
            "Beech, oak, ash and lime prefer the loess to the acid sand");
        foreach (var (name, _) in sites)
            Check(Math.Abs(success[name] - success["any soil"]) < 0.02, $"Soil sorts the species but does not thin the forest ({name}: {success[name]:0.00} vs {success["any soil"]:0.00})");
        // EA-0054 step 2 (site rules 1): one column's own site (wetness class, richness, pH) instead of the grid's shares
        TreeSite Column(int wetness, int richness, float ph)
        {
            var s = baseSite;
            s.HasSoil = true; s.ColumnSite = true; s.SoilPh = ph;
            s.SiteMoisture = SiteNiche.Moisture(wetness, 1.0); s.SiteNutrients = SiteNiche.Nutrients(richness);
            s.Wet = wetness >= SiteNiche.WetGround;   // as EarthTrees sets it
            return s;
        }
        var columns = new (string Name, TreeSite Site)[]
        {
            ("raised bog", Column(5, 0, 4.0f)), ("alder carr", Column(5, 2, 6.5f)), ("dry heath", Column(1, 0, 4.3f)), ("fresh rich", Column(2, 3, 6.8f)),
        };
        foreach (var (name, site) in columns)
        {
            double ok = picker.Expected(site, share);
            success[name] = ok;
            var by = new Dictionary<string, double>();
            for (int i = 0; i < share.Length; i++) if (share[i] > 0) by[picker.Generator(i)] = by.GetValueOrDefault(picker.Generator(i)) + share[i] / ok;
            mix[name] = by;
            Console.WriteLine($"  column {name,-11} success {ok:0.00}: " + string.Join(", ", by.OrderByDescending(kv => kv.Value).Take(7).Select(kv => $"{kv.Key.Substring(5)} {100 * kv.Value:0}%")));
        }
        string[] broadleaf = { "beech", "englishoak", "oldenglishoak", "ash", "linden" }, birch = { "silverbirch", "brideinwhite", "thinbirch", "dwarfbirch" };
        Check(Genus("raised bog", birch) + Genus("raised bog", "scotspine") > 2 * Genus("raised bog", broadleaf),
            $"Raised bog: birch and pine, not broadleaves ({Genus("raised bog", birch) + Genus("raised bog", "scotspine"):0.00} vs {Genus("raised bog", broadleaf):0.00})");
        Check(Genus("alder carr", "alder", "alnusglutinosa") >= 0.2 && Genus("alder carr", "alder", "alnusglutinosa") >= 2 * Genus("fresh rich", "alder", "alnusglutinosa") && Genus("alder carr", "alder", "alnusglutinosa") >= 2 * Genus("raised bog", "alder", "alnusglutinosa"),
            $"Alder carr: alder on wet rich ground ({Genus("alder carr", "alder", "alnusglutinosa"):0.00}), not on the bog or fresh ground");
        Check(Genus("dry heath", "scotspine") >= 1.5 * Genus("fresh rich", "scotspine") && Genus("dry heath", "scotspine") > Genus("dry heath", broadleaf),
            $"Dry heath: Scots pine ({Genus("dry heath", "scotspine"):0.00} vs {Genus("fresh rich", "scotspine"):0.00} on fresh rich ground)");
        Check(Genus("fresh rich", broadleaf) > 1.5 * Genus("raised bog", broadleaf) && Genus("fresh rich", broadleaf) > Genus("dry heath", broadleaf),
            $"Fresh rich ground: beech, oak, ash and lime ({Genus("fresh rich", broadleaf):0.00})");
        foreach (var (name, _) in columns)
            Check(Math.Abs(success[name] - success["any soil"]) < 0.02, $"The column's site sorts the species but does not thin the forest ({name}: {success[name]:0.00})");
        // outside Europe's flora nothing changes: a species without indicators keeps its weight on every soil
        var tokyo = h.Site(full.Catalog, 35.7, 139.7, false);
        var tokyoSoil = tokyo;
        tokyoSoil.HasSoil = true; tokyoSoil.WetShare = 0; tokyoSoil.PoorShare = 1; tokyoSoil.SoilPh = 4.5f; tokyoSoil.SoilMoisture = 5;
        Check(Math.Abs(picker.Expected(tokyo, null) - picker.Expected(tokyoSoil, null)) < 0.02, "Soil does not thin forests of unrated species");
        // outside Europe EIVE's values describe planted trees, not their home (bald cypress, American sycamore): there the
        // soil changes no species' share, on swamp marl in Louisiana or on the Iowa prairie's black soil
        foreach (var (place, lat, lon, wet, poor, grass, ph) in new[] { ("Atchafalaya swamp", 30.3, -91.7, 0.94f, 0.5f, 0f, 7.2f), ("Iowa", 41.75, -93.45, 0.14f, 0f, 0.97f, 6.5f) })
        {
            var plain = h.Site(full.Catalog, lat, lon, false, wet: wet > 0.5f);
            var soiled = plain;
            soiled.HasSoil = true; soiled.WetShare = wet; soiled.PoorShare = poor; soiled.GrassShare = grass; soiled.SoilPh = ph; soiled.SoilMoisture = 5.5f;
            var a = new float[picker.Count];
            double okA = picker.Expected(plain, a), okB = picker.Expected(soiled, share), diff = 0;
            for (int i = 0; i < share.Length; i++) diff = Math.Max(diff, Math.Abs(a[i] / okA - share[i] / okB));
            Console.WriteLine($"  {place}: largest change of a species' share on its soil {100 * diff:0.0} points (top: " +
                string.Join(", ", Enumerable.Range(0, a.Length).OrderByDescending(i => a[i]).Take(3).Select(i => $"{picker.Generator(i).Substring(5)} {100 * a[i] / okA:0}%")) + ")");
            Check(okA > 0 && diff < 0.005, $"Outside Europe soil does not sort the trees ({place})");
        }
        Check(full.Catalog.SoilIndicatorsApply(full.Catalog.Geography.FindBlended(51.0, 10.0)) && !full.Catalog.SoilIndicatorsApply(full.Catalog.Geography.FindBlended(25.9, -80.8))
            && !full.Catalog.SoilIndicatorsApply(full.Catalog.Geography.FindBlended(41.75, -93.45)), "Soil indicators apply in Europe only");
        // the calibrated Latvian mix on Latvia's real soils (sandy podzols, peat, loamy moraine) and wetlands
        h.Soils = soils;
        h.Wetlands = WetlandGrid.Read(File.ReadAllBytes(Path.Combine(h.Root, "assets/earth/config/wetlands.bin")));
        try
        {
            var latSoil = Grid.Latvia(h, full);
            Console.WriteLine("  Latvia, full pack on its soils: " + string.Join(", ", latSoil.Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
            Check(Within(latSoil, "pine", 0.2, 0.45) && Within(latSoil, "spruce/fir", 0.12, 0.3) && Within(latSoil, "birch", 0.15, 0.35),
                "Latvian composition on its soils keeps pine, spruce and birch");
        }
        finally { h.Soils = null; h.Wetlands = null; }
    }
    // ------------------------------------------------------------------------------------------- performance
    static void Performance(Harness h)
    {
        var full = h.Make("regional-full", h.AllProviders, true);
        var (us, bytes) = Grid.Bench(h, full);
        Console.WriteLine($"  pick: {us:0.00} µs and {bytes} bytes per attempt (208 registrations; 0.12.2 ~30 µs and 24 KB)");
        Check(bytes == 0, "Tree pick allocates nothing per attempt");
        Check(us < 5, $"Tree pick under 5 µs per attempt ({us:0.00})");
        var geo = full.Catalog.Geography;
        geo.FindBlended(57, 25);
        var sw = new Stopwatch();
        int sink = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (int i = 0; i < 200000; i++) sink += geo.FindBlended(57 + (i % 50) * 0.001, 25 + (i % 37) * 0.001);
        double lookup = sw.Elapsed.TotalMilliseconds * 1000 / 200000;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        Console.WriteLine($"  blended region lookup: {lookup:0.00} µs per attempt, {alloc} bytes in 200,000 lookups");
        Check(alloc == 0 && lookup < 2, "Blended region lookup is cached and allocation-free");
        // exact lookup (also used by the fauna spawn path): polygons with holes and the coastal fallback, no garbage
        var probes = new[] { (57.0, 25.0), (51.0, 10.0), (-17.8, 178.0), (67.75, 12.75), (0.0, -130.0), (41.9, 12.45), (-29.0, 27.5) };
        foreach (var p in probes) geo.Find(p.Item1, p.Item2);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++) { var p = probes[i % probes.Length]; sink += geo.CodeAt(p.Item1, p.Item2)?.Length ?? 0; }
        alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(alloc == 0, $"Exact region lookup allocates nothing ({alloc} bytes in 20,000 lookups)");
    }
}
