using System.Security.Cryptography;
using System.Text.Json;
using EarthMap;
using EarthMap.Core;
using ProtoBuf;
using Vintagestory.API.Datastructures;

/// <summary>
/// (EA-0089, CR-A5) A new world's params as WorldParamsBuilder makes them, for every play style of worldconfig.json and the
/// variations of the Earth tab, earth.json, Cylinder World and Hydrology around each, against the hashes pinned in
/// new-world-params.txt beside this file. A difference is a change to what new worlds get: find it, or, for a change meant
/// to be one, write the file again (EARTH_PARAMS_GOLDEN=write) and say so in the commit. The params read the data a server
/// has (EARTH_STORE, else the player's or the AgentBridge bench's ModData\earth), and the file names the data it was written
/// with: with other data, or none, the comparison is skipped, said.
/// </summary>
static class ParamsBuilderChecks
{
    sealed class Case
    {
        public string Name;
        public Action<TreeAttribute> Tab = _ => { };
        public Action<EarthMapConfig> Config = _ => { };
        public int Hydrology = 3;
        public bool KeepsSeam = true;
    }

    static TreeAttribute StyleConfig(JsonElement style)
    {
        var wc = new TreeAttribute();
        foreach (var kv in style.GetProperty("worldConfig").EnumerateObject())
        {
            switch (kv.Value.ValueKind)
            {
                case JsonValueKind.True: case JsonValueKind.False: wc.SetBool(kv.Name, kv.Value.GetBoolean()); break;
                case JsonValueKind.Number: wc.SetInt(kv.Name, kv.Value.GetInt32()); break;
                default: wc.SetString(kv.Name, kv.Value.GetString()); break;
            }
        }
        return wc;
    }

    /// <summary>The new world a play style makes on this machine's test inputs: the ring as SavedWorldDigestTests' new world
    /// has it, from the style's Earth size; its height; the sea level its terrain options give.</summary>
    static NewWorld World(TreeAttribute wc, Case c)
    {
        string size = wc.GetAsString("earthWorldSize", "100");
        SpawnSearch.SizeFor(size, out int W, out int L, out _);
        int H = EarthWorldOptions.Height(wc, out _);
        if (H <= 0) H = 384;
        int sea = EarthWorldOptions.Terrain(wc, H).SeaLevel(H, 165);
        return new NewWorld
        {
            MapSizeX = W, MapSizeY = H, SeaLevel = sea, Seed = 386217, WorldConfig = wc, HydrologyQuery = c.Hydrology,
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 608,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 40, CylinderKeepsEarthSeam = () => c.KeepsSeam,
        };
    }

    /// <summary>A new world's params from the Earth tab <paramref name="wc"/> (null: none) and <paramref name="cfg"/>, on a
    /// 1:100 ring <paramref name="height"/> blocks high, without data unless given: how the other checks make one (they
    /// called EarthWorldOptions.Freeze on bare params before EA-0089 C).</summary>
    public static EarthParams NewParams(ITreeAttribute wc, EarthMapConfig cfg, EarthData data = null, int height = 384)
    {
        SpawnSearch.SizeFor("100", out int W, out int L, out _);
        var w = new NewWorld
        {
            MapSizeX = W, MapSizeY = height, SeaLevel = height * 110 / 384, Seed = 386217, WorldConfig = wc, HydrologyQuery = 3,
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 608,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 40, CylinderKeepsEarthSeam = () => true,
        };
        return WorldParamsBuilder.Build(w, cfg, data, TestLog.Create().Logger, out _, out _);
    }

    static IEnumerable<Case> Cases()
    {
        Case T(string name, Action<TreeAttribute> tab) => new Case { Name = "tab " + name, Tab = tab };
        Case C(string name, Action<EarthMapConfig> config) => new Case { Name = "earth.json " + name, Config = config };
        yield return new Case { Name = "as the style has it" };
        foreach (var (rocks, ores) in new[] { ("groundcolumns", "maps"), ("regional", "vanilla"), ("regional", "maps"), ("vanilla", "vanilla"), ("vanilla", "maps") })
            yield return T("rocks " + rocks + ", ores " + ores, t => { t.SetString("earthRocks", rocks); t.SetString("earthOreDeposits", ores); });
        foreach (string legacy in new[] { "regional", "groundcolumns", "vanilla" })
            yield return T("only the old rocks-and-ores key " + legacy, t => { t.RemoveAttribute("earthRocks"); t.RemoveAttribute("earthOreDeposits"); t.SetString("earthOres", legacy); });
        yield return T("no rocks or ores key", t => { t.RemoveAttribute("earthRocks"); t.RemoveAttribute("earthOreDeposits"); });
        yield return T("nature vanilla", t => t.SetString("earthNature", "vanilla"));
        yield return T("no nature key, old animals vanilla", t => { t.RemoveAttribute("earthNature"); t.SetString("earthAnimals", "vanilla"); });
        yield return T("no nature key", t => t.RemoveAttribute("earthNature"));
        yield return T("biomes vanilla", t => t.SetString("earthBiomes", "vanilla"));
        yield return T("nature and biomes vanilla", t => { t.SetString("earthNature", "vanilla"); t.SetString("earthBiomes", "vanilla"); });
        yield return T("weather vanilla", t => t.SetString("earthWeather", "vanilla"));
        yield return T("rain 0.5, storms 5", t => { t.SetString("earthRainFrequency", "0.5"); t.SetString("earthStormFrequency", "5"); });
        yield return T("rain not a number", t => t.SetString("earthRainFrequency", "lots"));
        foreach (string elevation in new[] { "glo90", "glo90coarse", "builtin" })
            yield return T("elevation " + elevation, t => t.SetString("earthElevation", elevation));
        yield return T("springs streams, ponds wetlands", t => { t.SetString("earthMountainSprings", "streams"); t.SetString("earthSurfacePonds", "wetlands"); });
        yield return T("springs and ponds vanilla, rapids off", t => { t.SetString("earthMountainSprings", "vanilla"); t.SetString("earthSurfacePonds", "vanilla"); t.SetString("earthRiverRapids", "off"); });
        yield return T("extinct animals, chickens by range", t => { t.SetBool("earthIncludeExtinctAnimals", true); t.SetBool("earthChickensEverywhere", false); });
        yield return T("curve log250", t => t.SetString("earthHeightCurve", "log250"));
        yield return T("curve linear", t => t.SetString("earthHeightCurve", "linear"));
        foreach (string budget in new[] { "128", "160" })
            yield return T("vertical budget " + budget, t => t.SetString("earthVerticalBudget", budget));
        yield return T("height 512", t => t.SetInt("earthWorldHeight", 512));
        yield return T("summits off", t => t.SetString("earthSummitCorrection", "off"));
        yield return T("summits from 1000 m", t => t.SetString("earthSummitCorrection", "1000"));
        yield return T("rock floor off", t => t.SetString("earthRockFloor", "off"));
        yield return T("rock floor 128", t => t.SetString("earthRockFloor", "128"));
        yield return T("ground depth terrain", t => t.SetString("earthGroundDepth", "terrain"));
        yield return T("ground depth linear2000", t => t.SetString("earthGroundDepth", "linear2000"));
        // 2.0.1: one canals option ("both canals" keeps its pinned params). With it off (as every new world has it), the two
        // keys before 2.0.1, now off the screen, each on its own: what a server's world configuration that names them gives
        yield return T("both canals", t => t.SetBool("earthCanals", true));
        yield return T("both canals by the keys before 2.0.1", t => { t.SetBool("earthSuezCanal", true); t.SetBool("earthPanamaCanal", true); });
        yield return T("Suez only by the key before 2.0.1", t => t.SetBool("earthSuezCanal", true));
        yield return T("Panama only by the key before 2.0.1", t => t.SetBool("earthPanamaCanal", true));
        yield return C("rivers off", c => c.Rivers = false);
        yield return C("islands off", c => c.Islands = false);
        yield return C("coasts off", c => c.Coasts = false);
        yield return C("seam gutter", c => c.SeamGutter = true);
        yield return C("regional trees off", c => c.RegionalTrees = false);
        yield return C("regional geology off", c => c.RegionalGeology = false);
        yield return C("ground columns off", c => c.GroundColumnsPrototype = false);
        yield return C("regional ores on", c => c.RegionalOres = true);
        yield return C("regional fauna off, extinct animals", c => { c.RegionalFauna = false; c.IncludeExtinctAnimals = true; });
        yield return C("fauna ranges current", c => c.FaunaRangeProfile = "current");
        yield return C("streams all", c => c.Streams = "all");
        yield return C("streams none", c => c.Streams = "none");
        yield return C("weather off, pace 2", c => { c.Weather = false; c.WeatherPace = 2; });
        yield return C("temperature range extended", c => c.ExtendTemperatureRange = !new EarthMapConfig().ExtendTemperatureRange);
        yield return C("detail scale not a number", c => c.DetailScale = double.NaN);
        yield return C("land curve 0, gutter margin -5", c => { c.LandCurve = 0; c.GutterMargin = -5; });
        yield return C("min island 4, coast cells 2000", c => { c.MinIslandBlocks = 4; c.CoastMinCellBlocks = 2000; });
        yield return C("ores tuned", c => c.Ores = new OreTuning { BackgroundChance = 0.2, RadiusMultiplier = 2 });
        yield return new Case { Name = "Hydrology query 2", Hydrology = 2 };
        yield return new Case { Name = "Cylinder World without its Earth seam", KeepsSeam = false };
    }

    static string DataIdentity(EarthData d) => string.Join(" ",
        "referee=" + d.SeaRefereeAvailable, "courses=" + d.Glo90RiverCourses, "climatefix=" + d.ClimateFixAvailable,
        "weather=" + (d.Weather?.Signature ?? "-"), "coarse=" + (d.Coarse != null), "outlets=" + (d.Outlets != null),
        "works=" + (d.Works1318 != null), "perennial=" + (d.NonPerennial != null), "penman=" + (d.PenmanError == null),
        "rain=" + (d.FineRainError == null), "summits=" + (d.SummitsError == null), "glaciers=" + (d.GlaciersError == null));

    public static void Run(string root, Action<bool, string> check)
    {
        string store = Environment.GetEnvironmentVariable("EARTH_STORE");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        store = new[] { store, Path.Combine(appData, "VintagestoryData", "ModData", "earth"), Path.Combine(appData, "VintagestoryData-agentbridge", "ModData", "earth") }
            .FirstOrDefault(s => !string.IsNullOrEmpty(s) && Directory.Exists(Path.Combine(s, "data")));
        if (store == null) { Console.WriteLine("SKIP new-world params: no Earth data store on this machine (EARTH_STORE)"); return; }
        var data = EarthData.Load(name =>
        {
            string a = Path.Combine(root, "earthdata", name), b = Path.Combine(store, "data", name);
            return File.Exists(a) ? File.OpenRead(a) : File.Exists(b) ? File.OpenRead(b) : null;
        }, true);
        string identity = DataIdentity(data);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json")));
        var lines = new List<string>();
        var log = TestLog.Create().Logger;
        int built = 0;
        foreach (var style in doc.RootElement.GetProperty("playStyles").EnumerateArray())
        {
            string code = style.GetProperty("code").GetString();
            foreach (var c in Cases())
            {
                var wc = StyleConfig(style);
                c.Tab(wc);
                var cfg = new EarthMapConfig();
                c.Config(cfg);
                var world = World(wc, c);
                var p = WorldParamsBuilder.Build(world, cfg, data, log, out _, out _);
                // EA-0089 B: what RepairSaved lays out again for a saved world whose layout can't place the Earth is a new world's
                var geo = WorldParamsBuilder.Layout(world, cfg, log, out int gutterSeabed, out _);
                check(geo.RingStart == p.RingStart && geo.RingLength == p.RingLength && geo.Wraps == p.Wraps && geo.Gutter == p.Gutter && geo.EquatorZ == p.EquatorZ
                    && geo.BlocksPerDegreeZ == p.BlocksPerDegreeZ && geo.SeamLon0 == p.SeamLon0 && gutterSeabed == p.GutterSeabed,
                    "new-world params: the layout alone (RepairSaved's) is the new world's, " + code + " | " + c.Name);
                using var s = new MemoryStream();
                Serializer.Serialize(s, p);
                lines.Add(code + " | " + c.Name + " | " + Convert.ToHexString(SHA256.HashData(s.ToArray()))[..16]);
                built++;
            }
        }
        check(built > 0 && built == lines.Distinct().Count(), "new-world params: one line per case");
        string golden = Path.Combine(root, "tests", "EarthWorldOptionsTests", "new-world-params.txt");
        if (Environment.GetEnvironmentVariable("EARTH_PARAMS_GOLDEN") == "write")
        {
            File.WriteAllLines(golden, new[] { "# data: " + identity }.Concat(lines));
            Console.WriteLine("new-world params: wrote " + built + " cases to " + golden);
            return;
        }
        var pinned = File.Exists(golden) ? File.ReadAllLines(golden) : Array.Empty<string>();
        if (pinned.Length == 0 || pinned[0] != "# data: " + identity)
        {
            Console.WriteLine("SKIP new-world params: " + golden + " was written with other data (" + (pinned.Length > 0 ? pinned[0] : "no file") + "; here: " + identity + ")");
            return;
        }
        var differ = lines.Except(pinned.Skip(1)).ToList();
        check(differ.Count == 0 && pinned.Length - 1 == lines.Count,
            "new-world params differ from new-world-params.txt in " + differ.Count + " of " + lines.Count + " cases (" + (pinned.Length - 1) + " pinned), first: " + string.Join("; ", differ.Take(5)));
        Console.WriteLine("new-world params: " + built + " cases as pinned (" + doc.RootElement.GetProperty("playStyles").GetArrayLength() + " play styles)");
    }
}
