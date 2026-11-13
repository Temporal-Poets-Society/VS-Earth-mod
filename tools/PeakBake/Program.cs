using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length is <1 or >2)throw new ArgumentException("Usage: PeakBake allCountries.txt|allCountries.zip [earthdata/peaks.json]");
string output=args.Length==2?args[1]:"earthdata/peaks.json";
using var input=File.OpenRead(args[0]);
using var zip=args[0].EndsWith(".zip",StringComparison.OrdinalIgnoreCase)?new ZipArchive(input,ZipArchiveMode.Read):null;
using var stream=zip==null?input:zip.GetEntry("allCountries.txt")?.Open()??throw new InvalidDataException("allCountries.txt missing from ZIP");
using var reader=new StreamReader(stream);
byte[] bytes=GeoNamesBake.Bake(reader,out var counts);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllBytes(output,bytes);
Console.WriteLine(JsonSerializer.Serialize(counts));
Console.WriteLine($"{output}: {bytes.Length} bytes SHA256 {Convert.ToHexString(SHA256.HashData(bytes))}");
