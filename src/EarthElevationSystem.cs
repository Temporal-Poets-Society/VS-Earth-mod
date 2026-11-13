using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EarthMap.Core;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace EarthMap
{
    /// <summary>
    /// Decision 0051: Copernicus GLO-90 land heights, downloaded per square as the world needs them and kept on the server's
    /// machine (VintagestoryData/ModData/earth/glo90), shared by every world there and by every later Earth version. Squares
    /// come from Earth's data release on GitHub (already converted), or from Copernicus's originals where the release can't
    /// supply them. Worlds that use it (EarthParams.FineElevation) get them in their terrain; any player with the server's
    /// control privilege can download all squares ahead from the dialog (client command .earthelevation), choosing the
    /// release or Copernicus, or with /earth elevation download all. The Earth tab's GLO-90 land heights can ask for the
    /// same when a new world opens.
    /// </summary>
    public class EarthElevationSystem : ModSystem
    {
        public const string FallbackKey = "earth-glo90-fallback";
        const string ChannelName = "earth-elevation";
        // the notices the dataset's licence (eula_F.pdf, Article 6 (b) and (c)) requires for adapted data
        public const string Attribution = "Land heights produced using Copernicus WorldDEM™-90 © DLR e.V. 2010-2014 and © Airbus " +
            "Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. The " +
            "organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of " +
            "the Copernicus WorldDEM™-90.";

        // after EarthMapSystem (0.05), which attaches the world in its WorldReady step
        public override double ExecuteOrder() => 0.06;

        ICoreServerAPI sapi;
        ICoreClientAPI capi;
        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;
        EarthMapSystem core;

        /// <summary>Server: the tiles on this machine and their downloader (made on first use).</summary>
        public FineElevation Fine { get; private set; }
        public DemDownloader Downloader { get; private set; }
        /// <summary>Whether this world's terrain uses GLO-90.</summary>
        public bool WorldUses { get; private set; }
        TerrainModel model;
        (int source, int here, long bytes) census;
        DateTime censusAt;
        long censusConverted, censusStored;

        /// <summary>VintagestoryData/ModData/earth/glo90: one store per machine, shared by every world (a new world reuses what is here).</summary>
        public static string Folder => Path.Combine(GamePaths.DataPath, "ModData", "earth", "glo90");

        /// <summary>The squares GLO-90 has tiles for (data/glo90-tiles.bin, embedded).</summary>
        public static byte[] TileList()
        {
            using var s = typeof(EarthElevationSystem).Assembly.GetManifestResourceStream("EarthMap.glo90-tiles.bin")
                ?? throw new FileNotFoundException("embedded glo90-tiles.bin");
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }

        // ------------------------------------------------------------------------------------------ server

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            serverChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<ElevationRequest>()
                .RegisterMessageType<ElevationStatus>()
                .SetMessageHandler<ElevationRequest>(OnRequest);
            api.Event.RegisterGameTickListener(_ => Prefetch(), 5000, 2000);
            // the main thread made the spawn chunks (PrepareChunk let it wait for tiles); from now on chunks are made on the
            // generation threads, and a command or plan on the main thread must not wait minutes for a download (CR-D1)
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => { running = true; FineElevation.FetchAllowed = false; });
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, AutoDownload);
            api.Event.PlayerNowPlaying += OnNowPlaying;
            api.Event.RegisterGameTickListener(_ => AutoDownloadEnded(), 3000, 3000);
        }

        /// <summary>The tiles and downloader, made the first time they are needed.</summary>
        void Ensure()
        {
            if (Fine != null) return;
            var cfg = core?.Config;
            Fine = new FineElevation(Folder, TileList());
            // an empty setting means none: the config file's own values decide, the defaults only when the file lacks them
            Downloader = new DemDownloader(Fine, cfg != null ? cfg.ElevationSource : DemDownloader.Glo90Bucket,
                Math.Clamp(cfg?.ElevationDownloads ?? 3, 1, 8), cfg != null ? cfg.ElevationRepo : DemDownloader.Glo90Repo)
            {
                Log = s => Mod.Logger.Notification("Earth elevation: " + s),
            };
        }

        /// <summary>Called by EarthMapSystem once the world's model is built: GLO-90 heights for a world that chose them.</summary>
        public void AttachServer(TerrainModel terrain, EarthParams p)
        {
            model = terrain;
            if (p.FineElevation < 1) return;
            Ensure();
            if (p.FineElevation > FineElevation.Version)
                Mod.Logger.Warning("Earth elevation: this world's GLO-90 rules ({0}) are newer than this build's ({1}); current rules are used.", p.FineElevation, FineElevation.Version);
            byte[] saved = sapi.WorldManager.SaveGame.GetData(FallbackKey);
            int[] keys = saved != null ? SerializerUtil.Deserialize<int[]>(saved) ?? Array.Empty<int>() : Array.Empty<int>();
            Fine.SetFallback(keys);
            Fine.Fetch = (latS, lonW) => Downloader.AwaitForGeneration(latS, lonW, Downloader.FetchAsync(latS, lonW), GenerationWait);
            Fine.FellBack = OnFellBack;
            Fine.Lacks = Downloader.Lacks;
            Fine.FellBackForSession = OnFetchFailed;
            terrain.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
            terrain.Fine = Fine;
            WorldUses = true;
            if (dams != null) AttachDams(p);
            if (p.LakeOutlines >= 1) AttachLakeOutlines(terrain, p);
            if (p.RiverOutlines >= 1) AttachRiverOutlines(terrain, p);
            if (flow != null && p.FlowRules >= 1)
            {
                terrain.Flow = flow;
                terrain.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion);
                Used(FlowUse);
                Mod.Logger.Notification("Earth river flow: the water mask's rivers flow as solved on them ({0}, decision 0069): still water {1}, " +
                    "the river's depth across its whole width.", RiverFlowData.FileName,
                    p.ChannelRules >= TerrainModel.FlowChannelRules ? "where it carries none of the river's flow or its reaches are slower than " + TerrainModel.ReachFlowingSpeed + " m/s (channel rules 4, decision 0073)"
                        : "where slower than " + TerrainModel.StillBelow + " m/s");
            }
            var c = Census();
            Mod.Logger.Notification("Earth elevation: this world's land heights come from Copernicus GLO-90 (decision 0051). {0} of {1} squares " +
                "on this machine ({2:F1} GB) in {3}; {4} squares of this world keep other heights. Squares come from {5}, else {6}.",
                c.here, c.source, c.bytes / 1e9, Folder, keys.Length, Downloader.Repo ?? "(no release)", Downloader.Source ?? "(no Copernicus source)");
        }

        // ---- the release files that come with the GLO-90 heights (0053 items 17-19): lake and river outlines, the dam file

        /// <summary>Whether a file's bytes are the ones this build reads (each data class's IsExpected), and if not why.</summary>
        delegate bool FileCheck(byte[] file, out string why);

        /// <summary>
        /// How a world uses one of the release files: its save keys and what it has without the file. A world whose source
        /// answered that it lacks the file when the world first needed it goes without it for good (OffKey), so its chunks
        /// match each other; any other failure, or a world that has generated with it (OnKey), stops the server (review A3,
        /// <see cref="Unavailable"/>).
        /// </summary>
        sealed record ReleaseFileUse(string Label, string OnKey, string OffKey, string With, string Without);

        /// <summary>Save data: this world keeps the grid's lakes for good (0053 item 17: the lake outlines' file couldn't be had
        /// the first time the world needed it), so its chunks match each other.</summary>
        public const string LakeOutlinesOffKey = "earth-lake-outlines-off";
        /// <summary>Save data: this world has generated with the lake outlines.</summary>
        public const string LakeOutlinesOnKey = "earth-lake-outlines-on";
        /// <summary>Save data: this world keeps the river courses' channels for good (0053 item 18: the river outlines' file
        /// couldn't be had the first time the world needed it), so its chunks match each other.</summary>
        public const string RiverOutlinesOffKey = "earth-river-outlines-off";
        /// <summary>Save data: this world has generated with the river outlines.</summary>
        public const string RiverOutlinesOnKey = "earth-river-outlines-on";
        /// <summary>Save data: this world keeps GLO-90's modern dams for good (0053 item 19: the dam file couldn't be had, or
        /// was made for other river courses, the first time the world needed it), so its chunks match each other.</summary>
        public const string DamsOffKey = "earth-dams-off";
        /// <summary>Save data: this world has generated with the dams taken out.</summary>
        public const string DamsOnKey = "earth-dams-on";

        static readonly ReleaseFileUse LakeOutlinesUse = new("Earth lakes", LakeOutlinesOnKey, LakeOutlinesOffKey,
            "this world's lakes come from the water mask's outlines", "the grid's lakes");
        static readonly ReleaseFileUse RiverOutlinesUse = new("Earth rivers", RiverOutlinesOnKey, RiverOutlinesOffKey,
            "this world's rivers come from the water mask's outlines", "the river courses' channels");
        /// <summary>Save data: this world keeps the courses' blended river flow for good (decision 0069: the flow file couldn't
        /// be had the first time the world needed it), so its chunks match each other.</summary>
        public const string FlowOffKey = "earth-flow-off";
        /// <summary>Save data: this world has generated with the river flow file.</summary>
        public const string FlowOnKey = "earth-flow-on";
        static readonly ReleaseFileUse FlowUse = new("Earth river flow", FlowOnKey, FlowOffKey,
            "this world's rivers flow as solved on the water mask", "the courses' blended flow");
        static readonly ReleaseFileUse DamsUse = new("Earth dams", DamsOnKey, DamsOffKey,
            "this world has the modern dams taken out", "GLO-90's modern dams and reservoir floors");

        /// <summary>Whether this world goes without the file for good (logged).</summary>
        bool KeptWithout(ReleaseFileUse use, string name)
        {
            if (sapi.WorldManager.SaveGame.GetData(use.OffKey) == null) return false;
            Mod.Logger.Notification("{0}: this world keeps {1} ({2} couldn't be used when it first needed it).", use.Label, use.Without, name);
            return true;
        }

        /// <summary>
        /// A release file from the GLO-90 folder, fetched from Earth's data release (blocking: before any chunk) when it is
        /// missing or not the one this build reads; null, with the reason, when it can't be had. <paramref name="lacking"/>:
        /// whether that is for good (review A3): no release is set, the release answered that it doesn't have the file, or the
        /// file it has isn't one this build reads; not a network error or a download that didn't check out.
        /// </summary>
        byte[] ReleaseFile(ReleaseFileUse use, string name, long bytes, string sha256, FileCheck check, out string why, out bool lacking)
        {
            lacking = false;
            string path = Path.Combine(Folder, name);
            byte[] file = null;
            try { if (File.Exists(path)) file = File.ReadAllBytes(path); } catch (Exception e) { Mod.Logger.Warning("{0}: {1}: {2}", use.Label, path, e.Message); }
            if (check(file, out why)) return file;
            if (file != null) Mod.Logger.Warning("{0}: {1} isn't the file this build reads ({2}); fetching it again.", use.Label, path, why);
            if (Downloader.Repo == null) { why = "no data release set (ElevationRepo)"; lacking = true; return null; }
            Mod.Logger.Notification("{0}: fetching {1} ({2:F1} MB) from {3}.", use.Label, name, bytes / 1e6, Downloader.Repo);
            if (!Downloader.FetchFile(name, path, bytes, sha256, out string error, out lacking)) { why = error; return null; }
            try { file = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = e.Message; return null; }
            if (check(file, out why)) return file;
            lacking = true;   // fetched and checked against the release, yet not a file this build reads
            return null;
        }

        /// <summary>
        /// The file can't be had or used. A world that never generated with it goes without it for good only when that is the
        /// source's answer (<paramref name="lacking"/>; its chunks then all match). Otherwise the server stops, nothing saved
        /// (review A3): a failure that may pass (the network) must not become the world's choice for good, and a world that
        /// has generated with the file must not go on without it (its new chunks would differ from the rest).
        /// </summary>
        void Unavailable(ReleaseFileUse use, string name, string why, bool lacking)
        {
            var save = sapi.WorldManager.SaveGame;
            bool used = save.GetData(use.OnKey) != null;
            if (!used && lacking)
            {
                save.StoreData(use.OffKey, System.Text.Encoding.UTF8.GetBytes(why ?? "missing"));
                Mod.Logger.Warning("{0}: {1} can't be used ({2}); this world keeps {3} for good, so its chunks match.", use.Label, name, why, use.Without);
                return;
            }
            string message = used
                ? string.Format("{0}: {1}, but {2} can't be had now ({3}). The server stops, so that no chunk of this world gets {4}; put the file in {5} " +
                    "(from {6}), or let the server reach it, and start again.", use.Label, use.With, name, why, use.Without, Folder, Downloader?.Repo ?? DemDownloader.Glo90Repo)
                : string.Format("{0}: {1} could not be fetched ({2}) while the world starts. The server stops (nothing is saved), so that a failure " +
                    "that may pass doesn't leave this world with {3} for good; start again once the server reaches {4}, or put the file in {5}.",
                    use.Label, name, why, use.Without, Downloader?.Repo ?? DemDownloader.Glo90Repo, Folder);
            Mod.Logger.Error("{0}", message);
            sapi.Server.ShutDown();
            throw new InvalidOperationException(message);
        }

        /// <summary>The world generates with the file from now on.</summary>
        void Used(ReleaseFileUse use)
        {
            var save = sapi.WorldManager.SaveGame;
            if (save.GetData(use.OnKey) == null) save.StoreData(use.OnKey, new byte[] { 1 });
        }

        /// <summary>
        /// 0053 item 17: the water mask's lake outlines for a world that has them (EarthParams.LakeOutlines). Without the file
        /// the world keeps the grid's lakes (ReleaseFileUse).
        /// </summary>
        void AttachLakeOutlines(TerrainModel terrain, EarthParams p)
        {
            // (version 2, decision 0069 rule 9: with Global Dam Watch's reservoirs left out too)
            int version = Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion);
            string name = LakeOutlineData.FileNameFor(version);
            if (KeptWithout(LakeOutlinesUse, name)) return;
            if (p.LakeOutlines > LakeOutlineData.FileVersion)
                Mod.Logger.Warning("Earth lakes: this world's lake outlines ({0}) are newer than this build's ({1}); this build's are used.", p.LakeOutlines, LakeOutlineData.FileVersion);
            byte[] file = ReleaseFile(LakeOutlinesUse, name, LakeOutlineData.FileBytesFor(version), LakeOutlineData.FileSha256For(version),
                (byte[] f, out string w) => LakeOutlineData.IsExpected(version, f, out w), out string why, out bool lacking);
            if (file == null) { Unavailable(LakeOutlinesUse, name, why, lacking); return; }
            terrain.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(file), terrain.Geometry);
            Used(LakeOutlinesUse);
            Mod.Logger.Notification("Earth lakes: the water mask's lakes from their outlines ({0}, decision 0053 item 17), wherever this world reads GLO-90; " +
                "shores measured {1} blocks out.", name, terrain.LakeOutlines.Reach);
        }

        /// <summary>
        /// 0053 item 18: the water mask's river outlines for a world that has them (EarthParams.RiverOutlines). Without the
        /// file the world keeps the courses' channels (ReleaseFileUse).
        /// </summary>
        void AttachRiverOutlines(TerrainModel terrain, EarthParams p)
        {
            int version = RiverOutlineData.Usable(p.RiverOutlines);
            // version 2 goes with the dam file (item 19): a world that keeps GLO-90's dams for good reads version 1's
            if (version >= 2 && sapi.WorldManager.SaveGame.GetData(DamsOffKey) != null) version = 1;
            string name = RiverOutlineData.FileName(version);
            if (KeptWithout(RiverOutlinesUse, name)) return;
            if (p.RiverOutlines > RiverOutlineData.FileVersion)
                Mod.Logger.Warning("Earth rivers: this world's river outlines ({0}) are newer than this build's ({1}); this build's are used.", p.RiverOutlines, RiverOutlineData.FileVersion);
            byte[] file = ReleaseFile(RiverOutlinesUse, name, RiverOutlineData.FileBytes(version), RiverOutlineData.FileSha256(version),
                (byte[] f, out string w) => RiverOutlineData.IsExpected(version, f, out w), out string why, out bool lacking);
            if (file == null) { Unavailable(RiverOutlinesUse, name, why, lacking); return; }
            // (0063 channel rules 1: without the narrow pieces along the courses where they draw every stream down to 20 m³/s)
            // (0073 channel rules 4: and with the narrow pieces the river's flow passes through; the flow file is read lazily, when
            // a tile is decoded, after AttachServer has attached it)
            terrain.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(file), terrain.Geometry, p.DropsNarrowRiverPieces, terrain.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? terrain.FlowCarriesAt : null);
            Used(RiverOutlinesUse);
            Mod.Logger.Notification("Earth rivers: the water mask's rivers from their outlines ({0}, decision 0053 item 18{1}), wherever this world reads GLO-90; " +
                "shores measured {2} blocks out.", name, version >= 2 ? ", through the reservoirs of item 19" : "", terrain.RiverOutlines.Reach);
        }

        DamRemovalData dams;
        /// <summary>The dam file this world's river courses were lowered with (EarthParams.RiverSource), for the river audit;
        /// null when the world takes no dams out or keeps them this session.</summary>
        public DamRemovalData Dams => dams;

        /// <summary>
        /// 0053 item 19 (proposed): the dam file for a world that takes the modern dams out (EarthParams.DamRules). Called
        /// before the world's model is built, which sets the file's river course levels (AttachServer then gives its heights
        /// to the GLO-90 tiles). Null: GLO-90's dams stay (ReleaseFileUse).
        /// </summary>
        public DamRemovalData LoadDams(EarthParams p)
        {
            dams = null;
            if (p.DamRules < 1 || p.FineElevation < 1 || sapi == null) return null;
            Ensure();
            // dam rules 1 read dams-glo90-1.bin, 2 (0063 rule 3) dams-glo90-2.bin
            int rules = Math.Min(p.DamRules, DamRemovalData.NewestRules);
            string name = DamRemovalData.FileNameFor(rules);
            if (KeptWithout(DamsUse, name)) return null;
            if (p.DamRules > DamRemovalData.NewestRules)
                Mod.Logger.Warning("Earth dams: this world's dam rules ({0}) are newer than this build's ({1}); this build's are used.", p.DamRules, DamRemovalData.NewestRules);
            byte[] file = ReleaseFile(DamsUse, name, DamRemovalData.FileBytesFor(rules), DamRemovalData.FileSha256For(rules),
                (byte[] f, out string w) => DamRemovalData.IsExpected(f, rules, out w), out string why, out bool lacking);
            if (file == null) { Unavailable(DamsUse, name, why, lacking); return null; }
            // (checked against the release and still unreadable: a property of the file, not a failure that may pass)
            try { dams = DamRemovalData.Read(file); }
            catch (Exception e) { Unavailable(DamsUse, name, e.Message, true); return null; }
            return dams;
        }

        RiverFlowData flow;

        /// <summary>
        /// Decision 0069: the river flow file for a world whose rules read it (EarthParams.FlowRules), before the model is
        /// built (its rerouted mouths go into the courses; AttachServer then gives the flow to the terrain). Null: the world
        /// keeps the courses' blended flow (ReleaseFileUse).
        /// </summary>
        public RiverFlowData LoadFlow(EarthParams p)
        {
            flow = null;
            if (p.FlowRules < 1 || p.FineElevation < 1 || sapi == null) return null;
            Ensure();
            string name = RiverFlowData.FileName;
            if (KeptWithout(FlowUse, name)) return null;
            if (p.FlowRules > RiverFlowData.FileVersion)
                Mod.Logger.Warning("Earth river flow: this world's flow rules ({0}) are newer than this build's ({1}); this build's are used.", p.FlowRules, RiverFlowData.FileVersion);
            byte[] file = ReleaseFile(FlowUse, name, RiverFlowData.FileBytes, RiverFlowData.FileSha256, RiverFlowData.IsExpected, out string why, out bool lacking);
            if (file == null) { Unavailable(FlowUse, name, why, lacking); return null; }
            // (checked against the release and still unreadable: a property of the file, not a failure that may pass)
            try { flow = RiverFlowData.Read(file); }
            catch (Exception e) { Unavailable(FlowUse, name, e.Message, true); return null; }
            return flow;
        }

        /// <summary>After the model is built: the dam file's heights for the GLO-90 tiles, when the model took its course levels
        /// (a file made for other river courses is refused like a missing one).</summary>
        void AttachDams(EarthParams p)
        {
            if (p.DamCourseError != null)
            {
                // a file made for other river courses: a property of the data, not a failure that may pass
                Unavailable(DamsUse, dams?.Name ?? DamRemovalData.FileNameFor(p.DamRules), p.DamCourseError, true);
                dams = null;
                return;
            }
            Fine.Dams = dams;
            Used(DamsUse);
            Mod.Logger.Notification("Earth dams: modern dams taken out of GLO-90 ({0}, decision 0053 item 19): heights in {1} squares, {2} river course " +
                "stations lowered through the old reservoirs.", dams.Name, dams.Squares, p.DamCourseLevels);
        }

        void OnFellBack(int latS, int lonW)
        {
            Mod.Logger.Warning("Earth elevation: GLO-90 tile {0} is in none of this server's sources (the release's manifest and Copernicus); that square " +
                "keeps the built-in heights in this world for good, so later chunks match the ones generated now.", DemTiles.ShortName(latS, lonW));
            sapi.Event.EnqueueMainThreadTask(() =>
                sapi.WorldManager.SaveGame.StoreData(FallbackKey, SerializerUtil.Serialize(Fine.FallbackKeys)), "earth-glo90-fallback");
        }

        /// <summary>Whether the game runs (RunGame): before, nothing of the world is saved yet.</summary>
        bool running;

        /// <summary>
        /// Review A3: a square whose tile couldn't be fetched now (the network, a download that didn't check out, the disk), not
        /// one the sources lack. Nothing is saved: before the game runs (the spawn chunks) the server stops, so the world opens
        /// as it was at the next start; while it runs, the square takes the built-in heights for this session only, said once,
        /// and is fetched again at the next start.
        /// </summary>
        void OnFetchFailed(int latS, int lonW)
        {
            string square = DemTiles.ShortName(latS, lonW);
            if (!running)
            {
                string message = "Earth elevation: GLO-90 tile " + square + " could not be fetched while the world starts (see the lines above; from " +
                    (Downloader.Repo ?? Downloader.Source ?? "no source") + "). The server stops, so that this world's first chunks don't take the built-in " +
                    "heights there; nothing is saved, start again once the tile can be had.";
                Mod.Logger.Error("{0}", message);
                sapi.Server.ShutDown();
                throw new InvalidOperationException(message);
            }
            Mod.Logger.Warning("Earth elevation: GLO-90 tile {0} could not be fetched now (see the lines above); chunks generated in that square " +
                "in this session take the built-in heights and may meet later ones with a step. It is fetched again at the next start.", square);
        }

        /// <summary>
        /// Before a chunk column's terrain: the tiles under it and a margin are fetched (in parallel; the chunk waits), and
        /// this thread may fetch any other tile its columns reach. Called on the generation thread.
        /// </summary>
        public void PrepareChunk(int chunkX, int chunkZ)
        {
            if (!WorldUses || model == null) return;
            FineElevation.FetchAllowed = true;
            var waits = new List<(int latS, int lonW, Task<bool> task)>();
            foreach (var (latS, lonW) in ChunkSquares(chunkX, chunkZ))
                if (Fine.SourceHas(latS, lonW) && !Fine.IsFallback(latS, lonW) && !Fine.IsHere(latS, lonW))
                    waits.Add((latS, lonW, Downloader.FetchAsync(latS, lonW)));
            // CR-D2: the squares are waited for in turn, all fetched at once: one backstop for the chunk, not one more per square
            long since = Environment.TickCount64;
            foreach (var (latS, lonW, task) in waits)
                if (!Downloader.AwaitForGeneration(latS, lonW, task, GenerationWait, since)) Fine.MarkFallback(latS, lonW, Downloader.Lacks(latS, lonW));
        }

        /// <summary>
        /// How long chunk generation waits for one GLO-90 square (CR-D2). Unbounded before the game runs: the spawn chunks wait,
        /// and a failure there stops the server with nothing saved (OnFetchFailed). While it runs, two minutes of the square's
        /// own download, counted from when it gets a download slot, and ten minutes in all however long it queued
        /// (DemDownloader.AwaitForGeneration): a square that takes longer is a failed fetch for this session (its chunks take
        /// the built-in heights, said once; fetched again at the next start), instead of a chunk thread held for half an hour
        /// while no chunk loads.
        /// </summary>
        TimeSpan GenerationWait => running ? TimeSpan.FromMinutes(2) : System.Threading.Timeout.InfiniteTimeSpan;

        /// <summary>The GLO-90 squares under a chunk and a 64-block margin, the ones PrepareChunk has here before the chunk's
        /// columns are made.</summary>
        HashSet<(int, int)> ChunkSquares(int chunkX, int chunkZ)
        {
            const int margin = 64;
            double x0 = chunkX * 32 - margin, z0 = chunkZ * 32 - margin, x1 = chunkX * 32 + 32 + margin, z1 = chunkZ * 32 + 32 + margin;
            var squares = new HashSet<(int, int)>();
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
            {
                var g = model.Geometry.ToGeo(model.RealSeam ? model.Geometry.WrapX(x) : x, z);
                if (g.InGutter) continue;
                const double spare = 1.0 / DemTiles.Glo90Rows;
                foreach (var sq in FineElevation.SquaresFor(g.Lat - spare, g.Lat + spare, g.Lon - spare, g.Lon + spare)) squares.Add(sq);
            }
            return squares;
        }

        /// <summary>
        /// Whether a chunk's columns (and a 64-block margin) come out on any thread as its generation makes them: every GLO-90
        /// square PrepareChunk would fetch for it is here or settled as fallback for good. A thread that may not fetch (any but
        /// a generation thread) takes the built-in heights for a square still to come, which generation would not. Never
        /// fetches; true where the world has no GLO-90 heights.
        /// </summary>
        public bool ChunkReady(int chunkX, int chunkZ)
        {
            if (!WorldUses || model == null) return true;
            foreach (var (latS, lonW) in ChunkSquares(chunkX, chunkZ))
                if (Fine.SourceHas(latS, lonW) && !Fine.IsFallback(latS, lonW) && !Fine.IsHere(latS, lonW)) return false;
            return true;
        }

        /// <summary>Tiles around online players, fetched ahead of generation (failures here don't fall back: generation retries).</summary>
        void Prefetch()
        {
            if (!WorldUses || model == null) return;
            int reach = core?.Config?.ElevationPrefetchBlocks ?? 0;
            if (reach <= 0) return;
            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                var pos = player.Entity?.Pos;
                if (pos == null) continue;
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        double x = pos.X + i * reach, z = pos.Z + j * reach;
                        var g = model.Geometry.ToGeo(model.RealSeam ? model.Geometry.WrapX(x) : x, z);
                        if (g.InGutter) continue;
                        int latS = Math.Clamp((int)Math.Ceiling(g.Lat) - 1, -90, 89), lonW = Math.Clamp((int)Math.Floor(g.Lon), -180, 179);
                        if (!Fine.SourceHas(latS, lonW) || Fine.IsFallback(latS, lonW) || Fine.IsHere(latS, lonW)) continue;
                        int key = latS * 360 + lonW;
                        long now = Environment.TickCount64;
                        if (prefetchFailed.TryGetValue(key, out var failed) && now < failed.retryAt) continue;
                        // CR-D3: while a square's fetch runs, FetchAsync hands every tick and every player the same task, and
                        // each continuation counted its one failure again (a 20 s failure as four, an hour's wait at once).
                        // One continuation per square watched: the next is attached once this one has recorded its result.
                        if (!prefetchWatching.TryAdd(key, 0)) continue;
                        Downloader.FetchAsync(latS, lonW).ContinueWith(t =>
                        {
                            try
                            {
                                if (t.IsCompletedSuccessfully && t.Result) { prefetchFailed.TryRemove(key, out _); return; }
                                // CR-D3: offline, a failed square was asked again every 5 s for each player, 7 log lines each time,
                                // holding the download slots generation queues behind: it waits 5 minutes, doubling to an hour
                                int n = prefetchFailed.TryGetValue(key, out var was) ? was.failures + 1 : 1;
                                prefetchFailed[key] = (n, Environment.TickCount64 + Math.Min(60, 5L << Math.Min(n - 1, 4)) * 60_000);
                            }
                            // released only after the record: a tick in between would otherwise ask again before the wait is set
                            finally { prefetchWatching.TryRemove(key, out _); }
                        }, TaskScheduler.Default);
                    }
                }
            }
        }

        /// <summary>Squares whose prefetch failed: how often in a row, and when Prefetch may ask again (generation never waits for this).</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int failures, long retryAt)> prefetchFailed = new();
        /// <summary>Squares whose prefetch Prefetch is waiting on (CR-D3): added on the main thread, removed by the continuation.</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> prefetchWatching = new();

        /// <summary>Tiles here (refreshed every 30 s, counting the folder; in between from the downloader's count).</summary>
        (int source, int here, long bytes) Census()
        {
            Ensure();
            if (census.source == 0 || (DateTime.UtcNow - censusAt).TotalSeconds > 30)
            {
                census = Downloader.Census();
                censusAt = DateTime.UtcNow;
                censusConverted = Downloader.TilesStored;
                censusStored = Downloader.BytesStored;
            }
            long extra = Downloader.TilesStored - censusConverted;
            return (census.source, (int)Math.Min(census.source, census.here + extra), census.bytes + Downloader.BytesStored - censusStored);
        }

        public ElevationStatus Status(bool canControl, string message = null)
        {
            Ensure();
            var c = Census();
            var b = Downloader.All;
            long free = -1;
            // (the file system the folder is on, CR-D5; shown only to whoever controls the server, CR-D6)
            if (canControl) try { Directory.CreateDirectory(Folder); free = DataTransport.FreeBytes(Folder); } catch (Exception) { }
            return new ElevationStatus
            {
                WorldUses = WorldUses,
                FallbackSquares = Fine.FallbackKeys.Length,
                SourceTiles = c.source,
                TilesHere = c.here,
                BytesHere = c.bytes,
                Running = b?.Running == true,
                BatchTotal = b?.Total ?? 0,
                BatchDone = b?.Done ?? 0,
                BatchFailed = b?.Failed ?? 0,
                BatchSkipped = b?.Skipped ?? 0,
                Downloaded = b == null ? 0 : Downloader.BytesDownloaded - b.DownloadedAtStart,
                Stored = b == null ? 0 : Downloader.BytesStored - b.StoredAtStart,
                ElapsedMinutes = b == null ? 0 : ((b.Running || b.Ended == default ? DateTime.UtcNow : b.Ended) - b.Started).TotalMinutes,
                CanControl = canControl,
                FreeBytes = free,
                Message = message,
                SourceBytes = DemDownloader.Glo90SourceBytes,
                StoredBytes = DemDownloader.Glo90StoredBytes,
                // the admin's earth.json settings (local paths, a private mirror): for whoever controls the server only (CR-D6)
                Source = canControl ? Downloader.Source : null,
                Repo = canControl ? Downloader.Repo : null,
                RepoBytes = DemDownloader.Glo90RepoBytes,
                BatchFrom = b == null ? null : b.From == DemDownloader.Origin.Copernicus ? "copernicus" : "release",
                BatchExpected = b?.Expected ?? 0,
                BatchError = b?.Error == null ? null : canControl ? b.Error : "see the server log",
            };
        }

        void OnRequest(IServerPlayer player, ElevationRequest req)
        {
            bool control = player.HasPrivilege(Privilege.controlserver);
            string message = null;
            if (req.Action == "start" || req.Action == "cancel")
            {
                if (!control) message = "Only a player with the server's control privilege can start or stop the download.";
                else message = req.Action == "start"
                    ? StartAll(player.PlayerName, req.From == "copernicus" ? DemDownloader.Origin.Copernicus : DemDownloader.Origin.Repo)
                    : CancelAll(player.PlayerName);
            }
            serverChannel.SendPacket(Status(control, message), player);
        }

        public string StartAll(string who, DemDownloader.Origin from = DemDownloader.Origin.Repo)
        {
            Ensure();
            var c = Census();
            string origin = from == DemDownloader.Origin.Repo ? Downloader.Repo : Downloader.Source;
            if (origin == null) return from == DemDownloader.Origin.Repo ? "This server has no data release set (ElevationRepo)." : "This server has no Copernicus source set (ElevationSource).";
            if (!Downloader.StartAll(Math.Clamp(core?.Config?.ElevationDownloads ?? 3, 1, 8), from)) return "The download is already running.";
            Mod.Logger.Notification("Earth elevation: download all started by {0}: {1} of {2} squares already here, from {3} into {4}.",
                who, c.here, c.source, origin, Folder);
            return "Download started.";
        }

        public string CancelAll(string who)
        {
            if (Downloader?.All?.Running != true) return "No download is running.";
            stopAsked = true;
            Downloader.CancelAll();
            Mod.Logger.Notification("Earth elevation: download all stopped by {0}; tiles already being fetched finish.", who);
            return "Stopping after the tiles being fetched now.";
        }

        // ---- the Earth tab's GLO-90 land heights set to "Download all"

        /// <summary>Save data: how the Earth tab's download all ended in this world ("done" or "stopped").</summary>
        public const string AutoDownloadKey = "earth-glo90-download-all";
        bool autoRunning, stopAsked;

        /// <summary>
        /// The Earth tab's GLO-90 land heights set to "Download all": the download starts when the world opens, since the world
        /// creation screen runs no mod code, and its window opens for players who can stop it. It starts again each time the
        /// world opens until every square is here or a player stops it; the save remembers which.
        /// </summary>
        void AutoDownload()
        {
            // only an active Earth world on full GLO-90 heights (AttachServer ran at WorldReady): the game copies every
            // installed mod's world-config defaults into every new world, so a vanilla world, a refused one or one on
            // other heights holds "release" too
            if (core?.Params?.Enabled != true || !WorldUses) return;
            var save = sapi.WorldManager.SaveGame;
            string from = EarthWorldOptions.Glo90Download(save.WorldConfiguration);
            if (from == null || save.GetData(AutoDownloadKey) != null) return;
            Ensure();
            var c = Census();
            if (c.here >= c.source) { MarkAutoDownload("done"); return; }
            string result = StartAll("this world's Earth tab", from == "copernicus" ? DemDownloader.Origin.Copernicus : DemDownloader.Origin.Repo);
            autoRunning = Downloader.All?.Running == true;
            if (!autoRunning) Mod.Logger.Warning("Earth elevation: the Earth tab asks to download all GLO-90 squares, but: {0}", result);
        }

        void AutoDownloadEnded()
        {
            if (!autoRunning || Downloader?.All?.Running == true) return;
            autoRunning = false;
            var b = Downloader?.All;
            if (b == null) return;
            // stopped by a player: not again; stopped by the server shutting down, or failures: the next opening goes on
            if (b.Cancelled && stopAsked) MarkAutoDownload("stopped");
            else if (!b.Cancelled && b.Error == null && b.Failed == 0) MarkAutoDownload("done");
        }

        void MarkAutoDownload(string how)
        {
            sapi.WorldManager.SaveGame.StoreData(AutoDownloadKey, System.Text.Encoding.UTF8.GetBytes(how));
            Mod.Logger.Notification("Earth elevation: the Earth tab's download all is {0} in this world.", how);
        }

        void OnNowPlaying(IServerPlayer player)
        {
            if (!autoRunning || Downloader?.All?.Running != true || !player.HasPrivilege(Privilege.controlserver)) return;
            var s = Status(true, "This world's Earth tab asked for every GLO-90 square. You can play meanwhile.");
            s.Open = true;
            serverChannel.SendPacket(s, player);
        }

        public string StatusText(bool canControl)
        {
            var s = Status(canControl);
            return ElevationStatus.Describe(s) + "\nOpen the dialog with .earthelevation.\n" + Attribution;
        }

        // ------------------------------------------------------------------------------------------ client

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<ElevationRequest>()
                .RegisterMessageType<ElevationStatus>()
                .SetMessageHandler<ElevationStatus>(s =>
                {
                    if (s.Open) OpenWhenFree();
                    else dialog?.Show(s);
                });
            api.ChatCommands.Create("earthelevation")
                .WithDescription("Earth: land heights from Copernicus GLO-90 (status, download all)")
                .HandleWith(_ => { OpenDialog(); return TextCommandResult.Success(); });
        }

        ElevationDialog dialog;
        long tickId;

        void OpenDialog()
        {
            if (!clientChannel.Connected)
            {
                capi.ShowChatMessage("Earth elevation: the server doesn't have this Earth version's elevation channel.");
                return;
            }
            dialog ??= new ElevationDialog(capi, (action, from) => clientChannel.SendPacket(new ElevationRequest { Action = action, From = from }), () =>
            {
                if (tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
                tickId = 0;
            });
            if (!dialog.IsOpened()) dialog.TryOpen();
            clientChannel.SendPacket(new ElevationRequest { Action = "status" });
            if (tickId == 0) tickId = capi.Event.RegisterGameTickListener(_ => clientChannel.SendPacket(new ElevationRequest { Action = "status" }), 2000);
        }

        long pendingOpen;

        /// <summary>The window, once no other dialog is open: a new world's first join opens character creation, and this
        /// one waits for it.</summary>
        void OpenWhenFree()
        {
            if (pendingOpen != 0) return;
            pendingOpen = capi.Event.RegisterGameTickListener(_ =>
            {
                if (capi.Gui.OpenedGuis.Any(g => g.DialogType == EnumDialogType.Dialog && g != dialog)) return;
                capi.Event.UnregisterGameTickListener(pendingOpen);
                pendingOpen = 0;
                OpenDialog();
            }, 1000, 2000);
        }

        public override void Dispose()
        {
            Downloader?.Dispose();
            base.Dispose();
        }
    }

    [ProtoContract]
    public class ElevationRequest
    {
        /// <summary>"status", "start" or "cancel".</summary>
        [ProtoMember(1)] public string Action;
        /// <summary>For "start": "release" (Earth's data release, the default) or "copernicus".</summary>
        [ProtoMember(2)] public string From;
    }

    [ProtoContract]
    public class ElevationStatus
    {
        [ProtoMember(1)] public bool WorldUses;
        [ProtoMember(2)] public int FallbackSquares;
        [ProtoMember(3)] public int SourceTiles;
        [ProtoMember(4)] public int TilesHere;
        [ProtoMember(5)] public long BytesHere;
        [ProtoMember(6)] public bool Running;
        [ProtoMember(7)] public int BatchTotal;
        [ProtoMember(8)] public int BatchDone;
        [ProtoMember(9)] public int BatchFailed;
        [ProtoMember(10)] public int BatchSkipped;
        [ProtoMember(11)] public long Downloaded;
        [ProtoMember(12)] public long Stored;
        [ProtoMember(13)] public double ElapsedMinutes;
        [ProtoMember(14)] public bool CanControl;
        [ProtoMember(15)] public long FreeBytes;
        [ProtoMember(16)] public string Message;
        [ProtoMember(17)] public long SourceBytes;
        [ProtoMember(18)] public long StoredBytes;
        [ProtoMember(19)] public string Source;
        [ProtoMember(20)] public string Repo;
        [ProtoMember(21)] public long RepoBytes;
        /// <summary>The running or last "download all": "release" or "copernicus", and the bytes it expects to download.</summary>
        [ProtoMember(22)] public string BatchFrom;
        [ProtoMember(23)] public long BatchExpected;
        [ProtoMember(24)] public string BatchError;
        /// <summary>Open the window: the Earth tab's download all started as the world opened.</summary>
        [ProtoMember(25)] public bool Open;

        public static string GB(long b) => (b / 1e9).ToString(b < 10e9 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " GB";

        /// <summary>What "download all" from <paramref name="from"/> still has to download, and what it keeps.</summary>
        public (long download, long keep) Remaining(string from)
        {
            double left = SourceTiles > 0 ? Math.Max(0, SourceTiles - TilesHere) / (double)SourceTiles : 1;
            long all = from == "copernicus" ? SourceBytes : RepoBytes > 0 ? RepoBytes : StoredBytes;
            return ((long)(all * left), (long)(StoredBytes * left));
        }

        public static string Describe(ElevationStatus s)
        {
            var lines = new List<string>
            {
                s.WorldUses
                    ? "This world: land heights from GLO-90" + (s.FallbackSquares > 0 ? $" ({s.FallbackSquares} squares keep the included heights)." : ".")
                    : "This world: land heights chosen when it was created, without the full GLO-90.",
                $"On the server: {s.TilesHere:N0} of {s.SourceTiles:N0} squares ({GB(s.BytesHere)}); free disk space {(s.FreeBytes >= 0 ? GB(s.FreeBytes) : "unknown")}.",
            };
            if (s.Running || s.BatchTotal > 0)
            {
                int left = s.BatchTotal - s.BatchSkipped - s.BatchDone - s.BatchFailed;
                string from = s.BatchFrom == "copernicus" ? "from Copernicus" : "from Earth's data release";
                string eta = "";
                if (s.Running && s.ElapsedMinutes > 0.1 && s.Downloaded > 0 && s.BatchExpected > s.Downloaded)
                {
                    double min = (s.BatchExpected - s.Downloaded) / (s.Downloaded / s.ElapsedMinutes);
                    eta = min < 1 ? ", under a minute left"
                        : min < 60 ? $", about {Math.Ceiling(min):F0} min left"
                        : $", about {(int)(min / 60)} h {(int)(min % 60):00} min left";
                }
                string of = s.BatchExpected > 0 ? $" of about {GB(s.BatchExpected)}" : "";
                lines.Add((s.Running ? "Downloading " : "Last download ") + from + $": {s.BatchDone:N0} squares kept, {s.BatchFailed:N0} failed, " +
                    $"{Math.Max(0, left):N0} to go; {GB(s.Downloaded)}{of} downloaded{eta}." + (s.BatchError != null ? " Stopped: " + s.BatchError + "." : ""));
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>The client's dialog: status, where "download all" fetches from, and Download all behind a confirmation.</summary>
    public class ElevationDialog : GuiDialog
    {
        readonly Action<string, string> send;
        readonly Action closed;
        ElevationStatus last;
        bool confirming;
        /// <summary>Where Download all fetches from: "release" (Earth's data release, the default) or "copernicus".</summary>
        string from = "release";
        const double W = 600;

        static readonly string[] FromCodes = { "release", "copernicus" };
        static readonly string[] FromNames =
        {
            "Earth's data release (GitHub, 8.6 GB, ready to use)",
            "Copernicus originals (Amazon S3, 71 GB, converted here)",
        };

        public override string ToggleKeyCombinationCode => null;

        public ElevationDialog(ICoreClientAPI capi, Action<string, string> send, Action closed) : base(capi)
        {
            this.send = send;
            this.closed = closed;
            Compose();
        }

        /// <summary>What Download all does from the chosen origin, or the question before it starts.</summary>
        string Explain()
        {
            var s = last;
            var (download, keep) = s?.Remaining(from) ?? (from == "copernicus" ? DemDownloader.Glo90SourceBytes : DemDownloader.Glo90RepoBytes, DemDownloader.Glo90StoredBytes);
            string GB(long b) => ElevationStatus.GB(b);
            if (confirming)
                return (from == "copernicus"
                        ? "Download about " + GB(download) + " from Copernicus, convert it here and keep about " + GB(keep) + "?"
                        : "Download and keep about " + GB(keep) + " from Earth's data release?")
                    + (s != null && s.FreeBytes >= 0 && s.FreeBytes < keep * 1.2 ? " The server's disk has only " + GB(s.FreeBytes) + " free." : "");
            return (from == "copernicus"
                    ? "Download all fetches every original GLO-90 tile from Copernicus's public bucket on Amazon S3, a few at a time: about " +
                      GB(download) + " to download, over several hours. Each tile is converted as it arrives (this uses the CPU) and only the " +
                      "result is kept: about " + GB(keep) + " on the server's disk. The result is the same as from the release."
                    : "Download all fetches every GLO-90 square from Earth's data release on GitHub (Temporal-Poets-Society/earth-glo90), " +
                      "already converted: about " + GB(download) + " to download and keep on the server's disk. The files belong to no Earth " +
                      "version, so this is needed once per machine.") +
                " You can stop at any time and continue later; squares already here are skipped. Without this, worlds download " +
                "only the squares they reach.";
        }

        void Compose()
        {
            var s = last;
            var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            var status = ElementBounds.Fixed(0, 32, W, 100);
            var fromLabel = ElementBounds.Fixed(0, 144, 150, 25);
            var fromBox = ElementBounds.Fixed(150, 138, W - 150, 28);
            var explain = ElementBounds.Fixed(0, 178, W, 100);
            var note = ElementBounds.Fixed(0, 284, W, 60);
            var row = 352.0;
            SingleComposer?.Dispose();
            var c = capi.Gui.CreateCompo("earth-elevation", dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar("Earth: land heights (Copernicus GLO-90)", () => TryClose())
                .BeginChildElements(bg)
                    .AddDynamicText(s == null ? "Asking the server..." : ElevationStatus.Describe(s) + (s.Message != null ? "\n" + s.Message : ""),
                        CairoFont.WhiteSmallText(), status, "status")
                    .AddStaticText("Download from", CairoFont.WhiteSmallText(), fromLabel)
                    .AddDropDown(FromCodes, FromNames, Math.Max(0, Array.IndexOf(FromCodes, from)), (code, selected) =>
                        {
                            from = code;
                            SingleComposer?.GetDynamicText("explain")?.SetNewText(Explain());
                        }, fromBox, "from")
                    .AddDynamicText(Explain(), CairoFont.WhiteDetailText(), explain, "explain")
                    .AddStaticText(EarthElevationSystem.Attribution, CairoFont.WhiteDetailText().WithFontSize(11), note);
            if (confirming)
            {
                c.AddSmallButton("Yes, download all", () => { confirming = false; send("start", from); Compose(); return true; }, ElementBounds.Fixed(0, row, 200, 28), EnumButtonStyle.Normal, "yes")
                 .AddSmallButton("Cancel", () => { confirming = false; Compose(); return true; }, ElementBounds.Fixed(210, row, 120, 28));
            }
            else
            {
                bool running = s?.Running == true;
                c.AddSmallButton(running ? "Stop download" : "Download all...", () =>
                    {
                        if (running) send("cancel", null); else { confirming = true; Compose(); }
                        return true;
                    }, ElementBounds.Fixed(0, row, 200, 28), EnumButtonStyle.Normal, "start")
                 .AddSmallButton("Close", () => TryClose(), ElementBounds.Fixed(W - 120, row, 120, 28));
            }
            SingleComposer = c.EndChildElements().Compose();
            bool control = s != null && s.CanControl;
            var button = SingleComposer.GetButton(confirming ? "yes" : "start");
            if (button != null) button.Enabled = control;
            var box = SingleComposer.GetDropDown("from");
            if (box != null) box.Enabled = control && s?.Running != true;
        }

        /// <summary>A status from the server; the explanation's sizes follow what is still missing.</summary>
        public void Show(ElevationStatus s)
        {
            bool recompose = last == null || last.Running != s.Running || last.CanControl != s.CanControl || last.TilesHere != s.TilesHere && !s.Running;
            last = s;
            if (recompose || SingleComposer == null) { Compose(); return; }
            SingleComposer.GetDynamicText("status")?.SetNewText(ElevationStatus.Describe(s) + (s.Message != null ? "\n" + s.Message : ""));
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            confirming = false;
            closed?.Invoke();
        }
    }
}
