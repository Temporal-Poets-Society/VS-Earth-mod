using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: the natural column: the works of 1318, the canal strip, the water mask's lakes, and the grid column.
    public sealed partial class TerrainModel
    {
        Column NaturalColumn(double x, double z, double lat, double lon)
        {
            var work = new ColumnWork();
            return NaturalColumn(x, z, lat, lon, ref work);
        }

        Column NaturalColumn(double x, double z, double lat, double lon, ref ColumnWork work)
        {
            var s = Data.SampleTerrain(lat, lon, Coasts != null);
            // 0064: the works after 1318 undone: a lake of 1318 at its level; inside the sea or land of 1318 none of today's
            // lakes, and no grid sea on the land
            var wk = WorksAt(ref work, lat, lon);
            if (Works1318Data.IsLake(wk)) return OutlineLakeColumn(x, z, lat, lon, wk.LakeOf1318, 0);
            bool undo = Works1318Data.Undoes(wk);
            // channel rules 1: no outline water along the Suez and Panama canals (0020: the land as before them; the canal
            // option cuts its own), nor the grid's lakes there, salt or kept (the Great Bitter Lake was a dry salt basin until
            // the canal filled it in 1869, Gatun Lake is the canal's reservoir)
            bool canal = Rules.Dredged && (LakeOutlines != null || RiverOutlines != null) && SeaChannels.InCanalZone(lat, lon);
            if (canal) StripGridLakes(ref s, lat, lon, all: true);
            // 0053 item 17: the water mask's lakes from their outlines, in place of the grid's fresh lakes
            LakeOutlineLayer.Hit lk = default;
            if (OutlinesDecide(lat, lon))
            {
                lk = LakeOutlines.At(lat, lon);
                if (lk.Covered)
                {
                    double gridDepth = StripGridLakes(ref s, lat, lon);
                    if (canal) lk = new LakeOutlineLayer.Hit { Covered = true, Level = double.NaN, HoldLevel = double.NaN, Dist = LakeOutlines.Reach };
                    // (channel rules 4: a small mask lake in the river's water at its level is the river's: JoinsRiver)
                    if (lk.Inside && !undo && !canal && JoinsRiver(in lk, lat, lon, out var joined))
                    {
                        var jc = GridColumn(x, z, lat, lon, s, wk);
                        if (jc.Water == WaterNone && !jc.Gutter && jc.IceBottom > jc.Top)
                        {
                            jc.RiverOutline = RiverInOutline;
                            jc.RiverOutlineDist = (float)RiverOutlines.Reach;
                            jc.RiverOutlineLevel = (float)joined.Level;
                            return jc;
                        }
                    }
                    if (lk.Inside && !undo)
                    {
                        var oc = OutlineLakeColumn(x, z, lat, lon, lk, gridDepth);
                        // works rules 6 (decision 0107): a lower lake of today right beside a lake of 1318 gives way to its shore
                        if (Rules.ShoresOf1318 && wk.BesideLake1318(out var by1318)) ShoreOf1318(ref oc, by1318);
                        return oc;
                    }
                }
            }
            if (undo) UndoWater(ref s, wk);
            var c = GridColumn(x, z, lat, lon, s, wk);
            if (lk.Covered && !undo) OutlineShore(ref c, lk);
            // works rules 6 (decision 0107): beside a lake of 1318 the land is held at its water top as beside the mask's lakes
            if (Rules.ShoresOf1318 && !undo && wk.BesideLake1318(out var lake1318)) ShoreOf1318(ref c, lake1318);
            // 0053 item 18: dry land in or beside the water mask's rivers, for ApplyRiver
            if (RiverOutlines != null && !undo && !canal && c.Water == WaterNone && !c.Gutter && c.IceBottom > c.Top && RiverOutlinesDecide(lat, lon))
            {
                MarkRiverOutline(ref c, lat, lon);
                // (channel rules 4: river water whose nearest shore is a small lake that joins it measures no shore there)
                if (c.RiverOutline == RiverInOutline && lk.Covered && !lk.Inside && lk.Dist <= c.RiverOutlineDist + 1
                    && JoinsRiver(in lk, lat, lon, out _, c.RiverOutlineLevel))
                    c.RiverOutlineDist = (float)RiverOutlines.Reach;
            }
            return c;
        }

        /// <summary>
        /// Works rules 6 (decision 0107): a column beside a lake of 1318 (outside it, the nearest works area a LAKE area): dry
        /// land held at its water top as beside the water mask's lakes (OutlineShore); and water of a lower lake (today's
        /// fishponds and reservoirs in the drained bed, the grid's lakes) within LakeOutlineLayer.HoldReach of it gives way to a
        /// strip of shore at the higher of the two tops, as a lower lake beside a higher one does, so no water stands against
        /// another at a different level.
        /// </summary>
        void ShoreOf1318(ref Column c, in LakeOutlineLayer.Hit lake)
        {
            if (c.Water == WaterNone || c.Gutter) { OutlineShore(ref c, lake); return; }
            int top = WaterTopY(lake.Level);
            if (c.River || c.Water == WaterSea || lake.Dist > LakeOutlineLayer.HoldReach || top == c.WaterTop) return;
            // (a higher lake of today, Zumpango's regulated lagoon, keeps a rim at its own top between them)
            top = Math.Max(top, c.WaterTop);
            c.Water = WaterNone;
            c.WaterTop = -1;
            c.Top = c.LakeRim = c.LakeRingTop = c.LakeHoldTop = top;
            c.Beach = true;
            c.OutlineLake = false;
            c.IceBottom = int.MaxValue;
            c.NearLakeTop = top;
        }

        /// <summary>
        /// Channel rules 4: whether the mask's lake at hand (lk, inside or the nearest outside) is a small pool in the mask's
        /// river water at the river's level, which then is river water (NaturalColumn) and whose edge is no shore for the
        /// river's bed: no larger than JoinedLakeArea, its water within MaskPondRise blocks of the river's (riverLevel, m, or
        /// the river outlines' level here), and inside the river's water or beside it (within HoldReach). Before, such a pool
        /// was a lake of its own a block or more off the river's level, with a ring of shallow river bed round it (the
        /// Daugava where the Mīlgrāvis joins it). river: the river outlines' hit here.
        /// </summary>
        bool JoinsRiver(in LakeOutlineLayer.Hit lk, double lat, double lon, out LakeOutlineLayer.Hit river, double riverLevel = double.NaN)
        {
            river = default;
            if (!Rules.OneBed || RiverOutlines == null || !RiverOutlinesDecide(lat, lon) || double.IsNaN(lk.Level)) return false;
            double px = FineElevation.CellMetres / MetresPerBlock;
            if (!(LakeArea(lk) <= JoinedLakePixels * px * px)) return false;
            river = RiverOutlines.At(lat, lon);
            if (!river.Covered || double.IsNaN(river.Level) || !(river.Inside || river.Dist <= LakeOutlineLayer.HoldReach + px)) return false;
            double level = double.IsNaN(riverLevel) ? river.Level : riverLevel;
            return Math.Abs(WaterTopY(lk.Level) - WaterTopY(level)) <= MaskPondRise;
        }

        /// <summary>Channel rules 4: the largest mask lake (in the mask's 3" pixels) that joins the river's water round it.</summary>
        public const double JoinedLakePixels = 16;

        /// <summary>
        /// 0064: the works of 1318 at a place (the hit of a SEA, LAKE or LAND area; default where there are none). Every
        /// water decision reads it first, as NaturalColumn does: inside a LAKE area the lake of 1318 is the water, at its level,
        /// whatever today's lakes are (Works1318Data.IsLake), and inside a SEA or LAND area none of today's lakes is drawn
        /// (Works1318Data.Undoes). LakeTopAt and MaskPondJoins ask it again rather than trust a column's OutlineLake, which
        /// a lake of 1318 sets as the water mask's lakes do.
        /// </summary>
        WorksHit WorksAt(double lat, double lon)
        {
            if (Works == null || Coasts == null) return default;
            var h = Works.At(lat, lon);
            // works rules 3 (EA-0042): today's water and the slivers the areas' 90 m raster lost, between SEA areas, are
            // the same sea of 1318 (Rotterdam's basins and moles were not drawn as today's river and land inside it)
            if (Rules.SeaOf1318 && h.Covered && !h.Inside && !double.IsNaN(h.Level) && (h.Flags & Works1318Data.Sea) != 0)
            {
                var e = EnclosedBySea(lat, lon, h);
                return new WorksHit(e, e.Inside);
            }
            return new WorksHit(h, false);
        }

        /// <summary>
        /// Works rules 3: a place outside every SEA area, with a SEA area within the coast's reach both ways along one of four
        /// axes (and no LAND area first), lies between parts of the sea of 1318: the hit of that sea (Inside, Sea), its
        /// distance the nearer of the two. Elsewhere the hit as it was.
        /// </summary>
        LakeOutlineLayer.Hit EnclosedBySea(double lat, double lon, LakeOutlineLayer.Hit h, bool remember = true)
        {
            var cache = enclosedCache ??= new EnclosedEntry();
            if (cache.Set && cache.Lat == lat && cache.Lon == lon && cache.Owner == CacheId) return cache.Hit;
            double reach = Coasts.Reach, step = Math.Max(1, reach / 6);
            Geometry.FromGeo(lat, lon, out double x, out double z);
            double SeaAlong(double dx, double dz)
            {
                for (double t = step; t <= reach; t += step)
                {
                    var g = Geometry.ToGeo(x + dx * t, z + dz * t);
                    var o = Works.At(g.Lat, g.Lon);
                    if (o.Covered && o.Inside && (o.Flags & Works1318Data.Land) != 0) return double.NaN;
                    if (o.Covered && o.Inside && (o.Flags & Works1318Data.Sea) != 0) return t;
                }
                return double.NaN;
            }
            var result = h;
            foreach (var (dx, dz) in EnclosedAxes)
            {
                double a = SeaAlong(dx, dz);
                if (double.IsNaN(a)) continue;
                double b = SeaAlong(-dx, -dz);
                if (double.IsNaN(b)) continue;
                result.Inside = true;
                result.Flags = Works1318Data.Sea;
                result.Dist = Math.Min(a, b);
                break;
            }
            // (EA-0095) a coast search's own points (Land1318) do not take the place of the column's answer
            // EnclosedBySea's hits are never lakes the area rules read: no layer is kept alive by this thread's cache
            // (Fable re-review of 5d25355)
            result.Owner = null;
            if (remember) { cache.Lat = lat; cache.Lon = lon; cache.Hit = result; cache.Owner = CacheId; cache.Set = true; }
            return result;
        }

        // the four axes, made once (r = 0.70710678 as before)
        static readonly (double, double)[] EnclosedAxes = { (1.0, 0.0), (0.0, 1.0), (0.70710678, 0.70710678), (0.70710678, -0.70710678) };

        // Owner: the model's CacheId, not the model, so a pool thread doesn't keep a closed world's model alive (ER-22, T-3)
        sealed class EnclosedEntry { public double Lat, Lon; public LakeOutlineLayer.Hit Hit; public long Owner; public bool Set; }
        [ThreadStatic] static EnclosedEntry enclosedCache;

        /// <summary>0064: inside a SEA or LAND area of 1318 the grid's lakes (salt ones too) and their rings are taken out
        /// of the sample, and on LAND its sea as well: the coast decides there alone.</summary>
        static void UndoWater(ref EarthData.TerrainSample s, in WorksHit wk)
        {
            s.Lake = 0;
            s.LakeLevel = double.NaN;
            s.SaltLake = false;
            if (!double.IsNaN(s.ShoreLevel) && s.ShoreLevel != 0) s.ShoreLevel = s.SeaNear > 0 ? 0 : double.NaN;
            s.LakeNear = 0;
            if ((wk.Flags & Works1318Data.Land) != 0) s.Ocean = 0;
        }

        void MarkRiverOutline(ref Column c, double lat, double lon)
        {
            var rv = RiverOutlines.At(lat, lon);
            if (!rv.Covered || double.IsNaN(rv.Level)) return;
            if (rv.Inside) c.RiverOutline = RiverInOutline;
            else if (rv.Dist <= LakeOutlineLayer.HoldReach) c.RiverOutline = RiverBesideOutline;
            else return;
            c.RiverOutlineDist = (float)rv.Dist;
            c.RiverOutlineLevel = (float)rv.Level;
        }

        /// <summary>The open sea far from any coast: the 2 x 2 cells all sea and the bed deeper than 200 m. No detail noise, no
        /// GLO-90 and no coast profile there: the grid's own column.</summary>
        static bool OpenOcean(in EarthData.TerrainSample s) => s.Ocean >= 0.999 && s.Elev < -200;

        Column GridColumn(double x, double z, double lat, double lon, EarthData.TerrainSample s, in WorksHit wk)
        {
            double fine = FineLand(x, z, lat, lon, s);
            double detail = OpenOcean(s) ? 0 : Detail(x, z, s);
            // near a sea or lake the noise may raise the land but not dip it down to the water: on flat plains its dips
            // (±10 m on a 700 m plateau) put ground below a lake's level just outside the lake's data cells, and the
            // lake ended along the cell edges (straight shores)
            double h = s.Elev + detail;
            // Summits (decision 0031): where ETOPO's 30" ground stands above a smooth 2' surface, the excess is added back, in
            // rugged dry land only (the 2' grid averages a peak or a ridge away: Fuji's cone loses ~900 m, Everest ~1,400 m);
            // valleys, shores, lakes and the sea keep the 2' grid's ground, which the rivers and coasts are laid out on.
            // With GLO-90 (0051) dry land takes the data's own peaks instead (below), so neither applies; GLO-90 rules 2
            // lift only a listed peak's own top in the data to the survey (FineSummitLift).
            if (double.IsNaN(fine))
            {
                h += SummitExcess(lat, lon, s);
                if (Summits && Rules.SummitRestore && s.Elev > 0 && s.Ocean <= 0 && s.Lake <= 0)
                    h = KnownSummits.Restore(Geometry, x, z, h, SummitMinElevation, Peaks, SummitRules, PeakBaseOf, peakSupport);
            }
            // Eased lake shores (a lake's ring, not the sea's): the land is held at the lake's level, and kept from the noise's
            // dips, only in step with how near the lake it is (1 next to the lake, where the rules below hold as before,
            // fading to 0 where the 3-cell ring ends). Before, both stopped in full at the ring's edge: a straight cliff
            // along the cells (12 blocks south of Lake Bolsena, 7 on the slopes at the Rhône's mouth into Lake Geneva).
            bool lakeRing = LakeShores && !double.IsNaN(s.ShoreLevel) && s.ShoreLevel != 0 && s.Ocean <= 0;
            double lakeNear = lakeRing ? s.LakeNear : 1;
            double hold = lakeNear >= 1 || double.IsNaN(s.ShoreLevel) ? s.ShoreLevel
                : Math.Max(s.ShoreLevel - 50 * HoldFall, s.ShoreLevel - HoldFall * (1 - lakeNear) / Math.Max(lakeNear, 0.01));
            if (!double.IsNaN(s.ShoreLevel) && (s.Elev > s.ShoreLevel || lakeRing && s.Lake <= 0))
            {
                double above = s.Elev - s.ShoreLevel;
                double wet = Math.Max(s.Ocean, double.IsNaN(s.LakeLevel) ? 0 : s.Lake);
                // Land just above a nearby water level: the noise may dip it into the water only well inside the water's
                // data cells (irregular shores), fading out towards their edge. Outside them nothing knows about the
                // water, and a dip there made a wall or a shore running straight along the cells (south of Kilimanjaro).
                double dips = Geo.SmoothStep(wet / 0.35);
                if (lakeRing) dips = Math.Max(dips, 1 - lakeNear);
                // GenFixRules 2 (EA-0040): the dips' cut fades in from 20 to 40 m above the water instead of stopping at 30 m,
                // where the full dip below and the cut one above met in a crease of a few blocks (5-6 by Lake Geneva's Rhône
                // plain once eased shores let the land down to it)
                if (detail < 0 && Rules.EasedDips && above < 40)
                    h = s.Elev + detail * (dips + (1 - dips) * Geo.SmoothStep((above - 20) / 20));
                else if (detail < 0 && !Rules.EasedDips && above < 30) h = s.Elev + detail * dips;
                // High ground (a fjord's walls) and anything outside the water's cells never dips below the water: that
                // made pockets of sea up on the slopes.
                if (above >= 30 || wet < 0.05) h = Math.Max(h, Math.Min(s.Elev, hold + 0.5));
            }
            if (!double.IsNaN(s.ShoreLevel))
            {
                // Right at the water line, well inside the water's cells: a small, fine noise so flat shores don't run
                // straight along the data grid (the large-scale noise only shifts them as a whole)
                double wet = Math.Max(s.Ocean, double.IsNaN(s.LakeLevel) ? 0 : s.Lake);
                double near = 1 - Geo.SmoothStep(Math.Abs(h - s.ShoreLevel) / 8);
                if (near > 0 && wet > 0) h += 3.5 * SeamNoise(n1, x, z, 14, 2) * near * Geo.SmoothStep(wet / 0.35);
            }
            // under water in the data (the sea, a lake): the detail noise shapes the bottom but doesn't raise it out of
            // the water (in rugged places it made false islands that closed fjords, bays and river mouths)
            if (s.Ocean > 0 && s.Elev < 0) h = Math.Min(h, 0.5 * s.Elev);
            if (s.Lake > 0 && !double.IsNaN(s.LakeLevel) && s.Elev < s.LakeLevel) h = Math.Min(h, s.LakeLevel - 0.5 * (s.LakeLevel - s.Elev));
            var c = new Column { WaterTop = -1, IceBottom = int.MaxValue, Elev = h, Lat = lat, Lon = lon, NearLakeTop = -1 };
            int sea = SeaLevel;
            int top = (int)Math.Floor(Vertical.Y(h));
            double o = s.Ocean;
            double l = double.IsNaN(s.LakeLevel) ? 0 : s.Lake;
            int lakeTop = l > 0 ? WaterTopY(s.LakeLevel) : int.MinValue;
            if (l > 0) c.NearLakeTop = lakeTop;
            if (lakeRing)
            {
                c.LakeRingTop = WaterTopY(s.ShoreLevel);
                c.LakeHoldTop = WaterTopY(hold) + 1;
            }

            // A lake that meets the sea within one data cell (coastal lakes behind a dune strip narrower than the grid):
            // keep a strip of land between them instead of letting fresh and salt water (maybe at different heights) touch.
            bool barrier = false;
            if (o > 0 && l > 0)
            {
                if (Math.Abs(o - l) < 0.3) barrier = true;
                else if (o > l) l = 0;
                else o = 0;
            }
            // the big land's coast from its outline (the grid's lakes and the strips between them and the sea stay as they are)
            if (Coasts != null && !barrier && !(l > 0 && h < s.LakeLevel) && CoastColumn(x, z, s, detail, h, hold, lakeTop, fine, wk, ref c)) return c;

            if (!barrier && o > 0 && h < 0)
            {
                // deep enough for ships away from the shore (Neva Bay, the Gulf of Riga: a few metres are 1-2 blocks)
                int minDepth = 1 + (int)Math.Round((MinSeaDepth - 1) * Geo.SmoothStep((o - 0.5) / 0.5));
                c.Top = Math.Min(top, sea - 1 - minDepth);
                c.WaterTop = sea - 1;
                c.Water = WaterSea;
                return c;
            }
            // GenFixRules 3 (EA-0072): where the grid's lake share, not the ground, would end the water, a wobbled edge
            var edge = l > 0 && h < s.LakeLevel + BeachHeight ? GridLakeEdgeAt(x, z, l, h, s) : GridLakeEdge.AsBefore;
            if (!barrier && l > 0 && h < s.LakeLevel && edge.Water)
            {
                // likewise lakes: high up a block is 20 m or more, so Lake Victoria was only 1-2 blocks deep
                int minDepth = 1 + (int)Math.Round(3 * Geo.SmoothStep((l - 0.5) / 0.5));
                c.Top = Math.Min(top, lakeTop - minDepth);
                c.WaterTop = lakeTop;
                c.Water = s.SaltLake ? WaterSaltLake : WaterLake;
                return c;
            }

            // dry land: GLO-90's height where this world has it (0051); the beach and ice rules below then read that
            if (!double.IsNaN(fine))
            {
                c.Glo90Land = true;
                c.GridElev = h;
                h = fine;
                c.Elev = h;
            }
            // (dry land below 0 m continues from the land at 0 m with GenFixRules, CR-T2; else Vertical.Y as everywhere)
            top = DryLandTop(h);
            // dry land: never below a nearby water surface; at the water's edge flush with it (beach). With eased lake shores
            // the level a lake holds the land at sinks away from the lake (hold, above)
            if (!double.IsNaN(s.ShoreLevel)) top = Math.Max(top, WaterTopY(hold));
            if (barrier)
            {
                top = Math.Max(Math.Max(top, sea - 1), lakeTop);
                if (h < Math.Max(0, s.LakeLevel) + BeachHeight)
                {
                    // sand strip flush with the water, a dune one block higher along its middle
                    top = Math.Max(sea - 1, lakeTop) + (Math.Abs(o - l) < 0.12 ? 1 : 0);
                    c.Beach = true;
                }
            }
            // the beach height fades out with the distance from the water (the sea/lake fraction), so its inland edge
            // follows the ground instead of the data grid's cell edges (rectangles on flat deltas)
            else if (o > 0 && h < BeachHeight * Geo.SmoothStep(o / 0.35))
            {
                top = sea - 1;
                c.Beach = true;
            }
            else if (l > 0 && h < s.LakeLevel + BeachHeight * Geo.SmoothStep(l / 0.35) && edge.Beach)
            {
                top = lakeTop;
                c.Beach = true;
            }
            c.Top = top;
            if (s.Ice > 15)
            {
                double bed = h - s.Ice;
                int yBed = (int)Math.Floor(Vertical.Y(bed));
                if (yBed < top) c.IceBottom = Math.Max(1, yBed + 1);
                c.Beach = false;
            }
            return c;
        }

        /// <summary>GenFixRules 3: whether a grid lake's water, and its flush beach, reach a column below or near its level.</summary>
        readonly struct GridLakeEdge
        {
            public readonly bool Water, Beach;
            public GridLakeEdge(bool water, bool beach) { Water = water; Beach = beach; }
            public static readonly GridLakeEdge AsBefore = new GridLakeEdge(true, true);
        }

        /// <summary>
        /// GenFixRules 3 (EA-0072): where a grid lake's water and beach end, by the lake's share here (l, bilinear between
        /// the 2' cells' centres). Before rules 3 any share at all was water below the lake's level, and beach just above it.
        /// Where the ground beside a lake lies below its level in the grid (the bicubic pulls the lake's bed into the land
        /// cells next to it: 40 m under Lake Geneva's level at the Rhône's mouth), nothing but the share ended them: the
        /// water and its gravel flat ran out to where the share reaches 0, straight lines between cell centres, with land
        /// held flat at the lake's level beyond (a rectangular corner on the built-in heights). From rules 3, where the
        /// ground (this column's, with its detail, plus the smooth grid's rise along the share's fall) would still lie
        /// below the level where the share runs out, the water needs the share, wobbled by a noise a cell long, above GridLakeEdgeShare, and the beach the share
        /// times a shorter wobble of its own (half a cell) above GridLakeBeachShare (a contour that never reaches the share's own
        /// edge); the ground beyond is
        /// flush grass at the lake's level as before. Where the ground rises out of the water first (Lake Garda's cliffs),
        /// the ground ends them, as before.
        /// </summary>
        GridLakeEdge GridLakeEdgeAt(double x, double z, double l, double h, in EarthData.TerrainSample s)
        {
            if (!Rules.GridLakeEdge || double.IsNaN(s.LakeLevel) || l >= GridLakeEdgeShare + GridLakeEdgeWobble) return GridLakeEdge.AsBefore;
            // the share's and the smooth ground's gradients from a block either way
            EarthData.TerrainSample At(double ax, double az) { var g = TerrainGeoAt(ax, az); return Data.SampleTerrain(g.Lat, g.Lon); }
            var e = At(x + 1, z); var w = At(x - 1, z); var n = At(x, z - 1); var so = At(x, z + 1);
            double lx = (e.Lake - w.Lake) / 2, lz = (so.Lake - n.Lake) / 2, fall = Math.Sqrt(lx * lx + lz * lz);
            if (fall > 1e-6)
            {
                double ex = (e.Elev - w.Elev) / 2, ez = (so.Elev - n.Elev) / 2;
                // blocks to where the share reaches 0, going down its gradient, and the ground there
                double run = l / fall, rise = -(ex * lx + ez * lz) / fall * run;
                if (h + rise >= s.LakeLevel) return GridLakeEdge.AsBefore;
            }
            double cell = Geometry.BlocksPerDegreeZ / Data.TerrainRes;
            double wobble = SeamNoise(n3, x, z, Math.Max(16, cell), 2);
            return new GridLakeEdge(l + GridLakeEdgeWobble * wobble > GridLakeEdgeShare,
                l * (1 + GridLakeBeachWobble * SeamNoise(n2, x + 7919, z - 7919, Math.Max(8, cell / 2), 3)) > GridLakeBeachShare);
        }

        /// <summary>GenFixRules 3: the lake share a grid lake's water needs at its outer edge where the share ends it, and the
        /// wobble of that share; the share its beach needs, and the beach's wobble (a factor on the share).</summary>
        public const double GridLakeEdgeShare = 0.2, GridLakeEdgeWobble = 0.25, GridLakeBeachShare = 0.15, GridLakeBeachWobble = 0.9;

        /// <summary>Whether the lake outlines decide at a place (LakeOutlines): this thread reads GLO-90 and the square is
        /// GLO-90's in this world, so the levels are the heights' own.</summary>
        bool OutlinesDecide(double lat, double lon) => LakeOutlines != null && Glo90Decides(lat, lon);

        /// <summary>Whether the river outlines decide at a place (RiverOutlines), as OutlinesDecide.</summary>
        bool RiverOutlinesDecide(double lat, double lon) => RiverOutlines != null && Glo90Decides(lat, lon);

        /// <summary>
        /// 0063 channel rules 1: whether a river course this world draws runs by a place (within its water's half width and
        /// one and a half mask pixels, about 140 m, of its centre line). The river outlines' narrow pieces that lie mostly
        /// so are left out (RiverOutlineData.Layer): the course draws that river. Of the others, those that join the wide
        /// water in two places stay: side arms and the channels between wide waters that no course runs in (the Daugava's
        /// at Riga).
        /// </summary>
        public bool CourseDrawsAt(double lat, double lon)
        {
            if (Rivers == null) return false;
            Geometry.FromGeo(lat, lon, out double x, out double z);
            if (!Rivers.Nearest(x, z, out var hit)) return false;
            return hit.Dist <= hit.HalfWidth + 1.5 * FineElevation.CellMetres * Geometry.BlocksPerDegreeZ / Geo.MetresPerDegree;
        }

        bool Glo90Decides(double lat, double lon)
        {
            if (Fine == null || FineElevation.BuiltInOnly) return false;
            int latS = Math.Clamp((int)Math.Ceiling(lat) - 1, -90, 89), lonW = Math.Clamp((int)Math.Floor(Geo.Wrap180(lon)), -180, 179);
            return Fine.SourceHas(latS, lonW) && !Fine.IsFallback(latS, lonW);
        }

        /// <summary>
        /// Where the lake outlines decide: the grid's lakes taken out of the sample, apart from Earth's salt lakes and the
        /// terminal lakes it fills (the file leaves those to the grid), and their rings with them (the sea's ring stays).
        /// Returns the grid's own depth (m) of a lake taken out here, at half as the grid's lakes have it, 0 for none.
        /// </summary>
        double StripGridLakes(ref EarthData.TerrainSample s, double lat, double lon, bool all = false)
        {
            bool lake = s.Lake > 0 && !double.IsNaN(s.LakeLevel);
            bool ring = !double.IsNaN(s.ShoreLevel) && s.ShoreLevel != 0;
            if (!lake && !ring) return 0;
            // GenFixRules 5 (T-L14): any kept level near the place, not only the highest (two kept lakes of different levels
            // near one place both keep theirs); before, the highest alone
            bool keptLake, keptRing;
            if (all) keptLake = keptRing = false;
            else if (Rules.KeptLakeLevels)
            {
                var levels = Data.KeptLakeLevelsNear(lat, lon);
                keptLake = Array.IndexOf(levels, (float)s.LakeLevel) >= 0 && (double)(float)s.LakeLevel == s.LakeLevel;
                keptRing = Array.IndexOf(levels, (float)s.ShoreLevel) >= 0 && (double)(float)s.ShoreLevel == s.ShoreLevel;
            }
            else
            {
                double kept = Data.KeptLakeLevelNear(lat, lon);
                keptLake = s.LakeLevel == kept;
                keptRing = s.ShoreLevel == kept;
            }
            double depth = 0;
            if (lake && (all || !(s.SaltLake || keptLake)))
            {
                if (s.Elev < s.LakeLevel) depth = 0.5 * (s.LakeLevel - s.Elev) * Geo.SmoothStep(s.Lake / 0.5);
                s.Lake = 0;
                s.LakeLevel = double.NaN;
                s.SaltLake = false;
            }
            if (ring && !keptRing)
            {
                s.ShoreLevel = s.SeaNear > 0 ? 0 : double.NaN;
                s.LakeNear = 0;
            }
            return depth;
        }

        /// <summary>A column inside an outline lake: water at its level, a bed falling from the shore (LakeOutlines.Reach out)
        /// to OutlineLakeDeep or the grid's own bed, and at least 1 block of water at the shore, 3 a few blocks out.</summary>
        Column OutlineLakeColumn(double x, double z, double lat, double lon, in LakeOutlineLayer.Hit lk, double gridDepth)
        {
            int lakeTop = WaterTopY(lk.Level);
            double distM = lk.Dist * MetresPerBlock;
            double depth;
            // lake rules 1 (decision 0100, EA-0184): the lake's real depth (HydroLAKES), steep from the shore, the 2' grid's
            // bathymetry where ETOPO holds the lake's bed (the whole of it: gridDepth is half the grid's)
            bool real = Rules.RealLakeDepths && LakeDepths != null && lk.Owner == LakeOutlines;
            if (real) depth = LakeDepthAt(x, z, in lk, 2 * gridDepth);
            else
            {
                depth = Math.Min(OutlineLakeSlope * distM, OutlineLakeDeep);
                if (gridDepth > depth)
                {
                    // steeper from the shore, without limit at Reach, so the grid's deep basins (Baikal, Tanganyika) keep their depth
                    double room = Math.Max(1e-3, 1 - lk.Dist / LakeOutlines.Reach);
                    depth = Math.Max(depth, Math.Min(gridDepth, OutlineGridSlope * distM / room));
                }
            }
            // (lake rules 1: at most 3 m of it, so a deep lake's floor lies flat, as lake floors do; before, a quarter of the depth)
            double wobble = SeamNoise(n2, x, z, 40, 2);
            depth = real ? depth + Math.Min(0.25 * depth, 3) * wobble : depth * (1 + 0.25 * wobble);
            double bed = lk.Level - depth;
            // (lake rules 1: a shallow lake keeps its one block; before, every lake was 3 blocks deep from 4.5 blocks out)
            int minDepth = real ? 1 : 1 + (int)Math.Round(2 * Geo.SmoothStep((lk.Dist - 0.5) / 4));
            int top = Math.Min((int)Math.Floor(Vertical.Y(bed)), lakeTop - minDepth);
            // (lake rules 1: never below the deepest sea floor's block, well above the world's rock floor)
            if (real) top = Math.Min(Math.Max(top, LakeFloorY), lakeTop - minDepth);
            return new Column
            {
                Top = top,
                WaterTop = lakeTop,
                Water = (lk.Flags & LakeOutlineData.FlagSalt) != 0 ? WaterSaltLake : WaterLake,
                IceBottom = int.MaxValue, Elev = bed, Lat = lat, Lon = lon, NearLakeTop = lakeTop, OutlineLake = true,
            };
        }

        /// <summary>
        /// A column outside the outline lakes, within LakeOutlines.Reach of one: rivers join the nearest at its level
        /// (NearLakeTop), and its ring keeps river valleys from cutting under it (LakeRingTop, LakeHoldTop). Dry land beside a
        /// lake is held at its water top (the highest lake within HoldReach), sinking a block per block away; low land within
        /// a block of the water is flush with it (beach; not round ponds under OutlineBeachArea). Sea or a lower lake right
        /// beside a higher lake gives way to a strip of land at its top, so no water stands against a lower one.
        /// </summary>
        void OutlineShore(ref Column c, in LakeOutlineLayer.Hit lk)
        {
            if (double.IsNaN(lk.Level) || c.Gutter) return;
            int lakeTop = WaterTopY(lk.Level);
            int holdTop = double.IsNaN(lk.HoldLevel) ? int.MinValue : WaterTopY(lk.HoldLevel);
            if (c.Water != WaterNone)
            {
                if (holdTop > c.WaterTop)
                {
                    c.Water = WaterNone;
                    c.WaterTop = -1;
                    c.Top = c.LakeRim = c.LakeRingTop = c.LakeHoldTop = holdTop;
                    c.Beach = true;
                    c.IceBottom = int.MaxValue;
                    c.NearLakeTop = holdTop;
                }
                return;
            }
            if (c.NearLakeTop < 0) c.NearLakeTop = lakeTop;
            int hold = Math.Max(holdTop, lakeTop - (int)Math.Floor(Math.Max(0, lk.Dist - LakeOutlineLayer.HoldReach)));
            if (c.LakeRingTop <= lakeTop)
            {
                c.LakeRingTop = lakeTop;
                c.LakeHoldTop = hold;
            }
            if (holdTop > 0) c.LakeRim = holdTop;
            if (c.IceBottom <= c.Top) return;
            if (c.Top < hold) c.Top = hold;
            if (lk.Dist <= 1 && LakeArea(lk) >= OutlineBeachArea && c.Elev < lk.Level + BeachHeight && c.Top <= lakeTop + 1 && lakeTop >= holdTop)
            {
                c.Top = lakeTop;
                c.Beach = true;
            }
        }
    }
}
