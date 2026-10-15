using System.Text.Json;
using System.Xml.Linq;

namespace CardFileMaker;

public sealed record ChapterOutput(string Kind,int Id,XDocument Xml,string? SourcePath=null,string? SourceHash=null);
public sealed class ChapterBuild
{
    public List<ChapterOutput> Files { get; }=[];
    public List<string> Warnings { get; }=[];
}

public static class ChapterExporter
{
    public static ChapterBuild Build(ChapterDraft draft,ChapterCatalog catalog)
    {
        ChapterXml.ValidateStorage([draft]);
        if(catalog.Errors.Count>0)throw new InvalidDataException("引用库不完整，先修复读取错误：\n"+string.Join("\n",catalog.Errors.Take(10)));
        var result=new ChapterBuild();
        CheckId("Chapter",draft.Id,draft.Name,draft.SourcePath);
        if(draft.DisplayNumber<1||draft.SortOrder<0)throw new InvalidDataException("显示序号须为正数，排序号不能为负数。");
        if(draft.Songs.Count==0)throw new InvalidDataException("章节至少需要一首曲目。");
        if(draft.Songs.Select(x=>x.Id).Distinct().Count()!=draft.Songs.Count)throw new InvalidDataException("章节内曲目 ID 重复。");
        if(draft.CreateStory) {
            CheckId("Story",draft.StoryId,draft.StoryName,draft.StorySourcePath);
            var story=ChapterXml.New("Story",draft.StoryId,draft.StoryName);var s=story.Root!;
            s.Add(new XElement("DisplayNumber",draft.DisplayNumber),new XElement("SelectableFlag",true),new XElement("EventChapterFlag",false),new XElement("CanUseAlmightyJewel",true),ChapterXml.Ref("JewelGraphicName",draft.JewelId,draft.JewelName));
            result.Files.Add(new("Story",draft.StoryId,story,draft.StorySourcePath,draft.StorySourceHash));
            result.Warnings.Add("新 Story 使用现有章节素材；部分游戏标题固定显示为“第 N 章”。");
        } else {
            var story=catalog.Find("Story",draft.StoryId)??throw new InvalidDataException("所引用 Story 不存在。");
            if(ChapterXml.Parse(story.Xml,"StoryData").Root!.Element("SelectableFlag")?.Value=="false")throw new InvalidDataException("所选 Story 被设置为不可选，请选择可用 Story 或创建新分类。");
            if(ChapterXml.Parse(story.Xml,"StoryData").Root!.Element("EventChapterFlag")?.Value=="true")result.Warnings.Add("所选 Story 是活动章节，显示仍受服务端活动状态影响。");
        }
        bool KnownReference(string field,int id)=>catalog.Get("Chapter").Any(x=>ChapterXml.Id(ChapterXml.Parse(x.Xml,"ChapterData").Root!.Element(field))==id);
        if(!KnownReference("BackgroundName",draft.BackgroundId)||!KnownReference("JewelGraphicName",draft.JewelId))throw new InvalidDataException("背景和宝石图标须选择游戏或 AXXX 已有引用。");
        var chapter=string.IsNullOrWhiteSpace(draft.OriginalXml)?ChapterXml.New("Chapter",draft.Id,draft.Name):ChapterXml.Parse(draft.OriginalXml,"ChapterData");
        var root=chapter.Root!;
        ChapterXml.Set(root,"dataName","chapter"+draft.Id.ToString("D6"));ChapterXml.Set(root,"Name",ChapterXml.Ref("Name",draft.Id,draft.Name));
        ChapterXml.Set(root,"Story",ChapterXml.Ref("Story",draft.StoryId,draft.StoryName));
        if(root.Element("Type") is null)root.Add(new XElement("Type","MainChapter"));
        ChapterXml.Set(root,"DisplayNumber",draft.DisplayNumber);ChapterXml.Set(root,"SortOrder",draft.SortOrder);
        ChapterXml.Set(root,"IsOriginalEvent",false);ChapterXml.Set(root,"IsLockedAtTheBeginning",false);
        ChapterXml.Set(root,"BackgroundName",ChapterXml.Ref("BackgroundName",draft.BackgroundId,draft.BackgroundName));
        ChapterXml.Set(root,"JewelGraphicName",ChapterXml.Ref("JewelGraphicName",draft.JewelId,draft.JewelName));
        var oldSongs=root.Element("MusicList")?.Elements("ChapterMusic").ToList()??[];
        var songs=new XElement("MusicList");
        var purchases=root.Element("PurchaseItemList");if(purchases is null){purchases=new XElement("PurchaseItemList");root.Add(purchases);}
        foreach(var previous in purchases.Elements().Where(x=>x.Element("ItemType")?.Value=="Music"&&draft.ManagedChallengeMusicIds.Contains(ChapterXml.Id(x.Element("ItemName")))).ToArray())previous.Remove();
        foreach(var song in draft.Songs) {
            if(song.Id<1||string.IsNullOrWhiteSpace(song.Name))throw new InvalidDataException("曲目 ID 或名称无效。");
            var old=oldSongs.FirstOrDefault(x=>ChapterXml.Id(x.Element("MusicName"))==song.Id);
            var entry=old is null?new XElement("ChapterMusic",ChapterXml.Ref("MusicName",song.Id,song.Name),Condition("Condition","None",-1,"Invalid")):new XElement(old);
            ChapterXml.Set(entry,"MusicName",ChapterXml.Ref("MusicName",song.Id,song.Name));songs.Add(entry);
            if(song.Challenge.SourcePath is not null)foreach(var previous in purchases.Elements().Where(x=>x.Element("ItemType")?.Value=="Music"&&ChapterXml.Id(x.Element("ItemName"))==song.Id).ToArray())previous.Remove();
            if(!song.Challenge.Enabled) {
                if(catalog.Find("Music",song.Id) is null) {
                    var music=ChapterXml.Parse(song.Xml,"MusicData");
                    if(ChapterXml.Id(music.Root)!=song.Id)throw new InvalidDataException("曲目 XML 与所选 ID 不一致。");
                    result.Files.Add(new("Music",song.Id,music,song.SourcePath,song.SourceHash));
                }
                continue;
            }
            var challenge=song.Challenge;
            CheckId("Trophy",challenge.TrophyId,challenge.TrophyName,challenge.SourcePath);
            if(challenge.ScoreType is not ("TechnicalScore" or "BattleScore")||challenge.Score<1||(challenge.ScoreType=="TechnicalScore"&&challenge.Score>1010000))throw new InvalidDataException("分数门槛无效；技术分范围为 1～1010000。");
            if(!ChapterXml.Difficulties.Contains(challenge.Difficulty)||challenge.ClaimCost<0)throw new InvalidDataException("挑战难度或领取费用无效。");
            var musicXml=ChapterXml.Parse(song.Xml,"MusicData");
            if(ChapterXml.Id(musicXml.Root)!=song.Id)throw new InvalidDataException("挑战曲目 XML 与 ID 不一致。");
            int difficulty=Array.IndexOf(ChapterXml.Difficulties,challenge.Difficulty);
            var fumen=musicXml.Root!.Element("FumenData")?.Elements().ElementAtOrDefault(difficulty);
            if(fumen is null||string.IsNullOrWhiteSpace(fumen.Element("FumenFile")?.Element("path")?.Value))throw new InvalidDataException(song.Name+"：所选难度没有谱面文件。");
            bool special=musicXml.Root.Element("IsLunatic")?.Value is "true" or "1"||musicXml.Root.Element("IsReMaster")?.Value is "true" or "1";
            if(special!=(difficulty==4))throw new InvalidDataException(song.Name+"：挑战难度与歌曲的普通／特殊模式不一致。");
            ChapterXml.Set(musicXml.Root,"PossessingFromTheBeginning",false);ChapterXml.Set(musicXml.Root,"IsLockedAtTheBeginning",false);
            ChapterXml.Set(musicXml.Root,"FirstChapterName",ChapterXml.Ref("FirstChapterName",draft.Id,draft.Name));
            ChapterXml.Set(entry,"Condition",Condition("Condition","None",-1,"Invalid"));
            result.Files.Add(new("Music",song.Id,musicXml,song.SourcePath,song.SourceHash));
            result.Files.Add(new("Trophy",challenge.TrophyId,Trophy(song),challenge.SourcePath,challenge.SourceHash));
            foreach(var previous in purchases.Elements().Where(x=>x.Element("ItemType")?.Value=="Music"&&ChapterXml.Id(x.Element("ItemName"))==song.Id).ToArray())previous.Remove();
            purchases.Add(new XElement("PurchaseItem",new XAttribute(ChapterXml.Xsi+"type","PurchaseMusic"),new XElement("RewardCategory",0),new XElement("SortNumber",songs.Elements().Count()),new XElement("ItemType","Music"),
                ChapterXml.Ref("PurchaseItemName",challenge.TrophyId,challenge.TrophyName),new XElement("PurchaseCountLimit",1),new XElement("PurchaseConditionType","Hide"),
                new XElement("Condition",Condition("PurchaseCondition","Trophy",challenge.TrophyId,challenge.TrophyName)),ChapterXml.Ref("ItemName",song.Id,song.Name),new XElement("Cost",challenge.ClaimCost)));
        }
        ChapterXml.Set(root,"MusicList",songs);result.Files.Insert(0,new("Chapter",draft.Id,chapter,draft.SourcePath,draft.SourceHash));
        if(draft.Songs.Any(x=>x.Challenge.Enabled))result.Warnings.Add("分数达标后获得称号，随后在章节商店手动领取歌曲。原有拥有记录和全解补丁可能提前开放歌曲；跨登录保存取决于在线服务器。");
        result.Warnings.Add("导出只包含配置 XML；音源、谱面和影片素材需要在包中另行备齐。其他章节或解锁途径仍可能获得同一首歌曲。");
        if(draft.EnableExperiments) {
            foreach(var experiment in draft.Experiments) {
                if(experiment.Kind is not ("MemoryChapter" or "SecretMusicCondition" or "Movie"))throw new InvalidDataException("不支持的实验 XML 类型。");
                var xml=ChapterXml.Parse(experiment.Xml,experiment.Kind+"Data");int id=ChapterXml.Id(xml.Root);
                CheckId(experiment.Kind,id,ChapterXml.Name(xml.Root),experiment.SourcePath);
                ChapterXml.Set(xml.Root!,"dataName",experiment.Kind.ToLowerInvariant()+id.ToString("D6"));
                if(experiment.Kind=="MemoryChapter"&&id!=draft.Id)throw new InvalidDataException("MemoryChapter ID 必须与当前 Chapter ID 相同。");
                if(experiment.Kind=="MemoryChapter"&&xml.Root!.Element("memoryChapterType")?.Value is not ("Spring" or "Summer" or "Autumn" or "Winter" or "Ongeki" or "End"))throw new InvalidDataException("Memory 章节类型无效，请从已有模板复制。");
                if(experiment.Kind=="SecretMusicCondition"&&(xml.Root!.Element("SecretCondition") is null||xml.Root.Element("Type") is null))throw new InvalidDataException("隐藏曲条件缺少类型或条件内容。");
                if(experiment.Kind=="Movie") {
                    string path=xml.Root!.Element("MovieFileName")?.Element("path")?.Value??"";
                    if(string.IsNullOrWhiteSpace(path)||Path.IsPathRooted(path)||path.Split('/','\\').Contains("..")||!path.EndsWith(".usm",StringComparison.OrdinalIgnoreCase)||path.Contains(':'))throw new InvalidDataException("影片路径必须是包内相对 .usm 路径。");
                    result.Warnings.Add("Movie XML 仅配置引用，请另行准备兼容的 USM 影片："+path);
                }
                result.Files.Add(new(experiment.Kind,id,xml,experiment.SourcePath,experiment.SourceHash));
            }
            result.Warnings.Add("实验配置需要游戏内验证：Memory 章节依赖用户章节记录及全局推进链，影片观看状态按章节保存；不会把 MP4 转为 USM。");
        }
        foreach(var memory in result.Files.Where(x=>x.Kind=="MemoryChapter")) {
            foreach(var reference in memory.Xml.Descendants("MusicName").Concat(memory.Xml.Descendants("TargetMusicName")))Require("Music",ChapterXml.Id(reference));
            foreach(var reference in memory.Xml.Descendants("MovieFileName"))Require("Movie",ChapterXml.Id(reference));
            foreach(var reference in memory.Xml.Descendants("SecretConditionNameStringID").Concat(memory.Xml.Descendants("SecretMusicConditionNameStringID")))Require("SecretMusicCondition",ChapterXml.Id(reference.Element("StringID")));
            foreach(var reward in memory.Xml.Descendants("addMylistMusicList").Descendants("StringID"))if(draft.Songs.Any(s=>s.Challenge.Enabled&&s.Id==ChapterXml.Id(reward)))throw new InvalidDataException("Memory 通关奖励会绕过分数门槛，请移除该曲目的自动 Mylist 奖励。");
        }
        if(catalog.Find("MemoryChapter",draft.Id) is not null&&!result.Files.Any(x=>x.Kind=="MemoryChapter"&&x.Id==draft.Id)&&draft.Songs.Any(x=>x.Challenge.Enabled))throw new InvalidDataException("此章节已有 Memory 奖励逻辑，须导入实验配置检查是否绕过分数门槛。");
        foreach(var secret in result.Files.Where(x=>x.Kind=="SecretMusicCondition")) {
            foreach(var target in secret.Xml.Descendants("TargetName")) {int id=ChapterXml.Id(target);Require("Music",id);if(!draft.Songs.Any(x=>x.Id==id))throw new InvalidDataException("隐藏曲条件引用的歌曲必须在当前章节曲目列表中。");}
            foreach(var card in secret.Xml.Descendants("CardSetData"))Require("Card",ChapterXml.Id(card.Element("StringID")));
        }
        if(result.Files.Select(x=>(x.Kind,x.Id)).Distinct().Count()!=result.Files.Count)throw new InvalidDataException("导出配置中存在重复 ID。");
        result.Warnings.Add("静态校验不等同于游戏内验证；CFM 不修改游戏 DLL 或在线账号。");return result;

        void Require(string kind,int id) { if(id<1||catalog.Find(kind,id) is null&&!result.Files.Any(x=>x.Kind==kind&&x.Id==id))throw new InvalidDataException($"缺少引用：{kind} {id}"); }
        void CheckId(string kind,int id,string name,string? source) {
            if(id<1||id>999999||string.IsNullOrWhiteSpace(name))throw new InvalidDataException(kind+" ID 须为 1～999999，名称不能为空。");
            var conflicts=catalog.Get(kind).Where(x=>x.Id==id&&!string.Equals(x.Path,source,StringComparison.OrdinalIgnoreCase));
            if(conflicts.Any())throw new InvalidDataException($"{kind} ID {id} 已被占用，请分配新 ID。");
            if(source is not null&&catalog.Get(kind).Any(x=>x.Id==id&&x.Path.Equals(source,StringComparison.OrdinalIgnoreCase)&&x.Group!="自制 opt"))throw new InvalidDataException("请为导入的官方数据分配新 ID，再导出到自制包。");
        }
    }
    static XElement Condition(string name,string type,int id,string text)=>new(name,new XElement("ItemType",type),ChapterXml.Ref("ConditionItem",id,text),new XElement("OwnNumCondition",1),new XElement("Comparison","MoreThanOrEqual"));
    static XDocument Trophy(ChapterSong song) {
        var c=song.Challenge;
        var xml=string.IsNullOrWhiteSpace(c.OriginalXml)?ChapterXml.New("Trophy",c.TrophyId,c.TrophyName):ChapterXml.Parse(c.OriginalXml,"TrophyData");
        var root=xml.Root!;
        ChapterXml.Set(root,"dataName","trophy"+c.TrophyId.ToString("D6"));ChapterXml.Set(root,"Name",ChapterXml.Ref("Name",c.TrophyId,c.TrophyName));
        if(string.IsNullOrWhiteSpace(c.OriginalXml))root.Add(new XElement("Disabled",false),new XElement("PossessingFromTheBeginning",false),new XElement("IsLockedAtTheBeginning",false),new XElement("TrophyRarityType","Normal"),new XElement("SortOrder",c.TrophyId));
        ChapterXml.Set(root,"Description",$"{song.Name} / {c.Difficulty} / {c.ScoreType} ≥ {c.Score}");
        ChapterXml.Set(root,"TrophyConditionType","PlayResultReference");
        var rule=root.Element("TrophyCondition");
        if(rule is null) {
            rule=new XElement("TrophyCondition",new XAttribute(ChapterXml.Xsi+"type","TrophyConditionPlayResult"),new XElement("TrophyConditionType","None"),
                new XElement("PlayResultFlag",new[]{"Play","Retire","Completion","Win","FullCombo","AllBreak","FullBell","NoDamage"}.Select(x=>new XElement(x,x=="Completion"))),new XElement("TechnicalRank","None"),new XElement("BattleRank","None"));
            root.Add(rule);
        }
        ChapterXml.Set(rule,"MusicName",ChapterXml.Ref("MusicName",song.Id,song.Name));
        ChapterXml.Set(rule,"FumenDifficultyFlag",new XElement("FumenDifficultyFlag",ChapterXml.Difficulties.Select(x=>new XElement(x,x==c.Difficulty))));
        var flags=rule.Element("PlayResultFlag");if(flags is null){flags=new XElement("PlayResultFlag");rule.Add(flags);}
        ChapterXml.Set(flags,"Win",c.RequireWin);
        var value=rule.Element("PlayResultValue");if(value is null){value=new XElement("PlayResultValue");rule.Add(value);}
        ChapterXml.Set(value,"PlayResultValueType",c.ScoreType);ChapterXml.Set(value,"Value",c.Score);ChapterXml.Set(value,"Comparison","MoreThanOrEqual");
        return xml;
    }
    public static PreparedBatch Prepare(IReadOnlyList<ChapterDraft> chapters,ChapterCatalog catalog,string opt,string? backupRoot=null) {
        if(chapters.Count==0)throw new InvalidDataException("请选中要导出的章节。");
        string target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(opt));PathSafety.RejectLinkedAncestors(target);
        if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(target),"^A[0-9A-Z]{3}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new InvalidDataException("请选择具体的 AXXX 文件夹。");
        if(catalog.PackageDirectory!=""&&PathSafety.Within(target,catalog.PackageDirectory)&&!PathSafety.Within(target,Path.Combine(catalog.PackageDirectory,"option")))throw new IOException("不能输出到游戏基础数据目录。");
        backupRoot=Path.GetFullPath(backupRoot??Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"chapter-backups"));
        var batch=new PreparedBatch { OutputDirectory=target,BackupRoot=backupRoot,BackupDirectory=Path.Combine(backupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")) };
        var used=new HashSet<(string,int)>();
        foreach(var chapter in chapters) {
            var build=Build(chapter,catalog);batch.Warnings.AddRange(build.Warnings);
            foreach(var file in build.Files) {
                if(!used.Add((file.Kind,file.Id)))throw new InvalidDataException($"多个章节重复导出 {file.Kind} {file.Id}，请分别导出或调整引用。");
                string path=file.SourcePath is not null&&PathSafety.Within(file.SourcePath,target)?Path.GetFullPath(file.SourcePath):Path.Combine(target,file.Kind.ToLowerInvariant(),file.Kind.ToLowerInvariant()+file.Id.ToString("D6"),file.Kind+".xml");
                if(file.SourcePath is not null&&file.SourceHash is not null&&AutoSaveStore.FileHash(file.SourcePath)!=file.SourceHash)throw new IOException("来源 XML 已变化或丢失，请重新导入："+file.SourcePath);
                Add(path,file.Xml);
            }
        }
        string config=Path.Combine(target,"DataConfig.xml");
        if(!File.Exists(config)){using var input=TemplateResources.Open("DataConfig.xml");Add(config,XDocument.Load(input));}
        return batch;
        void Add(string path,XDocument xml) {
            PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
            if(File.Exists(path)&&(new FileInfo(path).Length>=100_000_000||File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))throw new IOException("目标 XML 无效或达到 100 MB。");
            using var stream=new MemoryStream();xml.Save(stream);if(stream.Length>=100_000_000)throw new IOException("导出 XML 达到 100 MB。");
            batch.Files.Add(new(path,stream.ToArray(),AutoSaveStore.FileHash(path)));
        }
    }
    public static void RecordExport(IReadOnlyList<ChapterDraft> drafts,PreparedBatch plan) {
        foreach(var file in plan.Files) {
            if(Path.GetFileName(file.Path)=="DataConfig.xml")continue;
            var xml=Catalog.ReadXml(file.Path);int id=ChapterXml.Id(xml.Root);string kind=xml.Root!.Name.LocalName.Replace("Data","");
            string hash=AutoSaveStore.FileHash(file.Path)!;
            foreach(var draft in drafts) {
                if(kind=="Chapter"&&draft.Id==id){draft.SourcePath=file.Path;draft.SourceHash=hash;draft.OriginalXml=xml.ToString();draft.ManagedChallengeMusicIds=draft.Songs.Where(x=>x.Challenge.Enabled).Select(x=>x.Id).ToList();}
                if(kind=="Story"&&draft.CreateStory&&draft.StoryId==id){draft.StorySourcePath=file.Path;draft.StorySourceHash=hash;}
                foreach(var song in draft.Songs) {
                    if(kind=="Music"&&song.Id==id){song.SourcePath=file.Path;song.SourceHash=hash;song.Xml=xml.ToString();}
                    if(kind=="Trophy"&&song.Challenge.Enabled&&song.Challenge.TrophyId==id){song.Challenge.SourcePath=file.Path;song.Challenge.SourceHash=hash;song.Challenge.OriginalXml=xml.ToString();}
                }
                foreach(var experiment in draft.Experiments)if(experiment.Kind==kind&&ChapterXml.Id(ChapterXml.Parse(experiment.Xml,kind+"Data").Root)==id){experiment.SourcePath=file.Path;experiment.SourceHash=hash;}
            }
        }
    }
}
