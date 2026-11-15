using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, thunder and hail: the shower field's top, thunder days and thresholds; moved
    // unchanged from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ thunder

        /// <summary>
        /// Thunder at a place and game time, 0..1: how strongly a thunderstorm is overhead (0 none). Thunderstorms are the
        /// strongest of the shower cells, the same field that brings the showers, so they move with the steering wind. Each
        /// month a place gets the thunder days of its climate (Core.Thunder: warm, wet and over land, fitted to the WWLLN
        /// lightning climatology), more in the afternoon over land. Under a thunderstorm it rains hard, unless the month's air
        /// is so dry that the rain evaporates on the way down (dry lightning, Core.Thunder.RainReach). The same as
        /// WeatherSample.Thunder, without the rest of the weather.
        /// </summary>
        public double ThunderAt(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            SteeringBlocks(lat, Data.ItczLat(lon, yr), out double vx, out double vz);
            double tie = ThunderTie * Thunder.RainReach(n.Temp, n.Precip);
            double theta = ThunderThreshold(HourOfDay(x, hours), n, vx, vz, tie, lat, lon);
            if (double.IsPositiveInfinity(theta)) return 0;
            double wh = hours * Settings.Pace;
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            if (tie > 0)
            {
                double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
                gm = Math.Sqrt(1 - tie * tie) * gm + tie * sp;
            }
            return ThunderFrom(gm, theta);
        }

        /// <summary>
        /// (Weather rules 6, EA-0019) Hail at a place and game time, 0..1 (0 none; 0 in worlds before rules 6, which keep
        /// vanilla's random hail events). Hail falls from the strongest part of a thunderstorm: the share
        /// <see cref="HailShare"/> of its time with the thunder field furthest above its threshold (the field's normal tail),
        /// so it moves with the storms and comes where and when they are strongest. <paramref name="metres"/>: the ground's
        /// height above the sea (the freezing level is over the ground: the High Plains and the Highveld get much hail).
        /// </summary>
        public double HailAt(double x, double z, double hours, double metres = 0)
        {
            if (!Settings.Has.Hail) return 0;
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double share = HailShare(n.Temp - GroundLapse * Math.Max(0, metres));
            if (share <= 0) return 0;
            SteeringBlocks(lat, Data.ItczLat(lon, yr), out double vx, out double vz);
            double tie = ThunderTie * Thunder.RainReach(n.Temp, n.Precip);
            // (rules 9) the hail of the unraised thunder: the region's extra storms bring no rain, so no hail either
            double theta = ThunderThreshold(HourOfDay(x, hours), n, vx, vz, tie, 1);
            if (double.IsPositiveInfinity(theta)) return 0;
            double wh = hours * Settings.Pace;
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            if (tie > 0)
            {
                double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
                gm = Math.Sqrt(1 - tie * tie) * gm + tie * sp;
            }
            if (gm <= theta) return 0;
            // the level above which the field spends the hail's share of the time it spends above the thunder threshold
            double level = WeatherNoise.InverseCdf(1 - share * (1 - WeatherNoise.Cdf(theta)));
            return gm <= level ? 0 : WeatherNoise.SmoothStep(level, level + HailCore, gm);
        }

        /// <summary>
        /// (Rules 6) 0..0.09: the share of a thunderstorm's time with hail reaching the ground, by the month's mean temperature
        /// at the ground, a stand-in for how high the freezing level is above it: none in cold months (no storms tall
        /// enough), most at 10-19 °C (the mid-latitude spring and summer: the Great Plains, the Alpine foreland, the Pampas,
        /// the Highveld, northern India before the monsoon), none from 26 °C (the tropics' freezing level near 5 km melts the
        /// hail on the way down). Not fitted to hail records (none on this PC): of the order of the share of thunderstorm
        /// days with hail reports in the mid-latitudes (WeatherTests rules6 lists hail days at stations).
        /// </summary>
        public static double HailShare(double groundTemp) => 0.09 * WeatherNoise.SmoothStep(4, 10, groundTemp) * (1 - WeatherNoise.SmoothStep(19, 26, groundTemp));

        /// <summary>°C the month's mean falls per metre up (the weather's normals are at sea level).</summary>
        public const double GroundLapse = 0.0065;

        /// <summary>0..1: the share of days with thunder at a place in the month of this game time (the climate's, with the
        /// rain frequency).</summary>
        public double ThunderDays(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out _, out var n);
            return 1 - PortableMath.Exp(-Thunder.SpellsPerDay(n.Temp, n.Precip, n.Land, Settings.Rules, Thunder.RegionFactor(lat, lon, Settings.Rules)) * Settings.RainFrequency);
        }

        /// <summary>How far above its threshold the shower field must be for full thunder (standard deviations).</summary>
        const double ThunderCore = 0.6;

        static double ThunderFrom(double gm, double theta) => gm <= theta ? 0 : WeatherNoise.SmoothStep(theta, theta + ThunderCore, gm);

        /// <summary>The level the thunder field (the shower field, tied to the wet spells by <paramref name="tie"/>) must pass
        /// for thunder at this hour of the day (infinity for none): from the spells a day the month's thunder days need, more
        /// in the afternoon over land, and how fast the field changes here. Storms tied to the wet spells come in runs and
        /// share days, so they need more spells for the same thunder days (1 + tie², measured in WeatherTests thunder).</summary>
        double ThunderThreshold(double hourOfDay, in WeatherData.Normals n, double vx, double vz, double tie, double lat, double lon)
            => ThunderThreshold(hourOfDay, n, vx, vz, tie, Thunder.RegionFactor(lat, lon, Settings.Rules));

        /// <summary>The same with the stroke density's regional factor given (Thunder.RegionFactor; 1 for none).</summary>
        double ThunderThreshold(double hourOfDay, in WeatherData.Normals n, double vx, double vz, double tie, double region)
        {
            double perDay = Thunder.SpellsPerDay(n.Temp, n.Precip, n.Land, Settings.Rules, region) * Settings.RainFrequency;
            if (!(perDay > 1e-6)) return double.PositiveInfinity;
            double perHour = (1 + tie * tie) * perDay / Settings.HoursPerDay * (1 + Thunder.Diurnal * Thunder.LandWeight(n.Land) * PortableMath.Cos(2 * Math.PI * (hourOfDay - 16) / 24));
            return Thunder.Threshold(perHour, ShowerRate(vx, vz) * Math.Sqrt(1 - tie * tie));
        }

        /// <summary>The hour of the day (0..24, local solar time when the sky follows the longitude) at x and a game time.</summary>
        double HourOfDay(double x, double hours)
        {
            double h = (hours % Settings.HoursPerDay + Settings.HoursPerDay) % Settings.HoursPerDay * 24 / Settings.HoursPerDay;
            var off = LocalHourOffset;
            return off != null ? h + off(x) : h;
        }

        // how fast the shower field changes at a place (Rice's ω², per weather hour²): from being carried across its cells
        // (cells per hour) and from its own change (lattice steps per hour), measured in WeatherTests (thunderrate)
        const double ShowerAcross = 6.6, ShowerChange = 11.7;

        /// <summary>How fast the shower field changes at a place, radians per game hour (Rice's ω: a smooth field crosses a
        /// level θ upward (ω / 2π)·e^(−θ²/2) times an hour): faster where the steering wind carries the cells by quickly (the
        /// bigger the world, the more blocks it covers an hour) and with the weather's pace.</summary>
        double ShowerRate(double vx, double vz)
        {
            double across = Math.Sqrt(vx * vx + vz * vz) / ShowerBlocks;
            return Settings.Pace * Math.Sqrt(ShowerAcross * across * across + ShowerChange / (ShowerEvolve * ShowerEvolve));
        }

        /// <summary>The shower field (standard normal) at a place and game time, which thunderstorms are the top of, and how
        /// many of its cells the steering wind carries past an hour there. For tests.</summary>
        public double ShowerField(double x, double z, double hours, out double cellsPerHour)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out _);
            double itcz = Data.ItczLat(lon, yr);
            SteeringBlocks(lat, itcz, out double vx, out double vz);
            cellsPerHour = Math.Sqrt(vx * vx + vz * vz) / ShowerBlocks;
            return Advected(key + 2, x, z, hours * Settings.Pace, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
        }

        /// <summary>The weather hours per lattice step of the shower field (WeatherTests fits <see cref="ShowerRate"/>).</summary>
        public const double ShowerEvolveHours = ShowerEvolve;
    }
}
