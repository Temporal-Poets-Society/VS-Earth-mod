using System.Globalization;
using System.Text.Json;
using EarthMap;
using EarthMap.Core;

// Offline checks of Earth's water table and Hydrate or Diedrate's aquifer data (decision 0059).
// Usage: dotnet run -- [repo root]
string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string wtJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/water-table-v1.json"));
string groundJson = File.ReadAllText(Path.Combine(root, "assets/earth/config/ground-rules-v1.json"));
byte[] ReadAsset(string loc) { var p = Path.Combine(root, "assets", loc.Split(':')[0], loc.Split(':')[1]); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }
string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

var wt = new WaterTable(wtJson);
Console.WriteLine("water-table rules " + wt.Fingerprint);

// ------------------------------------------------------------------ 1. every ground material has a class
using (var doc = JsonDocument.Parse(groundJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    int n = 0;
    foreach (var m in doc.RootElement.GetProperty("materials").EnumerateObject())
    {
        n++;
        Check(wt.HasMaterial(m.Name) && wt.OfMaterial(m.Name) != WaterTable.Class.None, "ground material '" + m.Name + "' has a permeability class");
    }
    Console.WriteLine($"{n} ground materials classified");
}
// the agreed amendment (habitat 3a) and decision 0059's table
Check(wt.OfMaterial("till-grey") == WaterTable.Class.Low && wt.OfMaterial("silt") == WaterTable.Class.Low, "tills and silt are low");
Check(wt.OfMaterial("clay-blue") == WaterTable.Class.VeryLow && wt.OfMaterial("claystone") == WaterTable.Class.VeryLow && wt.OfMaterial("shale") == WaterTable.Class.VeryLow, "clay, claystone, shale very low");
Check(wt.OfMaterial("sandstone") == WaterTable.Class.Medium && wt.OfMaterial("topsoil") == WaterTable.Class.Medium, "sandstone and topsoil medium");
Check(wt.OfMaterial("gravel") == WaterTable.Class.High && wt.OfMaterial("beach-sand") == WaterTable.Class.High, "gravel and sand high");
Check(wt.OfMaterial("limestone") == WaterTable.Class.Karst && wt.OfMaterial("dolostone") == WaterTable.Class.Karst && wt.OfMaterial("gypsum") == WaterTable.Class.Karst, "soluble rock karst");
Check(wt.OfMaterial("peat") == WaterTable.Class.Peat, "peat its own class");
Check(wt.IsCrystallineMaterial("granite") && wt.IsCrystallineMaterial("gneiss") && !wt.IsCrystallineMaterial("sandstone"), "crystalline rock");
// conductivities rank as the design says: gravel > karst > sandstone > fractured > peat > till > clay
double Y(WaterTable.Class c) => wt.Conductivity[(int)c];
Check(Y(WaterTable.Class.High) > Y(WaterTable.Class.Karst) && Y(WaterTable.Class.Karst) > Y(WaterTable.Class.Medium) && Y(WaterTable.Class.Medium) > Y(WaterTable.Class.Fractured)
      && Y(WaterTable.Class.Fractured) > Y(WaterTable.Class.Peat) && Y(WaterTable.Class.Peat) > Y(WaterTable.Class.Low) && Y(WaterTable.Class.Low) > Y(WaterTable.Class.VeryLow), "conductivities rank gravel > karst > sandstone > fractured > peat > till > clay");

// ------------------------------------------------------------------ 2. blocks by name
byte B(string path) => wt.OfBlockPath(path);
Check(B("rock-limestone") == (byte)WaterTable.Class.Karst, "rock-limestone karst");
Check(B("rock-granite") == ((byte)WaterTable.Class.VeryLow | WaterTable.CrystallineBit), "rock-granite very low, crystalline");
Check(B("crackedrock-sandstone") == (byte)WaterTable.Class.Medium, "cracked sandstone medium");
Check(B("ore-poor-nativecopper-granite") == ((byte)WaterTable.Class.VeryLow | WaterTable.CrystallineBit), "ore by its host rock");
Check(B("rock-kimberlite") == (byte)WaterTable.Class.VeryLow && B("rock-scoria") == (byte)WaterTable.Class.High, "vanilla rocks outside the ground materials");
Check(B("sand-sandstone") == (byte)WaterTable.Class.High && B("sandwavy-granite") == (byte)WaterTable.Class.High && B("gravel-granite") == (byte)WaterTable.Class.High, "sand and gravel blocks");
Check(B("soil-medium-normal") == (byte)WaterTable.Class.Medium && B("rawclay-blue-none") == (byte)WaterTable.Class.VeryLow && B("peat-none") == (byte)WaterTable.Class.Peat, "soil, raw clay, peat blocks");
Check(B("air") == 0 && B("glacierice") == 0 && B("rock-unobtainium") == 0 && B("planks-oak-ud") == 0, "not ground");

// ------------------------------------------------------------------ 3. the table at one column
WaterTable.Table T(double e, double b, double ai, double k, double perched = double.NaN) =>
    wt.At(new WaterTable.Column { Elevation = e, Base = b, Aridity = ai, TopK = k, PerchedTop = perched });
var valley = T(10, 10, 1.2, 0.3);
Check(Math.Abs(valley.Depth - 0.25) < 1e-9, "valley floor: 0.25 m (prototype's v1 floor)");
var tillHill = T(60, 10, 1.2, 0.3);
var sandHill = T(60, 10, 1.2, 0.9);
Check(Math.Abs(tillHill.Depth - (0.25 + 0.3 * 50)) < 1e-9, "humid till hill: 0.25 + 0.3 x 50 m");
Check(Math.Abs(sandHill.Depth - (0.25 + 0.9 * 50)) < 1e-9, "humid sand hill: 0.25 + 0.9 x 50 m");
Check(sandHill.Depth > tillHill.Depth, "sand drains the hill deeper than till");
var aridHill = T(60, 10, 0.05, 0.3);
Check(Math.Abs(aridHill.Depth - (0.25 + 50 + 50)) < 1e-9 && aridHill.Dryness == 1, "desert: flat table at the base, base 50 m lower");
var semiArid = T(60, 10, 0.35, 0.3);
Check(semiArid.Depth > tillHill.Depth && semiArid.Depth < aridHill.Depth, "semi-arid between");
Check(T(60, 10, double.NaN, double.NaN).K == wt.K[(int)WaterTable.Class.Medium], "unknown top and climate: medium, humid");
// 4 m above the base the regional table would be 0.25 + 0.35 x 4 = 1.65 m down; the till at 1 m holds it up
var perchedFlat = T(14, 10, 1.2, 0.35, 1.0);
Check(perchedFlat.Perched && Math.Abs(perchedFlat.Depth - 1.0) < 1e-9, "humid flat till plain: perched table on the till at 1 m");
Check(!T(12, 10, 1.2, 0.35, 1.0).Perched, "2 m above the base the regional table (0.95 m) is already above the till");
Check(!T(40, 10, 1.2, 0.35, 1.0).Perched, "no perched table on a hill (30 m above the base)");
Check(!T(14, 10, 0.3, 0.35, 1.0).Perched, "no perched table in a dry climate");
Check(!T(10.1, 10, 1.2, 0.35, 1.0).Perched, "the regional table above the tight layer wins");
var lake = wt.At(new WaterTable.Column { Elevation = 80, Base = 80, Water = true, WaterSurface = 92 });
Check(lake.Water && lake.Elevation == 92 && lake.Depth == -12, "open water: the table is the surface");
Check(T(5, 20, 1.2, 0.3).Hand == 0 && Math.Abs(T(5, 20, 1.2, 0.3).Depth - 0.25) < 1e-9, "a base above the ground counts as the ground");
// mapped wetland: data before the model's guess. Ķemeri's raised bog on the bench: lake clay, 13.8 m above the base, 7.3 m down
WaterTable.Table W(double e, double b, double ai, double k, double wet, double perched = double.NaN) =>
    wt.At(new WaterTable.Column { Elevation = e, Base = b, Aridity = ai, TopK = k, PerchedTop = perched, Wetland = wet });
var bogDry = W(23.8, 10, 1.2, 0.5, 0);
var bog = W(23.8, 10, 1.2, 0.5, wt.WetlandFullShare);
double Lift(double share, double hand) => Math.Min(1, share / wt.WetlandFullShare) * Math.Clamp(1 - hand / wt.WetlandMaxHand, 0, 1);
Check(!bogDry.Wet && bogDry.Depth > 7, $"a bog the map does not know: the model's {F(bogDry.Depth)} m");
Check(bog.Wet && Math.Abs(bog.Depth - (bogDry.Depth - (bogDry.Depth - wt.MinDepthMetres) * Lift(wt.WetlandFullShare, 13.8))) < 1e-9 && bog.Depth < bogDry.Depth / 2,
      $"Ķemeri as on the bench (13.8 m above the base) in a mapped peatland cell: {F(bogDry.Depth)} m rises to {F(bog.Depth)} m");
var flatBog = W(10.5, 10, 1.2, 0.5, wt.WetlandFullShare);
Check(flatBog.Wet && flatBog.Depth < 0.3, $"a mapped wetland on low ground (0.5 m above the base): at the surface ({F(flatBog.Depth)} m)");
var halfBog = W(23.8, 10, 1.2, 0.5, wt.WetlandFullShare / 2);
Check(Math.Abs(halfBog.Depth - (bogDry.Depth - (bogDry.Depth - wt.MinDepthMetres) * Lift(wt.WetlandFullShare / 2, 13.8))) < 1e-9 && halfBog.Depth > bog.Depth, $"half that share: half the lift ({F(halfBog.Depth)} m)");
Check(Math.Abs(W(23.8, 10, 1.2, 0.5, 1).Depth - bog.Depth) < 1e-9, "a share above the full share lifts no more");
var hillInBog = W(80, 10, 1.2, 0.3, 1);
Check(!hillInBog.Wet && Math.Abs(hillInBog.Depth - T(80, 10, 1.2, 0.3).Depth) < 1e-9, "a hill 70 m above its base in a boggy cell keeps the model's table");
Check(Math.Abs(W(10, 10, 0.05, 0.3, 1).Depth - wt.MinDepthMetres) < 1e-9, "a mapped marsh on low ground in a desert (the Sudd, the Okavango): at the surface in any climate");
var perchedBog = W(14, 10, 1.2, 0.35, wt.WetlandFullShare, 1.0);
Check(perchedBog.Wet && perchedBog.Depth < 0.5, $"a mapped wetland over a perched layer rises above it ({F(perchedBog.Depth)} m)");
Check(!W(10, 10, 1.2, 0.3, 1).Wet && Math.Abs(W(10, 10, 1.2, 0.3, 1).Depth - 0.25) < 1e-9, "a valley floor already at the surface stays there");
Check(wt.At(new WaterTable.Column { Elevation = 80, Base = 80, Water = true, WaterSurface = 92, Wetland = 1 }).Depth == -12, "open water ignores the wetland");

// block placement
var lin = new GroundColumns.DepthCurve(1, double.PositiveInfinity);
Check(WaterTable.BlockY(valley, 120, 0.25, double.NaN, -1) == 120, "a table 0.25 m down saturates the top block");
Check(WaterTable.BlockY(tillHill, 150, 15.25, 109, -1) == 135, "15.25 blocks down");
Check(WaterTable.BlockY(tillHill, 150, 60, 120, -1) == 120, "never below the drainage surface outside deserts");
Check(WaterTable.BlockY(aridHill, 150, 60, 120, -1) == 90, "deserts may lie below it");
Check(WaterTable.BlockY(lake, 100, 0, 0, 111) == 111, "open water: the water's top block");

// ------------------------------------------------------------------ 4. the column's layers place the table
var col = new GroundColumns.Column();
// topsoil 1 m in 1 block, till to 3.5 m (kept one block more), gravel to 40 m, sandstone to 200 m, basement below
col.Add(0, 1, 1, true); col.Add(1, 2, 3.5, true); col.Add(2, 6, 40, true); col.Add(3, 25, 200, true); col.Add(4, int.MaxValue, double.PositiveInfinity, true);
var curve = GroundColumns.DepthCurve.Fit(9.7, 4000, 114);
string[] names = { "topsoil", "till-grey", "gravel", "sandstone", "granite" };
Check(Math.Abs(WaterTable.BlocksAt(col, 0.5, curve) - 0.5) < 1e-9, "0.5 m is half the topsoil block");
Check(Math.Abs(WaterTable.BlocksAt(col, 21.75, curve) - 4) < 1e-9, "half-way down the gravel is its middle block");
Check(Math.Abs(WaterTable.MetresAt(col, WaterTable.BlocksAt(col, 123, curve), curve) - 123) < 1e-6, "metres and blocks invert in a layer");
Check(Math.Abs(WaterTable.MetresAt(col, WaterTable.BlocksAt(col, 1500, curve), curve) - 1500) < 1e-6, "and in the basement");
Check(WaterTable.BlocksAt(col, 1500, curve) > 25, "the basement is below the sandstone");
wt.TopOf(col, names, out double topK, out double perchedTop);
double expectK = (0.55 * 1 + 0.3 * 2.5 + 0.9 * 6.5) / 10;
Check(Math.Abs(topK - expectK) < 1e-9, $"top 10 m k {F(topK)} = {F(expectK)}");
Check(Math.Abs(perchedTop - 1) < 1e-9, "the till under the topsoil starts a perched layer at 1 m");

// ------------------------------------------------------------------ 5. the drainage base
// a valley along x = 0 with a river at 5 m; the ground rises 20 m per km away from it; the sea beyond x = 6000
WaterTable.Node Valley(double x, double z)
{
    if (x >= 6000) return new WaterTable.Node { Level = 0, Kind = WaterTable.Sea };
    if (Math.Abs(x) < 50) return new WaterTable.Node { Level = 5, Kind = WaterTable.River };
    return new WaterTable.Node { Level = 5 + 0.02 * Math.Abs(x), Kind = WaterTable.Land };
}
var one = new WaterTable.Base[1];
wt.BaseField(500, 0, 1, 1, Valley, 1, 1, one);
Check(Math.Abs(one[0].Level - 5) < 0.6 && !one[0].SeaNear, $"500 m from the river: the river is the base ({F(one[0].Level)})");
wt.BaseField(3000, 0, 1, 1, Valley, 1, 1, one);
Check(one[0].Level > 25 && one[0].Level < 45, $"3 km away: the lowest ground within 1.5 km ({F(one[0].Level)})");
wt.BaseField(5000, 0, 1, 1, Valley, 1, 1, one);
Check(one[0].Level < 1 && one[0].SeaNear, $"1 km from the coast: the sea ({F(one[0].Level)})");
// seamless: a 4 x 4 grid and single points at the same places agree (the lattice is aligned to the world)
var grid = new WaterTable.Base[16];
wt.BaseField(2004, 104, 4, 8, Valley, 1, 1, grid);
bool same = true;
for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
{
    wt.BaseField(2004 + 8 * i, 104 + 8 * j, 1, 1, Valley, 1, 1, one);
    if (Math.Abs(one[0].Level - grid[j * 4 + i].Level) > 1e-9) same = false;
}
Check(same, "the base does not depend on the chunk it is computed for");
// and where the metres per x block change with z (latitude): each lattice row's disc is its own, so a chunk's grid and each
// column's own query still agree (audit W2: the spacing and disc came from the asking position's latitude)
Func<double, double> narrowing = z => 1.0 / (1 + z / 4000.0);
wt.BaseField(2004, 2104, 4, 8, Valley, narrowing, 1, grid);
same = true;
for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++)
{
    wt.BaseField(2004 + 8 * i, 2104 + 8 * j, 1, 1, Valley, narrowing, 1, one);
    if (Math.Abs(one[0].Level - grid[j * 4 + i].Level) > 1e-9) same = false;
}
Check(same, "the base does not depend on the asking chunk where the x scale changes with latitude");
// salt lakes within reach; the search reads a lattice a third of the radius apart (500 m), so it finds lakes about that
// wide or wider; a narrower one only where a lattice point falls on it (rivers count through their valley floors)
WaterTable.Node SaltLakeAt(double x, double z, double cx) => Math.Abs(x - cx) < 400 && Math.Abs(z) < 400
    ? new WaterTable.Node { Level = 300, Kind = WaterTable.SaltLake } : new WaterTable.Node { Level = 310, Kind = WaterTable.Land };
wt.BaseField(0, 0, 1, 1, (x, z) => SaltLakeAt(x, z, 1000), 1, 1, one);
Check(one[0].SaltLakeNear && Math.Abs(one[0].Level - 300) < 1e-9, "an 800 m salt lake 1 km away is within reach, and is the base");
wt.BaseField(0, 0, 1, 1, (x, z) => SaltLakeAt(x, z, 2500), 1, 1, one);
Check(!one[0].SaltLakeNear && Math.Abs(one[0].Level - 310) < 1e-9, "one 2.5 km away is not");

// ------------------------------------------------------------------ 6. climate
double latvia = wt.Surplus(650, 1.2), steppe = wt.Surplus(400, 0.4), desert = wt.Surplus(30, 0.03);
Console.WriteLine($"water surplus: Latvia {F(latvia)} mm, steppe {F(steppe)} mm, desert {F(desert)} mm");
Check(latvia > 180 && latvia < 320, "Latvia's surplus is about 200-300 mm (runoff about 250 mm)");
Check(steppe < latvia && desert < 2, "drier climates have less");
Check(Math.Abs(wt.Recharge(30, 0.03) - wt.RechargeFloor) < 0.01 && wt.Recharge(2000, 3) == 1, "recharge: deserts keep the floor, wet climates 1");

// ------------------------------------------------------------------ 7. aquifer rating of a chunk column
// one sampled column type everywhere: top at y = 200, table at y = 190; gravel down to y = 150, granite below
int chunks = 32;
WaterTable.Sample S(int top, int table, double elev, double tableElev, bool sea = false, bool flat = false, bool near = false) =>
    new() { Top = top, TableY = table, Elevation = elev, TableElevation = tableElev, SeaFloor = sea, SaltFlat = flat, SeaNear = near };
(int[] Rating, bool[] Salty) RateColumn(WaterTable.Sample s, Func<int, byte> cls, Func<int, double> metres, double recharge = 1)
{
    var sums = new WaterTable.ChunkSums(chunks);
    for (int k = 0; k < wt.Samples * wt.Samples; k++) wt.Accumulate(s, cls, metres, sums);
    var r = new int[chunks]; var sy = new bool[chunks];
    wt.Rate(sums, wt.Samples * wt.Samples, recharge, r, sy);
    return (r, sy);
}
// the rating scale: Krasny's transmissivity classes on HoD's pick categories (none <= 10, very poor <= 15, poor <= 20, light <= 40, moderate <= 60, heavy)
Check(wt.RatingOf(0) == 0 && wt.RatingOf(0.001) == 0, "no transmissivity: 0");
Check(Math.Abs(wt.RatingOf(0.1) - 10) < 1e-9 && Math.Abs(wt.RatingOf(1000) - 60) < 1e-9 && wt.RatingOf(1e6) == 100, "anchors: 0.1 m2/day 10 (class VI ends), 1000 m2/day 60 (class I begins), above 10000 100");
Check(Math.Abs(wt.RatingOf(Math.Sqrt(10 * 100)) - 30) < 1e-9, "log-linear between anchors");
bool rising = true; for (double t = 0.005; t < 2e4; t *= 1.1) rising &= wt.RatingOf(t * 1.1) >= wt.RatingOf(t);
Check(rising, "the rating never falls as transmissivity rises");
byte gravel = B("gravel-granite"), granite = B("rock-granite"), clay = B("rawclay-blue-none"), limestone = B("rock-limestone"), sandstone = B("rock-sandstone");
var r1 = RateColumn(S(200, 190, 60, 59), y => y > 150 ? gravel : granite, d => d * 2.0);
Console.WriteLine("gravel over granite, by chunk from y=0: " + string.Join(" ", r1.Rating));
Check(r1.Rating[6] == 0, "the chunk above the table (192-223) is dry: 0");
Check(r1.Rating[5] > 60, $"62 m of saturated gravel (chunk 160-191) reads heavy ({r1.Rating[5]})");
Check(r1.Rating[4] > 40 && r1.Rating[4] < r1.Rating[5], $"18 m of gravel over the fractured granite top: moderate, below it ({r1.Rating[4]})");
Check(r1.Rating[3] > 15 && r1.Rating[3] <= 20, $"granite 100-200 m down is fractured: poor ({r1.Rating[3]})");
Check(r1.Rating[2] == 0 && r1.Rating[0] == 0, $"deep granite: none ({r1.Rating[2]}, {r1.Rating[0]})");
Check(!r1.Salty.Any(x => x), "inland, shallow: fresh");
var rc = RateColumn(S(200, 190, 60, 59), y => clay, d => d * 2.0);
Check(rc.Rating[5] == 0, $"62 m of saturated clay gives nothing ({rc.Rating[5]})");
var rk = RateColumn(S(200, 190, 60, 59), y => limestone, d => d * 2.0);
var rs = RateColumn(S(200, 190, 60, 59), y => sandstone, d => d * 2.0);
Check(rk.Rating[5] > 40 && rk.Rating[5] <= 60 && rs.Rating[5] > 20 && rs.Rating[5] <= 40, $"62 m: karst {rk.Rating[5]} moderate, sandstone {rs.Rating[5]} light");
// the same ground at every vertical scale: 30 m of gravel over clay, at 1 m and at 10 m a block, all in chunk 160-191
var g1 = RateColumn(S(191, 191, 30, 30), y => y > 161 ? gravel : clay, d => d * 1.0);
var g10 = RateColumn(S(191, 191, 30, 30), y => y > 188 ? gravel : clay, d => d * 10.0);
Console.WriteLine($"30 m of gravel over clay: {g1.Rating[5]} at 1 m a block, {g10.Rating[5]} at 10 m a block");
Check(g1.Rating[5] == g10.Rating[5] && g1.Rating[5] > 40, "30 m of gravel reads the same at 1 m and 10 m a block");
var till10 = RateColumn(S(191, 191, 30, 30), y => (byte)WaterTable.Class.Low, d => d * 10.0);
Check(till10.Rating[5] <= 15, $"320 m of saturated till at 10 m a block: very poor at most ({till10.Rating[5]})");
var sand10 = RateColumn(S(191, 191, 30, 30), y => sandstone, d => d * 10.0);
Check(sand10.Rating[5] > 40 && sand10.Rating[5] <= 60, $"320 m of saturated sandstone: moderate ({sand10.Rating[5]})");
var dry = RateColumn(S(200, 190, 60, 59), y => gravel, d => d * 2.0, wt.Recharge(30, 0.03));
Check(Math.Abs(dry.Rating[5] - r1.Rating[5] * wt.RechargeFloor) <= 1, "a desert keeps half (fossil water)");
// salt
var seaFloor = RateColumn(S(100, 200, -20, 0, sea: true), y => gravel, d => d * 2.0);
Check(seaFloor.Salty[2] && seaFloor.Rating[2] > 0, "under the sea floor: salty");
var flat = RateColumn(S(200, 190, 60, 59, flat: true), y => gravel, d => d * 2.0);
Check(flat.Salty[5], "under a salt flat: salty");
// coast: ground at 3 m, table at 1 m above the sea; 1 block = 2 m: the interface at -40 m lies 43 m = 21.5 blocks down
var coast = RateColumn(S(110, 109, 3, 1, near: true), y => gravel, d => d * 2.0);
Check(!coast.Salty[3] && coast.Salty[2], $"coast: the fresh lens floats on sea water 40 x 1 m below sea level (chunk 96-127 fresh, 64-95 salty)");
var inland = RateColumn(S(110, 109, 3, 1), y => gravel, d => d * 2.0);
Check(!inland.Salty[2], "the same without the sea in reach: fresh");
var brine = RateColumn(S(1000, 990, 400, 399), y => sandstone, d => d * 1.0);
Check(!brine.Salty[30] && brine.Salty[10], "deep brine below 500 m");
// a single block: Evaluate agrees with the column walk
double rockTop = double.NaN;
wt.Evaluate(S(200, 190, 60, 59), granite, 20, ref rockTop, out var c20, out double y20, out _);
Check(c20 == WaterTable.Class.Fractured && rockTop == 20, "the first granite block is the fractured top");
wt.Evaluate(S(200, 190, 60, 59), granite, 150, ref rockTop, out var c150, out _, out _);
Check(c150 == WaterTable.Class.VeryLow, "130 m into the granite: unfractured");

// ------------------------------------------------------------------ 8. Hydrate or Diedrate's bytes, decoded back
// an independent protobuf wire reader (not protobuf-net): field number, wire type, value
static List<(int Field, int Wire, ulong Value, byte[] Bytes)> Fields(byte[] b)
{
    var list = new List<(int, int, ulong, byte[])>();
    int i = 0;
    ulong Varint() { ulong v = 0; int shift = 0; while (true) { byte x = b[i++]; v |= (ulong)(x & 0x7f) << shift; if (x < 0x80) return v; shift += 7; } }
    while (i < b.Length)
    {
        ulong key = Varint();
        int field = (int)(key >> 3), wire = (int)(key & 7);
        if (wire == 0) list.Add((field, wire, Varint(), null));
        else if (wire == 2) { int len = (int)Varint(); list.Add((field, wire, (ulong)len, b[i..(i + len)])); i += len; }
        else throw new InvalidDataException("unexpected wire type " + wire);
    }
    return list;
}
static (int? Raw, bool Salty, int? Smoothed, int? Version) Decode(byte[] bytes)
{
    int? raw = null, sm = null, ver = null; bool salty = false;
    foreach (var (f, w, v, inner) in Fields(bytes))
    {
        if (f == 3 && w == 0) ver = (int)v;
        else if (f == 1 && w == 2)
            foreach (var (g, w2, v2, _) in Fields(inner))
            {
                if (w2 != 0) throw new InvalidDataException("data field " + g + " is not a varint");
                if (g == 1) raw = (int)v2; else if (g == 2) salty = v2 != 0; else if (g == 3) sm = (int)v2;
                else throw new InvalidDataException("unknown data field " + g);
            }
        else throw new InvalidDataException("unknown chunk field " + f + " wire " + w);
    }
    return (raw, salty, sm, ver);
}
// a 1024-high column: every one of its 32 chunks, as the game writes them
int bad = 0;
for (int i = 0; i < 32; i++)
{
    byte[] bytes = HodAquiferContract.Bytes(r1.Rating[i], r1.Salty[i]);
    var d = Decode(bytes);
    // protobuf-net leaves out zero and false (HoD reads them as its defaults, 0 and false); the smoothed rating is nullable, so 0 is written
    bool ok = d.Version == 2 && (d.Raw ?? 0) == r1.Rating[i] && d.Salty == r1.Salty[i] && d.Smoothed == r1.Rating[i];
    var back = HodAquiferContract.Read(bytes);
    ok &= back.Version == 2 && back.Data.AquiferRatingRaw == r1.Rating[i] && back.Data.AquiferRatingSmoothed == r1.Rating[i] && back.Data.IsSalty == r1.Salty[i];
    if (!ok) { bad++; Console.WriteLine($"chunk {i}: {BitConverter.ToString(bytes)} -> raw {d.Raw}, salty {d.Salty}, smoothed {d.Smoothed}, version {d.Version}"); }
}
Check(bad == 0, "all 32 chunks of a 1024-high column decode back to their rating, salt, smoothed rating and version 2");
// exact bytes: rating 73, salty: chunk {1: {1: 73, 2: true, 3: 73}, 3: 2}
string hex = BitConverter.ToString(HodAquiferContract.Bytes(73, true));
Check(hex == "0A-06-08-49-10-01-18-49-18-02", "rating 73 salty encodes as " + hex);
hex = BitConverter.ToString(HodAquiferContract.Bytes(0, false));
Check(hex == "0A-02-18-00-18-02", "rating 0 fresh encodes as " + hex + " (a smoothed rating is present, so HoD does not re-smooth)");

// ------------------------------------------------------------------ 9. the mapped wetlands Earth ships (GLWD v2)
var wetFile = ReadAsset(WetlandGrid.Asset);
if (wetFile == null) Console.WriteLine("(no wetland grid in the repo: skipped)");
else
{
    var wgrid = WetlandGrid.Read(wetFile);
    var weights = wt.WetlandWeights(wgrid.Layers);
    Check(weights.Length == wgrid.Layers.Length && weights.Count(x => x > 0) >= 3, "the rules weigh the grid's layers by name");
    // the best cell near each place (a 0.1 degree search), as /earth water would find walking over it
    (double Share, double Lat, double Lon) Best(double lat, double lon)
    {
        var best = (0.0, lat, lon);
        for (double a = lat - 0.1; a <= lat + 0.1001; a += 0.01)
            for (double o = lon - 0.15; o <= lon + 0.1501; o += 0.01)
            {
                double v = WaterTable.WetlandShare(wgrid, weights, a, o);
                if (v > best.Item1) best = (v, a, o);
            }
        return best;
    }
    foreach (var (name, lat, lon) in new[] { ("Ķemeri bog", 56.92, 23.47), ("Teiči bog", 56.62, 26.38), ("Riga centre", 56.95, 24.11), ("Gaiziņkalns", 56.87, 25.96) })
    {
        double here = WaterTable.WetlandShare(wgrid, weights, lat, lon);
        var (bs, ba, bo) = Best(lat, lon);
        Console.WriteLine($"wetland {name} {F(lat)} {F(lon)}: {F(here)} here; best {F(bs)} at {F(ba)} {F(bo)}; open {F(wgrid.OpenShare(ba, bo))}");
    }
    Check(WaterTable.WetlandShare(wgrid, weights, 56.92, 23.47) >= wt.WetlandFullShare && Best(56.62, 26.38).Share >= wt.WetlandFullShare, "Ķemeri and Teiči: the mapped peatland lifts the low ground fully");
    Check(WaterTable.WetlandShare(wgrid, weights, 56.95, 24.11) < 0.15, "Riga centre: little wetland");
    Check(WaterTable.WetlandShare(wgrid, new double[wgrid.Layers.Length], 56.92, 23.47) == 0, "no weights: no wetland");
}

// ------------------------------------------------------------------ 10. real ground columns (the rules and whatever ground tiles the repo holds offline)
var model = GroundColumns.Load(groundJson, ReadAsset, curve, 1234);
foreach (string m in model.Materials) Check(wt.HasMaterial(m), "model material '" + m + "' has a class");
var real = new GroundColumns.Column();
foreach (var (lat, lon) in new[] { (56.95, 24.10), (57.13, 24.20), (56.65, 23.72), (61.5, 25.5) })
{
    var s = model.At(lat, lon);
    model.Build(s, GroundColumns.Surface.Land, real);
    wt.TopOf(real, model.Materials, out double k, out double pt);
    var tab = wt.At(new WaterTable.Column { Elevation = 30, Base = 20, Aridity = 1.2, TopK = k, PerchedTop = pt });
    Console.WriteLine($"{F(lat)} {F(lon)}: {model.Describe(real)}\n  top 10 m k {F(k)}, tight layer {(double.IsNaN(pt) ? "none" : F(pt) + " m")}; 10 m above the base: {wt.Describe(tab)}");
    Check(k > 0 && k <= 1, $"{F(lat)} {F(lon)}: a k");
}

Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;
