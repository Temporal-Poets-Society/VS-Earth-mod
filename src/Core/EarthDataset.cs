using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's baked data files as read (see tools/bake_earth.py and EarthData for the grids): the layout from earth.json,
    /// climate.bin and its fix files, terrain.bin's tile index, weather.bin, and with terrain the optional overlay files
    /// (islands, terminal lakes, shores, outlets, coasts, the sea referee, works 1318) and the river sources. The optional
    /// grids a world may choose (CHELSA's temperatures and rain, the Penman aridity, summits, glaciers, the reservoir
    /// exclusions, the included GLO-90 heights) are read the first time a world asks for them, each once, and kept with
    /// why they could not be read. Which of them a world reads, and how, is EarthData's (one world per server).
    /// </summary>
    public sealed class EarthDataset
    {
        // where the data came from (a folder holding the files, or the mod zip): optional files are read from it later
        readonly Func<string, Stream> openSource;
        readonly object loadLock = new object();

        EarthDataset(Func<string, Stream> open) { openSource = open; RiverSources = new RiverSourceRepository(open); }

        /// <summary>An optional data file by name, from where the rest came (the mod, or the data pack the pack index routes it
        /// to); null when it isn't there. The caller disposes the stream.</summary>
        public Stream OpenOptional(string name) => openSource?.Invoke(name);

        // ------------------------------------------------------------------------------------------ terrain grid

        public int TerrainRes { get; private set; }
        public int TerrainW { get; private set; }
        public int TerrainH { get; private set; }
        /// <summary>Cells along a side of a terrain.bin tile, and the tiles across and down.</summary>
        public int Tile { get; private set; }
        public int TilesX { get; private set; }
        public int TilesY { get; private set; }
        byte[] terrainFile;
        int[] tileOffset, tileLength;
        float[] landLut, seaLut;
        public bool HasTerrain => terrainFile != null;

        /// <summary>The elevation (m) of a terrain.bin elevation code (land 50*expm1(q/landQ), sea -50*expm1(-q/seaQ)).</summary>
        public float ElevationOf(int v) => v >= 0 ? landLut[Math.Min(v, landLut.Length - 1)] : seaLut[Math.Min(-v, seaLut.Length - 1)];
        // (EA-0087) ElevationOf of every code a tile keeps (TerrainTile.Code: -4095..8191), one table, no branch
        float[] elevationOfCode;
        /// <summary>ElevationOf of a code as a tile keeps it (<see cref="TerrainTile.Code"/>).</summary>
        internal float ElevationOfCode(short c) => elevationOfCode[c + TerrainTile.SeaCodes];

        // ------------------------------------------------------------------------------------------ climate grid

        public int ClimateRes { get; private set; }
        public int ClimateW { get; private set; }
        public int ClimateH { get; private set; }
        // climate.bin's planes as stored (EA-0087: codes, 10 bytes a cell; ClimateCodes gives the floats they were kept as):
        // yearly mean, coldest and warmest month, precipitation, rainfall, summer dryness
        ClimateCodes climate;
        // (EA-0077 L-2) Sha8 of climate.bin's planes as stored (the plain layout, either coding): a predicted fix 1 or 2
        // names the climate.bin its differences are from
        ulong climateBase;

        // the climate fixes (decision 0018), each the cells of climate.bin it changes and its values there: [1]
        // climatefix.bin, UDel's missing months filled; [2] climatefix2.bin, the complete UDel normals (the "missing
        // months" and December's 0 mm were a cut-short download, 0018 Correction); [3] climatefix3.bin, v2 with UDel's
        // precipitation faded into GPCP's over a 1° coast band instead of one row (EA-0016); [4] climatefix4.bin, v3 with GPCP's
        // share at CHELSA's level on desert coasts (EA-0060). Fixes 1 and 2 are each against climate.bin;
        // fix 3 and later against the fix before (only the cells they change), so they need it too
        readonly int[][] fixIndex = new int[EarthData.ClimateFixVersion + 1][];
        readonly ClimateCodes[] fixNew = new ClimateCodes[EarthData.ClimateFixVersion + 1];
        // the planes with each fix a world asked for, made the first time it asks (ClimatePlanes); the versions in between
        // are not kept (EA-0087: every step of the chain 0-2-3-4 was, about 50 MB more on both sides)
        readonly ClimateCodes[] planesByFix = new ClimateCodes[EarthData.ClimateFixVersion + 1];
        /// <summary>The newest climate fix whose file was found (loaded on both sides), 0 for none.</summary>
        public int ClimateFixAvailable { get; private set; }
        /// <summary>Whether the file of this climate fix version was found (from version 3, with the versions it builds on).</summary>
        public bool HasClimateFixVersion(int version) => version > 0 && version <= EarthData.ClimateFixVersion && fixIndex[version] != null
            && (version < 3 || HasClimateFixVersion(version - 1));

        /// <summary>
        /// The climate grid's six planes (yearly mean, coldest and warmest month, precipitation, rainfall, summer dryness)
        /// with climate fix <paramref name="version"/> (one whose file is there; 0: climate.bin's own). Fixes 1 and 2 are
        /// against climate.bin itself, fix 3 and later against the version before, so climate.bin stays as read and every
        /// version can be asked for at any time. Made once per version asked for and shared, never changed; a plane the fix
        /// leaves code for code as it was is the array of the planes it starts from. As the stored codes (EA-0087):
        /// <see cref="ClimateCodes.Value"/> gives the floats the planes were kept as before.
        /// </summary>
        internal ClimateCodes ClimatePlanes(int version)
        {
            var made = Volatile.Read(ref planesByFix[version]);
            if (made != null) return made;
            lock (loadLock)
            {
                if (planesByFix[version] != null) return planesByFix[version];
                var planes = BuildPlanes(version);
                Volatile.Write(ref planesByFix[version], planes);
                return planes;
            }
        }

        /// <summary>The planes with fix <paramref name="version"/>, from a kept version where the chain passes one, without
        /// keeping the versions in between (under loadLock).</summary>
        ClimateCodes BuildPlanes(int version)
        {
            if (planesByFix[version] != null) return planesByFix[version];
            if (version == 0) return climate;
            var idx = fixIndex[version] ?? throw new ArgumentException("climate fix " + version + " isn't there");
            return (version >= 3 ? BuildPlanes(version - 1) : climate).With(idx, fixNew[version]);
        }

        // ------------------------------------------------------------------------------------------ files read at load

        /// <summary>The monthly normals for the weather (weather.bin), or null when the file isn't there or can't be read
        /// (WeatherError). Loaded on both sides.</summary>
        public WeatherData Weather { get; private set; }
        /// <summary>Why weather.bin could not be read (Weather is then null and vanilla's weather stays), or null.</summary>
        public string WeatherError { get; private set; }

        byte[] weatherGroundFile;
        WeatherGround weatherGround;
        bool weatherGroundTried;
        /// <summary>Why the weather's ground (rules 7) can't be had, or null.</summary>
        public string WeatherGroundError { get; private set; }
        /// <summary>(EA-0118) The weather's ground's fingerprint (weatherground.bin and the precipitation of fixes 3 and 4 it
        /// reads), set when <see cref="WeatherGround"/> is made; null before or without it.</summary>
        public string WeatherGroundSignature { get; private set; }

        /// <summary>(Weather rules 7) weatherground.bin's heights with the precipitation climate fix 4 takes off fix 3
        /// (Core.WeatherGround), made the first time it is asked for; null when either file is missing or damaged
        /// (WeatherGroundError). Loaded on both sides.</summary>
        public WeatherGround WeatherGround
        {
            get
            {
                if (Volatile.Read(ref weatherGroundTried)) return weatherGround;
                lock (loadLock)
                {
                    if (weatherGroundTried) return weatherGround;
                    try
                    {
                        if (weatherGroundFile == null) WeatherGroundError = "earthdata/" + WeatherGround.FileName + " is missing";
                        else if (!HasClimateFixVersion(4) || fixNew[4].Precip == null) WeatherGroundError = "earthdata/climatefix4.bin is missing";
                        else
                        {
                            // fix 3's precipitation where fix 4 changes it, without keeping fix 3's planes
                            var idx = fixIndex[4];
                            var before = BuildPlanes(3);
                            var old = new float[idx.Length];
                            var now = new float[idx.Length];
                            for (int k = 0; k < idx.Length; k++) { old[k] = before.Value(3, idx[k]); now[k] = fixNew[4].Value(3, k); }
                            weatherGround = WeatherGround.Read(new MemoryStream(weatherGroundFile), ClimateW, ClimateH, idx, now, old);
                            // (EA-0118) what the server sends for the clients to compare: the file and the precipitation it
                            // takes from fix 3 and fix 4 (so climate.bin and the fixes it reads are in it too)
                            WeatherGroundSignature = WeatherNoise.Fnv1a(weatherGroundFile).ToString("x16")
                                + WeatherNoise.Fnv1a(System.Runtime.InteropServices.MemoryMarshal.AsBytes<int>(idx)).ToString("x16")
                                + WeatherNoise.Fnv1a(System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(now)).ToString("x16")
                                + WeatherNoise.Fnv1a(System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(old)).ToString("x16");
                        }
                    }
                    catch (Exception e)
                    {
                        weatherGround = null;
                        WeatherGroundError = "earthdata/" + WeatherGround.FileName + ": " + e.Message;
                    }
                    Volatile.Write(ref weatherGroundTried, true);
                    return weatherGround;
                }
            }
        }
        /// <summary>rivers.bin and the rivers on GLO-90 with their overlays.</summary>
        public RiverSourceRepository RiverSources { get; }
        /// <summary>The small islands and their grid patches (islands.bin) as the file has them, or null when the file isn't
        /// there or wasn't requested.</summary>
        public IslandData Islands { get { lock (loadLock) { if (asReadDropped) ReadAsReadAgain(); return islands; } } private set => islands = value; }
        /// <summary>The big land's outlines (coasts.bin) as the file has them, or null.</summary>
        public CoastData Coasts { get { lock (loadLock) { if (asReadDropped) ReadAsReadAgain(); return coasts; } } private set => coasts = value; }
        /// <summary>Whether islands.bin was read (a world's view may turn the island overlay on), without reading it again.</summary>
        public bool HasIslands => hasIslands;

        // EA-0108 (the Fable pass's FP-18): the islands and coasts as read are dropped once the sea referee's are built (a world
        // reads one of the two; the referee's share most islands with them, so this frees 56 MB), and read again from the
        // files should a view without the referee ask for them
        IslandData islands;
        CoastData coasts;
        bool hasIslands, asReadDropped;

        /// <summary>EA-0108: the referee's islands and coasts are built: the as-read ones go (TerrainOverlayTables.Referee).</summary>
        internal void DropAsRead()
        {
            lock (loadLock)
            {
                if (islands == null || coasts == null || openSource == null) return;
                coastsHash = EarthBinary.Sha8(coasts.RawFile);
                islands = null;
                coasts = null;
                asReadDropped = true;
            }
        }

        void ReadAsReadAgain()
        {
            using (var isl = openSource("islands.bin") ?? throw new FileNotFoundException("earthdata/islands.bin"))
            {
                byte[] b = ReadAll(isl);
                if (EarthBinary.Sha8(b) != islandsHash) throw new InvalidDataException("islands.bin changed while the game ran");
                islands = IslandData.Read(new MemoryStream(b), TerrainW);
            }
            using (var co = openSource("coasts.bin") ?? throw new FileNotFoundException("earthdata/coasts.bin")) coasts = CoastData.Read(co);
            // (Fable's re-review of c6a56c1) both halves of the pair checked
            if (EarthBinary.Sha8(coasts.RawFile) != coastsHash) throw new InvalidDataException("coasts.bin changed while the game ran");
            asReadDropped = false;
        }
        /// <summary>The terminal lakes and their grid patches (lakes.bin), or null when the file isn't there or wasn't requested.</summary>
        public LakeData TerminalLakes { get; private set; }
        /// <summary>The land around lakes eased below their level (shores.bin), or null when the file isn't there or wasn't requested.</summary>
        public ShoreData Shores { get; private set; }
        /// <summary>The lakes' tongues past their outlet as land (outlets.bin), or null when the file isn't there or wasn't requested.</summary>
        public OutletData Outlets { get; private set; }
        /// <summary>The water mask's referee of the sea (sea-referee.bin), or null when the file isn't there or wasn't requested.</summary>
        public SeaRefereeData SeaReferee { get; private set; }
        /// <summary>Decision 0064: the areas where coasts and waters go back to 1318 (works-1318-2.bin, Works1318Data), or null
        /// when the file isn't there or wasn't requested.</summary>
        public LakeOutlineData Works1318 { get; private set; }
        /// <summary>Decision 0069 rule 8: the rivers GIRES has as non-perennial (rivers-nonperennial-2.bin, NonPerennialData),
        /// or null when the file isn't there or wasn't requested.</summary>
        public NonPerennialData NonPerennial { get; private set; }
        /// <summary>Decision 0070: OpenStreetMap's beaches by material (shores-osm-1.bin, ShoreMaterialData), or null when the
        /// file isn't there or wasn't requested.</summary>
        public ShoreMaterialData ShoreMaterial { get; private set; }
        ulong islandsHash, coastsHash;
        int refereeState = -1;

        /// <summary>0: the referee can't be had; 1: sea-referee.bin applies to these files; 2: they already have it.</summary>
        internal int RefereeState()
        {
            if (refereeState >= 0) return refereeState;
            lock (loadLock)
            {
                if (refereeState >= 0) return refereeState;
                int st = 0;
                if (SeaReferee != null && terrainFile != null && Islands != null && Coasts != null)
                {
                    var h = new[] { EarthBinary.Sha8(terrainFile), islandsHash, EarthBinary.Sha8(Coasts.RawFile) };
                    bool isBase = true, isTarget = true;
                    for (int i = 0; i < 3; i++) { isBase &= h[i] == SeaReferee.Base[i]; isTarget &= h[i] == SeaReferee.Target[i]; }
                    st = isBase ? 1 : isTarget ? 2 : 0;
                }
                return refereeState = st;
            }
        }

        // ------------------------------------------------------------------------------------------ files read on first use
        // (each once, under the load lock: a world chooses them when its model is built, but a tool or test may ask from
        // several threads)

        FineTemperature fine;
        bool fineTried;
        /// <summary>Why temperature.bin could not be read (the 0.25° grid's temperatures stay), or null.</summary>
        public string FineTemperatureError { get; private set; }

        /// <summary>CHELSA's temperatures on the 2' grid (temperature.bin), read the first time a world asks; null when the
        /// file isn't there or can't be read (FineTemperatureError).</summary>
        public FineTemperature FineTemperatures()
        {
            lock (loadLock)
            if (!fineTried)
            {
                fineTried = true;
                try
                {
                    using var s = openSource?.Invoke("temperature.bin");
                    if (s != null) fine = FineTemperature.Read(s);
                    else FineTemperatureError = "earthdata/temperature.bin is missing";
                }
                catch (Exception e)
                {
                    fine = null;
                    FineTemperatureError = "earthdata/temperature.bin: " + e.Message;
                }
            }
            return fine;
        }

        public PeakCatalog ReadPeaks()
        {
            using var s = openSource?.Invoke("peaks.json");
            return s == null ? null : new PeakCatalog(ReadAll(s));
        }

        Dictionary<int,int[]> excludedReservoirCells;

        /// <summary>The reservoirs' lake cells (reservoirs-v1.json) by terrain tile, as indices in the tile; read the first
        /// time a world asks. Throws when the file is missing or damaged.</summary>
        internal Dictionary<int,int[]> ReservoirCells()
        {
            lock (loadLock)
            if(excludedReservoirCells==null)
            {
                using var stream=openSource?.Invoke("reservoirs-v1.json") ?? throw new FileNotFoundException("reservoirs-v1.json");
                using var doc=JsonDocument.Parse(stream);
                if(doc.RootElement.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("reservoirs-v1.json version");
                var groups=new Dictionary<int,List<int>>();
                foreach(var cell in doc.RootElement.GetProperty("cells").EnumerateArray())
                {
                    int r=cell[0].GetInt32(),c=cell[1].GetInt32();
                    if(r<0||r>=TerrainH||c<0||c>=TerrainW)throw new InvalidDataException("Reservoir cell out of bounds");
                    int key=(r/Tile)*TilesX+c/Tile;
                    if(!groups.TryGetValue(key,out var list))groups[key]=list=new();
                    list.Add((r%Tile)*Tile+c%Tile);
                }
                excludedReservoirCells=new();foreach(var g in groups)excludedReservoirCells[g.Key]=g.Value.ToArray();
            }
            return excludedReservoirCells;
        }

        // decision 0031's data

        /// <summary>One of the optional tiled grids, read on first use; null (with the reason) when missing or unreadable.</summary>
        TiledGrid LoadGrid(string name, string magic, int bytesPerCell, out string error, int version = 1)
        {
            error = null;
            try
            {
                using var s = openSource?.Invoke(name);
                if (s == null) { error = "earthdata/" + name + " is missing"; return null; }
                var grid = new TiledGrid(TiledGrid.ReadAll(s), magic, bytesPerCell);
                if (grid.Version != version) { error = "earthdata/" + name + ": unsupported version " + grid.Version; return null; }
                return grid;
            }
            catch (Exception e)
            {
                error = "earthdata/" + name + ": " + e.Message;
                return null;
            }
        }

        TiledGrid fineRain, summits, glaciers, penman;
        bool fineRainTried, summitsTried, glaciersTried, penmanTried;
        /// <summary>Why rain.bin, summits.bin, glaciers.bin or aridity.bin could not be read, or null.</summary>
        public string FineRainError { get; private set; }
        public string SummitsError { get; private set; }
        public string GlaciersError { get; private set; }
        public string PenmanError { get; private set; }

        /// <summary>CHELSA's rainfall and summer dryness on the 2' grid (rain.bin), read on first use; null without.</summary>
        public TiledGrid FineRain()
        {
            lock (loadLock)
            if (!fineRainTried) { fineRainTried = true; fineRain = LoadGrid("rain.bin", "EMRN", 3, out var e); FineRainError = e; }
            return fineRain;
        }

        /// <summary>The Penman-Monteith aridity factor (aridity.bin v1), read on first use; null without.</summary>
        public TiledGrid PenmanAridity()
        {
            lock (loadLock)
            if (!penmanTried) { penmanTried = true; penman = LoadGrid("aridity.bin", "EMAI", 1, out var e); PenmanError = e; }
            return penman;
        }

        /// <summary>ETOPO 2022's 30" summit excess in mountain tiles (summits.bin v2), read on first use; null without.</summary>
        public TiledGrid Summits()
        {
            lock (loadLock)
            if (!summitsTried)
            {
                summitsTried = true;
                summits = LoadGrid("summits.bin", "EMSU", 1, out var e, 2);
                SummitsError = e;
            }
            return summits;
        }

        /// <summary>RGI 6's glacier coverage on a 30" grid (glaciers.bin), read on first use; null without.</summary>
        public TiledGrid Glaciers()
        {
            lock (loadLock)
            if (!glaciersTried) { glaciersTried = true; glaciers = LoadGrid("glaciers.bin", "EMGL", 1, out var e); GlaciersError = e; }
            return glaciers;
        }

        CoarseElevation coarse;
        bool coarseTried;
        readonly object coarseLock = new();
        /// <summary>Why the included GLO-90 heights could not be opened, or null.</summary>
        public string CoarseError { get; private set; }

        /// <summary>0053: GLO-90 averaged to a coarser grid, from Earth's height data packs; null without them.
        /// Opened on first use.</summary>
        public CoarseElevation Coarse
        {
            get
            {
                if (Volatile.Read(ref coarseTried)) return coarse;   // (EA-0092 A10) read outside the lock
                lock (coarseLock)
                {
                    if (!coarseTried)
                    {
                        // (the index without a part, a data pack not installed, is no error for TryOpen, but a world saved
                        // with these heights then generates on the built-in ones: its reason is kept as an error's)
                        try
                        {
                            string missing = null;
                            coarse = openSource != null ? CoarseElevation.TryOpen(openSource, out missing) : null;
                            if (coarse == null) CoarseError = missing;
                        }
                        catch (Exception e) { coarse = null; CoarseError = e.Message; }
                        Volatile.Write(ref coarseTried, true);
                    }
                }
                return coarse;
            }
        }

        // ------------------------------------------------------------------------------------------ loading

        /// <summary>Loads the data. <paramref name="open"/> returns a stream for a file name inside earthdata/, or null.</summary>
        public static EarthDataset Load(Func<string, Stream> open, bool loadTerrain)
        {
            var d = new EarthDataset(open);
            using (var s = open("earth.json") ?? throw new FileNotFoundException("earthdata/earth.json"))
            {
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                var t = root.GetProperty("terrain");
                d.TerrainRes = t.GetProperty("cellsPerDegree").GetInt32();
                d.TerrainW = t.GetProperty("width").GetInt32();
                d.TerrainH = t.GetProperty("height").GetInt32();
                d.Tile = t.GetProperty("tile").GetInt32();
                double landQ = t.GetProperty("landQ").GetDouble(), seaQ = t.GetProperty("seaQ").GetDouble();
                d.landLut = new float[8192];
                for (int q = 0; q < d.landLut.Length; q++) d.landLut[q] = (float)(50 * (Math.Exp(q / landQ) - 1));
                d.seaLut = new float[4096];
                for (int q = 0; q < d.seaLut.Length; q++) d.seaLut[q] = (float)(-50 * (Math.Exp(q / seaQ) - 1));
                d.elevationOfCode = new float[TerrainTile.SeaCodes + TerrainTile.LandCodes + 1];
                for (int code = -TerrainTile.SeaCodes; code <= TerrainTile.LandCodes; code++) d.elevationOfCode[code + TerrainTile.SeaCodes] = d.ElevationOf(code);

                var c = root.GetProperty("climate");
                d.ClimateRes = c.GetProperty("cellsPerDegree").GetInt32();
                d.ClimateW = c.GetProperty("width").GetInt32();
                d.ClimateH = c.GetProperty("height").GetInt32();
            }
            d.TilesX = d.TerrainW / d.Tile;
            d.TilesY = d.TerrainH / d.Tile;

            using (var s = open("climate.bin") ?? throw new FileNotFoundException("earthdata/climate.bin")) d.ReadClimate(s);
            for (int v = 1; v <= EarthData.ClimateFixVersion; v++)
            {
                using var cf = open(v == 1 ? "climatefix.bin" : "climatefix" + v + ".bin");
                if (cf != null) d.ReadClimateFix(cf, v);
            }
            // optional: a damaged or newer file (or zip entry, which open() decompresses) leaves vanilla's weather instead of
            // stopping the world from loading
            try
            {
                using var wx = open("weather.bin");
                if (wx != null) d.Weather = WeatherData.Read(wx);
            }
            catch (Exception e)
            {
                d.Weather = null;
                d.WeatherError = "earthdata/weather.bin: " + e.Message;
            }
            using (var wg = open(WeatherGround.FileName))
                if (wg != null) d.weatherGroundFile = ReadAll(wg);
            if (loadTerrain)
            {
                using (var s = open("terrain.bin") ?? throw new FileNotFoundException("earthdata/terrain.bin")) d.ReadTerrainIndex(s);
                d.RiverSources.WantRivers();
                using var isl = open("islands.bin");
                if (isl != null)
                {
                    byte[] islBytes = ReadAll(isl);
                    d.islandsHash = EarthBinary.Sha8(islBytes);
                    d.Islands = IslandData.Read(new MemoryStream(islBytes), d.TerrainW);
                    d.hasIslands = true;
                }
                using var lk = open("lakes.bin");
                if (lk != null) d.TerminalLakes = LakeData.Read(lk, d.TerrainW);
                using var sh = open("shores.bin");
                if (sh != null) d.Shores = ShoreData.Read(sh, d.TerrainW);
                using var ot = open("outlets.bin");
                if (ot != null) d.Outlets = OutletData.Read(ot, d.TerrainW);
                using var co = open("coasts.bin");
                if (co != null) d.Coasts = CoastData.Read(co);
                using var sr = open("sea-referee.bin");
                if (sr != null) d.SeaReferee = SeaRefereeData.Read(sr, d.TerrainW);
                d.RiverSources.ReadOverlays(open);
                using var works = open(Works1318Data.FileName);
                if (works != null) d.Works1318 = Works1318Data.Read(ReadAll(works));
                using var nonPerennial = open(NonPerennialData.FileName);
                if (nonPerennial != null) d.NonPerennial = NonPerennialData.Read(ReadAll(nonPerennial));
                using var shoreMaterial = open(ShoreMaterialData.FileName);
                if (shoreMaterial != null) d.ShoreMaterial = ShoreMaterialData.Read(ReadAll(shoreMaterial));
            }
            return d;
        }

        static byte[] ReadAll(Stream s) => EarthBinary.ReadAll(s);

        /// <summary>climate.bin's planes as stored: yearly mean, coldest and warmest month (°C x 100), precipitation (mm), rainfall
        /// (0..255), summer dryness (x 255; from version 3).</summary>
        static readonly PlaneType[] ClimateTypes = { PlaneType.I16, PlaneType.I16, PlaneType.I16, PlaneType.U16, PlaneType.U8, PlaneType.U8 };

        void ReadClimate(Stream s)
        {
            byte[] file = ReadAll(s);
            EarthBinary.Expect(file, "EMCL", 16, "climate.bin");
            int w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12);
            if (w != ClimateW || h != ClimateH) throw new InvalidDataException("climate.bin: size mismatch");
            int n = w * h;
            int headerVersion = BitConverter.ToInt32(file, 4);
            int version = PlaneCoding.ContentVersion(headerVersion), coding = PlaneCoding.Coding(headerVersion);
            // plain: brotli of the planes; predicted (EA-0077): the same planes, the coldest and warmest month against the mean
            byte[] raw = coding switch
            {
                PlaneCoding.Plain => EarthBinary.Unbrotli(file, 16, file.Length - 16, n * (version >= 3 ? 10 : 9), "climate.bin"),
                PlaneCoding.Predicted => PlaneCoding.Decode(file, 16, file.Length - 16, h, w, version >= 3 ? ClimateTypes : ClimateTypes.AsSpan(0, 5), "climate.bin"),
                _ => throw new InvalidDataException("climate.bin: unknown coding " + coding),
            };
            // the codes as stored (little-endian, as every platform the game runs on); no summer dryness before version 3: 0
            climateBase = EarthBinary.Sha8(raw);
            var c = climate = new ClimateCodes(n);
            Buffer.BlockCopy(raw, 0, c.Mean, 0, 2 * n);
            Buffer.BlockCopy(raw, 2 * n, c.Cold, 0, 2 * n);
            Buffer.BlockCopy(raw, 4 * n, c.Warm, 0, 2 * n);
            Buffer.BlockCopy(raw, 6 * n, c.Precip, 0, 2 * n);
            Buffer.BlockCopy(raw, 8 * n, c.Rain, 0, n);
            if (version >= 3) Buffer.BlockCopy(raw, 9 * n, c.Dry, 0, n);
        }

        void ReadClimateFix(Stream s, int expected)
        {
            byte[] file = ReadAll(s);
            // fixes 1 and 2: all six planes, the cell indices as they are; fix 3 and later (against the fix before): a mask of
            // the planes the file holds, the cell indices as steps from the one before (tools/bake_earth.py write_climate_fix)
            bool layered = expected >= 3;
            int head = layered ? 24 : 20;
            EarthBinary.Expect(file, "EMCF", head, "climatefix.bin");
            // a predicted fix 1 or 2: the header goes on with the Sha8 of the climate.bin planes it is against (L-2)
            if (!layered && PlaneCoding.Coding(BitConverter.ToInt32(file, 4)) == PlaneCoding.Predicted)
            {
                head += 8;
                EarthBinary.Expect(file, "EMCF", head, "climatefix.bin");
                if (BitConverter.ToUInt64(file, 20) != climateBase)
                    throw new InvalidDataException("climatefix.bin (version " + expected + ") holds differences from another climate.bin; bake the fixes again");
            }
            int headerVersion = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12), count = BitConverter.ToInt32(file, 16);
            int version = PlaneCoding.ContentVersion(headerVersion), coding = PlaneCoding.Coding(headerVersion);
            int mask = layered ? BitConverter.ToInt32(file, 20) : 0x3F;
            if (version != expected || w != ClimateW || h != ClimateH || count < 0 || count > w * h || (mask & ~0x3F) != 0
                || coding > (layered ? PlaneCoding.Plain : PlaneCoding.Predicted))
                throw new InvalidDataException("climatefix.bin: layout mismatch");
            int planeBytes = 0;
            for (int p = 0; p < 6; p++) if ((mask >> p & 1) != 0) planeBytes += p < 4 ? 2 : 1;
            byte[] raw = coding == PlaneCoding.Predicted
                ? PredictedFix(file, head, count, w * h)
                : EarthBinary.Unbrotli(file, head, file.Length - head, count * (4 + planeBytes), "climatefix.bin");
            var idx = new int[count];
            Buffer.BlockCopy(raw, 0, idx, 0, 4 * count);
            if (layered)
                for (int k = 1; k < count; k++) idx[k] += idx[k - 1];
            int cells = w * h;
            for (int k = 0; k < count; k++)
                if (idx[k] < 0 || idx[k] >= cells || (layered && k > 0 && idx[k] <= idx[k - 1]))
                    throw new InvalidDataException("climatefix.bin: cell index out of range");
            // the new values as codes, only the planes in the mask
            var fn = fixNew[expected] = new ClimateCodes(count, mask);
            int o = 4 * count;
            for (int p = 0; p < 6; p++)
            {
                if ((mask >> p & 1) == 0) continue;
                int width = p < 4 ? 2 : 1;
                Buffer.BlockCopy(raw, o, p switch { 0 => fn.Mean, 1 => fn.Cold, 2 => fn.Warm, 3 => fn.Precip, 4 => fn.Rain, _ => (Array)fn.Dry }, 0, width * count);
                o += width * count;
            }
            fixIndex[expected] = idx;
            if (HasClimateFixVersion(expected)) ClimateFixAvailable = Math.Max(ClimateFixAvailable, expected);
        }

        /// <summary>
        /// (EA-0077) A predicted fix 1 or 2 in the plain layout (the cell indices, then the six planes' new values): its
        /// header names the climate.bin it is against (the Sha8 of its planes, checked before this), and its brotli holds a
        /// bitmap of the cells (one bit a cell from the north-west, the high bit first) and each plane's new values as
        /// differences from climate.bin's, wrapped to the plane's type (16-bit planes zigzag, low bytes then high).
        /// </summary>
        byte[] PredictedFix(byte[] file, int head, int count, int cells)
        {
            int bitmapBytes = (cells + 7) / 8;
            byte[] block = EarthBinary.Unbrotli(file, head, file.Length - head, bitmapBytes + 10 * count, "climatefix.bin");
            var raw = new byte[14 * count];
            var idx = new int[count];
            int k = 0;
            for (int i = 0; i < cells; i++)
            {
                if ((block[i >> 3] >> (7 - (i & 7)) & 1) == 0) continue;
                if (k == count) throw new InvalidDataException("climatefix.bin: more cells than the header says");
                idx[k++] = i;
            }
            if (k != count) throw new InvalidDataException("climatefix.bin: fewer cells than the header says");
            Buffer.BlockCopy(idx, 0, raw, 0, 4 * count);
            int src = bitmapBytes, dst = 4 * count;
            for (int p = 0; p < 6; p++)
            {
                var type = ClimateTypes[p];
                int width = PlaneCoding.Bytes(type);
                var dstPlane = new Span<byte>(raw, dst, width * count);
                for (int j = 0; j < count; j++)
                {
                    int diff = width == 1 ? block[src + j] : PlaneCoding.UnZigzag(block[src + j] | block[src + count + j] << 8);
                    PlaneCoding.Put(dstPlane, type, j, PlaneCoding.Wrap(climate.Code(p, idx[j]) + diff, type));
                }
                src += width * count;
                dst += width * count;
            }
            return raw;
        }

        void ReadTerrainIndex(Stream s)
        {
            terrainFile = ReadAll(s);
            EarthBinary.Expect(terrainFile, "EMTR", 24, "terrain.bin");
            int version = BitConverter.ToInt32(terrainFile, 4);
            int w = BitConverter.ToInt32(terrainFile, 8), h = BitConverter.ToInt32(terrainFile, 12), t = BitConverter.ToInt32(terrainFile, 16);
            int count = BitConverter.ToInt32(terrainFile, 20);
            if (version != 2 || w != TerrainW || h != TerrainH || t != Tile || count != TilesX * TilesY) throw new InvalidDataException("terrain.bin: layout mismatch");
            // every tile within the file, so a damaged one fails here and not on a worldgen thread mid-chunk
            EarthBinary.TileIndex(terrainFile, 24, count, "terrain.bin", out tileOffset, out tileLength);
        }

        /// <summary>A terrain.bin tile as the file has it (no overlay applied): elevation, water level, flags and ice of
        /// its Tile x Tile cells, row by row.</summary>
        internal TerrainTile DecodeTile(int key)
        {
            int n = Tile * Tile;
            byte[] raw = EarthBinary.Unbrotli(terrainFile, tileOffset[key], tileLength[key], n * 7, "terrain.bin: tile " + key);
            var t = new TerrainTile { ElevCode = new short[n], Level = new short[n], Flags = new byte[n], IceSteps = new short[n] };
            int[] q = new int[n];
            UnPaeth(raw, 0, q);
            for (int i = 0; i < n; i++) t.ElevCode[i] = TerrainTile.Code(q[i]);
            Buffer.BlockCopy(raw, 2 * n, t.Level, 0, 2 * n);
            Buffer.BlockCopy(raw, 4 * n, t.Flags, 0, n);
            UnPaeth(raw, 5 * n, q);
            for (int i = 0; i < n; i++)
            {
                if (q[i] == (short)q[i]) { t.IceSteps[i] = (short)q[i]; continue; }
                // ice no 16-bit count of 4 m steps holds (none in terrain.bin): this tile keeps it as floats
                t.IceWide ??= new float[n];
                t.IceWide[i] = q[i] * 4f;
            }
            if (t.IceWide != null)
                for (int i = 0; i < n; i++) if (q[i] == (short)q[i]) t.IceWide[i] = q[i] * 4f;
            return t;
        }

        /// <summary>Undo the planar prediction (left + up - upleft) used by the baker.</summary>
        void UnPaeth(byte[] raw, int offset, int[] q)
        {
            int size = Tile;
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    int i = r * size + c;
                    int a = c > 0 ? q[i - 1] : 0;
                    int b = r > 0 ? q[i - size] : 0;
                    int ab = c > 0 && r > 0 ? q[i - size - 1] : 0;
                    q[i] = BitConverter.ToInt16(raw, offset + 2 * i) + a + b - ab;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ sources

        static Stream OpenFile(string path) => File.Exists(path) ? File.OpenRead(path) : null;

        /// <summary>An entry of the mod zip as a stream; null when it isn't there and <paramref name="required"/> is false.</summary>
        internal static Stream OpenInZip(string zipPath, string name, bool required = true)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry("earthdata/" + name);
            if (entry == null && !required) return null;
            if (entry == null) throw new FileNotFoundException("earthdata/" + name + " in " + zipPath);
            // exactly the entry's bytes, its buffer visible, so the readers take it without another copy (EarthBinary.ReadAll)
            if (entry.Length > int.MaxValue) throw new InvalidDataException("earthdata/" + name + " is too large");
            var bytes = new byte[entry.Length];
            using (var s = entry.Open()) s.ReadExactly(bytes);
            return new MemoryStream(bytes, 0, bytes.Length, false, true);
        }

        /// <summary>
        /// What <see cref="OpenDataFile"/> can read from a mod (its zip or folder), as text for a cache key: each data file's
        /// name, size and checksum (a zip entry's CRC-32 from the archive's directory, read without unpacking; in a folder, a
        /// development build's, the file's time of last change), sorted by name. The data the store fetches is checked against
        /// hashes compiled into the mod, but a few
        /// files come only from the mod itself (works-1318-2.bin, outlets.bin, reservoirs-v1.json) and the mod's own copy
        /// wins over the store's: a rebuilt zip or a development folder can change them with the same code (adversarial
        /// review of 2.0.0-rc.4).
        /// </summary>
        public static string DataIdentity(string sourcePath)
        {
            var lines = new List<string>();
            if (Directory.Exists(sourcePath))
            {
                string dir = Directory.Exists(Path.Combine(sourcePath, "earthdata")) ? Path.Combine(sourcePath, "earthdata") : sourcePath;
                foreach (var f in new DirectoryInfo(dir).GetFiles())
                    lines.Add(f.Name + " " + f.Length + " " + f.LastWriteTimeUtc.Ticks);
            }
            else if (File.Exists(sourcePath))
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                foreach (var e in zip.Entries)
                    if (e.FullName.StartsWith("earthdata/", StringComparison.Ordinal) && e.FullName.Length > "earthdata/".Length)
                        lines.Add(e.FullName.Substring("earthdata/".Length) + " " + e.Length + " " + e.Crc32.ToString("x8"));
            }
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        /// <summary>A separately owned stream, safe for deferred reads after the original archive is closed.</summary>
        public static Stream OpenDataFile(string sourcePath, string name)
        {
            if (Directory.Exists(sourcePath))
            {
                string dir = Directory.Exists(Path.Combine(sourcePath, "earthdata")) ? Path.Combine(sourcePath, "earthdata") : sourcePath;
                return OpenFile(Path.Combine(dir, name));
            }
            return OpenInZip(sourcePath, name, false);
        }
    }

    /// <summary>A decoded terrain.bin tile: its cells row by row. Elevation and ice as their 16-bit codes (EA-0087, code
    /// review 14 November 2026 ER-13: 7 bytes a cell instead of 11, 73 MB instead of 114 at the 320 tiles a server keeps);
    /// EarthDataset.ElevationOfCode and <see cref="IceAt"/> give the very floats the tile held before.</summary>
    sealed class TerrainTile
    {
        /// <summary>The codes a tile keeps: ElevationOf reads land codes up to 8191 and sea codes down to -4095 (beyond, the
        /// last of its table), so a code clamped to them has the same elevation.</summary>
        public const int LandCodes = 8191, SeaCodes = 4095;
        public static short Code(int v) => (short)Math.Clamp(v, -SeaCodes, LandCodes);

        public short[] ElevCode;
        public short[] Level;
        public byte[] Flags;
        /// <summary>Ice thickness in 4 m steps; <see cref="IceWide"/> instead in a tile whose ice a short can't count.</summary>
        public short[] IceSteps;
        public float[] IceWide;
        /// <summary>The ice thickness (m) of cell i.</summary>
        public float IceAt(int i) => IceWide != null ? IceWide[i] : IceSteps[i] * 4f;
        /// <summary>Nearness to a lake (shores.bin, 255 = fully near), null when the tile has none below 255.</summary>
        public byte[] Near;
    }
}
