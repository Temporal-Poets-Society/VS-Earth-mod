using System;
using System.Globalization;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace EarthMap
{
    /// <summary>
    /// EarthMapSystem, a world's parameters: the saved ones resolved (or refused), repaired where they can't make terrain,
    /// the runtime choices applied; a new world's computed, frozen and finished (spawn, Cylinder World's checks) (EA-0089:
    /// split from EarthMapSystem.cs, no change).
    /// </summary>
    public partial class EarthMapSystem
    {
        /// <summary>The coast outlines (EarthParams.Coasts) a world with this geometry gets by the config: none when they are off,
        /// without the islands, or where the grid's cells are under CoastMinCellBlocks wide.</summary>
        internal int CoastsFor(EarthGeometry geo, bool islands) => WorldParamsBuilder.Coasts(Config, geo, islands);

        /// <summary>The new world as the game and Cylinder World made it: WorldParamsBuilder's input (EA-0089).</summary>
        NewWorld NewWorldNow(CylinderCompat cw) => new NewWorld
        {
            MapSizeX = sapi.WorldManager.MapSizeX, MapSizeY = sapi.WorldManager.MapSizeY, SeaLevel = sapi.World.SeaLevel, Seed = sapi.WorldManager.Seed,
            WorldConfig = sapi.WorldManager.SaveGame.WorldConfiguration, HydrologyQuery = RequiredMods.QueryVersion(sapi),
            CwJumpInset = cw.JumpInset, CwCircumference = cw.Circumference, CwSeamHalfWidth = cw.SeamHalfWidth, CwEquatorZ = cw.EquatorZ,
            CwPoleDistance = cw.PoleDistance, CwSeabedY = cw.SeabedY, CylinderKeepsEarthSeam = () => CylinderKeepsEarthSeam(cw),
        };

        /// <summary>
        /// The saved parameters; without them (a new world, or Earth added to an existing one) a new world's, computed and
        /// frozen, or a disabled set that keeps the world vanilla (stored either way). A world saved with Earth whose Cylinder
        /// World isn't there is refused (the server stops), its save left as it is. <paramref name="fresh"/>: a new Earth world.
        /// </summary>
        EarthParams ResolveParams(ISaveGame save, CylinderCompat cw, out bool fresh)
        {
            fresh = false;
            var p = Saved(save);
            if (p == null)
            {
                // a new Earth world whose first start stopped before its params were stored (its data could not be had): the
                // game had already written the save, with the marker EarthVerticalBootstrap.StartPre set, so IsNewWorld still
                // counts it as new; resumed: this is that second start (as the save was at this start: Cylinder World has
                // stored its new record by now)
                bool resumed = !save.IsNew && NewWorldAtStart;
                if (!Config.Enabled)
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map is disabled in {0}; this world keeps vanilla terrain.", ConfigFile);
                }
                else if (!NewWorldAtStart)
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map is installed, but this world was not made as an Earth world; it keeps its terrain (Earth only generates new worlds of an Earth play style).");
                }
                else if (save.WorldType != "standard")
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map only generates 'standard' worlds (this one is '{0}').", save.WorldType);
                }
                else if (!EarthWorldOptions.IsEarthStyle(save.PlayStyle))
                {
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Notification("Earth Map is inactive for play style '{0}'; new Earth worlds use the 'earth' play style. This world keeps vanilla generation.", save.PlayStyle ?? "(missing)");
                }
                else if (CylinderWorldMissing(cw) is string missing)
                {
                    // like a failed startup step (decision 0008): no half-working Earth, the world keeps vanilla terrain
                    p = new EarthParams { Version = EarthParams.CurrentVersion, Enabled = false };
                    Mod.Logger.Error("Earth Map needs Cylinder World's planet layout (decision 0023), but {0}. This new world keeps vanilla terrain; " +
                        "fix ModConfig/cylinderworld.json and create a new world for the Earth.", missing);
                }
                else
                {
                    // (the marker is set and saved before this, in StartPre; the game saves nothing written here until it
                    // runs, so this start's own failure below leaves the world marked for the next one; cleared below)
                    if (resumed) Mod.Logger.Notification("Earth Map: this world's first start did not finish; it is created now.");
                    var data = LoadedData();
                    // EA-0089: the world's params off the game API (WorldParamsBuilder: earth.json and the data, the Earth tab, the
                    // settings that can't make terrain set back to the defaults)
                    p = WorldParamsBuilder.Build(NewWorldNow(cw), Config, data, Mod.Logger, out string report, out string badSettings);
                    // 0072: the world's own equator-to-pole distance for everything that reads polarEquatorDistance
                    string polar = EarthWorldOptions.ApplyPolarEquatorDistance(save.WorldConfiguration, p.BlocksPerDegreeZ);
                    if (polar != null) Mod.Logger.Notification(polar);
                    else Mod.Logger.Warning("Earth: this world's layout gives no equator-to-pole distance; polarEquatorDistance stays {0}.",
                        save.WorldConfiguration.GetAsString(EarthWorldOptions.PolarEquatorDistanceKey, "unset"));
                    Mod.Logger.Notification("Earth land heights: {0}; rivers {1}; detail noise rules {2} ({3:0.#} m per block across).",
                        p.FineElevation > 0 ? "Copernicus GLO-90, full (downloaded as needed)" + (p.CoarseElevation > 0 ? ", included GLO-90 where a square can't be" : "")
                        : p.CoarseElevation > 0 && data.Coarse != null ? "Copernicus GLO-90, included"
                        : "built-in" + (data.CoarseError != null ? " (included GLO-90 not readable: " + data.CoarseError + ")" : ""),
                        (p.RiverCourses >= RiverNetwork.Glo90WidthCourses ? "on GLO-90, every reach from 20 m³/s moved onto the water mask's rivers and smoothed, with the mask's widths and the side arms"
                            : p.RiverCourses >= RiverNetwork.Glo90ArmsCourses ? "on GLO-90, moved into the water mask's rivers, with their side arms"
                            : p.RiverCourses >= RiverNetwork.Glo90SnapCourses ? "on GLO-90, moved into the water mask's rivers" : p.RiverCourses > 0 ? "on GLO-90" : "from rivers.bin")
                        + (p.SeaReferee > 0 ? "; sea refereed by the water mask" : "") + (p.LakeOutlines > 0 ? "; lakes from the water mask's outlines" : "")
                        + (p.RiverOutlines > 0 ? "; rivers' water from the water mask's outlines" : "")
                        + (p.ChannelRules > 0 ? "; channels dredged along the courses (channel rules " + p.ChannelRules + ")" : ""),
                        p.DetailRules, EarthWorldOptions.MetresPerBlock(save.WorldConfiguration));
                    if (data.Glo90RiversError != null)
                        Mod.Logger.Warning("Earth: the rivers on GLO-90 could not be read ({0}); this new world's rivers come from rivers.bin.", data.Glo90RiversError);
                    if (badSettings.Length > 0) Mod.Logger.Warning("{0}: {1} can't make terrain; this new world uses the defaults for them (it saves them, so fix the config before creating the next one).", ConfigFile, badSettings);
                    Mod.Logger.Notification(report);
                    fresh = true;
                }
                // ER-23: a world Earth leaves vanilla gets nothing in its save: no disabled params (with them other systems, such
                // as the weather's claim before WorldReady, took it for a possible Earth world), and the creation marker is
                // cleared only where Earth set it. Each start decides again, with the same answer: no params and not new
                if (p.Enabled) save.StoreData(SaveKey, SerializerUtil.Serialize(p));
                if (save.GetData(CreatingKey) != null) save.StoreData(CreatingKey, System.Array.Empty<byte>());
            }
            else if (p.Enabled && (!p.Cylinder || cw == null))
            {
                // Earth runs only with Cylinder World (decision 0023). An Earth world is never opened without it (Codex round 3
                // item 1): vanilla's terrain in new chunks beside the Earth's would stay in the save for good. Refused like
                // RequiredMods and the terrain check: one error, the server stops, the saved params stay as they are (nothing
                // is saved before the game runs), so the world comes back as it was once Cylinder World is active again.
                Refuse(p.Cylinder
                    ? "this world was made with Cylinder World, but " + CylinderWorldMissing(cw) + "; restart once it is active (the world is unchanged)"
                    : "this world was made without Cylinder World, which Earth no longer supports (decision 0023); open it with the Earth Map " +
                      "version that made it");
            }
            return p;
        }

        /// <summary>An Earth world's saved settings, layout and vertical scale made usable where they can't make terrain: every
        /// chunk they made is broken anyway, so the repair is saved and happens once.</summary>
        void RepairSaved(EarthParams p, ISaveGame save, CylinderCompat cw)
        {
            if (p.Enabled)
            {
                // settings frozen with a broken value (NaN detail, a negative width) make broken terrain for good: repaired once
                string broken = p.RepairSettings(new EarthMapConfig());
                if (broken.Length > 0)
                {
                    Mod.Logger.Error("Earth Map: this world's saved settings can't make terrain ({0}); they now use the defaults. Chunks generated before keep what they got.", broken);
                    save.StoreData(SaveKey, SerializerUtil.Serialize(p));
                }
            }
            if (p.Enabled && !EarthGeometry.IsUsableLayout(p.RingLength, p.Gutter, p.BlocksPerDegreeZ, p.EquatorZ))
            {
                // a layout saved from settings that leave no Earth (no chunk of it can have generated properly): laid out again,
                // the layout alone (EA-0089 B: before, a whole new world's params were computed for it, with the data loaded as
                // for a new world, and only these fields kept)
                var relaid = WorldParamsBuilder.Layout(NewWorldNow(cw), Config, Mod.Logger, out int gutterSeabed, out _);
                Mod.Logger.Error("Earth Map: this world's saved layout can't place the Earth (ring {0}, gutter {1}, {2} blocks per degree north-south); it is laid out again with usable settings.",
                    p.RingLength, p.Gutter, p.BlocksPerDegreeZ);
                p.RingStart = relaid.RingStart; p.RingLength = relaid.RingLength; p.Wraps = relaid.Wraps; p.Gutter = relaid.Gutter;
                p.EquatorZ = relaid.EquatorZ; p.BlocksPerDegreeZ = relaid.BlocksPerDegreeZ; p.SeamLon0 = relaid.SeamLon0;
                // a layout without a gutter needs the real seam's repeating terrain (decision 0030)
                if (p.Gutter == 0) p.GutterSeabed = gutterSeabed;
                save.StoreData(SaveKey, SerializerUtil.Serialize(p));
            }
            if (p.Enabled && !p.BuildVertical().IsUsable)
            {
                // a world saved with a broken curve (0 gives NaN heights): every chunk it made is broken anyway, so the
                // defaults for its height let it generate terrain again; saved, so this happens once
                var fixedScale = VerticalScale.ForWorld(p.MapHeight, p.SeaLevel);
                Mod.Logger.Error("Earth Map: this world's saved vertical scale can't make terrain (land curve {0}, sea curve {1}, top y {2}, depth {3}); it now uses the defaults for its height. Chunks generated before keep their broken terrain.",
                    p.LandH0, p.SeaD0, p.TopY, p.MaxDepth);
                p.TopY = fixedScale.TopY; p.LandH0 = fixedScale.LandH0; p.MaxDepth = fixedScale.MaxDepth; p.SeaD0 = fixedScale.SeaD0;
                save.StoreData(SaveKey, SerializerUtil.Serialize(p));
            }
        }

        /// <summary>What every start takes from earth.json and this build, saved or not (the handshake fields clients read);
        /// the temperature range only in worlds made before it was frozen (EarthParams.TemperatureRangeFrozen).</summary>
        void ApplyRuntime(EarthParams p)
        {
            if (p.TemperatureRangeFrozen == 0) p.ExtendTemperature = Config.ExtendTemperatureRange;
            p.RealSeasons = Config.RealSeasons;
            EarthWorldOptions.Runtime(Config, p);
            p.WeatherRules = EarthWeather.RulesVersion;
        }

        /// <summary>A new world's spawn, handed to Cylinder World so vanilla's story chain starts there too.</summary>
        void FinishNewWorld(EarthParams p, ISaveGame save, CylinderCompat cw)
        {
            if (sapi.WorldManager.MapSizeY < 384)
                Mod.Logger.Notification("Earth Map: this world is {0} blocks high; Earth's heights are tuned for 384 (World height on the world creation screen).", sapi.WorldManager.MapSizeY);
            EarthSpawn.Choose(sapi, Model, Config, p, Mod.Logger);
            // Cylinder World picked its own spawn (map centre, starting-climate latitude) and moves vanilla's story
            // chain there; make it ours, so the chain and its vanilla spots (the devastation area's) start from here. Also on
            // the second start of a world whose first start did not finish (SaveGame.IsNew is false then; only a world
            // Earth has just made gets here).
            if (!CylinderCompat.SetSpawn(sapi, p.SpawnX, p.SpawnZ))
            {
                // ER-30: a renamed member is a warning's detail here, not a crash of WorldReady
                string at;
                try { at = string.Format(CultureInfo.InvariantCulture, "x={0} z={1}", cw.SpawnX, cw.SpawnZ); }
                catch (MissingMemberException) { at = "(Cylinder World no longer says where)"; }
                Mod.Logger.Warning("Cylinder World didn't take Earth's spawn: vanilla's story chain starts from its spawn at {0}.", at);
            }
            save.StoreData(SaveKey, SerializerUtil.Serialize(p));
        }

        public void SaveParams()
        {
            // only an active Earth world: a vanilla world (Earth off, or added to an existing world) gets no Earth params
            if (sapi != null && Params?.Enabled == true) sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(Params));
        }

        /// <summary>Why Cylinder World can't carry a new Earth world (decision 0023), or null when it can: it must be active here
        /// with its planet layout (layout v3+, polar walls and polar climate), so its latitudes, seasons and day length are the
        /// Earth's.</summary>
        static string CylinderWorldMissing(CylinderCompat cw)
        {
            if (cw == null) return "Cylinder World is not active in this world (set \"Enabled\": true in ModConfig/cylinderworld.json; " +
                "it also turns itself off on a map too narrow for its seam, see its log line)";
            var missing = new System.Collections.Generic.List<string>();
            if (cw.Version < 3) missing.Add("layout v3 or newer (it has v" + cw.Version + ")");
            if (!cw.HasWalls) missing.Add("\"PolarWalls\": true (a map too short for the walls turns them off, see its log)");
            if (!cw.PolarClimate) missing.Add("\"PolarClimate\": true");
            return missing.Count == 0 ? null : "Cylinder World is active without " + string.Join(", ", missing);
        }

        /// <summary>
        /// Whether Cylinder World keeps Earth's own terrain in its seam band (decision 0030): only then can the real Earth
        /// run across the jump line. That is its Earth polar mode in a new world (PolarIceMode "earth": SeamParams.EarthPolarIce)
        /// with layout v10 or later, where it keeps Earth's seabed and land (CW-D26, CW-D31). Otherwise its own sea band would
        /// cut the Earth along the jump line, so the world keeps the gutter.
        /// </summary>
        bool CylinderKeepsEarthSeam(CylinderCompat cw)
        {
            bool earthMode, layout10;
            try { earthMode = cw.GetBool("EarthPolarIce"); layout10 = cw.Get("Version") >= 10; }
            catch (MissingMemberException) { earthMode = layout10 = false; }
            if (earthMode && layout10) return true;
            Mod.Logger.Notification("Earth Map: Cylinder World is not in its Earth polar mode (PolarIceMode \"earth\") in this new world, so its sea band " +
                "would cut the Earth at the jump line; the world keeps the seam gutter of open sea (decision 0030).");
            return false;
        }
    }
}
