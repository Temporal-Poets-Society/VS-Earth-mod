using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EarthMap
{
    /// <summary>
    /// Lightning and thunder from Earth's own thunderstorms (Core.Thunder, WeatherModel.ThunderAt) instead of vanilla's thunder
    /// weather events, which come with the random weather pattern of a region, desert or not.
    ///  - Server: vanilla's region lightning is off (each region's weather event strikes at LightningRate 0 for the length of
    ///    its tick, and has its own rate back after it, so the save keeps vanilla's: CR-W8). Every quarter second
    ///    lightning strikes around each player where Earth's weather has a thunderstorm, as often as the storm is strong, through
    ///    vanilla's own strike (WeatherSystemServer.SpawnLightningFlash: the bolt, its damage, lightning rods, and with the
    ///    world's lightningFires on, ModSystemFireFromLightning's fire where the struck block burns and the last ten days
    ///    were dry, which Earth's rain decides).
    ///  - Client: vanilla's near thunder and the sky's flash follow the thunderstorm overhead and close by; the distant thunder
    ///    of storms out to about 1200 blocks is played from their side, even under a clear sky.
    /// With Earth's weather on (EarthWeather.Model); vanilla's thunder while an admin sets the precipitation (/weather), and
    /// for the session when a game update changed what the patch on vanilla's region tick needs (PatchVanilla's ThunderTable
    /// says which member). Letting go gives vanilla back every value Earth changed in the regions' weather events.
    /// </summary>
    public class EarthThunder : ModSystem
    {
        const string HarmonyId = "earth.thunder";

        /// <summary>Ground strikes per block² and second under the core of a thunderstorm (thunder 1).</summary>
        public const double StrikeRate = 1.0e-6;
        /// <summary>Strikes are drawn in this radius around each player (blocks): the bolts in view.</summary>
        public const double StrikeRadius = 320;
        const int StrikeSamples = 16;
        const float ServerTick = 0.25f;

        /// <summary>Vanilla's near thunder chance per client tick (50 ms) under the core of a storm.</summary>
        const float NearRate = 0.0035f;
        /// <summary>Distant thunder claps a second when a whole ring of the horizon has thunder.</summary>
        const double DistantPerSecond = 0.2;
        const double NearRingRadius = 140;
        static readonly (double radius, int points, double weight)[] DistantRings = { (350, 8, 1.0), (700, 12, 0.7), (1200, 16, 0.45) };

        static readonly object sync = new object();
        static Harmony harmony;
        static int users;
        static volatile EarthThunder serverInstance, clientInstance;

        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;
        WeatherSystemBase weather;
        /// <summary>This side holds one of the patch's users.</summary>
        bool applied;
        /// <summary>The patch could not be bound in this session (PatchVanilla said why, once): Earth's thunder stays off here
        /// and vanilla's thunder events run, never both.</summary>
        bool failed;
        long watchId, tickId;

        /// <summary>Whether Earth's thunder runs on this side now.</summary>
        public bool On { get; private set; }
        /// <summary>Client: vanilla's near thunder chance per tick for the regions (0 none).</summary>
        volatile float nearTarget;
        /// <summary>Strikes spawned since the server started (/earthweather, tests).</summary>
        public long Strikes { get; private set; }

        public override double ExecuteOrder() => 0.07;

        public static EarthThunder For(EnumAppSide side) => side == EnumAppSide.Server ? serverInstance : clientInstance;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            if (api.Side == EnumAppSide.Server) serverInstance = this; else clientInstance = this;
            watchId = api.Event.RegisterGameTickListener(_ => Watch(), 1000);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            tickId = api.Event.RegisterGameTickListener(StrikeTick, (int)(ServerTick * 1000));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            tickId = api.Event.RegisterGameTickListener(ClientTick, 500);
        }

        public override void Dispose()
        {
            if (api != null)
            {
                api.Event.UnregisterGameTickListener(watchId);
                api.Event.UnregisterGameTickListener(tickId);
            }
            On = false;
            Release();
            if (serverInstance == this) serverInstance = null;
            if (clientInstance == this) clientInstance = null;
            base.Dispose();
        }

        /// <summary>Earth's weather while vanilla's rain is Earth's too (CR-W5: the rain patch can be refused on its own after a
        /// game update; thunder over vanilla's rain, and lightning fires judged by vanilla's wetness, would not match).</summary>
        WeatherModel Model => EarthWeather.RainPatched ? EarthWeather.For(api.Side)?.Model : null;

        /// <summary>On while Earth's weather runs here, no admin has set the precipitation and vanilla's thunder events could be
        /// turned off (the patch is bound): Earth's strikes and thunder never come on top of vanilla's.</summary>
        void Watch()
        {
            weather ??= api.Side == EnumAppSide.Server ? api.ModLoader.GetModSystem<WeatherSystemServer>() : (WeatherSystemBase)api.ModLoader.GetModSystem<WeatherSystemClient>();
            bool on = !failed && Model != null && weather != null && !weather.OverridePrecipitation.HasValue;
            if (on == On) return;
            if (on)
            {
                if (!Apply())
                {
                    failed = true;
                    return;
                }
                On = true;
                if (api.Side == EnumAppSide.Server)
                    Mod.Logger.Notification("Earth thunder: lightning from Earth's thunderstorms (thunder rules v{0}); lightning fires {1} (world option lightningFires).",
                        Thunder.RulesVersion, api.World.Config.GetBool("lightningFires") ? "on" : "off");
            }
            else
            {
                On = false;
                nearTarget = 0;
                Release();
            }
        }

        // ------------------------------------------------------------------------------------------ vanilla's thunder events

        /// <summary>Takes one of the patch's users, binding it for the first (PatchVanilla's ThunderTable: the region tick, the
        /// weather system it injects and the event and snapshot fields the prefix changes, all checked first). False when it
        /// can't be bound (PatchVanilla's line says which member a game update changed); nothing of it is then patched.</summary>
        bool Apply()
        {
            if (applied) return true;
            lock (sync)
            {
                if (harmony == null)
                {
                    var h = new Harmony(HarmonyId);
                    var pv = PatchVanilla.For(api, Mod, h);
                    pv.Apply(PatchVanilla.ThunderTable());
                    // (the table binds the region tick's prefix and its finalizer together, CR-W8: one without the other is off)
                    if (!pv.On(PatchVanilla.Feature.Thunder)) return false;
                    harmony = h;
                }
                users++;
                applied = true;
                return true;
            }
        }

        /// <summary>Gives vanilla back every value Earth changed on this side, then this side's use of the patch (the last one
        /// unpatches). Called once <see cref="On"/> is false, so no prefix of this side changes them again.</summary>
        void Release()
        {
            if (!applied) return;
            applied = false;
            GiveBack(api?.Side == EnumAppSide.Client);
            lock (sync)
            {
                if (--users > 0 || harmony == null) return;
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        /// <summary>Before each region's weather tick: its weather event strikes no lightning (the server's strikes are Earth's),
        /// and on the client its thunder is Earth's near thunder, at any temperature (Earth's storms already need the warmth).</summary>
        internal static void RegionTickPrefix(WeatherSimulationRegion __instance, WeatherSystemBase ___ws, out (WeatherEventState State, float Rate) __state)
        {
            __state = default;
            var side = ___ws?.api?.Side;
            if (side == null) return;
            var self = For(side.Value);
            var st = __instance.CurWeatherEvent?.State;
            if (self == null || !self.On || st == null) return;
            // (CR-W8) the server's strike rate is 0 for this tick only (vanilla reads it in the tick): the state is saved with
            // the region (WeatherSystemServer.OnSaveGameSaving), and a 0 left in it would come back after a reload as vanilla's
            // own rate, which Taken would then note and give back. RegionTickFinalizer puts the rate back after the tick.
            if (side == EnumAppSide.Server) __state = (st, st.LightningRate);
            self.TakeOver(__instance, st, side == EnumAppSide.Client);
        }

        /// <summary>After each region's weather tick, also one that threw: the server's event gets vanilla's strike rate back
        /// (CR-W8). The state the prefix changed, as the tick may have picked a new event since.</summary>
        internal static void RegionTickFinalizer((WeatherEventState State, float Rate) __state)
        {
            if (__state.State != null) __state.State.LightningRate = __state.Rate;
        }

        /// <summary>The weather event state Earth last changed in a region, and vanilla's values it replaced there.</summary>
        sealed class Taken
        {
            WeatherEventState state;
            float lightningRate, nearThunderRate, distantThunderRate, lightningMinTemp;

            /// <summary>Notes vanilla's values of the region's state now, the first time Earth is about to change it (a new
            /// event, or on the client the server's update of it, is a new state); the one before gets its values back.</summary>
            public void Note(WeatherEventState st, bool client)
            {
                if (st == state) return;
                Restore(client);
                state = st;
                lightningRate = st.LightningRate;
                nearThunderRate = st.NearThunderRate;
                distantThunderRate = st.DistantThunderRate;
                lightningMinTemp = st.LightningMinTemp;
            }

            /// <summary>Gives the state Earth changed vanilla's values back (on the server Earth changed the strike rate only).</summary>
            public void Restore(bool client)
            {
                if (state == null) return;
                state.LightningRate = lightningRate;
                if (client)
                {
                    state.NearThunderRate = nearThunderRate;
                    state.DistantThunderRate = distantThunderRate;
                    state.LightningMinTemp = lightningMinTemp;
                }
                state = null;
            }
        }

        /// <summary>The regions whose event Earth changed on this side; weakly, so an unloaded region's goes with it.</summary>
        readonly ConditionalWeakTable<WeatherSimulationRegion, Taken> taken = new ConditionalWeakTable<WeatherSimulationRegion, Taken>();

        /// <summary>Earth's thunder in place of the region's event's, vanilla's values noted first.</summary>
        internal void TakeOver(WeatherSimulationRegion region, WeatherEventState st, bool client)
        {
            taken.GetValue(region, _ => new Taken()).Note(st, client);
            st.LightningRate = 0;
            if (client)
            {
                st.NearThunderRate = nearTarget;
                st.DistantThunderRate = 0;
                st.LightningMinTemp = -100;
                var wd = region.weatherData;
                wd.distantLightningRate = 0;
                wd.lightningMinTemp = -100;
            }
        }

        /// <summary>
        /// When Earth lets go (an admin's /weather, Earth's weather stopping, the world closing): every event state Earth changed
        /// gets vanilla's values back (WeatherEvent.Update doesn't restore them; they would stay Earth's until the event ends),
        /// and on the client each region's thunder snapshot is put at its event's values, which vanilla only eases towards by
        /// at most 0.001 a 50 ms tick (well over an hour of play from Earth's -100 °C back to a storm's minimum temperature).
        /// </summary>
        internal void GiveBack(bool client)
        {
            foreach (var (region, t) in taken)
            {
                t.Restore(client);
                if (!client) continue;
                var now = region.CurWeatherEvent?.State;
                var wd = region.weatherData;
                if (now == null || wd == null) continue;
                wd.distantLightningRate = now.DistantThunderRate;
                wd.lightningMinTemp = now.LightningMinTemp;
            }
            taken.Clear();
        }

        // ------------------------------------------------------------------------------------------ server: strikes

        readonly List<Vec3d> players = new List<Vec3d>();

        void StrikeTick(float dt)
        {
            if (!On || sapi == null) return;
            var m = Model;
            var ws = weather as WeatherSystemServer;
            if (m == null || ws == null) return;
            players.Clear();
            foreach (var p in sapi.World.AllOnlinePlayers)
            {
                if (p is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing && p.Entity != null) players.Add(p.Entity.Pos.XYZ);
            }
            if (players.Count == 0) return;
            double hours = sapi.World.Calendar.TotalHours;
            double area = Math.PI * StrikeRadius * StrikeRadius / StrikeSamples;
            var rand = sapi.World.Rand;
            foreach (var c in players)
            {
                for (int i = 0; i < StrikeSamples; i++)
                {
                    double r = StrikeRadius * Math.Sqrt(rand.NextDouble()), a = rand.NextDouble() * 2 * Math.PI;
                    double x = c.X + r * Math.Cos(a), z = c.Z + r * Math.Sin(a);
                    // a place near several players is drawn by each of them
                    int near = 0;
                    foreach (var o in players) if ((o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) <= StrikeRadius * StrikeRadius) near++;
                    double chance = StrikeRate * area * dt / Math.Max(1, near);
                    if (rand.NextDouble() >= chance) continue;
                    double t = m.ThunderAt(x, z, hours);
                    if (t <= 0 || rand.NextDouble() >= t) continue;
                    if (sapi.World.BlockAccessor.GetMapChunkAtBlockPos(new BlockPos((int)x, 0, (int)z)) == null) continue;
                    ws.SpawnLightningFlash(new Vec3d(x, sapi.World.SeaLevel, z));
                    Mod.Logger.VerboseDebug("Earth thunder: strike at ={0:0} ={1:0} (thunder {2:0.00})", x, z, t);
                    Strikes++;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ client: thunder

        static readonly AssetLocation DistantSound = new AssetLocation("sounds/weather/lightning-distant.ogg");
        readonly List<(double weight, double x, double z, double dist)> sources = new List<(double, double, double, double)>();

        void ClientTick(float dt)
        {
            if (!On || capi?.World?.Player?.Entity == null) return;
            var m = Model;
            if (m == null) return;
            var pos = capi.World.Player.Entity.Pos;
            double hours = capi.World.Calendar.TotalHours;
            // near: the storm overhead or just by
            double near = m.ThunderAt(pos.X, pos.Z, hours);
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3;
                near = Math.Max(near, 0.6 * m.ThunderAt(pos.X + NearRingRadius * Math.Cos(a), pos.Z + NearRingRadius * Math.Sin(a), hours));
            }
            nearTarget = (float)(NearRate * near);
            // distant: claps from the storms around, from their side
            sources.Clear();
            double total = 0, phase = capi.World.Rand.NextDouble();
            foreach (var (radius, points, weight) in DistantRings)
            {
                for (int k = 0; k < points; k++)
                {
                    double a = (k + phase) * 2 * Math.PI / points;
                    double x = pos.X + radius * Math.Cos(a), z = pos.Z + radius * Math.Sin(a);
                    double t = m.ThunderAt(x, z, hours);
                    if (t <= 0) continue;
                    double w = t * weight / points;
                    sources.Add((w, x, z, radius));
                    total += w;
                }
            }
            if (total <= 0) return;
            var rand = capi.World.Rand;
            if (rand.NextDouble() >= DistantPerSecond * total * dt) return;
            double pick = rand.NextDouble() * total;
            var s = sources[sources.Count - 1];
            foreach (var c in sources)
            {
                if ((pick -= c.weight) <= 0) { s = c; break; }
            }
            double dx = s.x - pos.X, dz = s.z - pos.Z, len = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
            float far = (float)GameMath.Clamp(s.dist / 1200, 0, 1);
            float muffle = WeatherSimulationSound.roomVolumePitchLoss;
            float volume = GameMath.Clamp((0.35f + 0.5f * (float)rand.NextDouble()) * (1 - 0.55f * far) * (1 - 0.6f * muffle), 0, 1);
            float pitch = GameMath.Clamp(0.95f - 0.3f * far + 0.15f * (float)rand.NextDouble() - 0.2f * muffle, 0.5f, 1.15f);
            capi.World.PlaySoundAt(DistantSound, pos.X + dx / len * 24, pos.Y + 8, pos.Z + dz / len * 24, null, EnumSoundType.Weather, pitch, 64, volume);
        }
    }
}
