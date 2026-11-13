using EarthMap.Core;
using System.Text.Json;

/// <summary>
/// Decision 0066: Earth 2.0 is one mod. Its zip holds the code, the assets and the climate files a client reads; every
/// other data file is in the server's data store (EarthDataStore, release ed-v1 of earth-data). These checks keep the
/// store's list true to the repository's data, the store's copying honest, and (when a release zip is built) the zip
/// under the Mod DB's limit and free of store files.
/// </summary>
static class PackageChecks
{
    /// <summary>The data files inside the mod: what a client reads, and a few small server files.</summary>
    static readonly string[] InZip = { "earth.json", "climate.bin", "climatefix.bin", "climatefix2.bin", "climatefix3.bin", "temperature.bin", "rain.bin", "weather.bin", "aridity.bin" };

    public static void Run(string root, Action<bool, string> check)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "modinfo.json")));
        string version = manifest.RootElement.GetProperty("version").GetString();
        check(manifest.RootElement.GetProperty("modid").GetString() == "earth" && manifest.RootElement.GetProperty("name").GetString() == "Earth",
            "Publication identity is Earth / earth");
        var deps = manifest.RootElement.GetProperty("dependencies").EnumerateObject().Select(d => d.Name).ToList();
        check(!deps.Any(d => EarthDataStore.LegacyPacks.Contains(d)), "Earth depends on no data-pack mod (0066): " + string.Join(", ", deps));

        // the store's list against the repository's own copies of its files
        int compared = 0;
        foreach (var f in EarthDataStore.Files)
        {
            foreach (string place in new[] { Path.Combine(root, "earthdata", f.Name), Path.Combine(root, "assets", "earth", "config", f.Name), Path.Combine(root, "assets", "earth", "config", "ground", f.Name) })
            {
                if (!File.Exists(place)) continue;
                compared++;
                check(DataTransport.FileMatches(place, f.Bytes, f.Sha256), "Data store list matches the repository's " + f.Name);
            }
        }
        check(compared >= 25, "Data store list compared with at least 25 repository files (" + compared + ")");
        check(EarthDataStore.Files.Select(f => f.Name).Distinct().Count() == EarthDataStore.Files.Length, "Data store names are unique");
        check(!EarthDataStore.Files.Any(f => InZip.Contains(f.Name)), "No client file is in the data store");

        // the store: a file comes in from an installed data pack, checked; a wrong copy is refused
        string temp = Path.Combine(Path.GetTempPath(), "earth-store-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lakes = EarthDataStore.Files.Single(f => f.Name == "lakes.bin");
            var shores = EarthDataStore.Files.Single(f => f.Name == "shores.bin");
            string pack = Path.Combine(temp, "pack"), store = Path.Combine(temp, "earth");
            Directory.CreateDirectory(Path.Combine(pack, "earthdata"));
            File.Copy(Path.Combine(root, "earthdata", "lakes.bin"), Path.Combine(pack, "earthdata", "lakes.bin"));
            File.WriteAllText(Path.Combine(pack, "earthdata", "shores.bin"), "not the shores");
            bool ok = EarthDataStore.Ensure(store, new[] { EarthDataStore.FromPack(pack) }, null, null, default, new[] { lakes });
            check(ok && DataTransport.FileMatches(EarthDataStore.PathOf(store, "lakes.bin"), lakes.Bytes, lakes.Sha256), "A data pack's file is copied into the store and checked");
            check(EarthDataStore.Missing(store, new[] { lakes }).Count == 0, "A copied file counts as present");
            bool wrong = EarthDataStore.Ensure(store, new[] { EarthDataStore.FromPack(pack) }, null, null, default, new[] { shores });
            check(!wrong && !File.Exists(EarthDataStore.PathOf(store, "shores.bin")) && !File.Exists(EarthDataStore.PathOf(store, "shores.bin") + ".copy"),
                "A data pack's wrong file is refused and leaves nothing behind");
            // a download cut short (name.part) survives a wrong local copy, so it can still resume
            File.WriteAllText(EarthDataStore.PathOf(store, "shores.bin") + ".part", "half a download");
            EarthDataStore.Ensure(store, new[] { EarthDataStore.FromPack(pack) }, null, null, default, new[] { shores });
            check(File.ReadAllText(EarthDataStore.PathOf(store, "shores.bin") + ".part") == "half a download", "A wrong local copy leaves a cut-short download alone");
            // a file changed on disk after its check is not trusted (no stamps: every start hashes)
            File.WriteAllBytes(EarthDataStore.PathOf(store, "lakes.bin"), new byte[lakes.Bytes]);
            check(EarthDataStore.Missing(store, new[] { lakes }).Count == 1, "A same-size changed file counts as missing");
            check(EarthDataStore.Open(store, "not-listed.bin") == null && EarthDataStore.Open(store, "shores.bin") == null, "Only listed files that are there open");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }

        // the release zip, when one is built
        string main = Path.Combine(root, "release", "earth-" + version + ".zip");
        if (!File.Exists(main))
        {
            Console.WriteLine("Skipping the release-zip checks: no built release zip at " + main + " (tools/package-earth.py makes it after dotnet build EarthMap.csproj -c Release).");
            return;
        }
        check(new FileInfo(main).Length <= 40_000_000, "Earth's zip within the Mod DB's 40 MB: " + new FileInfo(main).Length.ToString("N0"));
        using (var zip = System.IO.Compression.ZipFile.OpenRead(main))
        {
            check(zip.GetEntry("assets/earth/config/regionaltrees.json") != null && !zip.Entries.Any(e => e.FullName.StartsWith("assets/earthmap/")),
                "Published assets use the earth domain only");
            var leaked = zip.Entries.Where(e => EarthDataStore.Lists(e.Name)).Select(e => e.FullName).ToList();
            check(leaked.Count == 0, "No data store file in the zip" + (leaked.Count > 0 ? ": " + string.Join(", ", leaked.Take(5)) : ""));
            foreach (string n in InZip) check(zip.GetEntry("earthdata/" + n) != null, "Client data in the zip: " + n);
        }
        var client = EarthData.Load(name => EarthData.OpenDataFile(main, name), false);
        client.SetFineTemperature(true); client.SetFineRain(true); client.SetPenmanAridity(true);
        check(client.FineTemperatures && client.FineRain && client.PenmanAridity, "A client's climate loads from the zip alone");
        var server = EarthData.Load(name => EarthData.OpenDataFile(main, name) ?? EarthData.OpenDataFile(root, name), true);
        var source = EarthData.LoadFromFolder(root, true);
        foreach (var p in new[] { (56.95, 24.1), (35.36, 138.73), (27.99, 86.93), (-35.39, 149.12), (0.0, 179.99) })
        {
            var ca = server.SampleClimate(p.Item1, p.Item2); var cb = source.SampleClimate(p.Item1, p.Item2);
            check(ca.TMean == cb.TMean && ca.Rain == cb.Rain && ca.DrySummer == cb.DrySummer, "The zip's climate matches the source: " + p);
        }
    }
}
