using System;
using System.Diagnostics;
using System.IO;
using EarthMap.Core;

static class Program
{
    static int Main(string[] args)
    {
        string dataPath = args[0];
        var data = EarthData.LoadFromFolder(dataPath, true);
        var geo = EarthLayout.Build(new LayoutInput { WorldWidth = 256000, CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = 51200, CwPoleDistance = 51152, CwBandInner = 608, GutterMargin = 512 }, data, out _);
        foreach (int height in new[] { 256, 1024 })
        {
            int sea = height == 256 ? 110 : 440;
            var vertical = VerticalScale.ForWorld(height, sea);
            var m = new TerrainModel(data, geo, vertical, 290142474);
            m.SealTerrainReservations();
            Console.WriteLine("map height {0}, sea {1}: elevation at build limit {2:0} m", height, sea, vertical.Elevation(height - 2));
            foreach (var (name, lat, lon) in new[] { ("Alps", 45.83, 6.86), ("Amazon", -3.1, -60.0), ("Sahara", 23.0, 10.0), ("Baltic coast", 57.0, 24.0) })
            {
                geo.FromGeo(lat, lon, out double x0, out double z0);
                int bx = (int)x0 >> 5 << 5, bz = (int)z0 >> 5 << 5;
                // warm up
                double sink = 0; int frozen = 0;
                for (int i = 0; i < 20000; i++) { var g = m.TerrainGeoAt(bx + (i & 31), bz + ((i >> 5) & 31)); sink += m.Data.SampleClimate(g.Lat, g.Lon).TWarm; }
                const int N = 200000;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < N; i++) { var g = m.TerrainGeoAt(bx + (i & 31), bz + ((i >> 5) & 31)); sink += m.Data.SampleClimate(g.Lat, g.Lon).TWarm; }
                double geoClimate = sw.Elapsed.TotalMilliseconds * 1e6 / N;
                sw.Restart();
                for (int i = 0; i < N; i++) if (m.FrozenAllYear(bx + (i & 31) + .5, bz + ((i >> 5) & 31) + .5, height - 2)) frozen++;
                double frozenTop = sw.Elapsed.TotalMilliseconds * 1e6 / N;
                int frozenLow = 0;
                sw.Restart();
                for (int i = 0; i < N; i++) if (m.FrozenAllYear(bx + (i & 31) + .5, bz + ((i >> 5) & 31) + .5, sea + 5)) frozenLow++;
                double frozenAtSea = sw.Elapsed.TotalMilliseconds * 1e6 / N;
                sw.Restart();
                for (int i = 0; i < N; i++) sink += m.GetColumn(bx + (i & 31), bz + ((i >> 5) & 31)).Top;
                double column = sw.Elapsed.TotalMilliseconds * 1e6 / N;
                Console.WriteLine("  {0,-13} TerrainGeoAt+SampleClimate {1,6:0} ns; FrozenAllYear at build limit {2,6:0} ns ({3:0}% frozen); at sea+5 {4,6:0} ns ({5:0}% frozen); GetColumn {6,6:0} ns  [{7:0}]",
                    name, geoClimate, frozenTop, 100.0 * frozen / N, frozenAtSea, 100.0 * frozenLow / N, column, sink % 7);
            }
        }
        return 0;
    }
}
