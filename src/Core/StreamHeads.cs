using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace EarthMap.Core
{
    /// <summary>
    /// The heads of the world's smallest perennial streams (tools/bake_stream_heads.py, earthdata/stream-heads-1.bin), where
    /// springs rules 2 ("streams", decision 0063 rule 5) put a spring: HydroRIVERS v1.0's reaches nothing flows into, whose
    /// mean discharge where 10 km² drain to them (Q10) is at least 0.032 m³/s (101 mm of runoff a year; drier heads run
    /// only part of the year and are left out by the bake). Each head is HydroSHEDS' 15" cell it starts in, with a
    /// discharge class. Pure: the game and the offline probes read the same file the same way.
    /// </summary>
    public sealed class StreamHeads
    {
        public const string FileName = "stream-heads-1.bin";
        public const int FileVersion = 1;
        const int Rows = 180, Cols = 360;

        /// <summary>Cells per degree (240: HydroSHEDS' 15" grid), the least class in the file, and classes per doubling of Q10.</summary>
        public readonly int CellsPerDegree, FloorClass, ClassesPerDoubling;
        /// <summary>Q10 (m³/s) of class 0.</summary>
        public const double ClassZero = 0.001;
        /// <summary>The first head of each 1-degree tile (row from the north, column from 180° W), and one past the last.</summary>
        readonly int[] start;
        readonly ushort[] cells;
        readonly byte[] classes;

        public int Count => cells.Length;

        StreamHeads(int cellsPerDegree, int floorClass, int classesPerDoubling, int[] start, ushort[] cells, byte[] classes)
        {
            CellsPerDegree = cellsPerDegree; FloorClass = floorClass; ClassesPerDoubling = classesPerDoubling;
            this.start = start; this.cells = cells; this.classes = classes;
        }

        /// <summary>Reads stream-heads-1.bin; throws InvalidDataException when it isn't that file.</summary>
        public static StreamHeads Read(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] file = ms.ToArray();
            if (file.Length < 24 || file[0] != 'E' || file[1] != 'M' || file[2] != 'S' || file[3] != 'H')
                throw new InvalidDataException(FileName + ": not a stream heads file");
            int version = BitConverter.ToInt32(file, 4), perDegree = BitConverter.ToInt32(file, 8), count = BitConverter.ToInt32(file, 12);
            int floor = BitConverter.ToInt32(file, 16), steps = BitConverter.ToInt32(file, 20);
            if (version != FileVersion) throw new InvalidDataException(FileName + ": version " + version + ", expected " + FileVersion);
            if (perDegree < 1 || perDegree * perDegree > 65536 || count < 0 || steps < 1 || floor < 0 || floor > 255)
                throw new InvalidDataException(FileName + ": bad header");
            byte[] body;
            using (var dec = new BrotliStream(new MemoryStream(file, 24, file.Length - 24), CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                dec.CopyTo(o);
                body = o.ToArray();
            }
            int p = 0;
            int Varint()
            {
                int v = 0, shift = 0;
                while (true)
                {
                    if (p >= body.Length || shift > 28) throw new InvalidDataException(FileName + ": truncated");
                    byte b = body[p++];
                    v |= (b & 0x7F) << shift;
                    // (a fifth byte past 3 bits would overflow into the sign: no count or cell of this file is that large)
                    if (b < 0x80) return v >= 0 ? v : throw new InvalidDataException(FileName + ": a number out of range");
                    shift += 7;
                }
            }
            var start = new int[Rows * Cols + 1];
            for (int t = 0; t < Rows * Cols; t++)
            {
                int n = Varint();
                if (n > count - start[t]) throw new InvalidDataException(FileName + ": the tiles hold more heads than the header says (" + count + ")");
                start[t + 1] = start[t] + n;
            }
            if (start[Rows * Cols] != count) throw new InvalidDataException(FileName + ": the tiles hold " + start[Rows * Cols] + " heads, the header says " + count);
            var cells = new ushort[count];
            int limit = perDegree * perDegree;
            for (int t = 0; t < Rows * Cols; t++)
            {
                int cell = 0;
                for (int k = start[t]; k < start[t + 1]; k++)
                {
                    cell = k == start[t] ? Varint() : cell + Varint();
                    if (cell < 0 || cell >= limit) throw new InvalidDataException(FileName + ": a cell outside its tile");
                    cells[k] = (ushort)cell;
                }
            }
            if (body.Length - p != count) throw new InvalidDataException(FileName + ": " + (body.Length - p) + " class bytes for " + count + " heads");
            var classes = new byte[count];
            Buffer.BlockCopy(body, p, classes, 0, count);
            return new StreamHeads(perDegree, floor, steps, start, cells, classes);
        }

        /// <summary>Q10 (m³/s) of a discharge class.</summary>
        public double Discharge(int cls) => ClassZero * Math.Pow(2, cls / (double)ClassesPerDoubling);

        public struct Head
        {
            /// <summary>The head's index in the file (stable for a file version).</summary>
            public int Id;
            /// <summary>The centre of its cell; Lon in the running longitude of the query (may lie past ±180).</summary>
            public double Lat, Lon;
            public byte Class;
        }

        /// <summary>
        /// Adds the heads whose cell centre lies in the box to <paramref name="into"/>. The longitudes may run past ±180
        /// (lonW &lt;= lonE, at most 360 apart): each head comes back at the longitude inside the box.
        /// </summary>
        public void Find(double latS, double latN, double lonW, double lonE, List<Head> into)
        {
            if (!(latN >= latS) || !(lonE >= lonW)) return;
            if (lonE - lonW > 360) lonE = lonW + 360;
            double c = CellsPerDegree;
            int r0 = Math.Max(0, (int)Math.Floor(90 - Math.Min(90, latN))), r1 = Math.Min(Rows - 1, (int)Math.Floor(90 - Math.Max(-90, latS)));
            int c0 = (int)Math.Floor(lonW + 180), c1 = (int)Math.Floor(lonE + 180);
            for (int tr = r0; tr <= r1; tr++)
            {
                double north = 90 - tr;
                int rowLo = Math.Max(0, (int)Math.Ceiling((north - latN) * c - 0.5)), rowHi = Math.Min(CellsPerDegree - 1, (int)Math.Floor((north - latS) * c - 0.5));
                if (rowLo > rowHi) continue;
                for (int tc = c0; tc <= c1; tc++)
                {
                    int t = tr * Cols + ((tc % Cols) + Cols) % Cols;
                    int a = start[t], b = start[t + 1];
                    if (a == b) continue;
                    double west = tc - 180;
                    int colLo = Math.Max(0, (int)Math.Ceiling((lonW - west) * c - 0.5)), colHi = Math.Min(CellsPerDegree - 1, (int)Math.Floor((lonE - west) * c - 0.5));
                    if (colLo > colHi) continue;
                    for (int row = rowLo; row <= rowHi; row++)
                    {
                        int lo = row * CellsPerDegree + colLo, hi = row * CellsPerDegree + colHi;
                        int k = LowerBound(a, b, lo);
                        for (; k < b && cells[k] <= hi; k++)
                        {
                            int col = cells[k] % CellsPerDegree;
                            into.Add(new Head { Id = k, Lat = north - (row + 0.5) / c, Lon = west + (col + 0.5) / c, Class = classes[k] });
                        }
                    }
                }
            }
        }

        int LowerBound(int a, int b, int value)
        {
            while (a < b)
            {
                int m = (a + b) >> 1;
                if (cells[m] < value) a = m + 1; else b = m;
            }
            return a;
        }
    }
}
