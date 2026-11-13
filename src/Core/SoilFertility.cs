using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0048: the soil fertility vanilla's block layers see in Earth worlds. Vanilla derives fertility from its
    /// rainfall and temperature (Climate.GetFertility), tuned to its own climate maps. On Earth's real rainfall (a log curve
    /// of the aridity index, tools/bake_rain.py) it falls below vanilla's soil threshold (0.25) across semi-arid and
    /// summer-dry land worldwide, and the layers lay sand (8 °C and warmer) or gravel there: the Mediterranean, the steppes,
    /// the dry savannas, the Great Plains' west. Land that is not arid by the real aridity index (precipitation / potential
    /// evapotranspiration of at least 0.2, UNEP's arid/semi-arid boundary) has soil: there the layers see at least
    /// <see cref="Floor"/>, vanilla's fertility for low topsoil over low subsoil. Arid land keeps vanilla's sand and gravel;
    /// dunes, beaches, dry river beds and mountain cover stay with the systems that already place them.
    /// Rules 2 (8 November 2026) bring the same line to ground-column worlds, which had put topsoil on all dry land, the
    /// Sahara's included: arid land there has none either, and its cover's own top shows (sand sheets, alluvial silt, fan
    /// gravel, salt crust, weathered rock). The rivers' green belts (TerrainModel.RiparianRain, the same raised rainfall
    /// vanilla's fertility sees in the climate map) keep their soil; semi-arid land keeps it as before (<see cref="AridGround"/>).
    /// Worlds with EarthParams.AridityRules 1 read the index with CHELSA's Penman-Monteith evapotranspiration (the factor
    /// EarthData.ClimateSample.PenmanFactor), the evapotranspiration UNEP's classes are drawn on.
    /// Rules 3 (8 November 2026) give the arid floodplains their soil back: arid land in the natural flood of a river that
    /// keeps water through the dry season (FloodplainGrid, at or below the flood's level) keeps its topsoil, so the Nile
    /// valley, Mesopotamia, the Indus and the Amu Darya's oases are not silt beyond a kilometre-wide river belt; dunes and
    /// sand sheets in the flood stay sand (GroundColumns.WindSand).
    /// </summary>
    public static class SoilFertility
    {
        public const int RulesVersion = 3;
        /// <summary>The rules from which ground-column worlds leave arid land without topsoil (<see cref="AridGround"/>).</summary>
        public const int GroundRules = 2;
        /// <summary>The rules from which arid land in a river's natural flood (FloodplainGrid) keeps its topsoil.</summary>
        public const int FloodplainRules = 3;
        /// <summary>UNEP aridity index boundary between arid and semi-arid land.</summary>
        public const double DryLandAridity = .2;
        /// <summary>0.3 x 255, rounded up: blocklayers.json's minimum for "soil-low-none" subsoil (topsoil needs 0.25).</summary>
        public const int Floor = 77;
        /// <summary>Vanilla's coldest yearly mean for grassed topsoil (blocklayers.json, "soil-low-verysparse").</summary>
        public const double MinTemperature = -10;

        // tools/bake_rain.py: rain = interp(log ai, log XS, YS), then rain *= 1 - 0.25 dry (dry = DrySummer)
        static readonly double[] Ai = { .01, .05, .2, .5, .75, 1, 1.5, 2, 3, 5 };
        static readonly double[] Rain = { 0, 15, 50, 95, 115, 130, 150, 170, 200, 235 };

        /// <summary>
        /// The aridity index behind Earth's ambient rainfall (EarthData.ClimateSample.Rain and DrySummer). The humid tropics'
        /// +60 only applies above an index of 0.6 and only overstates it there.
        /// </summary>
        public static double Aridity(double rain, double drySummer)
        {
            double r = rain / Math.Max(.75, 1 - .25 * Math.Clamp(drySummer, 0, 1));
            if (!(r > 0)) return 0;
            if (r >= Rain[^1]) return Ai[^1];
            int i = 1;
            while (r > Rain[i]) i++;
            double t = (r - Rain[i - 1]) / (Rain[i] - Rain[i - 1]);
            return Math.Exp(Math.Log(Ai[i - 1]) + t * (Math.Log(Ai[i]) - Math.Log(Ai[i - 1])));
        }

        /// <summary>Earth's ambient rainfall for an aridity index without summer drought (the inverse of <see cref="Aridity"/>
        /// at drySummer 0).</summary>
        public static double RainFromAridity(double ai)
        {
            if (!(ai > Ai[0])) return Rain[0];
            if (ai >= Ai[^1]) return Rain[^1];
            int i = 1;
            while (ai > Ai[i]) i++;
            double t = (Math.Log(ai) - Math.Log(Ai[i - 1])) / (Math.Log(Ai[i]) - Math.Log(Ai[i - 1]));
            return Rain[i - 1] + t * (Rain[i] - Rain[i - 1]);
        }

        /// <summary>Whether the land carries soil: not arid, and not colder than vanilla's grassed topsoil. penmanFactor:
        /// EarthData.ClimateSample.PenmanFactor (worlds with AridityRules 1; 0 or 1 elsewhere).</summary>
        public static bool HasSoil(double rain, double drySummer, double temperature, double penmanFactor = 1)
            => temperature >= MinTemperature && Aridity(rain, drySummer) * Factor(penmanFactor) >= DryLandAridity;

        static double Factor(double penmanFactor) => penmanFactor > 0 ? penmanFactor : 1;

        /// <summary>
        /// Rules 2, ground columns: a real desert, without topsoil, is land arid by the aridity index (below
        /// <see cref="DryLandAridity"/>) on its ambient rainfall and still arid on the rainfall with a river's green belt
        /// counted in (TerrainModel.RiparianRain; callers ask it only when the ambient rainfall is arid, it is the costly
        /// part). No temperature test: cold land (larch taiga on permafrost) is not desert.
        /// </summary>
        public static bool AridGround(double rain, double drySummer, double penmanFactor = 1)
            => Aridity(rain, drySummer) * Factor(penmanFactor) < DryLandAridity;

        /// <summary>The fertility (0..255) the block layers see, from vanilla's own.</summary>
        public static int Apply(int vanilla, double rain, double drySummer, double temperature, double penmanFactor = 1)
            => HasSoil(rain, drySummer, temperature, penmanFactor) ? Math.Max(vanilla, Floor) : vanilla;
    }
}
