using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Earth's own rocks (gneiss, schist, quartzite, gypsum, dolostone) get their crafted stone textures the way vanilla
    /// makes its own: vanilla's shared template for each block type (cobblestone, ashlar, drystone, polished rock ...),
    /// recoloured into the Earth rock's palette (StoneRecolour). Runs in the client when assets load, before block textures
    /// are collected, and replaces the data of Earth's shipped textures of the same names; those stay as the fallback when
    /// vanilla's template is missing. Rocks whose textures come from another mod (Geology Additions, Advanced Geology) are
    /// left alone. The author, 7 November: "look how vanilla is done and try to match implementation, especially on cobble
    /// and ashlar blocks".
    /// </summary>
    public class EarthStoneTextures : ModSystem
    {
        public static readonly string[] Rocks = { "gneiss", "schist", "quartzite", "gypsum", "dolostone" };
        const string Stone = "block/stone/";

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public override void AssetsLoaded(ICoreAPI api)
        {
            try { Generate((ICoreClientAPI)api); }
            catch (Exception e) { Mod.Logger.Warning("Earth stone textures: kept Earth's own drawings ({0}).", e.Message); }
        }

        void Generate(ICoreClientAPI capi)
        {
            var assets = capi.Assets;
            string own = Norm(Mod.SourcePath);
            // (CR-O10) inside Earth's folder or the folder itself, separator to separator: a bare prefix test also took a sibling
            // folder such as "Earth Ruins" beside "Earth" for Earth's
            bool Ours(IAsset a) => a?.Origin?.OriginPath != null
                && (Norm(a.Origin.OriginPath) + Path.DirectorySeparatorChar).StartsWith(own + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            IAsset Get(string path) => assets.TryGet(new AssetLocation("game", "textures/" + path + ".png"));
            var bitmaps = new Dictionary<string, (int[] px, int w, int h)>();
            (int[] px, int w, int h)? Pixels(IAsset a)
            {
                if (a == null) return null;
                string key = a.Location.ToString();
                if (bitmaps.TryGetValue(key, out var hit)) return hit;
                using var bmp = a.ToBitmap(capi);
                if (bmp == null) return null;
                return bitmaps[key] = (bmp.Pixels, bmp.Width, bmp.Height);
            }
            StoneRecolour.Palette? Measure(params string[] paths)
            {
                var imgs = paths.Select(p => Pixels(Get(p))).Where(b => b != null).Select(b => b.Value.px).ToList();
                return imgs.Count == 0 ? null : StoneRecolour.Measure(imgs);
            }
            string[] RockFiles(string rock) => Enumerable.Range(1, 4).Select(i => Stone + "rock/" + rock + i).ToArray();

            // Vanilla's rocks: every rock with a cobblestone template, a rock texture and a sand, not drawn by Earth.
            var references = new Dictionary<string, StoneRecolour.Palette>(StringComparer.Ordinal);
            foreach (var a in assets.GetManyInCategory("textures", Stone + "cobblestone/", "game", false))
            {
                string name = a.Location.Path.Substring(("textures/" + Stone + "cobblestone/").Length);
                if (name.Contains('/') || !name.EndsWith("1.png", StringComparison.Ordinal)) continue;
                string rock = name.Substring(0, name.Length - "1.png".Length);
                if (Rocks.Contains(rock) || Ours(Get(Stone + "rock/" + rock + "1")) || Get(Stone + "sand/" + rock) == null) continue;
                var pal = Measure(RockFiles(rock));
                if (pal != null) references[rock] = pal.Value;
            }
            if (references.Count == 0) { Mod.Logger.Notification("Earth stone textures: no vanilla stone templates found; Earth's own drawings kept."); return; }

            var origins = new Dictionary<IAssetOrigin, RegeneratedOrigin>();
            var report = new List<string>();
            foreach (string code in Rocks)
            {
                if (!Ours(Get(Stone + "rock/" + code + "1"))) continue; // not Earth's rock here (absent, or another mod's)
                var ours = Measure(RockFiles(code));
                if (ours == null) continue;
                string reference = StoneRecolour.Nearest(references, ours.Value);
                var oursSand = Measure(Stone + "sand/" + code);
                var refSand = Measure(Stone + "sand/" + reference);
                int count = 0;
                foreach (string dir in StoneRecolour.TemplateDirs)
                {
                    bool sand = StoneRecolour.SandPalette(dir);
                    StoneRecolour.Palette? from = sand ? refSand : references[reference], to = sand ? oursSand : ours;
                    if (from == null || to == null) continue;
                    string refPrefix = "textures/" + Stone + dir + "/" + reference;
                    var made = new List<byte[]>();
                    foreach (var a in assets.GetManyInCategory("textures", Stone + dir + "/" + reference, "game", true)
                                            .OrderBy(a => a.Location.Path, StringComparer.Ordinal))
                    {
                        string suffix = a.Location.Path.Substring(refPrefix.Length);
                        if (suffix.Contains('/') || suffix.Length == 0 || !(char.IsDigit(suffix[0]) || suffix[0] == '.' || suffix[0] == '-')) continue;
                        var target = Get(Stone + dir + "/" + code + suffix.Substring(0, suffix.Length - ".png".Length));
                        if (target == null || !Ours(target)) continue;
                        var src = Pixels(a);
                        if (src == null) continue;
                        var png = StoneRecolour.EncodePng(StoneRecolour.Transfer(src.Value.px, from.Value, to.Value), src.Value.w, src.Value.h);
                        Replace(target, png, origins);
                        made.Add(png);
                        count++;
                    }
                    if (made.Count == 0) continue;
                    // Earth drew more alternates here than vanilla has (drystone 7-8): they repeat the recoloured ones,
                    // so every alternate in the folder has vanilla's look.
                    int extra = 0;
                    foreach (var a in assets.GetManyInCategory("textures", Stone + dir + "/" + code, "game", false))
                    {
                        string suffix = a.Location.Path.Substring(("textures/" + Stone + dir + "/" + code).Length);
                        if (suffix.Contains('/') || !(suffix.Length > 0 && (char.IsDigit(suffix[0]) || suffix[0] == '.' || suffix[0] == '-'))) continue;
                        if (!Ours(a) || (a.Origin is RegeneratedOrigin)) continue;
                        Replace(a, made[extra++ % made.Count], origins);
                    }
                }
                report.Add($"{code} from {reference} ({count})");
            }
            bitmaps.Clear();
            if (report.Count > 0)
                Mod.Logger.Notification("Earth stone textures: crafted stone of Earth's rocks drawn as vanilla draws its own (shared template, rock palette): {0}.", string.Join(", ", report));
        }

        /// <summary>Puts generated PNG data into the asset, and an origin that hands the same data back if the game reloads it.</summary>
        static void Replace(IAsset asset, byte[] png, Dictionary<IAssetOrigin, RegeneratedOrigin> origins)
        {
            var inner = asset.Origin is RegeneratedOrigin r ? r.Inner : asset.Origin;
            if (!origins.TryGetValue(inner, out var origin)) origins[inner] = origin = new RegeneratedOrigin(inner);
            origin.Data[asset.Location] = png;
            asset.Data = png;
            asset.Origin = origin;
        }

        static string Norm(string path) => string.IsNullOrEmpty(path) ? "\u0000" : Path.GetFullPath(path).TrimEnd('\\', '/');

        sealed class RegeneratedOrigin : IAssetOrigin
        {
            public readonly IAssetOrigin Inner;
            public readonly Dictionary<AssetLocation, byte[]> Data = new Dictionary<AssetLocation, byte[]>();
            public RegeneratedOrigin(IAssetOrigin inner) { Inner = inner; }
            public string OriginPath => Inner.OriginPath;
            public void LoadAsset(IAsset asset) { if (Data.TryGetValue(asset.Location, out var d)) asset.Data = d; else Inner.LoadAsset(asset); }
            public bool TryLoadAsset(IAsset asset) { if (Data.TryGetValue(asset.Location, out var d)) { asset.Data = d; return true; } return Inner.TryLoadAsset(asset); }
            public List<IAsset> GetAssets(AssetCategory category, bool shouldLoad = true) => Inner.GetAssets(category, shouldLoad);
            public List<IAsset> GetAssets(AssetLocation baseLocation, bool shouldLoad = true) => Inner.GetAssets(baseLocation, shouldLoad);
            public bool IsAllowedToAffectGameplay() => Inner.IsAllowedToAffectGameplay();
        }
    }
}
