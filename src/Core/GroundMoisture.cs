using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Ranges step 5c: how wet the ground is now, from the rain of the past days. Earth's weather is a pure function of place
    /// and time (<see cref="WeatherModel"/>), so the recent rain is read back from it, each past moment weighted by how much
    /// of its water the ground still holds (an exponential decay), and nothing is saved. A storm wets the ground where it
    /// passed and nowhere else, and the ground dries again over the following days.
    /// </summary>
    public static class GroundMoisture
    {
        /// <summary>Saved in new worlds (EarthParams.MoistureRules).</summary>
        public const int RulesVersion = 1;
        /// <summary>The ground keeps 1/e of a rain's water after this many game days (a real dry spell's ~10 days scaled to the
        /// game's 9-day months: about a third of a month).</summary>
        public const double DecayDays = 3;
        /// <summary>How far back the rain is read (game days).</summary>
        public const double WindowDays = 12;
        /// <summary>Hours between the readings.</summary>
        public const double StepHours = 3;
        /// <summary>Decay-weighted rain (mm per day) that reads as 1.</summary>
        public const double FullMmPerDay = 40;

        /// <summary>
        /// 0..1: the decay-weighted rain of the recent past (mm per day, WeatherModel.RainMmPerHour) over
        /// <see cref="FullMmPerDay"/>: the amount that fell, so a short tropical downpour wets the ground as much as a day of
        /// drizzle with the same millimetres. About 0 after a dry fortnight; about 0.15 just after a 20 mm storm, under 0.03
        /// again after five dry days; 0.3 and more after a monsoon's wet spell. Until weather rules v5 it was the share of
        /// the recent hours with rain, which made the heat's short heavy showers count for little once the weather got the
        /// hours right.
        /// </summary>
        public static double At(WeatherModel weather, double x, double z, double hours)
        {
            double hpd = weather.Settings.HoursPerDay > 0 ? weather.Settings.HoursPerDay : 24;
            double tau = DecayDays * hpd;
            int n = (int)(WindowDays * hpd / StepHours);
            double sum = 0, weights = 0;
            for (int k = 0; k < n; k++)
            {
                double age = k * StepHours;
                double w = Math.Exp(-age / tau);
                // (the rain alone: the clouds, pressure and wind of a full sample are not needed here)
                sum += w * WeatherModel.RainMmPerHour(weather.RainSample(x, z, hours - age));
                weights += w;
            }
            return weights > 0 ? Math.Min(1, sum / weights * 24 / FullMmPerDay) : 0;
        }
    }
}
