using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Earth's water table (decision 0059, habitat proposal 3a): a deterministic height for every land column from the
    /// ground's elevation, the drainage base around it (sea, lakes, rivers, else the lowest ground), how permeable the
    /// ground column's top layers are, the climate's aridity and the mapped wetlands. Also the aquifer reading of a
    /// 32-block chunk on Hydrate or Diedrate's 0-100 scale, from the transmissivity of the saturated ground it holds (real
    /// metres, so the same ground reads the same at every vertical scale). Pure: no game types, every number from the rules file
    /// (<see cref="RulesAsset"/>), so the game and the offline tests compute the same.
    /// </summary>
    public sealed class WaterTable
    {
        public const int RulesVersion = 1;
        public const string RulesAsset = "earth:config/water-table-v1.json";
        /// <summary>The query version other mods read (EarthWaterTable.WaterTableQueryVersion).</summary>
        public const int QueryVersion = 1;

        /// <summary>Permeability classes, poorest first. None: not ground (air, water, ice, structures, unknown blocks).</summary>
        public enum Class : byte { None = 0, VeryLow, Low, Peat, Fractured, Medium, Karst, High }
        public static readonly string[] ClassNames = { "none", "verylow", "low", "peat", "fractured", "medium", "karst", "high" };
        /// <summary>Set on a block class byte when the block is crystalline rock (its top <see cref="FracturedMetres"/> are fractured).</summary>
        public const byte CrystallineBit = 0x80;

        /// <summary>Water-table slope factor (depth per metre above the drainage base) and the hydraulic conductivity of a
        /// saturated metre (m/day) near the ground.</summary>
        public readonly double[] K = new double[8], Conductivity = new double[8];
        readonly Dictionary<string, Class> materials = new(StringComparer.Ordinal), rocks = new(StringComparer.Ordinal);
        readonly HashSet<string> crystalline = new(StringComparer.Ordinal);
        readonly List<(string Prefix, Class Class)> blockPrefixes = new();
        public readonly double FracturedMetres;
        // water table
        public readonly double RadiusMetres, MinDepthMetres, TopMetres, HumidAridity, DesertAridity, DesertDropMetres, PerchedWithinMetres, PerchedMaxHand;
        /// <summary>Mapped wetland: at this weighted share of a grid cell the cell's low ground is wetland (the lift is full);
        /// the lift fades out with the height above the drainage base, to none at <see cref="WetlandMaxHand"/>.</summary>
        public readonly double WetlandFullShare, WetlandMaxHand;
        readonly List<(string Layer, double Weight)> wetlandWeights = new();
        // salt
        public readonly double GhybenHerzberg, BrineBelowMetres;
        public readonly HashSet<string> SaltCovers = new(StringComparer.Ordinal);
        // rating
        public readonly int Samples;
        public readonly double HalfConductivityMetres, SurplusFullMm, RechargeFloor, BudykoOmega;
        /// <summary>The rating's anchors: log10 of the transmissivity (m2/day) and the rating there, rising.</summary>
        readonly double[] ratingLogT, ratingValue;
        public string Fingerprint { get; }

        static JsonDocument Parse(string json) => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        public static Class ClassOf(string name)
        {
            int i = Array.IndexOf(ClassNames, name);
            if (i <= 0) throw new FormatException("unknown permeability class '" + name + "'");
            return (Class)i;
        }

        public WaterTable(string rulesJson)
        {
            using var doc = Parse(rulesJson);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != RulesVersion) throw new FormatException("water-table rules version " + root.GetProperty("version").GetInt32() + ", expected " + RulesVersion);
            foreach (var c in root.GetProperty("classes").EnumerateObject())
            {
                if (c.Name.StartsWith("_")) continue;
                var cls = ClassOf(c.Name);
                K[(int)cls] = c.Value.GetProperty("k").GetDouble();
                Conductivity[(int)cls] = c.Value.GetProperty("conductivity").GetDouble();
            }
            for (int i = 1; i < ClassNames.Length; i++)
            {
                if (!(K[i] > 0)) throw new FormatException("permeability class '" + ClassNames[i] + "' has no k");
                if (!(Conductivity[i] > 0)) throw new FormatException("permeability class '" + ClassNames[i] + "' has no conductivity");
            }
            foreach (var m in root.GetProperty("materials").EnumerateObject()) if (!m.Name.StartsWith("_")) materials[m.Name] = ClassOf(m.Value.GetString());
            foreach (var m in root.GetProperty("rocks").EnumerateObject()) if (!m.Name.StartsWith("_")) rocks[m.Name] = ClassOf(m.Value.GetString());
            var cr = root.GetProperty("crystalline");
            foreach (var m in cr.GetProperty("materials").EnumerateArray()) crystalline.Add(m.GetString());
            FracturedMetres = cr.GetProperty("fracturedMetres").GetDouble();
            foreach (var b in root.GetProperty("blocks").EnumerateArray())
                blockPrefixes.Add((b.GetProperty("prefix").GetString(), ClassOf(b.GetProperty("class").GetString())));
            var w = root.GetProperty("waterTable");
            RadiusMetres = w.GetProperty("radiusMetres").GetDouble();
            MinDepthMetres = w.GetProperty("minDepthMetres").GetDouble();
            TopMetres = w.GetProperty("topMetres").GetDouble();
            HumidAridity = w.GetProperty("humidAridity").GetDouble();
            DesertAridity = w.GetProperty("desertAridity").GetDouble();
            DesertDropMetres = w.GetProperty("desertDropMetres").GetDouble();
            PerchedWithinMetres = w.GetProperty("perchedWithinMetres").GetDouble();
            PerchedMaxHand = w.GetProperty("perchedMaxHandMetres").GetDouble();
            WetlandFullShare = w.GetProperty("wetlandFullShare").GetDouble();
            WetlandMaxHand = w.GetProperty("wetlandMaxHandMetres").GetDouble();
            if (!(WetlandFullShare > 0) || !(WetlandMaxHand > 0)) throw new FormatException("waterTable.wetlandFullShare and wetlandMaxHandMetres must be above 0");
            foreach (var m in w.GetProperty("wetlands").EnumerateObject())
                if (!m.Name.StartsWith("_")) wetlandWeights.Add((m.Name, Math.Clamp(m.Value.GetDouble(), 0, 1)));
            var s = root.GetProperty("salt");
            GhybenHerzberg = s.GetProperty("ghybenHerzberg").GetDouble();
            BrineBelowMetres = s.GetProperty("brineBelowMetres").GetDouble();
            foreach (var c in s.GetProperty("covers").EnumerateArray()) SaltCovers.Add(c.GetString());
            var r = root.GetProperty("rating");
            Samples = r.GetProperty("samples").GetInt32();
            HalfConductivityMetres = r.GetProperty("halfConductivityMetres").GetDouble();
            var anchors = r.GetProperty("transmissivity").EnumerateArray().Select(a => (T: a[0].GetDouble(), R: a[1].GetDouble())).ToArray();
            if (anchors.Length < 2) throw new FormatException("rating.transmissivity needs at least two anchors");
            for (int i = 0; i < anchors.Length; i++)
                if (!(anchors[i].T > 0) || anchors[i].R < 0 || anchors[i].R > 100 || (i > 0 && (anchors[i].T <= anchors[i - 1].T || anchors[i].R < anchors[i - 1].R)))
                    throw new FormatException("rating.transmissivity anchors must rise: [m2/day above 0, rating 0-100]");
            ratingLogT = anchors.Select(a => Math.Log10(a.T)).ToArray();
            ratingValue = anchors.Select(a => a.R).ToArray();
            SurplusFullMm = r.GetProperty("surplusFullMm").GetDouble();
            RechargeFloor = r.GetProperty("rechargeFloor").GetDouble();
            BudykoOmega = r.GetProperty("budykoOmega").GetDouble();
            if (Samples < 1 || 32 % Samples != 0) throw new FormatException("rating.samples must divide 32");
            Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rulesJson))).Substring(0, 16);
        }

        // ------------------------------------------------------------------ classes

        /// <summary>The class of a ground-column material (rules' material name); None when the rules name none.</summary>
        public Class OfMaterial(string material) => material != null && materials.TryGetValue(material, out var c) ? c : Class.None;
        public bool HasMaterial(string material) => material != null && materials.ContainsKey(material);
        public bool IsCrystallineMaterial(string material) => material != null && crystalline.Contains(material);

        /// <summary>
        /// The class byte of a block by its code path (any domain): <c>rock-NAME</c>, <c>crackedrock-NAME</c> and ores
        /// (<c>ore-...-NAME</c>, by their host rock) by the rock's name (a ground material or the rules' rocks), then the
        /// rules' block prefixes. <see cref="CrystallineBit"/> marks crystalline rock. 0 (None): not ground.
        /// </summary>
        public byte OfBlockPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            string rock = path.StartsWith("rock-", StringComparison.Ordinal) ? path.Substring(5)
                : path.StartsWith("crackedrock-", StringComparison.Ordinal) ? path.Substring(12)
                : path.StartsWith("ore-", StringComparison.Ordinal) ? path.Substring(path.LastIndexOf('-') + 1) : null;
            if (rock != null)
            {
                var c = OfMaterial(rock);
                if (c == Class.None && !rocks.TryGetValue(rock, out c)) return 0;
                return (byte)((byte)c | (crystalline.Contains(rock) ? CrystallineBit : 0));
            }
            foreach (var (prefix, cls) in blockPrefixes)
                if (path.StartsWith(prefix, StringComparison.Ordinal)) return (byte)cls;
            return 0;
        }

        // ------------------------------------------------------------------ climate

        /// <summary>
        /// Yearly water surplus (mm) for a precipitation and aridity index (precipitation / potential evaporation), from
        /// the Budyko curve in Fu's form: evaporation / P = 1 + PET/P - (1 + (PET/P)^w)^(1/w).
        /// </summary>
        public double Surplus(double precipMm, double aridity)
        {
            if (!(precipMm > 0)) return 0;
            if (!(aridity > 0)) return 0;
            double phi = 1 / aridity, w = BudykoOmega;
            double et = 1 + phi - Math.Pow(1 + Math.Pow(phi, w), 1 / w);
            return precipMm * Math.Clamp(1 - et, 0, 1);
        }

        /// <summary>The recharge factor of the aquifer rating: deserts keep <see cref="RechargeFloor"/> (fossil water yields).</summary>
        public double Recharge(double precipMm, double aridity)
            => RechargeFloor + (1 - RechargeFloor) * Math.Min(1, Surplus(precipMm, aridity) / SurplusFullMm);

        /// <summary>0 in humid climates, 1 in deserts: how far the table flattens to the drainage base.</summary>
        public double Dryness(double aridity)
            => double.IsNaN(aridity) ? 0 : Math.Clamp((HumidAridity - aridity) / (HumidAridity - DesertAridity), 0, 1);

        // ------------------------------------------------------------------ mapped wetlands

        /// <summary>The rules' weight of each layer of a wetland grid, by the grid's layer names (<see cref="WetlandGrid.Layers"/>).
        /// Throws when the rules name a layer the grid lacks.</summary>
        public double[] WetlandWeights(string[] layers)
        {
            var w = new double[layers.Length];
            foreach (var (layer, weight) in wetlandWeights)
            {
                int i = Array.IndexOf(layers, layer);
                if (i < 0) throw new FormatException("the wetland grid has no layer '" + layer + "' (waterTable.wetlands)");
                w[i] = weight;
            }
            return w;
        }

        /// <summary>0..1: the mapped wetland at a place, the weighted sum of the grid's layer shares (GLWD v2), for <see cref="Column.Wetland"/>.</summary>
        public static double WetlandShare(WetlandGrid grid, double[] weights, double lat, double lon)
        {
            if (grid == null || weights == null) return 0;
            double s = 0;
            for (int l = 0; l < weights.Length; l++)
                if (weights[l] > 0) s += weights[l] * grid.Share(l, lat, lon);
            return Math.Min(1, s);
        }

        // ------------------------------------------------------------------ drainage base

        /// <summary>Kinds of drainage at a lattice node.</summary>
        public const byte Land = 0, Sea = 1, Lake = 2, SaltLake = 3, River = 4;

        /// <summary>A lattice node: the drainage level there (m; the water surface where there is water, else the ground).</summary>
        public struct Node { public double Level; public byte Kind; }

        /// <summary>The drainage base at one position: its level (m), and whether the sea or a salt lake lies within the radius.</summary>
        public struct Base { public double Level; public bool SeaNear, SaltLakeNear; }

        /// <summary>The drainage base with the same metres per x block everywhere (tests).</summary>
        public void BaseField(double x0, double z0, int n, double step, Func<double, double, Node> node, double metresX, double metresZ, Base[] result)
            => BaseField(x0, z0, n, step, node, _ => metresX, metresZ, result);

        /// <summary>The disc of a lattice point reaches at most this many times its north-south node count along x: from about
        /// 75.5° on, where a <see cref="RadiusMetres"/> disc would span ever more blocks east and west, it is cut there.</summary>
        public const int MaxDiscWidth = 4;

        /// <summary>
        /// The drainage base for an n x n grid of positions starting at (x0, z0), <paramref name="step"/> units apart,
        /// row-major (z outer): the lowest drainage level within <see cref="RadiusMetres"/>. Read on a lattice of about a
        /// third of the radius north-south, the same number of blocks apart along x, aligned to the world, and interpolated
        /// between lattice points, as <see cref="GroundRelief"/> does for the mean ground. Each lattice point's disc is
        /// measured in metres at its own row's latitude (<paramref name="metresXAt"/>: metres per x block at a world z), so a
        /// point's value doesn't depend on which chunk or column asks: neighbouring chunks, and the aquifer data and the
        /// query, agree. (Before, the spacing and the disc came from the asking position's latitude: they changed along
        /// chunk rows, and the writer, asking from the chunk's middle, and a column's query could differ.)
        /// </summary>
        public void BaseField(double x0, double z0, int n, double step, Func<double, double, Node> node, Func<double, double> metresXAt, double metresZ, Base[] result)
        {
            double rz = Math.Max(1, RadiusMetres / metresZ);
            double sx = Math.Max(1, Math.Round(rz / 3)), sz = sx;
            int kz = (int)Math.Ceiling(rz / sz);
            double xe = x0 + (n - 1) * step, ze = z0 + (n - 1) * step;
            int ix0 = (int)Math.Floor(x0 / sx), ix1 = (int)Math.Floor(xe / sx) + 1;
            int iz0 = (int)Math.Floor(z0 / sz), iz1 = (int)Math.Floor(ze / sz) + 1;
            int mw = ix1 - ix0 + 1, mh = iz1 - iz0 + 1;
            // each lattice row's reach along x (blocks) at its own latitude
            var rxRow = new double[mh];
            var kxRow = new int[mh];
            int kx = 0;
            for (int j = 0; j < mh; j++)
            {
                rxRow[j] = Math.Max(1, RadiusMetres / metresXAt((iz0 + j) * sz));
                kxRow[j] = Math.Min((int)Math.Ceiling(rxRow[j] / sx), MaxDiscWidth * kz);
                kx = Math.Max(kx, kxRow[j]);
            }
            int ex0 = ix0 - kx, ez0 = iz0 - kz, ew = mw + 2 * kx, eh = mh + 2 * kz;
            var e = new Node[ew * eh];
            for (int j = 0; j < eh; j++)
                for (int i = 0; i < ew; i++)
                    e[j * ew + i] = node((ex0 + i) * sx, (ez0 + j) * sz);
            var lv = new double[mw * mh];
            var sea = new double[mw * mh];
            var salt = new double[mw * mh];
            for (int j = 0; j < mh; j++)
                for (int i = 0; i < mw; i++)
                {
                    double lo = double.PositiveInfinity, rx = rxRow[j]; bool s = false, sl = false;
                    for (int dj = -kz; dj <= kz; dj++)
                        for (int di = -kxRow[j]; di <= kxRow[j]; di++)
                        {
                            double u = di * sx / rx, v = dj * sz / rz;
                            if (u * u + v * v > 1.0000001) continue;
                            var nd = e[(j + kz + dj) * ew + (i + kx + di)];
                            if (double.IsNaN(nd.Level)) continue;
                            if (nd.Level < lo) lo = nd.Level;
                            if (nd.Kind == Sea) s = true;
                            if (nd.Kind == SaltLake) sl = true;
                        }
                    lv[j * mw + i] = lo; sea[j * mw + i] = s ? 1 : 0; salt[j * mw + i] = sl ? 1 : 0;
                }
            for (int b = 0; b < n; b++)
                for (int a = 0; a < n; a++)
                {
                    double x = x0 + a * step, z = z0 + b * step;
                    double fx = x / sx - ix0, fz = z / sz - iz0;
                    int i = Math.Clamp((int)Math.Floor(fx), 0, mw - 2), j = Math.Clamp((int)Math.Floor(fz), 0, mh - 2);
                    double tx = fx - i, tz = fz - j;
                    double Lerp(double[] f) => (f[j * mw + i] * (1 - tx) + f[j * mw + i + 1] * tx) * (1 - tz) + (f[(j + 1) * mw + i] * (1 - tx) + f[(j + 1) * mw + i + 1] * tx) * tz;
                    result[b * n + a] = new Base { Level = Lerp(lv), SeaNear = Lerp(sea) >= 0.5, SaltLakeNear = Lerp(salt) >= 0.5 };
                }
        }

        // ------------------------------------------------------------------ the table at one column

        /// <summary>What the table needs to know about one column.</summary>
        public struct Column
        {
            /// <summary>Real elevation of the ground (m); under water, of the bed.</summary>
            public double Elevation;
            /// <summary>The drainage base here (m).</summary>
            public double Base;
            /// <summary>Precipitation over potential evaporation; NaN when unknown (humid).</summary>
            public double Aridity;
            /// <summary>The thickness-weighted k of the top <see cref="TopMetres"/>; NaN when unknown (medium).</summary>
            public double TopK;
            /// <summary>Metres below the ground to the top of the first very-low or low layer under a more permeable one; NaN none.</summary>
            public double PerchedTop;
            /// <summary>Open water above the ground (sea, lake, river), and its surface (m).</summary>
            public bool Water;
            public double WaterSurface;
            /// <summary>0..1: the mapped wetland here (<see cref="WetlandShare"/>); 0 none.</summary>
            public double Wetland;
        }

        public struct Table
        {
            /// <summary>Real elevation of the table (m) and its depth below the ground (negative under open water).</summary>
            public double Elevation, Depth;
            /// <summary>Height above the drainage base (m), the k used, the dryness (0 humid, 1 desert) and the mapped wetland share.</summary>
            public double Hand, K, Dryness, Wetland;
            /// <summary>Perched on a tight layer; open water; raised by a mapped wetland.</summary>
            public bool Perched, Water, Wet;
        }

        public Table At(in Column c)
        {
            if (c.Water) return new Table { Elevation = c.WaterSurface, Depth = c.Elevation - c.WaterSurface, Water = true };
            double dry = Dryness(c.Aridity);
            double k0 = double.IsNaN(c.TopK) ? K[(int)Class.Medium] : c.TopK;
            double k = k0 + (1 - k0) * dry;
            double hand = Math.Max(0, c.Elevation - Math.Min(c.Base, c.Elevation));
            double depth = MinDepthMetres + k * hand + DesertDropMetres * dry * dry;
            bool perched = false;
            if (dry <= 0 && !double.IsNaN(c.PerchedTop) && c.PerchedTop <= PerchedWithinMetres && hand <= PerchedMaxHand && c.PerchedTop < depth)
            {
                depth = c.PerchedTop; perched = true;
            }
            // mapped wetland (data before the model's guess): the grid gives a share of a 0.1 degree cell, not a place; the
            // wetland lies on the cell's low ground, so the table rises towards the surface by the share (full at
            // WetlandFullShare) times a fade with the height above the drainage base (none at WetlandMaxHand), in any
            // climate (a marsh in a dry land is still wet)
            bool wet = false;
            if (c.Wetland > 0 && depth > MinDepthMetres)
            {
                double lift = Math.Min(1, c.Wetland / WetlandFullShare) * Math.Clamp(1 - hand / WetlandMaxHand, 0, 1);
                if (lift > 0)
                {
                    depth -= (depth - MinDepthMetres) * lift;
                    wet = true;
                    if (lift >= 1) perched = false;
                }
            }
            return new Table { Elevation = c.Elevation - depth, Depth = depth, Hand = hand, K = k, Dryness = dry, Wetland = c.Wetland, Perched = perched, Wet = wet };
        }

        /// <summary>
        /// The top-layer inputs of <see cref="Column"/> from a ground column (material indices into <paramref name="materialNames"/>):
        /// the thickness-weighted k of the top <see cref="TopMetres"/>, and where a very-low or low layer starts under a
        /// more permeable one.
        /// </summary>
        public void TopOf(GroundColumns.Column col, string[] materialNames, out double topK, out double perchedTop)
        {
            topK = double.NaN; perchedTop = double.NaN;
            if (col == null || col.Count == 0) return;
            double from = 0, sum = 0, used = 0;
            bool permeableAbove = false;
            for (int i = 0; i < col.Count && from < TopMetres; i++)
            {
                double to = Math.Min(TopMetres, col.EndMetres[i]);
                var cls = OfMaterial(materialNames[col.Material[i]]);
                if (cls == Class.None) cls = Class.Medium;
                if (IsCrystallineMaterial(materialNames[col.Material[i]])) cls = Class.Fractured;
                if (to > from) { sum += K[(int)cls] * (to - from); used += to - from; }
                bool tight = cls is Class.VeryLow or Class.Low;
                if (tight && permeableAbove && double.IsNaN(perchedTop)) perchedTop = from;
                if (!tight) permeableAbove = true;
                from = col.EndMetres[i];
            }
            if (used > 0) topK = sum / used;
        }

        /// <summary>Blocks below the top at <paramref name="metres"/> below it, through the column's own layer boundaries
        /// (kept thin layers shift the blocks below); the curve where the column has no layers there.</summary>
        public static double BlocksAt(GroundColumns.Column col, double metres, in GroundColumns.DepthCurve curve)
        {
            if (col == null || col.Count == 0) return curve.Blocks(metres);
            double m0 = 0; int b0 = 0;
            for (int i = 0; i < col.Count; i++)
            {
                double m1 = col.EndMetres[i]; int b1 = col.End[i];
                if (metres < m1)
                {
                    if (b1 == int.MaxValue || double.IsInfinity(m1)) return b0 + curve.Blocks(metres) - curve.Blocks(m0);
                    return b0 + (m1 > m0 ? (metres - m0) / (m1 - m0) * (b1 - b0) : 0);
                }
                m0 = m1; b0 = b1;
            }
            return b0 + curve.Blocks(metres) - curve.Blocks(m0);
        }

        /// <summary>Metres below the top at block depth <paramref name="blocks"/> (the inverse of <see cref="BlocksAt"/>).</summary>
        public static double MetresAt(GroundColumns.Column col, double blocks, in GroundColumns.DepthCurve curve)
        {
            if (col == null || col.Count == 0) return curve.Metres(blocks);
            double m0 = 0; int b0 = 0;
            for (int i = 0; i < col.Count; i++)
            {
                double m1 = col.EndMetres[i]; int b1 = col.End[i];
                if (blocks < b1)
                {
                    // the open-ended basement follows the curve, shifted by the blocks kept thin layers added above it
                    if (b1 == int.MaxValue || double.IsInfinity(m1)) return curve.Metres(blocks - b0 + curve.Blocks(m0));
                    return m0 + (b1 > b0 ? (blocks - b0) / (b1 - b0) * (m1 - m0) : 0);
                }
                m0 = m1; b0 = b1;
            }
            return curve.Metres(blocks - b0 + curve.Blocks(m0));
        }

        /// <summary>
        /// The table's block Y: the highest saturated block. <paramref name="top"/> is the column's top ground block;
        /// <paramref name="baseY"/> the drainage surface in blocks, which the table keeps to outside deserts (so a pit beside
        /// a river never finds the table below the river). Open water: the water's top block.
        /// </summary>
        public static int BlockY(in Table t, int top, double depthBlocks, double baseY, int waterTop)
        {
            if (t.Water) return waterTop;
            int y = top - (int)Math.Floor(Math.Max(0, depthBlocks));
            if (t.Dryness < 0.5 && !double.IsNaN(baseY)) y = Math.Max(y, (int)Math.Floor(baseY));
            return Math.Min(y, top);
        }

        // ------------------------------------------------------------------ aquifer rating

        /// <summary>One sampled column of a chunk column, for <see cref="Rate"/>.</summary>
        public struct Sample
        {
            /// <summary>Top ground block, the table's block Y, and the ground's and the table's real elevation (m).</summary>
            public int Top, TableY;
            public double Elevation, TableElevation;
            /// <summary>Salt: the column is sea floor; a salt flat or salt lake is here or within the radius; the sea is within the radius.</summary>
            public bool SeaFloor, SaltFlat, SeaNear;
        }

        /// <summary>Per-chunk sums over the sampled columns of a chunk column, for <see cref="Rate"/>.</summary>
        public sealed class ChunkSums
        {
            /// <summary>Sums over the columns: each column's rating of the chunk before recharge (<see cref="RatingOf"/>), its
            /// transmissivity (m2/day), and its saturated and salty metres.</summary>
            public readonly double[] Rating, Transmissivity, Saturated, Salty;
            internal readonly double[] T, Sat, Salt;
            public ChunkSums(int chunks)
            {
                Rating = new double[chunks]; Transmissivity = new double[chunks]; Saturated = new double[chunks]; Salty = new double[chunks];
                T = new double[chunks]; Sat = new double[chunks]; Salt = new double[chunks];
            }
        }

        /// <summary>
        /// HoD's rating (0-100, before recharge) of ground with transmissivity <paramref name="t"/> (m2/day): log-linear
        /// between the rules' anchors, which put Krasny's transmissivity classes on HoD's prospecting categories.
        /// </summary>
        public double RatingOf(double t)
        {
            if (!(t > 0)) return 0;
            double l = Math.Log10(t);
            if (l <= ratingLogT[0]) return ratingValue[0];
            for (int i = 1; i < ratingLogT.Length; i++)
                if (l <= ratingLogT[i])
                    return ratingValue[i - 1] + (ratingValue[i] - ratingValue[i - 1]) * (l - ratingLogT[i - 1]) / (ratingLogT[i] - ratingLogT[i - 1]);
            return ratingValue[^1];
        }

        /// <summary>
        /// Adds one sampled column to per-chunk sums: every ground block at or below its table counts its real thickness
        /// times its class's conductivity (the chunk's transmissivity at that column), the conductivity halved at
        /// <see cref="HalfConductivityMetres"/> below the ground; crystalline rock within <see cref="FracturedMetres"/> of
        /// the rock's top counts as fractured. <paramref name="classAt"/>(y) gives the block class byte at y,
        /// <paramref name="metresAt"/>(d) the metres at d blocks below the top (a block spans d to d + 1).
        /// </summary>
        public void Accumulate(in Sample s, Func<int, byte> classAt, Func<int, double> metresAt, ChunkSums sums)
        {
            int chunks = sums.Rating.Length;
            Array.Clear(sums.T); Array.Clear(sums.Sat); Array.Clear(sums.Salt);
            double rockTop = double.NaN;
            int yTop = Math.Min(s.TableY, s.Top);
            // crystalline rock above the table still sets where the rock's fractured top begins
            for (int y = s.Top; y > yTop && y >= 1; y--)
            {
                byte b = classAt(y);
                if ((b & CrystallineBit) != 0) { rockTop = metresAt(s.Top - y); break; }
            }
            double mTop = metresAt(s.Top - yTop);
            for (int y = yTop; y >= 1; y--)
            {
                double mBottom = metresAt(s.Top - y + 1);
                byte b = classAt(y);
                int chunk = y >> 5;
                if ((b & 0x7f) != 0 && chunk < chunks)
                {
                    double thick = Math.Max(0, mBottom - mTop);
                    Evaluate(s, b, mTop, ref rockTop, out _, out double conductivity, out bool salt);
                    sums.T[chunk] += conductivity * thick;
                    sums.Sat[chunk] += thick;
                    if (salt) sums.Salt[chunk] += thick;
                }
                mTop = mBottom;
            }
            for (int i = 0; i < chunks; i++)
            {
                sums.Rating[i] += RatingOf(sums.T[i]);
                sums.Transmissivity[i] += sums.T[i];
                sums.Saturated[i] += sums.Sat[i];
                sums.Salty[i] += sums.Salt[i];
            }
        }

        /// <summary>
        /// One saturated block of class byte <paramref name="b"/> at <paramref name="m"/> metres below the ground: its class
        /// (crystalline rock within <see cref="FracturedMetres"/> of the rock's top, which the first crystalline block sets in
        /// <paramref name="rockTop"/>, is fractured), its conductivity (m/day, with the depth factor) and whether its water
        /// is salty. False for no ground.
        /// </summary>
        public bool Evaluate(in Sample s, byte b, double m, ref double rockTop, out Class cls, out double conductivity, out bool salty)
        {
            cls = (Class)(b & 0x7f); conductivity = 0; salty = false;
            if (cls == Class.None) return false;
            if ((b & CrystallineBit) != 0)
            {
                if (double.IsNaN(rockTop)) rockTop = m;
                if (m - rockTop < FracturedMetres) cls = Class.Fractured;
            }
            conductivity = Conductivity[(int)cls] / (1 + m / HalfConductivityMetres);
            salty = s.SeaFloor || s.SaltFlat || m > BrineBelowMetres
                || (s.SeaNear && s.Elevation - m < -GhybenHerzberg * Math.Max(0, s.TableElevation));
            return true;
        }

        /// <summary>
        /// HoD's 0-100 rating of each chunk: the mean of the sampled columns' ratings (the rating a well at a random place
        /// in the chunk finds; missing columns count 0) times the recharge factor; and whether its water is salty (most of
        /// its saturated metres are).
        /// </summary>
        public void Rate(ChunkSums sums, int samples, double recharge, int[] rating, bool[] saltyOut)
        {
            for (int i = 0; i < sums.Rating.Length; i++)
            {
                double mean = sums.Rating[i] / Math.Max(1, samples);
                rating[i] = (int)Math.Clamp(Math.Round(mean * recharge, MidpointRounding.AwayFromZero), 0, 100);
                saltyOut[i] = sums.Saturated[i] > 0 && sums.Salty[i] * 2 > sums.Saturated[i];
            }
        }

        public string Describe(in Table t)
            => t.Water ? string.Format(CultureInfo.InvariantCulture, "open water: table at the water surface, {0:0.#} m", t.Elevation)
             : string.Format(CultureInfo.InvariantCulture, "table at {0:0.#} m, {1:0.##} m below the ground; {2:0.#} m above the drainage base, k {3:0.00}, dryness {4:0.00}{5}{6}",
                 t.Elevation, t.Depth, t.Hand, t.K, t.Dryness, t.Perched ? ", perched on a tight layer" : "",
                 t.Wetland > 0 ? string.Format(CultureInfo.InvariantCulture, "; mapped wetland {0:0}%{1}", 100 * t.Wetland, t.Wet ? " raises it" : "") : "");
    }
}
