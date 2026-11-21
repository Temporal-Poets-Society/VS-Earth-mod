using System.Globalization;
using System.Text;
using System.Text.Json;
using EarthMap.Core;

// Ground-columns audit probe (read-only on the repo). Usage: Probe <repo root> <ground30 folder> <out folder> [mode]
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string root = args[0], data30 = args[1], outDir = args[2];
string mode = args.Length > 3 ? args[3] : "all";
Directory.CreateDirectory(outDir);
string rules = File.ReadAllText(Path.Combine(root, "assets/earth/config/ground-rules-v1.json"));
string oreRules = File.ReadAllText(Path.Combine(root, "assets/earth/config/ore-layers-v4.json"));
byte[] ReadAsset(string loc) { var p = Path.Combine(root, "assets", loc.Split(':')[0], loc.Split(':')[1]); return File.Exists(p) ? File.ReadAllBytes(p) : null; }

double Mpb(int h, bool linear) => 1 / new VerticalScale(h, 110, h - 2, 1000, 47, 20, linear).Slope(0);
var presets = new (string Name, double T)[]
{
    ("256 high log1000 (auto <512)", Mpb(256, false)),
    ("384 high log1000 (presets 1:200/1:100/1:50)", Mpb(384, false)),
    ("512 high linear (preset 1:25)", Mpb(512, true)),
    ("1024 high linear (preset 1:10; 1:5 at max height)", Mpb(1024, true)),
    ("1024 high log1000 (hand-chosen)", Mpb(1024, false)),
};
var sb = new StringBuilder();
void P(string s) { Console.WriteLine(s); sb.AppendLine(s); }

using var rdoc = JsonDocument.Parse(rules, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
var recipeMax = new Dictionary<string, double>();
foreach (var r in rdoc.RootElement.GetProperty("cover").GetProperty("recipes").EnumerateObject())
    recipeMax[r.Name] = r.Value.TryGetProperty("maxMetres", out var mx) ? mx.GetDouble() : double.PositiveInfinity;
var unitMap = new Dictionary<string, string>();
foreach (var u in rdoc.RootElement.GetProperty("cover").GetProperty("units").EnumerateObject()) unitMap[u.Name] = u.Value.GetString();

if (mode is "all" or "curve")
{
    P("== Depth curves (GroundColumns.DepthCurve.For, sea level 110, lowland column 114 blocks)");
    P("preset | choice | m/blk top | knee D0 | blocks: 1 m, 10 m, 100 m, 1000 m, 2000 m | metres at block 114 | m per block at 100 m, 1000 m");
    foreach (var (pn, t) in presets)
        foreach (var ch in GroundColumns.DepthCurve.Choices)
        {
            var c = GroundColumns.DepthCurve.For(ch, t, 110);
            double Local(double m) => c.Linear ? c.Top : c.Top * (1 + m / c.D0);
            P($"{pn} | {ch} | {c.Top:0.00} | {(c.Linear ? "linear" : c.D0.ToString("0"))} | {c.Blocks(1):0.00}, {c.Blocks(10):0.00}, {c.Blocks(100):0.0}, {c.Blocks(1000):0.0}, {c.Blocks(2000):0.0} | {c.Metres(114):0} | {Local(100):0.0}, {Local(1000):0.0}");
        }
}

GroundColumns ModelFor(double t, string choice = GroundColumns.DepthCurve.Default, bool with30 = true)
    => GroundColumns.Load(rules, ReadAsset, GroundColumns.DepthCurve.For(choice, t, 110), 1234, with30 ? (f => Path.Combine(data30, Path.GetFileName(f))) : null);

if (mode is "all" or "synthetic")
{
    P("\n== Synthetic columns (Build), Surface Land, RegionalRock true = hand-off below cover unless noted");
    foreach (var (pn, t) in new[] { presets[1], presets[3] })
    {
        var m = ModelFor(t);
        var ol = new OreLayers(oreRules, m);
        var col = new GroundColumns.Column();
        P($"-- {pn}: {m.Curve}");
        void Show(string label, string cover, double q, string soil, bool regional = true, double basement = 1000, string era = null, string prov = null, GroundColumns.Surface surf = GroundColumns.Surface.Land, bool frozen = false)
        {
            var s = new GroundColumns.Sample { Lat = 50, Lon = 10, Cover = cover, QuaternaryMetres = q, BasementMetres = basement, Soil = soil, Clay = double.NaN, Ph = double.NaN, RegionalRock = regional, Era = era, Province = prov, Slope = double.NaN, Relief = double.NaN, ReliefFactor = 1, Frozen = frozen };
            m.Build(s, surf, col);
            P($"{label}: {m.Describe(col)}");
            P($"    classes: {ol.DescribeColumn(col, era)}");
        }
        foreach (double q in new[] { 0.6, 1, 1.5, 2, 3, 5, 10, 20 }) Show($"alluvium q={q} m, Fluvisols", "alluvium", q, "Fluvisols");
        foreach (double q in new[] { 1, 3, 8 }) Show($"thin-till q={q}, Podzols", "thin-till", q, "Podzols");
        foreach (double q in new[] { 10, 25, 60 }) Show($"till q={q}, Luvisols", "till", q, "Luvisols");
        Show("outwash q=6, Podzols", "outwash", 6, "Podzols");
        Show("alluvium-on-till q=4, Gleysols", "alluvium-on-till", 4, "Gleysols");
        Show("lakeclay q=5, Gleysols", "lakeclay", 5, "Gleysols");
        Show("laterite q=12, Plinthosols", "laterite", 12, "Plinthosols");
        Show("laterite q=3, Lixisols", "laterite", 3, "Lixisols");
        Show("saprolite q=15, Ferralsols", "saprolite", 15, "Ferralsols");
        Show("residual q=3, Cambisols", "residual", 3, "Cambisols");
        Show("peat q=2, Histosols", "peat", 2, "Histosols");
        Show("peat q=6, Histosols", "peat", 6, "Histosols");
        Show("playa q=15, Solonchaks", "playa", 15, "Solonchaks");
        Show("loess q=20, Chernozems", "loess", 20, "Chernozems");
        Show("residual-karst q=1.5, Leptosols", "residual-karst", 1.5, "Leptosols");
        Show("100 m sediment over basement 1000 m, platform (craton, Mesozoic)", "sediment", 80, "Cambisols", regional: false, basement: 1000, era: "Mesozoic", prov: "craton");
        Show("shield, basement at 0 (CRUST1 0 m), Archean", "thin-till", 3, "Podzols", regional: false, basement: 0, era: "Archean", prov: "shield");
        Show("platform Paleozoic, basement 3000 m", "till", 25, "Luvisols", regional: false, basement: 3000, era: "Paleozoic", prov: "craton");
        Show("foldbelt Paleozoic, basement 8000 m", "thin", 1, "Leptosols", regional: false, basement: 8000, era: "Paleozoic", prov: "orogenic belt");
        Show("alluvium q=20 frozen (permafrost)", "alluvium", 20, "Cryosols", frozen: true);
        Show("riverbed, alluvium q=5", "alluvium", 5, "Fluvisols", surf: GroundColumns.Surface.RiverBed);
    }
}

if (mode is "all" or "global")
{
    // global land sampling at 0.1 degree; Place without terrain (no relief, no river valley), land surface
    double t10 = presets[3].T;
    var m = ModelFor(t10);
    var ol = new OreLayers(oreRules, m);
    var m384 = ModelFor(presets[1].T);
    string gridJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-grids-v1.json"));
    var regional = new RegionalGeologyCatalog(gridJson, File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-rules-v2.json")));
    using var gdoc = JsonDocument.Parse(gridJson);
    byte[] lith = gdoc.RootElement.GetProperty("lithology").GetBytesFromBase64();
    byte[] sed = gdoc.RootElement.GetProperty("sediment").GetBytesFromBase64();
    P($"\n== Global land sample at 0.1 deg (84 N .. 56 S), weights cos(lat); missing optional tiles: {(m.MissingTiles.Count == 0 ? "none" : string.Join(", ", m.MissingTiles))}");
    var W = new Dictionary<string, Dictionary<string, double>>();
    void Add(string table, string key, double w) { if (!W.TryGetValue(table, out var d)) W[table] = d = new(); d[key] = d.GetValueOrDefault(key) + w; }
    var qByRecipe = new Dictionary<string, List<(double q, double w)>>();
    const double step = 0.1;
    int nx = 3600, ny = 1400; // rows from 84 N
    var flags = new ushort[nx * ny];
    var recipeIdx = new byte[nx * ny];
    var recipeNames = new List<string> { "-" };
    double land = 0, sea = 0;
    var col = new GroundColumns.Column();
    var col384 = new GroundColumns.Column();
    int mClayRed = Array.IndexOf(m.Materials, "clay-red"), mClayBlue = Array.IndexOf(m.Materials, "clay-blue"), mGravel = Array.IndexOf(m.Materials, "gravel"),
        mSand = Array.IndexOf(m.Materials, "sand"), mPeat = m.Peat, mMarlLake = Array.IndexOf(m.Materials, "marl-lake"), mPfClay = Array.IndexOf(m.Materials, "permafrost-clay"),
        mPfGravel = Array.IndexOf(m.Materials, "permafrost-gravel"), mPfSand = Array.IndexOf(m.Materials, "permafrost-sand");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // iterate by 10-degree blocks so tiles stay unpacked
    for (int by = 0; by < 14; by++)
        for (int bx = 0; bx < 36; bx++)
            for (int j = by * 100; j < by * 100 + 100; j++)
            {
                double lat = 84 - (j + 0.5) * step;
                double w = Math.Cos(lat * Math.PI / 180);
                for (int i = bx * 100; i < bx * 100 + 100; i++)
                {
                    double lon = -180 + (i + 0.5) * step;
                    int k = j * nx + i;
                    var reg = regional.At(lat, lon);
                    var basePlace = new GroundColumns.Place { Elevation = double.NaN, Surface = GroundColumns.Surface.Land, RegionalRock = reg.HasRegionalBedrock, GroundRules = 2 };
                    var s = m.At(lat, lon, basePlace);
                    if (s.Soil == null)
                    {
                        sea += w;
                        string uc = s.CoverUnit == null ? "none (beyond fill)" : s.CoverUnit is "Rx" or "Rm" or "Rk" ? "R* rock code" : "GUM unit " + (unitMap.GetValueOrDefault(s.CoverUnit) ?? s.CoverUnit);
                        Add("sea/no-soil: cover unit", uc, w);
                        continue;
                    }
                    land += w;
                    ushort f = 1; // bit0 land
                    int gi = Math.Clamp((int)Math.Floor((90 - lat) * 2), 0, 359) * 720 + Math.Clamp((int)Math.Floor((lon + 180) * 2), 0, 719);
                    bool rawGum = sed[gi] != 0, su = lith[gi] == 1;
                    string unit = s.CoverUnit;
                    string ucls = unit == null ? "none" : unit is "Rx" or "Rm" or "Rk" ? "R* (no sediment mapped; GLiM rock class)" : unit is "Wu" or "Iy" or "Pu" or "Du" && !rawGum ? "GLiM-derived special (Wu/Iy/Pu/Du)" : rawGum ? "GUM sediment unit (mapped cell)" : su ? "GUM unit borrowed (GLiM su, nearest GUM)" : "GUM unit (other fill)";
                    Add("land: cover unit source", ucls, w);
                    Add("land: recipe", s.Cover, w);
                    string baseRecipe = unit == null ? "residual(default)" : unitMap.GetValueOrDefault(unit, "residual(default)");
                    Add("land: recipe decided by", s.Cover == baseRecipe ? "unit map (GUM/R* code)" : "region rule (soil group / glaciation / place)", w);
                    if (s.Cover != baseRecipe) Add("land: region-rule recipe (from unit-map recipe)", $"{s.Cover} (unit map: {baseRecipe})", w);
                    Add("land: soil group", s.Soil, w);
                    Add("land: soil source", s.SoilSource?.Contains("30s") == true ? "30\" SoilGrids" : s.SoilSource?.StartsWith("baltic") == true ? "Baltic 0.01 deg" : "2' SoilGrids", w);
                    string qs = s.QSource == null ? "typicalMetres (rule)" : s.QSource.Contains("30s") ? "30\" Shangguan" : s.QSource.StartsWith("latvia") ? "Latvia 250 m (national)" : "2' Shangguan";
                    Add("land: thickness source", qs, w);
                    if (s.QRule != null) Add("land: thickness rule applied", s.QRule, w);
                    double max = recipeMax.GetValueOrDefault(s.Cover, double.PositiveInfinity);
                    if (double.IsFinite(max) && s.QuaternaryMetres >= max - 1e-9) Add("land: thickness capped at recipe maxMetres", s.Cover + " (" + max + " m)", w);
                    Add("land: bedrock era (Chorlton)", s.Era ?? "(none)", w);
                    Add("land: era -> age class", s.Era == null ? "no era" : ol.AgeOfEra(s.Era).ToString(), w);
                    Add("land: province (Hasterok)", s.Province ?? "(none)", w);
                    m.Data.TryCategory("glaciation", lat, lon, m.Seed, out string glac, out _);
                    Add("land: glaciation", glac ?? "never (or S. Hemisphere)", w);
                    if (s.Frozen) Add("land: frozen cover (permafrost >= 50 %)", "frozen", w);
                    Add("land: regional geology", reg.HasRegionalBedrock ? "maps rock (hand-off below cover)" : "unknown/covered (province template, CRUST1 + Chorlton)", w);
                    Add("land: basement source", s.BasementSource ?? "default 1000 m", w);
                    if (!qByRecipe.TryGetValue(s.Cover, out var ql)) qByRecipe[s.Cover] = ql = new();
                    ql.Add((s.QuaternaryMetres, w));
                    // the built column (1:10 curve) and what the layers allow
                    m.Build(s, GroundColumns.Surface.Land, col);
                    bool clay = false, clayTop4 = false, alluvial = false, outwash = false, peat = false, laterite = false, saprolite = false, playa = false, gravelTop3 = false, sandGravelTop3 = false, coverAny = false;
                    int start = 0;
                    for (int g = 0; g < col.Segments; g++)
                    {
                        int code = ol.CodeOf(col, g);
                        var cls = OreLayers.ClassOf(code);
                        int mat = col.SegMaterial[g];
                        if (col.SegPart[g] is GroundColumns.Part.Cover or GroundColumns.Part.Top) coverAny |= mat != m.Topsoil;
                        if (mat == mClayRed || mat == mClayBlue || mat == mPfClay) { clay = true; if (start < 4) clayTop4 = true; }
                        if ((mat == mGravel || mat == mPfGravel) && start < 3) gravelTop3 = true;
                        if ((mat == mGravel || mat == mSand || mat == mPfGravel || mat == mPfSand) && start < 3 && col.SegPart[g] != GroundColumns.Part.Bed) sandGravelTop3 = true;
                        alluvial |= cls == OreLayers.Class.Alluvial; outwash |= cls == OreLayers.Class.Outwash; peat |= cls == OreLayers.Class.Peat;
                        laterite |= cls == OreLayers.Class.Laterite; saprolite |= cls == OreLayers.Class.Saprolite; playa |= cls == OreLayers.Class.Playa;
                        start = col.SegEnd[g];
                    }
                    void Flag(string name, bool v, int bit) { if (v) { Add("land: column holds (1:10 curve, interfluve)", name, w); f |= (ushort)(1 << bit); } }
                    Flag("raw clay (any depth in ground column)", clay, 1);
                    Flag("raw clay within top 4 blocks", clayTop4, 2);
                    Flag("alluvial class (placers)", alluvial, 3);
                    Flag("outwash class (placers)", outwash, 4);
                    Flag("peat class (bog iron)", peat, 5);
                    Flag("laterite class (bauxite)", laterite, 6);
                    Flag("saprolite class (kaolin/fire clay)", saprolite, 7);
                    Flag("playa class (salt flats)", playa, 8);
                    Flag("gravel within top 3 blocks", gravelTop3, 9);
                    Flag("sand or gravel within top 3 blocks (panning flint)", sandGravelTop3, 10);
                    Flag("no loose cover block at all (soil straight on rock/hand-off)", !coverAny, 11);
                    bool alluvialRecipe = s.Cover is "alluvium" or "alluvium-on-till" or "fan" or "delta";
                    if (alluvialRecipe && !alluvial) Add("land: alluvial recipe but NO alluvial-class block (sub-layers < 0.5 m)", s.Cover, w);
                    if (alluvialRecipe) Add("land: alluvial recipes", s.Cover, w);
                    // what a river valley would get here (NearRiver true)
                    var vPlace = basePlace; vPlace.NearRiver = true;
                    var sv = m.At(lat, lon, vPlace);
                    m.Build(sv, GroundColumns.Surface.Land, col);
                    bool vAll = false, vClay = false;
                    for (int g = 0; g < col.Segments; g++)
                    {
                        var cls = OreLayers.ClassOf(ol.CodeOf(col, g));
                        vAll |= cls is OreLayers.Class.Alluvial or OreLayers.Class.Outwash;
                        int mat = col.SegMaterial[g]; vClay |= mat == mClayRed || mat == mClayBlue || mat == mPfClay;
                    }
                    Add("valley (NearRiver): recipe", sv.Cover, w);
                    if (vAll) { Add("valley (NearRiver): column holds", "alluvial/outwash class", w); f |= 1 << 12; }
                    if (vClay) { Add("valley (NearRiver): column holds", "raw clay", w); f |= 1 << 13; }
                    if (glac != null) { f |= 1 << 14; Add("glaciated valley (NearRiver): recipe", sv.Cover, w); }
                    if (Math.Abs(lat) < 23.5) Add("tropics (|lat|<23.5): recipe", s.Cover, w);
                    flags[k] = f;
                    int ri = recipeNames.IndexOf(s.Cover); if (ri < 0) { recipeNames.Add(s.Cover); ri = recipeNames.Count - 1; }
                    recipeIdx[k] = (byte)ri;
                }
            }
    P($"sampled in {sw.Elapsed.TotalSeconds:0} s; land weight {land:0}, sea/no-soil weight {sea:0}");
    foreach (var (table, d) in W.OrderBy(x => x.Key))
    {
        double total = table.StartsWith("sea") ? sea : table.StartsWith("tropics") ? d.Values.Sum() : table.StartsWith("glaciated valley") ? d.Values.Sum() : land;
        P($"\n-- {table} (share of {(table.StartsWith("sea") ? "sea/no-soil" : table.StartsWith("tropics") ? "tropical land" : table.StartsWith("glaciated valley") ? "glaciated land" : "land")})");
        foreach (var (key, v) in d.OrderByDescending(x => x.Value).Take(40)) P($"   {100 * v / total,6:0.00} %  {key}");
    }
    P("\n-- cover thickness (m) by recipe: weighted p10 / median / p90 / max, share of land");
    foreach (var (r, l) in qByRecipe.OrderByDescending(x => x.Value.Sum(y => y.w)))
    {
        l.Sort((a, b) => a.q.CompareTo(b.q));
        double tw = l.Sum(x => x.w);
        double Pc(double p) { double acc = 0; foreach (var x in l) { acc += x.w; if (acc >= p * tw) return x.q; } return l[^1].q; }
        P($"   {r,-18} {Pc(.1),7:0.0} {Pc(.5),7:0.0} {Pc(.9),7:0.0} {l[^1].q,8:0.0}   {100 * tw / land:0.00} %");
    }
    File.WriteAllBytes(Path.Combine(outDir, "flags.u16"), flags.SelectMany(BitConverter.GetBytes).ToArray());
    File.WriteAllBytes(Path.Combine(outDir, "recipe.u8"), recipeIdx);
    File.WriteAllText(Path.Combine(outDir, "recipes.txt"), string.Join("\n", recipeNames));
}

if (mode is "sites")
{
    // placer and valley sites: 20 x 20 km boxes at 100 m with relief from GLO-90 (as GroundColumnsTests' probe), land surface,
    // NearRiver false and true; what valley floors (relief < -10 m) get
    var m = ModelFor(presets[3].T);
    var ol = new OreLayers(oreRules, m);
    string glo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData/ModData/earth/glo90");
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
        if (t == null) return 0;
        double r = (latS + 1 - la) * t.Rows, c = (lo - lonW) * t.Cols;
        int r0 = Math.Clamp((int)r, 0, t.Rows - 1), c0 = Math.Clamp((int)c, 0, t.Cols - 1);
        int r1 = Math.Min(r0 + 1, t.Rows - 1), c1 = Math.Min(c0 + 1, t.Cols - 1);
        double fr = r - r0, fc = c - c0;
        return (t.At(r0, c0) * (1 - fc) + t.At(r0, c1) * fc) * (1 - fr) + (t.At(r1, c0) * (1 - fc) + t.At(r1, c1) * fc) * fr;
    }
    var sites = new (string Name, double Lat, double Lon)[]
    {
        ("Klondike (Bonanza Ck)", 63.98, -139.30), ("Iss/Tura placers, Urals", 58.60, 59.70), ("Nizhny Tagil", 57.90, 59.95),
        ("Kondyor", 57.60, 134.65), ("Bodaybo (Lena gold)", 57.85, 114.20), ("Kolyma (Susuman)", 62.78, 148.15),
        ("Ballarat", -37.55, 143.85), ("Mother Lode (Coloma)", 38.80, -120.89), ("Otago (Clutha)", -45.20, 169.30),
        ("Choco (Condoto)", 5.10, -76.65), ("Cornwall (Carnon)", 50.23, -5.10), ("Lemmenjoki, Lapland", 68.70, 25.80),
        ("Rhine at Strasbourg", 48.58, 7.75), ("Daugava at Plavinas", 56.62, 25.72), ("Vistula at Warsaw", 52.23, 21.03),
        ("Ganges plain (Patna)", 25.60, 85.10), ("Mississippi (Memphis)", 35.15, -90.05), ("Bangka tin", -2.10, 106.10),
        ("Kinta valley tin", 4.60, 101.10), ("Guiana shield (Suriname)", 4.0, -55.5),
    };
    var mean1 = new double[1]; var slope1 = new double[1]; var norm1 = new double[1];
    P("\n== Sites: 20 x 20 km at 200 m, relief from GLO-90; valley floor = relief < -10 m; 1:10 curve");
    foreach (var (name, plat, plon) in sites)
    {
        double cosL = Math.Cos(plat * Math.PI / 180);
        double LatOf(double z) => plat - z * 100 / 111320.0;
        double LonOf(double x) => plon + x * 100 / (111320.0 * cosL);
        var all = new Dictionary<string, int>(); var floor = new Dictionary<string, int>(); var floorRiv = new Dictionary<string, int>();
        int n = 0, nf = 0, floorAlluvial = 0, floorAlluvialRiv = 0, floorSteep = 0, nClay = 0, nf2 = 0;
        var col = new GroundColumns.Column();
        var regional = new RegionalGeologyCatalog(File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-grids-v1.json")), File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-rules-v2.json")));
        for (int i = 0; i < 100; i++) for (int j = 0; j < 100; j++)
        {
            double x = (j - 50) * 2 + 1, z = (i - 50) * 2 + 1;
            GroundRelief.Field(x, z, 1, (u, v) => Elev(LatOf(v), LonOf(u)), 100, 100, m.ReliefRadiusMetres, mean1, slope1, m.ReliefWeight, norm1);
            double e = Elev(LatOf(z), LonOf(x));
            var place = new GroundColumns.Place { Elevation = e, Surface = GroundColumns.Surface.Land, RegionalRock = regional.At(LatOf(z), LonOf(x)).HasRegionalBedrock, HasRelief = true, Relief = e - mean1[0], Slope = slope1[0], ReliefNorm = norm1[0], GroundRules = 2 };
            var s = m.At(LatOf(z), LonOf(x), place);
            if (s.Soil == null) continue;
            n++; all[s.Cover] = all.GetValueOrDefault(s.Cover) + 1;
            m.Build(s, GroundColumns.Surface.Land, col);
            bool clay = false; for (int g = 0; g < col.Segments; g++) { var mn = m.Materials[col.SegMaterial[g]]; clay |= mn is "clay-red" or "clay-blue" or "permafrost-clay"; }
            if (clay) nClay++;
            if (place.Relief < -10)
            {
                nf++; floor[s.Cover] = floor.GetValueOrDefault(s.Cover) + 1;
                if (place.Slope >= 0.45) floorSteep++;
                bool al = false; for (int g = 0; g < col.Segments; g++) al |= OreLayers.ClassOf(ol.CodeOf(col, g)) is OreLayers.Class.Alluvial or OreLayers.Class.Outwash;
                if (al) floorAlluvial++;
                var pr = place; pr.NearRiver = true;
                var sr = m.At(LatOf(z), LonOf(x), pr);
                floorRiv[sr.Cover] = floorRiv.GetValueOrDefault(sr.Cover) + 1;
                m.Build(sr, GroundColumns.Surface.Land, col);
                al = false; for (int g = 0; g < col.Segments; g++) al |= OreLayers.ClassOf(ol.CodeOf(col, g)) is OreLayers.Class.Alluvial or OreLayers.Class.Outwash;
                if (al) floorAlluvialRiv++;
            }
        }
        string Top(Dictionary<string, int> d, int tot) => string.Join(", ", d.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key} {100 * x.Value / Math.Max(1, tot)}%"));
        var c0 = m.At(plat, plon);
        P($"\n{name} ({plat}, {plon}): unit {c0.CoverUnit ?? "-"}, soil {c0.Soil}, Q {c0.QuaternaryMetres:0.#} m ({c0.QSource}), glaciation? {(m.Data.TryCategory("glaciation", plat, plon, m.Seed, out var gl, out _) ? gl : "no")}, era {c0.Era}");
        P($"   land {n}: {Top(all, n)}; columns with raw clay {100 * nClay / Math.Max(1, n)}%");
        P($"   valley floors {nf} ({100 * nf / Math.Max(1, n)}% of land): {Top(floor, nf)}; steep (slope>=0.45) {100 * floorSteep / Math.Max(1, nf)}%; alluvial/outwash layer {100 * floorAlluvial / Math.Max(1, nf)}%");
        P($"   same floors if a river valley (NearRiver): {Top(floorRiv, nf)}; alluvial/outwash layer {100 * floorAlluvialRiv / Math.Max(1, nf)}%");
    }
}

if (mode is "extra")
{
    var m = ModelFor(presets[3].T);
    var ol = new OreLayers(oreRules, m);
    var col = new GroundColumns.Column();
    P("\n== Deep sea floors within GUM's 3-cell coastal fill (Surface.Water, sea, real elevation given)");
    foreach (var (name, lat, lon, elev) in new[] { ("Tyrrhenian abyssal plain", 39.6, 12.6, -3500.0), ("Ionian Sea", 36.5, 18.5, -3800.0), ("Black Sea centre", 43.0, 34.0, -2200.0),
        ("Japan Sea centre", 40.5, 135.5, -3500.0), ("Red Sea axis", 20.5, 38.8, -1500.0), ("off Valparaiso (trench side)", -33.0, -72.5, -4000.0), ("Norwegian Sea", 67.0, 7.0, -2500.0),
        ("Gulf of Mexico", 25.0, -90.0, -3500.0), ("Bay of Bengal fan", 15.0, 88.0, -2800.0), ("Aegean", 38.0, 25.0, -800.0), ("North Sea", 56.0, 3.0, -60.0), ("Adriatic", 43.5, 15.0, -150.0) })
    {
        var place = new GroundColumns.Place { Elevation = elev, Surface = GroundColumns.Surface.Water, RegionalRock = false, GroundRules = 2 };
        var s = m.At(lat, lon, place);
        m.Build(s, GroundColumns.Surface.Water, col);
        P($"{name} ({lat}, {lon}, {elev} m): unit {s.CoverUnit ?? "-"} -> {s.Cover}; seabed {s.Seabed} ({s.SeabedSource}); {m.Describe(col)}");
        P($"    classes: {ol.DescribeColumn(col, s.Era)}");
    }
    // where typicalMetres applies on land, frozen alluvium, Fluvisols and Histosols cross tables
    var W = new Dictionary<string, Dictionary<string, double>>();
    void Add(string table, string key, double w) { if (!W.TryGetValue(table, out var d)) W[table] = d = new(); d[key] = d.GetValueOrDefault(key) + w; }
    double land = 0;
    for (double lat = 83.95; lat > -56; lat -= 0.2)
    {
        double w = Math.Cos(lat * Math.PI / 180);
        for (double lon = -179.95; lon < 180; lon += 0.2)
        {
            var place = new GroundColumns.Place { Elevation = double.NaN, Surface = GroundColumns.Surface.Land, RegionalRock = true, GroundRules = 2 };
            var s = m.At(lat, lon, place);
            if (s.Soil == null) continue;
            land += w;
            if (s.QSource == null) Add("typicalMetres used (no thickness data): lat band / recipe", $"{(int)Math.Floor(lat / 10) * 10}..{(int)Math.Floor(lat / 10) * 10 + 10}: {s.Cover}", w);
            if (s.Soil == "Fluvisols") Add("Fluvisols land: recipe", s.Cover, w);
            if (s.Soil == "Histosols") Add("Histosols land: recipe", s.Cover, w);
            if (s.Soil is "Gleysols") Add("Gleysols land: recipe", s.Cover, w);
            if (s.Soil is "Andosols") Add("Andosols land: recipe", s.Cover, w);
            if (s.Soil is "Plinthosols" or "Lixisols" or "Ferralsols" or "Acrisols" or "Nitisols" or "Alisols") Add("tropical weathering soils: recipe", s.Cover, w);
            if (s.Soil is "Solonchaks") Add("Solonchaks: recipe", s.Cover, w);
            if (s.Soil is "Chernozems" or "Kastanozems") Add("steppe soils: recipe", s.Cover, w);
            if (s.Cover is "alluvium" or "alluvium-on-till" or "fan" or "delta")
            {
                Add("alluvial recipes: frozen?", s.Frozen ? "frozen (placer hosts gone under vanilla ores)" : "not frozen", w);
            }
            if (s.Cover is "loess" or "loess-on-till" && s.QuaternaryMetres > 100) Add("loess > 100 m: 10-degree box", $"{(int)Math.Floor(lat / 10) * 10},{(int)Math.Floor(lon / 10) * 10}", w);
            if (s.Cover is "till" && s.QuaternaryMetres > 100) Add("till > 100 m: 10-degree box", $"{(int)Math.Floor(lat / 10) * 10},{(int)Math.Floor(lon / 10) * 10}", w);
            if (s.Cover is "playa" && s.QuaternaryMetres > 100) Add("playa > 100 m: 10-degree box", $"{(int)Math.Floor(lat / 10) * 10},{(int)Math.Floor(lon / 10) * 10}", w);
            m.Data.TryCategory("glaciation", lat, lon, m.Seed, out string glac, out _);
            if (glac != null && s.Soil is "Fluvisols") Add("glaciated Fluvisols: recipe", s.Cover, w);
            if (s.Era != null) { var age = ol.AgeOfEra(s.Era); if (s.Cover is "peat" or "peat-on-till") Add("peat land: era age", age.ToString(), w); }
        }
    }
    foreach (var (table, d) in W.OrderBy(x => x.Key))
    {
        double total = d.Values.Sum();
        P($"\n-- {table}: {100 * total / land:0.00} % of land; within it:");
        foreach (var (key, v) in d.OrderByDescending(x => x.Value).Take(15)) P($"   {100 * v / total,6:0.0} %  {key}");
    }
}

File.WriteAllText(Path.Combine(outDir, "probe-" + mode + ".txt"), sb.ToString());
