using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's weather as the open sea contracts' <c>weather</c> role (Sea's docs/open-sea-contracts.md, sections 3-5;
    /// Earth decision 0017): one entry other mods find in <c>api.ObjectCache["seacontracts:weather"]</c> on each side,
    /// without referencing Earth's DLL. The entry holds .NET base types only: plain values and BCL delegates.
    ///
    /// Every function is pure and thread-safe and gives the same answer on the server and every client for the same
    /// (revision, position, time): the weather is a function of the world's parameters and the game time (0017), built the
    /// same on every side. Positions are world blocks (1 block = 1 m), +X east, +Z south; vectors point toward where the air
    /// goes; time is total game hours (the calendar), so the past can be asked too; wind is in m/s (Earth's wind unit is
    /// vanilla's, 1.0 = 25 m/s). There is no height: the answers are at sea level. While this side has no weather model
    /// (not built yet, or Earth's weather is off here) every function answers NaN, which the contract counts as no answer.
    ///
    /// Storm kinds (Rough Seas' codes, open question 3 of the contract): an Earth tropical cyclone is a <c>hurricane</c>
    /// whatever its strength now, a mid-latitude low a <c>gale</c>. Earth has no squalls: its showers bring rain but no wind
    /// of their own, so it never answers <c>squall</c>.
    /// </summary>
    public static class WeatherContract
    {
        public const string Key = "seacontracts:weather";
        /// <summary>Earth's mod ID; also the name Earth gives Sea's SetWindProvider, so Sea sees one owner both ways.</summary>
        public const string Owner = "earth";
        public const int Version = 1;
        /// <summary>The world's owner (R2).</summary>
        public const int Priority = 100;
        /// <summary>Metres per second for one wind unit (vanilla's and Earth's).</summary>
        public const double Mps = WeatherModel.MetersPerSecondPerUnit;
        public const string Squall = "squall", Gale = "gale", Hurricane = "hurricane";

        /// <summary>The members the entry has, in the contract's order.</summary>
        public static readonly string[] Members = { "owner", "version", "priority", "revision", "wind", "prevailing", "storm", "rain", "writesVanilla" };

        /// <summary>
        /// The entry: <paramref name="model"/> gives this side's weather now (null: none, every answer NaN);
        /// <paramref name="revision"/> changes whenever the model is built, rebuilt or dropped.
        /// </summary>
        public static Dictionary<string, object> Build(Func<WeatherModel> model, Func<long> revision)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (revision == null) throw new ArgumentNullException(nameof(revision));
            return new Dictionary<string, object>
            {
                ["owner"] = Owner,
                ["version"] = Version,
                ["priority"] = Priority,
                ["revision"] = revision,
                ["wind"] = new Func<double, double, double, bool, (double X, double Z)>((x, z, h, gusts) => Wind(model(), x, z, h, gusts)),
                ["prevailing"] = new Func<double, double, double, (double X, double Z)>((x, z, h) => Prevailing(model(), x, z, h)),
                ["storm"] = new Func<double, double, double, (double Presence, double WindMps, string Kind)>((x, z, h) => Storm(model(), x, z, h)),
                ["rain"] = new Func<double, double, double, (double Cloudness, double Precipitation)>((x, z, h) => Rain(model(), x, z, h)),
                // Earth drives vanilla's wind, rain, clouds and cloud patterns, and its lightning and thunder (EarthThunder, in
                // place of vanilla's thunder events; vanilla's hail stays its own); no other mod may write them. (CR-W9) Constant,
                // as the contract's shape has it, so also true where Earth doesn't write all of that: an older Sea without
                // SetWindProvider keeps its own wind (EarthWeather.ControlsWind false: the wind is Sea's, the rest Earth's),
                // and a client has no model yet until the server's parameters arrive (every answer NaN, nothing written)
                ["writesVanilla"] = true,
            };
        }

        /// <summary>
        /// Whether Earth may put its entry in place of <paramref name="existing"/>, what this side's api.ObjectCache holds under
        /// <see cref="Key"/> now (R2): yes over nothing, an entry that isn't one, or a lower priority (a mod's default, a
        /// fallback; a missing priority counts as 0); no over another world owner (priority 100 or more), whose owner is
        /// <paramref name="holder"/>. Not asked for Earth's own entry.
        /// </summary>
        public static bool MayReplace(object existing, out string holder)
        {
            holder = null;
            if (!(existing is IDictionary<string, object> d)) return true;
            int p = d.TryGetValue("priority", out var pr) && pr is int i ? i : 0;
            if (p < Priority) return true;
            holder = d.TryGetValue("owner", out var w) ? w as string ?? "?" : "?";
            return false;
        }

        /// <summary>The wind toward (X, Z) in m/s; without gusts the steady wind seas grow from.</summary>
        public static (double X, double Z) Wind(WeatherModel m, double x, double z, double hours, bool gusts)
        {
            if (m == null) return (double.NaN, double.NaN);
            var w = m.WindAt(x, z, hours, gusts);
            return (w.X * Mps, w.Z * Mps);
        }

        /// <summary>The month's prevailing wind toward (X, Z) in m/s (what the wind is on average there).</summary>
        public static (double X, double Z) Prevailing(WeatherModel m, double x, double z, double hours)
        {
            if (m == null) return (double.NaN, double.NaN);
            var w = m.Prevailing(x, z, hours);
            return (w.X * Mps, w.Z * Mps);
        }

        /// <summary>
        /// The storms here: <c>Presence</c> 0-1 of all of them together (<see cref="WeatherStorms.Effect.Presence"/>: 1 within a
        /// storm's strongest winds near its peak), <c>WindMps</c> their own wind (the circulation Earth's wind takes over from
        /// the background near them, a third weaker over land, at most 37.5), <c>Kind</c> that of the one whose wind is
        /// strongest here, or null where there is none.
        /// </summary>
        public static (double Presence, double WindMps, string Kind) Storm(WeatherModel m, double x, double z, double hours)
        {
            if (m == null) return (double.NaN, double.NaN, null);
            m.Place(x, z, hours, out double lat, out double lon, out _, out var n);
            var e = m.Storms.Influence(lat, lon, hours);
            if (e.Presence <= 0) return (0, 0, null);
            double wind = Math.Min(e.Wind.Strength * (1 - 0.3 * Math.Clamp(n.Land, 0, 1)), WeatherModel.MaxStrength);
            return (e.Presence, wind * Mps, KindOf(e.Storm ?? e.Present));
        }

        /// <summary>The contract's code for an Earth storm: a tropical cyclone is a hurricane, a mid-latitude low a gale.</summary>
        public static string KindOf(WeatherStorms.Storm s) => s == null ? null : s.Tropical ? Hurricane : Gale;

        /// <summary>
        /// Vanilla's meaning of the rain here: <c>Cloudness</c> is its rain-cloud overlay (<c>ClimateCondition.RainCloudOverlay</c>),
        /// <c>Precipitation</c> its rainfall (<c>ClimateCondition.Rainfall</c>), both from Earth's rain cloudness clamped to 0-1
        /// as vanilla's climate lookup does. It rains where Precipitation is above 0, Cloudness then being above 0.5. An admin's
        /// /weather precipitation is not included (it is not part of the world's weather).
        /// </summary>
        public static (double Cloudness, double Precipitation) Rain(WeatherModel m, double x, double z, double hours)
        {
            if (m == null) return (double.NaN, double.NaN);
            double c = m.Cloudness(x, z, hours);
            return (Math.Clamp(c, 0, 1), Math.Clamp(c - 0.5, 0, 1));
        }

        // ------------------------------------------------------------------------------------------ vanilla's wind (section 5)

        /// <summary>
        /// Vanilla's wind as a vector (contract section 5): X east, Z south, its length the strength in vanilla's unit (here
        /// <paramref name="strength"/>, the wind's own strength scaled for height), its direction the wind's.
        /// </summary>
        public static (double X, double Z) VanillaVector(WeatherWind w, double strength)
        {
            double s = w.Strength;
            if (s < 1e-12) return (strength, 0);
            return (w.X / s * strength, w.Z / s * strength);
        }

        /// <summary>The strength a vanilla reader that takes X as the strength gets instead: the vector's horizontal length.</summary>
        public static float VanillaStrength(float x, float z) => MathF.Sqrt(x * x + z * z);
    }
}
