using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarthMap.Core;

// Offline probe of decision 0060 (ores in their ground layer): sample ground columns at named sites, every deposit's vanilla
// depth draws and hosts through the layer rules, deposit blocks counted by layer class before (attempted) and after (kept).
// Usage: dotnet run -- [repo root]   (VS_ASSETS: the game's assets folder, default %APPDATA%\Vintagestory\assets)
string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string vsAssets = Environment.GetEnvironmentVariable("VS_ASSETS") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory/assets");
string groundRules = File.ReadAllText(Path.Combine(root, "assets/earth/config/ground-rules-v1.json"));
string layerRules = File.ReadAllText(Path.Combine(root, "assets/earth/config/ore-layers-v1.json"));
byte[] ReadAsset(string loc) { var p = Path.Combine(root, "assets", loc.Split(':')[0], loc.Split(':')[1]); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } else Console.WriteLine("ok   " + what); }
string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

// ---------------------------------------------------------------- deposits (vanilla survival + Earth's)
var deposits = new List<Deposit>();
string depositDir = Path.Combine(vsAssets, "survival/worldgen/deposits");
if (!Directory.Exists(depositDir)) { Console.WriteLine("Vintage Story assets not found at " + vsAssets + " (set VS_ASSETS)"); return 2; }
foreach (var file in Directory.GetFiles(depositDir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
    LoadDeposits(File.ReadAllText(file), "game", Path.GetRelativePath(vsAssets, file));
foreach (var file in Directory.GetFiles(Path.Combine(root, "assets/earth/worldgen/deposits"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
    LoadDeposits(File.ReadAllText(file), "earth", Path.GetRelativePath(root, file));
Console.WriteLine($"{deposits.Count} deposit variants ({deposits.Count(d => d.Parent != null)} children) from {depositDir} and Earth's assets");

void LoadDeposits(string text, string domain, string from)
{
    using var doc = JsonDocument.Parse(Json5(text), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    foreach (var v in doc.RootElement.EnumerateArray()) Add(v, null);
    void Add(JsonElement v, Deposit parent)
    {
        var d = new Deposit
        {
            Code = Str(v, "code"), Generator = Str(v, "generator") ?? "", Tries = Num(v, "triesPerChunk", 0), Parent = parent, From = from,
            Climate = v.TryGetProperty("climate", out _),
        };
        if (v.TryGetProperty("attributes", out var a))
        {
            if (a.TryGetProperty("inblock", out var ib)) (d.Host, d.HostVariants) = Pattern(ib, domain);
            if (a.TryGetProperty("placeblock", out var pb)) d.Place = Str(pb, "code");
            d.Depth = Nat(a, "depth"); d.YPosRel = Nat(a, "yPosRel"); d.Thickness = Nat(a, "thickness") ?? new NatF("uniform", 2, 0);
            d.Roughness = a.TryGetProperty("maxYRoughness", out _);
        }
        deposits.Add(d);
        if (v.TryGetProperty("childDeposits", out var ch)) foreach (var c in ch.EnumerateArray()) Add(c, d);
    }
}
static (Regex, HashSet<string>) Pattern(JsonElement ib, string domain)
{
    string code = Str(ib, "code");
    if (code == null) return (null, null);
    if (!code.Contains(':')) code = domain + ":" + code;
    var rx = new Regex("^" + Regex.Escape(code).Replace("\\*", "(.*)") + "$");
    HashSet<string> allowed = ib.TryGetProperty("allowedVariants", out var av) ? av.EnumerateArray().Select(e => e.GetString()).ToHashSet() : null;
    return (rx, allowed);
}
static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
static double Num(JsonElement e, string name, double def) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
static NatF Nat(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
    ? new NatF(Str(v, "dist") ?? "uniform", Num(v, "avg", 0), Num(v, "var", 0)) : null;

// vanilla's asset JSON: unquoted keys and comments
static string Json5(string s)
{
    var sb = new StringBuilder(s.Length + 256);
    bool str = false; char quote = '"';
    for (int i = 0; i < s.Length; i++)
    {
        char c = s[i];
        if (str) { sb.Append(c); if (c == '\\' && i + 1 < s.Length) sb.Append(s[++i]); else if (c == quote) str = false; continue; }
        if (c == '"' || c == '\'') { str = true; quote = c; sb.Append('"'); if (c == '\'') { int j = s.IndexOf('\'', i + 1); sb.Append(s, i + 1, j - i - 1).Append('"'); i = j; str = false; } continue; }
        if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; sb.Append('\n'); continue; }
        if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { i = s.IndexOf("*/", i + 2, StringComparison.Ordinal) + 1; continue; }
        if (char.IsLetter(c) || c == '_')
        {
            int j = i; while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
            int k = j; while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
            string word = s.Substring(i, j - i);
            if (k < s.Length && s[k] == ':') sb.Append('"').Append(word).Append('"'); else sb.Append(word);
            i = j - 1; continue;
        }
        sb.Append(c);
    }
    return sb.ToString();
}

// ---------------------------------------------------------------- worlds
var worlds = new (string Name, VerticalScale Scale, int MapHeight)[]
{
    ("1:10, 1024 high, linear", new VerticalScale(1024, 110, 1022, 1000, 47, 20, true), 1024),
    ("384 high, log1000", new VerticalScale(384, 110, 382, 1000, 47, 20), 384),
};
var sites = new (string Name, double Lat, double Lon, double Elevation, bool River, string Expect)[]
{
    ("Ruhr", 51.45, 7.2, 80, false, "coal"),
    ("Pennsylvania anthracite", 40.8, -76.2, 300, false, "coal"),
    ("Lusatia lignite", 51.55, 14.2, 120, false, "lignite"),
    ("Zechstein salt, Stassfurt", 51.85, 11.58, 100, false, "salt"),
    ("Death Valley", 36.25, -116.85, -80, false, "playa"),
    ("Salar de Uyuni", -20.2, -67.5, 3650, false, "playa"),
    ("Cornwall", 50.3, -5.1, 100, true, "placer"),
    ("Klondike", 63.9, -139.3, 400, true, "placer"),
    ("Kalgoorlie", -30.75, 121.47, 370, false, ""),
    ("Latvia", 56.95, 24.1, 50, false, "peat"),
    ("Boke", 10.95, -14.3, 50, false, "laterite"),
    ("Jamaica", 18.15, -77.3, 300, false, "laterite"),
    ("Canadian Shield", 58.0, -95.0, 250, false, ""),
    ("Amazon", -3.5, -64.0, 60, false, ""),
    ("Aquitaine", 44.87, -0.95, 50, false, ""),
    ("Georgia Piedmont", 33.9, -83.4, 250, false, "saprolite"),
    ("Minas Gerais", -19.9, -43.9, 900, false, "saprolite"),
};
// the classes a site's own layer should have somewhere within reach; looked for in a small box when the point lacks it
var expectClass = new Dictionary<string, OreLayers.Class[]>
{
    ["playa"] = new[] { OreLayers.Class.Playa }, ["placer"] = new[] { OreLayers.Class.Alluvial, OreLayers.Class.Outwash },
    ["peat"] = new[] { OreLayers.Class.Peat }, ["laterite"] = new[] { OreLayers.Class.Laterite }, ["saprolite"] = new[] { OreLayers.Class.Saprolite },
};

// kept blocks by rule name and code, for the checks over all sites
var keptAll = new Dictionary<string, Dictionary<int, long>>();
var tally = new Dictionary<string, (long Attempted, long Kept)>();
var siteKept = new Dictionary<(string World, string Site, string Rule), long>();
var rand = new Random(60);

foreach (var (wname, scale, mapHeight) in worlds)
{
    double mpb = 1 / scale.Slope(0);
    var cv = GroundColumns.DepthCurve.For(GroundColumns.DepthCurve.Default, mpb, 110);
    var model = GroundColumns.Load(groundRules, ReadAsset, cv, 1234);
    var rules = new OreLayers(layerRules, model);
    var col = new GroundColumns.Column();
    Console.WriteLine($"\n=========== {wname} ({F(mpb)} m per block at sea level; ore layers {rules.Fingerprint})");

    // bookkeeping: the segments are the column's blocks
    int segBad = 0, built = 0;
    for (double la = -50; la <= 70; la += 3.7) for (double lo = -170; lo <= 170; lo += 7.3)
    {
        var sm = model.At(la, lo, new GroundColumns.Place { Elevation = 100, Surface = GroundColumns.Surface.Land, RegionalRock = (int)(la + lo) % 2 == 0 });
        if (sm.Cover == null) continue;
        model.Build(sm, GroundColumns.Surface.Land, col); built++;
        int depth = Math.Min(col.Depth, 4000);
        // a column with no ground layers (regional rock from the top) has no segments either
        string why = col.Count == 0 ? (col.Segments == 0 ? null : "segments without layers") : col.Segments == 0 ? "no segments" : col.SegEnd[col.Segments - 1] != col.End[col.Count - 1] ? "ends differ" : null;
        for (int d = 0; why == null && d < depth; d++)
            if (col.SegMaterial[col.SegmentAt(d)] != col.MaterialAt(d)) why = $"block {d}: segment {model.Materials[col.SegMaterial[col.SegmentAt(d)]]}, block {model.Materials[col.MaterialAt(d)]}";
        if (why == null) continue;
        if (segBad++ == 0) Console.WriteLine($"  segments differ at ({F(la)}, {F(lo)}): {why}; {col.Count} layers, {col.Segments} segments; {sm.Cover}; {model.Describe(col)}");
    }
    Check(built > 100 && segBad == 0, $"{wname}: segments rebuild the blocks of {built} columns ({segBad} differ)");

    var blockOf = model.Candidates.Select(c => c.Length > 0 ? c[0] : null).ToArray();
    foreach (var site in sites)
    {
        var place = new GroundColumns.Place { Elevation = site.Elevation, Surface = site.River ? GroundColumns.Surface.RiverBed : GroundColumns.Surface.Land, NearRiver = site.River, RegionalRock = false };
        var sm = model.At(site.Lat, site.Lon, place);
        model.Build(sm, place.Surface, col);
        double lat = site.Lat, lon = site.Lon;
        if (expectClass.TryGetValue(site.Expect, out var want) && !HasClass(col, rules, want))
        {
            // the nearest column within about 50 km that has the site's layer
            bool found = false;
            for (int r = 1; r <= 25 && !found; r++)
                for (int i = -r; i <= r && !found; i++) for (int j = -r; j <= r && !found; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != r) continue;
                    double la2 = site.Lat + i * .02, lo2 = site.Lon + j * .02;
                    var s2 = model.At(la2, lo2, place);
                    if (s2.Cover == null) continue;
                    model.Build(s2, place.Surface, col);
                    if (HasClass(col, rules, want)) { sm = s2; lat = la2; lon = lo2; found = true; }
                }
            if (!found) model.Build(sm, place.Surface, col);
        }
        int top = (int)Math.Round(scale.Y(site.Elevation));
        Console.WriteLine($"\n{site.Name} ({F(lat)}, {F(lon)}), top y={top}: {sm.Cover}/{sm.Province}/{sm.Era}; {rules.DescribeColumn(col, sm.Era)}");
        var lines = new List<string>();
        foreach (var dep in deposits)
        {
            if (dep.Host == null || dep.Code == null) continue;
            var root0 = dep; while (root0.Parent != null) root0 = root0.Parent;
            var rule = RuleOf(dep, rules);
            long attempted = 0, kept = 0;
            var keptBy = new SortedDictionary<string, long>(); var lostBy = new SortedDictionary<string, long>();
            var anchor = root0;
            for (int s = 0; s < 400; s++)
            {
                double y0 = anchor.Generator switch
                {
                    "disc-followsurfacebelow" => top - (anchor.Depth?.Next(rand) ?? 0),
                    "disc-followsurface" => (anchor.YPosRel?.Next(rand) ?? 0) * top,
                    "disc-followsealevel" => (anchor.YPosRel?.Next(rand) ?? 0) * 110,
                    "disc-anywhere" => (anchor.YPosRel?.Next(rand) ?? 0) * mapHeight,
                    _ => double.NaN,
                };
                if (double.IsNaN(y0)) break;
                int thick = Math.Max(1, (int)anchor.Thickness.Next(rand));
                for (int k = 0; k < thick; k++)
                {
                    int y = (int)y0 + k;
                    if (y <= 1 || y > top) continue;
                    int d = top - y;
                    int m = col.MaterialAt(d);
                    string block = m >= 0 && m < blockOf.Length ? blockOf[m] : null;
                    // a child sits in its parent's ore: the parent's host here, and the parent's rule must keep it too
                    if (block == null || !Hosts(root0, block)) continue;
                    int code = rules.CodeOf(col, col.SegmentAt(d));
                    string rock = block.StartsWith("game:rock-", StringComparison.Ordinal) ? block.Substring(10) : null;
                    bool ok = OreLayers.Allows(RuleOf(root0, rules), code, d, rock) && OreLayers.Allows(rule, code, d, rock);
                    attempted++;
                    string cls = OreLayers.ClassNames[(int)OreLayers.ClassOf(code)];
                    if (ok)
                    {
                        kept++; keptBy[cls] = keptBy.GetValueOrDefault(cls) + 1;
                        string rn = rule?.Name ?? "(no rule)";
                        if (!keptAll.TryGetValue(rn, out var byCode)) keptAll[rn] = byCode = new();
                        byCode[code] = byCode.GetValueOrDefault(code) + 1;
                        siteKept[(wname, site.Name, rn)] = siteKept.GetValueOrDefault((wname, site.Name, rn)) + 1;
                    }
                    else lostBy[cls] = lostBy.GetValueOrDefault(cls) + 1;
                }
            }
            if (attempted == 0) continue;
            string key = (rule?.Name ?? "(no rule)") + " | " + dep.Code;
            var t = tally.GetValueOrDefault(key); tally[key] = (t.Attempted + attempted, t.Kept + kept);
            lines.Add($"  {dep.Code}{(dep.Parent != null ? " (child of " + dep.Parent.Code + ")" : "")} [{rule?.Name ?? "no rule"}]: {kept * 100 / attempted}% of {attempted} kept"
                + (keptBy.Count > 0 ? "; kept in " + string.Join(", ", keptBy.Select(p => p.Key + " " + p.Value)) : "")
                + (lostBy.Count > 0 ? "; lost in " + string.Join(", ", lostBy.Select(p => p.Key + " " + p.Value)) : ""));
        }
        foreach (var l in lines.Distinct()) Console.WriteLine(l);
    }
}

// ---------------------------------------------------------------- kept blocks follow 0060 section 2, restated
Console.WriteLine("\nKept share by rule and deposit over all sites and worlds:");
foreach (var p in tally.OrderBy(p => p.Key, StringComparer.Ordinal))
    Console.WriteLine($"  {p.Key}: {p.Value.Kept * 100 / Math.Max(1, p.Value.Attempted)}% of {p.Value.Attempted}");
Console.WriteLine();
bool All(string rule, Func<int, bool> ok) => !keptAll.TryGetValue(rule, out var byCode) || byCode.Keys.All(ok);
OreLayers.Class C(int code) => OreLayers.ClassOf(code);
Check(All("hard-rock ores and gems", c => C(c) is OreLayers.Class.Bed or OreLayers.Class.Basement), "no hard-rock ore or gem in cover, deep cover, peat, playa or other loose layers");
Check(All("rock-hosted (default)", c => C(c) is OreLayers.Class.Bed or OreLayers.Class.Basement), "rock-hosted deposits without a rule stay in rock");
Check(All("lignite", c => C(c) == OreLayers.Class.Bed && OreLayers.AgeOf(c) is OreLayers.Age.Cenozoic or OreLayers.Age.Mesozoic or OreLayers.Age.Unknown), "no lignite older than the Mesozoic");
Check(All("bituminous coal", c => C(c) == OreLayers.Class.Bed && OreLayers.AgeOf(c) != OreLayers.Age.Precambrian)
   && All("anthracite", c => C(c) == OreLayers.Class.Bed && OreLayers.AgeOf(c) != OreLayers.Age.Precambrian), "no coal in basement or Precambrian beds");
Check(All("salt", c => C(c) == OreLayers.Class.Bed && OreLayers.IsEvaporite(c)) && All("borates", c => OreLayers.IsEvaporite(c))
   && All("salt flats", c => C(c) == OreLayers.Class.Playa), "salt only in evaporite beds and playas");
Check(All("placers", c => C(c) is OreLayers.Class.Alluvial or OreLayers.Class.Outwash), "placers only in alluvial or outwash layers");
Check(All("bog iron", c => C(c) == OreLayers.Class.Peat), "bog iron only in peat");
Check(All("bauxite", c => C(c) == OreLayers.Class.Laterite), "bauxite only in laterite");
Check(All("fire clay", c => C(c) is OreLayers.Class.Saprolite or OreLayers.Class.Laterite), "fire clay only in saprolite or laterite");

// ---------------------------------------------------------------- the layer deposits find their layers where they belong
long At(string site, string rule) => worlds.Sum(w => siteKept.GetValueOrDefault((w.Name, site, rule)));
Check(At("Death Valley", "salt flats") + At("Salar de Uyuni", "salt flats") > 0, "salt flats in a playa (Death Valley or Uyuni)");
Check(At("Klondike", "placers") + At("Cornwall", "placers") > 0, "placers in river gravel or sand (Klondike or Cornwall)");
Check(At("Boke", "bauxite") + At("Jamaica", "bauxite") > 0, "bauxite in laterite (Boke or Jamaica)");
Check(At("Latvia", "bog iron") > 0, "bog iron in Latvian peat");
// GenDeposits leaves a deposit's centre at y=-99 unless it checks climate, and the disc generator skips every block further
// than maxYRoughness from that centre: without a climate check the deposit would never place a block.
Check(!deposits.Any(d => d.From.Contains("earth") && d.Roughness && !d.Climate), "Earth's deposits set maxYRoughness only with a climate check");
long hard = tally.Where(p => p.Key.StartsWith("hard-rock", StringComparison.Ordinal)).Sum(p => p.Value.Attempted), hardKept = tally.Where(p => p.Key.StartsWith("hard-rock", StringComparison.Ordinal)).Sum(p => p.Value.Kept);
Console.WriteLine($"hard-rock ores and gems: {hardKept * 100 / Math.Max(1, hard)}% of {hard} host blocks kept");
Check(hard > 0 && hardKept * 2 > hard, "most hard-rock host blocks the draws reach are in rock and kept");

// ore layers v2 (EA-0059): the same rules, and fire clay also as the underclay of a kept coal block (vanilla's BlockBituCoal
// lays rawclay-fire under coal in sedimentary beds); v1 keeps its rules unchanged
{
    var cv = GroundColumns.DepthCurve.For(GroundColumns.DepthCurve.Default, 30, 110);
    var model = GroundColumns.Load(groundRules, ReadAsset, cv, 1234);
    var v1 = new OreLayers(layerRules, model);
    var v2 = new OreLayers(File.ReadAllText(Path.Combine(root, OreLayers.RulesAsset(2).Replace("earth:", "assets/earth/"))), model);
    Check(v1.Version == 1 && v2.Version == 2 && OreLayers.RulesVersion == 2 && OreLayers.Supported(1) == 1 && OreLayers.Supported(2) == 2,
        "ore layers: v1 and v2 load with their versions; new worlds get v2");
    Check(v1.Rules.Select(r => r.Name).SequenceEqual(v2.Rules.Select(r => r.Name)) && v1.Rules.All(r => r.Under == 0),
        "ore layers v2: the same rules as v1; no rule in v1 allows an underclay");
    var clay = v2.RuleFor("earth-kaolin", false);
    var coal = v2.Rules.First(r => r.Name == "bituminous coal");
    var anthracite = v2.Rules.First(r => r.Name == "anthracite");
    var salt = v2.Rules.First(r => r.Name == "salt");
    Check(clay?.Name == "fire clay" && OreLayers.AllowsUnder(clay, 1u << coal.Index) && OreLayers.AllowsUnder(clay, 1u << anthracite.Index)
        && !OreLayers.AllowsUnder(clay, 1u << salt.Index) && !OreLayers.AllowsUnder(clay, 0) && !OreLayers.AllowsUnder(v1.RuleFor("earth-kaolin", false), 1u << coal.Index),
        "ore layers v2: fire clay is kept under bituminous coal or anthracite, not under other rules or nothing; v1 never");
    bool threw = false;
    try { new OreLayers(layerRules.Replace("\"codes\": [\"earth-kaolin\"]", "\"under\": [\"bituminous coal\"], \"codes\": [\"earth-kaolin\"]"), model); }
    catch (Exception) { threw = true; }
    Check(threw, "ore layers: a v1 file may not name an underclay rule");
}

Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;

static bool Hosts(Deposit d, string block)
{
    var m = d.Host?.Match(block);
    if (m == null || !m.Success) return false;
    return d.HostVariants == null || m.Groups.Count < 2 || d.HostVariants.Contains(m.Groups[1].Value);
}
static OreLayers.Rule RuleOf(Deposit d, OreLayers rules)
{
    bool rockHosted = (d.Parent == null ? d : Root(d)).Host?.ToString().Contains("rock-") == true;
    for (var cur = d; cur != null; cur = cur.Parent) if (rules.Names(cur.Code)) return rules.RuleFor(cur.Code, rockHosted);
    return rules.RuleFor(d.Code, rockHosted);
}
static Deposit Root(Deposit d) { while (d.Parent != null) d = d.Parent; return d; }
static bool HasClass(GroundColumns.Column col, OreLayers rules, OreLayers.Class[] want)
{
    for (int i = 0; i < col.Segments; i++) if (want.Contains(OreLayers.ClassOf(rules.CodeOf(col, i)))) return true;
    return false;
}

sealed class Deposit
{
    public string Code, Generator, From, Place;
    public double Tries;
    public bool Climate, Roughness;
    public Deposit Parent;
    public Regex Host; public HashSet<string> HostVariants;
    public NatF Depth, YPosRel, Thickness;
}
sealed record NatF(string Dist, double Avg, double Var)
{
    // vanilla's NatFloat distributions (the ones deposits use)
    public double Next(Random r)
    {
        double U() => r.NextDouble();
        return Dist.ToLowerInvariant() switch
        {
            "gaussian" => Avg + ((U() + U() + U()) / 3 - .5) * 2 * Var,
            "narrowgaussian" => Avg + ((U() + U() + U() + U() + U() + U()) / 6 - .5) * 2 * Var,
            "triangle" => Avg + ((U() + U()) / 2 - .5) * 2 * Var,
            "invexp" => Avg + U() * U() * Var,
            "stronginvexp" => Avg + U() * U() * U() * Var,
            "strongerinvexp" => Avg + U() * U() * U() * U() * Var,
            _ => Avg + (U() - .5) * 2 * Var,
        };
    }
}
