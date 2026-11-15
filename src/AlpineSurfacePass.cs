using System;
using EarthMap.Core;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// The surface pass's columns, after vanilla's block layers in the terrain pass (and after the river ramps,
    /// <see cref="RiverBlockPass"/>): each surface column by what it is (<see cref="Column"/>). Vanilla covers every slope with
    /// soil and grass; on real terrain that makes the high mountains green to the top, so steep slopes, slopes above the tree
    /// line and the nival zone lose their soil down to the rock; dry river beds and beaches get their sand or gravel, mountain
    /// glaciers their ice.
    /// </summary>
    sealed class AlpineSurfacePass
    {
        readonly ICoreServerAPI sapi;
        readonly TerrainModel model;
        readonly EarthParams p;
        readonly WorldGenBlocks blocks;
        readonly GroundData iceSheets;
        readonly TerrainHandoff handoff;
        /// <summary>For beaches the ground-columns prototype has laid already.</summary>
        EarthGeology geology;

        public AlpineSurfacePass(ICoreServerAPI sapi, TerrainModel model, EarthParams p, WorldGenBlocks blocks, GroundData iceSheets, TerrainHandoff handoff)
        {
            this.sapi = sapi;
            this.model = model;
            this.p = p;
            this.blocks = blocks;
            this.iceSheets = iceSheets;
            this.handoff = handoff;
        }

        /// <summary>What the surface pass reads once per chunk column (taking the terrain pass's flags for it).</summary>
        public SurfaceChunk Begin(IChunkColumnGenerateRequest request) => new SurfaceChunk(this, request);

        /// <summary>Every surface column of the chunk column.</summary>
        public void Columns(SurfaceChunk s)
        {
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++) Column(s, lx, lz);
            }
        }

        /// <summary>What the surface pass reads once per chunk column.</summary>
        public sealed class SurfaceChunk
        {
            public readonly IServerChunk[] Chunks;
            public readonly ushort[] Heights, Rain;
            public readonly int[] TopRock;
            public readonly int Cx, Cz, Sea, MapHeight;
            /// <summary>The climate at the chunk's centre.</summary>
            public readonly EarthData.ClimateSample Climate;
            /// <summary>Sand on beaches, gravel where the coast is cold (yearly mean below 0 °C).</summary>
            public readonly bool ColdCoast;
            /// <summary>Altitude zones by the real height of each surface block (decision 0031); older worlds keep the rules
            /// of Column's end.</summary>
            public readonly bool Zones;
            /// <summary>Mountain glaciers from RGI 6 (decision 0031).</summary>
            public readonly bool Glaciers;
            /// <summary>Wet mountains keep snow and ice to warmer summers (maritime glaciers: Jostedalsbreen, the coast ranges).</summary>
            public readonly double SnowLine;
            /// <summary>Metres per block across the chunk (for the real slope).</summary>
            public readonly double MetresX, MetresZ;
            /// <summary>Alpine surface rules 2: thinner turf on the ground of the last ice sheets.</summary>
            public readonly double Scoured;
            /// <summary>The terrain pass's flags per column (TerrainHandoff); null when Earth's terrain writer did not make this chunk
            /// (EarthWorldGen refuses to go on then).</summary>
            readonly byte[] flags;
            readonly TerrainModel model;

            public SurfaceChunk(AlpineSurfacePass pass, IChunkColumnGenerateRequest request)
            {
                model = pass.model;
                Chunks = request.Chunks;
                var mc = Chunks[0].MapChunk;
                Heights = mc.WorldGenTerrainHeightMap;
                Rain = mc.RainHeightMap;
                TopRock = mc.TopRockIdMap;
                Cx = request.ChunkX * 32;
                Cz = request.ChunkZ * 32;
                Sea = model.SeaLevel;
                MapHeight = pass.sapi.WorldManager.MapSizeY;
                var g = model.TerrainGeoAt(Cx + 16, Cz + 16);
                Climate = model.Data.SampleClimate(g.Lat, g.Lon);
                flags = pass.handoff.TakeFlags(request.ChunkX, request.ChunkZ);
                ColdCoast = Climate.TMean < 0;
                // decision 0075: vanilla biomes draw no altitude zones (turf, fell-field, scree); bare rock on steep and
                // cold ground and snow in the nival zone stay (the terrain's rock exposure, below)
                Zones = model.LocalAlpineEcology && !pass.p.PhysicalRockExposure && !model.VanillaBiomes;
                SnowLine = 0.5 + 2.5 * Geo.SmoothStep((Climate.Rain - 110) / 90);
                MetresX = Math.Max(1, OreScale.MetresPerDegree * Math.Cos(g.Lat * Math.PI / 180) / model.Geometry.BlocksPerDegreeX);
                MetresZ = OreScale.MetresPerDegree / model.Geometry.BlocksPerDegreeZ;
                Glaciers = pass.p.Glaciers >= 1 && model.Data.HasGlaciers && pass.blocks.GlacierIce > 0;
                Scoured = Zones && AlpineSurfaceFacts.IceSheetGround(model.AlpineSurfaceRules) && !g.InGutter && !g.BeyondPole
                    ? MountainEcology.IceSheetGround(pass.iceSheets, g.Lat, g.Lon, pass.p.NoiseSeed) : 0;
            }

            /// <summary>The visible surface: water counts as its surface (a shore next to a deep river bed isn't a cliff).</summary>
            public int H(int lx, int lz)
            {
                if (lx >= 0 && lx < 32 && lz >= 0 && lz < 32)
                {
                    int r = Rain[lz * 32 + lx], t = Heights[lz * 32 + lx];
                    return r < MapHeight ? Math.Max(t, r) : t;
                }
                var c = ColumnStore.Near(model, Cx + lx, Cz + lz);
                return Math.Max(c.Top, c.WaterTop);
            }

            /// <summary>Whether Earth's terrain writer made this chunk (it always hands over its flags, NoFlags when none).</summary>
            public bool MadeByEarth => flags != null;

            public bool BeachAt(int i2) => flags != null && flags.Length > 0 && (flags[i2] & TerrainHandoff.FlagBeach) != 0;

            /// <summary>A dry river bed (arid river rules): as the terrain pass recorded it, else from the model's column (the
            /// column store's, its chunk built whole: the later passes read the same chunk).</summary>
            public bool DryRiverAt(int lx, int lz) => flags != null ? flags.Length > 0 && (flags[lz * 32 + lx] & TerrainHandoff.FlagDryRiver) != 0
                : ColumnStore.At(model, Cx + lx, Cz + lz).DryRiver;
        }

        /// <summary>
        /// One surface column, by what it is: a dry river bed (sand or gravel), a beach (sand, gravel on cold coasts), a mountain
        /// glacier, the altitude zones of real-altitude worlds (AlpineColumn), or else bare rock where it is steep or cold
        /// enough: steep slopes (3+ blocks drop over 2 blocks), moderately steep ones above the tree line (warmest month below
        /// 5 °C) and everything in the nival zone (warmest month below 1 °C, with snow on gentle ground). Trees and shrubs then
        /// skip them; vanilla snow still covers them where it's cold.
        /// </summary>
        void Column(SurfaceChunk s, int lx, int lz)
        {
            var chunks = s.Chunks;
            int i2 = lz * 32 + lx;
            int h = s.Heights[i2];
            if(model.Rules.AridRivers && h>0 && h<s.MapHeight-2 && s.Rain[i2]==h && s.DryRiverAt(lx,lz))
            {
                // gravel on a quarter of the bed: (x ^ z) % 4 is 0 exactly where x and z agree mod 4, every fourth diagonal;
                // with GenFixRules a position hash scatters it (CR-G5)
                bool gravel = model.Rules.ScatteredBedGravel ? TerrainColumns.Hash(s.Cx + lx, s.Cz + lz) % 4 == 0 : ((s.Cx + lx) ^ (s.Cz + lz)) % 4 == 0;
                MakeBeach(chunks,lx,lz,h,s.TopRock[i2],gravel);
                return;
            }
            if (s.BeachAt(i2) && s.Rain[i2] == h && h > 0 && h < s.MapHeight - 2)
            {
                // the ground-columns prototype has laid this beach's sand already
                if ((geology ??= sapi.ModLoader.GetModSystem<EarthGeology>())?.GroundCovers(s.Cx + lx, s.Cz + lz) == true)
                {
                    // (decision 0069 rule 7: no tall grass on a beach's sand)
                    if (ShoreZoneFacts.Zones(model.ShoreZoneRules)) ClearPlantAbove(chunks, lx, lz, h);
                    return;
                }
                MakeBeach(chunks, lx, lz, h, s.TopRock[i2], s.ColdCoast);
                return;
            }
            if (h < s.Sea || h >= s.MapHeight - (AlpineSurfaceFacts.Turf(model.AlpineSurfaceRules) ? 1 : 2) || s.Rain[i2] != h) return;
            double warm = s.Climate.TWarm - EarthWorldGen.LapsePerMetre * model.Vertical.Elevation(h);
            // the column's own place: its climate (surface rules 1), glaciers, loose volcanic ground, the physical slope's metres
            var localGeo = model.TerrainGeoAt(s.Cx + lx, s.Cz + lz);
            double localRain = s.Climate.Rain;
            if (s.Zones && AlpineSurfaceFacts.Turf(model.AlpineSurfaceRules))
            {
                var localClimate = model.Data.SampleClimate(localGeo.Lat, localGeo.Lon);
                warm = localClimate.TWarm - EarthWorldGen.LapsePerMetre * model.Vertical.Elevation(h);
                localRain = localClimate.Rain;
            }
            // the steepest drop in blocks per block across the column, measured once and only where it is read
            double grad = double.NaN;
            double Gradient() => double.IsNaN(grad) ? grad = Math.Max(Math.Abs(s.H(lx + 1, lz) - s.H(lx - 1, lz)), Math.Abs(s.H(lx, lz + 1) - s.H(lx, lz - 1))) / 2.0 : grad;
            if (s.Glaciers)
            {
                double cover = localGeo.InGutter || localGeo.BeyondPole ? 0 : model.Data.GlacierCoverage(localGeo.Lat, localGeo.Lon);
                if (cover >= 0.5 && Glacier(chunks, lx, lz, h, cover, Gradient())) return;
            }
            if (s.Zones)
            {
                double slope = Gradient();
                // the real rise over run. Surface rules 1: the physical slope (decision 0032). Before them, below the tree line
                // only: the curve stretches low ground (6 m per block near the sea against ~100 m across), and a block slope
                // alone bared every island's flanks
                double real = double.PositiveInfinity;
                if (AlpineSurfaceFacts.Turf(model.AlpineSurfaceRules))
                {
                    real = MountainEcology.Slope(model.Vertical.Elevation(s.H(lx-2,lz)), model.Vertical.Elevation(s.H(lx+2,lz)),
                        model.Vertical.Elevation(s.H(lx,lz-2)), model.Vertical.Elevation(s.H(lx,lz+2)), s.MetresX, s.MetresZ, 4);
                }
                else if (warm >= 10 && slope >= 1.5)
                {
                    double ex = Math.Abs(model.Vertical.Elevation(s.H(lx + 1, lz)) - model.Vertical.Elevation(s.H(lx - 1, lz))) / (2 * s.MetresX);
                    double ez = Math.Abs(model.Vertical.Elevation(s.H(lx, lz + 1)) - model.Vertical.Elevation(s.H(lx, lz - 1))) / (2 * s.MetresZ);
                    real = Math.Max(ex, ez);
                }
                double localSnowLine = AlpineSurfaceFacts.Turf(model.AlpineSurfaceRules) ? .5 + 2.5 * Geo.SmoothStep((localRain-110)/90) : s.SnowLine;
                double volcanic = MountainEcology.LooseVolcanicGround(localGeo.Lat, localGeo.Lon, model.Vertical.Elevation(h));
                AlpineColumn(chunks, s.Cx + lx, s.Cz + lz, lx, lz, h, slope, real, warm, localSnowLine, s.TopRock[i2], volcanic, localRain, s.Scoured);
                return;
            }
            // nival zone (no month clearly above freezing): permanent snow, bare rock on cliffs; above the tree
            // line: bare rock on moderate slopes; elsewhere only on steep ones
            double need = warm < 1 ? 0 : warm < 5 ? 0.5 : 1.5;
            grad = Gradient();
            if (p.PhysicalRockExposure)
            {
                // Real rise/run, independent of vertical exaggeration and horizontal world size (across: at the column's own latitude).
                double mx = Math.Max(1, OreScale.MetresPerDegree * Math.Cos(localGeo.Lat * Math.PI / 180) / model.Geometry.BlocksPerDegreeX);
                double ex = Math.Abs(model.Vertical.Elevation(s.H(lx + 1, lz)) - model.Vertical.Elevation(s.H(lx - 1, lz))) / (2 * mx);
                double ez = Math.Abs(model.Vertical.Elevation(s.H(lx, lz + 1)) - model.Vertical.Elevation(s.H(lx, lz - 1))) / (2 * s.MetresZ);
                double physical = Math.Max(ex, ez);
                need = warm < 1 ? 0 : warm < 5 ? .45 : 1;
                if (physical < need) return;
            }
            else if (grad < need) return;
            bool snow = warm < 0.5 && grad < 1.25 && blocks.Snow > 0;
            int rock = s.TopRock[i2];
            if (rock <= 0 && !snow) return;
            ClearPlantAbove(chunks, lx, lz, h);
            // snow: two blocks deep on gentle ground, one on steeper, over the rock (or its soil cover)
            StripColumn(chunks, lx, lz, h, rock, snow ? (grad < 0.6 ? 2 : 1) : 0, 0, 0);
        }

        /// <summary>The plant standing on a surface column's top block (h) goes with its soil.</summary>
        void ClearPlantAbove(IServerChunk[] chunks, int lx, int lz, int h)
        {
            var rockKind = blocks.RockKind;
            int above = h + 1;
            int ia = ((above % 32) * 32 + lz) * 32 + lx;
            var dataAbove = chunks[above / 32].Data;
            int idAbove = dataAbove.GetBlockIdUnsafe(ia);
            if (idAbove > 0 && idAbove < rockKind.Length && rockKind[idAbove] == 2) dataAbove.SetBlockUnsafe(ia, 0);
        }

        /// <summary>
        /// The top six blocks of a surface column (h its top): <paramref name="snowDepth"/> blocks of snow over its ground,
        /// then its soil cover (soil, gravel, sand) down to the rock turned into <paramref name="rock"/>, except the top block
        /// as scree (<paramref name="gravel"/>, when above 0) and the first <paramref name="keepSoil"/> blocks of soil, which
        /// stay. It stops at the first block that isn't cover.
        /// </summary>
        void StripColumn(IServerChunk[] chunks, int lx, int lz, int h, int rock, int snowDepth, int gravel, int keepSoil)
        {
            var rockKind = blocks.RockKind;
            int soil = 0;
            for (int y = h; y > h - 6 && y > 0; y--)
            {
                int idx = ((y % 32) * 32 + lz) * 32 + lx;
                var data = chunks[y / 32].Data;
                int id = data.GetBlockIdUnsafe(idx);
                bool cover = id > 0 && id < rockKind.Length && rockKind[id] == 1;
                if (h - y < snowDepth && (cover || id == rock || (id > 0 && id < rockKind.Length && rockKind[id] == 0)))
                {
                    data.SetBlockUnsafe(idx, blocks.Snow);
                    continue;
                }
                if (!cover || rock <= 0) break;
                if (gravel > 0 && y == h) { data.SetBlockUnsafe(idx, gravel); continue; }
                if (++soil <= keepSoil) continue;
                data.SetBlockUnsafe(idx, rock);
            }
        }

        /// <summary>
        /// A mountain glacier (RGI 6 outline, decision 0031) on a surface column: its top blocks become glacier ice, 2 to 5 deep
        /// with the glacier's share of the cell (2 on steep ground), and the plant on it goes. ETOPO's ground over a glacier is the
        /// ice surface, so the ice takes the place of the ground's top. Not on an ice sheet's column (already ice). False when
        /// nothing was changed.
        /// </summary>
        bool Glacier(IServerChunk[] chunks, int lx, int lz, int h, double cover, double slope)
        {
            int top = ((h % 32) * 32 + lz) * 32 + lx;
            if (chunks[h / 32].Data.GetBlockIdUnsafe(top) == blocks.GlacierIce) return false;
            ClearPlantAbove(chunks, lx, lz, h);
            int depth = slope >= 2 ? 2 : 2 + (int)Math.Round(3 * Geo.SmoothStep((cover - 0.5) / 0.5));
            for (int y = h; y > h - depth && y > 0; y--)
                chunks[y / 32].Data.SetBlockUnsafe(((y % 32) * 32 + lz) * 32 + lx, blocks.GlacierIce);
            return true;
        }

        /// <summary>
        /// Altitude zones (decision 0031, worlds with real-altitude ecology), by the warmest month at the real elevation this
        /// surface block stands for: nival (below the snow line, 0.5 °C, up to 3 °C where it is wet): snow on gentle ground,
        /// bare rock on steeper; fell-field (below 3 °C): scree (the rock's gravel) on gentle ground with a few patches of
        /// thin soil, bare rock on slopes; alpine meadow (below 6 °C): one block of soil, rock from moderate slopes; tree line
        /// ecotone (below 10 °C): two blocks of soil, rock on steep slopes; below that vanilla's soil, rock on cliffs. Below the
        /// tree line a slope needs to be steep in reality as well (rise over run 0.6).
        /// Surface rules 1 instead use MountainEcology's local climate, physical slopes and loose volcanic ground (0032); rules 2
        /// (MountainEcology.Surface2) keep closed turf from the tree line to about 6 °C, thin it towards the snow line and in dry
        /// mountains (<paramref name="rain"/>: Earth's ambient rain) and on ground the last ice sheets covered (<paramref name="scoured"/>:
        /// MountainEcology.IceSheetGround), and shape its patches with smooth noise.
        /// </summary>
        void AlpineColumn(IServerChunk[] chunks, int x, int z, int lx, int lz, int h, double slope, double realSlope, double warm, double snowLine, int rock, double volcanic, double rain, double scoured)
        {
            bool snow = false, scree = false, strip = false;
            int keep = int.MaxValue;
            if (AlpineSurfaceFacts.Turf(model.AlpineSurfaceRules))
            {
                var cover = AlpineSurfaceFacts.IceSheetGround(model.AlpineSurfaceRules)
                    ? MountainEcology.Surface2(warm, snowLine, realSlope, volcanic, MountainEcology.TurfPatch(x, z, p.NoiseSeed), slope, rain, scoured)
                    : MountainEcology.Surface(warm, snowLine, realSlope, volcanic, GeologyTransitions.Hash(p.NoiseSeed, x >> 2, 0, z >> 2, 31), slope);
                snow = cover == MountainEcology.Cover.Snow && blocks.Snow > 0;
                scree = cover == MountainEcology.Cover.Scree;
                strip = cover == MountainEcology.Cover.Rock || cover == MountainEcology.Cover.Snow && !snow;
                if (cover == MountainEcology.Cover.Soil) { if (warm >= 13) return; keep = warm < 10 ? 1 : 2; }
            }
            else if (warm < snowLine) { if (slope < 1.25 && blocks.Snow > 0) snow = true; else strip = true; }
            else if (warm < 3)
            {
                // soil pockets in 4-block patches, more of them towards the meadow
                bool pocket = GeologyTransitions.Hash(p.NoiseSeed, x >> 2, 0, z >> 2, 31) < 0.1 + 0.1 * (warm - snowLine) / Math.Max(0.5, 3 - snowLine);
                if (slope >= 1) strip = true;
                else if (pocket) keep = 1;
                else scree = true;
            }
            else if (warm < 6) { if (slope >= 0.5) strip = true; else keep = 1; }
            else if (warm < 10) { if (slope >= 1) strip = true; else keep = 2; }
            else if (slope >= 1.5 && realSlope >= 0.6) strip = true;
            else return;
            if (rock <= 0 && !snow) return;
            // the plant on top goes with the soil
            if (snow || scree || strip) ClearPlantAbove(chunks, lx, lz, h);
            var gravelOf = blocks.GravelOf;
            int gravel = rock > 0 && rock < gravelOf.Length && gravelOf[rock] > 0 ? gravelOf[rock] : blocks.DefaultGravel;
            // snow: two blocks deep on gentle ground, one on steeper; scree: the top block the rock's gravel; soil kept: keep
            // blocks of it where neither snow, scree nor bare rock is wanted
            StripColumn(chunks, lx, lz, h, rock, snow ? (slope < 0.6 ? 2 : 1) : 0, scree ? gravel : 0, !snow && !scree && !strip ? keep : 0);
        }

        void MakeBeach(IServerChunk[] chunks, int lx, int lz, int h, int rock, bool cold)
        {
            var rockKind = blocks.RockKind;
            var gravelOf = blocks.GravelOf;
            var sandOf = blocks.SandOf;
            int cover = cold ? (rock > 0 && rock < gravelOf.Length ? gravelOf[rock] : 0) : (rock > 0 && rock < sandOf.Length ? sandOf[rock] : 0);
            if (cover == 0) cover = cold ? blocks.DefaultGravel : blocks.DefaultSand;
            if (cover == 0) return;
            ClearPlantAbove(chunks, lx, lz, h);
            for (int y = h; y > h - 3 && y > 0; y--)
            {
                int idx = ((y % 32) * 32 + lz) * 32 + lx;
                var data = chunks[y / 32].Data;
                int id = data.GetBlockIdUnsafe(idx);
                // replace the soil cover, and the rock itself right at the surface
                if (id <= 0 || id >= rockKind.Length || (rockKind[id] != 1 && !(y == h && id == rock))) break;
                data.SetBlockUnsafe(idx, cover);
            }
        }
    }
}
