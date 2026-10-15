using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace CardFileMaker;

public sealed class ChapterEditorPanel : UserControl
{
    readonly Action<string> log;
    readonly PathHistoryStore history;
    readonly Func<(string Xml,string? Path,string? Hash)> currentMusic;
    readonly Func<(string Game,string Opt)> directories;
    readonly ListBox library=new(){SelectionMode=SelectionMode.Extended,Background=Theme.Dark,Foreground=Brushes.White,BorderThickness=new Thickness(0)};
    readonly TabControl pages=new(){Background=Theme.Background,BorderThickness=new Thickness(0)};
    readonly TextBlock catalogStatus=Theme.Hint("选择本页后读取章节与曲目引用。"),report=Theme.Text("先创建或导入章节，再添加歌曲。",12,Theme.Muted);
    readonly ComboBox installed=Choice(),songPicker=Choice();
    readonly List<ChapterDraft> drafts=[];
    readonly TextBox search=NewTextBox("");
    ChapterDraft? current;
    ChapterCatalog catalog=new();
    bool loading,busy;
    string loadedGame="",loadedOpt="";
    public Action? Changed { get; set; }
    public Action<bool>? BusyChanged { get; set; }
    public ChapterEditorPanel(Action<string> log,PathHistoryStore history,Func<(string Xml,string? Path,string? Hash)> currentMusic,Func<(string Game,string Opt)> directories)
    {
        this.log=log;this.history=history;this.currentMusic=currentMusic;this.directories=directories;Foreground=Brushes.White;Background=Theme.Background;
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(290)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(14)});grid.ColumnDefinitions.Add(new());Content=grid;
        var left=new DockPanel();var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);left.Children.Add(top);
        top.Children.Add(Theme.Text("章节库",19));top.Children.Add(Theme.Hint("项目草稿 · Ctrl / Shift 多选导出"));
        top.Children.Add(Buttons(("新章节",NewChapter),("复制",Duplicate),("移除草稿",Remove)));
        top.Children.Add(Buttons(("导入 Chapter.xml",ImportChapter),("刷新引用",()=>_=Reload(true))));
        installed.ToolTip="选择游戏或 AXXX 内已有章节，再读取为草稿";top.Children.Add(installed);top.Children.Add(Button("读取所选章节",()=>{if(installed.SelectedItem is ChapterReference r)Import(r.Path);}));
        top.Children.Add(catalogStatus);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);left.Children.Add(bottom);
        bottom.Children.Add(Button("导出选中章节到 AXXX",()=>_=Export(),true));bottom.Children.Add(Theme.Hint("配置随顶部“保存项目”保存；每五分钟自动保存。覆盖备份放在 save/chapter-backups。"));
        left.Children.Add(library);grid.Children.Add(Theme.Panel(left));
        var right=new DockPanel();Grid.SetColumn(right,2);grid.Children.Add(right);
        var heading=new StackPanel();heading.Children.Add(Theme.Text("章节与挑战",22));heading.Children.Add(Theme.Hint("纯 AXXX · 分数达标后手动领取 · 隐藏曲与影片为实验配置"));DockPanel.SetDock(heading,Dock.Top);right.Children.Add(heading);right.Children.Add(pages);
        library.SelectionChanged+=(_,e)=>{if(!loading){current=e.AddedItems.OfType<ChapterDraft>().LastOrDefault()??library.SelectedItem as ChapterDraft;Render();}};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)_=Reload(false);};Render();
    }
    public List<ChapterDraft> Snapshot()=>drafts.Select(x=>x.Snapshot()).ToList();
    public void Restore(IEnumerable<ChapterDraft> values) {
        loading=true;drafts.Clear();drafts.AddRange(values.Select(x=>x.Snapshot()));library.ItemsSource=null;library.ItemsSource=drafts;current=drafts.FirstOrDefault();library.SelectedItem=current;loading=false;Render();
    }
    public void InvalidateCatalog() { loadedGame="\0";if(IsVisible)_=Reload(false); }
    public async Task Reload(bool force) {
        if(busy)return;var (game,opt)=directories();if(!force&&game==loadedGame&&opt==loadedOpt)return;
        busy=true;catalogStatus.Text="正在读取章节、曲目、称号和影片……";
        try {
            var next=await Task.Run(()=>ChapterCatalog.Load(game,opt));
            if(directories()!=(game,opt)) { loadedGame="\0";catalogStatus.Text="目录已变化，请刷新引用。";return; }
            catalog=next;loadedGame=game;loadedOpt=opt;installed.ItemsSource=catalog.Get("Chapter");
            catalogStatus.Text=$"{catalog.Get("Chapter").Count} 个章节 · {catalog.Get("Music").Count} 首曲目"+(catalog.Errors.Count==0?"":"\n有读取错误，导出前须修复。");Render();
        } catch(Exception e) { catalog=new();loadedGame="\0";catalogStatus.Text="读取失败："+e.Message; }
        finally { busy=false; }
    }
    void Touch(){if(!loading)Changed?.Invoke();}
    void RefreshLibrary() { loading=true;var selected=library.SelectedItems.Cast<ChapterDraft>().ToArray();library.Items.Refresh();foreach(var d in selected)if(!library.SelectedItems.Contains(d))library.SelectedItems.Add(d);loading=false; }
    void NewChapter() {
        var draft=new ChapterDraft{Id=catalog.NextId("Chapter",drafts.Select(x=>x.Id))};
        if(catalog.Get("Story").FirstOrDefault(x=>x.Id>0&&ChapterXml.Parse(x.Xml,"StoryData").Root!.Element("EventChapterFlag")?.Value=="false") is { } story){draft.StoryId=story.Id;draft.StoryName=story.Name;}
        AddDraft(draft);
    }
    void AddDraft(ChapterDraft draft) { loading=true;drafts.Add(draft);library.ItemsSource=null;library.ItemsSource=drafts;library.SelectedItem=draft;current=draft;loading=false;Render();Touch(); }
    void Duplicate() {
        if(current is null)return;var copy=current.Snapshot();copy.Key=Guid.NewGuid().ToString("N");copy.Id=catalog.NextId("Chapter",drafts.Select(x=>x.Id));copy.Name+=" 副本";copy.SourcePath=null;copy.SourceHash=null;
        var used=drafts.SelectMany(x=>x.Songs).Select(x=>x.Challenge.TrophyId).ToList();
        foreach(var song in copy.Songs){song.Challenge.TrophyId=catalog.NextId("Trophy",used);used.Add(song.Challenge.TrophyId);song.Challenge.SourcePath=null;song.Challenge.SourceHash=null;}
        copy.EnableExperiments=false;copy.Experiments.Clear();if(copy.CreateStory){copy.StoryId=catalog.NextId("Story",drafts.Select(x=>x.StoryId));copy.StorySourcePath=null;copy.StorySourceHash=null;}AddDraft(copy);
    }
    void Remove() { var selected=library.SelectedItems.Cast<ChapterDraft>().ToArray();foreach(var d in selected)drafts.Remove(d);Restore(drafts.ToArray());Touch(); }
    void ImportChapter() { var d=new OpenFileDialog{Title="导入章节 XML",Filter="Chapter.xml|Chapter.xml|XML|*.xml",InitialDirectory=history.Get(PathPurpose.ChapterImport)};if(d.ShowDialog()==true){Import(d.FileName);history.Remember(PathPurpose.ChapterImport,Path.GetDirectoryName(d.FileName)!);} }
    void Import(string path) { var draft=ChapterDraft.Import(path,catalog);if(drafts.Any(x=>x.Id==draft.Id))throw new InvalidDataException("项目已有同 ID 章节，请选择已有草稿或更改 ID。");AddDraft(draft); }
    void Render() {
        int selected=Math.Max(0,pages.SelectedIndex);foreach(var element in new FrameworkElement[]{search,songPicker,report})if(element.Parent is Panel parent)parent.Children.Remove(element);pages.Items.Clear();
        if(current is null){pages.Items.Add(Page("使用说明",Theme.Hint("左侧创建章节或导入 Chapter.xml。\n现有卡片和 Music.xml 功能仍在各自标签页中。")));return;}
        pages.Items.Add(Page("章节设置",BuildChapter()));pages.Items.Add(Page("曲目与分数挑战",BuildSongs()));pages.Items.Add(Page("隐藏曲与影片 · 实验",BuildExperiments()));
        var checks=new StackPanel();checks.Children.Add(Buttons(("检查配置与导出预览",Preview),("检查全解补丁",()=>_=InspectPatches())));checks.Children.Add(report);pages.Items.Add(Page("检查与导出",checks));pages.SelectedIndex=Math.Min(selected,pages.Items.Count-1);
    }
    UIElement BuildChapter() {
        var c=current!;var form=new StackPanel();form.Children.Add(Theme.Hint("普通本地章节默认可选；自定义 Story 的部分标题仍由游戏显示为“第 N 章”。"));
        form.Children.Add(Field("章节 ID",Number(c.Id,v=>c.Id=v)));form.Children.Add(Button("分配未占用的章节 ID",()=>{c.Id=catalog.NextId("Chapter",drafts.Where(x=>x!=c).Select(x=>x.Id));Touch();Render();RefreshLibrary();}));
        form.Children.Add(Field("章节名称",Text(c.Name,v=>c.Name=v)));form.Children.Add(Field("显示序号",Number(c.DisplayNumber,v=>c.DisplayNumber=v)));form.Children.Add(Field("排序号",Number(c.SortOrder,v=>c.SortOrder=v)));
        form.Children.Add(Field("新建 Story 分类",Bool(c.CreateStory,v=>{c.CreateStory=v;if(v){c.StoryId=catalog.NextId("Story",drafts.Where(x=>x!=c).Select(x=>x.StoryId));c.StorySourcePath=null;c.StorySourceHash=null;}Render();})));
        if(c.CreateStory){form.Children.Add(Field("Story ID",Number(c.StoryId,v=>c.StoryId=v)));form.Children.Add(Field("Story 名称",Text(c.StoryName,v=>c.StoryName=v)));}
        else {var choice=Choice();var items=catalog.Get("Story");choice.ItemsSource=items;choice.SelectedItem=items.FirstOrDefault(x=>x.Id==c.StoryId);choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is ChapterReference r){c.StoryId=r.Id;c.StoryName=r.Name;Touch();}};form.Children.Add(Field("所属 Story · ID / 名称 / 来源",choice));}
        form.Children.Add(Field("章节选曲背景",Reference("BackgroundName",c.BackgroundId,r=>{c.BackgroundId=r.Id;c.BackgroundName=r.Name;})));
        form.Children.Add(Field("章节宝石图标",Reference("JewelGraphicName",c.JewelId,r=>{c.JewelId=r.Id;c.JewelName=r.Name;})));
        form.Children.Add(Theme.Hint("此处是章节选曲背景。游玩舞台仍在 Music.xml 页面选择。"));return form;
    }
    UIElement BuildSongs() {
        var c=current!;var panel=new StackPanel();panel.Children.Add(Theme.Text("章节曲目",17));
        search.TextChanged-=SearchChanged;search.TextChanged+=SearchChanged;search.ToolTip="按 ID 或曲名搜索";panel.Children.Add(Field("搜索官方／自制曲目",search));FilterSongs();panel.Children.Add(songPicker);
        panel.Children.Add(Buttons(("添加所选曲目",()=>{if(songPicker.SelectedItem is ChapterReference r)AddSong(ChapterSong.From(r));}),("导入 Music.xml",ImportSong),("添加／更新当前 Music 草稿",()=>{var value=currentMusic();AddSong(ChapterSong.FromXml(value.Xml,value.Path,value.Hash),true);})));
        var list=new ListBox{ItemsSource=c.Songs,Height=145,Background=Theme.Dark,Foreground=Brushes.White,BorderThickness=new Thickness(0),Margin=new Thickness(0,8,0,8)};panel.Children.Add(list);
        var details=new StackPanel();
        panel.Children.Add(Buttons(("上移",()=>Move(-1)),("下移",()=>Move(1)),("移除曲目",()=>{if(list.SelectedItem is ChapterSong s){c.Songs.Remove(s);Touch();Render();}})));
        panel.Children.Add(details);list.SelectionChanged+=(_,_)=>RenderChallenge();list.SelectedIndex=c.Songs.Count==0?-1:0;
        return panel;
        void Move(int delta){if(list.SelectedItem is not ChapterSong s)return;int index=c.Songs.IndexOf(s),target=index+delta;if(target<0||target>=c.Songs.Count)return;c.Songs.RemoveAt(index);c.Songs.Insert(target,s);Touch();list.Items.Refresh();list.SelectedItem=s;}
        void RenderChallenge() {
            details.Children.Clear();if(list.SelectedItem is not ChapterSong song)return;var score=song.Challenge;
            details.Children.Add(Field("启用分数挑战 → 达标后手动领取",Bool(score.Enabled,v=>{score.Enabled=v;list.Items.Refresh();RenderChallenge();})));
            if(!score.Enabled){details.Children.Add(Theme.Hint("普通曲目：保留原有开放条件。分数挑战会令歌曲初始不拥有，并生成称号和领取项。"));return;}
            if(!string.IsNullOrWhiteSpace(score.OriginalXml))details.Children.Add(Theme.Hint("导入称号中的附加条件（如 FullCombo、等级要求）会原样保留；本页只修改分数、难度及 Boss 判定。"));
            details.Children.Add(Field("称号 ID",Number(score.TrophyId,v=>score.TrophyId=v)));details.Children.Add(Field("称号名称",Text(score.TrophyName,v=>score.TrophyName=v)));
            details.Children.Add(Field("判定难度",EnumChoice(score.Difficulty,ChapterXml.Difficulties,v=>score.Difficulty=v)));
            details.Children.Add(Theme.Hint("Basic / Advanced / Expert / Master 对应前四栏；Lunatic 对应第五栏，也用于 Re:MASTER。"));
            details.Children.Add(Field("分数类型",EnumChoice(score.ScoreType,["TechnicalScore","BattleScore"],v=>score.ScoreType=v)));
            details.Children.Add(Theme.Hint("TechnicalScore：技术分；BattleScore：战斗分。按本次完成游玩的成绩判定。"));
            details.Children.Add(Field("最低分数（包含该分数）",Number(score.Score,v=>score.Score=v)));details.Children.Add(Field("同时要求击败 Boss",Bool(score.RequireWin,v=>score.RequireWin=v)));details.Children.Add(Field("领取所需章节宝石 · 0 为免费",Number(score.ClaimCost,v=>score.ClaimCost=v)));
            details.Children.Add(Theme.Hint("流程：章节内挑战 → 达标获得称号 → 章节商店出现领取项 → 手动领取加入 Mylist。跨登录保留由服务器决定。"));
        }
    }
    void SearchChanged(object sender,TextChangedEventArgs e)=>FilterSongs();
    void FilterSongs(){string query=search.Text.Trim();songPicker.ItemsSource=catalog.Get("Music").Where(x=>query==""||x.Id.ToString().Contains(query)||x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();}
    void AddSong(ChapterSong song,bool update=false) {
        if(current is null)return;if(current.Songs.FirstOrDefault(x=>x.Id==song.Id) is { } existing) {
            if(!update)throw new InvalidDataException("章节已经包含这首曲目。");existing.Name=song.Name;existing.Xml=song.Xml;existing.SourcePath=song.SourcePath;existing.SourceHash=song.SourceHash;Touch();Render();return;
        }
        song.Challenge.TrophyId=catalog.NextId("Trophy",drafts.SelectMany(x=>x.Songs).Select(x=>x.Challenge.TrophyId));song.Challenge.TrophyName=song.Name+" 挑战达成";current.Songs.Add(song);Touch();Render();
    }
    void ImportSong(){var d=new OpenFileDialog{Title="导入章节曲目",Filter="Music.xml|Music.xml|XML|*.xml",InitialDirectory=history.Get(PathPurpose.ChapterMusicImport)};if(d.ShowDialog()==true){AddSong(ChapterSong.FromXml(Catalog.ReadXml(d.FileName).ToString(),d.FileName,AutoSaveStore.FileHash(d.FileName)));history.Remember(PathPurpose.ChapterMusicImport,Path.GetDirectoryName(d.FileName)!);}}
    UIElement BuildExperiments() {
        var c=current!;var panel=new StackPanel();panel.Children.Add(Theme.Hint("实验配置：??? 隐藏显示、影片触发依赖 Memory 章节状态，尚未实机验证。仅新增 AXXX 不保证服务器保存自定义章节记录。"));
        panel.Children.Add(Field("导出实验配置",Bool(c.EnableExperiments,v=>c.EnableExperiments=v)));
        var existing=Choice();existing.ItemsSource=catalog.Entries.Where(x=>x.Kind is "MemoryChapter" or "SecretMusicCondition" or "Movie").OrderBy(x=>x.Kind).ThenBy(x=>x.Id).ToArray();panel.Children.Add(Field("已有配置模板",existing));
        panel.Children.Add(Buttons(("复制所选模板",()=>{if(existing.SelectedItem is ChapterReference r)AddExperiment(r.Kind,r.Xml,null,null,true);}),("导入实验 XML",ImportExperiment),("新建影片引用",()=>{
            int id=catalog.NextId("Movie");var xml=ChapterXml.New("Movie",id,"自定义前置影片");xml.Root!.Add(new XElement("MovieFileName",new XElement("path","intro.usm")));AddExperiment("Movie",xml.ToString(),null,null,false);
        })));
        var list=new ListBox{ItemsSource=c.Experiments,Height=85,Background=Theme.Dark,Foreground=Brushes.White,Margin=new Thickness(0,8,0,8)};panel.Children.Add(list);
        var editor=new TextBox{AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),MinHeight=280,Height=340,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Theme.Dark,Foreground=Brushes.White};panel.Children.Add(editor);
        bool setting=false;list.SelectionChanged+=(_,_)=>{setting=true;editor.Text=(list.SelectedItem as ChapterXmlDraft)?.Xml??"";setting=false;};editor.TextChanged+=(_,_)=>{if(!setting&&list.SelectedItem is ChapterXmlDraft item){item.Xml=editor.Text;Touch();}};
        panel.Children.Add(Buttons(("移除实验配置",()=>{if(list.SelectedItem is ChapterXmlDraft item){c.Experiments.Remove(item);Touch();Render();}}),("填入当前章节与首曲引用",()=>{
            if(list.SelectedItem is not ChapterXmlDraft item)return;var xml=ChapterXml.Parse(item.Xml,item.Kind+"Data");
            if(item.Kind=="MemoryChapter") {ChapterXml.Set(xml.Root!,"Name",ChapterXml.Ref("Name",c.Id,c.Name));ChapterXml.Set(xml.Root!,"dataName","memorychapter"+c.Id.ToString("D6"));}
            if(c.Songs.FirstOrDefault() is { } song)foreach(var node in xml.Descendants().Where(x=>x.Name.LocalName is "TargetName" or "MusicName" or "TargetMusicName").ToArray())node.ReplaceWith(ChapterXml.Ref(node.Name.LocalName,song.Id,song.Name));
            editor.Text=xml.ToString();
        })));
        panel.Children.Add(Theme.Hint("可配置 MemoryChapter.xml、SecretMusicCondition.xml、Movie.xml。先复制模板，再修改 ID、曲目、卡片条件和影片引用；Memory ID 须等于 Chapter ID。影片仅接受相对 USM 路径，素材需另行放入 movie 对应目录。分数挑战不能同时配置绕过门槛的通关发曲奖励。"));list.SelectedIndex=c.Experiments.Count==0?-1:0;return panel;
    }
    void AddExperiment(string kind,string xml,string? source,string? hash,bool copy) {
        if(current is null)return;var doc=ChapterXml.Parse(xml,kind+"Data");
        if(copy) { int id=kind=="MemoryChapter"?current.Id:catalog.NextId(kind,current.Experiments.Where(x=>x.Kind==kind).Select(x=>{try{return ChapterXml.Id(ChapterXml.Parse(x.Xml,kind+"Data").Root);}catch{return 0;}}));ChapterXml.Set(doc.Root!,"Name",ChapterXml.Ref("Name",id,current.Name+" · "+kind));ChapterXml.Set(doc.Root!,"dataName",kind.ToLowerInvariant()+id.ToString("D6")); }
        current.Experiments.Add(new(){Kind=kind,Xml=doc.ToString(),SourcePath=source,SourceHash=hash});Touch();Render();
    }
    void ImportExperiment() {var d=new OpenFileDialog{Title="导入 MemoryChapter / SecretMusicCondition / Movie XML",Filter="XML|*.xml",InitialDirectory=history.Get(PathPurpose.ChapterExperimentImport)};if(d.ShowDialog()!=true)return;var xml=Catalog.ReadXml(d.FileName);string kind=xml.Root?.Name.LocalName.Replace("Data","")??"";if(kind is not ("MemoryChapter" or "SecretMusicCondition" or "Movie"))throw new InvalidDataException("不支持的实验 XML 类型。");AddExperiment(kind,xml.ToString(),d.FileName,AutoSaveStore.FileHash(d.FileName),false);history.Remember(PathPurpose.ChapterExperimentImport,Path.GetDirectoryName(d.FileName)!);}
    ChapterDraft[] Selected()=>library.SelectedItems.Cast<ChapterDraft>().ToArray();
    void Preview() {
        var selected=Selected();if(selected.Length==0)throw new InvalidDataException("请选择章节。");
        var messages=new List<string>();foreach(var draft in selected){var build=ChapterExporter.Build(draft,catalog);messages.Add(draft.ToString());messages.AddRange(build.Files.Select(x=>$"  {x.Kind.ToLowerInvariant()}/{x.Kind.ToLowerInvariant()}{x.Id:D6}/{x.Kind}.xml"));messages.AddRange(build.Warnings.Distinct().Select(x=>"提示："+x));}
        report.Text=string.Join("\n",messages);pages.SelectedIndex=3;
    }
    async Task InspectPatches() {
        if(busy)return;busy=true;report.Text="正在只读检查游戏程序集……";
        string game=directories().Game;
        try{var result=await Task.Run(()=>UnlockPatchInspector.Inspect(game));report.Text=result.ToString();}catch(Exception e){report.Text="无法判断："+e.Message;}finally{busy=false;}
    }
    async Task Export() {
        if(busy)return;try {
            var selected=Selected();if(selected.Length==0)throw new InvalidDataException("请选择要导出的章节。");var (game,opt)=directories();if(string.IsNullOrWhiteSpace(opt))throw new InvalidDataException("请先在窗口顶部选择 AXXX 文件夹。");
            busy=true;BusyChanged?.Invoke(true);
            var snapshots=selected.Select(x=>x.Snapshot()).ToArray();var fresh=await Task.Run(()=>ChapterCatalog.Load(game,opt));var plan=await Task.Run(()=>ChapterExporter.Prepare(snapshots,fresh,opt));
            var patches=string.IsNullOrWhiteSpace(game)?new UnlockPatchReport("无法判断",["未选择游戏目录。"]):await Task.Run(()=>UnlockPatchInspector.Inspect(game));
            report.Text=string.Join("\n",plan.Files.Select(x=>(x.OriginalHash is null?"新建：":"覆盖并备份：")+x.Path))+"\n\n"+string.Join("\n",plan.Warnings.Distinct())+"\n\n"+patches;
            pages.SelectedIndex=3;
            string confirmation=$"将输出 {plan.Files.Count} 个文件，其中覆盖 {plan.Files.Count(x=>x.OriginalHash is not null)} 个。\n备份：{plan.BackupDirectory}\n\n{report.Text}";
            if(MessageBox.Show(Window.GetWindow(this),confirmation,"确认章节导出",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
            await Task.Run(()=>CardBatchExporter.Commit(plan,true));ChapterExporter.RecordExport(selected,plan);Touch();report.Text="导出完成。\n"+report.Text;log("章节配置已输出到："+opt);loadedGame="\0";
        }catch(Exception e){report.Text="导出未完成："+e.Message;pages.SelectedIndex=3;log(report.Text);}
        finally{busy=false;BusyChanged?.Invoke(false);}
    }
    ComboBox Reference(string field,int id,Action<ReferenceChoice> set) {
        var items=catalog.Get("Chapter").Select(x=>ChapterXml.Parse(x.Xml,"ChapterData").Root!.Element(field)).Where(x=>x is not null).Select(x=>new ReferenceChoice(ChapterXml.Id(x),ChapterXml.Name(x))).DistinctBy(x=>x.Id).OrderBy(x=>x.Id).ToArray();
        var combo=Choice();combo.ItemsSource=items;combo.SelectedItem=items.FirstOrDefault(x=>x.Id==id);combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is ReferenceChoice r){set(r);Touch();}};return combo;
    }
    TextBox Number(int value,Action<int> set)=>Text(value.ToString(),v=>{set(int.TryParse(v,out int n)?n:-1);});
    TextBox Text(string value,Action<string> set){var t=NewTextBox(value);t.TextChanged+=(_,_)=>{set(t.Text);Touch();};t.LostKeyboardFocus+=(_,_)=>RefreshLibrary();return t;}
    ComboBox Bool(bool value,Action<bool> set)=>EnumChoice(value?"true":"false",["false","true"],v=>set(v=="true"));
    ComboBox EnumChoice(string value,IEnumerable<string> values,Action<string> set){var combo=Choice();combo.ItemsSource=values;combo.SelectedItem=value;combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string v){set(v);Touch();}};return combo;}
    static TextBox NewTextBox(string value)=>new(){Text=value,Background=Theme.Dark,Foreground=Brushes.White,MinHeight=30,Padding=new Thickness(6,3,6,3)};
    static ComboBox Choice()=>new(){IsTextSearchEnabled=true,MaxDropDownHeight=300,MinHeight=30,Foreground=Brushes.White,Margin=new Thickness(0,4,0,4)};
    static UIElement Field(string label,UIElement input){var panel=new StackPanel{Margin=new Thickness(0,6,0,5)};panel.Children.Add(Theme.Text(label,12,Theme.Muted));panel.Children.Add(input);return panel;}
    static TabItem Page(string title,UIElement content)=>new(){Header=title,Content=new ScrollViewer{Content=new Border{Child=content,Padding=new Thickness(16),Background=Theme.PanelBrush,CornerRadius=new CornerRadius(8)},VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
    Button Button(string text,Action action,bool primary=false){var b=Theme.Button(text,primary);b.Click+=(_,_)=>{try{action();}catch(Exception e){report.Text=e.Message;log(e.Message);MessageBox.Show(Window.GetWindow(this),e.Message,"章节与挑战",MessageBoxButton.OK,MessageBoxImage.Warning);}};return b;}
    UIElement Buttons(params (string Text,Action Action)[] actions){var panel=new WrapPanel();foreach(var pair in actions)panel.Children.Add(Button(pair.Text,pair.Action));return panel;}
}
