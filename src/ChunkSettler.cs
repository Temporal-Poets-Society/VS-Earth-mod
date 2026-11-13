using System;
using EarthMap.Core;

namespace EarthMap
{
    /// <summary>
    /// A chunk's terrain columns with their water settled together with their neighbours' (TerrainModel.SettleSteps): the one
    /// routine for the terrain pass (TerrainChunkWriter, which draws them) and the river query (EarthMapSystem.RiverAt, which
    /// must say what was drawn), so the two can never drift apart. The chunk's 32 x 32 columns go in the middle of a scratch
    /// array; where a river is near (or, pool layout 5, lake water meets water at another level) a margin of columns around
    /// them is added and all are settled like VS's liquid spreading would (ramps where a step has none, banks holding the
    /// water). Each caller brings its own scratch (<see cref="NewScratch"/>). Game-independent; any thread.
    /// </summary>
    sealed class ChunkSettler
    {
        readonly TerrainModel model;
        /// <summary>The margin of columns settled around a chunk: 8, and 20 from pool layout 6, whose rapids at a step read
        /// the settled ramps up to 6 columns away, which depend on steps up to 7 further.</summary>
        public readonly int Margin;
        /// <summary>The scratch's side: the chunk and its margin on both sides.</summary>
        public readonly int Size;

        public ChunkSettler(TerrainModel model)
        {
            this.model = model;
            Margin = model.Rivers?.PoolLayout >= 6 ? 20 : 8;
            Size = 32 + 2 * Margin;
        }

        public TerrainModel.Column[] NewScratch() => new TerrainModel.Column[Size * Size];

        /// <summary>Where the chunk's column (lx, lz) is in the scratch.</summary>
        public int Own(int lx, int lz) => (lz + Margin) * Size + lx + Margin;

        /// <summary>
        /// Chunk (<paramref name="cx"/>, <paramref name="cz"/>)'s columns into <paramref name="ext"/>, settled. Its own columns
        /// through the column store (ColumnStore.At: on a generation thread the chunk built whole, for the later passes; a query
        /// thread reads them from there or from the model), the margin and the lake check's ring beside it ColumnStore.Near (a
        /// neighbour's when stored, else the model's). The same columns as the model's (the store's premise), so the river
        /// query reads what the terrain pass read.
        /// </summary>
        public void Settle(int cx, int cz, TerrainModel.Column[] ext)
        {
            bool nearRiver = false;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    var c = ColumnStore.At(model, cx * 32 + lx, cz * 32 + lz);
                    nearRiver |= c.NearRiver || c.River;
                    ext[(lz + Margin) * Size + lx + Margin] = c;
                }
            }
            // pool layout 5: lake water meeting water at another level (a river in the next chunk above or below it, another
            // lake) gets its sill, edge sources and ramps here, where it is generated (a neighbouring river chunk settles it
            // too, but only in its margin, which it throws away)
            if (!nearRiver && model.Rivers?.PoolLayout >= 5) nearRiver = WaterLevelsMeet(ext, cx, cz);
            if (!nearRiver) return;
            for (int ez = 0; ez < Size; ez++)
            {
                for (int ex = 0; ex < Size; ex++)
                {
                    if (ex >= Margin && ex < Margin + 32 && ez >= Margin && ez < Margin + 32) continue;
                    ext[ez * Size + ex] = ColumnStore.Near(model, cx * 32 + ex - Margin, cz * 32 + ez - Margin);

                }
            }
            TerrainModel.SettleSteps(ext, Size, Size, model.Rivers?.PoolLayout >= 1, model.Rivers?.PoolLayout >= 4, model.Rivers?.PoolLayout >= 5,
                model.Rivers?.PoolLayout >= 6, model.Rivers?.PoolLayout >= 7, model.Rivers?.PoolLayout >= 8, model.Rivers?.PoolLayout >= 10);
        }

        /// <summary>Pool layout 5: whether lake water in this chunk meets lake or river water at another level, inside the
        /// chunk or right outside its border (the chunk's own columns are in ext's middle; only the ring of columns just
        /// outside is sampled, beside lake water on the border).</summary>
        bool WaterLevelsMeet(TerrainModel.Column[] ext, int cx, int cz)
        {
            static bool LakeWater(in TerrainModel.Column c) => (c.Water == TerrainModel.WaterLake || c.Water == TerrainModel.WaterSaltLake) && c.WaterTop >= 0;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    ref var c = ref ext[(lz + Margin) * Size + lx + Margin];
                    if (!LakeWater(c)) continue;
                    if (lx < 31 && LakeWater(ext[(lz + Margin) * Size + lx + 1 + Margin]) && ext[(lz + Margin) * Size + lx + 1 + Margin].WaterTop != c.WaterTop) return true;
                    if (lz < 31 && LakeWater(ext[(lz + 1 + Margin) * Size + lx + Margin]) && ext[(lz + 1 + Margin) * Size + lx + Margin].WaterTop != c.WaterTop) return true;
                }
            }
            for (int i = 0; i < 32; i++)
            {
                for (int side = 0; side < 4; side++)
                {
                    int lx = side == 0 ? 0 : side == 1 ? 31 : i, lz = side == 2 ? 0 : side == 3 ? 31 : i;
                    ref var c = ref ext[(lz + Margin) * Size + lx + Margin];
                    if (!LakeWater(c)) continue;
                    int ox = lx + (side == 0 ? -1 : side == 1 ? 1 : 0), oz = lz + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    var o = ColumnStore.Near(model, cx * 32 + ox, cz * 32 + oz);
                    if (LakeWater(o) && o.WaterTop != c.WaterTop) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Which of a settled chunk's columns (<paramref name="ext"/>, from <see cref="Settle"/>) the surface pass draws with a
        /// rapid top source (RiverBlockPass.PlaceRamps): those whose ramp word (RampWord.TryOf, as the terrain pass hands it
        /// over) is a rapid source (RampWord.RapidSource: an edge other than a plain step's, else a rapids reach's rapid top
        /// source), on fresh water at the top (the writer's clamps to <paramref name="mapHeight"/>; PlaceRamps relabels fresh
        /// water only), in a chunk whose lakes are not generated frozen (such a chunk gets no ramps or labelled sources at
        /// all). The river query's answer (EarthMapSystem.RiverAt): "rapids" means "drawn as rapid water".
        /// </summary>
        public bool[] DrawnRapidSources(int cx, int cz, TerrainModel.Column[] ext, int mapHeight)
        {
            var rapid = new bool[32 * 32];
            bool calmLabels = model.Rivers?.PoolLayout >= 11, lakeTop = false;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    ref var c = ref ext[Own(lx, lz)];
                    int top = Math.Clamp(c.Top, 1, mapHeight - 2), waterTop = c.WaterTop >= mapHeight - 1 ? mapHeight - 2 : c.WaterTop;
                    bool freshTop = waterTop > top && c.Water == TerrainModel.WaterLake;
                    lakeTop |= freshTop;
                    rapid[lz * 32 + lx] = freshTop && RampWord.TryOf(c, calmLabels, out uint word) && new RampWord(word).RapidSource;
                }
            }
            if (lakeTop && LakesFreeze(cx, cz)) Array.Clear(rapid);
            return rapid;
        }

        /// <summary>Lakes are generated frozen where the yearly mean at the chunk's centre is below -12 °C (vanilla: -15 °C at
        /// sea level); such a chunk gets no river ramps or labelled sources.</summary>
        public bool LakesFreeze(int cx, int cz)
        {
            var g = model.TerrainGeoAt(cx * 32 + 16, cz * 32 + 16);
            model.ClimateAt(g.Lat, g.Lon, out double t, out _, out _);
            return t < -12;
        }
    }
}
