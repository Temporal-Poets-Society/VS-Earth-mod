using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Earth's changes to vanilla's block layers (GenBlockLayers, the terrain pass after Earth's terrain): no arid basin fill
    /// below sea level, soil fertility by the real aridity index (decision 0048), and the share of vanilla's tall grass dry
    /// land keeps (ranges step 5a). Bound by PatchVanilla; each does nothing while Earth is inactive.
    /// </summary>
    static class BlockLayerAdjustments
    {
        /// <summary>Whether the arid basin fill, the soil fertility and the tall grass changes are in place this session.</summary>
        internal static bool AridFillPatched, SoilFertilityPatched, TallGrassPatched;

        /// <summary>
        /// Vanilla's block layer pass fills terrain below sea level in dry climates with up to 20 blocks of rock
        /// (min(max(0, (0.5 - rain) * 40), sea - height)), which would fill the Dead Sea, the Caspian and arid sea shelves.
        /// Replace the 40 with a call that returns 0 in Earth worlds.
        /// </summary>
        internal static IEnumerable<CodeInstruction> AridFillTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 3; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4 && list[i].operand is float f && f == 40f
                    && list[i - 3].opcode == OpCodes.Ldc_R4 && list[i - 3].operand is float h && h == 0.5f)
                {
                    var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockLayerAdjustments), nameof(AridFillFactor)));
                    call.labels.AddRange(list[i].labels);
                    list[i] = call;
                    AridFillPatched = true;
                    break;
                }
            }
            return list;
        }

        public static float AridFillFactor() => WorldGenerationContext.Current != null ? 0f : 40f;

        /// <summary>
        /// Decision 0048: vanilla's block layers take each column's fertility from its rain and temperature
        /// (Climate.GetFertilityFromUnscaledTemp), which on Earth's real rainfall lays sand or gravel over semi-arid land.
        /// Each call in LoadBlockLayers gets the layer's temperature and the column's position and goes through
        /// <see cref="SoilFertilityAt"/> instead, so vanilla still picks the soil grade, grass, subsoil and tall grass.
        /// </summary>
        internal static IEnumerable<CodeInstruction> SoilFertilityTranspiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase original)
        {
            var list = instructions.ToList();
            var fertility = AccessTools.Method(typeof(Climate), nameof(Climate.GetFertilityFromUnscaledTemp));
            var parameters = original.GetParameters();
            int temperature = Array.FindIndex(parameters, p => p.Name == "temperature" && p.ParameterType == typeof(float));
            int pos = Array.FindIndex(parameters, p => p.ParameterType == typeof(BlockPos));
            if (fertility == null || original.IsStatic || temperature < 0 || pos < 0) return list;
            var replacement = AccessTools.Method(typeof(BlockLayerAdjustments), nameof(SoilFertilityAt));
            var result = new List<CodeInstruction>(list.Count + 8);
            int replaced = 0;
            foreach (var instruction in list)
            {
                if (instruction.Calls(fertility))
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_S, (byte)(temperature + 1));
                    first.labels.AddRange(instruction.labels);
                    result.Add(first);
                    result.Add(new CodeInstruction(OpCodes.Ldarg_S, (byte)(pos + 1)));
                    result.Add(new CodeInstruction(OpCodes.Call, replacement));
                    replaced++;
                    continue;
                }
                result.Add(instruction);
            }
            if (replaced == 0) return list;
            SoilFertilityPatched = true;
            return result;
        }

        public static int SoilFertilityAt(int rain, int unscaledTemp, float posYRel, float temperature, BlockPos pos)
        {
            int vanilla = Climate.GetFertilityFromUnscaledTemp(rain, unscaledTemp, posYRel);
            var w = WorldGenerationContext.Current;
            if (w == null || w.Rules.SoilRules < 1 || pos == null) return vanilla;
            if (!soilKnown || soilModel != w.Model.CacheId || soilX != pos.X || soilZ != pos.Z)
            {
                var g = w.Model.TerrainGeoAt(pos.X, pos.Z);
                soilX = pos.X; soilZ = pos.Z; soilKnown = true; soilModel = w.Model.CacheId;
                if (g.InGutter || g.BeyondPole) soilRain = soilDry = double.NaN;
                else
                {
                    var climate = w.Model.Data.SampleClimate(g.Lat, g.Lon);
                    soilRain = climate.Rain;
                    soilDry = climate.DrySummer;
                    soilPenman = climate.PenmanFactor;
                }
            }
            return double.IsNaN(soilRain) ? vanilla : SoilFertility.Apply(vanilla, soilRain, soilDry, temperature, soilPenman);
        }
        /// <summary>
        /// The soil rule's last climate sample on this thread: vanilla's block layers ask for a column's fertility several
        /// times, and the tall grass share asks right after for the same column. Owned by the world's model (Codex item 5:
        /// keyed by TerrainModel.CacheId as well as the position; a number, so a thread never keeps an unloaded world alive), so
        /// a thread that sampled another world (a singleplayer relaunch, tests) never answers from that world's sample.
        /// </summary>
        [ThreadStatic] static int soilX, soilZ;
        [ThreadStatic] static long soilModel;
        [ThreadStatic] static bool soilKnown;
        [ThreadStatic] static double soilRain, soilDry, soilPenman;

        // ---- ranges step 5a: dry land keeps a share of vanilla's tall grass (Core.GroundCover, ground-cover.json)
        [ThreadStatic] static int layersChunkX, layersChunkZ;
        [ThreadStatic] static bool layersChunkKnown;

        /// <summary>The chunk the tall grass prefix works in on this thread (for the tests).</summary>
        internal static (int X, int Z, bool Known) LayersChunk => (layersChunkX, layersChunkZ, layersChunkKnown);

        static void EnterChunk(IChunkColumnGenerateRequest request)
        {
            layersChunkX = request.ChunkX;
            layersChunkZ = request.ChunkZ;
            layersChunkKnown = true;
        }

        /// <summary>
        /// Vanilla's block layer handler (in the terrain pass's public handler list, replaced by this wrapper through PatchVanilla)
        /// run inside its chunk's scope: the tall grass prefix gets local x and z only, so it reads the chunk from here; the scope
        /// before is put back afterwards, also when vanilla throws (the values are this thread's).
        /// </summary>
        internal static ChunkColumnGenerationDelegate InChunkScope(ChunkColumnGenerationDelegate blockLayers) => request =>
        {
            var before = (layersChunkX, layersChunkZ, layersChunkKnown);
            EnterChunk(request);
            try { blockLayers(request); }
            finally { (layersChunkX, layersChunkZ, layersChunkKnown) = before; }
        };

        /// <summary>The same scope as <see cref="InChunkScope"/>, as a Harmony prefix (with <see cref="GrassChunkFinalizer"/>) on
        /// vanilla's block layer method, where another mod has wrapped its handler before Earth could (PatchVanilla's second way).</summary>
        internal static void BlockLayersScopePrefix([HarmonyArgument(0)] IChunkColumnGenerateRequest request, out (int X, int Z, bool Known) __state)
        {
            __state = (layersChunkX, layersChunkZ, layersChunkKnown);
            EnterChunk(request);
        }

        /// <summary>Vanilla's GenerateGrass (around story structures and dungeon entrances) lays tall grass for its own chunk: the
        /// tall grass prefix reads that chunk while it runs, and the one before it again after (the values are this thread's).</summary>
        internal static void GrassChunkPrefix(IChunkColumnGenerateRequest request, out (int X, int Z, bool Known) __state)
        {
            __state = (layersChunkX, layersChunkZ, layersChunkKnown);
            EnterChunk(request);
        }

        internal static void GrassChunkFinalizer((int X, int Z, bool Known) __state)
        {
            layersChunkX = __state.X;
            layersChunkZ = __state.Z;
            layersChunkKnown = __state.Known;
        }

        /// <summary>
        /// Ranges step 5a: vanilla puts tall grass on almost all treeless soil; dry land keeps only a share of it by the real
        /// aridity index, the value soil rule 0048 reads. The column's rain comes from the soil rule's cache when it has just
        /// read this column, otherwise from the climate. New worlds only (EarthParams.GroundCoverRules).
        /// </summary>
        internal static bool TallGrassCoverPrefix(int x, int z)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || !layersChunkKnown) return true;
            int wx = layersChunkX * 32 + x, wz = layersChunkZ * 32 + z;
            // decision 0070: no tall grass on the shore strip (vanilla lets sand keep a tenth of it)
            if (OnShoreStrip(w, wx, wz)) return false;
            if (w.Rules.GroundCoverRules < 1 || w.Rules.GroundCover == null) return true;
            double share = TallGrassShareAt(w, wx, wz);
            return double.IsNaN(share) || GroundCover.Keeps(wx, wz, share);
        }

        /// <summary>Decision 0070: a column of the sea's or a big lake's shore strip (a beach with the coast's class), where
        /// neither tall grass nor land plants grow.</summary>
        internal static bool OnShoreStrip(WorldGenerationContext w, int wx, int wz)
        {
            if (w?.Model == null || w.Model.ShoreRules < 1) return false;
            var c = ColumnStore.At(w.Model, wx, wz);
            return c.Beach && c.Shore != ShoreMaterial.None && c.Water == TerrainModel.WaterNone;
        }

        /// <summary>The share (0..1) of vanilla's tall grass kept at a block column; NaN where the rule doesn't apply (the
        /// seam gutter, beyond the poles, or a world without it).</summary>
        internal static double TallGrassShareAt(WorldGenerationContext w, int wx, int wz)
        {
            var groundCover = w?.Rules.GroundCover;
            if (groundCover == null || w.Rules.GroundCoverRules < 1) return double.NaN;
            double rain, dry, penmanFactor;
            if (soilKnown && soilModel == w.Model.CacheId && soilX == wx && soilZ == wz) { rain = soilRain; dry = soilDry; penmanFactor = soilPenman; }
            else
            {
                var g = w.Model.TerrainGeoAt(wx, wz);
                if (g.InGutter || g.BeyondPole) return double.NaN;
                var climate = w.Model.Data.SampleClimate(g.Lat, g.Lon);
                rain = climate.Rain;
                dry = climate.DrySummer;
                penmanFactor = climate.PenmanFactor;
            }
            return double.IsNaN(rain) ? double.NaN : groundCover.TallGrassKept(rain, dry, penmanFactor);
        }
    }
}
