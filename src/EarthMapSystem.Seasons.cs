using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EarthMap.Core;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// The monthly temperature layer's data side (EA-0144, decision 0086): the server fetches seasons-1.bin into
    /// ModData/earth/data for a world whose SeasonRules read it, opens it (tiles read from disk as needed), and sends each
    /// player the 3 x 3 tiles of 3° around them as they move; a player's game builds its layer from those tiles. Players'
    /// games download nothing. Where the file can't be had this session, <see cref="Seasons"/> stays null: the server and
    /// its players use the old season shape (the weather runtime's fallback), and the server tries again at its next start.
    /// Nothing generated depends on it, so no choice is saved for good.
    /// </summary>
    public partial class EarthMapSystem
    {
        const string SeasonChannelName = "earth-seasons";
        /// <summary>How often the server looks for players who need new tiles (ms): a teleport's tiles follow within it.</summary>
        const int SeasonPushMs = 1000;

        /// <summary>The monthly temperature layer: the server's file, or the tiles a player's game was sent. Null where the
        /// world's SeasonRules are 0 or the file isn't here this session.</summary>
        public SeasonData Seasons { get; private set; }

        IServerNetworkChannel seasonServer;
        IClientNetworkChannel seasonClient;
        /// <summary>The tiles each player was sent this session, by player UID.</summary>
        readonly ConcurrentDictionary<string, HashSet<int>> seasonSent = new();
        /// <summary>Why the server has no layer this session (told to whoever can act on it), or null.</summary>
        string seasonsMissing;

        void StartSeasonsServer(ICoreServerAPI api)
        {
            seasonServer = api.Network.RegisterChannel(SeasonChannelName).RegisterMessageType<SeasonTile>();
            api.Event.PlayerDisconnect += plr =>
            {
                seasonSent.TryRemove(plr.PlayerUID, out _);
                SizeSeasonCache(api, plr);
            };
            api.Event.PlayerNowPlaying += plr =>
            {
                SizeSeasonCache(api, null);
                if (seasonsMissing != null && plr.HasPrivilege(Privilege.controlserver))
                    plr.SendMessage(GlobalConstants.GeneralChatGroup, "Earth seasons: " + seasonsMissing, EnumChatType.Notification);
            };
            api.Event.RegisterGameTickListener(_ => PushSeasonTiles(api), SeasonPushMs, SeasonPushMs);
        }

        void StartSeasonsClient(ICoreClientAPI api)
        {
            seasonClient = api.Network.RegisterChannel(SeasonChannelName)
                .RegisterMessageType<SeasonTile>()
                .SetMessageHandler<SeasonTile>(OnSeasonTile);
        }

        /// <summary>
        /// The server, once the world's params and model are ready: the layer for a world whose SeasonRules read it. The file is
        /// fetched (from earth.json's DataRepo, as the data store's files) when it is missing or doesn't check out; where it
        /// can't be had, the world runs on the old shape this session and says so.
        /// </summary>
        void LoadSeasons(EarthParams p)
        {
            Seasons?.Dispose();
            Seasons = null;
            seasonsMissing = null;
            if (p.SeasonRules < 1) return;
            if (!SeasonData.Published)
            {
                seasonsMissing = "this Earth build carries no published monthly temperature file yet; this world uses the old season " +
                    "shape until a build that has it.";
                Mod.Logger.Warning("Earth seasons: {0}", seasonsMissing);
                return;
            }
            string store = EarthGround.DataFolder;
            (DataTransport.SetResult result, string why) got;
            try
            {
                got = Task.Run(() => SeasonData.EnsureAsync(store, Config?.DataRepo,
                    m => { Mod.Logger.Notification("Earth seasons: {0}", m); Mod.Logger.StoryEvent("Earth seasons: " + m); }, Stopping)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
            {
                got = (DataTransport.SetResult.Failed, e.Message);
            }
            if (got.result != DataTransport.SetResult.Here)
            {
                seasonsMissing = "the monthly temperature file " + SeasonData.FileName + " can't be had (" + (got.why ?? "see the server log") +
                    "); this world uses the old season shape until it is here, and the server tries again at its next start.";
                Mod.Logger.Warning("Earth seasons: {0}", seasonsMissing);
                return;
            }
            try
            {
                Seasons = SeasonData.Open(SeasonData.PathIn(store));
                Mod.Logger.Notification("Earth seasons: monthly temperatures from {0} ({1} tiles of {2} cells); players are sent the tiles around them.",
                    SeasonData.PathIn(store), Seasons.TileCount, Seasons.Tile);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                seasonsMissing = "the monthly temperature file can't be read (" + e.Message + "); this world uses the old season shape this session.";
                Mod.Logger.Error("Earth seasons: {0}", seasonsMissing);
            }
        }

        /// <summary>(Fable's F2) The server's decoded tiles for the players online (<see cref="SeasonData.TilesFor"/>), without
        /// <paramref name="leaving"/>: readings follow the players, and a cache smaller than the tiles they read decodes again
        /// at most readings.</summary>
        void SizeSeasonCache(ICoreServerAPI api, IServerPlayer leaving)
        {
            var s = Seasons;
            if (s == null) return;
            int online = 0;
            foreach (var p in api.World.AllOnlinePlayers) if (p != leaving) online++;
            // the tiles a player's loaded land can touch, at this world's scale and the server's largest view distance
            var g = Model?.Geometry;
            double tileDegrees = s.Tile / (double)SeasonData.Res;
            int perPlayer = g == null ? 4 : SeasonData.TilesPerPlayer(g.BlocksPerDegreeX * tileDegrees,
                g.BlocksPerDegreeZ * tileDegrees, api.Server.Config.MaxChunkRadius);
            s.CachedTiles = SeasonData.TilesFor(online, perPlayer);
        }

        /// <summary>The server, every <see cref="SeasonPushMs"/>: each playing player gets the tiles around them not yet sent.</summary>
        void PushSeasonTiles(ICoreServerAPI api)
        {
            var s = Seasons;
            var m = Model;
            if (s == null || m == null || seasonServer == null) return;
            int across = s.TilesAcross, down = s.TileCount / across;
            foreach (var player in api.World.AllOnlinePlayers)
            {
                if (player is not IServerPlayer plr || plr.ConnectionState != EnumClientState.Playing || plr.Entity == null) continue;
                var pos = plr.Entity.Pos;
                var g = m.Geometry.ToGeo(pos.X, pos.Z);
                int key = s.KeyAt(g.Lat, g.Lon), ty = key / across, tx = key % across;
                var sent = seasonSent.GetOrAdd(plr.PlayerUID, _ => new HashSet<int>());
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = ty + dy;
                    if (y < 0 || y >= down) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = ((tx + dx) % across + across) % across, k = y * across + x;
                        if (!sent.Add(k)) continue;
                        byte[] blob;
                        try { blob = s.TileBlob(k); }
                        catch (Exception e) when (e is IOException or InvalidDataException)
                        {
                            sent.Remove(k);
                            Mod.Logger.Warning("Earth seasons: tile {0} can't be read for {1} ({2}).", k, plr.PlayerName, e.Message);
                            continue;
                        }
                        seasonServer.SendPacket(new SeasonTile { Tile = s.Tile, Key = k, Blob = blob }, plr);
                    }
                }
            }
        }

        /// <summary>A player's game: a tile from the server, for a world whose SeasonRules read the layer.</summary>
        void OnSeasonTile(SeasonTile t)
        {
            var p = Params;
            if (p == null || !p.Enabled || p.SeasonRules < 1 || t == null) return;
            try
            {
                if (Seasons == null || Seasons.Tile != t.Tile)
                {
                    Seasons = SeasonData.ForTiles(t.Tile);
                    // once a session: whether a player's game gets the server's tiles is otherwise invisible
                    Mod.Logger.Notification("Earth seasons: the server sends the monthly temperature tiles around this player (first tile {0}, {1:N0} bytes).",
                        t.Key, t.Blob?.Length ?? 0);
                }
            }
            catch (InvalidDataException e)
            {
                Mod.Logger.Warning("Earth seasons: the server's tiles can't be used ({0}); the old season shape stays.", e.Message);
                return;
            }
            Seasons.AddTile(t.Key, t.Blob ?? Array.Empty<byte>());
        }

        /// <summary>A player's game: a new world's params (or none) start from no tiles.</summary>
        void ResetClientSeasons() => Seasons = null;
    }

    /// <summary>One tile of the monthly temperature layer as the server's file holds it (coded), for a player's game.</summary>
    [ProtoContract]
    public class SeasonTile
    {
        /// <summary>Cells along a tile's side in the server's file.</summary>
        [ProtoMember(1)] public int Tile;
        /// <summary>The tile's key: row of tiles from the north times tiles across, plus the column from 180°W.</summary>
        [ProtoMember(2)] public int Key;
        /// <summary>The tile's bytes as the file stores them; empty for a tile without data.</summary>
        [ProtoMember(3)] public byte[] Blob;
    }
}
