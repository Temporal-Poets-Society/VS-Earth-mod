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
    /// EarthMapSystem, the Earth data (decision 0066): the server's data store made complete for this world (all files for a
    /// new world, those its saved rules read for a saved one), the files loaded, and what is said when a world's data or
    /// saved rules don't match this build's (EA-0089: split from EarthMapSystem.cs, no change).
    /// </summary>
    public partial class EarthMapSystem
    {
        /// <summary>
        /// The Earth data (decision 0066). A client reads only the climate files inside this mod. The server (with
        /// <paramref name="terrain"/>) first makes sure the data store's files are in VintagestoryData/ModData/earth/data
        /// (EarthDataStore: from an Earth 1.0 data pack still installed, else from the data release): every one for a new
        /// world, the ones its saved rules read for a saved <paramref name="world"/> (<see cref="StoreFilesRead"/>), then
        /// reads each file from this mod if it carries it, else from the store. A server that can't get them stops with
        /// instructions instead of generating a world with parts of its data missing.
        /// </summary>
        EarthData LoadData(bool terrain, EarthParams world = null)
        {
            string src = Mod.SourcePath;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EarthData data;
            try
            {
                string store = EarthGround.DataFolder;
                // (EA-0092 A6) a store file found damaged as it is read is fetched again from where the start fetches it
                if (terrain) EarthDataStore.Repair = name => RepairStoreFile(store, name);
                if (terrain) EnsureServerData(store, world);
                data = EarthData.Load(name => EarthData.OpenDataFile(src, name) ?? (terrain ? EarthDataStore.Open(store, name) : null), terrain);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth data cannot load: {0}", e.Message);
                sapi?.Server.ShutDown();
                throw;
            }
            Mod.Logger.Notification("Earth data loaded from {0}{1} in {2} ms", src, terrain ? " and " + EarthDataStore.Folder(EarthGround.DataFolder) : "", sw.ElapsedMilliseconds);
            if (data.WeatherError != null) Mod.Logger.Error("Earth weather data could not be read; vanilla weather is used. {0}", data.WeatherError);
            return data;
        }

        /// <summary>Where a store file can be copied from instead of downloaded: installed Earth 1.0 data packs, and a development
        /// build or an unpacked mod folder that still carries a file.</summary>
        System.Collections.Generic.List<System.Func<string, System.IO.Stream>> LocalPacks()
        {
            var local = new System.Collections.Generic.List<System.Func<string, System.IO.Stream>>();
            foreach (string id in EarthDataStore.LegacyPacks)
                if (sapi.ModLoader.IsModEnabled(id) && sapi.ModLoader.GetMod(id)?.SourcePath is string pack) local.Add(EarthDataStore.FromPack(pack));
            local.Add(EarthDataStore.FromPack(Mod.SourcePath));
            return local;
        }

        /// <summary>(EA-0092 A6) One store file found damaged as it was read (already set aside): fetched again as the start
        /// fetches the store's files; false when it can't be (the read then fails, naming the file).</summary>
        bool RepairStoreFile(string store, string name)
        {
            var file = EarthDataStore.Files.FirstOrDefault(f => f.Name == name);
            if (file == null) return false;
            Mod.Logger.Warning("Earth data: earthdata/{0} is damaged (not the release's size and SHA-256); it is set aside and fetched again.", name);
            bool ok = EarthDataStore.Ensure(store, LocalPacks(), Config?.DataRepo, m => Mod.Logger.Notification("Earth data: {0}", m), out string why, Stopping, new[] { file });
            if (!ok) Mod.Logger.Error("Earth data: earthdata/{0} could not be fetched again: {1}", name, why);
            return ok;
        }

        /// <summary>The data store's files this world needs in place (all for a new world, or while a one-shot upgrade is asked
        /// for: it moves the world to this build's rules), or an exception saying which are missing and how to get them.</summary>
        void EnsureServerData(string store, EarthParams world)
        {
            var local = LocalPacks();
            EarthDataStore.File[] files = EarthDataStore.Files;
            if (world != null && !WorldUpgrades.AnyAsked(Config))
            {
                var read = StoreFilesRead(world);
                files = EarthDataStore.Files.Where(f => read.Contains(f.Name)).ToArray();
                if (files.Length < EarthDataStore.Files.Length)
                    Mod.Logger.Notification("Earth data: this world's saved rules read {0} of the data store's {1} files.", files.Length, EarthDataStore.Files.Length);
            }
            // ER-16: cancelled when the server stops; its lines also on the loading screen (a first start fetches ~290 MB)
            if (EarthDataStore.Ensure(store, local, Config?.DataRepo, m => { Mod.Logger.Notification("Earth data: {0}", m); Mod.Logger.StoryEvent("Earth data: " + m); },
                out string why, Stopping, files)) return;
            var missing = EarthDataStore.Missing(store, files);
            long needed = EarthDataStore.TotalBytes(missing);
            // what happened, not always "the network" (review A9): a full disk says so
            string disk = DataTransport.DiskShortfall(EarthDataStore.Folder(store), needed);
            string repo = string.IsNullOrWhiteSpace(Config?.DataRepo) ? null : Config.DataRepo;
            throw new System.IO.FileNotFoundException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} of Earth's data files ({1:F0} MB) are missing from {2} and could not be had ({3}): {4}. {5}",
                missing.Count, needed / 1e6, EarthDataStore.Folder(store),
                string.Join(", ", missing.Take(6).Select(f => f.Name)) + (missing.Count > 6 ? ", …" : ""),
                disk ?? why ?? "not fetched",
                disk != null ? "Free space on that disk, then start again."
                : repo == null ? "Set DataRepo in earth.json (Earth's data release is " + EarthDataStore.Release + "), or put the files from that release into this folder by hand, then start again."
                : "Let the server reach " + repo + ", or put the files from that release into this folder by hand, then start again."));
        }

        /// <summary>The server's Earth data, loaded the first time it is needed (with the terrain): the store files a saved
        /// <paramref name="world"/>'s rules read in place, or all of them.</summary>
        internal EarthData LoadedData(EarthParams world = null) => Data ??= LoadData(true, world);

        /// <summary>
        /// The data store's files (EarthDataStore.Files) a saved world's rules read (review A5: decision 0066 promises that an
        /// offline server keeps working with an Earth 1.0 world's data packs, which carry 9 of the 38 files). Read by every
        /// world: the 2' terrain grid and rivers.bin, and the soil sites and wetlands (2.0 reads both for every world: the
        /// regional trees' soil fit, the fire, pond and wetland rules and the water table; kept for all, to be safe). The
        /// rest only where a saved rule reads it (EarthParams.BuildModel, RiverSourceRepository.RiverReaches, WorldGenRules,
        /// EarthGround, EarthSprings). A file left out is one this world's rules never read, so its absence can't change what
        /// the world generates; a new world, or one asked to take a one-shot upgrade, needs them all.
        /// </summary>
        public static System.Collections.Generic.HashSet<string> StoreFilesRead(EarthParams p)
        {
            var files = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "terrain.bin", "rivers.bin", "soil-site.bin", "wetlands.bin" };
            void If(bool rule, System.Collections.Generic.IEnumerable<string> names) { if (rule) files.UnionWith(names); }
            void One(bool rule, string name) { if (rule) files.Add(name); }
            // the grid's overlays (BuildModel); the sea referee is checked against the grid, the islands and the coasts
            One(p.Islands > 0 || p.Coasts > 0 || p.SeaReferee >= 1, "islands.bin");
            One(p.Coasts > 0 || p.SeaReferee >= 1, "coasts.bin");
            One(p.SeaReferee >= 1, "sea-referee.bin");
            One(p.RiverEnds > 0, "lakes.bin");
            One(p.LakeShores > 0, "shores.bin");
            // decision 0070's beaches by OSM's material (TerrainModel.ShoreData)
            One(ShoreMaterialFacts.Strip(p.ShoreRules), ShoreMaterialData.FileName);
            // decision 0031's mountains, and the included GLO-90 heights
            One(p.Summits >= 1, "summits.bin");
            One(p.Summits >= 2, "peaks.json");
            One(p.Glaciers >= 1, "glaciers.bin");
            If(p.CoarseElevation > 0, EarthDataStore.Files.Select(f => f.Name).Where(n => n.StartsWith(CoarseElevation.Prefix + "-", StringComparison.Ordinal)));
            // the river courses on GLO-90 and their overlays
            One(p.RiverCourses >= 1, RiverSourceRepository.Glo90RiversFile);
            One(p.RiverCourses >= RiverNetwork.Glo90SnapCourses, "rivers-glo90-snap.bin");
            One(p.RiverCourses >= RiverNetwork.Glo90ArmsCourses, "rivers-glo90-arms.bin");
            If(p.RiverCourses >= RiverNetwork.Glo90WidthCourses, new[] { "rivers-glo90-snap-2.bin", "rivers-glo90-arms-2.bin" });
            One(p.SpringRules >= EarthParams.SpringRulesStreams, StreamHeads.FileName);
            // the ground columns' 2' sets (ground-rules-v1.json), the floodplains they read, the ice sheets' ground
            If(GroundColumnFacts.Columns(p.GroundColumns), EarthDataStore.Files.Select(f => f.Name).Where(n => n.EndsWith(".egt", StringComparison.Ordinal) || n.EndsWith(".egts", StringComparison.Ordinal)));
            One(GroundColumnFacts.Columns(p.GroundColumns) && p.SoilRules >= SoilFertility.FloodplainRules, "floodplains.bin");
            One(AlpineSurfaceFacts.IceSheetGround(p.AlpineSurfaceRules), "global-glaciation.egts");
            return files;
        }

        /// <summary>An active world's saved rules: Hydrology's vessels setting, the sky's longitude. (No rule here is newer
        /// than this build's: such a world is refused before this, InitWorld, ER-05, so its "newer than this build" warnings
        /// went, S-R1 of the Fable pass. A world's layout Version is not compared: every rule is frozen in a field of its own,
        /// and saves from before the last layout change, Earth Map 0.12, are refused anyway for lacking Cylinder World.)</summary>
        void AnnounceSavedRules(EarthParams p, ISaveGame save)
        {
            // pool layout 5 draws a sill under every 1-block river step, so Hydrology's communicating vessels (deep canals fill
            // layer by layer) can't flood the steps: Hydrology reads this world setting (decision 0024)
            // (pool layout 8 has no sills: only a Hydrology that reads the river flow query may fill vessels there, so the world
            // asks under a key older Hydrology versions don't read; decision 0027)
            string vesselsKey = PoolLayoutFacts.RapidsReaches(p.RiverPoolLayout) ? HydrologyVesselsWithQueryKey : HydrologyVesselsKey;
            if (PoolLayoutFacts.StepSills(p.RiverPoolLayout) && !save.WorldConfiguration.GetBool(vesselsKey)) save.WorldConfiguration.SetBool(vesselsKey, true);
            UseSkyLongitude(p, sapi);
        }

        /// <summary>A world whose data its rules read isn't in this build: what new chunks get instead.</summary>
        void ReportDataMismatch(EarthParams p)
        {
            if (p.SeaReferee >= 1 && Data.SeaRefereeAvailable == 0)
                Mod.Logger.Warning("This world's sea is refereed by GLO-90's water mask (sea-referee.bin), but {0}; chunks generated now follow the installed coasts and may meet the older ones with a step.",
                    Data.SeaReferee == null ? "this Earth build has no sea-referee.bin" : "the installed terrain and water data packs aren't the ones it was made for");
            else if (p.SeaReferee >= 1 && p.SeaReferee < SeaRefereeData.FileVersion)
                Mod.Logger.Warning("This world's sea was refereed by an earlier test build (sea referee {0}); this build's data (sea referee {1}) also moves the coastline to the water mask's where GSHHG's differs, so chunks generated now may meet older ones with a step there.",
                    p.SeaReferee, SeaRefereeData.FileVersion);
            if (p.Works1318 >= 1 && Data.Works1318 == null)
                Mod.Logger.Warning("This world's coasts and waters are as of 1318 (decision 0064), but this Earth build has no {0}; chunks generated now have today's and may meet the older ones with a step.", Works1318Data.FileName);
            if (p.PerennialRules >= 1 && p.AridRiverRules >= 1 && Data.NonPerennial == null)
                Mod.Logger.Warning("This world's dry rivers follow GIRES' non-perennial rivers (decision 0069), but this Earth build has no {0}; " +
                    "chunks generated now dry rivers by their catchments' rainfall and may meet the older ones with a step.", NonPerennialData.FileName);
        }

        /// <summary>The data problem (missing or wrong data: IOException, InvalidDataException) an exception is or carries
        /// (the parallel steps' AggregateException holds the workers' exceptions), else null.</summary>
        static Exception DataProblem(Exception e)
        {
            if (e is IOException or InvalidDataException) return e;
            if (e is AggregateException a)
                foreach (var inner in a.Flatten().InnerExceptions)
                    if (inner is IOException or InvalidDataException) return inner;
            return null;
        }

        /// <summary>The climate data this world's rules read (EarthParams.ConfigureClimateData) that this Earth doesn't have,
        /// with what that means on this side.</summary>
        void ReportMissingClimateData(EarthParams p, string withoutFineTemperatures, string withoutClimateFix)
        {
            if (p.ClimateRules >= TerrainModel.RealLapseRules && !Data.FineTemperatures)
                Mod.Logger.Error("Earth Map: this world uses CHELSA's temperatures (earthdata/temperature.bin), which this Earth Map doesn't have{0}: {1}. Reinstall the mod.",
                    Data.FineTemperatureError != null ? " (" + Data.FineTemperatureError + ")" : "", withoutFineTemperatures);
            if (p.Climate1318 >= 1 && p.ClimateRules >= TerrainModel.RealLapseRules && Data.FineTemperatures && !Data.Paleo1318)
                Mod.Logger.Error("Earth Map: this world uses the temperatures as around 1318 (earthdata/{0}), which this Earth Map doesn't have{1}: the modern " +
                    "temperatures of temperature.bin instead ({2}). Reinstall the mod.", Core.EarthDataset.Temperature1318File,
                    Data.Paleo1318Error != null ? " (" + Data.Paleo1318Error + ")" : "", withoutFineTemperatures);
            if (p.ClimateData > 0 && Data.ClimateFixApplied != p.ClimateData)
                Mod.Logger.Error("Earth Map: this world uses the climate fix v{0} (earthdata/climatefix{1}.bin), which this Earth Map doesn't have: {2}. Reinstall the mod.",
                    p.ClimateData, p.ClimateData == 1 ? "" : p.ClimateData.ToString(), withoutClimateFix);
        }
    }
}
