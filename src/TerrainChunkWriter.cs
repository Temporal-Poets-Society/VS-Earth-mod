using System;
using EarthMap.Core;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// The terrain pass in Earth worlds, in place of vanilla's GenTerra: every chunk column is built from the terrain model:
    /// mantle, rock up to the real elevation, glacier ice on ice sheets, salt water in the sea, fresh (or salt) water in lakes
    /// at their own level, with the water between neighbouring columns settled where a river or lakes at different levels meet.
    /// What the surface pass needs from it (river ramps, beaches, dry river beds) goes into the <see cref="TerrainHandoff"/>.
    /// Everything after it (rock strata, caves, soil, vegetation, structures, Cylinder World's seam and walls) runs as usual on
    /// top.
    /// </summary>
    sealed class TerrainChunkWriter
    {
        readonly ICoreServerAPI sapi;
        readonly TerrainModel model;
        readonly EarthParams p;
        readonly GlobalConfig gcfg;
        readonly WorldGenBlocks blocks;
        readonly TerrainHandoff handoff;
        readonly ThreadLocalColumns columnsCache = new ThreadLocalColumns();
        EarthElevationSystem elevation;
        /// <summary>The settling, shared with the river query (ChunkSettler).</summary>
        readonly ChunkSettler settler;
        /// <summary>The settling's scratch, per generation thread (the query has its own).</summary>
        readonly System.Threading.ThreadLocal<TerrainModel.Column[]> extCache;

        sealed class ThreadLocalColumns : System.Threading.ThreadLocal<TerrainModel.Column[]>
        {
            public ThreadLocalColumns() : base(() => new TerrainModel.Column[32 * 32]) { }
        }

        public TerrainChunkWriter(ICoreServerAPI sapi, TerrainModel model, EarthParams p, GlobalConfig gcfg, WorldGenBlocks blocks, TerrainHandoff handoff, ChunkSettler settler)
        {
            this.sapi = sapi;
            this.model = model;
            this.p = p;
            this.gcfg = gcfg;
            this.blocks = blocks;
            this.handoff = handoff;
            this.settler = settler;
            extCache = new System.Threading.ThreadLocal<TerrainModel.Column[]>(settler.NewScratch);
        }

        public void Generate(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            int cx = request.ChunkX, cz = request.ChunkZ;
            int mapHeight = sapi.WorldManager.MapSizeY;
            int sea = model.SeaLevel;
            var cols = columnsCache.Value;
            // decision 0051 (proposed): the GLO-90 tiles under this chunk (and a margin) are here before its columns are made;
            // from now on this generation thread waits for any other tile its columns reach
            if (model.Fine != null) (elevation ??= sapi.ModLoader.GetModSystem<EarthElevationSystem>())?.PrepareChunk(cx, cz);

            // the model's columns, the water between neighbouring columns settled where a river is near (ChunkSettler, the one
            // routine the river query uses too): the chunk's own columns from the column store, built whole for the later
            // passes, the margin a neighbour's there
            var ext = extCache.Value;
            settler.Settle(cx, cz, ext);
            // the surface pass's flags per column (TerrainHandoff): dry river beds and beaches (settling changes neither)
            byte[] flags = null;
            bool dryRivers = model.AridRiverRules >= 1;

            int minFill = int.MaxValue;
            uint[] ramp = null;
            bool calmLabels = model.Rivers?.PoolLayout >= 11;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    var c = ext[settler.Own(lx, lz)];
                    if (dryRivers && c.DryRiver) (flags ??= new byte[32 * 32])[lz * 32 + lx] |= TerrainHandoff.FlagDryRiver;
                    if (c.Beach) (flags ??= new byte[32 * 32])[lz * 32 + lx] |= TerrainHandoff.FlagBeach;
                    // the river's water shapes for the surface pass (RampWord.TryOf: ramps, falls, edges, spills, labelled sources)
                    if (RampWord.TryOf(c, calmLabels, out uint word)) (ramp ??= new uint[32 * 32])[lz * 32 + lx] = word;
                    c.Top = Math.Clamp(c.Top, 1, mapHeight - 2);
                    if (c.WaterTop >= mapHeight - 1) c.WaterTop = mapHeight - 2;
                    cols[lz * 32 + lx] = c;
                    minFill = Math.Min(minFill, Math.Min(c.Top, c.IceBottom - 1));
                }
            }
            minFill = Math.Max(minFill, 0);

            int rockId = gcfg.defaultRockId;
            int glacierIceId = blocks.GlacierIce;
            var data = chunks[0].Data;
            data.SetBlockBulk(0, 32, 32, gcfg.mantleBlockId);
            // the rock floor (Earth tab): mantle for now from this depth below the chunk's lowest rock top, so rock strata and
            // ores skip it; RockFloorPass makes it plain rock after caves and ores
            int floorDepth = p.RockFloorDepth;
            int rockFloor = floorDepth > 0 ? minFill - floorDepth : 0;
            // full layers of rock below the lowest column
            for (int y = 1; y <= minFill; y++)
            {
                if (y % 32 == 0 || y == 1) data = chunks[y / 32].Data;
                data.SetBlockBulk((y % 32) * 32 * 32, 32, 32, y <= rockFloor ? gcfg.mantleBlockId : rockId);
            }

            var mapChunk = chunks[0].MapChunk;
            // in the map chunk, which is saved with chunks still generating; kept for prospecting (0: no floor in this column)
            if (floorDepth > 0) mapChunk.SetModdata(RockFloorPass.Moddata, BitConverter.GetBytes(Math.Max(rockFloor, 0)));
            ushort[] terrainHeight = mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeight = mapChunk.RainHeightMap;
            int yMax = 0;
            bool? frozen = null;

            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    int i2 = lz * 32 + lx;
                    var c = cols[i2];
                    for (int y = minFill + 1; y <= c.Top; y++)
                    {
                        chunks[y / 32].Data[((y % 32) * 32 + lz) * 32 + lx] = y >= c.IceBottom ? glacierIceId : rockId;
                    }
                    int top = c.Top;
                    if (c.WaterTop > c.Top)
                    {
                        int waterId = c.Water == TerrainModel.WaterLake ? gcfg.waterBlockId : gcfg.saltWaterBlockId;
                        for (int y = c.Top + 1; y <= c.WaterTop; y++)
                        {
                            int id = waterId;
                            if (y == c.WaterTop && c.Water == TerrainModel.WaterLake)
                            {
                                frozen ??= settler.LakesFreeze(cx, cz);
                                if (frozen.Value) id = gcfg.lakeIceBlockId;
                            }
                            chunks[y / 32].Data.SetFluid(((y % 32) * 32 + lz) * 32 + lx, id);
                        }
                        top = c.WaterTop;
                    }
                    terrainHeight[i2] = (ushort)c.Top;
                    rainHeight[i2] = (ushort)top;
                    if (top > yMax) yMax = top;
                }
            }
            mapChunk.YMax = (ushort)yMax;
            // always an entry: the surface pass tells "nothing flagged" from "terrain made in an earlier session"
            handoff.PutFlags(cx, cz, flags);
            if (ramp != null && frozen != true) handoff.PutRamps(cx, cz, ramp);
        }
    }
}
