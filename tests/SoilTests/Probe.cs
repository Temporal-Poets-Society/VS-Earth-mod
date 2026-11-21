using System.Globalization;
using EarthMap.Core;

/// <summary>
/// Soil rules 4, every input along a line, to see which one draws an edge (EA-0199, the author's rc.12 look). Run:
/// SoilTests &lt;root&gt; probe &lt;ModData/earth&gt; lat0 lon0 lat1 lon1 steps
/// Prints per point: the soil class and which map gave it, the ambient rain and aridity, the flood's level and GLO-90's
/// height, the fresh wetland share (oases), GUM's unit, the temperatures and both tier sets' decisions.
/// </summary>
static class Probe
{
    public static int Run(string root, string dataFolder, double lat0, double lon0, double lat1, double lon1, int steps)
    {
        string data(string name) => Path.Combine(dataFolder, "data", name);
        GroundTile.BudgetBytes = 2L << 30;
        var ts = Topsoil.Load(File.ReadAllText(Path.Combine(root, "assets/earth/config/soil-rules-v4.json")),
            a => { var f = data(Path.GetFileName(a.Split(':')[1])); return File.Exists(f) ? File.ReadAllBytes(f) : null; }, 1234, f => Path.Combine(dataFolder, f));
        var earth = EarthData.LoadFromFolder(root, true);
        earth.SetFineTemperature(true); earth.SetFineRain(true); earth.SetClimateFix(true);
        var floods = FloodplainGrid.Read(File.ReadAllBytes(data("floodplains.bin")));
        var wet = WetlandGrid.Read(File.ReadAllBytes(data("wetlands.bin")));
        var dem = new Dictionary<(int, int), DemTiles.Tile>();
        double Glo90(double lat, double lon)
        {
            int s = (int)Math.Floor(lat), w = (int)Math.Floor(lon);
            if (!dem.TryGetValue((s, w), out var t))
            {
                string p = Path.Combine(dataFolder, "glo90", DemTiles.ShortName(s, w) + ".egl");
                dem[(s, w)] = t = File.Exists(p) ? DemTiles.Decode(File.ReadAllBytes(p)) : null;
            }
            if (t == null) return double.NaN;
            return t.At(Math.Clamp((int)((s + 1 - lat) * t.Rows), 0, t.Rows - 1), Math.Clamp((int)((lon - w) * t.Cols), 0, t.Cols - 1));
        }
        string N(Topsoil.Tier t) => t switch { Topsoil.Tier.Vanilla => "vanilla", Topsoil.Tier.None => "none", Topsoil.Tier.BlackEarth => "blackearth", _ => Topsoil.TierNames[(int)t] };
        Console.WriteLine("lat lon | class (map) | rain aridity | flood level, GLO-90 m | wet share | GUM | T, warmest | realistic / vanilla");
        for (int i = 0; i <= steps; i++)
        {
            double f = steps == 0 ? 0 : (double)i / steps, lat = lat0 + (lat1 - lat0) * f, lon = lon0 + (lon1 - lon0) * f;
            var cs = earth.SampleClimate(lat, lon);
            double e = Glo90(lat, lon);
            ts.Soils.TryCategory(GroundColumns.FieldNames.Soil, lat, lon, 1234, out string cls, out string src);
            ts.Soils.TryCategory(GroundColumns.FieldNames.Cover, lat, lon, 1234, out string gum, out _);
            double level = floods.Level(lat, lon);
            double ws = wet.Share(WetlandGrid.Marsh, lat, lon) + wet.Share(WetlandGrid.SwampForest, lat, lon) + wet.Share(WetlandGrid.Damp, lat, lon);
            var p = new Topsoil.Place
            {
                Class = cls, Temperature = cs.TMean - .0065 * Math.Max(0, double.IsNaN(e) ? 0 : e), WarmestMonth = cs.TWarm - .0065 * Math.Max(0, double.IsNaN(e) ? 0 : e),
                Arid = SoilFertility.AridGround(cs.Rain, cs.DrySummer, cs.PenmanFactor), Flood = !double.IsNaN(level) && !double.IsNaN(e) && e <= level, Dune = ts.DuneAt(lat, lon),
            };
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.0000} {1:0.0000} | {2} ({3}) | {4:0.0} {5:0.000}{6} | {7} {8:0.0} | {9:0.00} | {10} | {11:0.0} {12:0.0} | {13} / {14}",
                lat, lon, cls ?? "-", src?.Split(' ')[0] ?? "-", cs.Rain, cs.Aridity, p.Arid ? " arid" : "", double.IsNaN(level) ? "-" : level.ToString("0.0", CultureInfo.InvariantCulture), e, ws, gum ?? "-",
                p.Temperature, p.WarmestMonth, N(ts.Decide(Topsoil.RealisticTiers, p)), N(ts.Decide(Topsoil.VanillaTiers, p))));
        }
        return 0;
    }
}
