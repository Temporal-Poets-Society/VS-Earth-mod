using System.Reflection;
using System.Text.Json;
using EarthMap;

static partial class Checks
{
    /// <summary>(Decision 0105, EA-0199) The forest floor in a world with Earth soil: what it gives for the block beneath, the
    /// patch that adds the behaviour to vanilla's forestfloor, and vanilla's block as the patch expects it (a behaviours list,
    /// its own fertility and drop untouched).</summary>
    static void ForestFloorChecks()
    {
        string vs = typeof(Checks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
        string root = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string G(string path, bool air = false, int tiers = EarthMap.Core.Topsoil.RealisticTiers) => BlockBehaviorEarthForestFloorSoil.GradeBeneath(path, air, tiers);
        // the soil it lies on, in its own grade
        foreach (var (block, grade) in new[] { ("soil-verylow-none", "verylow"), ("soil-low-none", "low"), ("soil-medium-none", "medium"),
                     ("soil-compost-none", "compost"), ("soil-high-none", "high"), ("soil-medium-normal", "medium") })
            Check(G(block) == grade, "a forest floor on " + block + " gives " + grade + " soil (got " + G(block) + ")");
        // no soil beneath: the closest sensible block
        Check(G("rawclay-red-none") == "low" && G("rawclay-blue-none") == "low", "on clay: low soil");
        foreach (string b in new[] { "rock-granite", "rock-limestone", "sand-granite", "gravel-granite", "muddygravel" })
            foreach (int tiers in new[] { EarthMap.Core.Topsoil.VanillaTiers, EarthMap.Core.Topsoil.RealisticTiers })
                Check(G(b, tiers: tiers) == "verylow", "on " + b + ": barren soil (got " + G(b, tiers: tiers) + ")");
        // peat: the tier set's grade of peat soils (soil-rules-v4.json's Histosols)
        Check(G("peat-none", tiers: EarthMap.Core.Topsoil.VanillaTiers) == "low" && G("bogpeat", tiers: EarthMap.Core.Topsoil.VanillaTiers) == "low"
            && G("peat-none", tiers: EarthMap.Core.Topsoil.RealisticTiers) == "verylow", "on peat: low (vanilla tiers), barren (realistic tiers)");
        var rules = new EarthMap.Core.Topsoil(File.ReadAllText(Path.Combine(root, "assets", "earth", "config", "soil-rules-v4.json")), null, 1);
        foreach (int tiers in new[] { EarthMap.Core.Topsoil.VanillaTiers, EarthMap.Core.Topsoil.RealisticTiers })
        {
            var histosols = rules.Decide(tiers, new EarthMap.Core.Topsoil.Place { Class = "Histosols", Temperature = 10, WarmestMonth = 20 });
            Check(G("peat-none", tiers: tiers) == EarthMap.Core.Topsoil.BlockCodes[(int)histosols], "a forest floor on peat gives what the tier set gives peat soils (" + histosols + ")");
        }
        Check(G(null) == "verylow", "on another mod's block (Earth's own rocks): barren soil");
        Check(G("looseboulders-granite-free") == "" && G("forestfloor-3") == "", "a loose stone or another forest floor decides nothing (look deeper)");
        Check(G("soil-medium-none", air: true) == null && G(null, air: true) == null, "air or water beneath: vanilla's drop");
        // the patch adds the behaviour to vanilla's forest floor, and nothing else
        var patch = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "assets", "earth", "patches", "forestfloor-soil.json"))).RootElement.EnumerateArray().ToList();
        Check(patch.Count == 1 && patch[0].GetProperty("file").GetString() == "game:blocktypes/soil/forestfloor.json" && patch[0].GetProperty("op").GetString() == "add"
            && patch[0].GetProperty("path").GetString() == "/behaviors/-" && patch[0].GetProperty("value").GetProperty("name").GetString() == EarthForestFloorSoilSystem.BehaviorName,
            "the patch appends EarthForestFloorSoil to vanilla's forest floor behaviours");
        // vanilla's block as the patch expects it: a behaviours list to append to; its fertility and drop are vanilla's
        string ff = File.ReadAllText(Path.Combine(vs, "assets", "survival", "blocktypes", "soil", "forestfloor.json"));
        Check(ff.Contains("behaviors:") && ff.Contains("MyceliumHost") && ff.Contains("code: \"soil-low-none\"") && ff.Contains("fertility: 120"),
            "vanilla's forest floor has a behaviours list, drops soil-low-none and has fertility 120 (the patch leaves both)");
    }
}
