using System.IO.Compression;
using EarthMap;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VsMaps = Vintagestory.API.Datastructures.OrderedDictionary<string, Vintagestory.API.Common.ColorMap>;

// EA-0154 (decision 0091): Earth's seven grass calendars are one colour map (GrassSeasonShader), and it gives way where the
// installed mods would pass the game's 40 colour maps in the block atlas (vanilla's ClientWorldMap.PopulateColorMaps throws
// on the 41st). The client path can't run headless: these build the game's own ColorMap objects in its own OrderedDictionary,
// as GameMain.ColorMaps holds them, count and apply as EarthColorMapBudget does at the end of AssetsLoaded, number them as
// vanilla's LoadColorMaps would, find Earth's map as EarthGrassSeason does, edit the installed game's colormap.vsh, and read
// the packed picture. tests/GrassShaderCheck compiles the edited chunk shaders and runs the edited row on this machine's GPU.
static partial class Checks
{
    static ColorMap Map(string code, bool atlas = true, bool texture = true, string domain = "game") => new()
    {
        Code = code, LoadIntoBlockTextureAtlas = atlas, Texture = texture ? new CompositeTexture(new AssetLocation(domain, "environment/" + code)) : null,
    };

    /// <summary>Vanilla's 26 (survival config/colormaps.json in 1.22.7: all into the atlas), Earth's one (its patch appends it),
    /// then <paramref name="others"/> of other mods (the first 4 Wildcraft Trees', the rest Floral Zones'), and
    /// <paramref name="extra"/> that stay out of the atlas.</summary>
    static VsMaps Maps(int others, int extra = 0, bool earth = true)
    {
        var d = new VsMaps();
        for (int i = 0; i < 26; i++) d["vanilla" + i] = Map("vanilla" + i);
        if (earth) d[GrassSeasonShader.MapCode] = Map(GrassSeasonShader.MapCode, domain: "earth");
        for (int i = 0; i < others; i++) d["other" + i] = Map("other" + i, domain: i < 4 ? "wildcrafttree" : "floralzonescaperegion");
        for (int i = 0; i < extra; i++) d["climateonly" + i] = Map("climateonly" + i, atlas: false);
        return d;
    }

    /// <summary>What vanilla's LoadColorMaps does (decompiled 1.22.7): a map without a texture leaves the atlas; each map
    /// with LoadIntoBlockTextureAtlas gets the next RectIndex; every map gets its pixels and size (Earth's packed one 128 x
    /// 168, the others 128 x 16). Returns how many PopulateColorMaps would write.</summary>
    static int VanillaLoad(VsMaps maps)
    {
        int n = 0;
        foreach (var kv in maps)
        {
            var m = kv.Value;
            if (m.Texture?.Base == null) { m.LoadIntoBlockTextureAtlas = false; continue; }
            if (m.LoadIntoBlockTextureAtlas) { m.RectIndex = n + (m.ExtraFlags << 6); n++; }
            m.Pixels = new int[4];
            m.OuterSize = new Size2i(128, kv.Key == GrassSeasonShader.MapCode ? GrassSeasonShader.Height : 16);
        }
        return n;
    }

    static void RunColorMaps()
    {
        int max = GlobalConstants.MaxColorMaps;
        Check(max == 40, "the game's limit of colour maps in the block atlas is 40 (GlobalConstants.MaxColorMaps)");
        Check(GrassPhenology.Codes.Length == 7 && GrassSeasonShader.Bands == 7 && GrassSeasonShader.Height == 168, "Earth's 7 calendars are 7 bands of 24 rows in one map");

        // the author's own Mods folder: vanilla 26 + Earth 1 + Wildcraft Trees 4 = 31 (37 with the seven maps of 2.0-2.1.2)
        var fits = Maps(4, extra: 3);
        var b = EarthColorMapBudget.Apply(fits);
        Check(b.Total == 31 && b.Earth == 1 && b.Others == 30 && !b.GaveWay && !b.StillOver && !EarthColorMapBudget.Lines(b).Any(), "31 maps fit: Earth's stays, no warning");
        Check(EarthColorMapBudget.Domains(b) == "game 26, wildcrafttree 4", "the others' maps by mod: " + EarthColorMapBudget.Domains(b));
        Check(fits[GrassSeasonShader.MapCode].LoadIntoBlockTextureAtlas, "Earth's map stays in the atlas while the game has room");
        Check(VanillaLoad(fits) == 31, "vanilla numbers 31 atlas maps");
        int place = EarthGrassSeason.PackedMapPlace(fits, b.GaveWay, out bool loading, out string off);
        Check(place == 26 && !loading && off == null, "Earth's map is the shader's place 26 (after vanilla's 26)");
        Check(EarthGrassSeason.PackedMapPlace(Maps(4), false, out loading, out off) == -1 && loading, "before vanilla has read it, it is still loading");
        var shortMap = Maps(4);
        VanillaLoad(shortMap);
        shortMap[GrassSeasonShader.MapCode].OuterSize = new Size2i(128, 16);
        Check(EarthGrassSeason.PackedMapPlace(shortMap, false, out loading, out off) == -1 && off.Contains("16 pixels high"), "a map of the wrong height (another mod's texture in its place) is not used: " + off);

        // 41 maps: Earth's one gives way, the other 40 fit
        var over = Maps(14);
        b = EarthColorMapBudget.Apply(over);
        Check(b.Total == 41 && b.Earth == 1 && b.Others == 40 && b.GaveWay && !b.StillOver, "41 maps: Earth's gives way, the other 40 fit");
        Check(!over[GrassSeasonShader.MapCode].LoadIntoBlockTextureAtlas && over.Where(kv => kv.Key != GrassSeasonShader.MapCode).All(kv => kv.Value.LoadIntoBlockTextureAtlas),
            "only Earth's map leaves the atlas");
        int loaded = VanillaLoad(over);
        Check(loaded == 40 && over.Values.All(m => m.Pixels != null), "vanilla then numbers 40 atlas maps (its limit) and still reads every picture");
        Check(over.Values.Where(m => m.LoadIntoBlockTextureAtlas).Select(m => m.RectIndex).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 40)),
            "the other maps get the places 0-39, none past the shader's array");
        var lines = EarthColorMapBudget.Lines(b).ToList();
        Check(lines.Count == 1 && lines[0].Contains("41 colour maps") && lines[0].Contains("40 places") && lines[0].Contains("1 of them Earth's grass season map")
            && lines[0].Contains("other mods' 40 fit (game 26, floralzonescaperegion 10, wildcrafttree 4)") && lines[0].Contains("vanilla's seasons"),
            "one warning names the count, the other mods' 40 that fit, by mod, and says grass follows vanilla's seasons: " + string.Join(" | ", lines));
        place = EarthGrassSeason.PackedMapPlace(over, b.GaveWay, out loading, out off);
        Check(place == -1 && !loading && off == null, "Earth's grass resolves to vanilla's seasons, quietly (the budget said it), and looks no map up by code");
        place = EarthGrassSeason.PackedMapPlace(over, false, out loading, out off);
        Check(place == -1 && off != null && off.Contains(GrassSeasonShader.MapCode) && off.Contains("missing"), "the map out of the atlas without the budget's word: one reason, vanilla's seasons");

        // the other mods alone pass the limit: Earth gives way but can't prevent the crash, and says so
        var hopeless = Maps(21);
        b = EarthColorMapBudget.Apply(hopeless);
        lines = EarthColorMapBudget.Lines(b).ToList();
        Check(b.Total == 48 && b.Others == 47 && b.GaveWay && b.StillOver && lines.Count == 1 && lines[0].Contains("Earth's grass season map stays out")
            && lines[0].Contains("the other mods alone have 47 (game 26, floralzonescaperegion 17, wildcrafttree 4)") && lines[0].Contains("The crash comes from those mods"),
            "48 maps: Earth's gives way and one error says the crash comes from the other mods' 47, the most first: " + string.Join(" | ", lines));
        Check(VanillaLoad(hopeless) == 47, "Earth adds nothing to an overflow it can't prevent");
        // many mods: the eight with the most, the rest summed
        var many = Maps(0);
        for (int m = 0; m < 12; m++) for (int k = 0; k <= m % 3; k++) many["mod" + m + "_" + k] = Map("mod" + m + "_" + k, domain: "plantmod" + m.ToString("00"));
        b = EarthColorMapBudget.Apply(many);
        string doms = EarthColorMapBudget.Domains(b);
        Check(doms.StartsWith("game 26, plantmod02 3, plantmod05 3, plantmod08 3, plantmod11 3, plantmod01 2") && doms.EndsWith("plantmod04 2, plantmod07 2, 5 more mods 6") && b.StillOver,
            "a long list keeps the eight with the most and sums the rest: " + doms);

        // Earth's map absent (a game whose Earth patch didn't apply): nothing of Earth's to take out
        var none = Maps(15, earth: false);
        b = EarthColorMapBudget.Apply(none);
        Check(b.Total == 41 && b.Earth == 0 && !b.GaveWay && b.StillOver && EarthColorMapBudget.Lines(b).Single().StartsWith("Earth Map: the installed mods have 41 colour maps for the game's 40 places; the other mods alone have 41"),
            "without Earth's map nothing gives way; the others' overflow is said: " + EarthColorMapBudget.Lines(b).Single());

        // maps out of the atlas or without a texture don't count (vanilla's LoadColorMaps leaves them out)
        var edge = Maps(13, extra: 5);
        edge["broken"] = Map("broken", texture: false);
        b = EarthColorMapBudget.Apply(edge);
        Check(b.Total == 40 && !b.GaveWay, "exactly 40 fit (maps out of the atlas and without a texture not counted)");
        Check(VanillaLoad(edge) == 40, "vanilla numbers the same 40");
        edge["one more"] = Map("one more");
        b = EarthColorMapBudget.Apply(edge);
        Check(b.Total == 41 && b.GaveWay && VanillaLoad(edge) == 40, "the 41st makes Earth's give way");

        RunGrassSeasonShader();
        Console.WriteLine("Colour map budget and packed grass map checks passed (EA-0154).");
    }

    static void RunGrassSeasonShader()
    {
        // the colour data: Earth's place + 1 and the band + 1 in bits 13-15; climate, temperature, rain and frost kept
        var data = new ColorMapData(5, 3, 200, 90, true);
        for (int band = 0; band < GrassSeasonShader.Bands; band++)
        {
            var c = new ColorMapData(GrassSeasonShader.WithBand(data.Value, 27, band));
            Check(c.SeasonMapIndex == 27 && c.ClimateMapIndex == 3 && c.Temperature == 200 && c.Rainfall == 90 && c.FrostableBit == 1 && ((c.Value >> 13) & 7) == band + 1,
                "band " + band + " in the colour data, the rest kept");
        }
        Check(((new ColorMapData(GrassSeasonShader.WithBand(data.Value | (7 << 13), 27, 0)).Value >> 13) & 7) == 1, "old bits 13-15 are replaced, not mixed");
        Check(new ColorMapData(GrassSeasonShader.WithBand(new ColorMapData(0x45, 3, 200, 90, true).Value, 27, 2)).SeasonMapIndex == 27,
            "vanilla's map flags (bits 6-7, the tropical weighting) are cleared, as Earth's own maps had none");

        // the rows: vanilla's row in a 16-row map (0.5 to 15.5) lands in the same row of the band, never in a pad or another band
        for (int band = 0; band < GrassSeasonShader.Bands; band++)
            foreach (double r in new[] { 0.5, 1.0, 7.25, 15.0, 15.5 })
            {
                double row = GrassSeasonShader.Row(band, r);
                Check(row == band * 24 + 4 + r && row >= band * 24 + 4.5 && row <= band * 24 + 19.5, "band " + band + " row " + r);
            }

        // the installed game's colormap.vsh: the row expression is there once, and the edit replaces exactly it
        string vs = typeof(Checks).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
        string vsh = File.ReadAllText(Path.Combine(vs, "assets", "game", "shaderincludes", "colormap.vsh"));
        int at = vsh.IndexOf(GrassSeasonShader.Needle, StringComparison.Ordinal);
        Check(at >= 0 && vsh.IndexOf(GrassSeasonShader.Needle, at + 1, StringComparison.Ordinal) < 0, "vanilla's colormap.vsh has the season row once (1.22.7)");
        string edited = GrassSeasonShader.Patch(vsh, 26);
        Check(edited != null && GrassSeasonShader.Carries(edited, 26) && !GrassSeasonShader.Carries(edited, 25)
            && edited.Replace(GrassSeasonShader.Expression(26), GrassSeasonShader.Needle) == vsh, "the edit replaces the row alone, for place 26");
        Check(edited.Contains("seasonMapIndex == 26 /* Earth grass seasons */ && ((colormapData >> 13) & 7) != 0") && edited.Contains("* 24 + 4)")
            && edited.Contains("/ 168.0") && !GrassSeasonShader.Expression(26).Contains('\''), "the band is taken only for Earth's place with bits 13-15 set (no apostrophe for strict drivers)");
        Check(GrassSeasonShader.Patch(vsh.Replace(GrassSeasonShader.Needle, "rect.y + rect.w * 0.5"), 26) == null, "a colormap.vsh without vanilla's row (another mod's) is not edited: null");
        Check(GrassSeasonShader.Patch("void main() {}", 26) == "void main() {}", "a shader without season maps comes back as it is");
        Check(GrassSeasonShader.Patch(vsh, -1) == null && GrassSeasonShader.Patch(vsh, 40) == null, "no place, no edit");
        foreach (string pass in GrassSeasonShader.ChunkPasses)
            Check(File.ReadAllText(Path.Combine(vs, "assets", "game", "shaders", pass + ".vsh")).Contains("colormap.vsh"), pass + " includes colormap.vsh");

        // the packed picture: 7 bands, each band's 4 rows above and below repeat its first and last row
        string png = Path.Combine(AppContext.BaseDirectory, "earthgrass-seasons.png");
        var (w, h, rgb) = ReadPng(File.ReadAllBytes(png));
        Check(w == 128 && h == GrassSeasonShader.Height, "the packed map is 128 x 168: " + w + " x " + h);
        for (int band = 0; band < GrassSeasonShader.Bands; band++)
        {
            int top = band * GrassSeasonShader.Pitch, first = top + GrassSeasonShader.Pad, last = first + GrassSeasonShader.Rows - 1;
            for (int y = 0; y < GrassSeasonShader.Pad; y++)
                Check(Row(rgb, w, top + y).SequenceEqual(Row(rgb, w, first)) && Row(rgb, w, last + 1 + y).SequenceEqual(Row(rgb, w, last)), "band " + band + " pad row " + y);
            Check(!Row(rgb, w, first).SequenceEqual(Row(rgb, w, last)), "band " + band + " has its own rows (not all one row)");
        }
    }

    static byte[] Row(byte[] rgb, int w, int y) => rgb.Skip(y * w * 3).Take(w * 3).ToArray();

    /// <summary>An 8-bit RGB or RGBA PNG (non-interlaced) as RGB bytes, row by row.</summary>
    static (int W, int H, byte[] Rgb) ReadPng(byte[] f)
    {
        int Be(int o) => (f[o] << 24) | (f[o + 1] << 16) | (f[o + 2] << 8) | f[o + 3];
        int w = 0, h = 0, ch = 3;
        var idat = new MemoryStream();
        for (int o = 8; o < f.Length;)
        {
            int len = Be(o); string type = System.Text.Encoding.ASCII.GetString(f, o + 4, 4);
            if (type == "IHDR") { w = Be(o + 8); h = Be(o + 12); ch = f[o + 17] == 6 ? 4 : 3; if (f[o + 16] != 8 || (f[o + 17] != 2 && f[o + 17] != 6) || f[o + 20] != 0) throw new Exception("unsupported PNG"); }
            if (type == "IDAT") idat.Write(f, o + 8, len);
            o += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new MemoryStream(); z.CopyTo(raw);
        byte[] d = raw.ToArray();
        int stride = w * ch;
        var cur = new byte[stride]; var prev = new byte[stride];
        var rgb = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            int ft = d[y * (stride + 1)];
            for (int i = 0; i < stride; i++)
            {
                int x = d[y * (stride + 1) + 1 + i], a = i >= ch ? cur[i - ch] : 0, b = prev[i], c = i >= ch ? prev[i - ch] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                cur[i] = (byte)(ft switch { 0 => x, 1 => x + a, 2 => x + b, 3 => x + (a + b) / 2, _ => x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c) });
            }
            for (int px = 0; px < w; px++) for (int k = 0; k < 3; k++) rgb[(y * w + px) * 3 + k] = cur[px * ch + k];
            (prev, cur) = (cur, prev);
        }
        return (w, h, rgb);
    }
}
