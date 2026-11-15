using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>Optional GeoNames surveyed elevations. Immutable, spatially indexed; never reads DEM estimates.</summary>
    /// <remarks>
    /// (EA-0116 D-R4) Kept compact: the peaks in one array in bucket order (row, then column, then the file's order), and
    /// per row its occupied buckets with where each one's peaks start, instead of a list per bucket in a dictionary (70,262
    /// lists for 155,318 peaks); the file is read by a streaming reader instead of a whole JSON document: 50.7 MB live became
    /// 18.5 MB, and <see cref="Near"/> gives the same peaks in the same order (2,388,374 asks over the globe hashed alike).
    /// </remarks>
    public sealed class PeakCatalog
    {
        // the peaks, bucket by bucket in key order (row * Columns + column), each bucket's in the file's order
        readonly KnownSummits.Peak[] all;
        // per row: the columns that hold peaks, ascending, and where each one's peaks start in all (one more: the row's end)
        readonly int[][] rowColumns = new int[Rows][];
        readonly int[][] rowStarts = new int[Rows][];
        public string Signature { get; }
        public int Count { get; }
        public PeakCatalog(byte[] bytes)
        {
            Signature = Convert.ToHexString(SHA256.HashData(bytes));
            var cells = new Dictionary<int, List<KnownSummits.Peak>>();
            int version = -1;
            bool sawPeaks = false;
            // (a UTF-8 byte order mark is skipped, as JsonDocument did)
            var json = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsSpan(3) : bytes.AsSpan();
            var r = new Utf8JsonReader(json);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("peaks.json is not an object");
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                string property = r.GetString();
                r.Read();
                if (property == "version") version = r.GetInt32();
                else if (property == "peaks")
                {
                    if (r.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("peaks.json: peaks is not an array");
                    sawPeaks = true;
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    {
                        if (r.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("peaks.json: a peak is not an array");
                        r.Read(); string name = r.GetString();
                        r.Read(); double lat = r.GetDouble();
                        r.Read(); double lon = r.GetDouble();
                        r.Read(); double height = r.GetDouble();
                        // more values after the four are not read
                        while (r.Read() && r.TokenType != JsonTokenType.EndArray) r.Skip();
                        if (!double.IsFinite(lat+lon+height) || Math.Abs(lat)>90 || Math.Abs(lon)>180 || height<=0 || height>9000)
                            throw new InvalidDataException("Invalid surveyed peak");
                        int key = Key(lat,lon);
                        if (!cells.TryGetValue(key,out var list)) cells[key]=list=new();
                        list.Add(new KnownSummits.Peak(name,lat,lon,height)); Count++;
                    }
                }
                else r.Skip();
            }
            if (version < 0 || !sawPeaks) throw new InvalidDataException("peaks.json lacks " + (version < 0 ? "its version" : "its peaks"));
            if (version != 1) throw new InvalidDataException("Unsupported peaks.json version");
            if (Count==0) throw new InvalidDataException("Empty peaks.json");
            // flattened in key order: per row its occupied columns ascending (Near's wide spans near the poles walk them)
            var keys = new List<int>(cells.Keys);
            keys.Sort();
            all = new KnownSummits.Peak[Count];
            int at = 0;
            for (int k = 0; k < keys.Count;)
            {
                int row = keys[k] / Columns, n = 0;
                while (k + n < keys.Count && keys[k + n] / Columns == row) n++;
                var cols = new int[n];
                var starts = new int[n + 1];
                for (int i = 0; i < n; i++)
                {
                    cols[i] = keys[k + i] % Columns;
                    starts[i] = at;
                    foreach (var p in cells[keys[k + i]]) all[at++] = p;
                }
                starts[n] = at;
                rowColumns[row] = cols;
                rowStarts[row] = starts;
                k += n;
            }
        }
        /// <summary>From this many longitude buckets each side (within about 4 degrees of a pole) Near walks the rows' occupied
        /// buckets instead of looking up every bucket: up to 3 x 3,599 lookups per column on the polar plateau.</summary>
        const int SparseSpan = 8;
        const int Rows=1800,Columns=3600;
        static int Key(double lat,double lon) => Math.Clamp((int)Math.Floor((lat+90)*10),0,Rows-1)*Columns
            + ((int)Math.Floor((lon+180)*10)%Columns+Columns)%Columns;

        /// <summary>The bucket (row, column)'s peaks in all, as [start, end); false where it has none.</summary>
        bool Bucket(int row, int column, out int start, out int end)
        {
            start = end = 0;
            var occ = rowColumns[row];
            if (occ == null) return false;
            int i = Array.BinarySearch(occ, column);
            if (i < 0) return false;
            start = rowStarts[row][i];
            end = rowStarts[row][i + 1];
            return true;
        }

        public Nearby Near(double lat,double lon)
        {
            // 3 km caps plus neighbours within 6 km for support separation. Fine buckets keep the global
            // catalogue out of the per-column hot path. More longitude buckets are needed near the poles.
            int span=Math.Min(Columns/2-1,(int)Math.Ceiling(.6/Math.Max(.0001,Math.Cos(lat*Math.PI/180))));
            int row=Math.Clamp((int)Math.Floor((lat+90)*10),0,Rows-1), col=(int)Math.Floor((lon+180)*10);
            if (span <= SparseSpan) return new Nearby(this,Math.Max(0,row-1),Math.Min(Rows-1,row+1),col,span);
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
                    int i = (i0 + k) % occ.Length, c = occ[i];
                    if (((c - start) % Columns + Columns) % Columns >= width) break;
                    for (int p = rowStarts[r][i]; p < rowStarts[r][i + 1]; p++) flat.Add(all[p]);
                }
            }
            return new Nearby(flat);
        }
        public readonly struct Nearby : IEnumerable<KnownSummits.Peak>
        {
            readonly PeakCatalog catalog;
            readonly int first,last,col,span;
            readonly List<KnownSummits.Peak> flat;
            internal Nearby(PeakCatalog catalog,int first,int last,int col,int span)
            {this.catalog=catalog;this.first=first;this.last=last;this.col=col;this.span=span;flat=null;}
            internal Nearby(List<KnownSummits.Peak> flat)
            {catalog=null;first=0;last=-1;col=0;span=0;this.flat=flat;}
            public Enumerator GetEnumerator()=>flat!=null?new(flat):new(catalog,first,last,col,span);
            IEnumerator<KnownSummits.Peak> IEnumerable<KnownSummits.Peak>.GetEnumerator()=>GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
            public struct Enumerator : IEnumerator<KnownSummits.Peak>
            {
                readonly PeakCatalog catalog;
                readonly int last,col,span;
                readonly List<KnownSummits.Peak> list;
                int row,delta,index,end;
                internal Enumerator(PeakCatalog catalog,int first,int last,int col,int span)
                {this.catalog=catalog;this.last=last;this.col=col;this.span=span;row=first;delta=-span;index=0;end=0;list=null;Current=null;}
                // a list already in order (Near near the poles): rows past the last, so only the list is read
                internal Enumerator(List<KnownSummits.Peak> flat)
                {catalog=null;last=-1;col=0;span=0;row=0;delta=0;index=0;end=flat.Count;list=flat;Current=null;}
                public KnownSummits.Peak Current {get;private set;}
                object System.Collections.IEnumerator.Current=>Current;
                public bool MoveNext()
                {
                    if(list!=null){if(index<end){Current=list[index++];return true;}return false;}
                    if(index<end){Current=catalog.all[index++];return true;}
                    while(row<=last)
                    {
                        int r=row,c=((col+delta)%Columns+Columns)%Columns;
                        if(++delta>span){row++;delta=-span;}
                        if(catalog.Bucket(r,c,out index,out end) && index<end){Current=catalog.all[index++];return true;}
                    }
                    return false;
                }
                public void Reset()=>throw new NotSupportedException();
                public void Dispose() { }
            }
        }
    }
}
