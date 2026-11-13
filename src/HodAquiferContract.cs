using System.IO;
using ProtoBuf;

namespace EarthMap
{
    /// <summary>
    /// Hydrate or Diedrate's per-chunk moddata <c>aquiferData</c> (decision 0059), as Earth's own protobuf contract with
    /// HoD 2.5.8's field numbers: the chunk {1 data, 3 version}, the data {1 raw rating, 2 salty, 3 smoothed rating}.
    /// Earth references no HoD assembly and copies none of its code; it writes the data format, which HoD then reads
    /// (the author, 7 November: "data format is not code. We can do it.").
    /// </summary>
    [ProtoContract]
    public sealed class HodAquiferChunkData
    {
        [ProtoMember(1)] public HodAquiferData Data { get; set; }
        [ProtoMember(3)] public int Version { get; set; }
    }

    [ProtoContract]
    public sealed class HodAquiferData
    {
        [ProtoMember(1)] public int AquiferRatingRaw { get; set; }
        [ProtoMember(2)] public bool IsSalty { get; set; }
        [ProtoMember(3)] public int? AquiferRatingSmoothed { get; set; }
    }

    public static class HodAquiferContract
    {
        public const string ModId = "hydrateordiedrate";
        /// <summary>Chunk moddata key HoD reads.</summary>
        public const string ModDataKey = "aquiferData";
        /// <summary>The data version Earth writes; HoD 2.5.8 regenerates a column whose data is missing or older.</summary>
        public const int Version = 2;
        /// <summary>HoD's types, read by reflection to check the contract at start-up.</summary>
        public const string ManagerType = "HydrateOrDiedrate.Wells.Aquifer.AquiferManager", VersionField = "CurrentAquiferDataVersion",
            ChunkDataType = "HydrateOrDiedrate.Wells.Aquifer.ModData.AquiferChunkData";

        /// <summary>
        /// The moddata bytes for one chunk: Earth's rating as both HoD's raw and smoothed rating, so HoD's neighbour
        /// smoothing leaves Earth's values as they are (decision 0059, open question 1).
        /// </summary>
        public static byte[] Bytes(int rating, bool salty)
        {
            var data = new HodAquiferChunkData
            {
                Data = new HodAquiferData { AquiferRatingRaw = rating, IsSalty = salty, AquiferRatingSmoothed = rating },
                Version = Version,
            };
            using var ms = new MemoryStream();
            ProtoBuf.Meta.RuntimeTypeModel.Default.Serialize(ms, data);
            return ms.ToArray();
        }

        public static HodAquiferChunkData Read(byte[] bytes)
        {
            if (bytes == null) return null;
            using var ms = new MemoryStream(bytes);
            return (HodAquiferChunkData)ProtoBuf.Meta.RuntimeTypeModel.Default.Deserialize(ms, null, typeof(HodAquiferChunkData));
        }
    }
}
