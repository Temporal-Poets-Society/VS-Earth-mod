using System;
using System.Diagnostics;
using EarthMap.Core;

static partial class P
{
    // what the first lookup in a new 12-hour storm slot costs the thread that asks (the game's main thread, usually)
    static void Slots()
    {
        foreach (double freq in new[] { 1.0, 5.0 })
        {
            var m = New(9, 777, s => s.StormFrequency = freq);
            geo.FromGeo(45, 10, out double x, out double z);
            var sw = Stopwatch.StartNew();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            m.Cloudness(x, z, 5000);
            double first = sw.Elapsed.TotalMilliseconds; long firstBytes = GC.GetAllocatedBytesForCurrentThread() - b0;
            double worst = 0, sum = 0; int n = 0; long bytes = 0; int alive = 0;
            for (int k = 1; k <= 200; k++)
            {
                double h = 5000 + k * 12;
                b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                m.Cloudness(x, z, h);
                double ms = sw.Elapsed.TotalMilliseconds;
                bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                worst = Math.Max(worst, ms); sum += ms; n++;
                alive += m.Storms.AliveIn(h).Length;
            }
            // and a lookup in a slot already built
            sw.Restart();
            for (int i = 0; i < 100000; i++) m.Cloudness(x + (i & 63), z, 5000 + 12 * 200 + 3);
            double warm = sw.Elapsed.TotalMilliseconds * 1000 / 100000;
            Console.WriteLine($"storm slots, StormFrequency {freq}: the first lookup of a world {first:0.0} ms ({firstBytes / 1024} kB); the first lookup in each next 12-hour slot {sum / n:0.00} ms on average, {worst:0.00} ms at most, {bytes / n / 1024} kB; {alive / n} storms alive in a slot; a lookup in a built slot {warm:0.00} us");
        }
    }
}
