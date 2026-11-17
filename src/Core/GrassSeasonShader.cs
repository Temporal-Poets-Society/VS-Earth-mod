using System;
using System.Globalization;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0154 (decision 0091): Earth's seven grass calendars (<see cref="GrassPhenology.Codes"/>) as one colour map, so Earth
    /// takes one of the game's 40 colour map places instead of seven. The map (earthgrass-seasons.png, tools/
    /// make_grass_seasons.py) stacks the calendars as bands of <see cref="Pitch"/> rows: <see cref="Pad"/> rows repeating
    /// the band's first row, its 16 rows (vanilla's 128 x 16 season map), and Pad rows repeating its last, so a band filtered
    /// at a coarser mip level blends only with itself. A grass block's colour data carries the band + 1 in bits 13-15, which
    /// vanilla reads only for leaves (their per-tree offset); Earth writes it only on the cube faces of seasonalGrass blocks
    /// (ChunkTesselator.TesselateBlock's colour data, ClientWorldMap.getColorMapData), which no chunk shader draws as leaves.
    /// Vanilla's colormap.vsh takes the row in a season map from its noise, clamped to the first and last row's centres of
    /// a 16-row map; this rewrites that one expression so that, for Earth's map alone (its place in the shader's array, known
    /// once vanilla has numbered the maps, so the shaders are loaded again with it) and bits 13-15 set, the same row is taken
    /// inside the band. Both are asked, so shaders still loaded with an old place (after leaving an Earth world) leave other
    /// worlds' maps alone: their colour data has no bits there outside leaves.
    /// Pixel for pixel the same look as the seven maps (the u, the noise and the in-band row are vanilla's).
    /// </summary>
    public static class GrassSeasonShader
    {
        /// <summary>The colour map's code (assets/earth/patches/grass-season-colormaps.json).</summary>
        public const string MapCode = "earthGrassSeasons";
        /// <summary>Rows of one calendar (vanilla's season maps are 16 high), repeated edge rows above and below, a band's rows.</summary>
        public const int Rows = 16, Pad = 4, Pitch = Rows + 2 * Pad;
        /// <summary>The bands: one per calendar.</summary>
        public static int Bands => GrassPhenology.Codes.Length;
        /// <summary>The map's height in pixels.</summary>
        public static int Height => Bands * Pitch;
        /// <summary>The colour data's bits that carry the band + 1 (vanilla's per-tree offset, read for leaves only; 0: none).</summary>
        public const int BandShift = 13, BandMask = 7;
        /// <summary>The chunk shaders that include colormap.vsh (1.22.7): all must carry the edit before Earth uses the map.</summary>
        public static readonly string[] ChunkPasses = { "chunkopaque", "chunktopsoil", "chunktransparent", "chunkliquid" };

        /// <summary>Vanilla's season map row in colormap.vsh (1.22.7): the noise clamped to a 16-row map's first and last row.</summary>
        public const string Needle = "rect.y + rect.w * clamp(noise, 0.5 / (rect.w * atlasHeight), 15.5 / (rect.w * atlasHeight))";

        /// <summary>A grass colour data value with Earth's map (its place + 1, as vanilla's season index; bits 6-7, vanilla's map
        /// flags, cleared as Earth's map has none, as the seven maps had) and a calendar's band.</summary>
        public static int WithBand(int colorMapData, int seasonIndex, int band) =>
            (colorMapData & ~0xFF & ~(BandMask << BandShift)) | (seasonIndex & 0x3F) | (((band + 1) & BandMask) << BandShift);

        /// <summary>
        /// The shader code with the season row taken inside Earth's band where the season map is Earth's (its place
        /// <paramref name="mapPlace"/> in colorMapRects, RectIndex), or null where the code lacks vanilla's expression (a game
        /// update or another mod changed colormap.vsh). Code without season maps at all (no calcColorMapUvs) comes back as it is.
        /// </summary>
        public static string Patch(string code, int mapPlace)
        {
            if (code == null || mapPlace < 0 || mapPlace >= 40) return null;
            int at = code.IndexOf(Needle, StringComparison.Ordinal);
            if (at < 0) return code.Contains("calcColorMapUvs", StringComparison.Ordinal) ? null : code;
            if (code.IndexOf(Needle, at + Needle.Length, StringComparison.Ordinal) >= 0) return null;
            return code.Substring(0, at) + Expression(mapPlace) + code.Substring(at + Needle.Length);
        }

        /// <summary>Whether shader code carries Earth's band rows for this place (a program loaded with <see cref="Patch"/>).</summary>
        public static bool Carries(string code, int mapPlace) => code != null && code.Contains(Marker(mapPlace), StringComparison.Ordinal);

        static string Marker(int mapPlace) => "seasonMapIndex == " + mapPlace.ToString(CultureInfo.InvariantCulture) + " /* Earth grass seasons */";

        /// <summary>The GLSL in place of <see cref="Needle"/>: for Earth's map the band's first row plus vanilla's in-band row.</summary>
        public static string Expression(int mapPlace) => string.Format(CultureInfo.InvariantCulture,
            "({0} && ((colormapData >> {1}) & {2}) != 0 ? rect.y + rect.w * (float((((colormapData >> {1}) & {2}) - 1) * {3} + {4}) + {5}.0 * clamp(noise, 0.5 / {5}.0, {6}.5 / {5}.0)) / {7}.0 : {8})",
            Marker(mapPlace), BandShift, BandMask, Pitch, Pad, Rows, Rows - 1, Height, Needle);

        /// <summary>The row (in pixels from the map's top) the edited shader samples for a band and vanilla's row in a 16-row map
        /// (0.5 to 15.5): the same arithmetic as <see cref="Expression"/>, for the offline checks.</summary>
        public static double Row(int band, double rowIn16) => band * Pitch + Pad + rowIn16;
    }
}
