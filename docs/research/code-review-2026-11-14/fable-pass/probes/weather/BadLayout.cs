using System;
using EarthMap.Core;

static partial class P
{
    // what a client's model answers when the server's layout numbers are unusable (EarthWeather.Watch does not check them)
    static void BadLayout()
    {
        foreach (var (name, make) in new (string, Func<EarthGeometry>)[]
        {
            ("blocks per degree 0", () => new EarthGeometry { RingStart = 0, RingLength = 204800, Wraps = true, EquatorZ = 51200, BlocksPerDegreeZ = 0, SeamLon0 = -169.25 }),
            ("blocks per degree NaN", () => new EarthGeometry { RingStart = 0, RingLength = 204800, Wraps = true, EquatorZ = 51200, BlocksPerDegreeZ = double.NaN, SeamLon0 = -169.25 }),
            ("ring length NaN", () => new EarthGeometry { RingStart = 0, RingLength = double.NaN, Wraps = true, EquatorZ = 51200, BlocksPerDegreeZ = 568, SeamLon0 = -169.25 }),
            ("ring length 0", () => new EarthGeometry { RingStart = 0, RingLength = 0, Wraps = true, EquatorZ = 51200, BlocksPerDegreeZ = 568, SeamLon0 = -169.25 }),
        })
        {
            string result;
            try
            {
                var m = new WeatherModel(data.Weather, make(), new WeatherSettings { Rules = 9 }, 1) { Ground = ground };
                double c = m.Cloudness(100000, 40000, 5000);
                var w = m.WindAt(100000, 40000, 5000, true);
                result = $"cloudness {c}, wind {w.X},{w.Z}, anomaly {m.TemperatureAnomaly(100000, 40000, 5000, 0)}";
            }
            catch (Exception e) { result = "throws " + e.GetType().Name + ": " + e.Message; }
            Console.WriteLine($"bad layout, {name}: {result}");
        }
        foreach (double sf in new[] { double.NaN, double.PositiveInfinity })
        {
            var m = New(9, 1, s => s.StormFrequency = sf);
            geo.FromGeo(45, 10, out double x, out double z);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double c = m.Cloudness(x, z, 5000);
            Console.WriteLine($"storm frequency {sf}: cloudness {c}, {m.Storms.AliveIn(5000).Length} storms alive, first lookup {sw.ElapsedMilliseconds} ms");
        }
    }
}
