using System.Globalization;
using EarthMap.Core;

// EA-0131: a map of a window on GLO-90 with river files drawn over it.
//   StreamMap <glo90-tiles.bin> <GLO-90 store> <out.png> <lat0> <lat1> <lon0> <lon1> <metres per pixel> [file@colour[@firstReach[@count]]] ...
// Files are rivers.bin v2 (EMRV); colour is rrggbb; the line's width grows with the discharge. Pixels are square in metres.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var fine = new FineElevation(args[1], File.ReadAllBytes(args[0]));
fine.SetFallback(Array.Empty<int>());
string outPath = args[2];
double la0 = double.Parse(args[3]), la1 = double.Parse(args[4]), lo0 = double.Parse(args[5]), lo1 = double.Parse(args[6]), mpp = double.Parse(args[7]);
double latMid = 0.5 * (la0 + la1), cl = Math.Cos(latMid * Math.PI / 180);
int w = (int)Math.Ceiling((lo1 - lo0) * Geo.MetresPerDegree * cl / mpp), h = (int)Math.Ceiling((la1 - la0) * Geo.MetresPerDegree / mpp);
var hgt = new double[w * h];
Parallel.For(0, h, j =>
{
    for (int i = 0; i < w; i++)
    {
        double lat = la1 - (j + 0.5) * (la1 - la0) / h, lon = lo0 + (i + 0.5) * (lo1 - lo0) / w;
        hgt[j * w + i] = fine.TryHeight(lat, lon, out double v) ? v : double.NaN;
    }
});
var rgb = new byte[3 * w * h];
double mn = hgt.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Min(), mx = hgt.Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max();
for (int j = 0; j < h; j++)
    for (int i = 0; i < w; i++)
    {
        double v = hgt[j * w + i];
        double dx = i + 1 < w ? hgt[j * w + i + 1] - v : 0, dy = j + 1 < h ? hgt[(j + 1) * w + i] - v : 0;
        double shade = Math.Clamp(0.75 - 0.6 * (dx - dy) / mpp, 0.15, 1);
        double t = (v - mn) / Math.Max(1, mx - mn);
        // low green to high brown-white, shaded
        double r = 120 + 120 * t, g = 150 + 70 * t - 40 * t * t, b = 100 + 120 * t * t;
        if (v <= 0.5) { r = 60; g = 90; b = 150; }
        rgb[3 * (j * w + i)] = (byte)Math.Clamp(r * shade, 0, 255);
        rgb[3 * (j * w + i) + 1] = (byte)Math.Clamp(g * shade, 0, 255);
        rgb[3 * (j * w + i) + 2] = (byte)Math.Clamp(b * shade, 0, 255);
    }
void Plot(double lat, double lon, int rad, byte cr, byte cg, byte cb)
{
    int ci = (int)Math.Floor((lon - lo0) / (lo1 - lo0) * w), cj = (int)Math.Floor((la1 - lat) / (la1 - la0) * h);
    for (int dj = -rad; dj <= rad; dj++)
        for (int di = -rad; di <= rad; di++)
        {
            int i = ci + di, j = cj + dj;
            if (i < 0 || j < 0 || i >= w || j >= h || di * di + dj * dj > rad * rad + rad) continue;
            rgb[3 * (j * w + i)] = cr; rgb[3 * (j * w + i) + 1] = cg; rgb[3 * (j * w + i) + 2] = cb;
        }
}
foreach (var spec in args.Skip(8))
{
    // ('@', not ':': Git Bash turns colon lists into Windows path lists)
    var parts = spec.Split('@');
    int k = 0;
    string file = parts[0];
    string col = parts[k + 1];
    int first = parts.Length > k + 2 ? int.Parse(parts[k + 2]) : 0, count = parts.Length > k + 3 ? int.Parse(parts[k + 3]) : int.MaxValue;
    byte cr = Convert.ToByte(col[..2], 16), cg = Convert.ToByte(col[2..4], 16), cb = Convert.ToByte(col[4..6], 16);
    var reaches = RiverData.Read(File.ReadAllBytes(file), Path.GetFileName(file));
    int drawn = 0;
    for (int r = first; r < reaches.Count && r - first < count; r++)
    {
        var re = reaches[r];
        int rad = re.Q < 2 ? 0 : re.Q < 5 ? 1 : re.Q < 20 ? 1 : 2;
        bool inside = false;
        for (int v = 0; v < re.Lon.Length; v++) if (re.Lat[v] >= la0 && re.Lat[v] <= la1 && re.Lon[v] >= lo0 && re.Lon[v] <= lo1) { inside = true; break; }
        if (!inside) continue;
        drawn++;
        for (int v = 0; v + 1 < re.Lon.Length; v++)
        {
            double d = Math.Sqrt(Math.Pow((re.Lon[v + 1] - re.Lon[v]) * cl, 2) + Math.Pow(re.Lat[v + 1] - re.Lat[v], 2)) * Geo.MetresPerDegree;
            int steps = Math.Max(1, (int)Math.Ceiling(d / (mpp * 0.5)));
            for (int s = 0; s <= steps; s++)
            {
                double f = (double)s / steps;
                Plot(re.Lat[v] + f * (re.Lat[v + 1] - re.Lat[v]), re.Lon[v] + f * (re.Lon[v + 1] - re.Lon[v]), rad, cr, cg, cb);
            }
        }
    }
    Console.WriteLine($"{file}: {drawn} reaches drawn");
}
Png.WriteRgb(outPath, w, h, rgb);
Console.WriteLine($"{outPath}: {w} x {h}");
