using System.IO.Compression;
using System.Text;
using EarthMap.Core;

// Earth's arid floodplains (decision 0048, soil rules 3; tools/FloodplainBake, assets/earth/config/floodplains.bin): the
// format, then the shipped grid at known places.
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
int passed = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL: " + msg); passed++; }
bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) < tol;
bool Throws(Func<object> read) { try { read(); return false; } catch (FormatException) { return true; } catch (InvalidDataException) { return true; } }
const int B = FloodplainGrid.Block, CPD = FloodplainGrid.CellsPerDegree;

// ---------------------------------------------------------------- the format, on small hand-made grids
Check(FloodplainGrid.Asset == "earth:config/floodplains.bin" && FloodplainGrid.Cols == 360 * CPD && FloodplainGrid.Rows == 180 * CPD, "Asset path and a global 15\" grid");
Check(CPD == 240 && FloodplainGrid.Cols % B == 0 && FloodplainGrid.Rows % B == 0, "Blocks tile the grid");
Check(Near(FloodplainGrid.Decode(FloodplainGrid.Encode(12.34)), 12.3) && Near(FloodplainGrid.Decode(FloodplainGrid.Encode(-412.06)), -412.1)
    && Near(FloodplainGrid.Decode(FloodplainGrid.Encode(FloodplainGrid.Base)), FloodplainGrid.Base) && double.IsNaN(FloodplainGrid.Decode(0)),
    "Levels keep decimetres; 0 is no flood");
Check(FloodplainGrid.Encode(-5000) == 1 && FloodplainGrid.Encode(1e9) == 65535, "Levels outside the range clamp, never to 0");
// one block at 30..29.933 N, 32..32.067 E: row 14400, column 50880
int row0 = (90 - 30) * CPD, col0 = (180 + 32) * CPD;
int key0 = row0 / B * FloodplainGrid.BlockCols + col0 / B;
var cells = new ushort[B * B];
cells[0] = FloodplainGrid.Encode(10); cells[1] = FloodplainGrid.Encode(20); cells[B] = FloodplainGrid.Encode(30); cells[B + 1] = FloodplainGrid.Encode(40);
cells[5 * B + 5] = FloodplainGrid.Encode(100);
var one = new FloodplainGrid(new Dictionary<int, ushort[]> { [key0] = cells });
Check(Near(one.CellLevel(row0, col0), 10) && Near(one.CellLevel(row0 + 1, col0 + 1), 40) && double.IsNaN(one.CellLevel(row0 + 2, col0)), "Cells read by row and column");
Check(double.IsNaN(one.CellLevel(-1, 0)) && double.IsNaN(one.CellLevel(FloodplainGrid.Rows, 0)) && Near(one.CellLevel(row0, col0 + FloodplainGrid.Cols), 10),
    "Rows outside are dry; columns wrap");
double Lat(double r) => 90 - (r + .5) / CPD;
double Lon(double c) => -180 + (c + .5) / CPD;
Check(Near(one.Level(Lat(row0), Lon(col0)), 10, 1e-6) && Near(one.Level(Lat(row0 + 1), Lon(col0 + 1)), 40, 1e-6), "A cell centre gives its own level");
Check(Near(one.Level(Lat(row0 + .5), Lon(col0 + .5)), 25, 1e-6) && Near(one.Level(Lat(row0), Lon(col0 + .25)), 12.5, 1e-6), "Bilinear between the centres");
// at (row0 + 5.5, col0 + 5.5) only one of the four corners is flooded: the dry ones take its level
Check(Near(one.Level(Lat(row0 + 5.5), Lon(col0 + 5.5)), 100, 1e-6) && Near(one.Level(Lat(row0 + 4.7), Lon(col0 + 5.2)), 100, 1e-6), "Dry corners take the mean of the flooded ones");
Check(double.IsNaN(one.Level(Lat(row0 + 8), Lon(col0 + 8))) && double.IsNaN(one.Level(10, 10)) && double.IsNaN(one.Level(90, 0)) && double.IsNaN(one.Level(double.NaN, 32)),
    "No flood far from the flooded cells, at the pole or at NaN");
Check(one.Floods(Lat(row0), Lon(col0), 10) && one.Floods(Lat(row0), Lon(col0), -3) && !one.Floods(Lat(row0), Lon(col0), 10.1) && !one.Floods(10, 10, -100),
    "Ground at or below the level is in the flood");
// several blocks, including the first and last of the grid and the date line, survive Write and Read
{
    var rnd = new Random(7);
    var blocks = new Dictionary<int, ushort[]>();
    foreach (int k in new[] { 0, 1, FloodplainGrid.BlockCols - 1, FloodplainGrid.BlockCols, key0, key0 + 1, FloodplainGrid.BlockRows * FloodplainGrid.BlockCols - 1 })
    {
        var b = new ushort[B * B];
        for (int i = 0; i < b.Length; i++) b[i] = rnd.Next(3) == 0 ? (ushort)0 : (ushort)rnd.Next(1, 65536);
        blocks[k] = b;
    }
    blocks[FloodplainGrid.BlockCols - 1][B - 1] = FloodplainGrid.Encode(321);
    var again = FloodplainGrid.Read(new FloodplainGrid(blocks).Write());
    bool same = again.BlockCount == blocks.Count;
    foreach (var (k, b) in blocks) same &= again.Blocks.TryGetValue(k, out var b2) && b.SequenceEqual(b2);
    Check(same, "Write then Read is the same grid");
    Check(Near(again.CellLevel(0, FloodplainGrid.Cols - 1), 321)
        && Near(again.CellLevel(0, -1), again.CellLevel(0, FloodplainGrid.Cols - 1)), "The last column is next to the first");
}
// broken files throw
var good = one.Write();
Check(!Throws(() => FloodplainGrid.Read(good)) && FloodplainGrid.Read(good).BlockCount == 1, "The hand-made file reads");
Check(Throws(() => FloodplainGrid.Read(null)) && Throws(() => FloodplainGrid.Read(new byte[0])) && Throws(() => FloodplainGrid.Read(good[..19])), "Missing or short headers throw");
byte[] Patch(int at, int value) { var f = (byte[])good.Clone(); BitConverter.GetBytes(value).CopyTo(f, at); return f; }
var badMagic = (byte[])good.Clone(); badMagic[3] = (byte)'2';
Check(Throws(() => FloodplainGrid.Read(badMagic)) && Throws(() => FloodplainGrid.Read(Patch(4, 2))) && Throws(() => FloodplainGrid.Read(Patch(8, 120)))
    && Throws(() => FloodplainGrid.Read(Patch(12, 32))) && Throws(() => FloodplainGrid.Read(Patch(16, -1))) && Throws(() => FloodplainGrid.Read(Patch(16, int.MaxValue))),
    "Another magic, version, resolution, block size or block count throws");
Check(Throws(() => FloodplainGrid.Read(Patch(16, 2))), "Fewer blocks than the header says throws");
byte[] Raw(int count, params byte[][] parts)
{
    var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("EFP1"));
    foreach (int v in new[] { FloodplainGrid.FileVersion, CPD, B, count }) ms.Write(BitConverter.GetBytes(v));
    using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) foreach (var p in parts) z.Write(p);
    return ms.ToArray();
}
byte[] Keys(params int[] k) => k.SelectMany(BitConverter.GetBytes).ToArray();
var empty = new byte[B * B * 2];
Check(!Throws(() => FloodplainGrid.Read(Raw(2, Keys(3, 5), empty, empty))), "A raw two-block file reads");
Check(Throws(() => FloodplainGrid.Read(Raw(2, Keys(5, 3), empty, empty))) && Throws(() => FloodplainGrid.Read(Raw(2, Keys(3, 3), empty, empty)))
    && Throws(() => FloodplainGrid.Read(Raw(1, Keys(FloodplainGrid.BlockRows * FloodplainGrid.BlockCols), empty))) && Throws(() => FloodplainGrid.Read(Raw(1, Keys(-1), empty))),
    "Unordered, repeated or out-of-grid block keys throw");
Check(Throws(() => FloodplainGrid.Read(Raw(2, Keys(3, 5), empty, empty[..10]))) && Throws(() => FloodplainGrid.Read(Raw(2, Keys(3, 5), empty, empty, new byte[] { 1 }))),
    "Missing cells or data after the cells throw");
var garbage = (byte[])good.Clone();
for (int i = 20; i < garbage.Length; i++) garbage[i] = 0xFF;
Check(Throws(() => FloodplainGrid.Read(garbage)), "A corrupt deflate stream throws");

// ---------------------------------------------------------------- the shipped grid
var path = Path.Combine(root, "assets/earth/config/floodplains.bin");
var file = File.ReadAllBytes(path);
var sw = System.Diagnostics.Stopwatch.StartNew();
var grid = FloodplainGrid.Read(file);
Console.WriteLine($"floodplains.bin: {file.Length:N0} bytes, {grid.BlockCount} blocks, read in {sw.ElapsedMilliseconds} ms");
Check(Throws(() => FloodplainGrid.Read(file[..(file.Length / 2)])) && Throws(() => FloodplainGrid.Read(file[..^16])), "The truncated asset throws");
var again2 = FloodplainGrid.Read(grid.Write());
{
    bool same = again2.BlockCount == grid.BlockCount;
    foreach (var (k, b) in grid.Blocks) same &= again2.Blocks.TryGetValue(k, out var b2) && b.SequenceEqual(b2);
    Check(same, "The asset round-trips through Write and Read");
}
{
    double lo = double.MaxValue, hi = double.MinValue;
    long cellsSet = 0;
    foreach (var b in grid.Blocks.Values)
        foreach (var v in b)
            if (v != 0) { double l = FloodplainGrid.Decode(v); lo = Math.Min(lo, l); hi = Math.Max(hi, l); cellsSet++; }
    Console.WriteLine($"levels {lo:0.0} .. {hi:0.0} m on {cellsSet:N0} cells");
    Check(cellsSet > 1_000_000 && lo > -450 && hi < 6000, "Over a million cells with levels between the Dead Sea's shore and the highest valleys");
}
// known places (baked 2026-11-08 at fall 0.15 m/km): fields lie below the flood's level, the ground being the column's own
// GLO-90 height the bake reported (fields, away from towns); deserts have no flood at all
foreach (var (name, lat, lon, ground) in new[]
{
    ("Luxor, west bank", 25.70, 32.60, 76.0), ("Luxor, east bank", 25.72, 32.68, 75.0), ("Asyut, east bank", 27.18, 31.25, 53.0),
    ("Beni Suef", 29.07, 31.08, 29.0), ("Nile delta, Rosetta side", 31.10, 30.80, 4.0), ("Nile delta, middle", 30.85, 31.20, 7.0),
    ("Nile delta, Damietta side", 31.10, 31.50, 3.0), ("Nile delta, west", 31.03, 30.47, 4.0), ("Nile delta, east", 30.70, 31.70, 5.0),
    ("Mesopotamia, Hilla", 32.50, 44.60, 26.0), ("Mesopotamia, south", 31.20, 46.50, 5.0), ("Indus, Sindh", 26.00, 68.60, 26.0),
    ("Khorezm", 41.50, 60.30, 91.0),
})
{
    double lv = grid.Level(lat, lon);
    Console.WriteLine($"{name,-28} level {lv,6:0.0} m, ground {ground,5:0} m");
    Check(lv >= ground, $"{name}: the flood ({lv:0.0} m) reaches the fields ({ground:0} m)");
}
foreach (var (name, lat, lon) in new[]
{
    ("Suez spot", 30.96, 32.29), ("Western Desert", 26.5, 28.5), ("Wadi Natrun", 30.40, 30.30), ("Negev", 30.6, 34.8), ("Gobi", 43.0, 105.0),
    ("Central Sahara", 23.0, 10.0), ("Rub' al Khali", 20.0, 50.0), ("Atacama", -23.5, -69.5), ("Namib", -24.5, 15.2),
    ("Karakum, 140 km from the Amu Darya", 39.2, 62.0),
})
    Check(double.IsNaN(grid.Level(lat, lon)), $"{name} has no flood");
{
    // the author's Suez spot (30.96 N 32.29 E) and the plain around it, from 30.91 to 30.97 N and from the lagoon's shore (32.22 E) to the canal
    // (from 31.0 N the delta's flood reaches the flats on Lake Manzala's eastern shore)
    int wet = 0;
    var wetAt = new List<string>();
    for (int i = 0; i <= 26; i++) for (int j = 0; j <= 6; j++) if (!double.IsNaN(grid.Level(30.91 + j * .01, 32.22 + i * .005))) { wet++; wetAt.Add($"{30.91 + j * .01:0.00},{32.22 + i * .005:0.000}:{grid.Level(30.91 + j * .01, 32.22 + i * .005):0.0}"); }
    if (wet > 0) Console.WriteLine("flood near the Suez spot: " + string.Join(" ", wetAt));
    Check(wet == 0, $"No flood on the plain around the Suez spot ({wet} of 189 samples)");
}
Console.WriteLine($"FloodplainTests: {passed} checks passed");
