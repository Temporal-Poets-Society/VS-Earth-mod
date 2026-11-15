using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using EarthMap;
using EarthMap.Core;

namespace Probe
{
    static partial class Program
    {
        public static (string Name, double Lat, double Lon)[] Places =
        {
            ("Open ocean", 0.02, -30.02), ("Daugava mouth", 57.05, 24.03), ("Rotterdam", 51.95, 4.1), ("Amazon Manaus", -3.1, -60.0),
            ("Zermatt", 45.98, 7.65), ("Sahara", 23.0, 10.0), ("Kansas (flat)", 38.5, -98.5), ("Sognefjord coast", 61.1, 6.0),
            ("Lake Geneva", 46.45, 6.55), ("Siberia Lena", 62.0, 129.7),
        };

        public static readonly (string Name, double Lat, double Lon)[] FinePlaces =
        {
            ("Open ocean", 0.02, -30.02), ("Daugava at Riga", 56.9465, 24.0915), ("Daugava mouth", 57.0635, 24.0215), ("Rotterdam Nieuwe Maas", 51.9085, 4.486),
            ("Rotterdam sea of 1318", 51.95, 4.1), ("Hoek van Holland", 51.985, 4.115), ("Amazon meeting of waters", -3.137, -59.902), ("Zermatt", 45.98, 7.65), ("Sahara", 23.0, 10.0),
            ("Kansas (flat)", 38.5, -98.5), ("Jurmala beach coast", 56.9755, 23.80), ("Bergen harbour", 60.397, 5.31), ("Montreux lake shore", 46.431, 6.908), ("Rhine at Cologne", 50.94, 6.965),
        };

        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string root = args[0];
            int scale = int.Parse(args[1]);
            string scratch = Environment.GetEnvironmentVariable("PROBE_SCRATCH") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scratch"));
            Directory.CreateDirectory(scratch);
            bool glo90 = Environment.GetEnvironmentVariable("ELEV") != "builtin";
            if (Environment.GetEnvironmentVariable("PLACES") == "fine") Places = FinePlaces;
            else if (Environment.GetEnvironmentVariable("PLACES") is string custom && custom.Length > 0)
                Places = custom.Split(';').Select(p => p.Split(',')).Select(p => (p[0], double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture))).ToArray();
            var w = World.Build(root, scale, scratch, Places.Select(p => (p.Lat, p.Lon)), glo90);
            for (int i = 2; i < args.Length; i++)
            {
                string cmd = args[i];
                Console.WriteLine($"== {cmd}");
                switch (cmd)
                {
                    case "cost": Cost(w); break;
                    case "determinism": Determinism(w); break;
                    case "seam": Seam(w); break;
                    case "poles": Poles(w); break;
                    case "floats": Floats(w); break;
                    case "cells": Cells(w); break;
                    case "misc": Misc(w); break;
                    case "fuzz": Fuzz(w, 3000, 200); break;
                    case "edges": Edges(w); break;
                    case "islands180": Islands180(w); break;
                    case "dump": Dump(w); break;
                    case "flowedge": FlowEdge(w); break;
                    case "dumpdiff": DumpDiff(w); break;
                    case "lakeareas": LakeAreas(w); break;
                    case "lakestrip": LakeStrip(w); break;
                    case "riveredges": RiverEdges(w); break;
                    default: Console.WriteLine("unknown command"); break;
                }
            }
            return 0;
        }

        static string Kind(in TerrainModel.Column c) =>
            c.River ? "river" : c.Water == TerrainModel.WaterSea ? "sea" : c.Water != TerrainModel.WaterNone ? "lake" : c.Beach ? "beach" : c.NearRiver ? "bank" : "land";

        /// <summary>Per-column cost of GetColumn over a 96 x 96 patch (9 chunks), row by row as a chunk fills: cold (first pass,
        /// tiles decoded), warm (the mean of 3 more passes), and warm with layers taken off one by one.</summary>
        static void Cost(World w)
        {
            var m = w.Model;
            int N = int.TryParse(Environment.GetEnvironmentVariable("PATCH"), out int np) ? np : 96;
            Console.WriteLine($"{"place",-18} {"cold us",8} {"warm us",8} {"B/col",7} | {"-shore",7} {"-strait",7} {"-chan",7} {"-mouth*",7} {"-rivers",7} {"-isl",7} {"-works",7} {"-outl",7} {"-coast",7} {"-fine",7} | kinds");
            foreach (var (name, lat, lon) in Places)
            {
                var (px, pz) = w.At(lat, lon);
                px -= N / 2; pz -= N / 2;
                long sink = 0;
                double Pass()
                {
                    var t = Stopwatch.StartNew();
                    for (int j = 0; j < N; j++)
                        for (int i = 0; i < N; i++) sink += m.GetColumn(px + i, pz + j).Top;
                    return t.Elapsed.TotalMilliseconds * 1000 / (N * N);
                }
                double Warm(out double bytes)
                {
                    Pass();
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    double best = double.MaxValue, sum = 0;
                    for (int k = 0; k < 3; k++) { double v = Pass(); sum += v; best = Math.Min(best, v); }
                    bytes = (GC.GetAllocatedBytesForCurrentThread() - b0) / (3.0 * N * N);
                    return best;
                }
                double cold = Pass();
                double warm = Warm(out double bytes);
                var kinds = new Dictionary<string, int>();
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++) { string k = Kind(m.GetColumn(px + i, pz + j)); kinds[k] = kinds.GetValueOrDefault(k) + 1; }
                // layers off, cumulatively (each column is then another column: the differences say where the time goes)
                var keep = (m.ShoreRules, m.Straits, m.Channels, m.ChannelRules, m.Rivers, m.DryFans, m.Islands, m.Works, m.LakeOutlines, m.RiverOutlines, m.Coasts, m.Fine, m.Coarse);
                var cols = new List<double>();
                try
                {
                    m.ShoreRules = 0; cols.Add(Warm(out _));
                    m.Straits = null; cols.Add(Warm(out _));
                    m.Channels = null; cols.Add(Warm(out _));
                    m.ChannelRules = Math.Min(m.ChannelRules, 2); cols.Add(Warm(out _));   // no ApplyMouth (and rules 2 channels)
                    m.Rivers = null; m.DryFans = null; cols.Add(Warm(out _));
                    m.Islands = null; cols.Add(Warm(out _));
                    m.Works = null; cols.Add(Warm(out _));
                    m.LakeOutlines = null; m.RiverOutlines = null; cols.Add(Warm(out _));
                    m.Coasts = null; cols.Add(Warm(out _));
                    m.Fine = null; m.Coarse = null; cols.Add(Warm(out _));
                }
                finally
                {
                    (m.ShoreRules, m.Straits, m.Channels, m.ChannelRules, m.Rivers, m.DryFans, m.Islands, m.Works, m.LakeOutlines, m.RiverOutlines, m.Coasts, m.Fine, m.Coarse) = keep;
                }
                Console.WriteLine($"{name,-18} {cold,8:0.00} {warm,8:0.00} {bytes,7:0} | {string.Join(" ", cols.Select(c => $"{c,7:0.00}"))} | {string.Join(" ", kinds.OrderByDescending(k => k.Value).Select(k => k.Key + ":" + k.Value))}");
                if (sink == 42) Console.WriteLine();
            }
        }
    }
}
