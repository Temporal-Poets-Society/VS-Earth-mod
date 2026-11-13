using System;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Earth's sea-level choice (decisions 0031/0036). Vanilla sets the sea level from the world's
    /// height at every start (GenTerra), so a lowered one is set again at every start, at WorldReady: registered before
    /// Cylinder World (0.01) and Earth Map (0.05), which capture it there. A new world takes it from its creation settings
    /// (only where Earth will generate it, so a world refused early keeps vanilla's), a saved Earth world from its EarthParams.
    /// A lowered level is also saved on its own (DatumKey): Earth can still refuse the new world after this ran (Cylinder World
    /// turned itself off on a narrow map), and that vanilla world keeps the level its first chunks were made at.
    /// Vanilla's sea-level deposits were scaled for vanilla's level before this runs; they are rescaled here (decision 0045).
    /// </summary>
    public sealed class EarthVerticalBootstrap : ModSystem
    {
        const string DatumKey = "earth-sealevel";
        public override double ExecuteOrder() => -.05;
        bool patched;

        // StartPre: the Earth tab's world height goes into a new Earth world before anything reads the height.
        public override void StartPre(ICoreAPI api)
        {
            if (api is not ICoreServerAPI sapi) return;
            var save = sapi.WorldManager.SaveGame;
            if (save.GetData(EarthMapSystem.SaveKey) != null
                || !EarthWorldOptions.CreatesEarth(EarthMapSystem.IsNewWorld(save), save.WorldType, save.PlayStyle, EarthMapSystem.ConfigEnabled(sapi))) return;
            // Mark the world as being created now: the game writes the save at AssetsFinalize, after StartPre, but saves
            // nothing more until the game runs, so a first start that stops at WorldReady (Earth's data could not be had, a
            // required mod refused) keeps this marker and the next start finishes creating the world
            // (EarthMapSystem.IsNewWorld). EarthMapSystem clears it once the world's params are stored.
            save.StoreData(EarthMapSystem.CreatingKey, new byte[] { 1 });
            try
            {
                string line = EarthWorldOptions.ApplyHeight(sapi);
                if (line != null) Mod.Logger.Notification(line);
            }
            catch (Exception e) { Mod.Logger.Error("Could not set the world height from the Earth tab ({0}); this world keeps the World generation tab's height.", e.Message); }
        }

        // Start runs before AssetsFinalize, where GenDeposits initialises the deposits.
        public override void Start(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Server) return;
            SealevelDeposits.Patch(api, Mod);
            patched = true;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () =>
            {
                // (the required providers were checked in EarthMapSystem.StartServerSide: a refusal there stops the server
                // before this phase)
                var save = api.WorldManager.SaveGame;
                var p = EarthMapSystem.Saved(save);
                byte[] datum = save.GetData(DatumKey);
                int vanilla = api.World.SeaLevel, sea = vanilla;
                bool earth = true;
                var earthSystem = api.ModLoader.GetModSystem<EarthMapSystem>();
                if (p?.Enabled == true) sea = p.SeaLevel;
                else if (datum?.Length == 4) sea = BitConverter.ToInt32(datum, 0);
                // (whether the world is new as this start found it: a WorldReady step, Cylinder World's, may store its record)
                else if (p == null && EarthWorldOptions.CreatesEarth(earthSystem?.NewWorldAtStart ?? EarthMapSystem.IsNewWorld(save), save.WorldType, save.PlayStyle,
                    earthSystem?.Config?.Enabled == true)
                    && CylinderCompat.HasEarthConfiguration(api))
                {
                    sea = EarthWorldOptions.Terrain(save.WorldConfiguration, api.WorldManager.MapSizeY).SeaLevel(api.WorldManager.MapSizeY, vanilla);
                    if (sea != vanilla) save.StoreData(DatumKey, BitConverter.GetBytes(sea));
                }
                else earth = false;
                if (sea != vanilla)
                {
                    api.WorldManager.SetSeaLevel(sea);
                    TerraGenConfig.seaLevel = Climate.Sealevel = sea;
                    Mod.Logger.Notification("Earth Map: sea level {0} (vanilla's for this height: {1}).", sea, vanilla);
                }
                if (!earth) return;
                string rescaled = SealevelDeposits.Rescale(api, vanilla);
                if (rescaled != null) Mod.Logger.Notification(rescaled);
            });
        }

        public override void Dispose()
        {
            if (patched) SealevelDeposits.Unpatch();
        }

        public override void StartClientSide(ICoreClientAPI api) => SeasonTintAltitude.Start(api, Mod.Logger);
    }
}
