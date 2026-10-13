using CardFileMaker;
public static class CardProjectTests
{
    public static void Run(Action<string,Action> check){
        check("后台保存快照独立于后续模式与裁剪修改",()=>{
            var method=typeof(CardDraft).GetMethod("Snapshot")??throw new Exception("缺少独立快照");
            var original=new CardDraft{QuickCrop=true,Crops=new(){["icon"]=new(){Png=[1,2,3],SourceName="原图.png",Transform=new(1,2,3)}}};
            var copy=(CardDraft)method.Invoke(original,null)!;
            original.QuickCrop=false;original.Crops["icon"].SourceName="改图.png";original.Crops["icon"].Transform=new(2,3,4);original.Crops.Clear();
            if(!copy.QuickCrop||copy.Crops.Count!=1||copy.Crops["icon"].SourceName!="原图.png"||copy.Crops["icon"].Transform.Scale!=1)throw new Exception("后续编辑改变后台快照");
        });

        check("快速裁剪模式随各卡独立保存且旧项目默认详细模式",()=>{
            var property=typeof(CardDraft).GetProperty("QuickCrop")??throw new Exception("缺少快速裁剪模式");
            var first=new CardDraft{CardId=900001,Crops=new(){["icon"]=new(){Png=[1,2,3],Transform=new(.75,-10,5)}}};
            property.SetValue(first,true);
            var restored=ProjectCodec.Decode(ProjectCodec.Encode(new(){Cards=[first,new(){CardId=900002}]}));
            if((bool?)property.GetValue(restored.Cards[0])!=true||(bool?)property.GetValue(restored.Cards[1])!=false||restored.Cards[0].Crops["icon"].Transform.X!=-10)throw new Exception("恢复丢失各卡模式或裁剪");
            var legacy=System.Text.Json.JsonSerializer.Deserialize<CardDraft>("{\"CardId\":900003}")!;
            if((bool?)property.GetValue(legacy)!=false)throw new Exception("旧项目未兼容详细模式");
        });
        check("UI资源注册保留原条目依赖且幂等",()=>{
 var method=typeof(Catalog).GetMethod("RegisterUiEntries")??throw new Exception("缺少UI资源注册功能");
 var original=new List<ManifestEntry>{new(4,"shared",[]),new(9,"ui_existing",[4])};
 var result=(List<ManifestEntry>)method.Invoke(null,new object[]{original,new[]{"ui_existing","UI_EXISTING","ui_new","ui_new"}})!;
 if(result.Count!=3||result[1].Id!=9||!result[1].Dependencies.SequenceEqual(new[]{4})||result[2].Id!=10)throw new Exception("注册破坏旧条目或新增重复");
 var again=(List<ManifestEntry>)method.Invoke(null,new object[]{result,new[]{"ui_new","ui_existing"}})!;
 if(!ManifestCodec.Write(result).SequenceEqual(ManifestCodec.Write(again)))throw new Exception("重复注册不幂等");
});
check("项目可保存XML草稿和角色引用方式",()=>{
            var p=new CardProject();
            var xml=typeof(CardProject).GetProperty("MusicXml")??throw new Exception("缺少Music草稿保存");
            xml.SetValue(p,"<MusicData><Name>");
            var mode=typeof(CardDraft).GetProperty("ReuseExistingChara")??throw new Exception("缺少角色引用方式");
            var card=new CardDraft();mode.SetValue(card,true);p.Cards.Add(card);
            var q=ProjectCodec.Decode(ProjectCodec.Encode(p));
            if((string?)xml.GetValue(q)!="<MusicData><Name>"||(bool?)mode.GetValue(q.Cards[0])!=true)throw new Exception("项目恢复丢失");
        });
        check("项目保存保留两张卡的独立裁剪和素材",()=>{
            var p=new CardProject{OptDirectory="sample-opt",Cards=[
                new(){CardId=900001,CardName="卡一",Crops=new(){["card"]=new(){Png=[1,2,3],SourceName="透明图.png",Transform=new(.6,-20,40)}}},
                new(){CardId=900002,CardName="卡二"}]};
            var q=ProjectCodec.Decode(ProjectCodec.Encode(p));
            if(q.Cards.Count!=2||q.Cards[0].Crops["card"].Transform.X!=-20||!q.Cards[0].Crops["card"].Png.SequenceEqual(new byte[]{1,2,3})||q.Cards[1].CardName!="卡二")throw new Exception("项目数据丢失");
        });
        check("项目拒绝重复卡片ID",()=>{try{ProjectCodec.Encode(new(){Cards=[new(),new()]});}catch(InvalidDataException){return;}throw new Exception("未拒绝重复ID");});
        check("项目拒绝未知格式",()=>{try{ProjectCodec.Encode(new(){FormatVersion=999});}catch(InvalidDataException){return;}throw new Exception("未拒绝未知格式");});
        check("批量导出要求至少一张卡",()=>{try{CardBatchExporter.Prepare(new("","",[]));}catch(ArgumentException){return;}throw new Exception("未拒绝空选择");});
    }

    public static void RunFiles(string root,Action<string,Action> check){
        root=Path.GetFullPath(root);
        if(Directory.Exists(root))throw new IOException("请使用新的测试目录："+root);
        Directory.CreateDirectory(root);
        RunRemovalChecks(root,check);
        check("五分钟自动保存恢复素材和未完成Music且不覆盖手动项目",()=>{
            var p=new CardProject{MusicXml="<MusicData><Name>",MusicSourcePath="source.xml",Cards=[new(){Crops=new(){["card"]=new(){Png=[1,2,3],Transform=new(1,0,0)}}}]};
            string save=AutoSaveStore.DirectoryFor(root);Directory.CreateDirectory(save);
            string manual=Path.Combine(save,"manual.cfmproj");File.WriteAllText(manual,"保留手动项目");
            string target=AutoSaveStore.Save(p,save);
            File.WriteAllText(target+".pending","旧中断样本");p.MusicXml="<MusicData><Name>第二次";AutoSaveStore.Save(p,save);
            var q=ProjectCodec.Decode(File.ReadAllBytes(target));
            if(q.MusicXml!=p.MusicXml||q.ProjectKey!=p.ProjectKey||!q.Cards[0].Crops["card"].Png.SequenceEqual(new byte[]{1,2,3})||File.ReadAllText(manual)!="保留手动项目")throw new Exception("自动保存数据不完整");
            if(AutoSaveStore.Interval!=TimeSpan.FromMinutes(5)||!File.Exists(target+".pending")||Directory.GetFiles(save,"*.pending-*").Length!=0)throw new Exception("自动保存周期或原子写入异常");
        });
        check("手动保存校验后原子覆盖并备份且拒绝过期确认",()=>{
            string path=Path.Combine(root,"manual-safe.cfmproj");
            var first=new CardProject{MusicXml="第一版"};var bytes=ProjectCodec.Encode(first);
            AutoSaveStore.WriteProjectFile(path,bytes,null,path+".pending-1",null);
            var old=File.ReadAllBytes(path);var hash=AutoSaveStore.FileHash(path);
            first.MusicXml="第二版";AutoSaveStore.WriteProjectFile(path,ProjectCodec.Encode(first),hash,path+".pending-2",path+".backup");
            if(ProjectCodec.Decode(File.ReadAllBytes(path)).MusicXml!="第二版"||!File.ReadAllBytes(path+".backup").SequenceEqual(old))throw new Exception("保存或备份不完整");
            bool rejected=false;try{AutoSaveStore.WriteProjectFile(path,bytes,hash,path+".pending-3",null);}catch(IOException){rejected=true;}
            if(!rejected||ProjectCodec.Decode(File.ReadAllBytes(path)).MusicXml!="第二版")throw new Exception("过期确认破坏项目");
        });
        check("官方资源索引无损读取和回写",()=>{
            foreach(var p in (Environment.GetEnvironmentVariable("CFM_TEST_MANIFESTS")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)){
                var b=File.ReadAllBytes(p);if(!ManifestCodec.Write(ManifestCodec.Read(b)).SequenceEqual(b))throw new Exception("官方索引回写不一致："+p);
            }
        });
        string game=Environment.GetEnvironmentVariable("CFM_TEST_GAME")??throw new InvalidOperationException("文件集成测试需要 CFM_TEST_GAME。");
        string opt=Path.Combine(root,"A990"),assets=Path.Combine(opt,"assets");Directory.CreateDirectory(assets);
        var spec=Assets.All.Single(x=>x.Key=="iconSmall");byte[] pixels=new byte[40000];for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
        string first=spec.BundleName(910001),second=spec.BundleName(910002);
        foreach(var id in new[]{910001,910002})File.WriteAllBytes(Path.Combine(assets,spec.BundleName(id)),BundleCodec.Build(spec,id,new RgbaImage(100,100,pixels),"CAB-"+Guid.NewGuid().ToString("N")));
        string index=Path.Combine(assets,"assets.bytes");
        File.WriteAllBytes(Path.Combine(assets,"shared"),[1,2,3]);
        var original=ManifestCodec.Write(new List<ManifestEntry>{new(4,"shared",[]),new(9,first,[4])});File.WriteAllBytes(index,original);
        check("UI注册先预览后备份覆盖并保留依赖",()=>{
            var plan=Catalog.PrepareUiRegistration(game,opt);
            if(!File.ReadAllBytes(index).SequenceEqual(original))throw new Exception("预览发生写入");
            bool rejected=false;try{CardBatchExporter.Commit(plan);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("未确认就覆盖");
            CardBatchExporter.Commit(plan,true);
            if(Directory.Exists(Path.Combine(opt,"card-maker-backups"))||!PathSafety.Within(plan.BackupDirectory,AutoSaveStore.DirectoryFor(AppContext.BaseDirectory)))throw new Exception("备份仍写到opt");
            using var backupInfo=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(plan.BackupDirectory,"backup-info.json")));
            if(backupInfo.RootElement.GetProperty("OptDirectory").GetString()!=opt||backupInfo.RootElement.GetProperty("Files")[0].GetProperty("OriginalPath").GetString()!=index)throw new Exception("备份JSON缺少来源映射");
            var saved=ManifestCodec.Read(File.ReadAllBytes(index));
            if(saved.Count!=3||saved[2].Name!=second||saved[2].Id!=10||!saved[1].Dependencies.SequenceEqual(new[]{4}))throw new Exception("注册结果错误");
            if(!File.ReadAllBytes(Path.Combine(plan.BackupDirectory,"assets","assets.bytes")).SequenceEqual(original))throw new Exception("没有保留原索引备份");
            var again=Catalog.PrepareUiRegistration(game,opt);if(!again.Files[0].Bytes.SequenceEqual(File.ReadAllBytes(index)))throw new Exception("重复注册改变索引");
        });
        check("UI注册确认后索引变化则拒绝覆盖",()=>{
            var plan=Catalog.PrepareUiRegistration(game,opt);File.AppendAllText(index,"changed");
            bool rejected=false;try{CardBatchExporter.Commit(plan,true);}catch(IOException){rejected=true;}
            if(!rejected)throw new Exception("覆盖了预览后改变的索引");
            File.WriteAllBytes(index,plan.Files[0].Bytes);
        });
        check("引用同一已有角色的两张卡可导出且不生成chara",()=>{
            var images=Assets.All.ToDictionary(s=>s.Key,s=>{
                byte[] b=new byte[s.Width*s.Height*4];
                for(int y=0;y<s.Height;y++)for(int x=0;x<(s.Key=="battle"?768:s.Width);x++){int i=(y*s.Width+x)*4;b[i]=127;b[i+3]=255;}
                return new RgbaImage(s.Width,s.Height,b);
            });
            var a=new CardDraft{CardId=920101,CharaId=1001,CharacterName="柏木 咲姫",ReuseExistingChara=true};
            var b=new CardDraft{CardId=920102,CharaId=1001,CharacterName="柏木 咲姫",ReuseExistingChara=true};
            var plan=CardBatchExporter.Prepare(new(game,opt,[new(a,images),new(b,images)]));
            if(plan.Files.Any(x=>Path.GetFileName(x.Path)=="Chara.xml"))throw new Exception("引用角色仍写入Chara.xml");
            foreach(var f in plan.Files.Where(x=>Path.GetFileName(x.Path)=="Card.xml")){
                var xml=System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(f.Bytes).TrimStart('\uFEFF'));
                if(xml.Root!.Element("CharaID")!.Element("id")!.Value!="1001"||xml.Root.Element("SkillID")!.Element("str")!.Value=="100005")throw new Exception("角色或技能名称错误");
            }
            CardBatchExporter.Commit(plan,true);
            if(Directory.Exists(Path.Combine(opt,"chara")))throw new Exception("创建了已有角色的目录");
            var c=new CardDraft{CardId=920103,CharaId=920103};
            var newPlan=CardBatchExporter.Prepare(new(game,opt,[new(c,images)]));
            if(!newPlan.Files.Any(x=>Path.GetFileName(x.Path)=="Chara.xml"))throw new Exception("新建空角色不再输出chara");
        });
    }
    static object CallRemoval(string method,params object?[] args){
        var type=typeof(CardDraft).Assembly.GetType("CardFileMaker.OptCardRemoval")??throw new Exception("缺少包内卡片管理");
        try{return type.GetMethod(method)!.Invoke(null,args)!;}catch(System.Reflection.TargetInvocationException ex){throw ex.InnerException!;}
    }
    static void RunRemovalChecks(string root,Action<string,Action> check){
        string NewFixture(string name){
            string opt=Path.Combine(root,name,"A991");Directory.CreateDirectory(Path.Combine(opt,"assets"));
            foreach(int id in new[]{930001,930002}){
                var dir=Path.Combine(opt,"card",$"card{id}");Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"Card.xml"),$"<CardData><Name><id>{id}</id><str>测试{id}</str></Name><CharaID><id>{id}</id><str>角色</str></CharaID><Attribute>Fire</Attribute><Rarity>R</Rarity></CardData>");
                foreach(var spec in Assets.All)File.WriteAllBytes(Path.Combine(opt,"assets",spec.BundleName(id)),[1,2,3]);
            }
            File.WriteAllBytes(Path.Combine(opt,"assets","assets.bytes"),ManifestCodec.Write(ManifestCodec.Append([],new[]{930001,930002}.SelectMany(id=>Assets.All.Select(s=>s.BundleName(id))))));
            return opt;
        }
        check("包内未登记卡片仍全部读取",()=>{
            string opt=NewFixture("read-opt");var entries=(System.Collections.IEnumerable)CallRemoval("Load",opt);
            if(entries.Cast<object>().Count()!=2)throw new Exception("未登记的卡片丢失");
        });
        check("删除预览不写入且确认后备份卡片资源并更新索引",()=>{
            string opt=NewFixture("delete-opt"),backupRoot=Path.Combine(root,"save","card-maker-backups");
            var original=Directory.GetFiles(opt,"*",SearchOption.AllDirectories).ToDictionary(x=>x,x=>File.ReadAllBytes(x));
            var plan=CallRemoval("Prepare","",opt,new[]{930001},backupRoot);
            if(original.Any(x=>!File.ReadAllBytes(x.Key).SequenceEqual(x.Value)))throw new Exception("预览写入了原包");
            bool rejected=false;try{CallRemoval("Commit",plan,false);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("未确认即删除");
            CallRemoval("Commit",plan,true);
            if(File.Exists(Path.Combine(opt,"card","card930001","Card.xml"))||Assets.All.Any(s=>File.Exists(Path.Combine(opt,"assets",s.BundleName(930001)))))throw new Exception("删除不完整");
            if(!File.Exists(Path.Combine(opt,"card","card930002","Card.xml")))throw new Exception("误删其它卡");
            var entries=ManifestCodec.Read(File.ReadAllBytes(Path.Combine(opt,"assets","assets.bytes")));if(entries.Count!=6||entries.Any(x=>x.Name.Contains("930001")))throw new Exception("索引残留");
            string backup=(string)plan.GetType().GetProperty("BackupDirectory")!.GetValue(plan)!;
            foreach(var p in original.Where(x=>x.Key.Contains("930001")||Path.GetFileName(x.Key)=="assets.bytes"))if(!File.ReadAllBytes(Path.Combine(backup,Path.GetRelativePath(opt,p.Key))).SequenceEqual(p.Value))throw new Exception("备份数据丢失");
            if(!File.Exists(Path.Combine(backup,"backup-info.json")))throw new Exception("没有备份映射");
        });
        check("删除阻止歌曲引用和索引依赖且拒绝预览后变化",()=>{
            string opt=NewFixture("delete-conflicts"),backups=Path.Combine(root,"save","card-maker-backups");
            var plan=CallRemoval("Prepare","",opt,new[]{930001},backups);
            string songDir=Path.Combine(opt,"music","music1");Directory.CreateDirectory(songDir);var song=Path.Combine(songDir,"Music.xml");File.WriteAllText(song,"<MusicData><BossCard><id>930001</id></BossCard></MusicData>");
            bool rejected=false;try{CallRemoval("Commit",plan,true);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("没有重新检查引用");
            rejected=false;try{CallRemoval("Prepare","",opt,new[]{930001},backups);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("歌曲引用未阻止删除");
            File.WriteAllText(song,"<MusicData><BossCard><id>930002</id></BossCard></MusicData>");
            var index=Path.Combine(opt,"assets","assets.bytes");var entries=ManifestCodec.Read(File.ReadAllBytes(index));entries.Add(new(99,"shared",[0]));File.WriteAllBytes(index,ManifestCodec.Write(entries));
            rejected=false;try{CallRemoval("Prepare","",opt,new[]{930001},backups);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("外部资源依赖未阻止删除");
        });
        check("删除阻止共享角色且允许独占二维角色一起备份",()=>{
            string opt=NewFixture("delete-chara"),backups=Path.Combine(root,"save","card-maker-backups");
            string dir=Path.Combine(opt,"chara","chara930001");Directory.CreateDirectory(dir);string chara=Path.Combine(dir,"Chara.xml");
            File.WriteAllText(chara,"<CharaData><Name><id>930001</id></Name><GraphicCardData><id>930001</id></GraphicCardData><IsModelless>true</IsModelless></CharaData>");
            string other=Path.Combine(opt,"card","card930002","Card.xml");var doc=Catalog.ReadXml(other);doc.Root!.Element("CharaID")!.Element("id")!.Value="930001";doc.Save(other);
            bool rejected=false;try{CallRemoval("Prepare","",opt,new[]{930001},backups);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("共享角色未拦截");
            doc.Root.Element("CharaID")!.Element("id")!.Value="930002";doc.Save(other);
            var plan=CallRemoval("Prepare","",opt,new[]{930001},backups);CallRemoval("Commit",plan,true);if(File.Exists(chara))throw new Exception("独占二维角色仍引用已删除卡片");
        });
        check("批量删除保留无关文件且不采用工具索引中的越界路径",()=>{
            string opt=NewFixture("delete-boundary"),backups=Path.Combine(root,"save","card-maker-backups");
            string outside=Path.Combine(Path.GetDirectoryName(opt)!,"outside.txt");File.WriteAllText(outside,"外部文件");
            string extra=Path.Combine(opt,"card","card930001","notes.txt");File.WriteAllText(extra,"保留备注");
            string index=Path.Combine(opt,"card-maker-index.json");File.WriteAllText(index,"{\"Future\":\"保留字段\",\"Cards\":{\"930001\":{\"Hashes\":{\"../outside.txt\":\"任意值\"}},\"930002\":{},\"777777\":{\"Key\":\"保留记录\"}}}");
            var plan=CallRemoval("Prepare","",opt,new[]{930001,930002},backups);CallRemoval("Commit",plan,true);
            if(File.ReadAllText(outside)!="外部文件"||File.ReadAllText(extra)!="保留备注")throw new Exception("越界或无关文件被修改");
            using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(index));
            if(json.RootElement.GetProperty("Future").GetString()!="保留字段"||json.RootElement.GetProperty("Cards").EnumerateObject().Count()!=1||!json.RootElement.GetProperty("Cards").TryGetProperty("777777",out _))throw new Exception("索引更新丢失无关内容");
        });
        check("卡片资源在预览后变化时完整拒绝删除",()=>{
            string opt=NewFixture("delete-changed"),backups=Path.Combine(root,"save","card-maker-backups");var plan=CallRemoval("Prepare","",opt,new[]{930001},backups);
            string resource=Path.Combine(opt,"assets",Assets.All[0].BundleName(930001));File.WriteAllBytes(resource,[7,8,9]);
            bool rejected=false;try{CallRemoval("Commit",plan,true);}catch(IOException){rejected=true;}
            if(!rejected||!File.ReadAllBytes(resource).SequenceEqual(new byte[]{7,8,9})||!File.Exists(Path.Combine(opt,"card","card930001","Card.xml")))throw new Exception("修改后的卡片仍被删除");
        });
    }
}
