using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, cloud cover and the cold seas' low cloud decks (rules 6); moved unchanged
    // from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        /// <summary>0..1: how much of the sky has clouds at a place and game time (WeatherSample.CloudCover, without the wind).</summary>
        public double CloudCover(double x, double z, double hours)
        {
            var s = new WeatherSample();
            Rain(x, z, hours, ref s, true, out _, out _);
            return s.CloudCover;
        }

        void Rain(double x, double z, double hours, ref WeatherSample s, bool full, out WeatherData.Normals n, out WeatherStorms.Effect st)
            => Rain(x, z, hours, ref s, full, out n, out st, out _, out _);

        /// <summary>The rain, and the steering wind it was carried by (blocks per weather hour; ER-12: the anomaly needs the same).</summary>
        void Rain(double x, double z, double hours, ref WeatherSample s, bool full, out WeatherData.Normals n, out WeatherStorms.Effect st, out double vx, out double vz)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out n);
            double itcz = Data.ItczLat(lon, yr);
            s.Lat = lat; s.Lon = lon; s.YearRel = yr;
            s.NormalPrecip = n.Precip;
            s.NormalTemp = n.Temp;
            double rateTemp = RateTemp(lat, lon, n);
            double f = RainChanceFor(n.Precip, rateTemp, n.Cloud, Settings.RainFrequency);
            double cv = ConvectiveFor(n.Temp);
            s.RainChance = f;
            s.Convective = cv;

            double wh = hours * Settings.Pace;
            SteeringBlocks(lat, itcz, out vx, out vz);
            double gs = Advected(key + 1, x, z, wh, vx, vz, synScale, synCells, synCells2, SynopticEvolve, SynopticReset);
            double gm = Advected(key + 2, x, z, wh, vx, vz, ShowerBlocks, showerCells, showerCells2, ShowerEvolve, ShowerReset);
            // the wet and dry spells: rain comes in runs of wet days with dry weeks between them (a dry month's few rainy days
            // together, not a shower every few days), the rain chance unchanged
            double sp = Advected(key + 31, x, z, wh, vx * SpellDrift, vz * SpellDrift, spellScale, spellCells, spellCells2, SpellEvolve, SpellReset);
            double sw = SpellWeight(rateTemp), rest = Math.Sqrt(1 - sw * sw);
            double g;
            var has = Settings.Has;
            if (has.RainEvents)
            {
                // (rules 8, EA-0020) the rain comes in events as long as at the airports: the shower field's share at least
                // FastShare's, the fronts and spells keeping their ratio
                double a = rest * Math.Sqrt(1 - cv * cv), b = rest * cv, fast = double.IsNaN(FastOverride) ? FastShare : FastOverride;
                if (fast > b)
                {
                    double k = Math.Sqrt((1 - fast * fast) / (a * a + sw * sw));
                    g = k * a * gs + fast * gm + k * sw * sp;
                }
                else g = a * gs + b * gm + sw * sp;
            }
            else g = rest * (Math.Sqrt(1 - cv * cv) * gs + cv * gm) + sw * sp;
            // the large-scale weather: fronts and spells (cloudier and lower pressure in a wet spell)
            double large = rest * gs + sw * sp;

            // showers over land peak in the mid-afternoon
            double hourOfDay = HourOfDay(x, hours);
            double diurnalAmp = has.RainEvents ? (double.IsNaN(DiurnalOverride) ? DiurnalAmplitude : DiurnalOverride) : 0.45;
            double diurnal = diurnalAmp * cv * Math.Clamp(n.Land, 0, 1) * PortableMath.Cos(2 * Math.PI * (hourOfDay - 15) / 24);

            st = Storms.Influence(lat, lon, hours);
            double theta = f > 0 ? WeatherNoise.InverseCdf(1 - f) - diurnal : double.PositiveInfinity;
            g += st.RainBoost;
            // a storm brings rain even where the month is dry, unless rain is switched off (RainFrequency 0)
            if (st.RainBoost > 0 && f <= 0 && Settings.RainFrequency > 0) theta = 2.5;
            double share = 0;
            s.Cloudness = has.RainEvents ? CloudnessFor(g, theta, n.Precip, f, RainSkew(rateTemp), out share) : CloudnessFor(g, theta, n.Precip, f);

            // a thunderstorm, the top of the shower cells (in a humid month, of the wet spells too): it rains hard under it,
            // unless the air below is so dry that its rain evaporates on the way down (dry lightning), when only its dark
            // cloud is overhead
            double wet = Thunder.RainReach(n.Temp, n.Precip), tie = ThunderTie * wet;
            double gt = tie > 0 ? Math.Sqrt(1 - tie * tie) * gm + tie * sp : gm;
            double region = Thunder.RegionFactor(lat, lon, Settings.Rules);
            s.Thunder = gt > 0 ? ThunderFrom(gt, ThunderThreshold(hourOfDay, n, vx, vz, tie, region)) : 0;
            // (rules 9, EA-0074) where the thunder is raised by region, the rain it brings is the unraised thunder's: the extra
            // storms are heard, struck and hailed from but rain no more hours (the Sahel's rain hours are already too many)
            double rainThunder = region == 1 || s.Thunder <= 0 ? s.Thunder : ThunderFrom(gt, ThunderThreshold(hourOfDay, n, vx, vz, tie, 1));
            if (rainThunder > 0)
            {
                double reach = WeatherNoise.SmoothStep(1 - wet, 1.3 - wet, rainThunder);
                s.Cloudness = Math.Max(s.Cloudness, reach > 0 ? 0.5 + reach : 0.45);
            }
            // (rules 9) the region's extra thunder under the dark sky of a storm, below the rain line (no rain added)
            else if (s.Thunder > 0) s.Cloudness = Math.Max(s.Cloudness, 0.45);
            if (has.RainEvents)
            {
                // (rules 8) the millimetres from the skewed share, which the drawn intensity's cap doesn't cut, or from a
                // thunderstorm's harder rain
                double p = s.Precipitation;
                s.RainShare = p > 0 ? Math.Max(share, p / MeanIntensity(f > 0 ? n.Precip / (24 * f) : 1)) : 0;
                s.HasRainShare = true;
            }
            if (!full) return;

            // the month's mean cloud cover from the reanalysis (the low cloud decks over cold coasts too), else from its rain
            double baseCover = double.IsNaN(n.Cloud) ? MeanCloudFor(n.Precip) : n.Cloud;
            if (has.ColdSeaDecks) baseCover = LowCloudDeck(baseCover, lat, lon, yr, n);
            double cover = Math.Clamp(baseCover + 0.28 * large + 0.1 * gm + 0.25 * st.RainBoost, 0, 1);
            if (s.Cloudness > 0) cover = Math.Max(cover, Math.Min(1, 0.55 + 0.9 * s.Cloudness));
            s.CloudCover = cover;
            s.Pressure = 1013 - 7 * large - st.PressureDrop;
            s.Storm = st.Storm;
            s.Prevailing = PrevailingFor(lat, itcz, n);
        }

        // (rules 6, EA-0023) the cold-water low cloud decks: how many degrees below the latitude's open-ocean mean the sea
        // must be before a deck forms, how much of the sky it covers for each degree beyond, and the most it covers
        const double DeckStart = 1.0, DeckPerDegree = 0.28, DeckMost = 0.8;

        /// <summary>
        /// (Weather rules 6, EA-0023) The month's cloud cover with the low stratocumulus decks over cold upwelling seas, which
        /// the NCEP/NCAR reanalysis Earth's cover comes from has far too little of (its low cloud off Peru and Namibia 20-27 %
        /// in July where satellites see about 70 %; its total cover there 50-55 % at the deck's peak, near the coast less). A
        /// deck forms where the sea is colder than the open ocean's mean at its latitude (<see cref="WeatherData.ColdWater"/>,
        /// a stand-in for the stable air over a cold sea that Klein and Hartmann (1993) tie the decks to), between about 10°
        /// and 45° of latitude, and the sky is at least that cloudy: 28 % for each degree beyond the first, at most 80 %.
        /// Checked against the satellites' total cover at the decks' seasonal peak (ISCCP, MODIS; about 80 % off Peru and
        /// Namibia, 75 % off California, 60 % off the Canaries, 55 % off Western Australia), WeatherTests lowcloud.
        /// </summary>
        double LowCloudDeck(double cover, double lat, double lon, double yr, in WeatherData.Normals n)
        {
            double cold = Data.ColdWater(lat, lon, yr, n.Temp, n.Land);
            if (!(cold > DeckStart)) return cover;
            double band = WeatherNoise.Band(Math.Abs(lat), 6, 12, 40, 48);
            double deck = Math.Min(DeckMost, DeckPerDegree * (cold - DeckStart)) * band;
            return Math.Max(cover, deck);
        }

        /// <summary>0..1: how strong the cold-water low cloud deck is at a place and month (<see cref="LowCloudDeck"/>'s
        /// cover over its most, 0 where there is none).</summary>
        double DeckStrength(double lat, double lon, double yr, in WeatherData.Normals n)
        {
            double cold = Data.ColdWater(lat, lon, yr, n.Temp, n.Land);
            if (!(cold > DeckStart)) return 0;
            return Math.Min(DeckMost, DeckPerDegree * (cold - DeckStart)) * WeatherNoise.Band(Math.Abs(lat), 6, 12, 40, 48) / DeckMost;
        }
    }
}
