using System.Globalization;
using System.Text;

namespace RiverSnap
{
    /// <summary>
    /// The reservoirs bake_rivers.py de-dams: HydroLAKES points (Lehner et al.) with Lake_type 2 and Lake_area of at least
    /// 1 km², as (pour point longitude, latitude, surface elevation m). Read from the shapefile's .dbf alone (the pour point
    /// is an attribute), in file order.
    /// </summary>
    static class HydroLakes
    {
        public static List<(double Lon, double Lat, double Elev)> Reservoirs(string dbfPath)
        {
            using var fs = new FileStream(dbfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var rd = new BinaryReader(fs);
            var head = rd.ReadBytes(32);
            int records = BitConverter.ToInt32(head, 4);
            int headerLen = BitConverter.ToUInt16(head, 8), recordLen = BitConverter.ToUInt16(head, 10);
            var fields = new Dictionary<string, (int Off, int Len)>();
            int off = 1; // the deletion flag
            while (fs.Position < headerLen - 1)
            {
                var d = rd.ReadBytes(32);
                if (d[0] == 0x0D) break;
                string name = Encoding.ASCII.GetString(d, 0, 11).TrimEnd('\0', ' ');
                int len = d[16];
                fields[name] = (off, len);
                off += len;
            }
            fs.Position = headerLen;
            var need = new[] { "Lake_type", "Lake_area", "Elevation", "Pour_long", "Pour_lat" };
            foreach (var n in need) if (!fields.ContainsKey(n)) throw new InvalidDataException(dbfPath + ": no field " + n);
            var list = new List<(double, double, double)>();
            var rec = new byte[recordLen];
            double Num(string f)
            {
                var (o, l) = fields[f];
                var s = Encoding.ASCII.GetString(rec, o, l).Trim();
                return s.Length == 0 ? double.NaN : double.Parse(s, CultureInfo.InvariantCulture);
            }
            for (int i = 0; i < records; i++)
            {
                if (fs.Read(rec, 0, recordLen) != recordLen) break;
                if (rec[0] == (byte)'*') continue;
                if (Num("Lake_type") != 2 || !(Num("Lake_area") >= 1.0)) continue;
                list.Add((Num("Pour_long"), Num("Pour_lat"), Num("Elevation")));
            }
            return list;
        }
    }
}
