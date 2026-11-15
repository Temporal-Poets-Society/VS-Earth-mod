using System;

namespace EarthMap.Core
{
    /// <summary>
    /// How often thunderstorms happen at a place, from the month's normal temperature (sea-level equivalent), precipitation
    /// and land share, the three things Earth's weather knows (weather.bin). Fitted by tools/fit_thunder.py against the WWLLN
    /// Global Lightning Climatology (WGLC, Kaplan and Lau; monthly, 0.5°, 2010-2022, corrected for detection efficiency),
    /// which is used for the fit only and not shipped:
    ///
    ///   strokes km⁻² day⁻¹  D = exp(c0 + cl·land^g) · σ((T − Tm)/Ts) · (P / (P + Ph))^k
    ///
    /// R² 0.63 on the log of the monthly densities, yearly log correlation 0.87. Thunder needs warm air (Tm 18.5 °C at sea
    /// level, so mountains in summer and the warm mid-latitude months too), rain to come from, and land: over the open sea
    /// it is a sixth as frequent, while coasts (sea breezes) are nearly as stormy as inland (g 0.18).
    ///
    /// The share of days with thunder is then fitted to stations' yearly thunderstorm days (Singapore 170, Tampa 85, Darwin
    /// 85, Oklahoma City 50, Denver 40, Vienna 26, Moscow 23, Paris 19, London 13, Cairo 3, Reykjavik 1.5; modelled 146, 72,
    /// 71, 62, 36, 27, 21, 22, 16, 1, 4): τ = 1 − exp(−(D / Dt)^b). Kampala's 240 is not fitted (WWLLN sees few of Africa's
    /// strokes; the model gives it 124).
    ///
    /// (Weather rules 7, EA-0022) The density fit misses the summer storms of hot dry places: WGLC's own strokes give the
    /// desert Southwest about its real July thunder (Phoenix a quarter of the days, El Paso two fifths) where the fit gave half
    /// that, because a month of 1 mm/day weighs as little rain. From rules 7 the density is raised in hot, dry months by
    /// 1 + DryBoost·smoothstep(T: DryWarm → +8 °C)·e^(−P/DryRain), fitted (tools/fit_thunder.py --dry) to the share of days
    /// with thunder (WT03) at 13 US first-order stations, 1991-2020 (GHCN-Daily; tools/ghcnd_weathertypes.py), with the yearly stations above kept as
    /// they were: the West's June-September error 8 → 4 points (Phoenix July 9 → 20 %, real 17; Albuquerque 14 → 24, real
    /// 41), and toward WGLC in Arabia, Iran and inland Australia. Tropical Africa stays low (WWLLN).
    ///
    /// In the weather a thunderstorm is the top of the shower field (WeatherModel.ThunderAt). A thunder day needs a spell, so
    /// the threshold is set from the spells a day the thunder days need (−ln(1 − τ), independent spells) and how often the
    /// field crosses a level where it is (Rice: (ω / 2π)·e^(−θ²/2) an hour, ω from how fast the steering wind carries the
    /// cells past and how fast they change, <see cref="Threshold"/>).
    /// </summary>
    public static class Thunder
    {
        /// <summary>Version of the thunder rules: 1, lightning from Earth's thunderstorms; 2, more in hot dry months (weather
        /// rules 7). Not saved on its own: the thunder
        /// is part of the weather rules (EarthParams.WeatherRules, EarthWeather.RulesVersion 3 and up), which server and
        /// clients must share.</summary>
        public const int RulesVersion = 2;

        const double C0 = -5.6707, CLand = 1.7736, LandExp = 0.1827;
        const double TMid = 18.541, TScale = 3.772;
        const double PHalf = 7.6689, PExp = 1.3957;
        const double DaysScale = 0.0100, DaysExp = 0.8822;
        // (rules 7) the hot dry months' boost
        const double DryBoost = 4.93, DryWarm = 24.86, DryRain = 0.696;


        /// <summary>(Rules 7) The factor on the stroke density of a hot, dry month: up to 5.9 at 33 °C (sea level) and no rain,
        /// 1 where it's cool or wet (under 1.5 at 3 mm/day).</summary>
        public static double DryFactor(double temp, double precip) =>
            1 + DryBoost * WeatherNoise.SmoothStep(DryWarm, DryWarm + 8, temp) * PortableMath.Exp(-Math.Max(0, precip) / DryRain);

        /// <summary>How much more thunder there is in the afternoon than at night over land: the hourly share is multiplied by
        /// 1 + Diurnal·cos(hour − 16 h) (so the day's mean stays), scaled by the land weight.</summary>
        public const double Diurnal = 0.85;

        /// <summary>0..1: how much a place counts as land for thunder (coasts nearly fully).</summary>
        public static double LandWeight(double land) => PortableMath.Pow(Math.Clamp(land, 0, 1), LandExp);

        /// <summary>Lightning strokes per km² and day (WWLLN's scale) in a month with these normals, under weather rules
        /// <paramref name="rules"/> (with the hot dry months' boost where WeatherRules.DryThunder).</summary>
        public static double Density(double temp, double precip, double land, int rules = 6)
        {
            if (!(precip > 0) || double.IsNaN(temp)) return 0;
            double warm = 1 / (1 + PortableMath.Exp(-(temp - TMid) / TScale));
            double d = PortableMath.Exp(C0 + CLand * LandWeight(land)) * warm * PortableMath.Pow(precip / (precip + PHalf), PExp);
            return WeatherRules.For(rules).DryThunder ? d * DryFactor(temp, precip) : d;
        }

        /// <summary>0..1: the share of days with thunder at a place for a stroke density.</summary>
        public static double DayShare(double density) => density > 0 ? 1 - PortableMath.Exp(-PortableMath.Pow(density / DaysScale, DaysExp)) : 0;

        /// <summary>0..1: the share of days with thunder in a month with these normals; <paramref name="region"/> multiplies
        /// the stroke density (<see cref="RegionFactor"/>, 1 elsewhere).</summary>
        public static double DayShare(double temp, double precip, double land, int rules = 6, double region = 1)
        {
            double d = Density(temp, precip, land, rules);
            if (region != 1) d *= region;
            return DayShare(d);
        }

        /// <summary>
        /// (Weather rules 9, EA-0074) WWLLN, and so WGLC and the density fit above, sees too few of West and Central Africa's
        /// strokes. Against the thunder days of 15 airports there in their routine METAR reports (2017-2023, thunder at the
        /// station or in its vicinity; tools/iem_metar_thunder.py; the method gives GHCN-Daily's own thunder days within 0-30 %
        /// at 11 US stations), the model had a third too few (log mean -0.31: Douala 157 a year against 244, Yaoundé 127
        /// against 220, Bangui 128 against 197, Abuja 104 against 166, Bamako 82 against 131). From rules 9 the density there
        /// is RegionAfrica times as high, faded in from 16 to 10 °W and out from 22 to 30 °E, from 10 to 5 °S and from 14 to
        /// 19 °N (log mean +0.02, rms 0.37 → 0.21). East and southern Africa are left as they were: Entebbe, Kigali and
        /// Johannesburg fit, and the highlands of Nairobi and Addis Ababa have too much thunder already.
        /// </summary>
        public static double RegionFactor(double lat, double lon, int rules)
        {
            if (!WeatherRules.For(rules).AfricaThunder) return 1;
            double w = WeatherNoise.Band(lon, -16, -10, 22, 30) * WeatherNoise.Band(lat, -10, -5, 14, 19);
            return w > 0 ? 1 + (RegionAfrica - 1) * w : 1;
        }

        /// <summary>(Rules 9) The stroke density's factor over West and Central Africa (<see cref="RegionFactor"/>).</summary>
        public const double RegionAfrica = 1.7;

        /// <summary>
        /// 0..1: how much of a thunderstorm's rain reaches the ground in a month with these normals (temperature at sea-level
        /// equivalent, so high plateaus count as hot): all of it where the month's rain is a good part of what its warmth
        /// could evaporate, none where the air is desert-dry and the rain evaporates on the way down (dry lightning: the dry
        /// West of North America, the Mediterranean summer, inland Australia, Siberia's warm months). Not fitted: the month's
        /// precipitation over a rough evaporation demand of 0.6 + 0.22·T mm/day, from 0.05 (none) to 0.4 (all).
        /// </summary>
        public static double RainReach(double temp, double precip)
        {
            if (!(precip > 0) || double.IsNaN(temp)) return 0;
            return WeatherNoise.SmoothStep(0.05, 0.4, precip / (0.6 + 0.22 * Math.Max(0, temp)));
        }

        /// <summary>Independent thunder spells a day that give a place its share of thunder days (Poisson: τ = 1 − e^−n).</summary>
        public static double SpellsPerDay(double temp, double precip, double land, int rules = 6, double region = 1) => -PortableMath.Log(1 - Math.Min(0.999, DayShare(temp, precip, land, rules, region)));

        /// <summary>The level (standard deviations, at least 0) a smooth standard normal field changing at <paramref name="rate"/>
        /// radians an hour must exceed to start <paramref name="spellsPerHour"/> spells an hour (Rice's formula); infinity
        /// for none.</summary>
        public static double Threshold(double spellsPerHour, double rate)
        {
            if (!(spellsPerHour > 0) || !(rate > 0)) return double.PositiveInfinity;
            double r = rate / (2 * Math.PI * spellsPerHour);
            return r > 1 ? Math.Sqrt(2 * PortableMath.Log(r)) : 0;
        }
    }
}
