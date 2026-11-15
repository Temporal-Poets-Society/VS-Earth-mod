using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Func4 = System.Func<double, double, double, bool, System.ValueTuple<double, double>>;
using Func3 = System.Func<double, double, double, System.ValueTuple<double, double>>;

namespace EarthMap
{
    /// <summary>
    /// Rain streaks along the drops' own paths (Sea's until 0.4, which moved it here with the weather): vanilla's particle
    /// shader tilts every rain streak by its angle from straight down about the world's Z axis, so streaks always lean
    /// toward the east; with the wind from any other side they cross the drops' paths. The quad's own y axis is set to
    /// the drop's direction of travel instead. Client only; skipped when an older Sea already did it.
    /// </summary>
    static class RainStreaks
    {
        const string HarmonyId = "earth.rainstreaks";
        const string Pass = "particlesquad";
        const string Marker = "earthRainStreak";
        const string SeaMarker = "seaRainStreak";
        const string Vanilla = "mvmat = mvmat * rotationZ(zangle);";
        const string Replacement = "mvmat[1] = modelViewMatrix * vec4(-u, 0.0); // " + Marker + ": along the drop's own path";
        static Harmony harmony;
        static ILogger log;
        public static bool Injected;

        public static void Apply(ICoreClientAPI api, ILogger logger)
        {
            if (harmony != null) return;
            log = logger;
            try
            {
                var registry = AccessTools.TypeByName("Vintagestory.Client.NoObf.ShaderRegistry") ?? throw new Exception("shader registry not found");
                harmony = new Harmony(HarmonyId);
                harmony.Patch(AccessTools.Method(registry, "LoadShaderProgram") ?? throw new Exception("LoadShaderProgram not found"),
                    postfix: new HarmonyMethod(typeof(RainStreaks), nameof(LoadShaderProgramPostfix)));
            }
            catch (Exception e)
            {
                logger.Warning("Earth weather: rain streaks keep vanilla's slant (always toward east): {0}", e.Message);
                return;
            }
            // the shaders were loaded before mods started: once the world is up, reload them unless another mod's reload
            // (Sea's, for its water) already brought the change in
            api.Event.LevelFinalize += () => api.Event.RegisterCallback(_ =>
            {
                if (!Injected && EarthWeather.For(EnumAppSide.Client)?.Model != null) api.Shader.ReloadShaders();
            }, 500);
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            Injected = false;
        }

        static void LoadShaderProgramPostfix(object program)
        {
            if (EarthWeather.For(EnumAppSide.Client)?.Model == null) return;
            if (!(program is IShaderProgram p) || p.PassName != Pass || p.VertexShader?.Code == null) return;
            string code = p.VertexShader.Code;
            if (code.Contains(Marker) || code.Contains(SeaMarker)) { Injected = true; return; }
            int i = code.IndexOf(Vanilla, StringComparison.Ordinal);
            if (i < 0)
            {
                log?.Warning("Earth weather: the particle shader has changed; rain streaks keep vanilla's slant (always toward east)");
                return;
            }
            p.VertexShader.Code = code.Remove(i, Vanilla.Length).Insert(i, Replacement);
            Injected = true;
        }
    }
}
