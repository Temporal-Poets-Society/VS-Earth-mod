namespace EarthMap.Core
{
    // TerrainModel, part: the column's shared lookups (EA-0089; the review's P-3, S-1 step 1, T-R1).
    public sealed partial class TerrainModel
    {
        /// <summary>
        /// What the steps of one column have already asked at its place (ColumnForExpansion makes one per column and hands it
        /// to NaturalColumn, ApplyIslands, ApplyMouth and the shore strip): the works of 1318 (WorksAt), the coast's distance
        /// (CoastDistanceAt) and the small islands' hit. Each is kept with the latitude and longitude it was asked at and
        /// given again only for the same place, so a step asking elsewhere asks afresh: the answers are the same calls with the
        /// same arguments, and a column comes out the same. Before, each step asked again (the one-entry thread caches
        /// behind WorksAt and CoastDistanceAt took some of the repeats).
        /// </summary>
        internal struct ColumnWork
        {
            internal double Lat, Lon;
            internal bool HaveWorks, HaveCoast, HaveIsland, IslandFound;
            internal WorksHit Works;
            internal double Coast;
            internal IslandLayer.Hit Island;

            /// <summary>Starts again at another place.</summary>
            internal void At(double lat, double lon)
            {
                if (Lat == lat && Lon == lon) return;
                this = default;
                Lat = lat;
                Lon = lon;
            }
        }

        /// <summary>WorksAt(lat, lon), asked once per column.</summary>
        WorksHit WorksAt(ref ColumnWork w, double lat, double lon)
        {
            w.At(lat, lon);
            if (!w.HaveWorks)
            {
                w.Works = WorksAt(lat, lon);
                w.HaveWorks = true;
            }
            return w.Works;
        }

        /// <summary>CoastDistanceAt(lat, lon, WorksAt(lat, lon)), asked once per column. Only where Coasts is set.</summary>
        double CoastDistanceAt(ref ColumnWork w, double lat, double lon)
        {
            var wk = WorksAt(ref w, lat, lon);
            if (!w.HaveCoast)
            {
                w.Coast = CoastDistanceAt(lat, lon, wk);
                w.HaveCoast = true;
            }
            return w.Coast;
        }

        /// <summary>Islands.Query(lat, lon), asked once per column. Only where Islands is set.</summary>
        bool IslandAt(ref ColumnWork w, double lat, double lon, out IslandLayer.Hit hit)
        {
            w.At(lat, lon);
            if (!w.HaveIsland)
            {
                w.IslandFound = Islands.Query(lat, lon, out w.Island);
                w.HaveIsland = true;
            }
            hit = w.Island;
            return w.IslandFound;
        }
    }
}
