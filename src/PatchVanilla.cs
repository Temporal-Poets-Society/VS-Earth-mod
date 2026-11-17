using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PatchVanillaTests")]

namespace EarthMap
{
    /// <summary>
    /// The one place where Earth's world generation reaches into vanilla's code (WORK-BRIEF-2 rule 2): every Harmony patch on a
    /// game method, every non-public game member Earth reads and the vanilla handlers it wraps through the game's public
    /// handler list, each under the Earth feature it serves: <see cref="Table"/> (EarthWorldGen's passes and guards, the trees
    /// and corals; bound at its start), <see cref="GeologyTable"/> and <see cref="OreTable"/> (bound by EarthGeology and
    /// EarthOres at their own times, with their own Harmony ids), and the ore code's field reads (<see cref="OreVanilla"/>,
    /// below). The patch methods stay with the pass or guard they belong to; this class only binds them. The rest of world
    /// generation and what it reads of the world, each table bound by its own system: <see cref="PlantsTable"/>,
    /// <see cref="PlacementTable"/>, <see cref="SealevelTable"/>, <see cref="ClimateLapseTable"/> (both sides),
    /// <see cref="FaunaTable"/> (world generation's and the running game's spawning) and <see cref="DungeonTable"/>.
    ///
    /// Scope: every Harmony patch and every non-public game member of Earth's world generation, and of the climate and spawning
    /// it reads; and the running game's and the client's patches of tall grass after the snow, dry land's grass after rain, the
    /// grass colour, the season tint, Earth's thunder and the creative panel's date rows (the runtime tables, at the end of this
    /// file), so that a game update leaves none of them half patched. (Earth's copy of the client map race fix is gone since
    /// 2.0.0-rc.3: Cylinder World, which Earth requires from 1.0.4 on, locks the same client map dictionaries itself in every
    /// version Earth accepts, and Earth's second lock only added a lock per packet.) Outside it, each binding its
    /// own patches and logging a missing member itself: EarthWeather's patches (per hook, each falling back to vanilla's value
    /// on its own; what other parts depend on is tied by flags instead of a feature: the wind is written as a vector only while
    /// every vanilla reader of its X takes the length (VanillaWindVector.ReadersTakeVector), else as vanilla's strength in X,
    /// and Earth's thunder and the ground moisture use the model only while the rain patch is in (EarthWeather.RainPatched),
    /// CR-W5); SelfTest's read-only probes (chunk storage,
    /// vanilla's forced climate list); the public fields and methods of the server's own assembly that EarthWorldOptions sets
    /// on a new world (looked up by name, as Earth doesn't compile against that assembly); and reflection on other mods
    /// (Cylinder World, AstraTerra, Hydrology, HoD), which belongs to their contracts, not the game's.
    ///
    /// At start-up each member is looked up once and checked against what Earth's patch binds to: the parameters it reads by
    /// name and their types, the return value it sets, the instance it takes; a field must have the type Earth reads it as.
    /// When a member is missing or changed (a game update), one line names the game version, the member and the Earth feature
    /// now off, and only that feature is off: none of its patches stay applied, so vanilla's own behaviour stays there. Never a
    /// crash, never half a feature, never a silent wrong result: a transpiler that does not find the code it rewrites turns
    /// its feature off the same way. Each feature's members are written in a lambda of their own, so even a game type that
    /// is gone (a type load failure) only turns off the feature that names it.
    ///
    /// Why each is a patch (no public event or API for it in 1.22.7, checked against C:\VS mods\devtools\vs-ref): see the
    /// table's comments.
    ///
    /// Two lifetimes (ER-22, code review 14 November 2026). Most systems undo their patches in Dispose (UnpatchAll). Six
    /// patch once for the process and leave them in: ClimateLapse, EarthGrassSeason, EarthGrassWinter, EarthGroundMoisture,
    /// SeasonTintAltitude and SnowMeltLight (each a static <c>patched</c>). That is allowed only where every patched method
    /// first tests a static switch the system clears in Dispose and sets for an Earth world alone, so a later world in the
    /// same process (a vanilla one, or the other side of a singleplayer game) runs vanilla's code plus one early return.
    /// </summary>
    internal sealed partial class PatchVanilla
    {
        internal const string HarmonyId = "earth";

        /// <summary>Earth's features that need vanilla's members.</summary>
        internal enum Feature
        {
            Terrain, AridBasins, SoilFertility, TallGrass, TallGrassAroundStructures, TreeSpecies, TreeAdapterReport, CoralSalinity,
            PatchReservations, StructuresWherePeopleLived, VillagesWherePeopleLived, SurfaceRuinsOutOfWater, SurfaceStructuresOutOfWater,
            ShallowWaterStructures, UndergroundStructures, VillageBuildings, StructureSizes, Ponds, Springs, WorldgenSpawnRanges,
            RuntimeSpawnRanges, Prospecting,
            // regional geology (GeologyTable) and the ore layer (OreTable)
            GeologyStrata, GeologySoil, GeologyDeposits, GeologyDepositColumns, GeologyDepositReload, OreMapFactor, OreReload, OreStrata,
            // the story locations (Table); regional plants (PlantsTable), plants as around 1318 (PlacementTable), the sea-level
            // deposits (SealevelTable), the real lapse (ClimateLapseTable), regional fauna (FaunaTable), the tiled dungeons' map
            // size (DungeonTable)
            StorySchematics, RegionalPlants, RegionalPlantsOnTrees, Placement1318, SealevelDeposits, ClimateLapseTemperature, ClimateLapseRain,
            RegionalFauna, DungeonMapSize,
            // the running game's and the client's features (the runtime tables, at the end of this file)
            GrassWinter, GroundMoisture, GrassSeason, SeasonTint, Thunder, CreativeDate, SnowMeltLight, GrassSeasonShader,
        }

        /// <summary>One feature: the members it needs, bound all together or not at all.</summary>
        internal sealed class Spec
        {
            public readonly Feature Feature;
            /// <summary>The feature's name in the log ("the spring guards").</summary>
            public readonly string What;
            /// <summary>What vanilla does while the feature is off (the end of the log line).</summary>
            public readonly string Without;
            /// <summary>The members, made when the table is resolved: a game type a game update removed fails here, for this
            /// feature only.</summary>
            public readonly Func<Bind[]> Binds;
            /// <summary>A feature this one only makes sense with: off with it, without a line of its own.</summary>
            public Feature? Needs;
            /// <summary>For a transpiler: whether it found and rewrote its code (asked after patching).</summary>
            public Func<bool> Rewrote;
            /// <summary>Told when the feature is bound, or why not.</summary>
            public Action Bound;
            public Action<string> Unbound;
            /// <summary>Tried when this one can't be bound (it is then logged as running this way instead).</summary>
            public Spec Instead;
            /// <summary>Run before binding (a transpiler's "found it" flag back to false).</summary>
            public Action Before;
            /// <summary>An error rather than a warning when off (Earth's terrain).</summary>
            public bool Critical;

            public Spec(Feature feature, string what, string without, Func<Bind[]> binds)
            {
                Feature = feature; What = what; Without = without; Binds = binds;
            }
        }

        /// <summary>One game member a feature needs.</summary>
        internal abstract class Bind
        {
            /// <summary>The member as the log names it ("GenRivulets.tryGenRivulet").</summary>
            public abstract string Name { get; }
            /// <summary>Looks the member up and checks it; null when it is there as Earth expects, else what is wrong.</summary>
            public abstract string Resolve();
            public abstract void Apply(Harmony harmony);
            /// <summary>Takes back what <see cref="Apply"/> did (another member of the same feature failed).</summary>
            public abstract void Undo(Harmony harmony);
            /// <summary>Whether what <see cref="Apply"/> did is still in place (another mod may have changed it since); null when it
            /// is, or when it can't be told from here (<see cref="Unverified"/>), else what is provably wrong.</summary>
            public virtual string StillInPlace(Harmony harmony) => null;
            /// <summary>Null, or what another mod changed in a way that may still run Earth's code (a wrapper around Earth's
            /// handler): nothing here can tell, so the feature's own per-use check decides.</summary>
            public virtual string Unverified(Harmony harmony) => null;
        }

        /// <summary>A Harmony patch on a game method: Earth's prefix, postfix, transpiler and finalizer for it.</summary>
        internal sealed class PatchMethod : Bind
        {
            /// <summary>The method name that stands for an instance constructor.</summary>
            internal const string Constructor = ".ctor";

            readonly Type type;
            readonly string typeName, method;
            readonly Type[] args;
            readonly Type returns;
            readonly bool declared;
            readonly (Type Type, string Name) prefix, postfix, transpiler, finalizer;
            readonly string[] after;
            MethodBase original;
            readonly List<MethodInfo> applied = new List<MethodInfo>();

            /// <param name="args">The method's parameter types, where the name alone could change meaning (an overload).</param>
            /// <param name="returns">The return type Earth relies on, where no __result says it.</param>
            /// <param name="after">Harmony ids whose patches on the same method run before these (Harmony's "after").</param>
            /// <param name="typeName">The type's name for the log when <paramref name="type"/> is looked up by name and is gone.</param>
            /// <param name="found">Told the game method once it is patched (for a diagnostic that calls it: EarthFauna's probe).</param>
            /// <param name="method">The method's name, or <see cref="Constructor"/> for an instance constructor (with
            /// <paramref name="args"/>).</param>
            public PatchMethod(Type type, string method, Type[] args = null, bool declared = false, (Type, string) prefix = default,
                (Type, string) postfix = default, (Type, string) transpiler = default, (Type, string) finalizer = default, string typeName = null,
                Type returns = null, string[] after = null, Action<MethodInfo> found = null)
            {
                this.type = type; this.typeName = type?.Name ?? typeName ?? "?"; this.method = method; this.args = args; this.declared = declared;
                this.prefix = prefix; this.postfix = postfix; this.transpiler = transpiler; this.finalizer = finalizer;
                this.returns = returns; this.after = after; this.found = found;
            }
            readonly Action<MethodInfo> found;

            public override string Name => typeName + "." + method;

            IEnumerable<(Type Type, string Name, bool Transpiler)> Patches()
            {
                if (prefix.Type != null) yield return (prefix.Type, prefix.Name, false);
                if (postfix.Type != null) yield return (postfix.Type, postfix.Name, false);
                if (transpiler.Type != null) yield return (transpiler.Type, transpiler.Name, true);
                if (finalizer.Type != null) yield return (finalizer.Type, finalizer.Name, false);
            }

            public override string Resolve()
            {
                if (type == null) return "not found (its type is gone)";
                original = method == Constructor ? AccessTools.Constructor(type, args)
                    : declared ? AccessTools.DeclaredMethod(type, method, args) : AccessTools.Method(type, method, args);
                if (original == null) return "not found";
                var returnType = (original as MethodInfo)?.ReturnType;
                if (returns != null && returnType != returns) return "changed (returns " + (returnType?.Name ?? "nothing") + ", not " + returns.Name + ")";
                foreach (var (patchType, patchName, isTranspiler) in Patches())
                {
                    var patch = AccessTools.Method(patchType, patchName);
                    if (patch == null) return "has no Earth patch " + patchType.Name + "." + patchName;
                    if (!isTranspiler && Mismatch(original, patch) is string why) return "changed (" + why + ")";
                }
                return null;
            }

            public override void Apply(Harmony harmony)
            {
                HarmonyMethod H((Type Type, string Name) p) => p.Type == null ? null : new HarmonyMethod(p.Type, p.Name) { after = after };
                // noted first: a patch Harmony fails to apply (a transpiler that throws) may still be registered, and is taken back
                foreach (var (patchType, patchName, _) in Patches()) applied.Add(AccessTools.Method(patchType, patchName));
                harmony.Patch(original, H(prefix), H(postfix), H(transpiler), H(finalizer));
                if (original is MethodInfo m) found?.Invoke(m);
            }

            public override void Undo(Harmony harmony)
            {
                foreach (var patch in applied)
                {
                    try { harmony.Unpatch(original, patch); } catch (Exception) { /* not applied after all */ }
                }
                applied.Clear();
            }

            public override string StillInPlace(Harmony harmony)
            {
                var info = Harmony.GetPatchInfo(original);
                var mine = info == null ? new HashSet<MethodInfo>() : new HashSet<MethodInfo>(info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                    .Concat(info.Finalizers).Where(p => p.owner == harmony.Id).Select(p => p.PatchMethod));
                return applied.All(mine.Contains) ? null : Name + "'s Earth patch was removed";
            }
        }

        /// <summary>
        /// A vanilla handler of a world generation pass, replaced in the game's public handler list (IWorldGenHandler, the
        /// list vanilla's chunk generation reads for every chunk) by Earth's, which wraps it: no Harmony and no non-public
        /// member, so a game update that renames the handler's method changes nothing. Found by its owner's type (vanilla's
        /// mod system); exactly one such handler must be in the pass.
        /// </summary>
        internal sealed class ReplaceHandler : Bind
        {
            readonly IWorldGenHandler handlers;
            readonly EnumWorldGenPass pass;
            readonly Type owner;
            readonly System.Func<ChunkColumnGenerationDelegate, ChunkColumnGenerationDelegate> wrap;
            List<ChunkColumnGenerationDelegate> list;
            ChunkColumnGenerationDelegate original, replacement;

            public ReplaceHandler(IWorldGenHandler handlers, EnumWorldGenPass pass, Type owner, System.Func<ChunkColumnGenerationDelegate, ChunkColumnGenerationDelegate> wrap)
            {
                this.handlers = handlers; this.pass = pass; this.owner = owner; this.wrap = wrap;
            }

            public override string Name => owner.Name + "'s " + pass + " pass handler";

            public override string Resolve()
            {
                var lists = handlers?.OnChunkColumnGen;
                list = lists != null && (int)pass < lists.Length ? lists[(int)pass] : null;
                if (list == null) return "not found (no " + pass + " pass handlers)";
                var found = list.Where(d => d?.Target != null && d.Target.GetType() == owner).ToArray();
                if (found.Length != 1) return found.Length == 0 ? "not found" : "found " + found.Length + " times";
                original = found[0];
                return null;
            }

            public override void Apply(Harmony harmony)
            {
                int i = list.IndexOf(original);
                if (i < 0) throw new InvalidOperationException(Name + " left the list");
                replacement = wrap(original);
                list[i] = replacement;
            }

            public override void Undo(Harmony harmony)
            {
                int i = replacement == null ? -1 : list.IndexOf(replacement);
                if (i >= 0) list[i] = original;
                replacement = null;
            }

            /// <summary>
            /// Provably not in place only where vanilla's own handler is back in the list (or Earth's replacement was taken
            /// back): it runs vanilla's code there. A mod that replaced Earth's entry after Earth may well call it from its own
            /// (request => earthHandler(request), Codex round 3 item 3); a delegate's body can't be read, so that case is
            /// <see cref="Unverified"/> and the per-chunk check decides (EarthWorldGen's surface pass finds the terrain
            /// writer's handoff for every chunk Earth's terrain made).
            /// </summary>
            public override string StillInPlace(Harmony harmony)
            {
                if (replacement == null) return "Earth's replacement of " + Name + " was taken back";
                if (list.Contains(replacement)) return null;
                return VanillaBack() ? "vanilla's own " + Name + " is back in the list (another mod put it back after Earth)" : null;
            }

            public override string Unverified(Harmony harmony)
                => replacement != null && !list.Contains(replacement) && !VanillaBack()
                    ? "Earth's replacement of " + Name + " is no longer in the list (another mod wrapped or replaced it after Earth)" : null;

            /// <summary>Whether a handler of vanilla's owner is in the list again (found as <see cref="Resolve"/> finds it).</summary>
            bool VanillaBack() => list.Any(d => d?.Target != null && d.Target.GetType() == owner);
        }

        /// <summary>A game field Earth needs where no field reference fits: on a type of the server's own assembly, looked up
        /// by name (Earth doesn't compile against it), or a public field Earth's patch reads, which must merely be there with
        /// its type (a patch method reading a field that is gone would fail when it first runs, not here).</summary>
        internal sealed class FieldByName : Bind
        {
            readonly Func<Type> owner;
            readonly string ownerName, field;
            readonly Type type;
            readonly Action<FieldInfo> into;
            FieldInfo found;

            public FieldByName(Func<Type> owner, string ownerName, string field, Type type, Action<FieldInfo> into = null)
            {
                this.owner = owner; this.ownerName = ownerName; this.field = field; this.type = type; this.into = into;
            }

            public override string Name => ownerName + "." + field;

            public override string Resolve()
            {
                var t = owner();
                if (t == null) return "not found (its type is gone)";
                found = AccessTools.Field(t, field);
                if (found == null) return "not found";
                if (found.IsStatic) return "changed (static)";
                if (found.FieldType != type) return "changed (a " + found.FieldType.Name + ", not a " + type.Name + ")";
                return null;
            }

            public override void Apply(Harmony harmony) => into?.Invoke(found);
            public override void Undo(Harmony harmony) => into?.Invoke(null);
        }

        /// <summary>A non-public game field Earth reads, bound to a field reference its reader keeps.</summary>
        internal sealed class ReadField<TOwner, TValue> : Bind
        {
            readonly string field;
            readonly Action<AccessTools.FieldRef<TOwner, TValue>> into;
            AccessTools.FieldRef<TOwner, TValue> fieldRef;

            public ReadField(string field, Action<AccessTools.FieldRef<TOwner, TValue>> into) { this.field = field; this.into = into; }

            public override string Name => typeof(TOwner).Name + "." + field;

            public override string Resolve()
            {
                fieldRef = Resolved(field, out string problem);
                return problem;
            }

            /// <summary>The field reference, or null with what is wrong (missing, static, or of another type).</summary>
            internal static AccessTools.FieldRef<TOwner, TValue> Resolved(string field, out string problem)
            {
                problem = null;
                try
                {
                    var f = AccessTools.Field(typeof(TOwner), field);
                    if (f == null) problem = "not found";
                    else if (f.IsStatic) problem = "changed (static)";
                    else if (f.FieldType != typeof(TValue)) problem = "changed (a " + f.FieldType.Name + ", not a " + typeof(TValue).Name + ")";
                    else return AccessTools.FieldRefAccess<TOwner, TValue>(f);
                }
                catch (Exception e) { problem = "can't be read (" + e.GetType().Name + ": " + e.Message + ")"; }
                return null;
            }

            public override void Apply(Harmony harmony) => into(fieldRef);
            public override void Undo(Harmony harmony) => into(null);
        }

        /// <summary>
        /// Why a patch method can't bind to <paramref name="original"/>, or null: every parameter it reads by name must be one of
        /// the original's, of a type it can take (the same type where it takes it by reference); __result must be the original's
        /// return type; __instance must take the original's declaring type. Harmony checks these only when patching (and
        /// throws); here a changed signature is found before anything is patched.
        /// </summary>
        internal static string Mismatch(MethodBase original, MethodInfo patch)
        {
            var args = original.GetParameters();
            foreach (var p in patch.GetParameters())
            {
                bool byRef = p.ParameterType.IsByRef;
                Type t = byRef ? p.ParameterType.GetElementType() : p.ParameterType;
                if (p.GetCustomAttribute<HarmonyArgument>() is { } bound)
                {
                    // bound by position (where the name is not part of a public, stable signature) or by another name
                    var arg = bound.Index >= 0 ? (bound.Index < args.Length ? args[bound.Index] : null) : args.FirstOrDefault(x => x.Name == bound.OriginalName);
                    if (arg == null) return bound.Index >= 0 ? "no argument " + bound.Index : "no parameter '" + bound.OriginalName + "'";
                    Type bt = arg.ParameterType.IsByRef ? arg.ParameterType.GetElementType() : arg.ParameterType;
                    if (byRef ? bt != t : !t.IsAssignableFrom(bt)) return "argument " + (bound.Index >= 0 ? bound.Index.ToString() : "'" + bound.OriginalName + "'") + " is a " + bt.Name + ", not a " + t.Name;
                    continue;
                }
                if (p.Name == "__instance")
                {
                    if (original.IsStatic || !t.IsAssignableFrom(original.DeclaringType)) return "no " + t.Name + " instance";
                    continue;
                }
                if (p.Name == "__result")
                {
                    var returns = (original as MethodInfo)?.ReturnType;
                    if (returns != t) return "returns " + (returns?.Name ?? "nothing") + ", not " + t.Name;
                    continue;
                }
                if (p.Name.StartsWith("___", StringComparison.Ordinal))
                {
                    // a private field of the instance, injected by name
                    var field = AccessTools.Field(original.DeclaringType, p.Name.Substring(3));
                    if (field == null) return "no field '" + p.Name.Substring(3) + "'";
                    if (byRef ? field.FieldType != t : !t.IsAssignableFrom(field.FieldType)) return "field '" + p.Name.Substring(3) + "' is a " + field.FieldType.Name + ", not a " + t.Name;
                    continue;
                }
                if (p.Name.Length > 2 && p.Name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(p.Name.Substring(2), out int index))
                {
                    // an argument by its position
                    if (index >= args.Length) return "no argument " + index;
                    Type it = args[index].ParameterType.IsByRef ? args[index].ParameterType.GetElementType() : args[index].ParameterType;
                    if (byRef ? it != t : !t.IsAssignableFrom(it)) return "argument " + index + " is a " + it.Name + ", not a " + t.Name;
                    continue;
                }
                if (p.Name.StartsWith("__", StringComparison.Ordinal)) continue;   // __state, __exception and Harmony's other injections
                var a = args.FirstOrDefault(x => x.Name == p.Name);
                if (a == null) return "no parameter '" + p.Name + "'";
                Type at = a.ParameterType.IsByRef ? a.ParameterType.GetElementType() : a.ParameterType;
                if (byRef ? at != t : !t.IsAssignableFrom(at)) return "parameter '" + p.Name + "' is a " + at.Name + ", not a " + t.Name;
            }
            return null;
        }

        static (Type, string) P(Type type, string name) => (type, name);

        /// <summary>
        /// Every patch and non-public member Earth's world generation uses, by feature (<paramref name="handlers"/>: the
        /// "standard" world type's generation handlers, for the vanilla handlers Earth replaces). Order: as they were patched
        /// before this table (the order matters only for the log).
        /// </summary>
        internal static Spec[] Table(IWorldGenHandler handlers, System.Func<ChunkColumnGenerationDelegate, ChunkColumnGenerationDelegate> inPlaceOfGenTerra = null) => new[]
        {
            // vanilla's terrain pass, replaced in Earth worlds through the public handler list (no event replaces a handler);
            // where another mod has wrapped GenTerra's handler already, a prefix on GenTerra's own (private) method, which its
            // wrapper still calls. Without either an Earth world is refused (EarthWorldGen), never generated on vanilla terrain.
            // (FP-01: the server's generator passes its own wrapper, bound to itself)
            new Spec(Feature.Terrain, "Earth's terrain through vanilla's handler list", "it runs as a Harmony prefix on GenTerra's own method instead", () => new Bind[]
            {
                new ReplaceHandler(handlers, EnumWorldGenPass.Terrain, typeof(GenTerra), inPlaceOfGenTerra ?? EarthWorldGen.InPlaceOfGenTerra),
            })
            {
                Instead = new Spec(Feature.Terrain, "Earth's terrain", "this server refuses Earth worlds (vanilla's terrain generator can't be replaced)", () => new Bind[]
                {
                    new PatchMethod(typeof(GenTerra), "OnChunkColumnGen", new[] { typeof(IChunkColumnGenerateRequest) }, prefix: P(typeof(EarthWorldGen), nameof(EarthWorldGen.GenTerraPrefix))),
                }) { Critical = true },
            },
            // a constant inside vanilla's block layer pass (its arid basin fill): only a transpiler reaches it
            new Spec(Feature.AridBasins, "the arid basin guard", "dry basins below sea level (Dead Sea, Caspian, Red Sea shelves) may get filled in", () => new Bind[]
            {
                new PatchMethod(typeof(GenBlockLayers), "OnChunkColumnGeneration", transpiler: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.AridFillTranspiler))),
            }) { Before = () => BlockLayerAdjustments.AridFillPatched = false, Rewrote = () => BlockLayerAdjustments.AridFillPatched },
            // a call inside vanilla's private LoadBlockLayers: only a transpiler reaches it
            new Spec(Feature.SoilFertility, "soil by the real aridity (decision 0048)", "semi-arid land keeps vanilla's sand and gravel", () => new Bind[]
            {
                new PatchMethod(typeof(GenBlockLayers), "LoadBlockLayers", transpiler: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.SoilFertilityTranspiler))),
            }) { Before = () => BlockLayerAdjustments.SoilFertilityPatched = false, Rewrote = () => BlockLayerAdjustments.SoilFertilityPatched },
            // vanilla decides each tuft inside PlaceTallGrass (no event); it gets local x, z only, so the block layers' chunk is
            // noted around vanilla's block layer handler (wrapped through the public handler list)
            new Spec(Feature.TallGrass, "the tall grass share of dry land (ranges step 5a), scoped through vanilla's handler list", "the chunk scope is a Harmony prefix on GenBlockLayers' own method instead", () => new Bind[]
            {
                new ReplaceHandler(handlers, EnumWorldGenPass.Terrain, typeof(GenBlockLayers), BlockLayerAdjustments.InChunkScope),
                new PatchMethod(typeof(GenBlockLayers), "PlaceTallGrass", prefix: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.TallGrassCoverPrefix))),
            })
            {
                // ER-22: set again by this server's binding, not left from an earlier one in the same process
                Before = () => BlockLayerAdjustments.TallGrassPatched = false,
                Bound = () => BlockLayerAdjustments.TallGrassPatched = true,
                // where another mod has wrapped GenBlockLayers' handler already: the same scope around its own (private) method
                Instead = new Spec(Feature.TallGrass, "the tall grass share of dry land (ranges step 5a)", "dry land keeps vanilla's tall grass density", () => new Bind[]
                {
                    new PatchMethod(typeof(GenBlockLayers), "OnChunkColumnGeneration", new[] { typeof(IChunkColumnGenerateRequest) },
                        prefix: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.BlockLayersScopePrefix)),
                        finalizer: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.GrassChunkFinalizer))),
                    new PatchMethod(typeof(GenBlockLayers), "PlaceTallGrass", prefix: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.TallGrassCoverPrefix))),
                }) { Bound = () => BlockLayerAdjustments.TallGrassPatched = true },
            },
            // vanilla's GenerateGrass (story structures, dungeon entrances) calls PlaceTallGrass for another chunk
            new Spec(Feature.TallGrassAroundStructures, "the tall grass share around story locations", "tall grass around story locations and dungeon entrances may follow another chunk's aridity", () => new Bind[]
            {
                new PatchMethod(typeof(ModStdWorldGen), "GenerateGrass", prefix: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.GrassChunkPrefix)),
                    finalizer: P(typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.GrassChunkFinalizer))),
            }) { Needs = Feature.TallGrass },
            // the species pick has no event; the pick needs the planting position and accessor, which vanilla keeps in private
            // fields, scoped to vanilla's tree and shrub loops (private)
            new Spec(Feature.TreeSpecies, "the regional tree species pick", "forests keep vanilla's species mix", () => new Bind[]
            {
                new ReadField<WgenTreeSupplier, TreeGeneratorsUtil>("treeGenerators", r => EarthTrees.generatorsRef = r),
                new ReadField<WgenTreeSupplier, TreeGenProperties>("treeGenProps", r => EarthTrees.propsRef = r),
                new ReadField<GenVegetationAndPatches, BlockPos>("tmpPos", r => EarthTrees.positionRef = r),
                new ReadField<GenVegetationAndPatches, IWorldGenBlockAccessor>("blockAccessor", r => EarthTrees.accessorRef = r),
                new PatchMethod(typeof(WgenTreeSupplier), nameof(WgenTreeSupplier.GetRandomGenForClimate), prefix: P(typeof(EarthTrees), nameof(EarthTrees.PickPrefix))),
                new PatchMethod(typeof(GenVegetationAndPatches), "genTrees", new[] { typeof(int), typeof(int) },
                    prefix: P(typeof(EarthTrees), nameof(EarthTrees.ChunkPrefix)), finalizer: P(typeof(EarthTrees), nameof(EarthTrees.ChunkFinalizer))),
                new PatchMethod(typeof(GenVegetationAndPatches), "genShrubs", new[] { typeof(int), typeof(int) },
                    prefix: P(typeof(EarthTrees), nameof(EarthTrees.ChunkPrefix)), finalizer: P(typeof(EarthTrees), nameof(EarthTrees.ChunkFinalizer))),
            }) { Before = EarthTrees.BeginBinding, Bound = EarthTrees.Bound, Unbound = EarthTrees.Unbound },
            // vanilla's tree list loading (internal), for the adapter's coverage report and a fresh picker list
            new Spec(Feature.TreeAdapterReport, "the tree adapter's coverage report", "the tree adapter's coverage is not logged", () => new Bind[]
            {
                new PatchMethod(typeof(WgenTreeSupplier), "LoadTrees", postfix: P(typeof(EarthTrees), nameof(EarthTrees.LoadedTreesPostfix))),
            }) { Needs = Feature.TreeSpecies },
            // vanilla's coral grows across columns and accepts any liquid, inside BlockCoral's own placement
            new Spec(Feature.CoralSalinity, "the coral salinity guard", "corals may grow in fresh water", () => new Bind[]
            {
                new PatchMethod(typeof(BlockCoral), "TryPlaceBlockForWorldGenUnderwater", declared: true, prefix: P(typeof(EarthCorals), nameof(EarthCorals.Attempt)),
                    transpiler: P(typeof(EarthCorals), nameof(EarthCorals.ReadTargets)), finalizer: P(typeof(EarthCorals), nameof(EarthCorals.EndAttempt))),
                new PatchMethod(typeof(BlockCoral), "PlaceCoral", declared: true, prefix: P(typeof(EarthCorals), nameof(EarthCorals.Target))),
                new PatchMethod(typeof(BlockCoral), "SpawnSeaPlantWeighted", declared: true, prefix: P(typeof(EarthCorals), nameof(EarthCorals.PlantTarget))),
            }),
            // block patches scatter into neighbouring chunks inside BlockPatch.Generate (no event per target)
            new Spec(Feature.PatchReservations, "the reserved-site guard on vanilla's block patches", "vanilla's plants, rocks and mushrooms may scatter onto companions' reserved sites", () => new Bind[]
            {
                new PatchMethod(typeof(BlockPatch), "Generate", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.ReservationPatchGuard))),
            }),
            // vanilla's one entry point for every worldgen structure placement (internal), and the villages'
            new Spec(Feature.StructuresWherePeopleLived, "the 1318 settlement guard on structures", "ruins and trader camps may spawn where nobody lived (Antarctica, ice caps)", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenStructure), "TryGenerate", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.StructureWherePeopleLivedGuard))),
            }),
            new Spec(Feature.VillagesWherePeopleLived, "the 1318 settlement guard on villages", "villages may spawn where nobody lived (Antarctica, ice caps)", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenVillage), "TryGenerate", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.VillageWherePeopleLivedGuard))),
            }),
            // vanilla's placement paths (internal) assume dry ground up to 20 blocks below sea level
            new Spec(Feature.SurfaceRuinsOutOfWater, "the water guard on surface ruins", "surface ruins may stand on the sea floor and in rivers", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenStructure), "TryGenerateRuinAtSurface", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.SurfaceStructureGuard))),
            }),
            new Spec(Feature.SurfaceStructuresOutOfWater, "the water guard on surface structures", "surface structures may stand on the sea floor and in rivers", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenStructure), "TryGenerateAtSurface", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.SurfaceStructureGuard))),
            }),
            new Spec(Feature.ShallowWaterStructures, "the river guard on shallow-water structures", "shallow-water structures may stand in the rivers' channels", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenStructure), "TryGenerateInShallowWater", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.RiverStructureGuard))),
            }),
            // decision 0026: the underground guard runs inside vanilla's private WouldOverlapAt, after vanilla's own cheap
            // checks; without it, up front in TryGenerateUnderground (the same outcome, slower)
            new Spec(Feature.UndergroundStructures, "the water guard on underground structures (after vanilla's own checks)", "it runs before vanilla's own checks instead", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenStructure), "WouldOverlapAt", new[] { typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockSchematicStructure), typeof(string) },
                    prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.DeferredUndergroundGuard))),
                new PatchMethod(typeof(WorldGenStructure), "TryGenerateUnderground", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.DeferUndergroundStructureGuard)),
                    finalizer: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.ClearDeferredUndergroundGuard))),
            })
            {
                Instead = new Spec(Feature.UndergroundStructures, "the water guard on underground structures", "underground ruins may break into rivers and lakes", () => new Bind[]
                {
                    new PatchMethod(typeof(WorldGenStructure), "TryGenerateUnderground", prefix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.UndergroundStructureGuard))),
                }),
            },
            // village buildings test only their corners for liquid, in a protected method
            new Spec(Feature.VillageBuildings, "the river guard on village buildings", "village buildings may straddle rivers", () => new Bind[]
            {
                new PatchMethod(typeof(WorldGenVillage), "CanGenerateStructureAt", postfix: P(typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.VillageBuildingGuard))),
            }),
            // the structures' schematic sizes are internal
            new Spec(Feature.StructureSizes, "the structure guards' real footprints", "the structure guards take every structure as 16 blocks across", () => new Bind[]
            {
                new ReadField<WorldGenStructure, BlockSchematicStructure[][]>("schematicDatas", r => StructurePlacementGuards.SchematicsRef = r),
            }),
            new Spec(Feature.Ponds, "the pond guard", "vanilla's ponds are dug anywhere: on ice sheets, in rivers and craters, outside the mapped wetlands", () => new Bind[]
            {
                new PatchMethod(typeof(GenPonds), "TryPlacePondAt", prefix: P(typeof(SpringsAndPonds), nameof(SpringsAndPonds.PondGuard))),
            }),
            // vanilla's springs come from private methods and its private random generator (the guards replay its draws)
            new Spec(Feature.Springs, "the spring and mountain-stream guard", "springs and rapids may come out of frozen cliffs (Antarctica, high mountains) and craters", () => new Bind[]
            {
                new ReadField<GenRivulets, LCGRandom>("rnd", r => SpringsAndPonds.RivuletRandom = r),
                new PatchMethod(typeof(GenRivulets), "tryGenRivulet", prefix: P(typeof(SpringsAndPonds), nameof(SpringsAndPonds.SpringGuard))),
                new PatchMethod(typeof(GenRivulets), "tryGenMountainSideRivers", prefix: P(typeof(SpringsAndPonds), nameof(SpringsAndPonds.SideRiverGuard))),
            }),
            // vanilla's spawn checks (protected; the runtime spawner is in the server's own assembly): no spawn event says what
            // is about to be placed where, in a way that can refuse it
            new Spec(Feature.WorldgenSpawnRanges, "the range guard on worldgen animals", "vanilla animals may be placed outside their ranges (polar bears in the tropics, goats in Antarctica)", () => new Bind[]
            {
                new PatchMethod(typeof(GenCreatures), "CanSpawnAtPosition", prefix: P(typeof(SpawnRangeGuards), nameof(SpawnRangeGuards.WorldgenSpawnRangeGuard))),
            }),
            new Spec(Feature.RuntimeSpawnRanges, "the range guard on spawning animals", "vanilla animals may spawn outside their ranges (polar bears in the tropics, goats in Antarctica)", () => new Bind[]
            {
                new PatchMethod(AccessTools.TypeByName("Vintagestory.Server.ServerSystemEntitySpawner"), "CanSpawnAt_offthread",
                    prefix: P(typeof(SpawnRangeGuards), nameof(SpawnRangeGuards.RuntimeSpawnRangeGuard)), typeName: "ServerSystemEntitySpawner"),
            }),
            // the story structures' schematics are internal: the story locations are moved by their buildings' size and given
            // their heights from them (StoryPlacer)
            new Spec(Feature.StorySchematics, "the story locations' placement", "story buildings keep vanilla's sites in new worlds and vanilla's heights in every world", () => new Bind[]
            {
                new ReadField<WorldGenStoryStructure, BlockSchematicPartial>("schematicData", r => StoryPlacer.SchematicRef = r),
            }),
            // the pick's reading of vanilla's disc deposits (no event)
            new Spec(Feature.Prospecting, "the rock floor in prospecting readings", "with a rock floor, prospecting still reports vanilla deposits below it", () => new Bind[]
            {
                new PatchMethod(typeof(DiscDepositGenerator), nameof(DiscDepositGenerator.GetPropickReading),
                    new[] { typeof(BlockPos), typeof(int), typeof(int[]), typeof(double).MakeByRefType(), typeof(double).MakeByRefType() },
                    prefix: P(typeof(RockFloorPass), nameof(RockFloorPass.RockFloorReading))),
            }),
        };

        /// <summary>
        /// Earth's regional geology (EarthGeology, bound in its StartServerSide with its own Harmony id, as before): strata,
        /// soil and deposits after vanilla's, and the Geology Additions clamp again after a world-generator reload. Each is a
        /// part of its own: vanilla's chunk passes are internal or protected methods, and no event runs inside them.
        /// </summary>
        internal static Spec[] GeologyTable() => new[]
        {
            new Spec(Feature.GeologyStrata, "regional geology's rock strata", "vanilla's rock strata", () => new Bind[]
            {
                new PatchMethod(typeof(GenRockStrataNew), "GenChunkColumn", declared: true, postfix: P(typeof(EarthGeology), nameof(EarthGeology.AfterStrata))),
            }),
            new Spec(Feature.GeologySoil, "regional geology's ground cover", "vanilla's soil, sand and gravel", () => new Bind[]
            {
                new PatchMethod(typeof(GenBlockLayers), "OnChunkColumnGeneration", declared: true, postfix: P(typeof(EarthGeology), nameof(EarthGeology.AfterSoil))),
            }),
            // reads vanilla's private block accessor and sub-deposit list (injected fields): no public member hands them over
            new Spec(Feature.GeologyDeposits, "regional geology's deposit placement", "vanilla places its deposits anywhere", () => new Bind[]
            {
                new PatchMethod(typeof(GenDeposits), "GenDeposit", declared: true, prefix: P(typeof(EarthGeology), nameof(EarthGeology.BeforeDeposit))),
            }),
            new Spec(Feature.GeologyDepositColumns, "regional geology's deposit layers", "vanilla's deposits stay as placed", () => new Bind[]
            {
                new PatchMethod(typeof(GenDeposits), "GenChunkColumn", declared: true, postfix: P(typeof(EarthGeology), nameof(EarthGeology.AfterDeposits))),
            }),
            new Spec(Feature.GeologyDepositReload, "the Geology Additions grade clamp after /wgen regen", "a world-generator reload loses the clamp until restart", () => new Bind[]
            {
                new PatchMethod(typeof(GenDeposits), "reloadWorldGen", declared: true, postfix: P(typeof(EarthGeology), nameof(EarthGeology.AfterDepositReload))),
            }),
        };

        /// <summary>
        /// Earth's ore layer (EarthOres, bound once its world's ore session starts, with its own Harmony id, as before): the ore
        /// map factor of every deposit variant (no event), the session again after /wgen regen, and Earth's bauxite after
        /// vanilla's (internal) rock strata, after regional geology's.
        /// </summary>
        internal static Spec[] OreTable() => new[]
        {
            new Spec(Feature.OreMapFactor, "Earth's ore layer", "vanilla's deposits as they come", () => new Bind[]
            {
                new PatchMethod(typeof(DepositVariant), nameof(DepositVariant.GetOreMapFactor), new[] { typeof(int), typeof(int) },
                    postfix: P(typeof(EarthOres), nameof(EarthOres.FactorPostfix))),
            }),
            new Spec(Feature.OreReload, "Earth's ore layer after /wgen regen", "after a world-generator reload Earth's ore layer stays off until restart", () => new Bind[]
            {
                new PatchMethod(typeof(GenDeposits), nameof(GenDeposits.reloadWorldGen), Type.EmptyTypes, returns: typeof(void), postfix: P(typeof(EarthOres), nameof(EarthOres.AfterReload))),
            }) { Needs = Feature.OreMapFactor },
            new Spec(Feature.OreStrata, "Earth's bauxite in the rock strata", "vanilla bauxite strata are kept and Earth bauxite lenses are not added", () => new Bind[]
            {
                new PatchMethod(typeof(GenRockStrataNew), "GenChunkColumn", new[] { typeof(IChunkColumnGenerateRequest) }, returns: typeof(void),
                    postfix: P(typeof(EarthOres), nameof(EarthOres.AfterStrata)), after: new[] { "earth.regionalgeology" }),
            }) { Needs = Feature.OreMapFactor },
        };

        /// <summary>
        /// Regional plants (EarthPlants, bound at WorldReady in Earth worlds, with its own Harmony id): every block patch vanilla
        /// tries (BlockPatch.Generate; no event per patch), the block patch config once vanilla loads it (initWorldGen), and the
        /// plants vanilla grows under and on trees, whose check sits inside the forest floor's private GenPatches (a transpiler:
        /// the check is small enough to be inlined, so its call sites are rewritten).
        /// </summary>
        internal static Spec[] PlantsTable() => new[]
        {
            new Spec(Feature.RegionalPlants, "regional plants", "plant packs, crops and berries keep their climate rules", () => new Bind[]
            {
                new PatchMethod(typeof(BlockPatch), nameof(BlockPatch.Generate), prefix: P(typeof(EarthPlants), nameof(EarthPlants.GeneratePrefix))),
                new PatchMethod(typeof(GenVegetationAndPatches), nameof(GenVegetationAndPatches.initWorldGen), postfix: P(typeof(EarthPlants), nameof(EarthPlants.InitWorldGenPostfix))),
            }),
            new Spec(Feature.RegionalPlantsOnTrees, "regional plants under and on trees", "plants growing on tree trunks keep their climate rules", () => new Bind[]
            {
                new PatchMethod(typeof(ForestFloorSystem), "GenPatches", prefix: P(typeof(EarthPlants), nameof(EarthPlants.ForestFloorPrefix)),
                    transpiler: P(typeof(EarthPlants), nameof(EarthPlants.ForestFloorTranspiler)), finalizer: P(typeof(EarthPlants), nameof(EarthPlants.ForestFloorFinalizer))),
            }) { Needs = Feature.RegionalPlants, Before = () => EarthPlants.TreeCallSites = 0, Rewrote = () => EarthPlants.TreeCallSites > 0 },
        };

        /// <summary>Plants as around 1318 (EarthPlacement, bound at WorldReady in worlds with placement rules, with its own Harmony
        /// id): the fruit trees vanilla's world generation places (no event), the block patch config once vanilla loads it, and
        /// the fruit trees' climate list the prefix reads. An error when off: the world was made with these rules.</summary>
        internal static Spec[] PlacementTable() => new[]
        {
            new Spec(Feature.Placement1318, "plants as around 1318", "plants keep vanilla placement by climate for this session", () => new Bind[]
            {
                new PatchMethod(typeof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch.TryPlaceBlockForWorldGen), prefix: P(typeof(EarthPlacement), nameof(EarthPlacement.FruitTreePrefix))),
                new PatchMethod(typeof(GenVegetationAndPatches), nameof(GenVegetationAndPatches.initWorldGen), postfix: P(typeof(EarthPlacement), nameof(EarthPlacement.InitWorldGenPostfix))),
                new FieldByName(() => typeof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch.WorldGenConds), typeof(FruitTreeWorldGenConds[])),
            }) { Critical = true },
        };

        /// <summary>The sea-level deposits (SealevelDeposits, bound at start by EarthVerticalBootstrap, once per process): the level
        /// each generator was scaled with, recorded as vanilla initialises it (no event).</summary>
        internal static Spec[] SealevelTable() => new[]
        {
            new Spec(Feature.SealevelDeposits, "the sea-level deposits' recorded levels", "deposit heights assume the sea level seen at WorldReady", () => new Bind[]
            {
                new PatchMethod(typeof(FollowSealevelDiscGenerator), nameof(FollowSealevelDiscGenerator.Init), postfix: P(typeof(SealevelDeposits), nameof(SealevelDeposits.InitPostfix))),
            }) { Bound = () => SealevelDeposits.Hooked = true },
        };

        /// <summary>The real lapse (ClimateLapse, bound once per process at EarthMapSystem.Start, on both sides, with its own Harmony
        /// id): vanilla's altitude adjustments of temperature and rain, static methods every reader of the climate map calls (no
        /// event); the four temperature ones together, so that a world reads one lapse everywhere.</summary>
        internal static Spec[] ClimateLapseTable() => new[]
        {
            new Spec(Feature.ClimateLapseTemperature, "the real lapse in vanilla's altitude adjustments", "worlds with climate rules v2 read too warm highlands", () => new Bind[]
            {
                new PatchMethod(typeof(Climate), nameof(Climate.GetScaledAdjustedTemperatureFloat), new[] { typeof(int), typeof(int) }, prefix: P(typeof(ClimateLapse), nameof(ClimateLapse.ScaledFloat))),
                new PatchMethod(typeof(Climate), nameof(Climate.GetScaledAdjustedTemperatureFloatClient), new[] { typeof(int), typeof(int) }, prefix: P(typeof(ClimateLapse), nameof(ClimateLapse.ScaledFloatClient))),
                new PatchMethod(typeof(Climate), nameof(Climate.GetScaledAdjustedTemperature), new[] { typeof(int), typeof(int) }, prefix: P(typeof(ClimateLapse), nameof(ClimateLapse.Scaled))),
                new PatchMethod(typeof(Climate), nameof(Climate.GetAdjustedTemperature), new[] { typeof(int), typeof(int) }, prefix: P(typeof(ClimateLapse), nameof(ClimateLapse.Adjusted))),
            }),
            new Spec(Feature.ClimateLapseRain, "the real rain in vanilla's altitude adjustment", "worlds with climate rules 3 read wetter highlands", () => new Bind[]
            {
                new PatchMethod(typeof(Climate), nameof(Climate.GetRainFall), new[] { typeof(int), typeof(int) }, prefix: P(typeof(ClimateLapse), nameof(ClimateLapse.Rain))),
            }),
        };

        /// <summary>
        /// Regional fauna (EarthFaunaSystem, bound at WorldReady in Earth worlds, with its own Harmony id): vanilla's worldgen spawn
        /// checks (GenCreatures, protected) and the server's runtime spawner (its own assembly, looked up by name), with its private
        /// climate helper and the position field the climate prefix reads: no spawn event says what is about to spawn where, in a
        /// way that can refuse it. All or none.
        /// </summary>
        internal static Spec[] FaunaTable() => new[]
        {
            new Spec(Feature.RegionalFauna, "regional fauna", "natural spawning stays vanilla/provider-controlled", () =>
            {
                var spawner = AccessTools.TypeByName("Vintagestory.Server.ServerSystemEntitySpawner");
                return new Bind[]
                {
                    new PatchMethod(typeof(GenCreatures), "CanSpawnAtPosition", prefix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.WorldgenPrefix))),
                    new PatchMethod(typeof(GenCreatures), "CanSpawnAtConditions", prefix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.WorldgenClimatePrefix)),
                        postfix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.WorldgenPostfix))),
                    new PatchMethod(spawner, "CanSpawnAt_offthread", prefix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.RuntimePrefix)),
                        postfix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.RuntimePostfix)), finalizer: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.RuntimeFinalizer)),
                        typeName: "ServerSystemEntitySpawner"),
                    new PatchMethod(spawner, "GetSuitableClimateTemperatureRainfall", prefix: P(typeof(EarthFaunaSystem), nameof(EarthFaunaSystem.RuntimeClimatePrefix)),
                        typeName: "ServerSystemEntitySpawner", found: m => EarthFaunaSystem.RuntimeClimateMethod = m),
                    new FieldByName(() => spawner, "ServerSystemEntitySpawner", "tmpPos", typeof(BlockPos), f => EarthFaunaSystem.RuntimeTmpPos = f),
                };
            }),
        };

        /// <summary>The tiled dungeons' map size (DungeonMapSize, at WorldReady; fields only, no Harmony): vanilla's generator keeps
        /// the map size it saw at start (an internal generator with a protected size), and nothing points it at the live map.</summary>
        internal static Spec[] DungeonTable() => new[]
        {
            new Spec(Feature.DungeonMapSize, "the tiled dungeons' live map size", "tiled dungeons keep the map size seen at start-up (in a new world sized on the Earth tab, none beyond it until a restart)", () => new Bind[]
            {
                new ReadField<ModSystemTiledDungeons, DungeonGenerator>("dungeonGen", r => EarthMap.DungeonMapSize.GeneratorRef = r),
                new ReadField<DungeonGenerator, Vec3i>("mapsize", r => EarthMap.DungeonMapSize.SizeRef = r),
            }),
        };

        /// <summary>"Earth x on Vintage Story y" for the lines of members looked up outside a table (OreVanilla), once known.</summary>
        internal static string Versions { get; private set; } = "Earth";

        /// <summary>A PatchVanilla for a mod system's own Harmony instance, logging to its logger.</summary>
        internal static PatchVanilla For(ICoreAPI api, Mod mod, Harmony harmony)
        {
            string game = api.ModLoader.GetMod("game")?.Info?.Version ?? Vintagestory.API.Config.GameVersion.ShortGameVersion;
            return new PatchVanilla(harmony, mod.Info.Version, game, line => mod.Logger.Warning("{0}", line), line => mod.Logger.Error("{0}", line));
        }

        readonly Harmony harmony;
        readonly Action<string> warn, error;
        readonly string versions;
        readonly HashSet<Feature> on = new HashSet<Feature>();
        /// <summary>Why a feature is off (the last reason), and what each bound feature applied.</summary>
        readonly Dictionary<Feature, string> offWhy = new Dictionary<Feature, string>();
        readonly Dictionary<Feature, Bind[]> applied = new Dictionary<Feature, Bind[]>();
        readonly List<string> lines = new List<string>();
        /// <summary>The vanilla handlers replaced, put back by <see cref="Release"/>.</summary>
        readonly List<ReplaceHandler> replaced = new List<ReplaceHandler>();

        /// <param name="harmony">Earth's Harmony instance.</param>
        /// <param name="earthVersion">Earth's version, for the log.</param>
        /// <param name="gameVersion">The running game's version (not the one Earth was built against), for the log.</param>
        internal PatchVanilla(Harmony harmony, string earthVersion, string gameVersion, Action<string> warn, Action<string> error)
        {
            this.harmony = harmony;
            this.warn = warn;
            this.error = error;
            versions = "Earth " + earthVersion + " on Vintage Story " + gameVersion;
            Versions = versions;
        }

        /// <summary>Whether a feature is bound this session.</summary>
        internal bool On(Feature feature) => on.Contains(feature);

        /// <summary>
        /// Null when the feature is bound and nothing it applied is provably gone now (a mod starting after Earth may have
        /// replaced a handler or removed a patch since); else what is wrong. A change that may still run Earth's code (a
        /// wrapper around Earth's handler) is no refusal: <see cref="Unverified"/> names it. EarthWorldGen asks both for
        /// Earth's terrain before an Earth world generates.
        /// </summary>
        internal string StillInPlace(Feature feature)
        {
            if (!on.Contains(feature)) return offWhy.TryGetValue(feature, out string why) ? why : feature + " was not bound";
            foreach (var b in applied[feature])
                if (b.StillInPlace(harmony) is string problem) return problem;
            return null;
        }

        /// <summary>Null, or what another mod changed of a bound feature in a way that can't be checked here (it may still call
        /// Earth's code): the feature's own per-use check decides.</summary>
        internal string Unverified(Feature feature)
        {
            if (!on.Contains(feature)) return null;
            foreach (var b in applied[feature])
                if (b.Unverified(harmony) is string note) return note;
            return null;
        }

        /// <summary>The lines logged (for the tests).</summary>
        internal IReadOnlyList<string> Lines => lines;

        /// <summary>Binds every feature of <paramref name="table"/> it can, in order; each one either wholly or not at all.</summary>
        internal void Apply(IEnumerable<Spec> table)
        {
            foreach (var spec in table)
            {
                if (spec.Needs is Feature needs && !on.Contains(needs))
                {
                    spec.Unbound?.Invoke(needs + " is off");
                    offWhy[spec.Feature] = needs + " is off";
                    continue;
                }
                string why = TryBind(spec);
                if (why == null) continue;
                // a feature with a second way logs the first's line (what it runs as instead), or the second's if that fails too
                if (spec.Instead == null) Log(spec, why);
                else if (TryBind(spec.Instead) is string instead) Log(spec.Instead, instead);
                else Log(spec, why);
            }
        }

        /// <summary>The one line for a feature that is off: the game version, the member and what is wrong, the feature, and
        /// what vanilla does instead.</summary>
        void Log(Spec spec, string why)
        {
            string line = string.Format("{0}: {1}; {2} is off: {3}.", versions, why, spec.What, spec.Without);
            lines.Add(line);
            if (spec.Critical) error(line); else warn(line);
        }

        /// <summary>Binds one feature; null when bound, else the reason (the member and what is wrong), with nothing of it left applied.</summary>
        string TryBind(Spec spec)
        {
            spec.Before?.Invoke();
            Bind[] binds;
            try { binds = spec.Binds(); }
            catch (Exception e)
            {
                string why = "a game type it names is gone (" + e.GetType().Name + ": " + e.Message + ")";
                spec.Unbound?.Invoke(why);
                offWhy[spec.Feature] = why;
                return why;
            }
            foreach (var b in binds)
            {
                string problem;
                try { problem = b.Resolve(); }
                catch (Exception e) { problem = "can't be read (" + e.GetType().Name + ": " + e.Message + ")"; }
                if (problem != null)
                {
                    string why = b.Name + " " + problem;
                    spec.Unbound?.Invoke(why);
                    offWhy[spec.Feature] = why;
                    return why;
                }
            }
            var done = new List<Bind>();
            try
            {
                // each is noted before it is applied, so that one failing half-way is taken back too
                foreach (var b in binds) { done.Add(b); b.Apply(harmony); }
                if (spec.Rewrote != null && !spec.Rewrote())
                {
                    Undo(done);
                    string why = binds[0].Name + " changed (the code Earth rewrites is not there)";
                    spec.Unbound?.Invoke(why);
                    offWhy[spec.Feature] = why;
                    return why;
                }
            }
            catch (Exception e)
            {
                Undo(done);
                var cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                string why = done[^1].Name + " could not be patched (" + cause.GetType().Name + ": " + cause.Message + ")";
                spec.Unbound?.Invoke(why);
                offWhy[spec.Feature] = why;
                return why;
            }
            on.Add(spec.Feature);
            offWhy.Remove(spec.Feature);
            applied[spec.Feature] = binds;
            replaced.AddRange(binds.OfType<ReplaceHandler>());
            spec.Bound?.Invoke();
            return null;
        }

        /// <summary>Puts vanilla's replaced handlers back. (EarthWorldGen no longer calls it when the server stops, FP-01: the
        /// handler list is that server's, and vanilla's terrain handler back in it gave a generation thread still running
        /// after the dispose vanilla terrain.)</summary>
        internal void Release()
        {
            foreach (var r in replaced) r.Undo(harmony);
            replaced.Clear();
        }

        void Undo(List<Bind> done)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                try { done[i].Undo(harmony); } catch (Exception) { /* keep undoing the rest */ }
            }
        }
    }

    /// <summary>
    /// The ore code's reads of vanilla's deposit generators past its public API, looked up through PatchVanilla's field check
    /// (once, at first use, as the ore code first needs them): when a game update removes, renames or retypes one, its
    /// accessor answers null, the ore feature that needs it stays off and <see cref="Report"/> says so once, in PatchVanilla's
    /// line. Members, as in VS 1.22.7:
    /// <list type="bullet">
    /// <item>DiscDepositGenerator.placeBlockByInBlockId (protected Dictionary&lt;int, ResolvedDepositBlock&gt;): the host blocks a
    /// deposit replaces and the ore blocks it puts there. Read by the ore maps (EarthOreGenerator), the deposit catalogue
    /// (EarthOreDeposits), the ore layers' deposit blocks (EarthOreLayers) and the Geology Additions grade clamp (EarthGeology,
    /// which edits it in place). No public member gives the resolved blocks: GetBearingBlocks gives only the hosts.</item>
    /// <item>DiscDepositGenerator.surfaceBlockByInBlockId (protected, same type): the loose-ore indicators per host. No public
    /// member gives them.</item>
    /// <item>DepositVariant.parentDeposit (internal DepositVariant): a child deposit's parent, whose ore-layer rule an unnamed
    /// child follows (EarthOreLayers.RuleFor). Kept: a map built from the public ChildDeposits would equal it only for the
    /// variants of vanilla's current list, not for a variant Earth's ore binder or a reload left behind.</item>
    /// </list>
    /// The world's deposit rate is no longer read from vanilla's private GenDeposits.chanceMultiplier: vanilla sets that field
    /// only from worldgen/deposits.json's ChanceMultiplier (GenDeposits.initAssets, 1.22.7), so it is read from that public
    /// asset (<see cref="DepositRate"/>). The ore code's patches are PatchVanilla's OreTable.
    /// </summary>
    static class OreVanilla
    {
        static readonly AccessTools.FieldRef<DiscDepositGenerator, Dictionary<int, ResolvedDepositBlock>> hostTable =
            PatchVanilla.ReadField<DiscDepositGenerator, Dictionary<int, ResolvedDepositBlock>>.Resolved("placeBlockByInBlockId", out hostProblem);
        static readonly AccessTools.FieldRef<DiscDepositGenerator, Dictionary<int, ResolvedDepositBlock>> indicatorTable =
            PatchVanilla.ReadField<DiscDepositGenerator, Dictionary<int, ResolvedDepositBlock>>.Resolved("surfaceBlockByInBlockId", out indicatorProblem);
        static readonly AccessTools.FieldRef<DepositVariant, DepositVariant> parentDeposit =
            PatchVanilla.ReadField<DepositVariant, DepositVariant>.Resolved("parentDeposit", out parentProblem);
        static readonly string hostProblem, indicatorProblem, parentProblem;

        /// <summary>Whether the providers' host tables can be read: without them there is no ore map, deposit catalogue or
        /// ore-layer gate (each says so when it starts).</summary>
        internal static bool HostTables => hostTable != null;
        internal static bool IndicatorTables => indicatorTable != null;
        /// <summary>A disc or child generator's host table (vanilla's own object: the Geology Additions clamp edits it in
        /// place, and everything Earth builds from it is read after that); null when unreadable.</summary>
        internal static Dictionary<int, ResolvedDepositBlock> HostTable(DiscDepositGenerator g) => g == null ? null : hostTable?.Invoke(g);
        /// <summary>A generator's loose-ore indicators by host; null when unreadable or when it has none.</summary>
        internal static Dictionary<int, ResolvedDepositBlock> IndicatorTable(DiscDepositGenerator g) => g == null ? null : indicatorTable?.Invoke(g);
        /// <summary>A child deposit's parent variant; null for a top-level deposit or when unreadable.</summary>
        internal static DepositVariant ParentOf(DepositVariant v) => v == null ? null : parentDeposit?.Invoke(v);

        /// <summary>The world's deposit rate as vanilla multiplies it into every attempt: worldgen/deposits.json's
        /// ChanceMultiplier, the asset vanilla's GenDeposits reads it from; null when the asset is missing or unreadable.</summary>
        internal static float? DepositRate(ICoreAPI api)
        {
            try { return api?.Assets.TryGet(new AssetLocation("worldgen/deposits.json"))?.ToObject<Vintagestory.ServerMods.Deposits>()?.ChanceMultiplier; }
            catch (Exception) { return null; }
        }

        static int reported;
        /// <summary>Says once per process which fields could not be found or changed type, and which ore feature is off for it.
        /// The patch targets are reported where they are bound (PatchVanilla.OreTable).</summary>
        internal static void Report(ILogger log)
        {
            if (log == null || System.Threading.Interlocked.Exchange(ref reported, 1) != 0) return;
            void Line(string member, string problem, string what, string without)
            {
                if (problem != null) log.Warning("{0}", string.Format("{0}: {1} {2}; {3} is off: {4}.", PatchVanilla.Versions, member, problem, what, without));
            }
            Line("DiscDepositGenerator.placeBlockByInBlockId", hostProblem, "Earth's ore maps, deposit catalogue and ore-layer gate", "vanilla deposits are used as they come");
            Line("DiscDepositGenerator.surfaceBlockByInBlockId", indicatorProblem, "Earth's loose-ore indicators", "Earth's ore maps place none and the ore layers leave every indicator in place");
            Line("DepositVariant.parentDeposit", parentProblem, "the parent's ore-layer rule for unnamed child deposits", "such a child gets the default rule");
        }
    }

    // ======================================================================================================================
    // The running game's and the client's tables (rc.2 review round 3, fix package R): kept in this region of their own, apart
    // from world generation's tables above.
    #region Runtime and client tables

    internal sealed partial class PatchVanilla
    {
        // Each feature is bound by its own system with its own Harmony id, at its own time: checked before anything is patched
        // (the parameters each patch reads by name and their types, the fields it reads), all of the feature or none of it, one
        // line naming the game version, the member and what stays vanilla's when a game update changed one, and the feature
        // off for the session. Before, each system patched on its own: a renamed parameter could leave part of a feature
        // patched and, in a 5-second watch, throw again every 5 s. None of these changes a generated block. Why each is a
        // patch (no public event or API for it in 1.22.7, checked against C:\VS-ref): see each table.

        /// <summary>Tall grass after the snow (EarthGrassWinter, on the server once a world with grass winter rules runs): vanilla
        /// swaps a tuft to and from its snow-covered variant inside Block.PerformSnowLevelUpdate, and grows soil only through
        /// BlockSoil's random ticks (decided off the main thread, set in OnServerGameTick); no event runs inside either.</summary>
        internal static Spec[] GrassWinterTable() => new[]
        {
            new Spec(Feature.GrassWinter, "tall grass after the snow (grass winter rules)", "tall grass stays as vanilla keeps it under snow", () => new Bind[]
            {
                new PatchMethod(typeof(Block), nameof(Block.PerformSnowLevelUpdate), prefix: P(typeof(EarthGrassWinter), nameof(EarthGrassWinter.SnowPrefix))),
                new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.ShouldReceiveServerGameTicks), postfix: P(typeof(EarthGrassWinter), nameof(EarthGrassWinter.ShouldTickPostfix))),
                new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.OnServerGameTick), prefix: P(typeof(EarthGrassWinter), nameof(EarthGrassWinter.OnTickPrefix))),
            }),
        };

        /// <summary>Light where snow melts (SnowMeltLight, EA-0063, on the server in Earth worlds): vanilla's snow accumulation
        /// swaps melting snow through a bulk accessor that does not relight, and no event says a block melted, so a postfix on
        /// the swap (Block.PerformSnowLevelUpdate, also patched by <see cref="GrassWinterTable"/>) notes the cell.</summary>
        internal static Spec[] SnowMeltLightTable() => new[]
        {
            new Spec(Feature.SnowMeltLight, "light where snow melts (EA-0063)", "rock where a snow block melted stays dark until something else relights it", () => new Bind[]
            {
                new PatchMethod(typeof(Block), nameof(Block.PerformSnowLevelUpdate), postfix: P(typeof(SnowMeltLight), nameof(SnowMeltLight.SnowPostfix))),
            }),
        };

        /// <summary>Dry land's grass after rain (EarthGroundMoisture, ranges step 5c, on the server once a world with moisture rules
        /// runs): the same random soil ticks as <see cref="GrassWinterTable"/>.</summary>
        internal static Spec[] GroundMoistureTable() => new[]
        {
            new Spec(Feature.GroundMoisture, "dry land greening after rain (ranges step 5c)", "dry land keeps its grass all year", () => new Bind[]
            {
                new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.ShouldReceiveServerGameTicks), postfix: P(typeof(EarthGroundMoisture), nameof(EarthGroundMoisture.ShouldTickPostfix))),
                new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.OnServerGameTick), prefix: P(typeof(EarthGroundMoisture), nameof(EarthGroundMoisture.OnTickPrefix))),
            }),
        };

        /// <summary>
        /// The seasonal grass colour (EarthGrassSeason, ranges step 5b, on the client at its start): the client's own assembly,
        /// looked up by name. The chunk mesh builds each block's colour data inside the private ChunkTesselator.TesselateBlock
        /// (a transpiler swaps its "new ColorMapData"), and blocks drawn through the world accessor take theirs from
        /// ClientWorldMap.getColorMapData (a postfix); no event or API hands over a block's colour data. Both or neither: a
        /// grass colour on one and not the other would draw terrain grass and the same grass on microblocks apart.
        /// </summary>
        internal static Spec[] GrassSeasonTable() => new[]
        {
            new Spec(Feature.GrassSeason, "the seasonal grass colour (ranges step 5b)", "grass keeps vanilla's colour", () => new Bind[]
            {
                // (two overloads in 1.22.7: the one that takes the block builds its colour data)
                new PatchMethod(AccessTools.TypeByName("Vintagestory.Client.NoObf.ChunkTesselator"), "TesselateBlock",
                    new[] { typeof(Block), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) },
                    transpiler: P(typeof(EarthGrassSeason), nameof(EarthGrassSeason.TesselateTranspiler)), typeName: "ChunkTesselator"),
                new PatchMethod(AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientWorldMap"), "getColorMapData",
                    postfix: P(typeof(EarthGrassSeason), nameof(EarthGrassSeason.ColorMapDataPostfix)), typeName: "ClientWorldMap"),
            }) { Before = () => EarthGrassSeason.TesselatorPatched = false, Rewrote = () => EarthGrassSeason.TesselatorPatched },
            // EA-0154 (decision 0091): the seven calendars in one colour map; the chunk shaders take the band from the colour
            // data (Core.GrassSeasonShader), written where the client's shader loader reads a program (as SeasonTintTable). Off:
            // grass keeps vanilla's seasons (the wet and dry seasons from the rain stay)
            new Spec(Feature.GrassSeasonShader, "Earth's grass calendars in one colour map", "grass keeps vanilla's seasons", () => new Bind[]
            {
                new PatchMethod(AccessTools.TypeByName("Vintagestory.Client.NoObf.ShaderRegistry"), "LoadShaderProgram",
                    postfix: P(typeof(EarthGrassSeason), nameof(EarthGrassSeason.ShaderLoaded)), typeName: "ShaderRegistry"),
            }),
        };

        /// <summary>The season tint at the real lapse (SeasonTintAltitude, climate rules 3, on the client at its start): the line
        /// it rewrites depends on the world's vertical scale, so it can't be an asset override of vanilla's shader include, and
        /// no event hands over a shader's source before it compiles; the client's shader loader (private, static, in the
        /// client's own assembly) is where it is read in.</summary>
        internal static Spec[] SeasonTintTable() => new[]
        {
            new Spec(Feature.SeasonTint, "the season tint at the real lapse (climate rules 3)", "the season tint keeps vanilla's altitude cooling", () => new Bind[]
            {
                new PatchMethod(AccessTools.TypeByName("Vintagestory.Client.NoObf.ShaderRegistry"), "LoadShaderProgram",
                    postfix: P(typeof(SeasonTintAltitude), nameof(SeasonTintAltitude.Loaded)), typeName: "ShaderRegistry"),
            }),
        };

        /// <summary>
        /// Earth's thunder (EarthThunder, both sides, while Earth's weather runs): vanilla's lightning and thunder come from each
        /// weather region's event (WeatherSimulationRegion.TickEvery25ms reads its rates every tick; no event hands them over),
        /// so a prefix sets the event's rates before each region's tick, finding its side through the region's protected
        /// weather system (injected). The public fields the prefix reads and sets are checked too: one renamed by a game update
        /// would otherwise throw from every region's tick. Off: vanilla's thunder events, and Earth's thunderstorms strike no
        /// lightning (EarthThunder stays off rather than striking alongside vanilla).
        /// </summary>
        internal static Spec[] ThunderTable() => new[]
        {
            new Spec(Feature.Thunder, "Earth's thunder in place of vanilla's thunder events", "vanilla's thunder events stay, and Earth's thunderstorms strike no lightning", () => new Bind[]
            {
                new FieldByName(() => typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.CurWeatherEvent), typeof(WeatherEvent)),
                new FieldByName(() => typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.weatherData), typeof(WeatherDataSnapshot)),
                new FieldByName(() => typeof(WeatherEvent), nameof(WeatherEvent), nameof(WeatherEvent.State), typeof(WeatherEventState)),
                new FieldByName(() => typeof(WeatherEventState), nameof(WeatherEventState), nameof(WeatherEventState.LightningRate), typeof(float)),
                new FieldByName(() => typeof(WeatherEventState), nameof(WeatherEventState), nameof(WeatherEventState.NearThunderRate), typeof(float)),
                new FieldByName(() => typeof(WeatherEventState), nameof(WeatherEventState), nameof(WeatherEventState.DistantThunderRate), typeof(float)),
                new FieldByName(() => typeof(WeatherEventState), nameof(WeatherEventState), nameof(WeatherEventState.LightningMinTemp), typeof(float)),
                new FieldByName(() => typeof(WeatherDataSnapshot), nameof(WeatherDataSnapshot), nameof(WeatherDataSnapshot.distantLightningRate), typeof(float)),
                new FieldByName(() => typeof(WeatherDataSnapshot), nameof(WeatherDataSnapshot), nameof(WeatherDataSnapshot.lightningMinTemp), typeof(float)),
                // (CR-W8: the finalizer gives the server's event its strike rate back after vanilla's tick, so a save keeps it)
                new PatchMethod(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.TickEvery25ms), prefix: P(typeof(EarthThunder), nameof(EarthThunder.RegionTickPrefix)),
                    finalizer: P(typeof(EarthThunder), nameof(EarthThunder.RegionTickFinalizer))),
            }),
        };

        /// <summary>
        /// The creative panel's Month and Day rows (EarthCreativeDate, on the client at its start): WorldEdit's client handler
        /// answers the panel's rows in its private OnSetValueSettings and OnGetValueSettings, and the rows are added as the
        /// panel's GuiJsonDialog is built (WorldEdit reloads its dialog files from disk, so a JSON patch would not last); no
        /// event or API for either in 1.22.7. All or none: rows that do nothing, or handlers answering for rows that aren't
        /// there, would be worse than vanilla's panel. Before rc.3 the three patches went in one by one and a failing later
        /// one left the earlier ones in.
        /// </summary>
        internal static Spec[] CreativeDateTable() => new[]
        {
            new Spec(Feature.CreativeDate, "the creative panel's date setting", "the creative panel has no Month and Day rows (/earth date still works)", () =>
            {
                var handler = AccessTools.TypeByName("Vintagestory.ServerMods.WorldEdit.WorldEditClientHandler");
                return new Bind[]
                {
                    new FieldByName(() => handler, "WorldEditClientHandler", "settingsDialog", typeof(GuiJsonDialog), f => EarthCreativeDate.SettingsDialogField = f),
                    new PatchMethod(handler, "OnSetValueSettings", new[] { typeof(string), typeof(string) },
                        prefix: P(typeof(EarthCreativeDate), nameof(EarthCreativeDate.OnSet)), typeName: "WorldEditClientHandler"),
                    new PatchMethod(handler, "OnGetValueSettings", new[] { typeof(string) },
                        prefix: P(typeof(EarthCreativeDate), nameof(EarthCreativeDate.OnGet)), typeName: "WorldEditClientHandler"),
                    new PatchMethod(typeof(GuiJsonDialog), PatchMethod.Constructor, new[] { typeof(JsonDialogSettings), typeof(ICoreClientAPI), typeof(bool) },
                        prefix: P(typeof(EarthCreativeDate), nameof(EarthCreativeDate.AddRows))),
                };
            }),
        };
    }

    #endregion
}