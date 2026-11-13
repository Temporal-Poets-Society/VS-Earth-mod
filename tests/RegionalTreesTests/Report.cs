using EarthMap.Core;
using System.Diagnostics;
using System.Globalization;

/// <summary>Measurement report (not a test): dotnet run -c Release --project tests/RegionalTreesTests -- . --report [--step 1]</summary>
static class Report
{
    public static readonly (string Name, double Lat, double Lon)[] Spots =
    {
        ("Latvia Riga", 57.0, 25.0), ("Latvia W (Kurzeme)", 56.9, 21.8), ("Finland S", 61.5, 25.0), ("Finland N", 67.0, 26.5), ("Sweden C", 60.5, 15.0),
        ("Germany C", 51.0, 10.0), ("France C", 47.0, 2.5), ("England", 52.0, -1.0), ("Poland E", 52.5, 22.0), ("Upper Rhine spawn", 48.6, 7.78),
        ("Spain C", 40.0, -4.0), ("Greece", 39.0, 22.0), ("Turkey W", 39.5, 29.0),
        ("Canada boreal SK", 55.0, -105.0), ("Canada boreal QC", 50.0, -73.0), ("Alaska interior", 64.8, -147.7), ("BC coast", 49.5, -123.5),
        ("US SE Georgia", 32.5, -83.5), ("US Midwest", 40.0, -89.0), ("Florida C", 28.5, -81.5), ("US Pacific NW", 46.0, -122.5), ("Vermont", 44.0, -72.7),
        ("Amazon Manaus", -3.0, -60.0), ("Atlantic forest", -23.0, -45.0), ("Cerrado", -15.5, -47.8), ("Paraguay E", -25.5, -55.5), ("Uruguay", -32.5, -55.5),
        ("Patagonia forest", -41.0, -71.5), ("Valdivia", -40.0, -73.0), ("Chile central", -35.0, -71.5),
        ("Australia SE", -35.5, 148.5), ("NSW highlands", -31.0, 151.5), ("Tasmania", -42.0, 146.5), ("Queensland wet", -17.0, 145.7), ("Australia SW", -34.0, 116.0),
        ("NZ North", -35.7, 174.0), ("NZ South", -42.5, 172.0),
        ("Japan Honshu", 35.5, 137.0), ("Japan Hokkaido", 43.0, 142.5), ("China E", 30.0, 118.0), ("Korea", 37.0, 128.0), ("Russian Far East", 45.0, 135.0), ("Siberia C", 60.0, 100.0),
        ("Cape Town", -33.9, 18.9), ("S Africa lowveld", -24.5, 31.5), ("Congo basin", 0.5, 20.0), ("Ghana", 6.5, -1.5), ("Ethiopia highlands", 9.0, 38.7),
        ("Madagascar E", -18.9, 48.5), ("Kenya", -0.5, 36.5),
        ("Cuba", 22.0, -79.5), ("Jamaica", 18.1, -77.3), ("Hispaniola", 19.0, -71.0), ("Puerto Rico", 18.2, -66.5), ("Costa Rica", 10.0, -84.0), ("Mexico Oaxaca", 17.0, -96.5),
        ("India C", 22.0, 78.0), ("India W Ghats", 12.0, 75.5), ("Borneo", 1.0, 114.0), ("Sumatra", 0.0, 102.0), ("New Guinea", -5.0, 142.0), ("Thailand", 16.0, 101.0),
        ("Hawaii Big Island", 19.6, -155.5), ("Iceland", 64.5, -20.0), ("Ireland", 53.0, -8.0), ("Caucasus", 43.0, 42.0), ("Morocco Atlas", 33.0, -5.0),
    };

    public static void Run(Harness h, string[] args)
    {
        var inv = CultureInfo.InvariantCulture;
        string Opt(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
        double step = double.Parse(Opt("--step", "1"), inv);
        var floral = h.AllProviders.Where(p => p.StartsWith("floralzones")).ToArray();
        var configs = new[]
        {
            h.Make("legacy-vanilla", Array.Empty<string>(), false),
            h.Make("regional-vanilla", Array.Empty<string>(), true),
            h.Make("regional-vanilla+wildcraft", new[] { "wildcrafttree" }, true),
            h.Make("regional-vanilla+floral8", floral, true),
            h.Make("regional-full", h.AllProviders, true),
            h.Make("legacy-full", h.AllProviders, false),
        };
        var grid = h.ForestGrid(step);
        Console.WriteLine($"Forest-capable land cells (forest potential >= 0.3) at {step}°: {grid.Count}");
        foreach (var c in configs)
        {
            var g = Grid.Evaluate(h, c, grid);
            Console.WriteLine($"{c.Name,-28} treeless (P(no tree)>0.9) {Harness.Pct(g.Treeless)}; mean success {Harness.Pct(g.Success)}; tiers native {Harness.Pct(g.Tier[1])} analogue {Harness.Pct(g.Tier[2])} fallback {Harness.Pct(g.Tier[3])}");
            if (c.Catalog != null && args.Contains("--regions"))
            {
                foreach (var kv in g.ByRegion.Where(kv => kv.Value.Treeless + kv.Value.Fallback > 0.001 * g.Area).OrderByDescending(kv => kv.Value.Treeless + kv.Value.Fallback).Take(30))
                    Console.WriteLine($"     {kv.Key} {c.Catalog.Geography.Name(kv.Key)}: treeless {Harness.Pct(kv.Value.Treeless / g.Area)} of world, fallback {Harness.Pct(kv.Value.Fallback / g.Area)} of world ({Harness.Pct((kv.Value.Treeless + kv.Value.Fallback) / kv.Value.Area)} of region)");
            }
        }
        if (args.Contains("--probes"))
        {
            foreach (var p in Probes.All)
                foreach (var c in configs.Where(c => c.Name is "regional-vanilla" or "regional-full"))
                {
                    var g = Grid.Genera(h, c, p.Lat, p.Lon);
                    var problems = p.Problems(g);
                    Console.WriteLine($"{(problems.Count == 0 ? "ok  " : "BAD ")}{p.Name,-20} {c.Name,-17} " + string.Join(", ", g.Where(kv => kv.Value >= 0.02).Take(7).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))) + (problems.Count > 0 ? "   <-- " + string.Join("; ", problems) : ""));
                }
        }
        if (args.Contains("--shrubs"))
        {
            foreach (string pt in Opt("--shrubs", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pt.Split(',');
                double la = double.Parse(parts[0], inv), lo = double.Parse(parts[1], inv);
                foreach (var c in configs.Where(c => c.Name is "regional-vanilla" or "regional-full"))
                {
                    var site = h.Site(c.Catalog, la, lo, true);
                    var (gens, _) = Grid.Mix(h, c, la, lo, shrubs: true);
                    Console.WriteLine($"   {pt} {c.Name,-18} T{site.Temp} cold{site.ColdMonth:0.0} warm{site.WarmMonth:0.0} dry{site.DrySummer:0.00} shrubs: " + string.Join(", ", gens.Take(6).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
                }
            }
        }
        if (args.Contains("--tiers"))
        {
            foreach (string pt in Opt("--tiers", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pt.Split(',');
                double la = double.Parse(parts[0], inv), lo = double.Parse(parts[1], inv);
                foreach (var c in configs.Where(c => c.Catalog != null))
                {
                    var site = h.Site(c.Catalog, la, lo, false);
                    Console.WriteLine($"   {pt} {c.Name,-26} [{c.Catalog.Geography.Code(site.Region)}] T{site.Temp} R{site.Rain} cold{site.ColdMonth:0.0} dry{site.DrySummer:0.00} F{site.Forest} " + Composition(c.Trees, site));
                }
            }
        }
        if (args.Contains("--points"))
        {
            // --points "57,25;64.5,26": per-generator selection shares averaged over a 3x3 grid of 0.1° around each point
            foreach (string pt in Opt("--points", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pt.Split(',');
                double la = double.Parse(parts[0], inv), lo = double.Parse(parts[1], inv);
                Console.WriteLine($"## {la},{lo}");
                foreach (var c in configs.Where(c => c.Name is "regional-vanilla" or "regional-full" or "legacy-full"))
                {
                    var (gens, woods) = Grid.Mix(h, c, la, lo);
                    Console.WriteLine($"   {c.Name,-18} by generator: " + string.Join(", ", gens.Where(kv => kv.Value >= 0.01).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
                    Console.WriteLine($"   {"",-18} by wood:      " + string.Join(", ", woods.Where(kv => kv.Value >= 0.01).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
                }
            }
        }
        if (args.Contains("--spots"))
        {
            foreach (var s in Spots)
            {
                Console.WriteLine($"## {s.Name} ({s.Lat},{s.Lon})");
                foreach (var c in configs.Where(c => c.Name is "regional-vanilla" or "regional-full" or "legacy-vanilla"))
                {
                    var site = h.Site(c.Catalog, s.Lat, s.Lon, false);
                    Console.WriteLine($"   {c.Name,-18} [{c.Catalog?.Geography.Code(site.Region)}] T{site.Temp} R{site.Rain} F{site.Forest} " + Composition(c.Trees, site));
                }
            }
        }
        if (args.Contains("--coverage"))
        {
            var full = configs.First(c => c.Name == "regional-full");
            foreach (var row in Grid.Coverage(h, full, h.ForestGrid(0.5)).OrderBy(r => r.Rule.Provider).ThenBy(r => r.Rule.Generators[0]))
                Console.WriteLine($"   {row.Rule.Provider,-34} {string.Join(" ", row.Rule.Generators.Select(g => g.Substring(5))),-48} eligible on {Harness.Pct(row.Eligible)}, >=0.5% of trees on {Harness.Pct(row.Presence)} of its native forest; mean share {Harness.Pct(row.MeanShare)}");
        }
        if (args.Contains("--latvia"))
        {
            foreach (var c in configs.Where(c => c.Catalog != null || c.Name == "legacy-vanilla"))
                Console.WriteLine($"Latvia {c.Name}: " + string.Join(", ", Grid.Latvia(h, c).Select(kv => kv.Key + " " + Harness.Pct(kv.Value))));
        }
        if (args.Contains("--fallback"))
        {
            // cells still using the climate fallback (tier 3) or treeless: their climate, to place analogue rules
            foreach (var c in configs.Where(c => c.Name is "regional-vanilla" or "regional-full"))
            {
                var tiers = new double[4];
                int shown = 0;
                foreach (var cell in grid)
                {
                    var site = h.Site(c.Catalog, cell.Lat, cell.Lon, false);
                    double success = c.Trees.Expected(site, null, tiers);
                    if (tiers[3] < 0.3 && success > 0.5) continue;
                    if (shown++ > 60) break;
                    Console.WriteLine($"   {c.Name} ({cell.Lat},{cell.Lon}) [{c.Catalog.Geography.Code(site.Region)}] T{site.Temp} R{site.Rain} cold{site.ColdMonth:0} dry{site.DrySummer:0.00} F{site.Forest} hRel{site.HeightRel:0.00} success {success:0.00} fallback {tiers[3]:0.00}");
                }
            }
        }
        if (args.Contains("--bench"))
        {
            var full = configs.First(c => c.Name == "regional-full");
            var (us, bytes) = Grid.Bench(h, full);
            Console.WriteLine($"Pick: {us.ToString("0.00", inv)} µs per attempt, {bytes} bytes allocated per attempt (full pack, 208 registrations)");
        }
    }

    public static string Composition(RegionalTreePicker picker, in TreeSite site)
    {
        var share = new float[picker.Count];
        var tiers = new double[4];
        double success = picker.Expected(site, share, tiers);
        var by = new Dictionary<string, double>();
        for (int i = 0; i < share.Length; i++) if (share[i] > 0) by[picker.Generator(i).Substring(5)] = by.GetValueOrDefault(picker.Generator(i).Substring(5)) + share[i];
        return $"success {success:0.00} (n{tiers[1]:0.00}/a{tiers[2]:0.00}/f{tiers[3]:0.00}): " +
            string.Join(", ", by.Where(kv => kv.Value / success >= 0.01).OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + (100 * kv.Value / success).ToString("0", CultureInfo.InvariantCulture) + "%"));
    }
}

static class Grid
{
    public sealed class RegionStat { public double Area, Treeless, Fallback; }
    public sealed class Result
    {
        public double Area, Treeless, Success;
        public double[] Tier = new double[4];
        public Dictionary<string, RegionStat> ByRegion = new();
    }

    public static Result Evaluate(Harness h, Harness.Config c, List<Harness.GridCell> grid)
    {
        var r = new Result();
        var tiers = new double[4];
        foreach (var cell in grid)
        {
            var site = h.Site(c.Catalog, cell.Lat, cell.Lon, false);
            double success = c.Trees.Expected(site, null, tiers);
            r.Area += cell.Area;
            r.Success += cell.Area * success;
            if (1 - success > 0.9) r.Treeless += cell.Area;
            for (int t = 1; t <= 3; t++) r.Tier[t] += cell.Area * tiers[t];
            if (c.Catalog != null)
            {
                string code = c.Catalog.Geography.Code(site.Region) ?? "-";
                if (!r.ByRegion.TryGetValue(code, out var s)) r.ByRegion[code] = s = new RegionStat();
                s.Area += cell.Area;
                if (1 - success > 0.9) s.Treeless += cell.Area;
                s.Fallback += cell.Area * tiers[3];
            }
        }
        r.Treeless /= r.Area; r.Success /= r.Area;
        for (int t = 1; t <= 3; t++) r.Tier[t] /= r.Area;
        return r;
    }

    public sealed class CoverageRow { public RegionalTreeCatalog.Rule Rule; public double Eligible, Presence, MeanShare, NativeArea; }

    /// <summary>Per mapped rule: share of its native forest-capable area where it is selected with probability ≥ 0.5%
    /// of trees, and its mean share of trees there.</summary>
    public static List<CoverageRow> Coverage(Harness h, Harness.Config c, List<Harness.GridCell> grid)
    {
        var rows = c.Catalog.Rules.Where(r => !r.Cultivated).ToDictionary(r => r, r => new CoverageRow { Rule = r });
        var share = new float[c.Trees.Count];
        foreach (var cell in grid)
        {
            var perRule = new Dictionary<RegionalTreeCatalog.Rule, double>();
            string code = null;
            foreach (bool wet in new[] { false, true })
            {
                var site = h.Site(c.Catalog, cell.Lat, cell.Lon, false, wet: wet, shore: wet);
                code = c.Catalog.Geography.Code(site.Region);
                if (code == null) break;
                double success = c.Trees.Expected(site, share);
                for (int i = 0; i < share.Length; i++)
                {
                    var rule = c.Trees.RuleOf(i);
                    bool special = rule?.Water is "riparian" or "shore";
                    if (rule != null && share[i] > 0 && special == wet) perRule[rule] = perRule.GetValueOrDefault(rule) + share[i] / Math.Max(1e-9, success);
                }
            }
            if (code == null) continue;
            foreach (var row in rows.Values)
            {
                if (!row.Rule.Regions.Contains(code)) continue;
                if (!c.TreeRegs.Any(t => row.Rule.Generators.Contains(t.Generator))) continue;
                row.NativeArea += cell.Area;
                double s = perRule.GetValueOrDefault(row.Rule);
                if (s > 0) row.Eligible += cell.Area;
                if (s >= 0.005) row.Presence += cell.Area;
                row.MeanShare += cell.Area * s;
            }
        }
        foreach (var row in rows.Values.Where(r => r.NativeArea > 0)) { row.Eligible /= row.NativeArea; row.Presence /= row.NativeArea; row.MeanShare /= row.NativeArea; }
        return rows.Values.Where(r => r.NativeArea > 0).ToList();
    }

    /// <summary>Latvian tree composition by category (expected shares over a 0.25° grid of Latvian forest land).</summary>
    public static Dictionary<string, double> Latvia(Harness h, Harness.Config c)
    {
        var cat = new Dictionary<string, double>();
        var share = new float[c.Trees.Count];
        double total = 0;
        for (double lat = 55.75; lat <= 57.9; lat += 0.25)
        for (double lon = 21.1; lon <= 28.1; lon += 0.25)
        {
            if (!h.IsLand(lat, lon)) continue;
            double fp = h.ForestPotential(lat, lon, out _, out _);
            if (fp < 0.3) continue;
            var site = h.Site(c.Catalog, lat, lon, false);
            if (c.Catalog != null && c.Catalog.Geography.Code(site.Region) != "BLT") continue;
            c.Trees.Expected(site, share);
            for (int i = 0; i < share.Length; i++)
            {
                if (share[i] <= 0) continue;
                string k = Category(c.Trees.Generator(i));
                cat[k] = cat.GetValueOrDefault(k) + share[i] * fp;
                total += share[i] * fp;
            }
        }
        return cat.ToDictionary(kv => kv.Key, kv => kv.Value / total).OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static string Category(string generator) => generator.Substring(5) switch
    {
        "scotspine" or "mountainpine" or "cedar" or "pinussibirica" => "pine",
        "fir" or "spruce" or "truefir" or "abiesbalsamea" or "piceaglauca" or "piceaglaucasmall" or "piceamariana" => "spruce/fir",
        "silverbirch" or "dwarfbirch" or "brideinwhite" or "thinbirch" or "betulaplatyphylla" or "riverbirch" or "himalayanbirch" => "birch",
        "larch" or "deepforestlarch" or "thinlarch" or "larixlaricina" => "larch",
        _ => "deciduous/other",
    };

    /// <summary>Selection shares (by generator and by wood) averaged over a 3x3 grid of 0.05° around a point, land only.</summary>
    public static (Dictionary<string, double> Generators, Dictionary<string, double> Woods) Mix(Harness h, Harness.Config c, double lat, double lon, bool shrubs = false, bool wet = false)
    {
        var picker = shrubs ? c.Shrubs : c.Trees;
        var share = new float[picker.Count];
        var gens = new Dictionary<string, double>();
        var woods = new Dictionary<string, double>();
        double total = 0;
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            double la = lat + 0.05 * i, lo = lon + 0.05 * j;
            if (!h.IsLand(la, lo)) continue;
            var site = h.Site(c.Catalog, la, lo, shrubs, wet: wet);
            double success = picker.Expected(site, share);
            if (success <= 0) continue;
            for (int k = 0; k < share.Length; k++)
            {
                if (share[k] <= 0) continue;
                string g = picker.Generator(k);
                gens[g.Substring(5)] = gens.GetValueOrDefault(g.Substring(5)) + share[k];
                string w = h.Woods.GetValueOrDefault(g) ?? g.Substring(5);
                woods[w] = woods.GetValueOrDefault(w) + share[k];
                total += share[k];
            }
        }
        if (total <= 0) return (gens, woods);
        return (gens.ToDictionary(kv => kv.Key, kv => kv.Value / total).OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                woods.ToDictionary(kv => kv.Key, kv => kv.Value / total).OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>Selection shares by genus label around a point (trees; inland/upland unless wet/shore).</summary>
    public static Dictionary<string, double> Genera(Harness h, Harness.Config c, double lat, double lon, bool wet = false, bool shore = false)
    {
        var share = new float[c.Trees.Count];
        var genera = new Dictionary<string, double>();
        double total = 0;
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            double la = lat + 0.05 * i, lo = lon + 0.05 * j;
            if (!h.IsLand(la, lo)) continue;
            var site = h.Site(c.Catalog, la, lo, false, wet: wet, shore: shore);
            if (c.Trees.Expected(site, share) <= 0) continue;
            for (int k = 0; k < share.Length; k++)
            {
                if (share[k] <= 0) continue;
                string g = Harness.Genus(c.Catalog, c.Trees.Generator(k));
                genera[g] = genera.GetValueOrDefault(g) + share[k];
                total += share[k];
            }
        }
        return total <= 0 ? genera : genera.ToDictionary(kv => kv.Key, kv => kv.Value / total).OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static (double Us, long Bytes) Bench(Harness h, Harness.Config c)
    {
        var sites = Report.Spots.Select(s => h.Site(c.Catalog, s.Lat, s.Lon, false)).ToArray();
        var rnd = new TestRandom(7);
        int sink = 0;
        for (int i = 0; i < 20000; i++) sink += c.Trees.Pick(sites[i % sites.Length], ref rnd, out _, out _);
        const int N = 400000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) sink += c.Trees.Pick(sites[i % sites.Length], ref rnd, out _, out _);
        sw.Stop();
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / N;
        GC.KeepAlive(sink);
        return (sw.Elapsed.TotalMilliseconds * 1000 / N, bytes);
    }
}
