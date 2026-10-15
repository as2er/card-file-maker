using CardFileMaker;
using System.Text.Json;
using System.Xml.Linq;

public static class ChapterTests
{
    public static void Run(Action<string, Action> check)
    {
        check("章节项目随原项目保存并恢复，旧项目可继续打开", () =>
        {
            var property = typeof(CardProject).GetProperty("Chapters") ?? throw new Exception("缺少独立章节项目数据");
            var project = new CardProject();
            var bytes = ProjectCodec.Encode(project);
            var restored = ProjectCodec.Decode(bytes);
            if (property.GetValue(restored) is not System.Collections.IEnumerable) throw new Exception("章节默认值无效");
        });
        check("章节导出具有独立的校验与文件预览入口", () =>
        {
            var type = typeof(CardProject).Assembly.GetType("CardFileMaker.ChapterExporter");
            if (type?.GetMethod("Build") == null || type.GetMethod("Prepare") == null) throw new Exception("缺少章节导出器");
        });
        check("分数挑战输出领取条件，保留原曲未知字段",()=>{
            var (draft,catalog)=Fixture();var build=ChapterExporter.Build(draft,catalog);
            var trophy=build.Files.Single(x=>x.Kind=="Trophy").Xml;
            Require(trophy.Descendants("PlayResultValueType").Single().Value=="TechnicalScore","分数类型错误");
            Require(trophy.Descendants("Value").Single().Value=="970000","分数门槛丢失");
            var music=build.Files.Single(x=>x.Kind=="Music").Xml.Root!;
            Require(music.Element("PossessingFromTheBeginning")!.Value=="false","未关闭初始拥有");
            Require(music.Element("FutureField")!.Value=="保留","丢失未知字段");
            var purchase=build.Files.Single(x=>x.Kind=="Chapter").Xml.Descendants("PurchaseItem").Single();
            Require(purchase.Element("Cost")!.Value=="0"&&purchase.Element("PurchaseConditionType")!.Value=="Hide","领取条件未生成");
            Require(purchase.Descendants("ConditionItem").Single().Element("id")!.Value==draft.Songs[0].Challenge.TrophyId.ToString(),"称号引用错误");
        });
        check("章节ID碰撞、重复曲目、无谱面难度均拒绝导出",()=>{
            var (draft,catalog)=Fixture();catalog.Entries.Add(new("Chapter",draft.Id,"冲突","官方 A000","other.xml",draft.OriginalXml));
            Reject(()=>ChapterExporter.Build(draft,catalog));catalog.Entries.RemoveAt(catalog.Entries.Count-1);
            draft.Songs.Add(draft.Songs[0]);Reject(()=>ChapterExporter.Build(draft,catalog));draft.Songs.RemoveAt(1);
            draft.Songs[0].Challenge.Difficulty="Lunatic";Reject(()=>ChapterExporter.Build(draft,catalog));
        });
        check("实验Memory奖励不能绕过分数门槛",()=>{
            var (draft,catalog)=Fixture();draft.EnableExperiments=true;
            draft.Experiments.Add(new(){Kind="MemoryChapter",Xml=$"<MemoryChapterData><Name><id>{draft.Id}</id><str>测试</str></Name><memoryChapterType>End</memoryChapterType><MusicClearRewardDataList><MusicClearRewardData><addMylistMusicList><MusicNameStringID><StringID><id>912345</id><str>测试曲</str></StringID></MusicNameStringID></addMylistMusicList></MusicClearRewardData></MusicClearRewardDataList></MemoryChapterData>"});
            try {ChapterExporter.Build(draft,catalog);throw new Exception("通关奖励未被拦截");}catch(InvalidDataException e){Require(e.Message.Contains("绕过"),"失败原因不是绕过分数门槛");}
        });
        check("影片拒绝绝对路径、越界路径和MP4引用",()=>{
            foreach(var path in new[]{"../bad.usm","C:/bad.usm","sample.mp4"}) {
                var (draft,catalog)=Fixture();draft.EnableExperiments=true;
                draft.Experiments.Add(new(){Kind="Movie",Xml=$"<MovieData><Name><id>910050</id><str>测试</str></Name><MovieFileName><path>{path}</path></MovieFileName></MovieData>"});
                Reject(()=>ChapterExporter.Build(draft,catalog));
            }
        });
        check("导入挑战保留附加成绩条件及未知条件字段",()=>{
            var (draft,catalog,opt)=ExportFixture();
            var trophy=catalog.Find("Trophy",draft.Songs[0].Challenge.TrophyId)!;
            var xml=ChapterXml.Parse(trophy.Xml,"TrophyData");var rule=xml.Root!.Element("TrophyCondition")!;
            rule.Element("PlayResultFlag")!.Element("FullCombo")!.Value="true";
            rule.Element("TechnicalRank")!.Value="SS";rule.Add(new XElement("FutureCondition","保留"));xml.Save(trophy.Path);
            catalog=ChapterCatalog.Load("",opt);catalog.Entries.AddRange(Fixture().Item2.Entries);
            var imported=ChapterDraft.Import(draft.SourcePath!,catalog);Require(imported.Songs[0].Challenge.Enabled,"未识别可编辑的分数挑战");
            imported.Songs[0].Challenge.Score=990000;
            var output=ChapterExporter.Build(imported,catalog).Files.Single(x=>x.Kind=="Trophy").Xml;
            Require(output.Descendants("FullCombo").Single().Value=="true","导入再导出丢失 FullCombo 条件");
            Require(output.Descendants("TechnicalRank").Single().Value=="SS"&&output.Descendants("FutureCondition").Single().Value=="保留","丢失等级或未知附加条件");
            Require(output.Descendants("PlayResultValue").Single().Element("Value")!.Value=="990000","分数编辑未生效");
        });
        check("开启实验但未纳入已有Memory仍拒绝分数挑战导出",()=>{
            var (draft,catalog)=Fixture();draft.EnableExperiments=true;
            string memory=$"<MemoryChapterData><Name><id>{draft.Id}</id><str>测试</str></Name><memoryChapterType>End</memoryChapterType></MemoryChapterData>";
            catalog.Entries.Add(new("MemoryChapter",draft.Id,"测试","自制 opt","memory.xml",memory));
            Reject(()=>ChapterExporter.Build(draft,catalog));
            draft.Experiments.Add(new(){Kind="Movie",Xml="<MovieData><Name><id>910050</id><str>影片</str></Name><MovieFileName><path>intro.usm</path></MovieFileName></MovieData>"});
            Reject(()=>ChapterExporter.Build(draft,catalog));
            draft.Experiments.Add(new(){Kind="MemoryChapter",Xml=memory,SourcePath="memory.xml"});
            ChapterExporter.Build(draft,catalog);
        });
        check("移除已导出挑战歌曲清理其领取项并保留无关商店条目",()=>{
            var (draft,catalog,opt)=ExportFixture(true);
            var imported=ChapterDraft.Import(draft.SourcePath!,catalog);
            foreach(var version in new[]{draft,imported}) {
                int removed=version.Songs[0].Id;version.Songs.RemoveAt(0);
                var original=ChapterXml.Parse(version.OriginalXml,"ChapterData");
                original.Root!.Element("PurchaseItemList")!.Add(new XElement("PurchaseItem",new XElement("ItemType","Card"),ChapterXml.Ref("ItemName",123,"保留")));
                version.OriginalXml=original.ToString();
                var chapter=ChapterExporter.Build(version,catalog).Files.Single(x=>x.Kind=="Chapter").Xml;
                Require(!chapter.Descendants("PurchaseItem").Any(x=>x.Element("ItemType")?.Value=="Music"&&ChapterXml.Id(x.Element("ItemName"))==removed),"被移除曲目的旧领取项残留");
                Require(chapter.Descendants("PurchaseItem").Any(x=>x.Element("ItemType")?.Value=="Card"),"误删无关商店条目");
                Require(chapter.Descendants("PurchaseItem").Count(x=>x.Element("ItemType")?.Value=="Music")==1,"保留曲目的领取项错误");
            }
        });
        check("超出项目清单容量时编码立即给出可理解错误",()=>{
            var p=new CardProject{Chapters=[new(){OriginalXml=new string('x',8_000_001)}]};
            Reject(()=>ProjectCodec.Encode(p));
        });
        string game=Environment.GetEnvironmentVariable("CFM_TEST_GAME")??"",opt=Environment.GetEnvironmentVariable("CFM_TEST_OPT")??"";
        if(Directory.Exists(game)&&Directory.Exists(opt))check("真实游戏章节库完整读取并生成自制曲挑战XML",()=>{
            var catalog=ChapterCatalog.Load(game,opt);Require(catalog.Errors.Count==0,string.Join("\n",catalog.Errors));
            Require(catalog.Get("Story").Count>0&&catalog.Get("Chapter").Count>0&&catalog.Get("MemoryChapter").Count>0,"章节引用不完整");
            var reference=catalog.Get("Music").First(x=>x.Group=="自制 opt");
            var song=ChapterSong.From(reference);song.Challenge.Enabled=true;song.Challenge.TrophyId=catalog.NextId("Trophy");
            var draft=new ChapterDraft{Id=catalog.NextId("Chapter"),Songs=[song]};var result=ChapterExporter.Build(draft,catalog);
            Require(result.Files.Select(x=>x.Kind).Order().SequenceEqual(new[]{"Chapter","Music","Trophy"}),"挑战未输出章节、曲目和称号配置");
            var hash=AutoSaveStore.FileHash(reference.Path);Require(hash==song.SourceHash,"只读分析修改了源文件");
            Console.WriteLine($"真实引用：{catalog.Get("Chapter").Count} 章节，{catalog.Get("Music").Count} 曲目，{catalog.Get("Movie").Count} 影片。");
        });
        check("章节草稿、未完成实验XML和挑战设置可自动保存回读",()=>{
            var (draft,_)=Fixture();draft.Experiments.Add(new(){Kind="Movie",Xml="<MovieData>"});
            var p=new CardProject{Chapters=[draft]};var restored=ProjectCodec.Decode(ProjectCodec.Encode(p));
            Require(restored.Chapters.Single().Songs.Single().Challenge.Score==970000,"挑战设置丢失");
            Require(restored.Chapters.Single().Experiments.Single().Xml=="<MovieData>","未完成草稿丢失");
        });
        check("章节文件预览不写磁盘，覆盖前保存备份并检查外部变化",()=>{
            var (draft,catalog)=Fixture();string root=Path.Combine(AppContext.BaseDirectory,"chapter-workflow-tests",Guid.NewGuid().ToString("N"));string opt=Path.Combine(root,"A999");
            var plan=ChapterExporter.Prepare([draft],catalog,opt,Path.Combine(root,"save","chapter-backups"));
            Require(!Directory.Exists(root),"预览提前写盘");CardBatchExporter.Commit(plan);
            string chapter=plan.Files.First(x=>Path.GetFileName(x.Path)=="Chapter.xml").Path;string original=File.ReadAllText(chapter);
            var refreshed=ChapterCatalog.Load("",opt);refreshed.Entries.AddRange(catalog.Entries);
            var imported=ChapterDraft.Import(chapter,refreshed);
            Require(imported.Songs[0].Challenge.Enabled&&imported.Songs[0].Challenge.Score==970000,"导入已导出的章节丢失挑战设置");
            ChapterExporter.Prepare([imported],refreshed,opt,Path.Combine(root,"save","chapter-backups"));
            draft.Name="修改后的章节";
            var second=ChapterExporter.Prepare([draft],catalog,opt,Path.Combine(root,"save","chapter-backups"));
            Reject(()=>CardBatchExporter.Commit(second));CardBatchExporter.Commit(second,true);
            Require(File.Exists(Path.Combine(second.BackupDirectory,"backup-info.json")),"没有备份记录");
            Require(File.ReadAllText(Path.Combine(second.BackupDirectory,Path.GetRelativePath(opt,chapter)))==original,"备份与原文件不一致");
            var third=ChapterExporter.Prepare([draft],catalog,opt,Path.Combine(root,"save","chapter-backups"));File.AppendAllText(chapter,"\n<!--外部修改-->");Reject(()=>CardBatchExporter.Commit(third,true));
        });
    }
    static (ChapterDraft,ChapterCatalog,string) ExportFixture(bool secondSong=false) {
        var (draft,catalog)=Fixture();
        if(secondSong) {
            var xml=ChapterXml.Parse(draft.Songs[0].Xml,"MusicData");ChapterXml.Set(xml.Root!,"Name",ChapterXml.Ref("Name",912346,"第二首"));
            var song=ChapterSong.FromXml(xml.ToString());song.Challenge.Enabled=true;song.Challenge.TrophyId++;draft.Songs.Add(song);
        }
        string root=Path.Combine(AppContext.BaseDirectory,"chapter-regression-tests",Guid.NewGuid().ToString("N")),opt=Path.Combine(root,"A999");
        var plan=ChapterExporter.Prepare([draft],catalog,opt,Path.Combine(root,"save","chapter-backups"));CardBatchExporter.Commit(plan);ChapterExporter.RecordExport([draft],plan);
        var refreshed=ChapterCatalog.Load("",opt);refreshed.Entries.AddRange(catalog.Entries);return(draft,refreshed,opt);
    }
    public static (ChapterDraft,ChapterCatalog) Fixture() {
        var catalog=new ChapterCatalog();
        catalog.Entries.Add(new("Story",1,"第1章","官方 A000","story.xml","<StoryData><Name><id>1</id><str>第1章</str></Name><EventChapterFlag>false</EventChapterFlag></StoryData>"));
        catalog.Entries.Add(new("Chapter",1001,"原章节","官方 A000","chapter.xml","<ChapterData><Name><id>1001</id><str>原章节</str></Name><BackgroundName><id>100101</id><str>校舎前</str></BackgroundName><JewelGraphicName><id>100</id><str>宝石</str></JewelGraphicName></ChapterData>"));
        var music=MusicDocument.CreateNew();music.Set("Name/id","912345");music.Set("Name/str","测试曲");music.Xml.Root!.Element("FumenData")!.Elements().ElementAt(3).Element("FumenFile")!.Element("path")!.Value="test.ogkr";music.Xml.Root!.Add(new XElement("FutureField","保留"));
        var song=ChapterSong.FromXml(music.Xml.ToString());song.Challenge.Enabled=true;
        return (new ChapterDraft{Songs=[song]},catalog);
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or IOException){return;}throw new Exception("未拒绝无效操作");}
}
