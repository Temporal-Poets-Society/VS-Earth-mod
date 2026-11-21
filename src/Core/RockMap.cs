using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>
    /// Geology rules 4 (EA-0198, decision 0106): the 30″ rock set (tools/ground/bake_rock_30s.py), 10° tiles read lazily
    /// through <see cref="GroundTile"/>. Every field of a tile shares one grid, so one cell answers all of them:
    /// <c>litho</c> GLiM's code, <c>macro</c> Macrostrat's code in the same vocabulary (national maps at 1:1M or finer),
    /// <c>source</c> the Macrostrat map that drew it, <c>bedrock</c> the bedrock under cover, water or ice (nodata: the same
    /// as the surface). Codes are GLiM's six letters; the rules map them to profiles.
    /// </summary>
    public sealed class RockMap
    {
        public const string Litho = "litho", Macro = "macro", SourceField = "source", Bedrock = "bedrock";

        /// <summary>A cell's answer: the surface code (Macrostrat's where it has one, else GLiM's), the bedrock code (the
        /// surface's when it is bedrock), whether the bedrock was borrowed from a nearby cell or GLiM under Macrostrat's cover,
        /// and which map gave the surface.</summary>
        public readonly struct Cell
        {
            public readonly string Surface, Rock, Source;
            public readonly bool Borrowed;
            public Cell(string surface, string rock, string source, bool borrowed) { Surface = surface; Rock = rock; Source = source; Borrowed = borrowed; }
            public bool Mapped => Surface != null || Rock != null;
        }

        readonly Dictionary<(int, int), (GroundTile Tile, GroundTile.Field Litho, GroundTile.Field Macro, GroundTile.Field Source, GroundTile.Field Bedrock)> tiles = new();

        /// <summary>The set's name (its file), part of the catalogue's fingerprint.</summary>
        public string Name { get; }

        public RockMap(string name, IEnumerable<GroundTile> set)
        {
            Name = name;
            foreach (var t in set)
            {
                if (!(Math.Abs(t.Step - 1.0 / 120) < 1e-9) || t.Nx != 1200 || t.Ny != 1200 || Math.Abs(t.West % 10) > 1e-9 || Math.Abs(t.North % 10) > 1e-9)
                    throw new FormatException($"rock set tile {t.Name}: not a 10-degree tile of 30\" cells");
                GroundTile.Field F(string n) => t.Fields.FirstOrDefault(f => f.Name == n && f.Category);
                var litho = F(Litho) ?? throw new FormatException($"rock set tile {t.Name}: no {Litho} field");
                tiles[((int)Math.Round(t.West), (int)Math.Round(t.North))] = (t, litho, F(Macro), F(SourceField), F(Bedrock));
            }
            if (tiles.Count == 0) throw new FormatException("rock set holds no tiles");
        }

        public int Count => tiles.Count;

        /// <summary>The cell holding the point (no warp: the world sampler bends contacts before asking).</summary>
        public Cell At(double lat, double lon)
        {
            if (!(lat >= -90 && lat <= 90) || !double.IsFinite(lon)) return default;
            lon = ((lon + 180) % 360 + 360) % 360 - 180;
            int west = (int)Math.Floor(lon / 10) * 10, north = Math.Min(90, (int)Math.Floor(lat / 10) * 10 + 10);
            if (!tiles.TryGetValue((west, north), out var e)) return default;
            int i = Math.Clamp((int)Math.Floor((lon - west) * 120), 0, 1199), j = Math.Clamp((int)Math.Floor((north - lat) * 120), 0, 1199);
            string litho = e.Tile.CategoryAtCell(e.Litho, i, j);
            string macro = e.Macro != null ? e.Tile.CategoryAtCell(e.Macro, i, j) : null;
            string surface = macro ?? litho;
            string bed = e.Bedrock != null ? e.Tile.CategoryAtCell(e.Bedrock, i, j) : null;
            string source = macro != null ? (e.Source != null ? e.Tile.CategoryAtCell(e.Source, i, j) : null) ?? "macrostrat" : litho != null ? "glim" : null;
            return new Cell(surface, bed ?? surface, source, bed != null);
        }
    }
}
