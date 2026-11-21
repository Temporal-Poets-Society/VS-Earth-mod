using System;
using System.Runtime.CompilerServices;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// Decision 0099 (EA-0181), vanilla strata rules 1: in worlds with vanilla rocks (<see cref="EarthParams.VanillaStrataRules"/>),
    /// each column gets the rock layers vanilla's standard world (256 high at sea level 110; for another sea level, the vanilla
    /// world with that sea level) gives a column standing at sea level plus the column's own relief, laid under the local
    /// ground.
    ///
    /// Vanilla fills a column with its default rock (granite), lays sedimentary and some other layers from the top down, the
    /// igneous and some metamorphic ones from y 1 up, scales every layer's thickness with world height, and thins each
    /// sedimentary layer by half the column's height above sea level so that its mountains show their hard core. On Earth's
    /// terrain that left granite under the plateaus (sediments thinned away by their 150-400 blocks above sea level at 1:10,
    /// the bottom-up layers 250-480 blocks down) and, in tall worlds, one layer filling the reach (four times as thick at 1024
    /// high: Vienna 100 % sandstone in the top 120 blocks at 1:10). So iron and the other ores vanilla binds to a few rocks were
    /// missing where their depth was right (decision 0097).
    ///
    /// Here vanilla's own code builds each column on a scratch column whose top stands at sea level plus the column's relief
    /// (its height above the regional surface, Earth's smoothed terrain over about 0.6 of a half-degree cell, as regional
    /// geology uses), with the standard world's layer thicknesses (the region's layer maps and the provinces' maximum
    /// thicknesses scaled by standard height / world height); that column is then laid with its top at the real ground. So
    /// a plateau keeps its sediments and has the bottom-up layers about a sea level's depth down, as a lowland does; a ridge
    /// that stands above its surroundings is thinned as vanilla's mountains are; and a tall world's layers are as thick as the
    /// standard world's. Below the scratch column's bottom the default rock stays. Only blocks that are still the default rock
    /// are replaced, as vanilla replaces them. The prospecting pick reads a column built the same way.
    /// </summary>
    public sealed class EarthVanillaStrata : ModSystem
    {
        internal const string HarmonyId = "earth.vanillastrata";
        static EarthVanillaStrata instance;
        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        PatchVanilla patches;
        int rules, sea, mapHeight;
        double scale;
        GeologyReference reference;
        GenRockStrataNew pickStrata;
        readonly object pickLock = new object();
        internal static AccessTools.FieldRef<GenRockStrataNew, ushort[]> HeightMapRef;
        internal static bool Active => instance?.active == true;
        volatile bool active;

        public override double ExecuteOrder() => .42;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            instance = this;
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, InitWorld);
            // vanilla builds its province table when its world generators start, after WorldReady (and again on a reload)
            api.Event.InitWorldGenerator(() => { if (active) ScaleProvinces(); }, "standard");
        }

        void InitWorld()
        {
            active = false;
            var p = core?.Params;
            if (p?.Enabled != true || p.VanillaStrataRules < 1 || core.Model == null) return;
            rules = p.VanillaStrataRules;
            sea = TerraGenConfig.seaLevel;
            mapHeight = api.WorldManager.MapSizeY;
            scale = VanillaStrataMath.StandardHeight(sea) / mapHeight;
            try
            {
                var geometry = core.Model.Geometry;
                // the regional surface as regional geology's rules v2 make it, without its undulation and thickness variation
                reference = new GeologyReference(core.Model, p.NoiseSeed, geometry.BlocksPerDegreeZ * .5, VanillaStrataMath.RadiusCells, .6, 0, 0);
                harmony ??= new Harmony(HarmonyId);
                patches ??= PatchVanilla.For(api, Mod, harmony);
                patches.Apply(PatchVanilla.VanillaStrataTable(api.Event.GetRegisteredWorldGenHandlers("standard"), InPlaceOfVanilla));
                active = patches.On(PatchVanilla.Feature.VanillaStrata);
                if (active) Mod.Logger.Notification("Earth vanilla rock layers (rules {0}): the standard world's layers under the local ground (thickness x {1:0.###}, sea level {2}).", rules, scale, sea);
            }
            catch (Exception e)
            {
                active = false;
                Mod.Logger.Error("Earth vanilla rock layers are off this session after an unexpected error; vanilla's own layers are used. {0}", e);
            }
        }

        /// <summary>(Decision 0083) Off where this world's saved rules use it.</summary>
        internal void FeaturesOff(EarthParams p, System.Collections.Generic.List<string> off)
        {
            if (p.VanillaStrataRules < 1) return;
            if (!active) off.Add("vanilla's rock layers under the local ground (vanilla strata rules " + p.VanillaStrataRules + "; its line is above)");
            else if (patches?.StillInPlace(PatchVanilla.Feature.VanillaStrata) is string why) off.Add("vanilla's rock layers under the local ground (" + why + ")");
        }

        // ------------------------------------------------------------------ the thicknesses

        static readonly ConditionalWeakTable<GeologicProvinces, StrongBox<double>> scaledProvinces = new();

        /// <summary>The provinces' maximum layer thicknesses (vanilla: times world height / 256) at the standard world's, once
        /// per province table (vanilla makes a new one when its world generators start or reload).</summary>
        void ScaleProvinces()
        {
            var provinces = NoiseGeoProvince.provinces;
            if (provinces?.Variants == null || scaledProvinces.TryGetValue(provinces, out _)) return;
            lock (scaledProvinces)
            {
                if (scaledProvinces.TryGetValue(provinces, out _)) return;
                foreach (var v in provinces.Variants)
                    foreach (var g in v?.RockStrataIndexed ?? Array.Empty<GeologicProvinceRockStrata>())
                        if (g != null) g.ScaledMaxThickness = (float)(g.ScaledMaxThickness * scale);
                scaledProvinces.Add(provinces, new StrongBox<double>(scale));
            }
        }

        void RestoreProvinces()
        {
            var provinces = NoiseGeoProvince.provinces;
            if (provinces?.Variants == null || !scaledProvinces.TryGetValue(provinces, out var box) || box.Value == 0) return;
            foreach (var v in provinces.Variants)
                foreach (var g in v?.RockStrataIndexed ?? Array.Empty<GeologicProvinceRockStrata>())
                    if (g != null) g.ScaledMaxThickness = (float)(g.ScaledMaxThickness / box.Value);
            scaledProvinces.Remove(provinces);
        }

        /// <summary>A new map region's layer thickness maps (vanilla: noise times world height) at the standard world's.</summary>
        internal static void RegionPostfix(IMapRegion mapRegion)
        {
            var self = instance;
            if (self?.active != true || mapRegion?.RockStrata == null) return;
            foreach (var m in mapRegion.RockStrata)
            {
                if (m?.Data == null) continue;
                for (int i = 0; i < m.Data.Length; i++) m.Data[i] = (int)Math.Round(m.Data[i] * self.scale);
            }
        }

        // ------------------------------------------------------------------ the columns

        /// <summary>In place of vanilla's rock layer pass (PatchVanilla.VanillaStrataTable).</summary>
        ChunkColumnGenerationDelegate InPlaceOfVanilla(ChunkColumnGenerationDelegate vanilla) => request =>
        {
            if (!active || vanilla.Target is not GenRockStrataNew gen) { vanilla(request); return; }
            EarthWorldGen.ThrowIfRefusedChunk(request.Chunks, request.ChunkX, request.ChunkZ);
            try { Lay(gen, request.Chunks, request.ChunkX, request.ChunkZ); }
            catch (Exception e) { EarthWorldGen.FailedPass("vanilla rock layers", request.Chunks, request.ChunkX, request.ChunkZ, e); }
        };

        sealed class Scratch
        {
            public ProPickWorkSpace.DummyChunk[] Chunks = Array.Empty<ProPickWorkSpace.DummyChunk>();
            public readonly ushort[] Tops = new ushort[1024];
            public readonly int[] Offsets = new int[1024];
            int[][] blocks = Array.Empty<int[]>();

            /// <summary>The block arrays of the scratch chunks <paramref name="cs"/> (this scratch's), by chunk y.</summary>
            public int[][] BlocksOf(ProPickWorkSpace.DummyChunk[] cs)
            {
                if (blocks.Length != cs.Length) blocks = new int[cs.Length][];
                for (int i = 0; i < cs.Length; i++) blocks[i] = Blocks(cs[i]);
                return blocks;
            }

            /// <summary>The block data of a chunk column's chunks, by chunk y (a new array: nothing of the column is kept).</summary>
            public static IChunkBlocks[] DataOf(IServerChunk[] chunks)
            {
                var data = new IChunkBlocks[chunks.Length];
                for (int i = 0; i < chunks.Length; i++) data[i] = chunks[i].Data;
                return data;
            }
        }
        [ThreadStatic] static Scratch scratch;

        static ProPickWorkSpace.DummyChunk[] ScratchChunks(Scratch s, int count, IMapChunk mapChunk)
        {
            if (s.Chunks.Length < count)
            {
                var grown = new ProPickWorkSpace.DummyChunk[count];
                Array.Copy(s.Chunks, grown, s.Chunks.Length);
                for (int i = s.Chunks.Length; i < count; i++) grown[i] = new ProPickWorkSpace.DummyChunk(32) { chunkY = i };
                s.Chunks = grown;
            }
            for (int i = 0; i < s.Chunks.Length; i++) s.Chunks[i].MapChunk = mapChunk;
            return s.Chunks;
        }

        static int[] Blocks(ProPickWorkSpace.DummyChunk c) => ((ProPickWorkSpace.DummyChunk.DummyChunkData)c.Blocks).blocks;

        /// <summary>The scratch column's top and its offset (real y = scratch y + offset) for a column whose ground is
        /// <paramref name="top"/> at <paramref name="x"/>, <paramref name="z"/>.</summary>
        void ScratchTop(int x, int z, int top, out int scratchTop, out int offset)
        {
            reference.At(x, z, out double datum, out _);
            scratchTop = VanillaStrataMath.ScratchTop(top, datum, sea, mapHeight);
            offset = top - scratchTop;
        }

        void Lay(GenRockStrataNew gen, IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            ScaleProvinces();
            var map = chunks[0].MapChunk;
            var heights = map.WorldGenTerrainHeightMap;
            int rock = gen.rockBlockId, x0 = chunkX * 32, z0 = chunkZ * 32, highest = 2;
            var s = scratch ??= new Scratch();
            for (int lz = 0; lz < 32; lz++)
                for (int lx = 0; lx < 32; lx++)
                {
                    int k = lz * 32 + lx;
                    ScratchTop(x0 + lx, z0 + lz, Math.Min(heights[k], mapHeight - 1), out int st, out int off);
                    s.Tops[k] = (ushort)st; s.Offsets[k] = off;
                    if (st > highest) highest = st;
                }
            var cs = ScratchChunks(s, highest / 32 + 1, map);
            // (EA-0193) the scratch's and the chunks' block arrays once per chunk, not per block
            var scratchBlocks = s.BlocksOf(cs);
            var data = Scratch.DataOf(chunks);
            try
            {
                // vanilla's column of default rock, from y 1 to the scratch top, as its own pass finds it (layer by layer, in
                // the arrays' own order: EA-0193)
                FillScratch(scratchBlocks, s.Tops, highest, rock);
                gen.preLoad(chunks, chunkX, chunkZ);
                ref ushort[] heightMap = ref HeightMapRef(gen);
                var real = heightMap;
                heightMap = s.Tops;
                try
                {
                    for (int lz = 0; lz < 32; lz++)
                        for (int lx = 0; lx < 32; lx++) gen.genBlockColumn(cs, chunkX, chunkZ, lx, lz);
                }
                finally { heightMap = real; }
                // laid under the real ground; only blocks still the default rock change, as vanilla changes them (the scratch
                // y, y - off, runs from 1 to the scratch top)
                for (int k = 0; k < 1024; k++)
                {
                    int top = Math.Min(heights[k], mapHeight - 1), st = s.Tops[k], off = s.Offsets[k];
                    int last = Math.Min(top, st + off);
                    for (int y = Math.Max(1, 1 + off); y <= last; y++)
                    {
                        int sy = y - off;
                        int b = scratchBlocks[sy >> 5][((sy & 31) << 10) | k];
                        if (b == rock || b == 0) continue;
                        var blocks = data[y >> 5];
                        int i = ((y & 31) << 10) | k;
                        if (blocks.GetBlockIdUnsafe(i) == rock) blocks.SetBlockUnsafe(i, b);
                    }
                }
            }
            finally
            {
                // the scratch is this thread's for the next chunk
                FillScratch(scratchBlocks, s.Tops, highest, 0);
            }
        }

        /// <summary>Sets each column k of the scratch to <paramref name="value"/> from y 1 to <paramref name="tops"/>[k], layer by
        /// layer (y outer: the arrays' own order; column by column every write was 4 kB from the last).</summary>
        static void FillScratch(int[][] scratchBlocks, ushort[] tops, int highest, int value)
        {
            for (int y = 1; y <= highest; y++)
            {
                var layer = scratchBlocks[y >> 5];
                int row = (y & 31) << 10;
                for (int k = 0; k < 1024; k++)
                    if (y <= tops[k]) layer[row | k] = value;
            }
        }

        /// <summary>
        /// The column the prospecting pick reads at <paramref name="pos"/> (RockFloorPass.RockFloorReading, before the floor is
        /// taken out): rock per y below the ground, built as the world pass builds it, with a strata generator of its own (the
        /// world's runs on the generation thread). Null where the rules are off, so the pick keeps vanilla's simulated column.
        /// </summary>
        internal static int[] PickColumn(BlockPos pos, int length)
        {
            var self = instance;
            if (self?.active != true || pos == null) return null;
            try { return self.Column(pos.X, pos.Z, length); }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref self.pickErrors) == 1) self.Mod.Logger.Warning("Earth vanilla rock layers: a prospecting column failed (said once a session); the pick reads vanilla's simulated layers. {0}", e.Message);
                return null;
            }
        }
        int pickErrors;
        // the pick asks once per deposit code at the same spot: the last column, by position and length
        int lastX = int.MinValue, lastZ, lastLength;
        int[] lastColumn;

        int[] Column(int x, int z, int length)
        {
            lock (pickLock)
            {
                if (pickStrata == null)
                {
                    var g = new GenRockStrataNew();
                    g.setApi(api);
                    g.initWorldGen();
                    pickStrata = g;
                }
                if (lastColumn != null && lastX == x && lastZ == z && lastLength == length) return (int[])lastColumn.Clone();
                ScaleProvinces();
                int cx = x / 32, cz = z / 32, lx = x - cx * 32, lz = z - cz * 32, k = lz * 32 + lx;
                var map = api.WorldManager.GetMapChunk(cx, cz);
                if (map == null) return null;
                int top = Math.Min(map.WorldGenTerrainHeightMap[k], mapHeight - 1);
                ScratchTop(x, z, top, out int st, out int off);
                var s = scratch ??= new Scratch();
                var cs = ScratchChunks(s, st / 32 + 1, map);
                int rock = pickStrata.rockBlockId;
                try
                {
                    for (int y = 1; y <= st; y++) Blocks(cs[y >> 5])[((y & 31) << 10) | k] = rock;
                    pickStrata.preLoad(cs, cx, cz);
                    ref ushort[] heightMap = ref HeightMapRef(pickStrata);
                    var real = heightMap;
                    Array.Copy(real, s.Tops, 1024);
                    s.Tops[k] = (ushort)st;
                    heightMap = s.Tops;
                    try { pickStrata.genBlockColumn(cs, cx, cz, lx, lz); }
                    finally { heightMap = real; }
                    var column = new int[Math.Max(length, top + 1)];
                    for (int y = 1; y <= top && y < column.Length; y++)
                    {
                        int sy = y - off;
                        column[y] = sy >= 1 && sy <= st ? Blocks(cs[sy >> 5])[((sy & 31) << 10) | k] : rock;
                    }
                    lastX = x; lastZ = z; lastLength = length; lastColumn = (int[])column.Clone();
                    return column;
                }
                finally
                {
                    for (int y = 1; y <= st; y++) Blocks(cs[y >> 5])[((y & 31) << 10) | k] = 0;
                }
            }
        }

        public override void Dispose()
        {
            if (active) { try { RestoreProvinces(); } catch (Exception) { /* the next world rebuilds the table */ } }
            active = false;
            patches?.Release();
            harmony?.UnpatchAll(HarmonyId);
            if (instance == this) instance = null;
            base.Dispose();
        }
    }

    /// <summary>The rules' arithmetic (decision 0099), apart for the tests.</summary>
    internal static class VanillaStrataMath
    {
        /// <summary>The regional surface's radius in half-degree cells (regional geology rules v2's datum).</summary>
        internal const double RadiusCells = .6;

        /// <summary>The height of the vanilla world with this sea level (256 at 110), as decision 0097.</summary>
        internal static double StandardHeight(int sea) => DepositDepth.StandardHeight(sea);

        /// <summary>The scratch column's top: the sea level plus the column's relief (ground <paramref name="top"/> above the
        /// regional surface <paramref name="datum"/>), within the world.</summary>
        internal static int ScratchTop(int top, double datum, int sea, int mapHeight)
            => Math.Clamp(sea + (int)Math.Round(top - datum), 2, mapHeight - 2);
    }
}
