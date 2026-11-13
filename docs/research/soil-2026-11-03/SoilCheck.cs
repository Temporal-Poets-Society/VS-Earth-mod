// Surface cover survey (soil research, 3 November 2026): the top block vanilla's block layers (blocklayers.json) choose
// from Earth's climate, on every land cell of a world grid, by climate class and precipitation.
// Run: dotnet run -c Release -- <folder holding earthdata/> [step degrees]
using EarthMap.Core;

string root = args.Length > 0 ? args[0] : ".";
double step = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : .25;
var data = EarthData.LoadFromFolder(root, true);
data.SetClimateFix(true);
data.SetFineRain(true);
var options = new VerticalTerrainOptions();
int height = 512, sea = options.SeaLevel(height, 110);
var vertical = options.Build(height, sea, .0625, options.CurveMetres(200), 20, .43);

// Simplified Köppen from yearly mean, coldest and warmest month, precipitation and summer dryness.
string Koppen(EarthData.ClimateSample c, double t, double cold, double warm)
{
    double threshold = 20 * Math.Max(t, -7) + 140 - 140 * c.DrySummer;
    if (warm < 10) return "E polar/tundra";
    if (c.Precip < threshold / 2) return t >= 18 ? "BWh hot desert" : "BWk cold desert";
    if (c.Precip < threshold) return t >= 18 ? "BSh hot steppe" : "BSk cold steppe";
    if (cold >= 18) return "A tropical";
    if (cold >= -3) return c.DrySummer >= .5 ? "Cs mediterranean" : "C temperate";
    return "D continental";
}
// Vanilla Climate.GetFertility (VintagestoryAPI 1.22).
double Fertility(double rain, double temp, double yRel)
{
    double t = Math.Clamp((temp + 20) * 4.25, 0, 255);
    double num = Math.Min(255, rain / 2 + Math.Max(0, rain * t / 512));
    double num2 = 1 - Math.Max(0, (80 - num) / 80);
    return num - Math.Max(0, 50 * (yRel - .5)) * num2;
}
// The top block of vanilla's layers: L1 soil (fertility >= 0.25), else L1 infertile soil (sand from 8 °C, gravel below).
string Top(double rain, double temp, double yRel, double floorRain = -1, double dry = 0)
{
    if (temp < -16) return "snow/ice";
    double fert = Fertility(rain, temp, yRel);
    if (floorRain >= 0) fert = SoilFertility.Apply((int)fert, floorRain, dry, temp);
    if (fert >= .25 * 255 && rain >= .08 * 255 && yRel <= .91) return "soil";
    if (temp >= -15 && yRel <= .7) return temp >= 8 ? "sand" : "gravel";
    return "other";
}

var byClass = new SortedDictionary<string, Dictionary<string, double>>();
var byPrecip = new SortedDictionary<int, Dictionary<string, double>>();
var after0 = new SortedDictionary<string, Dictionary<string, double>>();
var afterP = new SortedDictionary<int, Dictionary<string, double>>();
int BinOf(double mm) => mm < 100 ? 0 : mm < 250 ? 100 : mm < 400 ? 250 : mm < 600 ? 400 : mm < 1000 ? 600 : 1000;
double total = 0;
for (double lat = -56; lat <= 72; lat += step)
for (double lon = -180; lon < 180; lon += step)
{
    var ts = data.SampleTerrain(lat, lon);
    if (ts.Ocean > .5) continue;
    double elev = Math.Max(0, ts.Elev);
    var c = data.SampleClimate(lat, lon);
    double lapse = .0065 * elev, t = c.TMean - lapse;
    double y = vertical.Y(elev), yRel = (y - sea) / (height - sea);
    string top = Top(c.Rain, t, yRel), after = Top(c.Rain, t, yRel, c.Rain, c.DrySummer), k = Koppen(c, t, c.TCold - lapse, c.TWarm - lapse);
    double w = Math.Cos(lat * Math.PI / 180); // area weight
    total += w;
    if (!byClass.TryGetValue(k, out var d)) byClass[k] = d = new();
    d[top] = d.GetValueOrDefault(top) + w;
    if (!after0.TryGetValue(k, out var a)) after0[k] = a = new();
    a[after] = a.GetValueOrDefault(after) + w;
    if (!afterP.TryGetValue(BinOf(c.Precip), out var ap)) afterP[BinOf(c.Precip)] = ap = new();
    ap[after] = ap.GetValueOrDefault(after) + w;
    int bin = c.Precip < 100 ? 0 : c.Precip < 250 ? 100 : c.Precip < 400 ? 250 : c.Precip < 600 ? 400 : c.Precip < 1000 ? 600 : 1000;
    if (!byPrecip.TryGetValue(bin, out var p)) byPrecip[bin] = p = new();
    p[top] = p.GetValueOrDefault(top) + w;
}
string[] tops = { "soil", "sand", "gravel", "snow/ice", "other" };
void Table(string title, IEnumerable<(string key, Dictionary<string, double> d)> rows)
{
    Console.WriteLine($"| {title} | land share | " + string.Join(" | ", tops) + " |");
    Console.WriteLine("| --- | ---: | " + string.Join(" | ", tops.Select(_ => "---:")) + " |");
    foreach (var (key, d) in rows)
    {
        double n = d.Values.Sum();
        Console.WriteLine($"| {key} | {100 * n / total:F1}% | " + string.Join(" | ", tops.Select(x => $"{100 * d.GetValueOrDefault(x) / n:F0}%")) + " |");
    }
}
Console.WriteLine($"Land 56°S-72°N, {step}° grid, area-weighted. World height {height}, sea level {sea}.\n");
Table("Climate class", byClass.Select(p => (p.Key, p.Value)));
Console.WriteLine();
Table("Precipitation (mm)", byPrecip.Select(p => (p.Key == 1000 ? "1000+" : p.Key + "+", p.Value)));
Console.WriteLine($"\nWith Earth's soil fertility floor (decision 0048, floor {SoilFertility.Floor}/255 where the aridity index is {SoilFertility.DryLandAridity} or more):\n");
Table("Climate class", after0.Select(p => (p.Key, p.Value)));
Console.WriteLine();
Table("Precipitation (mm)", afterP.Select(p => (p.Key == 1000 ? "1000+" : p.Key + "+", p.Value)));
