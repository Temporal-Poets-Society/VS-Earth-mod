using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Seeded deposit geometry using provider-resolved host/output pairs. Every per-deposit decision is a pure function
    /// of (world seed, variant, deposit centre) and cached per thread, so all chunks a deposit spans agree.
    /// Wraps only the exact vanilla disc/child generator types.
    /// </summary>
    public sealed class EarthOreGenerator : DepositGeneratorBase
    {
        enum Kind { FollowSurface, FollowSurfaceBelow, FollowSealevel, Anywhere, Child }
        readonly EarthOres owner;
        internal readonly OreSession Session;
        internal readonly OreBinding Binding;
        readonly DiscDepositGenerator native;
        /// <summary>0060: the world's ore layers and this deposit's layer rule; null in worlds without EarthParams.OreLayers.</summary>
        readonly EarthOreLayers layers;
        internal EarthOreLayers Layers => layers;
        readonly OreLayers.Rule layerRule;
        /// <summary>The provider's generator this one wraps (its host and indicator tables), for the layer pass (0060).</summary>
        internal DiscDepositGenerator Native => native;
        readonly OreProfile profile;
        readonly Dictionary<int, Block[]> hosts, indicators;
        readonly Dictionary<int, bool> graded;
        /// <summary>Rules v3, "fill any rock": for each rock that isn't a host, the ore blocks of the most similar host
        /// (OreRules.SimilarHost), whether they are graded, and that host (for its surface indicators).</summary>
        readonly Dictionary<int, (Block[] Blocks, bool Graded, int Host)> anyRock = new();
        /// <summary>Fixed draws of the native position distribution, for prospecting (PlacedShare).</summary>
        readonly double[] positionSamples;
        readonly int salt;
        readonly Kind kind;
        readonly NatFloat positionDist;
        readonly int radiusCap;
        /// <summary>Expected placed blocks per attempt (hosts assumed), for prospecting expectations.</summary>
        readonly double expectedVolume;
        /// <summary>Child deposits: per fixed draw, the point cloud's radius and its tries x concentration (ChildBlocks).</summary>
        readonly (int Radius, double Tries)[] childSamples = Array.Empty<(int, double)>();
        /// <summary>Middle of this variant's depth window (blocks below the surface) at a column of the given height.</summary>
        internal double TypicalDepth(int top)
        {
            DepthWindow(top, out double min, out double max);
            return (min + max) / 2;
        }
        /// <summary>Single-block gem pockets (native radius under 2: emerald, peridot), which keep their native size.</summary>
        internal bool GemPocket => native.Radius != null && native.Radius.avg < 2;
        /// <summary>Mean thickness (blocks) of this variant's deposits, for its children's readings.</summary>
        internal double MeanThickness => Math.Max(1, native.Thickness.avg * profile.Thickness);
        readonly double triesScale;
        readonly int[] ownLayer;
        /// <summary>The deepest block below the surface a disc may fill (the saved MaxDepthBlocks; none for deposits that keep
        /// their native depth).</summary>
        readonly double maxDepth;
        double radiusMul;
        /// <summary>0065 (rules v4): attempts per chunk times this keep the blocks per chunk of the 1:100 calibration when the
        /// world's scale changes the deposits' radius (1 before v4).</summary>
        internal double AttemptScale { get; } = 1;
        public bool RockHosted { get; }
        public IEnumerable<string> HostSignature => hosts.OrderBy(p => p.Key).Select(p => Api.World.GetBlock(p.Key).Code + ":" + string.Join(",", p.Value.Select(b => b.Code.ToString())));

        public EarthOreGenerator(EarthOres owner, OreSession session, ICoreServerAPI api, DepositVariant variant, DiscDepositGenerator original, OreBinding binding)
            : base(api, variant, original.DepositRand, original.DistortNoiseGen)
        {
            this.owner = owner; Session = session; Binding = binding; native = original; profile = binding.Profile;
            var t = original.GetType();
            kind = t == typeof(ChildDepositGenerator) ? Kind.Child : t == typeof(FollowSurfaceDiscGenerator) ? Kind.FollowSurface : t == typeof(FollowSurfaceBelowDiscGenerator) ? Kind.FollowSurfaceBelow
                : t == typeof(FollowSealevelDiscGenerator) ? Kind.FollowSealevel : t == typeof(AnywhereDiscGenerator) ? Kind.Anywhere
                : throw new InvalidOperationException("unsupported generator " + t.Name);
            positionDist = kind switch
            {
                Kind.FollowSurface => ((FollowSurfaceDiscGenerator)original).YPosRel,
                Kind.FollowSealevel => ((FollowSealevelDiscGenerator)original).YPosRel,
                Kind.Anywhere => ((AnywhereDiscGenerator)original).YPosRel,
                Kind.FollowSurfaceBelow => original.Depth,
                _ => null,
            };
            if (kind != Kind.Child && (positionDist == null || original.Radius == null || original.Thickness == null)) throw new InvalidOperationException("incomplete native size/depth model");
            if (kind == Kind.Child && (original.Radius == null || ((ChildDepositGenerator)original).RandomTries == null)) throw new InvalidOperationException("incomplete native child model");
            salt = RegionalOreCatalog.StableCode(variant.Code + "|" + variant.Generator + "|" + variant.Attributes);
            ownLayer = new[] { binding.Layer };
            maxDepth = profile.MaxDepth > 0 && !binding.KeepsNativeDepth ? profile.MaxDepth : double.PositiveInfinity;
            radiusMul = kind == Kind.Child ? profile.Radius : OreRules.EffectiveRadiusMultiplier(original.Radius.avg, profile.Radius);
            radiusCap = variant.Climate == null ? 64 : 31;
            // the provider's blocks as every ore consumer reads them (ProviderBlocks, D10), shared with the deposit catalogue
            var provided = session.Blocks.Of(original) ?? throw new InvalidOperationException("provider host table unavailable");
            hosts = provided.Hosts; graded = provided.Graded; indicators = provided.Indicators;
            if (hosts.Count == 0) throw new InvalidOperationException("no usable hosts");
            var names = session.Blocks.Names;
            // any rock- host (also halite, bauxite and dashed names) makes a deposit rock-hosted
            RockHosted = hosts.Keys.Any(names.IsRock);
            // 0060: in ore-layer worlds every block asks its layer, and no deposit fills rock that is not its host
            layers = session.Layers;
            layerRule = layers?.RuleFor(variant, RockHosted);
            // rules v3 without ore layers: every other plain rock takes its most similar host's ore, by the hosts in the
            // provider's order
            var hostRocks = hosts.Keys.Select(id => (Rock: names.PlainRock(id), Id: id)).Where(h => h.Rock != null).ToList();
            if (hostRocks.Count > 0 && layers == null)
                session.Blocks.SimilarHosts(hostRocks, hosts.ContainsKey, (rock, host) => anyRock[rock] = (hosts[host], graded[host], host));
            if (positionDist != null)
            {
                var draw = new LCGRandom(4242);
                positionSamples = new double[16];
                for (int i = 0; i < positionSamples.Length; i++) { draw.InitPositionSeed(i, 7); positionSamples[i] = positionDist.nextFloat(1, draw); }
            }
            else positionSamples = Array.Empty<double>();
            triesScale = variant.ScaleWithWorldheight ? api.WorldManager.MapSizeY / 256.0 : 1;
            // 0065 section 5 (rules v4): rock-hosted background deposits take the world's scale relative to 1:100 (bigger and
            // rarer at fine scales, smaller and closer at coarse ones); gem pockets, soil and layer deposits and children keep theirs
            double baseRadiusMul = radiusMul;
            if (session.RulesV4 && kind != Kind.Child && RockHosted && !GemPocket && binding.Class != OreClass.Soil && !EarthOreLayers.IsLayerDeposit(variant))
                radiusMul *= OreBackground.RadiusScale(session.Rules, session.MetresPerBlock, original.Radius.avg * radiusMul, radiusCap);
            double baseVolume = 0;
            // Deterministic expectations from the native distributions (same rounding as placement).
            var rnd = new LCGRandom(9001); double vol = 0;
            var kidSamples = kind == Kind.Child ? new List<(int, double)>(256) : null;
            for (int i = 0; i < 256; i++)
            {
                rnd.InitPositionSeed(i, 3);
                if (kind == Kind.Child)
                {
                    double r = Math.Clamp(Math.Round(original.Radius.nextFloat(1, rnd) * profile.Radius), 1, 64);
                    double tries = Math.Clamp((int)(((ChildDepositGenerator)original).RandomTries.nextFloat(1, rnd) * profile.Thickness), 0, 20000);
                    kidSamples.Add(((int)r, tries * profile.Concentration));
                    continue;
                }
                double drawn = original.Radius.nextFloat(1, rnd);
                int rad = Math.Clamp((int)Math.Round(drawn * radiusMul), 0, radiusCap);
                int rad0 = Math.Clamp((int)Math.Round(drawn * baseRadiusMul), 0, radiusCap);
                double th = Math.Max(0, original.Thickness.nextFloat(1, rnd) * profile.Thickness);
                if (rad > 0) vol += Math.PI * rad * rad * ((rad > 3 ? .9 : 1) - .35) * th;
                if (rad0 > 0) baseVolume += Math.PI * rad0 * rad0 * ((rad0 > 3 ? .9 : 1) - .35) * th;
            }
            expectedVolume = vol / 256 * profile.Concentration;
            if (radiusMul != baseRadiusMul) AttemptScale = vol > 0 ? baseVolume / vol : 0;
            if (kidSamples != null) childSamples = kidSamples.ToArray();
        }


        // ---------------- vertical model ----------------
        double NativePosition(IRandom rnd) => positionDist.nextFloat(1, rnd);
        OreAnchor Anchor => kind switch
        {
            Kind.FollowSurface => OreAnchor.SurfaceFraction,
            Kind.FollowSurfaceBelow => OreAnchor.BelowSurface,
            _ => OreAnchor.AbsoluteY,
        };
        double NativeDepth(double position, int top) => OreRules.NativeDepth(Anchor, position, top);
        double DepthFor(double nativeDepth) => Binding.KeepsNativeDepth ? Math.Max(0, nativeDepth) : profile.Depth(nativeDepth);
        /// <summary>Burial at a column, NaN where the deposit plane lies above the ground (vanilla places nothing there).</summary>
        double Burial(double position, int top) => OreRules.Burial(Anchor, position, top, profile, Binding.KeepsNativeDepth);
        /// <summary>Allowed depth window (blocks below the local surface) of this variant at a column of the given height.</summary>
        void DepthWindow(int top, out double min, out double max)
        {
            double a = DepthFor(Math.Max(0, NativeDepth(positionDist.avg - positionDist.var, top)));
            double b = DepthFor(Math.Max(0, NativeDepth(positionDist.avg + positionDist.var, top)));
            min = Math.Min(a, b); max = Math.Max(a, b);
        }
        public override float GetMaxRadius() => (float)Math.Min(80, (native.Radius.avg + Math.Abs(native.Radius.var)) * radiusMul * 1.3);
        public override void GetYMinMax(BlockPos pos, out double miny, out double maxy)
        {
            miny = 1; maxy = Api.World.BlockAccessor.MapSizeY - 1;
            try
            {
                if (kind == Kind.Child) { native.GetYMinMax(pos, out miny, out maxy); return; }
                int top = TopAt(pos);
                DepthWindow(top, out double dmin, out double dmax);
                miny = Math.Max(1, top - dmax - Math.Max(1, native.Thickness.avg * profile.Thickness));
                maxy = Math.Max(miny, Math.Min(top, top - dmin));
            }
            catch (Exception e)
            {
                // the range stays the whole height of the world; logged once per session (CR-O4)
                if (Interlocked.Increment(ref Session.RangeErrors) == 1) owner.Log.Error("Earth ores: the depth range of {0} failed; the world's whole height is used (further errors suppressed). {1}", variant.Code, e);
            }
        }
        /// <summary>The surface at a column: the generated height map, else the model's.</summary>
        internal int TopAt(BlockPos pos)
        {
            int top = Api.World.BlockAccessor.GetTerrainMapheightAt(pos);
            return top > 0 ? top : Session.TopAt(pos.X, pos.Z);
        }

        // ---------------- prospecting ----------------
        public override void GetPropickReading(BlockPos pos, int oreDist, int[] blockColumn, out double ppt, out double totalFactor)
        {
            ppt = 0; totalFactor = 0;
            // (CR-O3) vanilla's prospecting workspace lists the deposit variants once, at RunGame, and keeps them after /wgen
            // regen, so the pick may still ask this generator of a retired session: the reading is the current session's, by
            // its binding of the same code (Reading sums every binding of the code); nothing when the session is gone or has
            // no such code
            var cur = owner.Session;
            var asked = this;
            if (cur != Session)
            {
                if (cur?.ByCode == null || !cur.ByCode.TryGetValue(variant.Code, out var same) || same.Length == 0) return;
                asked = same[0].Generator;
            }
            try { cur.Prospecting.Reading(asked, pos, blockColumn, out ppt, out totalFactor); }
            catch (Exception e)
            {
                ppt = 0; totalFactor = 0;
                // logged once per session (CR-O4)
                if (Interlocked.Increment(ref cur.ReadingErrors) == 1) owner.Log.Error("Earth ores prospecting reading failed for {0}; reporting nothing. {1}", variant.Code, e);
            }
        }
        internal double ExpectedPerChunk => variant.TriesPerChunk * triesScale * expectedVolume;

        /// <summary>
        /// Expected child blocks per seeded parent block: the point cloud tries positions in a cube of side 2r+1 and keeps
        /// those in parent ore, which fills about min(1, parent thickness / (2r+1)) of the cube's layers (one layer of a thin
        /// sheet, all of them inside a thick salt dome).
        /// </summary>
        internal double ChildBlocks(double parentThickness)
        {
            if (childSamples.Length == 0) return 0;
            double sum = 0;
            foreach (var (r, tries) in childSamples) sum += tries * Math.Min(1, parentThickness / (2 * r + 1));
            return sum / childSamples.Length;
        }

        /// <summary>
        /// Rules v3, for prospecting: the share of this variant's deposits at a column that lands in host rock, averaged over
        /// fixed draws of the native depth distribution and using placement's own rules (Complete): each draw is accepted
        /// with chance(heat at its burial) / chance(column heat), so buried-only evidence doesn't raise shallow draws (rules
        /// v4: the heat that raises attempts, OreSession.Evidence); a draw
        /// in a known district (by the heat at its burial) fills any rock; elsewhere one whose depth has no host may move
        /// within its depth window and is kept with the window's host share (HostShift). A deposit counts by the share of its
        /// thickness that finds host rock.
        /// </summary>
        internal double PlacedShare(int x, int z, int[] column, int top, OreSession.ChunkCtx ctx, System.Func<double, double> perDepth = null)
        {
            if (top < 2 || positionSamples.Length == 0 || ctx.Outside) return 0;
            double denom = profile.Chance(Session.Evidence(Binding, Session.ChunkHeat(ctx, Binding.HeatLayers)));
            if (denom <= 0) return 0;
            var geo = Session.Geology;
            System.Func<int, int> earth = geo == null ? null : d => geo.RegionalRock(x, z, d, top);
            double thick = Math.Max(1, native.Thickness.avg * profile.Thickness), sum = 0;
            foreach (double position in positionSamples)
            {
                double burial = Burial(position, top);
                if (double.IsNaN(burial)) continue;
                double heat = Session.HeatAtDepth(ctx, Binding.HeatLayers, burial);
                double accept = Math.Min(1, profile.Chance(Session.Evidence(Binding, heat)) / denom);
                if (accept <= 0) continue;
                bool lens = Binding.Lens == 2 || Binding.Lens == 1 && heat >= OreRules.LensHeat;
                int yTop = top - (int)Math.Round(burial);
                // as Complete: a deposit whose drawn depth has no host moves within its window and is kept with the share
                int shift = 0;
                double share = 1;
                var gate = LayerGate(x, z, top);
                if (!lens && Binding.HostSearch && earth != null) shift = HostShift(earth, top, burial, out share, gate);
                double fill = shift != 0 ? share * Fill(column, yTop + shift, thick, top, false, perDepth, gate) : Fill(column, yTop, thick, top, lens, perDepth, gate);
                sum += accept * fill;
            }
            return sum / positionSamples.Length;
        }
        /// <summary>
        /// For a child deposit (point cloud in its parent's blocks) at a parent burial: GenerateChild's chance to keep it there,
        /// chance(child heat at that depth) / chance(child heat of the chunk) (rules v4: the heat that raises attempts), and 0
        /// outside the child's MinDepth/MaxDepth.
        /// </summary>
        internal double ChildDepthFactor(OreSession.ChunkCtx ctx, double depth)
        {
            if (depth < profile.MinDepth || profile.MaxDepth > 0 && depth > profile.MaxDepth) return 0;
            double denom = profile.Chance(Session.Evidence(Binding, Session.ChunkHeat(ctx, Binding.Layer)));
            return denom <= 0 ? 0 : Math.Min(1, profile.Chance(Session.Evidence(Binding, Session.HeatAtDepth(ctx, ownLayer, depth))) / denom);
        }

        /// <summary>Share of a deposit's thickness from yTop down that placement fills (GenerateDisc's limits: the column, the
        /// surface, MaxDepthBlocks; host rock, or any rock in a known district). With <paramref name="perDepth"/> (a child
        /// deposit) each filled block counts by that factor at its own depth, as children are seeded block by block.</summary>
        double Fill(int[] column, int yTop, double thick, int top, bool anyRockToo, System.Func<double, double> perDepth = null, System.Func<int, int, bool> gate = null)
        {
            double weighted = 0;
            int n = Math.Max(1, (int)Math.Round(thick)), hit = 0;
            for (int k = 0; k < n; k++)
            {
                int y = yTop - k;
                if (y <= 1 || y >= column.Length || y > top) continue;
                if (top - y > maxDepth) continue;
                int r = column[y];
                if (!(hosts.ContainsKey(r) || anyRockToo && anyRock.ContainsKey(r))) continue;
                if (gate != null && !gate(top - y, r)) continue;
                hit++;
                if (perDepth != null) weighted += perDepth(top - y);
            }
            return (perDepth != null ? weighted : hit) / n;
        }

        // ---------------- per-deposit plan (thread-local cache) ----------------
        sealed class Plan
        {
            public OreBinding Owner; public int X, Z;
            public bool Stage2, Valid;
            public int Radius, Rx, Rz, CentreTop, Shift;
            public double GradeRandom, Position, Thickness, CluesRoll, AcceptRoll, Quantile, HostRoll;
            public bool Clues;
            /// <summary>Rules v3: this deposit fills any rock (a known district, or a surface starter).</summary>
            public bool Lens;
        }
        [ThreadStatic] static Plan[] plans;
        [ThreadStatic] static LCGRandom tlsRand;
        static LCGRandom Rand(long seed, int x, int z)
        {
            var r = tlsRand ??= new LCGRandom(0);
            r.SetWorldSeed(seed); r.InitPositionSeed(x, z);
            return r;
        }
        Plan GetPlan(BlockPos center)
        {
            var cache = plans ??= new Plan[2048];
            int slot = (int)((uint)(center.X * 73856093 ^ center.Z * 19349663 ^ Binding.Id * 83492791) & 2047);
            var p = cache[slot];
            if (p != null && p.Owner == Binding && p.X == center.X && p.Z == center.Z) return p;
            p ??= cache[slot] = new Plan();
            p.Owner = Binding; p.X = center.X; p.Z = center.Z; p.Stage2 = false;
            // Fixed draw order: independent of which chunk asks first.
            var rnd = Rand(Session.Seed + salt, center.X, center.Z);
            p.GradeRandom = rnd.NextDouble();
            p.Radius = Math.Clamp((int)Math.Round(native.Radius.nextFloat(1, rnd) * radiusMul), 0, radiusCap);
            double skew = (rnd.NextDouble() - .5) * .5;
            p.Rx = Math.Max(1, (int)(p.Radius * (1 - skew))); p.Rz = Math.Max(1, (int)(p.Radius * (1 + skew)));
            p.Position = NativePosition(rnd);
            p.Thickness = Math.Max(0, native.Thickness.nextFloat(1, rnd) * profile.Thickness);
            p.CluesRoll = rnd.NextDouble(); p.AcceptRoll = rnd.NextDouble();
            p.HostRoll = rnd.NextDouble(); // drawn last: the earlier draws are those of rules v2
            p.Valid = p.Radius > 0;
            return p;
        }
        /// <summary>Expensive, chunk-independent part, computed only for deposits that reach the generating chunk. A throw
        /// half way leaves the plan unfinished (Stage2 false), so the next chunk that asks completes it anew: before, it
        /// stayed "complete" with the values of whatever deposit had used the slot before (ER-21, code review 14 Nov 2026).</summary>
        void Complete(Plan p, BlockPos center)
        {
            try { CompleteStage2(p, center); }
            catch
            {
                p.Stage2 = false;
                throw;
            }
        }

        void CompleteStage2(Plan p, BlockPos center)
        {
            p.Stage2 = true;
            var ctx = Session.Context(center.X >> 5, center.Z >> 5);
            if (ctx.Outside) { p.Valid = false; return; }
            p.CentreTop = Session.TopAt(center.X, center.Z);
            // Vanilla places nothing for a deposit whose centre lies above the ground; do not clamp it under the surface.
            double depth = Burial(p.Position, p.CentreTop);
            if (double.IsNaN(depth)) { p.Valid = false; return; }
            double chunkHeat = Session.ChunkHeat(ctx, Binding.HeatLayers);
            double heat = Session.HeatAtDepth(ctx, Binding.HeatLayers, depth);
            // A buried-only hotspot must not boost a shallow attempt; attempts were drawn at the column's best heat. Rules v4
            // draw them at the heat that raises them (none for the resources the catalogue places), so the same heat decides
            // here: their background floor holds at every depth, not only where the records are shallow.
            double denom = profile.Chance(Session.Evidence(Binding, chunkHeat));
            if (denom <= 0 || p.AcceptRoll > profile.Chance(Session.Evidence(Binding, heat)) / denom) { p.Valid = false; return; }
            p.Quantile = RegionalOreCatalog.GradeQuantile(p.GradeRandom, heat, profile.QualityBias);
            p.Clues = p.CluesRoll < native.GenSurfaceBlockChance;
            p.Lens = Binding.Lens == 2 || Binding.Lens == 1 && heat >= OreRules.LensHeat;
            p.Shift = 0;
            var geo = Session.Geology;
            if (!p.Lens && Binding.HostSearch && geo != null)
            {
                int top = p.CentreTop;
                p.Shift = HostShift(d => geo.RegionalRock(center.X, center.Z, d, top), top, depth, out double share, LayerGate(center.X, center.Z, top));
                // a moved deposit is kept only as often as one in this window would have found its host by itself
                if (p.Shift != 0 && p.HostRoll >= share) p.Valid = false;
            }
        }
        /// <summary>
        /// Rules v3: when Earth's strata (<paramref name="rock"/>: block at a depth below the surface, 0 where Earth keeps
        /// vanilla strata) have no host at the drawn depth, move the deposit to the nearest host band inside the variant's
        /// own depth window (v2 also searched the whole column, which turned vanilla's misses into hits). Returns the shift
        /// (blocks up), 0 when nothing moves; <paramref name="share"/> is the window's host share, the chance that a deposit
        /// there would have found its host by itself (placement keeps a moved deposit with that chance). Deterministic.
        /// </summary>
        int HostShift(System.Func<int, int> rock, int top, double depth, out double share, System.Func<int, int, bool> gate = null)
        {
            share = 1;
            // a host at a depth: the deposit's own rock (0: Earth keeps vanilla strata, unknown) in a layer it may use (0060)
            bool Ok(int d, int r) => r <= 0 || hosts.ContainsKey(r) && (gate == null || gate(d, r));
            // (CR-O4) no process-wide switch-off after an error: it flipped mid-world (one deposit's plan differed between the
            // chunks completed before and after it) and carried into the next world; a failing search still moves nothing
            if (rock == null) return 0;
            try
            {
                if (top < 4) return 0;
                int d0 = Math.Clamp((int)Math.Round(depth), 1, top - 1);
                int r0 = rock(d0);
                if (Ok(d0, r0)) return 0;
                DepthWindow(top, out double wmin, out double wmax);
                int lo = Math.Clamp((int)Math.Floor(wmin), 1, top - 1), hi = Math.Clamp((int)Math.Ceiling(wmax), 1, top - 1);
                // Stride 2 cannot skip a stratum: synthesized beds are at least 2 blocks thick.
                int seen = 0, found = 0;
                for (int d = lo; d <= hi; d += 2)
                {
                    int r = rock(d);
                    seen++;
                    if (Ok(d, r)) found++;
                }
                share = seen > 0 ? found / (double)seen : 0;
                if (found == 0) return 0;
                for (int step = 2; d0 + step <= hi || d0 - step >= lo; step += 2)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        int d = side == 0 ? d0 + step : d0 - step;
                        if (d < lo || d > hi) continue;
                        int r = rock(d);
                        if (!Ok(d, r)) continue;
                        // Start the sheet at the top of that host band (within the window) so its thickness extends into the band.
                        for (int up = 0; up < 6 && d - 1 >= lo; up++)
                        {
                            int above = rock(d - 1);
                            if (!Ok(d - 1, above)) break;
                            d--;
                        }
                        return d0 - d;
                    }
                }
            }
            catch (Exception e)
            {
                // this deposit is not moved; logged once per session (CR-O4)
                if (Interlocked.Increment(ref Session.SearchErrors) == 1) owner.Log.Error("Earth ores host-band search failed for {0}; that deposit is not moved (further errors suppressed). {1}", variant.Code, e);
            }
            return 0;
        }

        /// <summary>0060: whether a host rock at a depth below a column's top lies in a layer this deposit may use, from the
        /// model (any column); null in worlds without ore layers.</summary>
        System.Func<int, int, bool> LayerGate(int x, int z, int top)
        {
            var geo = Session.Geology;
            if (layers == null || geo == null) return null;
            return (d, r) => OreLayers.Allows(layerRule, geo.LayerCodeAt(x, z, d, top, r), d, layers.RockName(r));
        }
        /// <summary>Rules v3's ore bodies: this deposit's ore blocks for <paramref name="rock"/> at a block of the generating
        /// chunk column, null for none. Without ore layers a host's own, else the most similar host's ("fill any rock"); with
        /// them (0060) only a host's own, and only where the layer there allows it.</summary>
        internal Block[] BodyBlocks(IServerChunk[] chunks, int cx, int cz, int lx, int y, int lz, int top, int rock)
        {
            if (layers == null) return hosts.TryGetValue(rock, out var own) ? own : anyRock.TryGetValue(rock, out var sub) ? sub.Blocks : null;
            if (!hosts.TryGetValue(rock, out var host)) return null;
            return OreLayers.Allows(layerRule, layers.CodeAt(chunks, cx, cz, lx, y, lz, rock), top - y, layers.RockName(rock)) ? host : null;
        }
        /// <summary>0060, prospecting: a host of this deposit at <paramref name="depth"/> below the top in Earth's column, in a
        /// layer it may use.</summary>
        internal bool CanPlace(int[] column, int top, int depth, int x, int z)
        {
            int y = top - depth;
            if (y <= 1 || y >= column.Length || !hosts.ContainsKey(column[y])) return false;
            var gate = LayerGate(x, z, top);
            return gate == null || gate(depth, column[y]);
        }

        // ---------------- generation ----------------
        public override void GenDeposit(IBlockAccessor accessor, IServerChunk[] chunks, int cx, int cz, BlockPos center, ref Dictionary<BlockPos, DepositVariant> children)
        {
            // (FP-12) the world's deposit pass (the prospecting pick's own GenDeposits keeps vanilla's generators): this thread
            // may wait for GLO-90 tiles, so rules v5's valley search never leaves a body out for a thread that can't
            using var fetch = FineElevation.Generating();
            try
            {
                if (kind == Kind.Child) GenerateChild(accessor, chunks, cx, cz, center);
                else GenerateDisc(accessor, chunks, cx, cz, center, children);
            }
            catch (Exception e)
            {
                // ER-08: a deposit that fails is refused as the terrain is (the chunk is made again at the next start, the
                // server stops), not skipped for good with one line a session (CR-O4)
                Interlocked.Increment(ref Session.GenErrors);
                EarthWorldGen.FailedPass("deposit " + variant.Code, chunks, cx, cz, e);
            }
        }
        void GenerateDisc(IBlockAccessor accessor, IServerChunk[] chunks, int cx, int cz, BlockPos center, Dictionary<BlockPos, DepositVariant> children)
        {
            var p = GetPlan(center);
            if (!p.Valid) return;
            // Cheap reject before any heat/terrain lookup.
            if (center.X + p.Rx < cx * 32 || center.X - p.Rx >= (cx + 1) * 32 || center.Z + p.Rz < cz * 32 || center.Z - p.Rz >= (cz + 1) * 32) return;
            if (!p.Stage2) Complete(p, center);
            if (!p.Valid) return;
            var map = chunks[0].MapChunk;
            var kids = variant.ChildDeposits;
            int kidCount = kids?.Length ?? 0;
            // Child chances depend only on this chunk: computed once per call, not per placed block.
            Span<double> kidChance = stackalloc double[Math.Min(kidCount, 16)];
            Span<int> kidSalt = stackalloc int[kidChance.Length];
            for (int i = 0; i < kidChance.Length; i++)
            {
                var v = kids[i];
                double factor = v.GeneratorInst is EarthOreGenerator kg && kg.Session == Session ? Session.AttemptFactor(kg.Binding, cx, cz) : v.WithOreMap ? v.GetOreMapFactor(cx, cz) : 1;
                kidChance[i] = v.TriesPerChunk / 1024.0 * factor;
                kidSalt[i] = RegionalOreCatalog.StableCode(v.Code);
            }
            int rx = p.Rx, rz = p.Rz;
            bool peat = Binding.Resource == "peat" && Session.BogPeatActive;
            // the chunk's layer table, fetched at the first block that asks (0060)
            EarthOreLayers.Table table = null;
            bool tableRead = false;
            for (int z = Math.Max(cz * 32, center.Z - rz); z < Math.Min((cz + 1) * 32, center.Z + rz + 1); z++)
            for (int x = Math.Max(cx * 32, center.X - rx); x < Math.Min((cx + 1) * 32, center.X + rx + 1); x++)
            {
                double distortion = p.Radius > 3 ? DistortNoiseGen.Noise(x / 3.0, z / 3.0) : 0;
                double edge = OreRules.DiscEdge(x - center.X, z - center.Z, rx, rz, p.Radius, distortion);
                if (edge <= 0) continue;
                int lx = x - cx * 32, lz = z - cz * 32, top = map.WorldGenTerrainHeightMap[lz * 32 + lx];
                if (Math.Abs(top - p.CentreTop) > native.MaxYRoughness) continue;
                double burial = Burial(p.Position, top);
                if (double.IsNaN(burial)) continue; // the deposit plane is above this column's ground
                int y = top - (int)Math.Round(burial) + p.Shift;
                int thick = (int)Math.Floor(p.Thickness * OreRules.Taper(edge) + RegionalOreCatalog.Random(Session.Seed, x, center.X, z, salt));
                for (int k = 0; k < thick; k++, y--)
                {
                    if (y <= 1 || y >= chunks.Length * 32 || y > top) continue;
                    if (top - y > maxDepth) continue;
                    if (RegionalOreCatalog.Random(Session.Seed, x, y, z, salt) >= profile.Concentration) continue;
                    int index = ((y % 32) * 32 + lz) * 32 + lx;
                    int host = chunks[y / 32].Data.GetBlockIdUnsafe(index);
                    bool isGraded;
                    if (hosts.TryGetValue(host, out var blocks)) isGraded = graded[host];
                    else if (p.Lens && anyRock.TryGetValue(host, out var similar)) { blocks = similar.Blocks; isGraded = similar.Graded; host = similar.Host; }
                    else continue;
                    if (layers != null)
                    {
                        if (!tableRead) { table = layers.TableAt(chunks, cx, cz); tableRead = true; }
                        if (!OreLayers.Allows(layerRule, layers.CodeAt(table, lx, y, lz, host), top - y, layers.RockName(host))) continue;
                    }
                    double q = isGraded ? p.Quantile : p.GradeRandom;
                    var block = blocks[RegionalOreCatalog.GradeIndex(q, blocks.Length)];
                    bool placed;
                    if (variant.WithBlockCallback || native.WithLastLayerBlockCallback && k == thick - 1)
                        placed = block.TryPlaceBlockForWorldGen(accessor, new BlockPos(x, y, z), BlockFacing.UP, Rand(Session.Seed ^ salt, x * 31 + y, z));
                    else { chunks[y / 32].Data.SetBlockUnsafe(index, block.Id); chunks[y / 32].Data.SetFluid(index, 0); placed = true; }
                    if (!placed) continue;
                    // Bog iron forms within/below the peat body; the grassed surface layer stays ordinary peat.
                    if (peat && y < top)
                    {
                        int bog = Session.BogPeat(x, y, z);
                        if (bog > 0) chunks[y / 32].Data.SetBlockUnsafe(index, bog);
                    }
                    Session.Totals.Record(Binding, block.Id);
                    for (int i = 0; i < kidChance.Length; i++)
                        if (kidChance[i] > 0 && RegionalOreCatalog.Random(Session.Seed, x, y, z, kidSalt[i]) < kidChance[i])
                            children[new BlockPos(x, y, z)] = kids[i];
                    if (p.Clues) PlaceIndicator(chunks, lx, lz, x, y, z, host);
                }
            }
        }

        void GenerateChild(IBlockAccessor accessor, IServerChunk[] chunks, int cx, int cz, BlockPos center)
        {
            var model = (ChildDepositGenerator)native;
            if (!RegionalOreCatalog.InColumn(center.X, center.Z, cx, cz)) return;
            var rnd = Rand(Session.Seed + salt, center.X * 31 + center.Y, center.Z);
            int radius = Math.Clamp((int)Math.Round(model.Radius.nextFloat(1, rnd) * profile.Radius), 1, 64);
            int tries = Math.Clamp((int)(model.RandomTries.nextFloat(1, rnd) * profile.Thickness), 0, 20000);
            double accept = rnd.NextDouble(), gradeRoll = rnd.NextDouble();
            var map = chunks[0].MapChunk;
            int top = map.WorldGenTerrainHeightMap[(center.Z - cz * 32) * 32 + center.X - cx * 32];
            var ctx = Session.Context(cx, cz);
            if (ctx.Outside) return;
            double chunkHeat = Session.ChunkHeat(ctx, Binding.Layer), heat = Session.HeatAtDepth(ctx, ownLayer, top - center.Y);
            // as Complete: rules v4 accept at the heat that raised the attempts
            double denominator = profile.Chance(Session.Evidence(Binding, chunkHeat));
            if (denominator <= 0 || accept > profile.Chance(Session.Evidence(Binding, heat)) / denominator) return;
            double q = RegionalOreCatalog.GradeQuantile(gradeRoll, heat, profile.QualityBias);
            EarthOreLayers.Table table = null;
            bool tableRead = false;
            for (int i = 0; i < tries; i++)
            {
                int x = center.X + rnd.NextInt(2 * radius + 1) - radius, z = center.Z + rnd.NextInt(2 * radius + 1) - radius, y = center.Y + rnd.NextInt(2 * radius + 1) - radius;
                if (!RegionalOreCatalog.InColumn(x, z, cx, cz) || y <= 1 || y >= chunks.Length * 32 || rnd.NextDouble() >= profile.Concentration) continue;
                int lx = x - cx * 32, lz = z - cz * 32;
                int depth = map.WorldGenTerrainHeightMap[lz * 32 + lx] - y;
                if (depth < profile.MinDepth || profile.MaxDepth > 0 && depth > profile.MaxDepth) continue;
                int index = ((y % 32) * 32 + lz) * 32 + lx;
                int host = chunks[y / 32].Data.GetBlockIdUnsafe(index);
                if (!hosts.TryGetValue(host, out var blocks)) continue;
                if (layers != null)
                {
                    if (!tableRead) { table = layers.TableAt(chunks, cx, cz); tableRead = true; }
                    if (!OreLayers.Allows(layerRule, layers.CodeAt(table, lx, y, lz, host), depth, layers.RockName(host))) continue;
                }
                var block = blocks[RegionalOreCatalog.GradeIndex(graded[host] ? q : rnd.NextDouble(), blocks.Length)];
                bool placed;
                if (variant.WithBlockCallback) placed = block.TryPlaceBlockForWorldGen(accessor, new BlockPos(x, y, z), BlockFacing.UP, rnd);
                else { chunks[y / 32].Data.SetBlockUnsafe(index, block.Id); chunks[y / 32].Data.SetFluid(index, 0); placed = true; }
                if (placed) { Session.Totals.Record(Binding, block.Id); PlaceIndicator(chunks, lx, lz, x, y, z, host); }
            }
        }

        void PlaceIndicator(IServerChunk[] chunks, int lx, int lz, int x, int oreY, int z, int host)
        {
            if (!indicators.TryGetValue(host, out var blocks)) return;
            int top = chunks[0].MapChunk.RainHeightMap[lz * 32 + lx];
            if (top < 1 || top + 1 >= chunks.Length * 32) return;
            // vanilla's depth scale (DiscDepositGenerator): 9 blocks at sea level 110, 13.5 in a 384-high world
            double chance = native.SurfaceBlockChance * Math.Max(0, 1.11 - (top - oreY) / (9.0 * TerraGenConfig.seaLevel / 110.0));
            if (RegionalOreCatalog.Random(Session.Seed, x, oreY, z, salt ^ 0x67891) >= chance) return;
            int below = chunks[top / 32].Data.GetBlockIdUnsafe(((top % 32) * 32 + lz) * 32 + lx);
            if (!Api.World.Blocks[below].SideSolid[BlockFacing.UP.Index]) return;
            int y = top + 1, index = ((y % 32) * 32 + lz) * 32 + lx;
            if (chunks[y / 32].Data.GetBlockIdUnsafe(index) == 0 && chunks[y / 32].Data.GetFluid(index) == 0)
                chunks[y / 32].Data.SetBlockUnsafe(index, blocks[0].Id);
        }
    }
}
