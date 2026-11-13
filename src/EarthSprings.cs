using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Springs rules 2, "streams" (the Earth tab's Mountain springs and streams; decision 0063 rule 5, Core.StreamSprings
    /// has the rules): a spring at the head of every perennial stream of HydroRIVERS (stream-heads-1.bin), and seeps where
    /// the water table (decision 0059) comes out of a slope. Vanilla's cave-wall springs stay as in "caves"
    /// (EarthWorldGen.SpringGuard), and vanilla makes no surface springs or mountain-side streams.
    /// Vegetation pass, after vanilla's springs (GenRivulets, 0.9), whose random draws this never touches. Each spring is
    /// one source block at the surface (vanilla's rivulet water, or its rapid water) with a scheduled update, so vanilla's
    /// liquid rules run it downhill once the chunks around it are done. Where a spring goes is decided from the terrain
    /// model and the data only, so it doesn't depend on which chunks were made first.
    /// </summary>
    public class EarthSprings : ModStdWorldGen
    {
        ICoreServerAPI sapi;
        IWorldGenBlockAccessor accessor;
        TerrainModel model;
        StreamHeads heads;
        WaterTable rules;
        EarthWaterTable table;
        volatile bool active;
        bool rapids;
        long seed;
        int waterId, rapidId, mapHeight;
        /// <summary>Per block id: ground a spring can rise from (soil, gravel, sand, rock), and what may be cleared above it
        /// (plants, snow).</summary>
        bool[] ground, clearable;
        readonly ConcurrentDictionary<int, StreamSprings.HeadPlan> plans = new();
        [ThreadStatic] static List<(StreamHeads.Head Head, double X, double Z)> found;
        [ThreadStatic] static List<StreamHeads.Head> scratch;
        long headsPlaced, headsRapid, headsShared, headsGround, headsStructure, headsHot, seepsPlaced, seepsClose, seepsGround, seepsStructure, seepTries;
        readonly long[] headsSkipped = new long[StreamSprings.SkipNames.Length];
        readonly long[] seepResults = new long[Enum.GetValues(typeof(StreamSprings.SeepResult)).Length];
        int errors, tableOff;

        public override double ExecuteOrder() => 0.91;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            if (!TerraGenConfig.DoDecorationPass) return;
            api.Event.InitWorldGenerator(Init, "standard");
            api.Event.GetWorldgenBlockAccessor(provider => accessor = provider.GetBlockAccessor(false));
            api.Event.ChunkColumnGeneration(OnVegetation, EnumWorldGenPass.Vegetation, "standard");
        }

        void Init()
        {
            active = false;
            var core = sapi.ModLoader.GetModSystem<EarthMapSystem>();
            var gen = EarthWorldGen.Instance;
            model = gen != null && gen.Active ? gen.Model : null;
            var p = core?.Params;
            if (model == null || p == null || p.SpringRules < EarthParams.SpringRulesStreams) return;
            LoadGlobalConfig(sapi);
            waterId = gcfg.rivuletWaterBlockId > 0 ? gcfg.rivuletWaterBlockId : gcfg.waterBlockId;
            rapidId = gcfg.rivuletRapidWaterBlockId > 0 ? gcfg.rivuletRapidWaterBlockId : waterId;
            rapids = !p.NoRiverRapids;
            seed = p.NoiseSeed;
            mapHeight = sapi.WorldManager.MapSizeY;
            var blocks = sapi.World.Blocks;
            ground = new bool[blocks.Count + 1];
            clearable = new bool[blocks.Count + 1];
            foreach (var b in blocks)
            {
                if (b == null || b.BlockId <= 0 || b.BlockId >= ground.Length || b.ForFluidsLayer) continue;
                var mat = b.BlockMaterial;
                ground[b.BlockId] = b.EntityClass == null && mat is EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or EnumBlockMaterial.Stone;
                clearable[b.BlockId] = b.EntityClass == null && mat is EnumBlockMaterial.Plant or EnumBlockMaterial.Snow;
            }
            heads = null;
            var sw = Stopwatch.StartNew();
            try
            {
                using var s = model.Data.OpenOptional(StreamHeads.FileName);
                if (s != null) heads = StreamHeads.Read(s);
                else Mod.Logger.Warning("Earth springs: {0} isn't installed; this session's chunks get no springs at stream heads (only seeps where the water table meets a slope).", StreamHeads.FileName);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                Mod.Logger.Error("Earth springs: {0} can't be read ({1}); this session's chunks get no springs at stream heads.", StreamHeads.FileName, e.Message);
            }
            rules = null;
            try
            {
                string json = sapi.Assets.TryGet(new AssetLocation(WaterTable.RulesAsset))?.ToText();
                if (json != null) rules = new WaterTable(json);
            }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Mod.Logger.Error("Earth springs: the water table's rules are invalid ({0}); no seeps.", e.Message);
            }
            table = sapi.ModLoader.GetModSystem<EarthWaterTable>();
            plans.Clear();
            active = true;
            Mod.Logger.Notification("Earth springs rules v{0} (streams): springs at the heads of {1} perennial streams (HydroRIVERS v1.0, read in {2} ms), " +
                "moved onto the valley floor within {3} blocks ({4:0} m; each metre away counts as {8} m of height), rapid water where the ground down-valley is steep for their flow{5}; seeps where the " +
                "water table meets a slope, up to {6} tries a chunk by the climate's water surplus{7}; vanilla's water springs in cave walls only.",
                p.SpringRules, heads?.Count ?? 0, sw.ElapsedMilliseconds, StreamSprings.SnapRadius(model.MetresPerBlock), StreamSprings.SnapMetres,
                rapids ? "" : " (none: this world has no rapids)", StreamSprings.SeepTriesFull, rules == null ? " (none: the water table's rules are missing)" : "",
                StreamSprings.SnapPenalty);
        }

        void OnVegetation(IChunkColumnGenerateRequest request)
        {
            if (!active) return;
            try
            {
                Generate(request);
            }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref errors) <= 3)
                    Mod.Logger.Error("Earth springs: chunk column {0},{1} failed and has no springs of its own. {2}", request.ChunkX, request.ChunkZ, e);
            }
        }

        void Generate(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            int cx = request.ChunkX, cz = request.ChunkZ, x0 = cx * 32, z0 = cz * 32;
            List<Cuboidi> suppress = null;
            sapi.World.BlockAccessor.WalkStructures(new BlockPos(x0, 0, z0, 0), new BlockPos(x0 + 32, mapHeight, z0 + 32, 0), s =>
            {
                if (s.SuppressRivulets) (suppress ??= new List<Cuboidi>()).Add(s.Location.Clone().GrowBy(1, 1, 1));
            });
            var used = new List<(int X, int Z)>();
            if (heads != null) Heads(chunks, x0, z0, suppress, used);
            if (rules != null) Seeps(chunks, cx, cz, suppress, used);
        }

        bool Suppressed(List<Cuboidi> suppress, int x, int y, int z)
        {
            if (suppress != null)
                foreach (var c in suppress)
                    if (c.Contains(x, y, z)) return true;
            return GetIntersectingStructure(x, z, RivuletsHashCode) != null;
        }

        static bool HotGround(int x, int z) => EarthVolcanoes.Instance?.InCrater(x + .5, z + .5) == true;

        StreamSprings.HeadPlan Plan(StreamHeads.Head h, double hx, double hz)
        {
            // planned in the ring's own lap, so the cache serves every lap of a wrapping world alike
            double wx = model.Geometry.WrapX(hx);
            if (plans.Count > 200_000) plans.Clear();
            var plan = plans.GetOrAdd(h.Id, _ => StreamSprings.PlanHead(model, h.Id, wx, hz, heads.Discharge(h.Class), rapids));
            int shift = (int)Math.Round(hx - wx);
            if (shift != 0) { plan.RawX += shift; plan.X += shift; }
            return plan;
        }

        void Heads(IServerChunk[] chunks, int x0, int z0, List<Cuboidi> suppress, List<(int X, int Z)> used)
        {
            int r = StreamSprings.SnapRadius(model.MetresPerBlock);
            var list = found ??= new List<(StreamHeads.Head, double, double)>();
            list.Clear();
            StreamSprings.HeadsNear(heads, model.Geometry, x0 - r - 1, z0 - r - 1, x0 + 33 + r, z0 + 33 + r, list, scratch ??= new List<StreamHeads.Head>());
            // in the file's order, so two heads that move to one column resolve the same way in every session
            list.Sort((a, b) => a.Head.Id.CompareTo(b.Head.Id));
            foreach (var (h, hx, hz) in list)
            {
                var plan = Plan(h, hx, hz);
                if (plan.Skip != StreamSprings.Skip.None)
                {
                    // counted once, by the chunk holding the head's own column
                    if (plan.RawX >= x0 && plan.RawX < x0 + 32 && plan.RawZ >= z0 && plan.RawZ < z0 + 32) Interlocked.Increment(ref headsSkipped[(int)plan.Skip]);
                    continue;
                }
                int lx = plan.X - x0, lz = plan.Z - z0;
                if (lx < 0 || lx >= 32 || lz < 0 || lz >= 32) continue;
                if (used.Exists(u => u.X == lx && u.Z == lz)) { Interlocked.Increment(ref headsShared); continue; }
                if (HotGround(plan.X, plan.Z)) { Interlocked.Increment(ref headsHot); continue; }
                if (Suppressed(suppress, plan.X, plan.Y, plan.Z)) { Interlocked.Increment(ref headsStructure); continue; }
                if (!Place(chunks, x0, z0, lx, lz, plan.Top, plan.Y, plan.Rapid ? rapidId : waterId)) { Interlocked.Increment(ref headsGround); continue; }
                used.Add((lx, lz));
                Interlocked.Increment(ref headsPlaced);
                if (plan.Rapid) Interlocked.Increment(ref headsRapid);
            }
        }

        bool TableAt(int x, int z, out int tableY, out bool openWater) => table.WaterTableAt(x, z, out tableY, out _, out openWater);

        void Seeps(IServerChunk[] chunks, int cx, int cz, List<Cuboidi> suppress, List<(int X, int Z)> used)
        {
            if (table == null || table.WaterTableQueryVersion < 1)
            {
                if (Interlocked.Increment(ref tableOff) == 1) Mod.Logger.Warning("Earth springs: the water table's query is off in this world; no seeps.");
                return;
            }
            int x0 = cx * 32, z0 = cz * 32;
            var g = model.Geometry.ToGeo(x0 + 16, z0 + 16);
            if (g.InGutter || g.BeyondPole) return;
            var climate = model.Data.SampleClimate(g.Lat, g.Lon);
            // the world's aridity index, as every other water-table reader has it (with AridityRules 1 Penman-Monteith's, not
            // the Holdridge index the rainfall was made from)
            double share = StreamSprings.SeepShare(rules, climate.Precip, climate.Aridity);
            ulong h = StreamSprings.ChunkHash(seed, cx, cz);
            int tries = StreamSprings.SeepTries(share, h);
            for (int i = 0; i < tries; i++)
            {
                StreamSprings.SeepColumn(h, i, out int lx, out int lz);
                Interlocked.Increment(ref seepTries);
                var result = StreamSprings.PlanSeep(model, x0 + lx, z0 + lz, TableAt, out int top, out int y, out _);
                Interlocked.Increment(ref seepResults[(int)result]);
                if (result != StreamSprings.SeepResult.Seep) continue;
                if (used.Exists(u => Math.Abs(u.X - lx) < StreamSprings.SeepSpacing && Math.Abs(u.Z - lz) < StreamSprings.SeepSpacing)) { Interlocked.Increment(ref seepsClose); continue; }
                if (HotGround(x0 + lx, z0 + lz) || Suppressed(suppress, x0 + lx, y, z0 + lz)) { Interlocked.Increment(ref seepsStructure); continue; }
                if (!Place(chunks, x0, z0, lx, lz, top, y, waterId)) { Interlocked.Increment(ref seepsGround); continue; }
                used.Add((lx, lz));
                Interlocked.Increment(ref seepsPlaced);
            }
        }

        /// <summary>
        /// One source block at (lx, y, lz): set into the ground (y at most top: the block must be ground) or standing on it
        /// (y = top + 1: the ground below must be ground, the block open or a plant). The column's ground as generated must be
        /// the model's (a cave mouth, crater or structure there: no spring). Plants and snow over an open source go.
        /// </summary>
        bool Place(IServerChunk[] chunks, int x0, int z0, int lx, int lz, int top, int y, int id)
        {
            var mc = chunks[0].MapChunk;
            int col = lz * 32 + lx;
            if (mc.WorldGenTerrainHeightMap[col] != top || y < 2 || y > top + 1 || y + 3 >= mapHeight) return false;
            int Block(int yy) => chunks[yy / 32].Data[((yy % 32) * 32 + lz) * 32 + lx];
            int Fluid(int yy) => chunks[yy / 32].Data.GetFluid(((yy % 32) * 32 + lz) * 32 + lx);
            int at = Block(y);
            if (Fluid(y) != 0 || Fluid(top) != 0) return false;
            bool fits = y <= top ? ground[at] : (at == 0 || clearable[at]) && ground[Block(top)];
            if (!fits) return false;
            if (y >= top)
            {
                for (int yy = y + 1; yy <= y + 2; yy++)
                {
                    int b = Block(yy);
                    if (b == 0 || !clearable[b]) break;
                    var d = chunks[yy / 32].Data;
                    d[((yy % 32) * 32 + lz) * 32 + lx] = 0;
                }
            }
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            data[i] = 0;
            data.SetFluid(i, id);
            if (y == top) mc.WorldGenTerrainHeightMap[col] = (ushort)(top - 1);
            if (mc.RainHeightMap[col] < y) mc.RainHeightMap[col] = (ushort)y;
            accessor?.ScheduleBlockUpdate(new BlockPos(x0 + lx, y, z0 + lz, 0));
            return true;
        }

        public string Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("stream heads: {0} springs ({1} rapid water)", Interlocked.Read(ref headsPlaced), Interlocked.Read(ref headsRapid));
            for (int k = 1; k < headsSkipped.Length; k++)
            {
                long n = Interlocked.Read(ref headsSkipped[k]);
                if (n > 0) sb.AppendFormat(", {0} skipped: {1}", n, StreamSprings.SkipNames[k]);
            }
            sb.AppendFormat(", {0} on a column another head took, {1} in a crater, {2} in a structure, {3} where the ground differs from the model",
                Interlocked.Read(ref headsShared), Interlocked.Read(ref headsHot), Interlocked.Read(ref headsStructure), Interlocked.Read(ref headsGround));
            sb.AppendFormat("; seeps: {0} of {1} tries", Interlocked.Read(ref seepsPlaced), Interlocked.Read(ref seepTries));
            var names = Enum.GetNames(typeof(StreamSprings.SeepResult));
            for (int k = 1; k < seepResults.Length; k++)
            {
                long n = Interlocked.Read(ref seepResults[k]);
                if (n > 0) sb.AppendFormat(", {0} {1}", n, names[k]);
            }
            sb.AppendFormat(", {0} too close to another spring, {1} in a structure or crater, {2} where the ground differs", Interlocked.Read(ref seepsClose),
                Interlocked.Read(ref seepsStructure), Interlocked.Read(ref seepsGround));
            if (errors > 0) sb.AppendFormat("; {0} chunk columns failed", errors);
            return sb.ToString();
        }

        public override void Dispose()
        {
            if (active) Mod?.Logger.Notification("Earth springs this session: {0}.", Report());
            active = false;
            base.Dispose();
        }
    }
}
