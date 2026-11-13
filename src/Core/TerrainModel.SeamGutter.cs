using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: the seam gutter (plain fade, Antarctic bridges, the seam seabed and carried land) and the real seam.
    public sealed partial class TerrainModel
    {
        Column GutterColumn(double x, double z, EarthGeometry.GeoPos g)
        {
            var edges = new SeamEdges(this, z, g);
            if (GutterBridges && TryGutterBridge(edges, out var bridge)) return bridge;
            if (GutterSeabed >= 2 && Geometry.Wraps) return SeamCarriedColumn(edges);
            if (GutterSeabed >= 1 && Geometry.Wraps) return SeamSeabedColumn(edges);
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

        /// <summary>
        /// Saves from 0.13.2 (EarthParams.GutterBridges): where the Earth is land or ice on both sides of the seam gutter
        /// south of GutterBridgeMaxLat, the gutter carries it across instead of cutting a sea channel through it. The
        /// seam (169.25°W in new worlds) crosses the Ross Ice Shelf and the polar plateau; before, the gutter cut a strait about
        /// two kilometres wide from the shelf front to the south pole.
        /// </summary>
        public bool GutterBridges;
        /// <summary>North of this the gutter stays open sea everywhere: the jump line keeps its sea for ships.</summary>
        public const double GutterBridgeMaxLat = -60;

        /// <summary>
        /// Both gutter edges are the same Earth point (the seam meridian at this latitude), so their natural columns differ only
        /// by detail noise. The bridge blends the east edge column (s = 0) into the west one (s = 1) across the gutter, with
        /// detail of its own in the middle (an x of its own, far outside the ring). It depends only on the position across
        /// the gutter and on z, never on the raw x, so both sides of Cylinder World's jump line (one ring apart) match
        /// exactly, and it meets the natural terrain seamlessly at both edges.
        /// </summary>
        bool TryGutterBridge(SeamEdges edges, out Column c)
        {
            c = default;
            var geo = Geometry;
            var g = edges.G;
            if (!geo.Wraps || geo.Gutter <= 0 || g.Lat > GutterBridgeMaxLat) return false;
            var east = edges.East;
            var west = edges.West;
            if (east.Water != WaterNone || west.Water != WaterNone) return false;

            double s = edges.S;
            double across = Geo.SmoothStep(s), own = 4 * s * (1 - s);
            var middle = edges.Middle;
            if (middle.Water != WaterNone) own = 0;
            double Blend(double e, double w, double m) => (1 - own) * (e + (w - e) * across) + own * m;

            int top = (int)Math.Round(Blend(east.Top, west.Top, middle.Top));
            double ice = Blend(TerrainColumns.IceThickness(east), TerrainColumns.IceThickness(west), TerrainColumns.IceThickness(middle));
            c = new Column
            {
                Top = top, WaterTop = -1, IceBottom = ice >= 1 ? Math.Max(1, top - (int)Math.Round(ice) + 1) : int.MaxValue,
                Gutter = true, Lat = g.Lat, Lon = g.Lon, Elev = Blend(east.Elev, west.Elev, middle.Elev), NearLakeTop = -1,
            };
            return true;
        }

        /// <summary>
        /// Saves from GutterSeabed 1 (EarthParams.GutterSeabed): the gutter's sea floor is the Earth's real seabed at the seam,
        /// carried across it, instead of fading to the flat GutterSeabedY. Both gutter edges are the same Earth point (the seam
        /// path at this latitude), so the floor blends the east edge's seabed into the west one's, like TryGutterBridge, with
        /// detail of its own in the middle. It depends only on the position across the gutter and on z, so both sides of
        /// Cylinder World's jump line (one ring apart) match exactly, and Cylinder World can keep it as the seam band's seabed
        /// (it reads this field). Open sea everywhere, at least MinSeaDepth deep: where an edge is land (north of the Antarctic
        /// bridge) its floor counts as that depth.
        /// GutterSeabed 2 (decision 0029): the seam meridian's land is carried across as well (SeamCarriedColumn), so St.
        /// Lawrence Island continues across the jump instead of being split by a sea channel.
        /// </summary>
        public int GutterSeabed;

        Column SeamSeabedColumn(SeamEdges edges)
        {
            var g = edges.G;
            var east = edges.East;
            var west = edges.West;
            double s = edges.S;
            double across = Geo.SmoothStep(s), own = 4 * s * (1 - s);
            var middle = edges.Middle;
            int shallowest = SeaLevel - 1 - Math.Max(1, MinSeaDepth);
            double Floor(in Column c) => c.Water == WaterSea ? Math.Min(c.Top, shallowest) : shallowest;
            double Blend(double e, double w, double m) => (1 - own) * (e + (w - e) * across) + own * m;
            return new Column
            {
                Top = Math.Min(shallowest, (int)Math.Round(Blend(Floor(east), Floor(west), Floor(middle)))),
                WaterTop = SeaLevel - 1, Water = WaterSea, IceBottom = int.MaxValue, Gutter = true,
                Lat = g.Lat, Lon = g.Lon, Elev = Blend(east.Elev, west.Elev, middle.Elev), NearLakeTop = -1,
            };
        }

        /// <summary>
        /// GutterSeabed 2 (decision 0029): the seam meridian's ground carried across the gutter, land included, at every latitude
        /// (north of the Antarctic bridge the seam crosses only St. Lawrence Island). Sea counts with SeamSeabedColumn's floor,
        /// a lake as land at its water surface; the blend is land where it reaches the water surface and sea below it. Where
        /// all three columns are sea this is exactly SeamSeabedColumn. Like it, it depends only on the position across the
        /// gutter and on z, so both sides of Cylinder World's jump line match; Cylinder World keeps the land in its seam band.
        /// </summary>
        Column SeamCarriedColumn(SeamEdges edges)
        {
            var g = edges.G;
            var east = edges.East;
            var west = edges.West;
            double s = edges.S;
            var middle = edges.Middle;
            if (east.Water == WaterSea && west.Water == WaterSea && middle.Water == WaterSea) return SeamSeabedColumn(edges);

            double across = Geo.SmoothStep(s), own = 4 * s * (1 - s);
            int shallowest = SeaLevel - 1 - Math.Max(1, MinSeaDepth);
            double Ground(in Column c) => c.Water == WaterSea ? Math.Min(c.Top, shallowest) : c.Water != WaterNone ? c.WaterTop : c.Top;
            double Blend(double e, double w, double m) => (1 - own) * (e + (w - e) * across) + own * m;

            int top = (int)Math.Round(Blend(Ground(east), Ground(west), Ground(middle)));
            var c = new Column
            {
                Top = top, WaterTop = -1, IceBottom = int.MaxValue, Gutter = true,
                Lat = g.Lat, Lon = g.Lon, Elev = Blend(east.Elev, west.Elev, middle.Elev), NearLakeTop = -1,
            };
            if (top < SeaLevel - 1)
            {
                c.WaterTop = SeaLevel - 1;
                c.Water = WaterSea;
                return c;
            }
            c.Beach = top == SeaLevel - 1;
            double ice = Blend(TerrainColumns.IceThickness(east), TerrainColumns.IceThickness(west), TerrainColumns.IceThickness(middle));
            if (ice >= 1) { c.IceBottom = Math.Max(1, top - (int)Math.Round(ice) + 1); c.Beach = false; }
            return c;
        }

        /// <summary>Is this gutter position land (see TryGutterBridge and SeamCarriedColumn)? For the ocean map.</summary>
        public bool GutterBridgedAt(double z, EarthGeometry.GeoPos g)
        {
            if (!g.InGutter) return false;
            var edges = new SeamEdges(this, z, g);
            return GutterBridges && TryGutterBridge(edges, out _) || GutterSeabed >= 2 && Geometry.Wraps && SeamCarriedColumn(edges).Water == WaterNone;
        }

        /// <summary>
        /// The seam gutter's three natural columns at a position across it, the two edges (the same Earth point, the seam
        /// meridian at this latitude) and the middle's own detail (an x of its own far outside the ring), and where the
        /// position lies across it (S: 0 at the east edge, 0.5 at the seam, the jump line, 1 at the west edge). Each column is
        /// made once, when first asked: the bridge, the carried land and the seam seabed all blend the same three (before,
        /// each made its own, up to 8 natural columns per gutter column).
        /// </summary>
        sealed class SeamEdges
        {
            readonly TerrainModel model;
            readonly double z;
            public readonly EarthGeometry.GeoPos G;
            Column east, west, middle;
            bool sides, centre;

            public SeamEdges(TerrainModel model, double z, EarthGeometry.GeoPos g) { this.model = model; this.z = z; G = g; }

            public double S { get { var geo = model.Geometry; return Math.Clamp(G.U >= 0.5 ? G.GutterDepth / geo.Gutter : 1 - G.GutterDepth / geo.Gutter, 0, 1); } }
            public Column East { get { Sides(); return east; } }
            public Column West { get { Sides(); return west; } }

            public Column Middle
            {
                get
                {
                    if (!centre)
                    {
                        var geo = model.Geometry;
                        middle = model.NaturalColumn(geo.RingStart + geo.RingLength + 8192 + S * geo.Gutter, z, G.Lat, G.Lon);
                        centre = true;
                    }
                    return middle;
                }
            }

            void Sides()
            {
                if (sides) return;
                var geo = model.Geometry;
                double half = geo.Gutter / 2;
                east = model.NaturalColumn(geo.RingStart + geo.RingLength - half, z, G.Lat, G.Lon);
                west = model.NaturalColumn(geo.RingStart + half, z, G.Lat, G.Lon);
                sides = true;
            }
        }

        /// <summary>GutterSeabed from which the world has no gutter and the real Earth runs across the jump line (decision 0030).</summary>
        public const int RealSeamVersion = 3;
        /// <summary>Blocks on each side of the jump line over which the detail noise blends into the other lap's (RealSeam).</summary>
        public const double SeamNoiseBlend = 512;
        /// <summary>RealSeam: terrain expansions stay this far from the jump line, out of Cylinder World's identical band.</summary>
        public const double RealSeamKeepOut = 1280;

        /// <summary>
        /// GutterSeabed 3 (decision 0030): no gutter; the real Earth runs across the jump line. Columns are taken at x wrapped
        /// into the ring, so both laps (one jump apart) are the same and Cylinder World's identical band holds Earth's own
        /// terrain; the data (elevation, coasts, islands) already repeats with longitude, and the detail noise is made to
        /// repeat by SeamNoise.
        /// </summary>
        public bool RealSeam => GutterSeabed >= RealSeamVersion && Geometry.Wraps;
    }
}
