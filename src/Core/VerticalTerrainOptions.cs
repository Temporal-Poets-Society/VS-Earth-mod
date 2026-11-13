using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The vertical layout chosen when a world is created (decision 0031). Defaults (user, 27 October): sea level 110 at any
    /// world height, Everest's 8849 m at the second-highest block (510 in a 512-high world: players can stand on it and keep a
    /// layer of snow), and a logarithmic curve with a 1000 m knee. The other presets are linear and logs of 250, 500 and
    /// 2000 m; "legacy" is the layout before 0.17 (vanilla's sea level, ModConfig's LandCurve and TopMargin). Resolved values
    /// are frozen into EarthParams. The Earth tab doesn't offer "legacy" (worldconfig.json): it is test-only, reached by
    /// hand-typed world-config keys (earthVerticalBudget, earthHeightCurve), and kept for the tests and for comparing with the
    /// old layout.
    /// </summary>
    public sealed class VerticalTerrainOptions
    {
        public const string DefaultBudget = "110", DefaultCurve = "log1000";
        public static readonly string[] Budgets = { "110", "128", "160", "legacy" };
        public static readonly string[] Curves = { "log1000", "linear", "log250", "log500", "log2000", "legacy" };

        public string Budget = DefaultBudget, Curve = DefaultCurve, Detail = "normal";
        public bool LocalEcology, PhysicalRock;
        /// <summary>Anything but the pre-0.17 layout: such worlds get climate rules 3 (real rainfall, decision 0009/0031). Every
        /// world made from the menu is; only the test-only legacy profile isn't, so the non-experimental branches where a new
        /// world's rules are chosen (EarthMapSystem.ComputeParams) are test-only too.</summary>
        public bool Experimental => Budget != "legacy" || Curve != "legacy" || Detail != "normal" || LocalEcology || PhysicalRock;
        public int SeaLevel(int height, int vanilla) => Budget == "110" ? Math.Min(110, height / 2) : Budget == "128" ? Math.Min(128, height / 2)
            : Budget == "160" ? Math.Min(160, height / 2) : vanilla;
        public double CurveMetres(double configured) => Curve switch
        {
            "log250" => 250, "log500" => 500, "log1000" => 1000, "log2000" => 2000,
            _ => configured,
        };
        public VerticalScale Build(int height, int sea, double margin, double curve, double seaCurve, double depthFraction)
        {
            var v = VerticalScale.ForWorld(height, sea, margin, CurveMetres(curve), seaCurve, depthFraction);
            // Everest at the second-highest block; the terrain is cut there anyway (EarthWorldGen: map height - 2)
            double top = Curve == "legacy" ? v.TopY : height - 2;
            // Keep the nominal deepest water near the vanilla-256 Earth profile (~47 blocks), independent of tall-world height.
            double depth = Budget == "legacy" ? v.MaxDepth : Math.Min(sea - 12, 47);
            return new VerticalScale(height, sea, top, v.LandH0, depth, v.SeaD0, Curve == "linear");
        }
        public double DetailMultiplier => Detail == "off" ? 0 : Detail == "half" ? .5 : 1;
        public static string Choice(string value, string fallback, params string[] choices)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(choices, value) >= 0 ? value : fallback;
        }
    }
}
