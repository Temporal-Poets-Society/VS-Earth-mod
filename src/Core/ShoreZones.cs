using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0069 rule 7 (EarthParams.ShoreZoneRules): plants by their distance from the water, so the forest no longer
    /// runs onto beaches and to the edge of every river and lake (the author: "Coastal zones and river banks"). Vanilla only
    /// refuses a tree or shrub on a block of fertility 0, and sand has 10. The widths are real metres (the research note
    /// docs/research/shore-zones-2026-11-12.md), so a world of any scale gets the zones it can draw:
    /// <list type="bullet">
    /// <item>a beach (Column.Beach): bare of trees and shrubs;</item>
    /// <item>a low sea coast: no trees or shrubs within SeaOpenMetres of the coast (the foredune's marram), no trees within
    /// SeaScrubMetres (dune grass and scrub), then the forest thinning in over as much again;</item>
    /// <item>a river: its low bank up to flood height is bare for a quarter of the river's real width (8√Q m), at most
    /// RiverBankMaxMetres, the forest thinning in over as much again (the riparian species of the region's catalogue
    /// still take the wet sites beyond); small streams keep their trees at the water;</item>
    /// <item>a lake: a sedge shore of LakeShoreMetres, the forest thinning in over as much again.</item>
    /// </list>
    /// Rules 1: a coast is low where the shore is under LowCoastMetres, and only low coasts have the open belts. Rules 2
    /// (decision 0070): every sea coast has them, a cliff top as much as a dune (salt wind keeps the forest off the chalk of
    /// the Pays de Caux as off Jūrmala's dunes), except where the land is rugged (the grid's relief over
    /// TerrainModel.FlatShoreRelief: fjords and rocky sheltered coasts keep their trees to the water).
    /// </summary>
    public static class ShoreZones
    {
        public const int RulesVersion = 2;
        public const double SeaOpenMetres = 40, SeaScrubMetres = 100, LowCoastMetres = 12;
        public const double RiverBankShare = 0.25, RiverBankMaxMetres = 30;
        public const double LakeShoreMetres = 15;
        /// <summary>A river's or lake's zones reach only ground up to this far above its water (its flood height): a cliff or
        /// a high bank above a river keeps its trees.</summary>
        public const double BankRiseMetres = 3;

        /// <summary>The share of plants a place keeps (0 bare, 1 all), for a tree or (<paramref name="shrub"/>) a shrub.</summary>
        public static double Keep(TerrainModel model, double x, double z, in TerrainModel.Column c, bool shrub)
        {
            if (c.Beach) return 0;
            double mpb = Math.Max(1e-3, model.MetresPerBlock);
            double keep = 1;
            // the sea coast (the coast outlines' distance, blocks, positive on land)
            if (model.Coasts != null && (c.Elev < LowCoastMetres || model.ShoreZoneRules >= 2))
            {
                double d = model.Coasts.SignedDistance(c.Lat, c.Lon);
                if (!double.IsNaN(d) && d >= 0 && d * mpb < 2 * SeaScrubMetres
                    && (c.Elev < LowCoastMetres || !(model.Data.SampleTerrain(c.Lat, c.Lon).Relief > TerrainModel.FlatShoreRelief)))
                {
                    double m = d * mpb;
                    if (m < SeaOpenMetres) return 0;
                    if (!shrub) keep = Math.Min(keep, Ramp(m, SeaScrubMetres));
                }
            }
            // a river: from the nearest course's water, a quarter of its real width
            if (model.Rivers != null && model.Rivers.Nearest(x, z, out var hit) && !hit.Mouth
                && model.Vertical.Elevation(c.Top) - model.Vertical.Elevation(hit.WaterTop) <= BankRiseMetres)
            {
                double bank = Math.Min(RiverBankMaxMetres, RiverBankShare * RiverNetwork.RealWidthMetres(hit.Q));
                double m = Math.Max(0, hit.Dist - hit.HalfWidth) * mpb;
                keep = Math.Min(keep, Ramp(m, bank));
            }
            // the water mask's river beside this column (decision 0053 item 18): its shore distance
            if (c.RiverOutline == TerrainModel.RiverBesideOutline && c.Elev - c.RiverOutlineLevel <= BankRiseMetres)
                keep = Math.Min(keep, Ramp(c.RiverOutlineDist * mpb, RiverBankMaxMetres / 2));
            // a lake (the water mask's outlines)
            if (model.LakeOutlines != null)
            {
                var lk = model.LakeOutlines.At(c.Lat, c.Lon);
                if (lk.Covered && !lk.Inside && !double.IsNaN(lk.Level) && c.Elev - lk.Level <= BankRiseMetres) keep = Math.Min(keep, Ramp(lk.Dist * mpb, LakeShoreMetres));
            }
            return keep;
        }

        /// <summary>0 within <paramref name="zone"/> metres, rising to 1 at twice that.</summary>
        static double Ramp(double metres, double zone) => zone <= 0 ? 1 : Math.Clamp((metres - zone) / zone, 0, 1);
    }
}
