using System.Globalization;
using EarthMap.Core;

// Offline checks of the ground columns against published Baltic columns (docs/research/geology-columns-2026-11-06).
// Usage: dotnet run -- [repo root]
string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string rules = File.ReadAllText(Path.Combine(root, "assets/earth/config/ground-rules-v1.json"));
byte[] ReadAsset(string loc) { var p = Path.Combine(root, "assets", loc.Split(':')[0], loc.Split(':')[1]); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

// depth curves (Earth tab, Ground depth) in the scale presets' worlds; terrain metres per block at sea level as VerticalScale gives it
var presets = new (string Name, double MetresPerBlock)[]
{
    ("1:10, 1024 high, linear", 1 / new VerticalScale(1024, 110, 1022, 1000, 47, 20, true).Slope(0)),
    ("1:25, 512 high, linear", 1 / new VerticalScale(512, 110, 510, 1000, 47, 20, true).Slope(0)),
    ("384 high, log1000", 1 / new VerticalScale(384, 110, 382, 1000, 47, 20).Slope(0)),
};
foreach (var (pname, t) in presets)
    foreach (string choice in GroundColumns.DepthCurve.Choices)
    {
        var c = GroundColumns.DepthCurve.For(choice, t, 110);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}, {1}: {2}; 17 m = {3:0.0} blk, 100 m = {4:0.0}, 300 m = {5:0.0}, 1000 m = {6:0.0}; y = 1 under lowland at {7:0} m",
            pname, choice, c, c.Blocks(17), c.Blocks(100), c.Blocks(300), c.Blocks(1000), c.Metres(114)));
        Check(Math.Abs(c.Blocks(1e-4) / 1e-4 - 1 / c.Top) < 1e-6 && c.Top >= t - 1e-9, $"{pname} {choice}: the top is at the terrain's scale or coarser");
        Check(Math.Abs(c.Metres(c.Blocks(777)) - 777) < 1e-6, $"{pname} {choice}: curve inverse");
        double floor = c.Metres(114);
        Check(choice switch
        {
            GroundColumns.DepthCurve.Terrain => Math.Abs(floor - 114 * t) < 1e-6,
            GroundColumns.DepthCurve.Log2000 => Math.Abs(floor - Math.Max(2000, 114 * t)) < 1e-3,
            GroundColumns.DepthCurve.Log4000 => Math.Abs(floor - Math.Max(4000, 114 * t)) < 1e-3,
            _ => Math.Abs(floor - Math.Max(2000, 114 * t)) < 1e-6 && c.Linear,
        }, $"{pname} {choice}: bottom of a lowland column at {floor:0} m");
    }
Check(Math.Abs(presets[0].MetresPerBlock - 8849.0 / 912) < 1e-9, "1:10 terrain is 9.7 m per block");

// the Baltic checks run in the 1:10 baseline world with the default curve (terrain scale at the top, 4000 m at the bottom)
Check(GroundColumns.DepthCurve.For(null, 9.7, 110).D0 == GroundColumns.DepthCurve.For("log4000", 9.7, 110).D0, "missing choice is log4000");
var cv = GroundColumns.DepthCurve.For(GroundColumns.DepthCurve.Default, presets[0].MetresPerBlock, 110);
var model = GroundColumns.Load(rules, ReadAsset, cv, 1234);

// Probe mode (GROUND_PROBE="name:lat:lon;..." or "qc" for the world list; tools/ground/qc-boxes): 100 x 100 km boxes sampled
// every km with the 30" sets (GROUND_DATA: the folder holding ground/*.egts, default rawdata/ground/baked-30s) and relief from
// Earth's GLO-90 tiles (GLO90_DIR, default the bench's store), cover statistics, a 5 km map and a 10 km detail at 250 m.
if (Environment.GetEnvironmentVariable("GROUND_PROBE") is { Length: > 0 } probeSpec)
{
    string data30 = Environment.GetEnvironmentVariable("GROUND_DATA") ?? Path.GetFullPath(Path.Combine(root, "../../../rawdata/ground/baked-30s"));
    string glo = Environment.GetEnvironmentVariable("GLO90_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData-agentbridge/ModData/earth/glo90");
    var pm = GroundColumns.Load(rules, ReadAsset, cv, 1234, file => Path.Combine(data30, Path.GetFileName(file)));
    Console.WriteLine($"30\" sets from {data30}; missing: {(pm.MissingTiles.Count == 0 ? "none" : string.Join(", ", pm.MissingTiles))}; heights from {glo}");
    var demCache = new Dictionary<(int, int), DemTiles.Tile>();
    DemTiles.Tile Dem(int latS, int lonW)
    {
        if (demCache.TryGetValue((latS, lonW), out var t)) return t;
        string p = Path.Combine(glo, DemTiles.ShortName(latS, lonW) + ".egl");
        t = File.Exists(p) ? DemTiles.Decode(File.ReadAllBytes(p)) : null;
        if (demCache.Count > 64) demCache.Clear();
        return demCache[(latS, lonW)] = t;
    }
    double Elev(double la, double lo)
    {
        int latS = (int)Math.Floor(la), lonW = (int)Math.Floor(lo);
        var t = Dem(latS, lonW);
        if (t == null) return 0;                            // no GLO-90 square: sea
        double r = (latS + 1 - la) * t.Rows, c = (lo - lonW) * t.Cols;
        int r0 = Math.Clamp((int)r, 0, t.Rows - 1), c0 = Math.Clamp((int)c, 0, t.Cols - 1);
        int r1 = Math.Min(r0 + 1, t.Rows - 1), c1 = Math.Min(c0 + 1, t.Cols - 1);
        double fr = r - r0, fc = c - c0;
        return (t.At(r0, c0) * (1 - fc) + t.At(r0, c1) * fc) * (1 - fr) + (t.At(r1, c0) * (1 - fc) + t.At(r1, c1) * fc) * fr;
    }
    var qcBoxes = File.ReadAllLines(Path.Combine(root, "tools/ground/qc-boxes.txt")).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToArray();
    var specs = probeSpec == "qc" ? qcBoxes : probeSpec.Split(';', StringSplitOptions.RemoveEmptyEntries);
    var mean1 = new double[1]; var slope1 = new double[1]; var norm1 = new double[1];
    foreach (var spec in specs)
    {
        var f = spec.Split(':');
        string pname = f[0].Trim(); double plat = double.Parse(f[1], CultureInfo.InvariantCulture), plon = double.Parse(f[2], CultureInfo.InvariantCulture);
        string expect = f.Length > 3 ? f[3].Trim() : "";
        string checks = f.Length > 4 ? f[4].Trim() : "";
        double cosL = Math.Cos(plat * Math.PI / 180);
        // a local 100 m grid: x east, z south, in units of 100 m from the box centre
        double LatOf(double z) => plat - z * 100 / 111320.0;
        double LonOf(double x) => plon + x * 100 / (111320.0 * cosL);
        GroundColumns.Sample At(double x, double z)
        {
            GroundRelief.Field(x, z, 1, (u, v) => Elev(LatOf(v), LonOf(u)), 100, 100, pm.ReliefRadiusMetres, mean1, slope1, pm.ReliefWeight, norm1);
            double e = Elev(LatOf(z), LonOf(x));
            var place = new GroundColumns.Place { Elevation = e, Surface = GroundColumns.Surface.Land, RegionalRock = true, HasRelief = true, Relief = e - mean1[0], Slope = slope1[0], ReliefNorm = norm1[0] };
            return pm.At(LatOf(z), LonOf(x), place);
        }
        var qs = new List<double>(); var recipes = new Dictionary<string, int>(); var units = new Dictionary<string, int>(); var soils = new Dictionary<string, int>();
        int frozen = 0, pn = 0, crest = 0; double fsum = 0; var map = new char[20, 20];
        string Classes = " .:-=+*#%@"; // <1, 1-2, 2-3, 3-5, 5-10, 10-20, 20-30, 30-50, 50-100, >100 m
        int Class(double q) => q < 1 ? 0 : q < 2 ? 1 : q < 3 ? 2 : q < 5 ? 3 : q < 10 ? 4 : q < 20 ? 5 : q < 30 ? 6 : q < 50 ? 7 : q < 100 ? 8 : 9;
        for (int i = 0; i < 100; i++) for (int j = 0; j < 100; j++)
        {
            var sm = At((j - 50) * 10 + 5, (i - 50) * 10 + 5);
            if (sm.Soil == null) { if (i % 5 == 2 && j % 5 == 2) map[i / 5, j / 5] = '~'; continue; } // no soil data: sea, lake or ice
            pn++; qs.Add(sm.QuaternaryMetres); fsum += sm.ReliefFactor;
            recipes[sm.Cover] = recipes.GetValueOrDefault(sm.Cover) + 1; units[sm.CoverUnit ?? "-"] = units.GetValueOrDefault(sm.CoverUnit ?? "-") + 1;
            soils[sm.Soil] = soils.GetValueOrDefault(sm.Soil) + 1; if (sm.Frozen) frozen++;
            if (sm.Cover is "thin" or "knob-till") crest++;
            if (i % 5 == 2 && j % 5 == 2) map[i / 5, j / 5] = Classes[Class(sm.QuaternaryMetres)];
        }
        Console.WriteLine($"\n== {pname} ({plat}, {plon}), 100 x 100 km, {pn} land samples{(expect.Length > 0 ? "; expected: " + expect : "")}");
        if (pn == 0) continue;
        Console.WriteLine($"centre column: {At(0, 0)}");
        qs.Sort(); double P(double x) => qs[(int)Math.Min(qs.Count - 1, x * qs.Count)];
        string Top(Dictionary<string, int> d, int k) => string.Join(", ", d.OrderByDescending(x => x.Value).Take(k).Select(x => $"{x.Key} {x.Value * 100 / pn}%"));
        Console.WriteLine($"cover m: mean {qs.Average():0.#}, p10 {P(.1):0.#}, median {P(.5):0.#}, p90 {P(.9):0.#}, max {qs[^1]:0.#}; under 1 m {qs.Count(q => q < 1) * 100 / pn}%, under 3 m {qs.Count(q => q < 3) * 100 / pn}%, over 20 m {qs.Count(q => q > 20) * 100 / pn}%; crests {crest * 100 / pn}%, mean relief factor {fsum / pn:0.00}{(frozen > 0 ? $"; frozen {frozen * 100 / pn}%" : "")}");
        Console.WriteLine($"recipes: {Top(recipes, 6)}\nsediment units: {Top(units, 5)}\nsoils: {Top(soils, 5)}");
        // checks: "under1>=25 under3>=40 over20<=5 median<=10" (percent of land samples; median in m)
        foreach (var c in checks.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = System.Text.RegularExpressions.Regex.Match(c, @"^(under1|under3|over20|median)(>=|<=)([0-9.]+)$");
            if (!m.Success) { Check(false, $"{pname}: bad check {c}"); continue; }
            double v = m.Groups[1].Value switch
            {
                "under1" => qs.Count(q => q < 1) * 100.0 / pn, "under3" => qs.Count(q => q < 3) * 100.0 / pn,
                "over20" => qs.Count(q => q > 20) * 100.0 / pn, _ => P(.5),
            };
            double lim = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            bool ok = m.Groups[2].Value == ">=" ? v >= lim : v <= lim;
            Check(ok, $"{pname}: {c} (is {v:0.#})");
            Console.WriteLine($"check {c}: {(ok ? "pass" : "FAIL")} ({v:0.#})");
        }
        Console.WriteLine("5 km map | 10 km detail at 250 m (centre)  | relief there (^ >8 m above the mean ground, v >8 m below, - between)");
        Console.WriteLine("  ' '<1 m '.'1-2 ':'2-3 '-'3-5 '='5-10 '+'10-20 '*'20-30 '#'30-50 '%'50-100 '@'>100, '~' no soil data");
        var detail = new string[40]; var relief = new string[40];
        for (int i = 0; i < 40; i++)
        {
            var d = new char[40]; var r = new char[40];
            for (int j = 0; j < 40; j++)
            {
                var sm = At((j - 20) * 2.5 + 1.25, (i - 20) * 2.5 + 1.25);
                d[j] = sm.Soil == null ? '~' : Classes[Class(sm.QuaternaryMetres)];
                r[j] = sm.Relief > 8 ? '^' : sm.Relief < -8 ? 'v' : '-';
            }
            detail[i] = new string(d); relief[i] = new string(r);
        }
        for (int i = 0; i < 40; i++)
        {
            string left = i < 20 ? "|" + new string(Enumerable.Range(0, 20).Select(j => map[i, j] == '\0' ? '?' : map[i, j]).ToArray()) + "|" : new string(' ', 22);
            Console.WriteLine($"  {left} |{detail[i]}| |{relief[i]}|");
        }
    }
    Console.WriteLine(failures == 0 ? "\nQC: all checks pass" : $"\nQC: {failures} checks fail");
    return failures == 0 ? 0 : 1;
}
var unknown = new List<string>();
model.UnknownName += unknown.Add;
int M(string name) => Array.IndexOf(model.Materials, name);

Console.WriteLine("tiles: " + string.Join(", ", model.Data.Tiles.Select(t => $"{t.Tile.Name} ({t.Priority}, {string.Join("/", t.Tile.Fields.Select(f => f.Name))})")));

// thin layers keep a block: Riga at 9.7 m per block has the same layers, in the same order, as at one block per metre
{
    var fine = GroundColumns.Load(rules, ReadAsset, new GroundColumns.DepthCurve(1, double.PositiveInfinity), 1234);
    var sm = model.At(56.95, 24.10);
    var a = new GroundColumns.Column(); var b1 = new GroundColumns.Column();
    model.Build(sm, GroundColumns.Surface.Land, a); fine.Build(sm, GroundColumns.Surface.Land, b1);
    var seqA = Enumerable.Range(0, a.Count).Select(i => model.Materials[a.Material[i]]).ToArray();
    var seqB = Enumerable.Range(0, b1.Count).Select(i => fine.Materials[b1.Material[i]]).ToArray();
    Console.WriteLine("\nRiga at 1 m per block: " + fine.Describe(b1) + "\nRiga at 9.7 m per block: " + model.Describe(a));
    Check(seqA.SequenceEqual(seqB), "Riga keeps every layer at 9.7 m per block: " + string.Join(", ", seqA) + " vs " + string.Join(", ", seqB));
    Check(Enumerable.Range(0, a.Count).All(i => a.End[i] > (i == 0 ? 0 : a.End[i - 1])), "every layer has at least one block");
    foreach (var (pname, t) in presets)
        foreach (string choice in GroundColumns.DepthCurve.Choices)
        {
            var m = GroundColumns.Load(rules, ReadAsset, GroundColumns.DepthCurve.For(choice, t, 110), 1234);
            m.Build(sm, GroundColumns.Surface.Land, a);
            Console.WriteLine($"  {pname}, {choice}: {m.Describe(a)}");
        }
}

// every legend name in the Baltic tiles has a rule
foreach (var (tile, _) in model.Data.Tiles)
    foreach (var f in tile.Fields.Where(f => f.Category && f.Name != GroundColumns.FieldNames.SubcropLith && tile.Name != "global-gum"))
        foreach (var name in f.Legend)
        {
            var s = new GroundColumns.Sample { Lat = 57, Lon = 24, Cover = "till", QuaternaryMetres = 10, BasementMetres = 800, Clay = double.NaN, Ph = double.NaN };
            if (f.Name == GroundColumns.FieldNames.Soil) s.Soil = name; else if (f.Name == GroundColumns.FieldNames.Subcrop) s.Subcrop = name; else if (f.Name == GroundColumns.FieldNames.Seabed) s.Seabed = name;
            model.Build(s, f.Name == GroundColumns.FieldNames.Seabed ? GroundColumns.Surface.Water : GroundColumns.Surface.Land, new GroundColumns.Column());
        }
Check(unknown.Count == 0, "rules know every name in the Baltic tiles: unknown " + string.Join(", ", unknown));
unknown.Clear();

bool IsBasement(int m) => m == M("granite") || m == M("gneiss");
string[] rocks = { "dolostone", "sandstone", "limestone", "claystone", "shale", "chalk", "marl-devonian", "conglomerate" };

// sites: name, lat, lon, Quaternary metres (Latvian grid / literature), basement metres (boreholes), first rock(s) per the
// LVGMC reports and boreholes in baltic-real-columns.md ('|' = either). Gap = a known miss of the 1:5M IGME subcrop map
// (Daugavpils and Cesis are Middle Devonian / Amata-Gauja sandstone; IGME has Late Devonian undifferentiated there):
// printed, not failed, until the LVGMC 1:200k pre-Quaternary map replaces IGME.
var sites = new (string Name, double Lat, double Lon, double Q, double Basement, string FirstRock, bool Gap)[]
{
    ("Riga", 56.95, 24.10, 17, 1000, "dolostone|marl-devonian", false),
    ("Jelgava", 56.65, 23.72, 18, 1050, "dolostone|marl-devonian", false),
    ("Staicele", 57.83, 24.75, 11, 688, "sandstone", false),
    ("Liepaja", 56.51, 21.05, 12, 1800, null, false),
    ("Daugavpils", 55.87, 26.53, 70, 800, "sandstone|claystone", true),
    ("Ventspils", 57.39, 21.56, 50, 1170, "sandstone|claystone|marl-devonian", false),
    ("Cesis", 57.31, 25.27, 8, 600, "sandstone", true),
    ("Gulbene", 57.18, 26.75, 27, 600, null, false),
    ("Saldus", 56.67, 22.49, 20, 1300, null, false),
    ("Tallinn", 59.40, 24.75, double.NaN, 150, "limestone", false),
};
var col = new GroundColumns.Column();
foreach (var s in sites)
{
    var sm = model.At(s.Lat, s.Lon);
    model.Build(sm, GroundColumns.Surface.Land, col);
    Console.WriteLine($"\n{s.Name}: {sm}");
    Console.WriteLine("  " + model.Describe(col));
    Check(model.Covers(s.Lat, s.Lon), s.Name + " inside the extent");
    Check(!col.HandOff, s.Name + " uses the Baltic template");
    Check(double.IsNaN(s.Q) || Math.Abs(sm.QuaternaryMetres - s.Q) <= Math.Max(8, s.Q * .6), $"{s.Name} Quaternary {sm.QuaternaryMetres:0} m vs {s.Q}");
    Check(Math.Abs(sm.BasementMetres - s.Basement) <= s.Basement * .45, $"{s.Name} basement {sm.BasementMetres:0} m vs {s.Basement}");
    Check(col.Material[0] == model.Topsoil || col.Material[0] == model.Peat, s.Name + " starts with soil or peat");
    Check(col.End[0] <= 2, s.Name + " topsoil at most 2 blocks");
    Check(IsBasement(col.Material[col.Count - 1]), s.Name + " ends in basement");
    if (s.FirstRock != null)
    {
        int firstRock = -1;
        for (int i = 0; i < col.Count; i++) if (rocks.Contains(model.Materials[col.Material[i]])) { firstRock = col.Material[i]; break; }
        bool ok = firstRock >= 0 && s.FirstRock.Split('|').Contains(model.Materials[firstRock]);
        string msg = $"{s.Name} first rock {(firstRock >= 0 ? model.Materials[firstRock] : "none")} vs {s.FirstRock}";
        if (s.Gap) Console.WriteLine((ok ? "gap closed: " : "KNOWN GAP ") + msg); else Check(ok, msg);
    }
    double basementTop = col.Count > 1 ? col.EndMetres[col.Count - 2] : 0;
    Check(Math.Abs(basementTop - sm.BasementMetres) <= sm.BasementMetres * .15 + 10, $"{s.Name} basement at {basementTop:0} m vs {sm.BasementMetres:0}");
    int basementBlock = col.Count > 1 ? col.End[col.Count - 2] : 0;
    Check(basementBlock >= cv.Blocks(sm.BasementMetres) - 1, $"{s.Name} kept thin layers push the basement down, never up (block {basementBlock}, curve {cv.Blocks(sm.BasementMetres):0})");
}

// shaft review (6 November): glacial cycles, compaction, Tallinn's shallow blue clay stays clay
{
    var sm = model.At(56.65, 23.72); sm.Cover = "till"; sm.QuaternaryMetres = 40;
    model.Build(sm, GroundColumns.Surface.Land, col);
    var cover = new List<string>();
    for (int i = 1; i < col.Count && col.EndMetres[i - 1] < 40; i++) cover.Add(model.Materials[col.Material[i]]);
    Console.WriteLine("\n40 m till: " + model.Describe(col));
    Check(cover.Count >= 4 && cover.Contains("sand") && cover.Contains("till-reddish") && cover.Contains("till-grey"), "till cover is cycles, not one unit: " + string.Join(", ", cover));
}
{
    var sm = model.At(59.40, 24.75);
    model.Build(sm, GroundColumns.Surface.Land, col);
    Check(Enumerable.Range(0, col.Count).Any(i => col.Material[i] == M("clay-blue")), "Tallinn keeps raw blue clay at about 100 m");
}

// sea floor and beach
// (EMODnet's 1:250k layer has no data off Liepaja: the rules' default seabed answers there)
foreach (var (name, lat, lon, mapped) in new[] { ("Gulf of Riga", 57.40, 23.60, true), ("Irbe Strait", 57.75, 22.10, true), ("off Liepaja", 56.50, 20.70, false) })
{
    var sm = model.At(lat, lon);
    model.Build(sm, GroundColumns.Surface.Water, col);
    Console.WriteLine($"\n{name} (water): {sm}\n  " + model.Describe(col));
    Check(col.Material[0] != model.Topsoil, name + " no topsoil under water");
    Check((sm.Seabed != null) == mapped, name + (mapped ? " has a mapped seabed" : " uses the default seabed"));
    if (!mapped) Check(col.Material[0] == M("mud"), name + " default seabed is mud");
}
var b = model.At(56.97, 23.75); model.Build(b, GroundColumns.Surface.Beach, col);
Console.WriteLine($"\nJurmala beach: {b}\n  " + model.Describe(col));
Check(col.Material[0] == model.BeachSand, "beach starts with sand");
model.Build(b, GroundColumns.Surface.RiverBed, col);
Check(col.Material[0] == M("silt"), "river bed starts with silt");

// decision 0070: a shore strip's material, OSM's first, else the coast's ground
{
    GroundColumns.Sample Shore(byte cls, string cover, double slope = 0.05, string seabed = null)
    {
        var x = model.At(56.97, 23.75); x.Shore = cls; x.Cover = cover; x.Slope = slope; x.Seabed = seabed; x.SeabedSource = seabed != null ? "test" : null;
        return x;
    }
    int Top(GroundColumns.Sample x) { model.Build(x, GroundColumns.Surface.Beach, col); return col.Count > 0 ? col.Material[0] : -1; }
    Check(Top(Shore(ShoreMaterial.Sand, "till")) == model.BeachSand, "OSM sand on till is sand");
    Check(Top(Shore(ShoreMaterial.Gravel, "dune")) == M("gravel"), "OSM gravel on dunes is gravel");
    Check(Top(Shore(ShoreMaterial.Mud, "dune")) == M("mud"), "OSM mud is mud");
    model.Build(Shore(ShoreMaterial.Rock, "till"), GroundColumns.Surface.Beach, col);
    Check(col.Count == 0 || (col.Material[0] != M("till-reddish") && col.Material[0] != model.BeachSand && col.Material[0] != model.Topsoil), "OSM rock is bare: no cover, sand or soil on it (" + model.Describe(col) + ")");
    Check(Top(Shore(ShoreMaterial.Rule, "dune")) == model.BeachSand, "dunes give sand");
    Check(Top(Shore(ShoreMaterial.Rule, "coastal")) == model.BeachSand, "coastal deposits give sand");
    int tillTop = Top(Shore(ShoreMaterial.Rule, "till"));
    Check(tillTop == M("gravel") || tillTop == model.BeachSand, "till gives gravel and sand in patches");
    Check(Top(Shore(ShoreMaterial.Rule, "peat")) == M("mud"), "peat shores are mud");
    Check(Top(Shore(ShoreMaterial.Beach, "peat")) == model.BeachSand, "an OSM beach on peat is sand, not mud");
    Check(Top(Shore(ShoreMaterial.Rule, "dune", seabed: "11. Mud")) == model.BeachSand, "a beach on land ignores the mapped sea floor (no basin mud on islands)");
    model.Build(Shore(ShoreMaterial.Rule, "dune", seabed: "3. Coarse-grained sediment"), GroundColumns.Surface.Water, col);
    Check(col.Count > 0 && col.Material[0] == M("gravel"), "the shallows take a coarse mapped sea floor: gravel");
    var shieldShore = Shore(ShoreMaterial.Rule, "till"); shieldShore.Province = "shield";
    Check(Top(shieldShore) == M("gravel"), "till on a shield's shore is gravel and stones, not sand");
    model.Build(Shore(ShoreMaterial.Rule, "thin", slope: 0.5), GroundColumns.Surface.Water, col);
    Check(col.Count == 0 || (col.Material[0] != M("mud") && col.Material[0] != model.BeachSand), "a rocky shore's shallows are bare rock, not the floor's default mud");
    model.Build(Shore(ShoreMaterial.Rule, "dune", slope: 2), GroundColumns.Surface.Beach, col);
    Check(col.Count == 0 || col.Material[0] != model.BeachSand, "a cliff is bare rock");
    Check(Top(Shore(ShoreMaterial.Beach, "dune", slope: 2)) == model.BeachSand, "an OSM beach under a steep slope stays sand");
    model.Build(Shore(ShoreMaterial.Rule, "thin", slope: 0.5), GroundColumns.Surface.Beach, col);
    Check(col.Count == 0 || col.Material[0] != model.Topsoil, "thin cover on a slope: bare rock");
    Check(Top(Shore(ShoreMaterial.Rule, "thin", slope: 0.05)) == M("gravel"), "thin cover on gentle ground: gravel");
    var noShore = Shore(0, "till"); Check(Top(noShore) == model.BeachSand, "an older world's beach (no shore class) is beach sand as before");
    // shore rules 3 (EA-0056): thin till over scoured rock is bare rock where OSM maps no beach; shore rules 2 worlds keep
    // gravel and sand there, and thick till and OSM's beaches are unchanged
    GroundColumns.Sample Rules(GroundColumns.Sample x, int rules) { x.ShoreRules = rules; return x; }
    int scoured = Top(Rules(Shore(ShoreMaterial.Rule, "thin-till"), 3));
    Check(scoured >= 0 && scoured != model.BeachSand && scoured != M("gravel") && scoured != model.Topsoil && scoured != M("till-reddish"),
        "shore rules 3: thin till at the shore is bare rock (" + model.Describe(col) + ")");
    model.Build(Rules(Shore(ShoreMaterial.Rule, "thin-till"), 3), GroundColumns.Surface.Water, col);
    Check(col.Count == 0 || (col.Material[0] != M("mud") && col.Material[0] != model.BeachSand && col.Material[0] != M("gravel")), "shore rules 3: and its shallows are rock too");
    int knob = Top(Rules(Shore(ShoreMaterial.Rule, "knob-till"), 3));
    Check(knob >= 0 && knob != model.BeachSand && knob != M("gravel") && knob != model.Topsoil, "shore rules 3: knob till at the shore is bare rock too");
    int pocket = Top(Rules(Shore(ShoreMaterial.Beach, "thin-till"), 3));
    Check(pocket == M("gravel") || pocket == model.BeachSand, "shore rules 3: an OSM beach on thin till stays gravel or sand");
    int olderThin = Top(Rules(Shore(ShoreMaterial.Rule, "thin-till"), 2));
    Check(olderThin == M("gravel") || olderThin == model.BeachSand, "shore rules 2 worlds: thin till keeps gravel and sand");
    int thick = Top(Rules(Shore(ShoreMaterial.Rule, "till"), 3));
    Check(thick == M("gravel") || thick == model.BeachSand, "shore rules 3: thick till is unchanged");
    // the surface table (OSM's tags)
    Check(ShoreMaterial.OfSurface("sand", false) == ShoreMaterial.Sand, "surface sand");
    Check(ShoreMaterial.OfSurface("pebblestone", false) == ShoreMaterial.Gravel, "surface pebblestone");
    Check(ShoreMaterial.OfSurface("sand;gravel", false) == ShoreMaterial.Mixed, "surface sand;gravel");
    Check(ShoreMaterial.OfSurface("rock", false) == ShoreMaterial.Rock, "surface rock");
    Check(ShoreMaterial.OfSurface("mud", false) == ShoreMaterial.Mud, "surface mud");
    Check(ShoreMaterial.OfSurface(null, false) == ShoreMaterial.Beach, "no surface: a beach");
    Check(ShoreMaterial.OfSurface(null, true) == ShoreMaterial.Gravel, "natural=shingle: gravel");
    Check(ShoreMaterial.OfSurface("grass", false) == ShoreMaterial.Beach, "an unknown surface: a beach");
}

// chernozem: two blocks of high topsoil (no chernozem on Baltic land, so set the group directly)
var ch = model.At(56.95, 24.10); ch.Soil = "Chernozems"; ch.Cover = "loess";
model.Build(ch, GroundColumns.Surface.Land, col);
Console.WriteLine("\nchernozem test: " + model.Describe(col));
Check(col.TopsoilFertility == 3 && col.End[0] == 2, "chernozem is high fertility, two blocks");
Check(GroundColumns.FertilityBlockCodes[col.TopsoilFertility] == "compost", "natural high fertility is soil-compost, never terra preta (soil-high)");

// 0048 soil rules 2: a real desert (Place.Arid) has no topsoil; the cover's own top is the ground
foreach (var (cover, first) in new[] { ("sand-sheet", "sand"), ("alluvium", "silt"), ("fan", "gravel") })
{
    var ds = model.At(56.95, 24.10, new GroundColumns.Place { Elevation = 50, Surface = GroundColumns.Surface.Land, RegionalRock = true, Arid = true });
    Check(ds.Arid, "the sample carries the place's aridity");
    ds.Cover = cover; ds.Soil = "Arenosols"; ds.QuaternaryMetres = 12;
    model.Build(ds, GroundColumns.Surface.Land, col);
    Console.WriteLine($"\ndesert on {cover}: " + model.Describe(col));
    Check(col.Material[0] != model.Topsoil && col.TopsoilFertility < 0, $"desert on {cover} has no topsoil");
    Check(col.Material[0] == M(first), $"desert on {cover} starts with {first} (got {model.Materials[col.Material[0]]})");
    ds.Arid = false;
    model.Build(ds, GroundColumns.Surface.Land, col);
    Check(col.Material[0] == model.Topsoil, $"semi-arid land on {cover} keeps its topsoil");
}

// 0048 soil rules 3: in a river's flood arid ground keeps its topsoil, wind-blown sand excepted
Check(GroundColumns.WindSand("dune") && GroundColumns.WindSand("dune-on-till") && GroundColumns.WindSand("sand-sheet")
    && !GroundColumns.WindSand("alluvium") && !GroundColumns.WindSand("delta") && !GroundColumns.WindSand("playa") && !GroundColumns.WindSand(null),
    "Dunes and sand sheets are wind-blown sand; alluvium, delta and playa are not");
{
    var coverRecipes = System.Text.Json.JsonDocument.Parse(rules).RootElement.GetProperty("cover").GetProperty("recipes");
    Check(new[] { "dune", "dune-on-till", "sand-sheet" }.All(r => coverRecipes.TryGetProperty(r, out _)), "The wind-blown sand recipes exist in the ground rules");
    foreach (var (lat, lon) in new[] { (56.95, 24.10), (25.70, 32.60), (39.20, 63.70), (26.00, 70.30) })
    {
        var dry = model.At(lat, lon, new GroundColumns.Place { Elevation = 50, Surface = GroundColumns.Surface.Land, RegionalRock = true, Arid = true });
        var flood = model.At(lat, lon, new GroundColumns.Place { Elevation = 50, Surface = GroundColumns.Surface.Land, RegionalRock = true, Arid = true, Floodplain = true });
        var notArid = model.At(lat, lon, new GroundColumns.Place { Elevation = 50, Surface = GroundColumns.Surface.Land, RegionalRock = true, Floodplain = true });
        Console.WriteLine($"flood at {lat}, {lon}: cover {flood.Cover} ({flood.CoverUnit}), arid {flood.Arid}");
        Check(dry.Arid && flood.Cover == dry.Cover && flood.Arid == GroundColumns.WindSand(flood.Cover) && !notArid.Arid,
            $"at {lat}, {lon} the flood keeps topsoil on {flood.Cover} only if it is not wind-blown sand, and never makes land arid");
    }
}

// no data: positions outside every regional tile get soil and cover from the global tiles and rules; with no template there, the
// column hands the rock below to Earth's regional geology
// (central Africa has the world-wide 2' soil and thickness sets; north of 84 N and the open Pacific have no data)
foreach (var (name, lat, lon, global) in new[] { ("central Africa", 5.0, 20.0, true), ("north of GUM", 86.0, 0.0, false), ("Pacific", -10.0, -140.0, false) })
{
    var sm = model.At(lat, lon);
    model.Build(sm, GroundColumns.Surface.Land, col);
    Console.WriteLine($"\n{name}: {sm}\n  " + model.Describe(col));
    Check(col.Count >= 1 && col.HandOff && col.Depth < 200, name + " hands the rock below to regional geology");
    Check(global ? sm.SoilSource?.StartsWith("global-soilgrids") == true && sm.QSource?.StartsWith("global-shangguan") == true
                 : sm.SoilSource == null && sm.QSource == null, name + (global ? " gets soil and thickness from the world sets" : " falls back to rules"));
}
var sahara = model.At(25.0, 10.0);
Check(sahara.QuaternaryMetres <= 20, $"bare rock keeps a thin cover where Shangguan is deep ({sahara.QuaternaryMetres:0} m)");
Console.WriteLine("unknown names outside the Baltic (default used, logged once): " + (unknown.Count == 0 ? "none" : string.Join(", ", unknown)));

// GUM's empty cells (sea and bare rock) are filled in the bake: coastal columns take their land neighbour (Carnikava, 6 November),
// bare rock gets a residual cover by GLiM class, and only formerly glaciated ground sits on till
int[] tills = { M("till-reddish"), M("till-grey") };
foreach (var (name, lat, lon, want) in new[] {
    ("Carnikava", 57.135, 24.235, new[] { "alluvium-on-till", "lakeclay", "outwash", "till", "peat-on-till", "marine-on-till", "dune-on-till" }),
    ("Amazon", -3.5, -64.0, new[] { "alluvium", "sediment" }),
    ("Antarctica", -80.0, 0.0, new[] { "ice" }),
    ("Sahara hamada", 25.0, 10.0, new[] { "residual", "residual-mafic", "residual-karst", "thin", "calcrete", "gypcrete", "sand-sheet" }),
    ("Loess Plateau", 36.0, 108.0, new[] { "loess" }),
    ("Salar de Uyuni", -20.2, -67.5, new[] { "playa" }),
    ("Deccan", 19.0, 76.0, new[] { "residual-mafic", "residual" }) })
{
    var sm = model.At(lat, lon);
    model.Build(sm, GroundColumns.Surface.Land, col);
    Console.WriteLine($"\n{name}: {sm}\n  " + model.Describe(col));
    Check(want.Contains(sm.Cover), $"{name} cover {sm.Cover} ({sm.CoverUnit}), expected {string.Join("/", want)}");
}
int tillOutside = 0, landOutside = 0;
for (double la = -55; la < 75; la += 1.5) for (double lo = -179; lo < 180; lo += 1.5)
{
    if (model.Data.TryCategory("glaciation", la, lo, model.Seed, out _, out _)) continue;
    var sm = model.At(la, lo);
    if (sm.CoverUnit == null) continue;
    model.Build(sm, GroundColumns.Surface.Land, col); landOutside++;
    if (sm.Cover is not ("till" or "lakeclay" or "outwash" or "ice") && Enumerable.Range(0, col.Count).Any(i => tills.Contains(col.Material[i]))) tillOutside++;
}
Console.WriteLine($"\noutside formerly glaciated ground: {landOutside} mapped cells, {tillOutside} with till under a non-glacial unit; tiles unpacked {GroundTile.Unpacked.Tiles}, {GroundTile.Unpacked.Bytes >> 20} MB");
Check(GroundTile.Unpacked.Bytes <= GroundTile.BudgetBytes, "unpacked tiles stay within the memory budget");
Check(model.Data.TryCategory("glaciation", 56.95, 24.10, model.Seed, out string gr, out _) && gr == "lgm", "Riga was under the last ice sheet");
Check(model.Data.TryCategory("glaciation", 43.7, -79.4, model.Seed, out string gt, out _) && gt == "lgm", "Toronto was under the last ice sheet");
Check(!model.Data.TryCategory("glaciation", 48.85, 2.35, model.Seed, out _, out _), "Paris was never under an ice sheet");
Check(landOutside > 2000 && tillOutside == 0, "no till under non-glacial units outside formerly glaciated ground");

// step 4: the place (elevation, lake or sea, river valley, regional rock) refines the cover, the sea floor and the rock below
GroundColumns.Sample Site(string name, double lat, double lon, GroundColumns.Place place)
{
    var sm = model.At(lat, lon, place);
    model.Build(sm, place.Surface, col);
    Console.WriteLine($"\n{name}: {sm}\n  " + model.Describe(col));
    return sm;
}
bool Has(params string[] names) => Enumerable.Range(0, col.Count).Any(i => names.Select(M).Contains(col.Material[i]));
int Basement() => col.Material[col.Count - 1];
GroundColumns.Place Sea(double elevation) => new() { Elevation = elevation, Surface = GroundColumns.Surface.Water, RegionalRock = false };
GroundColumns.Place Land(bool regionalRock = true, bool nearRiver = false) => new() { Elevation = 200, Surface = GroundColumns.Surface.Land, RegionalRock = regionalRock, NearRiver = nearRiver };

var deep = Site("deep Pacific", -10.0, -140.0, Sea(-4800));
Check(deep.Cover == "pelagic-clay" && deep.Seabed == "red clay", "abyssal plain: red clay below the carbonate compensation depth");
Check(!col.HandOff && new[] { M("basalt"), M("gabbro") }.Contains(Basement()), "oceanic crust under the sea-floor sediment: basalt with gabbro bodies");
var north = Site("North Pacific", 52.0, -165.0, Sea(-4000));
Check(north.Cover == "ooze-siliceous" && north.Seabed == "siliceous ooze", "high-latitude deep sea: siliceous ooze");
var ridge = Site("mid-Atlantic", -10.0, -14.0, Sea(-3000));
Check(ridge.Cover == "ooze-calcareous" && ridge.Seabed == "calcareous ooze", "deep sea above the CCD: calcareous ooze");
var nsea = Site("North Sea", 56.0, 3.0, Sea(-60));
Check(nsea.Seabed == "4. Mixed sediment" && nsea.Cover is not ("residual" or "ooze-calcareous"), "formerly glaciated shelf: gravelly floor over glacial or marine cover");
var bahamas = Site("Bahamas shelf", 24.0, -77.5, Sea(-10));
Check(bahamas.Seabed == "carbonate sand", "tropical shelf: carbonate sand");
var lake = Site("Lake Ladoga", 61.0, 31.5, new() { Elevation = -50, Surface = GroundColumns.Surface.Water, Lake = true });
Check(lake.Cover == "lakeclay" && lake.Seabed == null, "lake floor on glaciated ground: lake clay over till");
var wadi = Site("Sahara river valley", 25.0, 10.0, Land(nearRiver: true));
Check(wadi.Cover == "alluvium", "river valleys on bare rock take alluvium");
var seine = Site("Seine bed, Paris", 48.85, 2.35, new() { Elevation = 30, Surface = GroundColumns.Surface.RiverBed, NearRiver = true, RegionalRock = true });
Check(seine.Cover == "alluvium", "a river bed on loess takes alluvium (bench, 7 November)");
var niger = Site("Niger bed, Niamey", 13.5, 2.1, new() { Elevation = 180, Surface = GroundColumns.Surface.RiverBed, NearRiver = true, RegionalRock = true });
Check(niger.Cover == "alluvium", "a river through dunes takes alluvium (bench, 7 November)");
var yakutia = Site("Yakutia", 62.0, 129.7, Land());
Check(yakutia.Frozen && Has("permafrost-silt") && Has("permafrost-sand") && Has("permafrost-gravel") && Has("permafrost-clay") && !Has("sand", "gravel", "clay-blue"),
    "permafrost: silt, sand, gravel and clay frozen below the active layer");
var frozenDeep = yakutia; frozenDeep.QuaternaryMetres = 400; model.Build(frozenDeep, GroundColumns.Surface.Land, col);
Check(Enumerable.Range(1, col.Count - 1).All(i => !new[] { M("permafrost-sand"), M("permafrost-gravel") }.Contains(col.Material[i]) || col.EndMetres[i - 1] < 150)
      && Has("sandstone", "conglomerate"), "frozen sand and gravel still turn to rock below 150 m");
Check(!Site("Paris Basin", 48.85, 2.35, Land()).Frozen, "Paris is not frozen");
Check(col.HandOff, "where regional geology maps rock, the column hands the rock to it");
var amazon = Site("Amazon, no regional rock", -3.5, -64.0, Land(regionalRock: false));
Check(!col.HandOff && Has("sandstone", "claystone") && new[] { M("gneiss"), M("granite") }.Contains(Basement()), "loose-sediment basin: generic platform beds down to the basement");
var shield = Site("Canadian Shield, no regional rock", 58.0, -95.0, Land(regionalRock: false));
Check(shield.Province == "shield" && !col.HandOff && col.EndMetres[col.Count - 2] <= 100 && new[] { M("gneiss"), M("granite"), M("schist") }.Contains(Basement()),
    "shield: crystalline basement within CRUST1.0's thin sediment");
var northSeaRock = Site("North Sea, rock below", 56.0, 3.0, Sea(-60));
Check(Has("sandstone", "shale", "limestone") && !Has("tuff"), "a buried old arc (North Sea) gets basin beds, not volcanics");
foreach (var (name, lat, lon) in new[] { ("Sahel", 13.5, 2.1), ("Alps", 46.5, 9.8), ("Iceland", 64.8, -18.5), ("Great Plains", 40.0, -100.0), ("West Siberia", 62.0, 75.0) })
{
    var sm = Site(name, lat, lon, Land());
    Check(sm.Cover != null && col.Count >= 2, name + " has a column");
}

// statistics over Latvian land: fertility classes, cover thickness, basement mix, nothing loose below its compaction depth
var fert = new int[4]; int n = 0; var qb = new List<int>(); int gneiss = 0, granite = 0, looseDeep = 0;
int[] loose = { M("clay-red"), M("clay-blue"), M("silt"), M("mud"), M("sand"), M("gravel") };
for (double la = 55.70; la < 58.05; la += .02) for (double lo = 21.0; lo < 28.2; lo += .02)
{
    var sm = model.At(la, lo);
    if (sm.Soil == null) continue;
    model.Build(sm, GroundColumns.Surface.Land, col);
    if (col.TopsoilFertility >= 0) fert[col.TopsoilFertility]++;
    n++; qb.Add((int)Math.Round(cv.Blocks(sm.QuaternaryMetres)));
    if (col.Material[col.Count - 1] == M("gneiss")) gneiss++; else granite++;
    for (int i = 1; i < col.Count; i++) if (loose.Contains(col.Material[i]) && col.EndMetres[i - 1] > 205) looseDeep++;
}
qb.Sort();
Console.WriteLine($"\nLatvia box, {n} land samples: fertility verylow {fert[0] * 100 / n}%, low {fert[1] * 100 / n}%, medium {fert[2] * 100 / n}%, high {fert[3] * 100 / n}%; cover blocks median {qb[qb.Count / 2]}, p90 {qb[qb.Count * 9 / 10]}, max {qb[^1]}; basement gneiss {gneiss * 100 / n}%, granite {granite * 100 / n}%");
Check(fert[3] * 100 / Math.Max(1, n) < 5, "high fertility rare in the Baltics");
Check(gneiss * 100 / n is >= 40 and <= 85 && granite > 0, "basement mix of gneiss and granite");
Check(looseDeep == 0, $"no loose sediment below its compaction depth ({looseDeep} layers)");

// Crafted stone of Earth's rocks: vanilla's template moved into the rock's palette (EarthStoneTextures, the author 7 Nov)
{
    var rnd = new Random(5);
    int[] img = Enumerable.Range(0, 32 * 32).Select(_ => unchecked((int)0xFF000000) | (rnd.Next(150, 220) << 16) | (rnd.Next(140, 200) << 8) | rnd.Next(90, 150)).ToArray();
    var yellow = EarthMap.Core.StoneRecolour.Measure(new[] { img });
    var same = EarthMap.Core.StoneRecolour.Transfer(img, yellow, yellow);
    Check(img.Zip(same).All(t => Enumerable.Range(0, 3).All(c => Math.Abs(((t.First >> (8 * c)) & 255) - ((t.Second >> (8 * c)) & 255)) <= 1)),
        "stone recolour: the same palette leaves the image unchanged");
    var rose = new EarthMap.Core.StoneRecolour.Palette(180, -6, 12, yellow.SpreadY);
    var moved = EarthMap.Core.StoneRecolour.Measure(new[] { EarthMap.Core.StoneRecolour.Transfer(img, yellow, rose) });
    Check(Math.Abs(moved.Y - 180) < 3 && Math.Abs(moved.Cb + 6) < 2 && Math.Abs(moved.Cr - 12) < 2, $"stone recolour: yellow limestone-like palette moved to rose quartzite ({moved})");
    var png = EarthMap.Core.StoneRecolour.EncodePng(img, 32, 32);
    // decode: IHDR size, IDAT inflated back to the rows
    var ms = new MemoryStream(png, 8, png.Length - 8); var idat = new MemoryStream(); int w = 0, h = 0;
    while (ms.Position < ms.Length)
    {
        var hd = new byte[8]; ms.ReadExactly(hd); int len = (hd[0] << 24) | (hd[1] << 16) | (hd[2] << 8) | hd[3];
        string type = System.Text.Encoding.ASCII.GetString(hd, 4, 4); var data = new byte[len]; ms.ReadExactly(data); ms.Position += 4;
        if (type == "IHDR") { w = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]; h = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]; }
        if (type == "IDAT") idat.Write(data);
    }
    idat.Position = 0; var raw = new MemoryStream(); using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
    var rb = raw.ToArray(); int px = img[5 * 32 + 7], at = 5 * (32 * 4 + 1) + 1 + 7 * 4;
    Check(w == 32 && h == 32 && rb.Length == 32 * 129 && rb[at] == (byte)(px >> 16) && rb[at + 3] == (byte)(px >> 24), "stone recolour: PNG round trip");
}

// A8: two worlds in one process (single player goes back to the menu and on to another world): the process-wide memory
// budget's list of unpacked tiles must not keep a closed world's ground tiles (and their cells) alive
{
    byte[] head = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"a8\",\"west\":0,\"north\":1,\"step\":1,\"nx\":1,\"ny\":1,\"fields\":[{\"name\":\"x\",\"type\":\"u8\",\"nodata\":255}]}");
    var packedBody = new MemoryStream();
    using (var z = new System.IO.Compression.ZLibStream(packedBody, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) z.Write(new byte[] { 7 });
    byte[] tileFile = System.Text.Encoding.ASCII.GetBytes("EGT1").Concat(BitConverter.GetBytes((uint)head.Length)).Concat(head).Concat(packedBody.ToArray()).ToArray();
    var closedWorld = ClosedWorldTile(tileFile, out double value);
    Check(value == 7, "a world's tile is read through the process-wide budget");
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(!closedWorld.IsAlive, "the unpacked tiles' list does not keep a closed world's tile alive (A8)");
    var (tilesNow, bytesNow) = GroundTile.Unpacked;
    Check(bytesNow <= GroundTile.BudgetBytes && tilesNow >= 0, "the budget's count stays consistent once a tile is gone");
    GC.KeepAlive(model);
}
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference ClosedWorldTile(byte[] file, out double value)
{
    var tile = GroundTile.ReadAll(file)[0];
    value = tile.ValueAtCell(tile.Fields[0], 0, 0);
    return new WeakReference(tile);
}

// ground rules 2 (EA-0062): karst (GUM Rk) keeps terra rossa where it is warm, rendzina where it is cooler than 12 °C;
// worlds made with rules 1 keep terra rossa everywhere, and an unknown temperature changes nothing
Check(GroundColumns.RulesVersion == 2, "new worlds get ground rules 2");
{
    int karst = 0;
    foreach (var (name, la, lo) in new[] { ("Slovenian Karst", 45.70, 13.90), ("Dalmatia", 43.50, 16.50), ("Velebit", 44.60, 15.20),
        ("Attica", 38.00, 23.80), ("Provence", 43.80, 4.80), ("Apulia", 40.90, 16.60), ("Causses", 44.30, 3.30), ("Jura", 46.90, 6.50),
        ("Northern Calcareous Alps", 47.60, 13.00), ("Yucatan", 20.50, -89.00), ("Guangxi", 24.80, 110.40), ("Jamaica", 18.20, -77.50) })
    {
        GroundColumns.Place P(int rules, bool known, double t) => new() { Elevation = 400, Surface = GroundColumns.Surface.Land, RegionalRock = true, GroundRules = rules, HasTemperature = known, TMean = t };
        var old = model.At(la, lo, P(1, true, 5));
        if (old.CoverUnit != "Rk" || old.Cover != "residual-karst") { Console.WriteLine($"  {name}: unit {old.CoverUnit}, recipe {old.Cover} (not plain karst)"); continue; }
        karst++;
        string cool = model.At(la, lo, P(2, true, 8)).Cover, warm = model.At(la, lo, P(2, true, 16)).Cover, unknownT = model.At(la, lo, P(2, false, 0)).Cover;
        Check(cool == "rendzina" && warm == "residual-karst" && unknownT == "residual-karst",
            $"karst at {name}: rules 1 terra rossa; rules 2 rendzina at 8 °C (got {cool}), terra rossa at 16 °C (got {warm}) and when unknown (got {unknownT})");
    }
    Check(karst >= 3, $"at least three of the karst places are plain karst in the data ({karst})");
}

// EA-0116 D-L9: a set whose count is damaged is refused before anything is made from the count
{
    var damagedSet = new byte[64];
    System.Text.Encoding.ASCII.GetBytes("EGTS").CopyTo(damagedSet, 0);
    BitConverter.GetBytes(uint.MaxValue).CopyTo(damagedSet, 4);
    string kind = "none";
    try { GroundTile.ReadAll(damagedSet); }
    catch (Exception e) { kind = e.GetType().Name; }
    Check(kind == "FormatException", $"EA-0116 D-L9: a ground set with a count of 4,294,967,295 in 64 bytes is a FormatException ({kind})");
}

Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;
