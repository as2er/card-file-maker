using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace CardFileMaker;
public static class Exporter
{
    static string ResourceText(string name) => TemplateResources.ReadText(name);
    static void SetId(XElement root,string field,int id,string name) {
        var e=root.Element(field);if(e==null){e=new XElement(field);root.Add(e);}e.SetElementValue("id",id);e.SetElementValue("str",name);
    }
    static void SaveXml(XDocument xml,string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);xml.Save(path);}
    public static ExportResult Export(ExportRequest r,Action<string>? log=null)
    {
        var s=r.Settings;
        if(s.CardId<1||s.CardId>999999||s.CharaId<1||s.CharaId>999999)throw new ArgumentException("卡片和角色 ID 必须在 1～999999 之间。");
        if(string.IsNullOrWhiteSpace(s.CardName)||string.IsNullOrWhiteSpace(s.CharacterName)||s.CardName.Length>100||s.CharacterName.Length>100)throw new ArgumentException("请填写不超过 100 字的卡片和角色名称。");
        if(s.Attribute is not ("Fire" or "Aqua" or "Leaf"))throw new ArgumentException("属性无效。");
        if(s.BossLevel<1||s.BossLevel>999)throw new ArgumentException("Boss 等级必须在 1～999 之间。");
        if(!Regex.IsMatch(s.OptName,"^A[0-9]{3}$")||s.OptName=="A000")throw new ArgumentException("输出包名必须是 A001～A999。");
        string game=Catalog.ResolvePackage(r.GamePackagePath),source=Path.GetFullPath(r.SourceOptPath),parent=Path.GetFullPath(r.OutputParentPath),output=Path.Combine(parent,s.OptName);
        if(!Directory.Exists(source)||!Directory.Exists(parent))throw new DirectoryNotFoundException("源包和输出父目录必须存在。");
        PathSafety.RejectLinkedAncestors(game);PathSafety.RejectLinkedAncestors(source);PathSafety.RejectLinkedAncestors(parent);
        if(PathSafety.Within(output,source)||PathSafety.Within(output,game)||PathSafety.Within(source,output)||PathSafety.Within(game,output))throw new IOException("请导出到游戏目录和源包以外的独立文件夹。");
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("目标已存在，请换一个输出目录；程序不会覆盖现有包："+output);
        var files=PathSafety.Files(source).ToList();
        var directories=Directory.GetDirectories(source,"*",SearchOption.AllDirectories);
        var large=files.Where(x=>new FileInfo(x).Length>=100_000_000).ToList();
        if(large.Count>0&&!r.ConfirmLargeFiles)throw new LargeFileException(large);
        if(r.Songs.Count==0)throw new ArgumentException("请至少勾选一首关联歌曲。");
        var validSongs=Catalog.ReadSongs(source).ToDictionary(x=>Path.GetFullPath(x.XmlPath),StringComparer.OrdinalIgnoreCase);
        foreach(var song in r.Songs)if(!validSongs.TryGetValue(Path.GetFullPath(song.XmlPath),out var actual)||actual.Id!=song.Id)throw new InvalidDataException("歌曲不属于选定源包。");
        foreach(var spec in Assets.All){
            if(!r.Images.TryGetValue(spec.Key,out var im))throw new InvalidDataException("缺少裁剪结果："+spec.Label);
            im.Validate();if(im.Width!=spec.Width||im.Height!=spec.Height)throw new InvalidDataException(spec.Label+" 尺寸不正确。");
            if(!Enumerable.Range(0,im.Width*im.Height).Any(i=>im.Pixels[i*4+3]!=0))throw new InvalidDataException(spec.Label+" 完全透明，请重新裁剪。");
        }
        var battle=r.Images["battle"];
        for(int y=0;y<1024;y++)for(int x=768;x<1024;x++)if(battle.Pixels[(y*1024+x)*4+3]!=0)throw new InvalidDataException("战斗立绘右侧 256 像素必须透明。");
        log?.Invoke("重新查重并核验引用……");var catalog=Catalog.Scan(game,source,log);
        if(catalog.CardIds.Contains(s.CardId)||catalog.CharaIds.Contains(s.CharaId))throw new InvalidDataException("卡片或角色 ID 已占用，请重新分配。");
        foreach(var spec in Assets.All)if(catalog.AssetNames.Contains(spec.BundleName(s.CardId)))throw new InvalidDataException("资源名称已存在："+spec.BundleName(s.CardId));
        if(!catalog.SkillIds.Contains(100005)||!catalog.SkillIds.Contains(100046)||!catalog.VersionIds.Contains(1060))throw new InvalidDataException("游戏缺少模板引用的技能或 1.60 版本数据。");
        var rom=Catalog.ReadXml(Path.Combine(game,"mu3_Data","StreamingAssets","GameData","RomConfig.xml"));
        if(rom.Root?.Element("version")?.Element("major")?.Value!="1"||rom.Root?.Element("version")?.Element("minor")?.Value!="60")throw new InvalidDataException("当前版本仅支持 SDDT 1.60。");
        string manifestPath=Path.Combine(source,"assets","assets.bytes");
        if(!File.Exists(manifestPath))throw new FileNotFoundException("源包缺少 assets/assets.bytes。");
        var manifest=ManifestCodec.Read(File.ReadAllBytes(manifestPath));
        foreach(var e in manifest)if(!File.Exists(Path.Combine(source,"assets",e.Name)))throw new InvalidDataException("源包索引指向不存在的文件："+e.Name);
        var updated=ManifestCodec.Append(manifest,Assets.All.Select(x=>x.BundleName(s.CardId)));
        var built=new List<(AssetSpec Spec,string Cab,byte[] Bytes)>();
        foreach(var spec in Assets.All){
            string cab;do{cab="CAB-"+Guid.NewGuid().ToString("N");}while(!catalog.Cabs.Add(cab));
            log?.Invoke("生成资源："+spec.Label);byte[] b=BundleCodec.Build(spec,s.CardId,r.Images[spec.Key],cab);
            var check=BundleCodec.Inspect(b);
            if(check.Cab!=cab||check.BundleName!=spec.BundleName(s.CardId)||check.TextureName!=spec.TextureName(s.CardId)||!check.RgbaTopDown.SequenceEqual(r.Images[spec.Key].Pixels))throw new InvalidDataException("资源回读验证失败："+spec.Label);
            built.Add((spec,cab,b));
        }
        log?.Invoke("复制自制包到新目录……");
        Directory.CreateDirectory(output);
        foreach(var dir in directories)Directory.CreateDirectory(Path.Combine(output,Path.GetRelativePath(source,dir)));
        foreach(var file in files){
            string dst=Path.Combine(output,Path.GetRelativePath(source,file));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);File.Copy(file,dst,false);
        }
        string assets=Path.Combine(output,"assets");Directory.CreateDirectory(assets);
        foreach(var b in built)File.WriteAllBytes(Path.Combine(assets,b.Spec.BundleName(s.CardId)),b.Bytes);
        File.WriteAllBytes(Path.Combine(assets,"assets.bytes"),ManifestCodec.Write(updated));
        var card=XDocument.Parse(ResourceText("Card.xml"));var cr=card.Root!;
        cr.SetElementValue("dataName",$"card{s.CardId:D6}");SetId(cr,"Name",s.CardId,s.CardName);SetId(cr,"CharaID",s.CharaId,s.CharacterName);
        cr.SetElementValue("NickName",s.CardName);cr.SetElementValue("SortOrder",s.CardId);cr.SetElementValue("Attribute",s.Attribute);
        SetId(cr,"School",999,"プロモーション");SetId(cr,"CardRightsName",0,"-");cr.SetElementValue("CardNumberString","Custom Boss");cr.SetElementValue("IsTicketUsable",false);
        SaveXml(card,Path.Combine(output,"card",$"card{s.CardId:D6}","Card.xml"));
        var chara=XDocument.Parse(ResourceText("Chara.xml"));var ch=chara.Root!;
        ch.SetElementValue("dataName",$"chara{s.CharaId:D6}");SetId(ch,"Name",s.CharaId,s.CharacterName);SetId(ch,"Model",s.CharaId,s.CharacterName);SetId(ch,"GraphicCardData",s.CardId,s.CardName);ch.SetElementValue("SortOrder",s.CharaId);ch.SetElementValue("IsModelless",true);
        SaveXml(chara,Path.Combine(output,"chara",$"chara{s.CharaId:D6}","Chara.xml"));
        SaveXml(XDocument.Parse(ResourceText("DataConfig.xml")),Path.Combine(output,"DataConfig.xml"));
        foreach(var song in r.Songs){
            var p=Path.Combine(output,Path.GetRelativePath(source,song.XmlPath));var xml=Catalog.ReadXml(p);var root=xml.Root!;
            SetId(root,"BossCard",s.CardId,s.CardName);root.SetElementValue("BossLevel",s.BossLevel);root.SetElementValue("BossVoiceNo",1);
            var wave=root.Element("WaveAttribute");if(wave==null){wave=new XElement("WaveAttribute");root.Add(wave);}wave.ReplaceNodes(new XElement("AttributeType",s.Attribute));SaveXml(xml,p);
        }
        var warnings=catalog.Warnings.ToList();warnings.Add("已通过静态回读校验；选曲卡图、战斗显示和在线环境兼容性仍须实机验证。");
        var installed=Path.Combine(game,"option",s.OptName);
        if(Directory.Exists(installed))warnings.Add("游戏中已有同名 opt，安装前请核对并备份；不要盲目覆盖。");
        var report=new{
            工具="Card File Maker",生成时间=DateTimeOffset.Now,游戏版本="SDDT 1.60",状态="静态验证通过，待实机验证",
            卡片=s.CardId,角色=s.CharaId,卡片名称=s.CardName,角色名称=s.CharacterName,属性=s.Attribute,
            歌曲=r.Songs.Select(x=>new{ID=x.Id,名称=x.Title}),
            资源=built.Select(x=>new{文件=x.Spec.BundleName(s.CardId),CAB=x.Cab,纹理=x.Spec.TextureName(s.CardId),宽=x.Spec.Width,高=x.Spec.Height,Mipmap=x.Spec.Mips,格式="RGBA32"}),
            索引条目=updated.Count,说明=warnings
        };
        string reportPath=Path.Combine(output,"boss-card-report.json");if(File.Exists(reportPath))reportPath=Path.Combine(output,$"boss-card-report-{s.CardId}.json");
        File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}),new UTF8Encoding(false));
        foreach(var song in r.Songs){var x=Catalog.ReadXml(Path.Combine(output,Path.GetRelativePath(source,song.XmlPath)));if(x.Root?.Element("BossCard")?.Element("id")?.Value!=s.CardId.ToString())throw new InvalidDataException("歌曲引用回读失败。");}
        log?.Invoke("导出完成："+output);return new(output,reportPath,warnings);
    }
}
