using System;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The terrain grid's cells as one set of overlays leaves them: each terrain.bin tile is decoded the first time something
    /// reads it, the overlays applied in their fixed order (the referee, the shores, the islands, the terminal lakes, the
    /// reservoirs taken out, the outlets), and kept in a bounded TileCache. The overlays never change, so neither do the
    /// tiles: a world that changes its overlays gets a new cache (EarthDataView), and nothing decoded under the old ones is
    /// read again. Per thread, the last 4x4 neighbourhood, which nearly every sample of a chunk hits. Thread-safe.
    /// </summary>
    public sealed class TerrainTileCache
    {
        readonly EarthDataset data;
        readonly TerrainOverlays overlays;
        readonly int tile, tilesX, terrainW, terrainH;
        readonly TileCache<TerrainTile> tiles;
        /// <summary>Tiles kept (each about 360 KB); past it the least recently used are dropped.</summary>
        public int MaxCachedTiles { get => tiles.Max; set => tiles.Max = value; }
        // per thread: the last sample's neighbourhood (nearly every lookup of a chunk hits it; the tiles' own per-thread last
        // tile is TileCache's), keyed by a number rather than this object, so an unloaded world is not kept alive by a thread
        static int instances;
        readonly int instanceId = Interlocked.Increment(ref instances);
        [ThreadStatic] static Neighbourhood lastAround;

        internal TerrainTileCache(EarthDataset data, TerrainOverlays overlays, int maxCachedTiles)
        {
            this.data = data;
            this.overlays = overlays;
            tile = data.Tile;
            tilesX = data.TilesX;
            terrainW = data.TerrainW;
            terrainH = data.TerrainH;
            tiles = new TileCache<TerrainTile>(DecodeTile, maxCachedTiles);
        }

        internal sealed class Neighbourhood
        {
            public int Owner, R0, C0;
            /// <summary>Elevations of the 4x4 cells from (r0 - 1, c0 - 1).</summary>
            public readonly double[] Elev = new double[16];
            public EarthData.Cell C00, C01, C10, C11;
        }

        TerrainTile GetTile(int ty, int tx) => tiles.Get(ty * tilesX + tx);

        /// <summary>The 4x4 elevations and the middle 2x2 cells around (r0, c0) for SampleTerrain and AddCoastRefs, kept per
        /// thread: the columns of a chunk mostly share them.</summary>
        internal Neighbourhood Around(int r0, int c0)
        {
            var n = lastAround ??= new Neighbourhood();
            if (n.Owner == instanceId && n.R0 == r0 && n.C0 == c0) return n;
            n.Owner = 0;   // incomplete while filling
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++) n.Elev[j * 4 + i] = GetElev(r0 - 1 + j, c0 - 1 + i);
            }
            n.C00 = GetCell(r0, c0);
            n.C01 = GetCell(r0, c0 + 1);
            n.C10 = GetCell(r0 + 1, c0);
            n.C11 = GetCell(r0 + 1, c0 + 1);
            n.R0 = r0;
            n.C0 = c0;
            n.Owner = instanceId;
            return n;
        }

        TerrainTile DecodeTile(int key)
        {
            int n = tile * tile;
            var o = overlays;
            var t = data.DecodeTile(key);
            // the water mask's referee as part of the grid itself (a refereed terrain.bin), before every other overlay
            if (o.SeaReferee && o.RefereeTilePatches.TryGetValue(key, out var refereePatches)) ApplyPatches(t, refereePatches);
            // the shores first: where the islands or the terminal lakes change a cell too, theirs is the cell
            if (o.ShoreOverlay && o.ShoreTilePatches.TryGetValue(key, out var shorePatches)) ApplyPatches(t, shorePatches);
            if (o.ShoreOverlay && o.ShoreTileNear.TryGetValue(key, out var near))
            {
                t.Near = new byte[n];
                Array.Fill(t.Near, (byte)255);
                foreach (var (i, v) in near) t.Near[i] = v;
            }
            if (o.IslandOverlay && o.IslandTilePatches.TryGetValue(key, out var patches)) ApplyPatches(t, patches);
            if (o.LakeOverlay && o.LakeTilePatches.TryGetValue(key, out var lakePatches)) ApplyPatches(t, lakePatches);
            if (o.ReservoirExclusions && o.ReservoirCells.TryGetValue(key,out var excluded))
                foreach(int i in excluded)
                {
                    if((t.Flags[i]&EarthData.FlagOcean)!=0)continue;
                    t.Flags[i]&=unchecked((byte)~(EarthData.FlagLake|EarthData.FlagSalt));t.Level[i]=EarthData.NoLevel;
                    if(t.Near!=null)t.Near[i]=0;
                }
            // last: a tongue past a lake's outlet is land whatever the overlays above made of it
            if (o.OutletOverlay && o.OutletTilePatches.TryGetValue(key, out var outletPatches)) ApplyPatches(t, outletPatches);
            return t;
        }

        void ApplyPatches(TerrainTile t, IslandData.Patch[] patches)
        {
            foreach (var p in patches)
            {
                int i = (p.Row % tile) * tile + p.Col % tile;
                t.Elev[i] = data.ElevationOf(p.ElevCode);
                t.Level[i] = p.Level;
                t.Flags[i] = p.Flags;
                // a cell an island or a terminal lake changes is no longer the base lakes' ring: the old rules hold there
                if (t.Near != null) t.Near[i] = 255;
            }
        }

        /// <summary>A terrain cell. Rows are clamped at the poles, columns wrap around.</summary>
        public EarthData.Cell GetCell(int row, int col)
        {
            row = Math.Clamp(row, 0, terrainH - 1);
            col %= terrainW;
            if (col < 0) col += terrainW;
            var t = GetTile(row / tile, col / tile);
            int i = (row % tile) * tile + col % tile;
            return new EarthData.Cell { Elev = t.Elev[i], Level = t.Level[i], Flags = t.Flags[i], Ice = t.Ice[i], Near = t.Level[i] == EarthData.NoLevel ? (byte)0 : t.Near != null ? t.Near[i] : (byte)255 };
        }

        public float GetElev(int row, int col)
        {
            row = Math.Clamp(row, 0, terrainH - 1);
            col %= terrainW;
            if (col < 0) col += terrainW;
            var t = GetTile(row / tile, col / tile);
            return t.Elev[(row % tile) * tile + col % tile];
        }
    }
}
