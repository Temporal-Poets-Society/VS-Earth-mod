using System;
using System.Collections.Concurrent;
using System.Linq;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// Natural tree/shrub composition in Earth worlds. Vanilla picks each tree's species from climate ranges only, so any
    /// temperate spot gets the same mix. Earth replaces that pick (a prefix on WgenTreeSupplier.GetRandomGenForClimate,
    /// scoped by prefixes/finalizers on GenVegetationAndPatches.genTrees/genShrubs; player planting stays outside it).
    /// The pick itself is the game-independent <see cref="RegionalTreePicker"/> (src/Core, unit-tested offline):
    ///  - without regional trees: the pre-0.9 Earth pick (forest types from the real climate: boreal, hemiboreal,
    ///    broadleaf, Mediterranean, montane, hot-dry; vanilla's rule for everything else);
    ///  - with regional trees (rules v2): (1) species native to the planting position's botanical country, with Earth
    ///    climate envelopes for non-vanilla species; (2) if none qualifies, designated regional analogues for the biome;
    ///    (3) if still none, the pre-0.9 climate pick among vanilla trees. Borders between botanical countries are
    ///    blended with smooth noise; aliases of one species share its weight.
    /// Any failure in the hook falls back to vanilla's pick (logged once) instead of breaking world generation.
    /// </summary>
    public static class EarthTrees
    {
        static readonly ConcurrentDictionary<string, long> testSelections = new();
        public static string SelectionReport() => "Regional tree selections (self-test only; t1 native, t2 analogue, t3 climate fallback):\n" +
            string.Join("\n", testSelections.OrderBy(p => p.Key).Select(p => p.Key + ": " + p.Value));
        [ThreadStatic] static bool ctxSet;
        [ThreadStatic] static GenVegetationAndPatches ctxOwner;
        [ThreadStatic] static int ctxX, ctxZ;
        // per-chunk climate (forest-type weights, coldest month, sea), per thread
        [ThreadStatic] static long cachedKey;
        [ThreadStatic] static float[] cachedFactors;
        [ThreadStatic] static bool cachedSea, cachedRegional;
        [ThreadStatic] static float cachedCold, cachedWarm, cachedDry, cachedRain;
        // the chunk's soil for the species' soil indicators (SoilNiche); cachedSoil.HasSoil false: none mapped
        [ThreadStatic] static TreeSite cachedSoil;
        [ThreadStatic] static double cachedAridity;
        [ThreadStatic] static long cachedModel;   // TerrainModel.CacheId (not the model: a thread would keep an unloaded world alive)
        // the chunk centre's climate, for its forest-type weights, worked out only for the attempts that use them (with local
        // alpine ecology every attempt has its own)
        [ThreadStatic] static EarthData.ClimateSample cachedClimate;
        [ThreadStatic] static double cachedLat, cachedLon, cachedElev;
        [ThreadStatic] static bool cachedFactorsReady;

        /// <summary>Why the hooks are not active (null when <see cref="Patched"/>).</summary>
        public static string PatchError { get; private set; }
        public static bool Patched;
        /// <summary>Blocks beyond a river's water within which floodplain trees (black alder, poplars) grow.</summary>
        const double WetBelt = 16;
        /// <summary>Blocks between a bamboo root and river water (the grove's culms spread a few blocks from the root).</summary>
        const double BambooRiverClearance = 7;
        /// <summary>Salt of the position hash that thins the trees under vanilla's floor (zone rules 2).</summary>
        const int TreeFloorSalt = 7301;
        const int ShoreZoneSalt = 7307;
        static int failureLogged;

        // vanilla's non-public fields the pick reads, bound by PatchVanilla (its TreeSpecies feature) at start-up, not in the type
        // initializer: a field a game update renamed leaves the hooks off (PatchError) instead of failing every use of this class
        internal static AccessTools.FieldRef<WgenTreeSupplier, TreeGeneratorsUtil> generatorsRef;
        internal static AccessTools.FieldRef<WgenTreeSupplier, TreeGenProperties> propsRef;
        internal static AccessTools.FieldRef<GenVegetationAndPatches, BlockPos> positionRef;
        internal static AccessTools.FieldRef<GenVegetationAndPatches, Vintagestory.API.Server.IWorldGenBlockAccessor> accessorRef;

        // ------------------------------------------------------------------------------- compiled pickers
        // One picker per registered variant list (trees, shrubs) and catalogue, built once: generator codes, rules,
        // regions and populations are resolved here, never per tree attempt.
        sealed class PickerEntry
        {
            public TreeVariant[] Gens;
            public int Length;
            public RegionalTreeCatalog Catalog;
            public RegionalTreePicker Picker;
        }
        static volatile PickerEntry[] pickers = Array.Empty<PickerEntry>();
        static readonly object pickerLock = new();
        [ThreadStatic] static PickerEntry lastPicker;

        static RegionalTreePicker GetPicker(TreeVariant[] gens, RegionalTreeCatalog catalog, bool shrubs)
        {
            var last = lastPicker;
            if (last != null && last.Gens == gens && last.Catalog == catalog && last.Length == gens.Length) return last.Picker;
            foreach (var p in pickers)
                if (p.Gens == gens && p.Catalog == catalog && p.Length == gens.Length) { lastPicker = p; return p.Picker; }
            lock (pickerLock)
            {
                foreach (var p in pickers)
                    if (p.Gens == gens && p.Catalog == catalog && p.Length == gens.Length) { lastPicker = p; return p.Picker; }
                var infos = new TreeVariantInfo[gens.Length];
                for (int i = 0; i < gens.Length; i++)
                {
                    var v = gens[i];
                    infos[i] = v == null ? new TreeVariantInfo { Generator = "", TempRange = 1, RainRange = 1, FertRange = 1, ForestRange = 1, HeightRange = 1 } : new TreeVariantInfo
                    {
                        Generator = v.Generator?.ToString() ?? "", Weight = v.Weight, Habitat = (int)v.Habitat,
                        TempMid = v.TempMid, TempRange = v.TempRange, RainMid = v.RainMid, RainRange = v.RainRange,
                        FertMid = v.FertMid, FertRange = v.FertRange, ForestMid = v.ForestMid, ForestRange = v.ForestRange,
                        HeightMid = v.HeightMid, HeightRange = v.HeightRange,
                    };
                }
                var entry = new PickerEntry { Gens = gens, Length = gens.Length, Catalog = catalog, Picker = new RegionalTreePicker(catalog, infos, shrubs) };
                // keep the current catalogue's lists only (a few entries: trees and shrubs)
                pickers = pickers.Where(p => p.Catalog == catalog).Take(7).Append(entry).ToArray();
                lastPicker = entry;
                return entry.Picker;
            }
        }

        static readonly VerticalScale heightBandReference = VerticalScale.ForWorld(384, 165);
        [ThreadStatic] static float[] localFactors;
        [ThreadStatic] static BlockPos probePos;
        static Block FluidAbove(Vintagestory.API.Server.IWorldGenBlockAccessor accessor, BlockPos pos)
        {
            var p = probePos ??= new BlockPos(0);
            p.Set(pos.X, pos.Y + 1, pos.Z);
            p.dimension = pos.dimension;
            return accessor.GetBlock(p, BlockLayersAccess.Fluid);
        }

        struct LcgSource : ITreeRandom
        {
            readonly LCGRandom rnd;
            public LcgSource(LCGRandom rnd) { this.rnd = rnd; }
            public double NextDouble() => rnd.NextDouble();
        }

        /// <summary>Replaces vanilla's species pick in Earth worlds (see the class summary).</summary>
        public static bool PickPrefix(WgenTreeSupplier __instance, LCGRandom rnd, TreeVariant[] gens, int climate, int forest, int y, bool isUnderwater, ref TreeGenInstance __result)
        {
            var gen = EarthWorldGen.Instance;
            if (!ctxSet || ctxOwner == null || gen == null || !gen.Active || gens == null || rnd == null) return true;
            try
            {
                return Pick(gen, __instance, rnd, gens, climate, forest, y, isUnderwater, ref __result);
            }
            catch (Exception e)
            {
                if (System.Threading.Interlocked.Exchange(ref failureLogged, 1) == 0)
                    gen.Mod.Logger.Error("Earth tree pick failed; vanilla's pick is used for this tree (further failures are not logged): {0}", e);
                __result = null;
                return true;
            }
        }

        static bool Pick(EarthWorldGen gen, WgenTreeSupplier supplier, LCGRandom rnd, TreeVariant[] gens, int climate, int forest, int y, bool isUnderwater, ref TreeGenInstance __result)
        {
            var model = gen.Model;
            // Keep worldgen trunks and their neighbouring crowns out of companion architecture. tmpPos is set
            // immediately before the supplier call, including for shrubs.
            var pos = positionRef(ctxOwner);
            if (pos != null && model.IntersectsReservedTerrain(pos.X - 16, pos.Z - 16, pos.X + 16, pos.Z + 16))
            { __result = null; return false; }
            var catalog = gen.RegionalTrees;
            var picker = GetPicker(gens, catalog, gens == propsRef(supplier)?.ShrubGens);
            // decision 0069 rule 7: the shore zones (no trees or shrubs on a beach, the open belts of a low sea coast, a
            // river's low bank, a lake's sedge shore; the forest thinning in beyond). A position hash, so the plants that
            // stay keep their places.
            if (model.ShoreZoneRules >= 1 && pos != null && !isUnderwater)
            {
                var sc = ColumnStore.Near(model, pos.X, pos.Z);
                double keepShore = ShoreZones.Keep(model, pos.X, pos.Z, in sc, picker.Shrubs);
                if (keepShore < 1 && GeologyTransitions.Hash(gen.NoiseSeed, pos.X, pos.Y, pos.Z, ShoreZoneSalt) >= keepShore)
                { __result = null; return false; }
            }
            // zone rules 2: vanilla kept this try with at least its floor of 0.0025; keep only the share f² asks for (open
            // steppe had dozens of trees in sight at 1:10). A position hash, not rnd, so the trees that stay keep their shapes.
            // Shrubs have no floor in vanilla.
            if (model.NoTreeFloor && !picker.Shrubs && pos != null && ClimateZones.TreeKeep(forest) < 1
                && GeologyTransitions.Hash(gen.NoiseSeed, pos.X, pos.Y, pos.Z, TreeFloorSalt) >= ClimateZones.TreeKeep(forest))
            { __result = null; return false; }
            bool regional = catalog != null;
            long key = ((long)ctxX << 32) | (uint)ctxZ;
            if (cachedFactors == null || cachedKey != key || cachedModel != model.CacheId || cachedRegional != regional)
            {
                var g = model.Geometry.ToGeo(ctxX * 32 + 16, ctxZ * 32 + 16);
                var cs = model.Data.SampleClimate(g.Lat, g.Lon);
                double elev = Math.Max(0, model.Data.ElevBilinear(g.Lat, g.Lon));
                cachedFactors ??= new float[RegionalTreePicker.Groups];
                cachedClimate = cs; cachedLat = g.Lat; cachedLon = g.Lon; cachedElev = elev;
                cachedFactorsReady = false;
                cachedCold = (float)(cs.TCold - ZoneDrivers.LapseRate * elev);
                cachedWarm = (float)(cs.TWarm - ZoneDrivers.LapseRate * elev);
                cachedDry = (float)cs.DrySummer;
                cachedRain = (float)cs.Rain;
                cachedSea = g.InGutter || model.Data.SampleTerrain(g.Lat, g.Lon).Ocean > 0.2;
                cachedSoil = default;
                if (regional && model.Soils != null)
                {
                    cachedAridity = cs.Aridity;   // (the zone drivers' aridity is the climate's own)
                    SoilNiche.Fill(ref cachedSoil, model.Soils, g.Lat, g.Lon, cachedAridity, false);
                    SoilNiche.AddWetlands(ref cachedSoil, model.Wetlands, g.Lat, g.Lon);
                }
                cachedKey = key;
                cachedModel = model.CacheId;
                cachedRegional = regional;
            }
            // Vanilla tests for water only at the attempt block; on a river bed or lake floor the block above can be water
            // although that block is not. Such a site is underwater: only water-habitat trees, never shrubs, and never in a
            // river channel (trees there block boats). Lakes, swamps and coastal shallows keep their water trees.
            var accessor = accessorRef(ctxOwner);
            Block fluidAbove = null;
            bool fluidRead = false, flooded = isUnderwater;
            if (!flooded && pos != null && accessor != null)
            {
                fluidAbove = FluidAbove(accessor, pos);
                fluidRead = true;
                flooded = fluidAbove?.IsLiquid() == true;
            }
            TerrainModel.Column column = default;
            bool haveColumn = false;
            if (flooded && pos != null)
            {
                if (picker.Shrubs) { __result = null; return false; }
                column = ColumnStore.Near(model, pos.X, pos.Z);
                haveColumn = true;
                if (column.River || (column.NearRiver && column.Water == TerrainModel.WaterNone)) { __result = null; return false; }
                isUnderwater = true;
            }
            // pre-0.9 Earth: no trees standing in the sea (swamp trees only in fresh water)
            if (!regional && isUnderwater && cachedSea) { __result = null; return false; }

            int sea = TerraGenConfig.seaLevel;
            float mapHeight = model.Vertical.MapHeight;
            int rain = Climate.GetRainFall((climate >> 8) & 0xFF, y);
            int temp = Climate.GetScaledAdjustedTemperature((climate >> 16) & 0xFF, y - sea);
            float yRel = (y - sea) / (mapHeight - sea);
            var site = new TreeSite
            {
                Temp = temp, Rain = rain, Fert = Climate.GetFertility(rain, temp, yRel), Forest = forest, HeightRel = y / mapHeight,
                Factors = cachedFactors, ColdMonth = cachedCold, WarmMonth = cachedWarm, DrySummer = cachedDry, Region = -1, Underwater = isUnderwater,
                AmbientRain = cachedRain,
            };
            // Geography is required even with regional selection and local alpine ecology disabled.
            var plantingGeo=model.Geometry.ToGeo(pos?.X ?? ctxX*32+16,pos?.Z ?? ctxZ*32+16);
            site.Lat=plantingGeo.Lat; site.Lon=plantingGeo.Lon;
            site.Elevation=model.Vertical.Elevation(y);
            site.OutsideEarth=plantingGeo.InGutter || plantingGeo.BeyondPole;
            if (regional && pos != null)
            {
                // the actual planting position, not the chunk centre: shores, islands and borders run through chunks (the
                // planting geography above is that position's here)
                if (!site.OutsideEarth) site.Region = catalog.Geography.FindBlended(plantingGeo.Lat, plantingGeo.Lon);
                // floodplain/lake-shore and beach trees: only computed where such a species is native
                bool wet = picker.NeedsWet(site.Region), shore = picker.NeedsShore(site.Region);
                if (wet || shore)
                {
                    var tg = model.TerrainGeoAt(pos.X, pos.Z);
                    var t = model.Data.SampleTerrain(tg.Lat, tg.Lon);
                    site.Shore = t.Ocean > 0.02;
                    site.Wet = t.Lake > 0.02 && !t.SaltLake;
                    if (wet && !site.Wet && model.Rivers != null)
                    {
                        model.ProjectTerrain(pos.X, pos.Z, out double sx, out double sz);
                        site.Wet = model.Rivers.Nearest(sx, sz, out var hit) && hit.Dist <= hit.HalfWidth + WetBelt;
                        if(site.Wet && model.AridRiverRules>=1 && model.WetSourceFlow!=null && hit.Reach<model.WetSourceFlow.Length)
                            site.Wet=!AridHydrology.Dry(site.AmbientRain??site.Rain,site.WarmMonth,hit.Q,model.WetSourceFlow[hit.Reach]) || model.MaskKeepsWet(in hit);
                    }
                }
                if (isUnderwater || picker.NeedsShoreOnLand)
                {
                    // water type and depth only where a rule depends on them (the terrain column is comparatively costly)
                    var c = haveColumn ? column : ColumnStore.Near(model, pos.X, pos.Z);
                    bool saltLake = c.Water == TerrainModel.WaterSaltLake;
                    bool marine = c.Water == TerrainModel.WaterSea;
                    if (isUnderwater)
                    {
                        var fluid = fluidRead ? fluidAbove : accessor == null ? null : FluidAbove(accessor, pos);
                        if (fluid?.Code?.Path.StartsWith("saltwater", StringComparison.Ordinal) == true && !saltLake) marine = true;
                    }
                    else if (c.Beach && c.NearLakeTop < 0 && !c.River)
                        marine = model.Data.SampleTerrain(c.Lat, c.Lon).Ocean > .02;
                    site.Marine = marine;
                    site.SalineLake = saltLake;
                    site.Depth = Math.Max(0, c.WaterTop - pos.Y);
                    site.Elevation = c.Elev;
                    site.OutsideEarth |= c.Gutter;
                    if (site.OutsideEarth) site.Region = -1;
                }
            }
            if (model.LocalAlpineEcology && pos != null)
            {
                var g = model.TerrainGeoAt(pos.X, pos.Z);
                var cs = model.Data.SampleClimate(g.Lat, g.Lon);
                double elevation = Math.Max(TerrainModel.MinLandElevation, model.Vertical.Elevation(y));
                site.Lat = g.Lat; site.Lon = g.Lon;
                site.Elevation = elevation;
                site.ColdMonth = (float)(cs.TCold - ZoneDrivers.LapseRate * elevation);
                site.WarmMonth = (float)(cs.TWarm - ZoneDrivers.LapseRate * elevation);
                site.Temp = (int)Math.Round(cs.TMean - ZoneDrivers.LapseRate * elevation);
                site.Rain = (int)Math.Round(model.RiparianRain(pos.X, pos.Z, cs.Rain));
                site.AmbientRain = (float)cs.Rain;
                site.Fert = Climate.GetFertility(site.Rain, site.Temp, yRel);
                // Keep provider height bands in the original 384-high frame so a new datum does not change their ecology.
                site.HeightRel = (float)(heightBandReference.Y(elevation) / 384);
                site.Factors = localFactors ??= new float[RegionalTreePicker.Groups];
                if (model.Zones != null) model.Zones.TreeFactors(ZoneDrivers.From(model.Data, cs, g.Lat, g.Lon, ZoneDrivers.LapseRate * elevation), site.Factors);
                else RegionalTreePicker.ForestTypeFactors(cs.TMean, cs.TCold, cs.TWarm, cs.DrySummer, cs.Rain, elevation, site.Factors, regional);
                double suitability = MountainEcology.PlantingSuitability(g.Lat,g.Lon,elevation,site.WarmMonth,cs.Rain,picker.Shrubs,site.Wet,cs.DrySummer);
                if (suitability <= 0 || rnd.NextDouble() >= suitability) { __result = null; return false; }
            }
            if (site.Factors == cachedFactors && !cachedFactorsReady)
            {
                // habitats: in zone worlds the zones' tree mixes, the same zones that set the forest density
                if (model.Zones != null) model.Zones.TreeFactors(ZoneDrivers.From(model.Data, cachedClimate, cachedLat, cachedLon, ZoneDrivers.LapseRate * cachedElev), cachedFactors);
                else RegionalTreePicker.ForestTypeFactors(cachedClimate.TMean, cachedClimate.TCold, cachedClimate.TWarm, cachedClimate.DrySummer, cachedClimate.Rain,
                    cachedElev, cachedFactors, regional);
                cachedFactorsReady = true;
            }
            if (cachedSoil.HasSoil)
            {
                site.HasSoil = true;
                site.WetShare = cachedSoil.WetShare; site.PoorShare = cachedSoil.PoorShare;
                site.GrassShare = cachedSoil.GrassShare; site.SoilPh = cachedSoil.SoilPh;
                site.SoilMoisture = site.Wet ? SoilNiche.DrainedMoisture(cachedAridity, true) : cachedSoil.SoilMoisture;
            }
            var random = new LcgSource(rnd);
            int index = picker.Pick(site, ref random, out float sizeWeight, out int tier);
            __result = null;
            if (index < 0) return false;
            var v = gens[index];
            if (!regional && MountainEcology.HotDesert(site.AmbientRain ?? site.Rain,site.WarmMonth,site.DrySummer))
            {
                // Even the legacy picker must not export its generic acacia to another native range.
                var ranges=gen.DesertTreeRanges;
                if(ranges?.NativeAt(v?.Generator?.ToString(),site)!=true) return false;
            }
            var treeGen = v?.Generator == null ? null : generatorsRef(supplier).GetGenerator(v.Generator);
            if (treeGen == null) return false;
            // Bamboo grows as a grove of culms around the root; rooted on a river bank (flush with the water) the culms
            // spread into the channel and block boats. Keep groves a grove's width away from river water.
            if (pos != null && model.Rivers != null && v.Generator.Path.StartsWith("bamboo", StringComparison.Ordinal))
            {
                model.ProjectTerrain(pos.X, pos.Z, out double rx, out double rz);
                if (model.Rivers.DistanceToWater(rx, rz, BambooRiverClearance) <= BambooRiverClearance) return false;
            }
            var props = propsRef(supplier);
            float sizeBonus = Math.Clamp(0.7f - sizeWeight, 0f, 0.7f) / 0.7f * v.SuitabilitySizeBonus;
            float tUnscaled = Climate.DescaleTemperature(temp);
            float vineRain = Math.Max(0f, (rain / 255f - props.vinesMinRain) / (1f - props.vinesMinRain));
            float vineTemp = Math.Max(0f, (tUnscaled / 255f - props.descVineMinTempRel) / (1f - props.descVineMinTempRel));
            float rainRel = rain / 255f, tempRel = tUnscaled / 255f;
            __result = new TreeGenInstance
            {
                treeGen = treeGen,
                size = v.MinSize + (float)rnd.NextDouble() * (v.MaxSize - v.MinSize) + sizeBonus,
                vinesGrowthChance = 1.5f * vineRain * vineTemp + 0.5f * vineRain * Math.Clamp((vineTemp + 0.33f) / 1.33f, 0f, 1f),
                mossGrowthChance = (float)Math.Clamp(2.25 * rainRel - 0.5 + Math.Sqrt(tempRel) * 3.0 * Math.Max(-0.5, 0.5 - tempRel), 0, 1),
            };
            if (regional && gen.TraceTreeSelections)
                testSelections.AddOrUpdate((catalog.Geography.Code(site.Region) ?? "unmapped") + " " + v.Generator + " t" + tier, 1, (_, n) => n + 1);
            return false;
        }

        public static void LoadedTreesPostfix(WgenTreeSupplier __instance)
        {
            pickers = Array.Empty<PickerEntry>();
            var gen = EarthWorldGen.Instance;
            var catalog = gen?.RegionalTrees;
            if (catalog == null) return;
            try
            {
                var props = propsRef(__instance);
                var registered = props.TreeGens.Concat(props.ShrubGens).Where(v => v?.Generator != null).Select(v => v.Generator.ToString()).Distinct().ToHashSet();
                var missing = catalog.Rules.SelectMany(r => r.Generators).Where(g => !registered.Contains(g)).ToArray();
                var unknown = registered.Where(g => catalog.Find(g) == null).ToArray();
                gen.Mod.Logger.Notification("Earth tree adapter coverage: {0}/{1} registered natural generators mapped; {2} regional analogue rules.",
                    registered.Count - unknown.Length, registered.Count, catalog.Analogues.Count);
                if (missing.Length > 0) gen.Mod.Logger.Warning("Earth tree rules reference absent generators (provider may have changed): {0}", string.Join(", ", missing));
                if (unknown.Length > 0) gen.Mod.Logger.Warning("Earth has no geographic adapter for these generators; their provider rules remain active: {0}", string.Join(", ", unknown));
                foreach (string w in catalog.Warnings) gen.Mod.Logger.Warning("Earth tree data: {0}", w);
            }
            catch (Exception e)
            {
                gen.Mod.Logger.Warning("Earth tree adapter coverage could not be checked: {0}", e.Message);
            }
        }

        public readonly struct Scope
        {
            internal readonly GenVegetationAndPatches Owner;
            internal readonly int X,Z;
            internal readonly bool Set;
            internal Scope(GenVegetationAndPatches owner,int x,int z,bool set) { Owner=owner;X=x;Z=z;Set=set; }
        }
        public static void ChunkPrefix(GenVegetationAndPatches __instance, [HarmonyArgument(0)] int chunkX, [HarmonyArgument(1)] int chunkZ, out Scope __state)
        {
            __state=new Scope(ctxOwner,ctxX,ctxZ,ctxSet);
            ctxOwner=__instance;
            ctxSet=true;
            ctxX=chunkX;
            ctxZ=chunkZ;
        }
        public static void ChunkFinalizer(Scope __state)
        {
            ctxOwner=__state.Owner; ctxX=__state.X; ctxZ=__state.Z; ctxSet=__state.Set;
        }

        /// <summary>
        /// The hooks are bound by PatchVanilla (its TreeSpecies feature: the pick, the tree and shrub scopes and the fields the pick
        /// reads, all or none). Before binding: a fresh session's selections and pickers. Never throws: a missing or changed game
        /// member leaves <see cref="Patched"/> false with the reason in <see cref="PatchError"/>, and the game keeps vanilla's
        /// species pick (without the scopes the pick prefix never acts: ctxSet stays false).
        /// </summary>
        internal static void BeginBinding()
        {
            testSelections.Clear();
            pickers = Array.Empty<PickerEntry>();
            Patched = false;
            PatchError = null;
            // a new binding (a new world in this process) says its first failure again (CR-E4)
            System.Threading.Interlocked.Exchange(ref failureLogged, 0);
        }

        internal static void Bound() { Patched = true; PatchError = null; }

        internal static void Unbound(string why) { Patched = false; PatchError = why; }
    }
}
