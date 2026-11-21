using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0131 (decision 0061): streams-glo90-1.bin (tools/streams/bake_stream_tiles.py), the streams layer of worlds at 1:25
    /// and finer: the HydroRIVERS v1.0 reaches from 1 m³/s up to the large rivers' 20 m³/s (Lehner &amp; Grill 2013), without
    /// those GIRES v1.0 has stopping 30 days a year or more for want of water (kept where frost stops them), snapped onto
    /// GLO-90's valley floors with their levels from GLO-90 by tools/RiverSnap (pixel-is-point, as FineElevation reads it),
    /// the large rivers held where new worlds lay them, so each stream ends on its river.
    /// <para>Stored by stream system (a tree of streams down to its outlet: a large river, the sea or a sink, or the end of
    /// the data) in the 1° tile of its outlet, its home tile; each tile also lists the home tiles whose systems reach into it
    /// (with a margin), so a place's streams are its tile's and its visitors' home tiles' systems.</para>
    /// <para>Layout: "EMST", int32 version, int32 unit (vertex units per degree), int32 tile size (degrees), int32 tiles
    /// across, int32 tiles down; a state byte per tile (row 0 at 90°S, as LakeOutlineData); for each tile with data its
    /// offset and length (int32); each such tile's body brotli of: varint systems; per system varint end kind, varint
    /// reaches, per reach (in flow order) varint discharge (centi-m³/s), varint downstream reach + 1 in the system (0 at its
    /// outlet), varint vertices, their positions as zigzag varint steps in units (the first from the tile's south-west
    /// corner) and their levels (m) as zigzag varint steps (the first from 0); then varint visitors, each a zigzag varint
    /// step east and north (tiles) to its home tile.</para>
    /// </summary>
    public sealed class StreamData
    {
        public const string Magic = "EMST";
        /// <summary>The newest version this build reads.</summary>
        public const int FileVersion = 1;
        public const string FileName = "streams-glo90-1.bin";
        /// <summary>The version 1 file this build reads (size and SHA-256); any other is refused. (EA-0131: the bake of 17
        /// November 2026 on courses rules 4, not uploaded; it is baked again on EA-0148's courses before it goes up.)</summary>
        public const long FileBytes = 64_553_844;
        public const string FileSha256 = "37d36799e20ad8d21081c11818a021eb46bd23c60fc836bcf37a6a3afd07c276";
        /// <summary>Whether these bytes are the file this build reads.</summary>
        public static bool IsExpected(byte[] bytes, out string why) => LakeOutlineData.Matches(bytes, FileBytes, FileSha256, out why);
        public const byte NoData = 0, HasData = 1;
        /// <summary>A system's outlet: the end of the data (its downstream reach was left out), a large river, the sea or a
        /// sink (HydroRIVERS' outlets).</summary>
        public const byte EndFree = 0, EndRiver = 1, EndSea = 2;
        const int HeaderBytes = 24;

        public int Version { get; private set; }
        public int Unit { get; private set; }
        public int TileDeg { get; private set; }
        public int TilesX { get; private set; }
        public int TilesY { get; private set; }
        byte[] file, states;
        int[] offset, length;
        string name;

        /// <summary>One stream system: its outlet's kind and its reaches in flow order (each flowing into a later one, Next
        /// -1 at the outlet), with levels and no fixed vertices.</summary>
        public sealed class StreamSystem
        {
            public byte EndKind;
            public List<RiverReach> Reaches;
        }

        /// <summary>A tile's own systems and the home tiles (steps east and north, in tiles) whose systems reach into it.</summary>
        public sealed class Tile
        {
            public List<StreamSystem> Systems;
            public (int Dx, int Dy)[] Visitors;
        }

        public static StreamData Read(byte[] file, string name = FileName)
        {
            EarthBinary.Expect(file, Magic, HeaderBytes, name);
            int version = BitConverter.ToInt32(file, 4);
            if (version < 1 || version > FileVersion) throw new InvalidDataException(name + ": version " + version + ", this build reads 1 to " + FileVersion);
            var d = new StreamData
            {
                file = file, name = name, Version = version, Unit = BitConverter.ToInt32(file, 8), TileDeg = BitConverter.ToInt32(file, 12),
                TilesX = BitConverter.ToInt32(file, 16), TilesY = BitConverter.ToInt32(file, 20),
            };
            if (d.Unit <= 0 || d.TileDeg <= 0 || d.TilesX * d.TileDeg != 360 || d.TilesY * d.TileDeg != 180) throw new InvalidDataException(name + ": layout mismatch");
            int n = d.TilesX * d.TilesY;
            if (file.Length < HeaderBytes + n) throw new InvalidDataException(name + ": cut short");
            d.states = new byte[n];
            Buffer.BlockCopy(file, HeaderBytes, d.states, 0, n);
            d.offset = new int[n];
            d.length = new int[n];
            int o = HeaderBytes + n;
            for (int t = 0; t < n; t++)
            {
                if (d.states[t] != HasData) continue;
                if (o + 8 > file.Length) throw new InvalidDataException(name + ": cut short");
                d.offset[t] = BitConverter.ToInt32(file, o);
                d.length[t] = BitConverter.ToInt32(file, o + 4);
                if (d.offset[t] < 0 || d.length[t] <= 0 || (long)d.offset[t] + d.length[t] > file.Length) throw new InvalidDataException(name + ": bad tile index");
                o += 8;
            }
            return d;
        }

        /// <summary>The tile holding a point (row 0 at 90°S), or -1 outside the grid.</summary>
        public int TileAt(double lat, double lon)
        {
            double u = Geo.Wrap180(lon) + 180, v = lat + 90;
            int c = Math.Clamp((int)Math.Floor(u / TileDeg), 0, TilesX - 1), r = (int)Math.Floor(v / TileDeg);
            return r < 0 || r >= TilesY ? -1 : r * TilesX + c;
        }

        /// <summary>The tile a step of (dx, dy) tiles from another (east wrapping round the globe), or -1 beyond a pole.</summary>
        public int Step(int tile, int dx, int dy)
        {
            int r = tile / TilesX + dy, c = ((tile % TilesX + dx) % TilesX + TilesX) % TilesX;
            return r < 0 || r >= TilesY ? -1 : r * TilesX + c;
        }

        public byte State(int tile) => tile < 0 || tile >= states.Length ? NoData : states[tile];

        /// <summary>A tile's systems and visitors (an empty tile for one without data).</summary>
        public Tile Decode(int tile)
        {
            var t = new Tile { Systems = new List<StreamSystem>(), Visitors = Array.Empty<(int, int)>() };
            if (State(tile) != HasData) return t;
            byte[] b = EarthBinary.UnbrotliAll(file, offset[tile], length[tile], name + ": tile " + tile, out int size);
            int o = 0;
            int row = tile / TilesX, col = tile % TilesX;
            double lat0 = row * TileDeg - 90, lon0 = col * TileDeg - 180;
            ulong Var()
            {
                ulong v = 0;
                for (int shift = 0; ; shift += 7)
                {
                    if (o >= size || shift > 63) throw new InvalidDataException(name + ": tile " + tile + " cut short");
                    byte x = b[o++];
                    v |= (ulong)(x & 0x7f) << shift;
                    if (x < 0x80) return v;
                }
            }
            long Zig() { ulong v = Var(); return (long)(v >> 1) ^ -(long)(v & 1); }
            int systems = checked((int)Var());
            for (int s = 0; s < systems; s++)
            {
                var sys = new StreamSystem { EndKind = (byte)Var(), Reaches = new List<RiverReach>() };
                int reaches = checked((int)Var());
                for (int r = 0; r < reaches; r++)
                {
                    float q = Var() / 100f;
                    int next = checked((int)Var()) - 1;
                    if (next != -1 && (next <= r || next >= reaches)) throw new InvalidDataException(name + ": tile " + tile + " system " + s + " reach " + r + " flows into " + next);
                    int n = checked((int)Var());
                    var re = new RiverReach { Q = q, Next = next, Lon = new float[n], Lat = new float[n], Level = new short[n], Fixed = new bool[n] };
                    long x = 0, y = 0, lev = 0;
                    for (int k = 0; k < n; k++)
                    {
                        x += Zig(); y += Zig();
                        re.Lon[k] = (float)Geo.Wrap180(lon0 + (double)x / Unit);
                        re.Lat[k] = (float)(lat0 + (double)y / Unit);
                    }
                    for (int k = 0; k < n; k++)
                    {
                        lev += Zig();
                        re.Level[k] = (short)Math.Clamp(lev, short.MinValue, short.MaxValue);
                    }
                    sys.Reaches.Add(re);
                }
                t.Systems.Add(sys);
            }
            int visitors = checked((int)Var());
            t.Visitors = new (int, int)[visitors];
            for (int v = 0; v < visitors; v++) t.Visitors[v] = ((int)Zig(), (int)Zig());
            if (o != size) throw new InvalidDataException(name + ": tile " + tile + " has " + (size - o) + " bytes left over");
            return t;
        }
    }
}
