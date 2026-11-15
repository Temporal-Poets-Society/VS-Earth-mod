using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, the wind: steady, drift and gusts; moved unchanged from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ wind

        /// <summary>The wind at a place and game time; without gusts it's the steady wind the sea follows.</summary>
        public WeatherWind WindAt(double x, double z, double hours, bool gusts)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            double itcz = Data.ItczLat(lon, yr);
            return Wind(x, z, hours, lat, n, PrevailingFor(lat, itcz, n), Storms.Influence(lat, lon, hours), gusts);
        }

        /// <summary>The wind from the place's latitude, normals, prevailing wind and storms (<see cref="Sample"/> has them
        /// from the rain already).</summary>
        WeatherWind Wind(double x, double z, double hours, double lat, in WeatherData.Normals n, WeatherWind prevailing, in WeatherStorms.Effect st, bool gusts)
        {
            var w = prevailing;
            double wh = hours * Settings.Pace;
            w += Drift(x, z, wh, lat) * (1 - 0.35 * Math.Clamp(n.Land, 0, 1));
            if (st.Wind.Strength > 0)
            {
                // a storm's own circulation takes over from the background wind near it
                double take = Math.Min(1, st.Wind.Strength / 0.6) * 0.6;
                w = w * (1 - take) + st.Wind * (1 - 0.3 * Math.Clamp(n.Land, 0, 1));
            }
            if (gusts) w = Gust(w, x, z, wh);
            double l = w.Strength;
            return l > MaxStrength ? w.WithStrength(MaxStrength) : w;
        }

        /// <summary>Day-to-day drift: a smooth random wind over ~1000 km and ~30 weather hours, weaker in the trades.</summary>
        WeatherWind Drift(double x, double z, double wh, double lat)
        {
            if (Settings.Variability <= 0) return default;
            double lx = LatticeX(x, driftScale, driftCells), lz = z / driftScale, lt = wh / DriftEvolve;
            // the monthly mean winds are vectors averaged over the month, weaker than the wind usually is where it turns a lot
            // (the mid-latitudes); the drift is what turns it
            double amp = Settings.Variability * WeatherNoise.Lerp(0.16, 0.46, WeatherNoise.SmoothStep(18, 38, Math.Abs(lat)));
            return new WeatherWind(WeatherNoise.Fbm(key + 11, lx, lz, lt, driftCells) * amp, WeatherNoise.Fbm(key + 12, lx, lz, lt, driftCells) * amp);
        }

        /// <summary>Gusts: ±~20% strength and ±~12° direction, changing over tens of seconds (sized in blocks).</summary>
        WeatherWind Gust(WeatherWind w, double x, double z, double wh)
        {
            if (Settings.Gusts <= 0) return w;
            double lx = LatticeX(x, GustBlocks, gustCells), lz = z / GustBlocks, lt = wh / GustEvolve;
            double gs = WeatherNoise.Fbm(key + 21, lx, lz, lt, gustCells), ga = WeatherNoise.Fbm(key + 22, lx, lz, lt, gustCells);
            double s = w.Strength * (1 + 0.45 * gs * Settings.Gusts);
            return w.WithStrength(Math.Max(0, s)).Rotated(0.35 * ga * Settings.Gusts);
        }
    }
}
