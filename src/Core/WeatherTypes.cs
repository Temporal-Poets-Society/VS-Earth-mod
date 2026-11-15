using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) the weather's value types, moved out of WeatherModel.cs unchanged
    /// <summary>Wind at a place: the air moves toward (X, Z) in world axes (+X east, +Z south); the length is the strength in
    /// vanilla's wind units (0 still, 0.15 light breeze, 0.3 medium, 0.6 strong breeze, 1 storm, 1.5 at most). Sea's convention.</summary>
    public readonly struct WeatherWind
    {
        public readonly double X, Z;

        public WeatherWind(double x, double z) { X = x; Z = z; }

        public double Strength => Math.Sqrt(X * X + Z * Z);

        /// <summary>Compass bearing the wind blows from: 0 north, 90 east, 180 south, 270 west.</summary>
        public double FromDegrees => Bearing(-X, -Z);

        public WeatherWind WithStrength(double s)
        {
            double l = Strength;
            return l < 1e-9 ? new WeatherWind(s, 0) : new WeatherWind(X / l * s, Z / l * s);
        }

        /// <summary>Positive = clockwise seen from above (compass direction increases).</summary>
        public WeatherWind Rotated(double radians)
        {
            double c = PortableMath.Cos(radians), s = PortableMath.Sin(radians);
            return new WeatherWind(X * c - Z * s, X * s + Z * c);
        }

        public static WeatherWind operator +(WeatherWind a, WeatherWind b) => new WeatherWind(a.X + b.X, a.Z + b.Z);
        public static WeatherWind operator *(WeatherWind a, double k) => new WeatherWind(a.X * k, a.Z * k);

        /// <summary>Compass bearing of a direction in world axes (north = -Z).</summary>
        public static double Bearing(double x, double z)
        {
            if (x * x + z * z < 1e-18) return 0;
            double deg = PortableMath.Atan2(x, -z) * 180 / Math.PI;
            return deg < 0 ? deg + 360 : deg;
        }

        static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        public static string CompassName(double bearing) => Points[(int)Math.Round((bearing % 360 + 360) % 360 / 22.5) % 16];

        public override string ToString() => $"{Strength:0.00} from {CompassName(FromDegrees)}";
    }

    /// <summary>The weather's settings for one world (from the config; the same on every side).</summary>
    public sealed class WeatherSettings
    {
        /// <summary>Weather hours per game hour: 1 = fronts and storms move and change at their real speed (km/h, with the map's
        /// own kilometres per block), so a rainy morning lasts a morning. Higher: more weather per season, faster changes.</summary>
        public double Pace = 1;
        /// <summary>Multiplies how often it rains everywhere (the real monthly rain chance at 1).</summary>
        public double RainFrequency = 1;
        /// <summary>Multiplies how often storms form (real yearly counts at 1).</summary>
        public double StormFrequency = 1;
        /// <summary>Strength of the day-to-day wind changes on top of the prevailing winds.</summary>
        public double Variability = 1;
        /// <summary>Strength of the gusts.</summary>
        public double Gusts = 1;
        public double DaysPerYear = 108;
        public double HoursPerDay = 24;
        /// <summary>The world's weather rules (EarthParams.WeatherModelRules; EarthWeather.RulesVersion is the newest): what
        /// the model does beyond rules 5. 6 (new worlds from 13 November 2026): the low cloud decks over cold upwelling seas
        /// (EA-0023), hail from Earth's thunderstorms (EA-0019) and the weather's own warm and cold spells, cloud and rain in
        /// the temperature (EA-0025). 7 (new worlds from 14 November 2026): the ground under the weather (Core.WeatherGround:
        /// the rain rate at the ground's temperature, EA-0021; the desert coasts as dry as climate fix 4, EA-0060) and more
        /// thunder in hot dry months (Core.Thunder, EA-0022). 8 (new worlds from 14 November 2026): the rain in events as
        /// long as at the airports, through the whole day, and skewed in warm months (EA-0020: WeatherModel.FastShare,
        /// DiurnalAmplitude, RainSkew); the temperature anomaly the same over 32-block cells and ten-minute steps, kept per
        /// thread (EA-0073), and hail that hurts (EA-0093). 9: more thunder over West and Central Africa (EA-0074). Worlds made
        /// before keep 5.</summary>
        public int Rules = Newest;
        /// <summary>The newest weather rules this build has (EarthWeather.RulesVersion).</summary>
        public const int Newest = 9;
        /// <summary>What <see cref="Rules"/> turns on (ER-10).</summary>
        public WeatherRules Has => WeatherRules.For(Rules);
    }

    /// <summary>(Weather rules 7) Fog at one place and moment (WeatherModel.FogAt).</summary>
    public struct WeatherFog
    {
        /// <summary>0..1: fog lying on the ground or the sea (a layer the game draws as flat fog).</summary>
        public double Ground;
        /// <summary>0..1: how much the air itself is thick (the game's distance fog): in fog and heavy rain.</summary>
        public double Air;
        /// <summary>Whether the ground fog is the cold sea's (a layer from the sea up) rather than the land's own.</summary>
        public bool Sea;
    }

    /// <summary>Everything about the weather at one place and moment (WeatherModel.Sample).</summary>
    public struct WeatherSample
    {
        public double Lat, Lon, YearRel;
        /// <summary>The month's normal precipitation here, mm/day.</summary>
        public double NormalPrecip;
        /// <summary>The month's normal temperature (sea-level equivalent), °C.</summary>
        public double NormalTemp;
        /// <summary>0..1: how much of the time it rains here this month (the climate's rain chance).</summary>
        public double RainChance;
        /// <summary>0..1: share of showers and thunderstorms (vs. long frontal rain).</summary>
        public double Convective;
        /// <summary>Vanilla's "rain cloudness": above 0.5 it rains (precipitation = cloudness - 0.5); 0..0.5 the rain clouds
        /// gather before it starts (WeatherSystemBase.GetRainCloudness).</summary>
        public double Cloudness;
        /// <summary>0..1: how much of the sky has clouds (for the cloud patterns).</summary>
        public double CloudCover;
        /// <summary>Air pressure at sea level, hPa.</summary>
        public double Pressure;
        public WeatherWind Wind, Prevailing;
        /// <summary>The storm whose winds are strongest here, or null.</summary>
        public WeatherStorms.Storm Storm;
        /// <summary>0..1: how strongly a thunderstorm is overhead (0 none; WeatherModel.ThunderAt).</summary>
        public double Thunder;
        /// <summary>(Weather rules 8) This moment's rain over the month's mean while it rains (0 when dry), uncut by the drawn
        /// intensity's cap: WeatherModel.RainMmPerHour reads it where <see cref="HasRainShare"/>.</summary>
        public double RainShare;
        public bool HasRainShare;

        public double Precipitation => Math.Max(0, Cloudness - 0.5);
    }
}
