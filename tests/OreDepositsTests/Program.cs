using System.Diagnostics;
using System.Globalization;
using System.Text;
using EarthMap.Core;
using static EarthMap.Core.OreDepositCatalog;

// Offline test of decision 0065 (ore rules v4): the deposit catalogue read, checked and placed at 1:400, 1:100 and 1:10
// through model columns. Usage: dotnet run -- [catalogue.json ...]   (default: the test catalogue next to this file; more
// files, such as a group's catalogue, are read and planned too, as a smoke test).
string here = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
var extra = args.Where(File.Exists).ToArray();
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } else Console.WriteLine("ok   " + what); }
string F(double v, string f = "0.###") => (f.Contains(';') ? Math.Round(v, 1) + 0.0 : v).ToString(f, CultureInfo.InvariantCulture);
var clock = Stopwatch.StartNew();

// ---------------------------------------------------------------- loading and checks
var cat = new OreDepositCatalog();
cat.AddFile("test-catalogue.json", File.ReadAllText(Path.Combine(here, "test-catalogue.json")));
cat.AddFile("broken.json", "{ \"version\": 1, \"deposits\": [ {\"id\": ");
cat.AddFile("old.json", "{ \"version\": 0, \"deposits\": [] }");
Console.WriteLine("Catalogue: " + cat.Deposits.Count + " deposits, " + cat.Areas.Count + " areas, " + cat.Skipped + " skipped; problems:");
foreach (var p in cat.Problems) Console.WriteLine("     " + p);
Check(cat.Deposits.Count == 6 && cat.Areas.Count == 1, "six good deposits and one good area are read");
Check(cat.Skipped == 5, "five bad entries are skipped (no tonnage, unknown shape, latitude out of range, duplicate id, line geometry)");
Check(cat.Problems.Any(p => p.StartsWith("broken.json")) && cat.Problems.Any(p => p.StartsWith("old.json")), "a broken file and an unknown schema version are logged and skipped");
Check(Math.Abs(cat.Deposits.First(d => d.Id == "t:stock").Ores.Sum(o => o.Share) - 1) < 1e-9, "ore shares sum to 1");
Check(cat.Deposits.First(d => d.Id == "t:dome").Density == OreDepositCatalog.DefaultDensity(Shape.Dome, "halite"), "a missing density takes the ore's default");

// ---------------------------------------------------------------- model columns
var all = cat.Deposits.SelectMany(d => d.Ores).Concat(cat.Areas.SelectMany(a => a.Beds.Select(b => b.Ore)));
foreach (var o in all) o.Slot = 0;

OreScale ScaleFor(double h, GroundColumns.DepthCurve curve)
{
    double bpd = OreScale.MetresPerDegree / h;
    return new OreScale(0, 360 * bpd, 0, 90 * bpd, bpd, -169.25, curve);
}
var scales = new (string Name, double H)[] { ("1:400", 400), ("1:100", 100), ("1:10", 10) };

long PlaceAll(OreDepositPlan plan, OreDepositPlan.Body b, TestColumn col)
{
    plan.Ensure(b);
    long n = 0;
    for (int z = b.MinZ; z <= b.MaxZ; z++) for (int x = b.MinX; x <= b.MaxX; x++) n += (long)plan.PlaceBody(b, x, z, col);
    return n;
}
double PlaceArea(OreDepositPlan plan, OreDepositPlan.AreaPlan a, TestColumn col)
{
    var xs = new List<double>();
    double n = 0;
    for (int z = a.MinZ; z <= a.MaxZ; z++)
    {
        plan.Row(a, z, xs);
        if (xs.Count < 2) continue;
        for (int x = (int)Math.Floor(xs[0]) - 1; x <= (int)Math.Ceiling(xs[^1]) + 1; x++)
            if (OreDepositPlan.Inside(xs, x + .5)) n += plan.PlaceArea(a, x, z, col);
    }
    return n;
}

// ---------------------------------------------------------------- volume at three scales
Console.WriteLine();
Console.WriteLine("Volume: placed blocks against real volume / (h_x h_z v), linear curve (v = 20 m), and against the planned target on the default log curve");
foreach (var (name, h) in scales)
{
    foreach (bool linear in new[] { true, false })
    {
        var curve = linear ? new GroundColumns.DepthCurve(20, double.PositiveInfinity) : GroundColumns.DepthCurve.Fit(20, 4000, 110);
        var scale = ScaleFor(h, curve);
        var plan = new OreDepositPlan(cat, scale, 1234, 1);
        foreach (var b in plan.Bodies)
        {
            var col = new TestColumn();
            long placed = PlaceAll(plan, b, col);
            double real = b.D.Mt * 1e6 / b.D.Density / (scale.HxAt(b.D.Lat) * scale.Hz * 20);
            double reference = linear ? real : b.Target;
            double err = reference > 0 ? placed / reference - 1 : 0;
            // a body of a few blocks is a chance draw: its tolerance is the draw's spread
            double tol = Math.Max(.03, 3 / Math.Sqrt(Math.Max(1, reference)));
            Console.WriteLine($"     {name} {(linear ? "linear" : "log4000")} {b.D.Id,-12} {b.D.Shape,-8} target {F(b.Target, "0.0"),12} real {F(real, "0.0"),12} placed {placed,10} ({F(100 * err, "+0.0;-0.0")}%){(b.Compact ? " compact" : "")} fill {F(b.Fill)} sheets {b.Sheets}");
            if (linear) Check(Math.Abs(b.Target / real - 1) < .02 || b.D.Shape == Shape.Lens && Math.Abs(b.Target / real - 1) < .05, $"{name} {b.D.Id}: the target is the real volume over the block volume ({F(b.Target, "0.0")} vs {F(real, "0.0")})");
            Check(Math.Abs(err) <= tol, $"{name} {(linear ? "linear" : "log")} {b.D.Id}: placed within {F(100 * tol, "0")}% of the {(linear ? "real volume" : "target")} ({F(100 * err, "+0.0;-0.0")}%)");
        }
    }
}

// areas: a rectangle of about a million columns at 1:400 and 1:100 and 2.5 million at 1:10
Console.WriteLine();
foreach (var (name, h) in scales)
{
    var curve = new GroundColumns.DepthCurve(20, double.PositiveInfinity);
    var scale = ScaleFor(h, curve);
    double w = h >= 400 ? 4 : h >= 100 ? 1 : .2, lat0 = 51.3;
    var c2 = new OreDepositCatalog();
    c2.AddFile("area.json", "{\"version\":1,\"areas\":[{\"id\":\"a\",\"layer\":\"C_coal\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[7.0," + F(lat0, "0.######") + "],[" + F(7 + w, "0.######") + "," + F(lat0, "0.######") + "],[" + F(7 + w, "0.######") + "," + F(lat0 + w / 2, "0.######") + "],[7.0," + F(lat0 + w / 2, "0.######") + "]]]},\"beds\":[{\"code\":\"bituminouscoal\",\"topM\":30,\"thickM\":6,\"cover\":0.8},{\"code\":\"anthracite\",\"topM\":600,\"thickM\":3,\"cover\":0.6}]}]}");
    foreach (var o in c2.Areas.SelectMany(a => a.Beds)) o.Ore.Slot = 0;
    var plan = new OreDepositPlan(c2, scale, 99, 1);
    var col = new TestColumn();
    double placed = PlaceArea(plan, plan.Areas[0], col);
    // real area of the rectangle on the sphere, its beds' real volume over the block volume at the mean latitude
    double latMid = lat0 + w / 4, areaReal = w * (w / 2) * OreScale.MetresPerDegree * OreScale.MetresPerDegree * Math.Cos(latMid * Math.PI / 180);
    double real = areaReal * (.8 * 6 + .6 * 3) / (scale.HxAt(latMid) * scale.Hz * 20);
    double expected = plan.AreaExpected(plan.Areas[0]);
    double err = placed / real - 1, planErr = expected / real - 1;
    // a bed is present in patches about 3 km across: the share of them in a small basin is a chance draw
    double cells = w * (w / 2) * scale.BlocksPerDegreeX * scale.BlocksPerDegreeZ / ((double)plan.Areas[0].PatchCell * plan.Areas[0].PatchCell);
    double tol = Math.Max(.03, 3 * Math.Sqrt(.8 * .2 / cells) / .8);
    Console.WriteLine($"     {name} basin {F(w, "0.###")}° x {F(w / 2, "0.###")}°: real {F(real, "0")} blocks, expected by the plan {F(expected, "0")} ({F(100 * planErr, "+0.0;-0.0")}%), placed {F(placed, "0")} ({F(100 * err, "+0.0;-0.0")}%; {F(cells, "0")} patch cells)");
    Check(Math.Abs(planErr) < .02, $"{name} basin beds: the plan's expected blocks are within 2% of the real volume ({F(100 * planErr, "+0.0;-0.0")}%)");
    Check(Math.Abs(err) <= tol, $"{name} basin beds: placed within {F(100 * tol, "0")}% of the real volume, the spread of {F(cells, "0")} patches ({F(100 * err, "+0.0;-0.0")}%)");
}

// sub-block bodies: many small deposits keep their expected value
Console.WriteLine();
{
    var rnd = new Random(5);
    var sb = new StringBuilder("{\"version\":1,\"deposits\":[");
    for (int i = 0; i < 3000; i++)
        sb.Append(i > 0 ? "," : "").Append("{\"id\":\"s" + i + "\",\"lat\":" + F(-50 + rnd.NextDouble() * 100, "0.####") + ",\"lon\":" + F(-160 + rnd.NextDouble() * 320, "0.####")
            + ",\"layer\":\"Au\",\"ores\":[{\"code\":\"quartz_nativegold\",\"share\":1}],\"shape\":\"veins\",\"mt\":" + F(.002 + rnd.NextDouble() * .1, "0.#####")
            + ",\"topM\":0,\"baseM\":300,\"lenKm\":0.3,\"widKm\":0.05,\"dipDeg\":80}");
    sb.Append("]}");
    var small = new OreDepositCatalog();
    small.AddFile("small.json", sb.ToString());
    foreach (var o in small.Deposits.SelectMany(d => d.Ores)) o.Slot = 0;
    foreach (var (name, h) in scales)
    {
        var plan = new OreDepositPlan(small, ScaleFor(h, GroundColumns.DepthCurve.Fit(20, 4000, 110)), 77, 1);
        double target = 0; long placed = 0; int compact = 0;
        foreach (var b in plan.Bodies) { placed += PlaceAll(plan, b, new TestColumn()); target += b.Target; if (b.Compact) compact++; }
        double err = placed / target - 1, tol = Math.Max(.03, 3 / Math.Sqrt(target));
        Console.WriteLine($"     {name}: 3000 small vein deposits, expected {F(target, "0.0")} blocks, placed {placed} ({F(100 * err, "+0.0;-0.0")}%), {compact} compact");
        Check(Math.Abs(err) <= tol, $"{name} sub-block and small bodies keep their expected value within {F(100 * tol, "0")}% ({F(100 * err, "+0.0;-0.0")}%)");
    }
}

// ---------------------------------------------------------------- layers
Console.WriteLine();
{
    // a model column: soil, alluvium, cover, Lower Paleozoic sandstone (not evaporite) down to 1000 m, granite basement
    var layerCurve = GroundColumns.DepthCurve.Fit(20, 4000, 110);
    int bedBottom = (int)Math.Floor(layerCurve.Blocks(1000));
    string Layer(int d) => d == 0 ? "cover" : d <= 3 ? "alluvial" : d <= 5 ? "cover" : d < bedBottom ? "bed" : "basement";
    bool Allowed(Ore o, int d, OreWhere w)
    {
        string layer = Layer(d);
        if (w.Classes != 0) return (w.Classes & 1 << Array.IndexOf(OreLayers.ClassNames, layer)) != 0;   // the body's own layers
        return o.Code switch
        {
            "earth-placergold" => layer == "alluvial",
            "bituminouscoal" or "anthracite" or "halite" => layer == "bed" && w.Basin,   // bed age and evaporite fail; a basin takes any sedimentary bed
            "sulfur" => layer == "bed",
            _ => layer is "bed" or "basement",
        };
    }
    var scale = ScaleFor(100, GroundColumns.DepthCurve.Fit(20, 4000, 110));
    var plan = new OreDepositPlan(cat, scale, 4321, 1);
    var byCode = new Dictionary<string, long>();
    long violations = 0;
    foreach (var b in plan.Bodies)
    {
        var col = new TestColumn { Gate = Allowed, Layer = Layer };
        PlaceAll(plan, b, col);
        violations += col.Violations;
        foreach (var (k, v) in col.ByCode) byCode[k] = byCode.GetValueOrDefault(k) + v;
        foreach (var (k, v) in col.ByLayer) Console.WriteLine($"     {b.D.Id,-12} {k}");
    }
    var area = new TestColumn { Gate = Allowed, Layer = Layer };
    PlaceArea(plan, plan.Areas[0], area);
    violations += area.Violations;
    Console.WriteLine("     placed by code: " + string.Join(", ", byCode.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value)) + "; basin: " + string.Join(", ", area.ByCode.Select(p => p.Key + " " + p.Value)));
    Check(violations == 0, "nothing is placed where the column's layer does not allow it");
    Check(cat.Deposits.First(d => d.Id == "t:blanket").Classes == OreDepositCatalog.BlanketLayers, "a blanket naming no layers takes alluvial, outwash, laterite and saprolite");
    Check(byCode.GetValueOrDefault("earth-placergold") > 0, "the placer blanket finds the alluvium below the soil");
    Check(byCode.GetValueOrDefault("bituminouscoal") == 0, "coal outside a basin stays out of a bed of the wrong age");
    Check(area.ByCode.GetValueOrDefault("bituminouscoal") > 0, "coal in a catalogue basin takes the sedimentary bed of the wrong age");
    Check(area.ByCode.GetValueOrDefault("anthracite") > 0, "the deep anthracite bed (600 m) lies in the sandstone too");
    Check(byCode.GetValueOrDefault("halite") > 0, "a salt dome takes the sedimentary beds it pierces");
    Check(byCode.GetValueOrDefault("chalcopyrite") > 0 && byCode.GetValueOrDefault("sulfur") > 0, "hard-rock ores fill bed and basement; the dome's sulfur cap sits in the bed");
}

// ---------------------------------------------------------------- grade
Console.WriteLine();
{
    var scale = ScaleFor(100, new GroundColumns.DepthCurve(20, double.PositiveInfinity));
    var plan = new OreDepositPlan(cat, scale, 1, 1);
    var stock = plan.Bodies.First(b => b.D.Id == "t:stock");
    var col = new TestColumn();
    PlaceAll(plan, stock, col);
    double total = col.Tiers.Sum();
    var share = col.Tiers.Select(t => t / total).ToArray();
    // lognormal sigma .5 around 0.88 % Cu with bands 0.6 / 1.5 / 4.0: 30.3 % poor, 60.3 % medium, 9.4 % rich, 0.05 % bountiful
    double[] expect = { .3027, .6030, .0938, .0005 };
    Console.WriteLine("     t:stock tiers (poor/medium/rich/bountiful): " + string.Join(" / ", share.Select(s => F(100 * s, "0.0") + "%")));
    Check(share.Zip(expect).All(p => Math.Abs(p.First - p.Second) < .02), "graded tiers of a 0.88 % copper body follow the bands with the body's spread");
    var lens = plan.Bodies.First(b => b.D.Id == "t:lens");
    var lc = new TestColumn();
    PlaceAll(plan, lens, lc);
    var cu = lc.TiersByCode["chalcopyrite"]; var zn = lc.TiersByCode["sphalerite"];
    Check(cu[1] + cu[2] > .9 * cu.Sum() && cu[2] > cu[0], "a 2.1 % Cu body's chalcopyrite is mostly medium and rich, richer than poor");
    // its sphalerite takes its tier from the body's 12 % Zn (oreMetal and grades): 20 % medium or poor, 80 % rich or bountiful
    Console.WriteLine("     t:lens sphalerite tiers by its own 12 % Zn: " + string.Join(" / ", zn.Select(t => F(100.0 * t / zn.Sum(), "0.0") + "%")));
    Check(zn[2] + zn[3] > .7 * zn.Sum() && zn[3] > zn[0], "a co-product ore takes its tier from its own metal's grade (sphalerite at 12 % Zn mostly rich and bountiful)");
    var dome = plan.Bodies.First(b => b.D.Id == "t:dome");
    var dc = new TestColumn();
    PlaceAll(plan, dome, dc);
    Check(dc.Tiers[1] == dc.Tiers.Sum(), "a body without a grade or band takes the medium tier");
    // zones: malachite is the top 15 % of the porphyry, sphalerite the bottom 60 % of the lens
    double mal = col.ByCode.GetValueOrDefault("malachite") / total;
    Check(Math.Abs(mal - .15) < .01, $"top-zone malachite keeps its 15 % share ({F(100 * mal, "0.0")}%)");
    Check(col.MeanDepth("malachite") < col.MeanDepth("chalcopyrite") && lc.MeanDepth("sphalerite") > lc.MeanDepth("chalcopyrite"), "top ores sit above, bottom ores below the rest");
}

// ---------------------------------------------------------------- seamless and deterministic
Console.WriteLine();
{
    var scale = ScaleFor(100, GroundColumns.DepthCurve.Fit(20, 4000, 110));
    var a = new OreDepositPlan(cat, scale, 42, 1);
    var b = new OreDepositPlan(cat, scale, 42, 1);
    var veinsA = a.Bodies.First(x => x.D.Id == "t:veins");
    var veinsB = b.Bodies.First(x => x.D.Id == "t:veins");
    a.Ensure(veinsA); b.Ensure(veinsB);
    var ca = new TestColumn { Keep = true }; var cb = new TestColumn { Keep = true };
    // one plan walks the body column by column, the other chunk by chunk in reverse chunk order
    for (int z = veinsA.MinZ; z <= veinsA.MaxZ; z++) for (int x = veinsA.MinX; x <= veinsA.MaxX; x++) { ca.X = x; ca.Z = z; a.PlaceBody(veinsA, x, z, ca); }
    for (int cz = veinsB.MaxZ >> 5; cz >= veinsB.MinZ >> 5; cz--)
        for (int cx = veinsB.MaxX >> 5; cx >= veinsB.MinX >> 5; cx--)
            foreach (var bb in b.BodiesNear(cx * 32, cz * 32, cx * 32 + 31, cz * 32 + 31).Where(q => q.D.Id == "t:veins"))
                for (int z = cz * 32; z < cz * 32 + 32; z++) for (int x = cx * 32; x < cx * 32 + 32; x++) { cb.X = x; cb.Z = z; b.PlaceBody(bb, x, z, cb); }
    Check(ca.Blocks.SetEquals(cb.Blocks) && ca.Blocks.Count > 0, $"the same seed places the same {ca.Blocks.Count} blocks whatever the chunk order");
    var c = new OreDepositPlan(cat, scale, 43, 1);
    var veinsC = c.Bodies.First(x => x.D.Id == "t:veins");
    var cc = new TestColumn { Keep = true };
    c.Ensure(veinsC);
    for (int z = veinsC.MinZ; z <= veinsC.MaxZ; z++) for (int x = veinsC.MinX; x <= veinsC.MaxX; x++) { cc.X = x; cc.Z = z; c.PlaceBody(veinsC, x, z, cc); }
    Check(!cc.Blocks.SetEquals(ca.Blocks), "another seed places other blocks");
    double expected = 0;
    var stockA = a.Bodies.First(x => x.D.Id == "t:stock");
    var pc = new TestColumn();
    var sc = new TestColumn();
    PlaceAll(a, stockA, sc);
    for (int z = stockA.MinZ; z <= stockA.MaxZ; z++) for (int x = stockA.MinX; x <= stockA.MaxX; x++) expected += a.PlaceBody(stockA, x, z, pc, place: false, only: "chalcopyrite");
    long chalco = sc.ByCode.GetValueOrDefault("chalcopyrite");
    Check(Math.Abs(expected / chalco - 1) < .03, $"the prospecting reading's expected chalcopyrite matches what is placed ({F(expected, "0")} vs {chalco})");
    var half = new OreDepositPlan(cat, scale, 42, .5);
    Check(Math.Abs(half.Bodies.First(x => x.D.Id == "t:veins").Target / veinsA.Target - .5) < 1e-9, "the world's deposit rate multiplies the volume");
}

// ---------------------------------------------------------------- a body's own layers and codes without a provider (0065 §4)
Console.WriteLine();
{
    var own = new OreDepositCatalog();
    own.AddFile("layers.json", """
{ "version": 1, "group": "test",
  "deposits": [
    {"id": "t:laterite", "name": "Test nickel laterite", "lat": -21.3, "lon": 165.4, "layer": "Ni",
     "ores": [{"code": "limonite", "share": 0.6, "zone": "top"}, {"code": "pentlandite", "share": 0.4, "zone": "bottom"}],
     "shape": "blanket", "mt": 2000, "topM": 0, "baseM": 60, "lenKm": 6, "widKm": 3, "layers": ["laterite", "saprolite"], "src": "test", "q": "C"},
    {"id": "t:sands", "name": "Test mineral sands", "lat": -33.6, "lon": 115.3, "layer": "Ti",
     "ores": [{"code": "ilmenite", "share": 1}], "shape": "blanket", "mt": 20, "topM": 0, "baseM": 15, "lenKm": 8, "widKm": 1,
     "layers": ["alluvial", "cover", "dunes"], "src": "test", "q": "C"},
    {"id": "t:mixed", "name": "Test copper and platinum", "lat": 59.0, "lon": 59.5, "layer": "Cu",
     "ores": [{"code": "chalcopyrite", "share": 0.5}, {"code": "nativeplatinum", "share": 0.5}],
     "shape": "stock", "mt": 100, "topM": 0, "baseM": 400, "lenKm": 1, "widKm": 1, "src": "test", "q": "C"},
    {"id": "t:pt-only", "name": "Test platinum placer", "lat": 59.2, "lon": 59.6, "layer": "Pt",
     "ores": [{"code": "nativeplatinum", "share": 1}], "shape": "blanket", "mt": 5, "topM": 0, "baseM": 5, "lenKm": 2, "widKm": 0.5,
     "layers": ["alluvial", "outwash"], "src": "test", "q": "C"}
  ] }
""");
    foreach (var p in own.Problems) Console.WriteLine("     " + p);
    int Bits(params OreLayers.Class[] cs) => cs.Aggregate(0, (k, c) => k | 1 << (int)c);
    var lat = own.Deposits.First(d => d.Id == "t:laterite");
    Check(own.Deposits.Count == 4 && lat.Classes == Bits(OreLayers.Class.Laterite, OreLayers.Class.Saprolite)
        && own.Deposits.First(d => d.Id == "t:sands").Classes == Bits(OreLayers.Class.Alluvial, OreLayers.Class.Cover)
        && own.Deposits.First(d => d.Id == "t:mixed").Classes == 0, "a body's \"layers\" are read; a stock naming none keeps its codes' 0060 table");
    Check(own.Problems.Count == 1 && own.Problems[0].Contains("unknown layer \"dunes\""), "an unknown layer name is logged and left out");

    // a laterite profile: laterite from the top block down to 100 m, saprolite to 300 m, then bed rock
    var curve = GroundColumns.DepthCurve.Fit(20, 4000, 110);
    var scale = ScaleFor(100, curve);
    int lateriteBottom = (int)Math.Floor(curve.Blocks(100)), saproliteBottom = (int)Math.Floor(curve.Blocks(300));
    string Profile(int d) => d <= lateriteBottom ? "laterite" : d <= saproliteBottom ? "saprolite" : "bed";
    bool OwnLayers(Ore o, int d, OreWhere w) => w.Classes != 0
        ? (w.Classes & 1 << Array.IndexOf(OreLayers.ClassNames, Profile(d))) != 0
        : Profile(d) is "bed" or "basement";
    foreach (var o in own.Deposits.SelectMany(d => d.Ores)) o.Slot = 0;
    var plan = new OreDepositPlan(own, scale, 77, 1);
    var col = new TestColumn { Gate = OwnLayers, Layer = Profile, Keep = true };
    var lb = plan.Bodies.First(b => b.D.Id == "t:laterite");
    plan.Ensure(lb);
    long n = 0;
    for (int z = lb.MinZ; z <= lb.MaxZ; z++) for (int x = lb.MinX; x <= lb.MaxX; x++) { col.X = x; col.Z = z; n += (long)plan.PlaceBody(lb, x, z, col); }
    Console.WriteLine("     laterite blanket: " + string.Join(", ", col.ByLayer.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value)));
    Check(n > 0 && col.Violations == 0 && col.ByLayer.Keys.All(k => k.EndsWith(" in laterite") || k.EndsWith(" in saprolite")), "a body naming laterite and saprolite fills only those layers");
    Check(col.AtTop == 0, "a blanket never replaces the column's top block, even where the top block is in its layer");
    var columns = col.Blocks.GroupBy(k => (k.Item1, k.Item3)).ToList();
    int both = columns.Count(g => g.Any(k => k.Item4 == "limonite") && g.Any(k => k.Item4 == "pentlandite"));
    Check(both > 0 && columns.All(g => !g.Any(p => p.Item4 == "pentlandite" && g.Any(q => q.Item4 == "limonite" && q.Item2 > p.Item2))),
        $"zone top limonite lies above zone bottom pentlandite in every column ({both} columns with both)");
    double limShare = col.ByCode.GetValueOrDefault("limonite") / (double)n;
    Check(Math.Abs(limShare - .6) < .05, $"the shares hold in a thin sheet (limonite {F(100 * limShare, "0.0")}%)");

    double before = plan.Bodies.First(b => b.D.Id == "t:mixed").Target;
    var gone = own.DropOres(c => c != "nativeplatinum");
    foreach (string g in gone) Console.WriteLine("     skipped: " + g);
    var mixed = own.Deposits.FirstOrDefault(d => d.Id == "t:mixed");
    Check(mixed != null && mixed.Ores.Length == 1 && mixed.Ores[0].Code == "chalcopyrite" && Math.Abs(mixed.Ores[0].Share - 1) < 1e-12,
        "a code without a provider is left out and its share goes to the body's other codes");
    var after = new OreDepositPlan(own, scale, 77, 1);
    Check(Math.Abs(after.Bodies.First(b => b.D.Id == "t:mixed").Target / before - 1) < 1e-9, "the body keeps its volume");
    Check(gone.Count == 1 && gone[0].StartsWith("t:pt-only") && own.Deposits.All(d => d.Id != "t:pt-only") && own.Deposits.Count == 3,
        "a body with no installable code is skipped and reported");
}

// ---------------------------------------------------------------- ore rules v5: the blankets' valley search (EA-0058)
Console.WriteLine();
{
    string Json(string id, double lon) => $$"""
{"id": "{{id}}", "name": "Test placer {{id}}", "lat": 59.2, "lon": {{lon.ToString(CultureInfo.InvariantCulture)}}, "layer": "Pt",
 "ores": [{"code": "nativeplatinum", "share": 1}], "shape": "blanket", "mt": 20, "topM": 0, "baseM": 6, "lenKm": 2, "widKm": 0.6,
 "layers": ["alluvial", "outwash"], "src": "test", "q": "C"}
""";
    var vc = new OreDepositCatalog();
    vc.AddFile("valley.json", "{ \"version\": 1, \"group\": \"test\", \"deposits\": [" + Json("t:dry", 59.6) + "," + Json("t:wet", 70.0) + "] }");
    foreach (var o in vc.Deposits.SelectMany(d => d.Ores)) o.Slot = 0;
    var curve = GroundColumns.DepthCurve.Fit(20, 4000, 110);
    var scale = ScaleFor(50, curve);
    var probe = new OreDepositPlan(vc, scale, 5, 1);
    double dryX = probe.Bodies.First(b => b.D.Id == "t:dry").X, wetX = probe.Bodies.First(b => b.D.Id == "t:wet").X;
    double hx = probe.Bodies[0].Hx;
    // a valley strip 400 m wide, 8 km east of the dry placer's catalogue place, and alluvium all round the wet one; alluvium
    // in blocks 1-2 below the top
    int stripW = (int)Math.Floor(dryX + 8000 / hx), stripE = stripW + (int)Math.Ceiling(400 / hx) - 1;
    bool Alluvium(int x, int z) => x >= stripW && x <= stripE || Math.Abs(x - wetX) < 200;
    int LayerBlocks(int x, int z, int classes, int from, int to)
        => (classes & 1 << (int)OreLayers.Class.Alluvial) != 0 && Alluvium(x, z) ? Math.Max(0, Math.Min(2, to) - from + 1) : 0;
    OreDepositPlan Plan(bool search, Func<int, int, int, int, int, int> blocks = null) => new(vc, scale, 5, 1,
        search ? new OreDepositPlan.ValleySearch { LayerBlocks = blocks ?? LayerBlocks } : null);
    (long Placed, long Violations, OreDepositPlan.Body Body) Run(OreDepositPlan plan, string id)
    {
        var b = plan.Bodies.First(q => q.D.Id == id);
        plan.Ensure(b);
        var col = new TestColumn();
        col.Gate = (o, d, w) => d >= 1 && d <= 2 && Alluvium(col.X, col.Z);
        long n = 0;
        for (int z = b.MinZ; z <= b.MaxZ; z++) for (int x = b.MinX; x <= b.MaxX; x++) { col.X = x; col.Z = z; n += (long)plan.PlaceBody(b, x, z, col); }
        return (n, col.Violations, b);
    }
    var v4 = Run(Plan(false), "t:dry");
    Check(v4.Placed == 0 && v4.Body.MovedM == 0, $"rules v4 (no search): a placer whose footprint has no alluvium stays and places nothing ({v4.Placed} blocks)");
    var v5 = Run(Plan(true), "t:dry");
    Console.WriteLine($"     moved {F(v5.Body.MovedM / 1000, "0.0")} km, target {F(v5.Body.Target, "0")}, placed {v5.Placed}, fill {F(v5.Body.Fill)}");
    Check(v5.Body.LayerFound && v5.Body.MovedM >= 7500 && v5.Body.MovedM <= 8500, "rules v5: it moves to the nearest valley (8 km east)");
    Check(v5.Violations == 0 && v5.Placed >= .9 * v5.Body.Target && v5.Placed <= 1.1 * v5.Body.Target + 2,
        $"rules v5: sized by the columns its layer can hold, it places its volume ({v5.Placed} of {F(v5.Body.Target, "0")}) and only in the alluvium");
    var again = Run(Plan(true), "t:dry");
    Check(again.Body.X == v5.Body.X && again.Body.Z == v5.Body.Z && again.Placed == v5.Placed, "the search is repeatable");
    var wet4 = Run(Plan(false), "t:wet");
    var wet5 = Run(Plan(true), "t:wet");
    Check(wet5.Body.MovedM == 0 && wet5.Body.X == wet4.Body.X && wet5.Body.Z == wet4.Body.Z && wet5.Placed > 0,
        "rules v5: a placer with alluvium in its footprint stays where it is");
    var none = Run(Plan(true, (x, z, c, f, t) => 0), "t:dry");
    Check(!none.Body.LayerFound && none.Body.MovedM == 0 && none.Placed == 0, "rules v5: with no alluvium within the radius it stays and places nothing");
    Check(OreDepositPlan.Movable(Plan(true).Bodies.First(b => b.D.Id == "t:dry"))
        && new OreDepositPlan(cat, scale, 1, 1).Bodies.All(b => OreDepositPlan.Movable(b) == (b.D.Shape == Shape.Blanket && (b.Where.Classes & ~OreDepositPlan.CoverClasses) == 0 && b.Where.Classes != 0))
        && new OreDepositPlan(cat, scale, 1, 1).Bodies.Any(b => !OreDepositPlan.Movable(b)),
        "only blankets in cover layers move; stocks, lenses, veins and the rest stay");
}

// ---------------------------------------------------------------- older rules unchanged
Console.WriteLine();
Check(OreBackground.RadiusScale(3, 10, 5) == 1 && OreBackground.RadiusScale(3, 400, 5) == 1, "rules before v4: background sizes never change with the scale");
{
    // no record is not no ore: with no heat at all, every background keeps its class floor (host rock and layer decide where)
    var tuning = new OreTuning();
    bool floor = true;
    foreach (var (res, cls, dep) in new (string, OreClass, string)[] { ("Fe", OreClass.Progression, null), ("Sn", OreClass.Progression, null),
        ("C_coal", OreClass.Progression, null), ("Au", OreClass.Standard, null), ("Au", OreClass.Soil, "earth-placergold"), ("Sn", OreClass.Soil, "earth-placertin"),
        ("Al", OreClass.Soil, "earth-bauxite"), ("Cu", OreClass.Progression, null) })
    {
        var p = tuning.For(res, cls);
        double r = p.Ratio(OreBackground.Evidence(4, res, cls, 0, dep));
        floor &= r > 0 && r == p.Ratio(0) && r == p.Ratio(OreBackground.Evidence(4, res, cls, 1, dep));
    }
    Check(floor, "rules v4: iron, tin, coal, gold, placers and bauxite keep their class floor where no heat is (and no more where it is high)");
}
Check(OreBackground.Evidence(4, "Cu", OreClass.Progression, .9) == 0 && OreBackground.Evidence(4, "Au", OreClass.Standard, 1) == 0
      && OreBackground.Evidence(4, "Sn", OreClass.Soil, .5, "earth-placertin") == 0 && OreBackground.Evidence(4, "C_coal", OreClass.Progression, 1) == 0,
    "rules v4: no hotspot boost for what the catalogue places");
Check(OreBackground.Evidence(4, "SiO2", OreClass.Veins, .7) == .7 && OreBackground.Evidence(4, "peat", OreClass.Soil, .6) == .6
      && OreBackground.Evidence(4, "Fe", OreClass.Soil, .4, "earth-bogiron") == .4 && OreBackground.Evidence(3, "Cu", OreClass.Progression, .9) == .9,
    "rules v4: quartz veins, peat, clay, bog iron and salt flats keep their boost; older rules unchanged");
Check(OreBackground.RadiusScale(4, 100, 5) == 1 && OreBackground.RadiusScale(4, 100.2, 5) == 1, "rules v4 at 1:100 (100.2 m per block): today's calibration");
Check(Math.Abs(OreBackground.RadiusScale(4, 10, 5) - 10) < 1e-9 && Math.Abs(OreBackground.RadiusScale(4, 1, 5) - 64 / 5.0) < 1e-9 && Math.Abs(OreBackground.RadiusScale(4, 400, 5) - .25) < 1e-9,
    "rules v4: radius x10 at 1:10 (capped at 64 blocks), x0.25 at 1:400");

// ---------------------------------------------------------------- Earth's own ore and gem deposits (EarthParams.DepositSet)
Console.WriteLine();
{
    const string ores = "earth:worldgen/deposits/earth-ores.json", gems = "earth:worldgen/deposits/earth-gems.json";
    Check(EarthDepositSet.Of(0, 0, 0, 0) == 0, "a world saved before the deposit set (1.x, or a non-Earth world) has none of Earth's ore and gem deposits");
    Check(EarthDepositSet.Of(0, 1, 0, 0) == 1 && EarthDepositSet.Of(0, 0, OreBackground.CatalogueRules, 0) == 1 && EarthDepositSet.Of(0, 0, 0, 1) == 1,
        "an RC or dev world with ore layers, ore rules v4 or the water table rules (all newer than the deposits) keeps them");
    Check(EarthDepositSet.Of(EarthDepositSet.Version, 0, 0, 0) == EarthDepositSet.Version && EarthDepositSet.Of(2, 1, 4, 1) == 2, "a saved set is kept as it is");
    Check(!EarthDepositSet.Keeps(ores, 0) && !EarthDepositSet.Keeps(gems, 0) && EarthDepositSet.Keeps(ores, 1) && EarthDepositSet.Keeps(gems, 1),
        "set 0 leaves earth-ores.json and earth-gems.json out, set 1 keeps them");
    Check(EarthDepositSet.Keeps("game:worldgen/deposits/ore/ore-copper.json", 0) && EarthDepositSet.Keeps("earth:worldgen/deposits/earth-layers.json", 0)
          && EarthDepositSet.Keeps("earth:authored-ore-adapters", 0) && EarthDepositSet.Keeps(null, 0),
        "set 0 keeps every other deposit (vanilla's, other mods', the layer deposits with their own rule, Earth's adapters)");
    // a new deposit file under earth:worldgen/deposits must come with a new set, or older worlds would load it
    string folder = Path.GetFullPath(Path.Combine(here, "../../assets/earth/worldgen/deposits"));
    var shipped = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
        .Select(f => "earth:worldgen/deposits/" + Path.GetRelativePath(folder, f).Replace('\\', '/')).OrderBy(s => s, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    var unlisted = shipped.Where(f => f != "earth:worldgen/deposits/earth-layers.json" && !EarthDepositSet.Files.Contains(f)).ToArray();
    Check(shipped.Length > 0 && unlisted.Length == 0 && EarthDepositSet.Files.All(shipped.Contains),
        "every Earth deposit file is the layer file or in a deposit set, and every listed file ships" + (unlisted.Length > 0 ? " (unlisted: " + string.Join(", ", unlisted) + ")" : ""));
}

// ---------------------------------------------------------------- frozen hashes (StableHash: saved worlds depend on every bit)
Console.WriteLine();
{
    // the hashes as they were written out where they were used before they were gathered in StableHash
    static double Ore(long seed, int x, int y, int z, int salt)
    {
        unchecked
        {
            ulong v = (ulong)seed ^ (uint)x * 0x9E3779B185EBCA87UL ^ (uint)y * 0xC2B2AE3D27D4EB4FUL ^ (uint)z * 0x165667B19E3779F9UL ^ (uint)salt;
            v ^= v >> 30; v *= 0xbf58476d1ce4e5b9UL; v ^= v >> 27; v *= 0x94d049bb133111ebUL; v ^= v >> 31;
            return (v >> 11) * (1.0 / 9007199254740992.0);
        }
    }
    static double Grass(int x, int z)
    {
        unchecked
        {
            ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)z * 0xC2B2AE3D27D4EB4FUL ^ 0x5A17D9A2E3C1B7F1UL;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 27; h *= 0x94D049BB133111EBUL; h ^= h >> 31;
            return (h >> 11) * (1.0 / (1UL << 53));
        }
    }
    static double Ash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)x * 73856093u ^ (uint)z * 19349663u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }
    var rnd = new Random(20261108);
    bool same = true;
    for (int i = 0; i < 200000 && same; i++)
    {
        long seed = rnd.NextInt64(long.MinValue, long.MaxValue);
        int x = rnd.Next(int.MinValue, int.MaxValue), y = rnd.Next(-64, 4096), z = rnd.Next(int.MinValue, int.MaxValue), salt = rnd.Next(int.MinValue, int.MaxValue);
        same = StableHash.Unit(seed, x, y, z, salt) == Ore(seed, x, y, z, salt) && RegionalOreCatalog.Random(seed, x, y, z, salt) == Ore(seed, x, y, z, salt)
            && GeologyTransitions.Hash(seed, x, y, z, salt) == Ore(seed, x, y, z, salt)
            && StableHash.Column(x, z) == Grass(x, z) && StableHash.Unit24(x, z) == Ash(x, z);
    }
    Check(same, "StableHash gives the ore, geology, ground cover and volcano hashes bit for bit");
}

// ---------------------------------------------------------------- smoke test with other catalogues
foreach (string file in extra)
{
    Console.WriteLine();
    var other = new OreDepositCatalog();
    other.AddFile(Path.GetFileName(file), File.ReadAllText(file));
    Console.WriteLine($"{file}: {other.Deposits.Count} deposits, {other.Areas.Count} areas, {other.Skipped} skipped, {other.Problems.Count} problems");
    foreach (var p in other.Problems.Take(15)) Console.WriteLine("     " + p);
    foreach (var o in other.Deposits.SelectMany(d => d.Ores).Concat(other.Areas.SelectMany(a => a.Beds.Select(b => b.Ore)))) o.Slot = 0;
    foreach (var (name, h) in scales)
    {
        var sw = Stopwatch.StartNew();
        var plan = new OreDepositPlan(other, ScaleFor(h, GroundColumns.DepthCurve.Fit(h >= 100 ? 25 : 10, 4000, 110)), 1, 1);
        double bodies = 0, compact = 0, areas = 0;
        foreach (var b in plan.Bodies) { plan.Ensure(b); bodies += b.Target; if (b.Compact) compact++; }
        foreach (var a in plan.Areas) areas += plan.AreaExpected(a);
        Console.WriteLine($"     {name}: bodies {F(bodies, "0")} blocks ({compact} compact), basins {F(areas, "0")} blocks, planned in {sw.ElapsedMilliseconds} ms");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All checks passed ({clock.Elapsed.TotalSeconds:0.0} s)." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

sealed class TestColumn : IOreColumn
{
    public int Top => 4000;
    public int Floor => 1;
    public Func<Ore, int, OreWhere, bool> Gate = (o, d, w) => true;
    public Func<int, string> Layer;
    public long Violations, AtTop;
    public readonly long[] Tiers = new long[4];
    public readonly Dictionary<string, long[]> TiersByCode = new();
    public readonly Dictionary<string, long> ByCode = new();
    public readonly Dictionary<string, long> ByLayer = new();
    readonly Dictionary<string, double> depthSum = new();
    public bool Keep; public int X, Z;
    public readonly HashSet<(int, int, int, string)> Blocks = new();
    public bool Allows(Ore ore, int depth, OreWhere where) => Gate(ore, depth, where);
    public bool Place(Ore ore, int depth, int tier, double roll, OreWhere where)
    {
        if (!Gate(ore, depth, where)) Violations++;
        if (depth == 0) AtTop++;
        Tiers[tier]++;
        if (!TiersByCode.TryGetValue(ore.Code, out var byTier)) TiersByCode[ore.Code] = byTier = new long[4];
        byTier[tier]++;
        ByCode[ore.Code] = ByCode.GetValueOrDefault(ore.Code) + 1;
        depthSum[ore.Code] = depthSum.GetValueOrDefault(ore.Code) + depth;
        if (Layer != null) { string k = ore.Code + " in " + Layer(depth); ByLayer[k] = ByLayer.GetValueOrDefault(k) + 1; }
        if (Keep) Blocks.Add((X, depth, Z, ore.Code));
        return true;
    }
    public double MeanDepth(string code) => ByCode.TryGetValue(code, out long n) && n > 0 ? depthSum[code] / n : double.NaN;
}
