using System;
using EarthMap.Core;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// The river water shapes the terrain pass settled (TerrainChunkWriter, handed over as <see cref="RampWord"/>s in the
    /// <see cref="TerrainHandoff"/>), drawn at the start of the surface pass, after vanilla's block layers: in the terrain pass
    /// they would count as ground and get covered with soil.
    /// </summary>
    sealed class RiverBlockPass
    {
        readonly ICoreServerAPI sapi;
        readonly TerrainModel model;
        readonly WorldGenBlocks blocks;
        readonly GlobalConfig gcfg;
        readonly TerrainHandoff handoff;

        public RiverBlockPass(ICoreServerAPI sapi, TerrainModel model, WorldGenBlocks blocks, GlobalConfig gcfg, TerrainHandoff handoff)
        {
            this.sapi = sapi;
            this.model = model;
            this.blocks = blocks;
            this.gcfg = gcfg;
            this.handoff = handoff;
        }

        /// <summary>
        /// The flowing water ramps below river steps, on top of the lower pool. Pool layout 5 (decision 0024 step 3) draws
        /// the drops in the shape the 2026-10-23 rule study found stays as generated under Hydrology's rules: the upper
        /// water's edge sources labelled with the flow (rapid at a chute's step and down a curtain's lip), curtains of falling
        /// rapid water on a rapid foot, and a spill of falling rapid water where a chute's ramp ends beside the next step.
        /// </summary>
        public void PlaceRamps(IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            if (!handoff.TakeRamps(chunkX, chunkZ, out uint[] ramp)) return;
            int[] edgeIds = blocks.Edge, rapidEdgeIds = blocks.RapidEdge, flowIds = blocks.Flow, rapidIds = blocks.Rapid, rapidFallIds = blocks.RapidFall;
            int rapidSourceId = blocks.RapidSource, fallId = blocks.Fall;
            var mc = chunks[0].MapChunk;
            ushort[] rain = mc.RainHeightMap;
            int mapHeight = sapi.WorldManager.MapSizeY;
            int yMax = mc.YMax;
            bool shapes = model.Rivers?.PoolLayout >= 5;
            // the edges first, at the water's top as the terrain pass left it (an edge of a chute also carries a ramp); pool
            // layout 8: the top source of any other column of a rapids reach is rapid water (pool layout 9: labelled with the
            // river's flow, rapidwater-<dir>-7, so the whole reach looks like and pushes as running water; a still-labelled
            // rapid source looks calm and has no push, and the play-test saw the pools between steps as still water)
            // Pool layout 11 (decision 0046): rapid water only at a rapids reach's drops (its rapid edges, and the sources under
            // the rapid ramps below them); every other river column's top source is ordinary water labelled with the river's
            // flow, water-<dir>-7: a gentle current that boats can row against, with the creek's sound, not still water
            bool flowLabels = model.Rivers?.PoolLayout >= 9;
            for (int i2 = 0; i2 < 32 * 32; i2++)
            {
                var r = new RampWord(ramp[i2]);
                int edge = r.Edge;
                // the source's kind, as the river query reports it: rapid (a chute's or a curtain's edge, a rapid top source)
                // or ordinary
                bool rapids = r.RapidSource;
                if (edge == 0)
                {
                    int sourceId = !rapids ? edgeIds[r.Dir] : flowLabels ? rapidEdgeIds[r.Dir] : rapidSourceId;
                    if ((rapids || r.CalmRiver) && sourceId > 0)
                    {
                        int y = rain[i2], lx2 = i2 % 32, lz2 = i2 / 32;
                        int idx2 = ((y % 32) * 32 + lz2) * 32 + lx2;
                        var data2 = chunks[y / 32].Data;
                        if (data2.GetFluid(idx2) == gcfg.waterBlockId) data2.SetFluid(idx2, sourceId);
                    }
                    continue;
                }
                int dir = r.Dir;
                int id = (rapids ? rapidEdgeIds : edgeIds)[dir];
                if (id <= 0) continue;
                int depth = edge == TerrainModel.EdgeLip ? Math.Max(1, r.EdgeDepth) : 1;
                int lx = i2 % 32, lz = i2 / 32;
                for (int y = rain[i2], n = 0; n < depth && y > 0; y--, n++)
                {
                    int idx = ((y % 32) * 32 + lz) * 32 + lx;
                    var data = chunks[y / 32].Data;
                    if (data.GetFluid(idx) != gcfg.waterBlockId) break;
                    data.SetFluid(idx, id);
                }
            }
            for (int i2 = 0; i2 < 32 * 32; i2++)
            {
                var r = new RampWord(ramp[i2]);
                if (r.Bits == 0) continue;
                int h = r.Height, dir = r.Dir, fall = r.Fall, spill = r.Spill;
                if (h < 1 || h > 6) continue;
                int y0 = rain[i2] + 1;
                int lx = i2 % 32, lz = i2 / 32;
                int below = y0 - 1;
                // only on open water: the pool top below is water
                // (pool layout 5: or an edge source, relabelled above; a chute's step edge carries the ramp of the step before)
                int fluidBelow = chunks[below / 32].Data.GetFluid(((below % 32) * 32 + lz) * 32 + lx);
                if (fluidBelow != gcfg.waterBlockId && !(shapes && fluidBelow > 0 && (Array.IndexOf(edgeIds, fluidBelow) >= 0 || Array.IndexOf(rapidEdgeIds, fluidBelow) >= 0
                    || fluidBelow == rapidSourceId))) continue;
                // a waterfall: falling water from the higher neighbour's surface down to this pool, flowing water on top;
                // else the ramp layer
                int yTop = fall >= 2 && fallId > 0 ? y0 + fall - 1 : y0;
                // pool layout 5: the spill above a chute's ramp cell (pool layout 7: or above a curtain's top)
                int ySpill = shapes && spill > 0 ? yTop + 1 : -1;
                if (ySpill > 0) yTop = ySpill;
                for (int y = y0; y <= yTop && y < mapHeight - 1; y++)
                {
                    int idx = ((y % 32) * 32 + lz) * 32 + lx;
                    var data = chunks[y / 32].Data;
                    if (data.GetBlockIdUnsafe(idx) != 0 || data.GetFluid(idx) != 0) break; // something in the way
                    int id;
                    if (y == ySpill) id = rapidFallIds[Math.Clamp(spill, 1, 6) - 1];
                    else if (fall >= 2 && shapes)
                    {
                        // a curtain of falling rapid water up to the upper water's level, on a rapid foot on the plunge pool
                        id = y == y0 ? rapidIds[dir * 6 + 5] : rapidFallIds[5] > 0 ? rapidFallIds[5] : fallId;
                    }
                    else if (fall >= 2) id = y < yTop ? fallId : (r.Rapid ? rapidIds : flowIds)[dir * 6 + 5];
                    else id = (r.Rapid ? rapidIds : flowIds)[dir * 6 + h - 1];
                    if (id <= 0) break;
                    data.SetFluid(idx, id);
                    rain[i2] = (ushort)y;
                    if (y > yMax) yMax = y;
                }
            }
            mc.YMax = (ushort)yMax;
        }
    }
}
