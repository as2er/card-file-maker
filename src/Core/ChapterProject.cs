using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace CardFileMaker;

public sealed class ChapterDraft
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public int Id { get; set; } = 910001;
    public string Name { get; set; } = "自定义章节";
    public int StoryId { get; set; } = 1;
    public string StoryName { get; set; } = "第1章";
    public bool CreateStory { get; set; }
    public string? StorySourcePath { get; set; }
    public string? StorySourceHash { get; set; }
    public int DisplayNumber { get; set; } = 1;
    public int SortOrder { get; set; } = 200000;
    public int BackgroundId { get; set; } = 100101;
    public string BackgroundName { get; set; } = "校舎前";
    public int JewelId { get; set; } = 100;
    public string JewelName { get; set; } = "第1章ジュエル";
    public string OriginalXml { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? SourceHash { get; set; }
    public List<ChapterSong> Songs { get; set; } = [];
    public List<int> ManagedChallengeMusicIds { get; set; } = [];
    public bool EnableExperiments { get; set; }
    public List<ChapterXmlDraft> Experiments { get; set; } = [];
    public override string ToString() => $"{Id} · {Name}";
    public ChapterDraft Snapshot() => JsonSerializer.Deserialize<ChapterDraft>(JsonSerializer.Serialize(this))!;

    public static ChapterDraft Import(string path, ChapterCatalog catalog)
    {
        var xml = Catalog.ReadXml(path);
        var root = xml.Root ?? throw new InvalidDataException("章节 XML 为空。");
        if (root.Name != "ChapterData") throw new InvalidDataException("请选择 Chapter.xml。");
        var draft = new ChapterDraft {
            Id=ChapterXml.Id(root), Name=ChapterXml.Name(root), StoryId=ChapterXml.Id(root.Element("Story")), StoryName=ChapterXml.Name(root.Element("Story")),
            DisplayNumber=ChapterXml.Int(root,"DisplayNumber",1), SortOrder=ChapterXml.Int(root,"SortOrder",200000),
            BackgroundId=ChapterXml.Id(root.Element("BackgroundName")), BackgroundName=ChapterXml.Name(root.Element("BackgroundName")),
            JewelId=ChapterXml.Id(root.Element("JewelGraphicName")), JewelName=ChapterXml.Name(root.Element("JewelGraphicName")),
            OriginalXml=xml.ToString(), SourcePath=Path.GetFullPath(path), SourceHash=AutoSaveStore.FileHash(path)
        };
        foreach (var entry in root.Element("MusicList")?.Elements("ChapterMusic") ?? []) {
            int id=ChapterXml.Id(entry.Element("MusicName"));
            var music=catalog.Find("Music",id);
            draft.Songs.Add(music is null ? new ChapterSong { Id=id, Name=ChapterXml.Name(entry.Element("MusicName")) } : ChapterSong.From(music));
        }
        foreach(var song in draft.Songs) {
            var purchase=root.Element("PurchaseItemList")?.Elements().FirstOrDefault(x=>x.Element("ItemType")?.Value=="Music"&&ChapterXml.Id(x.Element("ItemName"))==song.Id);
            var conditions=purchase?.Element("Condition")?.Elements("PurchaseCondition").ToArray()??[];
            var condition=conditions.Length==1&&conditions[0].Element("ItemType")?.Value=="Trophy"?conditions[0]:null;
            if(condition is null||catalog.Find("Trophy",ChapterXml.Id(condition.Element("ConditionItem"))) is not { } reference)continue;
            var trophy=ChapterXml.Parse(reference.Xml,"TrophyData").Root!;var rule=trophy.Element("TrophyCondition");var value=rule?.Element("PlayResultValue");
            if(trophy.Element("TrophyConditionType")?.Value!="PlayResultReference"||rule?.Attribute(ChapterXml.Xsi+"type")?.Value!="TrophyConditionPlayResult")continue;
            string type=value?.Element("PlayResultValueType")?.Value??"";
            var difficulties=rule?.Element("FumenDifficultyFlag")?.Elements().Where(x=>x.Value is "true" or "1").ToArray()??[];
            if(type is not ("TechnicalScore" or "BattleScore")||value?.Element("Comparison")?.Value!="MoreThanOrEqual"||difficulties.Length!=1||ChapterXml.Id(rule?.Element("MusicName"))!=song.Id)continue;
            song.Challenge=new(){Enabled=true,TrophyId=reference.Id,TrophyName=reference.Name,ScoreType=type,Score=ChapterXml.Int(value!,"Value"),Difficulty=difficulties[0].Name.LocalName,RequireWin=rule?.Element("PlayResultFlag")?.Element("Win")?.Value is "true" or "1",ClaimCost=ChapterXml.Int(purchase!,"Cost"),SourcePath=reference.Path,SourceHash=AutoSaveStore.FileHash(reference.Path),OriginalXml=reference.Xml};
            draft.ManagedChallengeMusicIds.Add(song.Id);
        }
        return draft;
    }
}

public sealed class ChapterSong
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Xml { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? SourceHash { get; set; }
    public ScoreChallenge Challenge { get; set; } = new();
    public override string ToString() => $"{Id} · {Name}" + (Challenge.Enabled ? "  · 分数挑战" : "");
    public static ChapterSong From(ChapterReference reference) => FromXml(reference.Xml,reference.Path,AutoSaveStore.FileHash(reference.Path));
    public static ChapterSong FromXml(string xml,string? sourcePath=null,string? sourceHash=null) {
        var root=ChapterXml.Parse(xml,"MusicData").Root!;
        bool special=root.Element("IsLunatic")?.Value is "true" or "1"||root.Element("IsReMaster")?.Value is "true" or "1";
        int difficulty=special?4:3;
        var entries=root.Element("FumenData")?.Elements().ToArray()??[];
        if(!special&&entries.Length>=4&&string.IsNullOrWhiteSpace(entries[3].Element("FumenFile")?.Element("path")?.Value))difficulty=Array.FindIndex(entries.Take(4).ToArray(),x=>!string.IsNullOrWhiteSpace(x.Element("FumenFile")?.Element("path")?.Value));
        return new(){Id=ChapterXml.Id(root),Name=ChapterXml.Name(root),Xml=xml,SourcePath=sourcePath,SourceHash=sourceHash,Challenge=new(){Difficulty=ChapterXml.Difficulties[Math.Max(0,difficulty)]}};
    }
}

public sealed class ScoreChallenge
{
    public bool Enabled { get; set; }
    public int TrophyId { get; set; } = 910001;
    public string TrophyName { get; set; } = "挑战达成";
    public string ScoreType { get; set; } = "TechnicalScore";
    public int Score { get; set; } = 970000;
    public string Difficulty { get; set; } = "Master";
    public bool RequireWin { get; set; }
    public int ClaimCost { get; set; }
    public string? SourcePath { get; set; }
    public string? SourceHash { get; set; }
    public string OriginalXml { get; set; } = "";
}

public sealed class ChapterXmlDraft
{
    public string Kind { get; set; } = "MemoryChapter";
    public string Xml { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? SourceHash { get; set; }
    public override string ToString() { try { var root=ChapterXml.Parse(Xml,Kind+"Data").Root!; return $"{Kind} · {ChapterXml.Id(root)} · {ChapterXml.Name(root)}"; } catch { return Kind+" · 未完成的 XML"; } }
}

public static class ChapterXml
{
    public static readonly XNamespace Xsi="http://www.w3.org/2001/XMLSchema-instance";
    public static readonly string[] Difficulties=["Basic","Advanced","Expert","Master","Lunatic"];
    public static XDocument Parse(string xml,string rootName) {
        using var text=new StringReader(xml);
        using var reader=XmlReader.Create(text,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8_000_000 });
        var document=XDocument.Load(reader);
        if(document.Root?.Name!=rootName)throw new InvalidDataException("根节点应为 "+rootName+"。");
        return document;
    }
    public static int Id(XElement? root) => int.TryParse((root?.Element("Name")??root)?.Element("id")?.Value,out int value)?value:0;
    public static string Name(XElement? root) => (root?.Element("Name")??root)?.Element("str")?.Value??"";
    public static int Int(XElement root,string field,int fallback=0)=>int.TryParse(root.Element(field)?.Value,out int value)?value:fallback;
    public static XElement Ref(string field,int id,string name)=>new(field,new XElement("id",id),new XElement("str",name));
    public static void Set(XElement root,string field,object value) { var old=root.Element(field); var replacement=value is XElement e?new XElement(e):new XElement(field,value); if(old is null)root.Add(replacement);else old.ReplaceWith(replacement); }
    public static XDocument New(string kind,int id,string name)=>new(new XDeclaration("1.0","utf-8",null),new XElement(kind+"Data",new XAttribute(XNamespace.Xmlns+"xsi",Xsi),new XElement("dataName",kind.ToLowerInvariant()+id.ToString("D6")),Ref("Name",id,name)));
    public static void ValidateStorage(IReadOnlyList<ChapterDraft>? chapters) {
        if(chapters is null||chapters.Count>100)throw new InvalidDataException("章节项目无效或超过 100 个章节。");
        if(chapters.Any(x=>x is null)||chapters.Select(x=>x.Key).Distinct().Count()!=chapters.Count)throw new InvalidDataException("章节项目标识重复或为空。");
        foreach(var c in chapters) {
            if(!Guid.TryParseExact(c.Key,"N",out _)||c.Songs is null||c.Experiments is null||c.ManagedChallengeMusicIds is null||c.ManagedChallengeMusicIds.Count>1000||c.Songs.Count>1000||c.Experiments.Count>100)throw new InvalidDataException("章节项目结构无效。");
            if(c.Songs.Any(x=>x is null||x.Challenge is null)||c.Experiments.Any(x=>x is null))throw new InvalidDataException("章节项目含空条目。");
        }
    }
}
