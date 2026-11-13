using EarthMap.Core;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>Vanilla BlockCoral grows a shape across multiple columns and accepts any liquid.
    /// Check both the initial underwater attempt and each target, before coral or replacement saltwater is written. The patches
    /// below are bound by PatchVanilla (its CoralSalinity feature).</summary>
    static class EarthCorals
    {
        [System.ThreadStatic] static bool cacheActive;
        [System.ThreadStatic] static Dictionary<(int,int),bool> cache;
        [System.ThreadStatic] static long cacheModel;
        static bool Allowed(BlockPos pos)
        {
            var gen=EarthWorldGen.Instance;
            if(gen?.Active!=true || gen.Model==null) return true;
            var model=gen.Model;
            if(cacheActive)
            {
                cache??=new();
                if(cacheModel!=model.CacheId){cache.Clear();cacheModel=model.CacheId;}
                if(cache.TryGetValue((pos.X,pos.Z),out bool known))return known;
            }
            var g=model.TerrainGeoAt(pos.X,pos.Z);
            bool allowed=MarineEcology.CoralHabitat(ColumnStore.Near(model,pos.X,pos.Z),model.Data.SampleClimate(g.Lat,g.Lon));
            if(cacheActive)cache[(pos.X,pos.Z)]=allowed;
            return allowed;
        }
        internal static bool Attempt(BlockPos pos,ref bool __result,out bool __state)
        {
            __state=cacheActive;
            if(!cacheActive)cache?.Clear();
            cacheActive=true;
            if(Allowed(pos)) return true;
            __result=false; return false;
        }
        internal static void EndAttempt(bool __state) => cacheActive=__state;
        internal static bool Target(BlockPos pos) => Allowed(pos) && Allowed(pos.NorthCopy()) && Allowed(pos.SouthCopy())
            && Allowed(pos.EastCopy()) && Allowed(pos.WestCopy());
        internal static bool PlantTarget([HarmonyArgument(3)] BlockPos tmpPos) => Allowed(tmpPos);

        // Vanilla reads every shape column before replacing aquatic plants with saltwater.
        // Present dry air at disallowed targets: its search stops there, and Target vetoes placement.
        static Block ReadTarget(IBlockAccessor accessor,BlockPos pos,int layer)
            => Allowed(pos) ? accessor.GetBlock(pos,layer) : accessor.GetBlock(0);
        // Game 1.22 reads targets with the one-argument GetBlock(BlockPos).
        static Block ReadTargetDefault(IBlockAccessor accessor,BlockPos pos)
            => Allowed(pos) ? accessor.GetBlock(pos) : accessor.GetBlock(0);
        internal static IEnumerable<CodeInstruction> ReadTargets(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;
            foreach(var instruction in instructions)
            {
                if(instruction.operand is MethodInfo m && m.Name=="GetBlock" && m.ReturnType==typeof(Block))
                {
                    var p=m.GetParameters();
                    if(p.Length==2 && p[0].ParameterType==typeof(BlockPos) && p[1].ParameterType==typeof(int))
                    { instruction.opcode=OpCodes.Call; instruction.operand=AccessTools.Method(typeof(EarthCorals),nameof(ReadTarget)); count++; }
                    else if(p.Length==1 && p[0].ParameterType==typeof(BlockPos))
                    { instruction.opcode=OpCodes.Call; instruction.operand=AccessTools.Method(typeof(EarthCorals),nameof(ReadTargetDefault)); count++; }
                }
                yield return instruction;
            }
            if(count==0) throw new System.InvalidOperationException("BlockCoral target reads changed; coral guard requires review");
        }
    }
}
