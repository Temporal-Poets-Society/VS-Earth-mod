namespace EarthMap.Core
{
    /// <summary>
    /// rivers-outline-glo90-1.bin (tools/bake_river_outlines.py; 0053 item 18, proposed): the rivers of the Copernicus GLO-90
    /// water body mask north of 60°S as outlines, in lakes-glo90-1.bin's layout (LakeOutlineData) with "EMRO" for "EMLO":
    /// each piece of a tile's rivers a "lake" whose level is its median GLO-90 height (in the mask's rivers GLO-90 is the
    /// water's surface, flattened in steps of half a metre), with no flags, reaching a pixel into the lakes and the sea it
    /// touches. Reservoirs are left out (no modern dams in 1318; found from HydroLAKES' dams). TerrainModel draws them on
    /// dry land only, at the level of the nearest river course. About 30 MB: it comes with the GLO-90 heights, as the
    /// lakes do.
    ///
    /// rivers-outline-glo90-2.bin (0053 item 19) goes with the dam file (DamRemovalData): its reservoirs are the
    /// dam bake's, and the rivers run on through them and across their dams in corridors (tools/bake_dam_removal.py rule 5)
    /// as wide as the river where it comes in or goes out. It also fills the bridges the mask has as land across a river
    /// (Riga's Southern Bridge; tools/bake_river_outlines.py bridges), whose decks don't count towards a piece's level.
    /// Worlds keep the version they were made with; one that keeps the dams for good reads version 1.
    ///
    /// rivers-outline-glo90-3.bin (0063 rule 2): the mask's rivers are one or two pixels wide in most places, and version 2
    /// split an outline wherever two pixels touch only at a corner (Rome: 25 rings among 83 river pixels). Version 3 joins
    /// those contacts first (the lower of the two pixels across one becomes river), then splits the water into wide water
    /// (three pixels and more across, contoured smooth: the 0.5 contour of its pixels blurred by 0.7 pixels, with no pixel
    /// staircase) and narrow pieces (FlagNarrow; at the pixels' edges as before, reaching into the wide water they run into,
    /// so the two meet; the layer counts the overlap as one water, with no shore inside it). On a tile's edge the outlines
    /// are the pixels' own as before, so neighbouring tiles meet. Its unit is 24,000 a degree (a twentieth of a pixel
    /// north-south; versions 1 and 2: 2,400), so the smooth outlines keep their shape. Reservoirs, corridors and bridges
    /// as version 2.
    ///
    /// rivers-outline-glo90-5.bin (decision 0092, EA-0148): version 4's rules on GLO-90's own grid. The mask is
    /// pixel-is-point (cell (r, c) centred on (lat0 + 1 - r/1200, lon0 + c/cols), as FineElevation reads the heights);
    /// versions 1-4 drew each cell half a cell south and east of that. Each tile is contoured with its south and east
    /// neighbours' first row and column, whose cells lie on its edges on that grid, and clipped to its square; the
    /// reservoirs and corridors are dam rules 4's, on courses rules 5's list.
    /// </summary>
    public static class RiverOutlineData
    {
        /// <summary>The newest version, which new worlds with the dams taken out record (EarthParams.RiverOutlines).</summary>
        public const int FileVersion = 5;

        /// <summary>Version 3 (0063): a piece of river water under three mask pixels across (the courses draw those rivers
        /// where the world draws streams down to 20 m³/s; channel rules 1 leave such pieces out there). Wide water has no
        /// flags.</summary>
        public const byte FlagNarrow = 16;

        /// <summary>A version's file name in the GLO-90 folder and on Earth's data release.</summary>
        public static string FileName(int version) => $"rivers-outline-glo90-{version}.bin";

        /// <summary>The version a world reads: the one it saved, or this build's newest when it saved a newer one.</summary>
        public static int Usable(int saved) => System.Math.Clamp(saved, 1, FileVersion);

        /// <summary>The files this build reads (version 1 from the bake of 7 November 2026, 2 from 8 November's, 3 from 8
        /// November's 0063 bake on dam rules 2's reservoirs and corridors, 4 (decision 0069) version 3's rules on dam rules
        /// 3's, with Global Dam Watch's reservoirs, from 12 November's); any other is refused.</summary>
        public static long FileBytes(int version) => version switch { 1 => 30_314_258, 2 => 30_630_118, 3 => 35_782_184, 4 => 35_694_389, 5 => 35_947_613, _ => -1 };
        public static string FileSha256(int version) => version switch
        {
            1 => "52a0c79ebc7925ac086a271a1817b144b472b08f34aa28bd8547b1d549f3807b",
            2 => "6e10438fa0cdd68f679c2bcc3a443f7d5bdc43b3194cc8627a2ce6a8b820aaf1",
            3 => "ed4bb2d57db40175a9cc76731136a62321fa85a6d755fb10e7d27e324bd37e28",
            4 => "83b5da9a9f364d5795fcec05bffed761430aa02c4de00c17a7b3b9081b9e1235",
            5 => "b83cef11376d21f9eb50f9f5679066d922b1862fca6510c48da8e4d3517ee0c9",
            _ => "",
        };

        /// <summary>Whether these bytes are the file of this version this build was made for (size and SHA-256).</summary>
        public static bool IsExpected(int version, byte[] bytes, out string why) => LakeOutlineData.Matches(bytes, FileBytes(version), FileSha256(version), out why);

        /// <summary>A file of any version this build knows (the version is the file's own).</summary>
        public static LakeOutlineData Read(byte[] file)
        {
            int v = file != null && file.Length >= 8 ? System.BitConverter.ToInt32(file, 4) : 0;
            return LakeOutlineData.Read(file, FileName(Usable(v)), "EMRO", v is >= 1 and <= FileVersion ? v : FileVersion);
        }

        /// <summary>How far (blocks) a river column measures its distance to the shore: the bed reaches its depth well within
        /// it, and land within LakeOutlineLayer.HoldReach is held. About 160 m, 4..16 blocks.</summary>
        public static double ReachFor(EarthGeometry geo) => System.Math.Clamp(System.Math.Round(160 / (Geo.MetresPerDegree / geo.BlocksPerDegreeZ)), 4, 16);

        /// <summary>The river layer of a world: its own reach, and index cells of at least 32 blocks (a tile at 1:10 is
        /// 11,000 blocks across).</summary>
        public static LakeOutlineLayer Layer(LakeOutlineData data, EarthGeometry geo) => Layer(data, geo, false);

        /// <summary>The river layer, without the narrow pieces (FlagNarrow) where dropNarrow (EarthParams.DropsNarrowRiverPieces):
        /// as if the mask had no water there, its shores and holds included. Given courseAt (TerrainModel.CourseDrawsAt), a
        /// narrow piece away from the courses that joins the wide water in two places stays (LakeOutlineLayer.SkipOnlyWhere):
        /// the side arms and channels between wide waters no course runs in; one that only leaves the wide water (a canal)
        /// still goes. From version 3 on, pieces that overlap (a narrow piece's end in the wide water) are one water
        /// (LakeOutlineLayer.UnionShores): no shore where they meet. Given keepWhere (channel rules 4, TerrainModel.FlowCarriesAt),
        /// a narrow piece away from the courses that the river's flow passes through stays too (LakeOutlineLayer.KeepWhere).
        /// (EA-0179, decision 0098) With courseOnly (EarthParams.CoarseRiverRules, 1:50 and coarser): a piece under 2 blocks wide
        /// stays only along a drawn course (courseAt; LakeOutlineLayer.CourseOnlyAt): no loose dashes.</summary>
        public static LakeOutlineLayer Layer(LakeOutlineData data, EarthGeometry geo, bool dropNarrow, System.Func<double, double, bool> courseAt = null,
            System.Func<double, double, bool> keepWhere = null, bool courseOnly = false) =>
            new LakeOutlineLayer(data, geo, ReachFor(geo), 32, dropNarrow ? FlagNarrow : (byte)0, data.Version >= 3, dropNarrow ? courseAt : null,
                dropNarrow && courseAt != null ? keepWhere : null, courseOnly ? courseAt : null);

    }
}
