using System.Globalization;
using EarthMap.Core;

/// <summary>
/// Soil rules 4 (decision 0105) offline, as a new world with Earth soil decides it: the soil maps of soil-rules-v4.json
/// (the 30″ set and the data store's 2′ maps in VintagestoryData/ModData/earth), the climate as new worlds load it, the
/// floodplain grid at GLO-90 heights, the wetland grid. Rivers are Earth's terrain and are not built here, so a river's
/// green belt (TerrainModel.RiparianRain) is not counted: a column right beside a desert river may have soil in the game
/// where this says none. Prints the named places, transects across the Nile at Luxor and the Euphrates and Tigris at
/// Baghdad, and each tier set's shares of land. Run: SoilTests &lt;root&gt; census &lt;ModData/earth&gt; [step in degrees].
/// </summary>
static class Census
{
    public static int Run(string root, string dataFolder, double step)
    {
        string data(string name) => Path.Combine(dataFolder, "data", name);
        string rules = File.ReadAllText(Path.Combine(root, "assets/earth/config/soil-rules-v4.json"));
        GroundTile.BudgetBytes = 4L << 30;
        var ts = Topsoil.Load(rules, a => { var f = data(Path.GetFileName(a.Split(':')[1])); return File.Exists(f) ? File.ReadAllBytes(f) : null; }, 1234,
            f => Path.Combine(dataFolder, f));
        Console.WriteLine("Soil maps missing: " + (ts.MissingTiles.Count == 0 ? "none (30\" set read)" : string.Join(", ", ts.MissingTiles)));
        var earth = EarthData.LoadFromFolder(root, true);
        earth.SetFineTemperature(true); earth.SetFineRain(true); earth.SetClimateFix(true);
        var floods = FloodplainGrid.Read(File.ReadAllBytes(data("floodplains.bin")));
        var wet = WetlandGrid.Read(File.ReadAllBytes(data("wetlands.bin")));
        var dem = new Dictionary<(int, int), DemTiles.Tile>();
        double Glo90(double lat, double lon)
        {
            int s = (int)Math.Floor(lat), w = (int)Math.Floor(lon);
            DemTiles.Tile t;
            lock (dem)
            {
                if (!dem.TryGetValue((s, w), out t))
                {
                    string p = Path.Combine(dataFolder, "glo90", DemTiles.ShortName(s, w) + ".egl");
                    t = File.Exists(p) ? DemTiles.Decode(File.ReadAllBytes(p)) : null;
                    if (dem.Count > 400) dem.Clear();
                    dem[(s, w)] = t;
                }
            }
            if (t == null) return double.NaN;
            double r = (s + 1 - lat) * t.Rows, c = (lon - w) * t.Cols;
            int r0 = Math.Clamp((int)r, 0, t.Rows - 1), c0 = Math.Clamp((int)c, 0, t.Cols - 1);
            return t.At(r0, c0);
        }
        (Topsoil.Place Place, double Elev) PlaceAt(double lat, double lon, bool fine)
        {
            var cs = earth.SampleClimate(lat, lon);
            double elev = fine ? Glo90(lat, lon) : double.NaN;
            if (double.IsNaN(elev)) elev = earth.SampleTerrain(lat, lon).Elev;
            var p = new Topsoil.Place { Class = ts.ClassAt(lat, lon), Temperature = cs.TMean - .0065 * Math.Max(0, elev), WarmestMonth = cs.TWarm - .0065 * Math.Max(0, elev) };
            if (double.IsNaN(cs.Rain)) { p.Class = null; return (p, elev); }
            p.Arid = SoilFertility.AridGround(cs.Rain, cs.DrySummer, cs.PenmanFactor);
            if (!double.IsNaN(floods.Level(lat, lon)))
            {
                double e = fine ? elev : Glo90(lat, lon);
                p.Flood = !double.IsNaN(e) && floods.Floods(lat, lon, e);
            }
            p.Dune = ts.DuneAt(lat, lon);
            return (p, elev);
        }
        string Name(Topsoil.Tier t) => t switch { Topsoil.Tier.Vanilla => "vanilla's rule", Topsoil.Tier.None => "none", Topsoil.Tier.BlackEarth => "black earth", _ => Topsoil.TierNames[(int)t] };
        int fails = 0;

        // ------------------------------------------------------------ regions (EA-0199's census)
        var regions = new (string Name, double S, double N, double W, double E, double Step, Func<Dictionary<Topsoil.Tier, double>, Dictionary<Topsoil.Tier, double>, bool> Ok, string Want)[]
        {
            ("Poltava steppe", 49.0, 50.5, 33.0, 35.5, .05, (v, r) => v[Topsoil.Tier.High] >= .5 && r[Topsoil.Tier.BlackEarth] + r[Topsoil.Tier.High] >= .5, "high (vanilla tiers), black earth or high (realistic) on most of it"),
            ("Zemgale plain", 56.3, 56.8, 23.2, 24.6, .02, (v, r) => v[Topsoil.Tier.Medium] >= .5 && r[Topsoil.Tier.Medium] >= .5, "medium on most of it"),
            ("Central Europe", 49.0, 52.5, 7.0, 16.0, .05, (v, r) => v[Topsoil.Tier.Medium] >= .5 && r[Topsoil.Tier.Medium] >= .5, "medium on most of it"),
            ("Boreal Finland", 61.0, 65.0, 24.0, 30.0, .05, (v, r) => true, "podzols low in both tier sets (barren beyond the tree line); other classes by the table"),
            ("Karelia and Arkhangelsk", 61.0, 64.0, 33.0, 42.0, .05, (v, r) => true, "podzols low in both tier sets (barren beyond the tree line); other classes by the table"),
            ("Canadian shield", 48.0, 52.0, -90.0, -80.0, .05, (v, r) => true, "podzols low in both tier sets (barren beyond the tree line); other classes by the table"),
            ("Amazon", -10.0, 0.0, -70.0, -55.0, .1, (v, r) => v[Topsoil.Tier.Low] + v[Topsoil.Tier.Barren] >= .8 && r[Topsoil.Tier.Low] + r[Topsoil.Tier.Barren] >= .8, "low or barren"),
            ("Sahara", 18.0, 30.0, -10.0, 30.0, .1, (v, r) => v[Topsoil.Tier.None] >= .95 && r[Topsoil.Tier.None] >= .95, "none"),
        };
        Console.WriteLine();
        Console.WriteLine("Regions (share of land: vanilla tiers | realistic tiers; main soil classes):");
        string Shares(Dictionary<Topsoil.Tier, double> d) => string.Join(" ", d.Where(x => x.Value >= .005).OrderBy(x => x.Key).Select(x => $"{Name(x.Key)} {100 * x.Value:0}%"));
        foreach (var reg in regions)
        {
            var sv = Enum.GetValues<Topsoil.Tier>().ToDictionary(x => x, _ => 0.0); var sr = Enum.GetValues<Topsoil.Tier>().ToDictionary(x => x, _ => 0.0);
            var cls = new Dictionary<string, double>(); var podzolOk = true; double n = 0;
            for (double lat = reg.S + reg.Step / 2; lat < reg.N; lat += reg.Step)
                for (double lon = reg.W + reg.Step / 2; lon < reg.E; lon += reg.Step)
                {
                    var terrain = earth.SampleTerrain(lat, lon);
                    if (terrain.Ocean > .5 || terrain.Lake > .5) continue;
                    var (p, _) = PlaceAt(lat, lon, false);
                    if (p.Class == null) continue;
                    var v = ts.Decide(Topsoil.VanillaTiers, p); var r = ts.Decide(Topsoil.RealisticTiers, p);
                    sv[v]++; sr[r]++; n++; cls[p.Class] = cls.GetValueOrDefault(p.Class) + 1;
                    // Podzols low in both tier sets, barren in both only beyond the tree line
                    if (p.Class == "Podzols" && !(v == Topsoil.Tier.Low && r == Topsoil.Tier.Low || v == Topsoil.Tier.Barren && r == Topsoil.Tier.Barren)) podzolOk = false;
                }
            foreach (var k in sv.Keys.ToList()) { sv[k] /= Math.Max(1, n); sr[k] /= Math.Max(1, n); }
            bool ok = n > 0 && reg.Ok(sv, sr) && podzolOk;
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {reg.Name} (want {reg.Want}):");
            Console.WriteLine($"         vanilla tiers   {Shares(sv)}");
            Console.WriteLine($"         realistic tiers {Shares(sr)}");
            Console.WriteLine($"         classes         {string.Join(", ", cls.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key} {100 * x.Value / n:0}%"))}");
        }

        // ------------------------------------------------------------ named places (information)
        var places = new (string Name, double Lat, double Lon)[]
        {
            ("Poltava", 49.60, 34.55), ("Jelgava", 56.62, 23.80), ("Bauska", 56.40, 24.20), ("Goettingen", 51.53, 9.93), ("Kolin", 50.03, 15.20),
            ("Dijon", 47.32, 5.04), ("Kuopio", 62.90, 27.68), ("Manaus", -3.50, -62.00), ("Iowa, Corn Belt", 42.00, -93.50), ("Pampas", -35.00, -61.00),
            ("Iceland interior", 64.80, -18.00), ("Reykjavik lowland", 64.05, -21.50), ("Alps, 2700 m", 46.55, 8.40), ("Yakutsk taiga", 62.03, 129.70),
            ("Red River delta", 20.60, 106.30), ("Ganges plain", 26.50, 82.00),
            // the dedicated-server audit's sites
            ("Jyvaskyla", 62.30, 25.70), ("Luxor, valley", 25.70, 32.62), ("Luxor, desert", 25.70, 32.80), ("Baghdad", 33.30, 44.10), ("Sahara", 25.00, 28.00),
        };
        Console.WriteLine();
        Console.WriteLine("Named places (class, yearly and warmest month at height, flags; vanilla tiers / realistic tiers):");
        foreach (var (name, lat, lon) in places)
        {
            var (p, elev) = PlaceAt(lat, lon, true);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-20} {1,-12} {2,5:0.0} / {3,5:0.0} °C {4,6:0} m{5}{6}{7}  {8} / {9}",
                name, p.Class ?? "-", p.Temperature, p.WarmestMonth, elev, p.Arid ? " arid" : "", p.Flood ? " flood" : "", p.Dune ? " dune" : "",
                Name(ts.Decide(Topsoil.VanillaTiers, p)), Name(ts.Decide(Topsoil.RealisticTiers, p))));
        }

        // ------------------------------------------------------------ across the floods
        foreach (var (name, lat, lon0, lon1, dlon) in new[] { ("Nile at Luxor", 25.70, 32.45, 32.85, .005), ("Euphrates and Tigris at Baghdad", 33.30, 43.40, 45.20, .02) })
        {
            Console.WriteLine($"\n{name}, along {lat:0.00} N (realistic tiers; vanilla tiers the same except where noted):");
            string last = null; double from = lon0;
            int soil = 0, n = 0, soilOutside = 0, bareInside = 0;
            for (double lon = lon0; lon <= lon1 + 1e-9; lon += dlon)
            {
                var (p, elev) = PlaceAt(lat, lon, true);
                var r = ts.Decide(Topsoil.RealisticTiers, p);
                var v = ts.Decide(Topsoil.VanillaTiers, p);
                n++;
                if (r >= Topsoil.Tier.Barren) soil++;
                if (r >= Topsoil.Tier.Barren && !p.Flood && p.Arid) soilOutside++;
                if (p.Flood && r < Topsoil.Tier.Barren) bareInside++;
                string label = $"{(p.Flood ? "flood" : "dry")}, {p.Class ?? "-"}: {Name(r)}{(v != r ? " (vanilla tiers " + Name(v) + ")" : "")}";
                if (label != last)
                {
                    if (last != null) Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0:0.000}-{1:0.000} E  {2}", from, lon - dlon, last));
                    last = label; from = lon;
                }
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0:0.000}-{1:0.000} E  {2}", from, lon1, last));
            bool ok = soil > 0 && soilOutside == 0 && bareInside == 0;
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "ok" : "FAIL")}: {soil} of {n} points have soil; soil outside the flood {soilOutside}, flooded points without soil {bareInside}");
        }

        // ------------------------------------------------------------ shares of land
        var count = new double[3, 9];   // tier set (0 vanilla, 1 realistic), tier + 2
        double landW = 0;
        var byClass = new Dictionary<string, double>();
        int rows = (int)Math.Round(144 / step);
        Parallel.For(0, rows, () => (new double[3, 9], 0.0), (row, _, local) =>
        {
            double lat = 84 - (row + .5) * step;
            double w = Math.Cos(lat * Math.PI / 180);
            for (double lon = -180 + step / 2; lon < 180; lon += step)
            {
                var terrain = earth.SampleTerrain(lat, lon);
                if (terrain.Ocean > .5 || terrain.Lake > .5) continue;
                var (p, _) = PlaceAt(lat, lon, false);
                if (p.Class == null) continue;
                local.Item2 += w;
                local.Item1[0, (int)ts.Decide(Topsoil.VanillaTiers, p) + 2] += w;
                local.Item1[1, (int)ts.Decide(Topsoil.RealisticTiers, p) + 2] += w;
            }
            return local;
        }, local => { lock (count) { landW += local.Item2; for (int s = 0; s < 2; s++) for (int t = 0; t < 9; t++) count[s, t] += local.Item1[s, t]; } });
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nShares of mapped land every {0}° (84 N to 60 S, by area):", step));
        Console.WriteLine("  set          vanilla's rule   none   | barren   low   medium   high   black earth  (of land with topsoil)");
        var expected = new[] { new double[] { 10, 48, 37, 5, 0 }, new double[] { 30, 27, 35, 5, 3 } };
        for (int s = 0; s < 2; s++)
        {
            double withSoil = 0; for (int t = 2; t < 7; t++) withSoil += count[s, t];
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-12} {1,10:0.0}%  {2,6:0.0}%  | {3,6:0.0}% {4,6:0.0}% {5,7:0.0}% {6,6:0.0}% {7,8:0.0}%",
                s == 0 ? "vanilla" : "realistic", 100 * count[s, 0] / landW, 100 * count[s, 1] / landW,
                100 * count[s, 2] / withSoil, 100 * count[s, 3] / withSoil, 100 * count[s, 4] / withSoil, 100 * count[s, 5] / withSoil, 100 * count[s, 6] / withSoil));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-12} {1,10}   {2,6}   | {3,6:0}% {4,6:0}% {5,7:0}% {6,6:0}% {7,8:0}%   (section 4.1's estimate)",
                "", "", "", expected[s][0], expected[s][1], expected[s][2], expected[s][3], expected[s][4]));
        }
        Console.WriteLine(fails == 0 ? "\nCensus: every named place and transect as expected." : $"\nCensus: {fails} place(s) or transect(s) not as expected.");
        return fails == 0 ? 0 : 1;
    }
}
