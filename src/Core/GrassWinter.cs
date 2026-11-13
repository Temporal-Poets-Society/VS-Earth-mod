using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Tall grass through the winter: snow flattens it, and it grows back in spring. Vanilla puts snow on a tuft and gives the
    /// same tuft back when the snow melts. Here a tuft that comes out of the snow is lying flat: at most "short", dead and
    /// brown (the season map, <see cref="GrassPhenology"/>), and still cut for dry grass, as matted dead grass is gathered for
    /// tinder in a real spring. Once the month's mean at the ground reaches <see cref="GrowingBase"/>, it grows back a stage
    /// at a time to the height vanilla's block layers give the place (<see cref="TargetHeight"/>): about a stage a third of a
    /// month, faster in warmer months. Nothing is removed. Bare grassy soil sprouts a very short tuft again in growing
    /// months where vanilla's world generation put grass (<see cref="Covers"/>): vanilla only places tufts while a soil's grass
    /// cover is still growing, so grass burnt by fire, eaten or cut never came back. New worlds only (EarthParams.GrassWinterRules).
    /// </summary>
    public static class GrassWinter
    {
        public const int RulesVersion = 1;
        public const double GrowingBase = 5;
        /// <summary>vanilla's tall grass heights, shortest first (worldproperties/block/tallgrass.json, without "eaten").</summary>
        public static readonly string[] Heights = { "veryshort", "short", "mediumshort", "medium", "tall", "verytall" };
        /// <summary>The tallest a tuft comes out of the snow.</summary>
        public const int Flattened = 1;

        public static int HeightIndex(string height) => Array.IndexOf(Heights, height);

        /// <summary>The height a tuft has when its snow melts (<paramref name="height"/> -1, e.g. "eaten": short).</summary>
        public static int AfterSnow(int height) => height < 0 ? Flattened : Math.Min(height, Flattened);

        /// <summary>
        /// The height vanilla's block layers give tall grass at a place (survival worldgen/blocklayers.json "tallgrass", first
        /// match): yearly mean temperature (°C at the ground), rainfall 0..1 and forest density 0..1. -1: none.
        /// </summary>
        public static int TargetHeight(double temperature, double rain, double forest)
        {
            if (temperature >= 20 && rain >= 0.4 && forest <= 0.3) return 5;
            if (temperature >= 15 && rain >= 0.4 && forest <= 0.4) return 4;
            if (temperature >= 2 && rain >= 0.4 && forest <= 0.6) return 3;
            if (temperature >= -7 && rain >= 0.4 && forest <= 0.8) return 2;
            if (temperature >= -10 && rain >= 0.35) return 1;
            if (temperature >= -16 && rain >= 0.2) return 0;
            return -1;
        }

        /// <summary>
        /// Whether vanilla's world generation puts tall grass on this column (GenBlockLayers.PlaceTallGrass), with a fixed hash
        /// of the position for its density noise and dice: the noise must beat the forest density less the warm, wet bonus,
        /// and the soil's fertility (0..100) must beat a die. Bare grassy soil sprouts again only where this holds, so fire,
        /// grazing and cutting give back the grass the place had, and a forest floor stays as sparse as vanilla made it.
        /// <paramref name="temperature"/>: yearly mean at the ground (°C); <paramref name="rain"/> and <paramref name="forest"/> 0..1.
        /// </summary>
        public static bool Covers(int x, int z, double forest, double rain, double temperature, int fertility)
        {
            double tempRel = Math.Clamp((temperature + 20) / 60, 0, 1);   // the climate map's byte, (T + 20) * 4.25 / 255
            double threshold = Math.Clamp(forest - Math.Max(0, rain * tempRel - 0.25), 0.05, 0.99);
            return 1.15 * Hash(x, z, 0x4F1BBCDCBFA53E0BUL) > threshold && 100 * Hash(x, z, 0x2545F4914F6CDD1DUL) < fertility;
        }

        static double Hash(int x, int z, ulong salt)
        {
            unchecked
            {
                ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)z * 0xC2B2AE3D27D4EB4FUL ^ salt;
                h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 27; h *= 0x94D049BB133111EBUL; h ^= h >> 31;
                return (h >> 11) * (1.0 / (1UL << 53));
            }
        }

        /// <summary>Chance that one of vanilla's random soil ticks grows the tuft a stage: about three stages a month at
        /// 10 °C (vanilla gives a soil block about 4.7 random ticks a game day), from a quarter of that at 5 °C to one and a
        /// half times in hot months.</summary>
        public static double GrowChance(double monthMean, int daysPerMonth)
        {
            if (!(monthMean >= GrowingBase)) return 0;
            double perMonth = 3 * Math.Clamp((monthMean - GrowingBase) / 5, 0.25, 1.5);
            return Math.Clamp(perMonth / (Math.Max(1, daysPerMonth) * 4.7), 0, 1);
        }

        /// <summary>The most <see cref="GrowChance"/> gives in any month (from 12.5 °C): a random draw above it grows nothing
        /// whatever the month, so a soil tick can skip the weather for it.</summary>
        public static double MaxGrowChance(int daysPerMonth) => GrowChance(GrowingBase + 7.5, daysPerMonth);
    }
}
