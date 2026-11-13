using System.Diagnostics;
using System.Globalization;
using System.Text;
using EarthMap;
using EarthMap.Core;
using FloodplainBake;

// Arid floodplains (decision 0048, soil rules 3): assets/earth/config/floodplains.bin.
//   FloodplainBake <earth mod zip> <Earth data folder> <GLO-90 store> <glo90-tiles.bin> <output .bin> [key=value ...]
// Builds the terrain model as a new 1:10 GLO-90 world builds it (the mod zip, then the data store in the Earth data folder,
// VintagestoryData/ModData/earth, whose data/ holds release ed-v1 (decision 0066), as the game loads them;
// the GLO-90 store with its dams, lake and river outlines), floods the land from every river course of 10 m³/s or more
// with at least 10 m³/s from moist catchments (AridHydrology.WetSourceFlow) where it runs through dry land (aridity
// index below seedai at either end of a piece, on Holdridge's evapotranspiration or on CHELSA's Penman-Monteith from
// earthdata/aridity.bin, so worlds with and without aridity rules are covered; the mod zip must hold aridity.bin, which
// tall.22 does not: add it to a copy), and writes the flood's level on FloodplainGrid's 15" grid (never over an
// existing file). Keys: fall (m per km, 0.15), a and b (flood height a·Q^b, 1.2 and 0.3), hmin and hmax (1.5 and 15 m),
// depth (the flood covers ground down to this many flood heights below the river's water, 1), seedai (0.3),
// box=latMin,latMax,lonMin,lonMax (repeatable: only courses in the boxes, for tuning).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var ci = CultureInfo.InvariantCulture;
if (args.Length < 5)
{
    Console.WriteLine("usage: FloodplainBake <earth mod zip> <Earth data folder (ModData/earth)> <GLO-90 store> <glo90-tiles.bin> <output .bin> [key=value ...]");
    return 2;
}
string mainZip = args[0], earthFolder = args[1], store = args[2], tileList = args[3], output = args[4];
if (File.Exists(output)) { Console.WriteLine($"{output} exists; not overwritten"); return 2; }
var total = Stopwatch.StartNew();
void Log(string s) => Console.WriteLine($"[{total.Elapsed.TotalSeconds,6:F0}s] {s}");
var bake = new FloodBake(null);
double seedAridity = .3, minQ = 10;
int groundReach = 2;
var boxes = new List<double[]>();
var settings = new Dictionary<string, string>();
foreach (var a in args.Skip(5))
{
    var kv = a.Split('=', 2);
    if (kv.Length != 2) { Console.WriteLine("bad argument " + a); return 2; }
    double D() => double.Parse(kv[1], ci);
    switch (kv[0].ToLowerInvariant())
    {
        case "fall": bake.FallPerKm = D(); break;
        case "a": bake.HeightA = D(); break;
        case "b": bake.HeightB = D(); break;
        case "hmin": bake.HeightMin = D(); break;
        case "hmax": bake.HeightMax = D(); break;
        case "depth": bake.DepthFactor = D(); break;
        case "seedai": seedAridity = D(); break;
        case "groundreach": groundReach = int.Parse(kv[1], ci); break;
        case "box": boxes.Add(kv[1].Split(',').Select(v => double.Parse(v, ci)).ToArray()); break;
        default: Console.WriteLine("unknown key " + kv[0]); return 2;
    }
    settings[kv[0]] = kv[1];
}

// ---- the terrain model of a new 1:10 GLO-90 world ----
var data = EarthData.Load(name => EarthData.OpenDataFile(mainZip, name) ?? EarthDataStore.Open(earthFolder, name), true);
data.SetPenmanAridity(true); // the check; BuildModel sets it again from p.AridityRules
if (!data.PenmanAridity) { Console.WriteLine("the Penman-Monteith index is missing (" + data.PenmanError + "); add earthdata/aridity.bin to the mod zip"); return 1; }
int W = 5009920, L = W * 2 / 5;
var geo = EarthLayout.Build(new LayoutInput { WorldWidth = W, Gutter = false, CwRingStart = W / 10, CwRingLength = W * 4 / 5, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwBandInner = 608 }, data, out _);
int sea = 110;
var vs = VerticalScale.ForWorld(1024, sea);
var p = new EarthParams
{
    Version = EarthParams.CurrentVersion, Enabled = true,
    RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
    EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
    Cylinder = true, GutterBridges = true, GutterSeabed = TerrainModel.RealSeamVersion,
    RiverFalls = RiverFalls.Version, RiverChannels = SeaChannels.Version, RiverEnds = RiverEnds.Version, RiverDeltas = RiverDeltas.Version,
    RiverWidths = RiverNetwork.WidthRulesVersion, RiverPoolLayout = RiverNetwork.PoolLayoutVersion,
    MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = ShoreData.FileVersion, ClimateRules = TerrainModel.RealLapseRules,
    LinearLand = vs.LinearLand, LocalAlpineEcology = true,
    AridRiverRules = AridHydrology.Version, StraitRules = NavigableStraits.Version, ReservoirRules = 1,
    LakeOutlets = data.Outlets != null ? OutletData.FileVersion : 0, SeaReferee = data.SeaRefereeAvailable,
    AlpineSurfaceRules = MountainEcology.SurfaceVersion, SoilRules = SoilFertility.RulesVersion,
    ClimateData = data.ClimateFixAvailable, Canals = "",
    Islands = IslandLayer.RulesVersion, Coasts = CoastData.FileVersion, MinIslandBlocks = 4,
    MapHeight = 1024, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
    GutterSeabedY = sea - 1 - (int)vs.MaxDepth, NoiseSeed = 12345, DetailScale = 1.0,
    Rivers = true, RiverMinDischarge = 100, RiverWidthScale = 1.5, StreamMode = "all", StreamMinDischarge = 20,
    RiverStraighten = 0.5, RiverSmoothWidths = 2, RiverRelief = 1.0,
    FineElevation = FineElevation.Version, CoarseElevation = data.Coarse != null ? CoarseElevation.Version : 0,
    RiverCourses = data.Glo90RiverCourses,
};
p.LakeOutlines = LakeOutlineData.FileVersion;
p.AridityRules = EarthData.PenmanAridityVersion; // as a new world with aridity.bin: SampleClimate keeps the factor
p.DamRules = p.RiverCourses == DamRemovalData.CourseRules ? DamRemovalData.RulesVersion : 0;
p.RiverOutlines = p.DamRules >= 1 ? RiverOutlineData.FileVersion : 1;
p.DetailRules = TerrainModel.DetailRulesVersion;
var dams = p.DamRules >= 1 ? DamRemovalData.Read(File.ReadAllBytes(Path.Combine(store, DamRemovalData.FileName))) : null;
var model = p.BuildModel(data, dams);
if (p.DamCourseError != null) { Console.WriteLine("dam courses: " + p.DamCourseError); return 1; }
var fine = new FineElevation(store, File.ReadAllBytes(tileList));
fine.SetFallback(Array.Empty<int>());
model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
model.Fine = fine;
if (dams != null) fine.Dams = dams;
model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(store, LakeOutlineData.FileName))), model.Geometry);
int rv = RiverOutlineData.Usable(p.RiverOutlines);
model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(store, RiverOutlineData.FileName(rv)))), model.Geometry);
var reaches = model.Rivers?.Reaches;
var wet = model.WetSourceFlow;
if (reaches == null || wet == null) { Console.WriteLine("the model has no rivers or no wet source flow (arid river rules)"); return 1; }
Log($"model: {reaches.Count} river reaches, courses {p.RiverCourses}, dams {p.DamRules}, river outlines {rv}");

// ---- the flood ----
// a cell's ground: the lowest of its column's height and GLO-90 within about 6" (25 points 3" apart), the ground under
// town and palm heights; water other than a river (sea, lake) does not pass the flood on
bake = new FloodBake((la, lo) =>
{
    geo.FromGeo(la, lo, out double x, out double z);
    var c = model.GetColumn(x, z);
    bool blocked = c.Water == TerrainModel.WaterSea || (c.Water != TerrainModel.WaterNone && !c.River && c.RiverOutline == 0);
    double g = c.Elev;
    const double o = 1.0 / 1200;
    for (int i = -groundReach; i <= groundReach; i++)
        for (int j = -groundReach; j <= groundReach; j++)
            if (fine.TryHeight(la + i * o, lo + j * o, out double h) && h < g) g = h;
    return ((float)g, blocked);
}) { FallPerKm = bake.FallPerKm, HeightA = bake.HeightA, HeightB = bake.HeightB, HeightMin = bake.HeightMin, HeightMax = bake.HeightMax, DepthFactor = bake.DepthFactor, Log = Log };
var aridity = new Dictionary<long, double>();
double AridityAt(double la, double lo)
{
    long key = (long)Math.Floor(la * 20) * 100000 + (long)Math.Floor(lo * 20);
    if (!aridity.TryGetValue(key, out double ai))
    {
        var cl = data.SampleClimate(Math.Floor(la * 20) / 20 + .025, Math.Floor(lo * 20) / 20 + .025);
        double holdridge = SoilFertility.Aridity(cl.Rain, cl.DrySummer);
        aridity[key] = ai = Math.Min(holdridge, holdridge * (cl.PenmanFactor > 0 ? cl.PenmanFactor : 1));
    }
    return ai;
}
// the river's water: its course level, or GLO-90's lowest point within 3" where that is higher (the course levels sit a
// few metres below the water in the Nile delta)
double WaterAt(double la, double lo, double level)
{
    double g = double.MaxValue;
    const double o = 1.0 / 1200;
    for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            if (fine.TryHeight(la + i * o, lo + j * o, out double h) && h < g) g = h;
    return g == double.MaxValue ? level : Math.Max(level, g);
}
bool InBox(double la, double lo) => boxes.Count == 0 || boxes.Any(b => la >= b[0] && la <= b[1] && lo >= b[2] && lo <= b[3]);
int seededReaches = 0, pieces = 0;
for (int i = 0; i < reaches.Count; i++)
{
    var r = reaches[i];
    if (r.Q < minQ || wet[i] < minQ) continue;
    bool any = false;
    for (int k = 0; k + 1 < r.Lat.Length; k++)
    {
        if (!InBox(r.Lat[k], r.Lon[k])) continue;
        if (AridityAt(r.Lat[k], r.Lon[k]) >= seedAridity && AridityAt(r.Lat[k + 1], r.Lon[k + 1]) >= seedAridity) continue;
        bake.SeedSegment(r.Lat[k], r.Lon[k], WaterAt(r.Lat[k], r.Lon[k], r.Level[k]), r.Lat[k + 1], r.Lon[k + 1], WaterAt(r.Lat[k + 1], r.Lon[k + 1], r.Level[k + 1]), r.Q);
        pieces++;
        any = true;
    }
    if (any) seededReaches++;
}
Log($"seeds: {seededReaches} reaches through dry land, {pieces} pieces");
bake.Run();

// ---- the 15" grid: each cell the highest flood of its four 7.5" cells, the ring around the flood a step lower ----
var levels = new Dictionary<long, double>();
foreach (var (row, col, stage) in bake.Flooded())
{
    long key = (long)(row / 2) * FloodplainGrid.Cols + col / 2;
    if (!levels.TryGetValue(key, out double old) || stage > old) levels[key] = stage;
}
var ring = new Dictionary<long, double>();
double ringFall = bake.FallPerKm * 111.32 / FloodplainGrid.CellsPerDegree;
foreach (var (key, lv) in levels)
{
    int r0 = (int)(key / FloodplainGrid.Cols), c0 = (int)(key % FloodplainGrid.Cols);
    for (int dr = -1; dr <= 1; dr++)
        for (int dc = -1; dc <= 1; dc++)
        {
            if (r0 + dr < 0 || r0 + dr >= FloodplainGrid.Rows) continue;
            long k2 = (long)(r0 + dr) * FloodplainGrid.Cols + ((c0 + dc) % FloodplainGrid.Cols + FloodplainGrid.Cols) % FloodplainGrid.Cols;
            if (levels.ContainsKey(k2)) continue;
            double v = lv - ringFall;
            if (!ring.TryGetValue(k2, out double o2) || v > o2) ring[k2] = v;
        }
}
var blocks = new Dictionary<int, ushort[]>();
foreach (var (key, lv) in levels.Concat(ring))
{
    int r0 = (int)(key / FloodplainGrid.Cols), c0 = (int)(key % FloodplainGrid.Cols);
    int bk = r0 / FloodplainGrid.Block * FloodplainGrid.BlockCols + c0 / FloodplainGrid.Block;
    if (!blocks.TryGetValue(bk, out var b)) blocks[bk] = b = new ushort[FloodplainGrid.Block * FloodplainGrid.Block];
    b[r0 % FloodplainGrid.Block * FloodplainGrid.Block + c0 % FloodplainGrid.Block] = FloodplainGrid.Encode(lv);
}
var bytes = new FloodplainGrid(blocks).Write();
var check = FloodplainGrid.Read(bytes);
if (check.BlockCount != blocks.Count) { Console.WriteLine("the grid does not read back"); return 1; }
File.WriteAllBytes(output, bytes);

// ---- report: flooded area by 10-degree square ----
var report = new StringBuilder();
report.AppendLine($"floodplains.bin: {bytes.Length} bytes, {blocks.Count} blocks, {levels.Count} flooded 15\" cells, {ring.Count} ring cells");
report.AppendLine($"settings: fall {bake.FallPerKm} m/km, flood height {bake.HeightA}·Q^{bake.HeightB} within {bake.HeightMin}..{bake.HeightMax} m, down to {bake.DepthFactor} flood heights below the water, seeds where the aridity index is below {seedAridity}"
    + (settings.Count > 0 ? "; keys " + string.Join(" ", settings.Select(kv => kv.Key + "=" + kv.Value)) : ""));
report.AppendLine($"seeds: {seededReaches} reaches, {pieces} pieces, {bake.SeedCells} 7.5\" cells; flood: {bake.FloodedCells} 7.5\" cells, {bake.TileCount} tiles");
var squares = new SortedDictionary<(int, int), double>();
foreach (var key in levels.Keys)
{
    int r0 = (int)(key / FloodplainGrid.Cols), c0 = (int)(key % FloodplainGrid.Cols);
    double lat = 90 - (r0 + .5) / FloodplainGrid.CellsPerDegree, lon = -180 + (c0 + .5) / FloodplainGrid.CellsPerDegree;
    double km2 = Math.Pow(111.32 / FloodplainGrid.CellsPerDegree, 2) * Math.Cos(lat * Math.PI / 180);
    var sq = ((int)Math.Floor(lat / 10) * 10, (int)Math.Floor(lon / 10) * 10);
    squares[sq] = squares.GetValueOrDefault(sq) + km2;
}
report.AppendLine($"flooded area: {squares.Values.Sum():0} km² (cells whose flood level is set; the game floods only the ground at or below it)");
foreach (var (sq, km2) in squares.OrderByDescending(kv => kv.Value))
    report.AppendLine(string.Format(ci, "  {0,3}..{1,3} N {2,4}..{3,4} E  {4,9:0} km²", sq.Item1, sq.Item1 + 10, sq.Item2, sq.Item2 + 10, km2));
// named places: the grid's level against the column's own height, as the game reads them (EarthGeology.GroundAt)
report.AppendLine("places (level, ground, ambient aridity index; soil where the ground is at or below the level):");
foreach (var (name, la, lo) in new (string, double, double)[]
{
    // fields, away from towns (GLO-90 is a surface model: a town stands metres above its fields)
    ("Luxor, west bank", 25.70, 32.60), ("Luxor, east bank", 25.72, 32.68), ("Asyut, east bank", 27.18, 31.25),
    ("Beni Suef", 29.07, 31.08), ("Delta, Rosetta side", 31.10, 30.80), ("Delta, middle", 30.85, 31.20),
    ("Delta, Damietta side", 31.10, 31.50), ("Delta, west", 31.03, 30.47), ("Delta, east", 30.70, 31.70),
    ("Mesopotamia, Hilla", 32.50, 44.60), ("Mesopotamia, south", 31.20, 46.50), ("Indus, Sindh", 26.00, 68.60),
    ("Khorezm", 41.50, 60.30),
    // deserts
    ("Suez spot", 30.96, 32.29), ("Western Desert", 26.5, 28.5), ("Wadi Natrun", 30.40, 30.30), ("Negev", 30.6, 34.8),
    ("Gobi", 43.0, 105.0), ("Central Sahara", 23.0, 10.0), ("Rub' al Khali", 20.0, 50.0), ("Atacama", -23.5, -69.5),
    ("Namib", -24.5, 15.2), ("Karakum, 140 km from the Amu Darya", 39.2, 62.0),
})
{
    geo.FromGeo(la, lo, out double x, out double z);
    var c = model.GetColumn(x, z);
    double lv = check.Level(la, lo);
    string verdict = c.Water != TerrainModel.WaterNone ? "water" : AridityAt(la, lo) >= SoilFertility.DryLandAridity ? "not arid" : check.Floods(la, lo, c.Elev) ? "SOIL" : "bare";
    report.AppendLine(string.Format(ci, "  {0,-28} {1,7:0.0}N {2,7:0.0}E  level {3,7:0.0}  ground {4,7:0.0}  AI {5:0.00}  {6}", name, la, lo, lv, c.Elev, AridityAt(la, lo), verdict));
}
File.WriteAllText(Path.ChangeExtension(output, ".txt"), report.ToString());
Console.Write(report);
Log($"wrote {output}");
return 0;
