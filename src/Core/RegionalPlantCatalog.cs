using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Native ranges for the ground-plant block patches of regional plant packs (Floral Zones). Pure and immutable after
    /// construction: providers supply only block codes, the game adapter (EarthPlants) supplies positions.
    /// A patch is either outside the catalogue (vanilla/unrelated mods: unchanged), climate-only (wild crops, plants
    /// without an identifiable range) or gated: it may only generate in the WGSRPD level-3 botanical countries of its
    /// species (WCVP native range) or, where no species range exists, of its pack's floristic region.
    /// </summary>
    public sealed class RegionalPlantCatalog
    {
        public const int RulesVersion = 1;
        /// <summary>Farthest (degrees) an aquatic patch in open water looks for the nearest botanical country.</summary>
        public const double WaterSearchDegrees = BotanicalRegions.WaterSearchDegrees;

        public sealed class Rule
        {
            public string Id { get; set; }
            public string Provider { get; set; }
            /// <summary>species (WCVP native range), packRegion (pack's floristic region) or climateOnly.</summary>
            public string Basis { get; set; }
            public string Name { get; set; }
            public string PackTaxon { get; set; }
            public string[] Taxa { get; set; }
            public string[] TaxonIds { get; set; }
            public string[] Regions { get; set; }
            public string PackRegion { get; set; }
            public string Reason { get; set; }
            /// <summary>Aquatic block: may stand in open water outside the botanical polygons.</summary>
            public bool Water { get; set; }
            public string[] Codes { get; set; }
            public string Note { get; set; }
            public bool ClimateOnly => Basis == "climateOnly";
        }
        public sealed class InventoryEntry
        {
            public string Provider { get; set; }
            public string File { get; set; }
            public int Index { get; set; }
            public string Comment { get; set; }
            public string Placement { get; set; }
            public string[] Codes { get; set; }
            public string[] Rules { get; set; }
            public string Basis { get; set; }
        }
        public sealed class ProviderInfo
        {
            public string Id { get; set; }
            public string Version { get; set; }
            public string Sha256 { get; set; }
            public int Patches { get; set; }
            public string FloristicRegion { get; set; }
        }
        public sealed class RuleFile
        {
            public int Version { get; set; }
            public ProviderInfo[] Providers { get; set; }
            public Dictionary<string, string[]> PackRegions { get; set; }
            public Rule[] Rules { get; set; }
            public InventoryEntry[] Inventory { get; set; }
        }
        public enum GateKind { Vanilla, Unknown, ClimateOnly, Gated }

        public BotanicalRegions Geography { get; }
        /// <summary>Rules of installed providers.</summary>
        public IReadOnlyList<Rule> Rules { get; }
        /// <summary>Reviewed patch inventory of installed providers.</summary>
        public IReadOnlyList<InventoryEntry> Inventory { get; }
        /// <summary>Reviewed provider releases that are installed.</summary>
        public IReadOnlyList<ProviderInfo> Providers { get; }
        public IReadOnlyDictionary<string, string[]> PackRegions { get; }

        readonly Dictionary<string, Rule> byCode = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Rule, RegionSet> ranges = new(ReferenceEqualityComparer.Instance);
        readonly HashSet<string> domains = new(StringComparer.OrdinalIgnoreCase);
        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public RegionalPlantCatalog(string rulesJson, string regionsJson, IEnumerable<string> installedProviders)
            : this(rulesJson, BotanicalRegions.Shared(regionsJson), installedProviders) { }

        public RegionalPlantCatalog(string rulesJson, BotanicalRegions geography, IEnumerable<string> installedProviders)
        {
            var file = JsonSerializer.Deserialize<RuleFile>(rulesJson, JsonOptions);
            if (file?.Version != RulesVersion || file.Rules == null || file.Providers == null)
                throw new ArgumentException("Unsupported regional plant rules version");
            Geography = geography ?? throw new ArgumentNullException(nameof(geography));
            var installed = new HashSet<string>(installedProviders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            Providers = file.Providers.Where(p => installed.Contains(p.Id)).ToArray();
            foreach (var p in Providers) domains.Add(p.Id);
            var rules = new List<Rule>();
            foreach (var r in file.Rules)
            {
                if (r?.Provider == null || r.Codes == null || r.Codes.Length == 0 || r.Taxa == null || r.Regions == null)
                    throw new ArgumentException("Invalid regional plant rule " + r?.Id);
                if (r.Basis != "species" && r.Basis != "packRegion" && r.Basis != "climateOnly")
                    throw new ArgumentException("Unknown regional plant rule basis " + r.Basis + " for " + r.Id);
                if (r.ClimateOnly != (r.Regions.Length == 0))
                    throw new ArgumentException("Regional plant rule " + r.Id + " must list regions unless climate-only");
                var range = Geography.Set(r.Regions, code => throw new ArgumentException("Unknown botanical region " + code + " in " + r.Id));
                if (!domains.Contains(r.Provider)) continue;
                foreach (string code in r.Codes)
                {
                    if (!code.StartsWith(r.Provider + ":", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Regional plant code " + code + " outside its provider " + r.Provider);
                    if (!byCode.TryAdd(code, r)) throw new ArgumentException("Conflicting regional plant code " + code);
                }
                ranges[r] = range;
                rules.Add(r);
            }
            Rules = rules;
            Inventory = (file.Inventory ?? Array.Empty<InventoryEntry>()).Where(e => domains.Contains(e.Provider)).ToArray();
            var packRegions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in file.PackRegions ?? new Dictionary<string, string[]>())
            {
                if (pair.Value.Any(c => Geography.IndexOf(c) < 0)) throw new ArgumentException("Unknown botanical region in pack region " + pair.Key);
                packRegions[pair.Key] = pair.Value;
            }
            PackRegions = packRegions;
        }

        /// <summary>Rule for an exact block code ("domain:path"), or null.</summary>
        public Rule Find(string code) => code != null && byCode.TryGetValue(code, out var r) ? r : null;

        /// <summary>True when the code belongs to an installed provider covered by this catalogue.</summary>
        public bool Covers(string code)
        {
            if (code == null) return false;
            int colon = code.IndexOf(':');
            if (colon <= 0) return false;
            // Startup only; avoid a substring by scanning the (few) provider domains.
            foreach (string d in domains)
                if (d.Length == colon && string.Compare(code, 0, d, 0, colon, StringComparison.OrdinalIgnoreCase) == 0) return true;
            return false;
        }

        /// <summary>
        /// Gate for one block patch. Vanilla: no code from a covered provider. Unknown: a covered provider's code without
        /// a reviewed rule, or a mix with uncovered codes (keeps vanilla rules). ClimateOnly: any climate-only species.
        /// </summary>
        public PlantGate CreateGate(IEnumerable<string> codes, bool waterPlacement, out GateKind kind)
        {
            kind = GateKind.Vanilla;
            if (codes == null) return null;
            bool covered = false, uncovered = false, unknown = false, climateOnly = false, water = waterPlacement;
            var range = Geography.EmptySet();
            var rules = new List<Rule>();
            foreach (string code in codes)
            {
                if (!Covers(code)) { uncovered = true; continue; }
                covered = true;
                var r = Find(code);
                if (r == null) { unknown = true; continue; }
                if (r.ClimateOnly) { climateOnly = true; continue; }
                range.UnionWith(ranges[r]);
                water |= r.Water;
                if (!rules.Contains(r)) rules.Add(r);
            }
            if (!covered) return null;
            if (unknown || uncovered) { kind = GateKind.Unknown; return null; }
            if (climateOnly) { kind = GateKind.ClimateOnly; return null; }
            kind = GateKind.Gated;
            return new PlantGate(range, water, rules.ToArray());
        }

        /// <summary>Botanical region index at a position (-1: none): <see cref="BotanicalRegions.At"/> for land or water.</summary>
        public int RegionAt(double lat, double lon, bool water)
            => Geography.At(lat, lon, water ? BotanicalRegions.Surface.Water : BotanicalRegions.Surface.Land);

        /// <summary>Convenience for reports/tests: does this block code's rule allow generation here?</summary>
        public bool Allows(string code, double lat, double lon, bool water = false)
        {
            var r = Find(code);
            if (r == null || r.ClimateOnly) return true;
            int region = RegionAt(lat, lon, water || r.Water);
            return ranges[r].Contains(region);
        }
    }

    /// <summary>Immutable allowed regions of one block patch.</summary>
    public sealed class PlantGate
    {
        readonly RegionSet range;
        public bool Water { get; }
        public IReadOnlyList<RegionalPlantCatalog.Rule> Rules { get; }
        /// <summary>Names of the plants the gate stands for (pack taxa, or placement-catalogue taxa).</summary>
        public IReadOnlyList<string> Names { get; }
        /// <summary>Gate from the placement catalogue (decision 0047) rather than a plant pack's rules.</summary>
        public bool Placement { get; }
        /// <summary>Placement-catalogue taxa: their Earth climate envelopes replace the patch's own climate limits.</summary>
        public IReadOnlyList<PlacementCatalogue.Taxon> Taxa { get; } = Array.Empty<PlacementCatalogue.Taxon>();
        public PlantGate(RegionSet range, bool water, RegionalPlantCatalog.Rule[] rules)
        {
            this.range = range ?? throw new ArgumentNullException(nameof(range));
            Water = water;
            Rules = rules ?? Array.Empty<RegionalPlantCatalog.Rule>();
            Names = Rules.Select(r => r.Taxa.Length > 0 ? r.Taxa[0] : r.Id).ToArray();
        }
        public PlantGate(RegionSet range, bool water, PlacementCatalogue.Taxon[] taxa)
        {
            this.range = range ?? throw new ArgumentNullException(nameof(range));
            Water = water;
            Rules = Array.Empty<RegionalPlantCatalog.Rule>();
            Taxa = taxa ?? Array.Empty<PlacementCatalogue.Taxon>();
            Names = Taxa.Select(t => t.Name).ToArray();
            HasCrop = Taxa.Any(t => t.Kind == "crop");
            Placement = true;
        }
        /// <summary>Earth climate at the site (yearly mean and coldest month °C, rain 0-255) inside any taxon's envelope.</summary>
        public bool ClimateAllows(double temp, double coldMonth, double rain, bool arable = true)
        {
            for (int i = 0; i < Taxa.Count; i++)
                if ((arable || Taxa[i].Kind != "crop") && PlacementCatalogue.ClimateAllows(Taxa[i], temp, coldMonth, rain)) return true;
            return false;
        }
        /// <summary>Whether any of the gate's taxa is a crop (the arable-land habitat applies).</summary>
        public bool HasCrop { get; }
        public bool Allows(int region) => range.Contains(region);
    }
}
