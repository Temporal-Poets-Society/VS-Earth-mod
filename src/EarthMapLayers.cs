using System;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Earth's map regions: the climate map gets the real temperature and rainfall (so soil, grass, trees and crops follow the
    /// real climate), the ocean map the real sea (both as layers wrapped around vanilla's GenMaps, installed once per server
    /// start), and the forest and shrub maps the real climate's potentials (each new map region).
    /// </summary>
    static class RegionMaps
    {
        /// <summary>Wraps vanilla's climate layer and replaces its ocean layer (once: a layer already Earth's is kept).</summary>
        internal static void Install(ICoreServerAPI sapi, TerrainModel model, ILogger logger)
        {
            var genMaps = sapi.ModLoader.GetModSystem<GenMaps>();
            if (genMaps?.climateGen == null)
            {
                logger.Warning("Vanilla GenMaps not found; Earth climate not applied.");
                return;
            }
            var wc = sapi.WorldManager.SaveGame.WorldConfiguration;
            float tempMul = wc.GetString("globalTemperature", "1").ToFloat(1);
            float rainMul = wc.GetString("globalPrecipitation", "1").ToFloat(1);
            int regionSize = sapi.WorldManager.RegionSize;
            if (!(genMaps.climateGen is EarthClimateLayer))
            {
                genMaps.climateGen = new EarthClimateLayer(sapi.WorldManager.Seed + 4242, genMaps.climateGen, model, (double)regionSize / genMaps.noiseSizeClimate, tempMul, rainMul);
            }
            if (!(genMaps.oceanGen is EarthOceanLayer))
            {
                genMaps.oceanGen = new EarthOceanLayer(sapi.WorldManager.Seed + 4343, model, (double)regionSize / genMaps.noiseSizeOcean);
            }
        }

        /// <summary>
        /// Forests where the real climate grows them: vanilla's forest map (noise, already reduced where it's dry or cold)
        /// is scaled by the forest potential from the real climate (warmest month above ~10 °C = below the tree line,
        /// and moist enough), so the Amazon and the taiga are wooded and the steppe and tundra are open.
        /// </summary>
        internal static void ForestAndShrubs(ICoreServerAPI sapi, TerrainModel model, IMapRegion region, int regionX, int regionZ)
        {
            var map = region.ForestMap;
            if (map?.Data == null || map.Data.Length == 0) return;
            int inner = map.InnerSize;
            double bpp = (double)sapi.WorldManager.RegionSize / inner;
            for (int j = 0; j < map.Size; j++)
            {
                for (int i = 0; i < map.Size; i++)
                {
                    double x = (regionX * inner + i - map.TopLeftPadding) * bpp, z = (regionZ * inner + j - map.TopLeftPadding) * bpp;
                    double potential = model.ForestPotential(x, z);
                    int k = j * map.Size + i;
                    map.Data[k] = (int)Math.Clamp(potential * (110 + 0.6 * map.Data[k]), 0, 255);
                }
            }
            // shrubs likewise: none on desert sand, few in the steppe at the edge of a river's green belt
            var shrubs = region.ShrubMap;
            if (shrubs?.Data == null || shrubs.Data.Length == 0) return;
            inner = shrubs.InnerSize;
            bpp = (double)sapi.WorldManager.RegionSize / inner;
            for (int j = 0; j < shrubs.Size; j++)
            {
                for (int i = 0; i < shrubs.Size; i++)
                {
                    double x = (regionX * inner + i - shrubs.TopLeftPadding) * bpp, z = (regionZ * inner + j - shrubs.TopLeftPadding) * bpp;
                    int k = j * shrubs.Size + i;
                    shrubs.Data[k] = (int)Math.Clamp(model.ShrubPotential(x, z) * (40 + 0.9 * shrubs.Data[k]), 0, 255);
                }
            }
        }
    }

    /// <summary>The climate map: real temperature and rainfall; geologic activity kept from the wrapped (vanilla) layer.</summary>
    public class EarthClimateLayer : MapLayerBase
    {
        readonly MapLayerBase inner;
        readonly TerrainModel model;
        readonly double blocksPerPixel;
        readonly float tempMul, rainMul;

        public EarthClimateLayer(long seed, MapLayerBase inner, TerrainModel model, double blocksPerPixel, float tempMul, float rainMul) : base(seed)
        {
            this.inner = inner;
            this.model = model;
            this.blocksPerPixel = blocksPerPixel;
            this.tempMul = tempMul;
            this.rainMul = rainMul;
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ)
        {
            int[] result = inner.GenLayer(xCoord, zCoord, sizeX, sizeZ);
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int i = z * sizeX + x;
                    result[i] = model.ClimatePixel((xCoord + x) * blocksPerPixel, (zCoord + z) * blocksPerPixel, result[i] & 0xFF, tempMul, rainMul);
                }
            }
            return result;
        }
    }

    /// <summary>The ocean map (oceanicity 0..255): the real sea, and the seam sea.</summary>
    public class EarthOceanLayer : MapLayerBase
    {
        readonly TerrainModel model;
        readonly double blocksPerPixel;

        public EarthOceanLayer(long seed, TerrainModel model, double blocksPerPixel) : base(seed)
        {
            this.model = model;
            this.blocksPerPixel = blocksPerPixel;
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ)
        {
            var result = new int[sizeX * sizeZ];
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    var g = model.TerrainGeoAt((xCoord + x) * blocksPerPixel, (zCoord + z) * blocksPerPixel);
                    // the gutter is open sea, except where it carries Antarctica across (then the seam point's own value)
                    double o = g.InGutter && !model.GutterBridgedAt((zCoord + z) * blocksPerPixel, g) ? 1 : model.Data.SampleTerrain(g.Lat, g.Lon).Ocean;
                    // islands from their outlines are land the grid doesn't have
                    if (o > 0 && !g.InGutter && model.Islands != null && model.Islands.IsLand(g.Lat, g.Lon)) o = 0;
                    // near the big land's coasts the outline decides (worlds with Coasts)
                    else if (!g.InGutter && model.CoastLandAt(g.Lat, g.Lon, out bool land)) o = land ? 0 : Math.Max(o, 0.5);
                    result[z * sizeX + x] = (int)Math.Round(255 * o);
                }
            }
            return result;
        }
    }
}
