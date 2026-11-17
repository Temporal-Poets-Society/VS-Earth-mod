using System.Collections.Generic;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace EarthMap
{
    /// <summary>
    /// EA-0154 (decision 0091): Earth's grass season maps give way when the game would have more colour maps in its block
    /// atlas than it can hold. Vanilla's client numbers the maps with LoadIntoBlockTextureAtlas in ClientWorldMap.LoadColorMaps
    /// (a thread started after every mod's AssetsLoaded, ClientSystemStartup.LoadColorMapsAndCatalogSoundsIfSingleplayer) and
    /// PopulateColorMaps writes four floats per map into a fixed array of <see cref="GlobalConstants.MaxColorMaps"/> (40) maps:
    /// one more throws IndexOutOfRangeException and the client crashes while joining. Vanilla has 26, Earth 1 (its seven
    /// grass calendars in one map since 2.1.3, <see cref="GrassSeasonShader"/>; 7 before), Wildcraft Trees 4; flora mods
    /// add more. After every other mod's AssetsLoaded (vanilla registers config/colormaps.json at 0.3, mods register theirs
    /// in AssetsLoaded or before) this counts them and, past 40, keeps Earth's out of the atlas: they are still read as
    /// pictures, take no place, and grass follows vanilla's seasons (<see cref="EarthGrassSeason"/> finds them out and falls
    /// back). One warning says so. Where the other mods' maps alone pass 40 Earth can't prevent the crash: one error says so.
    /// </summary>
    public class EarthColorMapBudget : ModSystem
    {
        /// <summary>Last in the AssetsLoaded phase: after every mod has registered its maps.</summary>
        public override double ExecuteOrder() => 1e9;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        /// <summary>Earth's season maps were kept out of the block atlas this session.</summary>
        public static volatile bool GaveWay;

        /// <summary>The count and what was done: maps bound for the atlas (all, Earth's, the others'), whether Earth's gave way,
        /// whether the others alone still pass the limit, and the others' maps by the domain of their texture (the mod that
        /// adds them; "game" for vanilla's), most first.</summary>
        public readonly record struct Budget(int Total, int Earth, int Others, int Max, bool GaveWay, bool StillOver, IReadOnlyList<(string Domain, int Count)> ByDomain);

        public override void AssetsLoaded(ICoreAPI api)
        {
            GaveWay = false;
            var field = AccessTools.Field(AccessTools.TypeByName("Vintagestory.Common.GameMain"), "ColorMaps");
            if (field?.GetValue(api.World) is not IDictionary<string, ColorMap> maps)
            {
                Mod.Logger.Warning("Earth Map: the client's colour maps can't be read (GameMain.ColorMaps {0}); Earth's grass season maps can't check the game's limit of {1}.",
                    field == null ? "not found" : "is not a dictionary of colour maps", GlobalConstants.MaxColorMaps);
                return;
            }
            var b = Apply(maps);
            GaveWay = b.GaveWay;
            foreach (string line in Lines(b)) if (b.StillOver) Mod.Logger.Error(line); else Mod.Logger.Warning(line);
            if (!b.GaveWay && !b.StillOver)
                Mod.Logger.Notification("Earth Map: {0} of the game's {1} colour map places in use, {2} of them Earth's grass season map ({3}).", b.Total, b.Max, b.Earth, Domains(b));
        }

        /// <summary>A map vanilla's LoadColorMaps puts into the block atlas (it clears the flag of one without a texture).</summary>
        public static bool InAtlas(ColorMap map) => map != null && map.LoadIntoBlockTextureAtlas && map.Texture?.Base != null;

        /// <summary>Counts the maps bound for the block atlas and, past <paramref name="max"/>, takes Earth's out of it.</summary>
        public static Budget Apply(IDictionary<string, ColorMap> maps, int max = GlobalConstants.MaxColorMaps)
        {
            var earthCodes = new HashSet<string> { GrassSeasonShader.MapCode };
            int total = 0, earth = 0;
            var domains = new Dictionary<string, int>();
            foreach (var kv in maps)
            {
                if (!InAtlas(kv.Value)) continue;
                total++;
                if (earthCodes.Contains(kv.Key)) { earth++; continue; }
                string domain = string.IsNullOrEmpty(kv.Value.Texture.Base.Domain) ? GlobalConstants.DefaultDomain : kv.Value.Texture.Base.Domain;
                domains[domain] = domains.TryGetValue(domain, out int n) ? n + 1 : 1;
            }
            var byDomain = new List<(string, int)>();
            foreach (var d in domains) byDomain.Add((d.Key, d.Value));
            byDomain.Sort((a, b) => a.Item2 != b.Item2 ? b.Item2.CompareTo(a.Item2) : string.CompareOrdinal(a.Item1, b.Item1));
            bool give = total > max && earth > 0;
            if (give)
                foreach (string code in earthCodes)
                    if (maps.TryGetValue(code, out var m) && m != null) m.LoadIntoBlockTextureAtlas = false;
            return new Budget(total, earth, total - earth, max, give, total - earth > max, byDomain);
        }

        /// <summary>The others' maps by domain, most first ("game 26, wildcrafttree 4"); at most <paramref name="top"/>, the rest
        /// summed.</summary>
        public static string Domains(Budget b, int top = 8)
        {
            var parts = new List<string>();
            int rest = 0, restMods = 0;
            for (int i = 0; i < b.ByDomain.Count; i++)
            {
                if (i < top) parts.Add(b.ByDomain[i].Domain + " " + b.ByDomain[i].Count);
                else { rest += b.ByDomain[i].Count; restMods++; }
            }
            if (restMods > 0) parts.Add(restMods + " more mods " + rest);
            return string.Join(", ", parts);
        }

        /// <summary>The log lines for a count (none while it fits).</summary>
        public static IEnumerable<string> Lines(Budget b)
        {
            if (b.GaveWay && !b.StillOver)
                yield return string.Format("Earth Map: the installed mods have {0} colour maps for the game's {1} places ({2} of them Earth's grass season map); " +
                    "Earth's stays out and the other mods' {3} fit ({4}), so the game doesn't crash loading them. Grass follows vanilla's seasons instead of each place's own.",
                    b.Total, b.Max, b.Earth, b.Others, Domains(b));
            if (b.StillOver)
                yield return string.Format("Earth Map: the installed mods have {0} colour maps for the game's {1} places; {2}the other mods alone have {3} ({4}), " +
                    "so the game crashes loading them (ClientWorldMap.PopulateColorMaps), whatever Earth does. The crash comes from those mods: remove some that add colour maps.",
                    b.Total, b.Max, b.Earth > 0 ? "Earth's grass season map stays out, but " : "", b.Others, Domains(b));
        }
    }
}
