namespace EarthMap.Core
{
    public static class MarineEcology
    {
        // The sea is a separate water kind: saline inland lakes are not marine habitat.
        public static bool CoralHabitat(in TerrainModel.Column c, in EarthData.ClimateSample climate)
            => !c.Gutter && !c.River && c.Water == TerrainModel.WaterSea && c.WaterTop > c.Top
                && climate.TWarm >= 20 && climate.TMean >= 18;
    }
}
