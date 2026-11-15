using System;

namespace EarthMap.Core
{
    /// <summary>
    /// (2.0.1, EA-0141; the author: "in single player - spawn with morning independent on location. Now often spawn at night
    /// when not in UTC zone") A new world's first hour at its spawn. Where the sky follows Earth's meridians (AstraTerra's
    /// longitude sun), the local solar hour is the calendar's hour plus the longitude's share of a day, as Earth's weather
    /// also takes it (EarthWeather's LocalHourOffset): a world started at the game's morning hour starts at night far from
    /// longitude 0. Moving the new world's clock once, by minus that share, gives the spawn the game's start hour.
    /// </summary>
    public static class SpawnTime
    {
        /// <summary>
        /// Game hours to add to a new world's clock so that the local solar hour at <paramref name="longitude"/> (degrees, east
        /// positive) is the hour the clock shows now: minus the longitude's share of a day of <paramref name="hoursPerDay"/>,
        /// folded into half a day either way, and moved a day later where it would take the clock before its zero
        /// (<paramref name="totalHours"/>). 0 for a longitude or day length that isn't a number.
        /// </summary>
        public static double MorningShift(double longitude, double hoursPerDay, double totalHours)
        {
            if (!double.IsFinite(longitude) || !double.IsFinite(hoursPerDay) || hoursPerDay <= 0) return 0;
            double shift = -longitude / 360.0 * hoursPerDay;
            shift -= Math.Floor(shift / hoursPerDay + 0.5) * hoursPerDay;
            if (double.IsFinite(totalHours) && totalHours + shift < 0) shift += hoursPerDay;
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
