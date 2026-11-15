using System;
using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;

static partial class P
{
    // the equator: the steering wind's step there and what it does to the rain, clouds and pressure a block apart
    static void Equator(int rules)
    {
        var m = New(rules);
        var r = new Random(21);
        double sumStep = 0, maxStep = 0; int n = 0;
        // the steering wind (km/h east) a hair north and south of the equator, by longitude and month
        for (int lonI = -180; lonI < 180; lonI += 2)
            for (int month = 0; month < 12; month++)
            {
                double itcz = data.Weather.ItczLat(lonI, (month + 0.5) / 12);
                WeatherModel.SteeringKmh(1e-9, itcz, out double eN, out double nN);
                WeatherModel.SteeringKmh(-1e-9, itcz, out double eS, out double nS);
                double step = Math.Sqrt((eN - eS) * (eN - eS) + (nN - nS) * (nN - nS));
                sumStep += step; maxStep = Math.Max(maxStep, step); n++;
            }
        Console.WriteLine($"equator, rules {rules}: the steering wind steps by {sumStep / n:0.0} km/h on average across lat 0 ({maxStep:0.0} at most; over 180 longitudes x 12 months)");
        foreach (var (name, latLine) in new[] { ("lat 0 (the equator)", 0.0), ("lat 3 N (control)", 3.0), ("lat 3 S (control)", -3.0) })
        {
            int pairs = 0, rainOneSide = 0, cloudOneSide = 0; double sumDc = 0, sumDp2 = 0, maxDp = 0, sumDcover = 0, maxDc = 0, sumDa = 0, maxDa = 0;
            for (int i = 0; i < 40000; i++)
            {
                double lon = r.NextDouble() * 360 - 180, h = 3000 + r.NextDouble() * 108 * 24 * 3;
                geo.FromGeo(latLine, lon, out double x, out double z);
                // one block apart, north and south of the line
                var a = m.Sample(x, z - 0.5, h); var b = m.Sample(x, z + 0.5, h);
                pairs++;
                if ((a.Cloudness > 0.5) != (b.Cloudness > 0.5)) rainOneSide++;
                if ((a.Cloudness > 0) != (b.Cloudness > 0)) cloudOneSide++;
                double dc = Math.Abs(a.Cloudness - b.Cloudness), dp = Math.Abs(a.Pressure - b.Pressure);
                sumDc += dc; maxDc = Math.Max(maxDc, dc); sumDp2 += dp * dp; maxDp = Math.Max(maxDp, dp); sumDcover += Math.Abs(a.CloudCover - b.CloudCover);
                // the anomaly a cell apart (its cells are 32 blocks: 33 blocks apart to be in two)
                double da = Math.Abs(m.TemperatureAnomaly(x, z - 16.5, h, 0) - m.TemperatureAnomaly(x, z + 16.5, h, 0));
                sumDa += da; maxDa = Math.Max(maxDa, da);
            }
            Console.WriteLine($"  {name,-22}: one block apart at {pairs} places and hours: rain on one side only {100.0 * rainOneSide / pairs:0.00} %, rain cloud on one side only {100.0 * cloudOneSide / pairs:0.00} %; " +
                $"cloudness differs by {sumDc / pairs:0.0000} on average ({maxDc:0.00} at most), cloud cover by {sumDcover / pairs:0.0000}, pressure by {Math.Sqrt(sumDp2 / pairs):0.00} hPa rms ({maxDp:0.0} at most); the anomaly 33 blocks apart by {sumDa / pairs:0.00} C ({maxDa:0.0} at most)");
        }
        // a walk across the equator at Kampala's and Quito's meridians and the Amazon's mouth, one moment: every 2 blocks
        foreach (var (name, lon) in new[] { ("Kampala's meridian 32.6 E", 32.6), ("Quito's meridian 78.5 W", -78.5), ("Macapa 51.1 W", -51.1), ("Sumatra 100.4 E", 100.4) })
        {
            // the first hour of the year 2 at which the two sides differ most (searched over 400 hours)
            double bestH = 0, bestD = -1;
            for (int k = 0; k < 400; k++)
            {
                double h = 108 * 24 * 2 + k * 6.37;
                geo.FromGeo(0, lon, out double x, out double z);
                double d = Math.Abs(m.Cloudness(x, z - 0.5, h) - m.Cloudness(x, z + 0.5, h));
                if (d > bestD) { bestD = d; bestH = h; }
            }
            geo.FromGeo(0, lon, out double x0, out double z0);
            var line = new List<string>();
            foreach (int dz in new[] { -600, -200, -50, -10, -2, -1, 1, 2, 10, 50, 200, 600 })
            {
                var s = m.Sample(x0, z0 + dz - Math.Sign(dz) * 0.5, bestH);
                line.Add($"{(dz < 0 ? "N" : "S")}{Math.Abs(dz)}: {s.Cloudness:0.00}/{s.CloudCover:0.00}/{s.Pressure:0}");
            }
            Console.WriteLine($"  {name}, hour {bestH:0.0} (cloudness/cover/hPa by blocks north and south of the equator): {string.Join("  ", line)}");
        }
    }
}
