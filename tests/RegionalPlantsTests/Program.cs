using EarthMap.Core;
using System.Diagnostics;
using System.Text.Json;
using static EarthMap.Core.RegionalPlantCatalog;

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
string rulesJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/regionalplants.json"));
string regionsJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/treeregions.json"));
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var file = JsonSerializer.Deserialize<RuleFile>(rulesJson, options);
string[] providers = file.Providers.Select(p => p.Id).ToArray();
var geography = new BotanicalRegions(regionsJson);
var all = new RegionalPlantCatalog(rulesJson, geography, providers);
var none = new RegionalPlantCatalog(rulesJson, geography, Array.Empty<string>());
int passed = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL: " + msg); passed++; }
void Throws(Action a, string msg) { try { a(); } catch (ArgumentException) { passed++; return; } throw new Exception("FAIL (no exception): " + msg); }

const string Cape = "floralzonescaperegion:", Med = "floralzonesmediterraneanregion:", Nz = "floralzonesneozeylandicregion:",
    Boreal = "floralzonescircumborealregion:", Cosmo = "floralzonescosmopolitanregion:";
bool Allowed(string code, double lat, double lon) => all.Allows(code, lat, lon);
PlantGate Gate(string[] codes, out GateKind kind, bool water = false) => all.CreateGate(codes, water, out kind);

// ---------------------------------------------------------------- catalogue build and parse
Check(file.Providers.Length == 8, "Eight reviewed Floral Zones packs");
Check(all.Providers.Count == 8 && none.Providers.Count == 0, "Installed-provider filter");
Check(none.Rules.Count == 0 && none.Find(Cape + "flower-amaryllisbelladonna-free") == null, "Absent packs cannot activate rules");
Check(none.CreateGate(new[] { Cape + "flower-amaryllisbelladonna-free" }, false, out var noneKind) == null && noneKind == GateKind.Vanilla, "Absent pack is vanilla");
Check(all.Rules.Count == file.Rules.Length, "All rules of installed packs loaded");
Check(geography.Count == 369, "WGSRPD level 3 has 369 botanical countries");
Throws(() => new RegionalPlantCatalog(rulesJson.Replace("\"version\": 1", "\"version\": 2"), geography, providers), "Unsupported version rejected");
Throws(() => new RegionalPlantCatalog(rulesJson.Replace("\"regions\":[\"CPP\"]", "\"regions\":[\"XXX\"]"), geography, providers), "Unknown region code rejected");
var firstCode = file.Rules[0].Codes[0];
Throws(() => new RegionalPlantCatalog(rulesJson.Replace("\"codes\":[\"" + file.Rules[1].Codes[0] + "\"", "\"codes\":[\"" + firstCode + "\""), geography, providers), "Conflicting code rejected");
Throws(() => new BotanicalRegions("{\"version\":2,\"regions\":[]}"), "Unsupported boundaries rejected");

// ---------------------------------------------------------------- inventory coverage
var inventory = all.Inventory;
int species = inventory.Count(e => e.Basis == "species"), packRegion = inventory.Count(e => e.Basis == "packRegion"), climate = inventory.Count(e => e.Basis == "climateOnly");
Check(inventory.Count == 275, "Inventory has the reviewed 275 pack block patches (" + inventory.Count + ")");
Check(species + packRegion + climate == inventory.Count, "Every patch has a basis");
Check(species == 246 && packRegion == 10 && climate == 19, $"Coverage changed: {species}/{packRegion}/{climate}");
Check(inventory.Count(e => e.Basis == "climateOnly" && e.File == "crop.json") == 14, "All 14 wild-crop patches stay climate-only");
foreach (var e in inventory)
{
    foreach (string code in e.Codes) Check(all.Find(code) != null, "Inventory code without a rule: " + code);
    var gate = Gate(e.Codes, out var kind);
    var expected = e.Basis == "climateOnly" ? GateKind.ClimateOnly : GateKind.Gated;
    Check(kind == expected, $"{e.Provider}/{e.File}#{e.Index} resolves to {kind}, expected {expected}");
    Check((gate != null) == (kind == GateKind.Gated), "Gate exists exactly for gated patches");
    Check(e.Rules.All(id => all.Rules.Any(r => r.Id == id)), "Inventory rule ids exist");
}
foreach (var r in all.Rules)
{
    Check(r.ClimateOnly || r.Regions.Length > 0, r.Id + " has regions");
    Check(r.Basis != "species" || (r.TaxonIds.Length > 0 && r.Taxa.Length > 0), r.Id + " species rule has a WCVP taxon");
}
Check(inventory.Where(e => e.Provider.EndsWith("cosmopolitanregion")).All(e => e.Basis != "packRegion"), "Cosmopolitan pack has no pack-region fallback");

// ---------------------------------------------------------------- native ranges
(double, double) capeTown = (-33.9, 18.4), santiago = (-33.45, -70.67), losAngeles = (34.05, -118.25), sydney = (-33.87, 151.2),
    perth = (-31.95, 115.86), athens = (37.98, 23.73), hobart = (-42.88, 147.33), wellington = (-41.29, 174.78),
    christchurch = (-43.53, 172.64), auckland = (-36.85, 174.76), helsinki = (60.17, 24.94), patagonia = (-50.3, -72.3);
string belladonna = Cape + "flower-amaryllisbelladonna-free";
Check(Allowed(belladonna, capeTown.Item1, capeTown.Item2), "Cape belladonna lily at Cape Town");
foreach (var p in new[] { santiago, losAngeles, sydney, perth })
    Check(!Allowed(belladonna, p.Item1, p.Item2), $"Cape belladonna lily blocked at {p}");
var capeRules = all.Rules.Where(r => r.Provider == "floralzonescaperegion").ToArray();
Check(capeRules.All(r => r.Regions.Contains("CPP")), "Every Cape pack plant is native in the Cape Provinces");
Check(capeRules.All(r => !all.Allows(r.Codes[0], santiago.Item1, santiago.Item2) && !all.Allows(r.Codes[0], losAngeles.Item1, losAngeles.Item2)
    && !all.Allows(r.Codes[0], perth.Item1, perth.Item2)), "No Cape pack plant in other Mediterranean-climate regions");

string broom = Med + "tallplant-spartiumjunceum-land-normal-free";
Check(Allowed(broom, athens.Item1, athens.Item2), "Spanish broom native in Greece");
Check(!Allowed(broom, losAngeles.Item1, losAngeles.Item2), "Spanish broom (invasive in California) not native there");
foreach (var r in all.Rules.Where(r => r.Provider == "floralzonesmediterraneanregion" && !r.ClimateOnly))
{
    if (r.Regions.Contains("GRC")) Check(all.Allows(r.Codes[0], athens.Item1, athens.Item2), r.Id + " allowed in Greece");
    Check(all.Allows(r.Codes[0], losAngeles.Item1, losAngeles.Item2) == r.Regions.Contains("CAL"), r.Id + " in California only if WCVP-native");
}
string flax = Nz + "tallplant-phormiumtenax-land-normal-free";
Check(Allowed(flax, wellington.Item1, wellington.Item2), "NZ flax in Wellington");
Check(!Allowed(flax, hobart.Item1, hobart.Item2), "NZ flax blocked in Tasmania");
string taramea = Nz + "flower-aciphyllaaurea-free";
Check(Allowed(taramea, christchurch.Item1, christchurch.Item2) && !Allowed(taramea, auckland.Item1, auckland.Item2), "South Island endemic stays on the South Island");
Check(all.Rules.Where(r => r.Provider == "floralzonesneozeylandicregion" && !r.ClimateOnly && !r.Regions.Contains("TAS"))
    .All(r => !all.Allows(r.Codes[0], hobart.Item1, hobart.Item2)), "No NZ endemic in Tasmania");
string sundew = Nz + "flower-droserabinata-free";
Check(Allowed(sundew, hobart.Item1, hobart.Item2), "Forked sundew is WCVP-native in Tasmania too (species range beats pack label)");
string dietes = Nz + "flower-dietesrobinsoniana-free";
Check(Allowed(dietes, -31.55, 159.08) && !Allowed(dietes, wellington.Item1, wellington.Item2), "Lord Howe endemic only on Lord Howe (NFK)");
string lichen = Boreal + "fern-cetrariaislandica";
Check(Allowed(lichen, helsinki.Item1, helsinki.Item2) && !Allowed(lichen, patagonia.Item1, patagonia.Item2), "Pack-region fallback (Circumboreal)");
Check(all.Find(lichen).Basis == "packRegion", "Non-vascular lichen uses the pack region");
string daylily = Boreal + "flower-hemerocallislilioasphodelus-free";
Check(!Allowed(daylily, 45, -75), "Lemon daylily (Eurasian) not native in North America despite the Circumboreal label");
string maize = Cosmo + "crop-zeamays-4";
Check(Allowed(maize, helsinki.Item1, helsinki.Item2) && Gate(new[] { maize }, out var maizeKind) == null && maizeKind == GateKind.ClimateOnly, "Wild crops stay climate-only");
Check(Gate(new[] { Cosmo + "flower-cladoniarangiferina-free" }, out var lichenKind) == null && lichenKind == GateKind.ClimateOnly, "Cosmopolitan lichen without range is climate-only");
string vanilla = Cosmo + "fruitingbush-wild-vanillaplanifolia-free";
Check(Allowed(vanilla, 17.0, -92.0) && !Allowed(vanilla, -18.9, 47.5), "Wild vanilla native in Mexico, introduced in Madagascar");

// ---------------------------------------------------------------- open water, aquatic and marine plants
string wrack = Boreal + "aquaticplant-ascophyllumnodosum";
var wrackGate = Gate(new[] { wrack }, out var wrackKind, water: true);
Check(wrackKind == GateKind.Gated && wrackGate.Water, "Seaweed gate uses the open-water lookup");
int maine = all.RegionAt(43.55, -69.55, true), alaska = all.RegionAt(59.3, -152.4, true), norway = all.RegionAt(60.6, 4.4, true);
Check(geography.Code(maine) == "MAI" && wrackGate.Allows(maine), "Egg wrack offshore Maine (nearest country)");
Check(wrackGate.Allows(norway), "Egg wrack off western Norway");
Check(alaska >= 0 && !wrackGate.Allows(alaska), "No North Atlantic seaweed in the Gulf of Alaska");
Check(all.RegionAt(0, -140, true) == -1, "Mid-Pacific has no nearest country within the search radius");
Check(all.RegionAt(43.55, -69.55, false) == -1 || geography.Code(all.RegionAt(43.55, -69.55, false)) == "MAI", "Land lookup does not search far offshore");

// ---------------------------------------------------------------- unknown and mixed patches keep vanilla rules
Check(Gate(new[] { "game:flower-cornflower-free" }, out var vk) == null && vk == GateKind.Vanilla, "Vanilla patch untouched");
Check(Gate(new[] { Cape + "flower-notreviewed-free" }, out var uk) == null && uk == GateKind.Unknown, "Unreviewed pack code keeps climate rules");
Check(Gate(new[] { belladonna, "game:flower-cornflower-free" }, out var mk) == null && mk == GateKind.Unknown, "Mixed patch keeps climate rules");
Check(Gate(new[] { belladonna, maize }, out var ck) == null && ck == GateKind.ClimateOnly, "Climate-only species keeps the whole patch climate-only");
Check(all.Find(belladonna.ToUpperInvariant()) != null, "Asset codes are case-insensitive");
var union = Gate(new[] { belladonna, flax }, out _);
Check(union.Allows(all.RegionAt(capeTown.Item1, capeTown.Item2, false)) && union.Allows(all.RegionAt(wellington.Item1, wellington.Item2, false)), "Multi-species patch uses the union of ranges");
Check(!new PlantGate(geography.EmptySet(), false, Array.Empty<Rule>()).Allows(-1) && !union.Allows(-1) && !union.Allows(100000), "Outside Earth / unmapped is never allowed");

// ---------------------------------------------------------------- geography parity with the tree catalogue
var trees = new BotanicalRegions(regionsJson);
var rnd = new Random(1234);
int mismatches = 0, samples = 40000, land = 0;
for (int i = 0; i < samples; i++)
{
    double lat = rnd.NextDouble() * 170 - 85, lon = rnd.NextDouble() * 360 - 180;
    string expected = trees.CodeAt(lat, lon);
    int got = geography.Find(lat, lon);
    if (got >= 0) land++;
    if (expected != geography.Code(got)) mismatches++;
}
Check(mismatches == 0, $"Region lookup differs from BotanicalRegions at {mismatches} of {samples} points");
Check(geography.Find(-17.8, 178) == geography.Find(-17.8, 538), "Wrapped longitude identical");
Check(geography.Find(double.NaN, 0) == -1 && geography.Find(95, 0) == -1, "Invalid latitude excluded");
var geo = new EarthGeometry { RingStart = 25600, RingLength = 204800, Wraps = true, Gutter = 1024, EquatorZ = 51200, BlocksPerDegreeZ = 500, SeamLon0 = -168 };
foreach (var p in new[] { capeTown, wellington, athens, (57.0, 25.0) })
{
    geo.FromGeo(p.Item1, p.Item2, out double x, out double z);
    var g = geo.ToGeo(x + geo.RingLength, z);
    Check(geography.Find(p.Item1, p.Item2) == geography.Find(g.Lat, g.Lon), "Earth seam preserves the botanical country");
}

// ---------------------------------------------------------------- performance and allocations
var landPoints = new List<(double, double)>();
while (landPoints.Count < 20000)
{
    double lat = rnd.NextDouble() * 140 - 60, lon = rnd.NextDouble() * 360 - 180;
    if (geography.Find(lat, lon) >= 0) landPoints.Add((lat, lon));
}
var gates = all.Rules.Where(r => !r.ClimateOnly).Select(r => Gate(r.Codes, out _)).ToArray();
var landArray = landPoints.ToArray();
Bench.Lookup(all, gates, landArray, 5000, false); // warm-up (JIT)
var (coldMicros, allocated) = Bench.Lookup(all, gates, landArray, 200000, false);
Check(allocated == 0, $"Uncached lookup allocates {allocated} bytes");
Check(coldMicros < 2.0, $"Uncached land lookup {coldMicros:F3} µs (budget 2 µs)");
// Hot path as used per patch attempt: gate bit test after the per-chunk region cache.
int cachedRegion = all.RegionAt(capeTown.Item1, capeTown.Item2, false);
Bench.Cached(gates, cachedRegion, 1000);
var (hotNanos, hotBytes) = Bench.Cached(gates, cachedRegion, 10_000_000);
Check(hotBytes == 0 && hotNanos < 100, $"Cached gate test {hotNanos:F1} ns, {hotBytes} bytes");
var seaPoints = new List<(double, double)>();
while (seaPoints.Count < 2000)
{
    double lat = rnd.NextDouble() * 140 - 70, lon = rnd.NextDouble() * 360 - 180;
    if (geography.Find(lat, lon) < 0) seaPoints.Add((lat, lon));
}
var seaArray = seaPoints.ToArray();
// Shore points just outside the polygons resolved by the bounded coastal allowance: the slowest land lookups.
var shore = new List<(double, double)>();
while (shore.Count < 2000)
{
    double lat = rnd.NextDouble() * 140 - 60, lon = rnd.NextDouble() * 360 - 180;
    int inside = geography.Find(lat, lon, false);
    if (inside >= 0)
    {
        // walk outwards from a land point until it leaves the polygons
        for (double step = .01; step < .08; step += .01)
            if (geography.Find(lat, lon + step, false) < 0) { if (geography.Find(lat, lon + step) >= 0) shore.Add((lat, lon + step)); break; }
    }
}
var shoreArray = shore.ToArray();
Bench.Lookup(all, gates, shoreArray, 200, false);
var (shoreMicros, shoreBytes) = Bench.Lookup(all, gates, shoreArray, 20000, false);
Check(shoreBytes == 0 && shoreMicros < 5, $"Shore lookup {shoreMicros:F2} µs, {shoreBytes} bytes");
Bench.Lookup(all, gates, seaArray, 200, false);
var (seaLandMicros, _) = Bench.Lookup(all, gates, seaArray, 20000, false);
Check(seaLandMicros < 5, $"Open-sea land lookup {seaLandMicros:F2} µs");
Bench.Lookup(all, gates, seaArray, 200, true);
var (seaMicros, seaBytes) = Bench.Lookup(all, gates, seaArray, seaArray.Length, true);
int nearHits = seaArray.Count(p => all.RegionAt(p.Item1, p.Item2, true) >= 0);
Check(seaBytes == 0, $"Open-water lookup allocates {seaBytes} bytes");
Check(seaMicros < 200, $"Open-water lookup {seaMicros:F1} µs per chunk");

// ---------------------------------------------------------------- thread safety
int capeRegion = all.RegionAt(capeTown.Item1, capeTown.Item2, false);
var capeGate = Gate(new[] { belladonna }, out _);
Parallel.For(0, 200000, i =>
{
    if (all.RegionAt(capeTown.Item1, capeTown.Item2, false) != capeRegion || !capeGate.Allows(capeRegion)) throw new Exception("Concurrent lookup");
    var p = landPoints[i % landPoints.Count];
    if (all.RegionAt(p.Item1, p.Item2, false) != geography.Find(p.Item1, p.Item2)) throw new Exception("Concurrent lookup mismatch");
});
passed++;

Console.WriteLine($"PASS {passed} regional plant checks.");
Console.WriteLine($"Coverage: {inventory.Count} pack patches; by species range {species} ({100.0 * species / inventory.Count:F1}%), by pack region {packRegion} ({100.0 * packRegion / inventory.Count:F1}%), climate-only {climate} ({100.0 * climate / inventory.Count:F1}%; {inventory.Count(e => e.Basis == "climateOnly" && e.File == "crop.json")} wild crops), unmapped 0.");
Console.WriteLine($"Rules {all.Rules.Count}; region parity with BotanicalRegions on {samples} random points ({land} on land): {mismatches} differences.");
Console.WriteLine($"Perf: uncached land lookup + gate {coldMicros:F3} µs (shore points {shoreMicros:F2} µs, open sea {seaLandMicros:F2} µs), cached gate test {hotNanos:F1} ns, " +
    $"aquatic nearest-country lookup in open water {seaMicros:F1} µs ({nearHits}/{seaPoints.Count} sea points within {WaterSearchDegrees}°); 0 bytes allocated.");

static class Bench
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public static (double Micros, long Bytes) Lookup(RegionalPlantCatalog catalog, PlantGate[] gates, (double, double)[] points, int count, bool water)
    {
        int allowed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
        {
            var p = points[i % points.Length];
            if (gates[i % gates.Length].Allows(catalog.RegionAt(p.Item1, p.Item2, water))) allowed++;
        }
        long ticks = Stopwatch.GetTimestamp() - start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(allowed);
        return (ticks * 1e6 / Stopwatch.Frequency / count, bytes);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public static (double Nanos, long Bytes) Cached(PlantGate[] gates, int region, int count)
    {
        int allowed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) if (gates[i % gates.Length].Allows(region)) allowed++;
        long ticks = Stopwatch.GetTimestamp() - start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(allowed);
        return (ticks * 1e9 / Stopwatch.Frequency / count, bytes);
    }
}
