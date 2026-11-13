using EarthMap.Core;
using System.Text.Json;
using static EarthMap.Core.PlacementCatalogue;

// Earth's placement catalogue (decisions 0049, 0047): plants where they grew around 1318.
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
string json = File.ReadAllText(Path.Combine(root, "assets/earth/config/placement.json"));
var geography = new BotanicalRegions(File.ReadAllText(Path.Combine(root, "assets/earth/config/treeregions.json")));
var cat = new PlacementCatalogue(json, geography);
int passed = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL: " + msg); passed++; }
void Throws(Action a, string msg) { try { a(); } catch (ArgumentException) { passed++; return; } throw new Exception("FAIL (no exception): " + msg); }

Taxon T(string id) => cat.Find(id) ?? throw new Exception("missing taxon " + id);
bool InRange(string id, double lat, double lon) => cat.InRange(T(id), cat.RegionAt(lat, lon));

// ---------------------------------------------------------------- file
Check(cat.Profile == "1318", "Profile is 1318");
Check(cat.Taxa.Count >= 40 && cat.Mappings.Count >= cat.Taxa.Count, "Taxa and mappings loaded");
Throws(() => new PlacementCatalogue(json.Replace("\"version\": 1", "\"version\": 2"), geography), "Unsupported version rejected");
Throws(() => new PlacementCatalogue(json.Replace("\"taxon\": \"olea-europaea\"", "\"taxon\": \"no-such-taxon\""), geography), "Mapping to an unknown taxon rejected");
foreach (var t in cat.Taxa)
{
    Check(t.Regions.Length > 0 && t.Climate.MinTemp < t.Climate.MaxTemp && t.Climate.MinRain < t.Climate.MaxRain, t.Id + " has a range and an envelope");
    Check(t.Introductions.All(i => i.Source != null && i.Period != null), t.Id + " introductions carry a period and a source");
    Check(cat.Mappings.Any(m => m.Taxon == t.Id), t.Id + " is mapped");
}

// ---------------------------------------------------------------- mappings
foreach (string type in new[] { "olive", "pomegranate", "pinkapple", "redapple", "yellowapple", "cherry", "peach", "pear", "orange", "mango", "lychee", "breadfruit" })
    Check(cat.FruitTree(type) != null, "Vanilla fruit tree " + type + " is mapped");
Check(cat.FruitTree("olive").Id == "olea-europaea" && cat.FruitTree("OLIVE") != null, "Fruit types map case-insensitively");
Check(cat.FruitTree("dragonfruit") == null, "Unknown fruit types keep vanilla's climate test");
Check(cat.ForBlock("game:crop-spelt-4")?.Id == "triticum-spelta" && cat.ForBlock("crop-spelt-4")?.Id == "triticum-spelta", "Block codes with or without the game domain");
Check(cat.ForBlock("game:fruitingbush-wild-whitecurrant-free")?.Id == "ribes-rubrum", "White currant is red currant");
Check(cat.ForBlock("othermod:crop-spelt-4") == null && cat.ForBlock("game:crops-spelt") == null, "Prefix and domain must match");
Check(cat.Classify(new[] { "game:crop-rye-4", "game:crop-rye-6" }, out var rye) == PatchKind.Mapped && rye.Length == 1, "Vanilla rye patch is mapped");
Check(cat.Classify(new[] { "game:crop-rye-4", "game:mushroom-fieldmushroom-normal" }, out _) == PatchKind.Partial, "Mixed patch is partial");
Check(cat.Classify(new[] { "game:loosestones-granite-free" }, out _) == PatchKind.NotOrganism, "Loose stones are not organisms");
Check(cat.Classify(new[] { "game:saguarocactus-segment" }, out var saguaro) == PatchKind.Mapped && saguaro[0].Id == "carnegiea-gigantea"
    && cat.ForBlock("game:barrelcactus-normal")?.Id == "ferocactus-barrel" && cat.ForBlock("game:silvertorchcactus")?.Id == "cleistocactus-strausii",
    "Vanilla cacti are catalogue taxa (ranges step 3; the Americas box is gone)");
// Playtest #21 (Hoggar, 23.3 N 5.8 E): no American cacti in the Sahara. Native ranges and Earth climate (mean, coldest month, rain).
foreach (string id in new[] { "carnegiea-gigantea", "ferocactus-barrel", "cleistocactus-strausii" })
    Check(!InRange(id, 23.3, 5.8) && !InRange(id, 30.0, 31.2) && !InRange(id, -25, 135) && !InRange(id, 19.6, -155.5), id + " not in the Sahara, Egypt, Australia or Hawaii");
Check(InRange("carnegiea-gigantea", 32.2, -110.9) && !InRange("carnegiea-gigantea", -21.5, -64.7) && InRange("ferocactus-barrel", 31.8, -106.4)
    && InRange("cleistocactus-strausii", -21.5, -64.7) && !InRange("cleistocactus-strausii", 32.2, -110.9), "Saguaro at Tucson, barrel cactus at El Paso, silver torch at Tarija");
// Their climate limits are checked on Earth's own climate at named sites in tests/PlaytestIssuesTests.
Check(cat.Classify(new[] { "game:mushroom-fieldmushroom-normal" }, out _) == PatchKind.Unrelated, "Mushrooms are not in this slice");
// Ranges step 6: vanilla's wild flowers are catalogue taxa; the dry-land annuals bloom after rain (step 5c)
Check(cat.Classify(new[] { "game:flower-lupine-blue-free", "game:flower-lupine-purple-free" }, out var lupine) == PatchKind.Mapped && lupine[0].Id == "lupinus", "Vanilla lupines are mapped");
Check(cat.ForBlock("game:flower-goldenpoppy-free")?.Ephemeral == true && cat.ForBlock("game:flower-cornflower-free")?.Ephemeral == true
    && cat.ForBlock("game:flower-heather-free")?.Ephemeral == false, "California poppy and cornflower are ephemeral, heather is not");
Check(InRange("eschscholzia-californica", 34.5, -118.0) && !InRange("eschscholzia-californica", 37.0, -4.0), "California poppy in California, not Spain in 1318");
Check(InRange("centaurea-cyanus", 52.0, -1.0) && InRange("isatis-tinctoria", 52.0, -1.0), "Cornflower and woad in England by 1318");
Check(InRange("hyacinthoides-non-scripta", 52.0, -1.0) && !InRange("hyacinthoides-non-scripta", 55.0, 37.0), "Bluebell in England, not Russia");
Check(InRange("monotropa-uniflora", 40.0, -77.0) && !InRange("monotropa-uniflora", 50.0, 10.0), "Ghost pipe in North America, not Germany");
var gate = cat.CreateGate(new[] { "game:crop-spelt-4" }, false, out var kind);
Check(kind == PatchKind.Mapped && gate.Placement && gate.Names.Count == 1 && gate.Names[0] == T("triticum-spelta").Name, "Gate carries its taxon name");
Check(gate.Allows(cat.RegionAt(48.86, 2.35)) && !gate.Allows(cat.RegionAt(40.71, -74.0)), "Spelt gate: Paris yes, New York no");

// ---------------------------------------------------------------- 1318 ranges
var americas = new (string name, double lat, double lon)[]
{
    ("Los Angeles", 34.05, -118.24), ("Mexico City", 19.43, -99.13), ("New York", 40.71, -74.0), ("Lima", -12.05, -77.04),
    ("Santiago", -33.45, -70.67), ("Buenos Aires", -34.6, -58.38), ("Havana", 23.11, -82.37), ("São Paulo", -23.55, -46.63),
};
var oldWorld = new (string name, double lat, double lon)[]
{
    ("Paris", 48.86, 2.35), ("Rome", 41.9, 12.5), ("Athens", 37.98, 23.73), ("Cairo", 30.04, 31.24), ("Baghdad", 33.32, 44.37),
    ("Delhi", 28.61, 77.21), ("Beijing", 39.9, 116.4), ("Moscow", 55.76, 37.62), ("Kyiv", 50.45, 30.52), ("Seville", 37.39, -5.98),
};
foreach (var (name, lat, lon) in americas)
{
    Check(cat.RegionAt(lat, lon) >= 0, name + " lies in a botanical country");
    foreach (string id in new[] { "olea-europaea", "punica-granatum", "triticum-spelta", "secale-cereale", "malus-domestica", "citrus-sinensis", "oryza-sativa", "glycine-max", "mangifera-indica" })
        Check(!InRange(id, lat, lon), id + " not in the Americas of 1318: " + name);
}
foreach (var (name, lat, lon) in oldWorld)
{
    Check(cat.RegionAt(lat, lon) >= 0, name + " lies in a botanical country");
    foreach (string id in new[] { "zea-mays", "helianthus-annuus", "arachis-hypogaea", "ananas-comosus", "manihot-esculenta", "ipomoea-batatas", "phaseolus-vulgaris", "amaranthus-grain", "agave-tequilana" })
        Check(!InRange(id, lat, lon), id + " not in the Old World of 1318: " + name);
}
foreach (var (name, lat, lon) in new[] { ("Athens", 37.98, 23.73), ("Seville", 37.39, -5.98), ("Tunis", 36.81, 10.18), ("Jerusalem", 31.77, 35.21), ("Heraklion", 35.34, 25.13), ("Rome", 41.9, 12.5) })
    Check(InRange("olea-europaea", lat, lon), "Olive grown around 1318 at " + name);
Check(!InRange("olea-europaea", 52.52, 13.4) && !InRange("olea-europaea", 51.51, -0.13), "No olives in Berlin or London");
Check(InRange("zea-mays", 19.43, -99.13) && InRange("phaseolus-vulgaris", 19.43, -99.13), "Maize and beans in Mexico");
Check(InRange("citrus-sinensis", 23.13, 113.26) && !InRange("citrus-sinensis", 37.39, -5.98), "Sweet orange in southern China, not yet in Spain (c. 1500)");
Check(!InRange("citrus-sinensis", 45.75, 126.65), "No sweet orange in Manchuria");
Check(InRange("oryza-sativa", 23.13, 113.26) && InRange("oryza-sativa", 28.61, 77.21), "Rice in China and India");
Check(InRange("secale-cereale", 55.76, 37.62) && InRange("triticum-spelta", 48.86, 2.35), "Rye in Russia, spelt in France");

// ---------------------------------------------------------------- climate envelopes
var olive = T("olea-europaea");
Check(ClimateAllows(olive, 17.1, 10, 64), "Athens climate (17.1 °C, coldest month 10 °C, rain 64) admits the olive");
Check(!ClimateAllows(olive, 9, 1, 120), "Cold Balkan uplands (9 °C, coldest month 1 °C) do not");
Check(!ClimateAllows(olive, 17, 3, 64), "Coldest month below 5 °C excludes the olive");
Check(cat.Allows(olive, cat.RegionAt(37.98, 23.73), 17.1, 10, 64) && !cat.Allows(olive, cat.RegionAt(34.05, -118.24), 17.1, 10, 64),
    "Same climate: olive in Athens, not in Los Angeles");
Check(!ClimateAllows(T("mangifera-indica"), 8, 0, 150), "No mango where the coldest month freezes");

// Every taxon finds climate somewhere in its own range (region medians of the bake's climate histograms).
using var climateDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/tree-region-climate.json")));
var climate = climateDoc.RootElement;
double tMin = climate.GetProperty("tempMin").GetDouble(), tStep = climate.GetProperty("tempStep").GetDouble(), rStep = climate.GetProperty("rainStep").GetDouble();
double Median(JsonElement hist, double origin, double step)
{
    var bins = hist.EnumerateArray().Select(b => (b[0].GetDouble(), b[1].GetDouble())).OrderBy(b => b.Item1).ToArray();
    double total = bins.Sum(b => b.Item2), acc = 0;
    foreach (var (bin, w) in bins) { acc += w; if (acc >= total / 2) return origin + (bin + .5) * step; }
    return double.NaN;
}
foreach (var t in cat.Taxa)
{
    bool any = false;
    foreach (string code in t.Regions)
    {
        if (!climate.GetProperty("regions").TryGetProperty(code, out var r)) continue;
        double temp = Median(r.GetProperty("t"), tMin, tStep), rain = Median(r.GetProperty("r"), 0, rStep);
        if (ClimateAllows(t, temp, 30, rain)) { any = true; break; }
        // A habitat specialist (the Sonoran saguaro) fits part of a region, not its median: any of the region's
        // temperature bins and any of its rain bins inside the envelope.
        bool tIn = r.GetProperty("t").EnumerateArray().Any(b => { double v = tMin + (b[0].GetInt32() + .5) * tStep; return v >= t.Climate.MinTemp && v <= t.Climate.MaxTemp; });
        bool rIn = r.GetProperty("r").EnumerateArray().Any(b => { double v = (b[0].GetInt32() + .5) * rStep; return v >= t.Climate.MinRain && v <= t.Climate.MaxRain; });
        if (tIn && rIn) { any = true; break; }
    }
    Check(any, t.Id + " has climate inside its envelope in at least one of its regions");
}

Console.WriteLine($"Placement catalogue: {passed} checks passed.");
