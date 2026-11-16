using System;
using System.Globalization;
using System.IO;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace EarthMap
{
    /// <summary>
    /// EarthMapSystem on a client: the server's parameters received, the client's climate model and sea level from them,
    /// and the sea level put back when the client leaves (EA-0089: split from EarthMapSystem.cs, no change).
    /// </summary>
    public partial class EarthMapSystem
    {
        // ------------------------------------------------------------------------------------------ client

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<EarthParams>()
                .SetMessageHandler<EarthParams>(OnParams);
            StartSeasonsClient(api);
            api.Event.OnGetClimate += (climateHooks ??= new EarthClimateHooks(this)).OnGetClimate;
        }

        void OnParams(EarthParams p)
        {
            // (EA-0144) a world's params start its monthly temperature layer afresh: the server sends its tiles after this
            ResetClientSeasons();
            // (no provider check here: the required providers are server-side, RequiredMods, and the server checked them)
            Params = p;
            if (!p.Enabled)
            {
                Model = null;
                ClimateLapse.Use(this, null);
                astraTerra.Clear();
                sky.Set(null);
                RestoreSeaLevel();
                RaiseParamsReady(p);
                return;
            }
            UseSkyLongitude(p, capi);
            astraTerraLook.Suggest(capi, Mod.Logger);
            try
            {
                Data ??= LoadData(false);
                p.ConfigureClimateData(Data);
                ReportMissingClimateData(p, "the live temperatures here differ from the server's", "the live temperatures here differ from the server's");
                Model = new TerrainModel(Data, p.BuildGeometry(), p.BuildVertical(), p.NoiseSeed) { ClimateRules = p.ClimateRules, GeothermalRules = p.GeothermalRules, ClientClimate = true };
                // vanilla's client never sets this (its GetRainFall reads it): the world's own sea level, also a lowered one;
                // the value before is put back when this client leaves (CR-L4: a client that joins a non-Earth server next, in the
                // same process, would keep Earth's level, as vanilla resets it only on a server)
                seaLevelBefore ??= Climate.Sealevel;
                Climate.Sealevel = p.SeaLevel;
                UseClimateRules(p, Model);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Could not load the Earth climate data: {0}", e.Message);
            }
            RaiseParamsReady(p);
        }

        /// <summary>Client: the sea level vanilla's climate had before this client set the world's own (null while unset).</summary>
        int? seaLevelBefore;

        void RestoreSeaLevel()
        {
            if (seaLevelBefore is int before) Climate.Sealevel = before;
            seaLevelBefore = null;
        }
    }
}
