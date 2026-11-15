using System;

namespace EarthMap.Core
{
    /// <summary>
    /// (2.0.1, EA-0141; the author: "in single player - spawn with morning independent on location. Now often spawn at night
    /// when not in UTC zone") A new world's first hour at its spawn. Where the sky follows Earth's meridians (AstraTerra's
    /// longitude sun), the local solar hour is the calendar's hour plus the longitude's share of a day, as Earth's weather
    /// also takes it (EarthWeather's LocalHourOffset): a world started at the game's morning hour starts at night far from
    /// longitude 0. Moving the new world's clock once gives the spawn the game's start hour.
    /// <para>(2.0.2, EA-0142, Fable's review of 0fa502a, F1) Only forward. The game's calendar keeps the moment the world
    /// started apart from its clock, and a move back made its elapsed time negative; vanilla's rift weather then finds no
    /// pattern and fails the start, in a world whose first start stopped before the game ran.</para>
    /// </summary>
    public static class SpawnTime
    {
        /// <summary>
        /// Game hours to add to a new world's clock so that the local solar hour at <paramref name="longitude"/> (degrees, east
        /// positive) is the hour the clock shows now: minus the longitude's share of a day of <paramref name="hoursPerDay"/>,
        /// taken forward, in [0, hoursPerDay). 0 for a longitude or day length that isn't a number.
        /// </summary>
        public static double MorningShift(double longitude, double hoursPerDay)
        {
            if (!double.IsFinite(longitude) || !double.IsFinite(hoursPerDay) || hoursPerDay <= 0) return 0;
            double shift = -longitude / 360.0 * hoursPerDay;
            shift -= Math.Floor(shift / hoursPerDay) * hoursPerDay;
            // a hair below a whole day is no move (rounding of a longitude of 0 or a whole number of days)
            if (shift < 1e-9 || hoursPerDay - shift < 1e-9) return 0;
            return shift;
        }

        /// <summary>The local solar hour at <paramref name="longitude"/> for the calendar's <paramref name="hourOfDay"/>, in [0, hoursPerDay).</summary>
        public static double LocalHour(double hourOfDay, double longitude, double hoursPerDay)
        {
            double h = hourOfDay + longitude / 360.0 * hoursPerDay;
            return h - Math.Floor(h / hoursPerDay) * hoursPerDay;
        }
    }
}
