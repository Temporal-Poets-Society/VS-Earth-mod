using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: ecology: forest and shrub potentials, zone drivers, true temperature, frozen ground, the lapse, and the ecology data EarthWorldGen sets.
    public sealed partial class TerrainModel
    {
        /// <summary>The warmest month's mean (°C) at the ground's real elevation here (the column's own in real-altitude
        /// worlds, else the elevation map's), as the forest potential reads it.</summary>
        public double WarmMonthAtGround(double x, double z)
        {
            var g = TerrainGeoAt(x, z);
            double elev = PotentialElevation(x, z, g);
            return Data.SampleClimate(g.Lat, g.Lon).TWarm - 0.0065 * elev;
        }

        /// <summary>Habitats step 1: the vegetation zones (zones.json) in worlds saved with zone rules; null keeps the single
        /// rain curve of the forest and shrub potentials (older worlds).</summary>
        public ClimateZones Zones;
        /// <summary>Grassland kept open by fire (<see cref="FireRegime"/>) in worlds saved with fire rules and zones; null: the
        /// zones' densities as they are.</summary>
        public FireRegime Fire;
        /// <summary>The soil a place offers plants (<see cref="SoilSiteGrid.Asset"/>): the fire rules and the trees' soil
        /// fit; null when the asset is missing.</summary>
        public SoilSiteGrid Soils;
        /// <summary>GLWD v2 wetlands (<see cref="WetlandGrid.Asset"/>): the trees' wet ground, and with
        /// <see cref="OpenWetlands"/> the open bog and marsh without forest; null when the asset is missing.</summary>
        public WetlandGrid Wetlands;
        /// <summary>0048 soil rules 3: the natural flood of the rivers that keep water through the dry season
        /// (<see cref="FloodplainGrid.Asset"/>), where arid land keeps its topsoil; null when the asset is missing.</summary>
        public FloodplainGrid Floodplains;
        /// <summary>The world's wetland rules are on (EarthParams.WetlandRules): open wetland takes its share of the forest.</summary>
        public bool OpenWetlands;
        /// <summary>The world's pond rules (EarthParams.PondRules): 2 lets vanilla dig no surface ponds; 1 keeps them to the mapped
        /// wetlands (WetlandGrid.PondShare); 0 lets vanilla dig them anywhere Earth's other guards allow (older saves, or
        /// "vanilla"). Ponds in caves are vanilla's in every case.</summary>
        public int PondRules;
        /// <summary>Zone rules 2 (EarthParams.ZoneRules): trees as dense as the forest map says, without vanilla's floor
        /// (<see cref="ClimateZones.TreeKeep"/>); false in older worlds and worlds without zones.</summary>
        public bool NoTreeFloor;

        /// <summary>The ground's real elevation (m) as the forest and shrub potentials read it: the column's own in
        /// real-altitude worlds, else the elevation map's.</summary>
        double PotentialElevation(double x, double z, in EarthGeometry.GeoPos g)
            => LocalAlpineEcology ? Math.Max(MinLandElevation, GetColumn(x, z).Elev) : Math.Max(0, Data.ElevBilinear(g.Lat, g.Lon));

        /// <summary>The zone drivers at the ground here (habitats step 1).</summary>
        public ZoneDrivers ZoneDriversAt(double x, double z)
        {
            var g = TerrainGeoAt(x, z);
            return ZoneDrivers.From(Data, g.Lat, g.Lon, 0.0065 * PotentialElevation(x, z, g));
        }

        /// <summary>
        /// 0..1: how much forest the real climate supports here. With zones (habitats step 1): the zones' forest density by
        /// weight, which already holds the tree line (tundra and forest-tundra) and the dry limit (steppe, desert). Without
        /// (older worlds): trees need a warmest month above ~10 °C and enough moisture (VS rainfall from the aridity).
        /// </summary>
        public double ForestPotential(double x, double z)
        {
            var g = TerrainGeoAt(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            double elev = PotentialElevation(x, z, g);
            if (Zones != null)
            {
                var d = ZoneDrivers.From(Data, g.Lat, g.Lon, 0.0065 * elev);
                Zones.Density(d, out double zoneForest, out _);
                // fire keeps the prairie, parkland and forest-steppe open; the river belts below keep their trees
                if (Fire != null) zoneForest *= Fire.ForestKeptAt(Data, g.Lat, g.Lon, d, 0.0065 * elev);
                // open bog, marsh and saltmarsh stand without trees; wooded peat and swamp forest keep theirs
                if (OpenWetlands && Wetlands != null) zoneForest *= 1 - Wetlands.OpenShare(g.Lat, g.Lon);
                // scattered trees in the green belt of a river through dry land, not a forest
                if (zoneForest < 0.3) zoneForest = Math.Max(zoneForest, 0.3 * RiparianWeight(x, z, cs.Rain));
                return zoneForest * (1 - MountainEcology.LooseVolcanicGround(g.Lat, g.Lon, elev));
            }
            double warm = cs.TWarm - 0.0065 * elev;
            double moist = Geo.SmoothStep((cs.Rain - 60) / 80);
            // scattered trees in the green belt of a river through dry land, not a forest
            if (moist < 0.3) moist = Math.Max(moist, 0.3 * RiparianWeight(x, z, cs.Rain));
            if (!LocalAlpineEcology) return moist * Geo.SmoothStep((warm - 8) / 5);
            if (MountainEcology.HotDesert(cs.Rain, warm,cs.DrySummer)) moist = Math.Min(moist, .03);
            return moist * MountainEcology.TreeSuitability(warm) * (1 - MountainEcology.LooseVolcanicGround(g.Lat, g.Lon, elev));
        }

        /// <summary>
        /// 0..1: how many shrubs the real climate supports here (none in the desert, some in the steppe, full where it's
        /// moist), using the rainfall with the rivers' green belts.
        /// </summary>
        public double ShrubPotential(double x, double z)
        {
            var g = TerrainGeoAt(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            if (Zones != null)
            {
                // habitats step 1: the zones' shrub density by weight, more in a river's green belt through dry land
                double elev = PotentialElevation(x, z, g);
                var d = ZoneDrivers.From(Data, g.Lat, g.Lon, 0.0065 * elev);
                Zones.Density(d, out _, out double zoneShrub);
                if (Fire != null) zoneShrub *= 1 - FireRegime.ShrubCut * Fire.OpennessAt(Data, g.Lat, g.Lon, d, 0.0065 * elev);
                zoneShrub = Math.Max(zoneShrub, 0.6 * RiparianWeight(x, z, cs.Rain));
                return zoneShrub * (1 - MountainEcology.LooseVolcanicGround(g.Lat, g.Lon, elev));
            }
            double rain = RiparianRain(x, z, cs.Rain);
            double thermal = 1;
            if (LocalAlpineEcology)
            {
                double elev = Math.Max(MinLandElevation, GetColumn(x, z).Elev);
                thermal = MountainEcology.ShrubSuitability(cs.TWarm - .0065 * elev)
                    * (1 - MountainEcology.LooseVolcanicGround(g.Lat, g.Lon, elev));
            }
            return thermal * Geo.SmoothStep((rain - 40) / 60);
        }

        /// <summary>Real yearly mean temperature (°C) at a block, using the real elevation that block height stands for.</summary>
        public double TrueTemperature(double x, double z, double y)
        {
            var g = TerrainGeoAt(x, z);
            var cs = Data.SampleClimate(g.Lat, g.Lon);
            // no land lies below the Dead Sea (-430 m): deeper y is underground or sea floor, where the inverse of the
            // seabed curve would give tens of kilometres of "depth" and hundreds of degrees
            return cs.TMean - 0.0065 * Math.Max(Vertical.Elevation(y), MinLandElevation);
        }

        /// <summary>
        /// Ground frozen all year at a block: on an ice sheet, or where even the warmest month is below 0 °C at that
        /// height (permafrost). Underground the surface's warmth counts. No springs come out of such rock.
        /// </summary>
        public bool FrozenAllYear(double x, double z, double y)
        {
            var g = TerrainGeoAt(x, z);
            double warm = Data.SampleClimate(g.Lat, g.Lon).TWarm;
            // frozen at y itself is frozen at max(y, Top) too (Elevation only rises with y): most of vanilla's spring attempts
            // are near the build limit, where the climate alone answers without the column
            if (warm - 0.0065 * Vertical.Elevation(y) < 0) return true;
            var c = GetColumn(x, z);
            if (c.IceBottom <= c.Top) return true;
            return warm - 0.0065 * Vertical.Elevation(Math.Max(y, c.Top)) < 0;
        }

        /// <summary>Cooling (°C) from sea level up to block height y by the real lapse rate, 0.0065 °C per metre of the elevation y
        /// stands for (never below <see cref="MinLandElevation"/>): TrueTemperature's rule, and vanilla's altitude adjustment
        /// with climate rules v2 (ClimateLapse).</summary>
        public double LapseAt(double y) => Lapse(Vertical, y);

        public static double Lapse(VerticalScale vertical, double y) => 0.0065 * Math.Max(vertical.Elevation(y), MinLandElevation);

        /// <summary>Lowest elevation (m) taken as air temperature height (TrueTemperature): a little below the Dead Sea.</summary>
        public const double MinLandElevation = -500;
    }
}
