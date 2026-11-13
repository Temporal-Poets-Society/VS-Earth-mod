using EarthMap.Core;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// What Earth's patches on vanilla's world generation read about the world being generated (Codex design item 5): its
    /// terrain model, its saved rules and the rule data loaded beside them. EarthWorldGen makes one per server start and
    /// publishes it (<see cref="Current"/>) only once the world's generation is completely set up, the moment Earth becomes
    /// active; it is withdrawn when Earth goes inactive and when the generator is disposed. Vanilla's generators call Earth's
    /// patches as static methods, so they read Current rather than a generator instance, and nothing in it changes once
    /// published.
    /// </summary>
    sealed class WorldGenerationContext
    {
        static WorldGenerationContext current;

        /// <summary>The world Earth is generating, null while Earth is inactive (not an Earth world, not set up yet, or failed).</summary>
        public static WorldGenerationContext Current => System.Threading.Volatile.Read(ref current);

        public readonly ICoreServerAPI Api;
        public readonly TerrainModel Model;
        public readonly EarthParams Params;
        public readonly WorldGenRules Rules;

        public WorldGenerationContext(ICoreServerAPI api, TerrainModel model, EarthParams p, WorldGenRules rules)
        {
            Api = api;
            Model = model;
            Params = p;
            Rules = rules;
        }

        /// <summary>Vanilla's map height, read where it is used (as before the context).</summary>
        public int MapHeight => Api.WorldManager.MapSizeY;

        /// <summary>Makes <paramref name="context"/> the world being generated (null: none).</summary>
        public static void Publish(WorldGenerationContext context) => System.Threading.Volatile.Write(ref current, context);

        /// <summary>Withdraws <paramref name="context"/> if it is still the one published (a newer generator may have
        /// published its own since).</summary>
        public static void Withdraw(WorldGenerationContext context)
        {
            if (context != null) System.Threading.Interlocked.CompareExchange(ref current, null, context);
        }
    }
}
