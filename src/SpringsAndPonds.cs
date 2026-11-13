using System;
using System.Collections.Generic;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Vanilla's water features in Earth worlds: the known hot springs marked for vanilla's hot spring generator (a terrain
    /// pass handler), and the guards on vanilla's ponds, springs and mountain-side streams (patches bound by PatchVanilla):
    /// none from frozen rock or a volcano's crater, ponds only in the mapped wetlands (pond rules), springs in cave walls only
    /// (springs rules). Each lets vanilla be while Earth is inactive.
    /// </summary>
    static class SpringsAndPonds
    {
        /// <summary>The cave radius a known hot spring is marked with: vanilla's GenHotSprings makes a pool of sludgy gravel and
        /// bacterial mats twice as wide (16 blocks across) with boiling water in its middle few blocks.</summary>
        const double KnownSpringRadius = 4;

        /// <summary>
        /// Geothermal rules 1: the only hot springs are the known ones (Core.Geothermal: OpenStreetMap's and GeoNames' hot
        /// springs and geysers). Each is marked in its chunk for vanilla's hot spring generator, as vanilla's caves mark theirs
        /// (map chunk moddata "hotspringlocations", read by GenHotSprings in the next pass); one that would overlap a pool
        /// already marked in the chunk is left out (at coarse scales a chunk holds whole spring fields). Vanilla still decides
        /// whether the ground takes one: dry land above sea level, less than 4 blocks of relief across the pool, no structure
        /// there. The place is the site's own (Geometry.FromGeo), not moved with a companion's expanded landscape.
        /// </summary>
        internal static void KnownHotSprings(TerrainModel model, IChunkColumnGenerateRequest request)
        {
            if (model == null || model.GeothermalRules < 1) return;
            int x0 = request.ChunkX * 32, z0 = request.ChunkZ * 32;
            var geo = model.Geometry;
            double latA = geo.ToGeo(x0, z0).Lat, latB = geo.ToGeo(x0, z0 + 32).Lat;
            var mc = request.Chunks[0].MapChunk;
            Dictionary<Vec3i, HotSpringGenData> springs = null;
            int apart = (int)(4 * KnownSpringRadius);
            foreach (var s in Geothermal.SpringsBetween(Math.Min(latA, latB), Math.Max(latA, latB)))
            {
                geo.FromGeo(s.Lat, s.Lon, out double x, out double z);
                int lx = (int)Math.Floor(x) - x0, lz = (int)Math.Floor(z) - z0;
                if (lx < 0 || lx >= 32 || lz < 0 || lz >= 32) continue;
                springs ??= mc.GetModdata<Dictionary<Vec3i, HotSpringGenData>>("hotspringlocations") ?? new();
                bool overlaps = false;
                foreach (var p in springs.Keys) overlaps |= (p.X - lx) * (p.X - lx) + (p.Z - lz) * (p.Z - lz) < apart * apart;
                if (!overlaps) springs[new Vec3i(lx, mc.WorldGenTerrainHeightMap[lz * 32 + lx], lz)] = new HotSpringGenData { horRadius = KnownSpringRadius };
            }
            if (springs != null && springs.Count > 0) mc.SetModdata("hotspringlocations", springs);
        }

        /// <summary>
        /// Vanilla digs small ponds into the surface; none on the ice sheets or in the permanent snow (frozen ponds on top
        /// of Greenland's ice), nor in rivers, nor in a volcano's crater (geothermal rules 2: HotGround). With pond rules 1
        /// a surface pond only where the mapped wetlands keep it (WetlandGrid.PondShare): elsewhere the hollows it would
        /// fill are the terrain's noise, not real ponds. Vanilla's ponds in caves start below the surface and stay.
        /// </summary>
        internal static bool PondGuard(int dx, int pondYPos, int dz, int chunkX, int chunkZ)
        {
            var w = WorldGenerationContext.Current;
            if (w == null) return true;
            var model = w.Model;
            double x = chunkX * 32 + dx, z = chunkZ * 32 + dz;
            if (model.IntersectsReservedTerrain(x - 16, z - 16, x + 16, z + 16)) return false;
            if (HotGround(chunkX * 32 + dx, chunkZ * 32 + dz)) return false;
            var c = ColumnStore.Near(model, x, z);
            if (c.IceBottom <= c.Top || c.River) return false;
            // vanilla's surface tries start a block above the rain height (Top + 1 on bare ground), its cave tries below Top
            if (model.PondRules >= 1)
            {
                if (pondYPos < c.Top) System.Threading.Interlocked.Increment(ref pondsInCaves);
                else if (model.PondRules >= EarthParams.PondRulesNone)
                {
                    // surface ponds off (the Earth tab's default)
                    System.Threading.Interlocked.Increment(ref pondsOutsideWetlands);
                    return false;
                }
                else
                {
                    var g0 = model.TerrainGeoAt(x, z);
                    double share = model.Wetlands?.PondShare(g0.Lat, g0.Lon) ?? 0;
                    if (!WetlandGrid.KeepsPond(share, w.Params.NoiseSeed, chunkX * 32 + dx, chunkZ * 32 + dz))
                    {
                        System.Threading.Interlocked.Increment(ref pondsOutsideWetlands);
                        return false;
                    }
                    System.Threading.Interlocked.Increment(ref pondsInWetlands);
                }
            }
            var g = model.TerrainGeoAt(x, z);
            double warm = model.Data.SampleClimate(g.Lat, g.Lon).TWarm - EarthWorldGen.LapsePerMetre * model.Vertical.Elevation(pondYPos);
            return warm >= 0.5;
        }

        static long pondsOutsideWetlands, pondsInWetlands, pondsInCaves;
        /// <summary>Vanilla's pond tries the pond rules saw since the server started (for the self-test): each try that reached
        /// the wetland check, not each pond placed (vanilla still needs a closed hollow).</summary>
        public static string PondReport() => string.Format("surface pond tries turned away (outside the mapped wetlands, or all with surface ponds off): {0}, " +
            "let through in the wetlands: {1}; cave pond tries left to vanilla: {2}", System.Threading.Interlocked.Read(ref pondsOutsideWetlands),
            System.Threading.Interlocked.Read(ref pondsInWetlands), System.Threading.Interlocked.Read(ref pondsInCaves));

        /// <summary>The random generator of vanilla's rivulets (GenRivulets.rnd, not public), bound by PatchVanilla with the
        /// spring guards (the guards replay its draws).</summary>
        internal static AccessTools.FieldRef<GenRivulets, LCGRandom> RivuletRandom;

        /// <summary>
        /// No water coming out of rock that is frozen all year (TerrainModel.FrozenAllYear). Vanilla only asks for -15 °C
        /// at sea level, which puts springs and rapids into Antarctica's and the Himalaya's cliffs.
        /// </summary>
        static bool FrozenRock(TerrainModel model, int x, int y, int z) => model.FrozenAllYear(x + .5, z + .5, y);

        /// <summary>A volcano's crater or its rim (EarthVolcanoes.InCrater, geothermal rules 2): hot ground over open lava,
        /// without ponds or springs.</summary>
        static bool HotGround(int x, int z) => EarthVolcanoes.Instance?.InCrater(x + .5, z + .5) == true;

        /// <summary>
        /// Vanilla's springs in cliff faces and cave walls (GenRivulets.tryGenRivulet): the spot comes from the generator's
        /// random numbers, so the same draws are made here first; a frozen spot, or one in a volcano's crater (HotGround), is
        /// skipped with those draws used up.
        /// Lava springs: none from geothermal rules 1 (the author: lava "spawns out of mountain side or randomly inside
        /// rock"); open lava is for real volcanoes. Vanilla makes them last in each chunk, after the water springs, so skipping
        /// them without their draws moves no water spring. Older worlds keep them.
        /// </summary>
        internal static bool SpringGuard(GenRivulets __instance, [HarmonyArgument(0)] IServerChunk[] chunks, [HarmonyArgument(1)] int chunkX,
            [HarmonyArgument(2)] int chunkZ, [HarmonyArgument(4)] bool lava)
        {
            var w = WorldGenerationContext.Current;
            if (w == null) return true;
            if (lava) return w.Model.GeothermalRules < 1;
            var rnd = RivuletRandom(__instance);
            int mapHeight = w.MapHeight;
            int low = (int)(TerraGenConfig.seaLevel * 1.1f), high = mapHeight - low;
            long seed = rnd.currentSeed;
            // vanilla's draws, in its order
            int lx = 1 + rnd.NextInt(30);
            int y = Math.Min(1 + rnd.NextInt(low) + rnd.NextInt(high) * rnd.NextInt(high), mapHeight - 2);
            int lz = 1 + rnd.NextInt(30);
            if (FrozenRock(w.Model, chunkX * 32 + lx, y, chunkZ * 32 + lz) || HotGround(chunkX * 32 + lx, chunkZ * 32 + lz)) return false;
            // springs rules caves and streams: only in cave walls (at least 2 blocks under the ground of the column and its
            // four neighbours, so not in an open cliff face, whose open side's column is lower than the spring); streams adds
            // its own springs at the surface (EarthSprings)
            int rules = w.Params.SpringRules;
            if (rules >= EarthParams.SpringRulesCaves)
            {
                var hm = chunks[0].MapChunk.WorldGenTerrainHeightMap;
                int ground = Math.Min(Math.Min(hm[lz * 32 + lx], hm[lz * 32 + lx - 1]), Math.Min(hm[lz * 32 + lx + 1], Math.Min(hm[(lz - 1) * 32 + lx], hm[(lz + 1) * 32 + lx])));
                if (y >= ground - 2)
                {
                    System.Threading.Interlocked.Increment(ref springsAtSurface);
                    return false;
                }
            }
            rnd.currentSeed = seed;
            return true;
        }

        /// <summary>
        /// Vanilla's streams breaking out of mountain sides, 5 % of them rapids (GenRivulets.tryGenMountainSideRivers),
        /// skipped where the rock is frozen or in a crater like SpringGuard.
        /// </summary>
        internal static bool SideRiverGuard(GenRivulets __instance, [HarmonyArgument(0)] IServerChunk[] chunks, [HarmonyArgument(1)] int chunkX,
            [HarmonyArgument(2)] int chunkZ)
        {
            var w = WorldGenerationContext.Current;
            if (w == null) return true;
            // springs rules (cave springs only, or streams): no mountain-side streams
            if (w.Params.SpringRules >= EarthParams.SpringRulesCaves)
            {
                System.Threading.Interlocked.Increment(ref sideStreamsSkipped);
                return false;
            }
            var rnd = RivuletRandom(__instance);
            long seed = rnd.currentSeed;
            int lx = 4 + rnd.NextInt(24), lz = 4 + rnd.NextInt(24);
            int y = chunks[0].MapChunk.WorldGenTerrainHeightMap[lz * 32 + lx] - 2;
            if (FrozenRock(w.Model, chunkX * 32 + lx, y, chunkZ * 32 + lz) || HotGround(chunkX * 32 + lx, chunkZ * 32 + lz)) return false;
            rnd.currentSeed = seed;
            return true;
        }

        static long springsAtSurface, sideStreamsSkipped;

        /// <summary>All of this class's counters back to 0 for a new server (CR-G4: a single-player process otherwise counts the
        /// worlds before into "this session").</summary>
        internal static void ResetCounters()
        {
            System.Threading.Interlocked.Exchange(ref pondsOutsideWetlands, 0);
            System.Threading.Interlocked.Exchange(ref pondsInWetlands, 0);
            System.Threading.Interlocked.Exchange(ref pondsInCaves, 0);
            System.Threading.Interlocked.Exchange(ref springsAtSurface, 0);
            System.Threading.Interlocked.Exchange(ref sideStreamsSkipped, 0);
        }

        /// <summary>Vanilla's spring and mountain-side stream tries the springs rules turned away since the server started (for
        /// the self-test and the shutdown log).</summary>
        public static string SpringReport() => string.Format("springs at the surface turned away: {0}, mountain-side streams turned away: {1}",
            System.Threading.Interlocked.Read(ref springsAtSurface), System.Threading.Interlocked.Read(ref sideStreamsSkipped));
    }
}
