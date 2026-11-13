using System.Diagnostics;
using EarthMap;
using EarthMap.Core;

// args: params.bin earthdataFolder glo90Store tileList outDir then windows "name latS latN lonW lonE step"
var sw = Stopwatch.StartNew();
var p = ProtoBuf.Serializer.Deserialize<EarthParams>(new MemoryStream(File.ReadAllBytes(args[0])));
string broken = p.RepairSettings(new EarthMapConfig());
if (broken.Length > 0) Console.WriteLine("repaired: " + broken);
var data = EarthData.LoadFromFolder(args[1], true);
var model = p.BuildModel(data);
Console.WriteLine($"model {sw.ElapsedMilliseconds} ms; height {p.MapHeight} sea {p.SeaLevel} detail {model.DetailScale}");
var fine = new FineElevation(args[2], File.ReadAllBytes(args[3]));
var geo = model.Geometry;
string outDir = args[4];
Directory.CreateDirectory(outDir);
for (int a = 5; a < args.Length; a++)
{
    var f = args[a].Split(' ');
    string name = f[0];
    double lat0 = double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), lat1 = double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
    double lon0 = double.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture), lon1 = double.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture);
    int step = int.Parse(f[5]);
    geo.FromGeo(lat1, lon0, out double x0, out double z0);
    geo.FromGeo(lat0, lon1, out double x1, out double z1);
    int ix0 = (int)Math.Floor(x0), iz0 = (int)Math.Floor(z0);
    int nx = (int)((x1 - x0) / step), nz = (int)((z1 - z0) / step);
    foreach (var variant in new[] { "builtin", "glo90" })
    {
        model.Fine = variant == "glo90" ? fine : null;
        model.FineRules = variant == "glo90" ? FineElevation.Version : 0;
        var top = new short[nx * nz]; var elev = new float[nx * nz]; var mask = new byte[nx * nz]; var wtop = new short[nx * nz];
        var t = Stopwatch.StartNew();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var c = model.GetColumn(ix0 + i * step + 0.5, iz0 + j * step + 0.5);
                int k = j * nx + i;
                top[k] = (short)c.Top; elev[k] = (float)c.Elev; wtop[k] = (short)c.WaterTop;
                // 1 sea, 2 lake, 3 salt lake, 4 river, 8 near a river (bank or valley), 16 beach, 32 ice
                byte m = c.Water;
                if (c.River) m = 4;
                if (c.NearRiver) m |= 8;
                if (c.Beach) m |= 16;
                if (c.IceBottom <= c.Top) m |= 32;
                mask[k] = m;
            }
        Console.WriteLine($"{name} {variant}: {nx}x{nz} step {step}, {t.ElapsedMilliseconds} ms");
        void Save<T>(string what, T[] arr) where T : struct
        {
            var bytes = new byte[arr.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>()];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(outDir, $"{name}-{variant}-{what}.bin"), bytes);
        }
        Save("top", top); Save("elev", elev); Save("mask", mask); Save("wtop", wtop);
    }
    model.Fine = null; model.FineRules = 0;
    var gNW = geo.ToGeo(ix0 + 0.5, iz0 + 0.5);
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    File.WriteAllText(Path.Combine(outDir, $"{name}.json"), string.Format(inv,
        "{{\"nx\":{0},\"nz\":{1},\"step\":{2},\"x0\":{3},\"z0\":{4},\"latN\":{5:R},\"lonW\":{6:R},\"bpdX\":{7:R},\"bpdZ\":{8:R},\"sea\":{9},\"y8849\":{10:R}}}",
        nx, nz, step, ix0, iz0, gNW.Lat, gNW.Lon, geo.BlocksPerDegreeX, geo.BlocksPerDegreeZ, p.SeaLevel, model.Vertical.Y(8849)));
}
Console.WriteLine($"done {sw.ElapsedMilliseconds} ms; GLO-90 lookups {fine.Lookups}, tiles read {fine.Loads}, absent {fine.Absent}");
