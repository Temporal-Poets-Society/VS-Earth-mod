using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>One bound deposit variant. Immutable once <see cref="OreBinder"/> sealed its session (except the one-time base-mean refinement before generation starts).</summary>
    public sealed class OreBinding
    {
        internal int Id;
        public DepositVariant Variant { get; internal set; }
        public EarthOreGenerator Generator { get; internal set; }
        public string Resource { get; internal set; }
        public int Layer { get; internal set; }
        /// <summary>Layers whose maximum drives attempts: own layer plus bound children (the valuable part).</summary>
        public int[] HeatLayers { get; internal set; }
        public OreClass Class { get; internal set; }
        public bool IsChild => Parent != null || ChildOfUnbound;
        internal bool ChildOfUnbound;
        public OreBinding Parent { get; internal set; }
        public OreProfile Profile { get; internal set; }
        /// <summary>Vanilla generated this variant with an ore map (it is listed by the prospecting pick).</summary>
        public bool NativelyMapped { get; internal set; }
        /// <summary>Mean vanilla attempt factor this variant had (vanilla's sparse map mean, or 1 without a map).</summary>
        public double BaseMean { get; internal set; }
        public bool Clustered { get; internal set; }
        public bool KeepsNativeDepth { get; internal set; }
        public bool HostSearch { get; internal set; }
        /// <summary>Rules v3, "fill any rock": 0 never, 1 at strong evidence (OreRules.LensHeat), 2 always (surface starters).</summary>
        public int Lens { get; internal set; }
        public int TextureSalt { get; internal set; }
        internal float MapScale, MapContrast, MapSub;
        internal bool EarthAuthored;
        /// <summary>The session's totals of <see cref="Resource"/> (blocks, then poor/medium/rich/bountiful), null when it has none.</summary>
        internal long[] Totals;
    }

    /// <summary>
    /// All state of one activation of Earth's ore maps, made and sealed by <see cref="OreBinder"/>: the bound variants and the
    /// attempt factors vanilla asks for (from the heat maps, per chunk), with the parts around them: rules v3's ore bodies
    /// (<see cref="V3OreBodies"/>), the bauxite strata replacement (<see cref="BauxiteStrata"/>), rules v4's deposit catalogue
    /// (<see cref="EarthOreDeposits"/>), prospecting (<see cref="OreProspecting"/>) and the totals (<see cref="OreTotals"/>).
    /// Shared data is immutable once sealed; per-thread caches only.
    /// </summary>
    public sealed class OreSession
    {
        /// <summary>This session's key in per-thread caches (a number, so an unloaded world is not kept alive by a thread).</summary>
        internal readonly long CacheId = Interlocked.Increment(ref lastCacheId);
        static long lastCacheId;
        public readonly RegionalOreCatalog Catalog;
        public readonly OreTuning Tuning;
        public readonly long Seed;
        public readonly List<OreBinding> Bindings = new();
        public readonly List<string> Unsupported = new();
        readonly EarthOres owner;
        readonly EarthMapSystem core;
        /// <summary>The saved tuning's metres per block for reading the evidence's depths (Ores.MetresPerBlock, 5 by default),
        /// not the world's scale (<see cref="MetresPerBlock"/>).</summary>
        internal readonly double EvidenceMetresPerBlock;
        /// <summary>The bindings by deposit code, from the seal.</summary>
        internal Dictionary<string, OreBinding[]> ByCode;
        internal readonly int BauxiteId, ClaystoneId, BogPeatId;
        /// <summary>The providers' blocks and the block names, for this binding (D10).</summary>
        internal readonly ProviderBlocks Blocks;
        /// <summary>The world's deposit rate (GenDeposits.chanceMultiplier, from globalDepositSpawnRate): vanilla applies it to
        /// every attempt; Earth applies it to the coal measures and the prospecting readings.</summary>
        internal double DepositRate = 1;
        /// <summary>0065: the world's ore rules (EarthParams.OreRules; 0 before v4, whose generation the logs call rules v3).
        /// Not EarthParams.OreRulesVersion, a fix level that gates nothing.</summary>
        internal readonly int Rules;
        /// <summary>Rules v4 (<see cref="Rules"/> at least OreBackground.CatalogueRules): the deposit catalogue's bodies, every background
        /// at its class floor and sized by the world's scale, no rules v3 ore bodies. Chosen once for the session.</summary>
        internal readonly bool RulesV4;
        /// <summary>From rules v4, the deposit catalogue; null without it.</summary>
        internal EarthOreDeposits Deposits;
        /// <summary>h of decision 0065: metres per block north-south.</summary>
        internal readonly double MetresPerBlock;
        /// <summary>Puts the native deposit generators back as they were before binding (OreBinder); call at most once,
        /// only while this session is not active.</summary>
        internal Action Undo = () => { };
        public bool BogPeatActive { get; internal set; }
        /// <summary>The bound main peat deposit (bog iron forms in it), set while binding.</summary>
        internal OreBinding Peat;
        readonly int feLayer;
        /// <summary>Rules v3's ore bodies (none in rules v4 or without the strata hook: <see cref="V3OreBodies.Beds"/> empty).</summary>
        internal readonly V3OreBodies Bodies;
        /// <summary>Where the binding put rules v3's ore bodies: after the rock strata, or (ore-layer worlds) after the ground
        /// pass; null where they are not laid.</summary>
        internal V3OreBodies BodiesAfterStrata, BodiesAfterGround;
        /// <summary>The bauxite strata replacement, null where the binding keeps vanilla's strata.</summary>
        internal BauxiteStrata Bauxite;
        internal readonly OreProspecting Prospecting;
        internal readonly OreTotals Totals;
        /// <summary>(CR-O4) The error latches of this session's generators and hooks, counted with Interlocked: each logs its
        /// first error once per session, so a new world or the re-binding after /wgen regen reports its own first error
        /// instead of staying silent for the rest of the process. They only decide logging, never generation.</summary>
        internal int RangeErrors, ReadingErrors, SearchErrors, GenErrors, FactorErrors, StrataErrors, SeamErrors, DepositErrors, ColumnErrors;

        internal OreSession(EarthOres owner, ICoreServerAPI api, EarthMapSystem core, RegionalOreCatalog catalog, OreTuning tuning, EarthGeology geology)
        {
            this.owner = owner; this.core = core; Catalog = catalog; Tuning = tuning;
            // EarthGeology starts before the ore maps bind (both at WorldReady) and does not change its mind later
            Geology = geology?.Active == true ? geology : null;
            Layers = Geology?.Layers;
            Seed = core.Params.NoiseSeed;
            EvidenceMetresPerBlock = tuning.MetresPerBlock;
            Rules = core.Params.OreRules;
            RulesV4 = OreBackground.CatalogueBodies(Rules);
            MetresPerBlock = core.Model.MetresPerBlock;
            feLayer = catalog.LayerIndex("Fe");
            BauxiteId = api.World.GetBlock(new AssetLocation("game:rock-bauxite"))?.Id ?? 0;
            ClaystoneId = api.World.GetBlock(new AssetLocation("game:rock-claystone"))?.Id ?? 0;
            BogPeatId = api.World.GetItem(new AssetLocation("game:stone-bogiron")) != null ? api.World.GetBlock(new AssetLocation("earth:bogpeat"))?.Id ?? 0 : 0;
            Blocks = new ProviderBlocks(OreBlockNames.Of(api));
            Totals = new OreTotals(catalog, Blocks.Names);
            Bodies = new V3OreBodies(this);
            Prospecting = new OreProspecting(this);
        }
        /// <summary>Earth's geology when it runs in this world, else null (vanilla strata).</summary>
        internal readonly EarthGeology Geology;
        /// <summary>0060: the world's ore layers (EarthParams.OreLayers), null when off: the generators ask each block's layer,
        /// nothing fills rock that is not its host, and the beds are laid after the ground pass.</summary>
        internal readonly EarthOreLayers Layers;
        internal ILogger Log => owner.Log;
        internal EarthMapSystem Core => core;
        /// <summary>A rock block other than bauxite (any rock-: the ore layers' rule, OreBlockNames.RockName): what rules v3's
        /// ore bodies may replace and bauxite strata turn into.</summary>
        internal bool IsRock(int id) => Blocks.Names.IsRock(id) && id != BauxiteId;

        // ---------------- per-chunk context (thread-local) ----------------
        internal sealed class ChunkCtx
        {
            public long Key; public long Owner; public bool Outside; public int Top; public double Lat, Lon; public double[] Heat;
        }
        [ThreadStatic] static ChunkCtx[] ctxCache;
        internal ChunkCtx Context(int chunkX, int chunkZ)
        {
            var cache = ctxCache ??= new ChunkCtx[512];
            long key = ((long)chunkX << 32) ^ (uint)chunkZ;
            int slot = (int)((uint)(chunkX * 73856093 ^ chunkZ * 19349663) & 511);
            var c = cache[slot];
            if (c != null && c.Key == key && c.Owner == CacheId) return c;
            c ??= cache[slot] = new ChunkCtx();
            c.Owner = 0;   // incomplete while filling
            if (c.Heat == null || c.Heat.Length != Catalog.LayerCount) c.Heat = new double[Catalog.LayerCount];
            Array.Fill(c.Heat, double.NaN);
            double x = chunkX * 32 + 16, z = chunkZ * 32 + 16;
            var g = core.Model.Geometry.ToGeo(x, z);
            c.Outside = g.InGutter || g.BeyondPole; c.Lat = g.Lat; c.Lon = g.Lon;
            c.Top = c.Outside ? 0 : TopAt(x, z);
            c.Key = key; c.Owner = CacheId;
            return c;
        }
        /// <summary>Model surface; equals the generated WorldGenTerrainHeightMap (EarthWorldGen writes Column.Top), chunk-independent.</summary>
        internal int TopAt(double x, double z) => Math.Max(1, ColumnStore.Near(core.Model, x, z).Top);
        /// <summary>Chunk-level heat: evidence accessible anywhere in the column (upper bound of the per-deposit heat).</summary>
        internal double ChunkHeat(ChunkCtx c, int layer)
        {
            if ((uint)layer >= (uint)c.Heat.Length) return 0;
            double h = c.Heat[layer];
            if (double.IsNaN(h)) c.Heat[layer] = h = Catalog.Heat(layer, c.Lat, c.Lon, c.Top, EvidenceMetresPerBlock);
            return h;
        }
        internal double ChunkHeat(ChunkCtx c, int[] layers)
        {
            double h = 0;
            for (int i = 0; i < layers.Length; i++) h = Math.Max(h, ChunkHeat(c, layers[i]));
            return h;
        }
        internal double HeatAtDepth(ChunkCtx c, int[] layers, double depthBlocks)
        {
            double h = 0;
            for (int i = 0; i < layers.Length; i++) h = Math.Max(h, Catalog.Heat(layers[i], c.Lat, c.Lon, depthBlocks, EvidenceMetresPerBlock));
            return h;
        }
        internal double Texture(OreBinding b, int chunkX, int chunkZ)
            => b.Clustered ? OreRules.ClusteredTexture(Seed, chunkX, chunkZ, b.TextureSalt)
             : OreRules.SoftTexture(Seed, chunkX, chunkZ, b.TextureSalt, b.Class == OreClass.Surface ? OreRules.SurfaceAmplitude : OreRules.SoftAmplitude);
        /// <summary>Parent attempt ratio vs vanilla at this chunk, driven by max(parent, child) heat (rules v4: the heat that
        /// raises it, see <see cref="OreBackground.Evidence"/>).</summary>
        internal double ParentRatio(OreBinding parent, ChunkCtx c) => parent == null ? 1 : parent.Profile.Ratio(Evidence(parent, ChunkHeat(c, parent.HeatLayers)));
        /// <summary>The heat that raises a binding's attempts (<see cref="OreBackground.Evidence"/>): the heat itself before rules
        /// v4. Attempts and the acceptance by depth read the same heat.</summary>
        internal double Evidence(OreBinding b, double heat) => OreBackground.Evidence(Rules, b.Resource, b.Class, heat, b.Variant.Code);
        /// <summary>A background deposit's attempts relative to vanilla at this heat, before the world's scale and texture:
        /// rules v4 keep it at its class floor, in its host rock, unless the catalogue never places the resource (0065 section 5).</summary>
        internal double BackgroundRatio(OreBinding b, double heat) => b.Profile.Ratio(Evidence(b, heat));

        /// <summary>
        /// Replacement for vanilla's ore-map factor: vanilla mean x Earth ratio x mean-1 texture. Vanilla multiplies it by
        /// TriesPerChunk (unchanged), so the expected attempts relative to vanilla are exactly the class ratio.
        /// For children it is the per-parent-block factor (Frequency applied once, child gated by its own evidence).
        /// </summary>
        public double AttemptFactor(OreBinding b, int chunkX, int chunkZ)
        {
            var c = Context(chunkX, chunkZ);
            if (c.Outside) return 0;
            double tex = Texture(b, chunkX, chunkZ);
            if (b.IsChild)
            {
                double own = ChunkHeat(c, b.Layer);
                return b.BaseMean * OreRules.ChildRatio(b.Profile, Evidence(b, own), ParentRatio(b.Parent, c)) * tex;
            }
            double heat = ChunkHeat(c, b.HeatLayers);
            // 0065 section 5 (rules v4): the class floor everywhere in the host rock, no hotspot boost where the catalogue
            // places the real deposits; attempts follow the world's scale
            if (RulesV4) return b.Generator.AttemptScale * b.BaseMean * BackgroundRatio(b, heat) * tex;
            return b.BaseMean * b.Profile.Ratio(heat) * tex;
        }

        /// <summary>Finite bog iron in buried natural peat: BogIronChance x (1 + 2 x shallow iron evidence).</summary>
        public int BogPeat(int x, int y, int z)
        {
            if (!BogPeatActive) return 0;
            var c = Context(x >> 5, z >> 5);
            double e = feLayer >= 0 ? Catalog.Heat(feLayer, c.Lat, c.Lon, 0, EvidenceMetresPerBlock) : 0;
            if (RegionalOreCatalog.Random(Seed, x, y, z, 54827) >= Tuning.BogIronChance * (1 + 2 * e)) return 0;
            Totals.CountBogPeat();
            return BogPeatId;
        }

        internal OreBinding[] SameCode(OreBinding b) => ByCode != null && ByCode.TryGetValue(b.Variant.Code, out var a) ? a : new[] { b };

        public string GenerationTotals() => (Deposits != null ? Deposits.Totals() + "; " : "") + Totals.Text();

        /// <summary>/earth ores: the evidence and attempts of the main resources here, rules v3's ore bodies and the catalogue's
        /// deposits near.</summary>
        public string Describe(double x, double z)
        {
            var g = core.Model.Geometry.ToGeo(x, z);
            int cx = (int)Math.Floor(x / 32), cz = (int)Math.Floor(z / 32);
            var c = Context(cx, cz);
            string Line(string code)
            {
                var e = Catalog.At(code, g.Lat, g.Lon, g.InGutter || g.BeyondPole);
                int layer = Catalog.LayerIndex(code);
                var b = Bindings.FirstOrDefault(q => q.Resource == code && !q.IsChild && q.Class != OreClass.Surface && !EarthOreLayers.IsLayerDeposit(q.Variant)) ?? Bindings.FirstOrDefault(q => q.Resource == code);
                double h = b == null ? 0 : ChunkHeat(c, b.HeatLayers);
                string rate = b == null ? "no installed generator" : string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00}x vanilla attempts here", b.Class, BackgroundRatio(b, h));
                return string.Format(CultureInfo.InvariantCulture, "{0}: evidence={1:0.00}, potential={2:0.00}, deep={3:0.00}@{4}m, interpolated heat={5:0.00}; {6}",
                    code, e.Observed, e.Potential, e.Deep, e.MinimumMetres, layer >= 0 && !c.Outside ? ChunkHeat(c, layer) : 0, rate);
            }
            string seams = Bodies.Describe(x, z);
            string catalogue = "";
            if (Deposits != null)
            {
                // about 50 km around, at least 8 and at most 128 chunks
                int radius = (int)Math.Clamp(50000 / MetresPerBlock, 256, 4096);
                catalogue = ". " + Deposits.Near(x, z, radius);
            }
            return string.Join("; ", new[] { "Cu", "Sn", "Fe", "C_coal", "NaCl", "clay", "peat" }.Where(Catalog.Layers.ContainsKey).Select(Line)) + seams + catalogue;
        }
    }
}
