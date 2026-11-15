using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, fog (rules 7) and the month's cover; moved unchanged from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ fog (rules 7)

        /// <summary>
        /// (Weather rules 7, EA-0026) Fog at a place and game time (both 0 in worlds before rules 7, which keep vanilla's
        /// patterns' fog and mist):
        ///  - sea fog: the cold upwelling seas' low cloud deck (<see cref="LowCloudDeck"/>) down on the water and the coast
        ///    under settled weather (high pressure), over land at night and in the morning only (Lima's garúa, the Namib's and
        ///    California's coastal fog);
        ///  - radiation fog over land: a clear, calm night or morning in the cool season after rain has wetted the ground (in
        ///    the last day and a half), more in a humid month (its precipitation against what its warmth evaporates, as for
        ///    thunderstorm rain): the fog of a front's clearing night, the Central Valley's tule fog, the Po plain's autumn
        ///    and winter fog; none in the desert, little in the tropics;
        ///  - rain: heavy rain thins the air's visibility.
        /// <paramref name="metres"/>: the ground's height (its temperature for the cool season). Not fitted: checked against
        /// the share of days with heavy fog (WT02) at the US first-order stations of GHCN-Daily (WeatherTests fog).
        /// </summary>
        public WeatherFog FogAt(double x, double z, double hours, double metres = 0)
        {
            if (!Settings.Has.Fog) return default;
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, true, out var n, out var st);
            double hour = HourOfDay(x, hours);
            // 1 from midnight to about 9, 0 from noon to 9 pm (local time where the sky follows the longitude)
            double night = WeatherNoise.SmoothStep(-0.35, 0.35, PortableMath.Cos(2 * Math.PI * (hour - 4.5) / 24));
            double land = Math.Clamp(n.Land, 0, 1);
            // settled weather: high pressure, no front or storm near (the large-scale field's dry side)
            double settled = WeatherNoise.SmoothStep(-1, 4, s.Pressure - 1013);
            // only a strong deck comes down to the sea (its strength follows the upwelling's season)
            double sea = WeatherNoise.SmoothStep(0.5, 1, DeckStrength(s.Lat, s.Lon, s.YearRel, n)) * settled * (1 - land * (1 - night));
            double groundT = n.Temp - GroundLapse * Math.Max(0, metres);
            double humid = WeatherNoise.SmoothStep(0.2, 1.0, n.Precip / (0.6 + 0.22 * Math.Max(0, groundT)));
            double cool = 1 - WeatherNoise.SmoothStep(13, 23, groundT);
            // long nights: the ground cools for longer (autumn and winter, not the spring's equal clear nights)
            double longNight = WeatherNoise.SmoothStep(10.5, 14.5, NightHours(s.Lat, s.YearRel));
            double clear = 1 - WeatherNoise.SmoothStep(0.3, 0.75, s.CloudCover);
            double windMs = Wind(x, z, hours, s.Lat, n, s.Prevailing, st, false).Strength * MetersPerSecondPerUnit;
            double calm = 1 - WeatherNoise.SmoothStep(2.5, 6, windMs);
            // the ground wet from rain in the last day: a front's clearing night (the commonest radiation fog)
            double recent = Math.Max(Cloudness(x, z, hours - FogRainHours), Cloudness(x, z, hours - 2 * FogRainHours));
            double moist = WeatherNoise.SmoothStep(0.45, 0.6, recent) * (0.5 + 0.5 * humid);
            double radiation = land * moist * cool * longNight * clear * calm * night;
            double rain = 0.35 * WeatherNoise.SmoothStep(0.55, 0.95, s.Cloudness);
            double ground = Math.Max(sea, radiation);
            return new WeatherFog { Ground = ground, Air = Math.Max(rain, 0.5 * ground), Sea = sea >= radiation && sea > 0 };
        }

        /// <summary>Hours of night at a latitude and time of year (0..1 from January): the sun's declination over the
        /// calendar year and the sunset hour angle, without twilight; 0 or 24 beyond the polar circles.</summary>
        public static double NightHours(double lat, double yearRel)
        {
            double decl = -23.44 * Math.PI / 180 * PortableMath.Cos(2 * Math.PI * (yearRel + 10 / 365.0));
            // (ER-17) tan and acos from PortableMath's sine, cosine and atan2, so the fog is the same bits on every system
            double phi = lat * Math.PI / 180;
            double x = -(PortableMath.Sin(phi) / PortableMath.Cos(phi)) * (PortableMath.Sin(decl) / PortableMath.Cos(decl));
            if (x <= -1) return 0;
            if (x >= 1) return 24;
            return 24 - 24 / Math.PI * PortableMath.Atan2(Math.Sqrt((1 - x) * (1 + x)), x);
        }

        /// <summary>Hours back to look for the rain that wets the ground for radiation fog (and twice that).</summary>
        const double FogRainHours = 9;

        /// <summary>The month's mean cloud cover at a place (the reanalysis', with the cold-water decks from rules 6): what
        /// a sample's cover moves around.</summary>
        public double MonthCover(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double c = double.IsNaN(n.Cloud) ? MeanCloudFor(n.Precip) : n.Cloud;
            return Settings.Has.ColdSeaDecks ? LowCloudDeck(c, lat, lon, yr, n) : c;
        }
    }
}
