using System.IO.Compression;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

static class SourceBaselineAudit
{
    public static void Run(string archive,string root,string output)
    {
        using var zip=ZipFile.OpenRead(archive);
        using var stream=zip.GetEntry("EarthMap.pdb").Open();
        using var buffer=new MemoryStream();stream.CopyTo(buffer);buffer.Position=0;
        using var provider=MetadataReaderProvider.FromPortablePdbStream(buffer);
        var reader=provider.GetMetadataReader();
        var files=new List<object>();var covered=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matching=0,mismatched=0,missing=0;
        foreach(var handle in reader.Documents)
        {
            var doc=reader.GetDocument(handle);
            string original=reader.GetString(doc.Name),normalized=original.Replace('\\','/');
            int src=normalized.LastIndexOf("/src/",StringComparison.OrdinalIgnoreCase);
            if(src<0)continue;
            string relative=normalized[(src+1)..];covered.Add(relative);
            string path=Path.Combine(root,relative);
            string expected=Convert.ToHexString(reader.GetBlobBytes(doc.Hash)).ToLowerInvariant();
            string algorithm=reader.GetGuid(doc.HashAlgorithm).ToString();
            string actual=null;
            if(File.Exists(path))
            {
                byte[] bytes=File.ReadAllBytes(path);
                actual=Convert.ToHexString(algorithm=="8829d00f-11b8-4213-878b-770e8597ac16"?SHA256.HashData(bytes):
                    algorithm=="ff1816ec-aa5e-4d10-87f7-6f4963833460"?SHA1.HashData(bytes):throw new Exception("Unknown PDB document hash")).ToLowerInvariant();
                if(actual==expected)matching++;else mismatched++;
            }
            else missing++;
            files.Add(new{relative,original,algorithm,expected,actual,matches=actual==expected});
        }
        var extras=Directory.GetFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories)
            .Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).Where(p=>!covered.Contains(p)).ToArray();
        var result=new{archive,matching,mismatched,missing,extraSourceFiles=extras,files};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PDB source audit: {matching} match, {mismatched} differ, {missing} missing, {extras.Length} extra. {output}");
        foreach(var row in files)if(JsonSerializer.Serialize(row).Contains("\"matches\":false"))Console.WriteLine(JsonSerializer.Serialize(row));
    }
}
