using System.Collections.Generic;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// EA-0178: in Earth worlds a creature that spawns in water (vanilla's fish, other mods' water animals) has its height band
    /// measured from its own water's surface (<see cref="WaterDepthBand"/>), not from sea level, where vanilla's band left no
    /// fish in any lake or river above the sea.
    ///
    /// Vanilla tests that band in places no patch can reach (inside GenCreatures.CanSpawnAtConditions, and inline in the
    /// runtime spawner before CanSpawnAt_offthread), so while Earth is active each such type's band is widened to the whole
    /// world (0..2) and tested here instead, after vanilla's own test of the position: two postfixes bound by PatchVanilla
    /// (WorldgenWaterDepth on GenCreatures.CanSpawnAtPosition, RuntimeWaterDepth on CanSpawnAt_offthread). A side is widened
    /// only while its postfix is bound, so a game update that takes the patch away leaves vanilla's test, not none. When Earth
    /// goes inactive (a world generator restart, the end of the session) the bands are put back.
    ///
    /// A type spawns in water when every block its InsideBlockCodes name is a liquid. Reef fish (inside coral) keep vanilla's
    /// test; corals grow only in the sea, whose surface is the sea level, where both tests are the same. Not tied to a rules
    /// version: fish are not terrain (they move and die), and saved Earth worlds had the same missing fish.
    /// </summary>
    static class WaterSpawnDepth
    {
        sealed class Band
        {
            public float MinY, MaxY;
        }

        /// <summary>Each widened conditions object's own band (by reference); null while none is widened.</summary>
        static volatile Dictionary<BaseSpawnConditions, Band> bands;
        static IWorldAccessor world;
        static int seaLevel, mapHeight;
        static long worldgenKept, worldgenRefused, runtimeKept, runtimeRefused;

        /// <summary>
        /// Widens the water creatures' bands of the bound sides, once Earth is active. Returns the number of entity types widened
        /// (for the log).
        /// </summary>
        internal static int Widen(ICoreServerAPI api, bool worldgen, bool runtime)
        {
            Restore();
            if (!worldgen && !runtime) return 0;
            world = api.World;
            seaLevel = api.World.SeaLevel;
            mapHeight = api.WorldManager.MapSizeY;
            var map = new Dictionary<BaseSpawnConditions, Band>(ReferenceEqualityComparer.Instance);
            var liquidCache = new Dictionary<AssetLocation, bool>();
            int types = 0;
            foreach (var type in api.World.EntityTypes)
            {
                var conditions = type?.Server?.SpawnConditions;
                if (conditions == null) continue;
                bool any = false;
                if (worldgen) any |= Take(map, conditions.Worldgen, liquidCache);
                if (runtime) any |= Take(map, conditions.Runtime, liquidCache);
                if (any) types++;
            }
            worldgenKept = worldgenRefused = runtimeKept = runtimeRefused = 0;
            bands = map.Count > 0 ? map : null;
            return types;
        }

        static bool Take(Dictionary<BaseSpawnConditions, Band> map, BaseSpawnConditions sc, Dictionary<AssetLocation, bool> liquidCache)
        {
            if (sc == null || map.ContainsKey(sc) || (sc.MinY <= 0 && sc.MaxY >= 2) || !InWater(sc, liquidCache)) return false;
            map[sc] = new Band { MinY = sc.MinY, MaxY = sc.MaxY };
            sc.MinY = 0;
            sc.MaxY = 2;
            return true;
        }

        /// <summary>Whether every block the conditions spawn inside is a liquid (and there is one).</summary>
        static bool InWater(BaseSpawnConditions sc, Dictionary<AssetLocation, bool> liquidCache)
        {
            var codes = sc.InsideBlockCodes;
            if (codes == null || codes.Length == 0) return false;
            foreach (var code in codes)
            {
                if (code == null) return false;
                if (!liquidCache.TryGetValue(code, out bool liquid))
                {
                    var blocks = world.SearchBlocks(code);
                    liquid = blocks.Length > 0;
                    foreach (var block in blocks) liquid &= block.IsLiquid();
                    liquidCache[code] = liquid;
                }
                if (!liquid) return false;
            }
            return true;
        }

        /// <summary>Puts every widened band back (Earth inactive).</summary>
        internal static void Restore()
        {
            var map = bands;
            bands = null;
            if (map == null) return;
            foreach (var (sc, band) in map)
            {
                sc.MinY = band.MinY;
                sc.MaxY = band.MaxY;
            }
        }

        /// <summary>For the log at the end of the session; null when nothing was widened.</summary>
        internal static string Report() => bands == null && worldgenKept + worldgenRefused + runtimeKept + runtimeRefused == 0 ? null
            : $"placed at world generation {Interlocked.Read(ref worldgenKept)}, turned away for their depth {Interlocked.Read(ref worldgenRefused)}; " +
              $"spawning {Interlocked.Read(ref runtimeKept)}, turned away {Interlocked.Read(ref runtimeRefused)}";

        /// <summary>The first block above the liquid at <paramref name="pos"/> (the position itself when it isn't liquid).</summary>
        static int Surface(IBlockAccessor accessor, BlockPos pos)
        {
            var p = pos.Copy();
            while (p.Y < mapHeight && accessor.GetBlock(p, BlockLayersAccess.Fluid).IsLiquid()) p.Y++;
            return p.Y;
        }

        /// <summary>GenCreatures.CanSpawnAtPosition (protected; it tests the block the creature spawns in and the one under it):
        /// a water creature's place is also kept only in its depth band below its water's surface.</summary>
        internal static void WorldgenPostfix([HarmonyArgument(0)] IBlockAccessor blockAccessor, [HarmonyArgument(2)] BlockPos pos,
            [HarmonyArgument(3)] BaseSpawnConditions sc, ref bool __result)
        {
            var map = bands;
            if (!__result || map == null || sc == null || pos == null || blockAccessor == null || !map.TryGetValue(sc, out var band)) return;
            if (WaterDepthBand.Allows(band.MinY, band.MaxY, pos.Y, Surface(blockAccessor, pos), seaLevel, mapHeight))
            {
                Interlocked.Increment(ref worldgenKept);
                return;
            }
            Interlocked.Increment(ref worldgenRefused);
            __result = false;
        }

        /// <summary>ServerSystemEntitySpawner.CanSpawnAt_offthread (the server's own assembly): the place it found for a water
        /// creature is kept only in its depth band below its water's surface, read from the chunk column the spawner holds.</summary>
        internal static void RuntimePostfix([HarmonyArgument(2)] RuntimeSpawnConditions sc, [HarmonyArgument(3)] IWorldChunk[] chunkCol, ref Vec3d __result)
        {
            var map = bands;
            var w = world;
            if (__result == null || map == null || w == null || sc == null || chunkCol == null || !map.TryGetValue(sc, out var band)) return;
            var p = new BlockPos((int)__result.X, (int)__result.Y, (int)__result.Z);
            int y = p.Y;
            while (p.Y < mapHeight && p.Y / 32 < chunkCol.Length && chunkCol[p.Y / 32] is IWorldChunk chunk
                && chunk.GetLocalBlockAtBlockPos_LockFree(w, p, 2).IsLiquid())
                p.Y++;
            if (WaterDepthBand.Allows(band.MinY, band.MaxY, y, p.Y, seaLevel, mapHeight))
            {
                Interlocked.Increment(ref runtimeKept);
                return;
            }
            Interlocked.Increment(ref runtimeRefused);
            __result = null;
        }
    }
}
