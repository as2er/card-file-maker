using System.Xml;
using System.Xml.Linq;
using CardFileMaker;
public static class MusicTests
{
 public static void Run(Action<string,Action> check)
 {
  check("数据目录随乐曲ID即时生成且统一数值格式",()=>{
   var d=MusicDocument.CreateNew();d.Set("Name/id","009123");Assert(d.Get("dataName")=="music9123","没有由ID生成目录名");
   d.Set("Name/id","9124");Assert(d.Get("dataName")=="music9124","修改ID未更新目录名");
   d.Set("Name/id","");Assert(d.Get("dataName")=="","清空ID未清空目录名");
  });
  check("Bonus标记独立于难度与解锁状态",()=>{
   var d=ValidMusic();var before=new XDocument(d.Xml);d.Set("IsBonusTrack","true");before.Root!.Element("IsBonusTrack")!.Value="true";
   Assert(XNode.DeepEquals(before,d.Xml),"Bonus连带改变其它字段");
  });
  check("三种难度模式使用官方标记并归零禁用定数",()=>{
   var d=ValidMusic();var charts=d.Xml.Root!.Element("FumenData")!.Elements("FumenData").ToList();
   foreach(var chart in charts){chart.SetElementValue("FumenConstIntegerPart","13");chart.SetElementValue("FumenConstFractionalPart","70");}
   d.SetChartMode(MusicChartMode.Lunatic);
   Assert(d.Get("IsLunatic")=="true"&&d.Get("IsReMaster")=="false","LUNATIC标记错误");
   Assert(Enumerable.Range(0,5).Where(d.IsChartEnabled).SequenceEqual(new[]{4}),"LUNATIC可编辑栏错误");
   Assert(charts.Take(4).All(x=>x.Element("FumenConstIntegerPart")!.Value=="0"&&x.Element("FumenConstFractionalPart")!.Value=="0"),"前四栏没有归零");
   Assert(charts[4].Element("FumenConstIntegerPart")!.Value=="13","清掉了启用栏");
   d.SetChartMode(MusicChartMode.ReMaster);
   Assert(d.Get("IsLunatic")=="true"&&d.Get("IsReMaster")=="true"&&d.ChartMode==MusicChartMode.ReMaster,"ReMASTER没有官方双true");
   Assert(Enumerable.Range(0,5).Where(d.IsChartEnabled).SequenceEqual(new[]{4}),"ReMASTER可编辑栏错误");
   d.SetChartMode(MusicChartMode.Normal);
   Assert(d.Get("IsLunatic")=="false"&&d.Get("IsReMaster")=="false"&&Enumerable.Range(0,5).Where(d.IsChartEnabled).SequenceEqual(new[]{0,1,2,3}),"普通模式错误");
   Assert(charts[4].Element("FumenConstIntegerPart")!.Value=="0"&&charts[4].Element("FumenConstFractionalPart")!.Value=="0","第五栏没有归零");
  });
  check("保存只输出启用谱面且不删除草稿名称",()=>{
   var d=ValidMusic();var charts=d.Xml.Root!.Element("FumenData")!.Elements("FumenData").ToList();
   for(int i=0;i<5;i++)charts[i].Element("FumenFile")!.Element("path")!.Value=$"charts/9001_{i}.ogkr";
   foreach(var mode in new[]{MusicChartMode.Normal,MusicChartMode.Lunatic,MusicChartMode.ReMaster}){
    d.SetChartMode(mode);var xml=d.ExportXml();var exported=xml.Root!.Element("FumenData")!.Elements("FumenData").ToList();
    for(int i=0;i<5;i++){Assert(exported[i].Element("FumenFile")!.Element("path")!.Value==(d.IsChartEnabled(i)?$"charts/9001_{i}.ogkr":""),"导出包含禁用路径");Assert(charts[i].Element("FumenFile")!.Element("path")!.Value==$"charts/9001_{i}.ogkr","草稿谱面名丢失");}
   }
  });
  check("当前模式没有谱面时拒绝借用禁用栏通过校验",()=>{
   var d=ValidMusic();d.SetChartMode(MusicChartMode.Lunatic);
   Assert(d.Validate().Any(x=>x.Contains("当前模式")),"仅前四栏有路径仍通过LUNATIC校验");
   d.Xml.Root!.Element("FumenData")!.Elements("FumenData").ElementAt(4).Element("FumenFile")!.Element("path")!.Value="9001_10.ogkr";
   Assert(d.Validate().Count==0,string.Join(";",d.Validate()));
  });
  check("艺术家名称直接输入且兼容先名称后ID与导入编号",()=>{
   var d=MusicDocument.CreateNew();d.Set("ArtistName/str","A · B & C");foreach(var partial in new[]{"9","98","987","9876"})d.Set("Name/id",partial);
   Assert(d.Get("ArtistName/str")=="A · B & C"&&d.Get("ArtistName/id")=="9876","先填名称后填ID失败");
   var next=MusicDocument.CreateNew();next.Set("Name/id","9877");next.Set("ArtistName/str","123");
   Assert(next.Get("ArtistName/str")=="123"&&next.Get("ArtistName/id")=="9877","纯数字名称被当作ID");
   next.Set("ArtistName/id","33");next.Set("ArtistName/str","修改导入名");
   Assert(next.Get("ArtistName/id")=="33","改名字重置导入艺术家ID");
  });
  check("缺失LUNATIC标记的ReMASTER导入不丢第五定数",()=>{
   var xml=MusicDocument.CreateNew().Xml;xml.Root!.Element("IsLunatic")!.Remove();xml.Root.Element("IsReMaster")!.Value="1";
   var fifth=xml.Root.Element("FumenData")!.Elements("FumenData").ElementAt(4);fifth.SetElementValue("FumenConstIntegerPart","14");fifth.SetElementValue("FumenConstFractionalPart","70");fifth.Element("FumenFile")!.Element("path")!.Value="test_10.ogkr";
   var d=new MusicDocument(xml);var restored=d.Xml.Root!.Element("FumenData")!.Elements("FumenData").ElementAt(4);
   Assert(d.ChartMode==MusicChartMode.ReMaster&&d.Get("IsLunatic")=="true"&&d.Get("IsReMaster")=="true","缺失标记导致模式丢失");
   Assert(restored.Element("FumenConstIntegerPart")!.Value=="14"&&restored.Element("FumenConstFractionalPart")!.Value=="70","补默认标记清零启用栏");
  });
  check("空白音乐必须填写资源",()=>Assert(MusicDocument.CreateNew().Validate().Count>0,"漏校验"));
  check("新音乐默认版本日期和布尔值",()=>
  {
   var d=MusicDocument.CreateNew();
   Assert(d.Get("VersionID/id")=="1045"&&d.Get("VersionID/str")=="ver_1_45","版本默认值错误");
   Assert(d.Get("ReleaseVersion")=="2021-03-31T00:00:00","发布日期错误");
   Assert(d.Get("PossessingFromTheBeginning")=="true"&&d.Get("IsLockedAtTheBeginning")=="false"&&d.Get("IsBonusTrack")=="false","新建布尔值错误");
   Assert(d.Xml.Descendants("FumenConstIntegerPart").All(x=>x.Value=="0")&&d.Xml.Descendants("FumenConstFractionalPart").All(x=>x.Value=="0"),"无谱面定数非0/0");
  });
  check("导入日期和数值布尔值归一化",()=>
  {
   var xml=MusicDocument.CreateNew().Xml;
   xml.Root!.Element("ReleaseVersion")!.Value="1999-01-01T00:00:00";
   xml.Root.Element("IsLunatic")!.Value="1";xml.Root.Element("IsReMaster")!.Value="0";
   var d=new MusicDocument(xml);
   Assert(d.Get("ReleaseVersion")=="2021-03-31T00:00:00","导入日期未归一化");
   Assert(d.Get("IsLunatic")=="true"&&d.Get("IsReMaster")=="false","导入布尔值未归一化");
  });
  check("旧Music缺失布尔字段补齐且保留已有值",()=>
  {
   var empty=new MusicDocument(XDocument.Parse("<MusicData />"));
   foreach(var field in MusicDocument.BooleanFields)Assert(empty.Get(field)==(field=="PossessingFromTheBeginning"?"true":"false"),"缺失布尔值没有补齐："+field);
   var imported=new MusicDocument(XDocument.Parse("<MusicData><IsLunatic>1</IsLunatic><PossessingFromTheBeginning>0</PossessingFromTheBeginning><IsBonusTrack>true</IsBonusTrack></MusicData>"));
   Assert(imported.Get("IsReMaster")=="false"&&imported.Get("IsLockedAtTheBeginning")=="false","旧版模式或锁定默认值错误");
   Assert(imported.Get("IsLunatic")=="true"&&imported.Get("PossessingFromTheBeginning")=="false"&&imported.Get("IsBonusTrack")=="true","导入既有值被默认值覆盖");
  });
  check("名称始终单向覆盖排序名",()=>
  {
   var d=MusicDocument.CreateNew();d.Set("NameForSort","手工排序");d.Set("Name/str","第一首");
   Assert(d.Get("NameForSort")=="第一首","名称修改没有同步排序名");
   d.Set("NameForSort","另一个排序名");Assert(d.Get("Name/str")=="第一首","排序名反向修改了名称");
   d.Set("Name/str","第二首");Assert(d.Get("NameForSort")=="第二首","后续名称修改没有覆盖排序名");
  });
  check("Boss身份与所有波次始终同步",()=>
  {
   var d=MusicDocument.CreateNew();d.Xml.Root!.Add(new XElement("WaveAttribute",new XElement("AttributeType","Leaf")));
   d.SelectBoss(new BossCardChoice(123,"卡片","Aqua","项目"),false);
   Assert(d.Get("BossCard/id")=="123"&&d.Xml.Descendants("AttributeType").All(x=>x.Value=="Aqua"),"旧可选同步路径仍然跳过属性");
   Assert(!d.Xml.Root.Elements("WaveAttribute").Any(x=>x.Element("id")!=null),"属性生成了数字ID");
  });
  check("未知节点和额外难度保留",()=>
  {
   var d=MusicDocument.CreateNew();d.Xml.Root!.Add(new XElement("Unknown","保留"));
   var extra=new XElement(d.Xml.Root.Element("FumenData")!.Elements().First());extra.Add(new XElement("FutureFlag","future"));d.Xml.Root.Element("FumenData")!.Add(extra);
   var copy=new MusicDocument(d.Xml);copy.Set("Name/str","新歌");
   Assert(copy.Xml.Root!.Element("Unknown")!.Value=="保留"&&copy.Xml.Root.Element("FumenData")!.Elements().Count()==6&&XNode.DeepEquals(extra,copy.Xml.Root.Element("FumenData")!.Elements().Last()),"丢失未知节点或额外难度");
  });
  check("定数接受边界拒绝任意小数",()=>
  {
   var d=ValidMusic();var whole=d.Xml.Descendants("FumenConstIntegerPart").First();var fraction=d.Xml.Descendants("FumenConstFractionalPart").First();
   foreach(var w in new[]{0,20})foreach(var f in new[]{0,90}){whole.Value=w.ToString();fraction.Value=f.ToString();Assert(d.Validate().Count==0,string.Join(";",d.Validate()));}
   foreach(var f in new[]{-1,1,9,15,99,100}){fraction.Value=f.ToString();Assert(d.Validate().Any(x=>x.Contains("定数")),"接受非法小数 "+f);}
   fraction.Value="0";foreach(var w in new[]{-1,21}){whole.Value=w.ToString();Assert(d.Validate().Any(x=>x.Contains("定数")),"接受非法整数 "+w);}
  });
  check("拒绝无效属性与布尔值",()=>
  {
   var d=ValidMusic();d.SelectBoss(new BossCardChoice(123,"测试","Invalid","项目"),true);d.Set("IsLunatic","yes");
   var errors=d.Validate();Assert(errors.Any(x=>x.Contains("属性"))&&errors.Any(x=>x.Contains("IsLunatic")),"格式校验不完整");
  });
  check("版权ID不可为空且日期固定",()=>
  {
   var d=ValidMusic();d.Set("MusicRightsName/id","");d.Set("ReleaseVersion","");
   Assert(d.Validate().Any(x=>x.Contains("版权")),"空版权ID未拦截");Assert(d.Get("ReleaseVersion")=="2021-03-31T00:00:00","允许改写固定日期");
  });
  check("完整新Music数值类型可标准反序列化",()=>
  {
   var d=ValidMusic();Assert(d.Validate().Count==0,string.Join(";",d.Validate()));var parsed=XDocument.Parse(d.Xml.ToString());
   foreach(var node in parsed.Descendants().Where(x=>new[]{"id","FumenConstIntegerPart","FumenConstFractionalPart","BossLevel","CostToUnlock","FinishBonus","SortOrder","BossLockHpCoef","BossVoiceNo","RewardCount"}.Contains(x.Name.LocalName)))XmlConvert.ToInt32(node.Value);
   foreach(var node in parsed.Descendants().Where(x=>new[]{"IsLunatic","IsReMaster","EpReleaseFlag","PossessingFromTheBeginning","IsLockedAtTheBeginning","IsBonusTrack"}.Contains(x.Name.LocalName)))XmlConvert.ToBoolean(node.Value);
   XmlConvert.ToDateTime(parsed.Root!.Element("ReleaseVersion")!.Value,XmlDateTimeSerializationMode.RoundtripKind);
  });
  check("允许导入源原地保存到同一文件夹",()=>
  {
   var d=ValidMusic();var root=Path.GetFullPath(".");d.SourcePath=Path.Combine(root,"Music.xml");
   Assert(d.TargetPath(root)==d.SourcePath,"仍拒绝原地覆盖");
  });
 }
 public static void RunFiles(string root,Action<string,Action> check)
 {
  check("Music原地保存先备份且保持未知节点",()=>{
   var directory=Path.Combine(root,"music-overwrite");var d=ValidMusic();d.Xml.Root!.Add(new XElement("FutureFlag","保留"));d.SaveAs(directory);
   var target=Path.Combine(directory,"Music.xml");var original=File.ReadAllBytes(target);var loaded=MusicDocument.Load(target);loaded.Set("Name/str","新名称");
   var prepare=typeof(MusicDocument).GetMethod("PrepareSave")??throw new Exception("缺少安全保存预览");
   var commit=typeof(MusicDocument).GetMethod("CommitSave")??throw new Exception("缺少安全保存提交");
   var plan=prepare.Invoke(loaded,new object?[]{target,Path.Combine(root,"save","music-backups")})!;
   Assert(File.ReadAllBytes(target).SequenceEqual(original),"预览发生写入");commit.Invoke(loaded,new[]{plan,(object)true});
   var backup=(string)plan.GetType().GetProperty("BackupDirectory")!.GetValue(plan)!;
   Assert(File.ReadAllBytes(Path.Combine(backup,"Music.xml")).SequenceEqual(original),"没有备份原文件");
   Assert(File.Exists(Path.Combine(backup,"backup-info.json")),"缺少备份映射");
   var saved=MusicDocument.Load(target);Assert(saved.Get("Name/str")=="新名称"&&saved.Get("FutureFlag")=="保留","原地保存数据错误");
   Assert(Directory.GetFileSystemEntries(directory).Length==1,"歌曲目录遗留备份或临时文件");
   loaded.Set("Name/str","第二次");var again=prepare.Invoke(loaded,new object?[]{target,Path.Combine(root,"save","music-backups")})!;commit.Invoke(loaded,new[]{again,(object)true});
   Assert(MusicDocument.Load(target).Get("Name/str")=="第二次","首次保存后未更新来源哈希");
  });
  check("Music拒绝导入后及确认后外部修改",()=>{
   var directory=Path.Combine(root,"music-conflict");ValidMusic().SaveAs(directory);var target=Path.Combine(directory,"Music.xml");var loaded=MusicDocument.Load(target);
   var prepare=typeof(MusicDocument).GetMethod("PrepareSave")??throw new Exception("缺少安全保存预览");
   var commit=typeof(MusicDocument).GetMethod("CommitSave")??throw new Exception("缺少安全保存提交");
   var plan=prepare.Invoke(loaded,new object?[]{target,Path.Combine(root,"save","music-backups")})!;
   File.AppendAllText(target,"<!--外部修改-->");var changed=File.ReadAllBytes(target);
   bool rejected=false;try{commit.Invoke(loaded,new[]{plan,(object)true});}catch(System.Reflection.TargetInvocationException ex)when(ex.InnerException is IOException){rejected=true;}
   Assert(rejected&&File.ReadAllBytes(target).SequenceEqual(changed),"提交覆盖外部修改");
   rejected=false;try{prepare.Invoke(loaded,new object?[]{target,Path.Combine(root,"save","music-backups")});}catch(System.Reflection.TargetInvocationException ex)when(ex.InnerException is IOException){rejected=true;}
   Assert(rejected,"导入基线未校验");
  });
  check("Music直接保存到所选文件夹且不新建歌曲子目录",()=>{
   var d=ValidMusic();d.Set("Name/id","009123");
   var charts=d.Xml.Root!.Element("FumenData")!.Elements("FumenData").ToList();
   for(int i=0;i<5;i++)charts[i].Element("FumenFile")!.Element("path")!.Value=$"charts/9123_{i}.ogkr";
   foreach(var mode in new[]{MusicChartMode.Normal,MusicChartMode.Lunatic,MusicChartMode.ReMaster}){
    d.SetChartMode(mode);var directory=Path.Combine(Path.GetFullPath(root),"music-save-check",mode.ToString());
    d.SaveAs(directory);var path=Path.Combine(directory,"Music.xml");
    Assert(File.Exists(path),"未直接保存到选定文件夹");Assert(Directory.GetDirectories(directory).Length==0,"保存创建了额外子文件夹");
    var saved=XDocument.Load(path);Assert(saved.Root!.Element("dataName")!.Value=="music9123","XML数据目录名未随歌曲ID生成");
    var savedCharts=saved.Root.Element("FumenData")!.Elements("FumenData").ToList();
    for(int i=0;i<5;i++)Assert(savedCharts[i].Element("FumenFile")!.Element("path")!.Value==(d.IsChartEnabled(i)?$"charts/9123_{i}.ogkr":""),"保存禁用谱面路径不为空");
    Assert(saved.Root.Element("IsLunatic")!.Value==(mode==MusicChartMode.Normal?"false":"true")&&saved.Root.Element("IsReMaster")!.Value==(mode==MusicChartMode.ReMaster?"true":"false"),"保存官方模式标记错误");
   }
   Assert(charts.All(x=>!string.IsNullOrEmpty(x.Element("FumenFile")!.Element("path")!.Value)),"保存修改了草稿谱面名");
  });
 }
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static MusicDocument ValidMusic()
 {
  var d=MusicDocument.CreateNew();d.SelectBoss(new BossCardChoice(123,"测试卡","Fire","项目"),true);
  foreach(var reference in new[]{"ArtistName","Genre","FirstChapterName","VersionID","StageID"}){d.Set(reference+"/id","1");d.Set(reference+"/str","测试引用");}
  d.Set("Name/id","9001");d.Set("Name/str","测试");d.Set("MusicSourceName/id","9001");d.Set("MusicSourceName/str","音源");d.Xml.Descendants("path").First().Value="9001_00.ogkr";return d;
 }
}
