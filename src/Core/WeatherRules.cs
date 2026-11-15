namespace EarthMap.Core
{
    /// <summary>
    /// (ER-10, EA-0084) What a world's weather rules (WeatherSettings.Rules, frozen per world: EarthParams.WeatherModelRules)
    /// turn on, as named facts: the one place that says which version changed what. Worlds made before rules 6 have 5
    /// (EarthParams.WeatherModelRulesOrOld); rules 5 is the weather of the 2.0 release candidates up to rc.9. Every test of the
    /// rules in the weather goes through here, so "older worlds keep their weather bit for bit" is checked by reading this
    /// file (and by WeatherContractTests hash rules5, rules6, rules7).
    /// </summary>
    public readonly struct WeatherRules
    {
        /// <summary>The saved rules number.</summary>
        public readonly int Version;

        WeatherRules(int version) { Version = version; }

        public static WeatherRules For(int version) => new WeatherRules(version);

        // ---------------------------------------------------------------- rules 6 (new worlds from 13 November 2026)

        /// <summary>The cold upwelling seas' low cloud decks in the cloud cover (EA-0023).</summary>
        public bool ColdSeaDecks => Version >= 6;
        /// <summary>Hail from Earth's thunderstorms in place of vanilla's random hail events (EA-0019).</summary>
        public bool Hail => Version >= 6;
        /// <summary>The weather's warm and cold spells and the rain's chill in the live temperature (EA-0025).</summary>
        public bool TemperatureAnomaly => Version >= 6;

        // ---------------------------------------------------------------- rules 7 (new worlds from 14 November 2026)

        /// <summary>The ground under the weather (Core.WeatherGround): the rain rate at the ground's temperature (EA-0021) and
        /// the desert coasts' precipitation as climate fix 4 has it (EA-0060). Such a world needs weatherground.bin.</summary>
        public bool Ground => Version >= GroundFrom;
        /// <summary>More thunder in hot dry months (Core.Thunder.DryFactor, EA-0022).</summary>
        public bool DryThunder => Version >= 7;
        /// <summary>Earth's fog and mist (WeatherModel.FogAt, drawn by the client) in place of the patterns' (EA-0026).</summary>
        public bool Fog => Version >= 7;
        /// <summary>Cloud tiles as thick as Earth's cloud cover (drawn by the client, EA-0027).</summary>
        public bool CloudTiles => Version >= 7;

        // ---------------------------------------------------------------- rules 8 (new worlds from 14 November 2026, later)

        /// <summary>The rain as the airports report it: showers at least WeatherModel.FastShare of the rain field, the
        /// afternoon peak WeatherModel.DiurnalAmplitude, warm months skewed with the millimetres kept (EA-0020).</summary>
        public bool RainEvents => Version >= 8;
        /// <summary>The temperature anomaly the same over a 32-block cell and a ten-minute step, kept per thread (EA-0073).</summary>
        public bool AnomalyCells => Version >= 8;
        /// <summary>Earth's hail hurts as vanilla's large hail does (the precipitation state's particle size and level from the
        /// hail's strength, EA-0093). Rules 6 and 7 type it as hail only, which almost never hurt.</summary>
        public bool HailHurts => Version >= 8;

        // ---------------------------------------------------------------- rules 9 (new worlds from 14 November 2026, later still)

        /// <summary>More thunder over West and Central Africa, where WWLLN sees too few strokes (Thunder.RegionFactor,
        /// EA-0074).</summary>
        public bool AfricaThunder => Version >= 9;

        /// <summary>The first rules that read the ground (the build checks for weatherground.bin before building a model).</summary>
        public const int GroundFrom = 7;

        public override string ToString() => "weather rules " + Version;
    }
}
