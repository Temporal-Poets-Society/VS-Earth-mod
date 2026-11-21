using System;

namespace EarthMap.Core
{
    // TerrainModel, part: EA-0131's streams layer (decision 0061): small streams drawn on the land the world's rivers leave.
    public sealed partial class TerrainModel
    {
        /// <summary>
        /// EA-0131 (decision 0061): the streams layer, or null (every world before EarthParams.StreamRules 1, worlds coarser than
        /// 1:25, a world that goes without the file). Its streams are drawn after the world's own rivers, only where those left
        /// no water: a channel a block wide and deep at its pools' levels, with the river network's steps, chutes, falls and
        /// rapids, a bank held at the water and a small valley easing into the land (ApplyStream).
        /// </summary>
        public StreamLayer Streams;

        /// <summary>
        /// The column at a source position (no companion's expansion) as the world draws it without the streams layer: what a
        /// stream joins (StreamLayer). Any thread.
        /// </summary>
        public Column MainColumn(double x, double z) => ColumnForExpansion(null, x, z, mainOnly: true);

        /// <summary>
        /// The stream whose water is at a column (source coordinates): a stream's hit within its half width where the world
        /// draws no water of its own, and the stream's network (for its reach's rapids and slope). For the river query
        /// (EarthMapSystem.RiverAt), which answers stream columns with the stream's discharge.
        /// </summary>
        public bool StreamWaterAt(double x, double z, out RiverNetwork.Hit hit, out RiverNetwork net)
        {
            hit = default;
            net = null;
            if (Streams == null) return false;
            var g = Geometry.ToGeo(x, z);
            if (g.InGutter || !Streams.Nearest(x, z, g.Lat, g.Lon, out hit, out net) || hit.Dist > hit.HalfWidth) return false;
            return MainColumn(x, z).Water == WaterNone;
        }

        /// <summary>
        /// A stream on a column the world's rivers, lakes and sea left dry (ice and glaciers too stay as they are): as
        /// ApplyChannel draws a course on land, with the stream's network's hit. Its water at its pool's top (beside a lake
        /// within 3 blocks the lake's, StreamLevel), a bed of the channel's depth; its bank flush with the water (a block
        /// higher beside a ramp), cut down or built up to hold it; its valley easing into the land. Where a large river's
        /// bank or valley already shaped the land (Column.NearRiver), the stream only raises it to its own bank, never cuts
        /// it, so the river's banks keep holding the river.
        /// </summary>
        void ApplyStream(double x, double z, ref Column c)
        {
            if (c.Water != WaterNone || c.IceBottom <= c.Top || Rivers == null) return;
            if (!Streams.Nearest(x, z, c.Lat, c.Lon, out var hit, out var net)) return;
            if (hit.Mouth) return;
            bool shaped = c.NearRiver;
            double hw = hit.HalfWidth, bank = hit.BankWidth, valley = hit.ValleyWidth;
            StreamLevel(in hit, net, c.NearLakeTop, out int wt, out int ramp);
            if (wt >= Vertical.MapHeight - 4 || wt < 2) return;
            int bankTop = wt + (ramp > 0 ? 1 : 0);
            c.NearRiver = true;
            if (hit.Dist <= hw && hw > 0)
            {
                c.Top = wt - ChannelDepthAt(x, z, in hit, hit.Dist / hw);
                if (hit.Rocks && (TerrainColumns.Hash((int)Math.Floor(x), (int)Math.Floor(z)) & 7) == 0) c.Top = wt + 1;
                c.WaterTop = wt;
                c.Water = WaterLake;
                c.River = true;
                StreamFlow(x, z, in hit, net, ref c);
                c.Ramp = (byte)ramp;
                c.Rapid = hit.Rapid;
                c.RapidsReach = net.PoolLayout >= 8 && net.ReachRapids(hit.Reach);
                c.IceBottom = int.MaxValue;
                c.Beach = false;
                return;
            }
            int natural = c.Top;
            if (hit.Dist <= hw + bank)
            {
                c.Top = shaped ? Math.Max(c.Top, bankTop) : Math.Max(LakeHeld(c, wt, natural, bankTop), c.LakeRim);
                c.IceBottom = int.MaxValue;
                // (the author, 17 November 2026: "Yes, match the land": a bank of bare shore material only in a steep gully)
                c.Beach = hit.Dist <= hw + 2 && c.Top == bankTop && (!StreamBanksMatchLand || BareStreamBank(natural, bankTop, in hit, net));
                return;
            }
            if (shaped) return;
            double f = Geo.SmoothStep((hit.Dist - hw - bank) / valley);
            int floorTop = bankTop + 1;
            if (c.Top > floorTop) c.Top = (int)Math.Round(floorTop + (c.Top - floorTop) * f);
            else if (c.Top < wt && f < 0.25) c.Top = wt;
            c.Top = Math.Max(LakeHeld(c, wt, natural, c.Top), c.LakeRim);
            if (c.IceBottom <= c.Top && f < 1) c.IceBottom = int.MaxValue;
        }

        /// <summary>
        /// EA-0131 (the author, 17 November 2026: "Yes, match the land"): a stream's bank keeps the land's own surface (grass,
        /// soil, sand: whatever the column's surface pass gives it) except in a steep gully, where it is bare shore material (the
        /// shore strip's: stone, gravel). False: every bank flush with the water is shore material, as a river's (the first
        /// bench: from above, a gentle stream's grey banks read as a path).
        /// </summary>
        public const bool StreamBanksMatchLand = true;
        /// <summary>A gully: the bank cut this many blocks or more into the land, or the stream's water falling this much (blocks
        /// per block, RiverNetwork.ReachSlope) or more.</summary>
        public const int BareBankCut = 4;
        public const double BareBankSlope = 0.1;

        /// <summary>Whether a stream's bank column is bare (StreamBanksMatchLand): cut BareBankCut or more into the land, or on
        /// a reach falling BareBankSlope or more.</summary>
        static bool BareStreamBank(int natural, int bankTop, in RiverNetwork.Hit hit, RiverNetwork net)
            => natural - bankTop >= BareBankCut || net.ReachSlope(hit.Reach) >= BareBankSlope;

        /// <summary>A stream's water top and ramp at a hit: its pool's, or beside a lake within 3 blocks the lake's (as
        /// RiverLevel: from pool layout 9 decided at the centre line, only towards the reach's own end levels).</summary>
        void StreamLevel(in RiverNetwork.Hit hit, RiverNetwork net, int nearLakeTop, out int wt, out int ramp)
        {
            wt = hit.WaterTop;
            ramp = hit.Ramp;
            int nearLake = nearLakeTop;
            if (net.PoolLayout >= 9)
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

        /// <summary>A stream column's flow, as SetFlow labels a river's: the hit's direction, from channel rules 1 blended over
        /// the stream network's courses near, turned by the same smooth noise.</summary>
        void StreamFlow(double x, double z, in RiverNetwork.Hit hit, RiverNetwork net, ref Column c)
        {
            double fx = hit.DirX, fz = hit.DirZ;
            if (!Rules.Dredged)
            {
                c.FlowX = (float)fx;
                c.FlowZ = (float)fz;
                return;
            }
            net.FlowNear(x, z, ref fx, ref fz);
            double a = FlowWobble * SeamNoise(n2, x, z, DepthWavelength, 2), ca = Math.Cos(a), sa = Math.Sin(a);
            c.FlowX = (float)(fx * ca - fz * sa);
            c.FlowZ = (float)(fx * sa + fz * ca);
        }
    }
}
