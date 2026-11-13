using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>A disc of terrain forced to one height (story locations), blended into the natural terrain around it.</summary>
    public sealed class LegacyFlatZone
    {
        public string Code;
        public double X, Z, Radius, Blend;
        public int TargetTop;
    }

    /// <summary>
    /// The terrain of one block column, from the Earth data: real elevation (bicubic from the 2' grid) plus detail
    /// noise scaled by the local relief, mapped to block heights by the vertical scale. Decides sea, lakes, dry
    /// land and ice sheets. Stateless apart from its inputs, so it is safe to use from any worldgen thread.
    /// </summary>
    public sealed class LegacyTerrainModel
    {
        public readonly EarthData Data;
        public readonly EarthGeometry Geometry;
        public readonly VerticalScale Vertical;
        public int GutterSeabedY;
        public double GutterBlend = 512;
        public double DetailScale = 1.0;
        /// <summary>Land up to this height (m) above a sea or lake next to it is flush with the water surface: beaches.</summary>
        public double BeachHeight = 3;
        public List<LegacyFlatZone> LegacyFlatZones = new List<LegacyFlatZone>();
        readonly object reservationLock = new object();
        readonly HashSet<string> reservationOwners = new HashSet<string>(StringComparer.Ordinal);
        volatile TerrainReservationIndex reservations = TerrainReservationIndex.Empty;
        bool reservationsSealed;

        /// <summary>Publish immutable companion site pads during EarthWorldGen.TerrainPlanning. No updates
        /// are permitted once planning closes. A default Earth world has no reservations and is unchanged.</summary>
        public void RegisterTerrainReservations(string owner, IEnumerable<TerrainReservation> sites)
        {
            if (string.IsNullOrWhiteSpace(owner) || sites == null) throw new ArgumentException("Owner and sites are required.");
            lock (reservationLock)
            {
                if (reservationsSealed) throw new InvalidOperationException("Earth terrain planning has closed.");
                if (reservationOwners.Contains(owner)) throw new InvalidOperationException("Owner already published Earth reservations: " + owner);
                var all = new List<TerrainReservation>(reservations.All);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var site in sites)
                {
                    if (site == null || !ids.Add(site.Id) || site.TargetTop >= Vertical.MapHeight - 1)
                        throw new ArgumentException("Invalid or duplicate Earth reservation.");
                    if (IntersectsStoryTerrain(site.MinX - site.Blend, site.MinZ - site.Blend, site.MaxX + site.Blend, site.MaxZ + site.Blend))
                        throw new ArgumentException("Earth reservation intersects protected story terrain: " + site.Id);
                    foreach (var old in all)
                        if (old.Intersects(site.MinX - site.Blend, site.MinZ - site.Blend, site.MaxX + site.Blend, site.MaxZ + site.Blend))
                            throw new ArgumentException("Earth reservations overlap: " + site.Id + " / " + old.Id);
                    all.Add(site);
                }
                reservations = new TerrainReservationIndex(all);
                reservationOwners.Add(owner);
            }
        }

        public void SealTerrainReservations() { lock (reservationLock) reservationsSealed = true; }
        public bool HasTerrainReservations => reservations.All.Length > 0;
        public bool IntersectsReservedTerrain(double minX, double minZ, double maxX, double maxZ)
            => reservations.Intersects(minX, minZ, maxX, maxZ);

        public bool IntersectsStoryTerrain(double minX, double minZ, double maxX, double maxZ)
        {
            foreach (var zone in LegacyFlatZones)
            {
                double x = Math.Clamp(zone.X, minX, maxX), z = Math.Clamp(zone.Z, minZ, maxZ);
                double dx = zone.X - x, dz = zone.Z - z, r = zone.Radius + zone.Blend;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }
        /// <summary>Large rivers carved into the terrain, or null.</summary>
        public RiverNetwork Rivers;
        /// <summary>
        /// Share of the land's vertical relief the river surface follows (1 = the real level everywhere). Below 1, rivers
        /// sit lower in their valleys and their surface steps down less often (better for boats).
        /// </summary>
        public double RiverRelief = 1.0;

        readonly Simplex2 n1, n2, n3;

        public LegacyTerrainModel(EarthData data, EarthGeometry geometry, VerticalScale vertical, long seed)
        {
            Data = data;
            Geometry = geometry;
            Vertical = vertical;
            GutterSeabedY = vertical.SeaLevel - 40;
            n1 = new Simplex2(seed + 101);
            n2 = new Simplex2(seed + 202);
            n3 = new Simplex2(seed + 303);
        }

        public int SeaLevel => Vertical.SeaLevel;

        public struct Column
        {
            /// <summary>Top solid block (rock or glacier ice).</summary>
            public int Top;
            /// <summary>Top water block, -1 for none.</summary>
            public int WaterTop;
            /// <summary>0 none, 1 sea, 2 lake, 3 salt lake.</summary>
            public byte Water;
            /// <summary>Lowest glacier-ice block (up to Top), int.MaxValue for none.</summary>
            public int IceBottom;
            /// <summary>Real elevation (m) after the detail noise; the ice surface on ice sheets.</summary>
            public double Elev;
            public bool Gutter;
            /// <summary>Land at the water's edge, flush with the water surface (sand or gravel goes here).</summary>
            public bool Beach;
            /// <summary>River water (fresh), with the flow direction.</summary>
            public bool River;
            public float FlowX, FlowZ;
            /// <summary>0, or 1..6: flowing water of that height on top of the pool (the ramp just below a step down).</summary>
            public byte Ramp;
            /// <summary>The ramp is part of rapids (rapid water).</summary>
            public bool Rapid;
            /// <summary>0, or 2+: a neighbour's water stands this much higher: a curtain of falling water up to it.</summary>
            public byte Fall;
            /// <summary>Within a river's water, bank or valley.</summary>
            public bool NearRiver;
            /// <summary>Water top of a lake within one data cell, -1 if none (rivers join it at the same level).</summary>
            public int NearLakeTop;
            public double Lat, Lon;
        }

        public const byte WaterNone = 0, WaterSea = 1, WaterLake = 2, WaterSaltLake = 3;

        /// <summary>Top water block of a sea (level 0) or lake at the given level (m).</summary>
        public int WaterTopY(double level)
        {
            if (level >= 0) return (int)Math.Floor(Vertical.Y(level)) - 1;
            return (int)Math.Floor(Vertical.Y(level));
        }

        public Column GetColumn(double x, double z)
        {
            Column c = GetColumnWithoutReservations(x, z);
            if (c.Gutter || c.Water != WaterNone || c.River || c.NearRiver || c.Beach || c.IceBottom <= c.Top) return c;
            foreach (var site in reservations.At(x, z))
            {
                double w = site.Weight(x, z);
                if (w <= 0) continue;
                int delta = Math.Clamp(site.TargetTop - c.Top, -site.MaxCutFill, site.MaxCutFill);
                c.Top += (int)Math.Round(delta * w);
            }
            return c;
        }

        /// <summary>The original Earth/story surface, without companion site pads, for deterministic planning.</summary>
        public Column GetColumnWithoutReservations(double x, double z)
        {
            var g = Geometry.ToGeo(x, z);
            Column c = g.InGutter ? GutterColumn(x, z, g) : NaturalColumn(x, z, g.Lat, g.Lon);
            if (Rivers != null && !c.Gutter) ApplyRiver(x, z, ref c);
            if (LegacyFlatZones.Count > 0) ApplyLegacyFlatZones(x, z, ref c);
            return c;
        }

        Column GutterColumn(double x, double z, EarthGeometry.GeoPos g)
        {
            var edge = NaturalColumn(x, z, g.Lat, g.Lon);
            double t = Geo.SmoothStep(g.GutterDepth / Math.Max(1, Math.Min(GutterBlend, Geometry.Gutter / 2)));
            double edgeTop = edge.Water != WaterNone ? Math.Min(edge.Top, SeaLevel - 2) : edge.Top;
            int top = (int)Math.Round(edgeTop + (GutterSeabedY - edgeTop) * t);
            var c = new Column { Top = top, WaterTop = -1, IceBottom = int.MaxValue, Gutter = true, Lat = g.Lat, Lon = g.Lon, Elev = edge.Elev };
            if (top < SeaLevel)
            {
                c.Top = Math.Min(top, SeaLevel - 2);
                c.WaterTop = SeaLevel - 1;
                c.Water = WaterSea;
            }
            return c;
        }

        /// <summary>Detail noise (m) for a column: gentle undulation everywhere, ridges and valleys where the land is rugged.</summary>
        double Detail(double x, double z, in EarthData.TerrainSample s)
        {
            double relief = Math.Min(s.Relief, 2500);
            double mountain = Geo.SmoothStep((relief - 150) / 1200);
            double baseAmp = 1.5 + 0.03 * Math.Max(0, s.Elev);
            double d = baseAmp * n2.Fbm(x, z, 56, 3);
            d += relief * 0.22 * n1.Fbm(x, z, 150, 4);
            if (mountain > 0) d += mountain * relief * 0.4 * (n3.Ridged(x, z, 260, 4) - 0.45);
            return d * DetailScale;
        }

        Column NaturalColumn(double x, double z, double lat, double lon)
        {
            var s = Data.SampleTerrain(lat, lon);
            double detail = s.Ocean >= 0.999 && s.Elev < -200 ? 0 : Detail(x, z, s);
            // near a sea or lake the noise may raise the land but not dip it down to the water: on flat plains its dips
            // (±10 m on a 700 m plateau) put ground below a lake's level just outside the lake's data cells, and the
            // lake ended along the cell edges (straight shores)
            double h = s.Elev + detail;
            if (!double.IsNaN(s.ShoreLevel) && s.Elev > s.ShoreLevel)
            {
                double above = s.Elev - s.ShoreLevel;
                double wet = Math.Max(s.Ocean, double.IsNaN(s.LakeLevel) ? 0 : s.Lake);
                // Land just above a nearby water level: the noise may dip it into the water only well inside the water's
                // data cells (irregular shores), fading out towards their edge. Outside them nothing knows about the
                // water, and a dip there made a wall or a shore running straight along the cells (south of Kilimanjaro).
                if (detail < 0 && above < 30) h = s.Elev + detail * Geo.SmoothStep(wet / 0.35);
                // High ground (a fjord's walls) and anything outside the water's cells never dips below the water: that
                // made pockets of sea up on the slopes.
                if (above >= 30 || wet < 0.05) h = Math.Max(h, Math.Min(s.Elev, s.ShoreLevel + 0.5));
            }
            if (!double.IsNaN(s.ShoreLevel))
            {
                // Right at the water line, well inside the water's cells: a small, fine noise so flat shores don't run
                // straight along the data grid (the large-scale noise only shifts them as a whole)
                double wet = Math.Max(s.Ocean, double.IsNaN(s.LakeLevel) ? 0 : s.Lake);
                double near = 1 - Geo.SmoothStep(Math.Abs(h - s.ShoreLevel) / 8);
                if (near > 0 && wet > 0) h += 3.5 * n1.Fbm(x, z, 14, 2) * near * Geo.SmoothStep(wet / 0.35);
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

            // A lake that meets the sea within one data cell (coastal lakes behind a dune strip narrower than the grid):
            // keep a strip of land between them instead of letting fresh and salt water (maybe at different heights) touch.
            bool barrier = false;
            if (o > 0 && l > 0)
            {
                if (Math.Abs(o - l) < 0.3) barrier = true;
                else if (o > l) l = 0;
                else o = 0;
            }

            if (!barrier && o > 0 && h < 0)
            {
                // deep enough for ships away from the shore (Neva Bay, the Gulf of Riga: a few metres are 1-2 blocks)
                int minDepth = 1 + (int)Math.Round(4 * Geo.SmoothStep((o - 0.5) / 0.5));
                c.Top = Math.Min(top, sea - 1 - minDepth);
                c.WaterTop = sea - 1;
                c.Water = WaterSea;
                return c;
            }
            if (!barrier && l > 0 && h < s.LakeLevel)
            {
                // likewise lakes: high up a block is 20 m or more, so Lake Victoria was only 1-2 blocks deep
                int minDepth = 1 + (int)Math.Round(3 * Geo.SmoothStep((l - 0.5) / 0.5));
                c.Top = Math.Min(top, lakeTop - minDepth);
                c.WaterTop = lakeTop;
                c.Water = s.SaltLake ? WaterSaltLake : WaterLake;
                return c;
            }

            // dry land: never below a nearby water surface; at the water's edge flush with it (beach)
            if (!double.IsNaN(s.ShoreLevel)) top = Math.Max(top, WaterTopY(s.ShoreLevel));
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
            else if (l > 0 && h < s.LakeLevel + BeachHeight * Geo.SmoothStep(l / 0.35))
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
            if (!Rivers.Nearest(x, z, out var hit)) return;
            double hw = Rivers.HalfWidth(hit.Q), bank = Rivers.Bank(hit.Q), valley = Rivers.Valley(hit.Q);
            if (c.Water != WaterNone)
            {
                // a lake or the sea at the river's level (an estuary, a lake it flows through): keep the channel's depth
                // through the shallows
                if (hit.Dist <= hw && hit.WaterTop == c.WaterTop)
                {
                    double tt = hit.Dist / hw;
                    double dd = Rivers.Depth(hit.Q), de = Math.Min(2, dd);
                    int bedTop = c.WaterTop - (int)Math.Round(de + (dd - de) * Geo.SmoothStep((1 - tt) / 0.4));
                    if (bedTop < c.Top) c.Top = bedTop;
                }
                return;
            }
            // no rivers on ice sheets (HydroRIVERS draws flow lines across Greenland's ice; they begin at its edge)
            if (c.IceBottom <= c.Top) return;
            // the prolongation of a river mouth only cuts through low ground (beaches, bars, flats), not headlands
            if (hit.Mouth && (c.Elev > 8 || hit.Dist > hw + bank)) return;
            c.NearRiver = true;
            int wt = hit.WaterTop, ramp = hit.Ramp;
            // next to a lake the river is at the lake's level (their levels come from different data and round differently)
            if (c.NearLakeTop >= 0 && Math.Abs(c.NearLakeTop - wt) <= 3 && c.NearLakeTop != wt)
            {
                wt = c.NearLakeTop;
                ramp = 0;
            }
            if (wt >= Vertical.MapHeight - 4 || wt < 2) return;
            int bankTop = wt + (ramp > 0 ? 1 : 0);
            if (hit.Dist <= hw)
            {
                double t = hit.Dist / hw;
                double depth = Rivers.Depth(hit.Q), edge = Math.Min(2, depth);
                c.Top = wt - (int)Math.Max(1, Math.Round(edge + (depth - edge) * Geo.SmoothStep((1 - t) / 0.4)));
                c.WaterTop = wt;
                c.Water = WaterLake;
                c.River = true;
                c.FlowX = (float)hit.DirX;
                c.FlowZ = (float)hit.DirZ;
                c.Ramp = (byte)ramp;
                c.Rapid = hit.Rapid;
                c.IceBottom = int.MaxValue;
                c.Beach = false;
                return;
            }
            if (hit.Dist <= hw + bank)
            {
                c.Top = bankTop;
                c.IceBottom = int.MaxValue;
                c.Beach = hit.Dist <= hw + 2;
                return;
            }
            double f = Geo.SmoothStep((hit.Dist - hw - bank) / valley);
            int floorTop = bankTop + 1;
            if (c.Top > floorTop) c.Top = (int)Math.Round(floorTop + (c.Top - floorTop) * f);
            else if (c.Top < wt && f < 0.25) c.Top = wt;
            if (c.IceBottom <= c.Top && f < 1) c.IceBottom = int.MaxValue;
        }

        /// <summary>
        /// Settles the water of an area of columns (row-major, w x h) the way VS's own liquid spreading would: where a
        /// column's fresh water is a block lower than a neighbour's, flowing water spreads over it from there (height 6,
        /// one less per block), and dry land right next to river water is raised to hold it in. This fills in the few
        /// steps the river layout leaves without a ramp (inner edges of tight bends, meander loops that touch, tributary
        /// mouths, rivers meeting lakes). Columns near the edges of the area may miss sources outside it, so callers pass
        /// a margin of at least 8 columns around the ones they use.
        /// </summary>
        public static void SettleSteps(Column[] cols, int w, int h)
        {
            var level = new int[w * h];
            var from = new int[w * h];
            var fall = new int[w * h];
            Span<int> nb = stackalloc int[4];
            int Neighbours(int k, Span<int> o)
            {
                int x = k % w, z = k / w, n = 0;
                if (x > 0) o[n++] = k - 1;
                if (x < w - 1) o[n++] = k + 1;
                if (z > 0) o[n++] = k - w;
                if (z < h - 1) o[n++] = k + w;
                return n;
            }
            for (int k = 0; k < cols.Length; k++)
            {
                ref var c = ref cols[k];
                if (c.Water != WaterLake || c.WaterTop < 0) continue;
                int n = Neighbours(k, nb);
                for (int i = 0; i < n; i++)
                {
                    ref var d = ref cols[nb[i]];
                    if (d.Water == WaterNone || d.WaterTop <= c.WaterTop) continue;
                    // 2 or more blocks higher: a waterfall (a lake above a fall, rivers touching at different levels),
                    // also over a cliff where the upper water's bed lies above this pool
                    if (d.WaterTop - c.WaterTop >= 2)
                    {
                        fall[k] = Math.Max(fall[k], d.WaterTop - c.WaterTop);
                        level[k] = 6;
                        from[k] = nb[i];
                        continue;
                    }
                    // water in the neighbour right above this column's water surface
                    if (d.Top > c.WaterTop) continue;
                    level[k] = 6;
                    from[k] = nb[i];
                }
            }
            for (int l = 6; l > 1; l--)
            {
                for (int k = 0; k < cols.Length; k++)
                {
                    if (level[k] != l) continue;
                    int n = Neighbours(k, nb);
                    for (int i = 0; i < n; i++)
                    {
                        int m = nb[i];
                        if (cols[m].Water != WaterLake || cols[m].WaterTop != cols[k].WaterTop || level[m] >= l - 1) continue;
                        level[m] = l - 1;
                        from[m] = k;
                    }
                }
            }
            for (int k = 0; k < cols.Length; k++)
            {
                if (fall[k] > 0) cols[k].Fall = (byte)Math.Min(fall[k], 250);
                if (level[k] <= cols[k].Ramp) continue;
                ref var c = ref cols[k];
                c.Ramp = (byte)level[k];
                if (!c.River || (c.FlowX == 0 && c.FlowZ == 0))
                {
                    int f = from[k];
                    c.FlowX = k % w - f % w;
                    c.FlowZ = k / w - f / w;
                }
            }
            // banks: dry land next to river water is at least as high as its surface (one more beside a ramp)
            for (int k = 0; k < cols.Length; k++)
            {
                ref var c = ref cols[k];
                if (c.Water != WaterNone) continue;
                int need = int.MinValue;
                int n = Neighbours(k, nb);
                for (int i = 0; i < n; i++)
                {
                    ref var d = ref cols[nb[i]];
                    if (d.Water == WaterNone || (!d.River && d.Ramp == 0)) continue;
                    need = Math.Max(need, d.WaterTop + (d.Ramp > 0 ? 1 : 0));
                }
                if (need > c.Top) c.Top = need;
            }
        }

        void ApplyLegacyFlatZones(double x, double z, ref Column c)
        {
            foreach (var zone in LegacyFlatZones)
            {
                double dx = x - zone.X, dz = z - zone.Z;
                if (Geometry.Wraps)
                {
                    double L = Geometry.RingLength;
                    dx = ((dx % L) + L + L / 2) % L - L / 2;
                }
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= zone.Radius + zone.Blend) continue;
                // never over a river (sites keep clear of them; this protects worlds made before they did)
                if (c.River) continue;
                double w = d <= zone.Radius ? 1 : 1 - Geo.SmoothStep((d - zone.Radius) / zone.Blend);
                int natural = c.Top;
                int top = (int)Math.Round(natural + (zone.TargetTop - natural) * w);
                if (c.Water != WaterNone && w < 0.5)
                {
                    // a lake or the sea in the blend stays water (only its bed follows the site a little); before, any
                    // water above sea level there was drained, even where the site barely changed the ground
                    c.Top = Math.Min(top, c.WaterTop - 1);
                    continue;
                }
                if (c.Water != WaterNone)
                {
                    // inside the site: filled in, at least up to sea level
                    top = Math.Max(top, SeaLevel);
                    c.Water = WaterNone;
                    c.WaterTop = -1;
                }
                c.Top = top;
                c.IceBottom = int.MaxValue;
            }
        }

        // ------------------------------------------------------------------------------------------ climate

        /// <summary>Surface for climate purposes (no detail noise): block height, elevation (m), whether it is water.</summary>
        public void Surface(double lat, double lon, out double y, out double elev, out bool water)
        {
            var s = Data.SampleTerrain(lat, lon);
            if (s.Ocean >= 0.5)
            {
                water = true;
                elev = 0;
                y = SeaLevel;
                return;
            }
            if (s.Lake >= 0.5 && !double.IsNaN(s.LakeLevel))
            {
                water = true;
                elev = s.LakeLevel;
                y = WaterTopY(s.LakeLevel) + 1;
                return;
            }
            water = false;
            elev = s.Elev;
            y = Math.Max(Vertical.Y(elev), double.IsNaN(s.ShoreLevel) ? double.MinValue : WaterTopY(s.ShoreLevel) + 1);
        }

        /// <summary>
        /// The pixel for Vintage Story's climate map: (temperature &lt;&lt; 16) | (rain &lt;&lt; 8) | geologic activity.
        /// Real surface temperature and rainfall, stored so that vanilla's own altitude adjustments (−1 °C per 6.375
        /// blocks, more rain higher up and near sea level) give back the real values at the surface.
        /// </summary>
        public int ClimatePixel(double x, double z, int geologic, float tempMul, float rainMul)
        {
            var g = Geometry.ToGeo(x, z);
            ClimateAt(g.Lat, g.Lon, out double tReal, out double rainReal, out double y);
            rainReal = RiparianRain(x, z, rainReal);
            double tStore = tReal + (y - SeaLevel) / 6.375;
            double rainStore = rainReal - (y - SeaLevel) / 2.0 - 5 * Math.Clamp(8 + SeaLevel - y, 0, 8);
            int temp = Math.Clamp((int)(Math.Clamp((tStore + 20) * 4.25, 0, 255) * tempMul), 0, 255);
            int rain = Math.Clamp((int)Math.Round(rainStore * rainMul), 0, 255);
            return (temp << 16) | (rain << 8) | (geologic & 0xFF);
        }

        /// <summary>Real yearly mean surface temperature (°C), rainfall (VS 0..255) and surface block height at a place.</summary>
        public void ClimateAt(double lat, double lon, out double tReal, out double rain, out double surfaceY)
        {
            var cs = Data.SampleClimate(lat, lon);
            Surface(lat, lon, out surfaceY, out double elev, out _);
            tReal = cs.TMean - 0.0065 * elev;
            rain = cs.Rain;
        }

        /// <summary>
        /// Rivers through dry land get a green belt: the climate map's rainfall is raised near them (an oasis strip along
        /// the Nile, the Euphrates, the Indus), easing out through steppe into the desert. Only where the land is drier
        /// than VS rainfall 140.
        /// </summary>
        public double RiparianRain(double x, double z, double rain) => rain + (145 - rain) * RiparianWeight(x, z, rain);

        /// <summary>0..1: how much of the river's green belt this dry column is in.</summary>
        public double RiparianWeight(double x, double z, double rain)
        {
            if (Rivers == null || rain >= 140 || !Rivers.Nearest(x, z, out var hit)) return 0;
            double hw = Rivers.HalfWidth(hit.Q);
            double belt = 20 + 30 * Rivers.StreamT(hit.Q) + 6 * hw;
            return 1 - Geo.SmoothStep((hit.Dist - hw - 8) / belt);
        }

        /// <summary>
        /// 0..1: how much forest the real climate supports here. Trees need a warmest month above ~10 °C (the tree line,
        /// both polar and alpine) and enough moisture (VS rainfall from the aridity).
        /// </summary>
        public double ForestPotential(double x, double z)
        {
            var g = Geometry.ToGeo(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            double elev = Math.Max(0, Data.ElevBilinear(g.Lat, g.Lon));
            double warm = cs.TWarm - 0.0065 * elev;
            double moist = Geo.SmoothStep((cs.Rain - 60) / 80);
            // scattered trees in the green belt of a river through dry land, not a forest
            if (moist < 0.3) moist = Math.Max(moist, 0.3 * RiparianWeight(x, z, cs.Rain));
            return moist * Geo.SmoothStep((warm - 8) / 5);
        }

        /// <summary>
        /// 0..1: how many shrubs the real climate supports here (none in the desert, some in the steppe, full where it's
        /// moist), using the rainfall with the rivers' green belts.
        /// </summary>
        public double ShrubPotential(double x, double z)
        {
            var g = Geometry.ToGeo(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            double rain = RiparianRain(x, z, cs.Rain);
            return Geo.SmoothStep((rain - 40) / 60);
        }

        /// <summary>Real yearly mean temperature (°C) at a block, using the real elevation that block height stands for.</summary>
        public double TrueTemperature(double x, double z, double y)
        {
            var g = Geometry.ToGeo(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            return cs.TMean - 0.0065 * Vertical.Elevation(y);
        }
    }
}
