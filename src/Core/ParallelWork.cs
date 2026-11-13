using System;
using System.Threading;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// Start-up work spread over a few long-lived workers (2.0.0-rc.4): each worker sets up its own state once (search
    /// buffers, a column memo, a per-thread switch), takes item after item from a shared counter until none is left, then
    /// puts its state away. Parallel.For/ForEach with thread-local state instead start each worker again every time slice
    /// (.NET's task replication), so a worker's 20–40 MB of search buffers were made many times over; and an uncapped loop
    /// used every pool thread, each then keeping its recent terrain and tiles after the start (adversarial review of
    /// 2.0.0-rc.4). The items are only spread over the workers: callers write each item's result to its own slot and put
    /// the results together in item order, so the outcome never depends on the threads.
    /// </summary>
    internal static class ParallelWork
    {
        /// <summary>Workers for start-up work: half the logical processors, at most 8 (the rest stay with the client's own
        /// loading in single player, and memory per worker stays bounded).</summary>
        public static int DefaultWorkers => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

        /// <summary>Runs <paramref name="body"/> for items 0..count-1 (in chunks of <paramref name="chunk"/> taken in turn)
        /// on up to <paramref name="workers"/> workers (0: <see cref="DefaultWorkers"/>), each with the state
        /// <paramref name="init"/> makes for it and <paramref name="done"/> is given at its end (also when it fails).</summary>
        public static void For<T>(int count, Func<T> init, Action<int, T> body, Action<T> done = null, int chunk = 1, int workers = 0)
        {
            if (count <= 0) return;
            chunk = Math.Max(1, chunk);
            int w = Math.Clamp(workers > 0 ? workers : DefaultWorkers, 1, (count + chunk - 1) / chunk);
            int next = 0;
            void Work()
            {
                var state = init();
                try
                {
                    for (int start = Interlocked.Add(ref next, chunk) - chunk; start < count; start = Interlocked.Add(ref next, chunk) - chunk)
                        for (int i = start, end = Math.Min(count, start + chunk); i < end; i++) body(i, state);
                }
                finally { done?.Invoke(state); }
            }
            if (w == 1) { Work(); return; }
            Parallel.For(0, w, new ParallelOptions { MaxDegreeOfParallelism = w }, _ => Work());
        }

        /// <summary><see cref="For{T}"/> without per-worker state.</summary>
        public static void For(int count, Action<int> body, int chunk = 1, int workers = 0) =>
            For<object>(count, () => null, (i, _) => body(i), null, chunk, workers);
    }
}
