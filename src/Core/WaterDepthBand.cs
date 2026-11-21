namespace EarthMap.Core
{
    /// <summary>
    /// EA-0178: the height band of a creature that spawns in water, read as a depth below its own water's surface.
    ///
    /// Vanilla gives such creatures (fish, and the water animals of other mods) a band of minY..maxY in its relative height:
    /// y / sea level at or below the sea, 1 + (y - sea) / (map height - sea) above it. Every vanilla water body it was written
    /// for has its surface at sea level, so the band is a depth below the surface: freshwater fish in their top 20% of the
    /// sea level's height (0.8..1.0), catfish deeper (0.7..0.9), coelacanths far down (0.3..0.6). Earth's lakes and rivers lie
    /// at their real heights, nearly all above sea level, where that band leaves no water at all: no freshwater fish in any of
    /// them (EA-0178). Here the same band is measured from the water's own surface: the spawn height is moved by the water's
    /// height above (or below) the sea and then tested as vanilla tests it, so water at sea level gets exactly vanilla's test.
    /// </summary>
    public static class WaterDepthBand
    {
        /// <summary>Vanilla's relative height of block height <paramref name="y"/> (GenCreatures.CanSpawnAtConditions and the
        /// runtime spawner both).</summary>
        public static double RelativeY(int y, int seaLevel, int mapHeight) =>
            y > seaLevel ? 1.0 + (double)(y - seaLevel) / (mapHeight - seaLevel) : (double)y / seaLevel;

        /// <summary>
        /// Whether a spawn at block height <paramref name="y"/>, in water whose first block above the liquid is at
        /// <paramref name="surface"/>, lies in the band <paramref name="minY"/>..<paramref name="maxY"/> written for water at sea
        /// level (whose first block above the liquid is the sea level).
        /// </summary>
        public static bool Allows(float minY, float maxY, int y, int surface, int seaLevel, int mapHeight)
        {
            double rel = RelativeY(y - (surface - seaLevel), seaLevel, mapHeight);
            return minY <= rel && rel <= maxY;
        }
    }
}
