using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

string vs = typeof(Bench).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    string name = new AssemblyName(e.Name).Name + ".dll";
    foreach (string dir in new[] { vs, Path.Combine(vs, "Lib"), Path.Combine(vs, "Mods") })
    {
        string path = Path.Combine(dir, name);
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    return null;
};
Bench.Run();

static class Bench
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        var pool = new Vintagestory.Common.ChunkDataPool(32, null);
        const int rock = 1234, rock2 = 2345, soil = 77, water = 4321, salt = 4322;
        for (int round = 0; round < 3; round++)
        {
            // a chunk as Earth's terrain writer makes it, block by block through the indexer
            var a = Vintagestory.Common.ChunkData.CreateNew(32, pool);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 32768; i++) a[i] = rock;
            double indexer = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            var b = Vintagestory.Common.ChunkData.CreateNew(32, pool);
            sw.Restart();
            for (int i = 0; i < 32768; i++) b.SetBlockUnsafe(i, rock);
            double unsafeSet = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            var c = Vintagestory.Common.ChunkData.CreateNew(32, pool);
            sw.Restart();
            for (int y = 0; y < 32; y++) c.SetBlockBulk(y * 1024, 32, 32, rock);
            double bulk = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            var d = Vintagestory.Common.ChunkData.CreateNew(32, pool);
            sw.Restart();
            for (int i = 0; i < 32768; i++) d.SetFluid(i, salt);
            double fluid = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            // the strata, ground and ore passes: read every block, rewrite it with one of a few ids (a palette of 12)
            int[] ids = { rock, rock2, soil, 90, 91, 92, 93, 94, 95, 96, 97, 98 };
            foreach (int id in ids) b.SetBlockUnsafe(0, id);
            long sum = 0;
            sw.Restart();
            for (int i = 0; i < 32768; i++) sum += b.GetBlockIdUnsafe(i);
            double read = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            sw.Restart();
            for (int i = 0; i < 32768; i++) b.SetBlockUnsafe(i, ids[(i >> 10) % ids.Length]);
            double rewrite = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            sw.Restart();
            for (int i = 0; i < 32768; i++) sum += b.GetFluid(i);
            double readFluid = sw.Elapsed.TotalMilliseconds * 1e6 / 32768;
            Console.WriteLine("round {0}: indexer set {1:0.0} ns, SetBlockUnsafe {2:0.0} ns, SetBlockBulk {3:0.00} ns/block, SetFluid {4:0.0} ns, GetBlockIdUnsafe {5:0.0} ns, SetBlockUnsafe (12-id palette) {6:0.0} ns, GetFluid {7:0.0} ns [{8}]",
                round, indexer, unsafeSet, bulk, fluid, read, rewrite, readFluid, sum % 7);
        }
    }
}
