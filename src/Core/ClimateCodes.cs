using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The climate grid's six planes as climate.bin and its fixes store them (EA-0087, code review 14 November 2026 ER-13):
    /// the yearly mean, coldest and warmest month in °C x 100, the precipitation in mm, the rainfall 0..255 and the summer
    /// dryness x 255, 10 bytes a cell. They were kept as floats (24 bytes a cell), every version of the fix chain, on both
    /// sides: about 75 MB for a world with fix 4. A reader gets each value through <see cref="Temperature"/> and
    /// <see cref="Dryness"/>, tables of the very floats the float planes held (the same expression, c / 100f and c / 255f),
    /// so every sample is the same to the bit. Never changed once built; a plane a fix leaves as it was is the array of the
    /// version before.
    /// </summary>
    internal sealed class ClimateCodes
    {
        public short[] Mean, Cold, Warm;
        public ushort[] Precip;
        public byte[] Rain, Dry;

        public const int Planes = 6;

        static readonly float[] Hundredths = BuildHundredths(), DrynessOf = BuildDryness();

        static float[] BuildHundredths()
        {
            var t = new float[65536];
            for (int c = short.MinValue; c <= short.MaxValue; c++) t[c - short.MinValue] = (short)c / 100f;
            return t;
        }

        static float[] BuildDryness()
        {
            var t = new float[256];
            for (int c = 0; c < 256; c++) t[c] = (byte)c / 255f;
            return t;
        }

        /// <summary>A temperature code's °C, the float climate.bin's reader made of it.</summary>
        public static float Temperature(short c) => Hundredths[c - short.MinValue];
        /// <summary>A summer dryness code's 0..1, the float climate.bin's reader made of it.</summary>
        public static float Dryness(byte c) => DrynessOf[c];

        /// <summary>Planes of <paramref name="cells"/> cells; with <paramref name="mask"/> only the planes whose bit is set
        /// (a layered fix's), the others null.</summary>
        public ClimateCodes(int cells, int mask = 0x3F)
        {
            if ((mask & 1) != 0) Mean = new short[cells];
            if ((mask & 2) != 0) Cold = new short[cells];
            if ((mask & 4) != 0) Warm = new short[cells];
            if ((mask & 8) != 0) Precip = new ushort[cells];
            if ((mask & 16) != 0) Rain = new byte[cells];
            if ((mask & 32) != 0) Dry = new byte[cells];
        }

        ClimateCodes() { }

        public bool Has(int p) => p switch { 0 => Mean != null, 1 => Cold != null, 2 => Warm != null, 3 => Precip != null, 4 => Rain != null, _ => Dry != null };

        /// <summary>Plane p's stored code at cell i.</summary>
        public int Code(int p, int i) => p switch { 0 => Mean[i], 1 => Cold[i], 2 => Warm[i], 3 => Precip[i], 4 => Rain[i], _ => Dry[i] };

        /// <summary>Plane p's value at cell i as the float planes held it.</summary>
        public float Value(int p, int i) => p switch
        {
            0 => Temperature(Mean[i]),
            1 => Temperature(Cold[i]),
            2 => Temperature(Warm[i]),
            3 => Precip[i],
            4 => Rain[i],
            _ => Dryness(Dry[i]),
        };

        /// <summary>Sets plane p's code at cell i (only while building).</summary>
        public void Set(int p, int i, int code)
        {
            switch (p)
            {
                case 0: Mean[i] = (short)code; break;
                case 1: Cold[i] = (short)code; break;
                case 2: Warm[i] = (short)code; break;
                case 3: Precip[i] = (ushort)code; break;
                case 4: Rain[i] = (byte)code; break;
                default: Dry[i] = (byte)code; break;
            }
        }

        /// <summary>
        /// These planes with a fix applied: <paramref name="fix"/> holds, for the planes it has, the codes at the cells
        /// <paramref name="idx"/>. A plane the fix leaves code for code as it was stays this one's array; any other is a
        /// copy with the fix's codes.
        /// </summary>
        public ClimateCodes With(int[] idx, ClimateCodes fix)
        {
            var c = (ClimateCodes)MemberwiseClone();
            for (int p = 0; p < Planes; p++)
            {
                if (!fix.Has(p)) continue;
                bool same = true;
                for (int k = 0; k < idx.Length && same; k++) same = Code(p, idx[k]) == fix.Code(p, k);
                if (same) continue;
                switch (p)
                {
                    case 0: c.Mean = (short[])Mean.Clone(); break;
                    case 1: c.Cold = (short[])Cold.Clone(); break;
                    case 2: c.Warm = (short[])Warm.Clone(); break;
                    case 3: c.Precip = (ushort[])Precip.Clone(); break;
                    case 4: c.Rain = (byte[])Rain.Clone(); break;
                    default: c.Dry = (byte[])Dry.Clone(); break;
                }
                for (int k = 0; k < idx.Length; k++) c.Set(p, idx[k], fix.Code(p, k));
            }
            return c;
        }

        /// <summary>The bytes this set holds in arrays of its own or shared (each array once).</summary>
        public long Bytes => 2L * ((Mean?.Length ?? 0) + (Cold?.Length ?? 0) + (Warm?.Length ?? 0) + (Precip?.Length ?? 0)) + (Rain?.Length ?? 0) + (Dry?.Length ?? 0);
    }
}
