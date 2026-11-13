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
    /// Earth's weather in the game (decision 0017), both sides. The weather itself is Core.WeatherModel, a pure function
    /// of the world's key, layout and game time that the server and every client build from the same EarthParams, so they
    /// agree without anything being sent or saved. This system hands it to the game:
    ///  - rain, snow and the dark rain clouds: vanilla's rain cloudness (WeatherSystemBase.GetRainCloudness, which every
    ///    precipitation, wetness and snow-cover reading goes through) is Earth's;
    ///  - which cloud patterns a 512-block weather region picks follows Earth's cloud cover there (the server picks, vanilla
    ///    sends them to clients);
    ///  - the wind: vanilla's wind lookups (trees, waves, wind chill) get Earth's wind as a vector (X east, Z south, length the
    ///    strength; the open sea contracts' section 5), each weather region's wind (its sound, /weather), the slant of rain
    ///    and snow and the clouds' drift follow Earth's wind; Sea, when installed, gets it as its wind provider
    ///    (SetWindProvider), so ships and waves sail on the same wind;
    ///  - the open sea contracts' weather role (Core.WeatherContract): wind, prevailing wind, storms and rain for any mod, in
    ///    api.ObjectCache["seacontracts:weather"] on each side (decision 0017, "Open sea contracts").
    /// An older Sea that still makes its own wind (no SetWindProvider) keeps the wind; Earth then does rain and clouds only.
    /// </summary>
    public class EarthWeather : ModSystem
    {
        /// <summary>Version of the weather rules: server and clients must run the same to see the same weather.</summary>
        /// <remarks>1: 0.15.0. 2: 0.15.1, the reanalysis' winds and clouds, hashed rain-field keys, one canonical time. 3: thunderstorms
        /// (Core.Thunder; rain under them, EarthThunder's lightning), the rain fields at a fresh lattice phase each period.
        /// 4: wet and dry spells (rain in runs of wet days with dry weeks between, thunderstorms with them where the air is
        /// humid; clouds and pressure follow them). 5: rain hours (it rains for as much of the time as airport reports show:
        /// long light snowfalls in the cold, short heavy showers in the heat; more wet days in winter). 6 (13 November 2026):
        /// a build that also runs rules 6 for the worlds made with them (EarthParams.WeatherModelRules: the cold seas' low
        /// cloud decks, hail from Earth's thunderstorms, the weather in the temperature); worlds made before keep rules 5.
        /// Since 6 this is the handshake (the newest rules both sides can run); a world's own are WeatherModelRules.</remarks>
        public const int RulesVersion = WeatherSettings.Newest;
        const string HarmonyId = "earth.weather";
        /// <summary>The name Earth gives Sea's SetWindProvider: its mod ID, the same as the weather role's owner, so a Sea that
        /// follows the role and Earth's own call name one provider (it was "Earth" before the weather role, 7 November 2026).</summary>
        const string SeaProviderName = WeatherContract.Owner;

        static readonly object sync = new object();
        static Harmony harmony;
        static int users;
        static EarthWeather serverInstance, clientInstance;
        /// <summary>Patches that went in (self-test, /earth weather).</summary>
        public static readonly List<string> Active = new List<string>();
        /// <summary>Whether vanilla's rain is Earth's now: the rain cloudness patch went in. A game update can refuse it on its
        /// own while the other patches go in, so what has to match the rain the game shows (Earth's thunder, the ground
        /// moisture) uses the model only while this is set (CR-W5).</summary>
        public static volatile bool RainPatched;

        ICoreAPI api;
        ICoreClientAPI capi;
        EarthMapSystem earth;
        EarthParams builtFor;
        long tickId;
        volatile WeatherModel model;

        /// <summary>The weather, or null (Earth or its weather off in this world, or not ready yet). Any thread.</summary>
        public WeatherModel Model => model;
        /// <summary>Whether Earth decides the wind (no old Sea making its own).</summary>
        public bool ControlsWind { get; private set; }
        /// <summary>Sea's state as Earth found it: "none", "provider" (Earth's wind is Sea's) or "own wind" (an older Sea).</summary>
        public string SeaLink { get; private set; } = "none";

        public override double ExecuteOrder() => 0.06;

        public static EarthWeather For(EnumAppSide side) => side == EnumAppSide.Server ? serverInstance : clientInstance;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            earth = api.ModLoader.GetModSystem<EarthMapSystem>();
            if (api.Side == EnumAppSide.Server) serverInstance = this; else clientInstance = this;
            tickId = api.Event.RegisterGameTickListener(_ => Watch(), 1000);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            // after vanilla's handler (subscribed in WeatherSystemBase.Start), so ours has the last word
            api.Event.OnGetWindSpeed += OnGetWindSpeed;
            RegisterCommands(api);
            // the weather role, where this save is or will be an Earth world (a vanilla world never takes it from another mod);
            // dropped again at WorldReady if Earth's weather turns out to be off here
            if (MayBeEarthWorld(api)) ClaimRole();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.OnGetWindSpeed += OnGetWindSpeed;
            api.Event.RegisterGameTickListener(_ => UpdateLocalWind(), 100);
            RainStreaks.Apply(api, Mod.Logger);
            // a client can't tell an Earth world before the server's EarthParams arrive: claimed now, its answers NaN until the
            // weather is built, dropped when the server's parameters say Earth's weather is off here
            ClaimRole();
        }

        public override void Dispose()
        {
            if (api != null) api.Event.UnregisterGameTickListener(tickId);
            Release();
            UnlinkSea();
            WithdrawRole();
            if (serverInstance == this) serverInstance = null;
            if (clientInstance == this) { clientInstance = null; LocalWind = null; RainStreaks.Remove(); VanillaWindVector.Remove(capi); }
            base.Dispose();
        }

        /// <summary>Server: whether this save is an Earth world (its saved parameters) or a new one Earth is about to make.</summary>
        bool MayBeEarthWorld(ICoreServerAPI sapi)
        {
            try
            {
                var save = sapi.WorldManager.SaveGame;
                if (save.GetData(EarthMapSystem.SaveKey) != null) return true;
                bool enabled = earth?.Config?.Enabled ?? EarthMapSystem.ConfigEnabled(sapi);
                return EarthWorldOptions.CreatesEarth(EarthMapSystem.IsNewWorld(save), save.WorldType, save.PlayStyle, enabled);
            }
            catch (Exception) { return true; }
        }

        // ------------------------------------------------------------------------------------------ the model

        /// <summary>Builds (or drops) the weather when this side's Earth parameters arrive or change.</summary>
        void Watch()
        {
            RecheckRole();
            var p = earth?.Params;
            if (p == builtFor) return;
            if (p == null) return;
            // rules v2: the server's weather.bin fingerprint must be there and match (a server without the file sends none, and a
            // client with it must not make weather the server doesn't)
            bool sameData = earth.Data?.Weather != null && !string.IsNullOrEmpty(p.WeatherDataSignature) && p.WeatherDataSignature == earth.Data.Weather.Signature;
            if (!p.Enabled || !p.Weather || earth.Data?.Weather == null || p.WeatherRules != RulesVersion || !sameData)
            {
                if (p.Enabled && p.Weather && p.WeatherRules != RulesVersion)
                    Mod.Logger.Warning("Earth weather: the server runs weather rules v{0}, this Earth Map has v{1}; vanilla weather here until both match.", p.WeatherRules, RulesVersion);
                else if (p.Enabled && p.Weather && earth.Data?.Weather == null)
                    Mod.Logger.Warning("Earth weather: earthdata/weather.bin is missing or unreadable; vanilla weather.");
                else if (p.Enabled && p.Weather && !sameData)
                    Mod.Logger.Warning("Earth weather: this side's earthdata/weather.bin differs from the server's; vanilla weather here (reinstall the same Earth Map).");
                builtFor = p;
                bool had = model != null;
                model = null;
                if (had) Interlocked.Increment(ref revision);
                Release();
                UnlinkSea();
                VanillaWindVector.Remove(capi);
                WithdrawRole();
                return;
            }
            var cal = api.World.Calendar;
            var settings = new WeatherSettings
            {
                Pace = p.WeatherPace > 0 ? p.WeatherPace : 1,
                RainFrequency = p.RainFrequency,
                StormFrequency = p.StormFrequency,
                DaysPerYear = cal.DaysPerYear > 0 ? cal.DaysPerYear : 108,
                HoursPerDay = cal.HoursPerDay > 0 ? cal.HoursPerDay : 24,
                Rules = p.WeatherModelRulesOrOld,
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var built = new WeatherModel(earth.Data.Weather, p.BuildGeometry(), settings, p.NoiseSeed);
            // afternoon showers by the local sun when the server's AstraTerra sun follows Earth's meridians (decision 0010); the
            // server decides it (WeatherLocalSun) so every side has the same rain. Set before the model is published, so no
            // thread ever reads it without (the weather role's answers must be the same on every side).
            var es = earth;
            if (p.WeatherLocalSun) built.LocalHourOffset = x => (es.LongitudeAt(x) ?? 0) / 15.0;
            model = built;
            Interlocked.Increment(ref revision);
            builtFor = p;
            Apply();
            LinkSea();
            ClaimRole();
            // Earth writes vanilla's wind as a vector now: hand the vanilla readers that take X as the strength its length (where
            // one can't be, this client gets the wind in vanilla's own form instead, the strength in X: OnGetWindSpeed, CR-W5)
            if (capi != null && ControlsWind) VanillaWindVector.Apply(capi, Mod.Logger);
            // The first one-second watch can run after LevelFinalize's shader callback.
            if (capi != null && !RainStreaks.Injected) capi.Shader.ReloadShaders();
            Mod.Logger.Notification("Earth weather ({0}): rules v{1} (this build v{9}), pace {2}, rain x{3}, storms x{4}; {5} blocks per km; wind {6}; weather role {7}; built in {8} ms",
                api.Side, settings.Rules, settings.Pace, settings.RainFrequency, settings.StormFrequency, built.BlocksPerKm.ToString("0.00", CultureInfo.InvariantCulture),
                ControlsWind ? (SeaLink == "provider" ? "Earth's, also Sea's" : "Earth's") : "an older Sea's own", RoleStatus, sw.ElapsedMilliseconds, RulesVersion);
        }

        // ------------------------------------------------------------------------------------------ the weather role

        long revision;
        Dictionary<string, object> roleEntry;
        bool roleRefusedLogged;
        /// <summary>Earth means to hold the role on this side (claimed, not withdrawn since): what <see cref="RecheckRole"/> keeps.</summary>
        bool roleWanted;
        /// <summary>Earth's entry went in on this side at least once (so a later put-back says so in the log).</summary>
        bool roleHeldOnce;

        /// <summary>Changes whenever this side's weather is built, rebuilt or dropped (the role's <c>revision</c>).</summary>
        public long Revision => Interlocked.Read(ref revision);

        /// <summary>Whether this side's api.ObjectCache holds Earth's weather role now.</summary>
        public bool HoldsRole
        {
            get
            {
                if (api == null || roleEntry == null) return false;
                lock (api.ObjectCache) return api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && ReferenceEquals(o, roleEntry);
            }
        }

        /// <summary>For the log and /ew: "held", "held by &lt;owner&gt;" or "not held".</summary>
        public string RoleStatus
        {
            get
            {
                if (HoldsRole) return "held";
                if (api == null) return "not held";
                lock (api.ObjectCache)
                    return api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && o is IDictionary<string, object> d
                        ? "held by " + (d.TryGetValue("owner", out var w) ? w as string ?? "?" : "?") : "not held";
            }
        }

        /// <summary>
        /// Puts Earth's weather role in this side's api.ObjectCache (main thread), replacing an entry only of a lower priority
        /// (open sea contracts R2). A second world owner (priority 100 or more) keeps it, and Earth says so once; Earth's own
        /// weather still runs (two world owners are a setup error).
        /// </summary>
        void ClaimRole()
        {
            if (api == null) return;
            roleWanted = true;
            roleEntry ??= WeatherContract.Build(() => model, () => Interlocked.Read(ref revision));
            string other = null;
            bool lost = false;
            lock (api.ObjectCache)
            {
                api.ObjectCache.TryGetValue(WeatherContract.Key, out var o);
                if (ReferenceEquals(o, roleEntry)) return;
                if (WeatherContract.MayReplace(o, out other))
                {
                    api.ObjectCache[WeatherContract.Key] = roleEntry;
                    lost = roleHeldOnce;
                    roleHeldOnce = true;
                }
            }
            if (other != null && !roleRefusedLogged)
            {
                roleRefusedLogged = true;
                Mod.Logger.Warning("Earth weather: {0} already holds {1} as a world's owner; Earth doesn't publish its weather there.", other, WeatherContract.Key);
            }
            else if (other == null && (lost || roleRefusedLogged))
            {
                roleRefusedLogged = false;
                Mod.Logger.Notification("Earth weather: {0} was gone or another mod's; Earth publishes its weather there again.", WeatherContract.Key);
            }
        }

        /// <summary>
        /// (R2) A producer re-checks its role whenever another producer's entry may have gone: while Earth wants the role
        /// (claimed and not withdrawn), the one-second watch puts its entry back where it went missing or a lower priority
        /// took its place, and takes it where a world owner that kept Earth out has gone.
        /// </summary>
        void RecheckRole()
        {
            if (roleWanted && !HoldsRole) ClaimRole();
        }

        /// <summary>Takes Earth's weather role out of this side's api.ObjectCache, if it is still Earth's.</summary>
        void WithdrawRole()
        {
            roleWanted = false;
            if (api == null || roleEntry == null) return;
            lock (api.ObjectCache)
                if (api.ObjectCache.TryGetValue(WeatherContract.Key, out var o) && ReferenceEquals(o, roleEntry))
                    api.ObjectCache.Remove(WeatherContract.Key);
        }

        // ------------------------------------------------------------------------------------------ Sea

        object seaSystem;

        /// <summary>Sea, when installed: give it Earth's wind (SetWindProvider), or leave the wind to an older Sea that makes its own.</summary>
        void LinkSea()
        {
            ControlsWind = true;
            SeaLink = "none";
            var sea = api.ModLoader.GetModSystem("Sea.SeaSystem");
            if (sea == null) return;
            var set = RequiredMods.SeaWindSetter(sea);
            if (set == null)
            {
                ControlsWind = false;
                SeaLink = "own wind";
                Mod.Logger.Warning("Earth weather: this Sea makes its own wind (no SetWindProvider; Sea 0.4 takes Earth's). Sea keeps the wind, the clouds' drift and the rain's slant; Earth does the rain and clouds.");
                return;
            }
            var m = Model;
            Func4 windAt = (x, z, h, gusts) =>
            {
                var w = m.WindAt(x, z, h, gusts);
                return (w.X, w.Z);
            };
            Func3 prevailing = (x, z, h) =>
            {
                var w = m.Prevailing(x, z, h);
                return (w.X, w.Z);
            };
            try
            {
                bool ok = set.Invoke(sea, new object[] { SeaProviderName, windAt, prevailing }) is bool b && b;
                if (ok)
                {
                    seaSystem = sea;
                    SeaLink = "provider";
                }
                else Mod.Logger.Warning("Earth weather: Sea did not take Earth's wind.");
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Earth weather: could not give Sea the wind: {0}", e.InnerException?.Message ?? e.Message);
            }
        }

        void UnlinkSea()
        {
            if (seaSystem == null) return;
            try { RequiredMods.SeaWindClearer(seaSystem)?.Invoke(seaSystem, new object[] { SeaProviderName }); }
            catch (Exception e) { Mod.Logger.Warning("Earth weather: could not take the wind back from Sea: {0}", e.InnerException?.Message ?? e.Message); }
            seaSystem = null;
            SeaLink = "none";
        }

        // ------------------------------------------------------------------------------------------ patches

        bool applied;

        void Apply()
        {
            if (applied) return;
            applied = true;
            lock (sync)
            {
                users++;
                if (harmony != null) return;
                harmony = new Harmony(HarmonyId);
                var log = Mod.Logger;
                void Try(string what, Action patch)
                {
                    try { patch(); Active.Add(what); }
                    catch (Exception e) { log.Warning("Earth weather: could not patch ({0}), that part stays vanilla: {1}", what, e.Message); }
                }
                Try("rain and rain clouds", () =>
                {
                    harmony.Patch(AccessTools.Method(typeof(WeatherSystemBase), nameof(WeatherSystemBase.GetRainCloudness))
                        ?? throw new Exception("GetRainCloudness not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RainCloudnessPostfix)));
                    RainPatched = true;
                });
                Try("/weather stoprain's answer", () => harmony.Patch(AccessTools.Method(typeof(WeatherSystemCommands), "RainStopFunc", new[] { typeof(IPlayer), typeof(bool) })
                    ?? throw new Exception("WeatherSystemCommands.RainStopFunc not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(RainStopPrefix))));
                Try("cloud patterns by cloud cover", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.RandomWeatherPattern))
                    ?? throw new Exception("RandomWeatherPattern not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(RandomWeatherPatternPrefix))));
                // (rules 6, EA-0019) hail from Earth's thunderstorms in place of vanilla's random hail events: the hail that
                // hurts (EntityBehaviorHealth reads GetPrecipitationState's type), and on the client the hail drawn and heard
                if (Model?.Settings.Rules >= 6)
                {
                    Try("hail from Earth's thunderstorms", () =>
                    {
                        harmony.Patch(AccessTools.Method(typeof(WeatherSystemBase), nameof(WeatherSystemBase.GetPrecipitationState), new[] { typeof(Vec3d), typeof(double) })
                            ?? throw new Exception("GetPrecipitationState not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(PrecipitationStatePostfix)));
                        harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), "clientUpdate", new[] { typeof(float) })
                            ?? throw new Exception("WeatherSimulationRegion.clientUpdate not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RegionClientUpdatePostfix)));
                    });
                }
                Try("wind sound and weather regions", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.GetWindSpeed))
                    ?? throw new Exception("GetWindSpeed not found"), postfix: new HarmonyMethod(typeof(EarthWeather), nameof(RegionWindPostfix))));
                // (rain, snow and hail blow with the wind without a patch since the wind became a vector, 7 November 2026:
                // vanilla's weather particles take X and Z of GetWindSpeedAt as a velocity; Earth's old transpiler, which turned
                // an X-only strength to Earth's direction, would now turn it twice)
                foreach (var name in new[] { "FluffyClouds.CloudRendererMap", "Vintagestory.GameContent.CloudRenderer" })
                {
                    var t = AccessTools.TypeByName(name);
                    if (t == null) continue;
                    string what = "clouds drifting with the wind (" + name + ")";
                    // (CR-W12) looked up inside a try: an ambiguous match after a game update throws, and this runs in the
                    // one-second watch, a tick listener with no catch of its own
                    MethodInfo tick;
                    try { tick = AccessTools.Method(t, "CloudTick"); }
                    catch (Exception e) { log.Warning("Earth weather: could not patch ({0}), that part stays vanilla: {1}", what, e.Message); continue; }
                    if (PatchedBy(tick, "sea")) continue;
                    Try(what, () =>
                    {
                        Need(t, "windChangeTimer"); Need(t, "targetCloudSpeedX"); Need(t, "targetCloudSpeedZ");
                        cloudFields[t] = (AccessTools.FieldRefAccess<long>(t, "windChangeTimer"), AccessTools.FieldRefAccess<float>(t, "targetCloudSpeedX"),
                            AccessTools.FieldRefAccess<float>(t, "targetCloudSpeedZ"));
                        harmony.Patch(tick ?? throw new Exception("CloudTick not found"), prefix: new HarmonyMethod(typeof(EarthWeather), nameof(CloudTickPrefix)));
                    });
                }
            }
        }

        static FieldInfo Need(Type t, string name) => AccessTools.Field(t, name) ?? throw new Exception("field " + name + " not found");

        static bool PatchedBy(MethodBase m, string owner)
        {
            if (m == null) return false;
            var info = Harmony.GetPatchInfo(m);
            return info != null && info.Owners.Contains(owner);
        }

        void Release()
        {
            if (!applied) return;
            applied = false;
            lock (sync)
            {
                if (--users > 0 || harmony == null) return;
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
                Active.Clear();
                RainPatched = false;
                cloudFields.Clear();
            }
        }

        static WeatherModel ModelFor(ICoreAPI api) => api == null ? null : For(api.Side)?.Model;

        /// <summary>Whether an admin set this side's precipitation with /weather (then the weather's rain and its temperature
        /// effects are vanilla's).</summary>
        internal static bool PrecipitationOverridden(ICoreAPI api)
        {
            var self = api == null ? null : For(api.Side);
            if (self == null) return false;
            var ws = self.vanillaWeather ??= api.ModLoader.GetModSystem<WeatherSystemBase>();
            return ws?.OverridePrecipitation.HasValue == true;
        }

        WeatherSystemBase vanillaWeather;

        /// <summary>Metres above the sea at block height <paramref name="y"/> in this world (Earth's vertical scale), 0 at or
        /// below the sea or without Earth's terrain.</summary>
        internal static double GroundMetres(ICoreAPI api, double y)
        {
            var vertical = api == null ? null : For(api.Side)?.earth?.Model?.Vertical;
            if (vertical == null) return 0;
            double metres = vertical.Elevation(y);
            return double.IsFinite(metres) ? Math.Max(0, metres) : 0;
        }

        /// <summary>A weather hook outside this class failed (logged once a world per hook).</summary>
        internal static void ReportHookFailure(EnumAppSide side, string what, Exception e) => HookFailed(side, what, e);

        /// <summary>The hooks that have failed on this side in this world (logged): an instance's, so that the next world in a
        /// single-player process logs its own failures.</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> failedHooks = new();

        /// <summary>Whether this is the first failure of a hook in this world on this side.</summary>
        internal bool FirstFailure(string what) => failedHooks.TryAdd(what, true);

        /// <summary>A hook failed: that call keeps vanilla's value. Logged once a session per hook: a bug in the weather would
        /// otherwise quietly turn every rain, wind or cloud reading back to vanilla.</summary>
        static void HookFailed(EnumAppSide side, string what, Exception e)
        {
            var self = For(side) ?? serverInstance ?? clientInstance;
            if (self != null && self.FirstFailure(what))
                self.Mod.Logger.Error("Earth weather: {0} failed, vanilla's is used there (further failures are not logged): {1}", what, e);
        }

        /// <summary>Vanilla's rain cloudness is Earth's (unless an admin set the precipitation with /weather). At the calendar's
        /// own time, like the winds, storms and forecasts: vanilla's RainCloudDaysOffset (it only shifts its rain noise) is
        /// left out, so a save with one doesn't get one moment's rain with another's storm winds. A postfix on purpose, not a
        /// prefix: Cylinder World prefixes the same method (SeamRainPatch.Fold, returning false in every Earth-sized world),
        /// and Harmony skips any later prefix that would also replace the result, so a prefix here would never run and the
        /// rain would be vanilla's. A postfix runs after whichever prefix answered and has the last word.</summary>
        static void RainCloudnessPostfix(WeatherSystemBase __instance, double posX, double posZ, double totalDays, ref float __result)
        {
            if (__instance.OverridePrecipitation.HasValue) return;
            var m = ModelFor(__instance.api);
            if (m == null) return;
            try
            {
                __result = (float)m.Cloudness(posX, posZ, totalDays * m.Settings.HoursPerDay);
            }
            catch (Exception e) { HookFailed(__instance.api.Side, "the rain cloudness", e); }
        }

        /// <summary>
        /// Vanilla's /weather stoprain (WeatherSystemCommands.RainStopFunc with skipForward) moves vanilla's rain noise on by
        /// RainCloudDaysOffset and says the rain will stop; Earth's rain leaves that offset out (above), so nothing would
        /// change. While Earth's weather decides the rain the command says so instead and leaves the offset alone (CR-W6).
        /// /whenwillitstopraining (no skipForward) already reads Earth's rain, and an admin's set precipitation keeps
        /// vanilla's answer, so both stay vanilla's.
        /// </summary>
        static bool RainStopPrefix(bool skipForward, ref TextCommandResult __result)
        {
            var self = serverInstance;
            if (!skipForward || !RainPatched || self?.Model == null) return true;
            if (self.api.ModLoader.GetModSystem<WeatherSystemServer>()?.OverridePrecipitation.HasValue == true) return true;
            __result = TextCommandResult.Success("Earth's weather decides the rain in this world and can't be moved on: /weather stoprain changes nothing here. " +
                "/whenwillitstopraining says when the rain here stops; /weather setprecip sets the rain until /weather setprecipa.");
            return false;
        }

        /// <summary>Hail at least this strong (WeatherModel.HailAt) falls as hail.</summary>
        const double HailFrom = 0.25;

        /// <summary>(Rules 6, EA-0019) Hail where Earth's thunderstorms have it and nowhere else: what falls at a place is
        /// hail there, and vanilla's random hail event falls as rain or snow (Auto) instead.</summary>
        static void PrecipitationStatePostfix(WeatherSystemBase __instance, Vec3d pos, double totalDays, PrecipitationState __result)
        {
            if (__result == null || pos == null || __instance.OverridePrecipitation.HasValue || __result.Level <= 0) return;
            var m = ModelFor(__instance.api);
            if (m == null || m.Settings.Rules < 6) return;
            try
            {
                bool hail = m.HailAt(pos.X, pos.Z, totalDays * m.Settings.HoursPerDay, GroundMetres(__instance.api, pos.Y)) >= HailFrom;
                if (hail) __result.Type = EnumPrecipitationType.Hail;
                else if (__result.Type == EnumPrecipitationType.Hail) __result.Type = EnumPrecipitationType.Auto;
            }
            catch (Exception e) { HookFailed(__instance.api.Side, "the hail", e); }
        }

        /// <summary>(Rules 6, EA-0019) The client's weather regions draw and sound hail where Earth's thunderstorm over the
        /// player has it (the regions' snapshots are blended at the player), and vanilla's hail events as rain or snow.</summary>
        static void RegionClientUpdatePostfix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws)
        {
            var api = ___ws?.api as ICoreClientAPI;
            var m = ModelFor(api);
            var wd = __instance.weatherData;
            if (m == null || m.Settings.Rules < 6 || wd == null || ___ws.OverridePrecipitation.HasValue) return;
            var pos = api.World.Player?.Entity?.Pos;
            if (pos == null) return;
            try
            {
                bool hail = m.HailAt(pos.X, pos.Z, api.World.Calendar.TotalHours, GroundMetres(api, pos.Y)) >= HailFrom;
                if (hail) wd.BlendedPrecType = EnumPrecipitationType.Hail;
                else if (wd.BlendedPrecType == EnumPrecipitationType.Hail) wd.BlendedPrecType = EnumPrecipitationType.Auto;
            }
            catch (Exception e) { HookFailed(api.Side, "the hail drawn", e); }
        }

        /// <summary>The cloud codes of vanilla's patterns by how cloudy they are: fair, mixed (neutral) and cloudy.</summary>
        internal static double CoverWeight(string code, double cover)
        {
            code ??= "";
            bool fair = code.StartsWith("clearsky") || code.StartsWith("cirro") || code.StartsWith("alto");
            bool cloudy = code.StartsWith("overcast") || code.StartsWith("stratus") || code.Contains("haze") || code.StartsWith("cumulonimbus");
            if (fair) return 0.1 + 1.9 * Math.Pow(1 - cover, 1.5);
            if (cloudy) return 0.1 + 1.9 * Math.Pow(cover, 1.5);
            return 0.6 + 0.8 * (1 - Math.Abs(cover - 0.5) * 2);
        }

        /// <summary>
        /// A weather region picks its next cloud pattern by vanilla's chances (its rainfall and temperature) times how well
        /// the pattern fits Earth's cloud cover at the region's centre now: clear skies in the desert and the dry season,
        /// overcast where the month is wet and a front is near. Uses the region's own random numbers like vanilla.
        /// </summary>
        static bool RandomWeatherPatternPrefix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws, ref WeatherPattern __result)
        {
            var m = ModelFor(___ws?.api);
            var patterns = __instance.WeatherPatterns;
            if (m == null || patterns == null || patterns.Length == 0 || __instance.Rand == null) return true;
            try
            {
                int rs = ___ws.api.World.BlockAccessor.RegionSize;
                double cover = m.CloudCover((__instance.regionX + 0.5) * rs, (__instance.regionZ + 0.5) * rs, ___ws.api.World.Calendar.TotalHours);
                var cond = __instance.weatherData.climateCond;
                double total = 0;
                var w = new double[patterns.Length];
                for (int i = 0; i < patterns.Length; i++)
                {
                    patterns[i].updateHereChance(cond?.Rainfall ?? 0, cond?.Temperature ?? 15);
                    w[i] = Math.Max(0, patterns[i].hereChance) * CoverWeight(patterns[i].config?.Code, cover);
                    total += w[i];
                }
                if (total <= 0) return true;
                double r = __instance.Rand.NextFloat() * total;
                for (int i = 0; i < patterns.Length; i++)
                {
                    r -= w[i];
                    if (r <= 0) { __result = patterns[i]; return false; }
                }
                __result = patterns[patterns.Length - 1];
                return false;
            }
            catch (Exception e) { HookFailed(___ws.api.Side, "the cloud pattern pick", e); return true; }
        }

        // ------------------------------------------------------------------------------------------ wind

        /// <summary>Vanilla's wind strength by height (WeatherSimulationRegion.GetWindSpeed): stronger up high, weak underground.</summary>
        public static double AltitudeScale(double strength, double y, int seaLevel)
        {
            if (y > seaLevel) return Math.Min(strength * Math.Max(1.0, 0.9 + (y - seaLevel) / 100.0), WeatherModel.MaxStrength);
            return strength / (1.0 + (seaLevel - y) / 4.0);
        }

        /// <summary>
        /// Vanilla's wind lookups (trees and plants swaying, wind chill, the vanilla water's waves, rain, snow and particles):
        /// Earth's wind here as a vector (open sea contracts section 5): X east, Z south, the length its strength scaled for
        /// height in vanilla's unit. Vanilla itself puts the strength in X alone; its readers that take X as the strength get
        /// the length through <see cref="VanillaWindVector"/>. A client where they don't gets vanilla's own form (CR-W5).
        /// </summary>
        void OnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
        {
            var m = Model;
            if (m == null || !ControlsWind || pos == null || api.World?.Calendar == null) return;
            try
            {
                var w = m.WindAt(pos.X, pos.Z, api.World.Calendar.TotalHours, true);
                double strength = AltitudeScale(w.Strength, pos.Y, api.World.SeaLevel);
                // (CR-W5) a client whose X readers were not handed the length (Earth's patch refused after a game update) gets
                // the strength in X, as vanilla writes it: with an easterly as a vector vanilla's water wave intensity
                // (0.75 + 0.9 X) goes negative and its wind wave counters run backwards. Rain then slants east, as in vanilla.
                if (capi != null && !VanillaWindVector.ReadersTakeVector)
                {
                    windSpeed.X = strength;
                    windSpeed.Z = 0;
                    return;
                }
                var v = WeatherContract.VanillaVector(w, strength);
                windSpeed.X = v.X;
                windSpeed.Z = v.Z;
            }
            catch (Exception e) { HookFailed(api.Side, "the wind lookup", e); }
        }

        /// <summary>Each 512-block weather region keeps its own wind, which drives the wind sound and /weather: Earth's at its centre.</summary>
        static void RegionWindPostfix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws, double posY, ref double __result)
        {
            var api = ___ws?.api;
            var sys = api == null ? null : For(api.Side);
            var m = sys?.Model;
            if (m == null || !sys.ControlsWind) return;
            try
            {
                int rs = api.World.BlockAccessor.RegionSize;
                double s = m.WindAt((__instance.regionX + 0.5) * rs, (__instance.regionZ + 0.5) * rs, api.World.Calendar.TotalHours, false).Strength;
                __result = AltitudeScale(s, posY, api.World.SeaLevel);
            }
            catch (Exception e) { HookFailed(api.Side, "the weather region's wind", e); }
        }

        /// <summary>The wind at the local player for the clouds' drift: (toward x, toward z) unit direction, strength at sea
        /// level, strength at the player's height. Null = leave vanilla's.</summary>
        public static volatile float[] LocalWind;
        static long clientMs;

        void UpdateLocalWind()
        {
            clientMs = capi.ElapsedMilliseconds;
            var ep = capi.World?.Player?.Entity;
            var m = Model;
            if (ep == null || m == null || !ControlsWind) { LocalWind = null; return; }
            try
            {
                var w = m.WindAt(ep.Pos.X, ep.Pos.Z, capi.World.Calendar.TotalHours, true);
                double s = w.Strength;
                float dx = s < 1e-6 ? 1 : (float)(w.X / s), dz = s < 1e-6 ? 0 : (float)(w.Z / s);
                LocalWind = new[] { dx, dz, (float)s, (float)AltitudeScale(s, ep.Pos.Y, capi.World.SeaLevel) };
            }
            catch (Exception e) { LocalWind = null; HookFailed(EnumAppSide.Client, "the clouds' drift", e); }
        }

        /// <summary>Each cloud renderer type's timer and target drift, as typed references (the prefix runs every frame:
        /// FieldInfo.SetValue boxed three values a frame).</summary>
        static readonly Dictionary<Type, (AccessTools.FieldRef<object, long> timer, AccessTools.FieldRef<object, float> tx, AccessTools.FieldRef<object, float> tz)> cloudFields = new();

        /// <summary>Vanilla's cloud renderers pick a random eastward drift every 20-120 s and add 3 × the wind strength to X. Keep
        /// their timer from firing and give them Earth's drift instead (they ease toward it themselves).</summary>
        static void CloudTickPrefix(object __instance)
        {
            var d = LocalWind;
            if (d == null || !cloudFields.TryGetValue(__instance.GetType(), out var f)) return;
            float speed = 1f + 6f * d[2];
            f.timer(__instance) = clientMs + 60000;
            f.tx(__instance) = d[0] * speed - 3f * d[3];
            f.tz(__instance) = d[1] * speed;
        }

        // ------------------------------------------------------------------------------------------ commands

        void RegisterCommands(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("earthweather")
                .WithAlias("ew")
                .WithDescription("Earth's weather here: the month's climate, rain, clouds, wind, pressure, storms and thunder; 'forecast' for the next day, 'storms' for the storms now, 'thunder' for the nearest thunderstorm")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .WithArgs(parsers.OptionalWord("what"))
                .HandleWith(CmdWeather);
        }

        TextCommandResult CmdWeather(TextCommandCallingArgs args)
        {
            var m = Model;
            if (m == null) return TextCommandResult.Success("Earth's weather is off in this world (vanilla weather).");
            var pos = args.Caller.Player.Entity.Pos;
            double hours = api.World.Calendar.TotalHours;
            string what = (args[0] as string ?? "").ToLowerInvariant();
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            // (CR-W12) snow or rain by the temperature here at that hour (vanilla's climate, at the player's height, with Earth's
            // hooks), not by the month's sea-level normal, which says rain on a snowy mountain
            var bp = pos.AsBlockPos;
            double TemperatureAt(double h) => api.World.BlockAccessor.GetClimateAt(bp, EnumGetClimateMode.ForSuppliedDateValues, h / api.World.Calendar.HoursPerDay)?.Temperature ?? double.NaN;
            if (what == "storms")
            {
                double wh = hours * m.Settings.Pace;
                var g = m.Geo.ToGeo(pos.X, pos.Z);
                var list = m.Storms.AliveIn(wh).Where(s => s.AliveAt(wh)).Select(s =>
                {
                    s.At(wh, out double la, out double lo, out double inten);
                    double dn = (la - g.Lat) * 111.32, de = Geo.Wrap180(lo - g.Lon) * 111.32 * Math.Cos((la + g.Lat) * Math.PI / 360);
                    return (s, la, lo, inten, km: Math.Sqrt(dn * dn + de * de), bearing: WeatherWind.Bearing(de, -dn));
                }).OrderBy(t => t.km).Take(8).ToList();
                sb.Append($"{m.Storms.AliveIn(wh).Count(s => s.AliveAt(wh))} storms on the Earth now; nearest:");
                foreach (var t in list)
                    sb.Append(string.Format(ci, "\n {0}: {1:0} km {2} ({3}), winds {4:0} m/s", t.s.Name, t.km, WeatherWind.CompassName(t.bearing),
                        EarthMapSystem.FormatLatLon(t.la, t.lo), t.inten * 25));
                return TextCommandResult.Success(sb.ToString());
            }
            if (what == "forecast")
            {
                sb.Append("Next 24 hours here:");
                for (int k = 0; k <= 24; k += 3)
                {
                    var f = m.Sample(pos.X, pos.Z, hours + k);
                    sb.Append(string.Format(ci, "\n +{0,2}h: {1}, clouds {2:0}%, wind {3:0} m/s from {4}, {5:0} hPa", k, Describe(f, TemperatureAt(hours + k)), f.CloudCover * 100,
                        f.Wind.Strength * 25, WeatherWind.CompassName(f.Wind.FromDegrees), f.Pressure));
                }
                return TextCommandResult.Success(sb.ToString());
            }
            if (what == "thunder")
            {
                // the strongest thunder within 3000 blocks, and when it next thunders here (up to four days ahead)
                double best = 0, bx = 0, bz = 0, next = -1;
                for (int ring = 1; ring <= 12; ring++)
                    for (int k = 0; k < ring * 8; k++)
                    {
                        double a = k * 2 * Math.PI / (ring * 8), x = pos.X + ring * 250 * Math.Cos(a), z = pos.Z + ring * 250 * Math.Sin(a);
                        double t = m.ThunderAt(x, z, hours);
                        if (t > best) { best = t; bx = x; bz = z; }
                    }
                for (int k = 0; k <= 4 * 24 * 4 && next < 0; k++)
                    if (m.ThunderAt(pos.X, pos.Z, hours + k / 4.0) > 0) next = k / 4.0;
                double here = m.ThunderAt(pos.X, pos.Z, hours);
                sb.Append(string.Format(ci, "Thunder here: {0}; on {1:0}% of this month's days.", here > 0 ? string.Format(ci, "now ({0:0}%)", here * 100) : "none now",
                    m.ThunderDays(pos.X, pos.Z, hours) * 100));
                if (best > 0)
                {
                    double dx = bx - pos.X, dz = bz - pos.Z;
                    sb.Append(string.Format(ci, "\nThe strongest thunderstorm within 3000 blocks: {0:0} blocks {1} (={2:0} ={3:0}), {4:0}%.", Math.Sqrt(dx * dx + dz * dz),
                        WeatherWind.CompassName(WeatherWind.Bearing(dx, dz)), bx, bz, best * 100));
                }
                else sb.Append("\nNo thunderstorm within 3000 blocks.");
                sb.Append(next > 0 ? string.Format(ci, "\nNext thunder here in {0:0.#} hours.", next) : next == 0 ? "" : "\nNo thunder here in the next four days.");
                return TextCommandResult.Success(sb.ToString());
            }
            var s0 = m.Sample(pos.X, pos.Z, hours);
            var wNow = m.WindAt(pos.X, pos.Z, hours, true);
            sb.Append(string.Format(ci, "{0}, month {1}: normally {2:0.0} mm of rain a day, raining {3:0}% of the time ({4:0}% as showers).",
                EarthMapSystem.FormatLatLon(s0.Lat, s0.Lon), (int)(s0.YearRel * 12) + 1, s0.NormalPrecip, s0.RainChance * 100, s0.Convective * 100));
            sb.Append(string.Format(ci, "\nNow: {0}, clouds {1:0}%, {2:0} hPa.", Describe(s0, TemperatureAt(hours)), s0.CloudCover * 100, s0.Pressure));
            sb.Append(string.Format(ci, "\nThunder on {0:0}% of this month's days; {1}.", m.ThunderDays(pos.X, pos.Z, hours) * 100,
                s0.Thunder > 0 ? string.Format(ci, "a thunderstorm overhead now ({0:0}%), {1} strikes so far", Math.Max(1, s0.Thunder * 100), EarthThunder.For(EnumAppSide.Server)?.Strikes ?? 0) : "none overhead now"));
            // vanilla's own test before lightning sets fire (ModSystemFireFromLightning): the rain of the last ten days here
            var vanillaWeather = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (!api.World.Config.GetBool("lightningFires")) sb.Append(" Lightning sets no fires in this world (world option lightningFires).");
            else if (vanillaWeather != null)
            {
                double wet = vanillaWeather.GetEnvironmentWetness(pos.AsBlockPos, 10);
                sb.Append(wet < 0.01 ? " No rain here for ten days: lightning can set fire to what burns." :
                    string.Format(ci, " Rain here in the last ten days (wetness {0:0.00}; fires need under 0.01): lightning sets no fire.", wet));
            }
            sb.Append(string.Format(ci, "\nWind {0:0.0} m/s from {1} ({2:0}°), gusting; the month's prevailing wind {3:0.0} m/s from {4}.",
                wNow.Strength * 25, WeatherWind.CompassName(wNow.FromDegrees), wNow.FromDegrees, s0.Prevailing.Strength * 25, WeatherWind.CompassName(s0.Prevailing.FromDegrees)));
            if (s0.Storm != null) sb.Append($"\nIn the winds of {s0.Storm.Name}.");
            if (m.Settings.Rules >= 6)
            {
                // (rules 6) the hail and the weather's own warmth or chill at the player's height
                double metres = GroundMetres(api, pos.Y), hail = m.HailAt(pos.X, pos.Z, hours, metres), dt = m.TemperatureAnomaly(pos.X, pos.Z, hours, metres);
                sb.Append(string.Format(ci, "\nHail: {0}. The weather makes it {1:0.0} °C {2} than the season and the hour alone; the month's cloud cover {3:0}%.",
                    hail >= HailFrom ? string.Format(ci, "falling now ({0:0}%)", hail * 100) : s0.Thunder > 0 ? "none from this storm now" : "none",
                    Math.Abs(dt), dt >= 0 ? "warmer" : "colder", m.MonthCover(pos.X, pos.Z, hours) * 100));
            }
            sb.Append($"\nWeather rules v{m.Settings.Rules} (this build v{RulesVersion}). Wind: {(ControlsWind ? "Earth's" : "an older Sea's own")}; Sea: {SeaLink}; weather role ({WeatherContract.Key}): {RoleStatus}, revision {Revision}; patches: {string.Join(", ", Active)}.");
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>The weather in words; snow or rain by <paramref name="temperature"/>, °C at the place and hour (NaN, where the
        /// climate can't be read: the month's sea-level normal).</summary>
        static string Describe(WeatherSample s, double temperature)
        {
            double p = s.Precipitation;
            if (p <= 0) return s.Thunder > 0 ? "a dry thunderstorm, its rain not reaching the ground" : s.Cloudness > 0.25 ? "dry, rain clouds gathering" : "dry";
            string kind = (double.IsNaN(temperature) ? s.NormalTemp : temperature) < 0 ? "snow" : "rain";
            return (p < 0.15 ? "light " : p < 0.5 ? "" : "heavy ") + kind + (s.Thunder > 0 ? " with thunder" : "");
        }
    }
    /// <summary>
    /// The vanilla readers that take X of the wind as its strength, handed the vector's horizontal length instead (open sea
    /// contracts section 5), on a client while Earth writes vanilla's wind as a vector. Read in 1.22.7's code first:
    ///  - DefaultShaderUniforms.Update: four reads of GlobalConstants.CurrentSurfaceWindSpeedClient.X (the foliage wave
    ///    counters, WindSpeed, the vanilla water's wave intensity);
    ///  - EntityBoat.OnRenderFrame: CurrentWindSpeedClient.X for how much a boat rocks. Its weather vane's Atan2(X, Z) is a
    ///    direction and keeps the vector, so the vane now turns with the wind.
    /// Exactly one mod may apply this, or the second takes the length of a length: the first sets
    /// api.ObjectCache["seacontracts:vanillawind-vector"] to its mod ID, and Earth leaves it when the key is there (Rough Seas
    /// patches the same two). Other vanilla X readers (fruit-tree leaf fall, the calm-air gate of ambient particles) are not
    /// in the contract and stay as they are; the rest of vanilla already takes the wind as a vector or its length.
    /// Where a reader is left alone (a game update changed it), Earth writes this client's wind in vanilla's own form, the
    /// strength in X, rather than a vector the reader would take for a strength (<see cref="ReadersTakeVector"/>, CR-W5).
    /// </summary>
    static class VanillaWindVector
    {
        public const string Key = "seacontracts:vanillawind-vector";
        const string HarmonyId = "earth.windvector";
        static Harmony harmony;
        /// <summary>Readers patched now (empty when another mod holds the key or nothing went in).</summary>
        public static readonly List<string> Active = new List<string>();
        /// <summary>Whether every reader takes the vector's length on this client: Earth patched them all, or the mod that holds
        /// the key does it. False before, and after one was refused: EarthWeather.OnGetWindSpeed then writes the strength in
        /// X (CR-W5).</summary>
        public static volatile bool ReadersTakeVector;

        static readonly FieldInfo SurfaceWind = AccessTools.Field(typeof(GlobalConstants), nameof(GlobalConstants.CurrentSurfaceWindSpeedClient));
        static readonly FieldInfo Wind = AccessTools.Field(typeof(GlobalConstants), nameof(GlobalConstants.CurrentWindSpeedClient));
        static readonly FieldInfo VecX = AccessTools.Field(typeof(Vec3f), nameof(Vec3f.X));

        public static void Apply(ICoreClientAPI capi, ILogger log)
        {
            if (capi == null || harmony != null) return;
            lock (capi.ObjectCache)
            {
                if (capi.ObjectCache.TryGetValue(Key, out var who))
                {
                    log.Notification("Earth weather: {0} already hands vanilla's wind readers the wind vector's length; Earth leaves that to it.", who as string ?? "another mod");
                    ReadersTakeVector = true;
                    return;
                }
                harmony = new Harmony(HarmonyId);
                // (CR-W12) each reader is looked up inside Patch's try: an ambiguous match after a game update would otherwise
                // escape the one-second watch that calls this. Both are tried, also when the first is refused (&, not &&).
                ReadersTakeVector = Patch(log, "shader wind", () => AccessTools.Method(typeof(DefaultShaderUniforms), nameof(DefaultShaderUniforms.Update)), SurfaceWind, nameof(ShaderTranspiler))
                    & Patch(log, "boat rocking", () => AccessTools.Method(typeof(EntityBoat), nameof(EntityBoat.OnRenderFrame)), Wind, nameof(BoatTranspiler));
                if (Active.Count > 0) capi.ObjectCache[Key] = WeatherContract.Owner;
                else harmony = null;
            }
        }

        /// <summary>Client only: the patch is the process's, so a single player's server side must never take it out.</summary>
        public static void Remove(ICoreClientAPI capi)
        {
            if (capi == null) return;
            ReadersTakeVector = false;
            if (harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
            Active.Clear();
            lock (capi.ObjectCache)
                if (capi.ObjectCache.TryGetValue(Key, out var who) && who as string == WeatherContract.Owner) capi.ObjectCache.Remove(Key);
        }

        /// <summary>Patches one reader after checking its code still reads X of the wind as a strength (vanilla's code is
        /// looked at first, so a changed method is left alone rather than half patched). False when it is left alone.</summary>
        static bool Patch(ILogger log, string what, Func<MethodInfo> find, FieldInfo field, string transpiler)
        {
            MethodInfo method = null;
            try
            {
                method = find();
                if (method == null || field == null || VecX == null) throw new Exception("method or field not found");
                StrengthForX(PatchProcessor.GetOriginalInstructions(method), field, out int reads);
                if (reads == 0) throw new Exception("it no longer reads the wind's X as its strength");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(VanillaWindVector), transpiler));
                Active.Add(what);
                return true;
            }
            catch (Exception e)
            {
                log.Warning("Earth weather: could not hand {0} the wind's strength ({1}): {2}; this client gets the wind as vanilla writes it, the strength alone (rain slants east, as in vanilla)",
                    method?.DeclaringType?.Name + "." + method?.Name, what, e.Message);
                return false;
            }
        }

        /// <summary>The vanilla strength a reader gets instead of X: the vector's horizontal length.</summary>
        public static float Strength(Vec3f v) => v == null ? 0f : WeatherContract.VanillaStrength(v.X, v.Z);

        /// <summary>Each read of <paramref name="field"/>.X becomes <see cref="Strength"/> of it, except where the next few
        /// instructions read the same field again (a direction: Atan2(X, Z)).</summary>
        static List<CodeInstruction> StrengthForX(IEnumerable<CodeInstruction> instructions, FieldInfo field, out int replaced)
        {
            var codes = new List<CodeInstruction>(instructions);
            var strength = AccessTools.Method(typeof(VanillaWindVector), nameof(Strength));
            replaced = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (!codes[i].LoadsField(field) || !codes[i + 1].LoadsField(VecX)) continue;
                bool direction = false;
                for (int k = i + 2; k < Math.Min(codes.Count, i + 5) && !direction; k++) direction = codes[k].LoadsField(field);
                if (direction) continue;
                var old = codes[i + 1];
                codes[i + 1] = new CodeInstruction(OpCodes.Call, strength).WithLabels(old.labels).WithBlocks(old.blocks);
                replaced++;
            }
            return codes;
        }

        static IEnumerable<CodeInstruction> ShaderTranspiler(IEnumerable<CodeInstruction> instructions) => StrengthForX(instructions, SurfaceWind, out _);

        static IEnumerable<CodeInstruction> BoatTranspiler(IEnumerable<CodeInstruction> instructions) => StrengthForX(instructions, Wind, out _);
    }

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
