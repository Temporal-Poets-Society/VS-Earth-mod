using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: the coast profile from the big land's outlines, and the small islands from theirs.
    public sealed partial class TerrainModel
    {
        /// <summary>
        /// How much the outline decides here (0..1) rather than the grid, with the outline's signed distance d: wherever the
        /// grid has the sea within 3 cells, and within half a Reach of the outline's own coasts or out on its sea (fjords and
        /// lagoons the grid hasn't got at all: the inner Fjærlandsfjord is 15 km from the grid's sea); but not on the ice
        /// (Greenland's glaciers fill fjords that GSHHG has as sea, and its north coast has shelf ice GSHHG has as land) and
        /// not south of 60°S (no outline there).
        /// </summary>
        double CoastWeight(in EarthData.TerrainSample s, double d)
        {
            double half = 0.5 * Coasts.Reach, near = 1 - Geo.SmoothStep((d - half) / half);
            return Math.Max(Geo.SmoothStep(s.SeaNear / 0.25), near) * (1 - Geo.SmoothStep(s.Ice / 30));
        }

        /// <summary>
        /// The coast's signed distance (blocks, + land, ±Reach, NaN for no outline) with 0064's works taken in
        /// (Works1318Data.Combine). With works rules 3 (EA-0042) the sea's side near a SEA area is measured to the land of
        /// 1318: today's land inside a SEA area (Rotterdam's moles and port islands) is not a coast, so neither the water
        /// over it nor today's water beside it shelves towards today's outline. Searched in rings up to Reach, only where a
        /// SEA area is within Reach and today's land is no nearer than that area.
        /// </summary>
        double CoastDistanceAt(double lat, double lon, in WorksHit wk)
        {
            double dG = Coasts.SignedDistance(lat, lon);
            double d = Works1318Data.Combine(dG, wk, Coasts.Reach);
            if (!Rules.SeaOf1318 || Works == null || double.IsNaN(d) || d >= 0 || !wk.Covered || double.IsNaN(wk.Level)
                || (wk.Flags & Works1318Data.Sea) == 0) return d;
            // outside the SEA area with today's land nearer than the area: that land is still there in 1318
            if (!wk.Inside && !double.IsNaN(dG) && -dG < wk.Dist) return d;
            // EA-0095 (FP-08): the last few searches of this thread, not the last one: a column asks for its own place up to
            // four times (CoastColumn, MouthShelf, ApplyMouth, the shore strip) with each course's centre (Trough) in between.
            // The answer is a function of the place alone (every caller's wk is WorksAt there), so it is the same either way
            var memo = coast1318Memo ??= new double[3 * Coast1318MemoSize];
            var owners = coast1318Owners ??= new long[Coast1318MemoSize];
            for (int e = 0; e < Coast1318MemoSize; e++)
                if (owners[e] == CacheId && memo[3 * e] == lat && memo[3 * e + 1] == lon) return memo[3 * e + 2];
            double reach = Coasts.Reach, step = Math.Max(1, reach / 12);
            double near = !wk.Inside && !double.IsNaN(dG) && dG < 0 ? -dG : 0;
            // (EA-0095) inside a SEA area itself (not one only enclosed by them): no edge of any area lies nearer than its Dist,
            // nor a side of its tile, so every point of a ring within both is in that same sea and no land of 1318: those
            // rings are passed over (the first ring that can hold land is searched as before)
            double clear = 0;
            if (wk.Inside)
            {
                var own = Works.At(lat, lon);
                if (own.Covered && own.Inside && (own.Flags & Works1318Data.Sea) != 0 && (own.Flags & Works1318Data.Land) == 0)
                    clear = Math.Min(own.Dist, Works.TileSideDistance(lat, lon)) - 0.01;
            }
            Geometry.FromGeo(lat, lon, out double x, out double z);
            double found = reach;
            for (double r = Math.Max(step, near); r < reach && found >= reach; r += step)
            {
                if (r <= clear) continue;
                int n = Math.Clamp((int)Math.Ceiling(2 * Math.PI * r / step), 8, 32);
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * k / n;
                    var g = Geometry.ToGeo(x + r * Math.Cos(a), z + r * Math.Sin(a));
                    if (Land1318(g.Lat, g.Lon)) { found = r; break; }
                }
            }
            // the land found lies between the last ring and this one; never nearer than today's land
            double dist = found >= reach ? reach : Math.Max(near, found - 0.5 * step);
            double result = -Math.Clamp(dist, 0.5, reach);
            int slot = coast1318Next;
            coast1318Next = (slot + 1) % Coast1318MemoSize;
            memo[3 * slot] = lat; memo[3 * slot + 1] = lon; memo[3 * slot + 2] = result; owners[slot] = CacheId;
            return result;
        }

        /// <summary>
        /// Land of 1318 for <see cref="CoastDistanceAt"/>: a LAND area of 0064, or the outline's land outside its SEA areas.
        /// The same answer as WorksAt's hit and then the outline (EA-0095): the enclosure search (EnclosedBySea) can only make
        /// a place outside every area "inside the sea", so it is asked only where the outline has land, and its answer for a
        /// ring's point is not kept in place of the column's own.
        /// </summary>
        bool Land1318(double lat, double lon)
        {
            var h = Works.At(lat, lon);
            if (h.Covered && h.Inside && (h.Flags & Works1318Data.Land) != 0) return true;
            if (h.Covered && h.Inside && (h.Flags & Works1318Data.Sea) != 0) return false;
            double s = Coasts.SignedDistance(lat, lon);
            if (double.IsNaN(s) || s < 0) return false;
            if (Rules.SeaOf1318 && h.Covered && !h.Inside && !double.IsNaN(h.Level) && (h.Flags & Works1318Data.Sea) != 0)
            {
                var e = EnclosedBySea(lat, lon, h, remember: false);
                if (e.Covered && e.Inside && (e.Flags & Works1318Data.Sea) != 0) return false;
            }
            return true;
        }

        const int Coast1318MemoSize = 8;
        [ThreadStatic] static double[] coast1318Memo;
        // the model's CacheId, not the model: a pool thread would keep a closed world's model alive (ER-22, T-3)
        [ThreadStatic] static long[] coast1318Owners;
        [ThreadStatic] static int coast1318Next;

        /// <summary>
        /// Outline signed distance for the land/sea decision (blocks, + land, ±Reach): the grid's narrow channels (sea fraction
        /// s.Ocean) keep at least CoastLayer.ChannelBlocks of water, and the outline makes no sea right beside the grid's lakes.
        /// </summary>
        double CoastSide(double d, in EarthData.TerrainSample s)
        {
            double f = d - 0.5 * Coasts.ChannelBlocks * s.Ocean;
            if (f < 0) f *= 1 - Geo.SmoothStep(s.Lake / 0.25);
            return Math.Clamp(f, -Coasts.Reach, Coasts.Reach);
        }

        /// <summary>
        /// A column near the big land's coast where the world's blocks are much smaller than the grid's cells (Coasts): land
        /// or sea from the GSHHG outline, and an elevation that keeps the land above the water and the sea below it.
        /// Within Reach of the coast the ground follows a profile from CoastLand (land) or CoastSea (sea floor) at the coast
        /// line to the grid's own elevation of that side (LandElev / SeaElev: where the outline has land over the grid's sea
        /// it rises like the land around, a fjord's walls from the outline's narrower water), steeper the more rugged the
        /// land (cliffs in Dalmatia and Norway, beaches on deltas). Where the grid's sea is further than a few cells or there
        /// is ice, away from the outline's own coasts, the grid's own decision and elevation take over smoothly (CoastWeight).
        /// The sea deepens to MinSeaDepth a Reach out, and dry land is held at the level NaturalColumn holds it at (hold:
        /// with eased lake shores it sinks away from a lake). False where the grid decides alone.
        /// </summary>
        bool CoastColumn(double x, double z, EarthData.TerrainSample s, double detail, double h, double hold, int lakeTop, double fine, in WorksHit wk, ref Column c)
        {
            // the open sea far from any coast, and ice: the grid's own column
            if (s.Ice >= 30 || OpenOcean(s)) return false;
            double d = CoastDistanceAt(c.Lat, c.Lon, wk);
            if (double.IsNaN(d)) return false;
            // 0064: inside the sea or land of 1318 the outline decides alone
            double w = Works1318Data.Undoes(wk) ? 1 : CoastWeight(s, d);
            // inland, away from the outline's coasts: the grid's own column
            if (w <= 0) return false;
            if (double.IsNaN(s.LandElev)) Data.AddCoastRefs(c.Lat, c.Lon, ref s);
            double reach = Coasts.Reach, fo = CoastSide(d, s);
            bool landSide = fo >= 0;
            double dist = Math.Abs(fo);
            // the profile: 1 - (1 - t)^p rises fast from the coast where p is high (rugged land), gently where it is low; on
            // rugged land the coast line itself stands a few metres up and drops into deeper water (cliffs, not beaches)
            double rugged = Geo.SmoothStep((s.Relief - 150) / 1000), p = 1.5 + 3 * rugged;
            double g = 1 - Math.Pow(1 - Math.Min(dist / reach, 1), p);
            double edge = landSide ? CoastLand + 8 * rugged : CoastSea - 4 * rugged;
            double ho = edge + ((landSide ? s.LandElev + detail : Math.Min(s.SeaElev + detail, 0.5 * s.SeaElev)) - edge) * g;
            // the grid's own land/sea, as a signed value of the same kind: sea where it has sea and the ground is below 0
            double fg = s.Ocean > 0 ? Math.Clamp(h, -reach, reach) : reach;
            bool land = w * fo + (1 - w) * fg >= 0;
            double e = w * ho + (1 - w) * h;
            int sea = SeaLevel, top = (int)Math.Floor(Vertical.Y(e));
            c.Elev = e;
            if (!land)
            {
                // a block deep at the coast, the open sea's MinSeaDepth a Reach out
                int minDepth = 1 + (int)Math.Round((MinSeaDepth - 1) * Geo.SmoothStep(landSide ? 0 : (dist - 1) / reach));
                c.Top = Math.Min(top, sea - 1 - minDepth);
                c.WaterTop = sea - 1;
                c.Water = WaterSea;
                return true;
            }
            // dry land on the outline's side: GLO-90's height where this world has it (0051), not the profile from the coast.
            // Where GLO-90 has water (0 m: its shore lies off the outline's) the land is a low strip at the sea.
            if (!double.IsNaN(fine))
            {
                c.Glo90Land = true;
                c.GridElev = e;
                e = fine;
                top = (int)Math.Floor(Vertical.Y(e));
                c.Elev = e;
            }
            // dry land: never below a nearby water surface; at the coast flush with the sea (beach) where it is low
            if (!double.IsNaN(s.ShoreLevel)) top = Math.Max(top, WaterTopY(hold));
            top = Math.Max(top, sea - 1);
            double l = double.IsNaN(s.LakeLevel) ? 0 : s.Lake;
            // the beach's top fades from BeachHeight at the coast to 0 m a Reach inland. GLO-90 rules 5: beyond that nothing is
            // a beach; before, its 0 m made all land below the sea one wherever the outline weighs at all (fields and fish
            // ponds a metre below the sea 30 km from the coast near Suez, nearly all of the Dutch polders). The fade runs
            // from the outline itself where that is further than the side's narrowed channel: where the grid has sea over
            // the land (polders) the channel moved the coast inland, so dist never reached Reach and the top stayed above 0.
            double beachDist = Rules.FineBounded && d > dist ? Math.Min(d, reach) : dist;
            double beachTop = BeachHeight * (landSide ? 1 - Geo.SmoothStep(beachDist / reach) : 1);
            if (e < beachTop && (!Rules.FineBounded || beachTop > 0))
            {
                top = sea - 1;
                c.Beach = true;
            }
            else if (l > 0 && e < s.LakeLevel + BeachHeight * Geo.SmoothStep(l / 0.35))
            {
                top = lakeTop;
                c.Beach = true;
            }
            c.Top = top;
            if (s.Ice > 15)
            {
                int yBed = (int)Math.Floor(Vertical.Y(e - s.Ice));
                if (yBed < top) c.IceBottom = Math.Max(1, yBed + 1);
                c.Beach = false;
            }
            return true;
        }

        /// <summary>Land or sea at a place by the big land's outline, where it decides more than the grid (for the ocean map);
        /// false where the grid decides or there are no outlines.</summary>
        public bool CoastLandAt(double lat, double lon, out bool land)
        {
            bool decides = CoastSideAt(lat, lon, out double side);
            land = decides && side >= 0;
            return decides;
        }

        /// <summary>The outline's land/sea decision at a place as a signed distance (blocks, + land, ±Reach; CoastSide), where it
        /// decides more than the grid; false where the grid decides or there are no outlines (SeaChannels rules 4).</summary>
        public bool CoastSideAt(double lat, double lon, out double side)
        {
            side = 0;
            if (Coasts == null) return false;
            var s = Data.SampleTerrain(lat, lon);
            var wk = WorksAt(lat, lon);
            double d = CoastDistanceAt(lat, lon, wk);
            if (double.IsNaN(d)) return false;
            if (Works1318Data.Undoes(wk)) UndoWater(ref s, wk);
            else if (CoastWeight(s, d) < 0.5) return false;
            side = CoastSide(d, s);
            return true;
        }

        /// <summary>
        /// An island from its outline (IslandLayer): the land rises from a beach at the coast to the island's height at its innermost
        /// point, with a little relief away from the shore; around it the sea floor comes up to the shore (1 block deep
        /// there, a block deeper every 2 blocks out). Where the grid's own land is higher, that stays. Not in lakes. Works rules
        /// 4: not inside a SEA area those rules add (Works1318Data.Late): the island layer holds pieces of land made there after
        /// 1318 (the water mask's Maasvlakte 2), while 0064's own SEA areas keep their islands (Urk, Schokland).
        /// </summary>
        void ApplyIslands(double x, double z, ref Column c, ref ColumnWork work)
        {
            if (c.Water == WaterLake || c.Water == WaterSaltLake || !IslandAt(ref work, c.Lat, c.Lon, out var hit)) return;
            if (Rules.IslandsOf1318)
            {
                var wk = WorksAt(ref work, c.Lat, c.Lon);
                if (wk.Inside && (wk.Flags & (Works1318Data.Sea | Works1318Data.Late)) == (Works1318Data.Sea | Works1318Data.Late)) return;
            }
            int sea = SeaLevel;
            if (hit.Inside)
            {
                double h = hit.Height * Geo.SmoothStep(hit.Dist / hit.ProfileBlocks);
                h += Geo.SmoothStep(hit.Dist / 4) * (0.5 + 0.08 * hit.Height) * SeamNoise(n2, x, z, 56, 3) * DetailScale;
                h = Math.Max(h, 0);
                // low cays (2 m) are not all beach: their middle stands a block above the water
                bool beach = h < Math.Min(BeachHeight, 0.5 * hit.Height);
                int top = beach ? sea - 1 : Math.Max(sea, (int)Math.Floor(Vertical.Y(h)));
                if (c.Water == WaterNone && c.Top >= top) return;
                c.IslandOverSea = c.Water == WaterSea;
                c.Top = top;
                c.Water = WaterNone;
                c.WaterTop = -1;
                c.Beach = beach;
                c.Elev = h;
                c.Glo90Land = false;
                c.IceBottom = int.MaxValue;
            }
            else if (c.Water == WaterSea)
            {
                c.Top = Math.Max(c.Top, sea - 2 - (int)(hit.Dist / 2));
            }
        }
    }
}
