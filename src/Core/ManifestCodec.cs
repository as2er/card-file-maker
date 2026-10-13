using System.Text;
namespace CardFileMaker;
public static class ManifestCodec
{
    public static List<ManifestEntry> Read(byte[] bytes)
    {
        try {
            using var stream=new MemoryStream(bytes,false); using var r=new BinaryReader(stream,new UTF8Encoding(false,true));
            int count=r.ReadInt32(); if(count<0 || count>1_000_000) throw new InvalidDataException("资源索引条目数无效。");
            var entries=new List<ManifestEntry>(count);
            for(int i=0;i<count;i++) {
                int id=r.ReadInt32(); string name=r.ReadString(); int deps=r.ReadInt32();
                if(name.Length>512 || deps<0 || deps>count || (long)deps*4>stream.Length-stream.Position) throw new InvalidDataException("资源索引条目无效。");
                var d=new int[deps];for(int j=0;j<deps;j++)d[j]=r.ReadInt32();entries.Add(new(id,name,d));
            }
            if(stream.Position!=stream.Length)throw new InvalidDataException("资源索引含无法识别的尾部数据。");
            Validate(entries);return entries;
        } catch(Exception e) when(e is EndOfStreamException or DecoderFallbackException or FormatException or ArgumentException) { throw new InvalidDataException("资源索引损坏或截断。",e); }
    }
    static void Validate(IReadOnlyList<ManifestEntry> entries)
    {
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ids=entries.Select(x=>x.Id).ToHashSet();
        if(ids.Count!=entries.Count)throw new InvalidDataException("资源索引序号重复。");
        foreach(var e in entries) {
            if(e.Id<0 || e.Name.Length==0 || e.Name.IndexOfAny(['/', '\\', ':'])>=0 || e.Name is "." or ".." || !names.Add(e.Name))throw new InvalidDataException("资源索引名称无效或重复："+e.Name);
            if(e.Dependencies.Any(d=>!ids.Contains(d)))throw new InvalidDataException("资源依赖索引不存在："+e.Name);
        }
    }
    public static byte[] Write(IReadOnlyList<ManifestEntry> entries)
    {
        Validate(entries);using var ms=new MemoryStream();using(var w=new BinaryWriter(ms,Encoding.UTF8,true)) {
            w.Write(entries.Count);foreach(var e in entries){w.Write(e.Id);w.Write(e.Name);w.Write(e.Dependencies.Length);foreach(int d in e.Dependencies)w.Write(d);}
        }return ms.ToArray();
    }
    public static List<ManifestEntry> Append(IReadOnlyList<ManifestEntry> original,IEnumerable<string> names)
    {
        Validate(original);var result=original.ToList();int next=result.Count==0?0:checked(result.Max(x=>x.Id)+1);
        foreach(var name in names)result.Add(new(next++,name,[]));Validate(result);return result;
    }
}
