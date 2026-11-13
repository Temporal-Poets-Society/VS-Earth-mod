using System;
using System.Security.Cryptography;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthRequiredModsTest
{
    // Test-only observer. It runs in explicitly named disposable data paths, never a player profile.
    public class Probe : ModSystem
    {
        public override double ExecuteOrder() => -0.1;
        public override void StartServerSide(ICoreServerAPI api)
        {
            void Capture(string stage)
            {
                byte[] data = api.WorldManager.SaveGame.GetData("earth-params");
                byte[] datum = api.WorldManager.SaveGame.GetData("earth-sealevel");
                var earth = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
                var p = earth?.GetType().GetProperty("Params")?.GetValue(earth);
                var weather = api.ModLoader.GetModSystem("EarthMap.EarthWeather");
                var sea = api.ModLoader.GetModSystem("Sea.SeaSystem");
                api.Logger.Notification("REQUIREDMODS {0}: earth={1}; params={2}; datum={3}; enabled={4}; layout={5}; query={6}; weather={7}; wind={8}",
                    stage, api.ModLoader.IsModEnabled("earth"), data == null ? "none" : Convert.ToHexString(SHA256.HashData(data)),
                    datum == null ? "none" : BitConverter.ToString(datum), p?.GetType().GetField("Enabled")?.GetValue(p),
                    p?.GetType().GetField("RiverPoolLayout")?.GetValue(p), earth?.GetType().GetProperty("RiverQueryVersion")?.GetValue(earth),
                    weather?.GetType().GetProperty("SeaLink")?.GetValue(weather), sea?.GetType().GetProperty("WindProviderName")?.GetValue(sea));
            }
            Capture("EARLY");
            api.Event.GameWorldSave += () => Capture("SAVE");
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () => Capture("READY"));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ =>
            {
                Capture("RUN");
                api.Server.ShutDown();
            }, 3000));
        }
    }
}
