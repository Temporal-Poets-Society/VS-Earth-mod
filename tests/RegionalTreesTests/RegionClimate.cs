using EarthMap.Core;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Writes the climate distribution of every TDWG botanical country from Earth's baked climate (earthdata/), which
/// tools/bake_tree_regions.py turns into per-species Earth climate envelopes (native regions × real climate).
/// Real surface temperature (sea-level-equivalent yearly mean − 6.5 °C/km × elevation, 4×4 elevation samples per
/// 0.25° climate cell) and VS rainfall, weighted by area × forest potential (at least 0.05, so desert species count).
/// </summary>
static class RegionClimate
{
    public const double TempMin = -40, TempStep = 0.5, RainStep = 5;

    public static void Write(Harness h, string output)
    {
        var geo = new BotanicalRegions(h.RegionsJson);
        int n = geo.Count;
        var temp = new double[n][];
        var rain = new double[n][];
        for (int i = 0; i < n; i++) { temp[i] = new double[(int)((80 - TempMin) / TempStep) + 1]; rain[i] = new double[(int)(256 / RainStep) + 1]; }
        var total = new double[n];
        var data = h.Data;
        int res = data.ClimateRes;
        void Add(int region, double t, double r, double w)
        {
            int ti = Math.Clamp((int)Math.Floor((t - TempMin) / TempStep), 0, temp[region].Length - 1);
            int ri = Math.Clamp((int)Math.Floor(r / RainStep), 0, rain[region].Length - 1);
            temp[region][ti] += w; rain[region][ri] += w; total[region] += w;
        }
        object gate = new();
        Parallel.For(0, data.ClimateH, row =>
        {
            double lat = 90 - (row + 0.5) / res;
            var lt = new double[n][]; var lr = new double[n][]; var tot = new double[n];
            for (int col = 0; col < data.ClimateW; col++)
            {
                double lon = -180 + (col + 0.5) / res;
                if (!h.IsLand(lat, lon)) continue;
                int region = geo.Find(lat, lon);
                if (region < 0) continue;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    double la = lat + ((sy + 0.5) / 4 - 0.5) / res, lo = lon + ((sx + 0.5) / 4 - 0.5) / res;
                    double fp = h.ForestPotential(la, lo, out var cs, out double elev);
                    double w = Math.Cos(la * Math.PI / 180) * Math.Max(fp, 0.05) / 16;
                    double t = cs.TMean - 0.0065 * elev;
                    lt[region] ??= new double[temp[0].Length]; lr[region] ??= new double[rain[0].Length];
                    lt[region][Math.Clamp((int)Math.Floor((t - TempMin) / TempStep), 0, temp[0].Length - 1)] += w;
                    lr[region][Math.Clamp((int)Math.Floor(cs.Rain / RainStep), 0, rain[0].Length - 1)] += w;
                    tot[region] += w;
                }
            }
            lock (gate)
                for (int i = 0; i < n; i++)
                {
                    if (lt[i] == null) continue;
                    for (int k = 0; k < lt[i].Length; k++) temp[i][k] += lt[i][k];
                    for (int k = 0; k < lr[i].Length; k++) rain[i][k] += lr[i][k];
                    total[i] += tot[i];
                }
        });
        // botanical countries too small for the land mask at 0.25° (islets): sample their polygons' outline points
        var file = JsonSerializer.Deserialize<BotanicalRegions.RegionFile>(h.RegionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        int sampled = 0;
        foreach (var region in file.Regions)
        {
            int i = geo.IndexOf(region.Code);
            if (total[i] > 0) continue;
            sampled++;
            foreach (var poly in region.Polygons)
            {
                var ring = poly[0];
                int stride = Math.Max(1, ring.Length / 20);
                for (int k = 0; k < ring.Length; k += stride)
                {
                    double la = ring[k][1], lo = ring[k][0];
                    var cs = data.SampleClimate(la, lo);
                    double elev = Math.Max(0, data.ElevBilinear(la, lo));
                    Add(i, cs.TMean - 0.0065 * elev, cs.Rain, 1e-4);
                }
            }
        }
        string Sha(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(h.Root, "earthdata", name)))).ToLowerInvariant();
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("{\"version\":1,\"tempMin\":").Append(TempMin.ToString(inv)).Append(",\"tempStep\":").Append(TempStep.ToString(inv))
          .Append(",\"rainStep\":").Append(RainStep.ToString(inv))
          .Append(",\"weighting\":\"cos(lat) x max(forest potential, 0.05); real surface temperature (4x4 elevation samples per 0.25 deg cell); VS rainfall\"")
          .Append(",\"climateSha256\":\"").Append(Sha("climate.bin")).Append("\",\"terrainSha256\":\"").Append(Sha("terrain.bin")).Append("\",\"regions\":{");
        bool firstRegion = true;
        for (int i = 0; i < n; i++)
        {
            if (total[i] <= 0) continue;
            if (!firstRegion) sb.Append(',');
            firstRegion = false;
            sb.Append('"').Append(geo.Code(i)).Append("\":{\"w\":").Append(total[i].ToString("G5", inv)).Append(",\"t\":[");
            AppendHist(sb, temp[i], total[i]);
            sb.Append("],\"r\":[");
            AppendHist(sb, rain[i], total[i]);
            sb.Append("]}");
        }
        sb.Append("}}\n");
        File.WriteAllText(output, sb.ToString());
        Console.WriteLine($"Region climate for {n} botanical countries ({sampled} from outline samples) written to {output}.");
    }

    static void AppendHist(StringBuilder sb, double[] hist, double total)
    {
        bool first = true;
        for (int k = 0; k < hist.Length; k++)
        {
            if (hist[k] <= 0) continue;
            if (!first) sb.Append(',');
            first = false;
            // relative weight, 1e-6 resolution
            sb.Append('[').Append(k).Append(',').Append(Math.Max(1, (int)Math.Round(hist[k] / total * 1e6))).Append(']');
        }
    }
}
