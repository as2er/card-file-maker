namespace CardFileMaker;

public sealed record ChapterReference(string Kind,int Id,string Name,string Group,string Path,string Xml)
{
    public override string ToString()=>$"{Id} · {Name}  [{Group}]";
}

public sealed class ChapterCatalog
{
    public List<ChapterReference> Entries { get; }=[];
    public List<string> Errors { get; }=[];
    public string PackageDirectory { get; private set; }="";
    public string OptDirectory { get; private set; }="";
    public IReadOnlyList<ChapterReference> Get(string kind)=>Entries.Where(x=>x.Kind==kind).OrderBy(x=>x.Group=="自制 opt"?0:1).ThenBy(x=>x.Id).ToArray();
    public ChapterReference? Find(string kind,int id)=>Entries.LastOrDefault(x=>x.Kind==kind&&x.Id==id&&x.Group=="自制 opt")??Entries.LastOrDefault(x=>x.Kind==kind&&x.Id==id);
    public int NextId(string kind,IEnumerable<int>? reserved=null) {
        var used=Get(kind).Select(x=>x.Id).Concat(reserved??[]).ToHashSet();
        for(int id=910001;id<=999999;id++)if(!used.Contains(id))return id;
        throw new InvalidDataException("建议 ID 范围已用尽，请手动选择并查重。");
    }
    public static ChapterCatalog Load(string game,string opt) {
        var result=new ChapterCatalog { PackageDirectory=string.IsNullOrWhiteSpace(game)?"":Catalog.ResolvePackage(game), OptDirectory=string.IsNullOrWhiteSpace(opt)?"":Path.GetFullPath(opt) };
        var roots=new List<string>();
        if(result.PackageDirectory!="")foreach(var parent in new[]{Path.Combine(result.PackageDirectory,"mu3_Data","StreamingAssets","GameData"),Path.Combine(result.PackageDirectory,"option")}) {
            if(!Directory.Exists(parent))continue;
            PathSafety.RejectLinkedAncestors(parent);
            roots.AddRange(Directory.EnumerateDirectories(parent,"A???").OrderBy(x=>x,StringComparer.OrdinalIgnoreCase));
        }
        if(result.OptDirectory!="")roots.Add(result.OptDirectory);
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
            PathSafety.RejectLinkedAncestors(root);
            if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            string group=root.Equals(result.OptDirectory,StringComparison.OrdinalIgnoreCase)?"自制 opt":"官方 "+Path.GetFileName(root);
            foreach(string kind in new[]{"Story","Chapter","Music","Trophy","MemoryChapter","SecretMusicCondition","Movie","Card"}) {
                string directory=Path.Combine(root,kind.ToLowerInvariant());
                if(!Directory.Exists(directory))continue;
                foreach(string path in PathSafety.Files(directory).Where(x=>Path.GetFileName(x).Equals(kind+".xml",StringComparison.OrdinalIgnoreCase))) {
                    try {
                        var xml=Catalog.ReadXml(path);var element=xml.Root;
                        if(element?.Name!=kind+"Data"||!int.TryParse(element.Element("Name")?.Element("id")?.Value,out int id)||id<0||(id==0&&kind!="Story"))throw new InvalidDataException("根节点或 ID 无效");
                        result.Entries.Add(new(kind,ChapterXml.Id(element),ChapterXml.Name(element),group,path,xml.ToString()));
                    } catch(Exception e) when(e is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException) { result.Errors.Add(path+"："+e.Message); }
                }
            }
        }
        return result;
    }
}
