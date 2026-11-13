using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EarthMap.Core;

namespace EarthMap
{
    /// <summary>
    /// The ore blocks one session placed, per heat-map resource (blocks, then poor/medium/rich/bountiful) and bog iron in peat,
    /// for /earth and the self-test. Counted from every generation thread, so only with Interlocked.
    /// </summary>
    sealed class OreTotals
    {
        readonly Dictionary<string, long[]> totals = new(StringComparer.Ordinal);
        /// <summary>Each block's ore grade (0 poor .. 3 bountiful, -1 ungraded).</summary>
        readonly OreBlockNames names;
        long bogPeat;

        internal OreTotals(RegionalOreCatalog catalog, OreBlockNames names)
        {
            this.names = names;
            foreach (var c in catalog.Layers.Keys) totals[c] = new long[5];
        }
        /// <summary>A resource's counters (shared by every binding of it), null when the heat maps do not have it.</summary>
        internal long[] Of(string resource) => totals.TryGetValue(resource, out var t) ? t : null;

        /// <summary>Blocks placed by the deposit catalogue (0065) or rules v3's ore bodies, counted with the resource's totals.</summary>
        internal void RecordBlocks(string resource, long n)
        {
            if (resource != null && totals.TryGetValue(resource, out var a)) Interlocked.Add(ref a[0], n);
        }
        /// <summary>One ore block a binding placed, counted with its resource's totals by grade.</summary>
        internal void Record(OreBinding b, int blockId)
        {
            var a = b.Totals;
            if (a == null) return;
            int grade = names.Grade(blockId);
            Interlocked.Increment(ref a[0]); if (grade >= 0 && grade < 4) Interlocked.Increment(ref a[grade + 1]);
        }
        internal void CountBogPeat() => Interlocked.Increment(ref bogPeat);

        internal string Text() => "bogpeat=" + Interlocked.Read(ref bogPeat) + "; " + string.Join(", ", totals.OrderBy(p => p.Key, StringComparer.Ordinal).Where(p => Interlocked.Read(ref p.Value[0]) > 0).Select(p => p.Key + "=" + p.Value[0] + " [poor/medium/rich/bountiful=" + string.Join("/", p.Value.Skip(1).Take(4)) + "]"));
    }
}
