using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// shores-osm-1.bin (tools/bake_shore_material.py, decision 0070): OpenStreetMap's beaches (natural=beach and
    /// natural=shingle, © OpenStreetMap contributors, ODbL) on a 6″ grid per 1° square, each cell the class of the beach on
    /// it (ShoreMaterial: tagged surfaces before untagged beaches), grown by a cell so the terrain's own shore line finds it.
    /// <code>
    /// "EMSH", int32 version 1, int32 cells per degree, int32 squares; per square int16 south, int16 west, int32 offset,
    /// int32 length; then each square's brotli raster (cells per degree², rows from the north). Little-endian.
    /// </code>
    /// Squares are unpacked on first use and a bounded number kept.
    /// </summary>
    public sealed class ShoreMaterialData
    {
        public const string FileName = "shores-osm-1.bin", Magic = "EMSH";
        public const int FileVersion = 1;
        const int KeepSquares = 48;

        public readonly int CellsPerDegree;
        readonly byte[] file;
        readonly Dictionary<int, (int Offset, int Length)> index = new();
        readonly ConcurrentDictionary<int, byte[]> squares = new();
        public int Squares => index.Count;

        ShoreMaterialData(byte[] file, int cpd)
        {
            this.file = file;
            CellsPerDegree = cpd;
        }

        static int Key(int latS, int lonW) => (latS + 90) * 360 + (lonW + 180);

        public static ShoreMaterialData Read(byte[] file)
        {
            EarthBinary.Expect(file, Magic, 16, FileName);
            int version = BitConverter.ToInt32(file, 4);
            if (version != FileVersion) throw new InvalidOperationException($"{FileName}: version {version}, this build reads {FileVersion}");
            int cpd = BitConverter.ToInt32(file, 8), n = BitConverter.ToInt32(file, 12);
            if (cpd < 1 || cpd > 3600 || n < 0 || 16 + 12L * n > file.Length) throw new InvalidOperationException($"{FileName}: bad header");
            var d = new ShoreMaterialData(file, cpd);
            for (int i = 0; i < n; i++)
            {
                int at = 16 + 12 * i;
                int lat = BitConverter.ToInt16(file, at), lon = BitConverter.ToInt16(file, at + 2);
                int off = BitConverter.ToInt32(file, at + 4), len = BitConverter.ToInt32(file, at + 8);
                if (off < 16 + 12 * n || len < 0 || (long)off + len > file.Length) throw new InvalidOperationException($"{FileName}: square {lat},{lon} out of the file");
                d.index[Key(lat, lon)] = (off, len);
            }
            return d;
        }

        /// <summary>The class of OSM's beach at a place: ShoreMaterial.None where it has none.</summary>
        public byte At(double lat, double lon)
        {
            lon = Geo.Wrap180(lon);
            int latS = (int)Math.Floor(lat), lonW = (int)Math.Floor(lon);
            if (latS < -90 || latS > 89) return ShoreMaterial.None;
            if (lonW >= 180) lonW = 179;
            int key = Key(latS, lonW);
            if (!index.TryGetValue(key, out var at)) return ShoreMaterial.None;
            if (!squares.TryGetValue(key, out var cells))
            {
                cells = EarthBinary.UnbrotliAll(file, at.Offset, at.Length, FileName, CellsPerDegree * CellsPerDegree);
                if (cells.Length != CellsPerDegree * CellsPerDegree) throw new InvalidOperationException($"{FileName}: square {latS},{lonW} has {cells.Length} cells");
                if (squares.Count >= KeepSquares) squares.Clear();
                squares[key] = cells;
            }
            int row = Math.Clamp((int)((latS + 1 - lat) * CellsPerDegree), 0, CellsPerDegree - 1);
            int col = Math.Clamp((int)((lon - lonW) * CellsPerDegree), 0, CellsPerDegree - 1);
            return cells[row * CellsPerDegree + col];
        }
    }
}
