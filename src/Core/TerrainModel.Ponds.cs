using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: channel rules 5 (decision 0074), the pond profile of a reach: how far the mask's river water runs out
    // from its centre line and the river's one width there (EA-0089: moved from TerrainModel.Rivers.cs, no change). The
    // profile and its builder are in PondProfileBuilder.cs; the model keeps one per reach and the constants.
    public sealed partial class TerrainModel
    {
        static readonly PondProfile NoPondProfile = new PondProfile();

        /// <summary>A reach's pond profile (channel rules 5), built once for the whole model by whichever thread asks first:
        /// PondProfileBuilder.Build may read nothing that differs by thread or session (FineElevation.BuiltInOnly, Glo90Decides, a
        /// tile's arrival; ER-01, T-2 of the 14 November review). Only reached where the river outlines decide a column.</summary>
        PondProfile PondProfileOf(int reach)
        {
            if (Rivers == null || RiverOutlines == null || reach < 0) return null;
            var a = reachFacts.Ponds(Rivers.ReachCount);
            if (reach >= a.Length) return null;
            var p = System.Threading.Volatile.Read(ref a[reach]);
            if (p == null)
            {
                p = PondProfileBuilder.Build(new PondProfileBuilder.Layers(Rivers, RiverOutlines, LakeOutlines, Coasts, Geometry), reach) ?? NoPondProfile;
                System.Threading.Volatile.Write(ref a[reach], p);
            }
            return p.Step > 0 ? p : null;
        }

        /// <summary>Channel rules 5: the profile's spacing (blocks); how far a run goes before the water goes on (half widths);
        /// how long a widening must be to stay (the river's widths); the one width's slack (a share of it, and blocks); the
        /// beach along water made land (blocks); how wide land a run crosses to water beyond (half widths, at least
        /// PondIslandMin blocks); the widest river trimmed (m across its water).</summary>
        internal const double PondStep = 2, PondOpen = 6, PondShort = 5, PondSlack = 0.15, PondSlackBlocks = 1, PondBeach = 2,
            PondIsland = 1, PondIslandMin = 4, PondMaxMetres = 450;

        /// <summary>The water mask's pixel (m, GLO-90's 3 arc seconds).</summary>
        internal const double MaskPixelMetres = 90;
    }
}
