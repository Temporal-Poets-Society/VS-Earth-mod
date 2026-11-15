using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace EarthMap.Core
{
    /// <summary>
    /// 0053 item 19: modern dams taken out of GLO-90 (tools/bake_dam_removal.py). Earth's worlds are set
    /// around 1318; the reservoirs' water is already left out (items 17 and 18), but GLO-90 still has their dams and their flat
    /// water surfaces, and the river courses step down at the dams. The file holds, per GLO-90 square, the heights that
    /// change (dam walls lowered, a valley floor under each reservoir) and the river course levels through the reservoirs
    /// (falling by length from where the river enters a reservoir to the river's level below its dam, never rising).
    ///
    /// File (little-endian): "EMDM", version (the dam rules it was made by), square count, course key (uint64:
    /// <see cref="CourseKey"/> of the course list the levels were made for), course level count; per square (int16 latS,
    /// int16 lonW, int32 columns, int32 offset, int32 length) in key order; then each square's brotli stream (varint count;
    /// per pixel varint index gap, the index minus the one before minus one, from -1; then per pixel zigzag varint height
    /// change from the one before, from 0 m); then one brotli stream of course levels (per level varint reach gap, varint
    /// station or station gap within the same reach, zigzag varint level in metres), sorted by reach and station.
    ///
    /// Dam rules 2 (0063 rule 3, dams-glo90-2.bin; the same layout, keyed to courses rules 4's list): the river's corridor keeps its full width across a
    /// wall and the tailrace's banks, which come down to it; its width is the smaller of the river's going in and coming
    /// out (measured beyond the tailwater basin, at most a cap by the flow); no course level stands above the ground GLO-90
    /// has beside it less a metre, and a cascade's dam takes no level from the next reservoir's surface.
    /// </summary>
    public sealed class DamRemovalData
    {
        /// <summary>The newest file version this build reads (versions 1, 2 and 3 have the same layout).</summary>
        public const int FileVersion = 3;
        /// <summary>The rules new worlds record (EarthParams.DamRules): 1 = the heights and course levels of dams-glo90-1.bin,
        /// with RiverOutlineData version 2's rivers through the reservoirs.</summary>
        public const int RulesVersion = 1;
        /// <summary>Dam rules 2 (0063 rule 3): dams-glo90-2.bin. The newest dam rules this build reads; a world records it
        /// once new worlds are given it (with the river outlines baked from the same reservoirs and corridors).</summary>
        public const int Rules2 = 2;
        /// <summary>Dam rules 3 (decision 0069 rule 9): dams-glo90-3.bin, rules 2 with Global Dam Watch's reservoirs that
        /// HydroLAKES lacks taken out too (tools/reservoir_list.py: the Seimare Dam of 2013 among them), on the same course
        /// list.</summary>
        public const int Rules3 = 3;
        /// <summary>The newest dam rules this build can read.</summary>
        public const int NewestRules = Rules3;
        /// <summary>The course rules (EarthParams.RiverCourses) whose list dam rules 1's levels were made for (courses rules
        /// 3, key 9573d2e3e5bfbb59).</summary>
        public const int CourseRules = 3;
        /// <summary>The course rules whose list dam rules 2's levels are made for (0063: courses rules 4).</summary>
        public const int CourseRules2 = 4;
        /// <summary>Dam rules 1's file name in the GLO-90 folder and on Earth's data release; a new version gets a new name.</summary>
        public const string FileName = "dams-glo90-1.bin";
        /// <summary>The rules 1 file this build reads (from the global bake of 8 November 2026); any other is refused.</summary>
        public const long FileBytes = 4_829_546;
        public const string FileSha256 = "6e50b73c106fc26ad4ac86bd40559ef31943149b936defb5026fba436be1af66";
        /// <summary>Dam rules 2's file (tools/bake_dam_removal.py version 2 on the courses rules 4 list, course key
        /// 9693b65a3a265a62; global bake of 8 November 2026).</summary>
        public const string FileName2 = "dams-glo90-2.bin";
        public const long FileBytes2 = 5_142_018;
        public const string FileSha2562 = "93180bc47cab9e5fadce7d74ad36c9442bdbb7f91e93a72b548dd0b93cd7061c";
        /// <summary>Dam rules 3's file (tools/bake_dam_removal.py with EARTH_GDW=1 on courses rules 4's list, course key
        /// 9693b65a3a265a62; global bake of 12 November 2026).</summary>
        public const string FileName3 = "dams-glo90-3.bin";
        public const long FileBytes3 = 6_207_627;
        public const string FileSha2563 = "dcc997859742959470eae7dd99993c390fc301e350c2a2d8e6df9e3f06df7299";

        /// <summary>The file a world with these dam rules reads (rules 1: dams-glo90-1.bin, 2: dams-glo90-2.bin, 3 and later:
        /// dams-glo90-3.bin).</summary>
        public static string FileNameFor(int rules) => rules >= Rules3 ? FileName3 : rules >= Rules2 ? FileName2 : FileName;
        public static long FileBytesFor(int rules) => rules >= Rules3 ? FileBytes3 : rules >= Rules2 ? FileBytes2 : FileBytes;
        public static string FileSha256For(int rules) => rules >= Rules3 ? FileSha2563 : rules >= Rules2 ? FileSha2562 : FileSha256;

        /// <summary>Whether these bytes are dam rules 1's file this build was made for (size and SHA-256).</summary>
        public static bool IsExpected(byte[] bytes, out string why) => IsExpected(bytes, RulesVersion, out why);
        /// <summary>Whether these bytes are the file this build reads for these dam rules (size and SHA-256).</summary>
        public static bool IsExpected(byte[] bytes, int rules, out string why) =>
            LakeOutlineData.Matches(bytes, FileBytesFor(rules), FileSha256For(rules), out why);

        readonly byte[] file;
        readonly Dictionary<int, (int cols, int off, int len)> squares;
        readonly int courseOff, courseLen, courseCount;
        /// <summary>The course list's key the levels were made for.</summary>
        public readonly ulong CourseKey;
        /// <summary>The file's version: the dam rules it was made by.</summary>
        public readonly int Version;
        /// <summary>The file's name (by its version).</summary>
        public string Name => FileNameFor(Version);
        public int Squares => squares.Count;
        public int CourseLevels => courseCount;

        DamRemovalData(byte[] file, int version, Dictionary<int, (int cols, int off, int len)> squares, ulong key, int count, int off, int len)
        {
            this.file = file;
            Version = version;
            this.squares = squares;
            CourseKey = key;
            courseCount = count;
            courseOff = off;
            courseLen = len;
        }

        public static DamRemovalData Read(byte[] file)
        {
            if (file.Length < 24 || file[0] != 'E' || file[1] != 'M' || file[2] != 'D' || file[3] != 'M')
                throw new InvalidDataException("dam file: bad header");
            int version = BitConverter.ToInt32(file, 4);
            if (version < 1 || version > FileVersion) throw new InvalidDataException("dam file: version " + version);
            string name = FileNameFor(version);
            int n = BitConverter.ToInt32(file, 8);
            ulong key = BitConverter.ToUInt64(file, 12);
            int levels = BitConverter.ToInt32(file, 20);
            int o = 24, end = 24 + 16 * n;
            if (n < 0 || end > file.Length) throw new InvalidDataException(name + ": bad square count");
            var squares = new Dictionary<int, (int cols, int off, int len)>(n);
            for (int i = 0; i < n; i++, o += 16)
            {
                int latS = BitConverter.ToInt16(file, o), lonW = BitConverter.ToInt16(file, o + 2);
                int cols = BitConverter.ToInt32(file, o + 4), off = BitConverter.ToInt32(file, o + 8), len = BitConverter.ToInt32(file, o + 12);
                if (off < 24 + 16 * n || len < 0 || off + len > file.Length) throw new InvalidDataException(name + ": bad square index");
                squares[FineElevation.Key(latS, lonW)] = (cols, off, len);
                end = Math.Max(end, off + len);
            }
            // the course levels' stream follows the squares'
            return new DamRemovalData(file, version, squares, key, levels, end, file.Length - end);
        }

        public bool Has(int latS, int lonW) => squares.ContainsKey(FineElevation.Key(latS, lonW));

        /// <summary>Changes a GLO-90 tile's heights as the file has them (a tile of another width is left as it is); the
        /// number of points changed.</summary>
        public int ApplyTo(DemTiles.Tile t)
        {
            if (t == null || !squares.TryGetValue(FineElevation.Key(t.LatS, t.LonW), out var e) || e.cols != t.Cols) return 0;
            byte[] b = Unpack(e.off, e.len);
            int o = 0;
            int count = (int)Varint(b, ref o);
            var idx = new int[count];
            int prev = -1;
            for (int i = 0; i < count; i++) idx[i] = prev = prev + 1 + (int)Varint(b, ref o);
            int h = 0, changed = 0;
            for (int i = 0; i < count; i++)
            {
                h += Zigzag(Varint(b, ref o));
                if (idx[i] < 0 || idx[i] >= t.Heights.Length) continue;
                if (t.Heights[idx[i]] != h) changed++;
                t.Heights[idx[i]] = (short)h;
            }
            return changed;
        }

        /// <summary>The course levels: (reach, station, level m), sorted.</summary>
        public IEnumerable<(int reach, int k, short level)> Levels()
        {
            if (courseCount == 0) yield break;
            byte[] b = Unpack(courseOff, courseLen);
            int o = 0, reach = 0, k = -1;
            for (int i = 0; i < courseCount; i++)
            {
                int gap = (int)Varint(b, ref o);
                if (gap > 0) { reach += gap; k = (int)Varint(b, ref o); }
                else k += 1 + (int)Varint(b, ref o);
                yield return (reach, k, (short)Zigzag(Varint(b, ref o)));
            }
        }

        /// <summary>
        /// Sets the course levels through the reservoirs in a course list (as EarthData.RiverReaches gives it), never raising
        /// one: the number of stations lowered, or -1 (with the reason) when the list isn't the one the file was made for.
        /// </summary>
        public int LowerCourses(List<RiverReach> list, out string why)
        {
            why = null;
            ulong key = KeyOf(list);
            if (key != CourseKey) { why = $"{Name} was made for other river courses (key {CourseKey:x16}, these {key:x16})"; return -1; }
            int n = 0;
            foreach (var (reach, k, level) in Levels())
            {
                if (reach >= list.Count) continue;
                var r = list[reach];
                if (k >= r.Level.Length || r.Fixed[k] || level >= r.Level[k]) continue;
                r.Level[k] = level;
                n++;
            }
            return n;
        }

        /// <summary>A key of a course list (FNV-1a over each reach's flow, next reach, stations and levels), so the levels
        /// are only set on the list they were made for.</summary>
        public static ulong KeyOf(List<RiverReach> list)
        {
            ulong h = 14695981039346656037UL;
            void Mix(uint v) { for (int s = 0; s < 32; s += 8) { h ^= (v >> s) & 0xFF; h *= 1099511628211UL; } }
            Mix((uint)list.Count);
            foreach (var r in list)
            {
                Mix(BitConverter.SingleToUInt32Bits(r.Q));
                Mix((uint)r.Next);
                Mix((uint)r.Lon.Length);
                for (int k = 0; k < r.Lon.Length; k++)
                {
                    Mix(BitConverter.SingleToUInt32Bits(r.Lon[k]));
                    Mix(BitConverter.SingleToUInt32Bits(r.Lat[k]));
                    Mix((uint)(ushort)r.Level[k]);
                }
            }
            return h;
        }

        byte[] Unpack(int off, int len)
        {
            using var dec = new BrotliStream(new MemoryStream(file, off, len), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            dec.CopyTo(ms);
            return ms.ToArray();
        }

        static ulong Varint(byte[] b, ref int o)
        {
            ulong v = 0;
            for (int s = 0; ; s += 7)
            {
                byte x = b[o++];
                v |= (ulong)(x & 0x7F) << s;
                if ((x & 0x80) == 0) return v;
            }
        }

        static int Zigzag(ulong v) => (int)(v >> 1) ^ -(int)(v & 1);
    }
}
