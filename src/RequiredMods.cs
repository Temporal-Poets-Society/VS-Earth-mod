using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Func4 = System.Func<double, double, double, bool, System.ValueTuple<double, double>>;
using Func3 = System.Func<double, double, double, System.ValueTuple<double, double>>;

namespace EarthMap
{
    /// <summary>Required provider contracts, checked before an Earth world's size, datum or terrain is changed.</summary>
    public static class RequiredMods
    {
        /// <summary>The Hydrology this Earth needs: modinfo.json's dependency (RequiredModsTests checks they agree). Hydrology
        /// 0.6.2 reads river flow query version 3 (its EarthRiverQueryVersion), RiverQueryMinimum, so every new world gets
        /// the calm rivers (pool layout 11, decision 0046); Earth 2.0 no longer serves the 0.6.1 pairing (the author, 8
        /// November: "old save does not matter with this release"). Earth 2.0.0 needs the released 0.6.2 (its release
        /// candidates before rc.4 let a source spilled into the sea make the sea climb and flood the map, Hydrology decision
        /// 0005; the 2.0.0 release candidates accepted rc.4 and later). VS ranks 0.6.2's release candidates below 0.6.2.</summary>
        public const string HydrologyMinimum = "0.6.2";
        public const int RiverQueryMinimum = 3;

        public static bool NeedsProviders(EarthParams saved, bool isNew, string worldType, string playStyle, bool enabled)
            => saved != null ? saved.Enabled : EarthWorldOptions.CreatesEarth(isNew, worldType, playStyle, enabled);

        /// <summary>The check for the world being started (EarthMapSystem.StartServerSide, before anything of the world is
        /// changed): a refusal stops the server before WorldReady, so no later step checks again.</summary>
        public static void EnsureForWorld(ICoreServerAPI api, bool enabled)
        {
            var save = api.WorldManager.SaveGame;
            if (NeedsProviders(EarthMapSystem.Saved(save), EarthMapSystem.IsNewWorld(save), save.WorldType, save.PlayStyle, enabled)) Ensure(api);
        }

        /// <summary>What keeps Earth from running here, or null. The providers are server-side: a client has none to check.</summary>
        public static string Problem(ICoreAPI api)
        {
            try
            {
                string Version(string id) => api.ModLoader.IsModEnabled(id) ? api.ModLoader.GetMod(id)?.Info?.Version : null;
                bool server = api.Side == EnumAppSide.Server;
                return Problem(server ? Version("hydrology") : null, server ? HydrologySystem(api) : null, server);
            }
            catch (Exception e) { return "required provider inspection failed: " + e.GetBaseException().Message; }
        }

        static object HydrologySystem(ICoreAPI api) => api.ModLoader.GetModSystem("Hydrology.HydrologySystem");

        /// <summary>The river flow query version a Hydrology mod system reads (its EarthRiverQueryVersion), 0 without it.</summary>
        public static int QueryVersion(object hydrology)
        {
            try
            {
                var field = hydrology?.GetType().GetField("EarthRiverQueryVersion", BindingFlags.Public | BindingFlags.Static);
                return field?.GetValue(null) is int value ? value : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>The installed Hydrology's river flow query version, 0 without it.</summary>
        public static int QueryVersion(ICoreAPI api)
        {
            try { return QueryVersion(HydrologySystem(api)); }
            catch (Exception) { return 0; }
        }

        // BCL provider contracts only: Earth keeps no compiled reference to Hydrology's assembly. Sea is optional (decision
        // 0041): EarthWeather binds a compatible installed provider (SeaWindSetter), and otherwise drives vanilla weather.
        public static string Problem(string hydrologyVersion, object hydrology, bool server)
        {
            var problems = new List<string>();
            void Version(string name, string actual, string minimum)
            {
                bool valid = false;
                try
                {
                    // GameVersion's permissive parser accepts unknown words; require a numeric x.y.z core first.
                    string core = actual?.Split('-', '+')[0];
                    valid = System.Version.TryParse(core, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0
                        && GameVersion.IsAtLeastVersion(actual, minimum);
                }
                catch (Exception) { }
                if (!valid) problems.Add(name + " " + minimum + "+ is required (loaded: " + (actual ?? "missing or disabled") + ")");
            }
            if (server)
            {
                Version("Hydrology", hydrologyVersion, HydrologyMinimum);
                if (QueryVersion(hydrology) < RiverQueryMinimum)
                    problems.Add("Hydrology must load HydrologySystem with EarthRiverQueryVersion >= " + RiverQueryMinimum);
            }
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        static MethodInfo Method(object provider, string name, Type result, params Type[] arguments)
            => provider?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m =>
                m.Name == name && !m.ContainsGenericParameters && m.ReturnType == result
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments));

        public static MethodInfo SeaWindSetter(object sea)
            => Method(sea, "SetWindProvider", typeof(bool), typeof(string), typeof(Func4), typeof(Func3));

        public static MethodInfo SeaWindClearer(object sea)
            => Method(sea, "ClearWindProvider", typeof(void), typeof(string));

        public static void Ensure(ICoreAPI api)
        {
            string problem = Problem(api);
            Enforce(problem, api is ICoreServerAPI server ? () => server.Server.ShutDown() : null,
                message => api.Logger.Error(message));
        }

        public static void Enforce(string problem, Action stopServer, Action<string> log)
        {
            if (problem == null) return;
            string message = "Earth cannot load this world: " + problem +
                ". Install and enable a compatible Hydrology version, then restart. Earth terrain generation has not been enabled.";
            log?.Invoke(message);
            // ModLoader swallows ordinary StartServerSide exceptions. Request shutdown as well, rather than letting
            // it continue into vanilla worldgen. The exception also stops the rest of a WorldReady callback.
            stopServer?.Invoke();
            throw new InvalidOperationException(message);
        }
    }
}
