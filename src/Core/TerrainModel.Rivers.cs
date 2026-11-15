using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: river carving: channel rules 0 and 1, the water mask's rivers, mask ponds, the arid rules and lake levels.
    public sealed partial class TerrainModel
    {
        /// <summary>Top water block of a river at a level (m): like a lake, but following only RiverRelief of the land's relief.</summary>
        public int RiverWaterTop(double level)
        {
            if (level <= 0) return WaterTopY(level);
            return SeaLevel - 1 + (int)Math.Floor(RiverRelief * (Vertical.Y(level) - SeaLevel) + 1e-6);
        }

        /// <summary>
        /// Carve a river into a land column: a channel with a flat bed across its middle shelving to 2 blocks at the edges
        /// (the same depth all along the river), a bank strip flush with the water (it also holds the water in where the
        /// land beside it is lower), then a valley easing into the natural terrain. The water surface is a staircase of
        /// pools laid out by the river network; just below each step a ramp of flowing water lies on the lower pool, and
        /// the banks there are one block higher to hold it.
        /// </summary>
        void ApplyRiver(double x, double z, ref Column c)
        {
            // an island in the sea on the river's line (HydroRIVERS runs its centre line down estuaries): the island stays
            // whole and the water goes around it. Before island rules v3 the channel cut through it, with straight edges.
            if (c.IslandOverSea && Islands.Rules >= 3) return;
            if (Rules.Dredged)
            {
                ApplyChannel(x, z, ref c);
                return;
            }
            // 0053 item 18: dry land in the water mask's rivers is river water, whichever course is near
            if (c.RiverOutline == RiverInOutline && c.Water == WaterNone)
            {
                ApplyOutlineRiver(x, z, ref c);
                return;
            }
            if (!Rivers.Nearest(x, z, out var hit))
            {
                if (c.RiverOutline == RiverBesideOutline && c.Water == WaterNone) ApplyOutlineRiver(x, z, ref c);
                return;
            }
            double hw = Rivers.HalfWidth(hit.Q), bank = Rivers.Bank(hit.Q), valley = Rivers.Valley(hit.Q);
            // pool layout 4: a lake well above a river beside it (the lake's data reaching past a waterfall, over the gorge
            // below it) gives way to the river's channel and banks, and a rim LakeRim wide at the lake's level holds the lake
            // in. Before, the lake's whole edge along the lower river was one curtain, much wider than the river (Niagara in a
            // 1M world: 131 columns of curtain beside a river 55 wide).
            if (Rivers.PoolLayout >= 4 && (c.Water == WaterLake || c.Water == WaterSaltLake) && !c.River && !hit.Mouth
                && c.WaterTop > hit.WaterTop + 3 && hit.Dist <= hw + bank + RiverNetwork.LakeRim)
            {
                c.LakeRingTop = c.WaterTop;
                c.LakeHoldTop = c.Top = c.WaterTop + 1;
                c.NearLakeTop = c.WaterTop;
                c.Water = WaterNone;
                c.WaterTop = -1;
                c.IceBottom = int.MaxValue;
                c.Beach = false;
            }
            if (c.Water != WaterNone)
            {
                // a lake or the sea at the river's level (an estuary, a lake it flows through): keep the channel's depth
                // through the shallows
                if (hit.Dist <= hw && hit.WaterTop == c.WaterTop)
                {
                    double tt = hit.Dist / hw;
                    double dd = Rivers.Depth(hit.Q), de = Math.Min(2, dd);
                    // pool layout 3: a chute's or lip's shoal and a plunge pool reach through the lake too (the shoal brings
                    // even a deep lake bed up to it)
                    if (hit.Shoal > 0 && dd > 2) { dd += (2 - dd) * hit.Shoal; de = Math.Min(de, dd); }
                    dd += hit.PlungeDepth;
                    int bedTop = c.WaterTop - (int)Math.Round(de + (dd - de) * Geo.SmoothStep((1 - tt) / 0.4));
                    if (bedTop < c.Top) c.Top = bedTop;
                    else if (hit.Shoal > 0) c.Top = (int)Math.Round(c.Top + (bedTop - c.Top) * hit.Shoal);
                }
                return;
            }
            // no rivers on ice sheets (HydroRIVERS draws flow lines across Greenland's ice; they begin at its edge)
            if (c.IceBottom <= c.Top) return;
            // the prolongation of a river mouth only cuts through low ground (beaches, bars, flats), not headlands
            if (hit.Mouth && (c.Elev > 8 || hit.Dist > hw + bank)) return;
            // 0053 item 18: where the course's centre line lies in the water mask's rivers, the mask's water is the river: no
            // channel, bank or valley of the course's own there (one width along straight pieces), GLO-90's land as it is.
            // Just outside them (the course cutting a corner, or going on where the mask's river ends) the channel's half
            // width is at most OutlineTaper more than the centre line is far from them: it grows out of their shore, overlapping
            // their water, instead of ending square across it.
            if (RiverOutlines != null)
            {
                double gap = RiverOutlineGap(in hit);
                if (gap < 0)
                {
                    if (c.RiverOutline == RiverBesideOutline) ApplyOutlineRiver(x, z, ref c);
                    return;
                }
                hw = Math.Min(hw, gap + OutlineTaper);
            }
            c.NearRiver = true;
            if (Rules.AridRivers && c.Water == WaterNone)
            {
                var climate=Data.SampleClimate(c.Lat,c.Lon);
                if(AridHydrology.Dry(climate.Rain,climate.TWarm-.0065*c.Elev,hit.Q, WetSourceFlow != null && hit.Reach < WetSourceFlow.Length ? WetSourceFlow[hit.Reach] : hit.Q))
                {
                    // A shallow dry channel, easing back into the natural valley on either bank.
                    double weight=1-Geo.SmoothStep((hit.Dist-hw)/Math.Max(1,bank));
                    c.Top=(int)Math.Round(c.Top+(Math.Min(c.Top,hit.WaterTop-2)-c.Top)*weight);
                    if (c.LakeRim > c.Top) c.Top = c.LakeRim;
                    c.DryRiver=hit.Dist<=hw+bank; c.NearRiver=false; c.Beach=false;
                    return;
                }
            }
            int wt = hit.WaterTop, ramp = hit.Ramp;
            // next to a lake the river is at the lake's level (their levels come from different data and round differently).
            // Pool layout 9: decided at the river's centre line, so the whole width of the river changes level at one place
            // across it, and only towards the levels the reach itself has at its ends (up to the lake only where the reach
            // starts at or above it, down only where it ends at or below it): never a rise downstream. Before, each column
            // asked its own lake data, and where that ran out along the river (the Ticino leaving Lake Maggiore) the river
            // stood a block higher on one side than on the other for 40 blocks, with its step running along the river.
            int nearLake = c.NearLakeTop;
            if (Rivers.PoolLayout >= 9)
            {
                nearLake = LakeTopAt(hit.CentreX, hit.CentreZ);
                if (nearLake > wt && hit.StartTop < nearLake || nearLake < wt && hit.EndTop > nearLake) nearLake = -1;
            }
            if (nearLake >= 0 && Math.Abs(nearLake - wt) <= 3 && nearLake != wt)
            {
                wt = nearLake;
                ramp = 0;
            }
            if (wt >= Vertical.MapHeight - 4 || wt < 2) return;
            int bankTop = wt + (ramp > 0 ? 1 : 0);
            if (hit.Dist <= hw && hw > 0)
            {
                double t = hit.Dist / hw;
                c.Top = wt - ChannelDepth(in hit, t);
                // rock breaking the surface in about one column in 8 of a chute (a block above it, so no water beside it stands
                // against air: the water just upstream may be a block higher)
                if (hit.Rocks && (TerrainColumns.Hash((int)Math.Floor(x), (int)Math.Floor(z)) & 7) == 0) c.Top = wt + 1;
                c.WaterTop = wt;
                c.Water = WaterLake;
                c.River = true;
                c.FlowX = (float)hit.DirX;
                c.FlowZ = (float)hit.DirZ;
                c.Ramp = (byte)ramp;
                c.Rapid = hit.Rapid;
                c.RapidsReach = Rivers.PoolLayout >= 8 && Rivers.ReachRapids(hit.Reach);
                c.IceBottom = int.MaxValue;
                c.Beach = false;
                return;
            }
            int natural = c.Top;
            if (hit.Dist <= hw + bank)
            {
                c.Top = Math.Max(LakeHeld(c, wt, natural, bankTop), c.LakeRim);
                c.IceBottom = int.MaxValue;
                c.Beach = hit.Dist <= hw + 2 && c.Top == bankTop;
                return;
            }
            double f = Geo.SmoothStep((hit.Dist - hw - bank) / valley);
            int floorTop = bankTop + 1;
            if (c.Top > floorTop) c.Top = (int)Math.Round(floorTop + (c.Top - floorTop) * f);
            else if (c.Top < wt && f < 0.25) c.Top = wt;
            c.Top = Math.Max(LakeHeld(c, wt, natural, c.Top), c.LakeRim);
            if (c.IceBottom <= c.Top && f < 1) c.IceBottom = int.MaxValue;
        }

        /// <summary>
        /// 0063 rule 4, channel rules 1 ("dredging"): ApplyRiver with the water mask's rivers joined to the course. The course's
        /// channel, banks and valley are drawn everywhere along it at its hit's width and depth (the flow's, or the measured
        /// width where narrower, deepened for it: RiverNetwork.ChannelRules), never cut or narrowed by the outlines; so land strips the
        /// outlines held between their pieces along a course are the channel's. Dry land in the mask's rivers is river water
        /// at the course's level, the channel deepening its bed (ApplyOutlineRiver); land beside them outside the channel is
        /// held at the water's top. A mask lake in the channel or its banks within MaskPondRise blocks of the river's level
        /// becomes river (MaskPondJoins), and the mask's river water is never dried by the arid rules (DriesOut).
        /// </summary>
        void ApplyChannel(double x, double z, ref Column c)
        {
            bool found = Rivers.Nearest(x, z, out var hit);
            if (c.RiverOutline == RiverInOutline && c.Water == WaterNone)
            {
                ApplyOutlineRiver(x, z, ref c, found, hit);
                return;
            }
            if (!found)
            {
                if (c.RiverOutline == RiverBesideOutline && c.Water == WaterNone) ApplyOutlineRiver(x, z, ref c, false, hit);
                return;
            }
            double hw = hit.HalfWidth, bank = hit.BankWidth, valley = hit.ValleyWidth;
            // pool layout 4: a lake well above the river gives way to its channel and banks, with a rim (ApplyRiver)
            if (Rivers.PoolLayout >= 4 && (c.Water == WaterLake || c.Water == WaterSaltLake) && !c.River && !hit.Mouth
                && c.WaterTop > hit.WaterTop + 3 && hit.Dist <= hw + bank + RiverNetwork.LakeRim)
            {
                c.LakeRingTop = c.WaterTop;
                c.LakeHoldTop = c.Top = c.WaterTop + 1;
                c.NearLakeTop = c.WaterTop;
                c.Water = WaterNone;
                c.WaterTop = -1;
                c.IceBottom = int.MaxValue;
                c.Beach = false;
                c.OutlineLake = false;
            }
            if (c.Water != WaterNone)
            {
                if (c.OutlineLake && !hit.Mouth && hit.Dist <= hw + bank && MaskPondJoins(x, z, in hit, ref c)) return;
                // channel rules 3: the sea's bed where a river meets it is ApplyMouth's (no groove of the channel's own)
                if (c.Water == WaterSea && Rules.MouthBed) return;
                // a lake or the sea at the river's level: the channel's depth through the shallows (ApplyRiver)
                if (hit.Dist <= hw && hit.WaterTop == c.WaterTop)
                {
                    double tt = hit.Dist / hw;
                    double dd = hit.Depth, de = Math.Min(2, dd);
                    if (hit.Shoal > 0 && dd > 2) { dd += (2 - dd) * hit.Shoal; de = Math.Min(de, dd); }
                    dd += hit.PlungeDepth;
                    // (channel rules 4: the channel's own profile, as on the land beside: ChannelDepthExact)
                    int bedTop = c.WaterTop - (int)Math.Round(Rules.OneBed ? ChannelDepthExact(in hit, tt)
                        : de + (dd - de) * Geo.SmoothStep((1 - tt) / 0.4));
                    if (bedTop < c.Top) c.Top = bedTop;
                    else if (hit.Shoal > 0) c.Top = (int)Math.Round(c.Top + (bedTop - c.Top) * hit.Shoal);
                }
                return;
            }
            if (c.IceBottom <= c.Top) return;
            if (hit.Mouth && (c.Elev > 8 || hit.Dist > hw + bank)) return;
            c.NearRiver = true;
            if (Rules.AridRivers && DriesOut(in hit, in c))
            {
                // a shallow dry channel, easing back into the natural valley on either bank (ApplyRiver)
                double weight = 1 - Geo.SmoothStep((hit.Dist - hw) / Math.Max(1, bank));
                c.Top = (int)Math.Round(c.Top + (Math.Min(c.Top, hit.WaterTop - 2) - c.Top) * weight);
                if (c.LakeRim > c.Top) c.Top = c.LakeRim;
                c.DryRiver = hit.Dist <= hw + bank; c.NearRiver = false; c.Beach = false;
                // beside the mask's river water, which stays wet: held at least at its top (as ApplyOutlineRiver holds it)
                if (c.RiverOutline == RiverBesideOutline)
                {
                    RiverLevel(in hit, c.NearLakeTop, out int wtBeside, out _);
                    if (c.Top < wtBeside)
                    {
                        c.Top = wtBeside;
                        c.DryRiver = false;
                        c.Beach = true;
                    }
                }
                return;
            }
            RiverLevel(in hit, c.NearLakeTop, out int wt, out int ramp);
            if (wt >= Vertical.MapHeight - 4 || wt < 2) return;
            int bankTop = wt + (ramp > 0 ? 1 : 0);
            if (hit.Dist <= hw && hw > 0)
            {
                c.Top = MouthShelf(wt, wt - ChannelDepthAt(x, z, in hit, hit.Dist / hw), in c);
                if (hit.Rocks && (TerrainColumns.Hash((int)Math.Floor(x), (int)Math.Floor(z)) & 7) == 0) c.Top = wt + 1;
                c.WaterTop = wt;
                c.Water = WaterLake;
                c.River = true;
                SetFlow(x, z, in hit, ref c);
                c.Ramp = (byte)ramp;
                c.Rapid = hit.Rapid;
                c.RapidsReach = Rivers.PoolLayout >= 8 && Rivers.ReachRapids(hit.Reach);
                c.IceBottom = int.MaxValue;
                c.Beach = false;
                return;
            }
            int natural = c.Top;
            if (hit.Dist <= hw + bank)
            {
                c.Top = Math.Max(LakeHeld(c, wt, natural, bankTop), c.LakeRim);
                c.IceBottom = int.MaxValue;
                c.Beach = hit.Dist <= hw + 2 && c.Top == bankTop;
            }
            else
            {
                double f = Geo.SmoothStep((hit.Dist - hw - bank) / valley);
                int floorTop = bankTop + 1;
                if (c.Top > floorTop) c.Top = (int)Math.Round(floorTop + (c.Top - floorTop) * f);
                else if (c.Top < wt && f < 0.25) c.Top = wt;
                c.Top = Math.Max(LakeHeld(c, wt, natural, c.Top), c.LakeRim);
                if (c.IceBottom <= c.Top && f < 1) c.IceBottom = int.MaxValue;
            }
            // land beside the mask's rivers outside the channel: held at least at the water's top (as ApplyOutlineRiver holds it)
            if (c.RiverOutline == RiverBesideOutline && c.Top < bankTop)
            {
                c.Top = bankTop;
                c.Beach = true;
            }
        }

        /// <summary>
        /// Channel rules 3 (decision 0071): a sea column where a river at the sea's level meets the sea. One bed with the river's,
        /// never shallower than the sea's own (the coast profile's or the grid's), deeper only where the river is:
        /// <list type="bullet">
        /// <item>In the water mask's river water (GSHHG's sea up an estuary that the mask has as river, the Daugava below Riga):
        /// the depth that water has as river (MaskWaterDepth), reached from the sea's own depth at OutlineRiverSlope in from the
        /// mask's edge. At GSHHG's line across the river the sea's own depth is a block, as the river's shelf is there; at the
        /// mask's edge towards the open sea it is the sea's: no step on either.</item>
        /// <item>Along the courses and their mouths' prolongations near (up to twice the coast profile's reach and 60 blocks
        /// away), where a course's own line is at sea, the deepest of their troughs (Trough; the nearest course alone made a
        /// step between a delta's branches): the course's channel depth over its water, widened by the
        /// distance from the coast outline's shore (at most half the course's reach), shelving to the sea's own depth at
        /// OutlineRiverSlope beyond; along the prolongation and with the line's own distance out to sea its depth eases to
        /// the sea's over at least TroughEase blocks per block it loses (HydroRIVERS runs the Rhine's and the Loire's last
        /// reaches 5-10 km out over water). It shelves to the shore too, unless the course comes out of the land just
        /// upstream (GSHHG's line across the river).</item>
        /// </list>
        /// Rounded through DepthNoise, so the steps of a block wander.
        /// </summary>
        void ApplyMouth(double x, double z, ref Column c, ref ColumnWork work)
        {
            int wt = c.WaterTop;
            if (wt != SeaLevel - 1 || c.IceBottom <= c.Top) return;
            double own = wt - c.Top;
            LakeOutlineLayer.Hit rv = default;
            bool mask = RiverOutlinesDecide(c.Lat, c.Lon) && !SeaChannels.InCanalZone(c.Lat, c.Lon);
            if (mask)
            {
                rv = RiverOutlines.At(c.Lat, c.Lon);
                mask = rv.Covered && rv.Inside;
            }
            double fade = CoastLayer.ReachFor(Geometry), within = 2 * fade + 60;
            // ER-11 (code review 14 November 2026): out of the mask's water and with no river piece in any bucket the trough
            // search reads, want stays own: the end below as it would be, without the coast distance, the noise and the search
            // (the open sea's columns, every one of which paid for them)
            if (!mask && !Rivers.AnyBucketNear(x, z, within))
            {
                int plain = wt - Math.Max(1, (int)Math.Round(own));
                if (plain < c.Top) c.Top = plain;
                return;
            }
            // the shore by the coast outline (blocks, up to its reach; infinite without outlines or where they have no data)
            double shore = double.PositiveInfinity;
            if (Coasts != null)
            {
                double d = CoastDistanceAt(ref work, c.Lat, c.Lon);
                if (!double.IsNaN(d)) shore = Math.Max(0, -d);
            }
            double noise = DepthNoise(x, z), want = own;
            if (mask)
            {
                bool near = Rivers.Nearest(x, z, out var mh) || Rivers.NearestCourse(x, z, OutlineCourseSearch, out mh);
                double fx = mh.DirX, fz = mh.DirZ;
                double full;
                if (Rules.SolvedFlow && Flow != null)
                {
                    double speed = Flow.At(c.Lat, c.Lon, Rules.FlowAcrossTiles, out double east, out double north) ? Math.Sqrt(east * east + north * north) : CarryFull;
                    full = MaskWaterDepth(x, z, near, in mh, speed, ref fx, ref fz);
                }
                else full = OutlineDepthCap(x, z, near, in mh, ref fx, ref fz);
                want = Math.Max(want, Math.Min(own + ShoreSlope(full, own) * rv.Dist, full) + noise);
            }
            // every course at the sea's level within reach of its trough, the deepest trough (the branches of a delta); one static
            // delegate, its model and arguments this thread's for the call (no closure per column, ER-11; the model is let go
            // after it, so a pool thread keeps no world alive, and a MemberwiseClone of the model asks its own Trough)
            mouthModel = this; mouthOwn = own; mouthShore = shore; mouthWithin = within;
            double trough;
            try { trough = Rivers.MaxNear(x, z, within, MouthTrough, own); }
            finally { mouthModel = null; }
            if (trough > own) want = Math.Max(want, trough + noise * Math.Min(1, trough - own));
            int top = wt - Math.Max(1, (int)Math.Round(want));
            if (top < c.Top) c.Top = top;
        }

        // (Trough may not build a column, GetColumn or anything that reaches ApplyMouth: the finally below would clear
        // mouthModel under an outer MaxNear on the same thread)
        [ThreadStatic] static TerrainModel mouthModel;
        [ThreadStatic] static double mouthOwn, mouthShore, mouthWithin;
        static readonly System.Func<RiverNetwork.Hit, double> MouthTrough = hit => mouthModel.Trough(in hit, mouthOwn, mouthShore, mouthWithin);

        /// <summary>
        /// Channel rules 3: a river at the sea's level near the coast (within the coast outline's reach of it, on the land side)
        /// is no deeper than a block plus OutlineRiverSlope per block from the coast: a mouth bar, which the sea's side of the
        /// coast meets (ApplyMouth). In the water mask's river water the distance from the mask's edge counts too, scaled from
        /// the outlines' reach to the coast's, so a river whose mask water goes on into GSHHG's sea (the Daugava) gets no bar
        /// across it. top: the bed's top as drawn; returns it raised where the shelf is shallower.
        /// </summary>
        int MouthShelf(int wt, int top, in Column c)
        {
            if (!Rules.MouthBed || Coasts == null || wt != SeaLevel - 1) return top;
            double d = CoastDistanceAt(c.Lat, c.Lon, WorksAt(c.Lat, c.Lon));
            if (double.IsNaN(d) || d >= Coasts.Reach) return top;
            double from = Math.Max(0, d);
            if (c.RiverOutline == RiverInOutline && RiverOutlines != null)
                from = Math.Max(from, Math.Min(1, c.RiverOutlineDist / RiverOutlines.Reach) * Coasts.Reach);
            // (the coast's distance is known up to its reach only, so the shelf reaches the river's depth there: steeper than
            // OutlineRiverSlope where the reach is short, a block per block for a 9-block river at 1:100)
            double depth = wt - top, slope = Math.Max(OutlineRiverSlope, (depth - 1) / Coasts.Reach);
            return Math.Max(top, wt - Math.Max(1, (int)Math.Round(1 + slope * from)));
        }

        /// <summary>
        /// ApplyMouth: the depth (blocks) a course's trough gives a sea column (own: the sea's own depth there; shore: its
        /// distance from the coast outline's shore; within: how far courses are looked for), or own where the course gives
        /// none: a course at another level, or running along the coast on land (fading out as its line lies inland).
        /// </summary>
        double Trough(in RiverNetwork.Hit hit, double own, double shore, double within)
        {
            if (hit.WaterTop != SeaLevel - 1) return own;
            double dc = double.NaN;
            double hw = hit.HalfWidth;
            // the depth on its line: the course's channel depth, easing to the sea's own along the mouth's prolongation and with
            // the line's distance out to sea, each over at least TroughEase blocks per block it loses
            double reach = hw + hit.BankWidth + hit.ValleyWidth;
            double drop = Math.Max(0, ChannelDepthExact(in hit, 0) - own), w = 1;
            if (hit.Mouth) w *= 1 - Geo.SmoothStep(hit.Beyond / Math.Max(2 * hw + 40, TroughEase * drop));
            double widen = double.IsInfinity(shore) ? 0 : Math.Min(shore, Math.Min(0.5 * reach, CoastLayer.ReachFor(Geometry)));
            // EA-0095 (FP-08): the coast's distance at the course's centre (a ring search near the sea of 1318) only where the
            // answer can depend on it. With no drop left the line is the sea's own depth; and the line's distance out to sea can
            // only lower w, so a column beyond the widest the trough can reach gets the sea's own depth too (shore is never
            // negative, so the last clamp below keeps own). own either way, as before
            if (drop * w == 0 || hit.Dist >= hw + widen + drop * w / OutlineRiverSlope + 1e-6) return own;
            if (Coasts != null)
            {
                double SeaSideAt(double px, double pz)
                {
                    var g = Geometry.ToGeo(px, pz);
                    return g.InGutter ? double.NaN : CoastDistanceAt(g.Lat, g.Lon, WorksAt(g.Lat, g.Lon));
                }
                dc = SeaSideAt(hit.CentreX, hit.CentreZ);
            }
            if (dc < 0) w *= 1 - Geo.SmoothStep(-dc / Math.Max(Coasts.Reach, TroughEase * drop));
            // (and as it lies inland: a river running along the coast on land deepens no sea beside it)
            else if (dc > 0) w *= 1 - Geo.SmoothStep(dc / Math.Max(hw, TroughEase * drop));
            double line = own + drop * w;
            // across: that depth over its water, widened by the distance from the shore (at most half the course's reach and
            // the coast profile's reach, widen above), then shelving to the sea's at OutlineRiverSlope, reaching it within the search
            double edge = Math.Min(hw + widen + (line - own) / OutlineRiverSlope, within);
            double r = Math.Min(line, own + OutlineRiverSlope * Math.Max(0, edge - hit.Dist));
            // and to the shore, as the river on the land side shelves to it (MouthShelf): both a block or two at the coast
            if (!double.IsInfinity(shore)) r = Math.Min(r, own + OutlineRiverSlope * shore);
            return r;
        }

        /// <summary>The river's water top and ramp at a hit: the course's, or beside a lake within 3 blocks the lake's (pool layout
        /// 9: decided at the centre line, only towards the reach's own end levels; ApplyRiver).</summary>
        void RiverLevel(in RiverNetwork.Hit hit, int nearLakeTop, out int wt, out int ramp)
        {
            wt = hit.WaterTop;
            ramp = hit.Ramp;
            int nearLake = nearLakeTop;
            if (Rivers.PoolLayout >= 9)
            {
                nearLake = LakeTopAt(hit.CentreX, hit.CentreZ);
                if (nearLake > wt && hit.StartTop < nearLake || nearLake < wt && hit.EndTop > nearLake) nearLake = -1;
            }
            if (nearLake >= 0 && Math.Abs(nearLake - wt) <= 3 && nearLake != wt)
            {
                wt = nearLake;
                ramp = 0;
            }
        }

        /// <summary>
        /// Channel rules 1: a column of a mask lake within the river's channel or banks whose water is within MaskPondRise blocks
        /// of the river's becomes the river's: its level, the deeper of the lake's bed and the channel's (Rome: a 2×2-pixel pond
        /// a block above the Tiber in its middle was a ring of silt round a pit; the Tigris's marsh pools beside it, which the
        /// mask calls salt). Only a pond no bigger than a disc of the channel's and banks' half width: a lake the river runs
        /// through or along stays a lake (at the river's level its bed is deepened as before). False where it stays the
        /// lake's (the river is dry there, or too far above or below it).
        /// </summary>
        bool MaskPondJoins(double x, double z, in RiverNetwork.Hit hit, ref Column c)
        {
            if (c.River || c.Water != WaterLake && c.Water != WaterSaltLake) return false;
            RiverLevel(in hit, c.NearLakeTop, out int wt, out int ramp);
            if (Math.Abs(c.WaterTop - wt) > MaskPondRise || wt >= Vertical.MapHeight - 4 || wt < 2) return false;
            double r = hit.HalfWidth + hit.BankWidth;
            // the lake that made this column (OutlineLakeColumn): a lake of 1318 where there is one, as NaturalColumn takes
            // those first (a drained polder lake is land in the water mask, and the whole lake of 1318 was a "pond"); else
            // the water mask's (none without its layer: a lake of 1318 in a world without the mask's lakes)
            var wk = WorksAt(c.Lat, c.Lon);
            LakeOutlineLayer.Hit lk;
            if (Works1318Data.IsLake(wk)) lk = wk.LakeOf1318;
            else
            {
                if (LakeOutlines == null) return false;
                lk = LakeOutlines.At(c.Lat, c.Lon);
            }
            if (lk.Inside && LakeArea(lk) > Math.PI * r * r) return false;
            if (Rules.AridRivers && DriesOut(in hit, in c)) return false;
            int top = Math.Min(c.Top, wt - 1);
            if (hit.Dist <= hit.HalfWidth && hit.HalfWidth > 0) top = Math.Min(top, wt - ChannelDepthAt(x, z, in hit, hit.Dist / hit.HalfWidth));
            c.Top = top;
            c.WaterTop = wt;
            c.Water = WaterLake;
            c.River = true;
            c.OutlineLake = false;
            c.NearRiver = true;
            SetFlow(x, z, in hit, ref c);
            c.Ramp = (byte)ramp;
            c.Rapid = hit.Rapid;
            c.RapidsReach = Rivers.PoolLayout >= 8 && Rivers.ReachRapids(hit.Reach);
            c.IceBottom = int.MaxValue;
            c.Beach = false;
            return true;
        }

        /// <summary>The arid rules (AridHydrology.Dry): whether the river at this hit is dry here. Channel rules 1: never where
        /// the water mask has the river (MaskKeepsWet; Al-Qurna's 244 m³/s arm came out dry for a wet-source flow of
        /// 0.3 m³/s).</summary>
        bool DriesOut(in RiverNetwork.Hit hit, in Column c)
        {
            var climate = Data.SampleClimate(c.Lat, c.Lon);
            if (!AridHydrology.Dry(climate.Rain, climate.TWarm - .0065 * c.Elev, hit.Q, WetSourceFlow != null && hit.Reach < WetSourceFlow.Length ? WetSourceFlow[hit.Reach] : hit.Q))
                return false;
            return !MaskKeepsWet(in hit);
        }

        /// <summary>Channel rules 1: whether the water mask has the river at a hit, so the arid rules leave it wet (its channel,
        /// its green belt, its wet tree sites): the piece has the mask's width, or its reach lies in the mask's river water
        /// (ReachInMaskRiver). Always false before.</summary>
        public bool MaskKeepsWet(in RiverNetwork.Hit hit) => Rules.Dredged && (hit.Masked || ReachInMaskRiver(hit.Reach));

        /// <summary>The facts kept per reach (ReachFacts: in the mask's river water, the solved speed, the pond profile), one
        /// rule for all of them; a clone of the model shares them.</summary>
        readonly ReachFacts reachFacts = new ReachFacts();

        /// <summary>
        /// The points (latitude, longitude) of each reach the arid rules could dry (a piece of it under 10 m³/s, or its
        /// wet-source flow under 10 m³/s: AridHydrology.Dry), kept when WetSourceFlow is set, while the river network still
        /// has its reaches; null for the other reaches, which no rule asks about. The world drops the network's reaches before
        /// the first chunk (RiverNetwork.ReleaseStartupData), and ReachInMaskRiver read them from there: in game it never ran,
        /// only the offline tests had it (audit C F1).
        /// </summary>
        (float[] Lat, float[] Lon)[] dryReachCourses;

        void KeepDryReachCourses()
        {
            var reaches = Rivers?.Reaches;
            var wet = wetSourceFlow;
            if (reaches == null || wet == null) { dryReachCourses = null; return; }
            var kept = new (float[] Lat, float[] Lon)[reaches.Count];
            for (int r = 0; r < reaches.Count; r++)
                if (Rivers.Kept(r) && (r < wet.Length && wet[r] < 10 || reaches[r].Q < 10 || Rivers.Fades(r)))
                    kept[r] = (reaches[r].Lat, reaches[r].Lon);
            dryReachCourses = kept;
        }

        /// <summary>
        /// Channel rules 1: whether a third or more of a reach's points lie in the water mask's river water (not along the
        /// canals, whose water isn't drawn). Decided for the whole reach, so its channel isn't wet and dry by turns where the
        /// course wanders in and out of the mask's water (a dry bed beside wet water is a hollow below it: the Karkheh). The
        /// points come from dryReachCourses, so the rule holds after the start-up data is released; else (a model whose
        /// WetSourceFlow wasn't set) from the network's reaches while it has them.
        /// ER-01 (code review 14 Nov 2026): the answer is kept for the whole model, so it may not depend on the thread or the
        /// session that asked first. A thread on the built-in heights (FineElevation.BuiltInOnly: the story search before the
        /// first chunk, map sweeps) has no square where the outlines decide, so the mask keeps nothing wet for it: it answers
        /// false and keeps nothing. The points count by ReachPointDecides, which reads no per-thread switch and takes a square
        /// that fell back for this session only as the GLO-90 square it is in every other session. Before, a story search
        /// that asked first kept "not in the mask" for good, and generation then dried reaches the mask has wet. On purpose the
        /// column itself does not do the same: on a square fallen back for the session Glo90Decides is false, so the outlines
        /// decide no column there, while the reach's flag counts the square (the same flag in every session). Left: a square that
        /// becomes a for-good fallback during a session (the source lacks its tile) counts for reaches flagged before that and
        /// not after; from the next session on it is for good from the start.
        /// </summary>
        bool ReachInMaskRiver(int reach)
        {
            if (FineElevation.BuiltInOnly) return false;
            var courses = dryReachCourses;
            var reaches = Rivers?.Reaches;
            int count = courses?.Length ?? reaches?.Count ?? 0;
            if (RiverOutlines == null || reach < 0 || reach >= count) return false;
            float[] lats, lons;
            if (courses != null && courses[reach].Lat != null) { lats = courses[reach].Lat; lons = courses[reach].Lon; }
            // the network's reaches only for a model without kept courses: with them, a reach whose course wasn't kept (a hit
            // that walked across a joint into a reach no rule dries) is not in the mask, as in game, where the reaches are
            // released before the first chunk; the offline tests read them and disagreed with the game (CR-R4)
            else if (courses == null && reaches != null && reach < reaches.Count) { lats = reaches[reach].Lat; lons = reaches[reach].Lon; }
            else return false;
            var known = reachFacts.InMask(count);
            byte v = System.Threading.Volatile.Read(ref known[reach]);
            if (v != 0) return v == 2;
            int inside = 0, n = lats.Length;
            for (int k = 0; k < n; k++)
            {
                double lat = lats[k], lon = lons[k];
                if (!ReachPointDecides(lat, lon) || SeaChannels.InCanalZone(lat, lon)) continue;
                var rv = RiverOutlines.At(lat, lon);
                if (rv.Covered && rv.Inside) inside++;
            }
            bool wet = inside > 0 && 3 * inside >= n;
            System.Threading.Volatile.Write(ref known[reach], wet ? (byte)2 : (byte)1);
            return wet;
        }

        /// <summary>ReachInMaskRiver's test whether the outlines decide at a point of a reach: Glo90Decides without what
        /// differs by thread or session (ER-01). A square the source has and the world doesn't keep on built-in heights for
        /// good; a square that fell back for this session only counts, as in every session whose download succeeds.</summary>
        bool ReachPointDecides(double lat, double lon)
        {
            if (Fine == null || RiverOutlines == null) return false;
            int latS = Math.Clamp((int)Math.Ceiling(lat) - 1, -90, 89), lonW = Math.Clamp((int)Math.Floor(Geo.Wrap180(lon)), -180, 179);
            return Fine.SourceHas(latS, lonW) && !Fine.IsFallbackForGood(latS, lonW);
        }

        /// <summary>The channel's depth (blocks below its water top) at t (0 on the course's centre line, 1 at the channel's
        /// edge): a flat bed across its middle shelving to 2 blocks at the edges, with the shallow beds of rapids, a curtain's
        /// lip and the plunge pool below it.</summary>
        int ChannelDepth(in RiverNetwork.Hit hit, double t) => (int)Math.Max(1, Math.Round(ChannelDepthExact(in hit, t)));

        /// <summary>ChannelDepth at (x, z): channel rules 1 round it through DepthNoise where the channel is plain (not in
        /// rapids, at a curtain's lip or on its shoals, whose depths are kept as they are), so that a step of a block where
        /// the depth crosses a half wanders instead of running straight across the river (the Irtysh at 1:10).</summary>
        int ChannelDepthAt(double x, double z, in RiverNetwork.Hit hit, double t)
        {
            double exact = ChannelDepthExact(in hit, t);
            if (Rules.Dredged && !hit.Chute && hit.ChuteShoal <= 0 && hit.Shoal <= 0 && !hit.Lip) exact += DepthNoise(x, z);
            return (int)Math.Max(1, Math.Round(exact));
        }

        /// <summary>ChannelDepth before rounding.</summary>
        double ChannelDepthExact(in RiverNetwork.Hit hit, double t)
        {
            // (the flow's depth, Depth(Q); channel rules 1: deepened where the water mask narrows the channel)
            double depth = hit.Depth, edge = Math.Min(2, depth);
            // pool layout 2 (decision 0024 1b): rapids are a shallow chute (2 blocks, 1 at the edges, too shallow for a
            // caravel); a curtain has a shallow lip above it and a plunge pool below. Pool layout 3: the same, eased in and
            // out (Shoal, PlungeDepth)
            if (Rivers.Sections && (hit.Chute || hit.ChuteShoal > 0))
            {
                // pool layout 7: a white-water section's pools are WhiteWaterDepth deep across the full width (shallower
                // pools flooded when a sill was broken), eased in and out; a curtain's lip still shoals as before
                double ww = RiverNetwork.WhiteWaterDepth, lip = hit.Shoal > hit.ChuteShoal ? hit.Shoal : 0;
                if (lip > 0)
                {
                    if (depth > 2) depth += (2 - depth) * lip;
                    edge = Math.Min(edge, depth);
                }
                double cs = hit.Chute ? 1 : hit.ChuteShoal;
                depth += (ww - depth) * cs;
                edge += (ww - edge) * cs;
            }
            else if (hit.Shoal > 0)
            {
                if (depth > 2) depth += (2 - depth) * hit.Shoal;
                edge = Math.Min(edge, Math.Min(depth, 2 - (hit.Chute ? hit.Shoal : 0)));
            }
            else if (hit.Chute) { depth = 2; edge = 1; }
            else if (hit.Lip) { depth = Math.Min(depth, 2); edge = Math.Min(edge, depth); }
            // (channel rules 4: no deeper than its width lets its sides be at ChannelSide4 at most, as the bed eases over
            // ChannelEase4 of the half width with a smooth step's steepest slope, 1.5 times its mean: a channel 13 blocks wide
            // was cut 15 deep where the mask narrows the Rhine at Pannerden, the flow's depth doubled; plunge pools as before)
            if (Rules.OneBed && hit.HalfWidth > 0)
                depth = Math.Min(depth, Math.Max(edge, edge + ChannelSide4 * ChannelEase4 * hit.HalfWidth / 1.5));
            depth += hit.Plunge + hit.PlungeDepth;
            // (channel rules 4: easing from the bank over ChannelEase4 of the half width, not 0.4: a channel a few blocks wide and
            // several deep had walls of 2-3 blocks a step for sides, the Rhine at Pannerden)
            return edge + (depth - edge) * Geo.SmoothStep((1 - t) / (Rules.OneBed ? ChannelEase4 : 0.4));
        }

        /// <summary>
        /// Channel rules 1: the deepest the water mask's river water at (x, z) may be (blocks, before the shore's slope):
        /// every course near it as RiverNetwork.BlendNear weighs them, OutlineRiverLoneDepth beyond them all, with the
        /// rapids, a curtain's lip and a plunge pool of the course at hand (found: its hit) as its channel has them, fading
        /// as its depth does. Taking the nearest course's depth alone made straight-edged patches at Rīgas HES.
        /// </summary>
        double OutlineDepthCap(double x, double z, bool found, in RiverNetwork.Hit hit, ref double fx, ref double fz)
        {
            double cap = Rivers.BlendNear(x, z, OutlineRiverLoneDepth, ref fx, ref fz);
            if (found)
                cap += (ChannelDepthExact(in hit, 0) - hit.Depth)
                    * RiverNetwork.OutlineFade(hit.Dist, hit.HalfWidth, hit.HalfWidth + hit.BankWidth + hit.ValleyWidth);
            return Math.Max(1, cap);
        }

        /// <summary>
        /// Channel rules 3: the depth (blocks, before the banks' shelf) of the water mask's river water where the flow is solved,
        /// and of the sea the mask has as river (ApplyMouth). Every course near as RiverNetwork.BlendNear weighs them (their channels'
        /// depths, deepened where the mask narrows them, easing out over their reach), over the nearest course's depth for its
        /// flow (RiverNetwork.Depth: one value along a reach) beyond them, with the rapids, a lip or a plunge pool of the course
        /// at hand; and from OutlineRiverLoneDepth in still water to that as the solved speed rises to CarryFull. Before (rules
        /// 2), the nearest course's channel depth alone, and 3 blocks below CarryMin: straight steps of 2-6 blocks where the
        /// nearest piece of a course changed (two pieces of the Daugava 12 and 8.5 blocks wide, 7 and 9 deep) or the speed
        /// crossed 2 cm/s.
        /// </summary>
        double MaskWaterDepth(double x, double z, bool found, in RiverNetwork.Hit hit, double speed, ref double fx, ref double fz)
        {
            // (channel rules 4: the flow's depth blended over every course near, RiverNetwork.DepthNear; before, the nearest's)
            double far = Math.Max(OutlineRiverLoneDepth, Rules.OneBed ? Rivers.DepthNear(x, z, OutlineCourseSearch, OutlineRiverLoneDepth)
                : found ? Rivers.Depth(hit.Q) : OutlineRiverLoneDepth);
            double flowing = Rivers.BlendNear(x, z, far, ref fx, ref fz);
            // (channel rules 4: a shallower course in the wide water makes no ridge along its line: the courses only deepen it)
            if (Rules.OneBed) flowing = Math.Max(far, flowing);
            if (found)
                flowing += (ChannelDepthExact(in hit, 0) - hit.Depth)
                    * RiverNetwork.OutlineFade(hit.Dist, hit.HalfWidth, hit.HalfWidth + hit.BankWidth + hit.ValleyWidth);
            flowing = Math.Max(1, flowing);
            return OutlineRiverLoneDepth + (flowing - OutlineRiverLoneDepth) * Geo.SmoothStep(speed / CarryFull);
        }

        /// <summary>
        /// Channel rules 4: the median of the flow file's speeds along a reach's own course (RiverNetwork.ReachPoints), where it
        /// has the river's water (a course steps onto land at places); 0 where it has none. Sampled once per reach, when a
        /// column first asks (a pure function of the reach and the flow file, so threads that race compute the same). Kept for
        /// the whole model by whichever thread asks first: nothing that differs by thread or session may be read here
        /// (FineElevation.BuiltInOnly, Glo90Decides, a tile's arrival; ER-01, T-2 of the 14 November review).
        /// </summary>
        public double ReachSpeed(int reach)
        {
            if (Flow == null || Rivers == null || reach < 0) return 0;
            var a = reachFacts.Speed(Rivers.ReachCount);
            if (reach >= a.Length) return 0;
            float v = System.Threading.Volatile.Read(ref a[reach]);
            if (!float.IsNaN(v)) return v;
            var samples = new List<double>();
            Rivers.ReachPoints(reach, (px, pz) =>
            {
                var g = Geometry.ToGeo(px, pz);
                if (!g.InGutter && Flow.At(g.Lat, g.Lon, Rules.FlowAcrossTiles, out double e, out double n) && e * e + n * n > 0) samples.Add(Math.Sqrt(e * e + n * n));
            });
            samples.Sort();
            v = samples.Count == 0 ? 0 : (float)samples[samples.Count / 2];
            System.Threading.Volatile.Write(ref a[reach], v);
            return v;
        }

        /// <summary>Channel rules 4: a reach whose median solved speed (ReachSpeed) is at least this (m/s) is a flowing one; slower
        /// ones (a lake's outflow through a wide channel: the Mīlgrāvis from Ķīšezers) are still.</summary>
        public const double ReachFlowingSpeed = 0.15;

        /// <summary>
        /// Channel rules 4: how much (0..1) the courses near (x, z) are flowing ones (ReachSpeed at least ReachFlowingSpeed),
        /// blended as RiverNetwork.ReachBlend weighs them. The mask's river water is still or flowing by this, reach by reach,
        /// not by its own speed against a threshold: a slow lowland river hovering round any threshold was still and flowing
        /// in patches (the lower Lielupe at 10-20 cm/s, the Mīlgrāvis at 6-13).
        /// </summary>
        public double FlowingShare(double x, double z) =>
            Rivers == null ? 0 : Rivers.ReachBlend(x, z, OutlineCourseSearch, (r, q) => ReachSpeed(r) >= ReachFlowingSpeed ? 1 : 0, 0);

        /// <summary>
        /// Channel rules 4: the flow file's speed (m/s) averaged over the river water within AroundMetres of a place (its own and
        /// eight points on that circle; places without the river's flow left out), 0 where none has any. The slow water by a
        /// bank beside a fast core counts as the core's (the Norderelbe at Hamburg, beside the Alster's still water).
        /// </summary>
        double AroundSpeed(double lat, double lon)
        {
            if (Flow == null) return 0;
            double dLat = AroundMetres / Geo.MetresPerDegree, dLon = dLat / Math.Max(0.05, Math.Cos(lat * Math.PI / 180));
            double sum = 0;
            int n = 0;
            for (int k = -1; k < 8; k++)
            {
                double a = k * Math.PI / 4, la = k < 0 ? lat : lat + dLat * Math.Sin(a), lo = k < 0 ? lon : lon + dLon * Math.Cos(a);
                if (!Flow.At(la, lo, Rules.FlowAcrossTiles, out double e, out double nn)) continue;
                double v = Math.Sqrt(e * e + nn * nn);
                if (v <= 0) continue;
                sum += v;
                n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>Channel rules 4: AroundSpeed's radius (m): a few of the flow file's 90 m pixels.</summary>
        public const double AroundMetres = 200;

        /// <summary>
        /// The mask's river water's bed rises to its shore at this slope (blocks per block) from its depth (full) to the shore's
        /// (shore): OutlineRiverSlope, or from channel rules 4 steeper where the river outlines measure the shore over too short
        /// a reach for that (4 blocks at 1:100), so the water reaches its full depth there, but no steeper than ChannelSide4
        /// (a wide river at 1:100 is up to 9 blocks deep). Before, a wide river in the mask's
        /// water at 1:100 was 3 blocks deep at most (the Mississippi, the Amazon), and its course's channel cut a groove in it.
        /// </summary>
        double ShoreSlope(double full, double shore) =>
            Rules.OneBed && RiverOutlines != null ? Math.Clamp((full - shore) / RiverOutlines.Reach, OutlineRiverSlope, ChannelSide4) : OutlineRiverSlope;

        /// <summary>Channel rules 4: the share of a channel's half width over which its bed eases from the bank's depth to the
        /// middle's (ChannelDepthExact; 0.4 before).</summary>
        public const double ChannelEase4 = 0.7;
        /// <summary>Channel rules 4: the steepest a channel's side may be (blocks of depth per block across).</summary>
        public const double ChannelSide4 = 2;

        /// <summary>Channel rules 3: the solved speed (m/s) at which the water mask's river water has its full depth
        /// (MaskWaterDepth); still water keeps OutlineRiverLoneDepth.</summary>
        public const double CarryFull = 0.1;

        /// <summary>Channel rules 3: a river's trough in the sea eases out along its mouth and out to sea over at least this many
        /// blocks per block of depth it loses (ApplyMouth), so at fine and coarse scales alike no step is more than a block.</summary>
        const double TroughEase = 3;

        /// <summary>A river column's flow: its hit's direction; channel rules 1: blended over every course near
        /// (RiverNetwork.FlowNear, unless already blended: fx, fz), turned by up to about ±FlowWobble radians by a smooth
        /// noise, so the eight directions the water's blocks are labelled with meet along wandering lines, not straight ones
        /// across the river or between two courses (Rīgas HES).</summary>
        void SetFlow(double x, double z, in RiverNetwork.Hit hit, ref Column c, bool blended = false, double fx = 0, double fz = 0)
        {
            if (!Rules.Dredged)
            {
                c.FlowX = (float)hit.DirX;
                c.FlowZ = (float)hit.DirZ;
                return;
            }
            if (!blended)
            {
                fx = hit.DirX;
                fz = hit.DirZ;
                Rivers.FlowNear(x, z, ref fx, ref fz);
            }
            double a = FlowWobble * SeamNoise(n2, x, z, DepthWavelength, 2), ca = Math.Cos(a), sa = Math.Sin(a);
            c.FlowX = (float)(fx * ca - fz * sa);
            c.FlowZ = (float)(fx * sa + fz * ca);
        }

        /// <summary>SetFlow's turn (radians): about a third of an eighth of a turn at most.</summary>
        public const double FlowWobble = 0.3;

        /// <summary>Channel rules 1: how far (blocks, up to about ±DepthWobble) a smooth noise moves a river's depth (its
        /// channel's and the water mask's river water's) before it is rounded to whole blocks, so a step of a block between
        /// two depths wanders instead of running straight across the river (the Irtysh at 1:10).</summary>
        double DepthNoise(double x, double z) => DepthWobble * SeamNoise(n3, x, z, DepthWavelength, 2);

        /// <summary>DepthNoise's amplitude (blocks) and wavelength (blocks).</summary>
        public const double DepthWobble = 0.6, DepthWavelength = 24;

        /// <summary>0053 item 18: how far (blocks) a course's nearest centre-line point lies outside the water mask's rivers, up
        /// to the river layer's Reach; -1 in them, infinity where they don't decide or none is that near.</summary>
        double RiverOutlineGap(in RiverNetwork.Hit hit)
        {
            var g = Geometry.ToGeo(hit.CentreX, hit.CentreZ);
            if (g.InGutter || !RiverOutlinesDecide(g.Lat, g.Lon)) return double.PositiveInfinity;
            var rv = RiverOutlines.At(g.Lat, g.Lon);
            if (!rv.Covered || !rv.Inside && double.IsNaN(rv.Level)) return double.PositiveInfinity;
            return rv.Inside ? -1 : rv.Dist;
        }

        /// <summary>
        /// 0053 item 18: dry land in (Column.RiverOutline) the water mask's rivers is river water, at the level of
        /// the river course nearest to it (Nearest, or within OutlineCourseSearch beyond the course's own reach): one surface
        /// with the course's channel, its steps where the course's are, and at a lake's level beside a lake as the channel is.
        /// Where no course is that near (a side arm or an oxbow the courses don't have), at its piece's level (the median of
        /// GLO-90 in it, which is the water's surface there, flattened in half-metre steps; the column's own height wavers
        /// between them and made a checkerboard of two levels at Riga). The bed is a block below the water at the shore,
        /// deepening OutlineRiverSlope per block to the channel's depth at the course's centre line, and the channel's own
        /// where the channel is. Land beside the water (within LakeOutlineLayer.HoldReach) is held at least at its top.
        /// Not where the river is dry by the arid rules (the land stays as it is).
        /// </summary>
        void ApplyOutlineRiver(double x, double z, ref Column c)
        {
            bool found = Rivers.Nearest(x, z, out var hit) || Rivers.NearestCourse(x, z, OutlineCourseSearch, out hit);
            OutlineRiver(x, z, ref c, found, in hit);
        }

        /// <summary>ApplyOutlineRiver with Nearest's answer already in hand (near: it found a course).</summary>
        void ApplyOutlineRiver(double x, double z, ref Column c, bool near, RiverNetwork.Hit hit)
        {
            bool found = near || Rivers.NearestCourse(x, z, OutlineCourseSearch, out hit);
            OutlineRiver(x, z, ref c, found, in hit);
        }

        void OutlineRiver(double x, double z, ref Column c, bool found, in RiverNetwork.Hit hit)
        {
            int wt, ramp = 0;
            if (found)
            {
                // (channel rules 1: the water mask's river is never dried by the arid rules)
                if (Rules.AridRivers && !Rules.Dredged)
                {
                    var climate = Data.SampleClimate(c.Lat, c.Lon);
                    if (AridHydrology.Dry(climate.Rain, climate.TWarm - .0065 * c.Elev, hit.Q, WetSourceFlow != null && hit.Reach < WetSourceFlow.Length ? WetSourceFlow[hit.Reach] : hit.Q))
                        return;
                }
                wt = hit.WaterTop;
                ramp = hit.Ramp;
                // beside a lake at the lake's level, as ApplyRiver decides it
                int nearLake = c.NearLakeTop;
                if (Rivers.PoolLayout >= 9)
                {
                    nearLake = LakeTopAt(hit.CentreX, hit.CentreZ);
                    if (nearLake > wt && hit.StartTop < nearLake || nearLake < wt && hit.EndTop > nearLake) nearLake = -1;
                }
                if (nearLake >= 0 && Math.Abs(nearLake - wt) <= 3 && nearLake != wt)
                {
                    wt = nearLake;
                    ramp = 0;
                }
            }
            else wt = WaterTopY(c.RiverOutlineLevel);
            if (wt >= Vertical.MapHeight - 4 || wt < 2) return;
            c.NearRiver = true;
            int bankTop = wt + (ramp > 0 ? 1 : 0);
            // channel rules 5: the mask's water further out than the river's one width along it (PondProfile), up to where
            // it first meets land there, is land at the bank's height with a beach along the water; the water short of it
            // shelves to that new shore. Water past land (a side arm, an oxbow) and water going on to a lake, the sea or far
            // out (wide water of its own) stay.
            if (found && Rules.OneWidth && c.RiverOutline == RiverInOutline)
            {
                var pond = PondProfileOf(hit.Reach);
                if (pond != null && pond.At(hit.Along, ((x - hit.CentreX) * hit.DirZ - (z - hit.CentreZ) * hit.DirX) >= 0 ? 0 : 1,
                    out double runs, out double one))
                {
                    double limit = Math.Max(one, hit.HalfWidth) * (1 + PondSlack) + PondSlackBlocks;
                    if (hit.Dist > limit && hit.Dist <= runs + pond.Touch)
                    {
                        c.RiverOutline = RiverBesideOutline;
                        c.RiverOutlineDist = (float)(hit.Dist - limit);
                        if (c.Top < bankTop) c.Top = bankTop;
                        c.Beach = hit.Dist - limit <= PondBeach;
                        if (c.LakeRim > c.Top) c.Top = c.LakeRim;
                        return;
                    }
                    if (hit.Dist <= limit && runs > limit && c.RiverOutlineDist > limit - hit.Dist)
                        c.RiverOutlineDist = (float)(limit - hit.Dist);
                }
            }
            if (c.RiverOutline == RiverBesideOutline)
            {
                if (c.Top < bankTop)
                {
                    c.Top = bankTop;
                    c.Beach = true;
                }
                if (c.LakeRim > c.Top) c.Top = c.LakeRim;
                return;
            }
            // (channel rules 1: capped by every course near, not the nearest alone, and rounded through a smooth noise; the
            // flow blended over the same courses)
            double fx = hit.DirX, fz = hit.DirZ;
            // decision 0069: the flow solved on the mask's water (rivers-flow-glo90-1.bin), where this world reads it
            double east = 0, north = 0;
            bool solved = Rules.SolvedFlow && Flow != null && Rules.Dredged && Flow.At(c.Lat, c.Lon, Rules.FlowAcrossTiles, out east, out north);
            double speed = solved ? Math.Sqrt(east * east + north * north) : 0;
            // (T-R2, code review 14 November 2026) the water's depth, one method per rule set that froze it, so an edit to the
            // newest never touches the older ones
            int depth = solved && Rules.MouthBed ? OutlineDepthMouthBed(x, z, in c, found, in hit, speed, ref fx, ref fz)
                : solved ? OutlineDepthSolved(x, z, in c, found, in hit, speed, ref fx, ref fz)
                : Rules.Dredged ? OutlineDepthDredged(x, z, in c, found, in hit, ref fx, ref fz)
                : OutlineDepthRules0(in c, found, in hit);
            int top = wt - Math.Max(1, depth);
            if (found)
            {
                // (channel rules 1: the channel as its hit has it, narrowed by the water mask and deepened for it)
                double hw = Rules.Dredged ? hit.HalfWidth : Rivers.HalfWidth(hit.Q);
                if (hit.Dist <= hw)
                {
                    // (channel rules 4 where the flow is solved: the water's one depth, which is the course's own over its water
                    // already, and the channel only where the course comes into the mask's water from its edge, fading out a
                    // shore reach in: its channel cut deeper with walls for sides made a groove along the course, the Danube at
                    // Vienna)
                    if (!(solved && Rules.OneBed)) top = Math.Min(top, wt - ChannelDepthAt(x, z, in hit, hit.Dist / hw));
                    else
                    {
                        double edgeShare = 1 - Geo.SmoothStep(c.RiverOutlineDist / RiverOutlines.Reach);
                        int channel = wt - ChannelDepthAt(x, z, in hit, hit.Dist / hw);
                        if (channel < top) top = (int)Math.Round(top + (channel - top) * edgeShare);
                    }
                    if (hit.Rocks && (TerrainColumns.Hash((int)Math.Floor(x), (int)Math.Floor(z)) & 7) == 0) top = wt + 1;
                }
                if (!solved) SetFlow(x, z, in hit, ref c, true, fx, fz);
                c.Rapid = hit.Rapid;
                c.RapidsReach = Rivers.PoolLayout >= 8 && Rivers.ReachRapids(hit.Reach);
            }
            if (solved)
            {
                // rules 1 and 4: the solved direction (north is -z), and still water where it is slower than StillBelow (dead
                // ends, backwaters, the slack of a wide reach): no current labels there, and the river query says no flow
                c.FlowX = (float)east;
                c.FlowZ = (float)-north;
                // channel rules 4: still where it carries none of the river's flow (a dead end, a backwater), or where the
                // reaches near are slow ones (FlowingShare) and the water itself is no faster; before, wherever the water was
                // slower than StillBelow
                c.Still = (Rules.OneBed ? speed < CarryMin || AroundSpeed(c.Lat, c.Lon) < ReachFlowingSpeed && FlowingShare(x, z) < 0.5 : speed < StillBelow)
                    && !c.Rapid && ramp == 0;
            }
            c.Top = MouthShelf(wt, top, in c);
            c.WaterTop = wt;
            c.Water = WaterLake;
            c.River = true;
            c.Ramp = (byte)ramp;
            c.IceBottom = int.MaxValue;
            c.Beach = false;
        }

        /// <summary>Channel rules 3 with the solved flow: one smooth depth across the mask's water (MaskWaterDepth), shelving to
        /// the banks.</summary>
        int OutlineDepthMouthBed(double x, double z, in Column c, bool found, in RiverNetwork.Hit hit, double speed, ref double fx, ref double fz)
        {
            double full = MaskWaterDepth(x, z, found, in hit, speed, ref fx, ref fz);
            return (int)Math.Round(Math.Min(1 + ShoreSlope(full, 1) * c.RiverOutlineDist, full) + DepthNoise(x, z));
        }

        /// <summary>
        /// Channel rules 1-2 with the solved flow (decision 0069, rule 3): water that carries the river's flow is as deep as
        /// the river's channel across its whole width (the deepest course near, its own channel's depth), shelving to the
        /// banks; water that carries none (side arms, backwaters) keeps the lone depth. Before, wide water shelved to 3 blocks
        /// a bank and a valley away from the course, a groove along its line (the Daugava's mouth: 3 blocks across 170, 7 in 35).
        /// </summary>
        int OutlineDepthSolved(double x, double z, in Column c, bool found, in RiverNetwork.Hit hit, double speed, ref double fx, ref double fz)
        {
            double cap = OutlineDepthCap(x, z, found, in hit, ref fx, ref fz);
            double full = speed >= CarryMin ? Math.Max(cap, found ? ChannelDepthExact(in hit, 0) : OutlineRiverLoneDepth) : OutlineRiverLoneDepth;
            return (int)Math.Round(Math.Min(1 + OutlineRiverSlope * c.RiverOutlineDist, full) + DepthNoise(x, z));
        }

        /// <summary>Channel rules 1 without the solved flow: capped by every course near (OutlineDepthCap), rounded through a
        /// smooth noise.</summary>
        int OutlineDepthDredged(double x, double z, in Column c, bool found, in RiverNetwork.Hit hit, ref double fx, ref double fz) =>
            (int)Math.Round(Math.Min(1 + OutlineRiverSlope * c.RiverOutlineDist, OutlineDepthCap(x, z, found, in hit, ref fx, ref fz)) + DepthNoise(x, z));

        /// <summary>Channel rules 0 (0053 item 18): the nearest course's channel depth at its centre, or the lone depth.</summary>
        int OutlineDepthRules0(in Column c, bool found, in RiverNetwork.Hit hit) =>
            (int)Math.Round(Math.Min(1 + OutlineRiverSlope * c.RiverOutlineDist, found ? ChannelDepth(in hit, 0) : OutlineRiverLoneDepth));

        /// <summary>
        /// Pool layout 9: the water top of the lake data at a position (as a column there would have it in NearLakeTop), -1
        /// where there is none. 0064 first, as NaturalColumn decides it (WorksAt): a lake of 1318 at its level, and none at
        /// all inside a SEA or LAND area of 1318. After that the order is the one saved pool layout 9 worlds were made with,
        /// which is not quite NaturalColumn's: the grid's salt and kept lakes before an outline lake the place lies in, and
        /// along the canals no lake only where the outlines decide (NaturalColumn takes every grid lake out there).
        /// </summary>
        int LakeTopAt(double x, double z)
        {
            var g = Geometry.ToGeo(x, z);
            if (g.InGutter) return -1;
            var wk = WorksAt(g.Lat, g.Lon);
            if (Works1318Data.IsLake(wk)) return WaterTopY(wk.Level);
            if (Works1318Data.Undoes(wk)) return -1;
            // (without the coast references: only the lakes and their rings are read here)
            var s = Data.SampleTerrain(g.Lat, g.Lon, false);
            // 0053 item 17: the outline lakes as NaturalColumn gives them (the grid's salt and terminal lakes first)
            if (OutlinesDecide(g.Lat, g.Lon))
            {
                var lk = LakeOutlines.At(g.Lat, g.Lon);
                if (lk.Covered)
                {
                    StripGridLakes(ref s, g.Lat, g.Lon);
                    if (!double.IsNaN(s.LakeLevel) && s.Lake > 0) return WaterTopY(s.LakeLevel);
                    // (channel rules 1: none along the canals, as NaturalColumn)
                    if (Rules.Dredged && SeaChannels.InCanalZone(g.Lat, g.Lon)) return -1;
                    return double.IsNaN(lk.Level) ? -1 : WaterTopY(lk.Level);
                }
            }
            return !double.IsNaN(s.LakeLevel) && s.Lake > 0 ? WaterTopY(s.LakeLevel) : -1;
        }

        /// <summary>Pool layout 4: in the ring of a lake more than 3 blocks above the river, its banks and valley keep the land
        /// as high as the lake's eased shore holds it (not above the natural ground): a rim at the lake's level beside the lake,
        /// sinking away from it. Before, the valley cut the lake's shore and its water stood up to 11 blocks above dry land.</summary>
        int LakeHeld(in Column c, int wt, int natural, int carved) =>
            Rivers.PoolLayout >= 4 && c.LakeRingTop > wt + 3 ? Math.Max(carved, Math.Min(natural, c.LakeHoldTop)) : carved;

        /// <summary>RiverSettling.Settle with its rules as flags (pool layout 1, 4, 5, 6, 7, 8 and 10 and later; callers
        /// that know the pool layout can pass SettleRules.ForPoolLayout instead).</summary>
        public static void SettleSteps(Column[] cols, int w, int h, bool curtainBanks = false, bool lakeBanks = false, bool sills = false,
            bool rapidRule = false, bool whiteWater = false, bool noSills = false, bool noSpills = false)
            => RiverSettling.Settle(cols, w, h, new SettleRules(curtainBanks, lakeBanks, sills, rapidRule, whiteWater, noSills, noSpills));
    }
}
