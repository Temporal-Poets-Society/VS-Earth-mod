using System;
using System.Collections.Generic;
using System.Linq;
namespace EarthMap.Core
{
    public static class AridHydrology
    {
        public const int Version=1;
        // HydroRIVERS Q is long-term mean discharge (m³/s), not water present today.
        // Small drainage lines in a hot desert are ephemeral; imported perennial discharge survives.
        public static bool Dry(double rain,double warm,double discharge,double wetSourceFlow=0)
            => MountainEcology.HotDesert(rain,warm) && (discharge < 10 || wetSourceFlow < 10);

        /// <summary>Route the part of Q originating in moist catchments, not accumulated desert runoff.
        /// Source data has no monthly/base-flow series; this is a conservative perennial-flow proxy.</summary>
        public static double[] WetSourceFlow(EarthData data,IReadOnlyList<RiverReach> reaches)
        {
            int n=reaches.Count;var incoming=new double[n];var wet=new double[n];var degree=new int[n];var cell=new int[n];
            for(int i=0;i<n;i++)
            {
                var r=reaches[i];int mid=r.Lat.Length/2;
                cell[i]=Math.Clamp((int)((90-r.Lat[mid])*4),0,719)*1440+Math.Clamp((int)((180+r.Lon[mid])*4),0,1439);
            }
            // each cell's own sample, so the cells are read in parallel (in runs of neighbouring cells, as the order keeps them)
            var keys=cell.Distinct().OrderBy(key=>key/1440/24*60+key%1440/24).ThenBy(key=>key).ToArray();
            var wetShare=new double[keys.Length];
            // (ER-25: in runs of 1,024 cells on ParallelWork's few workers, not a raw uncapped Parallel.ForEach)
            const int Run=1024;
            ParallelWork.For((keys.Length+Run-1)/Run,j=>
            {
                for(int k=j*Run,end=Math.Min(keys.Length,k+Run);k<end;k++)
                {
                    int key=keys[k];
                    var cs=data.SampleClimate(90-(key/1440+.5)/4,-180+(key%1440+.5)/4);
                    wetShare[k]=Geo.SmoothStep((cs.Rain-100)/40);
                }
            });
            var sourceClimate=new Dictionary<int,double>(keys.Length);
            for(int k=0;k<keys.Length;k++) sourceClimate[keys[k]]=wetShare[k];
            foreach(var r in reaches) if(r.Next>=0 && r.Next<n) degree[r.Next]++;
            var queue=new Queue<int>();for(int i=0;i<n;i++)if(degree[i]==0)queue.Enqueue(i);
            while(queue.Count>0)
            {
                int i=queue.Dequeue();var r=reaches[i];
                wet[i]=Math.Min(r.Q,wet[i]+Math.Max(0,r.Q-incoming[i])*sourceClimate[cell[i]]);
                if(r.Next>=0 && r.Next<n)
                {
                    int next=r.Next;wet[next]+=wet[i];incoming[next]+=r.Q;
                    if(--degree[next]==0)queue.Enqueue(next);
                }
            }
            return wet;
        }
    }
}
