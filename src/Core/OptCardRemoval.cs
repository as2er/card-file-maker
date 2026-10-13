using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace CardFileMaker;

public sealed record OptCard(CardDraft Draft,string CardPath,string OptDirectory);
public sealed record RemovalFile(string Path,string Hash,byte[]? Replacement);
public sealed class PreparedCardRemoval
{
 public string GameDirectory {get;init;}="";
 public string OptDirectory {get;init;}="";
 public int[] CardIds {get;init;}=[];
 public string BackupRoot {get;init;}="";
 public string BackupDirectory {get;init;}="";
 public List<RemovalFile> Files {get;init;}=[];
 public Dictionary<string,string> Observed {get;init;}=new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>只操作所选包内的明确文件；索引记录不是删除路径的来源。</summary>
public static class OptCardRemoval
{
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
 static int Id(XElement root,string field)=>int.TryParse(root.Element(field)?.Element("id")?.Value,out int id)?id:0;
 static string Name(XElement root,string field)=>root.Element(field)?.Element("str")?.Value??"";
 static IEnumerable<string> DataFiles(string root,string category,string file){
  string dir=Path.Combine(root,category);if(!Directory.Exists(dir))return [];
  PathSafety.RejectLinkedAncestors(dir);
  return PathSafety.Files(dir).Where(x=>Path.GetFileName(x).Equals(file,StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
 }
 static string CheckOpt(string opt){
  opt=Path.TrimEndingDirectorySeparator(Path.GetFullPath(opt));
  if(!Regex.IsMatch(Path.GetFileName(opt),"^A[0-9A-Z]{3}$",RegexOptions.IgnoreCase)||!Directory.Exists(opt))throw new IOException("请选择存在的 AXXX 文件夹。");
  PathSafety.RejectLinkedAncestors(opt);return opt;
 }
 public static List<OptCard> Load(string opt){
  opt=CheckOpt(opt);var result=new List<OptCard>();
  foreach(var path in DataFiles(opt,"card","Card.xml")){
   var root=Catalog.ReadXml(path).Root??throw new InvalidDataException("卡片 XML 为空："+path);
   if(root.Name!="CardData"||Id(root,"Name")<=0)throw new InvalidDataException("卡片定义无效："+path);
   var draft=new CardDraft{CardId=Id(root,"Name"),CardName=Name(root,"Name"),CharaId=Id(root,"CharaID"),CharacterName=Name(root,"CharaID"),ReuseExistingChara=true,Attribute=root.Element("Attribute")?.Value??"Fire",Rarity=root.Element("Rarity")?.Value??"R",NickName=root.Element("NickName")?.Value??"",SchoolId=Id(root,"School"),SchoolName=Name(root,"School"),SkillId=Id(root,"SkillID"),SkillName=Name(root,"SkillID"),UpgradedSkillId=Id(root,"ChoKaikaSkillID"),UpgradedSkillName=Name(root,"ChoKaikaSkillID")};
   result.Add(new(draft,path,opt));
  }
  return result.OrderBy(x=>x.Draft.CardId).ToList();
 }
 public static PreparedCardRemoval Prepare(string game,string opt,IReadOnlyList<int> ids,string? backupRoot=null){
  opt=CheckOpt(opt);var selected=ids.ToHashSet();if(selected.Count==0||selected.Count!=ids.Count)throw new InvalidDataException("请选择包内卡片，且不能重复。");
  var local=Load(opt);if(selected.Any(id=>local.Count(x=>x.Draft.CardId==id)!=1))throw new InvalidDataException("卡片不存在或包内有重复 ID，请刷新并核对。");
  backupRoot=Path.GetFullPath(backupRoot??Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"card-maker-backups"));PathSafety.RejectLinkedAncestors(backupRoot);
  if(PathSafety.Within(backupRoot,opt))throw new IOException("备份必须保存在程序 save 中，不能放入所选包。");
  var plan=new PreparedCardRemoval{GameDirectory=game,OptDirectory=opt,CardIds=selected.Order().ToArray(),BackupRoot=backupRoot,BackupDirectory=Path.Combine(backupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"))};
  var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase){opt};
  if(!string.IsNullOrWhiteSpace(game)){
   string package=Catalog.ResolvePackage(game);PathSafety.RejectLinkedAncestors(package);
   if(PathSafety.Within(opt,package)&&!PathSafety.Within(opt,Path.Combine(package,"option")))throw new IOException("不能删除游戏基础数据包的卡片。");
   foreach(var parent in new[]{Path.Combine(package,"option"),Path.Combine(package,"mu3_Data","StreamingAssets","GameData")}){
    if(!Directory.Exists(parent))continue;PathSafety.RejectLinkedAncestors(parent);
    foreach(var dir in Directory.EnumerateDirectories(parent,"A???")){PathSafety.RejectLinkedAncestors(dir);roots.Add(Path.GetFullPath(dir));}
   }
  }
  byte[] Read(string p){
   PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(p)!);if(File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))throw new IOException("文件不能为链接："+p);
   var bytes=File.ReadAllBytes(p);plan.Observed[p]=Hash(bytes);return bytes;
  }
  XElement ReadXml(string p){var bytes=Read(p);using var stream=new MemoryStream(bytes,false);using var reader=System.Xml.XmlReader.Create(stream,new(){DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null});return XDocument.Load(reader).Root??throw new InvalidDataException("XML 为空："+p);}
  void Add(string p,byte[]? replacement=null){
   if(!PathSafety.Within(p,opt)||!File.Exists(p))throw new IOException("删除目标越界或不存在："+p);
   if(new FileInfo(p).Length>=100_000_000)throw new IOException("待备份文件达到 100 MB，已停止："+p);
   Read(p);if(!plan.Files.Any(x=>x.Path.Equals(p,StringComparison.OrdinalIgnoreCase)))plan.Files.Add(new(p,plan.Observed[p],replacement));
  }
  var allCards=new List<(string Path,int Id,int Chara)>();var allCharas=new List<(string Path,XElement Root)>();
  foreach(var root in roots){
   foreach(var path in DataFiles(root,"card","Card.xml")){var xml=ReadXml(path);if(xml.Name!="CardData"||Id(xml,"Name")<=0)throw new InvalidDataException("无法检查卡片引用："+path);allCards.Add((path,Id(xml,"Name"),Id(xml,"CharaID")));}
   foreach(var path in DataFiles(root,"music","Music.xml")){var xml=ReadXml(path);if(xml.Name!="MusicData")throw new InvalidDataException("无法检查歌曲引用："+path);if(selected.Contains(Id(xml,"BossCard")))throw new InvalidDataException("歌曲仍引用待删除卡片，请先更换 Boss："+path);}
   foreach(var path in DataFiles(root,"chara","Chara.xml"))allCharas.Add((path,ReadXml(path)));
  }
  var selectedPaths=local.Where(x=>selected.Contains(x.Draft.CardId)).Select(x=>x.CardPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
  foreach(var chara in allCharas.Where(x=>selected.Contains(Id(x.Root,"GraphicCardData")))){
   int charaId=Id(chara.Root,"Name");
   if(!PathSafety.Within(chara.Path,opt)||chara.Root.Element("IsModelless")?.Value!="true"||allCards.Any(x=>x.Chara==charaId&&!selectedPaths.Contains(x.Path)))throw new InvalidDataException("角色仍共用此卡图或不是所选包内的独占二维角色，不能删除："+chara.Path);
   Add(chara.Path);
  }
  var names=selected.SelectMany(id=>Assets.All.Select(s=>s.BundleName(id))).ToHashSet(StringComparer.OrdinalIgnoreCase);
  foreach(var path in selectedPaths)Add(path);
  foreach(var name in names){string path=Path.Combine(opt,"assets",name);if(File.Exists(path))Add(path);}
  string manifest=Path.Combine(opt,"assets","assets.bytes");
  if(File.Exists(manifest)){
   var entries=ManifestCodec.Read(Read(manifest));var removedIds=entries.Where(x=>names.Contains(x.Name)).Select(x=>x.Id).ToHashSet();
   var kept=entries.Where(x=>!names.Contains(x.Name)).ToList();if(kept.Any(x=>x.Dependencies.Any(removedIds.Contains)))throw new InvalidDataException("其它资源仍依赖此卡片资源，不能删除。");
   Add(manifest,ManifestCodec.Write(kept));
  }
  string index=Path.Combine(opt,"card-maker-index.json");
  if(File.Exists(index)){
   var json=JsonNode.Parse(Read(index)) as JsonObject??throw new InvalidDataException("工具记录损坏。");
   if(json["Cards"] is not JsonObject records)throw new InvalidDataException("工具记录缺少 Cards。");
   foreach(int id in selected)records.Remove(id.ToString());
   Add(index,JsonSerializer.SerializeToUtf8Bytes(json,new JsonSerializerOptions{WriteIndented=true}));
  }
  return plan;
 }
 public static void Commit(PreparedCardRemoval plan,bool confirmed=false){
  if(!confirmed)throw new IOException("删除尚未确认。");
  PreparedCardRemoval fresh;try{fresh=Prepare(plan.GameDirectory,plan.OptDirectory,plan.CardIds,plan.BackupRoot);}catch(Exception ex)when(ex is InvalidDataException or System.Xml.XmlException){throw new IOException("预览后引用变化，请刷新："+ex.Message,ex);}
  if(plan.Observed.Count!=fresh.Observed.Count||plan.Observed.Any(x=>!fresh.Observed.TryGetValue(x.Key,out var hash)||hash!=x.Value)||plan.Files.Count!=fresh.Files.Count||plan.Files.Any(x=>!fresh.Files.Any(f=>f.Path==x.Path&&f.Hash==x.Hash&&(f.Replacement==null?x.Replacement==null:x.Replacement!=null&&f.Replacement.SequenceEqual(x.Replacement)))))throw new IOException("预览后文件或引用发生变化，请刷新并重新确认。");
  if(!PathSafety.Within(plan.BackupDirectory,plan.BackupRoot)||Directory.Exists(plan.BackupDirectory)||File.Exists(plan.BackupDirectory))throw new IOException("备份目录已存在或越界。");
  PathSafety.RejectLinkedAncestors(plan.BackupDirectory);Directory.CreateDirectory(plan.BackupDirectory);
  var records=new List<object>();
  foreach(var file in plan.Files){
   string relative=Path.GetRelativePath(plan.OptDirectory,file.Path),backup=Path.Combine(plan.BackupDirectory,relative);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(file.Path,backup,false);
   if(HashFile(backup)!=file.Hash)throw new IOException("备份期间原文件变化，已停止删除："+file.Path);
   records.Add(new{OriginalPath=file.Path,BackupPath=backup,RelativePath=relative,Sha256=file.Hash,Action=file.Replacement==null?"Delete":"Replace"});
  }
  File.WriteAllBytes(Path.Combine(plan.BackupDirectory,"backup-info.json"),JsonSerializer.SerializeToUtf8Bytes(new{CreatedUtc=DateTime.UtcNow,OptDirectory=plan.OptDirectory,CardIds=plan.CardIds,Files=records},new JsonSerializerOptions{WriteIndented=true}));
  foreach(var file in plan.Files)if(AutoSaveStore.FileHash(file.Path)!=file.Hash)throw new IOException("删除前文件变化，已停止："+file.Path);
  try{
   foreach(var file in plan.Files.Where(x=>x.Replacement!=null)){File.WriteAllBytes(file.Path,file.Replacement!);if(HashFile(file.Path)!=Hash(file.Replacement!))throw new IOException("索引写入校验失败："+file.Path);}
   foreach(var file in plan.Files.Where(x=>x.Replacement==null)){if(HashFile(file.Path)!=file.Hash)throw new IOException("删除时文件变化："+file.Path);File.Delete(file.Path);}
  }catch(Exception ex){throw new IOException("操作中断；完整原文件备份位于："+plan.BackupDirectory+"。"+ex.Message,ex);}
 }
}
