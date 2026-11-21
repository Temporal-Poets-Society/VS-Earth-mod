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
        /// <summary>Per reach: the river ends in the sea already, with no mouth prolongation (river ends rules 2,
        /// RiverEnds.Result.SeaEnd), null for none.</summary>
        public bool[] SeaEnds { get; init; }
        /// <summary>Seam crossings by the ring's geometry (river end rules) instead of "a piece longer than 4000 blocks".</summary>
        public bool GeometrySeams { get; init; }
        /// <summary>EarthParams.RiverWidths (RiverNetwork.WidthRulesVersion).</summary>
        public int WidthRules { get; init; }
        /// <summary>(EA-0159, decision 0095) EarthParams.RiverWidthMultiplier, the Earth tab's "River width" (×1 = 1:10's widths
        /// in metres): 0 for 2.1's widths (every world made before, and a new one whose multiplier gives exactly those). A streams
        /// layer's networks take the world's.</summary>
        public double WidthMultiplier { get; init; }
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
        /// <summary>(EA-0131) Every reach given is shown, whatever its discharge: a streams layer's network (StreamLayer), whose
        /// streams below StreamMinQ take the smallest stream's width, depth, bank and valley, as the world's network draws its
        /// smallest streams. False for a world's own network.</summary>
        public bool KeepAll { get; init; }
        /// <summary>(EA-0179, decision 0098) Coarse river rules: every stream under MinQ is the smallest stream, one block wide and
        /// shallow (RiverNetwork.StreamT 0), instead of narrowing from MinQ's width to it: rivers from MinQ, one-block streams
        /// from StreamMinQ (EarthParams.CoarseRiverRules, worlds at 1:50 and coarser).</summary>
        public bool OneBlockStreams { get; init; }
        /// <summary>(EA-0179, coarse river rules 2) A reach below a river whose flow drops under MinQ (a losing river, the lower
        /// Darling) is drawn as a river, not a one-block stream: CoarseRivers.Apply.</summary>
        public bool RiversToTheirEnd { get; init; }
        /// <summary>(EA-0179, coarse river rules 2) Above each river whose biggest inflow is not drawn, that inflow's own upper
        /// main branch is a one-block stream for this many km of the data (0: none), so no river starts out of nothing:
        /// CoarseRivers.Apply.</summary>
        public double HeadStreamKm { get; init; }
        /// <summary>(EA-0179, coarse river rules 2) The one-block streams (OneBlockStreams, under MinQ) come down a steep valley
        /// as a streams layer's do (decision 0061): they fall as soon as they lag StreamMaxLag (RiverNetwork.LagOf). The rivers
        /// keep ChuteMaxDrop.</summary>
        public bool SteepStreams { get; init; }
    }
}
