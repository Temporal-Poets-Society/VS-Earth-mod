using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Soil rules 4 (decision 0105, EA-0199): the topsoil of worlds whose soil vanilla's block layers lay (vanilla rock and
    /// regional rock without ground columns), from Earth's real soil map instead of vanilla's rain and warmth formula. One
    /// decision per land column: no topsoil, or a fertility tier, by the column's WRB soil class (SoilGrids 2.0: the 30″
    /// set of release egts-v1 where the world reads it, else the 2′ map in Earth's data store) and its place: arid land has
    /// none except in a river's natural flood (FloodplainGrid), whose level against the ground draws the edge; river silt in the flood is at least medium; cold
    /// polar and alpine ground (beyond the tree line) and dunes are barren; colder than vanilla's grassed topsoil keeps vanilla's rule.
    /// Two tier sets on the one class table (the Earth tab's "Soil", ground columns 3 proposal section 4.1, graded with the
    /// author on 19 November 2026): vanilla tiers (barren, low, medium, high) with barren for inhospitable land only, and
    /// realistic tiers (barren, low, medium, high, black earth) with barren for the poorest quarter of soils and black earth
    /// (vanilla's soil-high, "Terra preta") on the Chernozems. Within a class every place has the class's tier (the measured
    /// properties index is for after 2.2). The rules and the tiles they read are in <see cref="RulesAsset"/>.
    /// Game-independent: the server's block-layer hook (BlockLayerAdjustments) asks <see cref="Decide"/>, gives vanilla's
    /// layers the tier's fertility (<see cref="LayerFertility"/>) and swaps the top block for the tiers vanilla never lays.
    /// </summary>
    public sealed class Topsoil
    {
        /// <summary>EarthParams.SoilRules of a world with Earth soil (either tier set). Climate only keeps
        /// <see cref="SoilFertility.RulesVersion"/>.</summary>
        public const int RulesVersion = 4;
        public const string RulesAsset = "earth:config/soil-rules-v4.json";

        /// <summary>EarthParams.Topsoil: the Earth tab's "Soil" (earthSoil). 0 climate only (today's rule, every world made
        /// before), 1 Earth soil with vanilla tiers, 2 Earth soil with realistic tiers.</summary>
        public const int ClimateOnly = 0, VanillaTiers = 1, RealisticTiers = 2;
        public static bool On(int topsoil) => topsoil is VanillaTiers or RealisticTiers;

        /// <summary>A column's topsoil. <see cref="Vanilla"/>: this rule says nothing here (no soil class mapped, colder than
        /// vanilla's grassed topsoil, the shore strip): the layers keep soil rules 3. <see cref="None"/>: no topsoil (arid land),
        /// vanilla's sand and gravel. The rest are the soil blocks' fertility, poorest first.</summary>
        public enum Tier : sbyte { Vanilla = -2, None = -1, Barren = 0, Low = 1, Medium = 2, High = 3, BlackEarth = 4 }

        public static readonly string[] TierNames = { "barren", "low", "medium", "high", "blackearth" };
        /// <summary>The vanilla soil block's fertility code of each tier (soil-{code}-{grass}): verylow (barren, 5 nutrients),
        /// low (25), medium (50), compost (natural high fertility, 65), high (terra preta, 80).</summary>
        public static readonly string[] BlockCodes = { "verylow", "low", "medium", "compost", "high" };
        /// <summary>The soil blocks' grass codes, barest first (soil-{fertility}-{grass}).</summary>
        public static readonly string[] GrassCodes = { "none", "verysparse", "sparse", "normal" };

        /// <summary>What decides a column, besides its soil class.</summary>
        public struct Place
        {
            /// <summary>The WRB reference soil group (SoilGrids name), null where no soil map covers the place.</summary>
            public string Class;
            /// <summary>Arid by the real aridity index on the ambient rainfall and on the rainfall with a river's green belt
            /// (SoilFertility.AridGround twice, as ground columns' soil rules 2).</summary>
            public bool Arid;
            /// <summary>In a river's natural flood (FloodplainGrid.Floods at the column's real height).</summary>
            public bool Flood;
            /// <summary>Wind-blown sand by GUM's 0.5° surface unit (eolian, not loess).</summary>
            public bool Dune;
            /// <summary>The temperature (°C) vanilla's layers see at the column's surface.</summary>
            public double Temperature;
            /// <summary>The warmest month's mean (°C) at the column's height; NaN where unknown.</summary>
            public double WarmestMonth;
        }

        sealed class ClassRule
        {
            public Tier Vanilla, Realistic;
            /// <summary>Tiers in a river's flood (null: <see cref="FloodMinimum"/> or the class's, whichever is higher).</summary>
            public Tier? FloodVanilla, FloodRealistic;
            /// <summary>Barren where GUM maps wind-blown sand, per tier set.</summary>
            public bool DuneBarrenVanilla, DuneBarrenRealistic;
        }

        readonly Dictionary<string, ClassRule> classes = new(StringComparer.Ordinal);
        readonly ClassRule fallback;
        readonly int[] fertility = new int[TierNames.Length];

        public int Version { get; }
        /// <summary>The lowest tier river silt in a flood has (fresh silt, at least medium).</summary>
        public Tier FloodMinimum { get; }
        /// <summary>The rain (0..1 of vanilla's scale) vanilla's layers see at a flooded arid column, so they lay soil there:
        /// below 0.08 vanilla lays none whatever the fertility.</summary>
        public double FloodRain { get; }
        /// <summary>Polar and alpine ground: barren in both tier sets where the warmest month at the column's height is below
        /// this (°C), about the tree line, whatever the class.</summary>
        public double TundraWarmestBelow { get; }
        /// <summary>The fertility (0..255) vanilla's layers see on arid land without topsoil: below their soil's 0.22, so they
        /// lay sand or gravel.</summary>
        public int NoneFertility { get; }
        /// <summary>The soil maps the rules read, highest priority first (see <see cref="Load"/>).</summary>
        public GroundData Soils { get; }
        /// <summary>Optional tiles that were not found (their area falls back to the lower-priority maps).</summary>
        public IReadOnlyList<string> MissingTiles { get; private set; } = Array.Empty<string>();
        /// <summary>A soil class the rules don't name (the default rule answered); each name once.</summary>
        public event Action<string> UnknownClass;
        readonly HashSet<string> reported = new(StringComparer.Ordinal);
        readonly long seed;

        static JsonDocument Parse(string json) => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        public Topsoil(string rulesJson, GroundData soils, long seed)
        {
            Soils = soils ?? new GroundData();
            this.seed = seed;
            using var doc = Parse(rulesJson);
            var root = doc.RootElement;
            Version = root.GetProperty("version").GetInt32();
            if (Version != RulesVersion) throw new FormatException("soil rules declare version " + Version + ", expected " + RulesVersion);
            var fert = root.GetProperty("layerFertility");
            for (int t = 0; t < TierNames.Length; t++)
            {
                int v = fert.GetProperty(TierNames[t]).GetInt32();
                if (v < 0 || v > 255 || (t > 0 && v <= fertility[t - 1])) throw new FormatException("layerFertility must rise from barren to black earth within 0..255");
                fertility[t] = v;
            }
            NoneFertility = root.GetProperty("noneFertility").GetInt32();
            if (NoneFertility < 0 || NoneFertility >= fertility[0]) throw new FormatException("noneFertility must be below barren's");
            FloodMinimum = ParseTier(root.GetProperty("floodMinimum").GetString());
            FloodRain = root.GetProperty("floodRain").GetDouble();
            if (!(FloodRain >= 0 && FloodRain <= 1)) throw new FormatException("floodRain must be 0..1");
            TundraWarmestBelow = root.GetProperty("tundraWarmestBelow").GetDouble();
            fallback = ReadRule(root.GetProperty("default"));
            foreach (var c in root.GetProperty("classes").EnumerateObject()) classes[c.Name] = ReadRule(c.Value);
        }

        static ClassRule ReadRule(JsonElement e)
        {
            var r = new ClassRule { Vanilla = ParseTier(e.GetProperty("vanilla").GetString()), Realistic = ParseTier(e.GetProperty("realistic").GetString()) };
            if (r.Vanilla == Tier.BlackEarth) throw new FormatException("vanilla tiers have no black earth");
            if (e.TryGetProperty("flood", out var f))
            {
                r.FloodVanilla = ParseTier(f.GetProperty("vanilla").GetString());
                r.FloodRealistic = ParseTier(f.GetProperty("realistic").GetString());
                if (r.FloodVanilla == Tier.BlackEarth) throw new FormatException("vanilla tiers have no black earth");
            }
            if (e.TryGetProperty("duneBarren", out var d))
            {
                r.DuneBarrenVanilla = d.TryGetProperty("vanilla", out var dv) && dv.GetBoolean();
                r.DuneBarrenRealistic = d.TryGetProperty("realistic", out var dr) && dr.GetBoolean();
            }
            return r;
        }

        public static Tier ParseTier(string name)
        {
            int i = Array.IndexOf(TierNames, name);
            if (i < 0) throw new FormatException("unknown soil tier '" + name + "'");
            return (Tier)i;
        }

        /// <summary>The tiles a rules file lists (as <see cref="GroundColumns.TileList"/>): an asset location, or a file in
        /// Earth's data folder; its priority (higher answers first); optional ones may be missing.</summary>
        public static List<(string Asset, string File, int Priority, bool Optional)> TileList(string rulesJson) => GroundColumns.TileList(rulesJson);

        /// <summary>
        /// The rules and the soil maps they list: assets through <paramref name="readAsset"/> (null for a missing one),
        /// data-folder files through <paramref name="dataFile"/> (their full path, read lazily; null: the world doesn't read
        /// them). A missing tile that isn't optional throws.
        /// </summary>
        public static Topsoil Load(string rulesJson, Func<string, byte[]> readAsset, long seed, Func<string, string> dataFile = null)
        {
            var data = new GroundData();
            var missing = new List<string>();
            foreach (var (asset, file, priority, optional) in TileList(rulesJson))
            {
                List<GroundTile> list = null;
                if (file != null)
                {
                    string path = dataFile?.Invoke(file);
                    if (path != null && File.Exists(path)) list = GroundTile.ReadAllFromFile(path);
                }
                else
                {
                    byte[] bytes = readAsset(asset);
                    if (bytes != null) list = GroundTile.ReadAll(bytes);
                }
                if (list == null)
                {
                    if (!optional) throw new FileNotFoundException("soil map " + (asset ?? file) + " is missing");
                    missing.Add(asset ?? file);
                    continue;
                }
                foreach (var tile in list) data.Add(tile, priority);
            }
            return new Topsoil(rulesJson, data, seed) { MissingTiles = missing };
        }

        /// <summary>The soil class at a place (with the tiles' coherent warp at class edges), null where none is mapped.</summary>
        public string ClassAt(double lat, double lon)
            => Soils.TryCategory(GroundColumns.FieldNames.Soil, lat, lon, seed, out string name, out _) ? name : null;

        /// <summary>Whether GUM's 0.5° surface unit here is wind-blown sand (eolian, not loess).</summary>
        public bool DuneAt(double lat, double lon)
            => Soils.TryCategory(GroundColumns.FieldNames.Cover, lat, lon, seed, out string unit, out _) && unit is "Ea" or "Ed" or "Er" or "Eu";

        ClassRule RuleFor(string name)
        {
            if (classes.TryGetValue(name, out var r)) return r;
            lock (reported) if (reported.Add(name)) UnknownClass?.Invoke(name);
            return fallback;
        }

        /// <summary>
        /// The column's topsoil in tier set <paramref name="tiers"/> (<see cref="VanillaTiers"/> or <see cref="RealisticTiers"/>):
        /// <list type="number">
        /// <item>colder than vanilla's grassed topsoil (SoilFertility.MinTemperature), or no soil class: vanilla's rule;</item>
        /// <item>arid land: none, unless in a river's flood (rc.12's oasis rule, fresh wetland in GLWD's 0.1° cells, drew straight
        /// edges across the Nile's desert plateau and acted nowhere but beside the Nile and in Mesopotamia: removed);</item>
        /// <item>the class's tier, barren on polar and alpine ground (TundraWarmestBelow) and where the class's dune rule says so;</item>
        /// <item>in a river's flood: the class's flood tier, else at least FloodMinimum (fresh river silt).</item>
        /// </list>
        /// </summary>
        public Tier Decide(int tiers, in Place p)
        {
            if (!On(tiers)) return Tier.Vanilla;
            if (p.Temperature < SoilFertility.MinTemperature || string.IsNullOrEmpty(p.Class)) return Tier.Vanilla;
            if (p.Arid && !p.Flood) return Tier.None;
            var r = RuleFor(p.Class);
            bool realistic = tiers == RealisticTiers;
            if (p.Flood)
            {
                var own = realistic ? r.FloodRealistic : r.FloodVanilla;
                if (own is Tier t) return t;
            }
            var tier = realistic ? r.Realistic : r.Vanilla;
            if (p.WarmestMonth < TundraWarmestBelow) tier = Tier.Barren;
            if (p.Dune && (realistic ? r.DuneBarrenRealistic : r.DuneBarrenVanilla)) tier = Tier.Barren;
            if (p.Flood && tier < FloodMinimum) tier = FloodMinimum;
            return tier;
        }

        /// <summary>The fertility (0..255) vanilla's block layers see for a tier: they lay low over low subsoil for barren and
        /// low, medium over medium subsoil for medium, high and black earth; barren, high and black earth are then swapped.
        /// <paramref name="vanilla"/>: what soil rules 3 give (<see cref="Tier.Vanilla"/>).</summary>
        public int LayerFertility(Tier tier, int vanilla)
            => tier switch { Tier.Vanilla => vanilla, Tier.None => Math.Min(vanilla, NoneFertility), _ => fertility[(int)tier] };

        /// <summary>Whether vanilla's layers lay this tier's top block themselves (low, medium); the others are swapped in after
        /// them (barren, high, black earth).</summary>
        public static bool Swapped(Tier tier) => tier is Tier.Barren or Tier.High or Tier.BlackEarth;

        /// <summary>The fertility code of the top block vanilla's layers lay for a tier that is swapped afterwards.</summary>
        public static string LaidAs(Tier tier) => tier == Tier.Barren ? "low" : "medium";
    }
}
