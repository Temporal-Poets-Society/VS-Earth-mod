using System;
using System.Globalization;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// /earth and its subcommands (server side): where a player is on the Earth and what this world's rules make of the
    /// place, teleports, test helpers, and the diagnostics the self-test runs too (the river audit, the world map). The
    /// world's state is EarthMapSystem's; this class only reads it.
    /// </summary>
    sealed class EarthCommands
    {
        readonly EarthMapSystem core;
        readonly ICoreServerAPI sapi;
        readonly ILogger logger;

        public EarthCommands(EarthMapSystem core, ICoreServerAPI sapi, ILogger logger)
        {
            this.core = core;
            this.sapi = sapi;
            this.logger = logger;
        }

        EarthParams Params => core.Params;
        TerrainModel Model => core.Model;
        EarthData Data => core.Data;
        RegionalTreeCatalog RegionalTrees => core.RegionalTrees;
        static string FormatLatLon(double lat, double lon) => EarthMapSystem.FormatLatLon(lat, lon);
        EarthElevationSystem Elevation => sapi.ModLoader.GetModSystem<EarthElevationSystem>();

        public void Register(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("earth")
                .WithDescription("Earth Map: where you are on the Earth, and test helpers")
                .RequiresPrivilege(Privilege.chat)
                // no RequiresPlayer here: the game checks the root's preconditions before any subcommand, so the server
                // console could run none of them (CR-L2); each subcommand that reads the caller's position requires a player
                .HandleWith(args => args.Caller.Player == null
                    ? TextCommandResult.Error("Caller must be player. From the console: /earth story, /earth map, /earth checkrivers, /earth elevation, /earth date.")
                    : CmdInfo(args))
                .BeginSubCommand("info")
                    .WithDescription("Latitude, longitude, real elevation and climate here")
                    .RequiresPlayer()
                    .HandleWith(CmdInfo)
                .EndSubCommand()
                .BeginSubCommand("where")
                    .WithDescription("Which botanical region (WGSRPD) this place belongs to for every range (trees, plants, animals), how it was found, and which regional datasets cover it")
                    .RequiresPlayer()
                    .HandleWith(CmdWhere)
                .EndSubCommand()
                .BeginSubCommand("trees")
                    .WithDescription("Native tree region and installed species candidates here (climate still applies)")
                    .RequiresPlayer()
                    .HandleWith(CmdTrees)
                .EndSubCommand()
                .BeginSubCommand("geology")
                    .WithDescription("Regional rocks, surface materials and geological data coverage here")
                    .RequiresPlayer()
                    .HandleWith(CmdGeology)
                .EndSubCommand()
                .BeginSubCommand("water")
                    .WithDescription("Water table here (decision 0059): its height and depth, the drainage base, aridity, and the aquifer rating of each chunk of this column, with Hydrate or Diedrate's stored data")
                    .RequiresPlayer()
                    .HandleWith(CmdWater)
                .EndSubCommand()
                .BeginSubCommand("site")
                    .WithDescription("Site drivers here (EA-0054 step 1): heat, light by slope and aspect, water supply, soil richness, wetness from the water table, disturbance, and the site type they make")
                    .RequiresPlayer()
                    .HandleWith(CmdSite)
                .EndSubCommand()
                .BeginSubCommand("ores")
                    .WithDescription("Regional ore evidence, reported burial and saved background settings here")
                    .RequiresPlayer()
                    .HandleWith(CmdOres)
                .EndSubCommand()
                .BeginSubCommand("propick")
                    .WithDescription("The prospecting pick's question here, for child deposits (silver in galena, gold in quartz, sylvite in halite, peridot in olivine): each one's ore map value, its parent's ore blocks in vanilla's simulated column, and the pick's factor, here or over every 4th column within a radius (at most 64) (EA-0075)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 0), parsers.OptionalWord("old"))
                    .HandleWith(CmdPropick)
                .EndSubCommand()
                .BeginSubCommand("orelayers")
                    .WithDescription("Ores in their ground layer (decision 0060): deposit blocks in loaded chunks around you by layer rule and layer class (radius in blocks, default 32, at most 128), and the layer pass's counts this session")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 32))
                    .HandleWith(CmdOreLayers)
                .EndSubCommand()
                .BeginSubCommand("fauna")
                    .WithDescription("Mapped fauna candidates (natural or current ranges) and regional coverage here")
                    .RequiresPlayer()
                    .HandleWith(CmdFauna)
                .EndSubCommand()
                .BeginSubCommand("plants")
                    .WithDescription("Native range of regional ground plants (Floral Zones) here: botanical country and gated patches allowed/blocked")
                    .RequiresPlayer()
                    .HandleWith(CmdPlants)
                .EndSubCommand()
                .BeginSubCommand("placement")
                    .WithDescription("Plants as around 1318 here: botanical country, climate, catalogue plants in range and climate, fruit trees placed this session")
                    .RequiresPlayer()
                    .HandleWith(CmdPlacement)
                .EndSubCommand()
                .BeginSubCommand("siterules")
                    .WithDescription("Site rules for chunks generated from now on in this session (EA-0054 step 2): 0 or 1, not saved, for comparing with /wgen regenk; no argument shows the world's. Test worlds: a world not in creative mode needs 'anyway' after the number (its new chunks keep these rules for good)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalInt("rules", -1), parsers.OptionalWord("anyway"))
                    .HandleWith(args =>
                    {
                        int v = (int)args[0];
                        if (Model == null) return TextCommandResult.Success("Earth is not active in this world.");
                        // (S-L4, code review 14 November 2026) a comparison tool for test worlds: elsewhere only when asked for twice
                        bool creative = string.Equals(sapi.World.Config.GetString("gameMode", ""), "creative", StringComparison.OrdinalIgnoreCase);
                        if (v >= 0 && v != Model.SiteRules && v != (Params?.SiteRules ?? 0) && !creative && args[1] as string != "anyway")
                            return TextCommandResult.Error("This world is not a creative test world: chunks generated while the site rules differ from the save (" + (Params?.SiteRules ?? 0) +
                                ") keep them for good. Add 'anyway' to switch: /earth siterules " + v + " anyway");
                        if (v >= 0 && v != Model.SiteRules)
                        {
                            Model.SiteRules = Math.Min(v, SiteNiche.RulesVersion);
                            if (Model.SiteRules != (Params?.SiteRules ?? 0))
                                logger.Warning("Earth: /earth siterules {0} by {1}: chunks generated from now until the world is reopened keep site rules {0} for good (the save says {2}).",
                                    Model.SiteRules, args.Caller.Player?.PlayerName ?? "the console", Params?.SiteRules ?? 0);
                            else logger.Notification("Earth: /earth siterules {0}: back to the saved site rules.", Model.SiteRules);
                        }
                        return TextCommandResult.Success("Site rules now " + Model.SiteRules + " (saved: " + (Params?.SiteRules ?? 0) + ")" +
                            (Model.SiteRules != (Params?.SiteRules ?? 0) ? ". Chunks generated now keep these rules permanently; the saved rules return when the world is reopened." : "."));
                    })
                .EndSubCommand()
                .BeginSubCommand("woods")
                    .WithDescription("Trees in loaded chunks around you by wood (radius in blocks, default 48, at most 128): trunk columns and their mean height; or trace: count the tree and shrub picks of generation from now on (EA-0054)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalWord("radius or trace"))
                    .HandleWith(CmdWoods)
                .EndSubCommand()
                .BeginSubCommand("census")
                    .WithDescription("Count catalogue plants, other crops and berry bushes, fruit trees and animals in loaded chunks around you (radius in blocks, default 64, at most 256)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 64))
                    .HandleWith(CmdCensus)
                .EndSubCommand()
                .BeginSubCommand("cost")
                    .WithDescription("Measure what Earth's climate hook costs in this session (on, off, or no argument for the figures so far)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("onoff"))
                    .HandleWith(args =>
                    {
                        if (Params?.Enabled != true) return NotActive();
                        string w = args[0] as string;
                        if (w == "on") { ClimateCost.Start(); return TextCommandResult.Success("Counting the climate hook's calls (both sides of this process)."); }
                        if (w == "off") ClimateCost.On = false;
                        return TextCommandResult.Success(ClimateCost.Report());
                    })
                .EndSubCommand()
                .BeginSubCommand("snowlight")
                    .WithDescription("Relight where snow melts (EA-0063): on (default) or off for a comparison")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("onoff"))
                    .HandleWith(args =>
                    {
                        if (Params?.Enabled != true) return NotActive();
                        string w = args[0] as string;
                        if (w == "on" || w == "off") SnowMeltLight.Paused = w == "off";
                        return TextCommandResult.Success("Relight where snow melts: " + (SnowMeltLight.Paused ? "off" : "on") + ".");
                    })
                .EndSubCommand()
                .BeginSubCommand("relight")
                    .WithDescription("Relight the loaded chunks around you (radius in blocks, default 32, at most 128): rock left dark where snow melted before Earth relit melting snow (EA-0063)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 32))
                    .HandleWith(args => Params?.Enabled != true ? NotActive() : TextCommandResult.Success("Relit " + SnowMeltLight.FullRelight(sapi, args.Caller.Player.Entity.Pos.AsBlockPos, (int)args[0]) + "."))
                .EndSubCommand()
                .BeginSubCommand("tp")
                    .WithDescription("Teleport to a latitude and longitude in degrees (south and west negative), e.g. /earth tp 51.5 -0.12. " +
                        "Aboard a boat or ship, or with 'water' at the end, you land on the nearest open water instead.")
                    .RequiresPrivilege(Privilege.tp)
                    .RequiresPlayer()
                    .WithArgs(parsers.Double("lat"), parsers.Double("lon"), parsers.OptionalWord("water"))
                    .HandleWith(CmdTp)
                .EndSubCommand()
                .BeginSubCommand("map")
                    .WithDescription("Render the whole Earth as this world generates it to a PNG (blocks per pixel, default 128)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalInt("blocksPerPixel", 128))
                    .HandleWith(CmdMap)
                .EndSubCommand()
                .BeginSubCommand("story")
                    .WithDescription("List the story locations with their real coordinates")
                    // (D-L5 of the Fable pass) every story location's place, which vanilla leaves to be found: the server's
                    // admins only, as /earth map and the other whole-world listings
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(CmdStory)
                .EndSubCommand()
                .BeginSubCommand("checkrivers")
                    .WithDescription("Walk the centre line of every river in this world and list where the water is broken (blocked, " +
                        "running uphill, falls of 2+ blocks), with map coordinates, to VintagestoryData/EarthMap/rivercheck-<seed>.txt")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(CmdCheckRivers)
                .EndSubCommand()
                .BeginSubCommand("elevation")
                    .WithDescription("Land heights from Copernicus GLO-90 (decision 0051, test): this world's source and the tiles on the server")
                    .HandleWith(args => TextCommandResult.Success(Elevation.StatusText(args.Caller.Player?.HasPrivilege(Privilege.controlserver) ?? true)))
                    .BeginSubCommand("download")
                        .WithDescription("Download every GLO-90 square not on the server yet into VintagestoryData/ModData/earth/glo90: from Earth's " +
                            "data release (about 8.6 GB, already converted), or with 'copernicus' from Copernicus's originals (about 71 GB, converted " +
                            "here, hours). Stop with /earth elevation cancel; squares already here are skipped next time.")
                        .RequiresPrivilege(Privilege.controlserver)
                        .WithArgs(parsers.Word("all"), parsers.OptionalWord("from"))
                        .HandleWith(args => Params?.Enabled != true || Params.FineElevation < 1 ? TextCommandResult.Error("This world doesn't read GLO-90 (it isn't an Earth world, or one on the built-in heights); nothing to download.")
                            : (string)args[0] != "all"
                            ? TextCommandResult.Error("Type /earth elevation download all (about 8.6 GB from Earth's data release), or /earth elevation download all copernicus.")
                            : args[1] is string from && from.Length > 0 && from != "copernicus" && from != "release"
                            ? TextCommandResult.Error("From 'release' (the default) or 'copernicus'.")
                            : TextCommandResult.Success(Elevation.StartAll(args.Caller.Player?.PlayerName ?? "the server console",
                                args[1] as string == "copernicus" ? Core.DemDownloader.Origin.Copernicus : Core.DemDownloader.Origin.Repo)))
                    .EndSubCommand()
                    .BeginSubCommand("cancel")
                        .WithDescription("Stop downloading all GLO-90 tiles (the tiles being fetched finish)")
                        .RequiresPrivilege(Privilege.controlserver)
                        .HandleWith(args => TextCommandResult.Success(Elevation.CancelAll(args.Caller.Player?.PlayerName ?? "the server console")))
                    .EndSubCommand()
                .EndSubCommand()
                .BeginSubCommand("boattest")
                    .WithDescription("Build four 100-block test canals next to you (flowing east) to try boats on river steps: " +
                        "1 ramps, 2 bare 1-block steps, 3 a cascade of ramps, 4 rapids")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .HandleWith(CmdBoatTest)
                .EndSubCommand();
            EarthDate.Register(api, api.ChatCommands.Get("earth"));
        }

        TextCommandResult CmdTrees(TextCommandCallingArgs args)
        {
            if (RegionalTrees == null || Model == null) return TextCommandResult.Success("Regional tree allocation is disabled in this save.");
            var pos = args.Caller.Player.Entity.Pos;
            var g = Model.Geometry.ToGeo(pos.X, pos.Z);
            return TextCommandResult.Success(g.InGutter || g.BeyondPole ? "Outside the Earth." : RegionalTrees.Describe(g.Lat, g.Lon));
        }

        TextCommandResult CmdGeology(TextCommandCallingArgs args)
        {
            var geology = sapi.ModLoader.GetModSystem<EarthGeology>();
            if (geology?.Active != true || Model == null)
                return TextCommandResult.Success("Regional geology is disabled in this save.");
            var pos = args.Caller.Player.Entity.Pos;
            return TextCommandResult.Success(geology.Describe(pos.X, pos.Z));
        }

        TextCommandResult CmdOreLayers(TextCommandCallingArgs args)
        {
            var geology = sapi.ModLoader.GetModSystem<EarthGeology>();
            var layers = geology?.Active == true ? geology.Layers : null;
            if (layers == null || Model == null)
                return TextCommandResult.Success("Ores in their ground layer (0060) are off in this save: it needs a world made with ground columns by this version.");
            int radius = Math.Clamp((int)args[0], 1, 128);
            var pos = args.Caller.Player.Entity.Pos.AsBlockPos;
            return TextCommandResult.Success(layers.Census(sapi.World.BlockAccessor, geology, pos.X, pos.Z, radius));
        }

        TextCommandResult CmdWater(TextCommandCallingArgs args)
        {
            var water = sapi.ModLoader.GetModSystem<EarthWaterTable>();
            if (water == null || Model == null) return TextCommandResult.Success("Earth's water table is off in this world.");
            var pos = args.Caller.Player.Entity.Pos;
            return TextCommandResult.Success(water.Describe(pos.X, pos.Y, pos.Z));
        }

        /// <summary>
        /// /earth site: the site drivers at the player's column (src/Core/SiteDrivers.cs; EA-0054 step 1). Read-only: the
        /// world's generation does not read them yet.
        /// </summary>
        TextCommandResult CmdSite(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return TextCommandResult.Success("Earth is not active in this world.");
            try
            {
                var pos = args.Caller.Player.Entity.Pos;
                double x = pos.X, z = pos.Z;
                var c = Model.GetColumn(x, z);
                var g = Model.TerrainGeoAt(x, z);
                if (c.Gutter || g.InGutter || g.BeyondPole) return TextCommandResult.Success("No site drivers here: outside the Earth (seam sea or beyond a pole).");
                double elev = c.Elev;
                var cs = Data.SampleClimate(g.Lat, g.Lon);
                // as the terrain model's climate: below sea level (Dead Sea, Caspian) it warms
                double lapse = 0.0065 * elev;
                var input = SiteInput.Create(g.Lat, g.Lon);
                input.ElevationM = elev;
                input.TMean = cs.TMean - lapse; input.TCold = cs.TCold - lapse; input.TWarm = cs.TWarm - lapse;
                input.Rain = cs.Rain; input.DrySummer = cs.DrySummer; input.AridityIndex = cs.Aridity;
                input.OpenWater = c.Water != TerrainModel.WaterNone || c.River && !c.DryRiver;

                // slope and aspect: a least-squares plane through 5 x 5 real heights about 100 m apart (a 400 m square);
                // a plain two-point difference reads single blocks and forest edges of the surface model as slopes
                double metresX = Math.Max(1e-3, Geo.MetresPerDegree * Math.Cos(g.Lat * Math.PI / 180) / Model.Geometry.BlocksPerDegreeX);
                double metresZ = Geo.MetresPerDegree / Model.Geometry.BlocksPerDegreeZ;
                int d = Math.Max(1, (int)Math.Round(100 / metresZ));
                double E(double px, double pz)
                {
                    var n = Model.GetColumn(px, pz);
                    return n.WaterTop > n.Top ? Math.Max(n.Elev, Model.Vertical.Elevation(n.WaterTop + 1)) : n.Elev;
                }
                SiteTerrain.PlaneSlope(E, x, z, d, metresX, metresZ, out input.SlopeRad, out input.AspectRad);

                // wetness and soil: the water table (0059) and the ground column at this place
                var water = sapi.ModLoader.GetModSystem<EarthWaterTable>();
                bool table = false, ground = false;
                if (water != null && water.SiteWater(x, z, out double depth, out double aboveBase, out double wetland, out bool openWater, out var sample, out ground))
                {
                    table = true;
                    input.WaterTableDepthM = depth; input.HeightAboveDrainageM = aboveBase; input.WetlandShare = wetland;
                    input.OpenWater |= openWater;
                    if (ground) input.Soil = SiteSoil.Input.FromSample(sample);
                }

                // coast: a coarse ring search for sea columns out to 10 km (rings 15 % apart, 32 directions)
                double mpb = Math.Max(1e-3, (metresX + metresZ) / 2);
                input.SeaDistanceM = SiteTerrain.SeaDistance((px, pz) => Model.GetColumn(px, pz).Water == TerrainModel.WaterSea, x, z, mpb, 10000);

                // flood: height above the nearest river
                if (Model.Rivers != null)
                {
                    Model.ProjectTerrain(x, z, out double sx, out double sz);
                    if (Model.Rivers.Nearest(sx, sz, out var hit))
                    {
                        input.RiverDistanceM = hit.Dist * mpb;
                        input.HeightAboveRiverM = c.River ? 0 : Math.Max(0, elev - Model.Vertical.Elevation(hit.WaterTop + 1));
                    }
                }

                var site = SiteDrivers.Compute(input);
                string head = string.Format(CultureInfo.InvariantCulture, "Site at {0}, {1:0} m real elevation{2} (slope: plane through heights over {3:0} m):",
                    FormatLatLon(g.Lat, g.Lon), elev, input.OpenWater ? ", water" : "", 4 * d * metresZ);
                string notes = (table ? "" : "\n(no water table in this world: wetness only from the soil)") + (ground ? "" : "\n(soil: no ground column here)");
                return TextCommandResult.Success(head + "\n" + site.Describe() + notes);
            }
            catch (Exception e)
            {
                logger.Error("Earth /earth site failed: {0}", e);
                return TextCommandResult.Error("Site drivers failed here: " + e.Message);
            }
        }

        /// <summary>
        /// /earth propick [radius] (EA-0075): what the prospecting pick computes for each child deposit (silver, gold,
        /// sylvite, peridot), the same way ItemProspectingPick.GenProbeResults does, Earth's reading prefix included: at the
        /// player's column, or over every 4th column within the radius. A child's hosts are its parent's ore blocks in
        /// vanilla's simulated column, which a one-block column crosses only where a parent's disc passes.
        /// </summary>
        TextCommandResult CmdPropick(TextCommandCallingArgs args)
        {
            if (!(sapi.ObjectCache.TryGetValue("propickworkspace", out object o) && o is Vintagestory.GameContent.ProPickWorkSpace ppws))
                return TextCommandResult.Success("Use a prospecting pick once in this session first (the game makes the pick's workspace then).");
            var ba = sapi.World.BlockAccessor;
            var here = args.Caller.Player.Entity.Pos.AsBlockPos;
            int radius = Math.Clamp((int)args[0], 0, 64), step = radius == 0 ? 1 : 4, rs = ba.RegionSize;
            // "old": the reading as before EA-0075's fix (Earth's rock over the simulated ores), for a comparison
            bool old = args[1] as string == "old";
            EarthOreLayers.ReadingAsBefore = old;
            try
            {
                var stats = new System.Collections.Generic.Dictionary<string, (double Map, int Hosted, int Read, double Max)>();
                var ores = new System.Collections.Generic.Dictionary<string, int>();
                var bearingOf = new System.Collections.Generic.Dictionary<Vintagestory.ServerMods.DiscDepositGenerator, System.Collections.Generic.HashSet<int>>();
                int columns = 0;
                for (int dz = -radius; dz <= radius; dz += step)
                    for (int dx = -radius; dx <= radius; dx += step)
                    {
                        var pos = new BlockPos(here.X + dx, 0, here.Z + dz, 0);
                        pos.Y = ba.GetTerrainMapheightAt(pos);
                        var region = ba.GetMapRegion(pos.X / rs, pos.Z / rs);
                        if (region?.OreMaps == null || pos.Y <= 0) continue;
                        int[] column = ppws.GetRockColumn(pos.X, pos.Z);
                        columns++;
                        foreach (int id in column)
                            if (id > 0 && sapi.World.Blocks[id]?.BlockMaterial == EnumBlockMaterial.Ore)
                            {
                                string c = sapi.World.Blocks[id].Code.Path;
                                ores[c] = ores.TryGetValue(c, out int n) ? n + 1 : 1;
                            }
                        foreach (var pair in region.OreMaps)
                        {
                            if (!ppws.depositsByCode.TryGetValue(pair.Key, out var variant) || OreVanilla.ParentOf(variant) == null) continue;
                            var map = pair.Value;
                            int dist = map.GetUnpaddedColorLerped((float)(pos.X % rs) / rs * map.InnerSize, (float)(pos.Z % rs) / rs * map.InnerSize);
                            int hosts = 0;
                            if (variant.GeneratorInst is Vintagestory.ServerMods.DiscDepositGenerator disc)
                            {
                                if (!bearingOf.TryGetValue(disc, out var bearing)) bearingOf[disc] = bearing = new System.Collections.Generic.HashSet<int>(disc.GetBearingBlocks());
                                foreach (int id in column) if (bearing.Contains(id)) hosts++;
                            }
                            variant.GetPropickReading(pos, dist, (int[])column.Clone(), out _, out double factor);
                            string key = variant.Code + " in " + OreVanilla.ParentOf(variant).Code;
                            var s = stats.TryGetValue(key, out var v) ? v : (0, 0, 0, 0);
                            stats[key] = (Math.Max(s.Map, (dist & 0xFF) / 255.0), s.Hosted + (hosts > 0 ? 1 : 0), s.Read + (factor > 0 ? 1 : 0), Math.Max(s.Max, factor));
                        }
                    }
                var sb = new System.Text.StringBuilder(string.Format(CultureInfo.InvariantCulture,
                    "The pick's question for child deposits{2}, {0} column(s) within {1} blocks:", columns, radius, old ? " (as before EA-0075)" : ""));
                sb.Append("\nOre blocks in vanilla's simulated columns: " + (ores.Count == 0 ? "none" : string.Join(", ", ores.OrderByDescending(e => e.Value).Select(e => e.Key + " x" + e.Value))));
                foreach (var e in stats.OrderBy(e => e.Key, StringComparer.Ordinal))
                    sb.Append('\n').AppendFormat(CultureInfo.InvariantCulture, "{0}: ore map up to {1:0.000}; parent ore in {2} column(s), a reading in {3}, factor up to {4:0.0000}",
                        e.Key, e.Value.Map, e.Value.Hosted, e.Value.Read, e.Value.Max);
                return TextCommandResult.Success(sb.ToString());
            }
            finally { EarthOreLayers.ReadingAsBefore = false; }
        }

        TextCommandResult CmdFauna(TextCommandCallingArgs args)
        {
            var fauna = sapi.ModLoader.GetModSystem<EarthFaunaSystem>();
            if (fauna == null || Model == null)
                return TextCommandResult.Success("Regional fauna is not available in this world.");
            var pos = args.Caller.Player.Entity.Pos;
            // Describe also explains the inactive cases (disabled in this save, no supported fauna mod installed, …).
            return TextCommandResult.Success(fauna.Describe(pos.X, pos.Z));
        }

        TextCommandResult CmdPlants(TextCommandCallingArgs args)
        {
            var plants = sapi.ModLoader.GetModSystem<EarthPlants>();
            if (plants == null || Model == null) return TextCommandResult.Success("Regional ground plants are not available in this world.");
            var pos = args.Caller.Player.Entity.Pos;
            return TextCommandResult.Success(plants.Describe(pos.X, pos.Z));
        }

        TextCommandResult CmdPlacement(TextCommandCallingArgs args)
        {
            var placement = sapi.ModLoader.GetModSystem<EarthPlacement>();
            if (placement == null || Model == null) return TextCommandResult.Success("Earth placement is not available in this world.");
            var pos = args.Caller.Player.Entity.Pos;
            return TextCommandResult.Success(placement.Describe(pos.X, pos.Y, pos.Z));
        }

        /// <summary>
        /// /earth woods (EA-0054): the trees standing in loaded chunks around the player, by wood: every column whose lowest
        /// log stands on something else counts as one trunk column (a thick trunk counts each of its columns), with the
        /// height of its log run; runs under <see cref="MinTreeLogs"/> logs count as shrubs. For comparing species mixes and tree sizes between places and builds.
        /// </summary>
        /// <summary>A log run shorter than this is a shrub (vanilla's shrubs grow from oak and other logs).</summary>
        const int MinTreeLogs = 4;

        TextCommandResult CmdWoods(TextCommandCallingArgs args)
        {
            var ba = sapi.World.BlockAccessor;
            var at = args.Caller.Player.Entity.Pos.AsBlockPos;
            string a = args[0] as string;
            if (a == "trace")
            {
                // count the picks themselves (generation from now on, e.g. after /wgen regenk), not the logs they leave
                if (EarthTrees.TracePicks) { EarthTrees.TracePicks = false; return TextCommandResult.Success(EarthTrees.TraceReport() + " (trace off)"); }
                EarthTrees.ResetTrace(); EarthTrees.TracePicks = true;
                return TextCommandResult.Success("Counting tree and shrub picks; /earth woods trace again shows them and stops.");
            }
            int radius = Math.Clamp(int.TryParse(a, out int ra) ? ra : 48, 8, 128);
            var count = new System.Collections.Generic.Dictionary<string, (int Trunks, long Height)>();
            var pos = new BlockPos(0, 0, 0, 0);
            int columns = 0, shrubs = 0;
            string WoodOf(Block b) => b?.Code == null || !b.Code.Path.StartsWith("log-grown-", StringComparison.Ordinal) ? null : b.Variant?["wood"] ?? b.Code.Path;
            for (int z = at.Z - radius; z <= at.Z + radius; z++)
                for (int x = at.X - radius; x <= at.X + radius; x++)
                {
                    pos.Set(x, 0, z);
                    if (ba.GetChunkAtBlockPos(pos) == null) continue;
                    columns++;
                    int ground = ba.GetTerrainMapheightAt(pos), top = Math.Min(ba.MapSizeY - 2, ba.GetRainMapHeightAt(pos));
                    for (int y = Math.Max(1, ground - 2); y <= top; y++)
                    {
                        pos.Y = y;
                        string wood = WoodOf(ba.GetBlock(pos));
                        if (wood == null) continue;
                        pos.Y = y - 1;
                        if (WoodOf(ba.GetBlock(pos)) != null) continue;
                        int h = 1;
                        for (pos.Y = y + 1; pos.Y <= top && WoodOf(ba.GetBlock(pos)) == wood; pos.Y++) h++;
                        if (h < MinTreeLogs) { shrubs++; break; }
                        var c = count.TryGetValue(wood, out var v) ? v : (Trunks: 0, Height: 0L);
                        count[wood] = (c.Trunks + 1, c.Height + h);
                        break;
                    }
                }
            int total = count.Values.Sum(v => v.Trunks);
            var sb = new System.Text.StringBuilder(string.Format(CultureInfo.InvariantCulture, "Trees within {0} blocks ({1} loaded columns): {2} trunk columns of {3}+ logs ({4} shorter: shrubs)", radius, columns, total, MinTreeLogs, shrubs));
            foreach (var e in count.OrderByDescending(e => e.Value.Trunks))
                sb.AppendFormat(CultureInfo.InvariantCulture, "; {0} {1} ({2:0%}, mean height {3:0.0})", e.Key, e.Value.Trunks, (double)e.Value.Trunks / total, (double)e.Value.Height / e.Value.Trunks);
            return TextCommandResult.Success(sb.ToString());
        }

        TextCommandResult CmdCensus(TextCommandCallingArgs args)
        {
            var placement = sapi.ModLoader.GetModSystem<EarthPlacement>();
            if (placement == null || Model == null) return TextCommandResult.Success("Earth placement is not available in this world.");
            return TextCommandResult.Success(placement.Census(args.Caller.Player.Entity.Pos.AsBlockPos, (int)args[0]));
        }

        TextCommandResult CmdOres(TextCommandCallingArgs args)
        {
            var ores = sapi.ModLoader.GetModSystem<EarthOres>();
            if (ores?.Active != true || Model == null) return TextCommandResult.Success("Regional ores are disabled in this save.");
            var pos = args.Caller.Player.Entity.Pos;
            return TextCommandResult.Success(ores.Describe(pos.X, pos.Z) + ". Positive evidence is not a guarantee of ore at this depth. Unknown suitable hosts retain the saved background chance.");
        }

        /// <summary>
        /// Test canals for boats, built like the generated rivers: flat still-water pools, and below each 1-block step down
        /// a ramp of flowing water (heights 6..1 over 6 blocks) on the lower pool. Lanes (south of the player, flowing
        /// east): 1 two ramps 33 blocks apart, 2 the same steps without ramps, 3 five ramps 6 blocks apart (a cascade),
        /// 4 the same cascade in rapid water.
        /// </summary>
        TextCommandResult CmdBoatTest(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true) return NotActive();
            var world = sapi.World;
            int rock = world.GetBlock(new AssetLocation("game:rock-granite"))?.Id ?? 0;
            int still = world.GetBlock(new AssetLocation("game:water-still-7"))?.Id ?? 0;
            if (rock == 0 || still == 0) return TextCommandResult.Error("Vanilla rock or water blocks not found.");
            int Flow(bool rapid, int h) => world.GetBlock(new AssetLocation((rapid ? "game:rapidwater-e-" : "game:water-e-") + h))?.Id ?? 0;
            var lanes = new (int[] Drops, bool Ramp, bool Rapid)[]
            {
                (new[] { 33, 66 }, true, false),
                (new[] { 33, 66 }, false, false),
                (new[] { 30, 36, 42, 48, 54 }, true, false),
                (new[] { 30, 36, 42, 48, 54 }, true, true),
            };
            const int Len = 100, Width = 5, Depth = 3;
            var pos = args.Caller.Entity.Pos;
            int x0 = (int)Math.Floor(pos.X) + 3, z0 = (int)Math.Floor(pos.Z) + 3, yTop = (int)Math.Floor(pos.Y) - 1;
            int dim = args.Caller.Entity.Pos.Dimension;
            var ba = world.GetBlockAccessorBulkUpdate(true, true);
            var bp = new BlockPos(dim);
            for (int l = 0; l < lanes.Length; l++)
            {
                var lane = lanes[l];
                int zl = z0 + l * (Width + 1);
                for (int d = -1; d <= Len; d++)
                {
                    int dd = Math.Clamp(d, 0, Len - 1);
                    int n = lane.Drops.Count(x => x <= dd);
                    int wt = yTop - n;
                    int since = n > 0 ? dd - lane.Drops[n - 1] : 99;
                    int ramp = lane.Ramp && since < 6 ? 6 - since : 0;
                    int floor = wt - Depth;
                    for (int dz = 0; dz <= Width + 1; dz++)
                    {
                        bool wall = dz == 0 || dz == Width + 1 || d < 0 || d >= Len;
                        for (int y = floor; y <= yTop + 6; y++)
                        {
                            bp.Set(x0 + d, y, zl + dz);
                            int solid = 0, fluid = 0;
                            if (y == floor || (wall && y <= yTop + 1)) solid = rock;
                            else if (y <= wt) fluid = still;
                            else if (y == wt + 1 && ramp > 0) fluid = Flow(lane.Rapid, ramp);
                            ba.SetBlock(solid, bp, BlockLayersAccess.Solid);
                            ba.SetBlock(fluid, bp, BlockLayersAccess.Fluid);
                        }
                    }
                }
            }
            ba.Commit();
            return TextCommandResult.Success(string.Format(CultureInfo.InvariantCulture,
                "Built 4 test canals from x={0} to {1}, z={2}..{3}, flowing east (downstream = +x). Lanes from north to south: " +
                "1 ramps (steps at 33 and 66), 2 bare 1-block steps, 3 cascade of 5 ramps, 4 rapids. Put a boat in at the east end and go upstream.",
                x0, x0 + Len - 1, z0, z0 + lanes.Length * (Width + 1)));
        }

        /// <summary>The answer of a command that needs an Earth world (ER-23: test and admin commands ran in vanilla worlds too).</summary>
        TextCommandResult NotActive() => TextCommandResult.Error("Earth Map is not active in this world (it only generates new worlds).");

        /// <summary>/earth where (WhereReport).</summary>
        TextCommandResult CmdWhere(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            return WhereReport.Run(core, sapi, args);
        }

        TextCommandResult CmdInfo(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            var pos = args.Caller.Entity.Pos;
            int missed = FineElevation.MissedReads;
            var c = Model.GetColumn(pos.X, pos.Z);
            string approximate = FineElevation.MissedReads != missed ? Approximate : "";
            Model.ClimateAt(c.Lat, c.Lon, out double t, out double rain, out _);
            var cs = Data.SampleClimate(c.Lat, c.Lon);
            double tHere = Model.TrueTemperature(pos.X, pos.Z, pos.Y);
            string water = c.Water switch { TerrainModel.WaterSea => "sea", TerrainModel.WaterLake => "lake", TerrainModel.WaterSaltLake => "salt lake", _ => "land" };
            return TextCommandResult.Success(string.Format(CultureInfo.InvariantCulture,
                "{0}{1}. Ground: {2:0} m ({3}, top block y={4}{5}). Climate: yearly mean {6:0.0} °C at the surface ({7:0.0} °C at your height), " +
                "coldest month {8:0.0} °C, warmest {9:0.0} °C, {10:0} mm/year (VS rainfall {11:0}).{12}",
                FormatLatLon(c.Lat, c.Lon), c.Gutter ? (c.Water != TerrainModel.WaterNone ? " (seam sea)" : " (seam bridge)") : "", c.Elev, water, c.Top,
                c.IceBottom <= c.Top ? string.Format(", ice {0} blocks", c.Top - c.IceBottom + 1) : "",
                t, tHere, cs.TCold - EarthWorldGen.LapsePerMetre * Math.Max(0, c.Elev), cs.TWarm - EarthWorldGen.LapsePerMetre * Math.Max(0, c.Elev), cs.Precip, rain, approximate));
        }

        /// <summary>ER-30: said where a column was read on the built-in heights because a GLO-90 tile isn't on this machine yet
        /// (the main thread may not wait for one; chunk generation does).</summary>
        const string Approximate = " Heights here are approximate: a GLO-90 tile is not on this machine yet (chunks wait for it).";

        TextCommandResult CmdTp(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            // the game's number parser takes "NaN", and Math.Clamp passes it through to the teleport (CR-L5)
            if (!double.IsFinite((double)args[0]) || !double.IsFinite((double)args[1])) return TextCommandResult.Error("Latitude and longitude must be numbers, e.g. /earth tp 51.5 -0.12.");
            double lat = Math.Clamp((double)args[0], -89.9, 89.9), lon = Geo.Wrap180((double)args[1]);
            if (args.Caller.Entity == null) return TextCommandResult.Error("Only a player can be teleported.");
            Model.Geometry.FromGeo(lat, lon, out double x, out double z);
            bool aboard = args.Caller.Entity is EntityAgent agent && agent.MountedOn != null;
            bool water = aboard || string.Equals(args[2] as string, "water", StringComparison.OrdinalIgnoreCase);
            var entity = args.Caller.Entity;
            // every teleport supersedes (and stops) a water search still running for the same entity (main thread only)
            if (waterSearches.Remove(entity.EntityId, out var previous)) previous.Cancel();
            if (!water)
            {
                // ER-30: where this world reads GLO-90 and the square's tile isn't here yet, the main thread would take the
                // built-in heights (the player lands in the ground or in the air): fetch it first, then teleport
                var fetch = Elevation?.FetchSquareAt(lat, lon);
                if (fetch == null) return TextCommandResult.Success(TeleportWhenLoaded(entity, x, z, false, "", args.Caller.Player as IServerPlayer));
                var wait = new System.Threading.CancellationTokenSource();
                waterSearches[entity.EntityId] = wait;
                var asker = args.Caller.Player as IServerPlayer;
                // (EA-0121) the wait has a limit and says why it gives up; each step is logged, so that a wait that never ends
                // shows whether the fetch or the main thread's continuation stuck (twice on the bench, not reproduced)
                string who = asker?.PlayerName ?? "entity " + entity.EntityId, square = EarthElevationSystem.SquareNameAt(lat, lon), place = FormatLatLon(lat, lon);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var limit = TeleportWait.Limit;
                logger.Notification("Earth tp: {0} waits for GLO-90 square {1} (at most {2:0.#} minutes).", who, square, limit.TotalMinutes);
                fetch.ContinueWith(t => logger.Notification("Earth tp: the fetch of {0} for {1} ended after {2:0.0} s: {3}.", square, who, clock.Elapsed.TotalSeconds,
                    t.IsCompletedSuccessfully && t.Result ? "here" : "not had"), System.Threading.Tasks.TaskScheduler.Default);
                System.Threading.Tasks.Task.WhenAny(fetch, System.Threading.Tasks.Task.Delay(limit, wait.Token)).ContinueWith(_ => sapi.Event.EnqueueMainThreadTask(() =>
                {
                    bool stale = !waterSearches.TryGetValue(entity.EntityId, out var current) || current != wait;
                    logger.Notification("Earth tp: {0}'s teleport to {1} goes on, on the main thread, after {2:0.0} s ({3}).", who, square, clock.Elapsed.TotalSeconds,
                        stale ? "superseded by a later /earth tp" : fetch.IsCompleted ? "the fetch ended" : "the wait ran out");
                    if (stale) return;
                    waterSearches.Remove(entity.EntityId);
                    wait.Cancel();   // the limit's timer, when the fetch ended first
                    if (entity == null || !entity.Alive) { logger.Notification("Earth tp: {0} not teleported: no longer alive.", who); return; }
                    var (lacks, why) = Elevation?.FetchOutcomeAt(lat, lon) ?? (false, (string)null);
                    var (go, message) = TeleportWait.After(fetch, lacks, why, square, place, limit);
                    try
                    {
                        if (go) message = TeleportWhenLoaded(entity, x, z, false, "", asker);
                        else logger.Warning("Earth tp: {0} not teleported to {1}: {2}", who, square, message);
                    }
                    catch (Exception e)
                    {
                        logger.Error("Earth tp: teleporting {0} to {1} failed: {2}", who, square, e);
                        go = false;
                        message = "The teleport failed: " + e.Message;
                    }
                    asker?.SendMessage(GlobalConstants.GeneralChatGroup, message, go ? EnumChatType.CommandSuccess : EnumChatType.CommandError);
                }, "earth-tp"), System.Threading.Tasks.TaskScheduler.Default);
                return TextCommandResult.Success("Fetching the GLO-90 heights of " + place + " first; you are teleported when they are here (at most " +
                    limit.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture) + " minutes).");
            }
            // the search can visit a million columns where there is no water: off the server thread, teleport on it
            var player = args.Caller.Player as IServerPlayer;
            var search = new System.Threading.CancellationTokenSource();
            waterSearches[entity.EntityId] = search;
            System.Threading.Tasks.Task.Run(() =>
            {
                string msg;
                bool found = false;
                double wx = x, wz = z;
                try { found = OpenWater.Find(Model.GetColumn, x, z, 3000, search.Token, out wx, out wz); msg = null; }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { msg = "Looking for open water failed: " + e.Message; }
                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    // a later /earth tp for this entity wins; this result is stale
                    if (!waterSearches.TryGetValue(entity.EntityId, out var current) || current != search) return;
                    waterSearches.Remove(entity.EntityId);
                    if (msg == null && !found) msg = "No open water within 3000 blocks of " + FormatLatLon(lat, lon) + ".";
                    else if (msg == null)
                    {
                        if (entity == null || !entity.Alive) return;
                        double moved = Math.Sqrt((wx - x) * (wx - x) + (wz - z) * (wz - z));
                        msg = TeleportWhenLoaded(entity, wx, wz, true, moved > 1 ? string.Format(CultureInfo.InvariantCulture, " Nearest open water, {0:0} blocks from the spot.", moved) : "", player);
                    }
                    player?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
                }, "earth-tp");
            });
            return TextCommandResult.Success("Looking for open water near " + FormatLatLon(lat, lon) + "...");
        }

        /// <summary>The water search or tile fetch still running per entity (main thread only): a newer /earth tp cancels it, and a
        /// result that arrives after it was superseded is dropped. Teleports that wait for nothing leave no entry.</summary>
        readonly System.Collections.Generic.Dictionary<long, System.Threading.CancellationTokenSource> waterSearches = new();

        /// <summary>
        /// SU-0010 (19 November 2026): teleports once the destination's chunk column is loaded; returns the message to show now.
        /// Vanilla's teleport loads a column that isn't loaded through its blocking queue (<c>LoadChunkColumnPriority</c>,
        /// <c>loadChunkAreaBlocking</c>), which the chunk thread works through inside one tick, each load sweeping every column
        /// players are waiting for: 6-10 s apiece with 30-60 players exploring at 1:10. Sixty teleports in two minutes kept the
        /// chunk thread there for minutes, the autosave could not pause it, and after 20 failed tries vanilla stopped the server
        /// ("Server autosave failure").
        /// <para>So the teleporting player waits instead. The column is asked for the way a player's view asks (the ordinary
        /// request queue, again every <see cref="TeleportWait.LandAskEvery"/>: an autosave drops every request in progress,
        /// repeats are merged), and the player goes as soon as it is loaded. Meanwhile the teleport waits in Earth's line:
        /// one teleport at a time may use vanilla's blocking load, and the next only <see cref="TeleportWait.LineGap"/> after
        /// it landed, so the chunk thread comes back between them and the autosave can pause it. Only the waiting players
        /// wait; a later /earth tp takes the place of a wait.</para>
        /// </summary>
        string TeleportWhenLoaded(Vintagestory.API.Common.Entities.Entity entity, double x, double z, bool water, string note, IServerPlayer asker)
        {
            int cx = (int)Math.Floor(x / GlobalConstants.ChunkSize), cz = (int)Math.Floor(z / GlobalConstants.ChunkSize);
            if (ColumnLoaded(cx, cz)) return TeleportTo(entity, x, z, water, note);
            var geo = Model.Geometry.ToGeo(x, z);
            var w = new LandWait
            {
                Entity = entity, X = x, Z = z, Water = water, Note = note, Asker = asker, Cx = cx, Cz = cz,
                Key = ((long)cx << 32) | (uint)cz, Who = asker?.PlayerName ?? "entity " + entity.EntityId,
                Place = FormatLatLon(geo.Lat, geo.Lon), Token = new System.Threading.CancellationTokenSource(),
            };
            waterSearches[entity.EntityId] = w.Token;   // a later /earth tp supersedes this one, as it does a search or a fetch
            if (!columnWaits.TryGetValue(w.Key, out var same)) columnWaits[w.Key] = same = new System.Collections.Generic.List<LandWait>();
            same.Add(w);
            if (!columnEventHooked)
            {
                sapi.Event.ChunkColumnLoaded += OnColumnLoaded;
                columnEventHooked = true;
            }
            int ahead = line.Count + (lineBusy != null ? 1 : 0);
            line.Add(w);
            void Ask(float _)
            {
                if (w.State != LandWait.Waiting) return;
                if (Stale(w)) { Drop(w, "a later /earth tp took its place, or the player left"); return; }
                if (ColumnLoaded(w.Cx, w.Cz)) { Arrive(w, "its land was loaded"); return; }
                sapi.WorldManager.LoadChunkColumn(w.Cx, w.Cz);
                Pump();
                sapi.Event.RegisterCallback(Ask, (int)TeleportWait.LandAskEvery.TotalMilliseconds);
            }
            Ask(0);
            logger.Notification("Earth tp: {0} waits for the land at {1} (chunk column {2},{3}; {4} teleports ahead).", w.Who, w.Place, cx, cz, ahead);
            return "Making the land at " + w.Place + " ready; you are teleported when it is" +
                (ahead == 1 ? " (1 other teleport waiting)." : ahead > 1 ? string.Format(CultureInfo.InvariantCulture, " ({0} other teleports waiting).", ahead) : ".");
        }

        /// <summary>A teleport waiting for its land (<see cref="TeleportWhenLoaded"/>); main thread only.</summary>
        sealed class LandWait
        {
            public const int Waiting = 0, Blocking = 1, Done = 2;
            public Vintagestory.API.Common.Entities.Entity Entity;
            public double X, Z;
            public bool Water;
            public string Note, Who, Place, Message;
            public IServerPlayer Asker;
            public int Cx, Cz, State;
            public long Key;
            public System.Threading.CancellationTokenSource Token;
            public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        }

        /// <summary>Waits by destination column, Earth's line in order, the one teleport in vanilla's blocking load (main thread only).</summary>
        readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<LandWait>> columnWaits = new();
        readonly System.Collections.Generic.List<LandWait> line = new();
        LandWait lineBusy;
        readonly System.Diagnostics.Stopwatch lineClock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan lineFreeAt, lineBusySince;
        bool columnEventHooked, pumpScheduled;

        bool ColumnLoaded(int cx, int cz) => sapi.WorldManager.GetChunk(cx, 0, cz) != null;

        bool Stale(LandWait w) => !waterSearches.TryGetValue(w.Entity.EntityId, out var current) || current != w.Token || !w.Entity.Alive ||
            (w.Asker != null && w.Asker.ConnectionState != EnumClientState.Playing);

        void Forget(LandWait w)
        {
            w.State = LandWait.Done;
            line.Remove(w);
            if (columnWaits.TryGetValue(w.Key, out var same) && same.Remove(w) && same.Count == 0) columnWaits.Remove(w.Key);
            if (waterSearches.TryGetValue(w.Entity.EntityId, out var current) && current == w.Token) waterSearches.Remove(w.Entity.EntityId);
        }

        void Drop(LandWait w, string why)
        {
            Forget(w);
            logger.Notification("Earth tp: {0} not teleported to {1}: {2}.", w.Who, w.Place, why);
        }

        /// <summary>The land came through the ordinary queue: teleport now (vanilla's teleport finds the column loaded).</summary>
        void Arrive(LandWait w, string how)
        {
            if (w.State != LandWait.Waiting) return;
            if (Stale(w)) { Drop(w, "a later /earth tp took its place, or the player left"); return; }
            Forget(w);
            w.Token.Cancel();
            string message;
            try { message = TeleportTo(w.Entity, w.X, w.Z, w.Water, w.Note); }
            catch (Exception e)
            {
                logger.Error("Earth tp: teleporting {0} to {1} failed: {2}", w.Who, w.Place, e);
                message = "The teleport failed: " + e.Message;
            }
            logger.Notification("Earth tp: {0} teleported to {1} after {2:0.0} s ({3}).", w.Who, w.Place, w.Clock.Elapsed.TotalSeconds, how);
            w.Asker?.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.CommandSuccess);
        }

        void OnColumnLoaded(Vec2i coord, IWorldChunk[] chunks)
        {
            if (columnWaits.Count == 0 || !columnWaits.TryGetValue(((long)coord.X << 32) | (uint)coord.Y, out var same)) return;
            // the event comes just before the column counts as loaded (vanilla's mainThreadLoadChunkColumn): go on after it
            foreach (var w in same.ToArray())
                if (w.State == LandWait.Waiting) sapi.Event.EnqueueMainThreadTask(() => Arrive(w, "its land is loaded"), "earth-tp");
        }

        /// <summary>Earth's line: the next waiting teleport takes vanilla's blocking load once the last one has landed and the gap passed.</summary>
        void Pump()
        {
            var now = lineClock.Elapsed;
            if (lineBusy != null)
            {
                if (now - lineBusySince < TeleportWait.LineBusyLimit) return;
                logger.Warning("Earth tp: {0}'s teleport to {1} has not landed after {2:0} s; the line goes on.", lineBusy.Who, lineBusy.Place, (now - lineBusySince).TotalSeconds);
                lineBusy = null;
                lineFreeAt = now;
            }
            if (now < lineFreeAt)
            {
                if (!pumpScheduled)
                {
                    pumpScheduled = true;
                    sapi.Event.RegisterCallback(_ => { pumpScheduled = false; Pump(); }, Math.Max(1, (int)(lineFreeAt - now).TotalMilliseconds));
                }
                return;
            }
            while (line.Count > 0)
            {
                var w = line[0];
                if (w.State != LandWait.Waiting) { line.RemoveAt(0); continue; }
                if (Stale(w)) { Drop(w, "a later /earth tp took its place, or the player left"); continue; }
                if (ColumnLoaded(w.Cx, w.Cz)) { Arrive(w, "its land was loaded"); continue; }
                line.RemoveAt(0);
                w.State = LandWait.Blocking;
                lineBusy = w;
                lineBusySince = now;
                try { w.Message = TeleportTo(w.Entity, w.X, w.Z, w.Water, w.Note, () => Landed(w)); }
                catch (Exception e)
                {
                    logger.Error("Earth tp: teleporting {0} to {1} failed: {2}", w.Who, w.Place, e);
                    w.Message = "The teleport failed: " + e.Message;
                    Landed(w);
                }
                return;
            }
        }

        /// <summary>Vanilla's blocking teleport landed (its callback can come twice for a mounted player).</summary>
        void Landed(LandWait w)
        {
            if (w.State == LandWait.Done) return;
            Forget(w);
            w.Token.Cancel();
            logger.Notification("Earth tp: {0} teleported to {1} after {2:0.0} s (its turn in the line: vanilla's load).", w.Who, w.Place, w.Clock.Elapsed.TotalSeconds);
            w.Asker?.SendMessage(GlobalConstants.GeneralChatGroup, w.Message, EnumChatType.CommandSuccess);
            if (lineBusy == w)
            {
                lineBusy = null;
                lineFreeAt = lineClock.Elapsed + TeleportWait.LineGap;
                Pump();
            }
        }

        /// <summary>Teleports on the water surface (a boat or ship floats there) or just above the ground; the message to show.
        /// <paramref name="onTeleported"/> runs when vanilla's teleport has landed.</summary>
        string TeleportTo(Vintagestory.API.Common.Entities.Entity entity, double x, double z, bool water, string note, Action onTeleported = null)
        {
            int missed = FineElevation.MissedReads;
            var c = Model.GetColumn(x, z);
            int y = water ? c.WaterTop + 1 : Math.Max(c.Top, c.WaterTop) + 2;
            entity.TeleportToDouble(Math.Floor(x) + 0.5, y, Math.Floor(z) + 0.5, onTeleported);
            var g = Model.Geometry.ToGeo(x, z);
            return string.Format(CultureInfo.InvariantCulture, "Teleported to {0} (x={1:0} z={2:0}, y={3}).{4}{5}", FormatLatLon(g.Lat, g.Lon), x, z, y, note,
                FineElevation.MissedReads != missed ? Approximate : "");
        }

        TextCommandResult CmdCheckRivers(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            if (Model.Rivers == null) return TextCommandResult.Error("This world has no rivers.");
            var caller = args.Caller.Player as IServerPlayer;
            // one audit at a time (CheckRivers): answer at once when one is running
            if (System.Threading.Volatile.Read(ref checkingRivers) != 0)
                return TextCommandResult.Error("The rivers are already being checked; you'll get a message when the report is written.");
            System.Threading.Tasks.Task.Run(() =>
            {
                string msg;
                try { msg = CheckRivers(out _); }
                catch (Exception e) { msg = "Checking the rivers failed: " + e.Message; }
                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    logger.Notification(msg);
                    caller?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
                }, "earth-checkrivers");
            });
            return TextCommandResult.Success("Checking every river in the background (about a minute), you'll get a message when the report is written.");
        }

        /// <summary>1 while a river audit runs (/earth checkrivers or the self-test).</summary>
        static int checkingRivers;

        /// <summary>Runs the river audit on this world's terrain (with its flattened story sites) and writes the report. One at a
        /// time: each walks every river (on one thread) and writes the same report file; a second call while one runs only says so
        /// (audit null).</summary>
        public string CheckRivers(out RiverAudit audit)
        {
            audit = null;
            if (System.Threading.Interlocked.CompareExchange(ref checkingRivers, 1, 0) != 0)
                return "The rivers are already being checked; this check was skipped.";
            try { return WriteRiverCheck(out audit); }
            finally { System.Threading.Interlocked.Exchange(ref checkingRivers, 0); }
        }

        string WriteRiverCheck(out RiverAudit audit)
        {
            // the network's reaches and courses were released after start-up (decision 0025): built again for the audit, the same
            // way, from the same courses (with the dam file's levels and the flow file's rerouted mouths where the model took
            // them; a few seconds and some hundred MB, dropped afterwards)
            var rivers = Model.Rivers;
            if (rivers != null && rivers.Reaches == null)
            {
                var source = Params.RiverSourceAsBuilt(Data, Elevation?.Dams, Elevation?.Flow);
                rivers = source != null ? Params.BuildRivers(Data, Model, source) : null;
            }
            audit = rivers != null ? RiverAudit.Run(Model, rivers: rivers) : new RiverAudit();
            var spawn = sapi.World.DefaultSpawnPosition;
            string report = audit.Report(spawn?.X ?? 0, spawn?.Z ?? 0);
            string dir = Path.Combine(GamePaths.DataPath, "EarthMap");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "rivercheck-" + sapi.WorldManager.Seed + ".txt");
            File.WriteAllText(file, report);
            return audit.Summary() + ". Report: " + file;
        }

        TextCommandResult CmdMap(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            int bpp = Math.Clamp((int)args[0], 16, 4096);
            var caller = args.Caller.Player as IServerPlayer;
            var markers = StoryPlacer.Markers(sapi, core);
            System.Threading.Tasks.Task.Run(() =>
            {
                string msg;
                try { RenderWorldMap(bpp, markers, out msg); }
                catch (Exception e) { msg = "Rendering the map failed: " + e.Message; }
                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    logger.Notification(msg);
                    caller?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
                }, "earth-map");
            });
            return TextCommandResult.Success("Rendering the map in the background, you'll get a message when it's written.");
        }

        /// <summary>Renders the whole Earth as this world generates it (no chunks needed). Returns the file, or null with an error message.</summary>
        public string RenderWorldMap(int bpp, out string message) => RenderWorldMap(bpp, StoryPlacer.Markers(sapi, core), out message);

        /// <summary>Largest world map image: bytes of RGB held while rendering (about 40 million pixels).</summary>
        const long MaxMapBytes = 120_000_000;
        /// <summary>1 while a world map is being rendered: one at a time, as each can take a few hundred MB and all cores.</summary>
        static int renderingMap;

        public string RenderWorldMap(int bpp, System.Collections.Generic.List<(double x, double z, bool spawn)> markers, out string message)
        {
            var g = Model.Geometry;
            var frame = WorldMapFrame.For(g, sapi.WorldManager.MapSizeZ, bpp);
            if (frame.Bytes > MaxMapBytes)
            {
                message = $"That would be a huge image ({frame.Width}x{frame.Height}); use at least {frame.BlocksPerPixelWithin(MaxMapBytes)} blocks per pixel.";
                return null;
            }
            if (System.Threading.Interlocked.CompareExchange(ref renderingMap, 1, 0) != 0)
            {
                message = "A world map is already being rendered; try again when it is written.";
                return null;
            }
            try
            {
                int w = (int)frame.Width, h = (int)frame.Height;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rgb = MapColors.Render(Model, g.RingStart, frame.Z0, w, h, bpp);
                frame.DrawMarkers(rgb, markers);
                string file = Path.Combine(GamePaths.DataPath, "EarthMap", "earth-" + sapi.WorldManager.Seed + ".png");
                Png.WriteRgb(file, w, h, rgb);
                message = $"Wrote {file} ({w}x{h}, {bpp} blocks per pixel) in {sw.ElapsedMilliseconds} ms. Red: spawn, yellow: story locations.";
                return file;
            }
            finally { System.Threading.Interlocked.Exchange(ref renderingMap, 0); }
        }

        TextCommandResult CmdStory(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            return TextCommandResult.Success(StoryPlacer.Describe(sapi, core));
        }
    }
}
