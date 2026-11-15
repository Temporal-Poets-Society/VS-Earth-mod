using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0066: Earth is one mod. The data a game client reads (the climate files) is inside the mod; every other file
    /// the server reads is listed here and lives in VintagestoryData/ModData/earth/data, one copy per machine shared by
    /// every world and every later Earth version. The server makes sure all of them are there before it builds a world:
    /// first from an Earth data-pack mod that is still installed (Earth 1.0's packs carry the same bytes), then from the
    /// data release (release ed-v1 of Temporal-Poets-Society/earth-data, or a mirror or local folder set in earth.json).
    /// Each file is checked against the size and SHA-256 below before it is put in place, and again at each server start.
    /// </summary>
    public static class EarthDataStore
    {
        /// <summary>Where the files come from: the release's download folder (a local folder holding the files also works).</summary>
        public const string Release = "https://github.com/Temporal-Poets-Society/earth-data/releases/download/ed-v1/";
        /// <summary>The folder under ModData/earth that holds the files.</summary>
        public const string Subfolder = "data";

        public sealed record File(string Name, long Bytes, string Sha256);

        /// <summary>Release ed-v1 (README and manifest.json of Temporal-Poets-Society/earth-data). Names never change meaning: a
        /// changed file gets a new name, so a world made with an older Earth still finds the data it was made from.</summary>
        public static readonly File[] Files =
        {
            // ground and ecology: the 2′ ground sets, soil sites, wetlands, floodplains
            new("baltic-basement.egt", 25_178, "db62dbdcde817f9c6f79f3639dd7c33c8aef83e1000610cef87d3454df60053a"),
            new("baltic-emodnet-seabed.egt", 15_473, "797adda893b22b7dfcefc2cdf68b518ec4322dc26afa51cffe6f3573e943bbc1"),
            new("baltic-soilgrids.egt", 526_591, "2e6ff10d489fb4075749cd4e481a4551ae85c02e2dbb1492b594e789725b2cd8"),
            new("europe-igme5000.egt", 4_139, "fa64748b2573d0a24f4e9f09196182bdb6f577e606d220688060834cb2d5c11b"),
            new("floodplains.bin", 2_707_673, "e7b8f25ff0700548d256c20dc0dc81a6c61bb62d1cb1299e0ae52d3a910ef57f"),
            new("global-chorlton.egts", 1_519_321, "c451fd9fab691adf4fba023d7027071906f44dfdff4e52a7046b7493d59fe57c"),
            new("global-crust1.egt", 54_610, "b9531d7b0d02534e380090a57a45c25a60e8b7d21566a0c07b252429030a1a5e"),
            new("global-glaciation.egts", 138_764, "b3879efe65a0f9bd92c3022bc8eccd77bdf0b7d168d048aa027b01d2634e5dac"),
            new("global-gum.egt", 32_765, "cd7ef04a1c1f53758e4263f57f4401f1ec7cdcde41b2e2d80f617fd183e75c3f"),
            new("global-permafrost.egts", 1_364_328, "2a0cc1b3576675f15c0068a13bb0bd365bc21ca21fe9431d1b166ef5559a2f6b"),
            new("global-provinces.egts", 1_576_653, "b10ce96ad6b3d8bd29b52c6e9672d9ee8c1123aff15131e95a70b2730f805f96"),
            new("global-shangguan.egts", 11_840_388, "c728dff70954b0a01e006b088957b0120db0655f14288ded9017baea6e413387"),
            new("global-soilgrids.egts", 13_199_820, "2a940a389cc03609510e4e5b90a99f293e8ce324971b4014bbc9f05dae0b6e88"),
            new("latvia-quaternary.egt", 89_493, "4c03aa9813d49c2294fb327cda3ca1a1ae6f2c104e0f66f1ef266720f300c607"),
            new("soil-site.bin", 1_618_438, "407f6dc3657bb396f00732c694cb3b26d8d8c08cadde4a6be3f324c48f2acb25"),
            new("wetlands.bin", 2_288_691, "644824a1e2942791dd2625034217cd6640cd2196b79cde2213ab22376c40f932"),
            // terrain: the 2′ base grid, summits, peaks, glaciers, the included GLO-90 land heights
            new("glaciers.bin", 1_220_181, "06c854595621eacfec624ec445ad80a9da32026ec87b1a28618a3ca71b0216ab"),
            new("glo90c-1.bin", 39_594_940, "69edad31d3df4e66afd1f10fe52ea61e54622c41295a36d510dfaad3813c558e"),
            new("glo90c-2.bin", 39_791_138, "1ce4ab9eab2fc57ca3f79bd8547ab0254d35dd4273989ffaa02763713a8276b4"),
            new("glo90c-3.bin", 39_796_380, "0b98d0ef7343c86b14ef5fa127db81e3cd5ff295359e357d152c116eb26b0a22"),
            new("glo90c-4.bin", 9_984_522, "6a669fda9a68b0c411dc7330014b2a93f401cb154823bdcbf5dc49251777e103"),
            new("glo90c-index.bin", 177_324, "d55d203d436fa7b14d2d6c5de4a412090e9a0bc6f6ce0f70b969481f87926977"),
            new("peaks.json", 6_642_227, "a779f5a67940b797b420437e0b0f574c3161b839ef64e4c0df98ced9293e7e5f"),
            new("summits.bin", 2_474_923, "9fe7dfacf0ee051c0c22fd98c20d2deb88831bafc5c17e3b09e23c7014368704"),
            new("terrain.bin", 30_347_837, "e733cca42ac825c13005482a87f4abf2147480bcb7c41f5c2abbc71cde1e4640"),
            // water: rivers, coasts, islands, lakes and the GLO-90 water corrections
            new("coasts.bin", 9_477_193, "2d5e2ad38166113f0ad7253f699a9ca556730717f365c423738d18ad9a5dfc1f"),
            new("islands.bin", 9_891_240, "2b9bc440aede37e03bc2f28219da50ec650fdf05c24caa46966d2944b4d72dc6"),
            new("lakes.bin", 17_444, "cbec2376eba342320098a7856d6ec7f62463273699ae6c3edb064e794718560d"),
            new("rivers.bin", 12_132_790, "aaebda1d8abf95a75e740c1d21774aa360419582bf774a4fcdda21e73446a8b5"),
            new("rivers-glo90.bin", 23_976_950, "461aff76afe30afd8ef4261646a7ce7e72251e0e10652274670b118ffd6baed4"),
            new("rivers-glo90-arms.bin", 185_836, "8748a916ead1e2fd9202e076c9ad6b53d5c2e6427f7fdd3cfca21783b9be057a"),
            new("rivers-glo90-arms-2.bin", 223_228, "e7f7f4408c9bb027b5e61ff1f522beadcda46ead3471986ba3baebb70b753bf7"),
            new("rivers-glo90-snap.bin", 4_510_852, "022a374374a662bdd0e015d9f37965d9a985b3fba7114000f8f949bdf375475f"),
            new("rivers-glo90-snap-2.bin", 8_235_212, "fa87c5895b6a0ff6df1def67f7908c994747d6effd51de4549b1f9a0c39c9dc1"),
            new("sea-referee.bin", 6_262_394, "3d823a2e280fa6c12504f07f87069f81ecc8721109c89b33eab4f6d8ff1c7fe7"),
            new("shores.bin", 126_401, "691673039f64ee8a85207ec8acad84ebd35b8b7b261ef99a23970b2c035cc8ce"),
            // decision 0070: OpenStreetMap's beaches by material (ODbL)
            new("shores-osm-1.bin", 2_220_894, "3151094cc16f753550e48dd33540a3928b8bc1a31556a8904855d0893fc101de"),
            new("stream-heads-1.bin", 4_712_127, "bffcc5f7ee6f89bff423e261695f7ec1dd40449af08810c976da7143f25380ad"),
        };

        static readonly Dictionary<string, File> byName = Files.ToDictionary(f => f.Name, StringComparer.Ordinal);

        public static long TotalBytes(IEnumerable<File> files = null) => (files ?? Files).Sum(f => f.Bytes);

        /// <summary>Whether <paramref name="name"/> is one of the store's files.</summary>
        public static bool Lists(string name) => name != null && byName.ContainsKey(name);

        public static string Folder(string earthFolder) => Path.Combine(earthFolder, Subfolder);
        public static string PathOf(string earthFolder, string name) => Path.Combine(earthFolder, Subfolder, name);

        /// <summary>
        /// (EA-0092 A6) Fetches a listed file again after its read found it damaged (it is set aside first), so that the read
        /// goes on with the release's bytes: the server sets it to its data source and installed packs, as at the start; null
        /// in tools and tests, where a damaged file throws InvalidDataException.
        /// </summary>
        public static Func<string, bool> Repair;

        /// <summary>
        /// A listed file from the store, or null when the name isn't listed or the file isn't there. (EA-0092 A6, code review
        /// 14 November 2026) Read whole and checked against its size and SHA-256 in that same read, which the caller then
        /// gets: before, every start hashed all of the store (289 MB) in a pass of its own and the loaders read it all again.
        /// A damaged file is still never used: it is set aside (.bad) and fetched again (<see cref="Repair"/>) before anything
        /// reads it, and when it can't be, InvalidDataException says so.
        /// </summary>
        public static Stream Open(string earthFolder, string name)
        {
            if (!Lists(name)) return null;
            string path = PathOf(earthFolder, name);
            if (!System.IO.File.Exists(path)) return null;
            var f = byName[name];
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (Checked(bytes, f)) return new MemoryStream(bytes, 0, bytes.Length, false, true);
            SetAside(path);
            if (Repair != null && Repair(name) && System.IO.File.Exists(path))
            {
                bytes = System.IO.File.ReadAllBytes(path);
                if (Checked(bytes, f)) return new MemoryStream(bytes, 0, bytes.Length, false, true);
                SetAside(path);
            }
            throw new InvalidDataException($"earthdata/{name} in {Folder(earthFolder)} is damaged (not the release's size and SHA-256) and could not be fetched again; it is set aside as {name}.bad");
        }

        static bool Checked(byte[] bytes, File f) => bytes.LongLength == f.Bytes && DataTransport.HashMatches(bytes, f.Sha256);

        static void SetAside(string path)
        {
            try { System.IO.File.Move(path, path + ".bad", true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { System.IO.File.Delete(path); } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
            }
        }

        /// <summary>A listed file's bytes, or null (see <see cref="Open"/>).</summary>
        public static byte[] Bytes(string earthFolder, string name)
        {
            using var s = Open(earthFolder, name);
            return s == null ? null : EarthBinary.ReadAll(s);   // (one array of the file's size, CR-D4)
        }

        /// <summary>
        /// The listed files missing from the store or not matching their size and SHA-256. Every file there is hashed, in
        /// parallel (a fraction of a second for all of them), so a file changed on disk is never trusted.
        /// </summary>
        public static List<File> Missing(string earthFolder, IEnumerable<File> files = null)
        {
            var list = (files ?? Files).ToArray();
            var bad = new bool[list.Length];
            Parallel.For(0, list.Length, i => bad[i] = !DataTransport.FileMatches(PathOf(earthFolder, list[i].Name), list[i].Bytes, list[i].Sha256));
            return list.Where((f, i) => bad[i]).ToList();
        }

        /// <summary>
        /// Makes sure every listed file is in the store, blocking until done; true when all are then there and checked.
        /// <paramref name="local"/>: places that may already hold a file (an installed Earth 1.0 data pack), asked first;
        /// each returns a stream for a name or null. <paramref name="source"/>: the data release or a mirror (a URL ending in
        /// '/' or a local folder); empty: nothing is downloaded. A downloaded file is written as name.part, resumed where it
        /// broke (up to four tries), checked and then moved in (DataTransport), so a file is either whole or absent. Servers
        /// sharing one store take turns (a lock file in the store), and each looks again once it has the turn.
        /// </summary>
        public static bool Ensure(string earthFolder, IEnumerable<Func<string, Stream>> local, string source, Action<string> log, CancellationToken token = default, IEnumerable<File> files = null)
            => Ensure(earthFolder, local, source, log, out _, token, files);

        /// <summary><see cref="Ensure(string, IEnumerable{Func{string, Stream}}, string, Action{string}, CancellationToken, IEnumerable{File})"/>,
        /// with why the files could not be had (the last fetch's error, or that no source is set; null when they are all there).</summary>
        public static bool Ensure(string earthFolder, IEnumerable<Func<string, Stream>> local, string source, Action<string> log, out string why, CancellationToken token = default, IEnumerable<File> files = null)
        {
            var (ok, reason) = Task.Run(() => EnsureCoreAsync(earthFolder, local, source, log, token, files), token).GetAwaiter().GetResult();
            why = reason;
            return ok;
        }

        public static async Task<bool> EnsureAsync(string earthFolder, IEnumerable<Func<string, Stream>> local, string source, Action<string> log, CancellationToken token = default, IEnumerable<File> files = null)
            => (await EnsureCoreAsync(earthFolder, local, source, log, token, files).ConfigureAwait(false)).Ok;

        static async Task<(bool Ok, string Why)> EnsureCoreAsync(string earthFolder, IEnumerable<Func<string, Stream>> local, string source, Action<string> log, CancellationToken token, IEnumerable<File> files)
        {
            // (EA-0092 A4) the one protocol for a set of published files (DataTransport.EnsureFilesAsync)
            var specs = (files ?? Files).Select(f => new DataTransport.FileSpec(f.Name, PathOf(earthFolder, f.Name), f.Bytes, f.Sha256)).ToList();
            var byName = (files ?? Files).ToDictionary(f => f.Name);
            var opens = local?.ToList() ?? new List<Func<string, Stream>>();
            // (EA-0092 A6) present and of the right size here; each file's SHA-256 is checked as it is read (Open)
            var (result, why) = await DataTransport.EnsureFilesAsync(Folder(earthFolder), specs, source, "DataRepo", "data source", "data file(s)", log, token, hash: false,
                copyIn: opens.Count == 0 ? null : f => opens.Any(open => CopyIn(earthFolder, byName[f.Name], open)),
                // (EA-0116 D-R3) a copy from an installed pack that a killed process left half written: with the folder's turn
                // held, no one else is copying (a download's own name.part is kept, to be resumed)
                inTurn: () =>
                {
                    try
                    {
                        foreach (var left in Directory.Exists(Folder(earthFolder)) ? Directory.GetFiles(Folder(earthFolder), "*.copy") : Array.Empty<string>())
                            try { System.IO.File.Delete(left); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }).ConfigureAwait(false);
            return (result == DataTransport.SetResult.Here, why);
        }
        /// <summary>
        /// A file inside a mod's source (its folder or zip) at <paramref name="relativePath"/> (forward slashes), read into
        /// memory so the archive can close; null when it isn't there. For the places an installed Earth 1.0 data pack keeps
        /// the store's files: earthdata/name, and assets/earth/config/ground/name in the ground pack.
        /// </summary>
        public static Stream OpenEntry(string sourcePath, string relativePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            try
            {
                if (Directory.Exists(sourcePath))
                {
                    string path = Path.Combine(sourcePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    return System.IO.File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                }
                if (!System.IO.File.Exists(sourcePath)) return null;
                using var zip = System.IO.Compression.ZipFile.OpenRead(sourcePath);
                var entry = zip.GetEntry(relativePath);
                if (entry == null) return null;
                var m = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                using (var s = entry.Open()) s.CopyTo(m);
                m.Position = 0;
                return m;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>The places an installed Earth 1.0 data pack (<paramref name="packSource"/>: its folder or zip) may hold a
        /// store file.</summary>
        public static Func<string, Stream> FromPack(string packSource)
            => name => OpenEntry(packSource, "earthdata/" + name) ?? OpenEntry(packSource, "assets/earth/config/ground/" + name)
                       ?? OpenEntry(packSource, "assets/earth/config/" + name);

        /// <summary>The Earth 1.0 data-pack mods; a store file found in one that is still installed is copied, not downloaded.</summary>
        public static readonly string[] LegacyPacks =
            { "earthterraindata", "earthwaterdata", "earthwaterfixdata", "earthheightdata1", "earthheightdata2", "earthheightdata3", "earthheightdata4", "earthgrounddata" };

        /// <summary>One file from a local place into the store, if that place has it with the right bytes.</summary>
        static bool CopyIn(string earthFolder, File f, Func<string, Stream> open)
        {
            // staged under its own name: target.part may be a download cut short, to be resumed
            string target = PathOf(earthFolder, f.Name), part = target + ".copy";
            try
            {
                using (var src = open(f.Name))
                {
                    if (src == null) return false;
                    using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                    src.CopyTo(dst);
                }
                if (!DataTransport.FileMatches(part, f.Bytes, f.Sha256)) { System.IO.File.Delete(part); return false; }
                System.IO.File.Move(part, target, true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                try { System.IO.File.Delete(part); } catch (IOException) { }
                return false;
            }
        }
    }
}
