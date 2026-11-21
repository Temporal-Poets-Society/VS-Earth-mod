using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// earthdata/rivers-nonperennial-2.bin (tools/bake_nonperennial.py; decision 0069 rule 8): for each reach of Earth's
    /// course list (courses rules 4, by its key), how long GIRES v1.0 (Messager, Lehner et al. 2021, CC BY-SA 4.0) has its
    /// flow stopping: all year, 1-29 days or 30 days or more a year. Matched at bake time to the GIRES reach nearest in
    /// discharge and place among all of them; side arms take their river's class. A world with
    /// <see cref="EarthParams.PerennialRules"/> takes its arid rule's wet-source flow from it (<see cref="WetSourceFlow"/>):
    /// a reach whose flow stops 30 days a year or more has none, every other reach all of its flow, so the arid rule
    /// (AridHydrology.Dry, only in hot deserts) never dries a river that flows all year. Before, the wet share of a reach's
    /// flow was estimated from the rainfall of its catchment's cells, and a catchment as dry on vanilla's scale as the
    /// Zagros (70-85, the share starting at 100) left the Seymareh and the Karkheh 0.3 m³/s of their 190-2,500. About
    /// 20 kB, in the mod; version 1 (9.2 MB: every GIRES reach, matched when a world started) was never released.
    /// </summary>
    public sealed class NonPerennialData
    {
        /// <summary>The newest rules, which new worlds record (EarthParams.PerennialRules); 0: the rainfall estimate.</summary>
        public const int RulesVersion = 1;
        public const string FileName = "rivers-nonperennial-2.bin";
        /// <summary>Decision 0092: the same classes matched to courses rules 5's list (tools/bake_nonperennial.py on it).</summary>
        public const string FileName3 = "rivers-nonperennial-3.bin";
        public const string Magic = "EMNP";
        /// <summary>A reach's class: flowing all year (or no match), its flow stopping 1-29 days a year, 30 days or more.</summary>
        public const byte Perennial = 0, SomeDaysDry = 1, OftenDry = 2;

        /// <summary>The course list (DamRemovalData.KeyOf) the classes are for.</summary>
        public readonly ulong CourseKey;
        readonly byte[] classes;
        public int Count => classes.Length;

        NonPerennialData(ulong key, byte[] classes)
        {
            CourseKey = key;
            this.classes = classes;
        }

        public static NonPerennialData Read(byte[] file)
        {
            EarthBinary.Expect(file, Magic, 20, FileName);
            int version = BitConverter.ToInt32(file, 4);
            if (version != 2 && version != 3) throw new InvalidOperationException($"{FileName}: version {version}, this build reads 2 and 3");
            ulong key = BitConverter.ToUInt64(file, 8);
            int n = BitConverter.ToInt32(file, 16);
            if (n < 0) throw new InvalidOperationException($"{FileName}: {n} reaches");
            byte[] cls = EarthBinary.UnbrotliAll(file, 20, FileName, n);
            if (cls.Length != n) throw new InvalidOperationException($"{FileName}: {cls.Length} classes for {n} reaches");
            for (int i = 0; i < n; i++)
                if (cls[i] > OftenDry) throw new InvalidOperationException($"{FileName}: class {cls[i]} at reach {i}");
            return new NonPerennialData(key, cls);
        }

        /// <summary>A reach's class (Perennial beyond the list).</summary>
        public byte ClassOf(int reach) => reach >= 0 && reach < classes.Length ? classes[reach] : Perennial;

        /// <summary>
        /// The arid rule's wet-source flow (TerrainModel.WetSourceFlow) from the classes: none for a reach whose flow stops
        /// 30 days a year or more, all of its flow for every other reach. For the course list the file was made for only
        /// (the caller checks CourseKey).
        /// </summary>
        public double[] WetSourceFlow(IReadOnlyList<RiverReach> reaches)
        {
            var wet = new double[reaches.Count];
            for (int r = 0; r < wet.Length; r++) wet[r] = ClassOf(r) == OftenDry ? 0 : reaches[r].Q;
            return wet;
        }
    }
}
