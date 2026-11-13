using System.Globalization;
using System.Text.Json;

public static class GeoNamesBake
{
    // GeoNames ids whose explicit elevation predates a newer official survey.
    // 1283416 Mount Everest: 8848 m; the 2020 China-Nepal survey gives 8848.86 m (Everest's block is set by it).
    static readonly Dictionary<long,int> NewerSurveys=new(){[1283416]=8849};

    public static byte[] Bake(TextReader reader,out Dictionary<string,long> counts)
    {
        var stats=new Dictionary<string,long>();
        void Count(string key){stats.TryGetValue(key,out long n);stats[key]=n+1;}
        var rows=new Dictionary<(double Lat,double Lon),(string Name,int Height,long Id)>();
        string line;
        while((line=reader.ReadLine())!=null)
        {
            Count("input");var c=line.Split('\t');
            if(c.Length!=19){Count("bad_columns");continue;}
            if(c[6]!="T" || c[7] is not ("PK" or "PKS" or "MT" or "VLC")){Count("other_feature");continue;}
            if(!long.TryParse(c[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out long id)
                || !double.TryParse(c[4],NumberStyles.Float,CultureInfo.InvariantCulture,out double lat)
                || !double.TryParse(c[5],NumberStyles.Float,CultureInfo.InvariantCulture,out double lon)
                || !int.TryParse(c[15],NumberStyles.Integer,CultureInfo.InvariantCulture,out int height))
            {Count("missing_or_invalid_explicit_elevation");continue;}
            if(!double.IsFinite(lat+lon)||Math.Abs(lat)>90||Math.Abs(lon)>180||height<=0||height>9000){Count("out_of_range");continue;}
            if(NewerSurveys.TryGetValue(id,out int surveyed)){height=surveyed;Count("newer_survey");}
            var key=(lat,lon);
            if(!rows.TryGetValue(key,out var old)||height>old.Height||height==old.Height&&id<old.Id) rows[key]=(c[1],height,id);
        }
        // Two named records closer than 1.4 km cannot both support a broad cap at this data resolution.
        // Highest surveyed record wins, deterministically; preserve separate major summits such as Everest/Lhotse.
        var accepted=new Dictionary<(int,int),List<(double Lat,double Lon)>>();
        var peaks=new List<(string Name,double Lat,double Lon,int Height)>();
        foreach(var row in rows.OrderByDescending(p=>p.Value.Height).ThenBy(p=>p.Value.Id))
        {
            var p=row.Key;int r=(int)Math.Floor(p.Lat),c=(int)Math.Floor(p.Lon);bool close=false;
            int span=Math.Min(179,(int)Math.Ceiling(.013/Math.Max(.0001,Math.Cos(p.Lat*Math.PI/180))));
            for(int dr=-1;dr<=1;dr++)for(int dc=-span;dc<=span;dc++)
            {
                int col=((c+dc+180)%360+360)%360-180;
                if(!accepted.TryGetValue((r+dr,col),out var bucket))continue;
                foreach(var q in bucket)
                {
                    double dl=(p.Lon-q.Lon+540)%360-180;
                    double dx=dl*111320*Math.Cos(p.Lat*Math.PI/180),dy=(p.Lat-q.Lat)*111320;
                    if(dx*dx+dy*dy<1400*1400){close=true;break;}
                }
            }
            if(close){Count("near_duplicate");continue;}
            if(!accepted.TryGetValue((r,c),out var list))accepted[(r,c)]=list=new();list.Add(p);
            peaks.Add((row.Value.Name,p.Lat,p.Lon,row.Value.Height));
        }
        stats["retained"]=peaks.Count;counts=stats;
        if(peaks.Count==0)throw new InvalidDataException("No explicit elevations; output not written");
        return JsonSerializer.SerializeToUtf8Bytes(new{version=1,source="GeoNames allCountries; CC BY 4.0; https://download.geonames.org/export/dump/readme.txt; explicit elevation column 16, never DEM column 17",peaks=peaks.OrderBy(p=>p.Lat).ThenBy(p=>p.Lon).Select(p=>new object[]{p.Name,p.Lat,p.Lon,p.Height})});
    }
}
