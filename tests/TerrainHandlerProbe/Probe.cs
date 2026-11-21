using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Vintagestory.ServerMods;

namespace EarthTerrainProbe
{
    /// <summary>
    /// (EA-0162) Test-only: what another mod may do with the terrain pass's handlers, so that a headless Earth world shows how
    /// Earth answers. The mode is the environment variable EARTH_TERRAIN_PROBE:
    /// - replace-before: before Earth starts, vanilla's terrain handler (GenTerra's) is replaced by one that makes nothing;
    /// - wrap-before: before Earth starts, vanilla's terrain handler is wrapped (the wrapper calls it);
    /// - replace-after: after Earth starts, Earth's terrain handler (the pass's first) is replaced by one that makes nothing;
    /// - wrap-after: after Earth starts, Earth's terrain handler is wrapped (the wrapper calls it);
    /// - replay: once the game runs, the terrain pass's handlers after the first are run on chunks of the probe's own at the spawn
    ///   area's middle chunk (as a far-terrain mod previews terrain without vanilla's terrain generator);
    /// - peek: once the game runs, vanilla's peek (a static translocator's search) at a chunk 40 chunks east of the spawn area.
    /// Each mode logs lines starting with "TERRAINPROBE"; the probe stops the server 40 seconds after the game runs.
    /// </summary>
    public abstract class ProbeBase : ModSystem
    {
        protected static string Mode => (Environment.GetEnvironmentVariable("EARTH_TERRAIN_PROBE") ?? "").Trim().ToLowerInvariant();
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        protected static System.Collections.Generic.List<ChunkColumnGenerationDelegate> TerrainList(ICoreServerAPI api)
            => api.Event.GetRegisteredWorldGenHandlers("standard").OnChunkColumnGen[(int)EnumWorldGenPass.Terrain];
    }

    /// <summary>Before Earth (0.7), after vanilla's GenTerra (0.0).</summary>
    public class Early : ProbeBase
    {
        public override double ExecuteOrder() => 0.05;

        public override void StartServerSide(ICoreServerAPI api)
        {
            if (Mode != "replace-before" && Mode != "wrap-before") return;
            var list = TerrainList(api);
            int i = list.FindIndex(d => d?.Target is GenTerra);
            if (i < 0) { api.Logger.Notification("TERRAINPROBE {0}: GenTerra's handler not found", Mode); return; }
            var genTerra = list[i];
            list[i] = Mode == "replace-before" ? TerrainOfItsOwn : request => genTerra(request);
            api.Logger.Notification("TERRAINPROBE {0}: GenTerra's handler at {1} {2}", Mode, i, Mode == "replace-before" ? "replaced" : "wrapped");
        }

        /// <summary>A terrain generator that makes nothing (the chunk stays air).</summary>
        static void TerrainOfItsOwn(IChunkColumnGenerateRequest request) { }
    }

    /// <summary>After Earth (0.7) and its companions.</summary>
    public class Late : ProbeBase
    {
        ICoreServerAPI api;

        public override double ExecuteOrder() => 0.95;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            string mode = Mode;
            if (mode == "") return;
            api.Logger.Notification("TERRAINPROBE mode {0}", mode);
            if (mode == "replace-after" || mode == "wrap-after")
            {
                var list = TerrainList(api);
                var earth = list[0];
                list[0] = mode == "replace-after" ? TerrainOfItsOwn : request => earth(request);
                api.Logger.Notification("TERRAINPROBE {0}: the terrain pass's first handler ({1}.{2}) {3}", mode, earth.Method.DeclaringType?.FullName,
                    earth.Method.Name, mode == "replace-after" ? "replaced" : "wrapped");
            }
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                api.Logger.Notification("TERRAINPROBE {0}: the game runs", mode);
                if (mode == "replay") api.Event.RegisterCallback(_ => new Thread(Replay) { IsBackground = true, Name = "terrainprobe" }.Start(), 5000);
                if (mode == "peek") api.Event.RegisterCallback(_ => Peek(), 5000);
                api.Event.RegisterCallback(_ =>
                {
                    api.Logger.Notification("TERRAINPROBE {0}: end, stopping the server", mode);
                    api.Server.ShutDown();
                }, 40000);
            });
        }

        static void TerrainOfItsOwn(IChunkColumnGenerateRequest request) { }

        /// <summary>The map's middle chunk: vanilla makes its 7 x 7 spawn chunks there in every new world (a headless server
        /// loads nothing at the player's spawn, which Earth moves elsewhere).</summary>
        (int cx, int cz) SpawnChunk() => (api.WorldManager.MapSizeX / 2 / 32, api.WorldManager.MapSizeZ / 2 / 32);

        /// <summary>The terrain pass's handlers after the first on chunks of the probe's own, on a thread of its own.</summary>
        void Replay()
        {
            try
            {
                var (cx, cz) = SpawnChunk();
                var world = api.World;
                var pool = world.GetType().GetField("serverChunkDataPool", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(world);
                var chunkMapField = typeof(ServerChunk).GetField("serverMapChunk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                int regionChunks = api.WorldManager.RegionSize / 32;
                var region = api.WorldManager.GetMapRegion(cx / regionChunks, cz / regionChunks) as ServerMapRegion;
                if (pool == null || chunkMapField == null || region == null)
                {
                    api.Logger.Notification("TERRAINPROBE replay: can't build chunks (pool {0}, field {1}, region {2})", pool != null, chunkMapField != null, region != null);
                    return;
                }
                var mapChunk = ServerMapChunk.CreateNew(region);
                var chunks = new IServerChunk[api.WorldManager.MapSizeY / 32];
                for (int y = 0; y < chunks.Length; y++)
                {
                    var c = ServerChunk.CreateNew((Vintagestory.Common.ChunkDataPool)pool);
                    chunkMapField.SetValue(c, mapChunk);
                    c.Unpack();
                    chunks[y] = c;
                }
                var request = new ReplayRequest(chunks, cx, cz);
                var handlers = TerrainList(api).ToArray();
                api.Logger.Notification("TERRAINPROBE replay: chunk {0},{1}, handlers 2 to {2} of the terrain pass", cx, cz, handlers.Length);
                for (int i = 1; i < handlers.Length; i++)
                {
                    try { handlers[i](request); }
                    catch (Exception e)
                    {
                        api.Logger.Notification("TERRAINPROBE replay: handler {0} ({1}.{2}) threw {3}: {4}", i + 1, handlers[i].Method.DeclaringType?.FullName,
                            handlers[i].Method.Name, e.GetType().Name, e.Message);
                    }
                }
                api.Logger.Notification("TERRAINPROBE replay: done");
            }
            catch (Exception e) { api.Logger.Notification("TERRAINPROBE replay failed: {0}", e); }
        }

        void Peek()
        {
            var (cx, cz) = SpawnChunk();
            cx += 40;
            api.Logger.Notification("TERRAINPROBE peek: chunk {0},{1} until the vegetation pass", cx, cz);
            api.WorldManager.PeekChunkColumn(cx, cz, new ChunkPeekOptions
            {
                UntilPass = EnumWorldGenPass.Vegetation,
                OnGenerated = columns => api.Logger.Notification("TERRAINPROBE peek: {0} column(s) back; the centre's top y {1}", columns.Count,
                    columns.TryGetValue(new Vintagestory.API.MathTools.Vec2i(cx, cz), out var cols) ? cols[0].MapChunk.YMax.ToString() : "?"),
            });
        }

        sealed class ReplayRequest : IChunkColumnGenerateRequest
        {
            public ReplayRequest(IServerChunk[] chunks, int cx, int cz) { Chunks = chunks; ChunkX = cx; ChunkZ = cz; }
            public IServerChunk[] Chunks { get; }
            public int ChunkX { get; }
            public int ChunkZ { get; }
            public ITreeAttribute ChunkGenParams => null;
            public ushort[][] NeighbourTerrainHeight => null;
            public bool RequiresChunkBorderSmoothing => false;
        }
    }
}
