using EarthMap.Core;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Offline model of Earth's tree attempts on the real baked climate (earthdata/): the same climate inputs the game
/// receives from Earth's climate and forest maps, fed to the production <see cref="RegionalTreePicker"/>.
/// </summary>
sealed class Harness
{
    public readonly string Root;
    public readonly EarthData Data;
    public readonly VerticalScale Vertical = VerticalScale.ForWorld(384, 165);
    public readonly string RulesJson, RegionsJson;
    public readonly List<Registration> Registrations;
    public readonly string[] AllProviders;
    /// <summary>Generator → wood type of its logs/leaves (what the in-game self-test canopy counts; vanilla fir is pine wood).</summary>
    public readonly Dictionary<string, string> Woods;

    public sealed class Registration
    {
        public string Provider { get; set; }
        public string Kind { get; set; }
        public string Generator { get; set; }
        public float Weight { get; set; }
        public string Habitat { get; set; }
        public int MinTemp { get; set; } public int MaxTemp { get; set; }
        public int MinRain { get; set; } public int MaxRain { get; set; }
        public int MinFert { get; set; } public int MaxFert { get; set; }
        public int MinForest { get; set; } public int MaxForest { get; set; }
        public float MinHeight { get; set; } public float MaxHeight { get; set; }
        public TreeVariantInfo Info() => TreeVariantInfo.FromLimits(Generator, Weight,
            Habitat == "water" ? TreeVariantInfo.HabitatWater : Habitat == "landwater" ? TreeVariantInfo.HabitatLandWater : TreeVariantInfo.HabitatLand,
            MinTemp, MaxTemp, MinRain, MaxRain, MinFert, MaxFert, MinForest, MaxForest, MinHeight, MaxHeight);
    }

    public Harness(string root, bool loadData = true)
    {
        Root = root;
        RulesJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/regionaltrees.json"));
        RegionsJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/treeregions.json"));
        Registrations = JsonSerializer.Deserialize<List<Registration>>(File.ReadAllText(Path.Combine(root, "tests/RegionalTreesTests/registrations.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        AllProviders = Registrations.Select(r => r.Provider).Where(p => p != "vanilla").Distinct().ToArray();
        Woods = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "tests/RegionalTreesTests/generator-woods.json")))!;
        if (loadData) Data = EarthData.LoadFromFolder(root, true);
    }

    /// <summary>A pick configuration: registrations of the installed providers and the catalogue (null = pre-0.9).</summary>
    public sealed class Config
    {
        public string Name;
        public RegionalTreeCatalog Catalog;
        public RegionalTreePicker Trees, Shrubs;
        public Registration[] TreeRegs, ShrubRegs;
    }

    public Config Make(string name, string[] providers, bool regional)
    {
        var set = new HashSet<string>(providers) { "vanilla" };
        var catalog = regional ? new RegionalTreeCatalog(RulesJson, RegionsJson, providers) : null;
        var trees = Registrations.Where(r => set.Contains(r.Provider) && r.Kind == "treegens").ToArray();
        var shrubs = Registrations.Where(r => set.Contains(r.Provider) && r.Kind == "shrubgens").ToArray();
        return new Config
        {
            Name = name, Catalog = catalog, TreeRegs = trees, ShrubRegs = shrubs,
            Trees = new RegionalTreePicker(catalog, trees.Select(r => r.Info()).ToArray()),
            Shrubs = new RegionalTreePicker(catalog, shrubs.Select(r => r.Info()).ToArray(), shrubs: true),
        };
    }

    public double ForestPotential(double lat, double lon, out EarthData.ClimateSample cs, out double elev)
    {
        elev = Math.Max(0, Data.ElevBilinear(lat, lon));
        cs = Data.SampleClimate(lat, lon);
        return Geo.SmoothStep((cs.Rain - 60) / 80) * Geo.SmoothStep((cs.TWarm - 0.0065 * elev - 8) / 5);
    }

    public bool IsLand(double lat, double lon)
    {
        var t = Data.SampleTerrain(lat, lon);
        return !(t.Ocean > 0.5 || t.Lake > 0.5 || t.Ice > 30);
    }

    static int DescaleT(float t) => Math.Clamp((int)((t + 20f) * 4.25f), 0, 255);
    // vanilla Climate.GetFertility
    static int Fert(int rain, float t, float yRel)
    {
        float a = Math.Min(255f, rain / 2f + Math.Max(0f, rain * DescaleT(t) / 512f));
        float b = 1f - Math.Max(0f, (80f - a) / 80f);
        return (int)Math.Max(0f, a - Math.Max(0f, 50f * (yRel - 0.5f)) * b);
    }

    /// <summary>The attempt Earth hands the supplier at a place, for trees or shrubs.</summary>
    public TreeSite Site(RegionalTreeCatalog catalog, double lat, double lon, bool shrubs, bool blended = true, float[] factors = null, bool wet = false, bool shore = false)
    {
        double fp = ForestPotential(lat, lon, out var cs, out double elev);
        int y = (int)Vertical.Y(elev);
        int temp = Math.Clamp((int)Math.Round(cs.TMean - 0.0065 * elev), -20, 40);
        int rain = Math.Clamp((int)cs.Rain, 0, 255);
        float yRel = (y - 165f) / (384 - 165f);
        var f = factors ?? new float[RegionalTreePicker.Groups];
        // zone worlds (habitats): the zones' tree mixes, as EarthTrees does
        if (Zones != null) Zones.TreeFactors(ZoneDrivers.From(Data, lat, lon, 0.0065 * elev), f);
        else RegionalTreePicker.ForestTypeFactors(cs.TMean, cs.TCold, cs.TWarm, cs.DrySummer, cs.Rain, elev, f, catalog != null);
        var site = new TreeSite
        {
            Temp = temp, Rain = rain, Fert = Fert(rain, temp, yRel),
            Forest = shrubs ? (int)(Geo.SmoothStep((cs.Rain - 40) / 60) * 155) : (int)(fp * 187),
            HeightRel = y / 384f, Lat = lat, Lon = lon, Elevation = elev,
            Region = catalog == null ? -1 : blended ? catalog.Geography.FindBlended(lat, lon) : catalog.Geography.Find(lat, lon),
            ColdMonth = (float)(cs.TCold - 0.0065 * elev), WarmMonth = (float)(cs.TWarm - 0.0065 * elev), DrySummer = (float)cs.DrySummer,
            Wet = wet, Shore = shore, Factors = f,
        };
        // the soil, as EarthTrees gives it (set Soils to pick with the trees' soil fit)
        if (Soils != null)
        {
            SoilNiche.Fill(ref site, Soils, lat, lon, ZoneDrivers.From(cs, 0.0065 * elev).Aridity, wet);
            SoilNiche.AddWetlands(ref site, Wetlands, lat, lon);
        }
        return site;
    }

    /// <summary>Set to give sites their soil (<see cref="SoilSiteGrid"/>); null: every soil fits.</summary>
    public SoilSiteGrid Soils;
    /// <summary>With <see cref="Soils"/>: the mapped wetlands join the sites' soil, as in EarthTrees.</summary>
    public WetlandGrid Wetlands;

    /// <summary>Set (report --zones) to pick with the zones' tree mixes instead of the coded forest-type weights.</summary>
    public ClimateZones Zones;

    public struct GridCell { public double Lat, Lon, Area, Fp; }

    /// <summary>Forest-capable land (forest potential ≥ minFp) on a regular grid, area-weighted by cos(lat) × potential.</summary>
    public List<GridCell> ForestGrid(double step, double minFp = 0.3, double south = -56, double north = 75)
    {
        var cells = new List<GridCell>();
        for (double lat = south + step / 2; lat < north; lat += step)
        for (double lon = -180 + step / 2; lon < 180; lon += step)
        {
            if (!IsLand(lat, lon)) continue;
            double fp = ForestPotential(lat, lon, out _, out _);
            if (fp < minFp) continue;
            cells.Add(new GridCell { Lat = lat, Lon = lon, Fp = fp, Area = Math.Cos(lat * Math.PI / 180) * fp });
        }
        return cells;
    }

    /// <summary>Genus label of a generator: the genera of its rule's taxa ("Abies/Picea" for the generic fir).</summary>
    public static string Genus(RegionalTreeCatalog catalog, string generator)
    {
        var rule = catalog?.Find(generator);
        if (rule == null) return generator.Substring(generator.IndexOf(':') + 1);
        return string.Join("/", rule.Taxa.Select(t => t.Split(' ')[0]).Distinct().OrderBy(g => g, StringComparer.Ordinal));
    }

    public static string Pct(double v) => (100 * v).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>Deterministic xorshift random source for Monte-Carlo checks.</summary>
struct TestRandom : ITreeRandom
{
    ulong s;
    public TestRandom(ulong seed) { s = seed * 0x9E3779B97F4A7C15UL + 1; }
    public double NextDouble()
    {
        s ^= s << 13; s ^= s >> 7; s ^= s << 17;
        return (s >> 11) * (1.0 / (1UL << 53));
    }
}
