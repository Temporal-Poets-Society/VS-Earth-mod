using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0070 (EarthParams.ShoreRules): the sea's and big lakes' shores are never soil to the water. Before, a shore
    /// column was a beach only where its height was under BeachHeight (3 m), so wherever GLO-90 reads the shore higher
    /// (the dune and pines behind Jūrmala's sand: 9 m in a 90 m cell) the topsoil and grass ran into the sea, and every
    /// beach was the same sand. Now a strip of real width along the shore is a beach at any height
    /// (<see cref="TerrainModel.Column.Shore"/>), and its material is the coast's own: what OpenStreetMap's beaches say
    /// (<see cref="ShoreMaterialData"/>), else a rule from the ground (GroundColumns.ShoreSubstrate): wind-blown and coastal
    /// sand, gravel and stones on glacial till, bare rock on bedrock and cliffs, mud on marshes and tidal flats.
    /// </summary>
    public static class ShoreMaterial
    {
        /// <summary>The newest shore rules, which new worlds record (EarthParams.ShoreRules). 2: the beach at the water's
        /// level and the shallows of its material (decision 0070, shore rules 2). 3 (EA-0056, 2.0.0-rc.11): thin glacial till
        /// at the shore is bare rock where OSM maps no beach (GroundColumns.ShoreSubstrate): Bergen's coast is rock, not sand
        /// and gravel in patches.</summary>
        public const int RulesVersion = 3;
        /// <summary>Column.Shore: not a shore strip; OSM's sand, gravel (pebbles, shingle), sand and gravel, rock, mud; an OSM
        /// beach without a surface; no OSM beach (the rule decides in both).</summary>
        public const byte None = 0, Sand = 1, Gravel = 2, Mixed = 3, Rock = 4, Mud = 5, Beach = 6, Rule = 7;

        /// <summary>The strip's width at the sea (m) by what OSM says: a sandy beach's dry part (20-60 m on the Baltic, the
        /// research note docs/research/shore-zones-2026-11-12.md), a shingle beach's, a rocky shore's splash zone, a tidal
        /// flat's upper marsh edge; varied ±<see cref="WidthVary"/> along the coast.</summary>
        public static double WidthMetres(byte cls) => cls switch
        {
            Sand => 40, Gravel => 20, Mixed => 30, Rock => 8, Mud => 30, Beach => 35, _ => 30,
        };
        public const double WidthVary = 0.35;
        /// <summary>At a lake: this share of the sea's width (no surf, smaller storms).</summary>
        public const double LakeShare = 0.5;
        /// <summary>Lakes smaller than this (km²) keep their shores as they are (eased shores, sedge zones). The lake
        /// outlines give the lake's part in one tile, so a big lake's piece can be a few km² (Vesijärvi at Lahti: 5.3).</summary>
        public const double LakeMinKm2 = 1;

        /// <summary>The OSM surface tag's class (lower case; null or empty: <see cref="Beach"/>).</summary>
        public static byte OfSurface(string surface, bool shingle)
        {
            if (string.IsNullOrEmpty(surface)) return shingle ? Gravel : Beach;
            bool sand = false, gravel = false, rock = false, mud = false;
            foreach (var raw in surface.Split(';'))
            {
                switch (raw.Trim())
                {
                    case "sand": case "fine_sand": case "coarse_sand": sand = true; break;
                    case "gravel": case "fine_gravel": case "pebblestone": case "pebbles": case "shingle": case "shells": case "stones": gravel = true; break;
                    case "rock": case "stone": case "bare_rock": case "boulders": case "rocks": rock = true; break;
                    case "mud": case "clay": case "dirt": case "silt": mud = true; break;
                }
            }
            if (sand && (gravel || rock)) return Mixed;
            if (sand) return Sand;
            if (gravel) return rock ? Mixed : Gravel;
            if (rock) return Rock;
            if (mud) return Mud;
            return shingle ? Gravel : Beach;
        }
    }
}
