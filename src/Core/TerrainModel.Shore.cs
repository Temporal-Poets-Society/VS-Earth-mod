using System;

namespace EarthMap.Core
{
    // TerrainModel, part: decision 0070's shore strip.
    public sealed partial class TerrainModel
    {
        /// <summary>Decision 0070 shore rules 2: a foredune of sand this share of the beach's width behind it.</summary>
        public const double ShoreDuneShare = 0.5;
        /// <summary>Shore rules 2: a beach lies at the water's level only where the shore stands no higher than this (m) above
        /// the water and the land around is no more rugged than FlatShoreRelief (the grid's relief, m): a bluff of dunes or
        /// till keeps a beach at its foot, a fjord's or a rocky coast's wall keeps its height.</summary>
        public const double FlatShoreMetres = 25, FlatShoreRelief = 300;
        /// <summary>Shore rules 2: the sea or lake floor this share of the beach's width out from it takes the beach's
        /// material (the shallow sand in front of a sandy beach, not the open floor's gravel or mud).</summary>
        public const double NearshoreShare = 1.0;
        /// <summary>Shore rules 2: behind a beach at the water's level the ground rises at most a block per block (the
        /// foredune's slope, then the land's), up to this many blocks beyond the foredune, so there is no wall at the beach's
        /// back.</summary>
        public const int ShoreRampBlocks = 4;

        /// <summary>
        /// Decision 0070 (ShoreRules): the shore of the sea (the coast outlines, where they decide), of a small island, or of a
        /// lake of LakeMinKm2 or more (the lake outlines; LakeShare of the width) is never soil to the water.
        /// <list type="bullet">
        /// <item>The beach: dry land within ShoreMaterial.WidthMetres (by OSM's class there, varied along the coast, at least
        /// one block) is a beach (Beach, Shore); with shore rules 2 it lies at the water's own level, flush with it, where the
        /// shore is low (FlatShoreMetres, FlatShoreRelief) and not OSM's rock, so the beach is several blocks wide at the
        /// water and not a step of sand on top of the coast.</item>
        /// <item>Shore rules 2, the foredune: the next ShoreDuneShare of the width is sand; behind a beach at the water's level
        /// the ground rises from it at most a block per block (ShoreRampBlocks beyond the dune), a slope rather than a wall,
        /// and a step that remains is the dune's sand face rather than soil or till.</item>
        /// <item>Shore rules 2, the shallows: sea or lake water within NearshoreShare of the width of the shore carries the
        /// class too (Shore, with Water), so the floor in front of a sandy beach is sand.</item>
        /// </list>
        /// Every beach of the older rules (low ground at the sea, a lake's or a river's water) takes the class too, so its
        /// material is the place's own. Glacier ice, river water and dry river beds stay as they are.
        /// </summary>
        void ApplyShoreStrip(double x, double z, ref Column c)
        {
            if (c.River || c.DryRiver || c.IceBottom <= c.Top) return;
            bool water = c.Water != WaterNone;
            if (water && ShoreRules < 2) return;
            double mpb = Math.Max(1e-3, MetresPerBlock);
            byte cls = ShoreData?.At(c.Lat, c.Lon) ?? ShoreMaterial.None;
            if (cls == ShoreMaterial.None) cls = ShoreMaterial.Rule;
            double vary = 1 + ShoreMaterial.WidthVary * SeamNoise(n3, x, z, Math.Max(8, 400 / mpb), 2);
            double width = Math.Max(1, ShoreMaterial.WidthMetres(cls) * vary / mpb);

            if (water)
            {
                // the shallows in front of the shore
                double out_ = Math.Max(1, NearshoreShare * width);
                if (c.Water == WaterSea && Coasts != null)
                {
                    double d = CoastDistanceAt(c.Lat, c.Lon, WorksAt(c.Lat, c.Lon));
                    if (!double.IsNaN(d) && d <= 0 && -d <= out_) c.Shore = cls;
                }
                else if (c.Water is WaterLake or WaterSaltLake && LakeOutlines != null && c.OutlineLake)
                {
                    var lk = LakeOutlines.At(c.Lat, c.Lon);
                    if (lk.Inside && lk.Area * mpb * mpb >= ShoreMaterial.LakeMinKm2 * 1e6 && lk.Dist <= Math.Max(1, out_ * ShoreMaterial.LakeShare))
                        c.Shore = cls;
                }
                return;
            }

            // a beach of the older rules (low ground at the sea, a lake or a river): the coast's material; with shore rules 2
            // only within the strip of the sea's, an island's or a big lake's shore (below), river banks and lake beaches as
            // they are
            bool older = c.Beach;
            if (older && (ShoreRules < 2 || c.NearRiver || c.NearLakeTop >= 0))
            {
                c.Shore = cls;
                return;
            }
            double dune = ShoreRules >= 2 ? width * ShoreDuneShare : 0;
            double strip = width + dune, reach = ShoreRules >= 2 ? strip + ShoreRampBlocks : strip;

            // distance (blocks, as strip widths at the sea) to the nearest water that has a strip, that water's top block
            // and level (m), and how rugged the land around is (m)
            double dist = double.PositiveInfinity, level = 0, relief = double.NaN;
            int waterTop = SeaLevel - 1;
            // the sea, where the coast outlines decide (as CoastColumn: the outline's side, its narrow channels kept)
            if (Coasts != null)
            {
                var wk = WorksAt(c.Lat, c.Lon);
                double d = CoastDistanceAt(c.Lat, c.Lon, wk);
                if (!double.IsNaN(d) && d <= reach + Coasts.ChannelBlocks)
                {
                    var s = Data.SampleTerrain(c.Lat, c.Lon);
                    bool undo = Works1318Data.Undoes(wk);
                    if (undo) UndoWater(ref s, wk);
                    // only on the outline's land side: land the outline has as sea is an island's or the grid's, and
                    // their own distance decides (Buļļu sala at the Daugava's mouth is GSHHG's sea)
                    double fo = CoastSide(d, s);
                    if (s.Ice < 30 && fo >= 0 && (undo || CoastWeight(s, d) >= 0.5)) { dist = fo; relief = s.Relief; }
                }
            }
            // a small island's coast
            if (dist > reach && Islands != null && Islands.Query(c.Lat, c.Lon, out var ih) && ih.Inside && ih.Dist < dist)
            {
                dist = ih.Dist;
                relief = double.NaN;
            }
            // a big lake's
            if (dist > reach && LakeOutlines != null && OutlinesDecide(c.Lat, c.Lon))
            {
                var lk = LakeOutlines.At(c.Lat, c.Lon);
                double ld = Math.Max(0, lk.Dist - 0.5) / ShoreMaterial.LakeShare;
                if (lk.Covered && !lk.Inside && !double.IsNaN(lk.Level) && lk.Area * mpb * mpb >= ShoreMaterial.LakeMinKm2 * 1e6 && ld < dist)
                {
                    dist = ld;
                    level = lk.Level;
                    waterTop = WaterTopY(lk.Level);
                    relief = double.NaN;
                }
            }
            if (older && dist > strip && !double.IsPositiveInfinity(dist))
            {
                // shore rules 2: low land beyond the strip is land, not one beach (whole low islands and spits were beach)
                c.Beach = false;
                return;
            }
            if (older && double.IsPositiveInfinity(dist))
            {
                // a coast the grid decides alone: no distance to measure, the older beach stays
                c.Shore = cls;
                return;
            }
            if (dist > reach) return;
            if (dist <= strip)
            {
                c.Beach = true;
                c.Shore = cls;
            }
            if (ShoreRules < 2 || cls == ShoreMaterial.Rock || c.Top <= waterTop) return;
            // the beach at the water's level and the slope behind it, where the shore is low
            if (double.IsNaN(relief)) relief = Data.SampleTerrain(c.Lat, c.Lon).Relief;
            int cap = dist <= width ? waterTop : waterTop + (int)Math.Ceiling(dist - width);
            // (a shore higher than FlatShoreMetres above the slope's own height there keeps its height)
            if (relief > FlatShoreRelief || c.Elev - Math.Max(level, Vertical.Elevation(cap + 1)) > FlatShoreMetres) return;
            if (c.Top > cap) c.Top = cap;
        }
    }
}
