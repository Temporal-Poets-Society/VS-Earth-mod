using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>Rate class of a bound deposit variant. Built into the v2 rules; per-resource overrides still win.</summary>
    public enum OreClass
    {
        /// <summary>Background floor + evidence hotspots (global tuning, frequency x StandardCompensation = 0.12 effective rate).</summary>
        Standard,
        /// <summary>Copper, tin, iron ores and coal: land average near vanilla volume, raised floor, 3-5x volume at hotspots.</summary>
        Progression,
        /// <summary>Starter "surface…" variants: at least half of vanilla count wherever hosts exist, about 2x at hotspots.</summary>
        Surface,
        /// <summary>Clay, red clay and peat: vanilla attempts where the geology gate allows, evidence boosts.</summary>
        Soil,
        /// <summary>Quartz veins: ubiquitous at the vanilla rate, boosted (never gated) by evidence.</summary>
        Veins,
    }

    public sealed class OreTuning
    {
        public double BackgroundChance { get; set; } = .08;
        public double HotspotChance { get; set; } = 1;
        public double FrequencyMultiplier { get; set; } = .4;
        public double RadiusMultiplier { get; set; } = 1.6;
        public double ThicknessMultiplier { get; set; } = 1.25;
        public double Concentration { get; set; } = .85;
        public double QualityBias { get; set; } = 0;
        public double DepthMultiplier { get; set; } = 1;
        public double MinDepthBlocks { get; set; } = 0;
        public double MaxDepthBlocks { get; set; } = 0;
        public double MetresPerBlock { get; set; } = 5;
        public double SurfaceFrequencyMultiplier { get; set; } = 1;
        public double BogIronChance { get; set; } = .025;
        /// <summary>Weight of the background map (the class floor, gated by host rocks) in the attempts: 1 = default, 0..10
        /// (decision 0006, "Tunable maps").</summary>
        public double BackgroundWeight { get; set; } = 1;
        /// <summary>Weight of the evidence map (the real data) in the attempts: 1 = default, 0..10; 0 also switches the ore
        /// bodies off (decision 0006, "Tunable maps").</summary>
        public double EvidenceWeight { get; set; } = 1;
        /// <summary>Coal measures under the coal basins (rules v3, OreRules.CoalMeasures): 1 = default thickness and cover,
        /// 0 = none, up to 3.</summary>
        public double CoalBasins { get; set; } = 1;
        /// <summary>Iron formations in the great iron districts (rules v3, OreRules.IronFormations): 1 = default, 0 = none, up to 3.</summary>
        public double IronFormations { get; set; } = 1;
        /// <summary>Rich ore bodies in the great districts of the other resources (rules v3, OreRules.District): 1 = default,
        /// 0 = none, up to 3.</summary>
        public double RichDistricts { get; set; } = 1;
        public Dictionary<string, OreOverride> Resources { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Effective v2 profile: global tuning, then the built-in rules of the rate class, then the per-resource
        /// override (which always wins). Soil and surface classes keep native burial: depth overrides do not apply.
        /// Standard deposits use FrequencyMultiplier x <see cref="OreRules.StandardCompensation"/> (0.12's effective rate);
        /// child deposits (gold/silver in quartz, silver in galena…) use FrequencyMultiplier x <see cref="OreRules.ChildCompensation"/>
        /// and are governed only by their own evidence (see ChildRatio). The compensation also scales per-resource
        /// FrequencyMultiplier overrides, so their 0.12 meaning is kept.
        /// Allocates; callers cache the result (one profile per bound variant).
        /// </summary>
        public OreProfile For(string code, OreClass cls, bool child = false)
        {
            OreOverride o = null;
            if (code != null) Resources?.TryGetValue(code, out o);
            double baseFreq = o?.FrequencyMultiplier ?? FrequencyMultiplier;
            double bg = BackgroundChance, hot = HotspotChance, radius = RadiusMultiplier,
                thick = ThicknessMultiplier, conc = Concentration, depth = DepthMultiplier, min = MinDepthBlocks, max = MaxDepthBlocks;
            double freq = baseFreq * (child ? OreRules.ChildCompensation : OreRules.StandardCompensation);
            double surfaceFreq = o?.SurfaceFrequencyMultiplier ?? SurfaceFrequencyMultiplier;
            if (!child) switch (cls)
            {
                case OreClass.Progression:
                    var r = OreRules.ProgressionRule(code);
                    bg = r.Background; hot = 1; freq = o?.FrequencyMultiplier ?? r.Frequency;
                    break;
                case OreClass.Surface:
                    bg = OreRules.SurfaceBackground; hot = 1; freq = OreRules.SurfaceFrequency * surfaceFreq;
                    radius = thick = conc = 1;
                    break;
                case OreClass.Soil:
                    bg = 1 / OreRules.SoilBoost; hot = 1; freq = o?.FrequencyMultiplier ?? OreRules.SoilBoost;
                    radius = thick = conc = 1;
                    break;
                case OreClass.Veins:
                    bg = 1 / OreRules.VeinBoost; hot = 1; freq = o?.FrequencyMultiplier ?? OreRules.VeinBoost;
                    radius = thick = conc = 1;
                    break;
            }
            if (o != null)
            {
                bg = o.BackgroundChance ?? bg; hot = o.HotspotChance ?? hot;
                radius = o.RadiusMultiplier ?? radius; thick = o.ThicknessMultiplier ?? thick; conc = o.Concentration ?? conc;
                depth = o.DepthMultiplier ?? depth; min = o.MinDepthBlocks ?? min; max = o.MaxDepthBlocks ?? max;
            }
            if (cls is OreClass.Soil or OreClass.Surface) { depth = 1; min = 0; max = 0; }
            return new OreProfile(bg, Math.Max(bg, hot), freq, radius, thick, conc, o?.QualityBias ?? QualityBias, depth, min, max,
                MetresPerBlock, surfaceFreq, o?.BackgroundWeight ?? BackgroundWeight, o?.EvidenceWeight ?? EvidenceWeight);
        }

        /// <summary>A resource's ore-body set with its district ramp from the settings (<see cref="DistrictRamp"/>).</summary>
        public OreRules.BedSet DistrictSet(OreRules.BedSet set)
        {
            var (from, to) = DistrictRamp(set.Layer);
            return from == set.From && to == set.To ? set : set with { From = from, To = to };
        }

        /// <summary>The evidence where a resource's ore bodies start (From) and are full (To): the per-resource DistrictFrom
        /// and DistrictTo, else the built-in ramp (<see cref="OreRules.DistrictRamp"/>).</summary>
        public (double From, double To) DistrictRamp(string code)
        {
            OreOverride o = null;
            if (code != null) Resources?.TryGetValue(code, out o);
            var d = OreRules.DistrictRamp(code);
            return (o?.DistrictFrom ?? d.From, o?.DistrictTo ?? d.To);
        }

        /// <summary>Richness of a resource's ore bodies: the family setting (CoalBasins, IronFormations or RichDistricts)
        /// times the resource's Richness, at most <see cref="OreRules.MaxRichness"/>. The world's deposit rate comes on top.</summary>
        public double DistrictRichness(string code, double family)
        {
            OreOverride o = null;
            if (code != null) Resources?.TryGetValue(code, out o);
            return Math.Min(OreRules.MaxRichness, family * (o?.Richness ?? 1));
        }

        /// <summary>
        /// Rules v3 ore bodies: whether the settings switch a resource's bodies off (frequency or concentration 0, or a hotspot
        /// chance of 0 as set; the profile lifts the hotspot chance to the background, so the setting itself is read).
        /// </summary>
        public bool OreBodiesOff(string code, OreClass cls)
        {
            var p = For(code, cls);
            if (!(p.Frequency > 0 && p.Concentration > 0 && p.EvidenceWeight > 0)) return true;
            OreOverride o = null;
            if (code != null) Resources?.TryGetValue(code, out o);
            return !((o?.HotspotChance ?? (cls == OreClass.Standard ? HotspotChance : 1)) > 0);
        }

        /// <summary>Never throws. Returns false with a readable reason for the first invalid value.</summary>
        public bool TryValidate(IEnumerable<string> codes, out string error)
        {
            error = null;
            if (Resources == null) { error = "Ores.Resources cannot be null"; return false; }
            if (!double.IsFinite(BogIronChance) || BogIronChance < 0 || BogIronChance > 1) { error = "BogIronChance must be 0..1"; return false; }
            if (!double.IsFinite(CoalBasins) || CoalBasins < 0 || CoalBasins > 3) { error = "CoalBasins must be 0..3"; return false; }
            if (!double.IsFinite(IronFormations) || IronFormations < 0 || IronFormations > 3) { error = "IronFormations must be 0..3"; return false; }
            if (!double.IsFinite(RichDistricts) || RichDistricts < 0 || RichDistricts > 3) { error = "RichDistricts must be 0..3"; return false; }
            var known = new HashSet<string>(codes ?? Array.Empty<string>(), StringComparer.Ordinal);
            string unknown = Resources.Keys.FirstOrDefault(k => !known.Contains(k));
            if (unknown != null) { error = "Unknown ore resource override '" + unknown + "'"; return false; }
            foreach (var (code, o) in Resources)
            {
                if (o?.Richness is double r && !(r >= 0 && r <= OreRules.MaxRichness)) { error = code + ": Richness must be 0..3"; return false; }
                var (from, to) = DistrictRamp(code);
                if (!(from >= 0 && to <= 1 && to - from >= .05)) { error = code + ": DistrictFrom and DistrictTo must satisfy 0 <= from, from + 0.05 <= to <= 1"; return false; }
            }
            // Raw multipliers keep the 0.12 bounds; effective (compensated) frequencies are checked per profile.
            if (!(FrequencyMultiplier >= 0 && FrequencyMultiplier <= 10) || Resources.Values.Any(r => r?.FrequencyMultiplier is < 0 or > 10 || r?.FrequencyMultiplier is double f && !double.IsFinite(f)))
            { error = "FrequencyMultiplier must be 0..10"; return false; }
            foreach (string code in known.Prepend(""))
                foreach (OreClass cls in Enum.GetValues(typeof(OreClass)))
                    foreach (bool child in new[] { false, true })
                        if (!For(code, cls, child).IsValid(out string why)) { error = (code.Length == 0 ? "global" : code) + "/" + cls + ": " + why; return false; }
            return true;
        }

        /// <summary>Throws ArgumentException on invalid settings (tests and tools). Runtime code uses <see cref="Load"/>.</summary>
        public void Validate(IEnumerable<string> codes)
        {
            if (!TryValidate(codes, out string error)) throw new ArgumentException("Invalid ore tuning: " + error);
        }

        /// <summary>
        /// Save-safe loader: parses the world's saved tuning; unknown resource overrides are dropped; any other problem
        /// returns the defaults. Never throws. <paramref name="problem"/> is null when the saved tuning was used unchanged.
        /// </summary>
        public static OreTuning Load(string json, IEnumerable<string> codes, out string problem)
        {
            problem = null;
            OreTuning t;
            try { t = JsonSerializer.Deserialize<OreTuning>(string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? new OreTuning(); }
            catch (Exception e) { problem = "saved ore tuning is not valid JSON (" + e.Message + "); defaults used"; return new OreTuning(); }
            t.Resources ??= new Dictionary<string, OreOverride>(StringComparer.Ordinal);
            var known = new HashSet<string>(codes ?? Array.Empty<string>(), StringComparer.Ordinal);
            var unknown = t.Resources.Keys.Where(k => k == null || !known.Contains(k)).ToList();
            if (unknown.Count > 0)
            {
                foreach (string k in unknown) t.Resources.Remove(k);
                problem = "ignored unknown resource override(s): " + string.Join(", ", unknown);
            }
            if (!t.TryValidate(known, out string error))
            {
                problem = "saved ore tuning is invalid (" + error + "); defaults used";
                return new OreTuning();
            }
            return t;
        }
    }
    public sealed class OreOverride
    {
        public double? BackgroundChance { get; set; }
        public double? HotspotChance { get; set; }
        public double? FrequencyMultiplier { get; set; }
        public double? RadiusMultiplier { get; set; }
        public double? ThicknessMultiplier { get; set; }
        public double? Concentration { get; set; }
        public double? QualityBias { get; set; }
        public double? DepthMultiplier { get; set; }
        public double? MinDepthBlocks { get; set; }
        public double? MaxDepthBlocks { get; set; }
        public double? SurfaceFrequencyMultiplier { get; set; }
        /// <summary>Weights of the background and evidence maps for this resource (replace the global ones).</summary>
        public double? BackgroundWeight { get; set; }
        public double? EvidenceWeight { get; set; }
        /// <summary>Evidence where this resource's ore bodies start and where they are full (replace the built-in ramp).</summary>
        public double? DistrictFrom { get; set; }
        public double? DistrictTo { get; set; }
        /// <summary>Richness of this resource's ore bodies, 0..3, times the family setting (CoalBasins, IronFormations, RichDistricts).</summary>
        public double? Richness { get; set; }
    }
    public sealed record OreProfile(double Background, double Hotspot, double Frequency, double Radius,
        double Thickness, double Concentration, double QualityBias, double DepthScale, double MinDepth,
        double MaxDepth, double MetresPerBlock, double SurfaceFrequency, double BackgroundWeight = 1, double EvidenceWeight = 1)
    {
        public bool IsValid(out string error)
        {
            error = null;
            double[] all = { Background, Hotspot, Frequency, Radius, Thickness, Concentration, QualityBias, DepthScale, MinDepth, MaxDepth, MetresPerBlock, SurfaceFrequency, BackgroundWeight, EvidenceWeight };
            if (all.Any(v => !double.IsFinite(v))) error = "non-finite value";
            else if (Background < 0 || Background > 1 || Hotspot < Background || Hotspot > 1) error = "chances must satisfy 0 <= background <= hotspot <= 1";
            else if (Frequency < 0 || Frequency > 10 * OreRules.ChildCompensation || SurfaceFrequency < 0 || SurfaceFrequency > 10) error = "frequency multipliers out of range";
            else if (Radius < .25 || Radius > 4 || Thickness < .25 || Thickness > 4) error = "radius/thickness multipliers must be .25..4";
            else if (Concentration < 0 || Concentration > 1) error = "concentration must be 0..1";
            else if (QualityBias < -1 || QualityBias > 1) error = "quality bias must be -1..1";
            else if (DepthScale < .1 || DepthScale > 10) error = "depth multiplier must be .1..10";
            else if (MinDepth < 0 || MaxDepth < 0 || MaxDepth > 0 && MaxDepth < MinDepth) error = "invalid depth interval";
            else if (MetresPerBlock <= 0 || MetresPerBlock > 100) error = "metres per block must be (0, 100]";
            else if (BackgroundWeight < 0 || BackgroundWeight > 10 || EvidenceWeight < 0 || EvidenceWeight > 10) error = "map weights must be 0..10";
            return error == null;
        }
        /// <summary>The blended map at this evidence: the weighted background plus the weighted evidence (decision 0006,
        /// "Tunable maps"). A relative rate (attempts and acceptance ratios), so it may exceed 1 with weights above 1.</summary>
        public double Chance(double evidence) => BackgroundWeight * Background + EvidenceWeight * (Hotspot - Background) * Math.Clamp(evidence, 0, 1);
        /// <summary>Mean attempts relative to vanilla at this evidence (texture has mean 1).</summary>
        public double Ratio(double evidence) => Frequency * Chance(evidence);
        public double Depth(double nativeDepth) => Math.Max(MinDepth, MaxDepth > 0 ? Math.Min(MaxDepth, Math.Max(0, nativeDepth) * DepthScale) : Math.Max(0, nativeDepth) * DepthScale);
    }

    /// <summary>Vertical reference of a vanilla disc generator.</summary>
    public enum OreAnchor
    {
        /// <summary>disc-followsurface: position is a fraction of the local surface height.</summary>
        SurfaceFraction,
        /// <summary>disc-followsurfacebelow: position is a depth below the local surface.</summary>
        BelowSurface,
        /// <summary>disc-followsealevel / disc-anywhere: position is an absolute Y.</summary>
        AbsoluteY,
    }

    /// <summary>Built-in v2 rate rules. Pure functions: no game types, no shared mutable state.</summary>
    public static class OreRules
    {
        /// <summary>Native burial (blocks below the local surface) of a drawn position. Negative means the deposit lies above the ground.</summary>
        public static double NativeDepth(OreAnchor anchor, double position, int top) => anchor switch
        {
            OreAnchor.SurfaceFraction => top * (1 - position),
            OreAnchor.BelowSurface => position,
            _ => top - position,
        };
        /// <summary>
        /// Final burial in blocks, or NaN when vanilla would place nothing (deposit above the ground at this column).
        /// The 0.12 adapter clamped such deposits to 0, creating sheets directly under the soil or seabed.
        /// </summary>
        public static double Burial(OreAnchor anchor, double position, int top, OreProfile profile, bool keepNativeDepth)
        {
            double native = NativeDepth(anchor, position, top);
            if (native < 0 || double.IsNaN(native)) return double.NaN;
            return keepNativeDepth ? native : profile.Depth(native);
        }
        /// <summary>Surface starters: background .5 x frequency 2 = vanilla's attempts everywhere (rules v3; v2 had .3, 0.6x).</summary>
        public const double SoilBoost = 1.75, VeinBoost = 1.5, SurfaceFrequency = 2, SurfaceBackground = .5;
        /// <summary>
        /// Standard class: 0.12 applied a 0.2 texture floor over vanilla's sparse ore map (mean 0.0705), which raised every
        /// mapped ore's effective rate by (0.2 + 0.8 x 0.0705) / 0.0705 = 3.64; its undistorted ellipses were also about 15%
        /// larger per attempt than v2's vanilla-shaped outline. v2 normalises the texture to mean 1 and keeps 0.12's effective
        /// volume explicitly: attempts = FrequencyMultiplier x 4 x chance (0.128x vanilla without evidence, 1.6x at hotspots
        /// by default; about 0.36x and 4.6x vanilla volume).
        /// </summary>
        public const double StandardCompensation = 4;
        /// <summary>
        /// Children (point clouds inside a parent ore) do not get the radius-squared volume boost of disc deposits
        /// (about 0.9x vs 2.8x vanilla volume per attempt), so their frequency is FrequencyMultiplier x 11 to land in the
        /// same volume band as standard ores.
        /// </summary>
        public const double ChildCompensation = 11;
        /// <summary>Soft spatial variation of formerly unmapped variants (mean 1).</summary>
        public const double SoftAmplitude = .3, SurfaceAmplitude = .15;
        /// <summary>A child may not be boosted beyond this many times the vanilla per-block chance.</summary>
        public const double MaxChildBoost = 6;
        public const int TextureCellChunks = 8;

        public static OreClass Classify(string deposit, string resource)
        {
            if (resource is "clay" or "peat") return OreClass.Soil;
            if (deposit != null && deposit.StartsWith("surface", StringComparison.Ordinal)) return OreClass.Surface;
            if (resource is "Cu" or "Sn" or "Fe" or "C_coal") return OreClass.Progression;
            if (deposit == "quartz") return OreClass.Veins;
            return OreClass.Standard;
        }
        /// <summary>
        /// Single-block gem pockets (native average radius below 2, e.g. emerald and peridot) keep their native radius:
        /// vanilla truncates their radius to 0 or 1, while Earth's rounded 1.6x radius would make them 1-2 blocks and
        /// quadruple the volume (about 7.8x vanilla at hotspots). Thickness and concentration still apply.
        /// </summary>
        public static double EffectiveRadiusMultiplier(double nativeRadiusAvg, double profileRadius) => nativeRadiusAvg < 2 ? 1 : profileRadius;
        /// <summary>Soil, surface and near-surface bauxite keep their provider burial (no depth remapping, no MinDepth).</summary>
        public static bool KeepsNativeDepth(string deposit, OreClass cls) => cls is OreClass.Soil or OreClass.Surface || deposit == "earth-bauxite";
        /// <summary>
        /// Progression calibration (see docs/regional-ores-implementation.md): hotspot attempts about 1.5x vanilla, which with the
        /// default 1.6x radius / 1.25x thickness / 0.85 concentration (about 2.7x volume per attempt) gives about 4x vanilla volume.
        /// Floors are set per resource so the land average stays near or above 70% of vanilla volume despite sparse evidence.
        /// Coal (lignite, bituminous, anthracite) is progression-critical fuel and uses the same targets.
        /// Rules v3: copper has tin's floor (it was .09), so copper stays within reach of every start.
        /// </summary>
        public static (double Background, double Frequency) ProgressionRule(string resource) => resource switch
        {
            "Cu" => (.19, 1.33),
            "Sn" => (.19, 1.33),
            "Fe" => (.17, 1.33),
            "C_coal" => (.17, 1.33),
            _ => (.15, 1.33),
        };
        /// <summary>
        /// Child per-block ratio relative to vanilla: the child's own class rate divided by the rate its parent was already
        /// attempted at (the parent is driven by max(parent, child) heat), so Frequency is applied once and an unbound
        /// parent (ratio 1) leaves the child at its own class rate.
        /// </summary>
        public static double ChildRatio(OreProfile child, double childHeat, double parentRatio)
        {
            if (child == null || !(parentRatio > 0)) return 0;
            return Math.Min(MaxChildBoost, child.Ratio(childHeat) / parentRatio);
        }
        /// <summary>Mean of vanilla's sparse noise ore-map factor (GenMaps.GetOreMap), measured over 864 region maps on VS 1.22.7.</summary>
        public static double VanillaOreMapMean(double scale, double contrast, double sub)
        {
            if (Math.Abs(scale - 1) < 1e-6 && Math.Abs(contrast - 1) < 1e-6 && Math.Abs(sub) < 1e-6) return .0705;
            if (Math.Abs(scale - 1.5) < 1e-6 && Math.Abs(contrast - 1.5) < 1e-6 && Math.Abs(sub - 40) < 1e-6) return .0303;
            return double.NaN;
        }

        static double Smooth(double t) => t * t * (3 - 2 * t);
        /// <summary>Value noise sampled at chunk centres on a lattice of <see cref="TextureCellChunks"/> chunks; mean exactly 0.5.</summary>
        public static double ChunkNoise(long seed, int chunkX, int chunkZ, int salt)
        {
            int cell = TextureCellChunks;
            int ix = (int)Math.Floor((double)chunkX / cell), iz = (int)Math.Floor((double)chunkZ / cell);
            double tx = Smooth((chunkX - ix * cell + .5) / cell), tz = Smooth((chunkZ - iz * cell + .5) / cell);
            double a = RegionalOreCatalog.Random(seed, ix, 7, iz, salt), b = RegionalOreCatalog.Random(seed, ix + 1, 7, iz, salt);
            double c = RegionalOreCatalog.Random(seed, ix, 7, iz + 1, salt), d = RegionalOreCatalog.Random(seed, ix + 1, 7, iz + 1, salt);
            return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
        }
        /// <summary>Low-contrast texture in [1-amplitude, 1+amplitude], mean 1.</summary>
        public static double SoftTexture(long seed, int chunkX, int chunkZ, int salt, double amplitude)
            => 1 + amplitude * (2 * ChunkNoise(seed, chunkX, chunkZ, salt) - 1);
        static readonly double clusterMean = ComputeClusterMean();
        static double ComputeClusterMean()
        {
            // Deterministic estimate of E[n^4] for the lattice noise (independent of seed by construction).
            double sum = 0; int n = 0;
            for (int z = 0; z < 512; z++) for (int x = 0; x < 512; x++) { double v = ChunkNoise(9137, x, z, 4211); v *= v; sum += v * v; n++; }
            return sum / n;
        }
        /// <summary>
        /// Clustered texture with mean 1 for ores that vanilla generated with a sparse noise map: a 0.3 floor
        /// everywhere and broad patches several times richer, so local prospecting still matters.
        /// </summary>
        public static double ClusteredTexture(long seed, int chunkX, int chunkZ, int salt)
        {
            double v = ChunkNoise(seed, chunkX, chunkZ, salt); v *= v;
            return .3 + .7 * (v * v) / clusterMean;
        }

        /// <summary>Rules v3: at this evidence and above a deposit fills any rock it meets (known districts; see SimilarHost).</summary>
        public const double LensHeat = .6;

        /// <summary>
        /// The provider host to borrow an ore block from for a rock that isn't one of its hosts (rules v3, "fill any rock"):
        /// the rock itself if it is a host, else the first host from the rock's family list (carbonates take chalk, granite
        /// basement the nearest igneous rock, ...), else the first host. Rock and host codes are the variant part
        /// ("limestone" for rock-limestone). Null when there are no hosts.
        /// </summary>
        public static string SimilarHost(string rock, IReadOnlyList<string> hosts)
        {
            if (hosts == null || hosts.Count == 0) return null;
            if (rock != null && hosts.Contains(rock)) return rock;
            if (rock != null && SimilarRocks.TryGetValue(rock, out var near))
                foreach (string r in near) if (hosts.Contains(r)) return r;
            return hosts[0];
        }
        static readonly Dictionary<string, string[]> SimilarRocks = new(StringComparer.Ordinal)
        {
            // sedimentary
            ["limestone"] = new[] { "chalk", "dolostone", "marble", "claystone", "shale", "sandstone" },
            ["dolostone"] = new[] { "limestone", "chalk", "marble", "claystone", "shale", "sandstone" },
            ["chalk"] = new[] { "limestone", "dolostone", "claystone", "shale", "sandstone" },
            ["chert"] = new[] { "chalk", "limestone", "sandstone", "shale" },
            ["claystone"] = new[] { "shale", "sandstone", "conglomerate", "chalk" },
            ["shale"] = new[] { "claystone", "sandstone", "slate", "conglomerate" },
            ["sandstone"] = new[] { "conglomerate", "shale", "claystone", "chert" },
            ["conglomerate"] = new[] { "sandstone", "shale", "claystone" },
            // metamorphic
            ["marble"] = new[] { "limestone", "chalk", "dolostone", "slate", "phyllite" },
            ["slate"] = new[] { "phyllite", "shale", "schist", "claystone" },
            ["phyllite"] = new[] { "slate", "schist", "shale" },
            ["schist"] = new[] { "phyllite", "slate", "gneiss", "granite" },
            ["gneiss"] = new[] { "granite", "schist", "phyllite", "slate" },
            ["quartzite"] = new[] { "sandstone", "chert", "granite" },
            // igneous and volcanic
            ["granite"] = new[] { "gneiss", "andesite", "diorite", "basalt", "peridotite" },
            ["diorite"] = new[] { "granite", "andesite", "basalt" },
            ["andesite"] = new[] { "basalt", "granite", "diorite" },
            ["basalt"] = new[] { "andesite", "peridotite", "granite" },
            ["peridotite"] = new[] { "basalt", "kimberlite", "andesite" },
            ["kimberlite"] = new[] { "peridotite", "basalt", "suevite" },
            ["suevite"] = new[] { "kimberlite", "basalt", "andesite" },
            ["obsidian"] = new[] { "basalt", "andesite" },
        };

        /// <summary>Value noise on a square lattice of <paramref name="cell"/> blocks, smoothstep-interpolated: 0..1, continuous.</summary>
        public static double BlockNoise(long seed, int x, int z, int cell, int salt)
        {
            int ix = (int)Math.Floor((double)x / cell), iz = (int)Math.Floor((double)z / cell);
            double tx = Smooth((x - ix * cell + .5) / cell), tz = Smooth((z - iz * cell + .5) / cell);
            double a = RegionalOreCatalog.Random(seed, ix, 11, iz, salt), b = RegionalOreCatalog.Random(seed, ix + 1, 11, iz, salt);
            double c = RegionalOreCatalog.Random(seed, ix, 11, iz + 1, salt), d = RegionalOreCatalog.Random(seed, ix + 1, 11, iz + 1, salt);
            return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
        }

        /// <summary>One bed of a <see cref="BedSet"/>: depth of its top below the surface (blocks) and how far it swings around
        /// that, its greatest thickness, and the deposit code whose ore blocks it is made of.</summary>
        public sealed record BedSpec(int Depth, int Swing, int MaxThickness, string Code);
        /// <summary>
        /// Stratiform ore bodies under a district (rules v3): where the evidence of <see cref="Layer"/> goes from
        /// <see cref="From"/> (none) to <see cref="To"/> (full), each bed covers up to Cover of the ground in patches of about
        /// <see cref="PatchCell"/> blocks.
        /// </summary>
        public sealed record BedSet(string Name, string Layer, double From, double To, double Cover, int PatchCell, int Salt, BedSpec[] Beds);
        /// <summary>A bed at a column: depth of its top below the surface, thickness (blocks), index into BedSet.Beds.</summary>
        public readonly record struct Bed(int Depth, int Thickness, int Index);
        public const int MaxBeds = 3;

        /// <summary>
        /// Coal measures: continuous seams under the coal basins, by default from coal evidence .45 (none) to .75 (full: Upper
        /// Silesia, Yorkshire and the Appalachians; the Ruhr and South Wales about .9 strength; .3 to .6 before 2026-10-17 blurred
        /// the Ruhr over 76,000 km²). Two bituminous seams 2-10 and about 22 blocks down, anthracite about 40 down;
        /// 1-4 blocks thick, each covering 75-94% of a full basin. "Practically unlimited for one person" (decision 0006).
        /// </summary>
        public static readonly BedSet CoalMeasures = new("coal measures", "C_coal", DistrictRamp("C_coal").From, DistrictRamp("C_coal").To, .75, 96, 9100, new[]
        {
            new BedSpec(6, 4, 4, "bituminouscoal"), new BedSpec(22, 6, 4, "bituminouscoal"), new BedSpec(40, 8, 4, "anthracite"),
        });
        /// <summary>
        /// Iron formations: large separate ore bodies in the great iron districts, by default from iron evidence .55 (none) to .85
        /// (full: Kiruna, Krivoy Rog, Lorraine, Carajás, Erzberg). A weathered limonite cap 2-8 blocks down (up to 3 thick),
        /// hematite about 18 down (up to 6), magnetite about 36 down (up to 8); lenses a few hundred blocks across, each a
        /// hundred or more vanilla iron deposits, enough for many players (decision 0006).
        /// </summary>
        public static readonly BedSet IronFormations = new("iron formations", "Fe", DistrictRamp("Fe").From, DistrictRamp("Fe").To, .45, 192, 9400, new[]
        {
            new BedSpec(5, 3, 3, "limonite"), new BedSpec(18, 5, 6, "hematite"), new BedSpec(36, 6, 8, "magnetite"),
        });

        /// <summary>
        /// Rich districts of any other resource: by default from evidence .7 (none) to .9 (full; known deposits sit at .85-.95 in the
        /// data), one bed of large lenses of the resource's main deposit, 2-5 blocks thick at about its usual depth
        /// (decision 0006: "all very large deposits very rich compared to vanilla").
        /// </summary>
        public static BedSet District(string layer, string code, int depth)
            => new(layer + " district", layer, DistrictRamp(layer).From, DistrictRamp(layer).To, .45, 160, 9700 + (RegionalOreCatalog.StableCode(layer) & 0xFFFF) * 8,
                new[] { new BedSpec(Math.Clamp(depth, 6, 250), 6, 5, code) });

        /// <summary>Built-in district ramps (evidence from none to full ore bodies); Ores.Resources.&lt;code&gt;.DistrictFrom
        /// and DistrictTo replace them.</summary>
        public static (double From, double To) DistrictRamp(string layer) => layer switch
        {
            "C_coal" => (.45, .75),
            "Fe" => (.55, .85),
            _ => (.7, .9),
        };
        /// <summary>Greatest richness of a set's ore bodies from the settings (family setting x per-resource Richness).</summary>
        public const double MaxRichness = 3;

        /// <summary>How much of a district a place is (0..1), from the evidence of the set's layer.</summary>
        public static double BedStrength(BedSet set, double heat) => Smooth(Math.Clamp((heat - set.From) / (set.To - set.From), 0, 1));

        /// <summary>
        /// The beds of a set at a column, a pure function of the position, the district strength (BedStrength) and the
        /// richness (the set's Ores setting x the world's deposit rate). Each bed is present where its patch noise is below
        /// the cover, pinching out at a patch's edge instead of ending in a wall; thickness grows with the strength.
        /// Returns the number written to <paramref name="beds"/>.
        /// </summary>
        public static int BedsAt(BedSet set, long seed, int x, int z, double strength, double richness, Span<Bed> beds)
        {
            if (!(strength > 0) || !(richness > 0)) return 0;
            int n = 0;
            double cover = Math.Min(.95, set.Cover * strength * Math.Min(1, richness));
            for (int k = 0; k < set.Beds.Length && n < beds.Length; k++)
            {
                var spec = set.Beds[k];
                double presence = BlockNoise(seed, x, z, set.PatchCell, set.Salt + k);
                if (presence >= cover) continue;
                double edge = Math.Min(1, (cover - presence) / .08);
                double t = spec.MaxThickness / 4.0 * (1 + 2 * strength) * (.6 + .8 * BlockNoise(seed, x, z, 64, set.Salt + 100 + k)) * Math.Max(1, richness) * edge;
                int thick = Math.Clamp((int)Math.Round(t), 0, (int)Math.Ceiling(spec.MaxThickness * Math.Max(1, richness)));
                if (thick < 1) continue;
                int depth = Math.Max(2, spec.Depth + (int)Math.Round(spec.Swing * (2 * BlockNoise(seed, x, z, 160, set.Salt + 200 + k) - 1)));
                beds[n++] = new Bed(depth, thick, k);
            }
            return n;
        }

        /// <summary>
        /// Edge value of a seeded ellipse column, using vanilla's DiscDepositGenerator shape: distortion noise in 0..1
        /// (vanilla's NormalizedSimplexNoise at x/3, z/3) subtracts up to 0.2 for radius above 3.
        /// </summary>
        public static double DiscEdge(int dx, int dz, int rx, int rz, int radius, double distortion)
            => 1 - (radius > 3 ? distortion * .2 : 0) - ((double)dx * dx / ((double)rx * rx) + (double)dz * dz / ((double)rz * rz));
        /// <summary>Thickness taper used by vanilla and Earth discs.</summary>
        public static double Taper(double edge) => Math.Clamp(edge * 2 - .2, 0, 1);
    }

    public sealed class RegionalOreCatalog
    {
        /// <summary>Ore rules version saved in worlds (v2: rate classes, bilinear evidence, on-the-fly attempt factors; v3: Earth-like
        /// ores, decision 0006: host search within the depth window, known districts fill any rock, coal measures, copper floor).
        /// Saved as EarthParams.OreRulesVersion, a fix level that every regional-ores world adopts at start (EarthMapSystem);
        /// it gates nothing. The generation version is EarthParams.OreRules (<see cref="OreBackground.CatalogueRules"/>, 0 before v4).</summary>
        public const int Version = 3;
        /// <summary>Heat-map file format version (unchanged).</summary>
        public const int DataVersion = 1;
        public const int Width = 720, Height = 360, Cells = Width * Height;
        public sealed class FileData
        {
            public int Version { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string DatasetId { get; set; }
            public Layer[] Layers { get; set; }
        }
        public sealed class Layer
        {
            public string Code { get; set; }
            public string Label { get; set; }
            public string[] Deposits { get; set; }
            public string Data { get; set; }
            // Byte evidence + byte potential + byte deep evidence + little-endian ushort minimum depth in metres. Unpacked when
            // first read: a world reads only its bound resources' layers (all 44 unpacked are about 57 MB).
            internal byte[] Grid => grid.Value;
            internal Lazy<byte[]> grid;
            internal int Index;
        }
        public readonly record struct Evidence(double Observed, double Potential, double Deep, int MinimumMetres)
        {
            public double Accessible(double depthBlocks, double metresPerBlock) => Math.Max(Math.Max(Observed, Potential), depthBlocks * metresPerBlock >= MinimumMetres ? Deep : 0);
            public double Any => Math.Max(Math.Max(Observed, Potential), Deep);
        }
        public IReadOnlyDictionary<string, Layer> Layers { get; }
        readonly Layer[] ordered;
        readonly Dictionary<string, string> deposits = new(StringComparer.Ordinal);
        public string DatasetId { get; }
        /// <summary>Hash of generation-relevant content only (format, layer codes, bindings and grids; not labels or dataset id).</summary>
        public string ContentHash { get; }
        public RegionalOreCatalog(string json)
        {
            var data = JsonSerializer.Deserialize<FileData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data?.Version != DataVersion || data.Width != Width || data.Height != Height || data.Layers == null || data.Layers.Length == 0 || string.IsNullOrWhiteSpace(data.DatasetId))
                throw new ArgumentException("Unsupported ore heat-map dataset");
            DatasetId = data.DatasetId;
            var layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
            var list = new List<Layer>();
            using var sha = System.Security.Cryptography.SHA256.Create();
            var grid = new byte[Cells * 5];   // each layer is unpacked here to be checked and hashed, and again when first read
            foreach (var l in data.Layers)
            {
                if (string.IsNullOrWhiteSpace(l.Code) || !layers.TryAdd(l.Code, l)) throw new ArgumentException("Duplicate/missing ore resource");
                string packed = l.Data;
                Unpack(packed, grid);
                l.grid = new Lazy<byte[]>(() => { var g = new byte[Cells * 5]; Unpack(packed, g); return g; });
                foreach (string d in l.Deposits ?? Array.Empty<string>())
                    if (!deposits.TryAdd(d, l.Code)) throw new ArgumentException("Ambiguous ore deposit binding: " + d);
                l.Index = list.Count; list.Add(l);
                byte[] head = System.Text.Encoding.UTF8.GetBytes(l.Code + "|" + string.Join(",", (l.Deposits ?? Array.Empty<string>()).OrderBy(s => s, StringComparer.Ordinal)) + "\n");
                sha.TransformBlock(head, 0, head.Length, null, 0);
                sha.TransformBlock(grid, 0, grid.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            ContentHash = Convert.ToHexString(sha.Hash);
            ordered = list.ToArray();
            Layers = layers;
        }
        /// <summary>A layer's grid from its base64 gzip text into <paramref name="into"/> (exactly that size).</summary>
        static void Unpack(string packed, byte[] into)
        {
            using var input = new MemoryStream(Convert.FromBase64String(packed ?? ""));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            gzip.ReadExactly(into);
            if (gzip.ReadByte() != -1) throw new ArgumentException("Oversize ore evidence grid");
        }
        public string ResourceFor(string deposit) => deposit != null && deposits.TryGetValue(deposit, out string c) ? c : null;
        public int LayerIndex(string code) => code != null && Layers.TryGetValue(code, out var l) ? l.Index : -1;
        public int LayerCount => ordered.Length;

        static int WrapX(int x) { x %= Width; return x < 0 ? x + Width : x; }
        /// <summary>Nearest-cell evidence (reports and tests). Generation uses <see cref="Heat"/>.</summary>
        public Evidence At(string code, double lat, double lon, bool outside = false)
        {
            if (outside || !double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || code == null || !Layers.TryGetValue(code, out var l)) return default;
            lon = (lon + 180) % 360; if (lon < 0) lon += 360;
            int x = Math.Clamp((int)(lon * 2), 0, Width - 1), y = Math.Clamp((int)((90 - lat) * 2), 0, Height - 1);
            return CellEvidence(l.Grid, y * Width + x);
        }
        static Evidence CellEvidence(byte[] g, int i) => new(g[i] / 255.0, g[Cells + i] / 255.0, g[2 * Cells + i] / 255.0, g[3 * Cells + i * 2] | g[3 * Cells + i * 2 + 1] << 8);
        static double CellHeat(byte[] g, int i, double depthMetres)
        {
            int shallow = Math.Max(g[i], g[Cells + i]);
            if (g[2 * Cells + i] > shallow && depthMetres >= (g[3 * Cells + i * 2] | g[3 * Cells + i * 2 + 1] << 8)) shallow = g[2 * Cells + i];
            return shallow / 255.0;
        }
        /// <summary>
        /// Bilinearly interpolated, depth-accessible evidence (0..1). Cell centres sit at ((x+.5)/2-180, 90-(y+.5)/2);
        /// longitude wraps, latitude clamps. Accessibility is evaluated per cell, then interpolated. No allocation.
        /// </summary>
        public double Heat(int layer, double lat, double lon, double depthBlocks, double metresPerBlock)
        {
            if ((uint)layer >= (uint)ordered.Length || !double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90) return 0;
            byte[] g = ordered[layer].Grid;
            double fx = (lon + 180) * 2 - .5, fy = (90 - lat) * 2 - .5;
            double x0f = Math.Floor(fx), y0f = Math.Floor(fy);
            double tx = fx - x0f, ty = fy - y0f;
            int x0 = WrapX((int)x0f), x1 = WrapX((int)x0f + 1);
            int y0 = (int)y0f, y1 = y0 + 1;
            if (y0 < 0) { y0 = y1 = 0; ty = 0; }
            else if (y1 > Height - 1) { y0 = y1 = Height - 1; ty = 0; }
            double m = Math.Max(0, depthBlocks) * metresPerBlock;
            double a = CellHeat(g, y0 * Width + x0, m), b = CellHeat(g, y0 * Width + x1, m);
            double c = CellHeat(g, y1 * Width + x0, m), d = CellHeat(g, y1 * Width + x1, m);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }
        public double Heat(string code, double lat, double lon, double depthBlocks, double metresPerBlock) => Heat(LayerIndex(code), lat, lon, depthBlocks, metresPerBlock);

        // Stable coordinate hash: independent of process hash seeds, iteration order and chunk scheduling.
        public static double Random(long seed, int x, int y, int z, int salt = 0) => StableHash.Unit(seed, x, y, z, salt);
        public static int StableCode(string code)
        {
            unchecked { int h = 216613626; foreach (char c in code ?? "") h = (h ^ c) * 16777619; return h; }
        }
        // Explicit gameplay prior, not an estimate of measured geological grade.
        public static double GradeQuantile(double uniform, double evidence, double bias) => Math.Pow(Math.Clamp(uniform, 0, 1), Math.Clamp(2.8 - 2 * evidence - 1.5 * bias, .3, 5));
        public static int GradeIndex(double quantile, int count) => Math.Clamp((int)(quantile * count), 0, Math.Max(0, count - 1));
        public static bool InColumn(int x, int z, int cx, int cz) => x >= cx * 32 && x < (cx + 1) * 32 && z >= cz * 32 && z < (cz + 1) * 32;
    }
}
