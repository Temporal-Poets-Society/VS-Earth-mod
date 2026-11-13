using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Ranges step 5b: grass greens in a place's wet months and turns straw in its dry months, from Earth's monthly normals
    /// (weather.bin). Each place gets a yearly curve of how wet the ground is now against its own yearly average, in log
    /// units: the month's precipitation against twice its temperature (Walter and Lieth's dry-month line), carried about a
    /// month forward because soil water lags the rain. Cold months count as not dry: below about 5 °C grass is dormant and
    /// vanilla's own season tint browns it. The client turns the curve into the rainfall byte of the grass colour, around the
    /// yearly rainfall vanilla already uses, so the yearly average colour stays as before and vanilla's plant tint (straw at
    /// low rainfall, green at high) draws the seasons. No shader is changed.
    /// </summary>
    public static class GrassSeason
    {
        /// <summary>Points per year in a curve (half months).</summary>
        public const int Steps = 24;
        /// <summary>The largest swing either way, in log units of the aridity index (x7.4 or /7.4).</summary>
        public const double MaxAnomaly = 2;

        const double MinRatio = 0.05, MaxRatio = 4;

        /// <summary>The place's curve: <see cref="Steps"/> values at yearRel i / Steps, each the log of the lagged wetness
        /// minus its yearly mean (so the values average 0). Writes zeros where the weather has no data.</summary>
        public static void Curve(WeatherData weather, double lat, double lon, float[] curve)
        {
            Span<double> r = stackalloc double[Steps];
            for (int i = 0; i < Steps; i++) r[i] = Ratio(weather.Sample(lat, lon, (i + 0.5) / Steps));
            Span<double> lagged = stackalloc double[Steps];
            double mean = 0;
            for (int i = 0; i < Steps; i++)
            {
                // soil water: this half month, the one before and the one before that
                double v = 0.5 * r[i] + 0.3 * r[(i + Steps - 1) % Steps] + 0.2 * r[(i + Steps - 2) % Steps];
                lagged[i] = Math.Log(v);
                mean += lagged[i];
            }
            mean /= Steps;
            for (int i = 0; i < Steps; i++) curve[i] = (float)Math.Clamp(lagged[i] - mean, -MaxAnomaly, MaxAnomaly);
        }

        /// <summary>The month's wetness: precipitation (mm per month) over twice the temperature (°C), at least 1 in cold months.</summary>
        public static double Ratio(in WeatherData.Normals n)
        {
            double p = WeatherData.PerMonth(n.Precip);
            double raw = p / (2 * Math.Max(n.Temp, 0.5));
            double coldFloor = 1 - Geo.SmoothStep((n.Temp - 2) / 6);
            return Math.Clamp(Math.Max(raw, coldFloor), MinRatio, MaxRatio);
        }

        /// <summary>The curve at a time of year (0..1), linear between its points, around the year.</summary>
        public static double At(float[] curve, double yearRel)
        {
            double f = (yearRel - Math.Floor(yearRel)) * Steps - 0.5;
            int i0 = (int)Math.Floor(f);
            double t = f - i0;
            int a = ((i0 % Steps) + Steps) % Steps, b = (a + 1) % Steps;
            return curve[a] * (1 - t) + curve[b] * t;
        }

        /// <summary>The rainfall byte (0..255) for the grass colour now: the yearly rainfall's aridity index scaled by the anomaly.</summary>
        public static int SeasonalRain(int yearlyRain, double anomaly)
        {
            if (yearlyRain <= 0 || anomaly == 0) return yearlyRain;
            double ai = SoilFertility.Aridity(yearlyRain, 0) * Math.Exp(Math.Clamp(anomaly, -MaxAnomaly, MaxAnomaly));
            return (int)Math.Round(Math.Clamp(SoilFertility.RainFromAridity(ai), 0, 255));
        }
    }
}
