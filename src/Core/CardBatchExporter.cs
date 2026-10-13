using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Text.RegularExpressions;
namespace CardFileMaker;
public static class CardBatchExporter
{
    sealed class Ownership{
        public Dictionary<string,OwnedCard> Cards{get;set;}=[];
    }
    sealed class OwnedCard{
        public string Key{get;set;}="";
        public int CharaId{get;set;}
        public Dictionary<string,string> Hashes{get;set;}=[];
    }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
    static XDocument Template(string name){
        using var stream=TemplateResources.Open(name);
        return XDocument.Load(stream);
    }
    static byte[] XmlBytes(XDocument xml){using var m=new MemoryStream();xml.Save(m);return m.ToArray();}
    static void SetId(XElement root,string field,int id,string name){var e=root.Element(field);if(e==null){e=new(field);root.Add(e);}e.SetElementValue("id",id);e.SetElementValue("str",name);}
    public static PreparedBatch Prepare(BatchExportRequest request,Action<string>? log=null)
    {
        if(request.Cards.Count==0)throw new ArgumentException("请先选择至少一张卡片。");
        string game=Catalog.ResolvePackage(request.GameDirectory),target=Path.GetFullPath(request.OptDirectory);
        if(!Regex.IsMatch(Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)),"^A[0-9A-Z]{3}$",RegexOptions.IgnoreCase))throw new ArgumentException("请选择具体的 AXXX opt 文件夹。");
        if(!Directory.Exists(target))throw new DirectoryNotFoundException("请先选择已有的 opt 输出文件夹。");
        PathSafety.RejectLinkedAncestors(game);PathSafety.RejectLinkedAncestors(target);
        if(PathSafety.Within(target,game)&&!PathSafety.Within(target,Path.Combine(game,"option")))throw new IOException("游戏内仅允许选择 option 下的 opt，不能写入游戏基础数据目录。");
        _=PathSafety.Files(target).ToArray();
        var cards=request.Cards;
        if(cards.Select(x=>x.Card.CardId).Distinct().Count()!=cards.Count||cards.Where(x=>!x.Card.ReuseExistingChara).Select(x=>x.Card.CharaId).Distinct().Count()!=cards.Count(x=>!x.Card.ReuseExistingChara))throw new InvalidDataException("本次导出的卡片 ID 或角色 ID 重复。");
        foreach(var item in cards){
            CardValidation.Validate(item.Card);
            foreach(var spec in Assets.All){
                if(!item.Images.TryGetValue(spec.Key,out var im))throw new InvalidDataException(item.Card.CardName+" 缺少 "+spec.Label);
                im.Validate();if(im.Width!=spec.Width||im.Height!=spec.Height)throw new InvalidDataException(spec.Label+" 尺寸不符。");
                bool visible=false;for(int i=3;i<im.Pixels.Length;i+=4)if(im.Pixels[i]!=0){visible=true;break;}
                if(!visible)throw new InvalidDataException(item.Card.CardName+" 的 "+spec.Label+" 完全透明。");
            }
            var b=item.Images["battle"];for(int y=0;y<1024;y++)for(int x=768;x<1024;x++)if(b.Pixels[(y*1024+x)*4+3]!=0)throw new InvalidDataException("战斗立绘右侧 256 像素必须透明。");
        }
        var rom=Catalog.ReadXml(Path.Combine(game,"mu3_Data","StreamingAssets","GameData","RomConfig.xml"));
        if(rom.Root?.Element("version")?.Element("major")?.Value!="1"||rom.Root?.Element("version")?.Element("minor")?.Value!="60")throw new InvalidDataException("当前仅支持 SDDT 1.60。");
        log?.Invoke("扫描游戏及所选 opt 的 ID / CAB……");
        var outside=Catalog.Scan(game,"",log,true,target);
        var local=Catalog.ReadCards(target);
        var references=GameCatalog.Load(game,target,null,false);
        var targetCharaIds=new HashSet<int>();
        string charaRoot=Path.Combine(target,"chara");
        if(Directory.Exists(charaRoot))foreach(var p in PathSafety.Files(charaRoot).Where(x=>Path.GetFileName(x).Equals("Chara.xml",StringComparison.OrdinalIgnoreCase)))targetCharaIds.Add(int.Parse(Catalog.ReadXml(p).Root!.Element("Name")!.Element("id")!.Value));
        string ownershipPath=Path.Combine(target,"card-maker-index.json");
        var ownership=File.Exists(ownershipPath)?JsonSerializer.Deserialize<Ownership>(File.ReadAllText(ownershipPath))??throw new InvalidDataException("工具记录损坏。"):new Ownership();
        string manifestPath=Path.Combine(target,"assets","assets.bytes");
        var entries=File.Exists(manifestPath)?ManifestCodec.Read(File.ReadAllBytes(manifestPath)):[];
        foreach(var e in entries)if(!File.Exists(Path.Combine(target,"assets",e.Name)))throw new InvalidDataException("原资源索引缺少文件："+e.Name);
        var existingAssets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var cabs=outside.Cabs;
        string assetRoot=Path.Combine(target,"assets");
        if(Directory.Exists(assetRoot))foreach(var file in PathSafety.Files(assetRoot)){
            existingAssets.Add(Path.GetFileName(file));using var stream=File.OpenRead(file);byte[] magic=new byte[8];if(stream.Read(magic)==8&&magic.AsSpan().SequenceEqual("UnityFS\0"u8))foreach(var cab in BundleCodec.ReadCabNames(file))cabs.Add(cab);
        }
        var result=new PreparedBatch{OutputDirectory=target,BackupDirectory=Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"card-maker-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]),Warnings=outside.Warnings.ToList()};
        void Add(string path,byte[] data){
            string full=Path.GetFullPath(Path.Combine(target,path));
            if(!PathSafety.Within(full,target))throw new IOException("导出路径越界。");
            if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("目标文件不能为链接："+full);
            if(data.Length>=100_000_000)throw new InvalidDataException("单个导出文件超过 100 MB，已停止。");
            result.Files.Add(new(full,data,File.Exists(full)?HashFile(full):null));
        }
        foreach(var item in cards){
            var c=item.Card;string cid=c.CardId.ToString();
            ownership.Cards.TryGetValue(cid,out var prior);
            bool owned=prior?.Key==c.Key;
            if(outside.CardIds.Contains(c.CardId)||(!c.ReuseExistingChara&&outside.CharaIds.Contains(c.CharaId)))throw new InvalidDataException($"卡片 {c.CardId} 或角色 {c.CharaId} 在其他游戏包中已存在。");
            if(local.Any(x=>x.Id==c.CardId)&&!owned)throw new InvalidDataException($"卡片 {c.CardId} 已存在于目标包且不属于当前项目。请使用新 ID。");
            if(!c.ReuseExistingChara&&targetCharaIds.Contains(c.CharaId)&&!(owned&&prior!.CharaId==c.CharaId))throw new InvalidDataException($"角色 {c.CharaId} 已存在于目标包。");
            if(c.ReuseExistingChara&&!outside.CharaIds.Contains(c.CharaId)&&!targetCharaIds.Contains(c.CharaId)&&!cards.Any(x=>!x.Card.ReuseExistingChara&&x.Card.CharaId==c.CharaId))
                throw new InvalidDataException("引用的角色不存在，请重新选择："+c.CharaId);
            foreach(var skill in new[]{c.SkillId,c.UpgradedSkillId})if(!outside.SkillIds.Contains(skill)&&!Catalog.ContainsId(target,"skill","Skill.xml",skill))throw new InvalidDataException("游戏中不存在技能 ID："+skill);
            if(!references.SkillPairs.TryGetValue(c.SkillId,out var pairedSkills)||!pairedSkills.Contains(c.UpgradedSkillId))throw new InvalidDataException("所选技能与超开花技能没有实际配对："+c.SkillId+" → "+c.UpgradedSkillId);
            if(!outside.VersionIds.Contains(1060))throw new InvalidDataException("游戏缺少版本 1060 数据。");
            if(owned)foreach(var h in prior!.Hashes){
                string p=Path.GetFullPath(Path.Combine(target,h.Key));
                if(!PathSafety.Within(p,target)||!File.Exists(p)||HashFile(p)!=h.Value)throw new IOException("上次导出的文件已被移动或由其他程序修改，请核对后以新 ID 导出："+p);
            }
            var ownedNow=new OwnedCard{Key=c.Key,CharaId=c.CharaId};
            void AddOwned(string p,byte[] data){
                if(File.Exists(Path.Combine(target,p))&&!(owned&&prior!.Hashes.ContainsKey(p)))throw new InvalidDataException("目标文件不属于当前项目，不能覆盖："+Path.Combine(target,p));
                Add(p,data);ownedNow.Hashes[p]=Hash(data);
            }
            foreach(var spec in Assets.All){
                string name=spec.BundleName(c.CardId),rel=Path.Combine("assets",name);
                if(outside.AssetNames.Contains(name)||(existingAssets.Contains(name)&&!(owned&&prior!.Hashes.ContainsKey(rel))))throw new InvalidDataException("资源名称冲突："+name);
                string cab;do{cab="CAB-"+Guid.NewGuid().ToString("N");}while(!cabs.Add(cab));
                log?.Invoke($"生成 {c.CardName} · {spec.Label}");var data=BundleCodec.Build(spec,c.CardId,item.Images[spec.Key],cab);var check=BundleCodec.Inspect(data);
                if(check.Cab!=cab||check.TextureName!=spec.TextureName(c.CardId)||!check.RgbaTopDown.SequenceEqual(item.Images[spec.Key].Pixels))throw new InvalidDataException("资源回读校验失败："+name);
                AddOwned(rel,data);
                if(!entries.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))entries=ManifestCodec.Append(entries,[name]);
            }
            var card=Template("Card.xml");var cr=card.Root!;cr.SetElementValue("dataName",$"card{c.CardId:D6}");SetId(cr,"Name",c.CardId,c.CardName);SetId(cr,"CharaID",c.CharaId,c.CharacterName);
            cr.SetElementValue("NickName",c.NickName);cr.SetElementValue("SortOrder",c.CardId);cr.SetElementValue("Attribute",c.Attribute);cr.SetElementValue("Rarity",c.Rarity);
            string RefName(string field,int id,string fallback)=>references.Get(field).LastOrDefault(x=>x.Id==id)?.Name??fallback;
            if(c.ReuseExistingChara)SetId(cr,"CharaID",c.CharaId,references.Characters.LastOrDefault(x=>x.Id==c.CharaId)?.Name??cards.First(x=>!x.Card.ReuseExistingChara&&x.Card.CharaId==c.CharaId).Card.CharacterName);
            SetId(cr,"School",c.SchoolId,RefName("School",c.SchoolId,c.SchoolName));
            SetId(cr,"SkillID",c.SkillId,RefName("SkillID",c.SkillId,c.SkillName));
            SetId(cr,"ChoKaikaSkillID",c.UpgradedSkillId,RefName("SkillID",c.UpgradedSkillId,c.UpgradedSkillName));
            SetId(cr,"CardRightsName",0,"-");cr.SetElementValue("CardNumberString","Custom Boss");cr.SetElementValue("IsTicketUsable",false);
            AddOwned(Path.Combine("card",$"card{c.CardId:D6}","Card.xml"),XmlBytes(card));
            if(!c.ReuseExistingChara){
            var chara=Template("Chara.xml");var ch=chara.Root!;ch.SetElementValue("dataName",$"chara{c.CharaId:D6}");SetId(ch,"Name",c.CharaId,c.CharacterName);SetId(ch,"Model",c.CharaId,c.CharacterName);SetId(ch,"GraphicCardData",c.CardId,c.CardName);ch.SetElementValue("SortOrder",c.CharaId);ch.SetElementValue("IsModelless",true);
            AddOwned(Path.Combine("chara",$"chara{c.CharaId:D6}","Chara.xml"),XmlBytes(chara));
            }
            ownership.Cards[cid]=ownedNow;
        }
        Add(Path.Combine("assets","assets.bytes"),ManifestCodec.Write(entries));
        string configPath=Path.Combine(target,"DataConfig.xml");
        var config=File.Exists(configPath)?Catalog.ReadXml(configPath):Template("DataConfig.xml");
        if(config.Root?.Name.LocalName!="DataConfig")throw new InvalidDataException("目标 DataConfig.xml 无效。");
        var v=config.Root.Element("version");if(v==null){v=new XElement("version");config.Root.Add(v);}v.SetElementValue("major",1);v.SetElementValue("minor",60);if(v.Element("release")==null)v.SetElementValue("release",0);
        Add("DataConfig.xml",XmlBytes(config));
        Add("card-maker-index.json",JsonSerializer.SerializeToUtf8Bytes(ownership,new JsonSerializerOptions{WriteIndented=true}));
        result.Warnings.Add("资源通过静态校验；游戏选曲和战斗显示仍需实机验证。");
        return result;
    }
    public static void Commit(PreparedBatch batch,bool allowOverwrite=false)
    {
        if(batch.Files.Count==0)throw new ArgumentException("没有可导出的文件。");
        PathSafety.RejectLinkedAncestors(batch.OutputDirectory);
        if(batch.Files.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=batch.Files.Count)throw new IOException("导出计划包含重复文件。");
        foreach(var f in batch.Files){
            if(!PathSafety.Within(f.Path,batch.OutputDirectory))throw new IOException("导出目标越界。");
            PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(f.Path)!);
            if(Directory.Exists(f.Path))throw new IOException("目标是目录："+f.Path);
            if(File.Exists(f.Path)&&(File.GetAttributes(f.Path)&FileAttributes.ReparsePoint)!=0)throw new IOException("目标不能是文件链接："+f.Path);
            if(f.OriginalHash==null&&File.Exists(f.Path))throw new IOException("预览后目标文件已创建，请重新导出："+f.Path);
            if(f.OriginalHash!=null&&(!allowOverwrite||!File.Exists(f.Path)||HashFile(f.Path)!=f.OriginalHash))throw new IOException("覆盖未确认或目标文件已变化："+f.Path);
        }
        // 所有已有目标先备份；生成记录保留，失败时便于恢复。
        string? backup=null;
        if(batch.Files.Any(x=>x.OriginalHash!=null)){
            backup=batch.BackupDirectory;
            if(string.IsNullOrWhiteSpace(backup)||!PathSafety.Within(backup,batch.BackupRoot)||Directory.Exists(backup)||File.Exists(backup))throw new IOException("备份目标无效或已存在。请重新生成导出计划。");
            PathSafety.RejectLinkedAncestors(batch.BackupRoot);PathSafety.RejectLinkedAncestors(backup);
            Directory.CreateDirectory(backup);
            var records=new List<object>();
            foreach(var f in batch.Files.Where(x=>x.OriginalHash!=null)){
                string relative=Path.GetRelativePath(batch.OutputDirectory,f.Path),p=Path.Combine(backup,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.Copy(f.Path,p,false);
                if(HashFile(p)!=f.OriginalHash)throw new IOException("备份期间原文件发生变化，已停止覆盖："+f.Path);
                records.Add(new{OriginalPath=f.Path,BackupPath=p,RelativePath=relative,Sha256=f.OriginalHash});
            }
            File.WriteAllBytes(Path.Combine(backup,"backup-info.json"),JsonSerializer.SerializeToUtf8Bytes(new{CreatedUtc=DateTime.UtcNow,OptDirectory=batch.OutputDirectory,Files=records},new JsonSerializerOptions{WriteIndented=true}));
        }
        try{
            foreach(var f in batch.Files){Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);using var stream=new FileStream(f.Path,f.OriginalHash==null?FileMode.CreateNew:FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(f.Bytes);stream.Flush(true);}
            foreach(var f in batch.Files)if(HashFile(f.Path)!=Hash(f.Bytes))throw new IOException("导出后校验失败："+f.Path);
        }catch(Exception e){throw new IOException("写入中断，可能已生成部分文件。"+(backup==null?"":"原有文件备份位置："+backup)+"；"+e.Message,e);}
    }
}
