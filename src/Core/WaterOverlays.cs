using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// sea-referee.bin (tools/water_overlays.py; Earth 0053 item 14, tall.14): release/1.0's water atlas (717fb18,
    /// tools/sea_referee.py) as its differences from the published terrain.bin, islands.bin and coasts.bin. Where GSHHG draws
    /// sea over what Copernicus GLO-90's water mask has as land at least 15 m high or under a glacier (Patagonia's fjords up
    /// to where the glaciers were decades ago, the Rakaia, Vanuatu's Maewo), that is land, and outlines GSHHG has shifted by
    /// 1–4 km nearby are opened as sea where the mask has ocean. Version 2 (tall.15, tools/sea_ocean.py) makes the
    /// coastline the mask's everywhere the two disagree by more than a shoreline offset: GSHHG land on the mask's ocean is
    /// sea (GLO-90 is 0 m there: lagoons, channels and fjord heads that were flat land at sea level), GSHHG sea on the mask's
    /// land is land (islands at their place, spits, reclaimed land). GSHHG land the mask has no land near (islands the mask
    /// lacks, such as Donousa and Silhouette; reefs GSHHG draws as islands) stays. Its changed coast tiles and islands are stored as
    /// stretches of the base outlines and new vertices (ReadPieces). Worlds with EarthParams.SeaReferee read the data as
    /// the referee left it (EarthData.SetSeaReferee); older worlds keep the files as they are.
    /// The file names its base files and the referee's results by the first 8 bytes of their SHA-256: it is applied only to
    /// the files it was made from, and data that already has the referee needs nothing.
    /// </summary>
    public sealed class SeaRefereeData
    {
        /// <summary>1: the referee (tall.14, never released); 2: with the mask's coastline (tall.15). The mod reads only its own.</summary>
        public const int FileVersion = 2;
        /// <summary>terrain.bin, islands.bin and coasts.bin: hashes of the files it applies to, and of the referee's.</summary>
        public readonly ulong[] Base = new ulong[3], Target = new ulong[3];
        readonly byte[] packed;
        readonly int gridWidth;
        bool loaded;
        readonly object loadLock = new object();

        /// <summary>The terrain grid cells the referee changed (as grid patches, applied before every other overlay).</summary>
        public IslandData.Patch[] TerrainPatches { get; private set; }
        HashSet<long> removedIslandCells;
        IslandData.Patch[] islandPatches;
        /// <summary>The refereed island list: an old island's index, or -1 with the island in newIslands.</summary>
        int[] islandSource;
        (float Area, float Height, float Width, Piece[] Outline)[] newIslands;
        Dictionary<int, CoastTile> coastTiles;

        SeaRefereeData(byte[] packed, int gridWidth) { this.packed = packed; this.gridWidth = gridWidth; }

        /// <summary>The first 8 bytes of a file's SHA-256 (EarthBinary.Sha8), how this file names its bases.</summary>
        public static ulong Hash(byte[] file) => EarthBinary.Sha8(file);

        /// <summary>Reads the header; the differences are unpacked on first use (Load).</summary>
        public static SeaRefereeData Read(Stream s, int gridWidth)
        {
            byte[] f = EarthBinary.ReadAll(s);
            EarthBinary.Expect(f, "EMSR", 60, "sea-referee.bin");
            if (BitConverter.ToInt32(f, 4) != FileVersion) throw new InvalidDataException("sea-referee.bin: version mismatch");
            if (BitConverter.ToInt32(f, 8) != gridWidth) throw new InvalidDataException("sea-referee.bin: layout mismatch");
            var d = new SeaRefereeData(f, gridWidth);
            for (int i = 0; i < 3; i++)
            {
                d.Base[i] = BitConverter.ToUInt64(f, 12 + 8 * i);
                d.Target[i] = BitConverter.ToUInt64(f, 36 + 8 * i);
            }
            return d;
        }

        public void Load()
        {
            lock (loadLock)
            {
                if (loaded) return;
                byte[] b = EarthBinary.UnbrotliAll(packed, 60, "sea-referee.bin");
                int o = 0;
                TerrainPatches = IslandData.ReadPatches(b, ref o, (int)EarthBinary.ReadVarint(b, ref o), gridWidth);
                int nRemoved = (int)EarthBinary.ReadVarint(b, ref o);
                removedIslandCells = new HashSet<long>();
                long cell = 0;
                for (int i = 0; i < nRemoved; i++) removedIslandCells.Add(cell += (long)EarthBinary.ReadVarint(b, ref o));
                islandPatches = IslandData.ReadPatches(b, ref o, (int)EarthBinary.ReadVarint(b, ref o), gridWidth);
                int nIslands = (int)EarthBinary.ReadVarint(b, ref o);
                islandSource = new int[nIslands];
                var inline = new List<(float Area, float Height, float Width, Piece[] Outline)>();
                int prev = -1;
                for (int i = 0; i < nIslands; i++)
                {
                    int tag = (int)EarthBinary.ReadVarint(b, ref o);
                    if (tag > 0) { islandSource[i] = prev += tag; continue; }
                    islandSource[i] = -1;
                    float area = BitConverter.ToSingle(b, o), height = BitConverter.ToSingle(b, o + 4), width = BitConverter.ToSingle(b, o + 8);
                    o += 12;
                    inline.Add((area, height, width, ReadPieces(b, ref o)));
                }
                newIslands = inline.ToArray();
                int nTiles = (int)EarthBinary.ReadVarint(b, ref o);
                coastTiles = new Dictionary<int, CoastTile>();
                int tile = 0;
                for (int i = 0; i < nTiles; i++)
                {
                    tile += (int)EarthBinary.ReadVarint(b, ref o);
                    var ct = new CoastTile { State = b[o++] };
                    if (ct.State != CoastData.NoData && (ct.State & CoastData.HasEdges) != 0)
                    {
                        ct.Runs = new Piece[(int)EarthBinary.ReadVarint(b, ref o)][];
                        for (int r = 0; r < ct.Runs.Length; r++) ct.Runs[r] = ReadPieces(b, ref o);
                    }
                    coastTiles[tile] = ct;
                }
                if (o != b.Length) throw new InvalidDataException("sea-referee.bin: trailing data");
                loaded = true;
            }
        }

        /// <summary>The islands and their grid patches as the referee left them.</summary>
        public IslandData ApplyTo(IslandData islands)
        {
            Load();
            var patches = new SortedDictionary<long, IslandData.Patch>();
            foreach (var p in islands.Patches)
            {
                long cell = (long)p.Row * gridWidth + p.Col;
                if (!removedIslandCells.Contains(cell)) patches[cell] = p;
            }
            foreach (var p in islandPatches) patches[(long)p.Row * gridWidth + p.Col] = p;
            var list = new IslandData.Island[islandSource.Length];
            int k = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (islandSource[i] >= 0) { list[i] = islands.Islands[islandSource[i]]; continue; }
                var (area, height, width, outline) = newIslands[k++];
                var isl = new IslandData.Island { AreaKm2 = area, Height = height, WidthKm = width };
                var lon = new List<double>();
                var lat = new List<double>();
                long qx = 0, qy = 0;
                foreach (var p in outline)
                {
                    if (p.Run < 0)
                    {
                        for (int m = 0; m < p.Count; m++)
                        {
                            qx += p.Dx[m];
                            qy += p.Dy[m];
                            lon.Add(qx * 1e-5);
                            lat.Add(qy * 1e-5);
                        }
                        continue;
                    }
                    var src = p.Run < islands.Islands.Length ? islands.Islands[p.Run] : null;
                    int end = p.Start + (p.Back ? 1 - p.Count : p.Count - 1);
                    if (src == null || p.Count < 1 || Math.Min(p.Start, end) < 0 || Math.Max(p.Start, end) >= src.Lon.Length)
                        throw new InvalidDataException("sea-referee.bin: an island copies outside the base islands");
                    for (int m = 0; m < p.Count; m++)
                    {
                        int j = p.Start + (p.Back ? -m : m);
                        lon.Add(src.Lon[j]);
                        lat.Add(src.Lat[j]);
                    }
                    // the base islands' vertices are whole 1e-5° steps
                    qx = (long)Math.Round(lon[lon.Count - 1] * 1e5);
                    qy = (long)Math.Round(lat[lat.Count - 1] * 1e5);
                }
                isl.Lon = lon.ToArray();
                isl.Lat = lat.ToArray();
                isl.MinLon = isl.MinLat = double.MaxValue;
                isl.MaxLon = isl.MaxLat = double.MinValue;
                for (int m = 0; m < isl.Lon.Length; m++)
                {
                    isl.MinLon = Math.Min(isl.MinLon, isl.Lon[m]);
                    isl.MaxLon = Math.Max(isl.MaxLon, isl.Lon[m]);
                    isl.MinLat = Math.Min(isl.MinLat, isl.Lat[m]);
                    isl.MaxLat = Math.Max(isl.MaxLat, isl.Lat[m]);
                }
                list[i] = isl;
            }
            var arr = new IslandData.Patch[patches.Count];
            patches.Values.CopyTo(arr, 0);
            return IslandData.Create(list, arr);
        }

        /// <summary>The coast outlines as the referee left them: each changed tile's runs put together from stretches of the
        /// base tile's runs and the vertices given.</summary>
        public CoastData ApplyTo(CoastData coasts)
        {
            Load();
            var tiles = new Dictionary<int, (byte, int[], int[], int[])>();
            foreach (var kv in coastTiles)
            {
                var ct = kv.Value;
                if (ct.Runs == null) { tiles[kv.Key] = (ct.State, null, null, null); continue; }
                int[] oRuns = Array.Empty<int>(), ox = Array.Empty<int>(), oy = Array.Empty<int>();
                byte old = coasts.State(kv.Key);
                if (old != CoastData.NoData && (old & CoastData.HasEdges) != 0) coasts.Decode(kv.Key, out oRuns, out ox, out oy);
                var first = new int[oRuns.Length];
                for (int r = 1; r < oRuns.Length; r++) first[r] = first[r - 1] + oRuns[r - 1];
                var runs = new int[ct.Runs.Length];
                var xs = new List<int>();
                var ys = new List<int>();
                for (int r = 0; r < ct.Runs.Length; r++)
                {
                    int before = xs.Count;
                    long px = 0, py = 0;
                    foreach (var p in ct.Runs[r])
                    {
                        if (p.Run < 0)
                        {
                            for (int m = 0; m < p.Count; m++)
                            {
                                px += p.Dx[m];
                                py += p.Dy[m];
                                xs.Add((int)px);
                                ys.Add((int)py);
                            }
                            continue;
                        }
                        int end = p.Start + (p.Back ? 1 - p.Count : p.Count - 1);
                        if (p.Run >= oRuns.Length || p.Count < 1 || Math.Min(p.Start, end) < 0 || Math.Max(p.Start, end) >= oRuns[p.Run])
                            throw new InvalidDataException("sea-referee.bin: coast tile " + kv.Key + " copies outside its base tile");
                        for (int m = 0; m < p.Count; m++)
                        {
                            int k = first[p.Run] + p.Start + (p.Back ? -m : m);
                            xs.Add(ox[k]);
                            ys.Add(oy[k]);
                        }
                        px = xs[xs.Count - 1];
                        py = ys[ys.Count - 1];
                    }
                    runs[r] = xs.Count - before;
                }
                tiles[kv.Key] = (ct.State, runs, xs.ToArray(), ys.ToArray());
            }
            return coasts.WithTiles(tiles);
        }

        /// <summary>A changed coast tile: its state and, when it has edges, its runs as pieces.</summary>
        sealed class CoastTile
        {
            public byte State;
            public Piece[][] Runs;
        }

        /// <summary>A stretch of base run Run (Count vertices from Start, backwards if Back), or with Run -1 Count vertices
        /// given as steps from the vertex before (the run's first from the tile's corner).</summary>
        struct Piece
        {
            public int Run, Start, Count;
            public bool Back;
            public int[] Dx, Dy;
        }

        /// <summary>A vertex list as pieces (tools/water_overlays.py encode_pieces): count, then per piece 0, n and n steps,
        /// or source+1, start and 2*length+backwards.</summary>
        static Piece[] ReadPieces(byte[] b, ref int o)
        {
            var pieces = new Piece[(int)EarthBinary.ReadVarint(b, ref o)];
            for (int q = 0; q < pieces.Length; q++)
            {
                int tag = (int)EarthBinary.ReadVarint(b, ref o);
                if (tag == 0)
                {
                    int n = (int)EarthBinary.ReadVarint(b, ref o);
                    var dx = new int[n];
                    var dy = new int[n];
                    for (int m = 0; m < n; m++)
                    {
                        dx[m] = (int)EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                        dy[m] = (int)EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                    }
                    pieces[q] = new Piece { Run = -1, Count = n, Dx = dx, Dy = dy };
                }
                else
                {
                    int start = (int)EarthBinary.ReadVarint(b, ref o), len = (int)EarthBinary.ReadVarint(b, ref o);
                    pieces[q] = new Piece { Run = tag - 1, Start = start, Count = len >> 1, Back = (len & 1) != 0 };
                }
            }
            return pieces;
        }
    }

    /// <summary>
    /// rivers-glo90-snap.bin (tools/water_overlays.py; Earth 0053 item 14, tall.14): the reaches of rivers-glo90.bin that
    /// release/1.0's water atlas (tools/snap_rivers_wbm.py) moved into the water GLO-90's water mask shows beside them (from
    /// 100 m³/s; ends, joins, discharges and the network's order unchanged), in rivers.bin's record format. River courses
    /// rules 2 (RiverNetwork.Glo90SnapCourses) read rivers-glo90.bin with these applied.
    ///
    /// Version 2 (rivers-glo90-snap-2.bin; 0063, tools/snap_rivers_channel.py): every reach from 20 m³/s moved onto the
    /// mask's water near it, smoothed off the pixel centres, and the mask's half width at every vertex (RiverReach.Width).
    /// A moved reach keeps its discharge, next reach and end vertices (flags too); its inner vertices lie on a
    /// 1/Grid degree grid and are stored column by column as whole steps (ApplyTo). The reaches left where they were and
    /// having any width get one width byte per vertex. River courses rules 4 (RiverNetwork.Glo90WidthCourses) read
    /// rivers-glo90.bin with these applied.
    /// </summary>
    public sealed class RiverSnapData
    {
        /// <summary>The newest version this build reads; it reads 1 as well.</summary>
        public const int FileVersion = 2;
        public readonly int Version, BaseCount, Changed, WidthOnly;
        /// <summary>Version 2: the moved reaches' inner vertices lie on multiples of 1/Grid degrees (65536: exact in float).</summary>
        public readonly int Grid;
        /// <summary>rivers-glo90.bin's hash (first 8 bytes of its SHA-256) before and after.</summary>
        public readonly ulong Base, Target;
        readonly byte[] packed;
        readonly int payload;

        RiverSnapData(byte[] packed)
        {
            this.packed = packed;
            Version = BitConverter.ToInt32(packed, 4);
            BaseCount = BitConverter.ToInt32(packed, 8);
            Changed = BitConverter.ToInt32(packed, 12);
            Base = BitConverter.ToUInt64(packed, 16);
            Target = BitConverter.ToUInt64(packed, 24);
            WidthOnly = Version >= 2 ? BitConverter.ToInt32(packed, 32) : 0;
            Grid = Version >= 2 ? BitConverter.ToInt32(packed, 36) : 0;
            payload = Version >= 2 ? 40 : 32;
        }

        public static RiverSnapData Read(Stream s)
        {
            byte[] f = EarthBinary.ReadAll(s);
            EarthBinary.Expect(f, "EMRP", 32, "rivers-glo90-snap.bin");
            int v = BitConverter.ToInt32(f, 4);
            if (v < 1 || v > FileVersion || (v >= 2 && (f.Length < 40 || BitConverter.ToInt32(f, 36) <= 0 || BitConverter.ToInt32(f, 36) > 1 << 16)))
                throw new InvalidDataException("rivers-glo90-snap.bin: version mismatch");
            return new RiverSnapData(f);
        }

        /// <summary>Whether it applies to (or is already in) the rivers file with this hash.</summary>
        public bool Fits(ulong riversHash) => riversHash == Base || riversHash == Target;

        /// <summary>Replaces the moved reaches in the list read from the rivers file with this hash.</summary>
        public void ApplyTo(List<RiverReach> reaches, ulong riversHash)
        {
            if (riversHash == Target) return;
            if (riversHash != Base || reaches.Count != BaseCount)
                throw new InvalidDataException("rivers-glo90-snap.bin was made for another rivers-glo90.bin (the installed height data packs differ from the ones this Earth build asks for)");
            byte[] b = EarthBinary.UnbrotliAll(packed, payload, "rivers-glo90-snap.bin");
            int o = 0;
            var index = new int[Changed];
            int r = 0;
            for (int i = 0; i < Changed; i++) index[i] = r += (int)EarthBinary.ReadVarint(b, ref o);
            var widthOnly = new int[WidthOnly];
            r = 0;
            for (int i = 0; i < WidthOnly; i++) widthOnly[i] = r += (int)EarthBinary.ReadVarint(b, ref o);
            if (Version >= 2) ReadMoved(b, ref o, reaches, index);
            else for (int i = 0; i < Changed; i++) reaches[index[i]] = RiverData.ReadReach(b, ref o, 2);
            foreach (int i in widthOnly)
            {
                var re = reaches[i];
                int n = re.Lon.Length;
                if (o + n > b.Length) throw new InvalidDataException("rivers-glo90-snap-2.bin: widths past the end");
                re.Width = new byte[n];
                Buffer.BlockCopy(b, o, re.Width, 0, n);
                o += n;
            }
            if (o != b.Length) throw new InvalidDataException("rivers-glo90-snap.bin: trailing data");
        }

        /// <summary>Version 2's moved reaches (tools/water_overlays.py build_snap2): per reach its vertex count; then column
        /// by column: the inner vertices' longitudes and latitudes in grid steps (zigzag deltas, each reach's first from its
        /// base first vertex floored onto the grid), the levels of every vertex after the first (zigzag deltas from the
        /// first's; carried levels never rise, so the last can be below the base's), the inner vertices' lake/sea flags (a
        /// byte each); then a width byte for every vertex. Discharge, next reach and both end vertices' positions and flags
        /// are the base's.</summary>
        void ReadMoved(byte[] b, ref int o, List<RiverReach> reaches, int[] index)
        {
            var moved = new RiverReach[Changed];
            for (int i = 0; i < Changed; i++)
            {
                var re = reaches[index[i]];
                int n = (int)EarthBinary.ReadVarint(b, ref o);
                if (n < 2 || re.Lon.Length < 2) throw new InvalidDataException("rivers-glo90-snap-2.bin: a moved reach without ends");
                var m = new RiverReach { Q = re.Q, Next = re.Next, Lon = new float[n], Lat = new float[n], Level = new short[n], Fixed = new bool[n], Width = new byte[n] };
                int last = re.Lon.Length - 1;
                m.Lon[0] = re.Lon[0]; m.Lat[0] = re.Lat[0]; m.Level[0] = re.Level[0]; m.Fixed[0] = re.Fixed[0];
                m.Lon[n - 1] = re.Lon[last]; m.Lat[n - 1] = re.Lat[last]; m.Level[n - 1] = re.Level[last]; m.Fixed[n - 1] = re.Fixed[last];
                moved[i] = m;
            }
            for (int axis = 0; axis < 2; axis++)
                foreach (var m in moved)
                {
                    var a = axis == 0 ? m.Lon : m.Lat;
                    long k = (long)Math.Floor((double)a[0] * Grid);
                    for (int j = 1; j < a.Length - 1; j++)
                    {
                        k += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o));
                        a[j] = (float)(k / (double)Grid);
                    }
                }
            foreach (var m in moved)
            {
                long lev = m.Level[0];
                for (int j = 1; j < m.Level.Length; j++) m.Level[j] = (short)(lev += EarthBinary.UnZigzag(EarthBinary.ReadVarint(b, ref o)));
            }
            foreach (var m in moved)
                for (int j = 1; j < m.Fixed.Length - 1; j++)
                {
                    if (o >= b.Length) throw new InvalidDataException("rivers-glo90-snap-2.bin: flags past the end");
                    m.Fixed[j] = b[o++] != 0;
                }
            foreach (var m in moved)
            {
                if (o + m.Width.Length > b.Length) throw new InvalidDataException("rivers-glo90-snap-2.bin: widths past the end");
                Buffer.BlockCopy(b, o, m.Width, 0, m.Width.Length);
                o += m.Width.Length;
            }
            for (int i = 0; i < Changed; i++) reaches[index[i]] = moved[i];
        }
    }

    /// <summary>
    /// rivers-glo90-arms.bin (tools/water_overlays.py; Earth 0053 item 16, tall.16): release/1.0's river side arms
    /// (tools/river_arms.py, e5d1d75) built on the snapped rivers on GLO-90. HydroRIVERS has one line per river, so where a
    /// river splits round an island (Doles sala on the Daugava, Yangzhong on the Yangtze) the other arm was missing; the
    /// water mask shows it, and each arm is a reach of its own from a vertex of the main course to where it rejoins. The file
    /// holds the arms' records and the vertices where a reach is split because an arm joins it there. The list keeps the
    /// game's flow order (every reach flows into a later one): the arms first (nothing flows into them), then each reach
    /// followed by its later pieces. River courses rules 3 (RiverNetwork.Glo90ArmsCourses) read rivers-glo90.bin with the
    /// snap and then these applied.
    ///
    /// Version 2 (rivers-glo90-arms-2.bin; 0063): the same tool on version 2 of the snap, the arms as record version 3 with
    /// the mask's half width at each vertex; a split reach's pieces keep their widths. River courses rules 4.
    /// </summary>
    public sealed class RiverArmsData
    {
        /// <summary>The newest version this build reads; it reads 1 as well.</summary>
        public const int FileVersion = 2;
        public readonly int Version, BaseCount, Arms, SplitReaches;
        /// <summary>The snapped rivers' hash (RiverSnapData.Target) and the armed rivers' (first 8 bytes of the SHA-256).</summary>
        public readonly ulong Base, Target;
        readonly byte[] packed;

        RiverArmsData(byte[] packed)
        {
            this.packed = packed;
            Version = BitConverter.ToInt32(packed, 4);
            BaseCount = BitConverter.ToInt32(packed, 8);
            Arms = BitConverter.ToInt32(packed, 12);
            SplitReaches = BitConverter.ToInt32(packed, 16);
            Base = BitConverter.ToUInt64(packed, 20);
            Target = BitConverter.ToUInt64(packed, 28);
        }

        public static RiverArmsData Read(Stream s)
        {
            byte[] f = EarthBinary.ReadAll(s);
            EarthBinary.Expect(f, "EMRA", 36, "rivers-glo90-arms.bin");
            int v = BitConverter.ToInt32(f, 4);
            if (v < 1 || v > FileVersion) throw new InvalidDataException("rivers-glo90-arms.bin: version mismatch");
            return new RiverArmsData(f);
        }

        /// <summary>Whether it was made on the rivers this snap overlay gives.</summary>
        public bool Fits(RiverSnapData snap) => snap != null && snap.Target == Base;

        /// <summary>The armed list from the snapped one (a new list; the reaches that aren't split share their arrays).</summary>
        public List<RiverReach> ApplyTo(List<RiverReach> snapped)
        {
            if (snapped.Count != BaseCount) throw new InvalidDataException("rivers-glo90-arms.bin was made for other rivers on GLO-90");
            byte[] b = EarthBinary.UnbrotliAll(packed, 36, "rivers-glo90-arms.bin");
            int o = 0;
            var splits = new Dictionary<int, int[]>(SplitReaches);
            int r = 0;
            for (int i = 0; i < SplitReaches; i++)
            {
                r += (int)EarthBinary.ReadVarint(b, ref o);
                var cuts = new int[(int)EarthBinary.ReadVarint(b, ref o)];
                int c = 0;
                for (int k = 0; k < cuts.Length; k++) cuts[k] = c += (int)EarthBinary.ReadVarint(b, ref o);
                splits[r] = cuts;
            }
            // each old reach's first piece in the new numbering
            var first = new int[BaseCount];
            int t = Arms;
            for (int i = 0; i < BaseCount; i++)
            {
                first[i] = t;
                t += 1 + (splits.TryGetValue(i, out var cs) ? cs.Length : 0);
            }
            var list = new List<RiverReach>(t);
            for (int i = 0; i < Arms; i++) list.Add(RiverData.ReadReach(b, ref o, Version >= 2 ? 3 : 2));
            if (o != b.Length) throw new InvalidDataException("rivers-glo90-arms.bin: trailing data");
            for (int i = 0; i < BaseCount; i++)
            {
                var re = snapped[i];
                int next = re.Next >= 0 ? first[re.Next] : -1;
                if (!splits.TryGetValue(i, out var cuts))
                {
                    list.Add(new RiverReach { Q = re.Q, Next = next, Lon = re.Lon, Lat = re.Lat, Level = re.Level, Fixed = re.Fixed, Width = re.Width });
                    continue;
                }
                int from = 0;
                for (int j = 0; j <= cuts.Length; j++)
                {
                    int to = j < cuts.Length ? cuts[j] : re.Lon.Length - 1;
                    list.Add(new RiverReach
                    {
                        Q = re.Q, Next = j < cuts.Length ? first[i] + j + 1 : next,
                        Lon = Slice(re.Lon, from, to), Lat = Slice(re.Lat, from, to), Level = Slice(re.Level, from, to), Fixed = Slice(re.Fixed, from, to),
                        Width = Slice(re.Width, from, to)
                    });
                    from = to;
                }
            }
            if (list.Count != t) throw new InvalidDataException("rivers-glo90-arms.bin: reach count");
            return list;
        }

        /// <summary>a[from..to] (inclusive) as a new array; null for null (a reach without widths).</summary>
        static T[] Slice<T>(T[] a, int from, int to)
        {
            if (a == null) return null;
            var s = new T[to - from + 1];
            Array.Copy(a, from, s, 0, s.Length);
            return s;
        }
    }
}
