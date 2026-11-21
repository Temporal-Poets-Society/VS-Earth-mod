using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// (EA-0162) Who keeps Earth's terrain out, named by mod, for the refusal when a chunk reaches Earth's surface pass without
    /// Earth's terrain (EarthWorldGen.BareRock). The game runs a chunk column's terrain pass once, on one thread, from the first
    /// handler to the last (Lib ServerSystemSupplyChunks.PopulateChunk/runGenerators; a peek too, with the other generation
    /// threads paused; a column resumed from the save starts after a whole pass), so with the game alone Earth's terrain writer
    /// always runs before its surface pass. A chunk without it comes from another mod: one that replaced vanilla's terrain
    /// handler before Earth started (Earth's terrain then runs inside vanilla's generator, which that mod's handler no longer
    /// calls), one that replaced Earth's handler after Earth started, a Harmony patch skipping either, or a mod running the
    /// handlers on chunks of its own. Before, the refusal said "a mod replaced Earth's terrain handler" without naming it, and a
    /// player with no mod they knew of as world generation could not act on it.
    /// The reading itself (<see cref="Read"/>, <see cref="Cause"/>) takes plain inputs, for the tests (PatchVanillaTests).
    /// </summary>
    static class TerrainPassReport
    {
        internal enum Kind { Vanilla, Earth, EarthTerrain, Other }

        /// <summary>One handler of the terrain pass: its type and method, the mod its code is in, and whose it is.</summary>
        internal readonly struct Handler
        {
            public readonly string Name, Mod;
            public readonly Kind Kind;
            public Handler(string name, string mod, Kind kind) { Name = name; Mod = mod; Kind = kind; }
            public override string ToString() => Kind == Kind.EarthTerrain ? "Earth's terrain (" + Name + ")" : Mod + ": " + Name;
        }

        /// <summary>The game's own assemblies (the engine and the vanilla content mods).</summary>
        static readonly HashSet<string> GameAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "VSEssentials", "VSSurvivalMod", "VSCreativeMod", "VintagestoryLib", "VintagestoryAPI" };

        /// <summary>
        /// The handlers in the order given, each named by the type and method its code is in (a lambda by the method it was
        /// written in) and the mod of that code (<paramref name="modOf"/>); <paramref name="earthTerrain"/> is Earth's own
        /// terrain handler in the list (null where Earth's terrain runs as a prefix on vanilla's generator).
        /// </summary>
        internal static Handler[] Read(IEnumerable<ChunkColumnGenerationDelegate> handlers, ChunkColumnGenerationDelegate earthTerrain, System.Func<Assembly, string> modOf)
        {
            var earth = typeof(TerrainPassReport).Assembly;
            var read = new List<Handler>();
            foreach (var d in handlers)
            {
                if (d == null) continue;
                var parts = d.GetInvocationList().Select(x => Describe(x)).ToArray();
                var asm = parts[0].Assembly;
                Kind kind = asm == earth ? (earthTerrain != null && ReferenceEquals(d, earthTerrain) ? Kind.EarthTerrain : Kind.Earth)
                    : asm != null && GameAssemblies.Contains(asm.GetName().Name) ? Kind.Vanilla : Kind.Other;
                read.Add(new Handler(string.Join(" + ", parts.Select(p => p.Name)), asm == null ? "an unknown mod" : modOf(asm), kind));
            }
            return read.ToArray();
        }

        /// <summary>A delegate's type and method (a closure's by the type it was written in) and the assembly of its code; a
        /// closure holding one other handler (a wrapper) also names the handler it wraps.</summary>
        static (string Name, Assembly Assembly) Describe(Delegate d) => Describe(d, 0);

        static (string Name, Assembly Assembly) Describe(Delegate d, int depth)
        {
            var m = d.Method;
            Type t = m?.DeclaringType;
            // a lambda's closure class (<>c, <>c__DisplayClass3_0) sits inside the type it was written in
            while (t != null && t.Name.StartsWith("<", StringComparison.Ordinal) && t.DeclaringType != null) t = t.DeclaringType;
            t ??= d.Target?.GetType();
            string method = m?.Name ?? "?";
            if (method.StartsWith("<", StringComparison.Ordinal))
            {
                int end = method.IndexOf('>');
                method = (end > 1 ? method.Substring(1, end - 1) : "") + " (a lambda)";
            }
            string name = (t?.FullName ?? "a dynamic method") + "." + method;
            if (depth < 2 && Wrapped(d.Target) is Delegate inner) name += " around " + Describe(inner, depth + 1).Name;
            return (name, t?.Assembly ?? m?.Module?.Assembly);
        }

        /// <summary>The one handler a lambda's closure holds (a wrapper's original), else null.</summary>
        static Delegate Wrapped(object target)
        {
            var type = target?.GetType();
            if (type == null || !type.Name.StartsWith("<", StringComparison.Ordinal)) return null;
            try
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)).ToArray();
                return fields.Length == 1 ? fields[0].GetValue(target) as Delegate : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Why chunk (<paramref name="cx"/>, <paramref name="cz"/>) reached Earth's surface pass without Earth's terrain, and what
        /// to do: from the terrain pass's handlers before the surface pass (<paramref name="beforeSurface"/>, <see cref="Read"/>),
        /// whether Earth's terrain runs as a prefix on vanilla's generator (<paramref name="viaGenTerra"/>: vanilla's handler was
        /// gone from the list when Earth started) and whether vanilla's handler is in the pass (<paramref name="genTerraInPass"/>),
        /// where the chunk came from when not from the game's own generation (<paramref name="requestSource"/>) and other mods'
        /// Harmony patches on vanilla's terrain generator or on Earth's code (<paramref name="patches"/>).
        /// </summary>
        internal static (string Cause, string Advice) Cause(int cx, int cz, IReadOnlyList<Handler> beforeSurface, bool viaGenTerra, bool genTerraInPass,
            string requestSource, IReadOnlyCollection<string> patches)
        {
            static string Named(Handler h) => h.Mod + "'s " + h.Name;
            var others = beforeSurface.Where(h => h.Kind == Kind.Other).Select(Named).Distinct().ToList();
            bool earthIn = beforeSurface.Any(h => h.Kind == Kind.EarthTerrain);
            bool earthRuns = viaGenTerra ? genTerraInPass : earthIn;
            // the terrain generator's place is the pass's first (vanilla's GenTerra starts first, at 0.0, and Earth's terrain takes
            // its place in the list): another mod's handler there replaced it, the others run beside it
            string first = beforeSurface.Count > 0 && beforeSurface[0].Kind == Kind.Other ? Named(beforeSurface[0]) : null;
            var beside = others.Where(o => o != first).ToList();
            string what;
            if (others.Count > 0 && !earthRuns)
            {
                what = viaGenTerra ? "vanilla's terrain generator was replaced before Earth started (Earth's terrain runs inside it)"
                    : "Earth's terrain handler was replaced after Earth started";
                what += first != null ? ", and in its place the terrain pass runs " + first + (beside.Count > 0 ? "; the pass also runs " + Join(beside) : "")
                    : "; the terrain pass runs " + Join(others);
            }
            else if (others.Count > 0)
                what = "Earth's terrain handler is in the terrain pass but did not run for this chunk; the pass also runs " + Join(others);
            else if (!earthRuns)
                what = viaGenTerra ? "vanilla's terrain generator, inside which Earth's terrain runs, was taken out of the terrain pass"
                    : "Earth's terrain handler was taken out of the terrain pass";
            else
                what = "Earth's terrain handler is in the terrain pass but did not run for this chunk";
            string cause = "the terrain pass made chunk " + cx + "," + cz + " without Earth's terrain: " + what;
            if (requestSource != null) cause += "; the chunk came from " + requestSource + ", not from the game's own generation";
            if (patches != null && patches.Count > 0) cause += "; other mods' Harmony patches there: " + Join(patches.ToList());
            string advice = others.Count > 0 || requestSource != null
                ? "Earth's terrain must replace vanilla's in an Earth world: remove the mod named here, or update it, or ask its author to leave " +
                  "Earth's terrain handler in place and call it; then restart. The terrain pass's handlers are listed in the line above."
                : "Earth's terrain must replace vanilla's in an Earth world: a mod's Harmony patch (named here if any) or a game update keeps it out. " +
                  "Remove that mod, or use an Earth version made for this game version, then restart. The terrain pass's handlers are listed in the line above.";
            return (cause, advice);
        }

        /// <summary>The terrain pass's handlers in order, for the log.</summary>
        internal static string Listing(IReadOnlyList<Handler> all, bool viaGenTerra, bool genTerraInPass)
            => "the terrain pass's handlers in this world, in order: " + string.Join("; ", all.Select((h, i) => (i + 1) + ". " + h)) +
               (viaGenTerra ? ". Earth's terrain runs inside vanilla's terrain generator (GenTerra's handler was not in the list when Earth started); " +
                   "vanilla's handler is " + (genTerraInPass ? "in the pass" : "not in the pass") : "");

        static string Join(List<string> items) => items.Count <= 1 ? string.Concat(items)
            : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
    }
}
