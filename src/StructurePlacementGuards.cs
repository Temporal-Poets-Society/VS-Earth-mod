using System;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Earth's guards on vanilla's structure placement (ruins, trader camps, structure-placed translocators, villages) and on
    /// vanilla's block patches near companion sites: where people lived around 1318, out of rivers, lakes and the sea, clear of
    /// companions' reserved terrain. Each guard is a patch on vanilla's generator (bound by PatchVanilla) that reads only the
    /// world's model and rule data (<see cref="WorldGenerationContext"/>), so its answer is a function of the position: the
    /// same in every run and on every thread. Each lets vanilla be while Earth is inactive.
    /// </summary>
    static class StructurePlacementGuards
    {
        /// <summary>
        /// No vanilla worldgen structures (ruins, trader camps, structure-placed translocators; surface, underground, in
        /// water) or villages where nobody lived around 1318: regions not yet settled (Antarctica, Svalbard, the
        /// sub-Antarctic and the islands found later) and ice-cap climate at the ground (the Greenland ice sheet, the
        /// highest summits). Data in people-1318.json (ranges proposal, step 4); replaces decision 0016's 70°N / 60°S lines.
        /// Every Earth world, new chunks only. Interim: the author plans to remake ruins.
        /// </summary>
        static bool OutsideWherePeopleLived(BlockPos pos)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || pos == null) return false;
            var people = w.Rules.People;
            if (people == null) return false;
            var g = w.Model.TerrainGeoAt(pos.X, pos.Z);
            if (g.InGutter || g.BeyondPole) return true;
            return !people.Allows(g.Lat, g.Lon, w.Model.WarmMonthAtGround(pos.X, pos.Z));
        }

        /// <summary>/earth where: this world's structure rule at a ground position.</summary>
        internal static string Describe(WorldGenerationContext w, BlockPos ground)
        {
            var model = w.Model;
            var g = model.TerrainGeoAt(ground.X, ground.Z);
            double warm = model.WarmMonthAtGround(ground.X, ground.Z);
            var peopleRange = w.Rules.People;
            if (peopleRange == null) return "Ruins, trader camps and villages: people-1318.json not loaded; placed anywhere.";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Ruins, trader camps and villages (where people lived around 1318): {0} (region {1}; warmest month at the ground {2:0.0} °C, limit {3:0.#} °C).",
                peopleRange.Allows(g.Lat, g.Lon, warm) ? "yes" : "no", peopleRange.Settled(g.Lat, g.Lon) ? "settled" : "not settled by 1318", warm, peopleRange.MinWarmMonth);
        }

        internal static bool StructureWherePeopleLivedGuard([HarmonyArgument(2)] BlockPos startPos, ref bool __result)
        {
            if (!OutsideWherePeopleLived(startPos)) return true;
            __result = false;
            return false;
        }

        internal static bool VillageWherePeopleLivedGuard(BlockPos pos, ref bool __result)
        {
            if (!OutsideWherePeopleLived(pos)) return true;
            __result = false;
            return false;
        }

        /// <summary>
        /// Vanilla lets surface ruins sit up to 20 blocks below sea level, assuming such ground is dry. The real Earth has
        /// wide shallow shelves (the Gulf of Riga, bays, lagoons) and narrow rivers, so ruins ended up on the sea floor
        /// and in rivers. Skip them wherever the Earth terrain has water anywhere under the largest footprint the
        /// structure can have (checked every 3 blocks, so no river slips between the checks).
        /// </summary>
        internal static bool SurfaceStructureGuard(WorldGenStructure __instance, [HarmonyArgument(2)] BlockPos startPos, ref bool __result)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || startPos == null) return true;
            int size = __instance != null ? FootprintOf(__instance) : 16;
            var model = w.Model;
            if (model.IntersectsReservedTerrain(startPos.X, startPos.Z, startPos.X + size, startPos.Z + size)
                || AnyFootprintSample(model, startPos.X, startPos.Z, startPos.X + size, startPos.Z + size,
                    c => c.Water != TerrainModel.WaterNone || c.River || c.Top < model.SeaLevel - 1))
            { __result = false; return false; }
            return true;
        }

        /// <summary>
        /// Whether any column of [x0, x1] × [z0, z1] matches, sampled every 3 blocks and along the far edges (so no river
        /// slips between the samples): row by row of z, stopping at the first match.
        /// </summary>
        static bool AnyFootprintSample(TerrainModel model, int x0, int z0, int x1, int z1, System.Func<TerrainModel.Column, bool> match)
        {
            for (int z = z0; ; z = Math.Min(z + 3, z1))
            {
                for (int x = x0; ; x = Math.Min(x + 3, x1))
                {
                    if (match(ColumnStore.Near(model, x, z))) return true;
                    if (x >= x1) break;
                }
                if (z >= z1) break;
            }
            return false;
        }

        /// <summary>
        /// The underground guard, deferred (decision 0026): it runs for nearly every structure attempt, so the generator's
        /// prefix only notes the attempt, and the guard runs when vanilla reaches WouldOverlapAt, the last check before it
        /// places the schematic (on both placing paths, the cave-attached one too). Most attempts fail vanilla's cheap checks
        /// first (the host block, check positions, distance), so most never pay for the guard. Every check on the way is
        /// read-only (vanilla's LastPlacedSchematic* is only read after a placement), each attempt reseeds the structure's
        /// random generator from its position, and this generator reads each schematic only after unpacking it, so the
        /// attempts that are placed are exactly those the up-front guard let through.
        /// </summary>
        [ThreadStatic] static WorldGenStructure deferredStructure;
        [ThreadStatic] static int deferredX, deferredY, deferredZ;
        static long deferredChecks;

        /// <summary>Notes the attempt for <see cref="DeferredUndergroundGuard"/>; the attempt before (an outer one, if vanilla ever
        /// nested them) comes back when this one ends (<see cref="ClearDeferredUndergroundGuard"/>).</summary>
        internal static bool DeferUndergroundStructureGuard(WorldGenStructure __instance, [HarmonyArgument(2)] BlockPos pos, out (WorldGenStructure Structure, int X, int Y, int Z) __state)
        {
            __state = (deferredStructure, deferredX, deferredY, deferredZ);
            deferredStructure = WorldGenerationContext.Current != null && pos != null && __instance != null ? __instance : null;
            deferredX = pos?.X ?? 0;
            deferredY = pos?.Y ?? 0;
            deferredZ = pos?.Z ?? 0;
            return true;
        }

        internal static void ClearDeferredUndergroundGuard((WorldGenStructure Structure, int X, int Y, int Z) __state)
            => (deferredStructure, deferredX, deferredY, deferredZ) = __state;

        internal static bool DeferredUndergroundGuard(WorldGenStructure __instance, [HarmonyArgument(1)] BlockPos pos, [HarmonyArgument(2)] BlockSchematicStructure schematic, ref bool __result)
        {
            if (deferredStructure == null || deferredStructure != __instance) return true;
            deferredStructure = null;
            var w = WorldGenerationContext.Current;
            if (w == null) return true;
            System.Threading.Interlocked.Increment(ref deferredChecks);
            // vanilla's cave-attached path moves the structure to the cave's pathway before this check and places the schematic
            // from that corner (WorldGenStructure.tryGenerateAttachedToCave: [pos, pos + Size]); the noted position is stale
            // there, and an uncentred box checked from it could miss a river bed up to its size west or north (CR-G3)
            bool moved = pos != null && schematic != null && (pos.X != deferredX || pos.Y != deferredY || pos.Z != deferredZ);
            bool blocked = moved
                ? ReachesWater(w.Model, pos.X - 1, pos.Z - 1, Math.Max(schematic.SizeX, schematic.SizeZ) + 2, pos.Y + schematic.SizeY)
                : UndergroundBlocked(w.Model, __instance, deferredX, deferredY, deferredZ);
            if (!blocked) return true;
            System.Threading.Interlocked.Increment(ref undergroundSkipped);
            __result = true; // "would overlap": vanilla turns the attempt away
            return false;
        }

        /// <summary>Structures meant for shallow water stay out of the rivers (the navigation channels); shores are fine.</summary>
        internal static bool RiverStructureGuard(WorldGenStructure __instance, [HarmonyArgument(2)] BlockPos startPos, ref bool __result)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || startPos == null || w.Model.Rivers == null) return true;
            int size = __instance != null ? FootprintOf(__instance) : 16;
            if (AnyFootprintSample(w.Model, startPos.X, startPos.Z, startPos.X + size, startPos.Z + size, c => c.River))
            {
                __result = false;
                return false;
            }
            return true;
        }

        static long undergroundSkipped, villageBuildingsSkipped;

        /// <summary>The counters back to 0 for a new server (CR-G4: a single-player process otherwise counts the worlds before).</summary>
        internal static void ResetCounters()
        {
            System.Threading.Interlocked.Exchange(ref deferredChecks, 0);
            System.Threading.Interlocked.Exchange(ref undergroundSkipped, 0);
            System.Threading.Interlocked.Exchange(ref villageBuildingsSkipped, 0);
        }

        /// <summary>How many structures the water guards turned away since the server started (for the self-test).</summary>
        public static string Report() => string.Format("underground structures kept out of water: {0}, village buildings kept out of water: {1}, " +
            "underground structures checked after vanilla's own checks: {2}",
            System.Threading.Interlocked.Read(ref undergroundSkipped), System.Threading.Interlocked.Read(ref villageBuildingsSkipped),
            System.Threading.Interlocked.Read(ref deferredChecks));

        /// <summary>
        /// Underground structures (ruins, buried chests, vugs, cave lakes) sit a few to many blocks under the surface, and
        /// vanilla never checks for water above them. Under Earth's rivers, lakes and seas they broke into the water: ruin
        /// walls and dry pockets in a river channel. Skip any whose box could reach the bed of a water body (or the block
        /// under it). The box is taken generously in x/z, as the schematic may be centred on the position.
        /// </summary>
        internal static bool UndergroundStructureGuard(WorldGenStructure __instance, [HarmonyArgument(2)] BlockPos pos, ref bool __result)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || pos == null) return true;
            if (!UndergroundBlocked(w.Model, __instance, pos.X, pos.Y, pos.Z)) return true;
            System.Threading.Interlocked.Increment(ref undergroundSkipped);
            __result = false;
            return false;
        }

        static bool UndergroundBlocked(TerrainModel model, WorldGenStructure structure, int x, int y, int z)
        {
            var box = BoxOf(structure);
            // (vanilla's AdjustStartPos centres all three; MiddleCenter, the vugs', also half its height down. It was taken as a
            // corner, so the west and north half of its box went unchecked, CR-G3)
            var origin = structure?.Origin;
            bool centred = origin == EnumOrigin.TopCenter || origin == EnumOrigin.BottomCenter || origin == EnumOrigin.MiddleCenter;
            int top = origin == EnumOrigin.TopCenter ? y : origin == EnumOrigin.MiddleCenter ? y + box.Height - box.Height / 2 : y + box.Height;
            int x0 = x - (centred ? box.Size / 2 + 1 : 0), z0 = z - (centred ? box.Size / 2 + 1 : 0);
            return ReachesWater(model, x0, z0, box.Size + 2, top);
        }

        /// <summary>
        /// Village buildings only test their four corners for liquid, so one can straddle a river narrower than the
        /// building. Turn away buildings over river, lake or sea water (the village may still place them elsewhere).
        /// </summary>
        internal static void VillageBuildingGuard([HarmonyArgument(2)] Cuboidi location, ref bool __result)
        {
            var w = WorldGenerationContext.Current;
            if (!__result || w == null || location == null) return;
            var model = w.Model;
            bool blocked = model.IntersectsReservedTerrain(location.X1, location.Z1, location.X2, location.Z2)
                || AnyFootprintSample(model, location.X1, location.Z1, location.X2, location.Z2,
                    c => c.Water != TerrainModel.WaterNone || c.River || c.Top < model.SeaLevel - 1);
            if (!blocked) return;
            System.Threading.Interlocked.Increment(ref villageBuildingsSkipped);
            __result = false;
        }

        /// <summary>
        /// Whether a box over [x0, x0+span]² reaching up to y=top would touch a water body or the block under its bed.
        /// Cheap away from water: a river-distance query and nine columns; the full 3-block grid only near water.
        /// </summary>
        static bool ReachesWater(TerrainModel model, int x0, int z0, int span, int top)
        {
            bool near = model.Rivers != null && model.Rivers.DistanceToWater(x0 + span / 2.0, z0 + span / 2.0, span) <= span;
            if (!near)
            {
                for (int i = 0; i <= 2 && !near; i++)
                    for (int j = 0; j <= 2; j++)
                    {
                        var c = ColumnStore.Near(model, x0 + span * i / 2, z0 + span * j / 2);
                        if (c.Water != TerrainModel.WaterNone || c.River || c.NearLakeTop >= 0 || c.Beach) { near = true; break; }
                    }
                if (!near) return false;
            }
            return AnyFootprintSample(model, x0, z0, x0 + span, z0 + span, c => (c.Water != TerrainModel.WaterNone || c.River) && top >= c.Top - 1);
        }

        /// <summary>Vanilla's structures' schematics (WorldGenStructure.schematicDatas, not public), bound by PatchVanilla; null
        /// when a game update renamed or changed the field: the guards then take every structure as 16 blocks.</summary>
        internal static AccessTools.FieldRef<WorldGenStructure, BlockSchematicStructure[][]> SchematicsRef;
        sealed class StructureBox { public int Size = 16, Height = 16; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldGenStructure, StructureBox> boxes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<WorldGenStructure, StructureBox>();

        /// <summary>The largest side of any of the structure's schematics (16 if unknown).</summary>
        static int FootprintOf(WorldGenStructure structure) => BoxOf(structure).Size;

        /// <summary>Largest horizontal side and height of any of the structure's schematics (16 if unknown).</summary>
        static StructureBox BoxOf(WorldGenStructure structure)
        {
            if (structure == null) return new StructureBox();
            return boxes.GetValue(structure, st =>
            {
                var box = new StructureBox();
                try
                {
                    var datas = SchematicsRef?.Invoke(st);
                    if (datas != null)
                    {
                        int size = 0, height = 0;
                        foreach (var rotations in datas)
                        {
                            if (rotations == null) continue;
                            foreach (var sch in rotations)
                            {
                                if (sch == null) continue;
                                size = Math.Max(size, Math.Max(sch.SizeX, sch.SizeZ));
                                height = Math.Max(height, sch.SizeY);
                            }
                        }
                        if (size > 0) box.Size = size;
                        if (height > 0) box.Height = height;
                    }
                }
                catch (Exception)
                {
                    // keep the defaults
                }
                box.Size = Math.Clamp(box.Size, 4, 96);
                box.Height = Math.Clamp(box.Height, 1, 128);
                return box;
            });
        }

        /// <summary>
        /// Vanilla's block patches (plants, rocks, mushrooms) scatter into neighbouring chunks after those chunks' own
        /// vegetation callbacks: keep each actual scatter target, not just the originating chunk or patch centre, off the
        /// companions' reserved terrain. BlockPatch.Generate's predicate is true when a target is blocked; any predicate
        /// vanilla supplied is kept exactly, as its caller's native modifier path has different named semantics.
        /// </summary>
        internal static void ReservationPatchGuard(ref ActionBoolReturn<int, int> intersectsWith)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || w.Model?.HasTerrainReservations != true) return;
            var model = w.Model;
            var original = intersectsWith;
            intersectsWith = (x, z) => original?.Invoke(x, z) == true || model.IntersectsReservedTerrain(x - 4, z - 4, x + 4, z + 4);
        }
    }
}
