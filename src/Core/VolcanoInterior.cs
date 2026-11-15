using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// A volcano's interior in worlds of geothermal rules 3 (decision 0078, EA-0067; the author of rules 2's: "built as slop
    /// work": a 6-block-thin lava sheet for a chamber, a 4-block pipe up to the crater, loose blocks in caves). Rules 2 sized
    /// every part in real metres on each axis, but a world's underground is squeezed: 10 km between sea level and the bottom
    /// are some 94 blocks at every scale, while across a block is 5-200 m. At 1:10 a chamber 1.6 km wide and 600 m high
    /// became 160 blocks by 6. Rules 3 take the real sizes across and give the shapes their real proportions in blocks:
    /// - The cone (composite volcanoes and shields): above its base, the regional rock is the volcano's own, in layers that
    ///   follow its slopes: lava flows and, between them, ash and scoria beds (shields: lava flows, an ash bed now and then).
    ///   The layers keep one depth under the cone's smooth profile, so valleys cut through them.
    /// - The magma chamber: a lens with a domed roof and a flatter floor, its outline slightly irregular, about 0.4 as high as
    ///   wide (shields and calderas 0.3), at the depth of rules 2. Its lava lies in a rind of crystallised magma; in a basaltic
    ///   chamber crystals have settled on its floor (peridotite). Dormant volcanoes: a lava core 0.6 of it, the rest solid.
    /// - The conduit: 40 m in radius (shields 50 m; at least 1.5 blocks), rising from the chamber's roof through a cupola,
    ///   bowing gently on its way (never by more than its radius), and flaring into a funnel under the crater; a rind of chilled rock around it (the
    ///   glassy obsidian of silicic magma). Active: lava up to 3 blocks of crust under the vent; dormant: a plug of solid rock.
    /// - Dykes and a sill (scales 1:50 and finer): blades of solid magma rock rising from the conduit towards the flanks, 1 block
    ///   thick (2 at 1:5), ending under the surface (composite cones 3, dormant 2; shields 2, along a rift); a sill along the
    ///   cone's base; a ring dyke over a caldera's chamber.
    /// - A hot zone: no cave or water within 3 blocks of any of these parts; a cave reaching it ends in its own rock.
    /// Lava is always 1.5 blocks or more inside solid rock, so none can flow out until a player opens it.
    /// </summary>
    public sealed class VolcanoInterior
    {
        /// <summary>What a block of the interior is. Lava, then the solid parts, then the hot zone take precedence.</summary>
        public enum Part : byte { None, Hot, Sill, Dyke, Margin, Sleeve, Plug, Crust, Cumulate, Lava }

        /// <summary>How far from the vent the cone's profile is looked for (m): composite cones and shields.</summary>
        public const double CompositeEdificeM = 20000, ShieldEdificeM = 40000;
        /// <summary>A cone lower than this over its base (blocks) gets no layers of its own.</summary>
        public const int MinEdificeRelief = 12;
        /// <summary>The conduit's radius (m), at least <see cref="MinConduitR"/> blocks.</summary>
        public const double ConduitRadiusM = 40, ShieldConduitRadiusM = 50, MinConduitR = 1.5;
        /// <summary>A chamber's least half-width (blocks), and its height over its half-width: composite, shield, caldera.</summary>
        public const double MinHalfWidth = 5, CompositeAspect = 0.4, ShieldAspect = 0.3, CalderaAspect = 0.3;
        /// <summary>The floor's half-height as a share of the roof's.</summary>
        public const double FloorShare = 0.6;
        /// <summary>Least rock between lava and anything else (blocks), the conduit's rind, and the hot zone beyond a part.</summary>
        public const double CoreMargin = 1.5, Sleeve = 1.5;
        public const int HotZone = 3;
        /// <summary>Crust over an active conduit's lava (blocks), the vent's own block included.</summary>
        public const int Crust = 3;
        /// <summary>Dykes and sills only where a block is at most this many metres across (1:50 and finer).</summary>
        public const double DykeMaxMetresPerBlock = 50;
        /// <summary>Dykes end this far under the surface (blocks).</summary>
        public const int DykeCover = 6;

        public readonly VolcanoPlan Plan;
        public readonly Geothermal.Magma Magma;
        public readonly bool Active;
        readonly int ventX, ventZ;

        // the chamber
        public readonly double ChamberY, A, BTop, BBot;
        public readonly int ChamberTop, ChamberBottom;
        readonly double cp2, cp3;

        // the conduit
        public readonly double R0, RCupola, RFunnel, Meander;
        /// <summary>Where the conduit's lava (active) or plug (dormant) ends, and its top (the vent or the lava lake's floor).</summary>
        public readonly int ConduitLavaTop, PlugTop, ConduitTop;
        readonly double cupolaY, cupolaH, funnelY, ma, mb, conduitReach;

        // dykes, ring dyke, sill
        public readonly IReadOnlyList<Dyke> Dykes;
        public readonly bool Ring;
        public readonly double RingR, RingY0, RingY1;
        public readonly bool Sill;
        public readonly int SillY;
        public readonly double SillR;
        readonly double dykeHalf;

        // the cone
        public readonly bool Edifice;
        public readonly int BaseY;
        public readonly double EdgeR, EdificeRelief;
        readonly double[] profile;
        readonly double profileStep, tilt;
        readonly int[] layerStart;
        readonly bool[] layerAsh;

        /// <summary>How far from the vent any part reaches (blocks).</summary>
        public readonly double Reach;

        /// <summary>A dyke: a vertical blade along an azimuth from the vent, from its root on the conduit (S0, Y0) up and out
        /// to its tip (S1, Y1), Width across at its widest.</summary>
        public sealed class Dyke
        {
            public double Angle, Cos, Sin, S0, Y0, S1, Y1, Width;
            /// <summary>Where its plane meets the conduit's axis at its root, from the vent.</summary>
            public double OX, OZ;
            internal double Len, Ux, Uy;
        }

        /// <summary>The per-column measures <see cref="At"/> and <see cref="Layer"/> read.</summary>
        public struct ColumnPrep
        {
            public int X, Z;
            public double D, Wobble, Angle;
            /// <summary>The cone's smooth surface here (NaN outside it) and its layers' shift.</summary>
            public double Profile, Shift;
            public double S0, P0, S1, P1, S2, P2;
            /// <summary>Whether any part but the cone's layers can reach this column.</summary>
            public bool Any;
        }

        /// <summary>GenFixRules 4 (ER-19, code review 14 November 2026), a vent without a crater: the conduit has nothing above
        /// the column's own top and lava only 2 blocks or more under it. Such a vent is wet (a crater lake, the sea, a shore),
        /// and where its column falls below the vent's top the conduit's lava stood in the water or the air. Vents with a
        /// crater (and lava lakes) are as before: their floor covers the conduit.</summary>
        readonly bool capToTop;

        public VolcanoInterior(VolcanoPlan plan, TerrainModel model)
        {
            Plan = plan;
            capToTop = model.Rules.ConduitUnderTop && !plan.Crater;
            var v = plan.Volcano;
            Magma = v.MagmaFamily;
            Active = v.Active;
            ventX = plan.VentX;
            ventZ = plan.VentZ;
            var vs = model.Vertical;
            double mpb = model.MetresPerBlock;
            double under = VolcanoPlan.BlocksPerMetreUnder(vs);
            int sea = vs.SeaLevel;
            bool shield = v.Form == Geothermal.Landform.Shield, caldera = v.Form == Geothermal.Landform.Caldera;
            int topY = plan.Lake ? plan.LakeFloorY : plan.VentTop;

            // the cone: the mean height of the land on rings around the vent, out to where it no longer falls
            if (!caldera && !plan.Submarine)
            {
                int cap = (int)Math.Max(16, (shield ? ShieldEdificeM : CompositeEdificeM) / mpb);
                int steps = Math.Min(160, cap);
                double step = cap / (double)steps;
                const int angles = 48;
                var mean = new double[steps + 1];
                int last = steps;
                for (int k = 0; k <= steps; k++)
                {
                    double r = k * step, sum = 0;
                    int land = 0, n = k == 0 ? 1 : angles;
                    for (int j = 0; j < n; j++)
                    {
                        double a = 2 * Math.PI * j / angles;
                        var c = model.GetColumn(ventX + r * Math.Cos(a), ventZ + r * Math.Sin(a));
                        if (c.Water == TerrainModel.WaterSea || c.Top < sea - 1) continue;
                        sum += c.Top;
                        land++;
                    }
                    if (k > 0 && land * 2 < n) { last = k - 1; break; }
                    mean[k] = land > 0 ? sum / land : mean[Math.Max(0, k - 1)];
                }
                double peak = double.MinValue, low = double.MaxValue;
                int peakK = 0;
                for (int k = 0; k <= last; k++)
                {
                    if (mean[k] > peak) { peak = mean[k]; peakK = k; }
                    low = Math.Min(low, mean[k]);
                }
                int edge = last;
                for (int k = peakK; k <= last; k++)
                {
                    if (mean[k] <= low + 0.1 * (peak - low)) { edge = k; break; }
                }
                double baseY = mean[edge];
                if (peak - baseY >= MinEdificeRelief && edge * step >= 8)
                {
                    Edifice = true;
                    BaseY = (int)Math.Round(baseY);
                    EdgeR = edge * step;
                    EdificeRelief = peak - baseY;
                    profileStep = step;
                    // non-increasing outwards: the crater and the valleys don't pull the layers down
                    profile = new double[edge + 1];
                    double run = baseY;
                    for (int k = edge; k >= 0; k--)
                    {
                        run = Math.Max(run, mean[k]);
                        profile[k] = run;
                    }
                    tilt = 2 * Math.PI * Hash(v.Number, 11);
                    // the layers from the top down: lava flows with ash beds between (shields: an ash bed every fourth layer)
                    var starts = new List<int>();
                    var ash = new List<bool>();
                    int depth = 0, k2 = 0;
                    while (depth <= EdificeRelief + 8)
                    {
                        double h = Hash(v.Number, 100 + k2);
                        bool isAsh = shield ? k2 % 4 == 3 : k2 % 2 == 1;
                        int t = isAsh ? (shield ? 1 : 1 + (h < 0.4 ? 1 : 0)) : (shield ? 1 + (int)(h * 3) : 2 + (int)(h * 3));
                        starts.Add(depth);
                        ash.Add(isAsh);
                        depth += t;
                        k2++;
                    }
                    starts.Add(int.MaxValue);
                    layerStart = starts.ToArray();
                    layerAsh = ash.ToArray();
                }
            }

            // the chamber: at rules 2's depth, real across, its height from its width
            double summitElev = vs.Elevation(plan.SummitY);
            double depthM = v.Lake != null || shield ? 2500 : caldera ? 4000 : 5000;
            double e = summitElev - depthM;
            double y = e >= 0 ? vs.Y(e) : sea + e * under;
            double halfW = (v.Active ? VolcanoPlan.ActiveHalfWidthM : VolcanoPlan.DormantHalfWidthM) * (caldera ? VolcanoPlan.CalderaWidening : 1);
            A = Math.Max(MinHalfWidth, halfW / mpb);
            double aspect = shield ? ShieldAspect : caldera ? CalderaAspect : CompositeAspect;
            double bTop = Math.Max(4, A * aspect), bBot = Math.Max(3, bTop * FloorShare);
            // room: a conduit of at least 12 blocks (and 30 % of the way) over the roof, 4 blocks of rock under the floor
            double roomTop = topY - Math.Max(12, 0.3 * (topY - y)), roomBot = 4;
            if (bTop + bBot > roomTop - roomBot)
            {
                double f = Math.Max(0.1, (roomTop - roomBot) / (bTop + bBot));
                bTop = Math.Max(1.5, bTop * f);
                bBot = Math.Max(1, bBot * f);
            }
            BTop = bTop;
            BBot = bBot;
            ChamberY = Math.Clamp(y, roomBot + bBot, Math.Max(roomBot + bBot, roomTop - bTop));
            ChamberTop = (int)Math.Floor(ChamberY + BTop);
            ChamberBottom = (int)Math.Ceiling(ChamberY - BBot);
            cp2 = 2 * Math.PI * Hash(v.Number, 21);
            cp3 = 2 * Math.PI * Hash(v.Number, 22);

            // the conduit
            ConduitTop = topY;
            double r0 = Math.Max(MinConduitR, (shield ? ShieldConduitRadiusM : ConduitRadiusM) / mpb);
            if (plan.Lake) r0 = Math.Max(MinConduitR, Math.Min(r0, plan.LakeR - 0.5));
            R0 = r0;
            double length = ConduitTop - ChamberY;
            RCupola = Math.Min(Math.Max(R0 + 1, 0.3 * A), R0 * 3 + 2);
            cupolaY = ChamberY + 0.6 * BTop;
            cupolaH = Math.Max(3, 1.5 * (RCupola - R0) + 2);
            RFunnel = plan.Crater && !plan.Lake ? Math.Max(R0, 0.25 * plan.CraterR) : R0;
            funnelY = ConduitTop - Math.Max(3, 3 * (RFunnel - R0));
            // within the conduit's own radius, so a cut through the vent always meets it
            Meander = length > 20 ? Math.Min(0.6 * R0, 0.012 * length) : 0;
            conduitReach = Math.Max(RCupola, RFunnel) + Sleeve + HotZone + Meander * 1.5 + 1;
            ma = 2 * Math.PI * Hash(v.Number, 31);
            mb = 2 * Math.PI * Hash(v.Number, 32);
            if (Active)
            {
                ConduitLavaTop = plan.Lake ? plan.LakeFloorY : ConduitTop - Crust;
                PlugTop = ConduitTop;
            }
            else
            {
                ConduitLavaTop = int.MinValue;
                PlugTop = ConduitTop - 4;
            }

            // dykes, the ring dyke and the sill, at 1:50 and finer
            var dykes = new List<Dyke>();
            // the extra thickness beyond one block (a digital plane's): 2 blocks at 1:5
            dykeHalf = mpb <= 7 ? 0.5 : 0;
            if (mpb <= DykeMaxMetresPerBlock && ConduitTop - ChamberTop >= 30)
            {
                double l = ConduitTop - ChamberTop;
                if (caldera)
                {
                    RingR = 0.75 * A;
                    RingY0 = ChamberY + 0.4 * BTop;
                    RingY1 = Math.Min(ChamberTop + 0.5 * l, ConduitTop - 8);
                    Ring = RingY1 - RingY0 >= 6;
                }
                int n = shield ? 2 : caldera ? 1 : Active ? 3 : 2;
                double a0 = 2 * Math.PI * Hash(v.Number, 41);
                for (int i = 0; i < n; i++)
                {
                    double ang = shield ? a0 + i * (Math.PI + (Hash(v.Number, 42) - 0.5) * 0.6) : a0 + i * 2 * Math.PI / n + (Hash(v.Number, 43 + i) - 0.5) * 0.8;
                    double y0 = ChamberTop + 0.25 * l;
                    double reach = Edifice ? (shield ? 0.6 : 0.35) * EdgeR : 3 * A;
                    double s1 = Math.Max(reach, 0.8 * A + 6);
                    Dyke d = null;
                    for (int tries = 0; tries < 2 && d == null; tries++, s1 *= 0.5)
                    {
                        int surf = int.MaxValue;
                        for (int k = -1; k <= 1; k++)
                            surf = Math.Min(surf, model.GetColumn(ventX + (s1 + k) * Math.Cos(ang), ventZ + (s1 + k) * Math.Sin(ang)).Top);
                        double y1 = Math.Min(surf - 10, y0 + (shield ? 0.4 : 0.55) * l);
                        if (y1 < y0 + 6 || s1 < 4) continue;
                        Axis(y0, out double ox, out double oz);
                        d = new Dyke { Angle = ang, Cos = Math.Cos(ang), Sin = Math.Sin(ang), S0 = 0, Y0 = y0, S1 = s1, Y1 = y1, OX = ox, OZ = oz };
                        double ds = d.S1 - d.S0, dy = d.Y1 - d.Y0;
                        d.Len = Math.Sqrt(ds * ds + dy * dy);
                        d.Ux = ds / d.Len;
                        d.Uy = dy / d.Len;
                        d.Width = Math.Clamp(0.18 * d.Len, 2, 40);
                    }
                    if (d != null) dykes.Add(d);
                }
                if (Edifice && BaseY > ChamberTop + 8 && BaseY < ConduitTop - 12)
                {
                    Sill = true;
                    SillY = BaseY;
                    SillR = Math.Clamp(0.7 * A, 4, Math.Max(4, 0.35 * EdgeR));
                }
            }
            Dykes = dykes;

            double reachAll = A * 1.13 + HotZone + 1;
            reachAll = Math.Max(reachAll, Math.Max(RCupola, RFunnel) + Sleeve + HotZone + Meander + 1);
            foreach (var d in dykes) reachAll = Math.Max(reachAll, d.S1 + d.Width + HotZone);
            if (Ring) reachAll = Math.Max(reachAll, RingR + 0.08 * (RingY1 - RingY0) + HotZone + 1);
            if (Sill) reachAll = Math.Max(reachAll, SillR + HotZone);
            if (Edifice) reachAll = Math.Max(reachAll, EdgeR + 1);
            Reach = reachAll;
        }

        static double Hash(int number, int salt) => StableHash.Unit24(number, salt);

        static double SmoothStep(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The farthest a body of this landform can reach at this scale (blocks), for indexing before planning: its
        /// cone's search, or (calderas) its dykes over the widest chamber.</summary>
        public static double MaxReach(double metresPerBlock, Geothermal.Landform form) => form switch
        {
            Geothermal.Landform.Shield => Math.Max(16, ShieldEdificeM / metresPerBlock) + 64,
            Geothermal.Landform.Caldera => 4 * Math.Max(MinHalfWidth, VolcanoPlan.ActiveHalfWidthM * VolcanoPlan.CalderaWidening / metresPerBlock) + 64,
            _ => Math.Max(16, CompositeEdificeM / metresPerBlock) + 64,
        };

        /// <summary>The lowest and highest block any part can take (the cone's layers included).</summary>
        public int Bottom => ChamberBottom - HotZone - 1;

        public ColumnPrep Prepare(int x, int z)
        {
            var c = new ColumnPrep { X = x, Z = z };
            double dx = x - ventX, dz = z - ventZ;
            c.D = Math.Sqrt(dx * dx + dz * dz);
            c.Angle = Math.Atan2(dz, dx);
            c.Wobble = 1 + 0.12 * (0.6 * Math.Sin(2 * c.Angle + cp2) + 0.4 * Math.Sin(3 * c.Angle + cp3));
            c.Profile = double.NaN;
            if (Edifice && c.D < EdgeR)
            {
                double f = c.D / profileStep;
                int k = (int)f;
                double t = f - k;
                c.Profile = k + 1 < profile.Length ? profile[k] * (1 - t) + profile[k + 1] * t : profile[profile.Length - 1];
                c.Shift = Math.Cos(c.Angle - tilt) * c.D / EdgeR;
            }
            c.Any = c.D <= A * 1.13 + HotZone + 1 || c.D <= conduitReach || Ring && c.D <= RingR + 0.08 * (RingY1 - RingY0) + 3
                || Sill && c.D <= SillR + HotZone;
            for (int i = 0; i < Dykes.Count && i < 3; i++)
            {
                var d = Dykes[i];
                double ex = dx - d.OX, ez = dz - d.OZ;
                double s = ex * d.Cos + ez * d.Sin, p = -ex * d.Sin + ez * d.Cos;
                if (i == 0) { c.S0 = s; c.P0 = p; }
                else if (i == 1) { c.S1 = s; c.P1 = p; }
                else { c.S2 = s; c.P2 = p; }
                if (Math.Abs(p) <= dykeHalf + 3 && s >= -0.5 && s <= d.S1 + d.Width) c.Any = true;
            }
            return c;
        }

        /// <summary>The cone's layer at a block: -1 outside the cone (or under its base), 0 a lava flow, 1 an ash bed.</summary>
        public int Layer(in ColumnPrep c, int y)
        {
            if (double.IsNaN(c.Profile) || y <= BaseY) return -1;
            double depth = c.Profile - y + c.Shift;
            if (depth < 0) return layerAsh[0] ? 1 : 0;
            int lo = 0, hi = layerStart.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (layerStart[mid] <= depth) lo = mid; else hi = mid - 1;
            }
            return layerAsh[lo] ? 1 : 0;
        }

        /// <summary>The conduit's axis at a height: it bows gently between the chamber and the vent, where it is straight over
        /// both.</summary>
        public void Axis(double y, out double ox, out double oz)
        {
            ox = oz = 0;
            if (Meander <= 0) return;
            double t = Math.Clamp((y - ChamberY) / (ConduitTop - ChamberY), 0, 1);
            double a = Meander * Math.Sin(Math.PI * t), b = 0.4 * Meander * Math.Sin(2 * Math.PI * t);
            ox = a * Math.Cos(ma) + b * Math.Cos(mb);
            oz = a * Math.Sin(ma) + b * Math.Sin(mb);
        }

        /// <summary>The conduit's radius at a height: the cupola over the chamber, the funnel under the crater.</summary>
        public double ConduitRadius(double y)
        {
            double r = R0;
            if (y < cupolaY + cupolaH) r = Math.Max(r, R0 + (RCupola - R0) * (1 - SmoothStep((y - cupolaY) / cupolaH)));
            if (y > funnelY) r = Math.Max(r, R0 + (RFunnel - R0) * SmoothStep((y - funnelY) / Math.Max(1, ConduitTop - funnelY)));
            return r;
        }

        static Part Max(Part a, Part b) => a >= b ? a : b;

        /// <summary>What the interior makes of a block (y) in a prepared column whose solid surface is at <paramref name="top"/>.</summary>
        public Part At(in ColumnPrep c, int y, int top)
        {
            var part = Part.None;
            // the chamber
            if (y >= ChamberBottom - HotZone - 1 && y <= ChamberTop + HotZone + 1 && c.D <= A * 1.13 + HotZone + 1)
            {
                double b = y >= ChamberY ? BTop : BBot, aw = A * c.Wobble, dy = y - ChamberY;
                double q = c.D * c.D / (aw * aw) + dy * dy / (b * b);
                if (q <= 1)
                {
                    double inner = (1 - Math.Sqrt(q)) * Math.Min(aw, b);
                    bool core = inner >= CoreMargin && (Active || Math.Sqrt(q) <= 0.6);
                    if (!core) part = Part.Margin;
                    else if (Magma == Geothermal.Magma.Basaltic && dy < -0.5 * BBot) part = Part.Cumulate;
                    else return Part.Lava;
                }
                else
                {
                    double ah = aw + HotZone, bh = b + HotZone;
                    if (c.D * c.D / (ah * ah) + dy * dy / (bh * bh) <= 1) part = Part.Hot;
                }
            }
            // the conduit
            if (y >= ChamberY && y <= ConduitTop && c.D <= conduitReach)
            {
                Axis(y, out double ox, out double oz);
                double ex = c.X - ventX - ox, ez = c.Z - ventZ - oz;
                double dc = Math.Sqrt(ex * ex + ez * ez), r = ConduitRadius(y);
                if (dc <= r + Sleeve + HotZone)
                {
                    if (Active)
                    {
                        if (capToTop && y > top && dc <= r + Sleeve) { }
                        else if (dc <= r) { if (y <= ConduitLavaTop && !(capToTop && y > top - 2)) return Part.Lava; part = Max(part, Part.Crust); }
                        else if (dc <= r + Sleeve) part = Max(part, y > ConduitLavaTop ? Part.Crust : Part.Sleeve);
                        else part = Max(part, Part.Hot);
                    }
                    else
                    {
                        if (y <= PlugTop && dc <= r) part = Max(part, Part.Plug);
                        else if (y <= PlugTop && dc <= r + Sleeve) part = Max(part, Part.Sleeve);
                        else part = Max(part, Part.Hot);
                    }
                }
            }
            if (part >= Part.Margin) return part;
            // dykes
            for (int i = 0; i < Dykes.Count && i < 3; i++)
            {
                double s = i == 0 ? c.S0 : i == 1 ? c.S1 : c.S2, p = i == 0 ? c.P0 : i == 1 ? c.P1 : c.P2;
                if (Math.Abs(p) > dykeHalf + 3 || s < -0.5) continue;
                var d = Dykes[i];
                double rs = s - d.S0, ry = y - d.Y0;
                double t = (rs * d.Ux + ry * d.Uy) / d.Len, across = Math.Abs(-rs * d.Uy + ry * d.Ux);
                if (t < 0 || t > 1) continue;
                double w = d.Width * Math.Sqrt(Math.Sin(Math.PI * (0.1 + 0.9 * t)));
                if (across > w + 2 || y > top - 1) continue;
                // a standard digital plane (|cos| + |sin| thick across the normal): its blocks meet by their sides at any azimuth
                double half = dykeHalf + 0.5 * (Math.Abs(d.Cos) + Math.Abs(d.Sin));
                if (p >= -half && p < half && across <= w && y <= top - DykeCover) part = Max(part, Part.Dyke);
                else part = Max(part, Part.Hot);
            }
            // the ring dyke over a caldera's chamber
            if (Ring && y >= RingY0 - 2 && y <= RingY1 + 2 && y <= top - 1)
            {
                double rr = RingR + 0.08 * (y - RingY0), off = c.D - rr;
                double half = 0.5 * (Math.Abs(Math.Cos(c.Angle)) + Math.Abs(Math.Sin(c.Angle)));
                if (off >= -half && off < half && y >= RingY0 && y <= RingY1 && y <= top - DykeCover) part = Max(part, Part.Dyke);
                else if (Math.Abs(off) <= 2.6) part = Max(part, Part.Hot);
            }
            // the sill along the cone's base
            if (Sill && c.D <= SillR + HotZone && Math.Abs(y - SillY) <= HotZone)
            {
                double f = c.D / SillR;
                if (f <= 1 && Math.Abs(y - SillY) <= 0.5 + (1 - f * f)) part = Max(part, Part.Sill);
                else part = Max(part, Part.Hot);
            }
            return part;
        }
    }
}
