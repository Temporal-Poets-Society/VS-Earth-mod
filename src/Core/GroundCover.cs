using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Ranges proposal, step 5a: how much of vanilla's tall grass dry land keeps (ground-cover.json). Vanilla's block layers
    /// put tall grass on almost all treeless soil (GenBlockLayers.PlaceTallGrass: the bar is the forest cover, at least
    /// 0.05), so since decision 0048 gave semi-arid land soil, desert edges, the steppe and Mediterranean land are carpeted.
    /// Vanilla still decides where grass grows and how tall (by rain and temperature); this only thins it by the real
    /// aridity index, the same value 0048 reads (<see cref="SoilFertility.Aridity"/>): the share kept is interpolated in
    /// log aridity between the file's knots. Colour (vanilla's climate tint by rainfall) and the soil block's own grass
    /// cover are left to vanilla.
    /// </summary>
    public sealed class GroundCover
    {
        /// <summary>Rules version saved with new worlds (EarthParams.GroundCoverRules); 0 keeps vanilla's density.</summary>
        public const int RulesVersion = 1;
        public const int FileVersion = 1;

        readonly double[] logAridity, kept;

        public GroundCover(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            int version = root.GetProperty("version").GetInt32();
            if (version != FileVersion) throw new FormatException("ground-cover.json: version " + version + ", expected " + FileVersion);
            var a = new List<double>();
            var k = new List<double>();
            foreach (var knot in root.GetProperty("tallGrassKept").EnumerateArray())
            {
                double ai = knot.GetProperty("aridity").GetDouble(), share = knot.GetProperty("kept").GetDouble();
                if (!(ai > 0) || share < 0 || share > 1) throw new FormatException("ground-cover.json: knot aridity " + ai + ", kept " + share);
                if (a.Count > 0 && Math.Log(ai) <= a[^1]) throw new FormatException("ground-cover.json: knots must rise in aridity");
                a.Add(Math.Log(ai));
                k.Add(share);
            }
            if (a.Count < 2) throw new FormatException("ground-cover.json: at least two tallGrassKept knots");
            logAridity = a.ToArray();
            kept = k.ToArray();
        }

        /// <summary>The share (0..1) of vanilla's tall grass kept at this aridity index; the end knots hold beyond the ends.</summary>
        public double TallGrassKept(double aridity)
        {
            if (!(aridity > 0)) return kept[0];
            double l = Math.Log(aridity);
            if (l <= logAridity[0]) return kept[0];
            if (l >= logAridity[^1]) return kept[^1];
            int i = 1;
            while (l > logAridity[i]) i++;
            double t = (l - logAridity[i - 1]) / (logAridity[i] - logAridity[i - 1]);
            return kept[i - 1] + t * (kept[i] - kept[i - 1]);
        }

        /// <summary>The share kept from Earth's rainfall (0..255) and summer drought, as the soil rule reads them
        /// (penmanFactor: EarthData.ClimateSample.PenmanFactor, 0 or 1 where the world doesn't use it).</summary>
        public double TallGrassKept(double rain, double drySummer, double penmanFactor = 1)
            => TallGrassKept(SoilFertility.Aridity(rain, drySummer) * (penmanFactor > 0 ? penmanFactor : 1));

        /// <summary>Whether the tall grass vanilla would place at this block column stays: a fixed hash of the position below
        /// the share, so a chunk generates the same way every time.</summary>
        public static bool Keeps(int x, int z, double share)
        {
            if (share >= 1) return true;
            if (share <= 0) return false;
            return StableHash.Column(x, z) < share;
        }
    }
}
