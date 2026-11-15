using System; using System.IO; using ProtoBuf;
[ProtoContract] public class Newer { [ProtoMember(200)] public int GenFixRules; [ProtoMember(207)] public int NewRules; [ProtoMember(206)] public string LastSavedWith; }
[ProtoContract] public class Older { [ProtoMember(200)] public int GenFixRules; [ProtoMember(206)] public string LastSavedWith; }
static class P {
  static byte[] Ser<T>(T v) { var m = new MemoryStream(); Serializer.Serialize(m, v); return m.ToArray(); }
  static T De<T>(byte[] b) => Serializer.Deserialize<T>(new MemoryStream(b));
  static void Main() {
    var saved = Ser(new Newer { GenFixRules = 4, NewRules = 1, LastSavedWith = "2.1.0" });
    var old = De<Older>(saved);            // the older build reads the newer world
    old.LastSavedWith = "2.0.0";           // and records itself, as InitWorld does
    var rewritten = Ser(old);
    var back = De<Newer>(rewritten);       // the newer build opens it again
    Console.WriteLine($"saved {saved.Length} bytes, rewritten {rewritten.Length} bytes; NewRules after the round trip through the older build: {back.NewRules} (was 1); GenFixRules {back.GenFixRules}");
  }
}
