using System;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Vanilla's bauxite strata (rock-bauxite layers anywhere) give way to the column's own rock in worlds with Earth's ore maps;
    /// Earth's bauxite is a host-gated deposit instead (Earth's bauxite lenses, or the ore layers' laterite deposit). Runs right
    /// after vanilla's rock strata, through the strata hook. Part of an <see cref="OreSession"/>.
    /// </summary>
    sealed class BauxiteStrata
    {
        readonly OreSession session;
        internal BauxiteStrata(OreSession session) { this.session = session; }

        /// <summary>Vanilla bauxite strata are replaced by the column's own (Earth or vanilla) rock; Earth bauxite lenses are host-gated deposits instead.</summary>
        internal void Replace(IChunkColumnGenerateRequest request)
        {
            int bauxite = session.BauxiteId, claystone = session.ClaystoneId;
            var chunks = request.Chunks; var map = chunks[0].MapChunk;
            var geo = session.Geology;
            int bx = request.ChunkX * 32, bz = request.ChunkZ * 32;
            // which chunks hold any bauxite at all (a lookup in the chunk's palette, exact): most chunk columns have none,
            // and the walk below skips the chunks without it instead of reading every block under the surface
            Span<bool> has = stackalloc bool[chunks.Length];
            bool any = false;
            for (int i = 0; i < chunks.Length; i++) any |= has[i] = chunks[i].Data.ContainsBlock(bauxite);
            if (!any) return;
            for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
            {
                int k = z * 32 + x;
                int top = Math.Min(chunks.Length * 32 - 1, map.WorldGenTerrainHeightMap[k]);
                for (int y = top; y > 0; y--)
                {
                    if (!has[y / 32]) { y = y / 32 * 32; continue; }
                    if (Id(chunks, x, y, z) != bauxite) continue;
                    int runTop = y;
                    while (y > 1 && Id(chunks, x, y - 1, z) == bauxite) y--;
                    int runBottom = y;
                    int above = runTop < top ? Id(chunks, x, runTop + 1, z) : 0, below = runBottom > 1 ? Id(chunks, x, runBottom - 1, z) : 0;
                    int neighbour = session.IsRock(above) ? above : session.IsRock(below) ? below : claystone;
                    int rTop = geo != null ? geo.RegionalRock(bx + x, bz + z, top - runTop, top) : 0;
                    int rBottom = runBottom == runTop ? rTop : geo != null ? geo.RegionalRock(bx + x, bz + z, top - runBottom, top) : 0;
                    for (int yy = runTop; yy >= runBottom; yy--)
                    {
                        int r = rTop != 0 && rTop == rBottom ? rTop : geo != null && rTop != rBottom ? geo.RegionalRock(bx + x, bz + z, top - yy, top) : 0;
                        if (r <= 0 || r == bauxite) r = neighbour;
                        if (r <= 0) continue;
                        chunks[yy / 32].Data.SetBlockUnsafe(((yy % 32) * 32 + z) * 32 + x, r);
                        if (map.TopRockIdMap[k] == bauxite) map.TopRockIdMap[k] = r;
                    }
                }
            }
        }
        static int Id(IServerChunk[] chunks, int x, int y, int z) => chunks[y / 32].Data.GetBlockIdUnsafe(((y % 32) * 32 + z) * 32 + x);
    }
}
