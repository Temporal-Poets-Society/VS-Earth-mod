using System.Reflection;
using System.Text.Json;
using EarthMap;
using Vintagestory.API.Common;
using Vintagestory.ServerMods;

static partial class Checks
{
    /// <summary>Decision 0102: the blocks Earth adds for galena in crystalline rock match CrystallineGalena's rocks, and vanilla's
    /// galena deposit is as CrystallineGalena reads it (its codes, its hosts without these rocks, its silver child); the
    /// prospecting pick's own deposits can be read.</summary>
    static void CrystallineGalenaChecks()
    {
        string vs = typeof(Checks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
        string root = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        Check(CrystallineGalena.PickReadable, "the prospecting pick's own deposits (ProPickWorkSpace.depositGen) can be read");

        // the patch: every rock's ore, silver, loose-ore and item variants, and no other rock
        var patch = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "assets", "earth", "patches", "galena-crystalline-ores.json"))).RootElement.EnumerateArray()
            .Select(p => (File: p.GetProperty("file").GetString(), Path: p.GetProperty("path").GetString(), Value: p.GetProperty("value").GetString())).ToList();
        Check(patch.All(p => p.Path == "/allowedVariants/-"), "the galena patch only adds variants");
        var rocks = patch.Select(p => p.Value.Split('-')).Select(s => s[^1] == "free" ? s[^2] : s[^1]).Distinct().ToArray();
        Check(rocks.OrderBy(r => r).SequenceEqual(CrystallineGalena.Rocks.OrderBy(r => r)), "the patch's rocks are CrystallineGalena's: " + string.Join(", ", rocks));
        bool Has(string file, string value) => patch.Any(p => p.File == "game:" + file && p.Value == value);
        foreach (string rock in CrystallineGalena.Rocks)
        {
            Check(Has("blocktypes/stone/ore-graded.json", $"ore-poor-galena-{rock}") && Has("blocktypes/stone/ore-graded.json", $"ore-medium-galena-{rock}")
                && Has("blocktypes/stone/ore-graded.json", $"ore-poor-galena_nativesilver-{rock}") && Has("blocktypes/stone/ore-graded.json", $"ore-medium-galena_nativesilver-{rock}")
                && Has("blocktypes/stone/looseores.json", $"looseores-galena-{rock}-free"), rock + ": galena, its silver and its loose ore are blocks");
            Check(Has("itemtypes/resource/ore-graded.json", $"ore-poor-galena-{rock}") && Has("itemtypes/resource/crystalizedore-graded.json", $"crystalizedore-poor-galena-{rock}"), rock + ": its ore items exist");
            int grades = patch.Count(p => p.File == "game:blocktypes/stone/ore-graded.json" && p.Value.EndsWith("-galena-" + rock));
            Check(grades == (rock == "granite" ? 2 : 4), $"{rock}: galena has sphalerite's grades in it ({grades})");
        }

        // vanilla's galena deposit as CrystallineGalena.AddTo reads it
        string survival = Path.Combine(vs, "assets", "survival");
        var galena = JsonUtil.FromString<DepositVariant[]>(File.ReadAllText(Path.Combine(survival, "worldgen", "deposits", "metalore", "galena.json")))
            .Single(v => v.Code == CrystallineGalena.DepositCode);
        var a = galena.Attributes;
        Check(a["inblock"]["code"].AsString() == "rock-*" && a["inblock"]["name"].AsString() == "rock" && a["placeblock"]["code"].AsString() == "ore-*-galena-{rock}"
            && a["surfaceBlock"]["code"].AsString() == "looseores-galena-{rock}-free", "vanilla's galena deposit names its blocks as CrystallineGalena builds them");
        var hosts = a["inblock"]["allowedVariants"].AsArray<string>();
        Check(hosts.Length > 0 && !hosts.Intersect(CrystallineGalena.Rocks).Any(), "vanilla's galena names none of the crystalline rocks (" + string.Join(", ", hosts) + ")");
        Check(galena.ChildDeposits?.Length == 1 && galena.ChildDeposits[0].Attributes["placeblock"]["code"].AsString() == "ore-*-galena_nativesilver-{rock}"
            && galena.ChildDeposits[0].Attributes["placeblock"]["allowedVariants"].AsArray<string>().SequenceEqual(new[] { "poor", "medium" }),
            "its silver nests in the host's galena blocks, poor and medium (the patch's two silver grades)");
        Console.WriteLine("  galena in crystalline rock: blocks, rocks and vanilla's deposit checked");
    }
}
