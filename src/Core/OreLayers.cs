using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0060: the host layer each deposit may use in a ground column, whatever the ore choice. A layer's class comes
    /// from its part of the column (<see cref="GroundColumns.Part"/>), its source (cover recipe or bed unit) and its material;
    /// beds also carry an age and whether they are evaporite beds. Deposits keep their provider's host blocks; these rules
    /// only narrow where those hosts count. Pure data and lookups: the game side (EarthOreLayers) applies them.
    /// </summary>
    public sealed class OreLayers
    {
        /// <summary>The rules new worlds are made with (EarthParams.OreLayers). 2: a deposit block may also sit directly under a
        /// block its rule's <c>under</c> rules keep (EA-0059: vanilla's underclay, the fire clay under a coal seam). Worlds keep the
        /// version they were made with; each version is its own file. 3 (EA-0064, decision 0076): <c>surface</c> rules lay
        /// loose flint and chert on the ground of their cover (glacial till, flint shingle, karst). 4 (EA-0068, decision 0077):
        /// v3 plus the rule for Earth's kyanite, sillimanite and andalusite, whose deposits only v4 worlds keep.</summary>
        public const int RulesVersion = 4;
        /// <summary>The first version whose worlds keep Earth's aluminosilicate deposits (EarthOreLayers.AluminosilicateFile).</summary>
        public const int AluminosilicateVersion = 4;
        public static string RulesAsset(int version) => "earth:config/ore-layers-v" + version.ToString(CultureInfo.InvariantCulture) + ".json";
        /// <summary>A saved version this build reads (1 to <see cref="RulesVersion"/>); a newer or unknown one reads the newest.</summary>
        public static int Supported(int saved) => saved >= 1 && saved <= RulesVersion ? saved : RulesVersion;
        /// <summary>The version of this instance's rules file.</summary>
        public readonly int Version;

        public enum Class : byte { None, Alluvial, Outwash, Peat, Laterite, Saprolite, Playa, Cover, DeepCover, Bed, Basement }
        public static readonly string[] ClassNames = { "none", "alluvial", "outwash", "peat", "laterite", "saprolite", "playa", "cover", "deep-cover", "bed", "basement" };
        /// <summary>Bed ages, youngest first. Paleozoic: undivided (a generic template unit). Unknown passes every age rule.</summary>
        public enum Age : byte { Unknown, Cenozoic, Mesozoic, PaleozoicLate, Paleozoic, PaleozoicEarly, Precambrian }
        public static readonly string[] AgeNames = { "unknown", "cenozoic", "mesozoic", "paleozoic-late", "paleozoic", "paleozoic-early", "precambrian" };

        // ------------------------------------------------------------------ layer codes
        // bits 0-3 class, 4-6 age, 7 evaporite bed, 8-23 ground material + 1 (0: regional rock or no column)

        public static int Pack(Class c, Age a, bool evaporite, int material)
            => (int)c | (int)a << 4 | (evaporite ? 1 << 7 : 0) | (Math.Clamp(material + 1, 0, 0xFFFF) << 8);
        public static Class ClassOf(int code) => (Class)(code & 15);
        public static Age AgeOf(int code) => (Age)(code >> 4 & 7);
        public static bool IsEvaporite(int code) => (code & 128) != 0;
        /// <summary>The ground material (index into <see cref="GroundColumns.Materials"/>), -1 for regional rock.</summary>
        public static int MaterialOf(int code) => (code >> 8) - 1;
        public const int None = 0;

        // ------------------------------------------------------------------ deposit rules

        /// <summary>Where one group of deposits may go. Any: no layer rule (cave air).</summary>
        public sealed class Rule
        {
            public string Name;
            /// <summary>Position in <see cref="Rules"/> (a bit in per-column masks).</summary>
            public int Index;
            public bool Any;
            /// <summary>Bit per <see cref="Class"/>.</summary>
            public int Classes;
            /// <summary>Bit per <see cref="Age"/>; Unknown is always allowed.</summary>
            public int Ages = 0xFF;
            public bool Evaporite;
            /// <summary>Host rock names (block code after "rock-"), null for any.</summary>
            public HashSet<string> Rocks;
            /// <summary>Blocks below the column's top.</summary>
            public int MinDepth;
            /// <summary>Rules v2 (<c>under</c>): a bit per rule index; a block of this rule may also sit directly under a block one
            /// of these rules keeps, in any layer (vanilla's underclay below a coal seam). 0: none.</summary>
            public uint Under;
            public override string ToString() => Name;
        }

        /// <summary>Rules v3: loose stones on the ground of a column, by its cover recipe, top and top material. First match
        /// wins; <see cref="Chance"/> per column. Block: a block code ("game:looseflints-chalk-free"); <see cref="InWater"/>:
        /// only under water (river and sea floors), else only on dry ground.</summary>
        public sealed class SurfaceRule
        {
            public string Name, Block;
            public int Index;
            public HashSet<string> Covers, Tops;
            public bool[] Materials;
            public double Chance;
            public bool InWater;
            public override string ToString() => Name;
        }
        public readonly IReadOnlyList<SurfaceRule> Surface;

        sealed class ClassRule { public Class Class; public HashSet<GroundColumns.Part> Parts; public HashSet<string> Sources; public bool[] Materials; }

        readonly List<ClassRule> classRules = new();
        readonly Dictionary<string, Age> unitAges = new(StringComparer.Ordinal), eraAges = new(StringComparer.Ordinal);
        readonly HashSet<string> evaporiteProfiles = new(StringComparer.Ordinal), evaporiteRocks = new(StringComparer.Ordinal), sedimentaryRocks = new(StringComparer.Ordinal);
        readonly bool[] evaporiteMaterial;
        readonly Dictionary<string, Rule> byCode = new(StringComparer.Ordinal);
        public readonly Rule RockHostedDefault;
        public readonly IReadOnlyList<Rule> Rules;
        public readonly GroundColumns Model;
        public string Fingerprint { get; }

        static JsonDocument Parse(string json) => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        /// <param name="model">The world's ground columns: material, recipe and unit names are checked against its rules.</param>
        public OreLayers(string rulesJson, GroundColumns model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            using var doc = Parse(rulesJson);
            var root = doc.RootElement;
            Version = root.GetProperty("version").GetInt32();
            if (Version < 1 || Version > RulesVersion) throw new FormatException("ore layer rules version " + Version + " is not one this build reads (1 to " + RulesVersion + ")");
            var materials = model.Materials;
            static string[] Strings(JsonElement e, string name) => e.TryGetProperty(name, out var a) ? a.EnumerateArray().Select(x => x.GetString()).ToArray() : null;
            int Material(string name)
            {
                int i = Array.IndexOf(materials, name);
                if (i < 0) throw new KeyNotFoundException("ore layer rules use undefined ground material '" + name + "'");
                return i;
            }
            static Class ClassNamed(string name)
            {
                int i = Array.IndexOf(ClassNames, name);
                if (i <= 0) throw new KeyNotFoundException("unknown layer class '" + name + "'");
                return (Class)i;
            }
            static Age AgeNamed(string name)
            {
                int i = Array.IndexOf(AgeNames, name);
                if (i < 0) throw new KeyNotFoundException("unknown bed age '" + name + "'");
                return (Age)i;
            }

            foreach (var c in root.GetProperty("classes").EnumerateArray())
            {
                var rule = new ClassRule { Class = ClassNamed(c.GetProperty("class").GetString()) };
                var parts = Strings(c, "parts");
                if (parts != null)
                    rule.Parts = new HashSet<GroundColumns.Part>(parts.Select(p =>
                    {
                        int i = Array.IndexOf(GroundColumns.PartNames, p);
                        return i >= 0 ? (GroundColumns.Part)i : throw new KeyNotFoundException("unknown column part '" + p + "'");
                    }));
                var sources = Strings(c, "sources");
                if (sources != null) rule.Sources = new HashSet<string>(sources, StringComparer.Ordinal);
                var mats = Strings(c, "materials");
                if (mats != null) { rule.Materials = new bool[materials.Length]; foreach (var m in mats) rule.Materials[Material(m)] = true; }
                classRules.Add(rule);
            }
            foreach (var p in root.GetProperty("unitAges").EnumerateObject()) unitAges[p.Name] = AgeNamed(p.Value.GetString());
            foreach (var p in root.GetProperty("eraAges").EnumerateObject()) eraAges[p.Name] = AgeNamed(p.Value.GetString());
            foreach (var s in Strings(root, "evaporiteProfiles") ?? Array.Empty<string>()) evaporiteProfiles.Add(s);
            foreach (var s in Strings(root, "evaporiteRocks") ?? Array.Empty<string>()) evaporiteRocks.Add(s);
            foreach (var s in Strings(root, "sedimentaryRocks") ?? Array.Empty<string>()) sedimentaryRocks.Add(s);
            evaporiteMaterial = new bool[materials.Length];
            for (int i = 0; i < materials.Length; i++) evaporiteMaterial[i] = evaporiteRocks.Contains(materials[i]);

            Rule RuleOf(JsonElement e) => new Rule
            {
                Name = e.GetProperty("name").GetString(),
                Any = e.TryGetProperty("any", out var any) && any.GetBoolean(),
                Classes = (Strings(e, "classes") ?? Array.Empty<string>()).Aggregate(0, (acc, n) => acc | 1 << (int)ClassNamed(n)),
                Ages = Strings(e, "ages") is { } ages ? ages.Aggregate(1 << (int)Age.Unknown, (acc, n) => acc | 1 << (int)AgeNamed(n)) : 0xFF,
                Evaporite = e.TryGetProperty("evaporite", out var ev) && ev.GetBoolean(),
                Rocks = Strings(e, "rocks") is { } rocks ? new HashSet<string>(rocks, StringComparer.Ordinal) : null,
                MinDepth = e.TryGetProperty("minDepth", out var md) ? md.GetInt32() : 0,
            };
            var rules = new List<Rule>();
            var under = new List<(Rule Rule, string[] Names)>();
            foreach (var d in root.GetProperty("deposits").EnumerateArray())
            {
                var rule = RuleOf(d);
                rules.Add(rule);
                foreach (var code in Strings(d, "codes") ?? Array.Empty<string>())
                    if (!byCode.TryAdd(code, rule)) throw new FormatException("deposit code '" + code + "' has two layer rules");
                if (Strings(d, "under") is { } names)
                {
                    if (!OreLayerFacts.Under(Version)) throw new FormatException("'under' needs ore layer rules v2");
                    under.Add((rule, names));
                }
            }
            var surface = new List<SurfaceRule>();
            if (root.TryGetProperty("surface", out var sf))
            {
                if (!OreLayerFacts.Surface(Version)) throw new FormatException("'surface' needs ore layer rules v3");
                foreach (var e in sf.EnumerateArray())
                {
                    var r = new SurfaceRule
                    {
                        Name = e.GetProperty("name").GetString(),
                        Block = e.GetProperty("block").GetString(),
                        Index = surface.Count,
                        Covers = Strings(e, "covers") is { } cv ? new HashSet<string>(cv, StringComparer.Ordinal) : null,
                        Tops = Strings(e, "tops") is { } tp ? new HashSet<string>(tp, StringComparer.Ordinal) : null,
                        Chance = e.GetProperty("chance").GetDouble(),
                        InWater = e.TryGetProperty("inWater", out var iw) && iw.GetBoolean(),
                    };
                    if (!(r.Chance > 0 && r.Chance <= 1)) throw new FormatException("surface rule '" + r.Name + "': chance must be in (0, 1]");
                    if (Strings(e, "materials") is { } ms) { r.Materials = new bool[materials.Length]; foreach (var m in ms) r.Materials[Material(m)] = true; }
                    if (r.Covers != null)
                        foreach (var c in r.Covers)
                            if (!model.HasRecipe(c)) throw new KeyNotFoundException("surface rule '" + r.Name + "' names undefined cover recipe '" + c + "'");
                    surface.Add(r);
                }
                if (surface.Count > 254) throw new FormatException("at most 254 surface rules");
            }
            Surface = surface;
            RockHostedDefault = RuleOf(root.GetProperty("defaults").GetProperty("rockHosted"));
            rules.Add(RockHostedDefault);
            if (rules.Count > 32) throw new FormatException("at most 32 deposit rules");
            for (int i = 0; i < rules.Count; i++) rules[i].Index = i;
            foreach (var (rule, names) in under)
                foreach (var n in names)
                    rule.Under |= 1u << (rules.FirstOrDefault(r => r.Name == n) ?? throw new KeyNotFoundException("'under' names no rule '" + n + "'")).Index;
            Rules = rules;
            Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rulesJson))).Substring(0, 16);
        }

        /// <summary>The rule for a deposit code: named, else the rock-hosted default, else null (unchanged).</summary>
        public Rule RuleFor(string code, bool rockHosted)
            => code != null && byCode.TryGetValue(code, out var r) ? r : rockHosted ? RockHostedDefault : null;
        public bool Names(string code) => code != null && byCode.ContainsKey(code);

        /// <summary>Whether <paramref name="rule"/> lets a deposit block sit in layer <paramref name="code"/> at
        /// <paramref name="depth"/> blocks below the column's top, in host rock <paramref name="hostRock"/> (null: not a rock).
        /// Places outside ground columns (<see cref="None"/>) are never restricted.</summary>
        public static bool Allows(Rule rule, int code, int depth, string hostRock)
        {
            if (rule == null || rule.Any || code == None) return true;
            var cls = ClassOf(code);
            if ((rule.Classes & 1 << (int)cls) == 0) return false;
            if (depth < rule.MinDepth) return false;
            if (cls is Class.Bed or Class.Basement)
            {
                var age = AgeOf(code);
                if (age != Age.Unknown && (rule.Ages & 1 << (int)age) == 0) return false;
                if (rule.Evaporite && !IsEvaporite(code)) return false;
            }
            if (rule.Rocks != null && (hostRock == null || !rule.Rocks.Contains(hostRock))) return false;
            return true;
        }
        /// <summary>Rules v2: whether a block of <paramref name="rule"/> may stay directly under a block kept by the rules in
        /// <paramref name="keptAbove"/> (a bit per rule index), whatever its own layer.</summary>
        public static bool AllowsUnder(Rule rule, uint keptAbove) => rule != null && (rule.Under & keptAbove) != 0;

        // ------------------------------------------------------------------ classifying a column

        /// <summary>Rules v3: the surface rule for a built column (index + 1), 0 for none. Cover: the column's cover recipe; top:
        /// the source of its top segment (soil, peat, beach, riverbed, seabed); material: its uppermost layer below the topsoil.</summary>
        public int SurfaceRuleOf(GroundColumns.Column col)
        {
            if (Surface.Count == 0 || col.Segments == 0) return 0;
            string top = col.SegPart[0] == GroundColumns.Part.Top ? col.SegSource[0] : null;
            int material = -1;
            for (int i = 0; i < col.Segments; i++) if (col.SegMaterial[i] != Model.Topsoil) { material = col.SegMaterial[i]; break; }
            foreach (var r in Surface)
            {
                if (r.Covers != null && (col.Recipe == null || !r.Covers.Contains(col.Recipe))) continue;
                if (r.Tops != null && (top == null || !r.Tops.Contains(top))) continue;
                if (r.Materials != null && (material < 0 || material >= r.Materials.Length || !r.Materials[material])) continue;
                return r.Index + 1;
            }
            return 0;
        }

        /// <summary>The age of a template unit (bed source), unknown when the rules do not name it.</summary>
        public Age AgeOfUnit(string unit) => unit != null && unitAges.TryGetValue(unit, out var a) ? a : Age.Unknown;
        /// <summary>The age of a mapped bedrock era (Chorlton), unknown for ranges and missing data.</summary>
        public Age AgeOfEra(string era) => era != null && eraAges.TryGetValue(era, out var a) ? a : Age.Unknown;

        /// <summary>The layer code of segment <paramref name="i"/> of a built column.</summary>
        public int CodeOf(GroundColumns.Column col, int i)
        {
            var part = col.SegPart[i];
            string source = col.SegSource[i];
            int m = col.SegMaterial[i];
            var cls = Class.None;
            foreach (var r in classRules)
            {
                if (r.Parts != null && !r.Parts.Contains(part)) continue;
                if (r.Sources != null && (source == null || !r.Sources.Contains(source))) continue;
                if (r.Materials != null && (m < 0 || m >= r.Materials.Length || !r.Materials[m])) continue;
                cls = r.Class; break;
            }
            if (cls == Class.None) cls = part is GroundColumns.Part.Bed ? Class.Bed : part is GroundColumns.Part.Basement ? Class.Basement : Class.Cover;
            var age = part switch
            {
                GroundColumns.Part.Bed => AgeOfUnit(source),
                GroundColumns.Part.Basement => Age.Precambrian,
                _ => Age.Cenozoic,
            };
            bool evaporite = part == GroundColumns.Part.Bed && m >= 0 && m < evaporiteMaterial.Length && evaporiteMaterial[m] && age != Age.Precambrian;
            return Pack(cls, age, evaporite, m);
        }

        /// <summary>
        /// The layer code of regional rock below a hand-off column: a bed when the rock is sedimentary, else basement; its age
        /// is the column's bedrock era; an evaporite bed in regional geology's evaporitic sequence or when its rock is one of
        /// the evaporite rocks and not Precambrian.
        /// </summary>
        public int RegionalCode(string rock, Age age, bool evaporiticProfile)
        {
            bool sedimentary = IsSedimentary(rock);
            bool evaporite = sedimentary && (evaporiticProfile || evaporiteRocks.Contains(rock) && age != Age.Precambrian);
            return Pack(sedimentary ? Class.Bed : Class.Basement, age, evaporite, -1);
        }
        /// <summary>Whether regional geology's profile is an evaporitic sequence (all its beds are evaporite beds).</summary>
        public bool IsEvaporiteProfile(string profile) => profile != null && evaporiteProfiles.Contains(profile);
        /// <summary>Whether a rock (block code after "rock-") is one of the rules' sedimentary rocks (beds, not basement).</summary>
        public bool IsSedimentary(string rock) => rock != null && sedimentaryRocks.Contains(rock);

        /// <summary>"bed (paleozoic-late, evaporite) limestone" and the like, for commands.</summary>
        public string Describe(int code)
        {
            if (code == None) return "none";
            var sb = new StringBuilder(ClassNames[(int)ClassOf(code)]);
            if (ClassOf(code) is Class.Bed or Class.Basement)
            {
                sb.Append(" (").Append(AgeNames[(int)AgeOf(code)]);
                if (IsEvaporite(code)) sb.Append(", evaporite");
                sb.Append(')');
            }
            int m = MaterialOf(code);
            if (m >= 0 && m < Model.Materials.Length) sb.Append(' ').Append(Model.Materials[m]);
            return sb.ToString();
        }

        /// <summary>The column's segments with their classes, for <c>/earth geology</c>.</summary>
        public string DescribeColumn(GroundColumns.Column col, string regionalEra = null)
        {
            var sb = new StringBuilder();
            int start = 0;
            for (int i = 0; i < col.Segments; i++)
            {
                if (i > 0) sb.Append(" / ");
                int end = col.SegEnd[i];
                sb.Append(end == int.MaxValue
                    ? string.Format(CultureInfo.InvariantCulture, "{0} from block {1}", Describe(CodeOf(col, i)), start)
                    : string.Format(CultureInfo.InvariantCulture, "{0} {1} blk", Describe(CodeOf(col, i)), end - start));
                start = end;
            }
            if (col.HandOff) sb.Append(" / regional rock: beds or basement (").Append(AgeNames[(int)AgeOfEra(regionalEra)]).Append(')');
            return sb.ToString();
        }
    }
}
