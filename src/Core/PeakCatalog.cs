using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>Optional GeoNames surveyed elevations. Immutable, spatially indexed; never reads DEM estimates.</summary>
    public sealed class PeakCatalog
    {
        readonly Dictionary<int, List<KnownSummits.Peak>> cells = new();
        public string Signature { get; }
        public int Count { get; }
        public PeakCatalog(byte[] bytes)
        {
            Signature = Convert.ToHexString(SHA256.HashData(bytes));
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported peaks.json version");
            foreach (var row in doc.RootElement.GetProperty("peaks").EnumerateArray())
            {
                string name = row[0].GetString();
                double lat = row[1].GetDouble(), lon = row[2].GetDouble(), height = row[3].GetDouble();
                if (!double.IsFinite(lat+lon+height) || Math.Abs(lat)>90 || Math.Abs(lon)>180 || height<=0 || height>9000)
                    throw new InvalidDataException("Invalid surveyed peak");
                int key = Key(lat,lon);
                if (!cells.TryGetValue(key,out var list)) cells[key]=list=new();
                list.Add(new KnownSummits.Peak(name,lat,lon,height)); Count++;
            }
            if (Count==0) throw new InvalidDataException("Empty peaks.json");
            // per row, the columns that hold peaks, ascending (Near's wide spans near the poles)
            var occupied = new List<int>[Rows];
            foreach (int key in cells.Keys) (occupied[key / Columns] ??= new()).Add(key % Columns);
            for (int r = 0; r < Rows; r++) if (occupied[r] != null) { occupied[r].Sort(); rowColumns[r] = occupied[r].ToArray(); }
        }
        readonly int[][] rowColumns = new int[Rows][];
        /// <summary>From this many longitude buckets each side (within about 4 degrees of a pole) Near walks the rows' occupied
        /// buckets instead of looking up every bucket: up to 3 x 3,599 lookups per column on the polar plateau.</summary>
        const int SparseSpan = 8;
        const int Rows=1800,Columns=3600;
        static int Key(double lat,double lon) => Math.Clamp((int)Math.Floor((lat+90)*10),0,Rows-1)*Columns
            + ((int)Math.Floor((lon+180)*10)%Columns+Columns)%Columns;
        public Nearby Near(double lat,double lon)
        {
            // 3 km caps plus neighbours within 6 km for support separation. Fine buckets keep the global
            // catalogue out of the per-column hot path. More longitude buckets are needed near the poles.
            int span=Math.Min(Columns/2-1,(int)Math.Ceiling(.6/Math.Max(.0001,Math.Cos(lat*Math.PI/180))));
            int row=Math.Clamp((int)Math.Floor((lat+90)*10),0,Rows-1), col=(int)Math.Floor((lon+180)*10);
            if (span <= SparseSpan) return new Nearby(cells,Math.Max(0,row-1),Math.Min(Rows-1,row+1),col,span);
            // the same peaks in the same order (row by row, west to east from col - span), from the occupied buckets only
            var flat = new List<KnownSummits.Peak>();
            int width = 2 * span + 1;
            for (int r = Math.Max(0,row-1); r <= Math.Min(Rows-1,row+1); r++)
            {
                var occ = rowColumns[r];
                if (occ == null) continue;
                int start = ((col - span) % Columns + Columns) % Columns, i0 = Array.BinarySearch(occ, start);
                if (i0 < 0) i0 = ~i0;
                for (int k = 0; k < occ.Length; k++)
                {
                    int c = occ[(i0 + k) % occ.Length];
                    if (((c - start) % Columns + Columns) % Columns >= width) break;
                    flat.AddRange(cells[r * Columns + c]);
                }
            }
            return new Nearby(flat);
        }
        public readonly struct Nearby : IEnumerable<KnownSummits.Peak>
        {
            readonly Dictionary<int,List<KnownSummits.Peak>> cells;
            readonly int first,last,col,span;
            readonly List<KnownSummits.Peak> flat;
            internal Nearby(Dictionary<int,List<KnownSummits.Peak>> cells,int first,int last,int col,int span)
            {this.cells=cells;this.first=first;this.last=last;this.col=col;this.span=span;flat=null;}
            internal Nearby(List<KnownSummits.Peak> flat)
            {cells=null;first=0;last=-1;col=0;span=0;this.flat=flat;}
            public Enumerator GetEnumerator()=>flat!=null?new(flat):new(cells,first,last,col,span);
            IEnumerator<KnownSummits.Peak> IEnumerable<KnownSummits.Peak>.GetEnumerator()=>GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
            public struct Enumerator : IEnumerator<KnownSummits.Peak>
            {
                readonly Dictionary<int,List<KnownSummits.Peak>> cells;
                readonly int last,col,span;
                int row,delta,index;List<KnownSummits.Peak> list;
                internal Enumerator(Dictionary<int,List<KnownSummits.Peak>> cells,int first,int last,int col,int span)
                {this.cells=cells;this.last=last;this.col=col;this.span=span;row=first;delta=-span;index=0;list=null;Current=null;}
                // a list already in order (Near near the poles): rows past the last, so only the list is read
                internal Enumerator(List<KnownSummits.Peak> flat)
                {cells=null;last=-1;col=0;span=0;row=0;delta=0;index=0;list=flat;Current=null;}
                public KnownSummits.Peak Current {get;private set;}
                object System.Collections.IEnumerator.Current=>Current;
                public bool MoveNext()
                {
                    if(list!=null && index<list.Count){Current=list[index++];return true;}
                    while(row<=last)
                    {
                        int key=row*Columns+((col+delta)%Columns+Columns)%Columns;
                        if(++delta>span){row++;delta=-span;}
                        if(cells.TryGetValue(key,out list) && list.Count>0){index=1;Current=list[0];return true;}
                    }
                    return false;
                }
                public void Reset()=>throw new NotSupportedException();
                public void Dispose() { }
            }
        }
    }
}
