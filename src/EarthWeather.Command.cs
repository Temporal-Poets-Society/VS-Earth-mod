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
    /// EarthWeather, /earthweather: the weather at a place and the role's state, for admins (EA-0089: split from EarthWeather.cs,
    /// no change).
    /// </summary>
    public partial class EarthWeather
    {
        // ------------------------------------------------------------------------------------------ commands

        void RegisterCommands(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("earthweather")
                .WithAlias("ew")
                .WithDescription("Earth's weather here: the month's climate, rain, clouds, wind, pressure, storms and thunder; 'forecast' for the next day, 'storms' for the storms now, 'thunder' for the nearest thunderstorm, 'fog' for the next fog here, 'hail' for the next hail here, 'map' for the rain and clouds around")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .WithArgs(parsers.OptionalWord("what"))
                .HandleWith(CmdWeather);
        }

        /// <summary>The players with a hail search running (D-L10).</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> hailSearches = new();

        /// <summary>When hail next falls at (x, z), a quarter-hour at a time over the next game year, and the nearest hail within
        /// 3000 blocks now (/earthweather hail; on a worker thread).</summary>
        static string HailReport(WeatherModel m, double px, double pz, double hours, double metres)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            int year = (int)Math.Ceiling(m.Settings.DaysPerYear * m.Settings.HoursPerDay * 4);
            sb.Append("Hail here in the next year (from the quarter-hour on):");
            int found = 0;
            bool was = false;
            for (int k = 1; k <= year && found < 6; k++)
            {
                double h = m.HailAt(px, pz, hours + k / 4.0, metres);
                bool on = h >= HailFrom;
                if (on && !was)
                {
                    found++;
                    sb.Append(string.Format(ci, "\n +{0:0.##}h: {1:0}%", k / 4.0, h * 100));
                }
                was = on;
            }
            if (found == 0) sb.Append(" none.");
            double best = 0, bx = 0, bz = 0;
            for (int ring = 1; ring <= 12; ring++)
                for (int k = 0; k < ring * 8; k++)
                {
                    double a = k * 2 * Math.PI / (ring * 8), x = px + ring * 250 * Math.Cos(a), z = pz + ring * 250 * Math.Sin(a);
                    double h = m.HailAt(x, z, hours, metres);
                    if (h > best) { best = h; bx = x; bz = z; }
                }
            if (best >= HailFrom)
            {
                double dx = bx - px, dz = bz - pz;
                sb.Append(string.Format(ci, "\nHail now {0:0} blocks {1} (={2:0} ={3:0}), {4:0}%.", Math.Sqrt(dx * dx + dz * dz),
                    WeatherWind.CompassName(WeatherWind.Bearing(dx, dz)), bx, bz, best * 100));
            }
            else sb.Append("\nNo hail within 3000 blocks now.");
            return sb.ToString();
        }

        TextCommandResult CmdWeather(TextCommandCallingArgs args)
        {
            var m = Model;
            if (m == null) return TextCommandResult.Success("Earth's weather is off in this world (vanilla weather).");
            var pos = args.Caller.Player.Entity.Pos;
            double hours = HoursNow(api.World, m);
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
            if (what == "fog")
            {
                // (rules 7) when there is fog here in the next 30 days, hour by hour (for looking at it: /time add N hours)
                if (!m.Settings.Has.Fog) return TextCommandResult.Success("Earth's fog needs weather rules 7; this world has v" + m.Settings.Rules + " (the patterns' fog).");
                double metres = GroundMetres(api, pos.Y);
                sb.Append("Fog here in the next 30 days (from the hour on):");
                int found = 0;
                bool was = false;
                for (int k = 1; k <= 30 * 24 && found < 8; k++)
                {
                    var f = m.FogAt(pos.X, pos.Z, hours + k, metres);
                    bool on = f.Ground >= 0.5;
                    if (on && !was)
                    {
                        found++;
                        sb.Append(string.Format(ci, "\n +{0}h: {1} {2:0}%", k, f.Sea ? "sea fog" : "ground fog", f.Ground * 100));
                    }
                    was = on;
                }
                if (found == 0) sb.Append(" none.");
                return TextCommandResult.Success(sb.ToString());
            }
            if (what == "hail")
            {
                // (rules 6, EA-0019) hail is rare (a few percent of a storm's time): when it next falls here, a quarter-hour at a
                // time over the next game year, and the nearest hail within 3000 blocks now (for looking at it: /time add N hours)
                if (!m.Settings.Has.Hail) return TextCommandResult.Success("Earth's hail needs weather rules 6; this world has v" + m.Settings.Rules + " (vanilla's hail events).");
                // (D-L10, the Fable pass) the year's search is up to 35,000 samples (5-64 ms): on a worker thread, the answer
                // sent when it is done, one search per player at a time (the model is safe on any thread)
                if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Only a player can ask for the hail here.");
                if (!hailSearches.TryAdd(player.PlayerUID, 0)) return TextCommandResult.Success("Still looking for hail from your last /earthweather hail.");
                double metres = GroundMetres(api, pos.Y), px = pos.X, pz = pos.Z;
                int group = args.Caller.FromChatGroupId;
                // (EA-0093) what the game's own hail damage reads here (EntityBehaviorHealth: hail, particle size 0.5 or more,
                // a hit with a chance of the level / 2 each second, only at or above the rain map); read here, on the main thread
                var vw = api.ModLoader.GetModSystem<WeatherSystemServer>();
                var ps = vw?.GetPrecipitationState(pos.XYZ);
                string game = ps == null ? "" : string.Format(ci, "\nThe game's precipitation here: {0}, level {1:0.00}, particle size {2:0.00}; rain map at y {3} (you at {4:0.0}).",
                    ps.Type, ps.Level, ps.ParticleSize, api.World.BlockAccessor.GetRainMapHeightAt((int)pos.X, (int)pos.Z), pos.Y);
                var events = api.Event;
                System.Threading.Tasks.Task.Run(() =>
                {
                    string text;
                    try { text = HailReport(m, px, pz, hours, metres) + game; }
                    catch (Exception e) { text = "Earth's hail search failed: " + e.Message; }
                    events.EnqueueMainThreadTask(() =>
                    {
                        hailSearches.TryRemove(player.PlayerUID, out _);
                        player.SendMessage(group, text, EnumChatType.CommandSuccess);
                    }, "earth-weather-hail");
                });
                return TextCommandResult.Success("Looking for hail here over the next year...");
            }
            if (what == "map")
            {
                // the rain and the clouds around (north up): # raining, + overcast, - broken, . fair, space clear; @ here
                const int Half = 12, Rows = 8;
                double step = 2 * api.World.BlockAccessor.RegionSize / 8.0;
                sb.Append(string.Format(ci, "Rain and clouds around, {0:0} blocks a character, north up (# rain, + overcast, - broken, . fair, blank clear):", step));
                for (int r = -Rows; r <= Rows; r++)
                {
                    sb.Append('\n');
                    for (int c = -Half; c <= Half; c++)
                    {
                        if (r == 0 && c == 0) { sb.Append('@'); continue; }
                        var f = m.Sample(pos.X + c * step, pos.Z + r * step, hours);
                        sb.Append(f.Cloudness > 0.5 ? '#' : f.CloudCover > 0.75 ? '+' : f.CloudCover > 0.45 ? '-' : f.CloudCover > 0.15 ? '.' : ' ');
                    }
                }
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
            if (m.Settings.Has.Hail)
            {
                // (rules 6) the hail and the weather's own warmth or chill at the player's height
                double metres = GroundMetres(api, pos.Y), hail = m.HailAt(pos.X, pos.Z, hours, metres), dt = m.TemperatureAnomaly(pos.X, pos.Z, hours, metres);
                sb.Append(string.Format(ci, "\nHail: {0}. The weather makes it {1:0.0} °C {2} than the season and the hour alone; the month's cloud cover {3:0}%.",
                    hail >= HailFrom ? string.Format(ci, "falling now ({0:0}%)", hail * 100) : s0.Thunder > 0 ? "none from this storm now" : "none",
                    Math.Abs(dt), dt >= 0 ? "warmer" : "colder", m.MonthCover(pos.X, pos.Z, hours) * 100));
            }
            if (m.Settings.Has.Fog)
            {
                // (rules 7) Earth's fog here (the client draws its weather regions' fog at their centres)
                var fog = m.FogAt(pos.X, pos.Z, hours, GroundMetres(api, pos.Y));
                sb.Append(string.Format(ci, "\nFog: {0}; the air {1:0}% thick. Nights here last {2:0.0} h.",
                    fog.Ground < 0.05 ? "none" : string.Format(ci, "{0} {1:0}%", fog.Sea ? "sea fog" : "ground fog", fog.Ground * 100), fog.Air * 100,
                    WeatherModel.NightHours(s0.Lat, s0.YearRel)));
            }
            sb.Append($"\nWeather rules v{m.Settings.Rules} (this build v{RulesVersion}). Wind: {(ControlsWind ? "Earth's" : "an older Sea's own")}; Sea: {SeaLink}; weather role ({WeatherContract.Key}): {RoleStatus}, revision {Revision}; patches: {string.Join(", ", Active)}.");
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>The weather in words; snow or rain by <paramref name="temperature"/>, °C at the place and hour (NaN, where the
        /// climate can't be read: the month's sea-level normal).</summary>
        static string Describe(WeatherSample s, double temperature)
        {
            double p = s.Precipitation;
            if (p <= 0) return s.Thunder > 0 ? "thunder, no rain reaching the ground here" : s.Cloudness > 0.25 ? "dry, rain clouds gathering" : "dry";
            string kind = (double.IsNaN(temperature) ? s.NormalTemp : temperature) < 0 ? "snow" : "rain";
            return (p < 0.15 ? "light " : p < 0.5 ? "" : "heavy ") + kind + (s.Thunder > 0 ? " with thunder" : "");
        }
    }
}
