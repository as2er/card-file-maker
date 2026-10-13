using System.Xml;
using System.Xml.Linq;
namespace CardFileMaker;
public static class Catalog
{
    public static XDocument ReadXml(string path)
    {
        using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8_000_000});
        return XDocument.Load(reader,LoadOptions.PreserveWhitespace);
    }
    public static string ResolvePackage(string path)
    {
        path=Path.GetFullPath(path.Trim().Trim('"'));
        if(Directory.Exists(Path.Combine(path,"package","mu3_Data")))path=Path.Combine(path,"package");
        if(!Directory.Exists(Path.Combine(path,"mu3_Data","StreamingAssets","GameData")))throw new DirectoryNotFoundException("请选择含 package 的游戏目录，或 package 目录。");
        return path;
    }
    public static IReadOnlyList<SongChoice> ReadSongs(string sourceOpt)
    {
        string music=Path.Combine(sourceOpt,"music");if(!Directory.Exists(music))throw new DirectoryNotFoundException("自制包中没有 music 文件夹。");
        var songs=new List<SongChoice>();var seen=new HashSet<int>();
        foreach(var p in PathSafety.Files(music).Where(x=>Path.GetFileName(x).Equals("Music.xml",StringComparison.OrdinalIgnoreCase)).Order()) {
            var root=ReadXml(p).Root??throw new InvalidDataException("歌曲 XML 为空。");
            if(!int.TryParse(root.Element("Name")?.Element("id")?.Value,out int id))throw new InvalidDataException("歌曲缺少有效 ID："+p);
            if(!seen.Add(id))throw new InvalidDataException("自制包中歌曲 ID 重复："+id);
            songs.Add(new(p,id,root.Element("Name")?.Element("str")?.Value??id.ToString()));
        }return songs;
    }
    public static CatalogResult Scan(string gamePath,string sourceOpt,Action<string>? log=null,bool scanCabs=true,string? excludeRoot=null)
    {
        string package=ResolvePackage(gamePath);var result=new CatalogResult();
        var roots=Directory.EnumerateDirectories(Path.Combine(package,"mu3_Data","StreamingAssets","GameData"),"A???").ToList();
        string option=Path.Combine(package,"option");
        if(Directory.Exists(option))roots.AddRange(Directory.EnumerateDirectories(option,"A???"));
        if(!string.IsNullOrWhiteSpace(sourceOpt)){if(!Directory.Exists(sourceOpt))throw new DirectoryNotFoundException(sourceOpt);roots.Add(Path.GetFullPath(sourceOpt));}
        roots=roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(x=>excludeRoot==null||!Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(excludeRoot).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)).ToList();
        foreach(var root in roots) {
            foreach(var pair in new[]{("card","Card.xml",result.CardIds),("chara","Chara.xml",result.CharaIds),("skill","Skill.xml",result.SkillIds),("version","Version.xml",result.VersionIds)}) {
                string dir=Path.Combine(root,pair.Item1);if(!Directory.Exists(dir))continue;
                foreach(string p in PathSafety.Files(dir).Where(x=>Path.GetFileName(x).Equals(pair.Item2,StringComparison.OrdinalIgnoreCase))) {
                    var xml=ReadXml(p);if(!int.TryParse(xml.Root?.Element("Name")?.Element("id")?.Value,out int id))throw new InvalidDataException("数据 ID 无效："+p);
                    pair.Item3.Add(id);
                }
            }
        }
        log?.Invoke($"已读取 {result.CardIds.Count} 张卡、{result.CharaIds.Count} 个角色。");
        var assetRoots=roots.Select(x=>Path.Combine(x,"assets")).Prepend(Path.Combine(package,"mu3_Data","StreamingAssets","assets")).Distinct(StringComparer.OrdinalIgnoreCase);
        int n=0,duplicateCabs=0;
        byte[] magic=new byte[8];
        foreach(var ar in assetRoots.Where(Directory.Exists)) {
            foreach(var p in PathSafety.Files(ar)) {
                string name=Path.GetFileName(p);result.AssetNames.Add(name);
                if(!scanCabs || name.Equals("assets.bytes",StringComparison.OrdinalIgnoreCase))continue;
                using(var f=File.OpenRead(p)){if(f.Read(magic)!=8 || !magic.AsSpan().SequenceEqual("UnityFS\0"u8))continue;}
                try {foreach(string cab in BundleCodec.ReadCabNames(p))if(!result.Cabs.Add(cab))duplicateCabs++;}
                catch(Exception e){throw new InvalidDataException("无法核验资源 CAB："+p+"；"+e.Message,e);}
                if(++n%1000==0)log?.Invoke($"已扫描 {n:N0} 个资源包……");
            }
        }
        if(duplicateCabs>0)result.Warnings.Add($"现有资源中发现 {duplicateCabs} 次重复 CAB；将阻止新资源使用这些标识。");
        log?.Invoke($"查重完成：{result.Cabs.Count:N0} 个 CAB，{result.AssetNames.Count:N0} 个资源名称。");
        return result;
    }

    public static bool ContainsId(string opt,string folder,string filename,int id){
        string root=Path.Combine(opt,folder);if(!Directory.Exists(root))return false;
        return PathSafety.Files(root).Where(x=>Path.GetFileName(x).Equals(filename,StringComparison.OrdinalIgnoreCase)).Any(x=>ReadXml(x).Root?.Element("Name")?.Element("id")?.Value==id.ToString());
    }
    public static IReadOnlyList<BossCardChoice> ReadCards(string opt){
        string root=Path.Combine(opt,"card");var result=new List<BossCardChoice>();if(!Directory.Exists(root))return result;
        PathSafety.RejectLinkedAncestors(opt);
        foreach(var p in PathSafety.Files(root).Where(x=>Path.GetFileName(x).Equals("Card.xml",StringComparison.OrdinalIgnoreCase))){
            var x=ReadXml(p).Root!;if(!int.TryParse(x.Element("Name")?.Element("id")?.Value,out int id))throw new InvalidDataException("卡片 ID 无效："+p);
            result.Add(new(id,x.Element("Name")?.Element("str")?.Value??id.ToString(),x.Element("Attribute")?.Value??"Fire","自制包 "+Path.GetFileName(opt),int.TryParse(x.Element("CharaID")?.Element("id")?.Value,out var chara)?chara:0,x.Element("CharaID")?.Element("str")?.Value??"","自制 opt",p));
        }return result.OrderBy(x=>x.Id).ToList();
    }

    public static List<ManifestEntry> RegisterUiEntries(IReadOnlyList<ManifestEntry> original,IEnumerable<string> names)
    {
        var existing=original.Select(x=>x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added=names.Where(x=>x.StartsWith("ui_",StringComparison.OrdinalIgnoreCase)&&existing.Add(x))
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        return ManifestCodec.Append(original,added);
    }
    public static PreparedBatch PrepareUiRegistration(string gamePath,string optPath,Action<string>? log=null)
    {
        string game=ResolvePackage(gamePath),target=Path.GetFullPath(optPath).TrimEnd(Path.DirectorySeparatorChar);
        PathSafety.RejectLinkedAncestors(target);
        if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(target),"^A[0-9A-Z]{3}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("请选择具体的 AXXX opt 文件夹。");
        if(PathSafety.Within(target,game)&&!PathSafety.Within(target,Path.Combine(game,"option")))
            throw new IOException("禁止覆盖游戏基础资源索引，请选择 option 下的 opt。");
        string assets=Path.Combine(target,"assets"),index=Path.Combine(assets,"assets.bytes");
        if(!Directory.Exists(assets))throw new DirectoryNotFoundException("所选 opt 没有 assets 文件夹。");
        PathSafety.RejectLinkedAncestors(assets);
        if(File.Exists(index)&&File.GetAttributes(index).HasFlag(FileAttributes.ReparsePoint))throw new IOException("资源索引不能为链接。");
        byte[]? before=File.Exists(index)?File.ReadAllBytes(index):null;
        var original=before==null?new List<ManifestEntry>():ManifestCodec.Read(before);
        var names=new List<string>();var warnings=new List<string>();var cabFiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in Directory.EnumerateFiles(assets).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            string name=Path.GetFileName(path);if(!name.StartsWith("ui_",StringComparison.OrdinalIgnoreCase))continue;
            if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("UI 资源不能为文件链接："+path);
            var cabs=BundleCodec.ReadCabNames(path);
            if(cabs.Count==0)throw new InvalidDataException("UI 文件没有 CAB 标识："+path);
            foreach(string cab in cabs){if(cabFiles.TryGetValue(cab,out var other))warnings.Add("重复 CAB："+name+" 与 "+other);else cabFiles[cab]=name;}
            names.Add(name);
        }
        var merged=RegisterUiEntries(original,names);
        var bytes=ManifestCodec.Write(merged);
        if(bytes.Length>=100_000_000)throw new InvalidDataException("资源索引达到 100 MB，已停止。");
        var result=new PreparedBatch{OutputDirectory=target,BackupDirectory=Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"card-maker-backups","ui-index-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6])};
        result.Files.Add(new(index,bytes,before==null?null:Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before))));
        result.Warnings.Add($"检测到 {names.Count} 个 UI 资源；原索引 {original.Count} 条，新增 {merged.Count-original.Count} 条，保存后 {merged.Count} 条。");
        foreach(var added in merged.Skip(original.Count))result.Warnings.Add("新增："+added.Name);
        result.Warnings.AddRange(warnings);
        log?.Invoke(result.Warnings[0]);return result;
    }
}
