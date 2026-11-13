using System.IO.Compression;
using System.Text;
using EarthMap.Core;

// Earth's wetland grid (tools/bake_wetlands.py from GLWD v2, assets/earth/config/wetlands.bin): the format, then known wetlands.
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
int passed = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL: " + msg); passed++; }
bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
bool Throws(Func<object> read) { try { read(); return false; } catch (FormatException) { return true; } }

// ---------------------------------------------------------------- the format, on small hand-made grids
byte[] Make(double west, double north, double step, int cols, int rows, string[] names, byte[] cells, int layerCount = -1, byte[] extra = null)
{
    var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("EWL1"));
    ms.Write(BitConverter.GetBytes(west)); ms.Write(BitConverter.GetBytes(north)); ms.Write(BitConverter.GetBytes(step));
    ms.Write(BitConverter.GetBytes(cols)); ms.Write(BitConverter.GetBytes(rows)); ms.Write(BitConverter.GetBytes(layerCount < 0 ? names.Length : layerCount));
    foreach (var n in names) { ms.WriteByte((byte)n.Length); ms.Write(Encoding.ASCII.GetBytes(n)); }
    using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) { z.Write(cells); if (extra != null) z.Write(extra); }
    return ms.ToArray();
}
// 3 x 2 cells of 1 degree from 10 E, 50 N; layer "a" then "b", rows from the north
var tiny = WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[] { 0, 250, 0, 125, 0, 0, /* b */ 0, 0, 0, 200, 0, 50 }));
Check(tiny.Cols == 3 && tiny.Rows == 2 && Near(tiny.West, 10) && Near(tiny.North, 50) && Near(tiny.Step, 1) && string.Join(",", tiny.Layers) == "a,b", "Header reads as written");
Check(tiny.Cell(0, 0, 1) == 250 && tiny.Cell(0, 1, 0) == 125 && tiny.Cell(1, 1, 0) == 200 && tiny.Cell(1, 1, 2) == 50 && tiny.Cell(1, 0, 0) == 0, "Cells are layer-major, rows from the north");
Check(Near(tiny.Share(0, 49.5, 11.5), 1) && Near(tiny.Share(0, 48.5, 10.5), .5) && Near(tiny.Share(1, 48.5, 12.5), .2), "A cell centre gives its own share");
Check(Near(tiny.Share(0, 49, 11), (0 + 250 + 125 + 0) / 4.0 / 250), "Bilinear between the four nearest centres");
Check(Near(tiny.Share(0, 49.9, 11.5), 1) && Near(tiny.Share(1, 48.1, 12.9), .2), "Within half a cell of the edge the edge cells hold");
Check(tiny.Share(0, 50.1, 11.5) == 0 && tiny.Share(0, 47.9, 11.5) == 0 && tiny.Share(0, 49.5, 9.9) == 0 && tiny.Share(0, 49.5, 13.1) == 0 && tiny.Total(60, 11) == 0,
    "Outside a regional grid there is no wetland");
Check(tiny.Share(0, double.NaN, 11) == 0 && tiny.Total(49, double.NaN) == 0 && tiny.Total(49, double.PositiveInfinity) == 0, "NaN and infinite coordinates are outside");
Check(Near(tiny.Total(48.5, 10.5), 1) && Near(tiny.Total(48.5, 12.5), .2) && Near(tiny.Total(49.5, 11.5), 1), "Total sums the layers and is clamped to 1");
bool outside = false;
try { tiny.Cell(0, 2, 0); } catch (ArgumentOutOfRangeException) { outside = true; }
Check(outside, "Cell outside the grid throws");
// a global grid wraps at the date line
var ring = WetlandGrid.Read(Make(-180, 40, 90, 4, 1, new[] { "a" }, new byte[] { 250, 0, 0, 0 }));
Check(Near(ring.Share(0, 39, 180), .5) && Near(ring.Share(0, 39, -180), .5) && Near(ring.Share(0, 39, -135), 1) && Near(ring.Share(0, 39, 225), 1) && Near(ring.Share(0, 39, -495), 1),
    "A global grid wraps around the date line");
// cells across several 16 x 16 blocks, with partial blocks at the east and south edges, survive Read and Write
{
    int cols = 37, rows = 21, layers = 3;
    var cells = new byte[layers * rows * cols];
    for (int i = 0; i < cells.Length; i++) cells[i] = (byte)(i % 7 == 0 || i % 11 == 0 ? (i * 31) % 251 : 0);
    Array.Clear(cells, cols * rows + 16 * cols, 5 * cols);   // empty block rows in layer 1
    var g = WetlandGrid.Read(Make(0, 10, .5, cols, rows, new[] { "x", "yy", "zzz" }, cells));
    bool same = true;
    for (int l = 0; l < layers; l++) for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) same &= g.Cell(l, r, c) == cells[(l * rows + r) * cols + c];
    Check(same, "Every cell reads back across blocks and partial edge blocks");
    var again = WetlandGrid.Read(g.Write());
    for (int l = 0; l < layers; l++) for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) same &= again.Cell(l, r, c) == cells[(l * rows + r) * cols + c];
    Check(same && string.Join(",", again.Layers) == "x,yy,zzz" && again.Cols == cols && again.Rows == rows && Near(again.Step, .5), "Write then Read is the same grid");
}
// broken files are FormatExceptions
var good = Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[12]);
Check(!Throws(() => WetlandGrid.Read(good)), "The hand-made file reads");
Check(Throws(() => WetlandGrid.Read(null)) && Throws(() => WetlandGrid.Read(new byte[0])) && Throws(() => WetlandGrid.Read(good[..30])), "Missing or short headers throw");
var badMagic = (byte[])good.Clone(); badMagic[3] = (byte)'2';
Check(Throws(() => WetlandGrid.Read(badMagic)), "Another magic throws");
Check(Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[12], 0))) && Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[12], 65)))
    && Throws(() => WetlandGrid.Read(Make(10, 50, 1, -3, 2, new[] { "a", "b" }, new byte[12]))) && Throws(() => WetlandGrid.Read(Make(10, 50, 0, 3, 2, new[] { "a", "b" }, new byte[12])))
    && Throws(() => WetlandGrid.Read(Make(10, 50, double.NaN, 3, 2, new[] { "a", "b" }, new byte[12]))) && Throws(() => WetlandGrid.Read(Make(10, 50, 1, 100_000, 100_000, new[] { "a" }, new byte[12]))),
    "Bad layer counts, sizes and steps throw");
Check(Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[12], 3))) && Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "" }, new byte[12])))
    && Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b\n" }, new byte[12]))), "Missing, empty or non-printable layer names throw");
Check(Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[11]))) && Throws(() => WetlandGrid.Read(good[..^3])), "Missing cells throw");
Check(Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, new byte[12], -1, new byte[] { 0 }))), "Data after the cells throws");
var over = new byte[12]; over[7] = 251;
Check(Throws(() => WetlandGrid.Read(Make(10, 50, 1, 3, 2, new[] { "a", "b" }, over))), "A share byte above 250 throws");
var garbage = (byte[])good.Clone();
for (int i = 46; i < garbage.Length; i++) garbage[i] = 0xFF;
Check(Throws(() => WetlandGrid.Read(garbage)), "A corrupt deflate stream throws");

// ---------------------------------------------------------------- the shipped grid
var path = Path.Combine(root, "assets/earth/config/wetlands.bin");
var file = File.ReadAllBytes(path);
var sw = System.Diagnostics.Stopwatch.StartNew();
var grid = WetlandGrid.Read(file);
Console.WriteLine($"wetlands.bin: {file.Length:N0} bytes, {grid.Cols} x {grid.Rows} cells of {grid.Step} deg, layers {string.Join(", ", grid.Layers)}, read in {sw.ElapsedMilliseconds} ms");
Check(WetlandGrid.Asset == "earth:config/wetlands.bin", "Asset path");
Check(Near(grid.West, -180) && Near(grid.North, 84) && Near(grid.Step, .1) && grid.Cols == 3600 && grid.Rows == 1440, "0.1 degree grid from 180 W, 84 N to 60 S");
Check(string.Join(",", grid.Layers) == "bog,peatforest,swampforest,marsh,mangrove,saltmarsh,damp"
    && grid.Layers[WetlandGrid.Bog] == "bog" && grid.Layers[WetlandGrid.PeatForest] == "peatforest" && grid.Layers[WetlandGrid.SwampForest] == "swampforest"
    && grid.Layers[WetlandGrid.Marsh] == "marsh" && grid.Layers[WetlandGrid.Mangrove] == "mangrove" && grid.Layers[WetlandGrid.SaltMarsh] == "saltmarsh"
    && grid.Layers[WetlandGrid.Damp] == "damp", "Layer order matches the constants");
// (cutting only the last byte or two can keep every cell: they hold the end-of-stream marker, which DeflateStream does not report)
Check(Throws(() => WetlandGrid.Read(file[..(file.Length / 2)])) && Throws(() => WetlandGrid.Read(file[..^16])) && Throws(() => WetlandGrid.Read(file[..60])), "The truncated asset throws");
{
    int over250 = 0, south = 0;
    long wet = 0;
    for (int r = 0; r < grid.Rows; r++)
        for (int c = 0; c < grid.Cols; c++)
        {
            int sum = 0;
            for (int l = 0; l < grid.Layers.Length; l++) sum += grid.Cell(l, r, c);
            if (sum > 253) over250++;           // each 15" pixel counts for one group: the layers sum to at most 1 (+ rounding)
            if (r >= 1400 && sum > 0) south++;  // GLWD stops at 56 S
            wet += sum;
        }
    Console.WriteLine($"cells over a full share: {over250}; wetland on {wet / 250.0 / grid.Cols / 1400:P1} of the grid's area north of 56 S (cells, not km2)");
    Check(over250 == 0, "No cell's layers add up to more than the whole cell");
    Check(south == 0, "No wetland south of GLWD's 56 S");
}
var again2 = WetlandGrid.Read(grid.Write());
{
    bool same = true;
    for (int l = 0; l < grid.Layers.Length && same; l++) for (int r = 0; r < grid.Rows; r += 7) for (int c = 0; c < grid.Cols; c++) same &= again2.Cell(l, r, c) == grid.Cell(l, r, c);
    Check(same, "The asset round-trips through Write and Read");
}

// ---------------------------------------------------------------- known places
// point: bilinear at the place; area: the mean over the 0.5 degree square around it (11 x 11 samples). Thresholds are about
// three quarters of what GLWD v2 gives on the 0.1 degree grid (baked 2026-11-07). Where GLWD classes a famous wetland other than its fame suggests,
// the check follows GLWD and says so: it maps the Everglades as subtropical peatland (class 27, "bog" here), the Pantanal
// and the Hudson Bay Lowlands as mostly forested (Hansen tree cover of 10 % or more), and the Seine floodplain through
// central Paris as riverine wetland (classes 11 and 15), so the dry checks are the Sahara and Madrid.
// Seasonally saturated and ephemeral classes (14, 15, 18-21) are the "damp" layer: GLWD calls them its most uncertain and
// their open or wooded state is mostly today's farming, so Picardy's and the Po plain's fields are damp ground, not marsh.
const int B = WetlandGrid.Bog, PF = WetlandGrid.PeatForest, SF = WetlandGrid.SwampForest, M = WetlandGrid.Marsh, MG = WetlandGrid.Mangrove, SM = WetlandGrid.SaltMarsh, D = WetlandGrid.Damp;
double[] Point(double lat, double lon) => Enumerable.Range(0, grid.Layers.Length).Select(l => grid.Share(l, lat, lon)).ToArray();
double[] Area(double lat, double lon)
{
    var s = new double[grid.Layers.Length];
    for (int i = -5; i <= 5; i++) for (int j = -5; j <= 5; j++) for (int l = 0; l < s.Length; l++) s[l] += grid.Share(l, lat + i * .05, lon + j * .05) / 121;
    return s;
}
string Row(double[] v) => string.Join(" ", v.Select(x => $"{x,6:F3}")) + $"  total {Math.Min(1, v.Sum()),5:F3}";
Console.WriteLine($"{"",-26}{string.Join(" ", grid.Layers.Select(n => $"{n[..Math.Min(6, n.Length)],6}"))}");
var sites = new (string name, double lat, double lon, Func<double[], double[], double, bool> ok, string want)[]
{
    // Polesie's marshes were largely drained in the 20th century; GLWD keeps peat and floodplain forest
    ("Polesie (Pripyat)", 51.9, 27.5, (p, a, t) => t > .12 && a.Sum() > .2, "has peat, swamp, marsh or damp ground (> 0.12 here, > 0.2 around)"),
    ("Hudson Bay Lowlands", 54.0, -85.0, (p, a, t) => p[B] + p[PF] > .85 && a[B] + a[PF] > .75, "is peatland (> 0.85 here, > 0.75 around)"),
    // GLWD's share of mire at the named point is modest; its core lies to the south-east
    ("Vasyugan mire", 57.5, 78.5, (p, a, t) => a[B] + a[PF] > .2, "is peatland (> 0.2 around)"),
    ("Vasyugan core", 56.9, 79.8, (p, a, t) => p[B] + p[PF] > .45 && a[B] + a[PF] > .45 && a[B] > .15, "is peatland (> 0.45), partly open bog"),
    ("Everglades", 25.9, -80.8, (p, a, t) => t > .95 && p[B] + p[M] > .6 && a[B] + a[M] > .5, "is open wetland (bog + marsh > 0.6 here, > 0.5 around)"),
    ("Sundarbans", 21.9, 89.2, (p, a, t) => p[MG] > .6 && a[MG] > .45, "is mangrove (> 0.6 here, > 0.45 around)"),
    ("Jade Bay (Wadden)", 53.45, 8.2, (p, a, t) => p[SM] > .025 && a[M] > .15, "has saltmarsh (> 0.025), and tidal flats as marsh around (> 0.15)"),
    ("Wadden, Schleswig-Holst.", 54.5, 8.9, (p, a, t) => p[SM] > .05 && a[SM] > .02, "has saltmarsh (> 0.05 here, > 0.02 around)"),
    ("Pantanal", -17.5, -57.0, (p, a, t) => t > .55 && a.Sum() > .6 && a[SF] > a[M] && a[SF] + a[D] > .55, "is wetland (> 0.55), mostly flooded forest and damp ground"),
    ("Central Sahara", 23.0, 10.0, (p, a, t) => t == 0 && a.Sum() < .001, "is dry"),
    ("Paris centre", 48.86, 2.35, (p, a, t) => t < .4 && p[B] + p[PF] + p[MG] + p[SM] < .01, "is at most the Seine's floodplain (< 0.4, no peat or coast)"),
    ("Picardy fields", 49.9, 2.8, (p, a, t) => a[D] > .2 && a[M] < .1, "is damp ground, not open marsh (damp > 0.2, marsh < 0.1 around)"),
    ("Po plain", 45.1, 10.5, (p, a, t) => p[D] > .3 && a[D] > .12 && a[M] < .15, "is damp ground, not open marsh (damp > 0.3 here, > 0.12 around, marsh < 0.15)"),
    ("Madrid", 40.42, -3.70, (p, a, t) => t < .02 && a.Sum() < .05, "is dry (< 0.02 here, < 0.05 around)"),
    ("Riga", 56.95, 24.1, (p, a, t) => true, "print"),
    ("Flow Country", 58.4, -3.9, (p, a, t) => p[B] > .22 && a[B] > .2, "is blanket bog (> 0.22 here, > 0.2 around)"),
    ("Irish midland bogs", 53.25, -7.9, (p, a, t) => p[B] > .2 && a[B] > .12, "is raised bog (> 0.2 here, > 0.12 around)"),
    ("Sumatra peat swamp", -0.5, 103.0, (p, a, t) => p[PF] > .6 && a[PF] > .6, "is peat swamp forest (> 0.6)"),
    ("Danube delta", 45.1, 29.3, (p, a, t) => p[M] > .55 && a[M] > .55, "is marsh (> 0.55)"),
    ("Niger delta", 4.5, 6.5, (p, a, t) => p[MG] > .7 && a[MG] > .35, "is mangrove (> 0.7 here, > 0.35 around)"),
    ("Camargue", 43.55, 4.5, (p, a, t) => p[M] > .25 && p[SM] > .15 && a[SM] > .04, "is marsh and saltmarsh"),
    ("Sudd", 7.5, 30.5, (p, a, t) => p[SF] + p[M] > .4 && a[SF] + a[M] > .28, "is swamp and marsh (> 0.4 here, > 0.28 around)"),
};
foreach (var (name, lat, lon, ok, want) in sites)
{
    var p = Point(lat, lon); var a = Area(lat, lon); double t = grid.Total(lat, lon);
    Console.WriteLine($"{name,-26}{Row(p)}");
    Console.WriteLine($"{"  0.5 deg mean",-26}{Row(a)}");
    Check(Near(t, Math.Min(1, p.Sum())), name + ": Total is the sum of the layers");
    Check(ok(p, a, t), $"{name}: {want}");
}
// ---------------------------------------------------------------- open wetland (EarthParams.WetlandRules): where trees do not stand
double Open(double lat, double lon) => grid.OpenShare(lat, lon);
Console.WriteLine($"open wetland: Everglades {Open(25.9, -80.8):0.00}, Danube delta {Open(45.1, 29.3):0.00}, Flow Country {Open(58.4, -3.9):0.00}, " +
    $"Hudson Bay Lowlands {Open(54.0, -85.0):0.00}, Sumatra peat swamp {Open(-0.5, 103.0):0.00}, Sundarbans {Open(21.9, 89.2):0.00}, Picardy {Open(49.9, 2.8):0.00}, Pantanal {Open(-17.5, -57.0):0.00}");
Check(Near(Open(25.9, -80.8), Math.Min(1, grid.Share(B, 25.9, -80.8) + grid.Share(M, 25.9, -80.8) + grid.Share(SM, 25.9, -80.8))), "Open wetland is bog, marsh and saltmarsh");
Check(Open(25.9, -80.8) > .5 && Open(45.1, 29.3) > .55 && Open(58.4, -3.9) > .25, "The Everglades, the Danube delta and the Flow Country are open");
Check(Open(-0.5, 103.0) < .05 && Open(21.9, 89.2) < .05 && Open(-17.5, -57.0) < .05, "Peat swamp forest, mangrove and the Pantanal's flooded forest keep their trees");
Check(Open(49.9, 2.8) < .05 && Open(45.1, 10.5) < .15, "Damp farmland (Picardy, the Po plain) is not opened");
Check(Open(23.0, 10.0) == 0 && Open(90, 0) == 0 && Open(double.NaN, 0) == 0, "Dry land, the grid's edge and NaN are not open");
// ---------------------------------------------------------------- surface ponds (EarthParams.PondRules): only in the mapped wetlands
double Pond(double lat, double lon) => grid.PondShare(lat, lon);
Console.WriteLine($"surface ponds kept: Everglades {Pond(25.9, -80.8):0.00}, Danube delta {Pond(45.1, 29.3):0.00}, Flow Country {Pond(58.4, -3.9):0.00}, " +
    $"Picardy {Pond(49.9, 2.8):0.00}, Teici bog {Pond(56.62, 26.35):0.00}, Riga {Pond(56.95, 24.1):0.00}, Val Venosta {Pond(46.62, 10.69):0.00}, Bernese Oberland {Pond(46.6, 7.9):0.00}, Sahara {Pond(23.0, 10.0):0.00}");
double PondByHand(double lat, double lon) => Math.Min(1, Enumerable.Range(0, grid.Layers.Length).Sum(l => (l == WetlandGrid.Damp ? .5 : 1) * grid.Share(l, lat, lon)));
Check(Near(Pond(25.9, -80.8), PondByHand(25.9, -80.8)) && Near(Pond(49.9, 2.8), PondByHand(49.9, 2.8)) && Near(Pond(-17.5, -57.0), PondByHand(-17.5, -57.0)),
    "Ponds are kept by every wetland group, damp ground at half");
Check(Pond(25.9, -80.8) > .9 && Pond(45.1, 29.3) > .55 && Pond(-0.5, 103.0) > .3, "The Everglades, the Danube delta and Sumatra's peat swamp keep their ponds");
Check(Pond(23.0, 10.0) == 0 && Pond(46.62, 10.69) < .05 && Pond(90, 0) == 0 && Pond(double.NaN, 0) == 0, "The Sahara, Val Venosta, the grid's edge and NaN get no surface ponds");
Check(!WetlandGrid.KeepsPond(0, 1, 5, 5) && WetlandGrid.KeepsPond(1, 1, 5, 5) && !WetlandGrid.KeepsPond(double.NaN, 1, 5, 5), "No share keeps none, a full share keeps all");
int kept = 0, repeat = 0;
for (int i = 0; i < 100000; i++)
{
    int px = i * 7919 % 100003, pz = i / 3 - 20000;
    bool k = WetlandGrid.KeepsPond(.3, 12345, px, pz);
    if (k) kept++;
    if (k == WetlandGrid.KeepsPond(.3, 12345, px, pz)) repeat++;
}
Check(Math.Abs(kept / 100000.0 - .3) < .01 && repeat == 100000, "A share of 0.3 keeps 30 % of pond tries, the same each time (" + kept + ")");
Console.WriteLine($"WetlandTests: {passed} checks passed");
