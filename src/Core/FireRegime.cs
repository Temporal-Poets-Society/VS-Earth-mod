using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Habitats: grassland kept open by fire (EarthParams.FireRules). The zones (<see cref="ClimateZones"/>) set the forest by
    /// climate alone, and climate alone forests the tallgrass prairie, the aspen parkland, the Pampas and the forest-steppe:
    /// Des Moines and Indianapolis, Winnipeg and the boreal plains, have nearly the same temperatures and aridity. What sets
    /// them apart is fire. Grass that cures in hot, dry summers burns every few years (lightning, and the people who burned
    /// the prairie and the steppe), and fire kills young trees. Where it has done so for millennia the grass built black
    /// soils (Chernozems, Phaeozems and Kastanozems, from SoilGrids' soil groups: <see cref="SoilSiteGrid"/>). So the
    /// chance that land is kept open is a logistic of the grassland-soil share around the place, the warmest three months'
    /// temperature and rain at the ground and the aridity index. It was fitted against RESOLVE Ecoregions 2017 (temperate
    /// grasslands and savannas against the temperate forests, where the zones give forest), with WWLLN lightning
    /// (WGLC, Kaplan) as a check that hot summers stand for the strikes: neither dataset ships with the mod. Open land keeps a
    /// fifth of the zones' forest (groves, and the river belts the forest potential adds afterwards); without grassland soil,
    /// fire thins the forest by at most a quarter (open pine and oak woodland). The tropics are left to the savanna zones.
    /// Rules 2 (8 November 2026, north of Crimea): the surer the fit is of grassland, the fewer groves open land keeps
    /// (<see cref="ForestCutFor"/>): the forest-steppe, aspen parkland and the prairie's oak edge (chance up to 0.9) keep a
    /// fifth, the core steppe, the Pampas and the tallgrass prairie (0.98 and more) a fiftieth; trees there stand in the
    /// river belts the forest potential adds afterwards, as they did in 1318.
    /// </summary>
    public sealed class FireRegime
    {
        public const int RulesVersion = 2;
        public const string SoilField = "soil_group";
        /// <summary>The soil groups that form under grassland (a mollic horizon from grass roots).</summary>
        public static string[] GrassSoils => SoilSiteGrid.GrassSoils;
        /// <summary>The logistic (calibration, docs/research): constant, grassland-soil share, summer temperature (°C),
        /// ln(summer rain mm per month + 5), ln(aridity index).</summary>
        public const double Constant = -4.80, Soil = 3.656, Heat = 0.278, Rain = -0.354, Dryness = -1.151;
        /// <summary>The chance from which land opens, and at which it is fully open.</summary>
        public const double OpenFrom = 0.5, OpenAt = 0.85;
        /// <summary>Share of the opening without grassland soil, and the soil share from which it is full.</summary>
        public const double WithoutSoil = 0.3, SoilFull = 0.4;
        /// <summary>The coldest month (°C) from which fire leaves the land to the tropical zones, fully at the second.</summary>
        public const double TropicsFrom = 11, TropicsAt = 15;
        /// <summary>How much of the forest and shrub density open land loses (rules 1; rules 2 for the forest where the
        /// chance is up to <see cref="CoreFrom"/>).</summary>
        public const double ForestCut = 0.8, ShrubCut = 0.5;
        /// <summary>Rules 2: the chance from which open land keeps fewer groves, the chance at which it keeps fewest, and how
        /// much of the forest it loses there.</summary>
        public const double CoreFrom = 0.9, CoreAt = 0.98, CoreForestCut = 0.98;
        public readonly SoilSiteGrid Soils;
        /// <summary>The world's fire rules (EarthParams.FireRules, at most <see cref="RulesVersion"/>).</summary>
        public readonly int Rules;

        /// <summary>From the grassland-soil grid (<see cref="SoilSiteGrid.Asset"/>) and the world's rules version.</summary>
        public FireRegime(SoilSiteGrid soils, int rules = RulesVersion)
        {
            Soils = soils ?? throw new ArgumentNullException(nameof(soils));
            Rules = Math.Clamp(rules, 1, RulesVersion);
        }

        /// <summary>How much of the zones' forest fully open land loses at a fire chance: rules 1 <see cref="ForestCut"/>;
        /// rules 2 from it at <see cref="CoreFrom"/> up to <see cref="CoreForestCut"/> at <see cref="CoreAt"/>.</summary>
        public static double ForestCutFor(double chance, int rules)
            => rules < 2 || double.IsNaN(chance) ? ForestCut
                : ForestCut + (CoreForestCut - ForestCut) * Geo.SmoothStep((chance - CoreFrom) / (CoreAt - CoreFrom));

        /// <summary>The share of the zones' forest a place keeps (1 where fire leaves it alone), by the world's rules.</summary>
        public double ForestKeptAt(EarthData data, double lat, double lon, in ZoneDrivers d, double lapse)
        {
            if (d.TCold >= TropicsAt) return 1;
            Summer(data.Weather, lat, lon, lapse, out double t, out double p);
            if (double.IsNaN(t)) return 1;
            double soil = GrassSoilShare(lat, lon);
            // the chance once, for both the openness and the cut
            double chance = Chance(soil, t, p, d.Aridity);
            double open = double.IsNaN(p) || double.IsNaN(d.Aridity) ? 0 : OpennessFor(chance, soil, d.TCold);
            return 1 - ForestCutFor(chance, Rules) * open;
        }

        /// <summary>0..1: the share of grassland soils around the place (0 where no soil is mapped).</summary>
        public double GrassSoilShare(double lat, double lon) => Soils.GrassShare(lat, lon);

        /// <summary>The mean temperature (°C, cooled by <paramref name="lapse"/>) and rain (mm per month) of the three warmest
        /// months from the monthly normals; NaN without them.</summary>
        public static void Summer(WeatherData weather, double lat, double lon, double lapse, out double temperature, out double rain)
        {
            temperature = rain = double.NaN;
            if (weather == null) return;
            Span<double> t = stackalloc double[12], p = stackalloc double[12];
            weather.MonthlyNormals(lat, lon, t, p);
            for (int m = 0; m < 12; m++) t[m] -= lapse;
            double ts = 0, ps = 0;
            for (int k = 0; k < 3; k++)
            {
                int best = -1;
                for (int m = 0; m < 12; m++) if (!double.IsNaN(t[m]) && (best < 0 || t[m] > t[best])) best = m;
                if (best < 0) return;
                ts += t[best]; ps += p[best];
                t[best] = double.NaN;
            }
            temperature = ts / 3;
            rain = ps / 3;
        }

        /// <summary>The fitted chance (0..1) that land is kept open by fire.</summary>
        public static double Chance(double grassSoil, double summerTemperature, double summerRain, double aridity)
        {
            double z = Constant + Soil * grassSoil + Heat * summerTemperature + Rain * Math.Log(Math.Max(0, summerRain) + 5)
                + Dryness * Math.Log(Math.Max(0.05, aridity));
            return 1 / (1 + Math.Exp(-z));
        }

        /// <summary>0..1: how open fire keeps the land: 1 cuts the forest by <see cref="ForestCutFor"/> (rules 1: <see cref="ForestCut"/>).</summary>
        public static double Openness(double grassSoil, double summerTemperature, double summerRain, double aridity, double coldMonth)
        {
            if (double.IsNaN(summerTemperature) || double.IsNaN(summerRain) || double.IsNaN(aridity)) return 0;
            return OpennessFor(Chance(grassSoil, summerTemperature, summerRain, aridity), grassSoil, coldMonth);
        }

        /// <summary><see cref="Openness"/> from the fitted chance (<see cref="Chance"/>).</summary>
        static double OpennessFor(double chance, double grassSoil, double coldMonth)
        {
            double open = Geo.SmoothStep((chance - OpenFrom) / (OpenAt - OpenFrom));
            double soil = WithoutSoil + (1 - WithoutSoil) * Geo.SmoothStep(grassSoil / SoilFull);
            double extratropical = Math.Clamp((TropicsAt - coldMonth) / (TropicsAt - TropicsFrom), 0, 1);
            return open * soil * extratropical;
        }

        /// <summary>The openness at a place whose zone drivers are <paramref name="d"/> (at the ground, cooled by <paramref name="lapse"/>).</summary>
        public double OpennessAt(EarthData data, double lat, double lon, in ZoneDrivers d, double lapse)
        {
            if (d.TCold >= TropicsAt) return 0;
            Summer(data.Weather, lat, lon, lapse, out double t, out double p);
            if (double.IsNaN(t)) return 0;
            return Openness(GrassSoilShare(lat, lon), t, p, d.Aridity, d.TCold);
        }
    }
}
