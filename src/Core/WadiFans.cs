using System;
using System.Collections.Generic;
namespace EarthMap.Core
{
    /// <summary>Small dry sediment fans where ephemeral drainage ends in land. Immutable after construction.</summary>
    public sealed class WadiFans
    {
        public sealed class Fan { public double X,Z,Dx,Dz,Length,Width; public int Top; }
        readonly Dictionary<(int,int),List<Fan>> grid=new();
        public readonly List<Fan> Sites=new();
        const int Cell=64;
        public WadiFans(TerrainModel model)
        {
            var rivers=model.Rivers;if(rivers?.Reaches==null||model.WetSourceFlow==null)return;
            var ends=new List<int>();
            for(int i=0;i<rivers.Reaches.Count;i++){var r=rivers.Reaches[i];if(r.Next<0&&rivers.Kept(i)&&r.Lat.Length>=2)ends.Add(i);}
            int Tile(int i){var r=rivers.Reaches[i];int last=r.Lat.Length-1;return (int)Math.Floor((90-r.Lat[last])/6)*60+(int)Math.Floor((r.Lon[last]+180)/6);}
            ends.Sort((a,b)=>{int ta=Tile(a),tb=Tile(b);return ta!=tb?ta.CompareTo(tb):a.CompareTo(b);});
            var climate=new Dictionary<int,EarthData.ClimateSample>(ends.Count);
            foreach(int i in ends){var r=rivers.Reaches[i];int last=r.Lat.Length-1;climate[i]=model.Data.SampleClimate(r.Lat[last],r.Lon[last]);}
            for(int i=0;i<rivers.Reaches.Count;i++)
            {
                var r=rivers.Reaches[i];if(r.Next>=0||!rivers.Kept(i)||r.Lat.Length<2)continue;
                int last=r.Lat.Length-1;var cs=climate[i];
                if(!AridHydrology.Dry(cs.Rain,cs.TWarm-.0065*r.Level[last],r.Q,model.WetSourceFlow[i]))continue;
                var terrain=model.Data.SampleTerrain(r.Lat[last],r.Lon[last]);if(terrain.Lake>0||terrain.Ocean>0)continue;
                model.Geometry.FromGeo(r.Lat[last],r.Lon[last],out double x,out double z);
                model.Geometry.FromGeo(r.Lat[last-1],r.Lon[last-1],out double px,out double pz);
                double dx=x-px,dz=z-pz,len=Math.Sqrt(dx*dx+dz*dz);if(len<.1)continue;
                double width=Math.Clamp(rivers.HalfWidth(r.Q)*2,6,40),length=width*3;
                var fan=new Fan{X=x,Z=z,Dx=dx/len,Dz=dz/len,Length=length,Width=width,Top=model.RiverWaterTop(r.Level[last])-1};Sites.Add(fan);
                double radius=length+width;
                for(int rz=(int)Math.Floor((z-radius)/Cell);rz<=(int)Math.Floor((z+radius)/Cell);rz++)
                for(int rx=(int)Math.Floor((x-radius)/Cell);rx<=(int)Math.Floor((x+radius)/Cell);rx++)
                {if(!grid.TryGetValue((rx,rz),out var list))grid[(rx,rz)]=list=new();list.Add(fan);}
            }
        }
        public void Apply(double x,double z,ref TerrainModel.Column c)
        {
            if(c.Gutter||c.Water!=0||c.IceBottom<=c.Top||!grid.TryGetValue(((int)Math.Floor(x/Cell),(int)Math.Floor(z/Cell)),out var fans))return;
            foreach(var f in fans)
            {
                double dx=x-f.X,dz=z-f.Z,along=dx*f.Dx+dz*f.Dz,across=Math.Abs(dx*f.Dz-dz*f.Dx);
                if(along>f.Length*.25||along< -f.Length)continue;
                double t=(along+f.Length)/(1.25*f.Length),width=f.Width*(.2+.8*t);
                if(across>=width||Math.Abs(c.Top-f.Top)>4)continue;
                double weight=(1-Geo.SmoothStep(across/width))*Geo.SmoothStep(t/.2)*(1-Geo.SmoothStep((t-.8)/.2));
                c.Top=(int)Math.Round(c.Top+(Math.Min(c.Top,f.Top)-c.Top)*weight);
                if(weight>.1){c.DryRiver=true;c.Beach=false;}
            }
        }
    }
}
