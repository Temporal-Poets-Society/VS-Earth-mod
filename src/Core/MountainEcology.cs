using System;

namespace EarthMap.Core
{
    /// <summary>Decision 0032: thermal limits, physical slope and reviewed loose volcanic ground.</summary>
    public static class MountainEcology
    {
        /// <summary>Alpine surface rules (EarthParams.AlpineSurfaceRules): 1 <see cref="Surface"/> (0032), 2 <see cref="Surface2"/>
        /// (alpine turf, thinner on ice-sheet ground, 8 November 2026).</summary>
        public const int SurfaceVersion = 2;
        public enum Cover { Soil, Rock, Scree, Snow }

        public static double TreeSuitability(double warm) => Geo.SmoothStep((warm - 10) / 3);
        public static double ShrubSuitability(double warm) => Geo.SmoothStep((warm - 5) / 5);

        public static double PlantingSuitability(double lat, double lon, double elevation, double warm, double ambientRain, bool shrub, bool wet, double drySummer=0)
        {
            double p = (shrub ? ShrubSuitability(warm) : TreeSuitability(warm)) * (1-LooseVolcanicGround(lat,lon,elevation));
            if (HotDesert(ambientRain,warm,drySummer)) p *= shrub ? .05 : wet ? .15 : .02;
            return p;
        }

        // Upper Holocene stratovolcano edifices worldwide: a loose-substrate proxy, not a mapped ash deposit.
        // Approximate historical catalogue coordinates affect ecology only, never surveyed summit heights.
        public static double LooseVolcanicGround(double lat, double lon, double elevation)
        {
            double result=0;
            foreach(var p in HoloceneCones.Near(lat))
            {
                double radius=Math.Clamp(p.Height/.3,2000,20000), dy=(lat-p.Lat)*111320;
                if(Math.Abs(dy)>=radius || elevation<p.Height*.60) continue;
                double dx=Geo.Wrap180(lon-p.Lon)*111320*Math.Cos(p.Lat*Math.PI/180);
                if(Math.Abs(dx)>=radius) continue;
                double distance=Math.Sqrt(dx*dx+dy*dy);
                result=Math.Max(result,(1-Geo.SmoothStep((distance/radius-.7)/.3))*Geo.SmoothStep((elevation/p.Height-.60)/.06));
            }
            return result;
        }

        // CHELSA precipitation/potential evaporation mapped to VS rainfall (0031), before riparian boosts.
        public static bool HotDesert(double rain, double warm, double drySummer=0) => rain < 70 && warm > 15 && drySummer < .25;

        /// <summary>Physical rise/run over several columns; independent of the selected vertical curve.</summary>
        public static double Slope(double west, double east, double north, double south, double metresX, double metresZ, double span)
            => Math.Sqrt(Math.Pow((east - west) / (span * metresX), 2) + Math.Pow((south - north) / (span * metresZ), 2));

        public static Cover Surface(double warm, double snowLine, double slope, double volcanic, double pocket, double blockSlope)
        {
            if (volcanic >= .5) return slope >= .8 ? Cover.Rock : Cover.Scree;
            if (warm < snowLine) return slope < .7 ? Cover.Snow : Cover.Rock;
            if (warm < 3) return slope >= .55 ? Cover.Rock : pocket < .15 ? Cover.Soil : Cover.Scree;
            if (warm < 8) return slope >= .45 ? Cover.Rock : pocket < .15 + .2 * (warm-3)/5 ? Cover.Soil : Cover.Scree;
            if (warm < 10)
            {
                // Grass survives on gentle alpine benches. Exposed slopes have scree and occasional soil pockets.
                if (slope >= .55) return Cover.Rock;
                double soilShare = slope >= .2 ? .15 : .3 + .2 * (warm-8)/2;
                return pocket < soilShare ? Cover.Soil : Cover.Scree;
            }
            if (warm < 13 && slope >= .45) return Cover.Rock;
            // Retain pre.8's warm lowland cliff gate as well as physical steepness.
            return slope >= .6 && blockSlope >= 1.5 ? Cover.Rock : Cover.Soil;
        }

        /// <summary>
        /// Surface rules 2 (8 November 2026): the alpine belt is turf, not scree. Rules 1 made about two thirds of gentle
        /// ground between the tree line and the snow line scree, with soil in 4 × 4-block squares, and bared slopes from a rise
        /// of 0.45 even in the montane forest: a ridge at 4,100 m in wet western Bhutan (warmest month 8 °C) was gravel with
        /// green squares, where alpine meadow and rhododendron scrub stand. Closed alpine turf reaches from the tree line (warmest
        /// month about 10 °C) up to about 6 °C; the subnival belt above breaks up into scree with patches of plants, which thin out
        /// towards the snow line (Körner, Alpine Plant Life, ch. 2: the Alps' closed grassland ends at 2,600–2,800 m, the
        /// nival belt starts near 3,000 m). Dry high mountains (western Tibet, the Pamirs, the dry puna) carry sparse alpine
        /// steppe on gravel at the same warmth: <see cref="TurfShare"/>. Mountains the last ice sheets covered
        /// (<paramref name="scoured"/>, <see cref="IceSheetGround"/>: Scandinavia, Scotland, Iceland, the Canadian Rockies) have
        /// thin young soils on hard rock and wide block fields above the low-alpine heath (play-test issue 16, Jotunheimen: "no
        /// visible rock"); the ice-sheet map stands in for that, and there turf closes only near the tree line. Bare rock from a
        /// rise of 0.8 (39°) where turf or forest holds the ground, from 0.65 (33°) above it, from 0.55 (29°) on ice-sheet ground
        /// above the tree line (<see cref="RockSlope"/>). The snow line, volcanic cones and the lowland cliff gate are as rules 1. <paramref name="patch"/> is uniform in 0..1 and smooth in space
        /// (<see cref="TurfPatch"/>), so the turf's edges follow natural patches instead of squares.
        /// </summary>
        public static Cover Surface2(double warm, double snowLine, double slope, double volcanic, double patch, double blockSlope, double rain, double scoured)
        {
            if (volcanic >= .5) return slope >= .8 ? Cover.Rock : Cover.Scree;
            if (warm < snowLine) return slope < .7 ? Cover.Snow : Cover.Rock;
            if (warm >= 13) return slope >= .6 && blockSlope >= 1.5 ? Cover.Rock : Cover.Soil;
            double rockSlope = RockSlope(warm, scoured);
            if (slope >= rockSlope) return Cover.Rock;
            // steep turf above the tree line thins to half towards the rock; the forest below keeps its ground
            double alpine = 1 - Geo.SmoothStep((warm - 8) / 4);
            double share = TurfShare(warm, rain, scoured) * (1 - .5 * alpine * Geo.SmoothStep((slope - .4) / (rockSlope - .4)));
            return patch < share ? Cover.Soil : Cover.Scree;
        }

        /// <summary>Rules 2: the rise over run from which the ground is bare rock: 0.65 in the subnival belt, 0.8 where closed turf
        /// or forest holds it (warmest month from 6 °C). Above the tree line on the last ice sheets' ground
        /// (<paramref name="scoured"/> 1, warmest month below 8 °C) from 0.55 (29°): ice-worn slabs and trough walls.</summary>
        public static double RockSlope(double warm, double scoured = 0)
        {
            double unglaciated = .65 + .15 * Geo.SmoothStep((warm - 4) / 2);
            return unglaciated + (.55 - unglaciated) * scoured * (1 - Geo.SmoothStep((warm - 8) / 2));
        }

        /// <summary>
        /// Rules 2: the share of the ground under turf (soil) between the snow line and the lowland, the rest scree: by the
        /// warmest month (none at 2 °C, closed from about 6.5 °C; on ground the last ice sheets scoured, <paramref name="scoured"/>
        /// 1, none at 4.5 °C and closed from about 10.5 °C, the tree line) and by moisture above the tree line (Earth's ambient
        /// rain from the aridity index: a fifth in the dry mountains, below 50; all from 110, an index of about 0.7). The
        /// montane forest below 12 °C keeps its ground whatever the rain.
        /// </summary>
        public static double TurfShare(double warm, double rain, double scoured = 0)
        {
            double thermal = Geo.SmoothStep((warm - 2 - 2.5 * scoured) / (4.5 + 1.5 * scoured));
            double moist = .2 + .8 * Geo.SmoothStep((rain - 50) / 60);
            double alpine = 1 - Geo.SmoothStep((warm - 8) / 4);
            return thermal * (1 - (1 - moist) * alpine);
        }

        /// <summary>Rules 2: the ground set of the ice sheets (field "glaciation": "lgm" under an ice sheet at the last glacial
        /// maximum, "older" under an earlier one only; Batchelor et al. 2019, Northern Hemisphere), also read by the ground
        /// columns.</summary>
        public const string IceSheetAsset = "earth:config/ground/global-glaciation.egts";

        public static GroundData ReadIceSheets(byte[] egts)
        {
            var data = new GroundData();
            foreach (var tile in GroundTile.ReadAll(egts)) data.Add(tile, 0);
            return data;
        }

        /// <summary>
        /// Rules 2: the share of the ground around a place that an ice sheet covered at the last glacial maximum, from nine
        /// samples 0.05° apart (the margin fades over about 10 km). Ground under an older ice sheet only has had more than a
        /// hundred thousand years to weather and counts as unglaciated. The data maps ice sheets, not mountain ice fields: the
        /// Alps, the Caucasus and the Himalaya count as unglaciated, and their alpine belts are meadow. Southern ice (Patagonia,
        /// New Zealand) is not in the data yet. 0 without the data.
        /// </summary>
        public static double IceSheetGround(GroundData iceSheets, double lat, double lon, long seed)
        {
            if (iceSheets == null) return 0;
            int n = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (iceSheets.TryCategory("glaciation", lat + .05 * j, lon + .05 * i, seed, out string stage, out _) && stage == "lgm") n++;
            return n / 9.0;
        }

        /// <summary>Rules 2: a value uniform in 0..1, smooth over about 16 and 6 blocks, for the turf's patches
        /// (<see cref="Surface2"/>): two octaves of value noise ranked through their own distribution, so a share s of the ground
        /// lies below s.</summary>
        public static double TurfPatch(double x, double z, long seed) => Rank(TurfMask(x, z, seed));

        static double TurfMask(double x, double z, long seed)
            => .7 * GeologyTransitions.Noise(x / 16, z / 16, seed, 6271) + .3 * GeologyTransitions.Noise(x / 6, z / 6, seed, 6287);

        static readonly Lazy<double[]> turfQuantiles = new Lazy<double[]>(() =>
        {
            // the mask's distribution does not depend on the seed: tabulate it once from a fixed sample
            var v = new double[16384];
            for (int i = 0; i < v.Length; i++)
                v[i] = TurfMask(GeologyTransitions.Hash(1, i, 0, 0, 41) * 1e6, GeologyTransitions.Hash(1, i, 0, 0, 43) * 1e6, 977);
            Array.Sort(v);
            return v;
        });

        static double Rank(double value)
        {
            var q = turfQuantiles.Value;
            int i = Array.BinarySearch(q, value);
            if (i < 0) i = ~i;
            return Math.Clamp(i / (double)q.Length, 0, 1);
        }
    }
}
