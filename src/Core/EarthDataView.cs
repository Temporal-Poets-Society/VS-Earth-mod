using System;

namespace EarthMap.Core
{
    /// <summary>
    /// What a world reads of Earth's data (EarthParams' rules; EarthData's Set* methods): the climate fix, CHELSA's
    /// temperatures and rainfall, the Penman aridity, the 30" summits and glaciers, and the terrain grid's overlays. As a
    /// view holds it, it is resolved against the data: a choice whose file is missing is off (Summits and Glaciers keep the
    /// world's choice, their grids say whether there is data), the climate fix is the newest one there not above the one asked
    /// for.
    /// </summary>
    public readonly record struct EarthDataChoice(int ClimateFix, bool FineTemperatures, bool FineRain, bool PenmanAridity,
        bool Summits, bool Glaciers, bool SeaReferee, bool IslandOverlay, bool LakeOverlay, bool ShoreOverlay,
        bool ReservoirExclusions, bool OutletOverlay, bool Paleo1318 = false, int TerminalLakeFile = 0)
    {
        /// <summary>Whether two choices read the terrain grid with the same overlays.</summary>
        public bool SameTerrain(in EarthDataChoice o) => SeaReferee == o.SeaReferee && IslandOverlay == o.IslandOverlay && LakeOverlay == o.LakeOverlay
            && TerminalLakeFile == o.TerminalLakeFile
            && ShoreOverlay == o.ShoreOverlay && ReservoirExclusions == o.ReservoirExclusions && OutletOverlay == o.OutletOverlay;

        /// <summary>Whether two choices read the same climate.</summary>
        public bool SameClimate(in EarthDataChoice o) => ClimateFix == o.ClimateFix && FineTemperatures == o.FineTemperatures && FineRain == o.FineRain
            && PenmanAridity == o.PenmanAridity && Paleo1318 == o.Paleo1318;
    }

    /// <summary>
    /// One world's selection of Earth's data, frozen: its resolved choices (<see cref="EarthDataChoice"/>), the terrain grid
    /// with its overlays (<see cref="TerrainSampler"/>, with its own tile cache) and its climate (<see cref="ClimateSampler"/>),
    /// and the summit and glacier grids. A view never changes: EarthData publishes a new one when a choice changes, and a
    /// sample read from one view never mixes two selections. A new view keeps the samplers of the one before where their
    /// choices are the same, so a climate choice doesn't drop the decoded terrain tiles. Thread-safe.
    /// </summary>
    public sealed class EarthDataView
    {
        public EarthDataset Dataset { get; }
        public EarthDataChoice Choice { get; }
        public TerrainSampler Terrain { get; }
        public ClimateSampler Climate { get; }
        // null where the world doesn't read them or the file isn't there
        readonly TiledGrid summits, glaciers;

        EarthDataView(EarthDataset data, EarthDataChoice choice, TerrainSampler terrain, ClimateSampler climate, TiledGrid summits, TiledGrid glaciers)
        {
            Dataset = data;
            Choice = choice;
            Terrain = terrain;
            Climate = climate;
            this.summits = summits;
            this.glaciers = glaciers;
        }

        /// <summary>The data as its files have it: no climate fix, no fine grids, no overlays.</summary>
        internal static EarthDataView Initial(EarthDataset data, TerrainOverlayTables tables, int maxCachedTiles)
        {
            var none = default(EarthDataChoice);
            return new EarthDataView(data, none, new TerrainSampler(data, new TerrainOverlays(data, none, tables), maxCachedTiles),
                new ClimateSampler(data, data.ClimatePlanes(0), null, null, null), null, null);
        }

        /// <summary>
        /// This view with other choices: each resolved against the data (a file a choice turns on is read the first time, see
        /// EarthDataset), and this view itself when the resolved choices are the same. Called under EarthData's selection lock.
        /// </summary>
        internal EarthDataView With(EarthDataChoice requested, TerrainOverlayTables tables, int maxCachedTiles)
        {
            var d = Dataset;
            int fix = Math.Clamp(requested.ClimateFix, 0, EarthData.ClimateFixVersion);
            while (fix > 0 && !d.HasClimateFixVersion(fix)) fix--;
            // (decision 0087) a world as around 1318 reads the CHELSA temperatures moved to 1318 (temperature-1318.bin)
            var fine1318 = requested.FineTemperatures && requested.Paleo1318 ? d.FineTemperatures1318() : null;
            var fine = !requested.FineTemperatures ? null : fine1318 ?? d.FineTemperatures();
            var rain = requested.FineRain ? d.FineRain() : null;
            var penman = requested.PenmanAridity ? d.PenmanAridity() : null;
            var summitGrid = requested.Summits ? d.Summits() : null;
            var glacierGrid = requested.Glaciers ? d.Glaciers() : null;
            var c = requested with
            {
                ClimateFix = fix,
                FineTemperatures = fine != null,
                FineRain = rain != null,
                PenmanAridity = penman != null,
                Paleo1318 = fine1318 != null,
                // each overlay only where its file is there (the referee only where it fits the files)
                SeaReferee = requested.SeaReferee && d.RefereeState() == 1,
                IslandOverlay = requested.IslandOverlay && d.HasIslands,
                LakeOverlay = requested.LakeOverlay && d.TerminalLakesOf(requested.TerminalLakeFile) != null,
                TerminalLakeFile = requested.LakeOverlay && d.TerminalLakesOf(requested.TerminalLakeFile) != null ? requested.TerminalLakeFile : 0,
                ShoreOverlay = requested.ShoreOverlay && d.Shores != null,
                OutletOverlay = requested.OutletOverlay && d.Outlets != null,
            };
            if (c == Choice) return this;
            var terrain = c.SameTerrain(Choice) ? Terrain : new TerrainSampler(d, new TerrainOverlays(d, c, tables), maxCachedTiles);
            var climate = c.SameClimate(Choice) ? Climate : new ClimateSampler(d, d.ClimatePlanes(c.ClimateFix), fine, rain, penman);
            return new EarthDataView(d, c, terrain, climate, summitGrid, glacierGrid);
        }

        /// <summary>Whether the 30" summit grid is read (EarthData.SetSummits).</summary>
        public bool HasSummits => summits != null;
        /// <summary>Whether the glacier coverage is read (EarthData.SetGlaciers).</summary>
        public bool HasGlaciers => glaciers != null;

        /// <summary>
        /// 0053: the 30" summit excess (m) with Catmull-Rom interpolation over 4 x 4 cells, so it has no kinks along the
        /// cells (bilinear showed as squares about 76 blocks wide at 1:12). Never below 0.
        /// </summary>
        public double SummitExcessCubic(double lat, double lon)
        {
            var g = summits;
            return g == null ? 0 : Math.Max(0, 8 * g.CatmullRom(lat, lon));
        }

        /// <summary>How far (m) ETOPO 2022's 30" dry ground stands above the bilinear surface of its own 2' means, bilinear between
        /// 30" cell centres (8 m steps): the ridges and peaks the 2' grid averages away. 0 outside the mountain tiles.</summary>
        public double SummitExcess(double lat, double lon)
        {
            var g = summits;
            return g == null ? 0 : 8 * g.Bilinear(lat, lon);
        }

        /// <summary>RGI 6's glacier coverage (0..1) at a place, bilinear between 30" cell centres.</summary>
        public double GlacierCoverage(double lat, double lon)
        {
            var g = glaciers;
            return g == null ? 0 : g.Bilinear(lat, lon) / 255;
        }
    }
}
