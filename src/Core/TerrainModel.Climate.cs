using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: the climate map: surface, climate pixel, corrections and the rivers' green belts.
    public sealed partial class TerrainModel
    {
        // ------------------------------------------------------------------------------------------ climate

        /// <summary>Surface for climate purposes (no detail noise): block height, elevation (m), whether it is water.</summary>
        public void Surface(double lat, double lon, out double y, out double elev, out bool water)
        {
            var s = Data.SampleTerrain(lat, lon);
            if (s.Ocean >= 0.5)
            {
                water = true;
                elev = 0;
                y = SeaLevel;
                return;
            }
            if (s.Lake >= 0.5 && !double.IsNaN(s.LakeLevel))
            {
                water = true;
                elev = s.LakeLevel;
                y = WaterTopY(s.LakeLevel) + 1;
                return;
            }
            water = false;
            elev = s.Elev;
            y = Math.Max(Vertical.Y(elev), double.IsNaN(s.ShoreLevel) ? double.MinValue : WaterTopY(s.ShoreLevel) + 1);
        }

        /// <summary>
        /// The pixel for Vintage Story's climate map: (temperature &lt;&lt; 16) | (rain &lt;&lt; 8) | geologic activity.
        /// Real surface temperature and rainfall, stored so that vanilla's own altitude adjustments (−1 °C per 6.375
        /// blocks, more rain higher up and near sea level) give back the real values at the surface.
        /// </summary>
        public int ClimatePixel(double x, double z, int geologic, float tempMul, float rainMul)
            => Pixel(x, z, geologic, tempMul, rainMul, ClimateRules < RealLapseRules);

        /// <summary>
        /// The climate pixel as <see cref="ClimatePixel"/> stores it for worlds before climate rules v2, in any world: the surface
        /// temperature plus vanilla's own 1 °C per 6.375 blocks, so that vanilla's adjustment applied at the surface gives the
        /// real surface temperature (for readers that apply vanilla's lapse themselves; tests/VerticalProfileTests; Wmap reads
        /// ClimatePixel). With
        /// climate rules v2 the climate map itself stores the sea-level temperature instead. The same as ClimatePixel in older
        /// worlds; in worlds 512+ blocks high warm highlands don't fit the byte (decision 0009).
        /// </summary>
        public int ClimatePixelForVanillaLapse(double x, double z, int geologic, float tempMul, float rainMul)
            => Pixel(x, z, geologic, tempMul, rainMul, true);

        int Pixel(double x, double z, int geologic, float tempMul, float rainMul, bool vanillaLapse)
        {
            var g = TerrainGeoAt(x, z);
            var cs = ClimateSampleAt(g.Lat, g.Lon, out double tReal, out double rainReal, out double y);
            rainReal = RiparianRain(x, z, rainReal);
            // climate rules v2: the sea-level temperature, which the patched adjustment (ClimateLapse) cools by the real lapse
            // (it fits the byte at any world height); before: the surface's, plus vanilla's 1 °C per 6.375 blocks back
            double tStore = vanillaLapse ? tReal + (y - SeaLevel) / 6.375 : cs.TMean;
            // climate rules v3 (decision 0031's test profiles): the real rainfall, which the patched GetRainFall leaves as it is;
            // before: less vanilla's altitude rain, which the byte cuts at 0 on dry high ground (read back as wet)
            double rainStore = !vanillaLapse && ClimateRules >= RealRainRules ? rainReal
                : rainReal - (y - SeaLevel) / 2.0 - 5 * Math.Clamp(8 + SeaLevel - y, 0, 8);
            int temp = Math.Clamp((int)(Math.Clamp((tStore + 20) * 4.25, 0, 255) * tempMul), 0, 255);
            int rain = Math.Clamp((int)Math.Round(rainStore * rainMul), 0, 255);
            // geothermal rules 1: heat under real volcanoes (decision 0057), never enough for vanilla's random hot springs
            if (GeothermalRules >= 1) geologic = Geothermal.Activity(g.Lat, g.Lon);
            return (temp << 16) | (rain << 8) | (geologic & 0xFF);
        }

        /// <summary>
        /// What vanilla's climate reading at (x, y, z) lacks of the real temperature: vanilla gives back the column's stored
        /// value (ClimatePixel: real surface temperature plus the altitude compensation, cut to the byte's -20..40 °C) less
        /// (y - sea level) / 6.375, cut to -20..40 °C again (VS Climate.GetScaledAdjustedTemperatureFloat). Where the stored
        /// value fits, this is just the real temperature's part beyond -20..40; where it doesn't (warm highlands in worlds
        /// 512+ blocks high), it is the whole difference.
        /// </summary>
        public double ClimateCorrection(double x, double z, double y)
        {
            if (ClimateRules >= RealLapseRules)
            {
                // climate rules v2: vanilla reads the stored sea-level temperature (cut to the byte's -20..40 °C) less the real
                // lapse at y (ClimateLapse), cut again to -20..40 on the server and -50..40 on the client; the same on both sides
                // once each side's own cut is undone (no terrain needed)
                var g = TerrainGeoAt(x, z);
                double seaT = Data.SampleClimate(g.Lat, g.Lon).TMean, lapse = LapseAt(y);
                return seaT - lapse - Math.Clamp(Math.Clamp(seaT, -20, 40) - lapse, VanillaFloor, 40);
            }
            if (ClientClimate || !Data.HasTerrain)
            {
                // clients load the climate only: the real temperature's part beyond the client's -50..40 °C (exact wherever the
                // stored value fits, which is everywhere in 256-384 high worlds but where the stored byte itself is cut at -20:
                // the coldest lowland ice, where the client then reads a few degrees warm)
                double t = TrueTemperature(x, z, y);
                return t - Math.Clamp(t, VanillaFloor, 40);
            }
            double stored = StoredTemperature(x, z, out double surface);
            // underground the surface's correction applies (vanilla's own rule below the surface; no extrapolated "elevation")
            double at = Math.Max(y, surface);
            double vanilla = Math.Clamp(Math.Clamp(stored, -20, 40) - (at - SeaLevel) / 6.375, -20, 40);
            return TrueTemperature(x, z, at) - vanilla;
        }

        /// <summary>The temperature ClimatePixel stores for a column before the byte cuts it, and the column's surface height
        /// (cached per thread by block column: asked on every climate query). Needs the terrain (server).</summary>
        public double StoredTemperature(double x, double z, out double surface)
        {
            int bx = (int)Math.Floor(x), bz = (int)Math.Floor(z);
            var cache = storedCache ??= new (long, int, int, double, double)[64];
            int slot = (int)((uint)(bx * 73856093 ^ bz * 19349663) & 63);
            var hit = cache[slot];
            if (hit.Item1 == CacheId && hit.Item2 == bx && hit.Item3 == bz) { surface = hit.Item5; return hit.Item4; }
            var g = TerrainGeoAt(bx + .5, bz + .5);
            ClimateAt(g.Lat, g.Lon, out double tReal, out _, out double y);
            double tStore = tReal + (y - SeaLevel) / 6.375;
            cache[slot] = (CacheId, bx, bz, tStore, y);
            surface = y;
            return tStore;
        }
        [ThreadStatic] static (long, int, int, double, double)[] storedCache;

        /// <summary>Real yearly mean surface temperature (°C), rainfall (VS 0..255) and surface block height at a place.</summary>
        public void ClimateAt(double lat, double lon, out double tReal, out double rain, out double surfaceY)
            => ClimateSampleAt(lat, lon, out tReal, out rain, out surfaceY);

        /// <summary><see cref="ClimateAt"/>, and the climate sample it came from.</summary>
        EarthData.ClimateSample ClimateSampleAt(double lat, double lon, out double tReal, out double rain, out double surfaceY)
        {
            var cs = Data.SampleClimate(lat, lon);
            Surface(lat, lon, out surfaceY, out double elev, out _);
            tReal = cs.TMean - 0.0065 * elev;
            rain = cs.Rain;
            return cs;
        }

        /// <summary>
        /// Rivers through dry land get a green belt: the climate map's rainfall is raised near them (an oasis strip along
        /// the Nile, the Euphrates, the Indus), easing out through steppe into the desert. So do the shores of the terminal
        /// lakes that have one (the revived Aral Sea's reed shores and deltas, Lake Chad; EarthData.TerminalShoreWeight).
        /// Only where the land is drier than VS rainfall 140.
        /// </summary>
        public double RiparianRain(double x, double z, double rain) => rain + (145 - rain) * RiparianWeight(x, z, rain);

        /// <summary>0..1: how much of a river's or a terminal lake's green belt this dry column is in.</summary>
        public double RiparianWeight(double x, double z, double rain)
        {
            if (rain >= 140) return 0;
            double shore = 0;
            if (Data.LakeOverlay)
            {
                var g = TerrainGeoAt(x, z);
                shore = Data.TerminalShoreWeight(g.Lat, g.Lon);
            }
            if (Rivers == null) return shore;
            ProjectTerrain(x, z, out double sourceX, out double sourceZ);
            if (!Rivers.Nearest(sourceX, sourceZ, out var hit)) return shore;
            if (AridRiverRules>=1 && WetSourceFlow!=null && hit.Reach<WetSourceFlow.Length)
            {
                var g=TerrainGeoAt(x,z);var cs=Data.SampleClimate(g.Lat,g.Lon);
                if(AridHydrology.Dry(cs.Rain,cs.TWarm-.0065*Data.ElevBilinear(g.Lat,g.Lon),hit.Q,WetSourceFlow[hit.Reach])
                    && !MaskKeepsWet(in hit)) return shore;
            }
            // (channel rules 1: the channel's width as the water mask narrows it; the flow's before, and where it doesn't)
            double hw = hit.HalfWidth;
            double belt = 20 + 30 * Rivers.StreamT(hit.Q) + 6 * hw;
            return Math.Max(shore, 1 - Geo.SmoothStep((hit.Dist - hw - 8) / belt));
        }
    }
}
