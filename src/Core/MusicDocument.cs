using System.Globalization;
using System.Xml;
using System.Xml.Linq;
namespace CardFileMaker;
public sealed record BossCardChoice(int Id,string Name,string Attribute,string Source,int CharaId=0,string CharaName="",string Group="当前项目",string CardPath="") { public override string ToString()=> $"{Id} · {Name}"; }
public enum MusicChartMode { Normal, Lunatic, ReMaster }
public sealed class MusicDocument
{
 public const string FixedReleaseDate="2021-03-31T00:00:00";
 public static readonly string[] BooleanFields=["IsLunatic","IsReMaster","EpReleaseFlag","PossessingFromTheBeginning","IsLockedAtTheBeginning","IsBonusTrack"];
 public XDocument Xml {get;}
 public string? SourcePath {get;set;}
 public string? SourceHash {get;set;}
 bool autoArtistId;
 public MusicDocument(XDocument xml)
 {
  if(xml.Root?.Name.LocalName!="MusicData")throw new InvalidDataException("根节点必须为 MusicData");
  Xml=new XDocument(xml);Set("ReleaseVersion",FixedReleaseDate);
  foreach(var field in BooleanFields)
  {
   var boolean=Xml.Root!.Element(field);
   if(boolean==null)Write(field,field=="PossessingFromTheBeginning"?"true":"false");
   else boolean.Value=NormalizeBoolean(boolean.Value);
  }
  autoArtistId=!PositiveId(Get("ArtistName/id"));
  Write("dataName",DataName());
  if(Get("IsReMaster")=="true")Write("IsLunatic","true");
  NormalizeInactiveConstants();
 }
 public static MusicDocument Load(string path) {var bytes=File.ReadAllBytes(path);using var stream=new MemoryStream(bytes,false);using var r=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});return new MusicDocument(XDocument.Load(r)){SourcePath=Path.GetFullPath(path),SourceHash=Hash(bytes)};}
 public static MusicDocument CreateNew() {using var s=TemplateResources.Open("Music.xml");return new MusicDocument(XDocument.Load(s));}
 public string Get(string path) {XElement? e=Xml.Root;foreach(var part in path.Split('/'))e=e?.Element(part);return e?.Value??"";}
 public MusicChartMode ChartMode=>Get("IsReMaster")=="true"?MusicChartMode.ReMaster:Get("IsLunatic")=="true"?MusicChartMode.Lunatic:MusicChartMode.Normal;
 public bool IsChartEnabled(int index)=>index>=5||index>=0&&(ChartMode==MusicChartMode.Normal?index<4:index==4);
 public void SetChartMode(MusicChartMode mode)
 {
  if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
  Write("IsLunatic",mode==MusicChartMode.Normal?"false":"true");Write("IsReMaster",mode==MusicChartMode.ReMaster?"true":"false");NormalizeInactiveConstants();
 }
 public void NormalizeInactiveConstants()
 {
  var charts=Xml.Root!.Element("FumenData")?.Elements("FumenData").ToList()??[];
  for(int i=0;i<Math.Min(5,charts.Count);i++)if(!IsChartEnabled(i)){
   charts[i].SetElementValue("FumenConstIntegerPart","0");charts[i].SetElementValue("FumenConstFractionalPart","0");
  }
 }
 public void Set(string path,string value)
 {
  if(path=="ReleaseVersion")value=FixedReleaseDate;
  else if(BooleanFields.Contains(path))value=NormalizeBoolean(value);
  else if(path=="dataName")value=DataName();
  Write(path,value);
  if(path=="Name/str")Write("NameForSort",value);
  if(path=="ArtistName/id")autoArtistId=!PositiveId(value);
  if(path=="Name/id")Write("dataName",DataName());
  if(path is "Name/id" or "ArtistName/str"){
   if((autoArtistId||!PositiveId(Get("ArtistName/id")))&&!string.IsNullOrWhiteSpace(Get("ArtistName/str"))){autoArtistId=true;var songId=Get("Name/id");Write("ArtistName/id",PositiveId(songId)?int.Parse(songId,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):"");}
  }
  if(path=="IsReMaster"&&value=="true")Write("IsLunatic","true");
  if(path=="IsLunatic"&&value=="false")Write("IsReMaster","false");
  if(path is "IsLunatic" or "IsReMaster")NormalizeInactiveConstants();
 }
 static bool PositiveId(string value)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var id)&&id>0;
 string DataName()=>PositiveId(Get("Name/id"))?"music"+int.Parse(Get("Name/id"),CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):"";
 void Write(string path,string value){var e=Xml.Root!;foreach(var part in path.Split('/')){var c=e.Element(part);if(c==null){c=new XElement(part);e.Add(c);}e=c;}e.Value=value;}
 public XDocument ExportXml()
 {
  var copy=new MusicDocument(Xml);
  var charts=copy.Xml.Root!.Element("FumenData")?.Elements("FumenData").ToList()??[];
  // 禁用栏的谱面名称仍留在编辑草稿，导出时按官方格式留空。
  for(int i=0;i<Math.Min(5,charts.Count);i++)if(!copy.IsChartEnabled(i)){
   var file=charts[i].Element("FumenFile");if(file==null){file=new XElement("FumenFile");charts[i].Add(file);}file.SetElementValue("path","");
  }
  return copy.Xml;
 }
 static string NormalizeBoolean(string value)=>value.Trim().ToLowerInvariant() switch {"1"=>"true","0"=>"false",var normalized=>normalized};
 public void SelectBoss(BossCardChoice choice,bool syncAttribute=true)
 {
  Set("BossCard/id",choice.Id.ToString(CultureInfo.InvariantCulture));Set("BossCard/str",choice.Name);
  SetWaveAttribute(choice.Attribute);
 }
 public void SetWaveAttribute(string attribute)
 {
  var waves=Xml.Root!.Elements("WaveAttribute").ToList();
  if(waves.Count==0)Set("WaveAttribute/AttributeType",attribute);
  else foreach(var wave in waves){var node=wave.Element("AttributeType");if(node==null)wave.Add(new XElement("AttributeType",attribute));else node.Value=attribute;}
 }
 public List<string> Validate() {
 var errors=new List<string>();if(!int.TryParse(Get("Name/id"),out var id)||id<=0)errors.Add("歌曲 ID 必须为正整数");if(string.IsNullOrWhiteSpace(Get("Name/str")))errors.Add("歌曲名称不能为空");if(!int.TryParse(Get("MusicSourceName/id"),out var source)||source<=0||string.IsNullOrWhiteSpace(Get("MusicSourceName/str")))errors.Add("请填写有效音源引用 ID 和名称");
 var charts=Xml.Root!.Element("FumenData")?.Elements("FumenData").ToList()??new();if(!charts.Where((x,i)=>IsChartEnabled(i)).Any(x=>!string.IsNullOrWhiteSpace(x.Element("FumenFile")?.Element("path")?.Value)))errors.Add("至少填写一个当前模式可用的谱面名称");foreach(var chart in charts){if(!int.TryParse(chart.Element("FumenConstIntegerPart")?.Value,out var whole)||whole<0||whole>20||!int.TryParse(chart.Element("FumenConstFractionalPart")?.Value,out var fraction)||fraction<0||fraction>90||fraction%10!=0)errors.Add("谱面定数范围：整数 0–20，小数仅允许 0、10、20、30、40、50、60、70、80、90");var path=chart.Element("FumenFile")?.Element("path")?.Value;if(!string.IsNullOrWhiteSpace(path)&&(Path.IsPathRooted(path)||path.Split('/', '\\').Any(x=>x=="..")))errors.Add("谱面名称必须为无上级跳转的相对路径");}
 foreach(var name in new[]{"ArtistName","Genre","FirstChapterName","VersionID","StageID"})if(!int.TryParse(Get(name+"/id"),out var reference)||reference<=0||string.IsNullOrWhiteSpace(Get(name+"/str")))errors.Add(name+" 必须填写已知实际引用的正整数 ID 与名称");
 if(!int.TryParse(Get("BossCard/id"),out var boss)||boss<=0)errors.Add("Boss 卡片 ID 必须为正整数");
 if(!int.TryParse(Get("BossLevel"),out var level)||level<1||level>999)errors.Add("Boss 等级范围为 1–999");
 foreach(var wave in Xml.Root!.Elements("WaveAttribute"))if(!new[]{"Fire","Aqua","Leaf"}.Contains(wave.Element("AttributeType")?.Value))errors.Add("波次属性必须为 Fire、Aqua 或 Leaf");
 if(!Xml.Root.Elements("WaveAttribute").Any())errors.Add("至少需要一个波次属性");
 foreach(var name in new[]{"CostToUnlock","FinishBonus","SortOrder","BossLockHpCoef","BossVoiceNo"})if(!int.TryParse(Get(name),out var value)||value<0)errors.Add(name+" 必须为非负整数");
 foreach(var name in BooleanFields){var e=Xml.Root.Element(name);if(e!=null&&e.Value!="true"&&e.Value!="false")errors.Add(name+" 必须为 true 或 false");}
 if(Xml.Root.Element("MusicRightsName") is { } rights&&(!int.TryParse(rights.Element("id")?.Value,out var rightsId)||rightsId<0))errors.Add("版权 ID 必须为非负整数（无版权引用填 0）");
 if(Get("IsReMaster")=="true"&&Get("IsLunatic")!="true")errors.Add("Re:MASTER 必须使用官方双 true 标记");
 if(Get("ReleaseVersion")!=FixedReleaseDate)errors.Add("发布日期必须为 "+FixedReleaseDate);
 foreach(var reward in Xml.Descendants("BossLockReward")){if(!int.TryParse(reward.Element("RewardCount")?.Value,out var count)||count<=0)errors.Add("Boss 奖励数量必须为正整数");if(!int.TryParse(reward.Element("RewardName")?.Element("id")?.Value,out var rewardId)||rewardId<=0)errors.Add("Boss 奖励引用 ID 必须为正整数");}
 return errors;
 }
 public string TargetPath(string directory) {if(string.IsNullOrWhiteSpace(directory))throw new InvalidDataException("请选择保存文件夹");var target=Path.Combine(Path.GetFullPath(directory),"Music.xml");CheckTarget(target);return target;}
 static string Hash(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
 void CheckTarget(string target){
  var errors=Validate();if(errors.Count>0)throw new InvalidDataException(string.Join(Environment.NewLine,errors));
  PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(target)!);
  if(Directory.Exists(target)||File.Exists(target)&&File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))throw new IOException("目标 XML 不能为目录或文件链接："+target);
 }
 public PreparedMusicSave PrepareSave(string target,string? backupRoot=null){
  target=Path.GetFullPath(target);CheckTarget(target);if(File.Exists(target)&&new FileInfo(target).Length>=100_000_000)throw new IOException("原 XML 达到 100 MB，备份前需单独确认，已停止保存。");string? before=AutoSaveStore.FileHash(target);
  if(SourcePath!=null&&target.Equals(Path.GetFullPath(SourcePath),StringComparison.OrdinalIgnoreCase)&&(SourceHash==null||before!=SourceHash))throw new IOException("原 XML 自导入后已变化或缺少校验信息，请重新导入，或保存到其它文件夹。");
  backupRoot=Path.GetFullPath(backupRoot??Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"music-backups"));PathSafety.RejectLinkedAncestors(backupRoot);
  string backup=Path.Combine(backupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
  using var buffer=new MemoryStream();ExportXml().Save(buffer);var bytes=buffer.ToArray();if(bytes.Length>=100_000_000)throw new IOException("XML 达到 100 MB，已停止保存。");
  return new(target,bytes,before,backupRoot,backup,target+".pending-"+Guid.NewGuid().ToString("N"));
 }
 public void CommitSave(PreparedMusicSave plan,bool allowOverwrite=false){
  CheckTarget(plan.TargetPath);PathSafety.RejectLinkedAncestors(plan.BackupRoot);
  if(plan.OriginalHash!=null&&!allowOverwrite)throw new IOException("覆盖尚未确认。");
  if(AutoSaveStore.FileHash(plan.TargetPath)!=plan.OriginalHash)throw new IOException("保存预览后 XML 已变化，请重新导入或重新保存。");
  if(Path.GetDirectoryName(plan.StagingPath)!=Path.GetDirectoryName(plan.TargetPath)||File.Exists(plan.StagingPath)||Directory.Exists(plan.StagingPath))throw new IOException("保存临时路径无效。");
  if(plan.OriginalHash!=null){
   if(!PathSafety.Within(plan.BackupDirectory,plan.BackupRoot)||Directory.Exists(plan.BackupDirectory)||File.Exists(plan.BackupDirectory))throw new IOException("备份路径无效。");
   PathSafety.RejectLinkedAncestors(plan.BackupDirectory);Directory.CreateDirectory(plan.BackupDirectory);
   string backup=Path.Combine(plan.BackupDirectory,"Music.xml");File.Copy(plan.TargetPath,backup,false);
   if(AutoSaveStore.FileHash(backup)!=plan.OriginalHash)throw new IOException("备份时原 XML 已变化，已停止覆盖。");
   File.WriteAllBytes(Path.Combine(plan.BackupDirectory,"backup-info.json"),System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{CreatedUtc=DateTime.UtcNow,OriginalPath=plan.TargetPath,BackupPath=backup,Sha256=plan.OriginalHash}));
  }
  Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath)!);
  using(var stream=new FileStream(plan.StagingPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(plan.Bytes);stream.Flush(true);}
  if(AutoSaveStore.FileHash(plan.TargetPath)!=plan.OriginalHash)throw new IOException("写入时原 XML 已变化，保留待存文件："+plan.StagingPath);
  if(plan.OriginalHash!=null)File.Replace(plan.StagingPath,plan.TargetPath,null);else File.Move(plan.StagingPath,plan.TargetPath);
  if(AutoSaveStore.FileHash(plan.TargetPath)!=Hash(plan.Bytes))throw new IOException("保存后校验失败。");
  SourcePath=plan.TargetPath;SourceHash=Hash(plan.Bytes);
 }
 public void SaveAs(string directory,bool overwrite=false)=>CommitSave(PrepareSave(TargetPath(directory)),overwrite);
}
public sealed record PreparedMusicSave(string TargetPath,byte[] Bytes,string? OriginalHash,string BackupRoot,string BackupDirectory,string StagingPath);




