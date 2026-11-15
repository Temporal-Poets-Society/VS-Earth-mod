using System;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// (EA-0089, the review's S-1 step 2 and T-R5) TerrainModel's facts kept per river reach, filled lazily by whichever
    /// thread asks first and kept for the whole model: whether a reach lies in the water mask's river water
    /// (TerrainModel.ReachInMaskRiver), its median solved speed (ReachSpeed) and its pond profile (PondProfileOf).
    /// <para>The one rule: a fact is a pure function of the reach and the model's data. Its computation may read nothing that
    /// differs by thread or session (FineElevation.BuiltInOnly, Glo90Decides, a tile's arrival; ER-01, T-2 of the 14 November
    /// review); where an answer would depend on such a switch the caller answers without storing (ReachInMaskRiver on the
    /// built-in heights). Threads that race compute the same value, so the last write is as good as the first.</para>
    /// <para>Held by reference: a MemberwiseClone of the model (World Map's snapshot) shares these tables on purpose, which
    /// changes no answer since each is pure.</para>
    /// </summary>
    internal sealed class ReachFacts
    {
        byte[] inMask;
        float[] speed;
        PondProfile[] ponds;

        /// <summary>Per reach: 0 not looked at yet, 1 not in the mask's river water, 2 in it; made with count entries on the
        /// first ask.</summary>
        public byte[] InMask(int count)
        {
            var a = inMask;
            if (a == null)
            {
                Interlocked.CompareExchange(ref inMask, new byte[count], null);
                a = inMask;
            }
            return a;
        }

        /// <summary>Per reach: the median solved speed (m/s), NaN not sampled yet; made with count entries on the first ask.</summary>
        public float[] Speed(int count)
        {
            var a = speed;
            if (a == null)
            {
                var fresh = new float[Math.Max(0, count)];
                Array.Fill(fresh, float.NaN);
                Interlocked.CompareExchange(ref speed, fresh, null);
                a = speed;
            }
            return a;
        }

        /// <summary>Per reach: the pond profile (null not built yet); made with count entries on the first ask.</summary>
        public PondProfile[] Ponds(int count)
        {
            var a = ponds;
            if (a == null)
            {
                Interlocked.CompareExchange(ref ponds, new PondProfile[Math.Max(0, count)], null);
                a = ponds;
            }
            return a;
        }
    }
}
