using CardFileMaker;
using System.Xml.Linq;
using System.Text;
int failures=0, count=0;
void Check(string name, Action test) { count++; try { test(); Console.WriteLine("PASS "+name); } catch(Exception e) { failures++; Console.WriteLine("FAIL "+name+": "+e.Message); } }
void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
void Throws<T>(Action a) where T:Exception { try {a();} catch(T){return;} throw new Exception("未拒绝无效输入"); }
RgbaImage Make(int w,int h) { var b=new byte[w*h*4]; for(int y=0;y<h;y++) for(int x=0;x<w;x++){int o=(y*w+x)*4;b[o]=(byte)(x%251);b[o+1]=(byte)(y%251);b[o+2]=77;b[o+3]=(byte)(x%255);} return new(w,h,b); }
Check("八类路径独立记忆、跨实例恢复并回退失效目录",()=>{
 var type=typeof(CardProject).Assembly.GetType("CardFileMaker.PathHistoryStore")??throw new Exception("缺少路径历史存储");
 var purpose=typeof(CardProject).Assembly.GetType("CardFileMaker.PathPurpose")!;
 string root=Path.Combine(AppContext.BaseDirectory,"path-history-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
 object New()=>Activator.CreateInstance(type,new object?[]{root,null})!;
 var first=New();var names=Enum.GetNames(purpose);Assert(names.Length==8,"路径类别不是八类");
 foreach(string name in names){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);type.GetMethod("Remember")!.Invoke(first,[Enum.Parse(purpose,name),dir]);}
 var reopened=New();
 foreach(string name in names)Assert((string)type.GetMethod("Get")!.Invoke(reopened,[Enum.Parse(purpose,name),root])! ==Path.Combine(root,name),"独立历史未恢复："+name);
 string history=Path.Combine(root,"save","path-history.json");Assert(File.Exists(history),"未保存到程序save目录");
 var before=File.ReadAllBytes(history);type.GetMethod("Remember")!.Invoke(first,[Enum.Parse(purpose,names[0]),Path.Combine(root,"missing")]);Assert(before.SequenceEqual(File.ReadAllBytes(history)),"无效选择改变历史");
 File.WriteAllText(history,"{\"GameDirectory\":\"Z:/directory-that-does-not-exist\"}");
 Assert((string)type.GetMethod("Get")!.Invoke(New(),[Enum.Parse(purpose,"GameDirectory"),root])! ==root,"失效目录未回退");
 File.WriteAllText(history,"{broken");Assert((string)type.GetMethod("Get")!.Invoke(New(),[Enum.Parse(purpose,"ProjectOpen"),root])! ==root,"损坏历史阻止启动");
 // 无法创建 save 时不影响用户已经完成的文件操作。
 string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);File.WriteAllText(Path.Combine(blocked,"save"),"occupied");
 var readonlyStore=Activator.CreateInstance(type,new object?[]{blocked,null})!;
 type.GetMethod("Remember")!.Invoke(readonlyStore,[Enum.Parse(purpose,"ImageImport"),root]);
});
Check("同ID卡面按官方和自制来源隔离并支持官方回退",()=>{
 string root=Path.Combine(AppContext.BaseDirectory,"preview-source-tests",Guid.NewGuid().ToString("N"));
 string game=Path.Combine(root,"game"),opt=Path.Combine(root,"A999"),official=Path.Combine(game,"mu3_Data","StreamingAssets","GameData","A000");
 string shared=Path.Combine(game,"mu3_Data","StreamingAssets","assets"),custom=Path.Combine(opt,"assets");
 Directory.CreateDirectory(shared);Directory.CreateDirectory(custom);
 foreach(var package in new[]{official,opt})foreach(int id in new[]{930001,930002}){
  string card=Path.Combine(package,"card","card"+id);Directory.CreateDirectory(card);
  File.WriteAllText(Path.Combine(card,"Card.xml"),$"<CardData><Name><id>{id}</id><str>卡片{id}</str></Name><Attribute>Fire</Attribute></CardData>");
 }
 foreach(int id in new[]{930001,930002})File.WriteAllBytes(Path.Combine(shared,$"ui_card_{id}_s"),[1]);
 File.WriteAllBytes(Path.Combine(custom,"ui_card_930001_s"),[2]);
 var catalog=GameCatalog.Load(game,opt,scanCabs:false);
 var original=catalog.Cards.Single(x=>x.Id==930001&&x.Group=="官方游戏包");
 var local=catalog.Cards.Single(x=>x.Id==930001&&x.Group=="自制 opt");
 var fallback=catalog.Cards.Single(x=>x.Id==930002&&x.Group=="自制 opt");
 Assert(catalog.ResolveCardPreview(original)==Path.Combine(shared,"ui_card_930001_s"),"官方卡误用了自制opt的同ID图片");
 Assert(catalog.ResolveCardPreview(local)==Path.Combine(custom,"ui_card_930001_s"),"自制卡没有使用自己的图片");
 Assert(catalog.ResolveCardPreview(fallback)==Path.Combine(shared,"ui_card_930002_s"),"自制卡缺图时没有回退官方图片");
});
if (typeof(BundleCodec).Assembly.GetManifestResourceNames().Any(n=>n.EndsWith(".iconSmall.bin")))
{
Check("新卡资源含独立名称且上下方向和透明通道正确",()=>{
 var spec=new AssetSpec("x","测试","ui_card_icon_","_s",100,100,1,"UI_Card_Icon_");
 var im=Make(100,100); var cab="CAB-"+new string('a',32); var bytes=BundleCodec.Build(spec,900001,im,cab); var info=BundleCodec.Inspect(bytes);
 Assert(info.Cab==cab && info.BundleName=="ui_card_icon_900001_s","资源命名不一致");
 Assert(info.TextureName=="UI_Card_Icon_900001_S" && info.ContainerPath.EndsWith("ui_card_icon_900001_s.png"),"内部资产名不一致");
 Assert(info.Width==100 && info.Height==100 && info.Format==4 && info.Mips==1,"纹理参数不一致");
 Assert(im.Pixels.SequenceEqual(info.RgbaTopDown),"颜色／透明度／上下方向发生变化");
});
Check("战斗立绘生成完整的11级mipmap",()=>{var s=Assets.All.Single(x=>x.Key=="battle");var info=BundleCodec.Inspect(BundleCodec.Build(s,900001,Make(1024,1024),"CAB-"+new string('b',32)));Assert(info.Mips==11&&info.Width==1024,"mipmap链错误");});
Check("六种占位模板均以用户素材完整替换",()=>{
 foreach(var spec in Assets.All){
  var pixels=Make(spec.Width,spec.Height);var cab="CAB-"+new string('c',32);
  var result=BundleCodec.Inspect(BundleCodec.Build(spec,912345,pixels,cab));
  Assert(result.BundleName==spec.BundleName(912345)&&result.TextureName==spec.TextureName(912345),"占位名称残留");
  Assert(result.Width==spec.Width&&result.Height==spec.Height&&result.Mips==spec.Mips,"占位尺寸残留");
  Assert(result.RgbaTopDown.SequenceEqual(pixels.Pixels),"占位像素未被完整替换");
 }
});
}
else Console.WriteLine("SKIP 资源生成测试：未提供本地模板。");
Check("保留索引原记录和依赖再追加六项",()=>{
 var original=new List<ManifestEntry>{new(0,"base",[]),new(1,"child",[0])};var updated=ManifestCodec.Append(original,Assets.All.Select(x=>x.BundleName(900001)));
 var decoded=ManifestCodec.Read(ManifestCodec.Write(updated));Assert(decoded.Count==8 && decoded[1].Dependencies.SequenceEqual([0]),"原索引被损坏");Assert(decoded[7].Id==7,"新序号错误");
});
Check("拒绝重复索引名称",()=>Throws<InvalidDataException>(()=>ManifestCodec.Append([new(0,"same",[])],["SAME"])));
Check("拒绝截断的索引文件",()=>Throws<InvalidDataException>(()=>ManifestCodec.Read([1,0,0,0])));
Check("候选ID避开已有卡片和角色",()=>{var c=new CatalogResult();c.CardIds.Add(900001);c.CharaIds.Add(900002);Assert(c.NextCardId()==900002 && c.NextCharaId()==900001,"不同类别未独立分配");});

if(args.Length>1&&args[0]=="--workflow-integration") {
 if(TemplateResources.Missing.Count>0) throw new InvalidOperationException("文件集成测试需要完整模板。");
 CardProjectTests.RunFiles(args[1],Check);MusicTests.RunFiles(args[1],Check);
}
CardProjectTests.Run(Check);
if(typeof(MusicDocument).Assembly.GetManifestResourceNames().Any(n=>n.EndsWith(".Music.xml"))) MusicTests.Run(Check);
else {
 Check("缺少模板时提供可操作的错误信息",()=>{
  try { MusicDocument.CreateNew(); throw new Exception("缺少模板仍创建成功"); }
  catch(FileNotFoundException e) { Assert(e.Message.Contains("Music.xml") && e.Message.Contains("CfmTemplateDirectory"),"错误未说明模板与配置方法"); }
 });
 Console.WriteLine("SKIP Music模板测试：未提供本地模板。");
}
EditorWorkflowTests.Run(Check);
Console.WriteLine($"RESULT {count-failures}/{count} passed");
return failures==0 ? 0 : 1;
