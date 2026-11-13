using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Everything a <see cref="RiverNetwork"/> is built with besides the courses, the ring and the water-top function: the
    /// world's saved river settings and rules numbers in one record (EarthParams.BuildRivers fills it from the save). The
    /// defaults are those of the older positional constructor, so a record with only the two required values builds what
    /// that constructor built with only those. Each rules number keeps its meaning for every save that stored it.
    /// </summary>
    public sealed record RiverBuildOptions(double MinQ, double WidthScale)
    {
        /// <summary>Streams: "ends" (each river continued upstream along its main branch), "all" or "none".</summary>
        public string StreamMode { get; init; } = "none";
        /// <summary>Smallest stream (m³/s), one block wide.</summary>
        public double StreamMinQ { get; init; } = 20;
        /// <summary>World size relative to the standard 256k world (Cylinder World ring 204 800).</summary>
        public double SizeScale { get; init; } = 1;
        /// <summary>0..1: how much the courses are straightened for the widths (loop cut-offs, smoothing).</summary>
        public double Straighten { get; init; }
        /// <summary>Douglas-Peucker smoothing in river widths (worlds before GLO-90 rivers).</summary>
        public double SmoothWidths { get; init; }
        /// <summary>The least pool length (blocks) in the standard world; scaled with SizeScale.</summary>
        public int MinPool { get; init; } = 32;
        /// <summary>The waterfalls to place (RiverFalls.All), null for none.</summary>
        public IReadOnlyList<RiverFall> Falls { get; init; }
        /// <summary>Whether a piece (x1, z1, x2, z2) crosses sea-level water cut through the land (canals and their lakes).</summary>
        public Func<double, double, double, double, bool> SeaLevelPiece { get; init; }
        /// <summary>Also keep everything below a kept reach (river end rules).</summary>
        public bool ContinueDownstream { get; init; }
        /// <summary>Per reach: the river ends in a dry basin and fades to a stream (RiverEnds), null for none.</summary>
        public bool[] FadeEnds { get; init; }
        /// <summary>Seam crossings by the ring's geometry (river end rules) instead of "a piece longer than 4000 blocks".</summary>
        public bool GeometrySeams { get; init; }
        /// <summary>EarthParams.RiverWidths (RiverNetwork.WidthRulesVersion).</summary>
        public int WidthRules { get; init; }
        /// <summary>EarthParams.RiverPoolLayout (RiverNetwork.PoolLayoutVersion).</summary>
        public int PoolLayout { get; init; }
        /// <summary>Whether the world has rapids reaches at all (the world option earthRiverRapids, pool layout 11).</summary>
        public bool Rapids { get; init; } = true;
        /// <summary>EarthParams.RiverCourses (RiverNetwork.Glo90CoursesVersion).</summary>
        public int Courses { get; init; }
        /// <summary>The land heights' cell (m) under GLO-90 rivers, 0 for the size-scaled banks and valleys.</summary>
        public double LandCellMetres { get; init; }
        /// <summary>EarthParams.ChannelRules (TerrainModel.ChannelRulesVersion).</summary>
        public int ChannelRules { get; init; }
    }
}
