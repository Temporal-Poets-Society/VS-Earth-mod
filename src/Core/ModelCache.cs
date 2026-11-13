using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace EarthMap.Core
{
    /// <summary>
    /// Results of a world's model build that are slow to make and small to keep, saved between starts (2.0.0-rc.4): the
    /// navigable straits' courses and the sea channels' ways (together about 8 s of a 1:10 start on a fast machine, far more
    /// on one with fewer cores). Each is a pure function of the build's inputs, so a file is only used for the same inputs:
    /// its name is the hash of them (<see cref="KeyOf"/>: this Earth build, the data files, the world's settings), and a
    /// world whose settings, data or Earth build changed gets a new file, built afresh. The file also checks itself (a hash
    /// of its content): one that doesn't, or can't be read, is ignored and replaced. Worlds with the same inputs share a
    /// file; the newest <see cref="KeepFiles"/> files are kept.
    /// </summary>
    public sealed class ModelCache
    {
        /// <summary>The file layout; a new one makes every older file a stranger (ignored, then pruned). 2: from 2.0.0-rc.4's
        /// release build (1 was its development builds', whose inputs had another layout).</summary>
        public const int FormatVersion = 2;
        /// <summary>How many files the folder keeps (the most recently used).</summary>
        public const int KeepFiles = 8;
        const uint Magic = 0x434D4145; // "EAMC"

        readonly string folder;
        readonly Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        readonly List<string> built = new List<string>(), reused = new List<string>();

        /// <summary>The inputs' hash (hex), also the file's name.</summary>
        public string Key { get; }
        /// <summary>The inputs as given (kept in the file, so a later start can say what changed).</summary>
        public byte[] Inputs { get; }
        /// <summary>Why an existing file for this key wasn't used (unreadable, damaged), else null.</summary>
        public string Problem { get; private set; }
        /// <summary>The parts taken from the file, and the ones built this start.</summary>
        public IReadOnlyList<string> Reused => reused;
        public IReadOnlyList<string> Built => built;

        ModelCache(string folder, string key, byte[] inputs)
        {
            this.folder = folder;
            Key = key;
            Inputs = inputs;
        }

        /// <summary>The key of a set of inputs: SHA-256 over the format version and each input (length-prefixed), hex.</summary>
        public static string KeyOf(byte[] inputs)
        {
            using var sha = SHA256.Create();
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(inputs.Length);
                w.Write(inputs);
            }
            return Convert.ToHexString(sha.ComputeHash(ms.ToArray())).ToLowerInvariant();
        }

        public static string PathOf(string folder, string key) => Path.Combine(folder, "model-" + key + ".bin");

        /// <summary>The cache for these inputs: the file's parts when there is a sound file for them, else empty (every part is
        /// then built and saved). Never throws for a missing or bad file.</summary>
        public static ModelCache Open(string folder, byte[] inputs)
        {
            var cache = new ModelCache(folder, KeyOf(inputs), inputs);
            string path = PathOf(folder, cache.Key);
            if (!File.Exists(path)) return cache;
            try { cache.Read(File.ReadAllBytes(path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
            {
                cache.parts.Clear();
                cache.Problem = e.Message;
            }
            return cache;
        }

        /// <summary>The inputs kept in another file of this folder (null if there is none, or it can't be read or is damaged:
        /// the whole file is checked as <see cref="Open"/> checks it): what a world was built with before, to say what
        /// changed.</summary>
        public static byte[] InputsOf(string folder, string key)
        {
            try
            {
                string path = PathOf(folder, key);
                if (!File.Exists(path)) return null;
                var other = new ModelCache(folder, key, null);
                other.Read(File.ReadAllBytes(path));
                return other.readInputs;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException or ArgumentException) { return null; }
        }

        /// <summary>The inputs <see cref="Read"/> found in a file opened without knowing them (InputsOf).</summary>
        byte[] readInputs;

        /// <summary>
        /// A part: read from the file when it has it and <paramref name="read"/> accepts it (returns non-null, reading it to
        /// its end), else built, kept for <see cref="Save"/>, and returned. <paramref name="read"/> may return null for a part
        /// that doesn't fit what this start has (it is then built). A null cache builds.
        /// </summary>
        public static T Use<T>(ModelCache cache, string part, Func<T> build, Action<BinaryWriter, T> write, Func<BinaryReader, T> read) where T : class
        {
            if (cache == null) return build();
            if (cache.parts.TryGetValue(part, out var bytes))
            {
                try
                {
                    using var r = new BinaryReader(new MemoryStream(bytes));
                    var value = read(r);
                    if (value != null && r.BaseStream.Position == bytes.Length)
                    {
                        cache.reused.Add(part);
                        return value;
                    }
                }
                catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or ArgumentException) { }
            }
            var made = build();
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) write(w, made);
            cache.parts[part] = ms.ToArray();
            cache.built.Add(part);
            return made;
        }

        /// <summary>Writes the file when a part was built this start (atomically: a new file renamed over it), marks it used,
        /// and prunes the folder to the newest <see cref="KeepFiles"/>. Returns null, or why it couldn't (the world goes on;
        /// the next start builds again).</summary>
        public string Save()
        {
            string path = PathOf(folder, Key);
            try
            {
                Directory.CreateDirectory(folder);
                if (built.Count > 0)
                {
                    string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(tmp, Bytes());
                        File.Move(tmp, path, true);
                    }
                    finally
                    {
                        // a write or rename that failed leaves no half file behind
                        if (File.Exists(tmp)) try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                else if (File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                Prune(folder, path);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
        }

        static void Prune(string folder, string keep)
        {
            var files = new DirectoryInfo(folder).GetFiles("model-*.bin").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files.Skip(KeepFiles))
                if (!string.Equals(f.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                    try { f.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // leftovers of a write that didn't finish
            foreach (var f in new DirectoryInfo(folder).GetFiles("model-*.tmp"))
                if (f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                    try { f.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // the file: magic, format, key, inputs, the parts (name, bytes), then SHA-256 of everything before it
        byte[] Bytes()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(Key);
                w.Write(Inputs.Length);
                w.Write(Inputs);
                w.Write(parts.Count);
                foreach (var kv in parts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.Length);
                    w.Write(kv.Value);
                }
            }
            using var sha = SHA256.Create();
            byte[] body = ms.ToArray();
            ms.Write(sha.ComputeHash(body));
            return ms.ToArray();
        }

        void Read(byte[] file)
        {
            if (file.Length < 32) throw new InvalidDataException("the file is too short");
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(file, 0, file.Length - 32);
                if (!hash.AsSpan().SequenceEqual(file.AsSpan(file.Length - 32))) throw new InvalidDataException("the file is damaged (its hash doesn't match)");
            }
            using var r = new BinaryReader(new MemoryStream(file, 0, file.Length - 32));
            if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion) throw new InvalidDataException("not a model cache of this format");
            if (r.ReadString() != Key) throw new InvalidDataException("the file is for other inputs");
            readInputs = r.ReadBytes(Length(r));
            if (Inputs != null && !readInputs.AsSpan().SequenceEqual(Inputs)) throw new InvalidDataException("the file is for other inputs");
            int n = r.ReadInt32();
            if (n < 0) throw new InvalidDataException("a negative part count");
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadString();
                parts[name] = r.ReadBytes(Length(r));
            }
        }

        /// <summary>A length field, checked against what is left of the file (a damaged one is never trusted).</summary>
        static int Length(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len < 0 || len > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("a length beyond the file");
            return len;
        }
    }
}
