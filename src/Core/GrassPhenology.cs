using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Which of Earth's grass calendars (tools/make_grass_seasons.py: earthgrass-*.png, and since 2.1.3 the bands of the one
    /// colour map earthgrass-seasons.png, GrassSeasonShader) a place's grass follows, from its
    /// own monthly temperature and rain. Vanilla tints all grass by one calendar ("seasonalGrass"). Here grass is dormant
    /// and brown while the month is below <see cref="GrowingBase"/>, turns a bright fresh green in the weeks after it warms,
    /// settles to summer green and goes to straw after the autumn frosts, so the growing season sets the map. Places with
    /// at least two dry months (precipitation below twice the temperature, Walter and Lieth) in the warm half of the year
    /// and at most one in the cool half get the summer-dry maps: Mediterranean where winter is mild, steppe where it is cold.
    /// The tropics keep the evergreen map: their wet and dry seasons come from the rain byte (<see cref="GrassSeason"/>).
    /// </summary>
    public static class GrassPhenology
    {
        public const double GrowingBase = 5;
        /// <summary>The calendars' codes, indexed by <see cref="MapFor"/> (their bands in GrassSeasonShader's map, in this order;
        /// World Map reads the names, docs/REFLECTION-SURFACE.md). Colour maps of their own until 2.1.2.</summary>
        public static readonly string[] Codes =
        {
            "earthGrassEvergreen", "earthGrassSeason80", "earthGrassSeason66", "earthGrassSeason52", "earthGrassSeason38",
            "earthGrassSummerDry", "earthGrassSteppe",
        };
        public const int Evergreen = 0, SummerDry = 5, Steppe = 6;
        /// <summary>Season lengths (share of the year above <see cref="GrowingBase"/>) of the maps 1..4; the texture tool's
        /// LENGTHS must match.</summary>
        public static readonly double[] Lengths = { 0.80, 0.66, 0.52, 0.38 };

        /// <summary>The share of the year whose (linearly interpolated) monthly mean is at least <see cref="GrowingBase"/>.</summary>
        public static double GrowingShare(ReadOnlySpan<double> monthTemp)
        {
            const int steps = 96;
            int warm = 0;
            for (int i = 0; i < steps; i++)
            {
                double f = (i + 0.5) / steps * 12 - 0.5;
                int a = (int)Math.Floor(f);
                double t = f - a;
                int m0 = (a % 12 + 12) % 12, m1 = (m0 + 1) % 12;
                if (monthTemp[m0] * (1 - t) + monthTemp[m1] * t >= GrowingBase) warm++;
            }
            return warm / (double)steps;
        }

        /// <summary>
        /// The map index for twelve monthly means (°C, January first, at the ground's height) and monthly precipitation
        /// (mm per month). <paramref name="tropical"/>: the coldest month is 15 °C or more.
        /// </summary>
        public static int MapFor(ReadOnlySpan<double> monthTemp, ReadOnlySpan<double> monthPrecip)
        {
            double cold = double.MaxValue;
            for (int m = 0; m < 12; m++) cold = Math.Min(cold, monthTemp[m]);
            if (cold >= 15) return Evergreen;
            double share = GrowingShare(monthTemp);

            // the six warmest months are the warm half
            Span<int> order = stackalloc int[12];
            for (int m = 0; m < 12; m++) order[m] = m;
            for (int i = 1; i < 12; i++)
                for (int j = i; j > 0 && monthTemp[order[j]] > monthTemp[order[j - 1]]; j--) (order[j], order[j - 1]) = (order[j - 1], order[j]);
            int dryWarm = 0, dryCool = 0;
            for (int i = 0; i < 12; i++)
            {
                int m = order[i];
                bool dry = monthTemp[m] >= GrowingBase && monthPrecip[m] < 2 * monthTemp[m];
                if (!dry) continue;
                if (i < 6) dryWarm++; else dryCool++;
            }
            if (dryWarm >= 2 && dryCool <= 1) return share >= 0.92 ? SummerDry : Steppe;

            if (share >= 0.92) return Evergreen;
            int best = 1;
            double bestD = double.MaxValue;
            for (int i = 0; i < Lengths.Length; i++)
            {
                double d = Math.Abs(share - Lengths[i]);
                if (d < bestD) { bestD = d; best = i + 1; }
            }
            return best;
        }
    }
}
