using System;
using System.Diagnostics;
using System.Reflection;

// Vanilla's own numbers, from the game's VintagestoryAPI.dll (loaded by reflection, read only; run on the .NET 10 runtime the
// game uses): the world-wide temperature noise of ModTemperature (YearlyTemperatureNoise * 3 + DailyTemperatureNoise, both
// at (totalDays, 0)) and what one vanilla rain noise reading costs.
static class V
{
    static Type simplex;

    static object Make(int octaves, double freq, double persistence, long seed)
        => simplex.GetMethod("FromDefaultOctaves", new[] { typeof(int), typeof(double), typeof(double), typeof(long) }).Invoke(null, new object[] { octaves, freq, persistence, seed });

    static Func<double, double, double> N2(object noise)
        => (Func<double, double, double>)Delegate.CreateDelegate(typeof(Func<double, double, double>), noise, simplex.GetMethod("Noise", new[] { typeof(double), typeof(double) }));

    static Func<double, double, double, double> N3(object noise)
        => (Func<double, double, double, double>)Delegate.CreateDelegate(typeof(Func<double, double, double, double>), noise, simplex.GetMethod("Noise", new[] { typeof(double), typeof(double), typeof(double) }));

    static void Main(string[] args)
    {
        var api = Assembly.LoadFrom(args[0]);
        simplex = api.GetType("Vintagestory.API.MathTools.SimplexNoise", true);
        Console.WriteLine($"runtime {Environment.Version}, {api.GetName().Name} {api.GetName().Version}");
        foreach (int seed in new[] { 1, 12345, 987654 })
        {
            var yearly = N2(Make(3, 0.001, 0.95, seed + 12109));
            var daily = N2(Make(3, 1.0, 0.95, seed + 128109));
            double sum = 0, sum2 = 0, min = 1e9, max = -1e9, sumD = 0, sumD2 = 0, minD = 1e9, maxD = -1e9, sumT = 0, sumT2 = 0;
            int n = 0;
            // 200 game years of 108 days, every 3 hours
            for (double d = 0; d < 108 * 200; d += 0.125)
            {
                double y = yearly(d, 0.0) * 3.0, dd = daily(d, 0.0), t = y + dd;
                sum += y; sum2 += y * y; min = Math.Min(min, y); max = Math.Max(max, y);
                sumD += dd; sumD2 += dd * dd; minD = Math.Min(minD, dd); maxD = Math.Max(maxD, dd);
                sumT += t; sumT2 += t * t;
                n++;
            }
            double mean = sum / n, sd = Math.Sqrt(sum2 / n - mean * mean), meanD = sumD / n, sdD = Math.Sqrt(sumD2 / n - meanD * meanD), meanT = sumT / n, sdT = Math.Sqrt(sumT2 / n - meanT * meanT);
            double worstYear = 0, sumYear2 = 0;
            for (int yr = 0; yr < 200; yr++)
            {
                double s = 0; int k = 0;
                for (double d = yr * 108; d < (yr + 1) * 108; d += 0.25) { s += yearly(d, 0.0) * 3.0 + daily(d, 0.0); k++; }
                worstYear = Math.Max(worstYear, Math.Abs(s / k));
                sumYear2 += (s / k) * (s / k);
            }
            Console.WriteLine($"seed {seed}: yearly noise x3: mean {mean:0.00}, sd {sd:0.00}, {min:0.0}..{max:0.0} C; daily noise: mean {meanD:0.00}, sd {sdD:0.00}, {minD:0.0}..{maxD:0.0} C; together sd {sdT:0.00} C; whole game years are {Math.Sqrt(sumYear2 / 200):0.00} C off (rms), the worst of 200 by {worstYear:0.0} C");
        }
        var p = N3(Make(4, 1.0 / 150.0, 0.95, 12345 - 18971121));
        var ps = N3(Make(3, 1.0 / 750.0, 0.95, 12345 - 1717121));
        var tn = N2(Make(3, 0.001, 0.95, 5));
        var dn = N2(Make(3, 1.0, 0.95, 6));
        double sink = 0;
        for (int warm = 0; warm < 2; warm++)
        {
            var sw = Stopwatch.StartNew();
            int calls = 2000000;
            for (int i = 0; i < calls; i++)
            {
                double posX = 100000 + (i & 63), posZ = 50000 + ((i >> 6) & 63), totalDays = 200 + i * 1e-6;
                sink += Math.Max(p(posX / 9.0 / 2.0 + totalDays * 18.0, posZ / 9.0 / 2.0, totalDays * 4.0) * 1.6
                    - Math.Clamp(ps(posX / 4.0 / 2.0 + totalDays * 24.0, posZ / 4.0 / 2.0, totalDays * 6.0) * 5.0 - 1.0 - 0.2, 0.0, 1.0) + 0.2, 0.0);
            }
            if (warm == 1) Console.WriteLine($"vanilla's rain cloudness noise: {sw.Elapsed.TotalMilliseconds * 1000 / calls:0.000} us a reading");
            sw.Restart();
            for (int i = 0; i < calls; i++) sink += tn(200 + i * 1e-6, 0.0) * 3.0 + dn(200 + i * 1e-6, 0.0);
            if (warm == 1) Console.WriteLine($"vanilla's temperature noises: {sw.Elapsed.TotalMilliseconds * 1000 / calls:0.000} us a reading");
        }
        if (sink == 0) Console.WriteLine();
    }
}
