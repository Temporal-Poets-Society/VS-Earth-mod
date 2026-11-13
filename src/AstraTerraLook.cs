using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Suggests Earth's look to AstraTerra (optional) in Earth worlds: a round sun and a telescope moon, both nearer their
    /// real size, a dimmer Milky Way, and a naked-eye star field with the bright stars standing out. By reflection only, as
    /// for the longitude (AstraTerraLongitude). AstraTerra takes a suggestion only for a setting the player left at its
    /// default, keeps it for the session and never writes it to astraterra.json; AcceptMapSuggestions: false there turns
    /// this off. Needs AstraTerra's SuggestSetting (not in the 0.15.0 release); without it Earth logs once and does nothing.
    /// Client only: these settings are how the sky is drawn.
    /// </summary>
    sealed class AstraTerraLook
    {
        const string SystemType = "AstraTerra.AstraTerraModSystem";

        /// <summary>Suggests each setting to AstraTerra on this (client) side, if it is loaded and can take suggestions.</summary>
        public void Suggest(ICoreAPI api, ILogger log)
        {
            var system = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == SystemType);
            if (system == null) return;
            if (SettingSuggestionBinding.Find(system.GetType()) == null)
            {
                log.Notification("Earth Map sky look: this AstraTerra has no SuggestSetting; its sky keeps its own settings.");
                return;
            }

            var applied = new List<string>();
            var kept = new List<string>();
            foreach (var setting in SkyLook.Settings)
            {
                switch (SettingSuggestionBinding.TrySuggest(system, setting.Key, setting.Value, out string error))
                {
                    case SettingSuggestionBinding.Result.Applied:
                        applied.Add(setting.Key + "=" + setting.Value);
                        break;
                    case SettingSuggestionBinding.Result.Kept:
                        kept.Add(setting.Key);
                        break;
                    default:
                        log.Warning("Earth Map sky look: AstraTerra refused {0}={1} ({2}).", setting.Key, setting.Value, error);
                        break;
                }
            }
            log.Notification("Earth Map sky look: AstraTerra took {0}; kept the player's own {1}.",
                applied.Count > 0 ? string.Join(", ", applied) : "nothing",
                kept.Count > 0 ? string.Join(", ", kept) : "nothing");
        }
    }
}
