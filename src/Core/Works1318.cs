namespace EarthMap.Core
{
    /// <summary>
    /// earthdata/works-1318-2.bin (tools/bake_works_1318.py; decision 0064): where a new world's coasts and waters go back to 1318,
    /// as areas in the lake outlines' layout (LakeOutlineData) under its own magic, each with a kind in its flags and a level
    /// (m) for lakes. TerrainModel.Works reads it with a LakeOutlineLayer: SEA areas are sea over today's land (the coast's
    /// signed distance takes them in, so the ocean map and the river exits see them too), LAKE areas are lakes at their
    /// level, and LAND areas are land over today's lakes, rivers and grid sea. Natural change after 1318 is not in it.
    /// Step 1: the Netherlands from the RCE paleogeographic maps of 1250, 1500 and 2000 AD; then the canals (0069 rule 6) and
    /// works rules 4's areas (<see cref="Late"/>).
    /// </summary>
    public static class Works1318Data
    {
        /// <summary>The newest rules, which new worlds record (EarthParams.Works1318); 0: today's coasts and waters. 2 (decision
        /// 0069 rule 6): and the canals dug after 1318 (the areas with <see cref="Canal"/>), which rules 1 worlds skip. 3 (EA-0042,
        /// 2.0.0-rc.10): the sea beside and over a SEA area measures its coast to the land of 1318, not to today's land inside
        /// the area (TerrainModel.CoastDistanceAt): no shallow ridges along Rotterdam's moles in the sea of 1318. 4 (EA-0053,
        /// EA-0057, 2.0.0-rc.11): and the areas file version 3 adds (<see cref="Late"/>): Maasvlakte 2 (2008-2013) is sea, and
        /// the Corinth, White Sea-Baltic and Ismailia canals, which OpenStreetMap names otherwise, are land.</summary>
        public const int RulesVersion = 4;
        /// <summary>The file in the mod (earthdata), version 3: version 2's areas byte for byte and in their order, then works
        /// rules 4's with <see cref="Late"/>. works-1318.bin (versions 1 and 2) is gone: this one serves every world, each
        /// skipping what its rules lack (<see cref="SkipFor"/>). A later bake that moves areas a saved world has needs a new
        /// version and name again.</summary>
        public const string FileName = "works-1318-2.bin";
        public const string Magic = "EMWK";
        /// <summary>The area kinds (flags): sea in 1318 over today's land, a lake at its level, land over today's water,
        /// fen at about sea level over today's land below it (not drawn yet).</summary>
        public const byte Sea = 16, Lake = 32, Land = 64, Fen = 128;
        /// <summary>Decision 0069 rule 6: a canal dug after 1318 (with Land), from OpenStreetMap's outlines and the water
        /// mask's river water along them; file version 2. Works rules 1 worlds leave these areas out.</summary>
        public const byte Canal = 8;
        /// <summary>Works rules 4 (file version 3): an area those rules add (land made in the sea after 2000, canals found
        /// under other names); worlds of works rules 1-3 leave these out.</summary>
        public const byte Late = 4;

        /// <summary>The file: version 1 (step 1, the Netherlands), 2 (and the canals') or 3 (and works rules 4's), by its
        /// header.</summary>
        public static LakeOutlineData Read(byte[] file)
        {
            int v = file != null && file.Length >= 8 ? System.BitConverter.ToInt32(file, 4) : 0;
            return LakeOutlineData.Read(file, FileName, Magic, v is 1 or 2 or 3 ? v : 3);
        }

        /// <summary>The flags a world of these works rules leaves out: the canals before rules 2, works rules 4's areas before
        /// rules 4.</summary>
        public static byte SkipFor(int rules) => (byte)((rules >= 2 ? 0 : Canal) | (rules >= 4 ? 0 : Late));

        /// <summary>Inside an area that takes today's water or land away (SEA or LAND): today's lakes and river outlines
        /// there are not drawn, and the coast decides alone.</summary>
        public static bool Undoes(in LakeOutlineLayer.Hit h) => h.Inside && (h.Flags & (Sea | Land)) != 0;

        public static bool IsLake(in LakeOutlineLayer.Hit h) => h.Inside && (h.Flags & Lake) != 0;

        /// <summary><see cref="Undoes(in LakeOutlineLayer.Hit)"/> of TerrainModel.WorksAt's answer.</summary>
        public static bool Undoes(in WorksHit w) => Undoes(w.Raw);

        /// <summary><see cref="IsLake(in LakeOutlineLayer.Hit)"/> of TerrainModel.WorksAt's answer.</summary>
        public static bool IsLake(in WorksHit w) => IsLake(w.Raw);

        /// <summary><see cref="Combine(double, in LakeOutlineLayer.Hit, double)"/> of TerrainModel.WorksAt's answer.</summary>
        public static double Combine(double d, in WorksHit w, double reach) => Combine(d, w.Raw, reach);

        /// <summary>The coast's signed distance (blocks, + land, NaN for no outline) with a works hit taken in: the union
        /// with SEA areas' water, then with LAND areas' land, measured up to reach.</summary>
        public static double Combine(double d, in LakeOutlineLayer.Hit h, double reach)
        {
            if (!h.Covered || double.IsNaN(h.Level)) return d;
            double dist = System.Math.Min(h.Dist, reach);
            bool sea = (h.Flags & Sea) != 0, land = (h.Flags & Land) != 0;
            if (h.Inside)
            {
                if (sea) return double.IsNaN(d) ? -dist : System.Math.Min(d, -dist);
                if (land) return double.IsNaN(d) ? dist : System.Math.Max(d, dist);
                return d;
            }
            if (double.IsNaN(d)) return d;
            if (sea) return System.Math.Min(d, dist);
            if (land) return System.Math.Max(d, -dist);
            return d;
        }
    }

    /// <summary>
    /// (T-R3, code review 14 November 2026) The works of 1318 at a place as TerrainModel.WorksAt answers it: a type of its
    /// own, not a lake outline's hit, so it can't reach the lake rules (TerrainModel.LakeArea, LakeOutlineLayer.WholeArea)
    /// by mistake; a LAKE area of 1318 is handed to them only through <see cref="LakeOf1318"/>. <see cref="Dist"/> is the
    /// distance to the area's edge, except at a place <see cref="Enclosed"/> by SEA areas, where it is the nearer SEA area
    /// along one axis.
    /// </summary>
    public readonly struct WorksHit
    {
        readonly LakeOutlineLayer.Hit hit;
        /// <summary>Works rules 3: a place between SEA areas and in none (TerrainModel.EnclosedBySea): Inside, with the Sea flag,
        /// and Dist along an axis.</summary>
        public readonly bool Enclosed;

        public WorksHit(in LakeOutlineLayer.Hit hit, bool enclosed)
        {
            this.hit = hit;
            Enclosed = enclosed;
        }

        /// <summary>The works' data has the place's tile.</summary>
        public bool Covered => hit.Covered;
        /// <summary>Inside an area (or <see cref="Enclosed"/> by SEA areas).</summary>
        public bool Inside => hit.Inside;
        /// <summary>Works1318Data's flags: Sea, Lake, Land, Fen, Canal, Late.</summary>
        public byte Flags => hit.Flags;
        /// <summary>The area's level (m; NaN where none is near).</summary>
        public double Level => hit.Level;
        /// <summary>Blocks to the area's edge; for an <see cref="Enclosed"/> place, to the nearer SEA area along its axis.</summary>
        public double Dist => hit.Dist;

        /// <summary>A LAKE area of 1318 as a lake (its level, distance from the shore and flags; its whole area its own
        /// layer's). Only where <see cref="Works1318Data.IsLake(in WorksHit)"/>.</summary>
        public LakeOutlineLayer.Hit LakeOf1318 => Works1318Data.IsLake(hit) ? hit : throw new System.InvalidOperationException("not a LAKE area of 1318");

        /// <summary>The layer's hit, for Works1318Data's tests alone.</summary>
        internal LakeOutlineLayer.Hit Raw => hit;
    }
}
