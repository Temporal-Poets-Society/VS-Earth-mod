using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// The overlays one world reads the terrain grid with (EarthDataView), frozen: the water mask's referee (with the islands
    /// and coasts it leaves), the islands' reef patches, the filled terminal lakes, the eased lake shores, the reservoirs
    /// taken out, the lakes' outlet tongues. Each overlay's lookup tables come from <see cref="TerrainOverlayTables"/>
    /// (built once per data), so views that share an overlay share its tables. TerrainTileCache applies them to each tile in
    /// their fixed order.
    /// </summary>
    public sealed class TerrainOverlays
    {
        public readonly bool SeaReferee, IslandOverlay, LakeOverlay, ShoreOverlay, ReservoirExclusions, OutletOverlay;
        /// <summary>The islands and the coasts this world reads: the files' own, or as the referee left them.</summary>
        public readonly IslandData Islands;
        public readonly CoastData Coasts;
        internal readonly Dictionary<int, IslandData.Patch[]> RefereeTilePatches, IslandTilePatches, LakeTilePatches, ShoreTilePatches, OutletTilePatches;
        internal readonly Dictionary<int, (int Index, byte Near)[]> ShoreTileNear;
        internal readonly Dictionary<int, int[]> ReservoirCells;
        internal readonly HashSet<long> LakePatchCells, OutletLandCells;
        internal readonly Dictionary<long, float> LakeShoreWeight;

        /// <summary>The overlays a resolved choice turns on (EarthDataView: each only where its file is there).</summary>
        internal TerrainOverlays(EarthDataset data, in EarthDataChoice c, TerrainOverlayTables tables)
        {
            SeaReferee = c.SeaReferee;
            IslandOverlay = c.IslandOverlay;
            LakeOverlay = c.LakeOverlay;
            ShoreOverlay = c.ShoreOverlay;
            ReservoirExclusions = c.ReservoirExclusions;
            OutletOverlay = c.OutletOverlay;
            if (SeaReferee) tables.Referee(out RefereeTilePatches, out Islands, out Coasts);
            else { Islands = data.Islands; Coasts = data.Coasts; }
            // the island overlay's grid patches follow the islands
            if (IslandOverlay) IslandTilePatches = tables.IslandTilePatches(Islands);
            if (LakeOverlay) tables.TerminalLakes(out LakeTilePatches, out LakePatchCells, out LakeShoreWeight);
            if (ShoreOverlay) tables.Shores(out ShoreTilePatches, out ShoreTileNear);
            if (ReservoirExclusions) ReservoirCells = data.ReservoirCells();
            if (OutletOverlay) tables.Outlets(out OutletTilePatches, out OutletLandCells);
        }
    }

    /// <summary>
    /// The overlays' lookup tables (patches by terrain tile, cell sets, the terminal lakes' shore weights), each built the first
    /// time a world turns its overlay on and kept for the data's life. Built under EarthData's selection lock.
    /// </summary>
    sealed class TerrainOverlayTables
    {
        readonly EarthDataset data;
        readonly int tile, tilesX;
        Dictionary<int, IslandData.Patch[]> refereeTilePatches;
        IslandData refereeIslands;
        CoastData refereeCoasts;
        // the islands' patches by tile, for the islands as the file has them and as the referee left them
        Dictionary<int, IslandData.Patch[]> baseIslandTilePatches, refereeIslandTilePatches;
        Dictionary<int, IslandData.Patch[]> lakeTilePatches;
        HashSet<long> lakePatchCells;
        Dictionary<long, float> lakeShoreWeight;
        Dictionary<int, IslandData.Patch[]> shoreTilePatches;
        Dictionary<int, (int Index, byte Near)[]> shoreTileNear;
        Dictionary<int, IslandData.Patch[]> outletTilePatches;
        HashSet<long> outletLandCells;

        public TerrainOverlayTables(EarthDataset data)
        {
            this.data = data;
            tile = data.Tile;
            tilesX = data.TilesX;
        }

        int TerrainW => data.TerrainW;
        int TerrainH => data.TerrainH;
        int TerrainRes => data.TerrainRes;

        /// <summary>The water mask's referee (sea-referee.bin, which must fit the files): its grid patches by tile and the
        /// islands and coasts as it leaves them.</summary>
        public void Referee(out Dictionary<int, IslandData.Patch[]> tilePatches, out IslandData islands, out CoastData coasts)
        {
            if (refereeTilePatches == null)
            {
                var referee = data.SeaReferee;
                referee.Load();
                refereeTilePatches = ByTile(referee.TerrainPatches);
                refereeIslands = referee.ApplyTo(data.Islands);
                refereeCoasts = referee.ApplyTo(data.Coasts);
            }
            tilePatches = refereeTilePatches;
            islands = refereeIslands;
            coasts = refereeCoasts;
        }

        /// <summary>The patches of these islands (the files' own or the referee's) by tile.</summary>
        public Dictionary<int, IslandData.Patch[]> IslandTilePatches(IslandData islands)
        {
            if (ReferenceEquals(islands, data.Islands)) return baseIslandTilePatches ??= ByTile(islands.Patches);
            if (ReferenceEquals(islands, refereeIslands)) return refereeIslandTilePatches ??= ByTile(islands.Patches);
            throw new ArgumentException("islands of another data set");
        }

        /// <summary>lakes.bin's patches by tile, the cells that are lake only through them, and the green of their shores.</summary>
        public void TerminalLakes(out Dictionary<int, IslandData.Patch[]> tilePatches, out HashSet<long> lakeCells, out Dictionary<long, float> shoreWeight)
        {
            if (lakeTilePatches == null)
            {
                var lakes = data.TerminalLakes;
                lakeTilePatches = ByTile(lakes.Patches);
                var cells = new HashSet<long>();
                foreach (var p in lakes.Patches) if ((p.Flags & EarthData.FlagLake) != 0) cells.Add((long)p.Row * TerrainW + p.Col);
                lakePatchCells = cells;
                lakeShoreWeight = ShoreWeights(cells);
            }
            tilePatches = lakeTilePatches;
            lakeCells = lakePatchCells;
            shoreWeight = lakeShoreWeight;
        }

        /// <summary>shores.bin's patches by tile, and its nearness values by tile as (index in the tile, nearness).</summary>
        public void Shores(out Dictionary<int, IslandData.Patch[]> tilePatches, out Dictionary<int, (int Index, byte Near)[]> tileNear)
        {
            if (shoreTilePatches == null)
            {
                var shores = data.Shores;
                shoreTilePatches = ByTile(shores.Patches);
                var lists = new Dictionary<int, List<(int, byte)>>();
                for (int k = 0; k < shores.NearCells.Length; k++)
                {
                    int row = (int)(shores.NearCells[k] / TerrainW), col = (int)(shores.NearCells[k] % TerrainW);
                    int key = row / tile * tilesX + col / tile;
                    if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<(int, byte)>();
                    l.Add(((row % tile) * tile + col % tile, shores.Near[k]));
                }
                shoreTileNear = new Dictionary<int, (int, byte)[]>();
                foreach (var kv in lists) shoreTileNear[kv.Key] = kv.Value.ToArray();
            }
            tilePatches = shoreTilePatches;
            tileNear = shoreTileNear;
        }

        /// <summary>outlets.bin's patches by tile, and the cells they make land.</summary>
        public void Outlets(out Dictionary<int, IslandData.Patch[]> tilePatches, out HashSet<long> landCells)
        {
            if (outletTilePatches == null)
            {
                var outlets = data.Outlets;
                outletTilePatches = ByTile(outlets.Patches);
                var cells = new HashSet<long>();
                foreach (var p in outlets.Patches) if ((p.Flags & (EarthData.FlagLake | EarthData.FlagOcean)) == 0) cells.Add((long)p.Row * TerrainW + p.Col);
                outletLandCells = cells;
            }
            tilePatches = outletTilePatches;
            landCells = outletLandCells;
        }

        /// <summary>
        /// 0..1 per grid cell: how green the dry land around the terminal lakes is (LakeData.Lake.ShoreKm: the Aral's reed
        /// shores and deltas, Lake Chad's margins), 1 on the lake, easing out over the lake's shore width. Each lake is its
        /// patched lake cells connected to its seed.
        /// </summary>
        Dictionary<long, float> ShoreWeights(HashSet<long> lakeCells)
        {
            var weight = new Dictionary<long, float>();
            double kmRow = 111.32 / TerrainRes;
            foreach (var lake in data.TerminalLakes.Lakes)
            {
                if (lake.ShoreKm <= 0) continue;
                int r0 = (int)Math.Floor((90 - lake.Lat) * TerrainRes), c0 = (int)Math.Floor((lake.Lon + 180) * TerrainRes);
                // the lake: from the patched lake cell nearest to its seed
                long seed = -1;
                for (int d = 0; d <= 6 && seed < 0; d++)
                    for (int dr = -d; dr <= d && seed < 0; dr++)
                        for (int dc = -d; dc <= d && seed < 0; dc++)
                        {
                            long k = Key(r0 + dr, c0 + dc);
                            if (lakeCells.Contains(k)) seed = k;
                        }
                if (seed < 0) continue;
                // distance (km) out from the lake, over the cells (Dijkstra; the lake's own cells at 0)
                var dist = new Dictionary<long, double>();
                var pq = new PriorityQueue<long, double>();
                var lakeQueue = new Queue<long>();
                lakeQueue.Enqueue(seed);
                dist[seed] = 0;
                while (lakeQueue.Count > 0)
                {
                    long k = lakeQueue.Dequeue();
                    pq.Enqueue(k, 0);
                    int row = (int)(k / TerrainW), col = (int)(k % TerrainW);
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            long n = Key(row + dr, col + dc);
                            if (n >= 0 && lakeCells.Contains(n) && dist.TryAdd(n, 0)) lakeQueue.Enqueue(n);
                        }
                }
                while (pq.TryDequeue(out long k, out double dk))
                {
                    if (dk > dist[k]) continue;
                    int row = (int)(k / TerrainW), col = (int)(k % TerrainW);
                    double kmCol = kmRow * Math.Cos((90 - (row + 0.5) / TerrainRes) * Math.PI / 180);
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            long n = Key(row + dr, col + dc);
                            if (n < 0) continue;
                            double nd = dk + Math.Sqrt(Math.Pow(dr * kmRow, 2) + Math.Pow(dc * kmCol, 2));
                            if (nd > lake.ShoreKm || (dist.TryGetValue(n, out double od) && od <= nd)) continue;
                            dist[n] = nd;
                            pq.Enqueue(n, nd);
                        }
                }
                foreach (var kv in dist)
                {
                    float w = (float)(1 - Geo.SmoothStep(kv.Value / lake.ShoreKm));
                    if (!weight.TryGetValue(kv.Key, out float old) || w > old) weight[kv.Key] = w;
                }
            }
            return weight;
        }

        /// <summary>A grid cell's number (row * TerrainW + column, columns wrapped); -1 beyond the poles.</summary>
        long Key(int row, int col)
        {
            if (row < 0 || row >= TerrainH) return -1;
            col %= TerrainW;
            if (col < 0) col += TerrainW;
            return (long)row * TerrainW + col;
        }

        Dictionary<int, IslandData.Patch[]> ByTile(IslandData.Patch[] patches)
        {
            var lists = new Dictionary<int, List<IslandData.Patch>>();
            foreach (var p in patches)
            {
                int key = p.Row / tile * tilesX + p.Col / tile;
                if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<IslandData.Patch>();
                l.Add(p);
            }
            var byTile = new Dictionary<int, IslandData.Patch[]>();
            foreach (var kv in lists) byTile[kv.Key] = kv.Value.ToArray();
            return byTile;
        }
    }
}
