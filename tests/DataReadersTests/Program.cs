using EarthMap.Core;
using EarthMap.Tests;

// EA-0116 D-R5 (the Fable pass of the 14 November 2026 code review): the Core data readers no test named before, on the
// files a server reads.
// - DamRemovalData: each dam rules' file is the one this build pins (size and SHA-256), a changed byte is not, and it reads
//   with its rules as its version.
// - TiledGrid (temperature.bin): its layout; at every sampled cell centre the bilinear and Catmull-Rom lookups give the
//   cell's own value; columns wrap; each tile decodes to its full size.
// - The water overlays: the rivers' snap and arms files fit the installed rivers-glo90.bin and each other, and nothing
//   else; the sea referee reads against the terrain's grid.
// usage: DataReadersTests <repo root>. The data store: EARTH_STORE, else the player's or the AgentBridge bench's
// ModData\earth; without one only TiledGrid is checked (exit 3, a partial pass).
var t = new TestChecks("Data readers");
string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
string store = new[] { Environment.GetEnvironmentVariable("EARTH_STORE"), Path.Combine(appData, "VintagestoryData", "ModData", "earth"),
        Path.Combine(appData, "VintagestoryData-agentbridge", "ModData", "earth") }
    .FirstOrDefault(s => !string.IsNullOrEmpty(s) && Directory.Exists(Path.Combine(s, "data")) && Directory.Exists(Path.Combine(s, "glo90")));
bool skipped = false;
var missing = new List<string>();   // named on the last line, which Run-All shows (Fable's low on 942ef27)

// ---------------------------------------------------------------- TiledGrid
{
    byte[] file = File.ReadAllBytes(Path.Combine(root, "earthdata", "temperature.bin"));
    var grid = new TiledGrid(file, "EMTP", 7, 48, "temperature.bin", new[] { PlaneType.I16, PlaneType.I16, PlaneType.I16, PlaneType.U8 });
    t.Check(grid.Width == 2 * grid.Height && grid.Res * 360 == grid.Width && grid.Width % grid.Tile == 0, $"TiledGrid: temperature.bin is {grid.Width} x {grid.Height} at {grid.Res} cells a degree in tiles of {grid.Tile}");
    var rnd = new Random(1318);
    int centres = 0, bad = 0;
    for (int n = 0; n < 3000; n++)
    {
        int row = rnd.Next(grid.Height), col = rnd.Next(grid.Width);
        double lat = 90 - (row + 0.5) / grid.Res, lon = -180 + (col + 0.5) / grid.Res;
        grid.Position(lat, lon, out double fr, out double fc);
        var tile = grid.TileOf(row, col, out int i);
        double own = tile == null ? 0 : tile[i];
        if (Math.Abs(fr - row) > 1e-6 || Math.Abs(fc - col) > 1e-6 || Math.Abs(grid.Bilinear(lat, lon) - own) > 1e-6 || Math.Abs(grid.CatmullRom(lat, lon) - own) > 1e-6) bad++;
        centres++;
    }
    t.Check(bad == 0, $"TiledGrid: at {centres} cell centres Position gives the cell, and Bilinear and CatmullRom give its own value ({bad} differ)");
    int k0 = grid.KeyOf(10, -1, out int i0), k1 = grid.KeyOf(10, grid.Width - 1, out int i1), k2 = grid.KeyOf(-5, 3, out int i2), k3 = grid.KeyOf(0, 3, out int i3);
    t.Check(k0 == k1 && i0 == i1 && k2 == k3 && i2 == i3, "TiledGrid: columns wrap and rows are held at the poles");
    int tiles = grid.Width / grid.Tile * (grid.Height / grid.Tile), wrong = 0, full = 0;
    for (int key = 0; key < tiles; key++)
    {
        int len = grid.Decode(key).Length;
        if (len == 0) continue;
        full++;
        if (len != grid.CellsPerTile * 7) wrong++;
    }
    t.Check(full > 0 && wrong == 0, $"TiledGrid: each of the {full} tiles with data decodes to {grid.CellsPerTile} cells of 7 bytes ({wrong} do not)");
}

if (store == null)
{
    Console.WriteLine("SKIP: no Earth data store on this machine (EARTH_STORE); the dam and water overlay files are not checked");
    skipped = true; missing.Add("the data store");
}
else
{
    // ------------------------------------------------------------ DamRemovalData
    string glo90 = Path.Combine(store, "glo90");
    for (int rules = 1; rules <= DamRemovalData.NewestRules; rules++)
    {
        string path = Path.Combine(glo90, DamRemovalData.FileNameFor(rules));
        if (!File.Exists(path)) { Console.WriteLine("SKIP: " + DamRemovalData.FileNameFor(rules) + " is not in " + glo90); skipped = true; missing.Add(DamRemovalData.FileNameFor(rules)); continue; }
        byte[] bytes = File.ReadAllBytes(path);
        bool expected = DamRemovalData.IsExpected(bytes, rules, out string why);
        var data = DamRemovalData.Read(bytes);
        t.Check(expected && bytes.Length == DamRemovalData.FileBytesFor(rules) && data != null && data.Version == rules,
            $"DamRemovalData: dam rules {rules} read {DamRemovalData.FileNameFor(rules)}, {bytes.Length:N0} bytes, the pinned file, version {data?.Version} ({why})");
        bytes[bytes.Length / 2] ^= 0x40;
        t.Check(!DamRemovalData.IsExpected(bytes, rules, out _), $"DamRemovalData: a changed byte in {DamRemovalData.FileNameFor(rules)} is not the pinned file");
    }
    t.Check(DamRemovalData.FileNameFor(DamRemovalData.NewestRules + 1) == DamRemovalData.FileNameFor(DamRemovalData.NewestRules), "DamRemovalData: rules above the newest read the newest file");

    // ------------------------------------------------------------ the water overlays
    string data2 = Path.Combine(store, "data");
    string F(string name) => File.Exists(Path.Combine(root, "earthdata", name)) ? Path.Combine(root, "earthdata", name) : Path.Combine(data2, name);
    if (!File.Exists(F("rivers-glo90.bin")) || !File.Exists(F("rivers-glo90-snap-2.bin")) || !File.Exists(F("rivers-glo90-arms-2.bin")))
    {
        Console.WriteLine("SKIP: the rivers on GLO-90 or their overlays are not in the store");
        skipped = true; missing.Add("the rivers on GLO-90 and their overlays");
    }
    else
    {
        ulong rivers = EarthBinary.Sha8(File.ReadAllBytes(F("rivers-glo90.bin")));
        RiverSnapData snap;
        RiverArmsData arms;
        using (var s = File.OpenRead(F("rivers-glo90-snap-2.bin"))) snap = RiverSnapData.Read(s);
        using (var s = File.OpenRead(F("rivers-glo90-arms-2.bin"))) arms = RiverArmsData.Read(s);
        t.Check(snap.Fits(rivers) && !snap.Fits(rivers ^ 1) && snap.Changed > 0, $"RiverSnapData: the snap fits the installed rivers-glo90.bin and no other ({snap.Changed:N0} reaches changed)");
        t.Check(arms.Fits(snap) && !arms.Fits(null) && arms.Arms > 0, $"RiverArmsData: the side arms fit that snap ({arms.Arms:N0} arms)");
    }
    var ds = EarthDataset.Load(name => File.Exists(F(name)) ? File.OpenRead(F(name)) : null, true);
    t.Check(ds.SeaReferee != null && ds.TerrainW > 0, $"SeaRefereeData: the sea referee reads against the terrain's grid ({ds.TerrainW} cells across)");
}

int code = t.Summary();
if (skipped) Console.WriteLine("Part skipped, not on this machine: " + string.Join(", ", missing));
return code != 0 ? code : skipped ? 3 : 0;
