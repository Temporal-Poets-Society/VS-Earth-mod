using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>
    /// The frame of /earth map's picture of the whole Earth (EarthCommands.RenderWorldMap, EA-0088): one ring wide, pole to
    /// pole within the world's height, <see cref="BlocksPerPixel"/> blocks a pixel; and the markers drawn on it. Off the game
    /// API so tests/ServerToolsTests can check it.
    /// </summary>
    public readonly struct WorldMapFrame
    {
        public readonly long Width, Height;
        /// <summary>The z of the picture's top row (the north pole, or the world's edge).</summary>
        public readonly double Z0;
        public readonly int BlocksPerPixel;
        readonly double ringStart, ringLength, z1;

        WorldMapFrame(EarthGeometry g, double z0, double z1, int bpp)
        {
            ringStart = g.RingStart; ringLength = g.RingLength; Z0 = z0; this.z1 = z1; BlocksPerPixel = bpp;
            Width = (long)Math.Ceiling(g.RingLength / bpp);
            Height = (long)Math.Ceiling((z1 - z0) / bpp);
        }

        /// <param name="mapSizeZ">The world's size north to south (the picture never reaches past it).</param>
        public static WorldMapFrame For(EarthGeometry g, double mapSizeZ, int bpp)
            => new(g, Math.Max(0, g.EquatorZ - 90 * g.BlocksPerDegreeZ), Math.Min(mapSizeZ, g.EquatorZ + 90 * g.BlocksPerDegreeZ), bpp);

        /// <summary>The RGB bytes the picture holds while it is rendered.</summary>
        public long Bytes => MapColors.RenderBytes(Width, Height);

        /// <summary>The fewest blocks per pixel at which the picture holds no more than <paramref name="maxBytes"/>.</summary>
        public int BlocksPerPixelWithin(long maxBytes)
        {
            int bpp = Math.Max(BlocksPerPixel, (int)Math.Ceiling(BlocksPerPixel * Math.Sqrt(Bytes / (double)maxBytes)));
            while (Rescaled(bpp).Bytes > maxBytes) bpp++;
            return bpp;
        }

        WorldMapFrame Rescaled(int bpp) => new(new EarthGeometry { RingStart = ringStart, RingLength = ringLength }, Z0, z1, bpp);

        /// <summary>An x moved onto the ring (RingStart to RingStart + RingLength).</summary>
        public double Wrap(double x)
        {
            double pos = (x - ringStart) % ringLength;
            if (pos < 0) pos += ringLength;
            return ringStart + pos;
        }

        /// <summary>The pixel a block column falls on (x wrapped onto the ring; it may lie outside the picture north or south).</summary>
        public (int I, int J) Pixel(double x, double z)
            => ((int)Math.Round((Wrap(x) - ringStart) / BlocksPerPixel), (int)Math.Round((z - Z0) / BlocksPerPixel));

        /// <summary>Markers as 5 x 5 squares cut at the picture's edges: the spawn red, drawn last so it stays on top; the
        /// others (the story locations) yellow.</summary>
        public void DrawMarkers(byte[] rgb, IEnumerable<(double x, double z, bool spawn)> markers)
        {
            int w = (int)Width, h = (int)Height;
            foreach (var (mx, mz, spawn) in markers.OrderBy(m => m.spawn))
            {
                var (pi, pj) = Pixel(mx, mz);
                for (int j = Math.Max(0, pj - 2); j <= Math.Min(h - 1, pj + 2); j++)
                    for (int i = Math.Max(0, pi - 2); i <= Math.Min(w - 1, pi + 2); i++)
                    {
                        long k = 3L * ((long)j * w + i);
                        rgb[k] = 255; rgb[k + 1] = spawn ? (byte)40 : (byte)230; rgb[k + 2] = spawn ? (byte)40 : (byte)0;
                    }
            }
        }
    }
}
