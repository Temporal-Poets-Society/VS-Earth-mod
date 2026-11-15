using EarthMap;
using System.Text.Json;
using Func4 = System.Func<double, double, double, bool, System.ValueTuple<double, double>>;
using Func3 = System.Func<double, double, double, System.ValueTuple<double, double>>;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
string Problem(string hv = RequiredMods.HydrologyMinimum, object hydro = null, bool server = true)
    => RequiredMods.Problem(hv, hydro ?? new Hydro(), server);
Check(Problem() == null, "Minimum provider versions and APIs load");
Check(Problem("0.6.2") == null && Problem("0.6.3") == null && Problem("0.7.0") == null,
    "Hydrology 0.6.2 and later releases (river query version 3) load");
foreach (string version in new[] { null, "", "garbage", "0.1.9", "0.1.10", "0.1.13", "0.6.0", "0.6.1", "0.6.1-pre.1", "0.6.1-rivers.1", "0.6.2-rivers.7", "0.6.2-rc.1", "0.6.2-rc.3", "0.6.2-rc.4", "0.6.2-rc.13" })
    Check(Problem(hv: version)?.Contains("Hydrology") == true, "Reject old/missing/invalid Hydrology: " + version);
Check(Problem(hydro: new OldHydro())?.Contains("EarthRiverQueryVersion") == true, "Reject version 2 river capabilities (Hydrology 0.6.1)");
Check(Problem(hydro: new object())?.Contains("EarthRiverQueryVersion") == true, "Reject a missing Hydrology API despite its version");
Check(RequiredMods.Problem(RequiredMods.HydrologyMinimum, null, true) != null, "Reject a disabled/unloaded Hydrology system");
Check(RequiredMods.QueryVersion(new Hydro()) == 3 && RequiredMods.QueryVersion(new OldHydro()) == 2 && RequiredMods.QueryVersion(new object()) == 0
    && RequiredMods.QueryVersion((object)null) == 0, "River query version read from a Hydrology system, 0 without one");
Check(RequiredMods.Problem(null, null, false) == null, "Client accepts server-only Hydrology");
// Sea is optional (decision 0041): no provider check, only EarthWeather's binding of a compatible one
Check(RequiredMods.SeaWindSetter(new SeaProvider()) != null && RequiredMods.SeaWindClearer(new SeaProvider()) != null, "A compatible Sea provider binds");
Check(RequiredMods.SeaWindSetter(new ExtraOverloadProvider()) != null, "Compatible providers may add other overloads");
foreach (object provider in new object[] { new object(), new WrongReturn(), new StaticProvider(), new GenericOnlyProvider() })
    Check(RequiredMods.SeaWindSetter(provider) == null, "An incompatible Sea provider doesn't bind: " + provider.GetType().Name);
Check(RequiredMods.SeaWindClearer(new MissingClear()) == null && RequiredMods.SeaWindSetter(null) == null, "No clearer, no provider: nothing binds");

Check(RequiredMods.NeedsProviders(new EarthParams { Enabled = true }, false, "standard", "surviveandbuild", false), "Saved Earth cannot bypass guards by changing style/config");
Check(!RequiredMods.NeedsProviders(new EarthParams { Enabled = false }, true, "standard", "earth", true), "Saved inactive world remains inactive");
Check(RequiredMods.NeedsProviders(null, true, "standard", "earth", true), "New Earth requires providers");
foreach (var input in new[] { (false, "standard", "earth", true), (true, "standard", "surviveandbuild", true), (true, "superflat", "earth", true), (true, "standard", "earth", false) })
    Check(!RequiredMods.NeedsProviders(null, input.Item1, input.Item2, input.Item3, input.Item4), "Inactive/vanilla worlds do not run provider guards");
var events = new List<string>();
RequiredMods.Enforce(null, () => events.Add("stop"), _ => events.Add("log"));
Check(events.Count == 0, "Successful preflight has no stop/log effects");
try
{
    RequiredMods.Enforce("Hydrology capability missing", () => events.Add("stop"), _ => events.Add("log"));
    throw new Exception("Expected startup refusal");
}
catch (InvalidOperationException e)
{
    Check(events.SequenceEqual(new[] { "log", "stop" }), "Stop request precedes the exception swallowed by the game loader");
    Check(e.Message.Contains("Hydrology capability missing") && e.Message.Contains("restart"), "Actionable failure reaches the loader");
}
// review A5: a saved world needs the data store files its rules read, not all 38. An Earth 1.0.x world reads only what
// its two Mod DB packs carry (tools/package-publication.py at v1.0.2) plus the soil sites and wetlands 2.0 reads for
// every world; a world with every rule reads them all
var store = EarthMap.Core.EarthDataStore.Files.Select(f => f.Name).ToHashSet();
var packs = new[] { "terrain.bin", "summits.bin", "peaks.json", "glaciers.bin", "rivers.bin", "islands.bin", "coasts.bin", "lakes.bin", "shores.bin" };
Check(packs.All(store.Contains) && store.Count == 38, "the 1.0.2 packs' 9 files are store files, of 38");
var v10 = new EarthParams { Enabled = true, Cylinder = true, Rivers = true, Islands = 1, Coasts = 2, RiverEnds = 1, LakeShores = 1, Summits = 2, Glaciers = 1, AlpineSurfaceRules = 1 };
var v10Files = EarthMapSystem.StoreFilesRead(v10);
Check(v10Files.SetEquals(packs.Append("soil-site.bin").Append("wetlands.bin")),
    "A5: a 1.0.x world reads its packs' files and the soil sites and wetlands, not the other 27 (" + string.Join(", ", v10Files.OrderBy(n => n)) + ")");
var bare = EarthMapSystem.StoreFilesRead(new EarthParams { Enabled = true, Cylinder = true });
Check(bare.SetEquals(new[] { "terrain.bin", "rivers.bin", "soil-site.bin", "wetlands.bin" }), "A5: every world reads the grid, rivers.bin, the soil sites and wetlands");
var all = new EarthParams { Enabled = true, Cylinder = true, Rivers = true, Islands = 1, Coasts = 2, RiverEnds = 1, LakeShores = 1, Summits = 2, Glaciers = 1,
    AlpineSurfaceRules = 2, SeaReferee = 2, CoarseElevation = 1, RiverCourses = 4, SpringRules = EarthParams.SpringRulesStreams, GroundColumns = 1, SoilRules = 3, ShoreRules = 1 };
var allFiles = EarthMapSystem.StoreFilesRead(all);
Check(allFiles.SetEquals(store), "A5: a world with every rule reads all 38 (missing: " + string.Join(", ", store.Except(allFiles)) + ")");
Check(EarthMapSystem.StoreFilesRead(new EarthParams { Enabled = true, RiverCourses = 1 }).Contains("rivers-glo90.bin")
    && !EarthMapSystem.StoreFilesRead(new EarthParams { Enabled = true, RiverCourses = 1 }).Contains("rivers-glo90-snap.bin")
    && EarthMapSystem.StoreFilesRead(new EarthParams { Enabled = true, GroundColumns = 1, SoilRules = 2 }).Count(n => n.EndsWith(".egt") || n.EndsWith(".egts")) == 13
    && !EarthMapSystem.StoreFilesRead(new EarthParams { Enabled = true, GroundColumns = 1, SoilRules = 2 }).Contains("floodplains.bin"),
    "A5: each rule brings its own files (river courses 1: rivers-glo90.bin alone; ground columns: the 13 2' sets, floodplains only with soil rules 3)");
string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "modinfo.json")));
var deps = manifest.RootElement.GetProperty("dependencies");
Check(deps.GetProperty("hydrology").GetString() == RequiredMods.HydrologyMinimum, "Manifest agrees with Hydrology's runtime minimum");
Check(!deps.TryGetProperty("sea", out _), "Manifest has no hard Sea dependency");
Console.WriteLine($"Required mod checks: {checks} passed.");

sealed class Hydro { public const int EarthRiverQueryVersion = 3; }
sealed class OldHydro { public const int EarthRiverQueryVersion = 2; }
class SeaProvider
{
    public bool SetWindProvider(string name, Func4 wind, Func3 prevailing) => true;
    public void ClearWindProvider(string name) { }
}
sealed class MissingClear { public bool SetWindProvider(string name, Func4 wind, Func3 prevailing) => true; }
sealed class WrongReturn
{
    public void SetWindProvider(string name, Func4 wind, Func3 prevailing) { }
    public void ClearWindProvider(string name) { }
}
sealed class StaticProvider
{
    public static bool SetWindProvider(string name, Func4 wind, Func3 prevailing) => true;
    public static void ClearWindProvider(string name) { }
}
sealed class ExtraOverloadProvider : SeaProvider
{
    public bool SetWindProvider<T>(string name, Func4 wind, Func3 prevailing) => true;
}
sealed class GenericOnlyProvider
{
    public bool SetWindProvider<T>(string name, Func4 wind, Func3 prevailing) => true;
    public void ClearWindProvider(string name) { }
}
