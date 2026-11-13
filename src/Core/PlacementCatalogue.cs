using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's placement catalogue (decisions 0049, 0047): where each taxon grew around 1318 and in which climate, baked by
    /// tools/bake_placement.py into assets/earth/config/placement.json. Taxa do not depend on mods; providers (the game,
    /// plant packs) only map their assets onto taxa. Pure and immutable after construction: the game adapters
    /// (EarthPlacement, EarthPlants) supply positions and climate.
    /// A taxon's range is a set of WGSRPD level-3 botanical countries (WCVP native range plus introductions established by
    /// 1318); its envelope bounds Earth's real yearly mean surface temperature (°C), the coldest month and rain (0-255).
    /// </summary>
    public sealed class PlacementCatalogue
    {
        public const int FileVersion = 1;
        /// <summary>Placement rules saved with a world (EarthParams.PlacementRules); 0: vanilla placement by climate.</summary>
        public const int RulesVersion = 1;

        public sealed class ClimateLimits
        {
            public double MinTemp { get; set; }
            public double MaxTemp { get; set; }
            public double MinRain { get; set; }
            public double MaxRain { get; set; }
            public double? MinColdMonth { get; set; }
            public string Basis { get; set; }
            public string Reason { get; set; }
        }
        public sealed class Introduction
        {
            public string[] Regions { get; set; }
            public string Period { get; set; }
            public string Source { get; set; }
        }
        public sealed class Taxon
        {
            public string Id { get; set; }
            public string Name { get; set; }
            /// <summary>fruitTree, crop, berry or wild (wild plants such as the vanilla cacti).</summary>
            public string Kind { get; set; }
            public string[] Scientific { get; set; }
            public string[] WcvpIds { get; set; }
            public string[] Native { get; set; }
            public string[] Regions { get; set; }
            public Introduction[] Introductions { get; set; }
            public ClimateLimits Climate { get; set; }
            public string Note { get; set; }
            /// <summary>Ranges step 5c: an annual that blooms on dry land while the ground is wet and dies back when it dries
            /// (EarthGroundMoisture).</summary>
            public bool Ephemeral { get; set; }
        }
        public sealed class Mapping
        {
            public string Provider { get; set; }
            /// <summary>fruitTree (Key: the game's fruit-tree type) or blockPatch (Prefix: block code path prefix).</summary>
            public string Path { get; set; }
            public string Key { get; set; }
            public string Prefix { get; set; }
            public string Taxon { get; set; }
        }
        /// <summary>An asset family the catalogue deliberately leaves alone (not an organism, or gated by another system).</summary>
        public sealed class Exemption
        {
            public string Provider { get; set; }
            public string Prefix { get; set; }
            public string Reason { get; set; }
            public string By { get; set; }
        }
        public sealed class FaunaNotes
        {
            /// <summary>Taxa (fauna-species.json "taxon") marked extinct there that still lived somewhere in 1318.</summary>
            public string[] AliveIn1318 { get; set; }
            public string Evidence { get; set; }
        }
        public sealed class CatalogueFile
        {
            public int Version { get; set; }
            public string Profile { get; set; }
            public Dictionary<string, JsonElement> Sources { get; set; }
            public Taxon[] Taxa { get; set; }
            public Mapping[] Mappings { get; set; }
            public Exemption[] NotOrganisms { get; set; }
            public Exemption[] ControlledElsewhere { get; set; }
            public FaunaNotes Fauna { get; set; }
        }
        public enum PatchKind
        {
            /// <summary>No code maps to the catalogue: another system's or the provider's own rules.</summary>
            Unrelated,
            /// <summary>Every code maps to a taxon: gated by the taxa's 1318 ranges.</summary>
            Mapped,
            /// <summary>Some codes map, others do not: kept as it is and reported.</summary>
            Partial,
            NotOrganism,
            ControlledElsewhere,
        }

        public BotanicalRegions Geography { get; }
        public string Profile { get; }
        public IReadOnlyList<Taxon> Taxa { get; }
        public IReadOnlyList<Mapping> Mappings { get; }
        public IReadOnlyList<Exemption> NotOrganisms { get; }
        public IReadOnlyList<Exemption> ControlledElsewhere { get; }
        public IReadOnlyCollection<string> AliveIn1318 { get; }

        readonly Dictionary<string, Taxon> byId = new(StringComparer.Ordinal);
        readonly Dictionary<Taxon, RegionSet> ranges = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, Taxon> fruitTrees = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Block-patch mappings by provider domain, longest prefix first.</summary>
        readonly Dictionary<string, (string Prefix, Taxon Taxon)[]> prefixes = new(StringComparer.OrdinalIgnoreCase);
        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public PlacementCatalogue(string json, string regionsJson) : this(json, BotanicalRegions.Shared(regionsJson)) { }

        public PlacementCatalogue(string json, BotanicalRegions geography)
        {
            var file = JsonSerializer.Deserialize<CatalogueFile>(json, JsonOptions);
            if (file?.Version != FileVersion || file.Taxa == null || file.Mappings == null)
                throw new ArgumentException("Unsupported placement catalogue version");
            Geography = geography ?? throw new ArgumentNullException(nameof(geography));
            Profile = file.Profile;
            foreach (var t in file.Taxa)
            {
                if (t?.Id == null || t.Regions == null || t.Regions.Length == 0 || t.Climate == null)
                    throw new ArgumentException("Invalid placement taxon " + t?.Id);
                if (t.Climate.MinTemp > t.Climate.MaxTemp || t.Climate.MinRain > t.Climate.MaxRain)
                    throw new ArgumentException("Empty climate envelope for " + t.Id);
                if (!byId.TryAdd(t.Id, t)) throw new ArgumentException("Duplicate placement taxon " + t.Id);
                ranges[t] = Geography.Set(t.Regions, code => throw new ArgumentException("Unknown botanical region " + code + " in " + t.Id));
            }
            var lists = new Dictionary<string, List<(string, Taxon)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in file.Mappings)
            {
                if (m?.Provider == null || m.Taxon == null || !byId.TryGetValue(m.Taxon, out var taxon))
                    throw new ArgumentException("Placement mapping to an unknown taxon " + m?.Taxon);
                if (m.Path == "fruitTree")
                {
                    if (m.Key == null || !fruitTrees.TryAdd(m.Key, taxon)) throw new ArgumentException("Duplicate or empty fruit-tree mapping " + m.Key);
                }
                else if (m.Path == "blockPatch")
                {
                    if (string.IsNullOrEmpty(m.Prefix)) throw new ArgumentException("Block-patch mapping without a prefix for " + m.Taxon);
                    if (!lists.TryGetValue(m.Provider, out var list)) lists[m.Provider] = list = new List<(string, Taxon)>();
                    if (list.Any(e => e.Item1.Equals(m.Prefix, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Duplicate block-patch mapping " + m.Provider + ":" + m.Prefix);
                    list.Add((m.Prefix, taxon));
                }
                else throw new ArgumentException("Unknown placement mapping path " + m.Path);
            }
            foreach (var pair in lists) prefixes[pair.Key] = pair.Value.OrderByDescending(e => e.Item1.Length).ToArray();
            Taxa = file.Taxa;
            Mappings = file.Mappings;
            NotOrganisms = file.NotOrganisms ?? Array.Empty<Exemption>();
            ControlledElsewhere = file.ControlledElsewhere ?? Array.Empty<Exemption>();
            AliveIn1318 = new HashSet<string>(file.Fauna?.AliveIn1318 ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The fauna section alone (EarthFauna reads it without building the region index).</summary>
        public static HashSet<string> ReadAliveIn1318(string json)
        {
            var file = JsonSerializer.Deserialize<CatalogueFile>(json, JsonOptions);
            if (file?.Version != FileVersion) throw new ArgumentException("Unsupported placement catalogue version");
            return new HashSet<string>(file.Fauna?.AliveIn1318 ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public Taxon Find(string id) => id != null && byId.TryGetValue(id, out var t) ? t : null;

        /// <summary>Taxon of a game fruit-tree type (BlockEntityFruitTreeBranch.TreeType), or null when unmapped.</summary>
        public Taxon FruitTree(string type) => type != null && fruitTrees.TryGetValue(type, out var t) ? t : null;

        /// <summary>Taxon of a block code ("domain:path") through the longest matching block-patch prefix, or null.</summary>
        public Taxon ForBlock(string code)
        {
            if (!Split(code, out string domain, out string path) || !prefixes.TryGetValue(domain, out var list)) return null;
            foreach (var (prefix, taxon) in list)
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return taxon;
            return null;
        }

        static bool Split(string code, out string domain, out string path)
        {
            domain = path = null;
            if (code == null) return false;
            int colon = code.IndexOf(':');
            domain = colon > 0 ? code.Substring(0, colon) : "game";
            path = colon >= 0 ? code.Substring(colon + 1) : code;
            return true;
        }

        static Exemption Match(IReadOnlyList<Exemption> list, string code)
        {
            if (!Split(code, out string domain, out string path)) return null;
            foreach (var e in list)
                if (string.Equals(e.Provider, domain, StringComparison.OrdinalIgnoreCase) && path.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>
        /// What the catalogue says about a block patch with these codes. Mapped: every code maps to a taxon (taxa: the
        /// distinct ones). Exemptions apply when every code is exempt; anything else unmapped is Unrelated.
        /// </summary>
        public PatchKind Classify(IEnumerable<string> codes, out Taxon[] taxa)
        {
            taxa = Array.Empty<Taxon>();
            if (codes == null) return PatchKind.Unrelated;
            var found = new List<Taxon>();
            int total = 0, mapped = 0, inert = 0, elsewhere = 0;
            foreach (string code in codes)
            {
                if (code == null) continue;
                total++;
                var t = ForBlock(code);
                if (t != null) { mapped++; if (!found.Contains(t)) found.Add(t); continue; }
                if (Match(NotOrganisms, code) != null) inert++;
                else if (Match(ControlledElsewhere, code) != null) elsewhere++;
            }
            if (total == 0) return PatchKind.Unrelated;
            if (mapped == total) { taxa = found.ToArray(); return PatchKind.Mapped; }
            if (mapped > 0) { taxa = found.ToArray(); return PatchKind.Partial; }
            if (inert == total) return PatchKind.NotOrganism;
            if (elsewhere == total) return PatchKind.ControlledElsewhere;
            return PatchKind.Unrelated;
        }

        /// <summary>Region gate of a mapped block patch (union of its taxa's 1318 ranges), or null when not mapped.</summary>
        public PlantGate CreateGate(IEnumerable<string> codes, bool water, out PatchKind kind)
        {
            kind = Classify(codes, out var taxa);
            if (kind != PatchKind.Mapped) return null;
            var range = Geography.EmptySet();
            foreach (var t in taxa) range.UnionWith(ranges[t]);
            return new PlantGate(range, water, taxa);
        }

        public bool InRange(Taxon t, int region) => t != null && ranges[t].Contains(region);

        /// <summary>Earth climate at the site (surface yearly mean and coldest month °C, rain 0-255) within the taxon's envelope.</summary>
        public static bool ClimateAllows(Taxon t, double temp, double coldMonth, double rain)
        {
            var c = t?.Climate;
            if (c == null) return false;
            return temp >= c.MinTemp && temp <= c.MaxTemp && rain >= c.MinRain && rain <= c.MaxRain
                && (c.MinColdMonth is not double cold || coldMonth >= cold);
        }

        public bool Allows(Taxon t, int region, double temp, double coldMonth, double rain)
            => InRange(t, region) && ClimateAllows(t, temp, coldMonth, rain);

        /// <summary>Region index for a land position (-1: outside the botanical countries).</summary>
        public int RegionAt(double lat, double lon) => Geography.FindLand(lat, lon);
    }
}
