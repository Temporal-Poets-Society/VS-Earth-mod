using EarthMap.Core;
using System.Globalization;
using System.Text.Json;

static class SourcePeakSurvey
{
    public static void Write(EarthData data, JsonElement sites, string path)
    {
        bool previousOverlay = data.IslandOverlay;
        data.SetIslandOverlay(true);
        try
        {
            var geometry = new EarthGeometry { RingStart = 51200, RingLength = 409600, Wraps = true, Gutter = 1024,
                EquatorZ = 102400, BlocksPerDegreeZ = 204800.0 / 180 };
            var model = new TerrainModel(data, geometry, VerticalScale.ForWorld(512, 128), 386217) { DetailScale = 0 };
            if (data.Islands != null) model.Islands = new IslandLayer(data.Islands, geometry, 4, IslandLayer.RulesVersion);
            double Elevation(double lat, double lon)
            {
                geometry.FromGeo(lat, lon, out double x, out double z);
                return model.GetColumn(x, z).Elev;
            }
            using var csv = new StreamWriter(path);
            csv.WriteLine("site,continent,target_latitude,target_longitude,grid_elevation_m,model_elevation_without_detail_m,nearby_model_max_m,nearby_max_latitude,nearby_max_longitude,radius_degrees,step_degrees,rainfall_suitability");
            string N(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
            foreach (var site in sites.EnumerateArray())
            {
                double lat = site.GetProperty("latitude").GetDouble(), lon = site.GetProperty("longitude").GetDouble();
                double grid = data.SampleTerrain(lat, lon).Elev, point = Elevation(lat, lon);
                double maximum = point, maxLat = lat, maxLon = lon;
                // A bounded nearby model maximum is a diagnostic, not identification of a surveyed summit.
                const double step = 1.0 / 60, radius = .15;
                for (int north = -9; north <= 9; north++)
                for (int east = -9; east <= 9; east++)
                {
                    double y = lat + north * step, x = lon + east * step;
                    double elevation = Elevation(y, x);
                    if (elevation > maximum) { maximum = elevation; maxLat = y; maxLon = x; }
                }
                csv.WriteLine(string.Join(",", site.GetProperty("name").GetString(), site.GetProperty("continent").GetString(), N(lat), N(lon), N(grid), N(point), N(maximum), N(maxLat), N(maxLon), N(radius), N(step), N(data.SampleClimate(lat, lon).Rain)));
            }
        }
        finally { data.SetIslandOverlay(previousOverlay); }
    }
}
