using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// /earth where: the one region every range uses here (BotanicalRegions, ranges proposal step 1) and how it was found,
    /// the world's range settings, and which regional datasets cover the place, one method per section of the report.
    /// Read-only.
    /// </summary>
    sealed class WhereReport
    {
        readonly EarthMapSystem core;
        readonly ICoreServerAPI sapi;
        readonly List<string> lines = new();

        WhereReport(EarthMapSystem core, ICoreServerAPI sapi)
        {
            this.core = core;
            this.sapi = sapi;
        }

        EarthParams Params => core.Params;
        TerrainModel Model => core.Model;
        EarthData Data => core.Data;
        RegionalTreeCatalog RegionalTrees => core.RegionalTrees;

        /// <summary>The report for the caller's place; any failure is logged and answered with an error.</summary>
        public static TextCommandResult Run(EarthMapSystem core, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            try { return new WhereReport(core, sapi).Describe(args.Caller.Entity.Pos); }
            catch (Exception e)
            {
                sapi.Logger.Error("Earth /earth where failed: {0}", e);
                return TextCommandResult.Error("/earth where failed here: " + e.Message);
            }
        }

        TextCommandResult Describe(EntityPos pos)
        {
            var c = Model.GetColumn(pos.X, pos.Z);
            var g = Model.TerrainGeoAt(pos.X, pos.Z);
            if (c.Gutter || g.InGutter || g.BeyondPole) return TextCommandResult.Success("Outside the Earth (seam or beyond a pole): no region, no natural ranges.");
            // the regions the tree catalogues loaded at start (the same treeregions.json); decoding and hashing the 945 KB file
            // on every /earth where (any player may ask) only where neither loaded (CR-L6)
            var regions = (core.RegionalTrees ?? core.DesertTreeRanges)?.Geography;
            if (regions == null)
            {
                string json = sapi.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"))?.ToText();
                if (json == null) return TextCommandResult.Error("treeregions.json is missing; regional ranges are off.");
                regions = BotanicalRegions.Shared(json);
            }
            Region(c, g, regions);
            Rivers(pos);
            // This world's own loaded rules at the ground under the player (ranges proposal, steps 2-4).
            var ground = new BlockPos((int)pos.X, c.Top + 1, (int)pos.Z, 0);
            var gen = EarthWorldGen.Instance;
            if (gen != null && gen.Active) Ecology(gen, g, ground);
            Placement(g, ground);
            Geology(pos);
            return TextCommandResult.Success(string.Join("\n", lines));
        }

        /// <summary>The place, its region and the world's range settings.</summary>
        void Region(TerrainModel.Column c, EarthGeometry.GeoPos g, BotanicalRegions regions)
        {
            bool land = c.Water == TerrainModel.WaterNone && !c.River;
            string surface = c.Water switch { TerrainModel.WaterSea => "sea", TerrainModel.WaterLake => "lake", TerrainModel.WaterSaltLake => "salt lake", _ => c.River ? "river" : "land" };
            string region;
            if (land) region = regions.DescribeLand(g.Lat, g.Lon);
            else
            {
                int r = regions.Find(g.Lat, g.Lon);
                int near = regions.FindNear(g.Lat, g.Lon, BotanicalRegions.WaterSearchDegrees);
                region = r >= 0 ? regions.Name(r) + " (" + regions.Code(r) + ")"
                    : "none for open-sea animals" + (near >= 0 ? "; aquatic plants and shore animals use " + regions.Name(near) + " (" + regions.Code(near) + ", nearest within " + BotanicalRegions.WaterSearchDegrees + "°)" : "");
            }
            lines.Add(EarthMapSystem.FormatLatLon(g.Lat, g.Lon) + ", " + surface + ".");
            lines.Add("Region (trees, ground plants, animals; WGSRPD level 3): " + region + ".");
            lines.Add("Blended region for trees (borders drawn as irregular transition zones): " + regions.Code(regions.FindBlended(g.Lat, g.Lon)) + ".");
            lines.Add("Ranges in this world: animals " + (Params.RegionalFauna ? "by range, profile " + (string.IsNullOrEmpty(Params.FaunaRangeProfile) ? "natural" : Params.FaunaRangeProfile) : "off")
                + "; vanilla animals " + (Params.VanillaFaunaRanges >= 1 ? "by range" : "off") + "; plants " + (Params.PlacementRules >= 1 ? "placement catalogue v" + Params.PlacementRules : "vanilla climate placement") + ".");
            lines.Add(Params.VanillaBiomes >= 1
                ? "Biomes in this world: vanilla (world setting): vanilla's forest and shrub maps and tall grass, no vegetation zones, fire, open wetlands, ground moisture, grass in winter or alpine turf and scree."
                : string.Format(CultureInfo.InvariantCulture, "Biomes in this world: Earth's: zones v{0}, fire v{1}, wetlands v{2}, ground cover v{3}, ground moisture v{4}, grass in winter v{5}, alpine surfaces v{6}.",
                    Params.ZoneRules, Params.FireRules, Params.WetlandRules, Params.GroundCoverRules, Params.MoistureRules, Params.GrassWinterRules, Params.AlpineSurfaceRules));
        }

        /// <summary>0063: the river channel rules, and the nearest course's channel here as they draw it.</summary>
        void Rivers(EntityPos pos)
        {
            string channels = Params.ChannelRules >= 1
                ? "channel rules " + Params.ChannelRules + " (the course's channel everywhere along it, at the flow's width or the river's measured one where narrower, down to half and deepened for it"
                    + (Params.ChannelRules >= TerrainModel.MouthChannelRules ? "; one bed where a river meets the sea, and one smooth depth across the mask's river water" : "")
                    + (Params.ChannelRules >= TerrainModel.FlowChannelRules ? "; side channels the flow passes through kept open, small pools in a river joined to it, still or flowing water reach by reach" : "")
                    + (Params.ChannelRules >= RiverNetwork.PondChannelRules ? "; one width along a river, without the mask's ponds" : "") + ")"
                : Params.RiverOutlines >= 1 ? "channel rules 0 (the water mask's rivers cut and narrow the courses' channels)" : "the courses' channels alone";
            string channelHere = "";
            if (Model.Rivers != null)
            {
                Model.ProjectTerrain(pos.X, pos.Z, out double rx, out double rz);
                if (Model.Rivers.Nearest(rx, rz, out var rh))
                    channelHere = string.Format(CultureInfo.InvariantCulture, " Nearest course: {0:0.#} m³/s, {1:0.#} blocks from its centre line, half width {2:0.#}{3}, depth {4:0.#}{5}.",
                        rh.Q, rh.Dist, rh.HalfWidth, rh.MaskHalfWidth > 0 ? " (measured " + rh.MaskHalfWidth.ToString("0.#", CultureInfo.InvariantCulture) + ")" : "",
                        rh.Depth, rh.Depth > Model.Rivers.Depth(rh.Q) + 1e-9 ? " (deepened ×" + (rh.Depth / Model.Rivers.Depth(rh.Q)).ToString("0.00", CultureInfo.InvariantCulture) + ")" : "");
            }
            lines.Add("Rivers: " + channels + "." + channelHere);
        }

        /// <summary>The generator's own rules here: structures and vanilla animals, the vegetation zones, tall grass, ground
        /// moisture and grass in winter.</summary>
        void Ecology(EarthWorldGen gen, EarthGeometry.GeoPos g, BlockPos ground)
        {
            lines.AddRange(gen.DescribeRanges(ground));
            // ranges step 5a: the share of vanilla's tall grass this world keeps here, by the real aridity index
            var gc = Data.SampleClimate(g.Lat, g.Lon);
            double share = gen.TallGrassShareAt(ground.X, ground.Z);
            var zones = gen.Zones;
            if (zones != null) Zones(gen, zones, g, ground);
            else lines.Add(Params.VanillaBiomes >= 1 ? "Zone: none (vanilla biomes: vanilla's forest and shrub maps)."
                : "Zone: this world has no zone rules (forest and shrubs by rain alone).");
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Tall grass (new chunks): {0} (aridity index {1:0.00}, {2}).",
                double.IsNaN(share) ? "vanilla's density, this world has no ground cover rules" : (100 * share).ToString("0", CultureInfo.InvariantCulture) + "% of vanilla's kept",
                gc.Aridity, gc.PenmanFactor > 0 ? "Penman-Monteith evapotranspiration, x" + gc.PenmanFactor.ToString("0.00", CultureInfo.InvariantCulture) + " the Holdridge index"
                    : "Holdridge evapotranspiration"));
            var moisture = sapi.ModLoader.GetModSystem<EarthGroundMoisture>();
            if (moisture != null && Params.MoistureRules >= 1)
            {
                double gm = moisture.MoistureAt(ground.X, ground.Y, ground.Z, sapi.World.Calendar.TotalHours, out double mt);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Ground moisture now: {0:0.00} (tufts grow from {1:0.00}, wither below {2:0.00}; month's mean at the ground {3:0.0} °C, dormant below 5 °C); only where some tall grass is thinned out.",
                    gm, EarthGroundMoisture.Wet, EarthGroundMoisture.Dry, mt));
            }
            if (Params.GrassWinterRules >= 1)
            {
                var wc = sapi.World.BlockAccessor.GetClimateAt(ground, EnumGetClimateMode.WorldGenValues);
                int target = wc == null ? -1 : GrassWinter.TargetHeight(wc.Temperature, wc.WorldgenRainfall, wc.ForestDensity);
                lines.Add(target < 0 ? "Tall grass in winter: none grows here."
                    : string.Format(CultureInfo.InvariantCulture, "Tall grass in winter: lies flat ({0} at most) when its snow melts and grows back to {1} in months from {2} °C.",
                        GrassWinter.Heights[GrassWinter.Flattened], GrassWinter.Heights[target], GrassWinter.GrowingBase));
            }
        }

        /// <summary>The vegetation zones' weights and density here, then fire, wetlands, the trees' soil and the tree mix.</summary>
        void Zones(EarthWorldGen gen, ClimateZones zones, EarthGeometry.GeoPos g, BlockPos ground)
        {
            var drivers = Model.ZoneDriversAt(ground.X, ground.Z);
            var weights = new double[zones.Count];
            zones.Weights(drivers, weights);
            zones.Density(drivers, out double zf, out double zs);
            var top = Enumerable.Range(0, zones.Count).OrderByDescending(i => weights[i]).Take(3).Where(i => weights[i] >= .05)
                .Select(i => zones.Zones[i].Name + " " + (100 * weights[i]).ToString("0", CultureInfo.InvariantCulture) + "%");
            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "Zone (new chunks): {0}; forest {1:0.00}, shrubs {2:0.00} (growing degree days {3:0}, coldest month {4:0.0} °C, warmest {5:0.0} °C, aridity {6:0.00}, dry summer {7:0.00}, dry months {8:0.0}).",
                string.Join(", ", top), zf, zs, drivers.Gdd5, drivers.TCold, drivers.TWarm, drivers.Aridity, drivers.DrySummer, drivers.DryMonths));
            lines.Add(gen.Model?.NoTreeFloor == true ? "Trees (new chunks): as dense as the forest map says (zone rules 2: without vanilla's floor of a tree every 7 to 8 chunks)."
                : "Trees (new chunks): vanilla's floor adds a tree every 7 to 8 chunks on fertile ground whatever the forest map says (zone rules 1).");
            Fire(gen, g, drivers, zf);
            Wetlands(gen, g);
            TreeSoil(gen, g, drivers);
            var mix = new float[RegionalTreePicker.Groups];
            zones.TreeFactors(drivers, mix);
            lines.Add("Tree mix (forest-type weights, 1 = neutral): " + string.Join(", ", Enumerable.Range(0, mix.Length).OrderByDescending(i => mix[i])
                .Select(i => RegionalTreePicker.GroupName(i) + " " + mix[i].ToString("0.0", CultureInfo.InvariantCulture))) + ".");
        }

        void Fire(EarthWorldGen gen, EarthGeometry.GeoPos g, ZoneDrivers drivers, double zf)
        {
            var fire = gen.Model?.Fire;
            if (fire != null)
            {
                double lapse = Data.SampleClimate(g.Lat, g.Lon).TMean - drivers.TMean;
                FireRegime.Summer(Data.Weather, g.Lat, g.Lon, lapse, out double st, out double sp);
                double soilShare = fire.GrassSoilShare(g.Lat, g.Lon);
                double open = FireRegime.Openness(soilShare, st, sp, drivers.Aridity, drivers.TCold);
                double chance = double.IsNaN(st) ? double.NaN : FireRegime.Chance(soilShare, st, sp, drivers.Aridity);
                double cut = FireRegime.ForestCutFor(chance, fire.Rules);
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "Fire v{7} (new chunks): kept {0:0}% open, forest {1:0.00} -> {2:0.00} (grassland soils {3:0}%, summer {4:0.0} °C and {5:0} mm a month, chance {6:0.00}; open land keeps {8:0}% of the forest).",
                    100 * open, zf, zf * (1 - cut * open), 100 * soilShare, st, sp, chance, fire.Rules, 100 * (1 - cut)));
            }
            else lines.Add("Fire: this world has no fire rules (the zones' forest as it is).");
        }

        void Wetlands(EarthWorldGen gen, EarthGeometry.GeoPos g)
        {
            var wetlands = gen.Model?.Wetlands;
            if (wetlands == null) return;
            string Pct(int layer) => (100 * wetlands.Share(layer, g.Lat, g.Lon)).ToString("0", CultureInfo.InvariantCulture) + "%";
            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "Wetland (GLWD v2): bog {0}, wooded peat {1}, swamp forest {2}, marsh {3}, mangrove {4}, saltmarsh {5}, damp ground {6}; {7}.",
                Pct(WetlandGrid.Bog), Pct(WetlandGrid.PeatForest), Pct(WetlandGrid.SwampForest), Pct(WetlandGrid.Marsh),
                Pct(WetlandGrid.Mangrove), Pct(WetlandGrid.SaltMarsh), Pct(WetlandGrid.Damp),
                gen.Model.OpenWetlands ? "open wetland takes " + (100 * wetlands.OpenShare(g.Lat, g.Lon)).ToString("0", CultureInfo.InvariantCulture) + "% of the forest (new chunks)"
                    : "this world keeps the zones' forest over wetlands"));
            lines.Add(gen.Model.PondRules >= EarthParams.PondRulesNone
                ? "Surface ponds (new chunks): none (this world turned vanilla's surface ponds off); cave ponds as vanilla."
                : gen.Model.PondRules >= 1
                ? "Surface ponds (new chunks): " + (100 * wetlands.PondShare(g.Lat, g.Lon)).ToString("0", CultureInfo.InvariantCulture) + "% of vanilla's tries kept (mapped wetlands only); cave ponds as vanilla."
                : "Surface ponds: vanilla's (this world has no pond rules).");
        }

        /// <summary>Trees by soil (SoilNiche): the site the tree picker sees here and the native species it suits best and worst.</summary>
        void TreeSoil(EarthWorldGen gen, EarthGeometry.GeoPos g, ZoneDrivers drivers)
        {
            var soils = gen.Model?.Soils;
            if (soils == null || RegionalTrees == null) return;
            var site = new TreeSite();
            SoilNiche.Fill(ref site, soils, g.Lat, g.Lon, drivers.Aridity, false);
            SoilNiche.AddWetlands(ref site, gen.Model.Wetlands, g.Lat, g.Lon);
            int treeRegion = RegionalTrees.Geography.FindBlended(g.Lat, g.Lon);
            if (!site.HasSoil) lines.Add("Soil for trees: none mapped here (trees by climate alone).");
            else if (!RegionalTrees.SoilIndicatorsApply(treeRegion))
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "Soil for trees (SoilGrids and GLWD): waterlogged {0:0}%, nutrient-poor {1:0}%, grassland soils {2:0}%, pH {3:0.0}; outside Europe, where EIVE's soil indicators are measured, soil does not sort the trees.",
                    100 * site.WetShare, 100 * site.PoorShare, 100 * site.GrassShare, site.SoilPh));
            else
            {
                var fits = RegionalTrees.Rules.Where(r => r.Niche != null && !r.Cultivated && treeRegion >= 0 && r.RegionMask[treeRegion] && r.Taxa?.Length > 0)
                    // a rule for one species by its name; a generic one (vanilla's fir: 26 firs) by its tree
                    .GroupBy(r => r.Taxa.Length == 1 || r.Generators == null || r.Generators.Length == 0 ? r.Taxa[0]
                        : r.Generators[0].Substring(r.Generators[0].IndexOf(':') + 1) + " (" + r.Taxa.Length + " species)")
                    .Select(x => (Name: x.Key, Fit: x.Max(r => SoilNiche.Fit(r.Niche, site))))
                    .OrderByDescending(f => f.Fit).ToList();
                string List(IEnumerable<(string Name, float Fit)> f) =>
                    string.Join(", ", f.Select(x => x.Name + " " + x.Fit.ToString("0.00", CultureInfo.InvariantCulture)));
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "Soil for trees (SoilGrids and GLWD; new chunks): waterlogged {0:0}%, nutrient-poor {1:0}%, grassland soils {2:0}%, pH {3:0.0}, drained ground moisture {4:0.0} (EIVE 0-10). {5}",
                    100 * site.WetShare, 100 * site.PoorShare, 100 * site.GrassShare, site.SoilPh, site.SoilMoisture,
                    fits.Count == 0 ? "No native tree here has EIVE soil indicators (all fit every soil)."
                        : "Soil fit of the native trees with EIVE indicators, best: " + List(fits.Take(4))
                          + (fits.Count > 4 ? "; worst: " + List(fits.Skip(Math.Max(4, fits.Count - 3))) : "") + "."));
            }
        }

        /// <summary>Wild catalogue plants that may grow here (plants as around 1318).</summary>
        void Placement(EarthGeometry.GeoPos g, BlockPos ground)
        {
            var placement = sapi.ModLoader.GetModSystem<EarthPlacement>()?.Catalogue;
            if (placement == null) return;
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            double lapse = TerrainModel.Lapse(Model.Vertical, ground.Y);
            int at = placement.RegionAt(g.Lat, g.Lon);
            var wild = placement.Taxa.Where(t => t.Kind == "wild").ToList();
            var grows = wild.Where(t => placement.Allows(t, at, cs.TMean - lapse, cs.TCold - lapse, cs.Rain)).Select(t => t.Name).ToList();
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Wild catalogue plants that may grow here ({0} of {1}; mean {2:0.0} °C, coldest month {3:0.0} °C, rain {4:0}): {5}.",
                grows.Count, wild.Count, cs.TMean - lapse, cs.TCold - lapse, cs.Rain, grows.Count == 0 ? "none" : string.Join(", ", grows)));
        }

        void Geology(EntityPos pos)
        {
            var geology = sapi.ModLoader.GetModSystem<EarthGeology>();
            if (geology?.Active == true)
            {
                var s = geology.SampleColumn(pos.X, pos.Z);
                lines.Add("Regional geology: " + (s.HasRegionalBedrock ? "mapped bedrock here" : "no mapped bedrock here (vanilla strata)") + ".");
            }
            else lines.Add("Regional geology: off in this world.");
        }
    }
}
