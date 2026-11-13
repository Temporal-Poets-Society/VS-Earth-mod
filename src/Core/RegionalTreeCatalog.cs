using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    // Deliberately independent of game and content-mod assemblies. Providers only supply asset codes.
    // Immutable after construction; safe to share between world-generation threads.
    public sealed class RegionalTreeCatalog
    {
        /// <summary>
        /// 2: three-tier pick (native, regional analogue, pre-0.9 climate pick), Earth climate envelopes from each species'
        /// native range, spruces in the generic fir analogue, corrected elm/alder taxa, WCVP error exclusions,
        /// shared (not averaged) alias weights and blended botanical borders.
        /// </summary>
        public const int RulesVersion = 2;

        public sealed class Rule
        {
            public string Provider { get; set; }
            public string[] Generators { get; set; }
            public string[] Taxa { get; set; }
            public string[] TaxonIds { get; set; }
            public string[] Regions { get; set; }
            public string Note { get; set; }
            public bool Cultivated { get; set; }
            /// <summary>"coastal" (marine, intertidal), "riparian" (near rivers and lakes), "shore" (near the sea), "fresh".</summary>
            public string Water
            {
                get => water;
                set
                {
                    water = value;
                    WaterKind = value switch { null => WaterHabitat.None, "fresh" => WaterHabitat.Fresh, "coastal" => WaterHabitat.Coastal,
                        "riparian" => WaterHabitat.Riparian, "shore" => WaterHabitat.Shore, _ => WaterHabitat.Unknown };
                }
            }
            string water;
            /// <summary><see cref="Water"/> as read once (the habitat check runs per species and tree attempt).</summary>
            internal WaterHabitat WaterKind;
            /// <summary>Earth climate envelope (real yearly mean °C at the surface, VS rainfall 0..255). Replaces the
            /// provider's vanilla-scale temperature, rainfall, fertility and altitude limits when present.</summary>
            public float? MinTemperature { get; set; }
            public float? MaxTemperature { get; set; }
            public float? MinRain { get; set; }
            public float? MaxRain { get; set; }
            /// <summary>Limits on the real coldest/warmest monthly mean (°C), e.g. an oak's northern limit.</summary>
            public float? MinColdMonth { get; set; }
            public float? MaxColdMonth { get; set; }
            public float? MinWarmMonth { get; set; }
            public float? MaxWarmMonth { get; set; }
            public double? MinElevation { get; set; }
            public double? MaxElevation { get; set; }
            public double[][] Envelopes { get; set; }
            /// <summary>Earth forest-type group of a vanilla temperate/arid species ("fir", "pine", "oak" ...).</summary>
            public string Group { get; set; }
            /// <summary>Earth forest-type weighting for another provider's species (factor only; its envelope still applies).</summary>
            public string ForestType { get; set; }
            /// <summary>Multiplies the provider's selection weight (documented calibration).</summary>
            public float WeightScale { get; set; } = 1;
            /// <summary>Where the climate envelope comes from ("native-range" or "manual").</summary>
            public string Climate { get; set; }
            /// <summary>Skip vanilla's relative-height band (for lowland species whose band lies below sea level).</summary>
            public bool IgnoreHeight { get; set; }
            /// <summary>Finer natural range inside the file's <see cref="RuleFile.RangeGrid"/> (base64 of the deflated bit raster,
            /// north-west first, rows of <see cref="RangeGridInfo.Cols"/> bits): inside the grid the tree grows only where a bit
            /// is set; outside it the botanical regions alone decide. From chorological maps (e.g. Caudullo et al. 2017 for
            /// Europe), where a botanical country is too coarse (beech in the Baltic States only in Kaliningrad).</summary>
            public string RangeMask { get; set; }
            /// <summary>Where <see cref="RangeMask"/> comes from (citation and species).</summary>
            public string RangeSource { get; set; }
            /// <summary>Soil indicators (EIVE 1.0): niche position and width of moisture, nutrients and soil reaction; null
            /// outside Europe's flora (such a species fits every soil). See <see cref="SoilNiche"/>.</summary>
            public Indicators Eive { get; set; }

            internal float[] Niche;
            internal byte[] RangeBits;
            internal RangeGridInfo Grid;
            internal RegionSet RegionMask;
            /// <summary><see cref="Envelopes"/> as read (null for none).</summary>
            internal GeoBox[] Boxes;
            internal int Population = -1;
            internal int GroupIndex = -1, ForestTypeIndex = -1;
            public bool HasEarthClimate => MinTemperature != null && MaxTemperature != null;
        }

        internal enum WaterHabitat { None, Fresh, Coastal, Riparian, Shore, Unknown }

        /// <summary>EIVE niche position and width, each [position, width] on the 0..10 scale.</summary>
        public sealed class Indicators
        {
            public double[] M { get; set; }
            public double[] N { get; set; }
            public double[] R { get; set; }
        }

        public sealed class AnalogueMember
        {
            public string Provider { get; set; }
            public string[] Generators { get; set; }
            /// <summary>Relative weight of this stand-in within its analogue rule (replaces the provider weight).</summary>
            public float Weight { get; set; }
        }

        /// <summary>
        /// Regional analogue: designated stand-in trees for a biome where no native species of the installed content
        /// qualifies (or too few do). Restricted to listed botanical regions and to an Earth climate envelope. Trees only.
        /// </summary>
        public sealed class AnalogueRule
        {
            public string Name { get; set; }
            public string Note { get; set; }
            public string[] Regions { get; set; }
            public float MinTemperature { get; set; } = -60;
            public float MaxTemperature { get; set; } = 60;
            public float MinRain { get; set; }
            public float MaxRain { get; set; } = 255;
            /// <summary>Optional limits on the real coldest-month mean (°C).</summary>
            public float? MinColdMonth { get; set; }
            public float? MaxColdMonth { get; set; }
            /// <summary>Optional summer-drought limits (0..1, 1 = Mediterranean).</summary>
            public float? MinDrySummer { get; set; }
            public float? MaxDrySummer { get; set; }
            /// <summary>Optional latitude band (absolute degrees), e.g. 25 for the tropics.</summary>
            public float? MinAbsLatitude { get; set; }
            public float? MaxAbsLatitude { get; set; }
            /// <summary>Scale vanilla forest-type species by Earth's forest types (boreal, broadleaf ...) within this rule.</summary>
            public bool ForestTypes { get; set; }
            public AnalogueMember[] Members { get; set; }
            internal RegionSet RegionMask;
        }

        public sealed class RuleFile
        {
            public int Version { get; set; }
            public Rule[] Entries { get; set; }
            public AnalogueRule[] Analogues { get; set; }
            /// <summary>The grid of the entries' <see cref="Rule.RangeMask"/>s; null when no entry has one.</summary>
            public RangeGridInfo RangeGrid { get; set; }
        }

        /// <summary>A regular latitude-longitude grid over the area a chorological atlas covers (cell centres at
        /// West + (col + 0.5) Step, North - (row + 0.5) Step).</summary>
        public sealed class RangeGridInfo
        {
            public double West { get; set; }
            public double North { get; set; }
            public double Step { get; set; }
            public int Cols { get; set; }
            public int Rows { get; set; }
            public string Source { get; set; }
            public bool Contains(double lat, double lon) => lon >= West && lon < West + Cols * Step && lat <= North && lat > North - Rows * Step;
        }

        /// <summary>The grid all range masks use; null without any.</summary>
        public RangeGridInfo RangeGrid { get; private set; }

        /// <summary>Inside the range grid: whether the rule's range mask holds this point. Outside the grid, or without a mask: true.</summary>
        internal static bool InRange(Rule r, RangeGridInfo g, double lat, double lon)
        {
            if (r.RangeBits == null || g == null || !g.Contains(lat, lon)) return true;
            int col = Math.Min(g.Cols - 1, (int)((lon - g.West) / g.Step)), row = Math.Min(g.Rows - 1, (int)((g.North - lat) / g.Step));
            long n = (long)row * g.Cols + col;
            return (r.RangeBits[n >> 3] & (1 << (int)(n & 7))) != 0;
        }

        static byte[] Inflate(string base64)
        {
            using var input = new System.IO.MemoryStream(Convert.FromBase64String(base64));
            using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new System.IO.MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>A site for <see cref="Allows(Rule, Site)"/>, by region code (tests; world generation uses <see cref="TreeSite"/>).</summary>
        internal readonly struct Site
        {
            public readonly double Lat, Lon, Elevation;
            public readonly string Region;
            public readonly bool Underwater, Marine, SalineLake, OutsideEarth, Wet, Shore;
            public readonly int Depth;
            public Site(double lat, double lon, double elevation, string region, bool underwater = false,
                bool marine = false, bool salineLake = false, int depth = 0, bool outsideEarth = false, bool wet = false, bool shore = false)
            { Lat = lat; Lon = lon; Elevation = elevation; Region = region; Underwater = underwater;
              Marine = marine; SalineLake = salineLake; Depth = depth; OutsideEarth = outsideEarth; Wet = wet; Shore = shore; }
        }

        public BotanicalRegions Geography { get; }
        public IReadOnlyList<Rule> Rules { get; }
        /// <summary>
        /// EIVE's own range, where soil sorts the trees (<see cref="SoilNiche"/>): the WGSRPD level-3 regions of Europe
        /// (level 1, region 1). EIVE describes where a species grows in Europe. For a tree brought there from another
        /// continent (bald cypress, American sycamore) that is the ground it was planted on, not its home niche, and outside
        /// Europe a generic rule stands for other species (the vanilla fir in Iowa is the balsam fir, not the silver fir).
        /// </summary>
        public static readonly string[] EiveRegions =
        {
            "DEN", "FIN", "FOR", "GRB", "ICE", "IRE", "NOR", "SVA", "SWE", // Northern Europe
            "AUT", "BGM", "CZE", "GER", "HUN", "NET", "POL", "SWI", // Middle Europe
            "BAL", "COR", "FRA", "POR", "SAR", "SPA", // Southwestern Europe
            "ALB", "BUL", "GRC", "ITA", "KRI", "ROM", "SIC", "TUE", "YUG", // Southeastern Europe
            "BLR", "BLT", "KRY", "RUC", "RUE", "RUN", "RUS", "RUW", "UKR", // Eastern Europe
        };
        readonly RegionSet eiveMask;
        /// <summary>Whether the species' soil indicators sort the trees in this (blended) botanical region.</summary>
        public bool SoilIndicatorsApply(int region) => eiveMask.Contains(region);
        public IReadOnlyList<AnalogueRule> Analogues { get; }
        public string[] Providers { get; }
        /// <summary>Data problems that were skipped instead of failing (invalid or conflicting rules).</summary>
        public IReadOnlyList<string> Warnings { get; }
        public int PopulationCount { get; }
        readonly Dictionary<string, Rule> byGenerator = new(StringComparer.Ordinal);
        readonly Dictionary<string, (AnalogueRule Rule, AnalogueMember Member)[]> analoguesByGenerator = new(StringComparer.Ordinal);
        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        static readonly string[] WaterModes = { null, "fresh", "coastal", "riparian", "shore" };

        public RegionalTreeCatalog(string rulesJson, string regionsJson, IEnumerable<string> installedProviders)
        {
            var file = JsonSerializer.Deserialize<RuleFile>(rulesJson, JsonOptions);
            if (file?.Version != RulesVersion || file.Entries == null) throw new ArgumentException("Unsupported regional tree rules version " + file?.Version);
            Geography = BotanicalRegions.Shared(regionsJson);
            var warnings = new List<string>();
            var grid = file.RangeGrid;
            if (grid != null && !(grid.Step > 0 && grid.Cols > 0 && grid.Rows > 0)) { warnings.Add("Invalid range grid ignored"); grid = null; }
            RangeGrid = grid;
            var installed = new HashSet<string>(installedProviders ?? Array.Empty<string>(), StringComparer.Ordinal) { "vanilla" };
            var rules = new List<Rule>();
            var populations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in file.Entries)
            {
                if (r == null || !installed.Contains(r.Provider ?? "")) continue;
                if (r.Generators == null || r.Generators.Length == 0 || r.Regions == null || r.Taxa == null || r.TaxonIds == null)
                { warnings.Add("Skipped incomplete regional tree rule for " + r.Provider); continue; }
                if (!GeoBox.TryFrom(r.Envelopes, out var boxes))
                { warnings.Add("Skipped regional tree rule with an invalid geographic envelope: " + string.Join(" ", r.Generators)); continue; }
                r.Boxes = boxes;
                if (Array.IndexOf(WaterModes, r.Water) < 0)
                { warnings.Add("Skipped regional tree rule with an unknown water habitat: " + string.Join(" ", r.Generators)); continue; }
                var unknown = r.Regions.Where(c => !Geography.ContainsCode(c)).ToArray();
                if (unknown.Length > 0) warnings.Add("Unknown botanical regions ignored for " + string.Join(" ", r.Generators) + ": " + string.Join(" ", unknown));
                if (r.HasEarthClimate && !(r.MaxTemperature > r.MinTemperature)) { warnings.Add("Invalid temperature envelope ignored for " + r.Generators[0]); r.MinTemperature = r.MaxTemperature = null; }
                if (r.MinRain != null && r.MaxRain != null && !(r.MaxRain > r.MinRain)) { warnings.Add("Invalid rainfall envelope ignored for " + r.Generators[0]); r.MinRain = r.MaxRain = null; }
                if (!(r.WeightScale > 0) || !float.IsFinite(r.WeightScale)) { warnings.Add("Invalid weight scale ignored for " + r.Generators[0]); r.WeightScale = 1; }
                if (r.RangeMask != null)
                {
                    r.Grid = grid;
                    try { r.RangeBits = grid == null ? null : Inflate(r.RangeMask); }
                    catch (Exception e) when (e is FormatException || e is System.IO.InvalidDataException) { r.RangeBits = null; }
                    if (r.RangeBits == null || r.RangeBits.Length * 8L < (long)grid.Cols * grid.Rows)
                    { warnings.Add("Unreadable range mask ignored for " + r.Generators[0]); r.RangeBits = null; }
                }
                if (r.Eive != null && (r.Niche = SoilNiche.Niche(r.Eive)) == null) warnings.Add("Incomplete soil indicators ignored for " + r.Generators[0]);
                r.RegionMask = Geography.Set(r.Regions);   // (unknown codes warned about above)
                r.GroupIndex = RegionalTreePicker.GroupIndex(r.Group);
                r.ForestTypeIndex = RegionalTreePicker.GroupIndex(r.ForestType);
                string key = PopulationKey(r);
                if (!populations.TryGetValue(key, out int pop)) populations[key] = pop = populations.Count;
                r.Population = pop;
                bool any = false;
                foreach (string g in r.Generators)
                {
                    if (g == null) continue;
                    if (!byGenerator.TryAdd(g, r)) { warnings.Add("Conflicting regional tree generator kept its first rule: " + g); continue; }
                    any = true;
                }
                if (any) rules.Add(r);
            }
            PopulationCount = populations.Count;
            var analogues = new List<AnalogueRule>();
            var byGen = new Dictionary<string, List<(AnalogueRule, AnalogueMember)>>(StringComparer.Ordinal);
            foreach (var a in file.Analogues ?? Array.Empty<AnalogueRule>())
            {
                if (a?.Regions == null || a.Members == null || !(a.MaxTemperature > a.MinTemperature) || !(a.MaxRain >= a.MinRain))
                { warnings.Add("Skipped invalid analogue rule " + a?.Name); continue; }
                a.RegionMask = Geography.Set(a.Regions);
                bool any = false;
                foreach (var m in a.Members)
                {
                    if (m?.Generators == null || !(m.Weight > 0) || !installed.Contains(m.Provider ?? "")) continue;
                    foreach (string g in m.Generators)
                    {
                        if (g == null) continue;
                        if (!byGen.TryGetValue(g, out var list)) byGen[g] = list = new List<(AnalogueRule, AnalogueMember)>();
                        list.Add((a, m));
                        any = true;
                    }
                }
                if (any) analogues.Add(a);
            }
            foreach (var kv in byGen) analoguesByGenerator[kv.Key] = kv.Value.ToArray();
            Rules = rules;
            Analogues = analogues;
            Warnings = warnings;
            eiveMask = Geography.Set(EiveRegions);
            Providers = rules.Select(r => r.Provider).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Never throws: returns null and the reason when the data cannot be used (the caller falls back to the
        /// pre-0.9 climate pick).</summary>
        public static RegionalTreeCatalog TryLoad(string rulesJson, string regionsJson, IEnumerable<string> installedProviders, out string error)
        {
            try
            {
                error = null;
                return new RegionalTreeCatalog(rulesJson, regionsJson, installedProviders);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        public Rule Find(string generator) => generator != null && byGenerator.TryGetValue(generator, out var r) ? r : null;
        public bool NativeAt(string generator,in TreeSite site)
        {
            var rule=Find(generator);int region=Geography.FindLand(site.Lat,site.Lon);
            return rule!=null && !rule.Cultivated && region>=0 && rule.RegionMask[region] && Habitat(rule,site,true);
        }

        /// <summary>For /earth trees: the (blended) botanical region at a position, native candidates and analogue rules.</summary>
        public string Describe(double lat, double lon)
        {
            int region = Geography.FindBlended(lat, lon);
            string code = Geography.Code(region), exact = Geography.Code(Geography.FindLand(lat, lon));
            var natives = Rules.Where(r => !r.Cultivated && region >= 0 && r.RegionMask[region]).SelectMany(r => r.Taxa).Distinct().OrderBy(t => t, StringComparer.Ordinal);
            var analogues = Analogues.Where(a => region >= 0 && a.RegionMask[region]).Select(a => a.Name);
            return "Botanical region: " + Geography.Name(code) + " (" + code + ")" +
                (exact != code ? "; blended border zone, exact position in " + Geography.Name(exact) + " (" + exact + ")" : "") +
                ". Native candidates before local climate, elevation and water checks: " + string.Join(", ", natives) +
                ". Regional analogue rules (trees, where natives are missing or too few): " + string.Join("; ", analogues) +
                ". Providers: " + string.Join(", ", Providers);
        }
        internal (AnalogueRule Rule, AnalogueMember Member)[] FindAnalogues(string generator)
            => generator != null && analoguesByGenerator.TryGetValue(generator, out var a) ? a : null;

        /// <summary>Native range and habitat (without local climate). Cold/warm-month limits are not checked here. For tests: world
        /// generation goes through <see cref="RegionalTreePicker"/> and <see cref="NativeAt"/>.</summary>
        internal static bool Allows(Rule r, Site site)
        {
            // Unrelated providers retain their own rules. All rules here are natural generation only.
            if (r == null) return true;
            if (site.OutsideEarth || r.Cultivated || site.Region == null || !r.Regions.Contains(site.Region)) return false;
            var s = new TreeSite { Lat = site.Lat, Lon = site.Lon, Elevation = site.Elevation, Underwater = site.Underwater, Marine = site.Marine,
                SalineLake = site.SalineLake, Depth = site.Depth, Wet = site.Wet, Shore = site.Shore, ColdMonth = float.NaN, WarmMonth = float.NaN };
            return Habitat(r, s, true);
        }

        internal static bool Habitat(Rule r, in TreeSite s, bool geographicBoxes)
        {
            if (s.Elevation < r.MinElevation || s.Elevation > r.MaxElevation) return false;
            if (s.ColdMonth < r.MinColdMonth || s.ColdMonth > r.MaxColdMonth || s.WarmMonth < r.MinWarmMonth || s.WarmMonth > r.MaxWarmMonth) return false;
            // a finer natural range inside the chorological grid (beech in the Baltic States only at Kaliningrad's border)
            if (geographicBoxes && !InRange(r, r.Grid, s.Lat, s.Lon)) return false;
            if (geographicBoxes && r.Boxes != null && !GeoBox.Any(r.Boxes, s.Lat, s.Lon)) return false;
            if (r.WaterKind == WaterHabitat.Coastal)
            {
                // Marine/intertidal habitat: never saline inland lakes or deep ocean.
                return !(s.SalineLake || !s.Marine || (s.Underwater && s.Depth > 2));
            }
            if (s.Underwater) return !(s.Marine || s.SalineLake);
            // floodplain and lake-shore trees (black alder, poplars) only near fresh water; beach trees only near the sea
            if (r.WaterKind == WaterHabitat.Riparian && !s.Wet) return false;
            if (r.WaterKind == WaterHabitat.Shore && !s.Shore) return false;
            return true;
        }

        // One biological species (taxon set), without removing providers or increasing tree attempts.
        // Multi-species proxies remain distinct: their geographic meaning is intentionally broader.
        static string PopulationKey(Rule r) => string.Join("|", r.TaxonIds);
    }

    /// <summary>Game-independent copy of a registered TreeVariant (vanilla's mid/range derivation).</summary>
    public sealed class TreeVariantInfo
    {
        public const int HabitatLand = 0, HabitatLandWater = 1, HabitatWater = 2;
        public string Generator;
        public float Weight;
        public int Habitat;
        public int TempMid, RainMid, FertMid, ForestMid;
        public float TempRange, RainRange, FertRange, ForestRange, HeightMid, HeightRange;

        /// <summary>A registration from vanilla-style limits (tests; the game adapter copies the registered TreeVariant).</summary>
        internal static TreeVariantInfo FromLimits(string generator, float weight, int habitat, int minTemp = -40, int maxTemp = 40,
            int minRain = 0, int maxRain = 255, int minFert = 0, int maxFert = 255, int minForest = 0, int maxForest = 255,
            float minHeight = 0, float maxHeight = 1)
        {
            static float R(int min, int max) { float r = (max - min) / 2; return r == 0 ? 1 : r; }
            float h = (maxHeight - minHeight) / 2f;
            return new TreeVariantInfo
            {
                Generator = generator, Weight = weight, Habitat = habitat,
                TempMid = (minTemp + maxTemp) / 2, TempRange = R(minTemp, maxTemp),
                RainMid = (minRain + maxRain) / 2, RainRange = R(minRain, maxRain),
                FertMid = (minFert + maxFert) / 2, FertRange = R(minFert, maxFert),
                ForestMid = (minForest + maxForest) / 2, ForestRange = R(minForest, maxForest),
                HeightMid = (minHeight + maxHeight) / 2f, HeightRange = h == 0 ? 1 : h,
            };
        }
    }

    /// <summary>Vanilla-compatible random source; implemented as a struct by callers so the pick never allocates.</summary>
    public interface ITreeRandom { double NextDouble(); }

    /// <summary>One tree/shrub attempt. Temperature, rainfall, fertility, forest and relative height are vanilla's
    /// per-attempt values (in Earth worlds the temperature is the real yearly mean in °C).</summary>
    public struct TreeSite
    {
        public int Temp, Rain, Fert, Forest;
        /// <summary>Unboosted climate rainfall; a river green belt does not turn Sahara into Mediterranean forest.</summary>
        public float? AmbientRain;
        public float HeightRel;
        public double Lat, Lon, Elevation;
        /// <summary>Botanical region index (<see cref="BotanicalRegions.Code"/>), -1 for none.</summary>
        public int Region;
        public bool Underwater, Marine, SalineLake, OutsideEarth;
        /// <summary>In a river channel: never a tree or shrub (they would block boats), whatever its habitat.</summary>
        public bool RiverChannel;
        public int Depth;
        /// <summary>Near a river or lake (floodplain/shore trees); near the sea (beach trees).</summary>
        public bool Wet, Shore;
        /// <summary>Real coldest/warmest monthly mean (°C) at the surface; NaN = unknown (not checked).</summary>
        public float ColdMonth, WarmMonth;
        /// <summary>0..1 summer drought (1 = Mediterranean).</summary>
        public float DrySummer;
        /// <summary>Earth forest-type weights per group (<see cref="RegionalTreePicker.ForestTypeFactors"/>); null = 1.</summary>
        public float[] Factors;
        /// <summary>The soil here (<see cref="SoilNiche.Fill"/>); false: unknown, every soil fits.</summary>
        public bool HasSoil;
        /// <summary>EIVE moisture of the drained ground; shares (0..1) of waterlogged, nutrient-poor and grassland soils;
        /// topsoil pH (NaN: unknown).</summary>
        public float SoilMoisture, WetShare, PoorShare, GrassShare, SoilPh;
    }

    /// <summary>
    /// The tree/shrub species pick for Earth worlds, independent of game types. Built once per registered variant list
    /// (and catalogue); immutable and shared between threads; per-call scratch is thread-local.
    /// Regional mode, trees: (1) native species of the botanical region — where they are few (low effective diversity),
    /// the biome's regional analogues join them at a reduced share; (2) if no native qualifies, the analogues alone;
    /// (3) if still none, the pre-0.9 Earth climate pick among vanilla trees (never species the forest type rules out).
    /// Regional mode, shrubs: native species only (no shrub is better than a foreign one).
    /// Without a catalogue it is exactly the pre-0.9 climate pick over all registrations.
    /// </summary>
    public sealed class RegionalTreePicker
    {
        public const int GroupOther = 0, GroupFir = 1, GroupPine = 2, GroupLarch = 3, GroupBirch = 4, GroupOak = 5, GroupMaple = 6,
            GroupWalnut = 7, GroupArid = 8, Groups = 9;
        static readonly string[] GroupNames = { "other", "fir", "pine", "larch", "birch", "oak", "maple", "walnut", "arid" };
        public static int GroupIndex(string name) => name == null ? -1 : Array.IndexOf(GroupNames, name.ToLowerInvariant());
        public static string GroupName(int group) => GroupNames[group];
        /// <summary>Analogues fill at most this share of a monoculture native pool, less as native diversity rises.</summary>
        public const float MaxAnalogueBlend = 0.5f;
        /// <summary>Effective number of native species (inverse Simpson) from which analogues no longer blend in.</summary>
        public const float FullDiversity = 2.5f;

        /// <summary>Pre-0.9 forest-type groups of vanilla generators (used without a catalogue and by tier 3).</summary>
        static readonly Dictionary<string, int> legacyGroups = new(StringComparer.Ordinal)
        {
            ["game:fir"] = GroupFir,
            ["game:scotspine"] = GroupPine, ["game:mountainpine"] = GroupPine,
            ["game:larch"] = GroupLarch, ["game:deepforestlarch"] = GroupLarch,
            ["game:silverbirch"] = GroupBirch, ["game:riverbirch"] = GroupBirch, ["game:himalayanbirch"] = GroupBirch, ["game:dwarfbirch"] = GroupBirch,
            ["game:englishoak"] = GroupOak, ["game:oldenglishoak"] = GroupOak,
            ["game:norwaymaple"] = GroupMaple, ["game:sugarmaple"] = GroupMaple, ["game:sugarmaplesmall"] = GroupMaple, ["game:mountainmaple"] = GroupMaple,
            ["game:crimsonkingmaple"] = GroupMaple, ["game:japanesemaple"] = GroupMaple,
            ["game:walnut"] = GroupWalnut,
            ["game:acacia"] = GroupArid, ["game:deadacacia"] = GroupArid, ["game:ebony"] = GroupArid, ["game:bristleconepine"] = GroupArid,
            ["game:pricklymoses"] = GroupArid,
        };
        public static int LegacyGroup(string generator) => generator != null && legacyGroups.TryGetValue(generator, out int g) ? g : -1;

        //                                      Other Fir  Pine Larch Birch Oak  Maple Walnut Arid
        static readonly float[] Boreal        = { 1, 3.0f, 0.8f, 1.5f, 3.0f, 0.02f, 0.02f, 0, 0 };
        static readonly float[] Hemiboreal    = { 1, 1.6f, 1.6f, 0.3f, 2.2f, 0.3f, 0.25f, 0.05f, 0 };
        static readonly float[] Broadleaf     = { 1, 0.35f, 0.35f, 0.1f, 0.5f, 5.0f, 1.2f, 5.0f, 0 };
        static readonly float[] Mediterranean = { 1, 0.2f, 2.5f, 0, 0.1f, 5.0f, 0.2f, 0.5f, 0.3f };
        static readonly float[] Montane       = { 1, 3.0f, 1.5f, 1.5f, 0.8f, 0.3f, 0.5f, 0.1f, 0 };
        static readonly float[] HotDry        = { 0.3f, 0, 0.1f, 0, 0, 0.05f, 0, 0, 6.0f };
        // Regional trees (rules v2): the generic fir now also stands for the native spruces, so the hemiboreal and
        // boreal mixes are pine-led with spruce and birch (Latvia: pine 34%, birch 31%, spruce 18%; Finland: pine 50%,
        // spruce 30%, birch 17%).
        static readonly float[] HemiborealRegional = { 1, 0.5f, 2.4f, 0.3f, 1.1f, 0.9f, 0.6f, 0.05f, 0 };
        static readonly float[] BorealRegional     = { 1, 1.5f, 2.8f, 1.5f, 2.0f, 0.02f, 0.02f, 0, 0 };

        /// <summary>
        /// Earth forest-type weights per group from the real climate (sea-level-equivalent yearly mean, coldest and
        /// warmest month, summer drought, rainfall) and elevation: boreal, hemiboreal, temperate broadleaf,
        /// Mediterranean, montane and hot-dry mixes. Writes <see cref="Groups"/> values. <paramref name="regional"/>
        /// selects the regional-trees tables (pre-0.9 worlds keep the original ones).
        /// </summary>
        public static void ForestTypeFactors(double tMean, double tCold, double tWarm, double drySummer, double rain, double elev, float[] f,
            bool regional = true)
        {
            var hemiboreal = regional ? HemiborealRegional : Hemiboreal;
            var borealTable = regional ? BorealRegional : Boreal;
            double cold = tCold - ZoneDrivers.LapseRate * elev, warm = tWarm - ZoneDrivers.LapseRate * elev;
            double med = Geo.SmoothStep((drySummer - 0.3) / 0.4) * Geo.SmoothStep(cold / 4);
            double boreal = Geo.SmoothStep((-3 - cold) / 6) * (1 - med);
            double hemi = Geo.SmoothStep((cold + 8) / 3) * Geo.SmoothStep((-0.5 - cold) / 3) * (1 - med);
            double broad = Geo.SmoothStep((cold + 1.5) / 3) * Geo.SmoothStep((warm - 14) / 3) * (1 - med);
            // regional trees: montane conifer forest from about 450 m (Black Forest, Vosges, Alps foothills: spruce, fir, beech)
            double montane = (regional ? Geo.SmoothStep((elev - 450) / 600) : Geo.SmoothStep((elev - 700) / 600)) * Geo.SmoothStep((cold + 15) / 6);
            // hot and dry by the real rainfall (not the rivers' green belts: the Nile valley is still in a desert)
            double arid = Geo.SmoothStep((tMean - ZoneDrivers.LapseRate * elev - 17) / 5) * Geo.SmoothStep((90 - rain) / 40);
            med *= 1 - arid; broad *= 1 - arid; montane *= 1 - arid;
            double sum = med + boreal + hemi + broad + montane + arid;
            if (sum > 1)
            {
                med /= sum; boreal /= sum; hemi /= sum; broad /= sum; montane /= sum; arid /= sum;
                sum = 1;
            }
            for (int i = 0; i < Groups; i++)
            {
                f[i] = (float)(med * Mediterranean[i] + boreal * borealTable[i] + hemi * hemiboreal[i] + broad * Broadleaf[i] + montane * Montane[i]
                    + arid * HotDry[i] + (1 - sum));
            }
        }

        struct Analogue
        {
            public RegionalTreeCatalog.AnalogueRule Rule;
            public float Weight;      // member weight × this alias' share of the member's provider weight
        }

        sealed class Entry
        {
            public TreeVariantInfo V;
            public RegionalTreeCatalog.Rule Rule;
            public int Group = -1, LegacyGroup = -1, ForestType = -1, Population;
            public bool Tier3;
            /// <summary>Its generator and rule, read once per picker: a tree of hot deserts (RegionalTreePicker.DesertTree); a
            /// floodplain tree.</summary>
            public bool DesertTree, Riparian;
            public float Scale = 1;
            // Earth envelope of this registration: the provider's own band within its species' registrations, mapped
            // onto the species' Earth envelope (keeps e.g. a warm-climate variant with a higher weight warm)
            public float MinT, MaxT, MinR = -1, MaxR = 999;
            public Analogue[] Analogues;
        }

        readonly Entry[] entries;
        readonly RegionalTreeCatalog catalog;
        readonly bool shrubs;
        readonly int populations;
        // per botanical region: tier-1 entries (native rule includes the region, or unmapped) and tier-2 entries, in registration order
        readonly int[][] nativeByRegion, analogueByRegion;
        readonly bool[] wetByRegion, shoreByRegion;
        readonly int[] unmapped, tier3, all;
        public int Count => entries.Length;
        public RegionalTreeCatalog Catalog => catalog;
        public bool Shrubs => shrubs;
        /// <summary>A land variant has a coastal (marine) rule, so land attempts need the shore check.</summary>
        public bool NeedsShoreOnLand { get; }
        /// <summary>Native candidates in this region depend on <see cref="TreeSite.Wet"/>.</summary>
        public bool NeedsWet(int region) => wetByRegion != null && region >= 0 && wetByRegion[region];
        /// <summary>Native candidates in this region depend on <see cref="TreeSite.Shore"/>.</summary>
        public bool NeedsShore(int region) => shoreByRegion != null && region >= 0 && shoreByRegion[region];

        public RegionalTreePicker(RegionalTreeCatalog catalog, IReadOnlyList<TreeVariantInfo> variants, bool shrubs = false)
        {
            this.catalog = catalog;
            this.shrubs = shrubs;
            entries = new Entry[variants.Count];
            var extraPops = new Dictionary<string, int>(StringComparer.Ordinal);
            int basePops = catalog?.PopulationCount ?? 0;
            for (int i = 0; i < entries.Length; i++)
            {
                var v = variants[i];
                var e = entries[i] = new Entry { V = v, LegacyGroup = LegacyGroup(v.Generator) };
                e.Rule = catalog?.Find(v.Generator);
                e.DesertTree = DesertTree(v.Generator, e.Rule);
                e.Riparian = e.Rule?.WaterKind == RegionalTreeCatalog.WaterHabitat.Riparian;
                if (catalog == null) { e.Group = e.LegacyGroup; e.Tier3 = true; continue; }
                e.Group = e.Rule != null ? e.Rule.GroupIndex : e.LegacyGroup;
                if (e.Rule != null)
                {
                    e.Population = e.Rule.Population;
                    e.Scale = e.Rule.WeightScale;
                    if (e.Group < 0) e.ForestType = e.Rule.ForestTypeIndex;
                }
                else
                {
                    if (!extraPops.TryGetValue(v.Generator ?? "", out int p)) extraPops[v.Generator ?? ""] = p = basePops + extraPops.Count;
                    e.Population = p;
                }
                e.Tier3 = e.Rule == null || (e.Rule.Provider == "vanilla" && !e.Rule.Cultivated);
                if (e.Rule != null && e.Rule.Water == "coastal" && v.Habitat != TreeVariantInfo.HabitatWater) NeedsShoreOnLand = true;
            }
            populations = basePops + extraPops.Count;
            all = Enumerable.Range(0, entries.Length).ToArray();
            tier3 = all.Where(i => entries[i].Tier3).ToArray();
            if (catalog == null) return;
            MapProviderBands();
            // analogue members (trees only): weight split between a member's aliases by their provider weights
            foreach (var e in entries)
            {
                if (shrubs || e.Rule?.Cultivated == true) continue;
                var found = catalog.FindAnalogues(e.V.Generator);
                if (found == null) continue;
                var list = new List<Analogue>();
                foreach (var (rule, member) in found)
                {
                    float max = 0;
                    foreach (var o in entries)
                        if (Array.IndexOf(member.Generators, o.V.Generator) >= 0) max = Math.Max(max, o.V.Weight);
                    if (max <= 0 || e.V.Weight <= 0) continue;
                    list.Add(new Analogue { Rule = rule, Weight = member.Weight * e.V.Weight / max });
                }
                if (list.Count > 0) e.Analogues = list.ToArray();
            }
            int regions = catalog.Geography.Count;
            unmapped = all.Where(i => entries[i].Rule == null).ToArray();
            nativeByRegion = new int[regions][];
            analogueByRegion = new int[regions][];
            wetByRegion = new bool[regions];
            shoreByRegion = new bool[regions];
            for (int r = 0; r < regions; r++)
            {
                nativeByRegion[r] = all.Where(i => entries[i].Rule == null || entries[i].Rule.RegionMask[r]).ToArray();
                analogueByRegion[r] = all.Where(i => entries[i].Analogues?.Any(a => a.Rule.RegionMask[r]) == true).ToArray();
                wetByRegion[r] = nativeByRegion[r].Any(i => entries[i].Rule?.Water == "riparian");
                shoreByRegion[r] = nativeByRegion[r].Any(i => entries[i].Rule?.Water == "shore");
            }
        }

        /// <summary>
        /// A provider may register one species several times with different climate bands and weights (Floral Zones'
        /// black alder: weight 40 at 8..16, weight 10 at 2..16). The species' Earth envelope replaces the provider's
        /// vanilla-scale climate scale; each registration keeps its relative position inside it.
        /// </summary>
        void MapProviderBands()
        {
            foreach (var group in entries.Where(e => e.Rule != null && e.Group < 0 && e.Rule.HasEarthClimate).GroupBy(e => e.Rule))
            {
                var r = group.Key;
                float uMinT = group.Min(e => e.V.TempMid - e.V.TempRange), uMaxT = group.Max(e => e.V.TempMid + e.V.TempRange);
                float uMinR = group.Min(e => e.V.RainMid - e.V.RainRange), uMaxR = group.Max(e => e.V.RainMid + e.V.RainRange);
                foreach (var e in group)
                {
                    (e.MinT, e.MaxT) = Map(e.V.TempMid - e.V.TempRange, e.V.TempMid + e.V.TempRange, uMinT, uMaxT, r.MinTemperature.Value, r.MaxTemperature.Value, 3);
                    if (r.MinRain != null && r.MaxRain != null)
                        (e.MinR, e.MaxR) = Map(e.V.RainMid - e.V.RainRange, e.V.RainMid + e.V.RainRange, uMinR, uMaxR, r.MinRain.Value, r.MaxRain.Value, 20);
                }
            }
        }

        static (float, float) Map(float vMin, float vMax, float uMin, float uMax, float eMin, float eMax, float minWidth)
        {
            if (uMax - uMin < 0.5f || (vMin <= uMin && vMax >= uMax)) return (eMin, eMax);
            float a = eMin + (vMin - uMin) / (uMax - uMin) * (eMax - eMin);
            float b = eMin + (vMax - uMin) / (uMax - uMin) * (eMax - eMin);
            if (b - a < minWidth) { float m = (a + b) / 2; a = Math.Max(eMin, m - minWidth / 2); b = Math.Min(eMax, m + minWidth / 2); }
            return (a, b);
        }

        sealed class Scratch
        {
            public int[] Index = new int[0], Pop = new int[0], PopStamp = new int[0];
            public float[] W = new float[0], Size = new float[0], P = new float[0], PopMax = new float[0], PopSum = new float[0], Fit = new float[0];
            public int Stamp;
            public void Ensure(int n, int pops)
            {
                if (Index.Length < n) { Index = new int[n]; Pop = new int[n]; W = new float[n]; Size = new float[n]; P = new float[n]; Fit = new float[n]; }
                if (PopStamp.Length < pops) { PopStamp = new int[pops]; PopMax = new float[pops]; PopSum = new float[pops]; }
            }
            public int NextStamp()
            {
                if (++Stamp == int.MaxValue) { Array.Clear(PopStamp); Stamp = 1; }
                return Stamp;
            }
        }
        [ThreadStatic] static Scratch scratch;

        static Scratch GetScratch(int n, int pops)
        {
            var s = scratch ??= new Scratch();
            s.Ensure(n, pops);
            return s;
        }

        static bool HabitatOk(int habitat, bool underwater) => underwater ? habitat != TreeVariantInfo.HabitatLand : habitat != TreeVariantInfo.HabitatWater;

        bool TierAvailable(int tier, in TreeSite s)
        {
            if (catalog == null) return tier == 3;
            if (shrubs) return tier == 1;
            if (tier == 2) return s.Region >= 0 && !s.OutsideEarth;
            // tier 3 never puts trees into the sea or saline lakes (pre-0.9 Earth did not either)
            if (tier == 3) return !(s.Underwater && (s.Marine || s.SalineLake));
            return true;
        }

        int[] TierList(int tier, in TreeSite s)
        {
            if (catalog == null) return all;
            if (tier == 1) return s.Region >= 0 && !s.OutsideEarth ? nativeByRegion[s.Region] : unmapped;
            if (tier == 2) return analogueByRegion[s.Region];
            return tier3;
        }

        /// <summary>Collects the tier's candidates (pass probability, weight, size weight) into the scratch arrays from
        /// <paramref name="at"/>. No randomness.</summary>
        int Collect(int tier, in TreeSite s, Scratch sc, int at)
        {
            int n = at;
            double typedRaw = 0, typedScaled = 0;
            bool soilFit = false;
            var list = TierList(tier, s);
            // Broad botanical native ranges are not evidence for a temperate tree in a hot desert.
            // Apply the ambient aridity and native habitat limits to every selection tier worldwide.
            bool hotDesert = MountainEcology.HotDesert(s.AmbientRain ?? s.Rain, s.WarmMonth, s.DrySummer);
            foreach (int i in list)
            {
                var e = entries[i];
                var v = e.V;
                if (hotDesert
                    && (!(e.DesertTree || s.Wet && e.Riparian)
                        || catalog != null && (e.Rule == null || s.Region < 0 || !e.Rule.RegionMask[s.Region] || !RegionalTreeCatalog.Habitat(e.Rule,s,true)))) continue;
                if (!HabitatOk(v.Habitat, s.Underwater)) continue;
                float p, w, size, fit = -1;
                if (tier == 1)
                {
                    var r = e.Rule;
                    if (r != null)
                    {
                        if (r.Cultivated || !RegionalTreeCatalog.Habitat(r, s, true)) continue;
                        float suit, raw;
                        // soil: EIVE-rated species by how well this ground suits them (redistributed below)
                        if (r.Niche != null && s.HasSoil && catalog != null && catalog.SoilIndicatorsApply(s.Region)) { fit = SoilNiche.Fit(r.Niche, s); soilFit = true; }
                        float soil = fit < 0 ? 1f : fit;
                        if (e.Group >= 0)
                        {
                            bool desertAcacia = DesertAcacia(v.Generator, s);
                            if (!EvalGroup(v, e.Group, desertAcacia ? 8 : r.MinTemperature, desertAcacia ? 40 : r.MaxTemperature,
                                s, out p, out suit, desertAcacia || r.IgnoreHeight)) continue;
                            raw = v.Weight / 100f * suit * e.Scale * soil;
                            w = raw * Factor(s, e.Group);
                            if (w > 0 && p > 0) { typedRaw += raw; typedScaled += w; }
                        }
                        else if (r.HasEarthClimate)
                        {
                            if (!EvalEarth(v, e.MinT, e.MaxT, e.MinR, e.MaxR, s, out p, out suit)) continue;
                            raw = v.Weight / 100f * suit * e.Scale * soil;
                            w = e.ForestType >= 0 ? raw * Factor(s, e.ForestType) : raw;
                            if (e.ForestType >= 0 && w > 0 && p > 0) { typedRaw += raw; typedScaled += w; }
                        }
                        else
                        {
                            if (!EvalVanilla(v, null, null, s, out p, out suit)) continue;
                            w = v.Weight / 100f * suit * e.Scale * soil;
                        }
                    }
                    else
                    {
                        // unmapped generator: its provider's own rules (pre-0.9 Earth handling)
                        if (!EvalLegacy(v, e.LegacyGroup, s, 0, out p, out w)) continue;
                    }
                    size = fit > 0 ? w / fit : w; // tree size by climate, not soil
                }
                else if (tier == 2)
                {
                    w = 0; p = 0; size = 0;
                    foreach (var a in e.Analogues)
                    {
                        var ar = a.Rule;
                        if (!ar.RegionMask[s.Region] || s.Rain < ar.MinRain || s.Rain > ar.MaxRain) continue;
                        if (s.ColdMonth < ar.MinColdMonth || s.ColdMonth > ar.MaxColdMonth) continue;
                        if (s.DrySummer < ar.MinDrySummer || s.DrySummer > ar.MaxDrySummer) continue;
                        double absLat = Math.Abs(s.Lat);
                        if (absLat < ar.MinAbsLatitude || absLat > ar.MaxAbsLatitude) continue;
                        // the species' own elevation, cold/warm-month and water limits still apply; its geographic boxes do not
                        if (e.Rule != null && !RegionalTreeCatalog.Habitat(e.Rule, s, false)) continue;
                        float ap, suit, aw;
                        if (e.Group >= 0)
                        {
                            if (!EvalGroup(v, e.Group, ar.MinTemperature, ar.MaxTemperature, s, out ap, out suit, e.Rule?.IgnoreHeight == true)) continue;
                            aw = a.Weight * (ar.ForestTypes ? Factor(s, e.Group) : 1f) * suit;
                        }
                        else
                        {
                            if (!EvalEarth(v, ar.MinTemperature, ar.MaxTemperature, ar.MinRain, ar.MaxRain, s, out ap, out suit)) continue;
                            aw = a.Weight * suit;
                        }
                        if (aw > w) { w = aw; p = ap; size = v.Weight / 100f * suit; }
                    }
                }
                else
                {
                    // the climate fallback never uses a species that the forest type rules out (oak in the taiga or desert)
                    if (!EvalLegacy(v, e.LegacyGroup, s, catalog == null ? 0 : 0.15f, out p, out w)) continue;
                    size = w;
                }
                if (w <= 0 || p <= 0) continue;
                sc.Index[n] = i; sc.W[n] = w; sc.Size[n] = size; sc.P[n] = p; sc.Pop[n] = entries[i].Population; sc.Fit[n] = fit;
                n++;
            }
            if (typedScaled > typedRaw && typedRaw > 0)
            {
                // Earth's forest types redistribute weight among species of a forest type (oak ×5 in broadleaf forest,
                // fir ×3 in the taiga); they must not also crowd out the other native species, so the typed species
                // together keep the weight they would have without the factors.
                float scale = (float)(typedRaw / typedScaled);
                for (int k = at; k < n; k++)
                {
                    var e = entries[sc.Index[k]];
                    if (e.Rule != null && (e.Group >= 0 || e.ForestType >= 0)) sc.W[k] *= scale;
                }
            }
            if (soilFit)
            {
                // Soil sorts the rated species (pine on sand, alder in the wet) but does not take forest from them: together
                // they keep the weight they would have on any soil, so the unrated (non-European) species keep their share.
                double fitted = 0, unfitted = 0;
                for (int k = at; k < n; k++)
                    if (sc.Fit[k] > 0) { fitted += sc.W[k]; unfitted += sc.W[k] / sc.Fit[k]; }
                if (fitted > 0)
                {
                    float back = (float)(unfitted / fitted);
                    for (int k = at; k < n; k++) if (sc.Fit[k] > 0) sc.W[k] *= back;
                }
            }
            return n - at;
        }

        static float Factor(in TreeSite s, int group) => s.Factors == null ? 1f : s.Factors[group];

        static bool DesertTree(string generator, RegionalTreeCatalog.Rule rule)
        {
            if (generator == "game:acacia" || generator == "game:deadacacia") return true;
            // Supported native species retain their ordinary range and habitat checks. No conifer stand-in.
            if(rule?.Taxa==null)return false;
            foreach(string t in rule.Taxa)
                if(t.StartsWith("Vachellia ",StringComparison.Ordinal) || t.StartsWith("Acacia ",StringComparison.Ordinal)
                    || t.StartsWith("Tamarix ",StringComparison.Ordinal) || t=="Olea europaea"
                    || t=="Olea europaea subsp. laperrinei" || t=="Cupressus dupreziana")return true;
            return false;
        }

        /// <summary>Pre-0.9 Earth rule: forest-type weights for vanilla temperate/arid species, vanilla's rule otherwise.</summary>
        static bool EvalLegacy(TreeVariantInfo v, int group, in TreeSite s, float minFactor, out float p, out float w)
        {
            float suit;
            if (DesertAcacia(v.Generator, s))
            {
                if (!EvalGroup(v, GroupArid, 8, 40, s, out p, out suit, true)) { w=0; return false; }
                w = v.Weight / 100f * suit;
                return true;
            }
            if (group >= 0)
            {
                float f = Factor(s, group);
                if (f < minFactor || !EvalGroup(v, group, null, null, s, out p, out suit)) { p = 0; w = 0; return false; }
                w = v.Weight / 100f * f * suit;
                return true;
            }
            if (!EvalVanilla(v, null, null, s, out p, out suit)) { w = 0; return false; }
            w = v.Weight / 100f * suit;
            return true;
        }

        static bool DesertAcacia(string generator, in TreeSite s) => (generator == "game:acacia" || generator == "game:deadacacia")
            && MountainEcology.HotDesert(s.AmbientRain ?? s.Rain, s.WarmMonth,s.DrySummer);

        /// <summary>Earth forest-type rule: only vanilla's temperature and altitude limits (or an explicit Earth temperature
        /// envelope); its fertility and forest-density limits would exclude oak and maple from dense forests.</summary>
        static bool EvalGroup(TreeVariantInfo v, int group, float? minT, float? maxT, in TreeSite s, out float p, out float suit, bool ignoreHeight = false)
        {
            p = 1; suit = 0;
            float dT = Math.Abs(s.Temp - v.TempMid) / v.TempRange;
            if (minT != null && maxT != null)
            {
                float min = minT.Value, max = maxT.Value;
                if (s.Temp < min || s.Temp > max) return false;
                dT = Math.Abs(s.Temp - (min + max) / 2) / ((max - min) / 2);
            }
            float dH = ignoreHeight ? 0 : Math.Abs(s.HeightRel - v.HeightMid) / v.HeightRange;
            if (group == GroupArid)
            {
                // vanilla only grows acacia from 28 °C yearly mean (the Nile valley is 25 °C): wider temperature
                // limits, but not in the wet tropics
                float dR = Math.Abs(s.Rain - v.RainMid) / v.RainRange;
                if (dT > 2.5f || dH > 1.1f || dR > 1.5f && !DesertAcacia(v.Generator, s)) return false;
                suit = Math.Clamp(1.2f - 0.3f * (dT + dH), 0.3f, 1f);
            }
            else
            {
                if (dT > 1.1f || dH > 1.1f) return false;
                suit = Math.Clamp(1.2f - 0.5f * (dT + dH), 0.3f, 1f);
            }
            return true;
        }

        /// <summary>Vanilla's rule (random rejection outside the comfort ranges).</summary>
        static bool EvalVanilla(TreeVariantInfo v, float? minT, float? maxT, in TreeSite s, out float p, out float suit)
        {
            float dT = Math.Abs(s.Temp - v.TempMid) / v.TempRange;
            if (minT != null && maxT != null)
            {
                float min = minT.Value, max = maxT.Value;
                if (s.Temp < min || s.Temp > max) { p = 0; suit = 0; return false; }
                dT = Math.Abs(s.Temp - (min + max) / 2) / ((max - min) / 2);
            }
            float dH = Math.Abs(s.HeightRel - v.HeightMid) / v.HeightRange;
            float dF = Math.Abs(s.Fert - v.FertMid) / v.FertRange;
            float dR = Math.Abs(s.Rain - v.RainMid) / v.RainRange;
            float dFo = Math.Abs(s.Forest - v.ForestMid) / v.ForestRange;
            double reject = Math.Max(0, 1.2 * dF * dF - 1) + Math.Max(0, 1.2 * dR * dR - 1) + Math.Max(0, 1.2 * dT * dT - 1)
                + Math.Max(0, 1.2 * dFo * dFo - 1) + Math.Max(0, 1.2 * dH * dH - 1);
            p = (float)Math.Clamp(1 - reject, 0, 1);
            suit = Math.Clamp(1f - (dF + dR + dT + dFo + dH) / 5f, 0f, 1f);
            return p > 0;
        }

        /// <summary>
        /// Earth climate envelope (real °C, VS rainfall) in place of the provider's vanilla-scale temperature, rainfall,
        /// fertility and altitude limits; the provider's forest-density preference (open vs closed stands) is kept.
        /// </summary>
        static bool EvalEarth(TreeVariantInfo v, float minT, float maxT, float minR, float maxR, in TreeSite s, out float p, out float suit)
        {
            p = 0; suit = 0;
            if (s.Temp < minT || s.Temp > maxT || s.Rain < minR || s.Rain > maxR) return false;
            float dT = Math.Abs(s.Temp - (minT + maxT) / 2) / Math.Max(0.5f, (maxT - minT) / 2);
            float dR = maxR - minR < 500 ? Math.Abs(s.Rain - (minR + maxR) / 2) / Math.Max(1f, (maxR - minR) / 2) : 0;
            float dFo = Math.Abs(s.Forest - v.ForestMid) / v.ForestRange;
            double reject = Math.Max(0, 1.2 * dFo * dFo - 1) + Math.Max(0, 1.2 * dT * dT - 1) + Math.Max(0, 1.2 * dR * dR - 1);
            p = (float)Math.Clamp(1 - reject, 0, 1);
            suit = Math.Clamp(1f - (dT + dR + Math.Min(dFo, 1.5f)) / 4.5f, 0.1f, 1f);
            return p > 0;
        }

        static void SharePopulations(Scratch sc, int at, int n)
        {
            // Aliases of one species share the species' weight (the largest alias weight), split in proportion to their
            // own weights: a second pack's copy of a species neither multiplies nor dilutes it.
            int stamp = sc.NextStamp();
            for (int k = at; k < at + n; k++)
            {
                int p = sc.Pop[k];
                if (sc.PopStamp[p] != stamp) { sc.PopStamp[p] = stamp; sc.PopMax[p] = 0; sc.PopSum[p] = 0; }
                if (sc.W[k] > sc.PopMax[p]) sc.PopMax[p] = sc.W[k];
                sc.PopSum[p] += sc.W[k];
            }
            for (int k = at; k < at + n; k++)
            {
                int p = sc.Pop[k];
                sc.W[k] = sc.PopMax[p] * sc.W[k] / sc.PopSum[p];
            }
        }

        /// <summary>Effective number of species (inverse Simpson) of the expected weights; marks their populations with
        /// the returned stamp.</summary>
        static float Diversity(Scratch sc, int n, out int stamp)
        {
            stamp = sc.NextStamp();
            for (int k = 0; k < n; k++)
            {
                int p = sc.Pop[k];
                float e = sc.P[k] * sc.W[k];
                if (sc.PopStamp[p] != stamp) { sc.PopStamp[p] = stamp; sc.PopMax[p] = e; sc.PopSum[p] = 0; }
                else if (e > sc.PopMax[p]) sc.PopMax[p] = e;
            }
            double sum = 0, sum2 = 0;
            for (int k = 0; k < n; k++)
            {
                int p = sc.Pop[k];
                if (sc.PopSum[p] != 0) continue;
                sc.PopSum[p] = 1;
                sum += sc.PopMax[p];
                sum2 += (double)sc.PopMax[p] * sc.PopMax[p];
            }
            return sum2 > 0 ? (float)(sum * sum / sum2) : 0;
        }

        static float BlendShare(float diversity) => diversity <= 0 ? 0 : MaxAnalogueBlend * Math.Clamp((FullDiversity - diversity) / (FullDiversity - 1), 0, 1);

        /// <summary>Thin native pool: collects the biome's analogues (except species already native here) after the
        /// natives at <paramref name="n"/>; returns their count and the share they may fill.</summary>
        int CollectBlend(in TreeSite s, Scratch sc, int n, out float blend)
        {
            blend = 0;
            if (shrubs || catalog == null || n == 0 || !TierAvailable(2, s)) return 0;
            blend = BlendShare(Diversity(sc, n, out int stamp));
            if (blend <= 0) return 0;
            int n2 = Collect(2, s, sc, n), m = n;
            for (int k = n; k < n + n2; k++)
            {
                if (sc.PopStamp[sc.Pop[k]] == stamp) continue;
                sc.Index[m] = sc.Index[k]; sc.W[m] = sc.W[k]; sc.Size[m] = sc.Size[k]; sc.P[m] = sc.P[k]; sc.Pop[m] = sc.Pop[k];
                m++;
            }
            if (m == n) blend = 0;
            return m - n;
        }

        // vanilla's random rejection outside the comfort ranges; survivors are compacted to the front of [at, at+n)
        static int Reject<TRandom>(Scratch sc, int at, int n, ref TRandom rnd) where TRandom : struct, ITreeRandom
        {
            int m = at;
            for (int k = at; k < at + n; k++)
            {
                if (sc.P[k] < 1 && rnd.NextDouble() >= sc.P[k]) continue;
                if (m != k) { sc.Index[m] = sc.Index[k]; sc.W[m] = sc.W[k]; sc.Size[m] = sc.Size[k]; sc.Pop[m] = sc.Pop[k]; }
                m++;
            }
            return m - at;
        }

        // river channels stay open; shrubs never grow in water (vanilla's shrub pass has no water check)
        bool Blocked(in TreeSite s) => s.RiverChannel || (shrubs && s.Underwater);

        static double Sum(Scratch sc, int at, int n)
        {
            double t = 0;
            for (int k = at; k < at + n; k++) t += sc.W[k];
            return t;
        }

        /// <summary>
        /// Picks a registered variant (index into the list given to the constructor), or -1 for no tree.
        /// <paramref name="tier"/>: 1 native, 2 analogue (alone, or blended into a thin native pool), 3 climate fallback
        /// (always 3 without a catalogue).
        /// </summary>
        public int Pick<TRandom>(in TreeSite s, ref TRandom rnd, out float sizeWeight, out int tier) where TRandom : struct, ITreeRandom
        {
            if (Blocked(s)) { tier = 0; sizeWeight = 0; return -1; }
            var sc = GetScratch(2 * entries.Length, populations);
            for (tier = 1; tier <= 3; tier++)
            {
                if (!TierAvailable(tier, s)) continue;
                int n = Collect(tier, s, sc, 0);
                if (n == 0) continue;
                float blend = 0;
                int n2 = tier == 1 ? CollectBlend(s, sc, n, out blend) : 0;
                if (n2 == 0) blend = 0;
                int m = Reject(sc, 0, n, ref rnd);
                if (m == 0) continue;
                int m2 = n2 > 0 ? Reject(sc, n, n2, ref rnd) : 0;
                if (catalog != null && tier < 3) SharePopulations(sc, 0, m);
                double t1 = Sum(sc, 0, m), t2 = 0, scale2 = 0;
                if (m2 > 0)
                {
                    SharePopulations(sc, n, m2);
                    t2 = Sum(sc, n, m2);
                    if (t2 > 0) scale2 = blend / (1 - blend) * t1 / t2;
                }
                double total = t1 + t2 * scale2;
                if (total <= 0) continue;
                double pick = rnd.NextDouble() * total;
                for (int k = 0; k < m; k++)
                {
                    pick -= sc.W[k];
                    if (pick > 0.001) continue;
                    sizeWeight = sc.Size[k];
                    return sc.Index[k];
                }
                for (int k = n; k < n + m2; k++)
                {
                    pick -= sc.W[k] * scale2;
                    if (pick > 0.001) continue;
                    sizeWeight = sc.Size[k];
                    tier = 2;
                    return sc.Index[k];
                }
                sizeWeight = 0;
                return -1;
            }
            tier = 0;
            sizeWeight = 0;
            return -1;
        }

        /// <summary>
        /// Analysis without randomness: the probability that an attempt yields a tree, the probability of each tier,
        /// and approximate per-variant selection probabilities (expected weights within the tier that yields).
        /// </summary>
        public double Expected(in TreeSite s, float[] share, double[] tierShare = null)
        {
            if (share != null) Array.Clear(share);
            if (tierShare != null) Array.Clear(tierShare);
            if (Blocked(s)) return 0;
            var sc = GetScratch(2 * entries.Length, populations);
            double reach = 1;
            for (int tier = 1; tier <= 3; tier++)
            {
                if (!TierAvailable(tier, s)) continue;
                int n = Collect(tier, s, sc, 0);
                if (n == 0) continue;
                float blend = 0;
                int n2 = tier == 1 ? CollectBlend(s, sc, n, out blend) : 0;
                if (n2 == 0) blend = 0;
                double none = 1;
                for (int k = 0; k < n; k++) { none *= 1 - sc.P[k]; sc.W[k] *= sc.P[k]; }
                for (int k = n; k < n + n2; k++) sc.W[k] *= sc.P[k];
                if (catalog != null && tier < 3) SharePopulations(sc, 0, n);
                if (n2 > 0) SharePopulations(sc, n, n2);
                double t1 = Sum(sc, 0, n), t2 = Sum(sc, n, n2);
                double f2 = t2 > 0 ? blend : 0;
                double yields = reach * (1 - none);
                if (tierShare != null) { tierShare[tier] += yields * (1 - f2); tierShare[2] += yields * f2; }
                if (share != null)
                {
                    if (t1 > 0) for (int k = 0; k < n; k++) share[sc.Index[k]] += (float)(yields * (1 - f2) * sc.W[k] / t1);
                    if (t2 > 0) for (int k = n; k < n + n2; k++) share[sc.Index[k]] += (float)(yields * f2 * sc.W[k] / t2);
                }
                reach *= none;
            }
            return 1 - reach;
        }

        public string Generator(int index) => entries[index].V.Generator;
        public RegionalTreeCatalog.Rule RuleOf(int index) => entries[index].Rule;
    }
}
