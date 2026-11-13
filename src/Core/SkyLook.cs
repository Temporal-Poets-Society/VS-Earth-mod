using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's look for AstraTerra's sky, as the settings are named and written in astraterra.json: a round sun and a
    /// telescope moon, both nearer their real size, a dimmer Milky Way, and a naked-eye star field with the bright stars
    /// standing out. Suggested to AstraTerra in Earth worlds (AstraTerraLook); the player's own settings win.
    /// </summary>
    public static class SkyLook
    {
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Settings = new[]
        {
            new KeyValuePair<string, string>("SunArt", "pixel"),
            new KeyValuePair<string, string>("SunSizeDeg", "3"),
            new KeyValuePair<string, string>("MoonArt", "telescope"),
            new KeyValuePair<string, string>("MoonSizeDeg", "3"),
            new KeyValuePair<string, string>("MilkyWayBrightness", "0.3"),
            new KeyValuePair<string, string>("BrightStarEmphasis", "1"),
            new KeyValuePair<string, string>("StarLimitMagnitude", "5"),
            new KeyValuePair<string, string>("FaintStarBrightness", "0.25"),
        };
    }
}
