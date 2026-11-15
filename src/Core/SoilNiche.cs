using System;

namespace EarthMap.Core
{
    /// <summary>
    /// How well a place's soil suits a species, from the species' Ecological Indicator Values for Europe (EIVE 1.0, Dengler
    /// et al. 2023, CC BY 4.0): niche position and width for moisture (M), nutrients (N) and soil reaction (R), each on EIVE's
    /// 0..10 scale (<see cref="RegionalTreeCatalog.Rule.Eive"/>). The place's values come from the soil site grid
    /// (<see cref="SoilSiteGrid"/>) and the climate, put on the same scales:
    /// <list type="bullet">
    /// <item>M: the drained ground by the aridity index, 5 (fresh) at P = PET, 2 log units per e-fold, +1.5 near rivers and
    /// lakes; the share of waterlogged soils (Gleysols, Histosols ...) at <see cref="WaterloggedMoisture"/>;</item>
    /// <item>N: <see cref="RichNutrients"/> (more on the black grassland soils), the share of leached, sandy and peat soils at
    /// <see cref="PoorNutrients"/>;</item>
    /// <item>R: from topsoil pH, Ellenberg's reaction classes (pH 3.5: 1, 4.5: 3, 5.5: 5, 6.5: 7, 7.5: 9).</item>
    /// </list>
    /// A species fits each axis by a Gaussian around its position. EIVE's width (nw3) is a range: the mean amplitude of the
    /// source systems plus twice the spread of their positions, capped at 10; sigma is the standard deviation of an even
    /// spread across it (width / sqrt 12, at least 1). Over the mixed ground of a place the fit is the share-weighted mean
    /// (alder on the wet share, pine on the sandy share), and the three axes multiply. The picker redistributes weight among
    /// the rated species by their fit and keeps their total, so soil decides the mix, not how much forest grows.
    /// </summary>
    public static class SoilNiche
    {
        public const float WaterloggedMoisture = 7.5f, PoorNutrients = 2.5f, NearWaterMoisture = 1.5f;
        public const float MinSigma = 1f;

        /// <summary>EIVE M of the drained ground at this aridity index (precipitation / PET).</summary>
        public static float DrainedMoisture(double aridity, bool nearWater)
        {
            if (!(aridity > 0)) aridity = 0.01;
            return (float)Math.Clamp(5 + 2 * Math.Log(aridity) + (nearWater ? NearWaterMoisture : 0), 1, 9);
        }

        /// <summary>EIVE N of the richer ground: 5.5, up to 7.5 on black grassland soils.</summary>
        public static float RichNutrients(double grassShare) => (float)(5.5 + 2 * Math.Clamp(grassShare, 0, 1));

        /// <summary>EIVE R at this topsoil pH.</summary>
        public static float Reaction(double ph) => (float)Math.Clamp(2 * (ph - 3), 0, 10);

        /// <summary>The niche of a rule's EIVE values as position and sigma for M, N, R; null if incomplete.</summary>
        public static float[] Niche(RegionalTreeCatalog.Indicators eive)
        {
            if (eive?.M == null || eive.N == null || eive.R == null || eive.M.Length != 2 || eive.N.Length != 2 || eive.R.Length != 2) return null;
            var n = new float[6];
            double[][] axes = { eive.M, eive.N, eive.R };
            for (int a = 0; a < 3; a++)
            {
                double pos = axes[a][0], width = axes[a][1];
                if (!double.IsFinite(pos) || !double.IsFinite(width) || width < 0) return null;
                n[2 * a] = (float)pos;
                n[2 * a + 1] = (float)Math.Max(MinSigma, width / Math.Sqrt(12));
            }
            return n;
        }

        static double Gauss(double x, float pos, float sigma)
        {
            double z = (x - pos) / sigma;
            return Math.Exp(-0.5 * z * z);
        }

        /// <summary>0..1: how well the site's soil suits a species with this niche (<see cref="Niche"/>); 1 without either.</summary>
        public static float Fit(float[] niche, in TreeSite s)
        {
            if (niche == null || !s.HasSoil) return 1f;
            if (s.ColumnSite)
            {
                // site rules 1: one place, one value per axis (no shares to mix)
                double cr = float.IsNaN(s.SoilPh) ? 1 : Gauss(Reaction(s.SoilPh), niche[4], niche[5]);
                return (float)Math.Max(1e-4, Gauss(s.SiteMoisture, niche[0], niche[1]) * Gauss(s.SiteNutrients, niche[2], niche[3]) * cr);
            }
            double wet = Math.Clamp(s.WetShare, 0, 1), poor = Math.Clamp(s.PoorShare, 0, 1);
            double m = (1 - wet) * Gauss(s.SoilMoisture, niche[0], niche[1]) + wet * Gauss(WaterloggedMoisture, niche[0], niche[1]);
            double n = (1 - poor) * Gauss(RichNutrients(s.GrassShare), niche[2], niche[3]) + poor * Gauss(PoorNutrients, niche[2], niche[3]);
            double r = float.IsNaN(s.SoilPh) ? 1 : Gauss(Reaction(s.SoilPh), niche[4], niche[5]);
            return (float)Math.Max(1e-4, m * n * r);
        }

        /// <summary>
        /// Adds the mapped wetlands (<see cref="WetlandGrid"/>) to a site's soil: the trees that still stand where open bog,
        /// marsh and saltmarsh take part of the ground grow on its wooded wetland, so the waterlogged share among them is the
        /// wooded peat, swamp forest and half the damp ground over the wooded part; the peat is also nutrient-poor. The soil
        /// map's own share stands where it is larger (its 2' classes see small mires the wetland map's dominant class drops).
        /// </summary>
        public static void AddWetlands(ref TreeSite s, WetlandGrid wetlands, double lat, double lon)
        {
            if (!s.HasSoil || wetlands == null) return;
            double open = wetlands.OpenShare(lat, lon), wooded = Math.Max(0.05, 1 - open);
            double peat = wetlands.Share(WetlandGrid.PeatForest, lat, lon);
            double wet = (peat + wetlands.Share(WetlandGrid.SwampForest, lat, lon) + 0.5 * wetlands.Share(WetlandGrid.Damp, lat, lon)) / wooded;
            s.WetShare = (float)Math.Max(s.WetShare, Math.Min(1, wet));
            s.PoorShare = (float)Math.Max(s.PoorShare, Math.Min(1, peat / wooded));
        }

        /// <summary>Fills a site's soil from the grid at a place (HasSoil false where none is mapped).</summary>
        public static void Fill(ref TreeSite s, SoilSiteGrid soils, double lat, double lon, double aridity, bool nearWater)
        {
            s.HasSoil = false;
            if (soils == null) return;
            double wet = soils.Value(SoilSiteGrid.Wet, lat, lon);
            if (double.IsNaN(wet)) return;
            s.HasSoil = true;
            s.WetShare = (float)wet;
            s.PoorShare = (float)soils.Value(SoilSiteGrid.Poor, lat, lon);
            s.GrassShare = (float)soils.Value(SoilSiteGrid.Grass, lat, lon);
            s.SoilPh = (float)soils.Value(SoilSiteGrid.Ph, lat, lon);
            s.SoilMoisture = DrainedMoisture(aridity, nearWater);
        }
    }
}
