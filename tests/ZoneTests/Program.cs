using EarthMap.Core;

// Habitats step 1: vegetation zones from climate (zones.json, Core.ClimateZones), checked at named places on Earth's own
// climate, for gaps over all land, and against the forest potential it replaces.
// Read as new worlds read it: the aridity index with CHELSA's Penman-Monteith evapotranspiration (AridityRules 1).
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
bool verbose = args.Contains("--map");
int passed = 0, failed = 0;
void Check(bool ok, string msg) { if (ok) passed++; else { failed++; Console.Error.WriteLine("FAIL " + msg); } }

// the grassland-soil grid Earth ships (fire rules), rebuilt from SoilGrids' soil-group tiles of the ground pack
string soilTiles = Path.Combine(root, "assets/earth/config/ground/global-soilgrids.egts");
string soilSitePath = Path.Combine(root, "assets/earth/config/soil-site.bin");
if (args.Contains("--bake-soil-site"))
{
    var baked = SoilSiteGrid.Bake(File.ReadAllBytes(soilTiles));
    File.WriteAllBytes(soilSitePath, baked.Write());
    Console.WriteLine($"Wrote {soilSitePath}: {baked.Cols} x {baked.Rows} cells, {new FileInfo(soilSitePath).Length} bytes");
    return 0;
}
string zonesJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/zones.json"));
// as new worlds read it (EarthParams.AridityRules 1): the aridity index with CHELSA's Penman-Monteith evapotranspiration,
// and the zone limits fitted to it; zonesHoldridge are older worlds' limits
var zones = new ClimateZones(zonesJson, penman: true);
var zonesHoldridge = new ClimateZones(zonesJson);
var data = EarthData.LoadFromFolder(root, true);
data.SetClimateFix(true);
data.SetFineRain(true);
// as new worlds read it (climate rules 2, decision 0009): CHELSA temperatures on the 2' grid, real mountain climates
data.SetFineTemperature(true);
Check(data.FineTemperatures, "CHELSA temperatures load (earthdata/temperature.bin)");
data.SetPenmanAridity(true);
Check(data.PenmanAridity, "The Penman-Monteith factors load (earthdata/aridity.bin)");

// ---------------------------------------------------------------- the model itself
Check(Math.Abs(ZoneDrivers.GrowingDegreeDays(15, 15, 15, 5) - 3652.5) < 1e-6, "Constant 15 °C gives 10 x 365.25 degree days");
Check(ZoneDrivers.GrowingDegreeDays(-12, -30, 4, 5) == 0, "A year below 5 °C has none");
Check(ZoneDrivers.GrowingDegreeDays(6, -6, 18, 5) > ZoneDrivers.GrowingDegreeDays(6, 0, 12, 5), "A wider year above the base has more");
double[] w = new double[zones.Count];
var any = new ZoneDrivers { Gdd5 = 2000, TCold = -2, TWarm = 19, Aridity = .9, DrySummer = 0 };
zones.Weights(any, w);
double total = 0; foreach (double x in w) total += x;
Check(Math.Abs(total - 1) < 1e-9, "Weights sum to 1");

// ---------------------------------------------------------------- the aridity index (EarthParams.AridityRules, aridity.bin)
{
    var plain = new EarthData.ClimateSample { Rain = 120, DrySummer = .3 };
    Check(plain.Aridity == SoilFertility.Aridity(120, .3) && plain.AridityOf(80) == SoilFertility.Aridity(80, .3), "Without the Penman factor the index is the rainfall's (older worlds)");
    double Factor(double lat, double lon) => data.SampleClimate(lat, lon).PenmanFactor;
    double paris = Factor(48.86, 2.35), lincoln = Factor(40.81, -96.7), kherson = Factor(46.64, 32.6), yakutsk = Factor(62.03, 129.73);
    Console.WriteLine($"Penman factors: Paris {paris:F2}, Lincoln {lincoln:F2}, Kherson {kherson:F2}, Yakutsk {yakutsk:F2}");
    Check(Math.Abs(paris - 1) < .06 && lincoln < .8 && kherson > .75 && kherson < .9 && yakutsk > 1,
        "Holdridge's evapotranspiration runs low on the sunny, windy Plains and steppe and high in the cold (Paris about 1, Lincoln under 0.8, Kherson 0.75-0.9, Yakutsk over 1)");
    var pontic = new ZoneDrivers { Gdd5 = 2500, TCold = -3, TWarm = 23, TMean = 10, Aridity = .5, DrySummer = 0, DryMonths = double.NaN };
    Check(zonesHoldridge.Zones[zonesHoldridge.Dominant(pontic)].Code == "foreststeppe" && zones.Zones[zones.Dominant(pontic)].Code == "steppe",
        "Aridity 0.5 in a Pontic climate: forest-steppe on older worlds' limits, steppe on the Penman ones");
}

ZoneDrivers At(double lat, double lon, double elev = double.NaN)
{
    var c = data.SampleClimate(lat, lon);
    if (double.IsNaN(elev)) elev = Math.Max(0, data.ElevBilinear(lat, lon));
    return ZoneDrivers.From(data, lat, lon, .0065 * elev);
}
string Name(int i) => zones.Zones[i].Code;

// ---------------------------------------------------------------- named places: the dominant zone is one of the expected
var sites = new (string name, double lat, double lon, double elev, string expect)[]
{
    ("Utqiagvik", 71.29, -156.79, double.NaN, "tundra"), ("Greenland Summit", 72.58, -38.46, 3216, "ice"), ("Churchill", 58.77, -94.17, double.NaN, "tundra foresttundra"),
    ("Arkhangelsk", 64.54, 40.54, double.NaN, "boreal"), ("Fairbanks", 64.84, -147.72, double.NaN, "boreal"), ("Yakutsk", 62.03, 129.73, double.NaN, "boreal steppe foreststeppe"),
    ("Helsinki", 60.17, 24.94, double.NaN, "hemiboreal boreal"), ("Riga", 56.95, 24.1, double.NaN, "hemiboreal"), ("Moscow", 55.75, 37.62, double.NaN, "hemiboreal"),
    ("Berlin", 52.52, 13.4, double.NaN, "temperate hemiboreal"), ("Paris", 48.86, 2.35, double.NaN, "temperate oceanic"), ("Dublin", 53.35, -6.26, double.NaN, "oceanic"),
    ("Bergen", 60.39, 5.32, double.NaN, "oceanic"), ("Chicago", 41.88, -87.63, double.NaN, "temperate temperatecontinental"), ("Kyiv", 50.45, 30.52, double.NaN, "foreststeppe temperate temperatecontinental hemiboreal"),
    ("Budapest", 47.5, 19.04, double.NaN, "foreststeppe temperate"), ("Kazakh steppe", 50.3, 68.0, double.NaN, "steppe"), ("Ulaanbaatar", 47.92, 106.92, double.NaN, "steppe"),
    ("Odesa steppe", 46.8, 31.0, double.NaN, "steppe"), ("Kherson", 46.64, 32.6, double.NaN, "steppe"), ("North Crimea", 45.80, 33.81, double.NaN, "steppe"),
    ("Dobruja", 44.2, 28.3, double.NaN, "steppe"), ("Volgograd", 48.7, 44.5, double.NaN, "steppe"), ("Lhasa", 29.65, 91.1, 3650, "steppe"), ("Great Plains W", 40.5, -102.0, double.NaN, "steppe"),
    ("Chisinau", 47.0, 28.86, double.NaN, "foreststeppe temperate"), ("Vienna", 48.2, 16.37, double.NaN, "temperate"), ("Belgrade", 44.8, 20.46, double.NaN, "temperate foreststeppe"), ("Ankara", 39.93, 32.86, double.NaN, "steppe foreststeppe submediterranean"), ("Denver", 39.74, -104.99, double.NaN, "steppe foreststeppe"),
    ("Madrid", 40.42, -3.70, double.NaN, "mediterranean"), ("Seville", 37.39, -5.98, double.NaN, "mediterranean"), ("Athens", 37.98, 23.73, double.NaN, "mediterranean"),
    ("Rome", 41.9, 12.5, double.NaN, "mediterranean"), ("Los Angeles", 34.05, -118.24, double.NaN, "mediterranean"), ("Perth", -31.95, 115.86, double.NaN, "mediterranean"),
    ("Cape Town", -33.92, 18.42, double.NaN, "mediterranean"), ("Santiago", -33.45, -70.67, double.NaN, "mediterranean"),
    ("Tucson", 32.22, -110.97, double.NaN, "subtropicalscrub semidesert"), ("Alice Springs", -23.7, 133.88, double.NaN, "subtropicalscrub semidesert savanna"),
    ("Phoenix", 33.45, -112.07, double.NaN, "semidesert"), ("Hoggar", 23.3, 5.8, 1400, "semidesert desert"), ("Western Desert", 25.0, 28.0, double.NaN, "desert"),
    ("Rub al Khali", 20.0, 50.0, double.NaN, "desert"), ("Atacama", -23.5, -69.5, double.NaN, "desert"), ("Tehran", 35.69, 51.39, double.NaN, "steppe semidesert mediterranean"),
    ("Kano", 12.0, 8.52, double.NaN, "savanna tropicaldry"), ("Bamako", 12.64, -8.0, double.NaN, "savanna tropicaldry"), ("Dodoma", -6.16, 35.75, double.NaN, "savanna"),
    ("Serengeti", -2.33, 34.83, double.NaN, "savanna"), ("Darwin", -12.46, 130.84, double.NaN, "savanna tropicaldry"), ("Cuiaba", -15.6, -56.1, double.NaN, "savanna tropicaldry"),
    ("Nagpur", 21.15, 79.09, double.NaN, "tropicaldry savanna"), ("Manaus", -3.12, -60.02, double.NaN, "rainforest"), ("Kisangani", 0.52, 25.2, double.NaN, "rainforest"),
    ("Pontianak", -0.03, 109.34, double.NaN, "rainforest"), ("Atlanta", 33.75, -84.39, double.NaN, "warmhumid"), ("Shanghai", 31.23, 121.47, double.NaN, "warmhumid"),
    ("Tokyo", 35.68, 139.69, double.NaN, "warmhumid temperate"), ("Everest", 27.99, 86.93, 8849, "ice"), ("Jungfraujoch", 46.55, 7.98, 3450, "ice tundra"),
    ("Davos", 46.8, 9.83, 1560, "montaneconifer hemiboreal boreal temperate"),
    // beech's eastern limit: broadleaf west of it, hemiboreal east (Earth's coldest month about -2.5 °C)
    ("Germany C", 51.0, 10.0, double.NaN, "temperate"), ("Denmark", 56.0, 9.5, double.NaN, "temperate oceanic"), ("Latgale", 56.3, 27.5, double.NaN, "hemiboreal"),
    ("Belarus", 53.9, 27.6, double.NaN, "hemiboreal"), ("Black Forest", 47.9, 8.1, 1000, "montaneconifer"), ("Caucasus", 43.0, 42.5, 1800, "montaneconifer"), ("Appalachian crest", 35.6, -83.5, 1500, "montaneconifer"), ("Mexico City", 19.43, -99.13, 2240, "warmhumid temperate tropicaldry subtropicalscrub"),
};
foreach (var (name, lat, lon, elev, expect) in sites)
{
    var d = At(lat, lon, elev);
    zones.Weights(d, w);
    int dom = zones.Dominant(d);
    zones.Density(d, out double forest, out double shrub);
    // the forest potential zones replace (TerrainModel.ForestPotential without zones, map-elevation worlds)
    var cs = data.SampleClimate(lat, lon);
    double oldForest = Geo.SmoothStep((cs.Rain - 60) / 80) * Geo.SmoothStep((d.TWarm - 8) / 5);
    var top = Enumerable.Range(0, zones.Count).OrderByDescending(i => w[i]).Take(2).Select(i => $"{Name(i)} {w[i]:P0}");
    Console.WriteLine($"{name,-16} dryM {d.DryMonths,4:F1} gdd5 {d.Gdd5,5:F0} cold {d.TCold,6:F1} warm {d.TWarm,5:F1} ai {d.Aridity:F2} dry {d.DrySummer:F2} -> {string.Join(", ", top),-36} forest {forest:F2} (was {oldForest:F2}) shrub {shrub:F2}");
    // "?": Earth's climate data, not the zones, is off here (reported in the ranges README); a warning, not a failure
    if (expect.StartsWith("?")) { if (!expect[1..].Split(' ').Contains(Name(dom))) Console.WriteLine($"WARN {name} is {expect[1..]} in reality; Earth's climate gives {Name(dom)} (aridity {d.Aridity:F2})"); }
    else Check(expect.Split(' ').Contains(Name(dom)), $"{name} is {expect} (got {Name(dom)})");
}

// ---------------------------------------------------------------- tree mixes follow the zones
// Latvia and Finland are the oracle the pre-zone tables were tuned on (pine-led, then birch, then spruce): the zone
// mixes must keep them.
float[] Mix(double lat, double lon, double elev = double.NaN) { var f = new float[RegionalTreePicker.Groups]; zones.TreeFactors(At(lat, lon, elev), f); return f; }
int G(string g) => RegionalTreePicker.GroupIndex(g);
foreach (var (name, lat, lon) in new[] { ("Riga", 56.95, 24.1), ("Helsinki", 60.17, 24.94), ("Arkhangelsk", 64.54, 40.54) })
{
    var f = Mix(lat, lon);
    Console.WriteLine($"Trees {name}: pine {f[G("pine")]:F2} birch {f[G("birch")]:F2} fir/spruce {f[G("fir")]:F2} oak {f[G("oak")]:F2}");
    Check(f[G("pine")] > f[G("birch")] && f[G("birch")] > f[G("fir")] * 0.9 && f[G("pine")] > f[G("oak")] * 1.5, $"{name} trees stay pine-led with birch and spruce");
}
foreach (var (name, lat, lon, elev) in new[] { ("Black Forest", 47.9, 8.1, 1000.0), ("Alps", 46.8, 9.83, 1500.0), ("Caucasus", 43.0, 42.5, 1800.0) })
{
    var f = Mix(lat, lon, elev);
    Check(f[G("fir")] > f[G("pine")] && f[G("fir")] > f[G("oak")], $"{name} at {elev} m is spruce-fir led (fir {f[G("fir")]:F2}, pine {f[G("pine")]:F2})");
}
foreach (var (name, lat, lon) in new[] { ("Paris", 48.86, 2.35), ("Chicago", 41.88, -87.63), ("Atlanta", 33.75, -84.39) })
{
    var f = Mix(lat, lon);
    Check(f[G("oak")] > 3 * f[G("pine")], $"{name} is broadleaf (oak {f[G("oak")]:F2}, pine {f[G("pine")]:F2})");
}
foreach (var (name, lat, lon) in new[] { ("Athens", 37.98, 23.73), ("Madrid", 40.42, -3.70) })
{
    var f = Mix(lat, lon);
    Check(f[G("pine")] > 1.5 && f[G("oak")] > 3 && f[G("birch")] < .5, $"{name} is pine and oak (Mediterranean)");
}
foreach (var (name, lat, lon) in new[] { ("Kano", 12.0, 8.52), ("Alice Springs", -23.7, 133.88), ("Phoenix", 33.45, -112.07) })
{
    var f = Mix(lat, lon);
    Check(f[G("arid")] > 3 && f[G("oak")] < .5, $"{name} is thorn and dry trees (arid {f[G("arid")]:F2})");
}
{
    var f = Mix(-3.12, -60.02);
    Check(Math.Abs(f[G("oak")] - 1) < .02 && f[G("arid")] < .5, "Rainforest keeps its native species' own weights, no thorn trees");
}

// ---------------------------------------------------------------- habitats step 7: animals by forest and open land
// Vanilla's animals already gate on the forest and shrub density at the spawn position (ClimateSpawnCondition MinForest,
// MaxForest, MinForestOrShrubs; e.g. wolf minForest 0.5, gazelle maxForest 0.1, moose minForestOrShrubs 0.35), and in
// zone worlds those densities come from the zones (TerrainModel.ForestPotential/ShrubPotential away from rivers and
// volcanic ground). Earth scales vanilla's noise by the potential (EarthWorldGen: forest p * (110 + 0.6 noise) / 255,
// shrubs s * (40 + 0.9 noise) / 255), so a zone admits an animal where some noise value meets its gate.
(double lo, double hi) ForestRange(double p) => (p * 110 / 255, Math.Min(1, p * 263 / 255));
(double lo, double hi) ShrubRange(double s) => (s * 40 / 255, Math.Min(1, s * 269.5 / 255));
bool Wolf(double f, double s) => ForestRange(f).hi >= 0.5;
bool Gazelle(double f, double s) => ForestRange(f).lo <= 0.1;
bool Moose(double f, double s) => Math.Max(ForestRange(f).hi, ShrubRange(s).hi) >= 0.35 && ForestRange(f).lo <= 0.95;
foreach (var (name, lat, lon, elev, wolf, gazelle, moose) in new[] {
    ("Arkhangelsk", 64.54, 40.54, double.NaN, true, false, true), ("Riga", 56.95, 24.1, double.NaN, true, false, true),
    ("Black Forest", 47.9, 8.1, 1000.0, true, false, true), ("Kazakh steppe", 50.3, 68.0, double.NaN, false, true, false), ("Mongolian steppe", 47.92, 106.92, double.NaN, false, true, false),
    ("Riyadh", 24.71, 46.68, double.NaN, false, true, false), ("Central Sahara", 23.0, 12.0, double.NaN, false, true, false) })
{
    zones.Density(At(lat, lon, elev), out double f, out double s);
    Console.WriteLine($"Animals {name}: forest {ForestRange(f).lo:F2}..{ForestRange(f).hi:F2}, shrubs {ShrubRange(s).lo:F2}..{ShrubRange(s).hi:F2}; wolf {Wolf(f, s)}, gazelle {Gazelle(f, s)}, moose {Moose(f, s)}");
    Check(Wolf(f, s) == wolf && Gazelle(f, s) == gazelle && Moose(f, s) == moose, $"{name}: vanilla's forest gates admit wolf {wolf}, gazelle {gazelle}, moose {moose}");
}

// ---------------------------------------------------------------- arable land: where the catalogue's wild and feral crops grow
foreach (var (name, lat, lon, elev, arable) in new[] { ("Hoggar", 23.3, 5.8, 1400.0, false), ("Western Desert", 25.0, 28.0, double.NaN, false), ("Greenland", 72.58, -38.46, 3216.0, false),
    ("Paris", 48.86, 2.35, double.NaN, true), ("Seville", 37.39, -5.98, double.NaN, true), ("Kano", 12.0, 8.52, double.NaN, true), ("Nile Delta (climate alone; the river belt makes it arable)", 30.8, 31.0, double.NaN, false), ("Tunis", 36.8, 10.18, double.NaN, true) })
{
    double share = zones.ArableShare(At(lat, lon, elev));
    Check(arable ? share >= .5 : share < .5, $"{name} is {(arable ? "" : "not ")}arable (arable zones {share:P0})");
}

// ---------------------------------------------------------------- 5b: grass colour through the wet and dry seasons
Check(Math.Abs(SoilFertility.RainFromAridity(SoilFertility.Aridity(130, 0)) - 130) < 1e-6 && Math.Abs(SoilFertility.RainFromAridity(SoilFertility.Aridity(42, 0)) - 42) < 1e-6,
    "Rainfall from aridity inverts the aridity of the rainfall");
Check(GrassSeason.SeasonalRain(120, 0) == 120, "No anomaly keeps the yearly rainfall");
var grassCurve = new float[GrassSeason.Steps];
int GrassRain(double lat, double lon, int month)
{
    GrassSeason.Curve(data.Weather, lat, lon, grassCurve);
    int yearly = (int)Math.Round(data.SampleClimate(lat, lon).Rain);
    return GrassSeason.SeasonalRain(yearly, GrassSeason.At(grassCurve, (month - 0.5) / 12));
}
foreach (var (name, lat, lon, wet, dry) in new[] {
    ("Seville", 37.39, -5.98, 1, 7), ("Athens", 37.98, 23.73, 1, 7), ("Perth", -31.95, 115.86, 7, 1), ("Kano", 12.0, 8.52, 8, 3),
    ("Nagpur", 21.15, 79.09, 8, 4), ("Darwin", -12.46, 130.84, 2, 8), ("Bamako", 12.64, -8.0, 8, 3), ("Phoenix", 33.45, -112.07, 8, 5) })
{
    GrassSeason.Curve(data.Weather, lat, lon, grassCurve);
    double mean = grassCurve.Average();
    int yearly = (int)Math.Round(data.SampleClimate(lat, lon).Rain);
    string months = string.Join(" ", Enumerable.Range(1, 12).Select(m => GrassRain(lat, lon, m).ToString().PadLeft(3)));
    Console.WriteLine($"Grass rain {name,-8} yearly {yearly,3}: {months}");
    Check(Math.Abs(mean) < .3, $"{name}'s curve averages about 0 (exactly 0 before the swing is capped)");
    Check(GrassRain(lat, lon, wet) >= GrassRain(lat, lon, dry) + 30, $"{name} greener in month {wet} than in {dry}");
}
{
    string months = string.Join(" ", Enumerable.Range(1, 12).Select(m => GrassRain(56.95, 24.1, m).ToString().PadLeft(3)));
    Console.WriteLine($"Grass rain Riga     yearly {(int)Math.Round(data.SampleClimate(56.95, 24.1).Rain),3}: {months}");
    int y = (int)Math.Round(data.SampleClimate(56.95, 24.1).Rain);
    Check(Enumerable.Range(1, 12).Where(m => m != 12 && m != 1).All(m => Math.Abs(GrassRain(56.95, 24.1, m) - y) <= 30), "Riga stays green all year (December and January aside: the weather data's December gap)");
}

// ---------------------------------------------------------------- no gaps: every land cell is clearly inside some zone
int land = 0, weak = 0;
double aridLand = 0, aridPenman = 0, aridHoldridge = 0;
var area = new double[zones.Count];
var weakCells = new List<string>();
for (double lat = -89.75; lat < 90; lat += .5)
    for (double lon = -179.75; lon < 180; lon += .5)
    {
        if (data.SampleTerrain(lat, lon).Ocean > .5) continue;
        land++;
        var d = At(lat, lon);
        if (lat > -56 && lat < 84)
        {
            var cs = data.SampleClimate(lat, lon);
            double cw = Math.Cos(lat * Math.PI / 180);
            aridLand += cw;
            if (cs.Aridity < .2) aridPenman += cw;
            if (SoilFertility.Aridity(cs.Rain, cs.DrySummer) < .2) aridHoldridge += cw;
        }
        // the strongest zone's raw (unnormalised) weight: below 0.05 the cell falls between envelopes
        int dom = zones.Dominant(d);
        area[dom] += Math.Cos(lat * Math.PI / 180);
        if (zones.RawStrongest(d) < .05) { weak++; if (weakCells.Count < (verbose ? 1000 : 25)) weakCells.Add($"{lat:F2},{lon:F2} {Name(dom)} dryM {d.DryMonths:F1} gdd {d.Gdd5:F0} cold {d.TCold:F1} warm {d.TWarm:F1} ai {d.Aridity:F2} dry {d.DrySummer:F2}"); }
    }
double areaSum = area.Sum();
Console.WriteLine($"Land cells {land}; between envelopes (strongest raw weight < 0.05): {weak} ({(double)weak / land:P1})");
foreach (string c in weakCells) Console.WriteLine("  weak " + c);
Console.WriteLine("Land area by dominant zone: " + string.Join(", ", Enumerable.Range(0, zones.Count).OrderByDescending(i => area[i]).Select(i => $"{Name(i)} {area[i] / areaSum:P1}")));
Check((double)weak / land < .01, "Under 1 % of land falls between the zones' envelopes");
// UNEP's World Atlas of Desertification (Middleton and Thomas 1997): 7.5 % of land hyper-arid and 12.1 % arid
double sharePenman = aridPenman / aridLand, shareHoldridge = aridHoldridge / aridLand;
Console.WriteLine($"Land with an aridity index under 0.2 (56 S to 84 N): Penman-Monteith {sharePenman:P1}, Holdridge {shareHoldridge:P1}, UNEP 19.6 %");
Check(Math.Abs(sharePenman - .196) < .03 && Math.Abs(sharePenman - .196) < Math.Abs(shareHoldridge - .196), "The Penman-Monteith index finds about as much arid land as UNEP, more nearly than Holdridge's");

// ---------------------------------------------------------------- fire rules: grassland kept open (Core.FireRegime)
var soils = SoilSiteGrid.Read(File.ReadAllBytes(soilSitePath));
if (File.Exists(soilTiles))
{
    var again = SoilSiteGrid.Bake(File.ReadAllBytes(soilTiles));
    bool same = again.Cols == soils.Cols && again.Rows == soils.Rows;
    for (int l = 0; l < SoilSiteGrid.LayerNames.Length; l++) for (int r = 0; same && r < soils.Rows; r++) for (int c = 0; same && c < soils.Cols; c++) same = again.Cell(l, r, c) == soils.Cell(l, r, c);
    Check(same, "soil-site.bin is the bake of the ground pack's SoilGrids tiles (run ZoneTests --bake-soil-site after changing them)");
}
var fire = new FireRegime(soils);
var fire1 = new FireRegime(soils, 1);
Check(fire.Rules == FireRegime.RulesVersion && fire1.Rules == 1 && new FireRegime(soils, 99).Rules == FireRegime.RulesVersion, "The fire rules version is kept and capped");
Check(FireRegime.ForestCutFor(.99, 1) == FireRegime.ForestCut && FireRegime.ForestCutFor(.5, 1) == FireRegime.ForestCut, "Fire rules 1: open land keeps a fifth of the forest at any chance");
Check(FireRegime.ForestCutFor(FireRegime.CoreFrom, 2) == FireRegime.ForestCut && FireRegime.ForestCutFor(.7, 2) == FireRegime.ForestCut
    && Math.Abs(FireRegime.ForestCutFor(FireRegime.CoreAt, 2) - FireRegime.CoreForestCut) < 1e-12 && FireRegime.ForestCutFor(.999, 2) == FireRegime.CoreForestCut,
    "Fire rules 2: a fifth kept up to the chance 0.9, a fiftieth from 0.98");
bool cutRises = true;
for (double c = .5; c < 1; c += .005) cutRises &= FireRegime.ForestCutFor(c + .005, 2) >= FireRegime.ForestCutFor(c, 2);
Check(cutRises, "Fire rules 2: the surer the grassland, the more forest it loses");
Check(FireRegime.Chance(1, 24, 90, 1.2) > FireRegime.Chance(0, 24, 90, 1.2) && FireRegime.Chance(.8, 25, 90, 1.2) > FireRegime.Chance(.8, 20, 90, 1.2)
    && FireRegime.Chance(.8, 24, 60, 1.2) > FireRegime.Chance(.8, 24, 120, 1.2) && FireRegime.Chance(.8, 24, 90, .9) > FireRegime.Chance(.8, 24, 90, 1.4),
    "Fire keeps land open more on grassland soil, in hotter and drier summers and drier climates");
Check(FireRegime.Openness(1, 30, 20, .5, 16) == 0 && FireRegime.Openness(1, 30, 20, .5, 5) > .99, "The tropics are left to the savanna zones");
Check(FireRegime.Openness(0, 30, 20, .5, 0) <= FireRegime.WithoutSoil + 1e-9, "Without grassland soil fire only thins the forest");
(double before, double after, double soil) Fire(double lat, double lon)
{
    var d = At(lat, lon);
    zones.Density(d, out double f, out _);
    double elev = Math.Max(0, data.ElevBilinear(lat, lon));
    return (f, f * fire.ForestKeptAt(data, lat, lon, d, .0065 * elev), fire.GrassSoilShare(lat, lon));
}
// fire rules 1 as they were: open land keeps a fifth of the forest
bool v1Same = true;
foreach (var (lat, lon) in new[] { (41.59, -93.6), (50.0, 36.23), (45.80, 33.81), (-33.89, -60.57), (48.85, 2.35), (49.9, -97.14) })
{
    var d = At(lat, lon);
    double lapse = .0065 * Math.Max(0, data.ElevBilinear(lat, lon));
    v1Same &= Math.Abs(fire1.ForestKeptAt(data, lat, lon, d, lapse) - (1 - FireRegime.ForestCut * fire1.OpennessAt(data, lat, lon, d, lapse))) < 1e-12;
}
Check(v1Same, "Fire rules 1 keep a fifth of the zones' forest on open land, as before");
// open in 1318: tallgrass prairie, aspen parkland, Pampas, forest-steppe and steppe edge
foreach (var (name, lat, lon, most) in new (string, double, double, double)[]
{
    ("Des Moines", 41.59, -93.6, .35), ("Ames", 42.03, -93.62, .35), ("Omaha", 41.26, -95.94, .35), ("Lincoln", 40.8, -96.7, .35), ("Manhattan KS", 39.18, -96.57, .35),
    ("Fargo", 46.9, -96.8, .35), ("Winnipeg", 49.9, -97.14, .4), ("Saskatoon", 52.13, -106.67, .35), ("Brandon", 49.85, -99.95, .35), ("Pergamino", -33.89, -60.57, .35),
    ("Kharkiv", 50.0, 36.23, .4), ("Voronezh", 51.67, 39.2, .45), ("Omsk", 54.98, 73.37, .45),
})
{
    var (b, a, s) = Fire(lat, lon);
    Console.WriteLine($"Fire  {name,-14} grassland soil {s:F2}: forest {b:F2} -> {a:F2}");
    Check(a <= most, $"{name} is kept open by fire (forest {b:F2} -> {a:F2}, at most {most:F2})");
}
// forest in 1318: the forests east of the prairie, Europe's and East Asia's broadleaf forests, the boreal forest
foreach (var (name, lat, lon) in new (string, double, double)[]
{
    ("Columbus", 39.96, -83.0), ("Detroit", 42.33, -83.05), ("Toronto", 43.65, -79.38), ("Pittsburgh", 40.44, -80.0), ("Nashville", 36.16, -86.78),
    ("Paris", 48.85, 2.35), ("Kassel", 51.31, 9.48), ("Riga", 56.95, 24.1), ("Moscow", 55.75, 37.62), ("Tokyo", 35.68, 139.7), ("Thunder Bay", 48.38, -89.25),
})
{
    var (b, a, s) = Fire(lat, lon);
    Console.WriteLine($"Fire  {name,-14} grassland soil {s:F2}: forest {b:F2} -> {a:F2}");
    Check(b - a <= .05, $"{name} keeps its forest (forest {b:F2} -> {a:F2})");
}
// ---------------------------------------------------------------- trees per chunk (zone rules 2 and fire rules 2, north of Crimea)
// Vanilla's genTrees: about 70 tries a chunk (taken as 0.75 for the rain it reads in dry land), each kept with max(0.0025, f²);
// Earth's forest map is the potential times (110 + 0.6 vanilla noise), the noise taken evenly over 0..255. Zone rules 2 keep
// a try under the floor with ClimateZones.TreeKeep, so the floor's trees go.
Check(ClimateZones.TreeKeep(255) == 1 && ClimateZones.TreeKeep(13) == 1 && ClimateZones.TreeKeep(12) < 1 && ClimateZones.TreeKeep(0) < .01, "Zone rules 2: only tries under vanilla's floor are thinned");
bool keepRises = true;
for (int k = 0; k < 255; k++) keepRises &= ClimateZones.TreeKeep(k + 1) >= ClimateZones.TreeKeep(k);
Check(keepRises, "Zone rules 2: a denser forest map never keeps fewer trees");
double PerChunk(double potential, bool floor)
{
    double sum = 0; int n = 0;
    for (int v = 0; v <= 255; v += 5, n++)
    {
        int forest = (int)Math.Clamp(potential * (110 + .6 * v), 0, 255);
        double f = Math.Clamp(potential * (110 + .6 * v), 0, 255) / 255;
        sum += Math.Max(ClimateZones.VanillaTreeFloor, f * f) * (floor ? 1 : ClimateZones.TreeKeep(forest));
    }
    return 70 * .75 * sum / n;
}
(double v1, double v2) Trees(double lat, double lon)
{
    var d = At(lat, lon);
    zones.Density(d, out double f, out _);
    double lapse = .0065 * Math.Max(0, data.ElevBilinear(lat, lon));
    return (PerChunk(f * fire1.ForestKeptAt(data, lat, lon, d, lapse), true), PerChunk(f * fire.ForestKeptAt(data, lat, lon, d, lapse), false));
}
// steppe, Pampas and tallgrass prairie in 1318: no trees outside the river belts (the belts are added on top of this). The
// steppe at most a tree in 100 chunks; the Pampas and the prairie keep a fiftieth of a larger zone forest (0.9), so at most
// one in 50
foreach (var (name, lat, lon, most) in new (string, double, double, double)[]
{
    ("north Crimea", 45.80, 33.81, .01), ("Kherson", 46.6, 32.6, .01), ("Odesa", 46.48, 30.7, .01), ("Zaporizhzhia", 47.5, 35.0, .01), ("Rostov", 47.2, 39.7, .01),
    ("Kazakh steppe", 50.3, 68.0, .01), ("Lincoln", 40.8, -96.7, .02), ("Pergamino", -33.89, -60.57, .02),
})
{
    var (a, b2) = Trees(lat, lon);
    Console.WriteLine($"Trees {name,-14} per chunk: rules 1 {a:F3} (one in {1 / a:F0} chunks), rules 2 {b2:F4} (one in {1 / b2:F0})");
    Check(b2 <= most, $"{name}: open grassland keeps at most a tree in {1 / most:F0} chunks (rules 2: {b2:F4} a chunk, rules 1 {a:F3})");
}
// groves: the forest-steppe, aspen parkland and the prairie's oak edge keep theirs
foreach (var (name, lat, lon) in new (string, double, double)[] { ("Kursk", 51.73, 36.19), ("Winnipeg", 49.9, -97.14), ("Des Moines", 41.59, -93.6) })
{
    var (a, b2) = Trees(lat, lon);
    Console.WriteLine($"Trees {name,-14} per chunk: rules 1 {a:F3}, rules 2 {b2:F3}");
    Check(b2 >= .3 && Math.Abs(b2 - a) <= .05 * a, $"{name}: groves stay (rules 2: {b2:F3} trees a chunk, rules 1 {a:F3})");
}
// a forest is untouched
foreach (var (name, lat, lon) in new (string, double, double)[] { ("Kassel", 51.31, 9.48), ("Riga", 56.95, 24.1), ("Columbus", 39.96, -83.0) })
{
    var (a, b2) = Trees(lat, lon);
    Check(Math.Abs(b2 - a) <= 1e-9, $"{name}: forest trees unchanged ({a:F2} a chunk)");
}

Console.WriteLine($"ZoneTests: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
