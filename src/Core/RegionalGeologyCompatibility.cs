using System;

namespace EarthMap.Core
{
    /// <summary>Host-grade compatibility matching native DiscDepositGenerator's min(index, length-1) behavior.</summary>
    public static class RegionalGeologyCompatibility
    {
        public static T[] PadChildGrades<T>(T[] resolved,int globalGradeCount)
        {
            if(resolved==null || resolved.Length==0)return Array.Empty<T>();
            if(globalGradeCount<=resolved.Length)return resolved;
            var result=new T[globalGradeCount];
            Array.Copy(resolved,result,resolved.Length);
            for(int i=resolved.Length;i<result.Length;i++)result[i]=resolved[^1];
            return result;
        }
    }
}
