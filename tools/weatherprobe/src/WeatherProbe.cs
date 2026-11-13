// A consumer of the open sea contracts' weather role written as a third-party mod would write it (Sea's
// docs/open-sea-contracts.md, sections 3-5): no reference to Earth or Sea, only api.ObjectCache and .NET base types.
// Built as a tiny DLL mod (WeatherProbe.csproj, only VintagestoryAPI referenced). Earth test tool, never released.
//   /wprobe [x] [z] [hours]   server        .wprobe [x] [z] [hours]   client
// Defaults: the block you stand in, the last whole game hour. The same arguments on both sides must give the same
// numbers (R5). It also shows vanilla's wind here now against the role's (section 5: written once, as a vector), who holds
// seacontracts:vanillawind-vector and which Harmony owners transpile vanilla's two X-as-strength readers, and the wind
// Sea's seastate role follows (Sea follows Earth's wind).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace WeatherProbe
{
    public class WeatherProbeSystem : ModSystem
    {
        public override void StartServerSide(ICoreServerAPI api)
        {
            var p = api.ChatCommands.Parsers;
            api.ChatCommands.Create("wprobe")
                .WithDescription("Weather contract probe: the role's answers at x z (default: here) and game hour (default: the last whole one)")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(p.OptionalDouble("x", double.NaN), p.OptionalDouble("z", double.NaN), p.OptionalDouble("hours", double.NaN))
                .HandleWith(a => TextCommandResult.Success(Probe(api, a.Caller.Player?.Entity?.Pos, (double)a[0], (double)a[1], (double)a[2])));
            RivalCommands(api, api.ChatCommands);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            var p = api.ChatCommands.Parsers;
            api.ChatCommands.Create("wprobe")
                .WithDescription("Weather contract probe (client side)")
                .WithArgs(p.OptionalDouble("x", double.NaN), p.OptionalDouble("z", double.NaN), p.OptionalDouble("hours", double.NaN))
                .HandleWith(a => TextCommandResult.Success(Probe(api, api.World.Player?.Entity?.Pos, (double)a[0], (double)a[1], (double)a[2])));
            RivalCommands(api, api.ChatCommands);
        }

        /// <summary>
        /// R2 checks: wprobetake P puts a rival entry (owner "weatherprobe", priority P, no members) in the role's place;
        /// wprobedrop takes out whatever holds it; wprobewho says who holds it. A world owner (Earth) should put its entry
        /// back within seconds over nothing or a lower priority, and stay out while a priority of 100 or more holds it.
        /// </summary>
        static void RivalCommands(ICoreAPI api, IChatCommandApi cc)
        {
            const string key = "seacontracts:weather";
            cc.Create("wprobetake").WithDescription("Put a rival weather entry of priority P").RequiresPrivilege(Privilege.controlserver)
                .WithArgs(cc.Parsers.Int("priority"))
                .HandleWith(a =>
                {
                    lock (api.ObjectCache) api.ObjectCache[key] = new Dictionary<string, object> { ["owner"] = "weatherprobe", ["version"] = 1, ["priority"] = (int)a[0] };
                    return TextCommandResult.Success($"{api.Side}: weatherprobe holds {key} at priority {(int)a[0]}");
                });
            cc.Create("wprobedrop").WithDescription("Take the weather entry out").RequiresPrivilege(Privilege.controlserver)
                .HandleWith(a =>
                {
                    lock (api.ObjectCache) api.ObjectCache.Remove(key);
                    return TextCommandResult.Success($"{api.Side}: {key} removed");
                });
            cc.Create("wprobewho").WithDescription("Who holds the weather entry").RequiresPrivilege(Privilege.chat)
                .HandleWith(a =>
                {
                    IDictionary<string, object> d;
                    lock (api.ObjectCache) d = api.ObjectCache.TryGetValue(key, out var o) ? o as IDictionary<string, object> : null;
                    return TextCommandResult.Success(d == null ? $"{api.Side}: nobody holds {key}"
                        : $"{api.Side}: {key} held by {(d.TryGetValue("owner", out var w) ? w : "?")} at priority {(d.TryGetValue("priority", out var p) ? p : "?")}");
                });
        }

        static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

        static bool BaseTypesOnly(Type t)
        {
            if (t.Assembly != typeof(object).Assembly) return false;
            return !t.IsGenericType || t.GetGenericArguments().All(BaseTypesOnly);
        }

        static string Probe(ICoreAPI api, EntityPos pos, double x, double z, double hours)
        {
            if (!double.IsFinite(x)) x = pos == null ? 0 : Math.Floor(pos.X) + 0.5;
            if (!double.IsFinite(z)) z = pos == null ? 0 : Math.Floor(pos.Z) + 0.5;
            if (!double.IsFinite(hours)) hours = Math.Floor(api.World.Calendar.TotalHours);
            var sb = new StringBuilder();
            sb.Append(FormattableString.Invariant($"wprobe {api.Side} at ({x:0.###}, {z:0.###}), hour {hours:0.###}:"));

            IDictionary<string, object> d;
            object vec;
            lock (api.ObjectCache)
            {
                d = api.ObjectCache.TryGetValue("seacontracts:weather", out var o) ? o as IDictionary<string, object> : null;
                api.ObjectCache.TryGetValue("seacontracts:vanillawind-vector", out vec);
            }
            if (d == null) return sb.Append(" no seacontracts:weather entry").ToString();

            var foreign = d.Where(kv => kv.Value == null || !BaseTypesOnly(kv.Value.GetType())).Select(kv => kv.Key).ToList();
            object Get(string k) => d.TryGetValue(k, out var v) ? v : null;
            var revision = Get("revision") as System.Func<long>;
            sb.Append($"\n role: owner {Get("owner")}, version {Get("version")}, priority {Get("priority")}, revision {(revision != null ? revision().ToString() : "-")}, writesVanilla {Get("writesVanilla")}; members {string.Join(" ", d.Keys)}; base types only: {(foreign.Count == 0 ? "yes" : "NO: " + string.Join(" ", foreign))}");

            var wind = Get("wind") as System.Func<double, double, double, bool, (double X, double Z)>;
            var prevailing = Get("prevailing") as System.Func<double, double, double, (double X, double Z)>;
            var storm = Get("storm") as System.Func<double, double, double, (double Presence, double WindMps, string Kind)>;
            var rain = Get("rain") as System.Func<double, double, double, (double Cloudness, double Precipitation)>;
            if (wind == null || storm == null || rain == null) return sb.Append("\n a member is missing or not of the contract's type").ToString();
            var steady = wind(x, z, hours, false);
            var gusty = wind(x, z, hours, true);
            sb.Append($"\n wind steady ({F(steady.X)}, {F(steady.Z)}) m/s, gusts ({F(gusty.X)}, {F(gusty.Z)}) m/s");
            if (prevailing != null) { var pv = prevailing(x, z, hours); sb.Append($", prevailing ({F(pv.X)}, {F(pv.Z)}) m/s"); }
            var s = storm(x, z, hours);
            var r = rain(x, z, hours);
            sb.Append($"\n storm presence {F(s.Presence)}, wind {F(s.WindMps)} m/s, kind {s.Kind ?? "none"}; rain cloudness {F(r.Cloudness)}, precipitation {F(r.Precipitation)}");
            sb.Append($"\n R7 (no answer as NaN): {(new[] { steady.X, steady.Z, gusty.X, gusty.Z, s.Presence, s.WindMps, r.Cloudness, r.Precipitation }.All(double.IsFinite) ? "all finite" : "some NaN")}");

            // Sea follows the weather owner's wind: its sea-state role's wind at the same place and hour
            IDictionary<string, object> sea;
            lock (api.ObjectCache) sea = api.ObjectCache.TryGetValue("seacontracts:seastate", out var so) ? so as IDictionary<string, object> : null;
            if (sea != null && (sea.TryGetValue("at", out var at) ? at : null) is System.Func<double, double, double, (double Height, double Period, double DirX, double DirZ, double WindX, double WindZ, double Storm)> seaAt)
            {
                var ss = seaAt(x, z, hours);
                double off = Math.Min(Math.Sqrt(Sq(ss.WindX - gusty.X) + Sq(ss.WindZ - gusty.Z)), Math.Sqrt(Sq(ss.WindX - steady.X) + Sq(ss.WindZ - steady.Z)));
                sb.Append($"\n Sea (seastate.at, owner {(sea.TryGetValue("owner", out var so2) ? so2 : "?")}): wind ({F(ss.WindX)}, {F(ss.WindZ)}) m/s, {F(off)} m/s from the role's; waves {F(ss.Height)} m, {F(ss.Period)} s, storm {F(ss.Storm)}");
            }
            else sb.Append("\n no seacontracts:seastate (Sea not installed?)");

            // vanilla's wind here now (section 5): the vector's direction and length against the role's gusty wind now / 25
            double now = api.World.Calendar.TotalHours;
            var wn = wind(x, z, now, true);
            var v = api.World.BlockAccessor.GetWindSpeedAt(new Vec3d(x, api.World.SeaLevel, z));
            double vl = Math.Sqrt(v.X * v.X + v.Z * v.Z), wl = Math.Sqrt(wn.X * wn.X + wn.Z * wn.Z) / 25;
            double angle = vl > 1e-9 && wl > 1e-9 ? Math.Acos(Math.Clamp((v.X * wn.X + v.Z * wn.Z) / 25 / (vl * wl), -1, 1)) * 180 / Math.PI : double.NaN;
            sb.Append($"\n vanilla wind at sea level now: ({F(v.X)}, {F(v.Z)}) length {F(vl)}; role now / 25: ({F(wn.X / 25)}, {F(wn.Z / 25)}) length {F(wl)}; {angle.ToString("0.00", CultureInfo.InvariantCulture)}° apart");
            if (api.Side == EnumAppSide.Client)
            {
                var c = GlobalConstants.CurrentWindSpeedClient;
                sb.Append($"\n CurrentWindSpeedClient ({F(c.X)}, {F(c.Z)}) length {F(Math.Sqrt(c.X * c.X + c.Z * c.Z))}");
            }
            sb.Append($"\n seacontracts:vanillawind-vector = {vec ?? "(none)"}; X readers transpiled by: shader {Transpilers("DefaultShaderUniforms", "Update")}, boat {Transpilers("EntityBoat", "OnRenderFrame")}");
            return sb.ToString();
        }

        static double Sq(double a) => a * a;

        /// <summary>The Harmony owners that transpile a vanilla method, found by reflection (the probe references no Harmony).</summary>
        static string Transpilers(string typeName, string methodName)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var type = assemblies.SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.Name == typeName && (t.Namespace ?? "").StartsWith("Vintagestory"));
                var method = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == methodName);
                if (method == null) return "(method not found)";
                var harmony = assemblies.Select(a => a.GetType("HarmonyLib.Harmony", false)).FirstOrDefault(t => t != null);
                var info = harmony?.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { method });
                if (info == null) return "none";
                var list = info.GetType().GetField("Transpilers")?.GetValue(info) ?? info.GetType().GetProperty("Transpilers")?.GetValue(info);
                var owners = new List<string>();
                foreach (var patch in (list as IEnumerable) ?? Array.Empty<object>())
                    owners.Add((patch.GetType().GetField("owner")?.GetValue(patch) ?? patch.GetType().GetProperty("owner")?.GetValue(patch))?.ToString() ?? "?");
                return owners.Count == 0 ? "none" : string.Join("+", owners);
            }
            catch (Exception e) { return "(" + e.GetType().Name + ")"; }
        }
    }
}
