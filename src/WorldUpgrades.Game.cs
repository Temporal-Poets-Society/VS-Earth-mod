using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace EarthMap
{
    /// <summary>WorldUpgrades in the game: the save's data, the world's options, the loaded Earth data, the server's run phases
    /// and save event, and ModConfig/earth.json (EA-0088).</summary>
    sealed class GameUpgradeHost : IUpgradeHost
    {
        readonly EarthMapSystem core;
        readonly ICoreServerAPI sapi;

        public GameUpgradeHost(EarthMapSystem core, ICoreServerAPI sapi)
        {
            this.core = core;
            this.sapi = sapi;
        }

        public EarthMapConfig Config => core.Config;
        public void Store(EarthParams p) => sapi.WorldManager.SaveGame.StoreData(EarthMapSystem.SaveKey, SerializerUtil.Serialize(p));
        public void SaveParams() => core.SaveParams();
        public string CanalCodes() => EarthWorldOptions.CanalCodes(sapi.WorldManager.SaveGame.WorldConfiguration);
        public int ClimateFixAvailable => core.LoadedData().ClimateFixAvailable;
        public string PeakCatalogSignature() => core.LoadedData().ReadPeaks()?.Signature;
        public int CoastsFor(Core.EarthGeometry geo, bool islands) => core.CoastsFor(geo, islands);
        public void OnRunGame(Action handler) => sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, handler);
        public void AddSaveHandler(Action handler) => sapi.Event.GameWorldSave += handler;
        public void RemoveSaveHandler(Action handler) => sapi.Event.GameWorldSave -= handler;
        public void ResetFlags(EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log)
            => EarthConfigStore.ResetFlags(sapi, session, reset, names, log);
    }
}
