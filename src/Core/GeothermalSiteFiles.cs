using System;
using System.IO;
using System.Text;

namespace EarthMap.Core
{
    // Geothermal, part: the site lists (decision 0057), read from the two data files tools/bake_geothermal_sites.py writes and
    // the build embeds (EA-0092: the lists were generated C# until 14 November 2026; the same values, byte for byte).
    public static partial class Geothermal
    {
        /// <summary>The embedded GVP volcanoes and GeoNames springs (data/geothermal-sites.bin).</summary>
        public const string SitesResource = "EarthMap.geothermal-sites.bin";
        /// <summary>The embedded OpenStreetMap springs (data/geothermal-osm.bin): kept apart because that list is a derivative
        /// database of OpenStreetMap under the Open Database License 1.0, (c) OpenStreetMap contributors.</summary>
        public const string OsmResource = "EarthMap.geothermal-osm.bin";

        /// <summary>
        /// The two files' contents. Both little-endian: a four-letter magic ("EGEO" for the sites, "EGOS" for OpenStreetMap's),
        /// int32 version 1, int32 length and that many UTF-8 bytes of the source notice; then the sites file has int32 n
        /// volcanoes, float32 lat, lon per volcano, n bytes kind (SiteKind), n names (int32 length, UTF-8), n int32 GVP numbers,
        /// n bytes landform, n int16 summit elevations (m), n bytes active in the game's time (0/1), n bytes magma, then the
        /// GeoNames springs; a spring list is int32 m, float32 lat, lon per spring, m bytes kind. The OpenStreetMap file holds
        /// one spring list.
        /// </summary>
        sealed class SiteFiles
        {
            public float[] GvpLatLon, GeoNamesSpringLatLon, OsmSpringLatLon;
            public byte[] GvpKind, GvpForm, GvpMagma, GeoNamesSpringKind, OsmSpringKind;
            public string[] GvpName;
            public int[] GvpNumber;
            public short[] GvpElevation;
            public bool[] GvpEra;
            public string SitesNotice, OsmNotice;

            public static SiteFiles Read()
            {
                var f = new SiteFiles();
                using (var r = Open(SitesResource, "EGEO", out f.SitesNotice))
                {
                    int n = r.ReadInt32();
                    f.GvpLatLon = Floats(r, 2 * n);
                    f.GvpKind = Bytes(r, n);
                    f.GvpName = new string[n];
                    for (int i = 0; i < n; i++) f.GvpName[i] = Encoding.UTF8.GetString(Bytes(r, r.ReadInt32()));
                    f.GvpNumber = new int[n];
                    for (int i = 0; i < n; i++) f.GvpNumber[i] = r.ReadInt32();
                    f.GvpForm = Bytes(r, n);
                    f.GvpElevation = new short[n];
                    for (int i = 0; i < n; i++) f.GvpElevation[i] = r.ReadInt16();
                    var era = Bytes(r, n);
                    f.GvpEra = new bool[n];
                    for (int i = 0; i < n; i++) f.GvpEra[i] = era[i] != 0;
                    f.GvpMagma = Bytes(r, n);
                    Springs(r, out f.GeoNamesSpringLatLon, out f.GeoNamesSpringKind);
                    End(r, SitesResource);
                }
                using (var r = Open(OsmResource, "EGOS", out f.OsmNotice))
                {
                    Springs(r, out f.OsmSpringLatLon, out f.OsmSpringKind);
                    End(r, OsmResource);
                }
                return f;
            }

            static BinaryReader Open(string name, string magic, out string notice)
            {
                var s = typeof(Geothermal).Assembly.GetManifestResourceStream(name)
                    ?? throw new FileNotFoundException("embedded " + name + " (a project that compiles src/Core embeds data/"
                        + name.Substring("EarthMap.".Length) + " under that name, as EarthMap.csproj does)");
                var r = new BinaryReader(s, Encoding.UTF8);
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != magic) throw new InvalidDataException(name + ": not a " + magic + " file");
                int version = r.ReadInt32();
                if (version != 1) throw new InvalidDataException(name + ": version " + version + ", this build reads 1");
                notice = Encoding.UTF8.GetString(Bytes(r, r.ReadInt32()));
                return r;
            }

            static void Springs(BinaryReader r, out float[] latLon, out byte[] kind)
            {
                int m = r.ReadInt32();
                latLon = Floats(r, 2 * m);
                kind = Bytes(r, m);
            }

            static float[] Floats(BinaryReader r, int count)
            {
                var a = new float[count];
                for (int i = 0; i < count; i++) a[i] = r.ReadSingle();
                return a;
            }

            static byte[] Bytes(BinaryReader r, int count)
            {
                var b = r.ReadBytes(count);
                if (b.Length != count) throw new EndOfStreamException("geothermal site file cut short");
                return b;
            }

            static void End(BinaryReader r, string name)
            {
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException(name + ": bytes after the lists");
            }
        }

        // Field initializers in this one part, in this order: the files first, then the arrays the rest of the class reads
        // (Geothermal's static constructor and Volcanoes.cs read only these, after every initializer has run).
        static readonly SiteFiles siteFiles = SiteFiles.Read();
        static readonly float[] GvpLatLon = siteFiles.GvpLatLon;
        static readonly byte[] GvpKind = siteFiles.GvpKind;
        static readonly string[] GvpName = siteFiles.GvpName;
        static readonly int[] GvpNumber = siteFiles.GvpNumber;
        static readonly byte[] GvpForm = siteFiles.GvpForm;
        static readonly short[] GvpElevation = siteFiles.GvpElevation;
        static readonly bool[] GvpEra = siteFiles.GvpEra;
        static readonly byte[] GvpMagma = siteFiles.GvpMagma;
        static readonly float[] GeoNamesSpringLatLon = siteFiles.GeoNamesSpringLatLon;
        static readonly byte[] GeoNamesSpringKind = siteFiles.GeoNamesSpringKind;
        static readonly float[] OsmSpringLatLon = siteFiles.OsmSpringLatLon;
        static readonly byte[] OsmSpringKind = siteFiles.OsmSpringKind;

        /// <summary>The sources' notices as the data files carry them (the volcano and GeoNames list's, and OpenStreetMap's ODbL
        /// notice).</summary>
        public static string SitesNotice => siteFiles.SitesNotice;
        public static string OsmNotice => siteFiles.OsmNotice;
    }
}
