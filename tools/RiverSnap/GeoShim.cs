namespace EarthMap.Core
{
    /// <summary>
    /// The one member of EarthData.cs's Geo that the linked FineElevation.cs uses, with the same arithmetic. Linking
    /// EarthData.cs itself would pull in most of the mod.
    /// </summary>
    public static class Geo
    {
        public static double Wrap180(double lon)
        {
            lon %= 360;
            if (lon >= 180) lon -= 360;
            if (lon < -180) lon += 360;
            return lon;
        }
    }
}
