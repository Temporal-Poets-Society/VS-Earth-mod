using System;
using System.Globalization;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Climate rules 3 (decision 0031's test profiles), client: vanilla's plant tint shader (shaderincludes/colormap.vsh) takes
    /// back its own altitude cooling, as seaLevelDist * 1.5 climate-byte units, to find the sea-level temperature that sets
    /// how strongly the seasons tint. There the reading was cooled by the real lapse (ClimateLapse), so the line takes back
    /// that instead, written for the world's vertical scale when the chunk shaders load; they load before the world's rules
    /// arrive and are loaded again once the level is in. Other worlds keep vanilla's line. Not yet seen in game.
    /// </summary>
    static class SeasonTintAltitude
    {
        const string HarmonyId = "earth.seasontint";
        const string Needle = "x += max(0.0, seaLevelDist * 1.5);";
        /// <summary>The chunk shaders whose vertex code has the line (through colormap.vsh) in 1.22.7.</summary>
        static readonly string[] TintedPasses = { "chunkopaque", "chunktopsoil", "chunktransparent", "chunkliquid" };
        static readonly object gate = new object();
        /// <summary>The loader's postfix is in (it stays for the process; it changes nothing outside worlds with rules 3).</summary>
        static bool patched;
        /// <summary>The scale the loaded chunk shaders were written for (null: vanilla's line).</summary>
        static volatile VerticalScale loaded;
        /// <summary>This session's log (a warning line).</summary>
        static Action<string> warn;
        /// <summary>Whether this session has said that a chunk shader lacks the line.</summary>
        static int warned;

        /// <summary>A new session (a client start): its log, and nothing said yet.</summary>
        internal static void Begin(Action<string> log)
        {
            warn = log;
            Interlocked.Exchange(ref warned, 0);
        }

        public static void Start(ICoreClientAPI api, ILogger log)
        {
            lock (gate)
            {
                Begin(line => log.Warning("{0}", line));
                if (!patched)
                {
                    // PatchVanilla's SeasonTintTable: the shader loader's postfix, checked first (one line naming the member when
                    // a game update changed it, and the tint stays vanilla's: no shader reload either)
                    var pv = PatchVanilla.For(api, api.ModLoader.GetModSystem<EarthVerticalBootstrap>().Mod, new Harmony(HarmonyId));
                    pv.Apply(PatchVanilla.SeasonTintTable());
                    if (!pv.On(PatchVanilla.Feature.SeasonTint)) return;
                    patched = true;
                }
            }
            // EarthMapSystem.OnParams has the rules by now (the server sends them before the level finalizes)
            api.Event.LevelFinalize += () => api.Event.RegisterCallback(_ =>
            {
                if (ClimateLapse.RealRainVertical != loaded && !api.Shader.ReloadShaders())
                    log.Warning("Earth Map: reloading the shaders for the season tint failed; see the client log.");
            }, 500);
        }

        /// <summary>
        /// After vanilla's loader has read a shader program: the plant tint's altitude line written for this world's vertical
        /// scale (climate rules 3), or left as it is. A chunk shader without vanilla's line, in a world that would rewrite it,
        /// means a game update changed that line: said once a session, and the tint keeps vanilla's altitude cooling there.
        /// </summary>
        internal static void Loaded(object program)
        {
            if (program is not IShaderProgram p || p.VertexShader?.Code is not string code) return;
            var v = ClimateLapse.RealRainVertical;
            if (!code.Contains(Needle, StringComparison.Ordinal))
            {
                if (v != null && Array.IndexOf(TintedPasses, p.PassName) >= 0 && Interlocked.Exchange(ref warned, 1) == 0)
                    warn?.Invoke(string.Format(CultureInfo.InvariantCulture, "{0}: the {1} shader no longer has the line \"{2}\" Earth rewrites; the season tint " +
                        "keeps vanilla's altitude cooling in this world (climate rules 3), not the real lapse.", PatchVanilla.Versions, p.PassName, Needle));
                return;
            }
            loaded = v;
            if (v == null) return;
            p.VertexShader.Code = code.Replace(Needle, "x += " + Metres(v) + " * " + N(0.0065 * Climate.TemperatureScaleConversion) + "; // Earth Map: the real lapse");
        }

        /// <summary>GLSL for the real elevation (m) of seaLevelDist blocks above sea level (VerticalScale.Elevation).</summary>
        static string Metres(VerticalScale v)
        {
            const string above = "max(0.0, seaLevelDist)";
            return v.LinearLand ? "(" + above + " * " + N(VerticalScale.Everest / (v.TopY - v.SeaLevel)) + ")"
                : "(" + N(v.LandH0) + " * (exp(" + above + " * " + N(Math.Log(1 + VerticalScale.Everest / v.LandH0) / (v.TopY - v.SeaLevel)) + ") - 1.0))";
        }

        /// <summary>A GLSL float literal.</summary>
        static string N(double x) => x.ToString("0.0###########", CultureInfo.InvariantCulture);
    }
}
