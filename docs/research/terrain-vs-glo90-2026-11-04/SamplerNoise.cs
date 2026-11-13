using System.Diagnostics;
using EarthMap;
using EarthMap.Core;

// args: params.bin earthdataFolder outDir then windows "name lat0 lat1 lon0 lon1 step"
var sw = Stopwatch.StartNew();
var p = ProtoBuf.Serializer.Deserialize<EarthParams>(new MemoryStream(File.ReadAllBytes(args[0])));
Console.WriteLine($"params: version {p.Version} detail {p.DetailScale} seed {p.NoiseSeed} rivers {p.Rivers} summits {p.Summits}");
string broken = p.RepairSettings(new EarthMapConfig());
if (broken.Length > 0) Console.WriteLine("repaired: " + broken);
var data = EarthData.LoadFromFolder(args[1], true);
Console.WriteLine($"data {sw.ElapsedMilliseconds} ms");
var model = p.BuildModel(data);
Console.WriteLine($"model {sw.ElapsedMilliseconds} ms");
var geo = model.Geometry;
Console.WriteLine($"bpd X {geo.BlocksPerDegreeX:F2} Z {geo.BlocksPerDegreeZ:F2}");
var vs = model.Vertical;
foreach (double h in new[] { 0.0, 1000, 3000, 5000, 7000, 8849 })
    Console.WriteLine($"  {h} m -> y {vs.Y(h):F1}");
string outDir = args[2];
Directory.CreateDirectory(outDir);
double detail = model.DetailScale;
for (int a = 3; a < args.Length; a++)
{
    var f = args[a].Split(' ');
    string name = f[0];
    double lat0 = double.Parse(f[1]), lat1 = double.Parse(f[2]), lon0 = double.Parse(f[3]), lon1 = double.Parse(f[4]);
    int step = int.Parse(f[5]);
    geo.FromGeo(lat1, lon0, out double x0, out double z0); // north-west corner
    geo.FromGeo(lat0, lon1, out double x1, out double z1);
    int ix0 = (int)Math.Floor(x0), iz0 = (int)Math.Floor(z0);
    int nx = (int)((x1 - x0) / step), nz = (int)((z1 - z0) / step);
    Console.WriteLine($"{name}: x {ix0} z {iz0} {nx}x{nz} step {step}");
    foreach (var variant in new[] { "game", "nodetail" })
    {
        model.DetailScale = variant == "game" ? detail : 0;
        var top = new short[nx * nz]; var elev = new float[nx * nz];
        var t = Stopwatch.StartNew();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var c = model.GetColumn(ix0 + i * step + 0.5, iz0 + j * step + 0.5);
                top[j * nx + i] = (short)c.Top; elev[j * nx + i] = (float)c.Elev;
            }
        Console.WriteLine($"  {variant} {t.ElapsedMilliseconds} ms");
        using (var w = new BinaryWriter(File.Create(Path.Combine(outDir, $"{name}-{variant}-top.bin")))) foreach (var v in top) w.Write(v);
        using (var w = new BinaryWriter(File.Create(Path.Combine(outDir, $"{name}-{variant}-elev.bin")))) foreach (var v in elev) w.Write(v);
    }
    model.DetailScale = detail;
    // the data alone: SampleTerrain's bicubic of the 2' grid (no summits, no noise), and the relief that scales the noise
    var raw = new float[nx * nz]; var relief = new float[nx * nz];
    for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            var g = geo.ToGeo(ix0 + i * step + 0.5, iz0 + j * step + 0.5);
            var s = data.SampleTerrain(g.Lat, g.Lon);
            raw[j * nx + i] = (float)s.Elev; relief[j * nx + i] = (float)s.Relief;
        }
    using (var w = new BinaryWriter(File.Create(Path.Combine(outDir, $"{name}-raw-elev.bin")))) foreach (var v in raw) w.Write(v);
    using (var w = new BinaryWriter(File.Create(Path.Combine(outDir, $"{name}-relief.bin")))) foreach (var v in relief) w.Write(v);
    var gNW = geo.ToGeo(ix0 + 0.5, iz0 + 0.5);
    var gSE = geo.ToGeo(ix0 + (nx - 1) * step + 0.5, iz0 + (nz - 1) * step + 0.5);
    File.WriteAllText(Path.Combine(outDir, $"{name}.json"),
        "{" + $"\"nx\":{nx},\"nz\":{nz},\"step\":{step},\"x0\":{ix0},\"z0\":{iz0},\"latN\":{gNW.Lat:R},\"lonW\":{gNW.Lon:R},\"latS\":{gSE.Lat:R},\"lonE\":{gSE.Lon:R},\"bpdX\":{geo.BlocksPerDegreeX:R},\"bpdZ\":{geo.BlocksPerDegreeZ:R},\"detail\":{detail:R}" + "}");
}
Console.WriteLine($"done {sw.ElapsedMilliseconds} ms");
