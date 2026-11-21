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
            // (FP-11, Fable's low 5 on ad56a74) a generation entry point inside vanilla's pass: this thread may wait for a missing tile
            using var fetch = Core.FineElevation.Generating();
            int vanilla = Climate.GetFertilityFromUnscaledTemp(rain, unscaledTemp, posYRel);
            var w = WorldGenerationContext.Current;
            if (w == null || w.Rules.SoilRules < 1 || pos == null) return vanilla;
            SampleColumn(w, pos.X, pos.Z, temperature);
            if (double.IsNaN(soilRain)) return vanilla;
            int rules3 = SoilFertility.Apply(vanilla, soilRain, soilDry, temperature, soilPenman);
            // (decision 0105) Earth soil: the column's tier, as the layers' fertility; Vanilla where the rule says nothing
            var topsoil = w.Rules.Topsoil;
            return topsoil == null ? rules3 : topsoil.LayerFertility(soilTier, rules3);
        }

        /// <summary>
        /// The column's climate (and, with Earth soil, its topsoil tier) into this thread's cache, once per column: vanilla's
        /// layers ask for a column's fertility several times, the flood rain asks first, the tall grass share right after.
        /// <paramref name="temperature"/>: the layers' temperature at the column's surface (the first ask is at the surface).
        /// </summary>
        static void SampleColumn(WorldGenerationContext w, int x, int z, double temperature)
        {
            if (soilKnown && soilModel == w.Model.CacheId && soilX == x && soilZ == z) return;
            var g = w.Model.TerrainGeoAt(x, z);
            soilX = x; soilZ = z; soilKnown = true; soilModel = w.Model.CacheId;
            soilTier = Topsoil.Tier.Vanilla; soilArid = false;
            if (g.InGutter || g.BeyondPole) { soilRain = soilDry = double.NaN; return; }
            var climate = w.Model.Data.SampleClimate(g.Lat, g.Lon);
            soilRain = climate.Rain;
            soilDry = climate.DrySummer;
            soilPenman = climate.PenmanFactor;
            var topsoil = w.Rules.Topsoil;
            if (topsoil == null || double.IsNaN(soilRain)) return;
            soilTier = TopsoilTier(w, topsoil, x, z, g.Lat, g.Lon, climate, temperature, out soilArid);
            RecordTier(x, z, soilTier);
        }

        /// <summary>(Decision 0105) A column's Earth soil (Core.Topsoil.Decide) from what the world knows about the place: the
        /// shore strip keeps soil rules 3 (its beach is the shore pass's); arid by the ambient rain and the river's green belt
        /// as ground columns' soil rules 2; in a river's flood by the floodplain grid at the column's real height; fresh
        /// wetland in the 0.1° cell for oases; GUM's wind-blown sand; the warmest month for polar and alpine ground.</summary>
        internal static Topsoil.Tier TopsoilTier(WorldGenerationContext w, Topsoil topsoil, int x, int z, double lat, double lon,
            in EarthData.ClimateSample climate, double temperature, out bool arid)
        {
            arid = false;
            if (OnShoreStrip(w, x, z)) return Topsoil.Tier.Vanilla;
            // the warmest month at the column's height: the climate's, lapsed as vanilla's layers lapse the yearly mean
            var place = new Topsoil.Place { Class = topsoil.ClassAt(lat, lon), Temperature = temperature,
                WarmestMonth = double.IsNaN(climate.TWarm) || double.IsNaN(climate.TMean) ? double.NaN : climate.TWarm + (temperature - climate.TMean) };
            if (place.Class == null || temperature < SoilFertility.MinTemperature) return Topsoil.Tier.Vanilla;
            arid = SoilFertility.AridGround(climate.Rain, climate.DrySummer, climate.PenmanFactor)
                && SoilFertility.AridGround(w.Model.RiparianRain(x, z, climate.Rain), climate.DrySummer, climate.PenmanFactor);
            place.Arid = arid;
            var floods = w.Model.Floodplains;
            if (floods != null && !double.IsNaN(floods.Level(lat, lon)))
                place.Flood = floods.Floods(lat, lon, ColumnStore.At(w.Model, x, z).Elev);
            place.Dune = topsoil.DuneAt(lat, lon);
            return topsoil.Decide(w.Rules.TopsoilTiers, place);
        }

        /// <summary>
        /// The soil rule's last climate sample on this thread: vanilla's block layers ask for a column's fertility several
        /// times, and the tall grass share asks right after for the same column. Owned by the world's model (Codex item 5:
        /// keyed by TerrainModel.CacheId as well as the position; a number, so a thread never keeps an unloaded world alive), so
        /// a thread that sampled another world (a singleplayer relaunch, tests) never answers from that world's sample.
        /// With Earth soil it also holds the column's tier and whether it is arid.
        /// </summary>
        [ThreadStatic] static int soilX, soilZ;
        [ThreadStatic] static long soilModel;
        [ThreadStatic] static bool soilKnown, soilArid;
        [ThreadStatic] static double soilRain, soilDry, soilPenman;
        [ThreadStatic] static Topsoil.Tier soilTier;

        // ---- decision 0105: Earth soil's top blocks, swapped in after vanilla's layers of a chunk
        [ThreadStatic] static sbyte[] chunkTiers;
        [ThreadStatic] static int tiersChunkX, tiersChunkZ;
        [ThreadStatic] static bool tiersChunkKnown;

        static void RecordTier(int x, int z, Topsoil.Tier tier)
        {
            if (!tiersChunkKnown || x >> 5 != tiersChunkX || z >> 5 != tiersChunkZ) return;
            chunkTiers[(z & 31) * 32 + (x & 31)] = (sbyte)tier;
        }

        /// <summary>(Decision 0105) Vanilla's layers lay no soil where their rain is below 0.08, nor grassed soil below 0.12, so a
        /// column of an arid river's flood gets Topsoil.FloodRain in vanilla's layers
        /// (not in the climate map, the grass or anything else). Harmony prefix on GenBlockLayers.LoadBlockLayers.</summary>
        internal static void FloodRainPrefix(ref float rainRel, float temperature, BlockPos pos)
        {
            using var fetch = Core.FineElevation.Generating();
            var w = WorldGenerationContext.Current;
            var topsoil = w?.Rules.Topsoil;
            if (topsoil == null || pos == null) return;
            SampleColumn(w, pos.X, pos.Z, temperature);
            if (soilArid && soilTier >= Topsoil.Tier.Barren && rainRel < topsoil.FloodRain) rainRel = (float)topsoil.FloodRain;
        }

        /// <summary>(Decision 0105) The chunk whose layers the tiers are recorded for: every column starts at the tier that swaps
        /// nothing. Harmony prefix on GenBlockLayers.OnChunkColumnGeneration.</summary>
        internal static void TopsoilChunkPrefix([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var w = WorldGenerationContext.Current;
            if (w?.Rules.Topsoil == null || request == null) { tiersChunkKnown = false; return; }
            chunkTiers ??= new sbyte[32 * 32];
            Array.Fill(chunkTiers, (sbyte)Topsoil.Tier.Vanilla);
            tiersChunkX = request.ChunkX; tiersChunkZ = request.ChunkZ; tiersChunkKnown = true;
        }

        /// <summary>
        /// (Decision 0105) After vanilla's layers of a chunk: the tiers vanilla never lays go in on the top soil block, with its
        /// grass, and on the bare soil block under it: barren (laid as low) becomes soil-verylow, high and black earth (laid as
        /// medium over medium subsoil) become soil-compost and soil-high. So vanilla's forest floor, laid later on the top
        /// block, lies on the column's grade (EarthForestFloorSoil drops it). A top block of another kind or grade (snow, sand, a
        /// grade vanilla's rain or cold chose) is left as it is. Harmony postfix on GenBlockLayers.OnChunkColumnGeneration.
        /// </summary>
        internal static void TopsoilChunkPostfix([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var rules = WorldGenerationContext.Current?.Rules;
            if (rules?.Topsoil == null || request == null || !tiersChunkKnown || tiersChunkX != request.ChunkX || tiersChunkZ != request.ChunkZ) return;
            tiersChunkKnown = false;
            var chunks = request.Chunks;
            var heights = chunks[0].MapChunk.RainHeightMap;
            var blocks = rules.TopsoilBlocks; var tierOf = rules.SoilTierOf; var grassOf = rules.SoilGrassOf;
            for (int k = 0; k < 1024; k++)
            {
                var tier = (Topsoil.Tier)chunkTiers[k];
                if (!Topsoil.Swapped(tier)) continue;
                int laid = tier == Topsoil.Tier.Barren ? (int)Topsoil.Tier.Low : (int)Topsoil.Tier.Medium;
                int lx = k & 31, lz = k >> 5;
                for (int y = heights[k], low = Math.Max(0, heights[k] - 2); y >= low; y--)
                {
                    int cy = y / 32;
                    if (cy >= chunks.Length) continue;
                    int index3d = (32 * (y % 32) + lz) * 32 + lx;
                    var data = chunks[cy].Data;
                    int id = data.GetBlockIdUnsafe(index3d);
                    if (id <= 0 || id >= tierOf.Length || tierOf[id] < 0) continue;
                    if (tierOf[id] == laid)
                    {
                        data.SetBlockUnsafe(index3d, blocks[(int)tier, grassOf[id]]);
                        // the block under it too (bare), so the forest floor vanilla lays later lies on the column's grade
                        // and gives it when broken (EarthForestFloorSoil)
                        int below = y - 1;
                        if (below > 0)
                        {
                            int bi = (32 * (below % 32) + lz) * 32 + lx;
                            var bd = chunks[below / 32].Data;
                            int bid = bd.GetBlockIdUnsafe(bi);
                            if (bid > 0 && bid < tierOf.Length && tierOf[bid] == laid && grassOf[bid] == 0) bd.SetBlockUnsafe(bi, blocks[(int)tier, 0]);
                        }
                    }
                    break;
                }
            }
        }

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
            using var fetch = Core.FineElevation.Generating();
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
            using var fetch = Core.FineElevation.Generating();
            if (w?.Model == null || !w.Model.Rules.ShoreStrip) return false;
            // (EA-0193) from the chunk's kept shore-strip bits when its columns are no longer stored
            return ColumnStore.OnShoreStrip(w.Model, wx, wz);
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
