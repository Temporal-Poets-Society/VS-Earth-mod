using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Vanilla's tiled dungeons (ModSystemTiledDungeons, 1.22) keep the map size they saw in their StartServerSide. In a
    /// new world whose size Earth sets from the Earth tab, the game builds the map again after the start phase, so the
    /// generator kept the World generation tab's size and refused every dungeon beyond it ("Dungeon at … would reach out
    /// of bounds"; 192,500 warnings in one 4,096,000-wide test world, none after a restart). At WorldReady this points the
    /// generator at the live map size. Only that field changes; nothing else in the save or the generator.
    /// </summary>
    public static class DungeonMapSize
    {
        /// <summary>The generator (ModSystemTiledDungeons.dungeonGen, internal) and its map size (DungeonGenerator.mapsize,
        /// protected), bound by PatchVanilla's DungeonTable; null when a game update changed either (its line says so).</summary>
        internal static AccessTools.FieldRef<ModSystemTiledDungeons, DungeonGenerator> GeneratorRef;
        internal static AccessTools.FieldRef<DungeonGenerator, Vec3i> SizeRef;

        public static string Fix(ICoreServerAPI sapi, Mod mod)
        {
            var system = sapi.ModLoader.GetModSystem<ModSystemTiledDungeons>();
            if (system == null) return null;
            if (GeneratorRef == null || SizeRef == null) PatchVanilla.For(sapi, mod, null).Apply(PatchVanilla.DungeonTable());
            if (GeneratorRef == null || SizeRef == null) return null;
            var gen = GeneratorRef(system);
            if (gen == null) return null;
            var live = sapi.World.BlockAccessor.MapSize;
            var kept = SizeRef(gen);
            if (live == null || (kept != null && kept.X == live.X && kept.Y == live.Y && kept.Z == live.Z)) return null;
            SizeRef(gen) = live;
            return string.Format("Tiled dungeons: map size {0} × {1} × {2} instead of the {3} × {4} × {5} seen at start-up.",
                live.X, live.Y, live.Z, kept?.X, kept?.Y, kept?.Z);
        }
    }
}
