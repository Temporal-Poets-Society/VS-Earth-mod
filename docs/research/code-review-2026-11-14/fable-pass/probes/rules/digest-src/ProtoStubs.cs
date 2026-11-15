// Only the attribute declarations are stubbed. Actual EarthParams/config/model code compiles offline.
// Serialization wire compatibility still requires the coordinator's game-assembly build.
namespace ProtoBuf
{
    [System.AttributeUsage(System.AttributeTargets.Class|System.AttributeTargets.Struct)]
    public sealed class ProtoContractAttribute:System.Attribute { }
    [System.AttributeUsage(System.AttributeTargets.Field|System.AttributeTargets.Property)]
    public sealed class ProtoMemberAttribute:System.Attribute
    {public int Tag {get;} public ProtoMemberAttribute(int tag){Tag=tag;}}
}
