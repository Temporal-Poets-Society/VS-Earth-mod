using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// The baked Earth data (see tools/bake_earth.py), in geographic coordinates:
    ///  - terrain: 2' grid in 6° tiles, decoded on demand and cached: elevation (m, ice surface where there is ice,
    ///    lake beds under lakes), water level (m) of the nearest sea/lake, flags, ice thickness (m)
    ///  - climate: 0.25° grid: sea-level-equivalent temperatures (°C), yearly precipitation (mm), VS rainfall (0..255)
    ///  - seam: longitude of the cut through the Pacific for every 0.1° of latitude
    /// Cell (row, col) of a grid with n cells per degree covers latitude 90 - row/n .. 90 - (row+1)/n and
    /// longitude -180 + col/n .. -180 + (col+1)/n. Thread-safe.
    ///
    /// What callers use: the files as read are an <see cref="EarthDataset"/> (with the river sources,
    /// <see cref="RiverSourceRepository"/>); what the world chose to read of them (the climate fix, CHELSA's grids, the
    /// overlays) is the current <see cref="View"/>, which never changes. The Set* methods publish a new view with one choice
    /// changed (one world per server: they apply to every reader of this data from then on); every sample reads the view
    /// current when it starts. To read one selection across several calls, take <see cref="View"/> once.
    /// </summary>
    public sealed class EarthData
    {
        public const byte FlagOcean = 1, FlagLake = 2, FlagSalt = 4, FlagIce = 8;
        public const short NoLevel = short.MinValue;

        readonly EarthDataset dataset;
        readonly RiverSourceRepository riverSources;
        readonly TerrainOverlayTables overlayTables;
        readonly object selectLock = new object();
        volatile EarthDataView view;
        int maxCachedTiles = 320;

        EarthData(EarthDataset dataset)
        {
            this.dataset = dataset;
            riverSources = dataset.RiverSources;
            overlayTables = new TerrainOverlayTables(dataset);
            view = EarthDataView.Initial(dataset, overlayTables, maxCachedTiles);
        }

        /// <summary>The data files as read.</summary>
        public EarthDataset Dataset => dataset;

        /// <summary>What this world reads of the data now: its choices, terrain and climate, frozen.</summary>
        public EarthDataView View => view;

        /// <summary>Publishes the view with these choices (resolved against the data; nothing when they come out the same).</summary>
        void Select(Func<EarthDataChoice, EarthDataChoice> change)
        {
            lock (selectLock)
            {
                var v = view;
                view = v.With(change(v.Choice), overlayTables, maxCachedTiles);
            }
        }

        // terrain grid
        public int TerrainRes => dataset.TerrainRes;
        public int TerrainW => dataset.TerrainW;
        public int TerrainH => dataset.TerrainH;
        public bool HasTerrain => dataset.HasTerrain;

        /// <summary>Terrain tiles a view keeps decoded (each about 360 KB); past it the least recently used are dropped.</summary>
        public int MaxCachedTiles
        {
            get => maxCachedTiles;
            set { lock (selectLock) { maxCachedTiles = value; view.Terrain.Tiles.MaxCachedTiles = value; } }
        }

        // climate grid
        public int ClimateRes => dataset.ClimateRes;
        public int ClimateW => dataset.ClimateW;
        public int ClimateH => dataset.ClimateH;

        // ------------------------------------------------------------------------------------------ climate choices

        /// <summary>The newest climate fix: 1 = UDel's missing precipitation months filled (no one-row stripes, 0.15.1);
        /// 2 = the complete UDel normals (December over the land, 2026-11-07).</summary>
        public const int ClimateFixVersion = 3;
        /// <summary>The newest climate fix whose file was found (loaded on both sides), 0 for none.</summary>
        public int ClimateFixAvailable => dataset.ClimateFixAvailable;
        /// <summary>Whether a climate fix file was found.</summary>
        public bool HasClimateFix => ClimateFixAvailable > 0;
        /// <summary>Whether the file of this climate fix version was found.</summary>
        public bool HasClimateFixVersion(int version) => dataset.HasClimateFixVersion(version);
        /// <summary>The climate fix version applied to the climate grid, 0 for none.</summary>
        public int ClimateFixApplied => view.Choice.ClimateFix;

        /// <summary>
        /// Worlds with a climate fix (EarthParams.ClimateData, decision 0018) see the climate grid with that fix: 1, UDel's
        /// missing precipitation months filled (no one-row stripes of ocean data across the continents); 2, the complete
        /// UDel normals (December's precipitation over the land too). Older worlds keep the grid they were generated with.
        /// A version whose file is missing gives the newest one below it that is there (ClimateFixApplied says which).
        /// </summary>
        public void SetClimateFix(int version) => Select(c => c with { ClimateFix = version });

        /// <summary>The newest climate fix (on) or none (off): for tests and tools.</summary>
        public void SetClimateFix(bool on) => SetClimateFix(on ? ClimateFixVersion : 0);

        /// <summary>Why temperature.bin could not be read (the 0.25° grid's temperatures stay), or null.</summary>
        public string FineTemperatureError => dataset.FineTemperatureError;
        /// <summary>Whether SampleClimate's temperatures come from temperature.bin (SetFineTemperature).</summary>
        public bool FineTemperatures => view.Choice.FineTemperatures;

        /// <summary>
        /// Worlds with climate rules v2 (decision 0009) take the temperatures from CHELSA on the 2' grid (temperature.bin) instead
        /// of the 0.25° climate grid, where CHELSA has them (all but north of 84°N); precipitation and rainfall stay as they are.
        /// The file is read the first time a world turns this on (worlds before v2 never hold it); a missing or damaged file
        /// leaves the 0.25° grid (FineTemperatureError).
        /// </summary>
        public void SetFineTemperature(bool on) => Select(c => c with { FineTemperatures = on });

        /// <summary>Why rain.bin, summits.bin or glaciers.bin could not be read, or null.</summary>
        public string FineRainError => dataset.FineRainError;
        public string SummitsError => dataset.SummitsError;
        public string GlaciersError => dataset.GlaciersError;
        /// <summary>Whether SampleClimate's rainfall and summer dryness come from CHELSA on the 2' grid (rain.bin, SetFineRain).</summary>
        public bool FineRain => view.Choice.FineRain;
        /// <summary>Whether the 30" summit grid is loaded (SetSummits): <see cref="SummitExcess"/>.</summary>
        public bool HasSummits => view.HasSummits;
        /// <summary>Whether the glacier coverage is loaded (SetGlaciers): <see cref="GlacierCoverage"/>.</summary>
        public bool HasGlaciers => view.HasGlaciers;

        /// <summary>Worlds with FineRain (decision 0031): rainfall and summer dryness from CHELSA's 30" precipitation on the 2' grid
        /// (rain.bin) instead of the 0.25° grid's (UDel, 0.5°), which misses islands and mountain rain (St Helena's misty Peaks).</summary>
        public void SetFineRain(bool on) => Select(c => c with { FineRain = on });

        /// <summary>EarthParams.AridityRules of new worlds: the Penman-Monteith index (aridity.bin v1).</summary>
        public const int PenmanAridityVersion = 1;
        /// <summary>Why aridity.bin could not be read, or null.</summary>
        public string PenmanError => dataset.PenmanError;
        /// <summary>Whether SampleClimate fills <see cref="ClimateSample.PenmanFactor"/> (aridity.bin, SetPenmanAridity).</summary>
        public bool PenmanAridity => view.Choice.PenmanAridity;

        /// <summary>Worlds with EarthParams.AridityRules 1: the aridity index's evapotranspiration is CHELSA's Penman-Monteith
        /// instead of the Holdridge estimate behind the rainfall (aridity.bin, tools/bake_aridity.py). The rainfall itself is
        /// unchanged; <see cref="ClimateSample.Aridity"/> applies the factor.</summary>
        public void SetPenmanAridity(bool on) => Select(c => c with { PenmanAridity = on });

        /// <summary>Worlds with Summits (decision 0031): how far ETOPO 2022's 30" ground stands above a smooth 2' surface, in
        /// mountain tiles (summits.bin v2, tools/bake_summits.py).</summary>
        public void SetSummits(bool on) => Select(c => c with { Summits = on });

        /// <summary>Worlds with Glaciers (decision 0031): RGI 6's glacier coverage on a 30" grid (glaciers.bin).</summary>
        public void SetGlaciers(bool on) => Select(c => c with { Glaciers = on });

        /// <summary>0053: the 30" summit excess (m), Catmull-Rom over 4 x 4 cells, never below 0 (EarthDataView).</summary>
        public double SummitExcessCubic(double lat, double lon) => view.SummitExcessCubic(lat, lon);

        /// <summary>How far (m) ETOPO 2022's 30" dry ground stands above the bilinear surface of its own 2' means, bilinear
        /// (EarthDataView). 0 outside the mountain tiles.</summary>
        public double SummitExcess(double lat, double lon) => view.SummitExcess(lat, lon);

        /// <summary>RGI 6's glacier coverage (0..1) at a place, bilinear between 30" cell centres.</summary>
        public double GlacierCoverage(double lat, double lon) => view.GlacierCoverage(lat, lon);

        public PeakCatalog ReadPeaks() => dataset.ReadPeaks();

        // ------------------------------------------------------------------------------------------ files

        /// <summary>The monthly normals for the weather (weather.bin), or null when the file isn't there or can't be read
        /// (WeatherError). Loaded on both sides.</summary>
        public WeatherData Weather => dataset.Weather;
        /// <summary>Why weather.bin could not be read (Weather is then null and vanilla's weather stays), or null.</summary>
        public string WeatherError => dataset.WeatherError;

        /// <summary>An optional data file by name, from where the rest came (the mod, or the data pack the pack index routes it
        /// to); null when it isn't there. The caller disposes the stream.</summary>
        public Stream OpenOptional(string name) => dataset.OpenOptional(name);

        /// <summary>Why the included GLO-90 heights could not be opened, or null.</summary>
        public string CoarseError => dataset.CoarseError;

        /// <summary>0053 (proposed): GLO-90 averaged to a coarser grid, from Earth's height data packs; null without them.
        /// Opened on first use.</summary>
        public CoarseElevation Coarse => dataset.Coarse;

        // ------------------------------------------------------------------------------------------ rivers

        /// <summary>The large rivers (rivers.bin), or null when the file isn't there or wasn't requested.</summary>
        public List<RiverReach> Rivers => riverSources.Rivers;

        /// <summary>
        /// Drops the river reaches (about 3 M small objects) once the world's river network is built: nothing reads them while
        /// chunks generate (decision 0025). <see cref="RiverReaches()"/> reads them again from the mod for whoever needs them later
        /// (the river audit, another model).
        /// </summary>
        public void ReleaseRivers() => riverSources.ReleaseRivers();

        /// <summary>The rivers.bin reaches: as loaded, or read again from the mod after <see cref="ReleaseRivers"/> (a new list,
        /// equal to the released one); null without the file or when it wasn't requested.</summary>
        public List<RiverReach> RiverReaches() => riverSources.RiverReaches();

        /// <summary>0053 (proposed): the rivers on GLO-90 (tools/RiverSnap): rivers.bin's reaches with their courses snapped to
        /// GLO-90's valley floors and levels from GLO-90, in rivers.bin's format.</summary>
        public const string Glo90RiversFile = RiverSourceRepository.Glo90RiversFile;

        /// <summary>The reaches a world's river courses rules read: rivers.bin (0) or rivers-glo90.bin (1 and later, read anew
        /// each time: only start-up and the river audit need them).</summary>
        public List<RiverReach> RiverReaches(int courses) => riverSources.RiverReaches(courses);

        /// <summary>The river courses rules a new world on GLO-90 gets (RiverSourceRepository.Glo90RiverCourses); a file that
        /// can't be read gives 0 and its reason in Glo90RiversError.</summary>
        public int Glo90RiverCourses => riverSources.Glo90RiverCourses;

        /// <summary>Why rivers-glo90.bin could not be read when Glo90RiverCourses looked (a new world on GLO-90 then gets
        /// courses rules 0, the built-in rivers), or null.</summary>
        public string Glo90RiversError => riverSources.Glo90RiversError;

        /// <summary>The moved reaches of the rivers on GLO-90 (rivers-glo90-snap.bin), or null.</summary>
        public RiverSnapData RiverSnap => riverSources.RiverSnap;
        /// <summary>The side arms on the snapped rivers on GLO-90 (rivers-glo90-arms.bin), or null.</summary>
        public RiverArmsData RiverArms => riverSources.RiverArms;
        /// <summary>0063 (courses rules 4): the reaches moved onto the mask's water with their widths (rivers-glo90-snap-2.bin),
        /// or null.</summary>
        public RiverSnapData RiverSnap2 => riverSources.RiverSnap2;
        /// <summary>0063 (courses rules 4): the side arms on those (rivers-glo90-arms-2.bin), or null.</summary>
        public RiverArmsData RiverArms2 => riverSources.RiverArms2;

        // ------------------------------------------------------------------------------------------ terrain overlays

        /// <summary>The small islands and their grid patches (islands.bin; as the referee left them with SetSeaReferee), or
        /// null when the file isn't there or wasn't requested.</summary>
        public IslandData Islands => view.Terrain.Overlays.Islands;
        /// <summary>The big land's outlines (coasts.bin; as the referee left them with SetSeaReferee), or null when the file
        /// isn't there or wasn't requested.</summary>
        public CoastData Coasts => view.Terrain.Overlays.Coasts;
        /// <summary>The terminal lakes and their grid patches (lakes.bin), or null when the file isn't there or wasn't requested.</summary>
        public LakeData TerminalLakes => dataset.TerminalLakes;
        /// <summary>The land around lakes eased below their level (shores.bin), or null when the file isn't there or wasn't requested.</summary>
        public ShoreData Shores => dataset.Shores;
        /// <summary>The lakes' tongues past their outlet as land (outlets.bin), or null when the file isn't there or wasn't requested.</summary>
        public OutletData Outlets => dataset.Outlets;
        /// <summary>The water mask's referee of the sea (sea-referee.bin), or null when the file isn't there or wasn't requested.</summary>
        public SeaRefereeData SeaReferee => dataset.SeaReferee;
        /// <summary>Decision 0064: the areas where coasts and waters go back to 1318 (works-1318-2.bin, Works1318Data), or null
        /// when the file isn't there or wasn't requested.</summary>
        public LakeOutlineData Works1318 => dataset.Works1318;
        /// <summary>Decision 0069 rule 8: the rivers GIRES has as non-perennial (NonPerennialData), or null.</summary>
        public NonPerennialData NonPerennial => dataset.NonPerennial;
        /// <summary>Decision 0070: OpenStreetMap's beaches by material (ShoreMaterialData), or null.</summary>
        public ShoreMaterialData ShoreMaterial => dataset.ShoreMaterial;

        /// <summary>Whether the terrain grid, the islands and the coasts are read as the referee left them (SetSeaReferee).</summary>
        public bool SeaRefereeOverlay => view.Choice.SeaReferee;
        /// <summary>Whether the terrain grid has the islands.bin patches applied (SetIslandOverlay).</summary>
        public bool IslandOverlay => view.Choice.IslandOverlay;
        /// <summary>Whether the terrain grid has the lakes.bin patches applied (SetLakeOverlay).</summary>
        public bool LakeOverlay => view.Choice.LakeOverlay;
        /// <summary>Whether the terrain grid has the shores.bin patches applied (SetShoreOverlay).</summary>
        public bool ShoreOverlay => view.Choice.ShoreOverlay;
        /// <summary>Whether the reservoirs' lake cells (reservoirs-v1.json) are taken out of the grid (SetReservoirExclusions).</summary>
        public bool ReservoirExclusions => view.Choice.ReservoirExclusions;
        /// <summary>Whether the terrain grid has the outlets.bin patches applied (SetOutletOverlay).</summary>
        public bool OutletOverlay => view.Choice.OutletOverlay;

        /// <summary>SeaRefereeData.FileVersion when this data can be read as the water mask's referee left it: sea-referee.bin
        /// was made from these terrain.bin, islands.bin and coasts.bin, or they already are the referee's. Else 0.</summary>
        public int SeaRefereeAvailable => dataset.RefereeState() > 0 ? SeaRefereeData.FileVersion : 0;

        /// <summary>
        /// Worlds with the sea refereed by the water mask (EarthParams.SeaReferee) read the terrain grid, the islands and the
        /// coast outlines as release/1.0's water atlas left them (sea-referee.bin): glacier fjords GSHHG has as sea are land,
        /// shifted outlines nearby are moved. The grid cells are patched before every other overlay, as a refereed terrain.bin
        /// would be. Nothing changes where the files already have it or sea-referee.bin doesn't fit them (SeaRefereeAvailable).
        /// </summary>
        public void SetSeaReferee(bool on) => Select(c => c with { SeaReferee = on });

        /// <summary>
        /// Worlds with islands (EarthParams.Islands) see the grid without the land of the small islands: shallow reef where
        /// ETOPO had them (and its false islands), the islands themselves come from IslandLayer.
        /// </summary>
        public void SetIslandOverlay(bool on) => Select(c => c with { IslandOverlay = on });

        /// <summary>
        /// Worlds with river ends (EarthParams.RiverEnds) see the grid with the terminal lakes filled (lakes.bin): the revived
        /// Aral Sea, Lop Nur, ... and the natural salt lakes at a filled level.
        /// </summary>
        public void SetLakeOverlay(bool on) => Select(c => c with { LakeOverlay = on });

        /// <summary>
        /// Worlds with eased lake shores (EarthParams.LakeShores) see the grid with the shores.bin patches: land around a lake
        /// lower than the lake slopes down to the real ground instead of standing flat at the lake's level.
        /// </summary>
        public void SetShoreOverlay(bool on) => Select(c => c with { ShoreOverlay = on });

        /// <summary>Worlds with reservoir rules (EarthParams.ReservoirRules) see the reservoirs' lake cells (reservoirs-v1.json)
        /// as dry ground without a water level. Throws when the file is missing or damaged.</summary>
        public void SetReservoirExclusions(bool on) => Select(c => c with { ReservoirExclusions = on });

        /// <summary>
        /// Worlds with lake outlets (EarthParams.LakeOutlets) see the grid with the outlets.bin patches: the lakes' tongues past
        /// their outlet down the outflow river (Lake Erie's upper Niagara) are land, and the river runs through them.
        /// </summary>
        public void SetOutletOverlay(bool on) => Select(c => c with { OutletOverlay = on });

        /// <summary>Whether a grid cell is land, or dry land's ring, only because of the outlets.bin overlay, with the overlay
        /// on: river points there are no longer held at the lake's level.</summary>
        public bool IsOutletLandCell(int row, int col) => view.Terrain.IsOutletLandCell(row, col);

        /// <summary>Whether a grid cell is lake only because of the lakes.bin overlay (a terminal lake), with the overlay on.</summary>
        public bool IsTerminalLakeCell(int row, int col) => view.Terrain.IsTerminalLakeCell(row, col);

        /// <summary>
        /// 0053 item 17: the highest level (m) of the grid's lakes that the GLO-90 lake outlines leave to the grid, Earth's
        /// salt lakes and the terminal lakes it fills (lake cells flagged salt, or lake only through lakes.bin), within 3
        /// cells of the 2×2 cells around a point (their rings' reach); NaN where there is none. Kept per cell.
        /// </summary>
        public double KeptLakeLevelNear(double lat, double lon) => view.Terrain.KeptLakeLevelNear(lat, lon);

        /// <summary>0..1, bilinear between cell centres: how green the land is here for a terminal lake's shore; 0 without the
        /// lakes overlay.</summary>
        public double TerminalShoreWeight(double lat, double lon) => view.Terrain.TerminalShoreWeight(lat, lon);

        // ------------------------------------------------------------------------------------------ cells and sampling

        public struct Cell
        {
            public float Elev;
            public short Level;
            public byte Flags;
            public float Ice;
            /// <summary>Nearness to a lake, 0..255 (shores.bin): 0 for a cell without a water level (outside every ring), 255 for
            /// the other cells shores.bin doesn't list and everywhere without the shores overlay.</summary>
            public byte Near;
        }

        /// <summary>A terrain cell. Rows are clamped at the poles, columns wrap around.</summary>
        public Cell GetCell(int row, int col) => view.Terrain.GetCell(row, col);

        public float GetElev(int row, int col) => view.Terrain.GetElev(row, col);

        public struct TerrainSample
        {
            /// <summary>Elevation (m), bicubic. The ice surface on ice sheets, the lake bed under lakes.</summary>
            public double Elev;
            /// <summary>0..1: how much of the surrounding 2x2 cells is sea / lake.</summary>
            public double Ocean, Lake;
            /// <summary>Level (m) of the lake among the surrounding 2x2 cells, NaN if there is none.</summary>
            public double LakeLevel;
            public bool SaltLake;
            /// <summary>Highest water level (m) of the sea (0) or lakes near these cells (within 3 cells), NaN if none: dry land never lies below it.</summary>
            public double ShoreLevel;
            /// <summary>Ice thickness (m), bilinear.</summary>
            public double Ice;
            /// <summary>0..1, bilinear: how near the lake these cells are, with the shores overlay (1 at the cells touching a lake,
            /// fading to 0 where its 3-cell ring ends); counting only the cells at the level held here (ShoreLevel); 0 without water nearby.</summary>
            public double LakeNear;
            /// <summary>Local relief (m): elevation range of the surrounding 4x4 cells.</summary>
            public double Relief;
            /// <summary>0053: the same relief with smooth (C1) weights, so whatever it sizes has no kinks along the cell lines.</summary>
            public double ReliefSmooth;
            /// <summary>0..1: how much of the surrounding 2x2 cells has the sea within 3 cells (water level 0).</summary>
            public double SeaNear;
            /// <summary>With coast references, where SeaNear > 0: the bicubic elevation (m) of the land alone (the sea cells
            /// among the 4x4 take the mean of the land cells around, at least 2 m) and of the sea alone (the land cells
            /// take the sea's mean, at most -5 m). NaN otherwise.</summary>
            public double LandElev, SeaElev;
        }

        public TerrainSample SampleTerrain(double lat, double lon) => view.Terrain.SampleTerrain(lat, lon, false);

        /// <summary>The terrain at a point; <paramref name="coastRefs"/> also gives LandElev and SeaElev near the sea (CoastLayer).</summary>
        public TerrainSample SampleTerrain(double lat, double lon, bool coastRefs) => view.Terrain.SampleTerrain(lat, lon, coastRefs);

        /// <summary>LandElev and SeaElev for a sample taken without them (or where SeaNear was 0).</summary>
        public void AddCoastRefs(double lat, double lon, ref TerrainSample s) => view.Terrain.AddCoastRefs(lat, lon, ref s);

        /// <summary>Elevation (m), bilinear and without the 4x4 neighbourhood (cheaper; for climate and previews).</summary>
        public double ElevBilinear(double lat, double lon) => view.Terrain.ElevBilinear(lat, lon);

        public Cell NearestCell(double lat, double lon) => view.Terrain.NearestCell(lat, lon);

        public struct ClimateSample
        {
            /// <summary>Yearly mean, coldest and warmest monthly mean (°C), reduced to sea level (6.5 °C/km).</summary>
            public double TMean, TCold, TWarm;
            /// <summary>Yearly precipitation (mm).</summary>
            public double Precip;
            /// <summary>Vintage Story rainfall 0..255 from the aridity (precipitation / potential evaporation).</summary>
            public double Rain;
            /// <summary>0..1: how dry the summers are for a climate that isn't a desert (1 = Mediterranean).</summary>
            public double DrySummer;
            /// <summary>Worlds with EarthParams.AridityRules 1 (EarthData.SetPenmanAridity): the factor that turns the index
            /// behind <see cref="Rain"/> (Holdridge's evapotranspiration) into CHELSA's Penman-Monteith index. 0 in other
            /// worlds, read as 1.</summary>
            public double PenmanFactor;

            /// <summary>The aridity index (precipitation / potential evapotranspiration) the world's rules read here.</summary>
            public double Aridity => AridityOf(Rain);

            /// <summary>The aridity index of another rainfall at this place (a river's green belt, TerrainModel.RiparianRain).</summary>
            public double AridityOf(double rain) => SoilFertility.Aridity(rain, DrySummer) * (PenmanFactor > 0 ? PenmanFactor : 1);
        }

        public ClimateSample SampleClimate(double lat, double lon) => view.Climate.SampleClimate(lat, lon);

        // ------------------------------------------------------------------------------------------ loading

        /// <summary>Loads the data. <paramref name="open"/> returns a stream for a file name inside earthdata/, or null.</summary>
        public static EarthData Load(Func<string, Stream> open, bool loadTerrain) => new EarthData(EarthDataset.Load(open, loadTerrain));

        /// <summary>Loads from a folder that holds earthdata/ (or is it).</summary>
        public static EarthData LoadFromFolder(string folder, bool loadTerrain)
        {
            return Load(name => OpenDataFile(folder, name), loadTerrain);
        }

        /// <summary>Loads from a mod zip that holds earthdata/.</summary>
        public static EarthData LoadFromZip(string zipPath, bool loadTerrain)
        {
            return Load(name => EarthDataset.OpenInZip(zipPath, name, false), loadTerrain);
        }

        /// <summary>A separately owned stream, safe for deferred reads after the original archive is closed.</summary>
        public static Stream OpenDataFile(string sourcePath, string name) => EarthDataset.OpenDataFile(sourcePath, name);
    }

    public static class Geo
    {
        /// <summary>Metres per degree of latitude (and of longitude at the equator) as Earth's layout and terrain measure
        /// it. (CoarseElevation.MetresPerDegree is the mean sphere's, 111,195 m, which its grid was made with.)</summary>
        public const double MetresPerDegree = 111320;

        public static double Wrap180(double lon)
        {
            lon %= 360;
            if (lon >= 180) lon -= 360;
            if (lon < -180) lon += 360;
            return lon;
        }

        public static double SmoothStep(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }
    }
}
