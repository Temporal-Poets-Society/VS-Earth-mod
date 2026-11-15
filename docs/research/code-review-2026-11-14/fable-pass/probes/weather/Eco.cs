using System;
using EarthMap.Core;

static partial class P
{
    // the live temperature's season term at the equator: EarthClimateHooks.SeasonCorrection takes the northern season curve
    // for lat > 0 and the southern one otherwise, each times the place's real swing (warmest minus coldest month), which is not
    // 0 at the equator: the step across lat 0 is swing * (sNorth - sSouth), the whole swing in mid-January and mid-July
    static partial void Eco(string[] args)
    {
        double sum = 0, max = 0, maxLon = 0; int n = 0, over1 = 0, over2 = 0;
        for (double lon = -180; lon < 180; lon += 0.05)
        {
            if (data.Weather.Sample(0, lon, 0.5).Land < 0.5) continue;
            var cs = data.SampleClimate(0.0, lon);
            double swing = cs.TWarm - cs.TCold;
            sum += swing; n++;
            if (swing > 1) over1++;
            if (swing > 2) over2++;
            if (swing > max) { max = swing; maxLon = lon; }
        }
        Console.WriteLine($"equator, land ({n} points 0.05 deg apart): warmest minus coldest month {sum / n:0.00} C on average, {max:0.00} C at most (lon {maxLon:0.00}); over 1 C at {100.0 * over1 / n:0} %, over 2 C at {100.0 * over2 / n:0} % of the land on the equator");
        foreach (var (name, lon) in new[] { ("Kampala's meridian", 32.6), ("Nairobi's meridian (Mt Kenya)", 37.0), ("Quito's meridian", -78.5), ("Macapa", -51.1), ("Pontianak", 109.3), ("Libreville", 9.5), ("Sumatra", 100.4) })
        {
            var cs = data.SampleClimate(0.0, lon);
            Console.WriteLine($"  {name}: swing {cs.TWarm - cs.TCold:0.00} C (the live temperature steps by this across the equator in mid-January and mid-July)");
        }
    }
}
