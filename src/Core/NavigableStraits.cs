using System;
using System.Collections.Generic;
namespace EarthMap.Core
{
    /// <summary>Reviewed shipping corridors, like the sea-level canals (0020). Physical water outside a
    /// corridor is untouched. Minimum clearance is in game blocks, independent of map scale.</summary>
    public sealed class NavigableStraits
    {
        public const int Version=1, Depth=6;
        public const double HalfWidth=12, Bank=8;
        public sealed class Strait
        {
            public readonly string Name; public readonly (double Lat,double Lon)[] Course;
            public Strait(string name,params (double,double)[] course){Name=name;Course=course;}
        }
        public static readonly Strait[] Courses={
            new("Bosporus",(41.245,29.13),(41.22,29.125),(41.182,29.095),(41.146,29.069),(41.125,29.071),(41.103,29.058),(41.084,29.065),(41.067,29.049),(41.045,29.037),(41.02,29.014),(41.00,29.005),(40.982,28.982)),
            new("Dardanelles",(40.451,26.743),(40.37,26.60),(40.29,26.55),(40.20,26.40),(40.15,26.40),(40.11,26.39),(40.08,26.35),(40.03,26.23),(39.98,26.18)),
            new("Kerch",(45.40,36.65),(45.35,36.62),(45.30,36.51),(45.25,36.47),(45.20,36.45),(45.15,36.45),(45.10,36.48)),
            new("Messina",(38.29,15.66),(38.25,15.635),(38.20,15.61),(38.15,15.60),(38.10,15.61),(38.05,15.60)),
            new("Menai",(53.25,-4.57),(53.20,-4.43),(53.18,-4.37),(53.20,-4.25),(53.22,-4.17),(53.23,-4.14),(53.25,-4.10),(53.27,-4.08),(53.30,-4.04))
        };
        public (double X,double Z)[][] WorldCourses { get; }
        /// <summary>The route search's window (dx, dz within 24), nearest first, then row by row (dz, then dx).</summary>
        static readonly (int Dx,int Dz,int D2,int Raster)[] Window=MakeWindow();
        static (int,int,int,int)[] MakeWindow()
        {
            var w=new List<(int Dx,int Dz,int D2,int Raster)>();
            int raster=0;
            for(int dz=-24;dz<=24;dz++)for(int dx=-24;dx<=24;dx++)w.Add((dx,dz,dx*dx+dz*dz,raster++));
            w.Sort((p,q)=>p.D2!=q.D2?p.D2.CompareTo(q.D2):p.Raster.CompareTo(q.Raster));
            return w.ToArray();
        }
        readonly Dictionary<(int,int),List<(double X,double Z,double Bx,double Bz)>> grid=new();
        const int Cell=64;
        /// <param name="natural">The terrain the courses follow the sea in (null: the reviewed courses as they are).</param>
        /// <param name="routes">The courses as <see cref="FollowSea"/> made them for this world before (the model cache), in place
        /// of following the sea again; ignored unless one per strait.</param>
        public NavigableStraits(EarthGeometry geo,TerrainModel natural=null,(double X,double Z)[][] routes=null)
        {
            bool cached=routes!=null&&routes.Length==Courses.Length;
            var points=WorldCourses=cached?routes:Reviewed(geo);
            if(!cached&&natural!=null)for(int i=0;i<points.Length;i++)points[i]=FollowSea(natural,points[i]);
            for(int i=0;i<points.Length;i++)
            {
                for(int k=1;k<points[i].Length;k++)
                {
                    var a=points[i][k-1];var b=points[i][k];double reach=HalfWidth+Bank;
                    for(int rz=(int)Math.Floor((Math.Min(a.Z,b.Z)-reach)/Cell);rz<=(int)Math.Floor((Math.Max(a.Z,b.Z)+reach)/Cell);rz++)
                    for(int rx=(int)Math.Floor((Math.Min(a.X,b.X)-reach)/Cell);rx<=(int)Math.Floor((Math.Max(a.X,b.X)+reach)/Cell);rx++)
                    {if(!grid.TryGetValue((rx,rz),out var list))grid[(rx,rz)]=list=new();list.Add((a.X,a.Z,b.X,b.Z));}
                }
            }
        }
        /// <summary>The model cache's part with the courses <see cref="FollowSea"/> made (layout: <see cref="WriteRoutes"/>).</summary>
        public const string CachePart="strait-routes-1";

        public static void WriteRoutes(System.IO.BinaryWriter w,(double X,double Z)[][] routes)
        {
            w.Write(routes.Length);
            foreach(var route in routes){w.Write(route.Length);foreach(var (x,z) in route){w.Write(x);w.Write(z);}}
        }

        /// <summary>The courses kept, or null when they aren't one per strait of this build.</summary>
        public static (double X,double Z)[][] ReadRoutes(System.IO.BinaryReader r)
        {
            int n=r.ReadInt32();
            if(n!=Courses.Length)return null;
            var routes=new (double X,double Z)[n][];
            for(int i=0;i<n;i++)
            {
                int m=r.ReadInt32();
                if(m<2)return null;
                routes[i]=new (double X,double Z)[m];
                for(int k=0;k<m;k++)routes[i][k]=(r.ReadDouble(),r.ReadDouble());
            }
            return routes;
        }

        /// <summary>The reviewed courses in world coordinates.</summary>
        static (double X,double Z)[][] Reviewed(EarthGeometry geo)
        {
            var points=new (double,double)[Courses.Length][];
            for(int i=0;i<points.Length;i++)
            {
                points[i]=new (double,double)[Courses[i].Course.Length];
                for(int k=0;k<points[i].Length;k++)
                {var p=Courses[i].Course[k];geo.FromGeo(p.Lat,p.Lon,out double x,out double z);points[i][k]=(x,z);}
            }
            return points;
        }

        /// <summary>Each strait's course following the existing sea in its reviewed corridor (the coarse geographic course
        /// locates a strait; it must not draw straight lines through the banks between its bends). Pure: the same terrain
        /// gives the same courses (the model cache keeps them between starts).</summary>
        public static (double X,double Z)[][] FollowSea(EarthGeometry geo,TerrainModel natural)
        {
            var points=Reviewed(geo);
            for(int i=0;i<points.Length;i++)points[i]=FollowSea(natural,points[i]);
            return points;
        }

        /// <summary>
        /// A course moved onto the sea: every 3 blocks along it the sea column nearest (less half its depth, at most 6) in a
        /// 49 x 49 window, visited nearest first, so the search stops once no column further out can win (a depth takes at
        /// most 6 off); ties go to the first in the window's rows, as when it was walked row by row (about 1.1 M columns at
        /// 1:10 before). The points are independent of each other, so they are searched on several cores (ParallelWork, from
        /// 2.0.0-rc.4; 4 s on one core at 1:10), each worker remembering the columns it read (neighbouring windows overlap); a
        /// point's pick depends only on the terrain, so the course is the same as one core makes it.
        /// </summary>
        static (double X,double Z)[] FollowSea(TerrainModel natural,(double X,double Z)[] course)
        {
            var samples=new List<(double X,double Z)>();
            for(int k=1;k<course.Length;k++)
            {
                var a=course[k-1];var b=course[k];
                int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Z-a.Z,2))/3));
                for(int t=k==1?0:1;t<=steps;t++)samples.Add((a.X+(b.X-a.X)*t/steps,a.Z+(b.Z-a.Z)*t/steps));
            }
            var route=new (double X,double Z)[samples.Count];
            // runs of 64 neighbouring points per take, so a worker's memo serves the windows it shares with the point before
            ParallelWork.For(samples.Count,()=>new Dictionary<(double,double),(bool Sea,int Depth)>(),
                (s,columns)=>route[s]=NearestSea(natural,samples[s].X,samples[s].Z,columns),chunk:64);
            return route;
        }

        static (double X,double Z) NearestSea(TerrainModel natural,double x,double z,Dictionary<(double,double),(bool Sea,int Depth)> columns)
        {
            double best=double.MaxValue;
            var pick=(x,z);
            int bestRaster=int.MaxValue;
            foreach(var o in Window)
            {
                if(o.D2>best+6)break;
                var at=(Math.Round(x)+o.Dx,Math.Round(z)+o.Dz);
                if(!columns.TryGetValue(at,out var c))
                {
                    var column=natural.GetColumn(at.Item1,at.Item2);
                    columns[at]=c=(column.Water==TerrainModel.WaterSea,column.WaterTop-column.Top);
                }
                if(!c.Sea)continue;
                double score=o.D2-.5*Math.Min(12,c.Depth);
                if(score<best||score==best&&o.Raster<bestRaster){best=score;bestRaster=o.Raster;pick=at;}
            }
            return pick;
        }

        public void Apply(int sea,double x,double z,ref TerrainModel.Column c)
        {
            if(c.Gutter || c.IceBottom<=c.Top) return;
            if(!grid.TryGetValue(((int)Math.Floor(x/Cell),(int)Math.Floor(z/Cell)),out var segments))return;
            double distance=double.MaxValue;
            foreach(var segment in segments)
            {
                var a=(X:segment.X,Z:segment.Z);var b=(X:segment.Bx,Z:segment.Bz);double reach=HalfWidth+Bank;
                if(x<Math.Min(a.X,b.X)-reach||x>Math.Max(a.X,b.X)+reach||z<Math.Min(a.Z,b.Z)-reach||z>Math.Max(a.Z,b.Z)+reach)continue;
                double dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz;
                double t=len>0?Math.Clamp(((x-a.X)*dx+(z-a.Z)*dz)/len,0,1):0;
                distance=Math.Min(distance,Math.Sqrt(Math.Pow(x-a.X-t*dx,2)+Math.Pow(z-a.Z-t*dz,2)));
            }
            if(distance>=HalfWidth+Bank)return;
            int target=sea-1-Depth;
            double weight=1-Geo.SmoothStep((distance-HalfWidth)/Bank);
            c.Top=(int)Math.Round(c.Top+(Math.Min(c.Top,target)-c.Top)*weight);
            if(c.Top<sea-1)
            {
                c.Water=TerrainModel.WaterSea;c.WaterTop=sea-1;c.River=c.NearRiver=c.DryRiver=c.Beach=c.Rapid=c.RapidsReach=false;
                c.FlowX=c.FlowZ=0;c.Ramp=c.Fall=0;c.IceBottom=int.MaxValue;
            }
        }
    }
}
