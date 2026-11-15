using System.Text;

static class LakeAudit
{
    public static void Write(string root,string output)
    {
        string path=Path.GetFullPath(Path.Combine(RawData.Folder(root),"hydrolakes/HydroLAKES_points_v10_shp/HydroLAKES_points_v10"));
        using var db=new BinaryReader(File.OpenRead(path+".dbf"));using var shp=new BinaryReader(File.OpenRead(path+".shp"));
        db.BaseStream.Position=4;int count=db.ReadInt32(),header=db.ReadUInt16(),size=db.ReadUInt16();
        var fields=new Dictionary<string,(int Offset,int Length)>(StringComparer.OrdinalIgnoreCase);int offset=1;
        for(int at=32;at<header-1;at+=32){db.BaseStream.Position=at;byte[] b=db.ReadBytes(32);fields[Encoding.ASCII.GetString(b,0,11).TrimEnd('\0')]=(offset,b[16]);offset+=b[16];}
        using var csv=new StreamWriter(Path.Combine(output,"iraq-hydrolakes-source.csv"));
        csv.WriteLine("Hylak_id,Lake_name,Lake_type,Grand_id,Lake_area,lat,lon");shp.BaseStream.Position=100;
        for(int i=0;i<count;i++)
        {
            shp.ReadInt32();int length=System.Net.IPAddress.NetworkToHostOrder(shp.ReadInt32())*2;long end=shp.BaseStream.Position+length;
            int type=shp.ReadInt32();if(type!=1){shp.BaseStream.Position=end;continue;}
            double lon=shp.ReadDouble(),lat=shp.ReadDouble();shp.BaseStream.Position=end;
            if(lat<30||lat>33.5||lon<43||lon>48.5)continue;
            db.BaseStream.Position=header+(long)i*size;byte[] row=db.ReadBytes(size);
            string F(string name)=>fields.TryGetValue(name,out var f)?Encoding.UTF8.GetString(row,f.Offset,f.Length).Trim().Replace(",",";"):"";
            csv.WriteLine($"{F("Hylak_id")},{F("Lake_name")},{F("Lake_type")},{F("Grand_id")},{F("Lake_area")},{lat},{lon}");
        }
    }
}
