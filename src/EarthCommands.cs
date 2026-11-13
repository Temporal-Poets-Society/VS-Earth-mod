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
                .BeginSubCommand("ores")
                    .WithDescription("Regional ore evidence, reported burial and saved background settings here")
                    .RequiresPlayer()
                    .HandleWith(CmdOres)
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
                .BeginSubCommand("census")
                    .WithDescription("Count catalogue plants, other crops and berry bushes, fruit trees and animals in loaded chunks around you (radius in blocks, default 64, at most 256)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 64))
                    .HandleWith(CmdCensus)
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
                    .RequiresPrivilege(Privilege.chat)
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
                        .HandleWith(args => (string)args[0] != "all"
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
            var c = Model.GetColumn(pos.X, pos.Z);
            Model.ClimateAt(c.Lat, c.Lon, out double t, out double rain, out _);
            var cs = Data.SampleClimate(c.Lat, c.Lon);
            double tHere = Model.TrueTemperature(pos.X, pos.Z, pos.Y);
            string water = c.Water switch { TerrainModel.WaterSea => "sea", TerrainModel.WaterLake => "lake", TerrainModel.WaterSaltLake => "salt lake", _ => "land" };
            return TextCommandResult.Success(string.Format(CultureInfo.InvariantCulture,
                "{0}{1}. Ground: {2:0} m ({3}, top block y={4}{5}). Climate: yearly mean {6:0.0} °C at the surface ({7:0.0} °C at your height), " +
                "coldest month {8:0.0} °C, warmest {9:0.0} °C, {10:0} mm/year (VS rainfall {11:0}).",
                FormatLatLon(c.Lat, c.Lon), c.Gutter ? (c.Water != TerrainModel.WaterNone ? " (seam sea)" : " (seam bridge)") : "", c.Elev, water, c.Top,
                c.IceBottom <= c.Top ? string.Format(", ice {0} blocks", c.Top - c.IceBottom + 1) : "",
                t, tHere, cs.TCold - EarthWorldGen.LapsePerMetre * Math.Max(0, c.Elev), cs.TWarm - EarthWorldGen.LapsePerMetre * Math.Max(0, c.Elev), cs.Precip, rain));
        }

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
            if (!water) return TextCommandResult.Success(TeleportTo(entity, x, z, false, ""));
            // the search can visit a million columns where there is no water: off the server thread, teleport on it
            var player = args.Caller.Player as IServerPlayer;
            var search = new System.Threading.CancellationTokenSource();
            waterSearches[entity.EntityId] = search;
            System.Threading.Tasks.Task.Run(() =>
            {
                string msg;
                bool found = false;
                double wx = x, wz = z;
                try { found = FindOpenWater(x, z, 3000, search.Token, out wx, out wz); msg = null; }
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
                        msg = TeleportTo(entity, wx, wz, true, moved > 1 ? string.Format(CultureInfo.InvariantCulture, " Nearest open water, {0:0} blocks from the spot.", moved) : "");
                    }
                    player?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
                }, "earth-tp");
            });
            return TextCommandResult.Success("Looking for open water near " + FormatLatLon(lat, lon) + "...");
        }

        /// <summary>The water search still running per entity (main thread only): a newer /earth tp cancels it, and a result
        /// that arrives after it was superseded is dropped. Teleports without a search leave no entry.</summary>
        readonly System.Collections.Generic.Dictionary<long, System.Threading.CancellationTokenSource> waterSearches = new();

        /// <summary>Teleports on the water surface (a boat or ship floats there) or just above the ground; the message to show.</summary>
        string TeleportTo(Vintagestory.API.Common.Entities.Entity entity, double x, double z, bool water, string note)
        {
            var c = Model.GetColumn(x, z);
            int y = water ? c.WaterTop + 1 : Math.Max(c.Top, c.WaterTop) + 2;
            entity.TeleportToDouble(Math.Floor(x) + 0.5, y, Math.Floor(z) + 0.5);
            var g = Model.Geometry.ToGeo(x, z);
            return string.Format(CultureInfo.InvariantCulture, "Teleported to {0} (x={1:0} z={2:0}, y={3}).{4}", FormatLatLon(g.Lat, g.Lon), x, z, y, note);
        }

        /// <summary>
        /// The nearest spot with open water around it (sea, lake or river at least 4 blocks deep, and water at least 2
        /// deep for 8 blocks all round, room for a ship), searching rings every 4 blocks out to the given distance.
        /// </summary>
        bool FindOpenWater(double x, double z, double maxDist, System.Threading.CancellationToken cancel, out double wx, out double wz)
        {
            bool Open(double px, double pz)
            {
                var c = Model.GetColumn(px, pz);
                if (c.Water == TerrainModel.WaterNone || c.Gutter || c.WaterTop - c.Top < 4) return false;
                for (int dz = -8; dz <= 8; dz += 4)
                {
                    for (int dx = -8; dx <= 8; dx += 4)
                    {
                        var n = Model.GetColumn(px + dx, pz + dz);
                        if (n.Water == TerrainModel.WaterNone || n.WaterTop - n.Top < 2 || Math.Abs(n.WaterTop - c.WaterTop) > 1) return false;
                    }
                }
                return true;
            }
            for (double r = 0; r <= maxDist; r += 4)
            {
                cancel.ThrowIfCancellationRequested();
                int n = r == 0 ? 1 : (int)Math.Ceiling(2 * Math.PI * r / 4);
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    double px = x + r * Math.Cos(a), pz = z + r * Math.Sin(a);
                    if (Open(px, pz))
                    {
                        wx = px; wz = pz;
                        return true;
                    }
                }
            }
            wx = x; wz = z;
            return false;
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
            // way, from the same courses (with the dam file's levels where the model took them; a few seconds and some hundred
            // MB, dropped afterwards)
            var rivers = Model.Rivers;
            if (rivers != null && rivers.Reaches == null)
            {
                var source = Params.RiverSource(Data, Elevation?.Dams, out _, out _);
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
            long wl = (long)Math.Ceiling(g.RingLength / bpp);
            double z0 = Math.Max(0, g.EquatorZ - 90 * g.BlocksPerDegreeZ);
            double z1 = Math.Min(sapi.WorldManager.MapSizeZ, g.EquatorZ + 90 * g.BlocksPerDegreeZ);
            long hl = (long)Math.Ceiling((z1 - z0) / bpp);
            if (MapColors.RenderBytes(wl, hl) > MaxMapBytes)
            {
                message = $"That would be a huge image ({wl}x{hl}); use at least {Math.Ceiling(bpp * Math.Sqrt(MapColors.RenderBytes(wl, hl) / (double)MaxMapBytes))} blocks per pixel.";
                return null;
            }
            if (System.Threading.Interlocked.CompareExchange(ref renderingMap, 1, 0) != 0)
            {
                message = "A world map is already being rendered; try again when it is written.";
                return null;
            }
            try
            {
                int w = (int)wl, h = (int)hl;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rgb = MapColors.Render(Model, g.RingStart, z0, w, h, bpp);
                // markers: 5x5 squares, the spawn last so it stays on top
                foreach (var (mx, mz, spawn) in markers.OrderBy(m => m.spawn))
                {
                    int pi = (int)Math.Round((Wrapped(mx) - g.RingStart) / bpp), pj = (int)Math.Round((mz - z0) / bpp);
                    for (int j = Math.Max(0, pj - 2); j <= Math.Min(h - 1, pj + 2); j++)
                        for (int i = Math.Max(0, pi - 2); i <= Math.Min(w - 1, pi + 2); i++)
                        {
                            long k = 3L * ((long)j * w + i);
                            rgb[k] = 255; rgb[k + 1] = spawn ? (byte)40 : (byte)230; rgb[k + 2] = spawn ? (byte)40 : (byte)0;
                        }
                }
                string file = Path.Combine(GamePaths.DataPath, "EarthMap", "earth-" + sapi.WorldManager.Seed + ".png");
                Png.WriteRgb(file, w, h, rgb);
                message = $"Wrote {file} ({w}x{h}, {bpp} blocks per pixel) in {sw.ElapsedMilliseconds} ms. Red: spawn, yellow: story locations.";
                return file;
            }
            finally { System.Threading.Interlocked.Exchange(ref renderingMap, 0); }
        }

        double Wrapped(double x)
        {
            var g = Model.Geometry;
            double pos = (x - g.RingStart) % g.RingLength;
            if (pos < 0) pos += g.RingLength;
            return g.RingStart + pos;
        }

        TextCommandResult CmdStory(TextCommandCallingArgs args)
        {
            if (Params?.Enabled != true || Model == null) return NotActive();
            return TextCommandResult.Success(StoryPlacer.Describe(sapi, core));
        }
    }
}
