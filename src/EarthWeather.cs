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
    public partial class EarthWeather : ModSystem
    {
        /// <summary>Version of the weather rules: server and clients must run the same to see the same weather.</summary>
        /// <remarks>1: 0.15.0. 2: 0.15.1, the reanalysis' winds and clouds, hashed rain-field keys, one canonical time. 3: thunderstorms
        /// (Core.Thunder; rain under them, EarthThunder's lightning), the rain fields at a fresh lattice phase each period.
        /// 4: wet and dry spells (rain in runs of wet days with dry weeks between, thunderstorms with them where the air is
        /// humid; clouds and pressure follow them). 5: rain hours (it rains for as much of the time as airport reports show:
        /// long light snowfalls in the cold, short heavy showers in the heat; more wet days in winter). 6 (13 November 2026):
        /// a build that also runs rules 6 for the worlds made with them (EarthParams.WeatherModelRules: the cold seas' low
        /// cloud decks, hail from Earth's thunderstorms, the weather in the temperature); worlds made before keep rules 5.
        /// Since 6 this is the handshake (the newest rules both sides can run); a world's own are WeatherModelRules.
        /// 7 (14 November 2026): the ground under the weather (the rain rate at the ground's temperature, the desert coasts as
        /// dry as climate fix 4: Core.WeatherGround, weatherground.bin) and more thunder in hot dry months, for new worlds.</remarks>
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
            // ER-18: built as soon as the params are set; the one-second watch stays for the role's re-claim (R2) and changes
            if (earth != null) earth.ParamsReady += OnParamsReady;
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

        void OnParamsReady(EarthParams p) => Watch();

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
            // rules 7 read the ground (weatherground.bin, climatefix4.bin, both in the mod): without it this side would make
            // other weather than one that has it, so it keeps vanilla's
            bool noGround = WeatherRules.For(p.WeatherModelRulesOrOld).Ground && earth.Data?.Weather != null && earth.Data.WeatherGround == null;
            // (EA-0118) and the same ground as the server's, where it sends one
            // ("none": the server's rules read the ground and it has none, so it runs vanilla's weather; the client does too)
            bool otherGround = !noGround && WeatherRules.For(p.WeatherModelRulesOrOld).Ground && earth.Data?.Weather != null
                && !string.IsNullOrEmpty(p.WeatherGroundSignature) && p.WeatherGroundSignature != earth.Data.WeatherGroundSignature;
            // (X-L6) the parameters the model is built from, as received: a broken one keeps vanilla's weather on this side
            bool badParams = double.IsNaN(p.WeatherPace) || double.IsInfinity(p.WeatherPace)
                || !(p.RainFrequency >= 0) || double.IsInfinity(p.RainFrequency) || !(p.StormFrequency >= 0) || double.IsInfinity(p.StormFrequency);
            if (!p.Enabled || !p.Weather || earth.Data?.Weather == null || p.WeatherRules != RulesVersion || !sameData || noGround || otherGround || badParams)
            {
                if (p.Enabled && p.Weather && p.WeatherRules != RulesVersion)
                    Mod.Logger.Warning("Earth weather: the server runs weather rules v{0}, this Earth Map has v{1}; vanilla weather here until both match.", p.WeatherRules, RulesVersion);
                else if (p.Enabled && p.Weather && earth.Data?.Weather == null)
                    Mod.Logger.Warning("Earth weather: earthdata/weather.bin is missing or unreadable; vanilla weather.");
                else if (p.Enabled && p.Weather && !sameData)
                    Mod.Logger.Warning("Earth weather: this side's earthdata/weather.bin differs from the server's; vanilla weather here (reinstall the same Earth Map).");
                else if (p.Enabled && p.Weather && noGround)
                    Mod.Logger.Warning("Earth weather: this world's weather rules v{0} need the weather's ground, which can't be read ({1}); vanilla weather here (reinstall Earth Map).",
                        p.WeatherModelRulesOrOld, earth.Data.WeatherGroundError);
                else if (p.Enabled && p.Weather && otherGround && p.WeatherGroundSignature == EarthParams.NoWeatherGround)
                    Mod.Logger.Warning("Earth weather: the server has no weather ground (earthdata/weatherground.bin or the climate fixes), so it runs vanilla weather; vanilla weather here too (reinstall Earth Map on the server).");
                else if (p.Enabled && p.Weather && otherGround)
                    Mod.Logger.Warning("Earth weather: this side's weather ground (earthdata/weatherground.bin and the climate fixes) differs from the server's; vanilla weather here (reinstall the same Earth Map).");
                else if (p.Enabled && p.Weather && badParams)
                    Mod.Logger.Warning("Earth weather: the weather's settings are broken (pace {0}, rain x{1}, storms x{2}); vanilla weather here.",
                        p.WeatherPace, p.RainFrequency, p.StormFrequency);
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
            // rules 7: the ground under the weather, set before the model is published like the local sun below
            if (settings.Has.Ground) built.Ground = earth.Data.WeatherGround;
            // afternoon showers by the local sun when the server's AstraTerra sun follows Earth's meridians (decision 0010); the
            // server decides it (WeatherLocalSun) so every side has the same rain. Set before the model is published, so no
            // thread ever reads it without (the weather role's answers must be the same on every side).
            var es = earth;
            if (p.WeatherLocalSun) built.LocalHourOffset = x => (es.LongitudeAt(x) ?? 0) / 15.0;
            model = built;
            rainHookFailed = false;
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
        /// <summary>Changes whenever this side's weather is built, rebuilt or dropped (the role's <c>revision</c>).</summary>
        public long Revision => Interlocked.Read(ref revision);

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

        // ------------------------------------------------------------------------------------------ wind

        /// <summary>(X-L2, the Fable pass) The weather's hour now: the calendar's TotalDays (with vanilla's timelapse, which
        /// TotalHours leaves out) times the day's hours, as vanilla asks the rain hook (GetRainCloudness's totalDays) and the
        /// climate hook. The drawn clouds, wind, hail, fog and thunder ask at this hour too, so they are the rain's own hour to
        /// the bit (TotalHours differed by up to 8.8e-13 h, and by the timelapse's days while one ran).</summary>
        public static double HoursNow(IWorldAccessor world, WeatherModel m) => world.Calendar.TotalDays * m.Settings.HoursPerDay;

        /// <summary>Vanilla's wind strength by height (WeatherSimulationRegion.GetWindSpeed): stronger up high, weak underground.</summary>

        public static double AltitudeScale(double strength, double y, int seaLevel)
        {
            if (y > seaLevel) return Math.Min(strength * Math.Max(1.0, 0.9 + (y - seaLevel) / 100.0), WeatherModel.MaxStrength);
            return strength / (1.0 + (seaLevel - y) / 4.0);
        }

    }
}
