using System;
using System.Globalization;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>A new Earth world's spawn: where the Earth tab says to start, on dry land (EarthMapSystem, once at creation).</summary>
    static class EarthSpawn
    {
        /// <summary>The place for each starting climate when no random place matching it is found.</summary>
        static readonly (string climate, double lat, double lon, string name)[] SpawnAnchors =
        {
            ("temperate", 48.60, 7.80, "the Upper Rhine valley"),
            ("warm", 37.40, -5.90, "Andalusia"),
            ("hot", 13.50, 2.10, "the Sahel on the Niger"),
            ("cool", 64.50, 40.60, "the White Sea coast"),
            ("icy", 69.30, 88.20, "the Taymyr lowland"),
        };

        /// <summary>
        /// The Earth tab's start (decision 0023): a random place at the starting climate (SpawnSearch.FindClimatePlace,
        /// lowland), a capital, or coordinates (the nearest dry, gentle land at any height).
        /// </summary>
        /// <returns>A line for the players who join while this world is being made, or null: the start coordinates couldn't be
        /// read (2.0.1: said in chat with the formats that work, not only in the server log).</returns>
        public static string Choose(ICoreServerAPI sapi, TerrainModel model, EarthMapConfig config, EarthParams p, ILogger log)
        {
            string notice = null;
            var wc = sapi.WorldManager.SaveGame.WorldConfiguration;
            int mapX = sapi.WorldManager.MapSizeX, mapZ = sapi.WorldManager.MapSizeZ;
            string choice = EarthWorldOptions.Spawn(wc);
            double lat = double.NaN, lon = double.NaN;
            string where = null;
            int maxAboveSea = sapi.WorldManager.MapSizeY;
            if (choice == EarthWorldOptions.CustomSpawn)
            {
                string coords = EarthWorldOptions.SpawnCoordinates(wc);
                if (Coordinates.TryParse(coords, out double la, out double lo, out string why))
                {
                    lat = Math.Clamp(la, -85, 85);
                    lon = Geo.Wrap180(lo);
                    where = EarthMapSystem.FormatLatLon(lat, lon);
                }
                else
                {
                    notice = string.Format("Earth: the Earth start tab's coordinates '{0}' couldn't be read ({1}), so this world starts at a random place " +
                        "at the starting climate. Coordinates work as {2}.", coords, why, Coordinates.Formats);
                    log.Warning("{0}", notice);
                }
            }
            else if (choice != EarthWorldOptions.RandomSpawn)
            {
                var c = Array.Find(EarthWorldOptions.Capitals, s => s.code == choice);
                if (c.code != null) { lat = c.lat; lon = c.lon; where = c.name; }
                else log.Warning("Earth tab: unknown start '{0}'; using a random place at the starting climate.", choice);
            }
            if (double.IsNaN(lat))
            {
                maxAboveSea = SpawnSearch.LowlandAboveSea;
                string climate = wc?.GetString("startingClimate", "temperate") ?? "temperate";
                if (SpawnSearch.FindClimatePlace(model, mapX, mapZ, sapi.WorldManager.Seed, climate, out lat, out lon, out double t))
                {
                    where = string.Format(CultureInfo.InvariantCulture, "a random {0} place ({1:0.0} °C yearly mean)", climate, t);
                }
                else
                {
                    var a = Array.Find(SpawnAnchors, s => s.climate == climate);
                    if (a.climate == null) a = SpawnAnchors[0];
                    lat = a.lat; lon = a.lon; where = a.name;
                    log.Warning("No random {0} place found; starting in {1}.", climate, where);
                }
            }

            model.Geometry.FromGeo(lat, lon, out double ax, out double az);
            if (!SpawnSearch.FindDryLand(model, mapX, mapZ, ax, az, config.SpawnSearchRadius, maxAboveSea, out int sx, out int sz))
            {
                log.Warning("No dry land found within {0} blocks of {1}; spawning there anyway.", config.SpawnSearchRadius, where);
                sx = (int)ax; sz = (int)az;
            }
            var g = model.Geometry.ToGeo(sx, sz);
            p.SpawnX = sx; p.SpawnZ = sz; p.SpawnLat = g.Lat; p.SpawnLon = g.Lon;
            // y = null: the engine puts players on the surface there
            sapi.WorldManager.SaveGame.DefaultSpawn = new PlayerSpawnPos(sx, null, sz);
            log.Notification("Earth Map: spawn in {0}, at {1}.", where, EarthMapSystem.FormatLatLon(g.Lat, g.Lon));
            return notice;
        }
    }
}
