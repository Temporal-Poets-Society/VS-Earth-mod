using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Offline checks of Earth's sky longitude (EarthGeometry.SkyLonAt, SkyLongitude) and of handing it to AstraTerra by
    /// reflection (LongitudeProviderBinding), against fakes, the local AstraTerra branch build (with SetLongitudeProvider)
    /// and the installed AstraTerra 0.15.0 package (without it; read from the zip in memory, nothing extracted).
    ///   dotnet run -c Release -- [repoRoot] [branchAstraTerra.dll] [AstraTerra-0.15.0.zip]
    /// A missing AstraTerra build or package is reported as SKIP, not as a failure.
    /// </summary>
    static class Program
    {
        static int failures, skipped;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static void Skip(string what)
        {
            Console.WriteLine("  SKIP " + what);
            skipped++;
        }

        static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : FindRoot();
            string branchDll = args.Length > 1 ? args[1]
                : Path.GetFullPath(Path.Combine(root, "..", "AstraTerra", "src", "AstraTerra", "bin", "Release", "net10.0", "AstraTerra.dll"));
            string releaseZip = args.Length > 2 ? args[2]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData", "Mods", "AstraTerra-0.15.0.zip");
            Console.WriteLine("runtime " + Environment.Version);

            var data = EarthData.LoadFromFolder(root, false);
            var geo = LiveWorld(data);
            CheckLayout(geo);
            CheckSkyAgainstMap(geo);
            CheckGutterAndWrap(geo);
            CheckService(geo);
            CheckBindingFakes();
            CheckSuggestionFakes();
            CheckBranch(branchDll, geo);
            CheckBranchLook(branchDll);
            CheckRelease(releaseZip);

            Console.WriteLine((failures == 0 ? "all checks passed" : failures + " check(s) failed") + (skipped > 0 ? $", {skipped} skipped" : ""));
            return failures == 0 ? 0 : 1;
        }

        static string FindRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "modinfo.json"))) d = d.Parent;
            return d?.FullName ?? throw new Exception("repo root not found; pass it as the first argument");
        }

        /// <summary>The live world's layout (server-main.log, 17 October): Cylinder World 256000 wide, ring 204800 from x=25600,
        /// seam band half-width 26272 (672 beyond the jump line), latitudes of a 102400-long world.</summary>
        static EarthGeometry LiveWorld(EarthData data)
        {
            var inp = new LayoutInput
            {
                WorldWidth = 256000,
                CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = 51200, CwPoleDistance = 51200 - 48, CwBandInner = 26272 - 25600,
            };
            return EarthLayout.Build(inp, data, out _);
        }

        static double Z(EarthGeometry g, double lat) => g.EquatorZ - lat * g.BlocksPerDegreeZ;

        static void CheckLayout(EarthGeometry g)
        {
            Console.WriteLine("live-world layout");
            Check(g.Gutter == 2368, $"seam gutter {g.Gutter} blocks");
            Check(Math.Abs(g.BlocksPerDegreeX - 562.31) < 0.01, $"{g.BlocksPerDegreeX:0.000} blocks per degree east-west (log: 562.3)");
            Check(Math.Abs(g.SeamLon0 - EarthLayout.SeamLon) < 1e-12 && Math.Abs(g.CenterLon - 10.75) < 1e-9,
                $"cut along {g.SeamLon0:0.00}°, world centre meridian {g.CenterLon:0.000}°");
            g.FromGeo(38.7, -9.15, out double x, out double z);
            var map = g.ToGeo(x, z);
            Check(Math.Abs(map.Lat - 38.7) < 1e-9 && Math.Abs(map.Lon - (-9.15)) < 1e-9, $"Lisbon at x={x:0}, z={z:0} and back ({map.Lat:0.0000}°, {map.Lon:0.0000}°)");
            Check(Math.Abs(g.SkyLonAt(x) - map.Lon) < 1e-9, "  ... with the sky's longitude there the same");
        }

        /// <summary>The sky's meridian is the map's longitude everywhere: every meridian is a straight X column.</summary>
        static void CheckSkyAgainstMap(EarthGeometry g)
        {
            Console.WriteLine("sky meridian against the map's longitude");
            double[] lats = { -89, -78, -60, -33.9, 0, 21, 38.7, 51.5, 60, 63.3, 65.8, 70, 78, 89 };
            int samples = 0;
            double worst = 0;
            for (double x = g.RingStart; x < g.RingStart + g.RingLength; x += 37)
            {
                double sky = g.SkyLonAt(x);
                foreach (double lat in lats)
                {
                    worst = Math.Max(worst, Math.Abs(Geo.Wrap180(sky - g.ToGeo(x, Z(g, lat)).Lon)));
                    samples++;
                }
            }
            Check(samples > 70000 && worst < 1e-9, $"{samples} samples over the ring, the Bering seam and the poles included, equal the map's longitude (worst {worst:E1}°)");
            double roundTrip = 0;
            foreach (double lat in lats)
                for (double lon = -180; lon < 180; lon += 0.37)
                {
                    g.FromGeo(lat, lon, out double x, out double z);
                    roundTrip = Math.Max(roundTrip, Math.Abs(Geo.Wrap180(g.ToGeo(x, z).Lon - lon)));
                }
            Check(roundTrip < 1e-9, $"FromGeo then ToGeo gives every longitude back (worst {roundTrip:E1}°)");
        }

        static void CheckGutterAndWrap(EarthGeometry g)
        {
            Console.WriteLine("gutter, jump line and wrap");
            double half = g.Gutter / 2, seam = Geo.Wrap180(g.SeamLon0);
            bool gutterSeam = true;
            foreach (double x in new[] { g.RingStart, g.RingStart + 1, g.RingStart + half - 1, g.RingStart + g.RingLength - 1, g.RingStart + g.RingLength - half + 1 })
                gutterSeam &= Math.Abs(Geo.Wrap180(g.SkyLonAt(x) - seam)) < 1e-9;
            Check(gutterSeam, $"the whole gutter has the seam's meridian ({seam:0.000}°)");
            bool wraps = true;
            foreach (double x in new[] { 30000.0, 114898, 200000, 230399 })
                wraps &= Math.Abs(Geo.Wrap180(g.SkyLonAt(x + g.RingLength) - g.SkyLonAt(x))) < 1e-9
                         && Math.Abs(Geo.Wrap180(g.SkyLonAt(x - g.RingLength) - g.SkyLonAt(x))) < 1e-9;
            Check(wraps, "one ring east or west gives the same longitude");
            // one block at a time over the whole ring and across both jump lines: never a jump, never westwards
            double maxStep = 0, minStep = double.MaxValue;
            double prev = g.SkyLonAt(g.RingStart - 3000);
            for (double x = g.RingStart - 2999; x <= g.RingStart + g.RingLength + 3000; x += 1)
            {
                double cur = g.SkyLonAt(x);
                double step = Geo.Wrap180(cur - prev);
                maxStep = Math.Max(maxStep, step);
                minStep = Math.Min(minStep, step);
                prev = cur;
            }
            Check(maxStep <= 1 / g.BlocksPerDegreeX + 1e-9 && minStep >= -1e-12,
                $"block to block the sky moves 0..{maxStep:0.000000}° east (1/{g.BlocksPerDegreeX:0.0} = {1 / g.BlocksPerDegreeX:0.000000}°), across the jump lines too");
        }

        static void CheckService(EarthGeometry g)
        {
            Console.WriteLine("longitude service (EarthMapSystem.LongitudeAt)");
            var s = new SkyLongitude();
            Check(s.At(114898) == null && !s.Active, "null before Earth is active in the world");
            s.Set(g);
            Check(s.At(114898) is double v && Math.Abs(v - g.SkyLonAt(114898)) < 1e-12, "the sky meridian once the layout is set");
            Check(s.At(double.NaN) == null && s.At(double.PositiveInfinity) == null, "null for an X that isn't a number");
            var broken = new EarthGeometry { RingStart = 0, RingLength = 1000, Gutter = 1000, Wraps = true };
            s.Set(broken);
            Check(s.At(10) == null, "null (no opinion) for a layout that holds no Earth, never NaN");
            s.Set(null);
            Check(s.At(114898) == null && !s.Active, "null again after the world unloads");
            s.Set(g);
            int wrong = 0;
            System.Threading.Tasks.Parallel.For(0, 200_000, i =>
            {
                double x = 25600 + i * 1.01;
                if (s.At(x) != g.SkyLonAt(x)) System.Threading.Interlocked.Increment(ref wrong);
            });
            Check(wrong == 0, "200,000 calls from many threads at once all answer correctly");
        }

        // ------------------------------------------------------------------------------------------ binding

        sealed class WithMethod
        {
            public Func<double, double?> Provider;
            public int Calls;
            public void SetLongitudeProvider(Func<double, double?> provider) { Provider = provider; Calls++; }
        }

        sealed class WithoutMethod { public void SetLatitudeProvider(Func<double, double?> provider) { } }
        sealed class WrongSignature { public void SetLongitudeProvider(Func<double, double> provider) => throw new Exception("must not be called"); }
        sealed class StaticOnly { public static void SetLongitudeProvider(Func<double, double?> provider) => throw new Exception("must not be called"); }
        sealed class Throws { public void SetLongitudeProvider(Func<double, double?> provider) => throw new InvalidOperationException("not started on this side"); }

        sealed class TakesSuggestions
        {
            public readonly Dictionary<string, string> Taken = new();
            public bool SuggestSetting(string name, string value)
            {
                if (name == "Unknown") throw new ArgumentException("no such setting");
                if (name == "PlayerSet") return false;
                Taken[name] = value;
                return true;
            }
        }

        sealed class SuggestionVoid { public void SuggestSetting(string name, string value) => throw new Exception("must not be called"); }

        static void CheckSuggestionFakes()
        {
            Console.WriteLine("setting suggestions (fakes)");
            var ok = new TakesSuggestions();
            Check(SettingSuggestionBinding.TrySuggest(ok, "SunArt", "pixel", out _) == SettingSuggestionBinding.Result.Applied && ok.Taken["SunArt"] == "pixel",
                "a bool SuggestSetting(string, string) takes the suggestion");
            Check(SettingSuggestionBinding.TrySuggest(ok, "PlayerSet", "1", out _) == SettingSuggestionBinding.Result.Kept, "false (the player's own setting): Kept");
            var r = SettingSuggestionBinding.TrySuggest(ok, "Unknown", "1", out string error);
            Check(r == SettingSuggestionBinding.Result.Failed && error.Contains("ArgumentException"), $"a method that throws: Failed, not thrown ({error})");
            Check(SettingSuggestionBinding.TrySuggest(new SuggestionVoid(), "SunArt", "pixel", out _) == SettingSuggestionBinding.Result.NoMethod,
                "a SuggestSetting returning nothing is not called");
            Check(SettingSuggestionBinding.TrySuggest(new WithMethod(), "SunArt", "pixel", out _) == SettingSuggestionBinding.Result.NoMethod, "no such method: NoMethod");
            Check(SettingSuggestionBinding.TrySuggest(null, "SunArt", "pixel", out _) == SettingSuggestionBinding.Result.NoMethod, "no target: NoMethod");
        }

        static void CheckBindingFakes()
        {
            Console.WriteLine("reflection binding (fakes)");
            Func<double, double?> provider = x => x / 1000;
            var ok = new WithMethod();
            Check(LongitudeProviderBinding.TrySet(ok, provider, out _) == LongitudeProviderBinding.Result.Bound && ok.Provider == provider,
                "a SetLongitudeProvider(Func<double, double?>) receives the provider");
            Check(LongitudeProviderBinding.TrySet(ok, null, out _) == LongitudeProviderBinding.Result.Bound && ok.Provider == null && ok.Calls == 2,
                "null clears it");
            Check(LongitudeProviderBinding.TrySet(new WithoutMethod(), provider, out _) == LongitudeProviderBinding.Result.NoMethod, "no such method: NoMethod");
            Check(LongitudeProviderBinding.TrySet(new WrongSignature(), provider, out _) == LongitudeProviderBinding.Result.NoMethod, "another signature is not called");
            Check(LongitudeProviderBinding.TrySet(new StaticOnly(), provider, out _) == LongitudeProviderBinding.Result.NoMethod, "a static method is not called");
            Check(LongitudeProviderBinding.TrySet(null, provider, out _) == LongitudeProviderBinding.Result.NoMethod, "no target: NoMethod");
            var r = LongitudeProviderBinding.TrySet(new Throws(), provider, out string error);
            Check(r == LongitudeProviderBinding.Result.Failed && error.Contains("InvalidOperationException"), $"a method that throws: Failed, not thrown ({error})");
        }

        /// <summary>Loads one AstraTerra build in its own load context, resolving the game's assemblies from the game folder.</summary>
        sealed class ModContext : AssemblyLoadContext
        {
            readonly string[] dirs;

            public ModContext(string name) : base(name, isCollectible: false)
            {
                string game = Environment.GetEnvironmentVariable("VINTAGE_STORY");
                if (string.IsNullOrEmpty(game)) game = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory");
                dirs = new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") };
            }

            protected override Assembly Load(AssemblyName name)
            {
                foreach (string d in dirs)
                {
                    string p = Path.Combine(d, name.Name + ".dll");
                    if (File.Exists(p)) return LoadFromAssemblyPath(p);
                }
                return null;
            }
        }

        /// <summary>Stands in for the game's ICoreAPI and IWorldAccessor: only ICoreAPI.World answers.</summary>
        public class FakeApi : DispatchProxy
        {
            public object World;

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "get_World") return World;
                if (method.Name == "get_Logger") return DispatchProxy.Create(method.ReturnType, typeof(FakeApi));
                return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }

        static Assembly branch;

        /// <summary>The branch build, loaded once: the longitude and look checks share it and the game's assemblies.</summary>
        static Assembly LoadBranch(string dll) => branch ??= new ModContext("astraterra-branch").LoadFromAssemblyPath(dll);

        static void CheckBranch(string dll, EarthGeometry g)
        {
            Console.WriteLine("AstraTerra branch build (feat/world-longitude-provider): " + dll);
            if (!File.Exists(dll))
            {
                Skip("branch DLL not built; build it with the repo-local SDK (see docs/decisions/0010)");
                return;
            }
            Type type;
            try
            {
                var asm = LoadBranch(dll);
                type = asm.GetType("AstraTerra.AstraTerraModSystem", true);
            }
            catch (Exception e)
            {
                Check(false, "load AstraTerraModSystem: " + e.Message);
                return;
            }
            Check(LongitudeProviderBinding.Find(type) != null, "has SetLongitudeProvider(Func<double, double?>)");

            object system = Activator.CreateInstance(type);
            var r = LongitudeProviderBinding.TrySet(system, x => 0, out string error);
            Check(r == LongitudeProviderBinding.Result.Failed, $"before AstraTerra starts on a side: Failed, nothing thrown ({error})");

            // what AstraTerra's Start leaves behind: its side's api, whose World keys the provider
            var sideApi = type.GetField("sideApi", BindingFlags.NonPublic | BindingFlags.Instance);
            if (sideApi == null)
            {
                Check(false, "AstraTerraModSystem.sideApi not found (the branch changed): cannot run the end-to-end check");
                return;
            }
            var worldType = sideApi.FieldType.GetProperty("World").PropertyType;
            object world = DispatchProxy.Create(worldType, typeof(FakeApi));
            var api = (FakeApi)DispatchProxy.Create(sideApi.FieldType, typeof(FakeApi));
            api.World = world;
            sideApi.SetValue(system, api);

            var service = new SkyLongitude();
            service.Set(g);
            r = LongitudeProviderBinding.TrySet(system, service.At, out error);
            Check(r == LongitudeProviderBinding.Result.Bound, "after start: Earth's provider is bound" + (error != null ? " (" + error + ")" : ""));

            var mapper = type.Assembly.GetType("AstraTerra.Astronomy.LatitudeMapper", true)
                .GetMethod("MapWorldLongitude", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(double), worldType }, null);
            double worst = 0;
            int n = 0;
            foreach (double x in new[] { 114898.0, 25600, 26000, 128000, 200000, 229000, 230400 + 5000, -12345 }.Concat(Enumerable.Range(0, 200).Select(i => 25600 + i * 1023.7)))
            {
                double astra = (double)mapper.Invoke(null, new object[] { x, world });
                worst = Math.Max(worst, Math.Abs(Geo.Wrap180(astra - g.SkyLonAt(x))));
                n++;
            }
            Check(worst < 1e-9, $"AstraTerra's MapWorldLongitude (sun, moon, stars, clock) returns Earth's meridian at {n} X positions (worst {worst:E1}°)");
            g.FromGeo(38.7, -9.15, out double lisbonX, out _);
            double lisbon = (double)mapper.Invoke(null, new object[] { lisbonX, world });
            Check(Math.Abs(lisbon - (-9.15)) < 1e-9, $"  ... Lisbon at {lisbon:0.000}° in AstraTerra");

            var tryGet = type.Assembly.GetType("AstraTerra.Astronomy.WorldLongitude", true).GetMethod("TryGet");
            r = LongitudeProviderBinding.TrySet(system, null, out _);
            var tryArgs = new object[] { world, 114898.0, 0.0 };
            Check(r == LongitudeProviderBinding.Result.Bound && !(bool)tryGet.Invoke(null, tryArgs), "clearing (Earth's Dispose) removes it: AstraTerra falls back to its own mapping");
        }

        /// <summary>Earth's look (SkyLook) suggested to the branch build: every setting taken, a player's own kept, nothing saved.</summary>
        static void CheckBranchLook(string dll)
        {
            Console.WriteLine("Earth's sky look suggested to the AstraTerra branch build");
            if (!File.Exists(dll))
            {
                Skip("branch DLL not built");
                return;
            }
            Assembly asm;
            Type type;
            try
            {
                asm = LoadBranch(dll);
                type = asm.GetType("AstraTerra.AstraTerraModSystem", true);
            }
            catch (Exception e)
            {
                Check(false, "load AstraTerraModSystem: " + e.Message);
                return;
            }
            if (SettingSuggestionBinding.Find(type) == null)
            {
                Check(false, "has bool SuggestSetting(string, string)");
                return;
            }
            Check(true, "has bool SuggestSetting(string, string)");

            object system = Activator.CreateInstance(type);
            var r = SettingSuggestionBinding.TrySuggest(system, "SunArt", "pixel", out string error);
            Check(r == SettingSuggestionBinding.Result.Failed, $"before AstraTerra starts on a side: Failed, nothing thrown ({error})");

            var sideApi = type.GetField("sideApi", BindingFlags.NonPublic | BindingFlags.Instance);
            var configField = type.GetField("config", BindingFlags.NonPublic | BindingFlags.Instance);
            var configType = asm.GetType("AstraTerra.Config.AstraTerraConfig", true);
            if (sideApi == null || configField == null)
            {
                Check(false, "AstraTerraModSystem.sideApi or .config not found (the branch changed): cannot run the end-to-end check");
                return;
            }
            sideApi.SetValue(system, DispatchProxy.Create(sideApi.FieldType, typeof(FakeApi)));

            // a player who never touched the file: everything Earth suggests is taken
            object config = Activator.CreateInstance(configType);
            configField.SetValue(system, config);
            foreach (var s in SkyLook.Settings)
            {
                r = SettingSuggestionBinding.TrySuggest(system, s.Key, s.Value, out error);
                object now = configType.GetProperty(s.Key).GetValue(config);
                string shown = Convert.ToString(now, System.Globalization.CultureInfo.InvariantCulture);
                Check(r == SettingSuggestionBinding.Result.Applied && shown == s.Value, $"{s.Key}={s.Value} taken (now {shown})" + (error != null ? " (" + error + ")" : ""));
            }

            // saving writes the file's own values, not Earth's
            var suggestions = asm.GetType("AstraTerra.Config.ConfigSuggestions", true);
            var withFileValues = suggestions.GetMethod("WithFileValues");
            string savedSunArt = null;
            withFileValues.Invoke(null, new object[] { config, (Action)(() => savedSunArt = (string)configType.GetProperty("SunArt").GetValue(config)) });
            Check(savedSunArt == "vanilla" && (string)configType.GetProperty("SunArt").GetValue(config) == "pixel",
                $"saving astraterra.json writes the player's own SunArt ({savedSunArt}), then Earth's holds again");

            // a player who set their own moon size keeps it
            config = Activator.CreateInstance(configType);
            configType.GetProperty("MoonSizeDeg").SetValue(config, 5.0);
            configField.SetValue(system, config);
            r = SettingSuggestionBinding.TrySuggest(system, "MoonSizeDeg", "3", out _);
            Check(r == SettingSuggestionBinding.Result.Kept && (double)configType.GetProperty("MoonSizeDeg").GetValue(config) == 5.0, "a player's own MoonSizeDeg (5) is kept");

            // AcceptMapSuggestions: false keeps everything as it is
            config = Activator.CreateInstance(configType);
            configType.GetProperty("AcceptMapSuggestions").SetValue(config, false);
            configField.SetValue(system, config);
            r = SettingSuggestionBinding.TrySuggest(system, "SunArt", "pixel", out _);
            Check(r == SettingSuggestionBinding.Result.Kept && (string)configType.GetProperty("SunArt").GetValue(config) == "vanilla", "AcceptMapSuggestions false: nothing taken");

            r = SettingSuggestionBinding.TrySuggest(system, "TelescopeSunDamage", "false", out error);
            Check(r == SettingSuggestionBinding.Result.Failed, $"a setting outside the look is refused ({error})");
        }

        static void CheckRelease(string zip)
        {
            Console.WriteLine("installed AstraTerra 0.15.0 package: " + zip);
            if (!File.Exists(zip))
            {
                Skip("package not found");
                return;
            }
            Type type;
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                var entry = archive.Entries.First(e => e.FullName.Equals("AstraTerra.dll", StringComparison.OrdinalIgnoreCase));
                using var ms = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(ms);
                ms.Position = 0;
                var asm = new ModContext("astraterra-0.15.0").LoadFromStream(ms);
                type = asm.GetType("AstraTerra.AstraTerraModSystem", true);
            }
            catch (Exception e)
            {
                Check(false, "load AstraTerraModSystem from the package: " + e.Message);
                return;
            }
            Check(LongitudeProviderBinding.Find(type) == null, "has no SetLongitudeProvider");
            object system = Activator.CreateInstance(type);
            LongitudeProviderBinding.Result r;
            try { r = LongitudeProviderBinding.TrySet(system, x => 0, out _); }
            catch (Exception e) { Check(false, "binding threw: " + e.Message); return; }
            Check(r == LongitudeProviderBinding.Result.NoMethod, "binding is a clean no-op (NoMethod, nothing thrown); Earth logs once and goes on");
            Check(type.Assembly.GetType("AstraTerra.Astronomy.WorldLongitude") == null, "  ... and the package has no WorldLongitude at all (it really is the old API)");
            Check(SettingSuggestionBinding.TrySuggest(system, "SunArt", "pixel", out _) == SettingSuggestionBinding.Result.NoMethod,
                "suggesting Earth's look is a clean no-op too (no SuggestSetting)");
        }
    }
}
