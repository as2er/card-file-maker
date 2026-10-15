using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CardFileMaker;
public sealed class MainWindow:Window
{
 readonly TextBox game=new(),opt=new(),cardId=new(),charaId=new(),cardName=new(),charaName=new(),nick=new();
  readonly ComboBox attribute=new(),rarity=new(),assetSelect=new(),cropMode=new(),school=new(){IsEditable=true},skill=new(),upSkill=new(),charaMode=new(),charaSource=new(),existingChara=new();
 readonly System.Windows.Threading.DispatcherTimer directoryTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
 readonly System.Windows.Threading.DispatcherTimer autoSaveTimer=new(){Interval=AutoSaveStore.Interval};
 GameCatalog? gameCatalog;
 int catalogGeneration;
 bool autoSaving;
 string projectKey=Guid.NewGuid().ToString("N");
 string SaveDirectory=>AutoSaveStore.DirectoryFor(AppContext.BaseDirectory);
 readonly TextBlock status=new(),selection=new(),dimensions=new(),imageLabel=new();
 readonly CardSelectionList library=new();
 readonly CropCanvas canvas=new();
 readonly Image preview=new();
 readonly TextBlock cropHint=new(){TextWrapping=TextWrapping.Wrap,Foreground=Theme.Muted,FontSize=11};
 Button fillEmptyButton=null!;
 bool changingAssetList;
 (EditableCard Card,string Key)? pendingCrop;
 readonly List<EditableCard> cards=[];
 readonly List<EditableCard> installedCards=[];
 Grid cardEditor=null!;
 readonly Dictionary<Control,FrameworkElement> cardFieldRows=[];
 readonly TabControl tabs=new();
 readonly MusicEditorPanel music;
 readonly ChapterEditorPanel chapters;
 readonly PathHistoryStore pathHistory;
 readonly Grid workspace=new();
 readonly string[] args;
 readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(160)};
 EditableCard? current;
 bool loading,busy,dirty;
 string? projectPath;
 CatalogResult? catalog;
 public MainWindow(string[] args){
  this.args=args;Title="Card File Maker · Boss 卡片工作台";Width=1500;Height=970;MinWidth=1120;MinHeight=750;Background=Theme.Background;Foreground=Brushes.White;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  pathHistory=new PathHistoryStore(AppContext.BaseDirectory,Log);Theme.Install(Resources);music=new MusicEditorPanel(GetBossChoices,Log,pathHistory);music.ReloadRequested=()=>_ = ReloadCatalog();music.DraftPreview=choice=>{
   if(choice.Group!="当前项目")return null;
   var card=cards.FirstOrDefault(x=>x.Draft.CardId==choice.Id);if(card==null)return null;
   FlushCropChanges();var state=card.States["card"].Image!=null?card.States["card"]:card.States["cardSmall"];
   return state.Image==null?null:state.Render();
  };chapters=new ChapterEditorPanel(Log,pathHistory,()=>(music.CaptureXml(),music.SourcePath,music.SourceHash),()=>(game.Text,opt.Text));chapters.Changed=MarkDirty;chapters.BusyChanged=SetBusy;Content=Build();
  loading=true;
  game.Text=pathHistory.Get(PathPurpose.GameDirectory);
  opt.Text=pathHistory.Get(PathPurpose.OptDirectory);
    music.OptDirectory=opt.Text;
  charaMode.ItemsSource=new[]{"新建空角色","引用已有角色"};charaMode.SelectedIndex=0;
  charaSource.ItemsSource=new[]{"官方游戏包","自制 opt","当前项目"};charaSource.SelectedIndex=0;
  school.ItemsSource=new[]{new ReferenceChoice(0,"-"),new ReferenceChoice(1,"奏坂学園"),new ReferenceChoice(999,"プロモーション")};
  skill.ItemsSource=new[]{new ReferenceChoice(100005,"ボスアタック +10")};
  upSkill.ItemsSource=new[]{new ReferenceChoice(100046,"ボスアタック +12")};
  attribute.ItemsSource=new[]{new KeyValuePair<string,string>("Fire","Fire · 火"),new KeyValuePair<string,string>("Aqua","Aqua · 水"),new KeyValuePair<string,string>("Leaf","Leaf · 叶")};attribute.DisplayMemberPath="Value";attribute.SelectedValuePath="Key";rarity.ItemsSource=new[]{"N","R","SR","SSR"};assetSelect.ItemsSource=Assets.All;assetSelect.DisplayMemberPath="Label";assetSelect.SelectedIndex=0;cropMode.ItemsSource=new[]{"快速修改","详细修改"};cropMode.SelectedIndex=0;loading=false;
  timer.Tick+=(_,_)=>Guard(()=>{FlushCropChanges();UpdatePreview();});canvas.Changed=()=>CropChanged();
  foreach(var input in new[]{cardId,charaId,cardName,charaName,nick})input.TextChanged+=(_,_)=>MarkDirty();
  cardName.TextChanged+=(_,_)=>{if(!loading&&charaMode.SelectedIndex==0)charaName.Text=cardName.Text;};
  charaName.IsReadOnly=true;
  attribute.SelectionChanged+=(_,_)=>MarkDirty();rarity.SelectionChanged+=(_,_)=>MarkDirty();
    directoryTimer.Tick+=async(_,_)=>{directoryTimer.Stop();await ReloadCatalog();};
  autoSaveTimer.Tick+=async(_,_)=>await AutoSave();
  game.TextChanged+=(_,_)=>DirectoryChanged();
  game.TextChanged+=(_,_)=>{if(!loading)chapters.InvalidateCatalog();};
  opt.TextChanged+=(_,_)=>{if(!loading)chapters.InvalidateCatalog();};
  opt.TextChanged+=(_,_)=>{if(!loading)music.OptDirectory=opt.Text;DirectoryChanged();};
  school.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>MarkDirty()));
  school.SelectionChanged+=(_,_)=>MarkDirty();
  skill.SelectionChanged+=(_,_)=>{if(!loading){RefreshUpSkills(false);MarkDirty();}};
  upSkill.SelectionChanged+=(_,_)=>MarkDirty();
  charaSource.SelectionChanged+=(_,_)=>{if(!loading)RefreshCharacters();};
  existingChara.SelectionChanged+=(_,_)=>{if(!loading&&existingChara.SelectedItem is ReferenceChoice r){charaId.Text=r.Id.ToString();charaName.Text=r.Name;MarkDirty();}};
  charaMode.SelectionChanged+=(_,_)=>{if(!loading){if(charaMode.SelectedIndex==0){charaId.Text=NextId(true).ToString();charaName.Text=cardName.Text;}RefreshCharacters();MarkDirty();}};
  library.SelectionChanged+=(_,_)=>{if(!loading&&!library.IsMarqueeSelecting)Guard(ChangeSelection);};library.SelectionFinished=()=>Guard(ChangeSelection);
  assetSelect.SelectionChanged+=(_,_)=>SwitchAsset();
  cropMode.SelectionChanged+=(_,_)=>Guard(ChangeCropMode);
  tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs&&tabs.SelectedIndex==1&&!loading)Guard(()=>{CommitEditor();music.SetCatalog(gameCatalog??new GameCatalog());});};
  Closing+=(_,e)=>{if(busy||autoSaving){e.Cancel=true;return;}if((dirty||music.HasUnsavedChanges)&&!args.Contains("--render-test")&&MessageBox.Show(this,"卡片项目或歌曲配置有未保存的更改。是否放弃并关闭？","未保存的更改",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)e.Cancel=true;};
  PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S){Guard(()=>SaveProject(false));e.Handled=true;}};
  NewCard();dirty=false;Loaded+=LoadedWindow;Closed+=(_,_)=>{timer.Stop();autoSaveTimer.Stop();directoryTimer.Stop();catalogGeneration++;};
 }
 UIElement Build(){
  var root=new Grid{Margin=new Thickness(18)};foreach(var h in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=h});
  var head=new DockPanel{Margin=new Thickness(0,0,0,12)};var commands=Theme.Row(B("新建项目",NewProject),B("打开项目",OpenProject),B("保存项目",()=>SaveProject(false)),B("另存为",()=>SaveProject(true)));DockPanel.SetDock(commands,Dock.Right);head.Children.Add(commands);
  var title=new StackPanel();title.Children.Add(Theme.Text("Card File Maker",25));title.Children.Add(Theme.Text("BOSS 卡片工作台   /   素材制作 · 歌曲配置",12,Theme.Muted));head.Children.Add(title);root.Children.Add(head);
  var paths=new Grid{Margin=new Thickness(0,0,0,12)};paths.ColumnDefinitions.Add(new());paths.ColumnDefinitions.Add(new());
  paths.Children.Add(PathField("游戏目录 · 自动读取角色、舞台、技能及 CAB",game,()=>Browse(game)));
  var op=PathField("选定 AXXX 文件夹 · Boss 卡默认输出到此文件夹",opt,()=>Browse(opt));Grid.SetColumn(op,1);op.Margin=new Thickness(12,0,0,0);paths.Children.Add(op);Grid.SetRow(paths,1);root.Children.Add(paths);
  tabs.Background=Theme.Background;tabs.BorderThickness=new Thickness(0);tabs.Items.Add(new TabItem{Header="  卡片项目  ",Content=BuildCards()});tabs.Items.Add(new TabItem{Header="  Music.xml 创建 / 编辑  ",Content=music});
  tabs.Items.Add(new TabItem{Header="  章节与挑战  ",Content=chapters});
  workspace.Children.Add(tabs);Grid.SetRow(workspace,2);root.Children.Add(workspace);status.Text="准备就绪 · 卡片和 Music.xml 分开导出";status.Foreground=Theme.Muted;status.Margin=new Thickness(0,10,0,0);Grid.SetRow(status,3);root.Children.Add(status);return root;
 }
 UIElement BuildCards(){
  var grid=new Grid{Margin=new Thickness(0,12,0,0)};foreach(var w in new[]{new GridLength(325),new GridLength(14),new GridLength(1,GridUnitType.Star)})grid.ColumnDefinitions.Add(new(){Width=w});
  var left=new Grid();foreach(var h in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})left.RowDefinitions.Add(new(){Height=h});
  var top=new StackPanel();top.Children.Add(Theme.Text("卡片库",18));top.Children.Add(Theme.Row(B("新卡",NewCard,true),B("复制",DuplicateCard),B("移除草稿",RemoveCards)));top.Children.Add(Theme.Row(B("删除包内卡片",()=>_ = DeleteInstalledCards()),B("刷新",()=>_ = ReloadCatalog())));top.Children.Add(Theme.Row(B("全选",()=>library.SelectAll()),B("清空选择",()=>library.UnselectAll())));top.Children.Add(Theme.Hint("草稿可编辑 · 包内卡可预览、复制或删除\nCtrl / Shift 多选 · 拖动框选"));left.Children.Add(top);
  library.Background=Theme.Dark;library.BorderThickness=new Thickness(0);library.SelectionMode=SelectionMode.Extended;var panel=new FrameworkElementFactory(typeof(WrapPanel));panel.SetValue(WrapPanel.OrientationProperty,Orientation.Horizontal);library.ItemsPanel=new ItemsPanelTemplate(panel);
  library.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);library.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);library.Margin=new Thickness(0,8,0,8);Grid.SetRow(library,1);left.Children.Add(library);
  var bottom=new StackPanel();selection.Foreground=Theme.Accent;bottom.Children.Add(selection);bottom.Children.Add(B("批量导出选中的卡片",()=>_ = ExportCards(),true));bottom.Children.Add(B("注册 opt 内全部 UI 资源",()=>_ = RegisterUiAssets()));bottom.Children.Add(Theme.Hint("输出 card 和图片资源；新建角色时附带 chara。"));Grid.SetRow(bottom,2);left.Children.Add(bottom);grid.Children.Add(Theme.Panel(left));
  var editor=cardEditor=new Grid();foreach(var w in new[]{new GridLength(245),new GridLength(14),new GridLength(1,GridUnitType.Star)})editor.ColumnDefinitions.Add(new(){Width=w});Grid.SetColumn(editor,2);grid.Children.Add(editor);
  var fields=new StackPanel();fields.Children.Add(Theme.Text("卡片详情",18));fields.Children.Add(Theme.Hint("切换卡片时保留更改。"));
    foreach(var pair in new (string,Control)[]{("卡片名称",cardName),("卡片 ID",cardId),("角色方式",charaMode),("角色数据来源",charaSource),("已有角色 · ID / 名称",existingChara),("新角色名称",charaName),("新角色 ID",charaId)})fields.Children.Add(Field(pair.Item1,pair.Item2));
  fields.Children.Add(B("扫描并分配未占用 ID",()=>_ = AllocateIds()));
  foreach(var pair in new (string,Control)[]{("属性 · Fire 火 / Aqua 水 / Leaf 叶",attribute),("稀有度",rarity),("卡片昵称",nick),("学校 / 作品分组 · 可编辑编号",school),("技能 · ID / 名称",skill),("超开花技能 · ID / 名称",upSkill)})fields.Children.Add(Field(pair.Item1,pair.Item2));
  fields.Children.Add(Theme.Hint("学校是卡片所属学校或作品分组，例如奏坂学園、東方Project；不决定舞台。999 为推广分组。新建空角色输出二维角色；引用已有角色不改写其 Chara.xml。"));editor.Children.Add(Theme.Panel(new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}));
  var crop=new Grid();foreach(var h in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})crop.RowDefinitions.Add(new(){Height=h});Grid.SetColumn(crop,2);editor.Children.Add(crop);
  var cropTop=new StackPanel();cropTop.Children.Add(Theme.Text("素材裁剪",18));cropMode.Margin=new Thickness(0,8,0,6);cropTop.Children.Add(cropMode);cropTop.Children.Add(cropHint);var tools=new DockPanel{Margin=new Thickness(0,8,0,8)};var import=B("导入图片",ImportImage,true);DockPanel.SetDock(import,Dock.Right);tools.Children.Add(import);tools.Children.Add(assetSelect);cropTop.Children.Add(tools);dimensions.Foreground=Theme.Accent;dimensions.TextWrapping=TextWrapping.Wrap;cropTop.Children.Add(dimensions);imageLabel.Foreground=Theme.Muted;imageLabel.TextTrimming=TextTrimming.CharacterEllipsis;cropTop.Children.Add(imageLabel);crop.Children.Add(cropTop);
  canvas.MinHeight=0;var stage=new Border{Child=canvas,CornerRadius=new CornerRadius(8),ClipToBounds=true,Margin=new Thickness(0,10,0,10)};Grid.SetRow(stage,1);crop.Children.Add(stage);
  var foot=new Grid();foot.ColumnDefinitions.Add(new(){Width=new GridLength(125)});foot.ColumnDefinitions.Add(new());preview.Height=145;preview.Stretch=Stretch.Uniform;foot.Children.Add(new Border{Background=Theme.Dark,Padding=new Thickness(8),Child=preview});
  var actions=new WrapPanel{Margin=new Thickness(10,0,0,0)};Grid.SetColumn(actions,1);foot.Children.Add(actions);
  void AdaptCropLayout(){
   bool compact=ActualHeight<850||ActualWidth<1250;
   preview.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
   foot.ColumnDefinitions[0].Width=new GridLength(compact?0:125);
   actions.Margin=new Thickness(compact?0:10,0,0,0);
  }
  SizeChanged+=(_,_)=>AdaptCropLayout();Loaded+=(_,_)=>AdaptCropLayout();
  foreach(var b in new[]{B("居中裁剪 / 补透明",CenterCrop),B("完整放入",()=>Fit(false)),B("铺满画布",()=>Fit(true)),B("放大 ＋",()=>canvas.Zoom(1.12)),B("缩小 －",()=>canvas.Zoom(1/1.12)),fillEmptyButton=B("填充空白素材",FillEmpty)})actions.Children.Add(b);
  actions.Children.Add(Theme.Hint("拖动位置，滚轮缩放。\n不足尺寸时两侧均分透明补边。\n战斗立绘右侧自动透明。"));Grid.SetRow(foot,2);crop.Children.Add(foot);return grid;
 }
 Button B(string text,Action action,bool primary=false){var b=Theme.Button(text,primary);b.Click+=(_,_)=>Guard(action);return b;}
 UIElement Field(string label,Control input){var p=new StackPanel{Margin=new Thickness(0,6,0,2)};p.Children.Add(Theme.Text(label,11,Theme.Muted));input.Margin=new Thickness(0,4,0,3);p.Children.Add(input);cardFieldRows[input]=p;return p;}
 FrameworkElement PathField(string label,TextBox input,Action browse){var p=new StackPanel();p.Children.Add(Theme.Text(label,11,Theme.Muted));var row=new DockPanel{Margin=new Thickness(0,4,0,0)};var b=B("选择",browse);DockPanel.SetDock(b,Dock.Right);row.Children.Add(b);row.Children.Add(input);p.Children.Add(row);return p;}
 void Guard(Action a){try{a();}catch(Exception e){Error(e);}}
 void Error(Exception e){Log("错误："+e.Message);MessageBox.Show(this,e.Message,"无法完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
 void MarkDirty(){if(!loading){dirty=true;Title="Card File Maker · "+(projectPath==null?"未保存的项目":Path.GetFileName(projectPath))+" *";}}
 void Log(string s){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(()=>Log(s));return;}status.Text=s;}
 void SetBusy(bool value){busy=value;workspace.IsEnabled=!value;game.IsEnabled=!value;opt.IsEnabled=!value;Mouse.OverrideCursor=value?Cursors.Wait:null;}
 bool Browse(TextBox input){if(busy)return false;var d=new OpenFolderDialog{Title="选择文件夹",Multiselect=false};var purpose=ReferenceEquals(input,game)?PathPurpose.GameDirectory:PathPurpose.OptDirectory;d.InitialDirectory=Directory.Exists(input.Text)?input.Text:pathHistory.Get(purpose,SaveDirectory);if(d.ShowDialog(this)!=true)return false;input.Text=d.FolderName;pathHistory.Remember(purpose,d.FolderName);return true;}
 static int Number(TextBox t,string name){if(!int.TryParse(t.Text,out int v))throw new InvalidDataException(name+" 必须是整数。");return v;}

 static ReferenceChoice? Ref(ComboBox input)=>input.SelectedItem as ReferenceChoice;
 static void SelectRef(ComboBox input,int id){input.SelectedItem=input.Items.Cast<ReferenceChoice>().FirstOrDefault(x=>x.Id==id);}
 ReferenceChoice SchoolChoice(){
  var text=school.Text.Trim();if(Ref(school) is {} selected&&text==selected.ToString())return selected;
  if(!int.TryParse(text.Split('·')[0].Trim(),out int id)||id<0)throw new InvalidDataException("学校编号必须是非负整数。");
  return gameCatalog?.Get("School").LastOrDefault(x=>x.Id==id)??new ReferenceChoice(id,current?.Draft.SchoolId==id?current.Draft.SchoolName:id==999?"プロモーション":"自定义分组");
 }
 void DirectoryChanged(){
  catalog=null;gameCatalog=null;catalogGeneration++;MarkDirty();
  if(loading)return;ReplaceInstalledCards([]);directoryTimer.Stop();directoryTimer.Start();
 }
 async Task ReloadCatalog(){
  int generation=++catalogGeneration;string gp=game.Text,op=opt.Text;
  Log("正在读取角色、卡片、舞台、技能和 CAB……");
  try{
   var local=string.IsNullOrWhiteSpace(op)?new List<OptCard>():await Task.Run(()=>OptCardRemoval.Load(op));
   if(generation!=catalogGeneration)return;ReplaceInstalledCards(local);
   if(string.IsNullOrWhiteSpace(gp)){music.SetCatalog(new GameCatalog());Log($"已读取 {local.Count} 张包内卡片；选择游戏目录后可检查完整引用。");return;}
   var data=await Task.Run(()=>GameCatalog.Load(gp,op,Log));
   if(generation!=catalogGeneration)return;
   gameCatalog=data;catalog=data.Ids;RefreshReferenceInputs();music.SetCatalog(data);
   Log($"读取完成：{data.Cards.Count} 张卡、{data.Characters.Count} 个角色、{data.Get("StageID").Count} 个舞台、{data.Ids.Cabs.Count:N0} 个 CAB。"+(data.Warnings.Count==0?"":$" {data.Warnings.Count} 条提示：{data.Warnings[0]}"));
  }catch(Exception ex){if(generation==catalogGeneration){gameCatalog=null;catalog=null;music.SetCatalog(new GameCatalog());Log("目录读取失败："+ex.Message);}}
 }
 void RefreshReferenceInputs(){
  bool was=loading;loading=true;
  try{
   int schoolId;try{schoolId=SchoolChoice().Id;}catch{schoolId=current?.Draft.SchoolId??999;}
   int skillId=Ref(skill)?.Id??current?.Draft.SkillId??100005;
   if(gameCatalog!=null){school.ItemsSource=gameCatalog.Get("School");skill.ItemsSource=gameCatalog.Get("SkillID").Where(x=>gameCatalog.SkillPairs.ContainsKey(x.Id)).ToList();}
   SelectRef(school,schoolId);if(school.SelectedItem==null)school.Text=schoolId.ToString();
   SelectRef(skill,skillId);RefreshUpSkills(true);RefreshCharacters();
  }finally{loading=was;}
 }
 void RefreshUpSkills(bool preserve){
  int oldId=preserve?(Ref(upSkill)?.Id??current?.Draft.UpgradedSkillId??100046):-1;
  if(gameCatalog==null)return;
  IReadOnlyList<ReferenceChoice> choices=Array.Empty<ReferenceChoice>();
  if(Ref(skill) is {} chosen&&gameCatalog.SkillPairs.TryGetValue(chosen.Id,out var paired)&&paired.Count>0)
   choices=gameCatalog.Get("SkillID").Where(x=>paired.Contains(x.Id)).ToArray();
  upSkill.ItemsSource=choices;
  SelectRef(upSkill,oldId);
  if(!preserve&&upSkill.SelectedItem==null&&upSkill.Items.Count>0)upSkill.SelectedIndex=0;
 }
 List<ReferenceChoice> ProjectCharacters()=>cards.Where(x=>!x.Draft.ReuseExistingChara&&x!=current)
  .Select(x=>new ReferenceChoice(x.Draft.CharaId,x.Draft.CharacterName,"当前项目","当前项目")).DistinctBy(x=>x.Id).ToList();
 void RefreshCharacters(){
  bool was=loading;loading=true;
  try{
   string source=charaSource.SelectedItem as string??"官方游戏包";
   int.TryParse(charaId.Text,out int id);
   var choices=(source=="当前项目"?ProjectCharacters():gameCatalog?.Characters.Where(x=>x.Group==source).ToList()??[]).ToList();
   existingChara.ItemsSource=choices;SelectRef(existingChara,id);
   bool reuse=charaMode.SelectedIndex==1;existingChara.IsEnabled=reuse;charaSource.IsEnabled=reuse;charaId.IsEnabled=!reuse;charaName.IsEnabled=!reuse;
   foreach(var control in new Control[]{charaSource,existingChara})if(cardFieldRows.TryGetValue(control,out var row))row.Visibility=reuse?Visibility.Visible:Visibility.Collapsed;
   foreach(var control in new Control[]{charaName,charaId})if(cardFieldRows.TryGetValue(control,out var row))row.Visibility=reuse?Visibility.Collapsed:Visibility.Visible;
  }finally{loading=was;}
 }
 async Task AutoSave(){
  if(busy||autoSaving)return;
  autoSaving=true;
  try{var project=SnapshotProject();string dir=SaveDirectory;var path=await Task.Run(()=>AutoSaveStore.Save(project,dir));Log("已自动保存："+path);}
  catch(Exception ex){Log("自动保存未完成："+ex.Message);}
  finally{autoSaving=false;}
 }

 void CommitEditor(){
  if(current==null||current.Installed!=null||loading)return;
  FlushCropChanges();var c=current.Draft;bool reuse=charaMode.SelectedIndex==1;
  var ch=Ref(existingChara);if(reuse&&ch==null){if(!c.ReuseExistingChara)throw new InvalidDataException("请从所选来源选择已读取的角色。");ch=new ReferenceChoice(c.CharaId,c.CharacterName);}
  var sc=SchoolChoice();
  var sk=Ref(skill)??new ReferenceChoice(c.SkillId,c.SkillName);
  var up=Ref(upSkill)??new ReferenceChoice(c.UpgradedSkillId,c.UpgradedSkillName);
  var next=new CardDraft{Key=c.Key,CardId=Number(cardId,"卡片 ID"),CharaId=reuse?ch!.Id:Number(charaId,"角色 ID"),CardName=cardName.Text.Trim(),CharacterName=reuse?ch!.Name:cardName.Text.Trim(),ReuseExistingChara=reuse,NickName=nick.Text,Attribute=attribute.SelectedValue as string??"Fire",Rarity=rarity.SelectedItem as string??"R",SchoolId=sc.Id,SchoolName=sc.Name,SkillId=sk.Id,SkillName=sk.Name,UpgradedSkillId=up.Id,UpgradedSkillName=up.Name,QuickCrop=c.QuickCrop,Crops=c.Crops};
  CardValidation.Validate(next);if(Ref(skill)!=null&&Ref(upSkill)!=null&&gameCatalog!=null&&(!gameCatalog.SkillPairs.TryGetValue(next.SkillId,out var paired)||!paired.Contains(next.UpgradedSkillId)))throw new InvalidDataException("没有发现所选技能与超开花技能的实际配对，请重新选择。");
  if(cards.Any(x=>x!=current&&(x.Draft.CardId==next.CardId||(!next.ReuseExistingChara&&!x.Draft.ReuseExistingChara&&x.Draft.CharaId==next.CharaId))))throw new InvalidDataException("项目内卡片 ID 或新角色 ID 重复。");
  current.Draft=next;current.RefreshTile();
 }

 void ChangeSelection(){
  var next=(library.SelectedItem as ListBoxItem)?.Tag as EditableCard;
  if(next!=current){try{CommitEditor();}catch{loading=true;library.SelectedItem=current?.Item;loading=false;throw;}current=next;LoadEditor();}
  selection.Text=$"草稿 {cards.Count} · 包内 {installedCards.Count} · 已选 {library.SelectedItems.Count}";
 }

 void LoadEditor(){
  loading=true;
  try{
   cardEditor.IsEnabled=current!=null&&current.Installed==null;current?.LoadInstalledImages();
   var c=current?.Draft;cardId.Text=c?.CardId.ToString()??"";charaId.Text=c?.CharaId.ToString()??"";cardName.Text=c?.CardName??"";charaName.Text=c?.ReuseExistingChara==false?c.CardName:c?.CharacterName??"";nick.Text=c?.NickName??"";
   SelectRef(school,c?.SchoolId??999);if(school.SelectedItem==null)school.Text=(c?.SchoolId??999).ToString();
   SelectRef(skill,c?.SkillId??100005);RefreshUpSkills(true);SelectRef(upSkill,c?.UpgradedSkillId??100046);
   charaMode.SelectedIndex=c?.ReuseExistingChara==true?1:0;
   if(c?.ReuseExistingChara==true){var source=gameCatalog?.Characters.LastOrDefault(x=>x.Id==c.CharaId)?.Group??(ProjectCharacters().Any(x=>x.Id==c.CharaId)?"当前项目":"官方游戏包");charaSource.SelectedItem=source;}
   RefreshCharacters();attribute.SelectedValue=c?.Attribute;rarity.SelectedItem=c?.Rarity;cropMode.SelectedIndex=c?.QuickCrop==true?0:1;RefreshAssetOptions();
  }finally{loading=false;}
 }

 int NextId(bool chara){
  var used=new HashSet<int>(cards.Select(x=>chara?x.Draft.CharaId:x.Draft.CardId));if(catalog!=null)used.UnionWith(chara?catalog.CharaIds:catalog.CardIds);
  if(Directory.Exists(opt.Text)){
   PathSafety.RejectLinkedAncestors(opt.Text);
   if(!chara)used.UnionWith(Catalog.ReadCards(opt.Text).Select(x=>x.Id));
   else{string root=Path.Combine(opt.Text,"chara");if(Directory.Exists(root))foreach(var path in PathSafety.Files(root).Where(x=>Path.GetFileName(x).Equals("Chara.xml",StringComparison.OrdinalIgnoreCase))){if(int.TryParse(Catalog.ReadXml(path).Root?.Element("Name")?.Element("id")?.Value,out int taken))used.Add(taken);}}
  }
  int id=900001;while(used.Contains(id)&&id<999999)id++;if(used.Contains(id))throw new InvalidOperationException("可用 ID 已用尽。");return id;
 }
 void AddCard(EditableCard card){cards.Add(card);library.Items.Add(card.Item);library.SelectedItem=card.Item;card.Item.BringIntoView();MarkDirty();ChangeSelection();}
 void NewCard(){if(busy)return;CommitEditor();string name="新卡片 "+(cards.Count+1);AddCard(new EditableCard(new(){QuickCrop=true,CardId=NextId(false),CharaId=NextId(true),CardName=name,CharacterName=name}));}
 void DuplicateCard(){
  if(busy||current==null)return;CommitEditor();var old=current;var c=old.Draft;
  var copy=new EditableCard(new(){CardId=NextId(false),CharaId=c.ReuseExistingChara?c.CharaId:NextId(true),ReuseExistingChara=c.ReuseExistingChara,QuickCrop=c.QuickCrop,SchoolName=c.SchoolName,SkillName=c.SkillName,UpgradedSkillName=c.UpgradedSkillName,CardName=c.CardName+" 副本",CharacterName=c.ReuseExistingChara?c.CharacterName:c.CardName+" 副本",Attribute=c.Attribute,Rarity=c.Rarity,NickName=c.NickName,SchoolId=c.SchoolId,SkillId=c.SkillId,UpgradedSkillId=c.UpgradedSkillId});
  foreach(var key in old.States.Keys){var a=old.States[key];var b=copy.States[key];b.Image=a.Image;b.SourceName=a.SourceName;b.Transform=a.Transform;}copy.RefreshTile();AddCard(copy);
 }
 void RemoveCards(){
  if(busy)return;var selected=library.SelectedItems.Cast<ListBoxItem>().Select(x=>(EditableCard)x.Tag).Where(x=>x.Installed==null).ToList();if(selected.Count==0){Log("请选择项目草稿；包内卡片请使用“删除包内卡片”。");return;}
  if(MessageBox.Show(this,$"从项目移除 {selected.Count} 张卡片？已导出的 opt 文件会保留。","移除卡片",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  FlushCropChanges();loading=true;foreach(var c in selected){cards.Remove(c);library.Items.Remove(c.Item);}current=null;loading=false;LoadEditor();if(cards.Count>0)library.SelectedIndex=0;MarkDirty();ChangeSelection();
 }
 void ReplaceInstalledCards(IReadOnlyList<OptCard> values){
  string? selectedPath=current?.Installed?.CardPath;bool was=loading;loading=true;
  try{
   foreach(var card in installedCards)library.Items.Remove(card.Item);installedCards.Clear();
   foreach(var value in values){var card=new EditableCard(value.Draft){Installed=value};card.LoadInstalledImages(true);card.RefreshTile();installedCards.Add(card);library.Items.Add(card.Item);}
   if(selectedPath!=null){current=null;library.SelectedItem=installedCards.FirstOrDefault(x=>x.Installed!.CardPath.Equals(selectedPath,StringComparison.OrdinalIgnoreCase))?.Item;}
  }finally{loading=was;}
  if(!loading){ChangeSelection();if(current==null)LoadEditor();}
 }
 async Task DeleteInstalledCards(){
  if(busy)return;
  try{
   CommitEditor();var selected=library.SelectedItems.Cast<ListBoxItem>().Select(x=>(EditableCard)x.Tag).Where(x=>x.Installed!=null).ToArray();
   if(selected.Length==0)throw new InvalidOperationException("请在卡片库选择标有“包内”的卡片。");
   if(string.IsNullOrWhiteSpace(game.Text))throw new InvalidOperationException("请先选择游戏目录，以检查游戏及其它包内的引用。");
   var ids=selected.Select(x=>x.Draft.CardId).ToArray();
   var musicSnapshot=music.Snapshot();if(int.TryParse(musicSnapshot.Root?.Element("BossCard")?.Element("id")?.Value,out int boss)&&ids.Contains(boss))throw new InvalidOperationException("当前 Music 草稿仍引用待删除卡片，请先更换 Boss。");
   string gp=game.Text,op=opt.Text;SetBusy(true);var plan=await Task.Run(()=>OptCardRemoval.Prepare(gp,op,ids));
   var removedCharas=plan.Files.Where(x=>x.Replacement==null&&Path.GetFileName(x.Path).Equals("Chara.xml",StringComparison.OrdinalIgnoreCase)).Select(x=>int.Parse(Catalog.ReadXml(x.Path).Root!.Element("Name")!.Element("id")!.Value)).ToHashSet();
   if(cards.Any(x=>x.Draft.ReuseExistingChara&&removedCharas.Contains(x.Draft.CharaId)))throw new InvalidOperationException("项目草稿仍引用本次将删除的角色，请先更换角色或移除该草稿。");
   var paths=plan.Files.Select(x=>(x.Replacement==null?"删除文件：":"更新索引：")+x.Path).Append("先备份到："+plan.BackupDirectory);
   if(!Confirm("确认删除包内卡片",$"删除 {ids.Length} 张卡片及其专属资源。逐个操作文件，不递归删除目录。",paths))return;
   await Task.Run(()=>OptCardRemoval.Commit(plan,true));await ReloadCatalog();Log("包内卡片已删除；完整备份："+plan.BackupDirectory);
  }catch(Exception ex){Error(ex);}finally{SetBusy(false);}
 }
 async Task AllocateIds(){
  if(busy||current==null)return;SetBusy(true);
  try{string gp=game.Text,op=opt.Text;catalog=await Task.Run(()=>Catalog.Scan(gp,op,Log,false));cardId.Text=NextId(false).ToString();if(charaMode.SelectedIndex==0)charaId.Text=NextId(true).ToString();CommitEditor();Log("已分配不与游戏、opt 或当前项目冲突的 ID。");}catch(Exception e){Error(e);}finally{SetBusy(false);}
 }
 void RefreshAssetOptions(){
  string key=(assetSelect.SelectedItem as AssetSpec)?.Key??"card";
  bool quick=current?.Draft.QuickCrop==true;
  if(quick)key=key is "icon" or "iconSmall"?"icon":"card";
  changingAssetList=true;
  try{
   assetSelect.ItemsSource=quick
    ?new[]{Assets.All.Single(x=>x.Key=="icon") with{Label="头像组 · 大头像 / 小头像"},Assets.All.Single(x=>x.Key=="card") with{Label="卡面 / 立绘组 · 四种尺寸"}}
    :Assets.All;
   assetSelect.SelectedItem=assetSelect.Items.Cast<AssetSpec>().FirstOrDefault(x=>x.Key==key)??assetSelect.Items[0];
  }finally{changingAssetList=false;}
  fillEmptyButton.Visibility=quick?Visibility.Collapsed:Visibility.Visible;
  cropHint.Text=quick?"每组导入一张清晰原图，调整后同步本组。小图保持画面，仅降低分辨率。":"六种素材分别导入和调整；切换模式保留现有图片。";
  SwitchAsset();
 }
 void ChangeCropMode(){
  if(loading||current==null)return;
  FlushCropChanges();current.Draft.QuickCrop=cropMode.SelectedIndex==0;RefreshAssetOptions();MarkDirty();
 }
 void SwitchAsset(){
  if(changingAssetList)return;FlushCropChanges();
  if(assetSelect.SelectedItem is not AssetSpec spec)return;
  canvas.State=current?.States[spec.Key];canvas.InvalidateVisual();
  dimensions.Text=current?.Draft.QuickCrop==true
   ?spec.Key=="icon"?"头像 256 × 256  ·  小头像 100 × 100":"卡面 / 竖版 768 × 1052  ·  小卡面 256 × 356\n战斗 1024 × 1024（左侧 768 × 1024 有效）"
   :$"{spec.Width} × {spec.Height}"+(spec.Key=="battle"?" · 左侧 768 × 1024 有效":"");
  UpdatePreview();
 }
 void CropChanged(bool immediate=false){
  if(current==null||canvas.State?.Image==null)return;
  if(current.Draft.QuickCrop)pendingCrop=(current,canvas.State.Spec.Key);
  MarkDirty();timer.Stop();
  if(immediate){FlushCropChanges();UpdatePreview();}else timer.Start();
 }
 void FlushCropChanges(){
  timer.Stop();if(pendingCrop is not {} pending)return;
  pending.Card.ApplyQuickCrop(pending.Key);pendingCrop=null;
 }
 void UpdatePreview(){var s=canvas.State;preview.Source=s?.Image==null?null:s.Render();imageLabel.Text=s?.Image==null?"尚未导入图片":s.SourceName;current?.RefreshTile();}
 void ImportImage(){
  if(current==null||canvas.State==null)return;
  var d=new OpenFileDialog{Title=current.Draft.QuickCrop?"为当前组选择清晰原图":"选择卡片素材",Filter="图片|*.png;*.jpg;*.jpeg;*.bmp",InitialDirectory=pathHistory.Get(PathPurpose.ImageImport,SaveDirectory)};if(d.ShowDialog(this)!=true)return;
  var s=canvas.State;s.Image=ImageFiles.Read(d.FileName);s.SourceName=Path.GetFileName(d.FileName);pathHistory.Remember(PathPurpose.ImageImport,Path.GetDirectoryName(d.FileName)!);
  if(current.Draft.QuickCrop)s.Fit(false);else s.Center();
  if(!current.Draft.QuickCrop&&current.States.Values.Count(x=>x.Image!=null)==1)FillEmpty();
  canvas.InvalidateVisual();CropChanged(true);
 }
 void FillEmpty(){if(current==null||canvas.State?.Image==null||current.Draft.QuickCrop)return;foreach(var s in current.States.Values.Where(x=>x.Image==null)){s.Image=canvas.State.Image;s.SourceName=canvas.State.SourceName;s.Center();}MarkDirty();UpdatePreview();}
 void CenterCrop(){canvas.State?.Center();canvas.InvalidateVisual();CropChanged(true);}
 void Fit(bool cover){canvas.State?.Fit(cover);canvas.InvalidateVisual();CropChanged(true);}
 IReadOnlyList<BossCardChoice> GetBossChoices(){
    var result=new List<BossCardChoice>();
  if(gameCatalog!=null)result.AddRange(gameCatalog.Cards);
  
  result.AddRange(cards.Select(x=>new BossCardChoice(x.Draft.CardId,x.Draft.CardName,x.Draft.Attribute,"当前项目",x.Draft.CharaId,x.Draft.CharacterName,"当前项目")));
  return result.OrderBy(x=>x.Id).ThenBy(x=>x.Source).ToList();
 }
 CardProject SnapshotProject(){
  CommitEditor();var cache=new Dictionary<BitmapSource,byte[]>();
  foreach(var card in cards){card.Draft.Crops=[];foreach(var pair in card.States){var s=pair.Value;if(s.Image==null)continue;if(!cache.TryGetValue(s.Image,out var png)){png=ImageFiles.Png(s.Image);cache[s.Image]=png;}card.Draft.Crops[pair.Key]=new(){Png=png,SourceName=s.SourceName,Transform=s.Transform};}}
  return new(){ProjectKey=projectKey,MusicXml=music.CaptureXml(),MusicRawDraft=music.IsRawDraft,MusicSourcePath=music.SourcePath,MusicSourceHash=music.SourceHash,GameDirectory=game.Text,OptDirectory=opt.Text,MusicOutputDirectory=music.OutputDirectory,Cards=cards.Select(x=>x.Draft.Snapshot()).ToList(),Chapters=chapters.Snapshot()};
 }
 bool SaveProject(bool saveAs){
  if(busy||autoSaving)return false;var project=SnapshotProject();string? path=projectPath;
  if(saveAs||path==null){var d=new SaveFileDialog{Title="保存卡片项目（含素材与裁剪）",Filter="卡片项目|*.cfmproj",InitialDirectory=pathHistory.Get(PathPurpose.ProjectSave,SaveDirectory),FileName=path==null?"我的 Boss 卡片.cfmproj":Path.GetFileName(path),OverwritePrompt=false};if(d.ShowDialog(this)!=true)return false;path=d.FileName;}
  PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("项目文件不能为链接。");
  var bytes=ProjectCodec.Encode(project);string? before=AutoSaveStore.FileHash(path);string staging=path+".pending-"+Guid.NewGuid().ToString("N");string? backup=before==null?null:path+".backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6];
  if((bytes.Length>=100_000_000||before!=null)&&!Confirm("保存项目",$"将{(before!=null?"覆盖并备份":"创建")}项目文件，约 {bytes.Length/1048576.0:F1} MB，含 {cards.Count} 张卡。",new[]{path,staging}.Concat(backup==null?Array.Empty<string>():new[]{backup})))return false;
  AutoSaveStore.WriteProjectFile(path,bytes,before,staging,backup);pathHistory.Remember(PathPurpose.ProjectSave,Path.GetDirectoryName(path)!);projectPath=path;music.MarkSaved();dirty=false;Title="Card File Maker · "+Path.GetFileName(path);Log("卡片项目已保存："+path);return true;
 }
 bool CanReplace()=>!(dirty||music.HasUnsavedChanges)||MessageBox.Show(this,"卡片项目尚未保存。是否放弃更改并继续？","未保存的项目",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 void NewProject(){if(busy||autoSaving||!CanReplace())return;pendingCrop=null;timer.Stop();projectKey=Guid.NewGuid().ToString("N");music.NewDocument();chapters.Restore([]);loading=true;cards.Clear();library.Items.Clear();foreach(var card in installedCards)library.Items.Add(card.Item);current=null;projectPath=null;loading=false;LoadEditor();NewCard();dirty=false;Title="Card File Maker · 新项目";}
 void OpenProject(){
  if(busy||autoSaving||!CanReplace())return;var d=new OpenFileDialog{Title="打开卡片项目",Filter="卡片项目|*.cfmproj",InitialDirectory=pathHistory.Get(PathPurpose.ProjectOpen,SaveDirectory)};if(d.ShowDialog(this)!=true)return;
  var p=ProjectCodec.Decode(File.ReadAllBytes(d.FileName));var loaded=p.Cards.Select(x=>new EditableCard(x)).ToList();chapters.Restore(p.Chapters);chapters.InvalidateCatalog();
  loading=true;
  try{pendingCrop=null;timer.Stop();cards.Clear();installedCards.Clear();library.Items.Clear();current=null;game.Text=p.GameDirectory;opt.Text=p.OptDirectory;music.OutputDirectory=p.MusicOutputDirectory;
   foreach(var c in loaded){cards.Add(c);library.Items.Add(c.Item);}projectPath=d.FileName;projectKey=p.ProjectKey;
  }finally{loading=false;}
  music.OptDirectory=p.OptDirectory;if(p.MusicRawDraft||!string.IsNullOrWhiteSpace(p.MusicXml))music.RestoreXml(p.MusicXml,p.MusicSourcePath,p.MusicRawDraft,p.MusicSourceHash);else music.NewDocument();LoadEditor();if(cards.Count>0)library.SelectedIndex=0;dirty=false;Title="Card File Maker · "+Path.GetFileName(d.FileName);ChangeSelection();directoryTimer.Stop();directoryTimer.Start();pathHistory.Remember(PathPurpose.ProjectOpen,Path.GetDirectoryName(d.FileName)!);Log("已恢复卡片、素材、裁剪参数和 Music 编辑内容。");
 }

 async Task RegisterUiAssets(){
  if(busy)return;
  SetBusy(true);
  try{
   string gp=game.Text,op=opt.Text;
   var batch=await Task.Run(()=>Catalog.PrepareUiRegistration(gp,op,Log));
   var paths=new List<string>{batch.Files[0].OriginalHash==null?"新建："+batch.Files[0].Path:"覆盖："+batch.Files[0].Path};
   if(batch.Files[0].OriginalHash!=null)paths.Add("先备份到："+batch.BackupDirectory);
   paths.AddRange(batch.Warnings);
   if(!Confirm("注册 opt 内全部 UI 资源","扫描 assets 文件夹内 ui_ 开头的 Unity 资源，更新 assets.bytes。",paths))return;
   await Task.Run(()=>CardBatchExporter.Commit(batch,true));
   Log("UI 资源索引已保存："+batch.Files[0].Path);
  }catch(Exception ex){Error(ex);}finally{SetBusy(false);}
 }

 async Task ExportCards(){
  if(busy)return;
  try{
   CommitEditor();var selected=library.SelectedItems.Cast<ListBoxItem>().Select(x=>(EditableCard)x.Tag).Where(x=>x.Installed==null).ToArray();if(selected.Length==0)throw new InvalidOperationException("请选择要导出的项目草稿。包内卡片可先复制到草稿。");
   var missing=selected.Where(x=>x.MissingAssetLabels().Count>0).Select(x=>$"{x.Draft.CardName}（ID {x.Draft.CardId}）：{string.Join("、",x.MissingAssetLabels())}").ToArray();
   if(missing.Length>0)throw new InvalidOperationException("请先补齐以下素材，快速模式每组导入一张即可：\n\n"+string.Join("\n",missing));
   var items=selected.Select(x=>new CardExportItem(x.Draft,x.States.ToDictionary(a=>a.Key,a=>a.Value.Rgba()))).ToArray();
   var request=new BatchExportRequest(game.Text,opt.Text,items);SetBusy(true);var batch=await Task.Run(()=>CardBatchExporter.Prepare(request,Log));
   var paths=batch.Files.Select(x=>(x.OriginalHash==null?"新建：":"覆盖并备份：")+x.Path).ToList();if(batch.Files.Any(x=>x.OriginalHash!=null))paths.Add("新建备份目录："+batch.BackupDirectory);
   if(!Confirm("确认批量导出",$"向所选 opt 导出 {selected.Length} 张卡片。下列文件将写入；Music.xml 不会改变。",paths)){Log("已取消，未写入 opt。");return;}
   await Task.Run(()=>CardBatchExporter.Commit(batch,true));await ReloadCatalog();Log($"已导出 {selected.Length} 张 Boss 卡：{batch.OutputDirectory}");
   MessageBox.Show(this,"卡片资源已导出。\n\n"+string.Join("\n",batch.Warnings)+"\n\n可在 Music.xml 页选择这些卡片，并独立导出歌曲配置。","导出完成",MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception e){Error(e);}finally{SetBusy(false);}
 }
 bool Confirm(string title,string intro,IEnumerable<string> paths){
  var w=new Window{Owner=this,Title=title,Width=820,Height=500,Background=Theme.PanelBrush,Foreground=Brushes.White,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var g=new Grid{Margin=new Thickness(18)};g.RowDefinitions.Add(new(){Height=GridLength.Auto});g.RowDefinitions.Add(new());g.RowDefinitions.Add(new(){Height=GridLength.Auto});g.Children.Add(Theme.Text(intro));
  var list=new TextBox{Text=string.Join(Environment.NewLine,paths),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,12)};Grid.SetRow(list,1);g.Children.Add(list);
  var yes=Theme.Button(title.Contains("删除")?"确认删除":"确认写入",true);yes.Click+=(_,_)=>w.DialogResult=true;var cancel=Theme.Button("取消");cancel.Click+=(_,_)=>w.DialogResult=false;var buttons=Theme.Row(yes,cancel);buttons.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(buttons,2);g.Children.Add(buttons);w.Content=g;return w.ShowDialog()==true;
 }
 void VerifyQuickCropWorkflow(){
  static void Require(bool ok,string message){if(!ok)throw new Exception("快速裁剪："+message);}
  static BitmapSource Bitmap(int width,int height,Func<int,int,(byte R,byte G,byte B,byte A)> pixel){
   var bytes=new byte[width*height*4];for(int y=0;y<height;y++)for(int x=0;x<width;x++){var p=pixel(x,y);int i=(y*width+x)*4;bytes[i]=p.B;bytes[i+1]=p.G;bytes[i+2]=p.R;bytes[i+3]=p.A;}
   var result=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);result.Freeze();return result;
  }
  var draft=new EditableCard(new(){QuickCrop=true});
  var master=draft.States["card"];master.Image=Bitmap(1200,1052,(x,y)=>x<216||x>=984?((byte)0,(byte)255,(byte)0,(byte)255):y<526?((byte)230,(byte)30,(byte)30,(byte)255):((byte)30,(byte)80,(byte)230,(byte)255));master.SourceName="分组测试.png";master.Center();
  Require(draft.MissingAssetLabels().Count==5,"未导入素材统计错误");
  draft.ApplyQuickCrop("card");
  Require(draft.MissingAssetLabels().Count==2&&draft.States["icon"].Image==null&&draft.States["iconSmall"].Image==null,"卡面组写入了头像组");
  var cardPixels=draft.States.Where(x=>x.Value.Image!=null).ToDictionary(x=>x.Key,x=>x.Value.Rgba().Pixels);
  foreach(var key in new[]{"cardSmall","battle","portrait"}){
   var output=draft.States[key].Rgba();var spec=Assets.All.Single(x=>x.Key==key);Require(output.Width==spec.Width&&output.Height==spec.Height,key+"尺寸错误");
   for(int i=0;i<output.Pixels.Length;i+=4)if(output.Pixels[i+3]>250)Require(output.Pixels[i+1]<150,key+"重新引入了裁剪外的绿色区域");
  }
  Require(cardPixels["portrait"].SequenceEqual(cardPixels["card"]),"同尺寸竖版未保留同一画面");
  var battle=draft.States["battle"].Rgba();
  for(int y=0;y<1024;y++)for(int x=768;x<1024;x++)Require(battle.Pixels[(y*1024+x)*4+3]==0,"战斗右侧不透明");
  var smallCard=draft.States["cardSmall"].Rgba();Require(smallCard.Pixels[3]==0&&smallCard.Pixels[((smallCard.Height-1)*smallCard.Width)*4+3]==0,"小卡面比例差未补透明边");
  var icon=draft.States["icon"];icon.Image=Bitmap(256,256,(x,y)=>x<32||x>=224?((byte)0,(byte)0,(byte)0,(byte)0):x<128?((byte)220,(byte)40,(byte)20,(byte)255):((byte)20,(byte)40,(byte)220,(byte)255));icon.SourceName="头像测试.png";icon.Center();draft.ApplyQuickCrop("icon");
  Require(draft.MissingAssetLabels().Count==0,"完整素材仍报缺少");
  foreach(var pair in cardPixels)Require(pair.Value.SequenceEqual(draft.States[pair.Key].Rgba().Pixels),"头像组改变了卡面组 "+pair.Key);
  var smallIcon=draft.States["iconSmall"].Rgba();
  Require(smallIcon.Width==100&&smallIcon.Height==100&&smallIcon.Pixels[(50*100)*4+3]==0,"小头像尺寸或透明边错误");
  int red=(50*100+25)*4,blue=(50*100+75)*4;
  Require(Math.Abs(smallIcon.Pixels[red]-220)<=1&&Math.Abs(smallIcon.Pixels[red+1]-40)<=1&&Math.Abs(smallIcon.Pixels[blue+2]-220)<=1,"缩小改变了画面构图或实色区域");
  foreach(var pair in draft.States){var s=pair.Value;draft.Draft.Crops[pair.Key]=new(){Png=ImageFiles.Png(s.Image!),SourceName=s.SourceName,Transform=s.Transform};}
  var recovered=new EditableCard(ProjectCodec.Decode(ProjectCodec.Encode(new(){Cards=[draft.Draft]})).Cards[0]);
  Require(recovered.Draft.QuickCrop,"项目恢复丢失模式");
  foreach(var pair in draft.States)Require(pair.Value.Rgba().Pixels.SequenceEqual(recovered.States[pair.Key].Rgba().Pixels),"项目恢复改变 "+pair.Key);
  // 验证界面切换不覆盖素材，最后一次缩放在保存、切组和切卡前完成同步。
  Require(current!=null,"界面没有当前卡片");var selected=current!;
  cropMode.SelectedIndex=1;assetSelect.SelectedItem=assetSelect.Items.Cast<AssetSpec>().Single(x=>x.Key=="cardSmall");
  selected.States["cardSmall"].Image=icon.Image;selected.States["cardSmall"].Fit(false);var custom=selected.States["cardSmall"].Rgba().Pixels;
  cropMode.SelectedIndex=0;Require(assetSelect.Items.Count==2&&selected.States["cardSmall"].Rgba().Pixels.SequenceEqual(custom),"切模式覆盖独立素材");
  cropMode.SelectedIndex=1;Require(assetSelect.Items.Count==6&&selected.States["cardSmall"].Rgba().Pixels.SequenceEqual(custom),"详细模式丢失独立素材");
  cropMode.SelectedIndex=0;assetSelect.SelectedItem=assetSelect.Items.Cast<AssetSpec>().Single(x=>x.Key=="card");
  canvas.Zoom(.9);Require(pendingCrop!=null,"裁剪没有等待同步");
  var snapshot=SnapshotProject();Require(pendingCrop==null,"保存遗漏最后一次裁剪");
  Require(selected.States["portrait"].Rgba().Pixels.SequenceEqual(selected.States["card"].Rgba().Pixels),"保存前未更新派生素材");
  var untouched=selected.States["iconSmall"].Rgba().Pixels;
  canvas.Zoom(.95);assetSelect.SelectedItem=assetSelect.Items.Cast<AssetSpec>().Single(x=>x.Key=="icon");
  Require(pendingCrop==null&&untouched.SequenceEqual(selected.States["iconSmall"].Rgba().Pixels),"切组漏同步或改变另一组");
  canvas.Zoom(.9);var previous=selected;
  var next=cards.First(x=>x!=previous);library.SelectedItem=next.Item;ChangeSelection();
  Require(pendingCrop==null,"切卡遗漏最后一次裁剪");
  var expected=new CropState(Assets.All.Single(x=>x.Key=="iconSmall")){Image=previous.States["icon"].Render()};expected.Fit(false);
  Require(expected.Rgba().Pixels.SequenceEqual(previous.States["iconSmall"].Rgba().Pixels),"切卡后上一张头像未同步");
  library.SelectedItem=previous.Item;ChangeSelection();
  assetSelect.SelectedItem=assetSelect.Items.Cast<AssetSpec>().Single(x=>x.Key=="card");
  Log("快速裁剪回归通过：分组隔离、六种尺寸、透明边、保存恢复与切换同步。");
 }
 async void LoadedWindow(object sender,RoutedEventArgs e){
    try{PathSafety.RejectLinkedAncestors(SaveDirectory);Directory.CreateDirectory(SaveDirectory);}catch(Exception ex){Log("无法创建默认保存目录："+ex.Message);}
  if(args.Length<2||args[0]!="--render-test"){await ReloadCatalog();autoSaveTimer.Start();return;}
  try{
   if(args.Contains("--preview-test")){loading=true;try{game.Text=Environment.GetEnvironmentVariable("CFM_TEST_GAME")??"";opt.Text=Environment.GetEnvironmentVariable("CFM_TEST_OPT")??"";}finally{loading=false;}music.OptDirectory=opt.Text;}
   if(args.Contains("--compact")){Width=1120;Height=750;}
   await ReloadCatalog();
   var p=new byte[600*800*4];for(int y=0;y<800;y++)for(int x=0;x<600;x++){int i=(y*600+x)*4;p[i]=(byte)(80+x/5);p[i+1]=(byte)(90+y/6);p[i+2]=190;p[i+3]=(byte)((x-300)*(x-300)/4+(y-400)*(y-400)/5<25000?255:0);}
   var bm=BitmapSource.Create(600,800,96,96,PixelFormats.Bgra32,null,p,2400);bm.Freeze();foreach(var s in current!.States.Values){s.Image=bm;s.SourceName="测试素材.png";s.Center();}
   cardName.Text="星空 · 测试 Boss";CommitEditor();DuplicateCard();DuplicateCard();library.SelectAll();
   var bytes=ProjectCodec.Encode(SnapshotProject());var restored=ProjectCodec.Decode(bytes);if(restored.Cards.Count!=3||restored.Cards[0].Crops.Count!=6)throw new Exception("项目恢复失败。");
   var restoredCard=new EditableCard(restored.Cards[0]);if(!restoredCard.States["card"].Rgba().Pixels.SequenceEqual(cards[0].States["card"].Rgba().Pixels))throw new Exception("重开项目的裁剪像素不同。");
   var testPixels=new byte[99*80*4];for(int i=0;i<testPixels.Length;i+=4){testPixels[i+2]=255;testPixels[i+3]=255;}
   var small=BitmapSource.Create(99,80,96,96,PixelFormats.Bgra32,null,testPixels,396);small.Freeze();
   var cropTest=new CropState(Assets.All.Single(x=>x.Key=="iconSmall")){Image=small};cropTest.Center();var cropped=cropTest.Rgba();
   for(int y=0;y<100;y++)for(int x=0;x<100;x++){int i=(y*100+x)*4;byte expected=(byte)(y>=10&&y<90&&x<99?255:0);if(cropped.Pixels[i+3]!=expected)throw new Exception("透明补边像素范围错误。");if(expected==255&&cropped.Pixels[i]!=255)throw new Exception("补边改变了图像颜色。");}
   var widePixels=new byte[120*80*4];for(int y=0;y<80;y++)for(int x=0;x<120;x++){int i=(y*120+x)*4;widePixels[i+2]=(byte)x;widePixels[i+3]=255;}
   cropTest.Image=BitmapSource.Create(120,80,96,96,PixelFormats.Bgra32,null,widePixels,480);cropTest.Center();var wide=cropTest.Rgba();if(wide.Pixels[(10*100)*4]!=10||wide.Pixels[(89*100+99)*4]!=109)throw new Exception("超宽图片未等量居中裁剪。");
   VerifyQuickCropWorkflow();MusicEditorPanel.VerifyRefreshIsolation();MusicEditorPanel.VerifyEditorWorkflow();
   tabs.SelectedIndex=0;await Task.Delay(100);UpdateLayout();
   if(!args.Contains("--preview-test")){
   var firstRow=cards[0].Item.TransformToAncestor(library).TransformBounds(new Rect(cards[0].Item.RenderSize));
   var secondRow=cards[1].Item.TransformToAncestor(library).TransformBounds(new Rect(cards[1].Item.RenderSize));
   if(Math.Abs(firstRow.Y-secondRow.Y)>2)throw new Exception("卡片库没有显示两列；库 "+library.RenderSize+"；第一项 "+firstRow+"；第二项 "+secondRow);
   var visibleCrop=(FrameworkElement)canvas.Parent;var cropBounds=canvas.TransformToAncestor(visibleCrop).TransformBounds(new Rect(canvas.RenderSize));
   if(cropBounds.Bottom>visibleCrop.ActualHeight+1)throw new Exception("裁剪画布被父容器截断；画布 "+cropBounds+"；容器 "+visibleCrop.RenderSize);
   var first=cards[0].Item.TransformToAncestor(library).TransformBounds(new Rect(cards[0].Item.RenderSize));var second=cards[1].Item.TransformToAncestor(library).TransformBounds(new Rect(cards[1].Item.RenderSize));
   first.Union(second);library.SelectRectangle(first,[]);if(library.SelectedItems.Count!=2)throw new Exception("框选两张卡片失败，实际 "+library.SelectedItems.Count+" 张；选区 "+first+"；卡片 "+string.Join(" / ",cards.Select(x=>x.Item.TransformToAncestor(library).TransformBounds(new Rect(x.Item.RenderSize)).ToString())));
   library.SelectAll();cards[0].Item.BringIntoView();
   }
   if(args.Contains("--music")){music.LoadXml(Environment.GetEnvironmentVariable(args.Contains("--remaster")?"CFM_TEST_REMASTER_XML":"CFM_TEST_MUSIC_XML")??throw new InvalidOperationException("请设置测试 Music.xml 路径环境变量。"));tabs.SelectedIndex=1;}
   if(args.Contains("--preview-test")){await music.VerifyLoadedPreviews();music.ShowBossForPreview();}
   await Task.Delay(250);UpdateLayout();var render=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);render.Render(this);File.WriteAllBytes(args[1],ImageFiles.Png(render));
  }catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());UpdateLayout();var failureImage=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);failureImage.Render(this);File.WriteAllBytes(args[1],ImageFiles.Png(failureImage));}finally{dirty=false;Close();}
 }
}
sealed class EditableCard
{
 public CardDraft Draft{get;set;}
 public OptCard? Installed{get;init;}
 readonly HashSet<string> attemptedImages=[];
 string imageWarning="";
 public Dictionary<string,CropState> States{get;}=Assets.All.ToDictionary(x=>x.Key,x=>new CropState(x));
 public ListBoxItem Item{get;}
 readonly Image image=new(){Height=108,Width=104,Stretch=Stretch.Uniform};
 readonly TextBlock name=new(){TextWrapping=TextWrapping.Wrap,MaxHeight=36,Foreground=Brushes.White,FontSize=12};
 readonly TextBlock id=new(){Foreground=Theme.Accent,FontSize=11};
 public EditableCard(CardDraft draft){
  Draft=draft;var cache=new Dictionary<string,BitmapSource>();
  foreach(var pair in draft.Crops){if(pair.Value.Png.Length==0)continue;var c=pair.Value;string key=c.ImageId.Length==0?pair.Key:c.ImageId;if(!cache.TryGetValue(key,out var bm)){bm=ImageFiles.Read(c.Png);cache[key]=bm;}var s=States[pair.Key];s.Image=bm;s.SourceName=c.SourceName;s.Transform=c.Transform;}
  var content=new StackPanel();content.Children.Add(image);content.Children.Add(name);content.Children.Add(id);Item=new ListBoxItem{Tag=this,Content=new Border{Background=Theme.PanelBrush,Padding=new Thickness(7),CornerRadius=new CornerRadius(5),Child=content},Width=126,Margin=new Thickness(3),Padding=new Thickness(2)};RefreshTile();
 }
 public void ApplyQuickCrop(string key){
  if(key is not ("card" or "icon"))throw new InvalidOperationException("快速模式只能编辑头像组或卡面组。");
  var master=States[key];if(master.Image==null)return;
  // 先固定用户已裁好的画面，派生尺寸只缩放和补透明边。
  var cropped=master.Render();
  string[] targets=key=="icon"?["iconSmall"]:["cardSmall","battle","portrait"];
  foreach(var target in targets){var state=States[target];state.Image=cropped;state.SourceName=master.SourceName+" · 快速生成";state.Fit(false);}
  RefreshTile();
 }
 public List<string> MissingAssetLabels()=>Assets.All.Where(x=>States[x.Key].Image==null).Select(x=>x.Label).ToList();
 public void LoadInstalledImages(bool thumbnailOnly=false){
  if(Installed==null)return;
  foreach(var spec in Assets.All.Where(x=>!thumbnailOnly||x.Key=="cardSmall")){
   if(!attemptedImages.Add(spec.Key))continue;
   try{
    string path=Path.Combine(Installed.OptDirectory,"assets",spec.BundleName(Draft.CardId));
    if(!File.Exists(path))continue;PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
    if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("图片资源为链接");
    var info=BundleCodec.Inspect(File.ReadAllBytes(path));var pixels=info.RgbaTopDown;
    for(int i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);
    var bm=BitmapSource.Create(info.Width,info.Height,96,96,PixelFormats.Bgra32,null,pixels,info.Width*4);bm.Freeze();
    States[spec.Key].Image=bm;States[spec.Key].SourceName=Path.GetFileName(path);States[spec.Key].Fit(false);
   }catch(Exception ex){imageWarning="部分素材无法预览："+ex.Message;}
  }
  RefreshTile();
 }
 public void RefreshTile(){name.Text=Draft.CardName;id.Text=$"ID {Draft.CardId} · {Draft.Attribute}\n"+(Installed==null?$"草稿 · 素材 {6-MissingAssetLabels().Count} / 6":"包内 · "+Path.GetFileName(Installed.OptDirectory));var state=States["card"].Image==null?States["cardSmall"]:States["card"];image.Source=state.Image==null?null:state.Thumbnail();Item.ToolTip=Installed==null?"项目草稿":Installed.CardPath+(imageWarning==""?"":"\n"+imageWarning);}
}
static class ImageFiles
{
 public static BitmapSource Read(string path)=>Read(File.ReadAllBytes(path));
 public static BitmapSource Read(byte[] bytes){
  using var source=new MemoryStream(bytes,false);var decoder=BitmapDecoder.Create(source,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);int w=decoder.Frames[0].PixelWidth,h=decoder.Frames[0].PixelHeight;
  if(w<1||h<1||(long)w*h>64_000_000)throw new InvalidDataException("图片超过 6400 万像素，请先缩小。");source.Position=0;var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;
  if(Math.Max(w,h)>4096)b.DecodePixelWidth=Math.Max(1,(int)(w*4096.0/Math.Max(w,h)));b.StreamSource=source;b.EndInit();b.Freeze();return b;
 }
 public static byte[] Png(BitmapSource source){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
}
static class Theme
{
 public static readonly SolidColorBrush Background=new(Color.FromRgb(14,20,29)),PanelBrush=new(Color.FromRgb(24,33,46)),Dark=new(Color.FromRgb(11,18,27)),Muted=new(Color.FromRgb(160,178,198)),Accent=new(Color.FromRgb(78,225,183));
 public static TextBlock Text(string text,double size=13,Brush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Brushes.White,TextWrapping=TextWrapping.Wrap};
 public static TextBlock Hint(string text){var t=Text(text,11,Muted);t.Margin=new Thickness(0,7,0,7);return t;}
 public static Border Panel(UIElement content)=>new(){Background=PanelBrush,CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Child=content};
 public static StackPanel Row(params UIElement[] elements){var p=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,0)};foreach(var e in elements)p.Children.Add(e);return p;}
 public static Button Button(string text,bool primary=false)=>new(){Content=text,Background=primary?Accent:new SolidColorBrush(Color.FromRgb(43,62,82)),Foreground=primary?Background:Brushes.White,Margin=new Thickness(0,4,6,4),Padding=new Thickness(10,7,10,7),BorderThickness=new Thickness(0),Cursor=Cursors.Hand};
 public static void Install(ResourceDictionary resources){
  var t=new Style(typeof(TextBox));t.Setters.Add(new Setter(Control.BackgroundProperty,Dark));t.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));t.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(60,77,95))));t.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,5,7,5)));resources.Add(typeof(TextBox),t);
  var combo=new Style(typeof(ComboBox));combo.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5)));combo.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.Black));resources.Add(typeof(ComboBox),combo);
  const string markup="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="{x:Type Button}">
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Button}">
   <Border x:Name="Frame" CornerRadius="6" Background="{TemplateBinding Background}" BorderBrush="#3D526B" BorderThickness="1" Padding="{TemplateBinding Padding}">
    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#58DFBD"/></Trigger>
    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.75"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="{x:Type ComboBoxItem}">
  <Setter Property="Foreground" Value="#E5EDF7"/><Setter Property="Padding" Value="10,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBoxItem}">
   <Border x:Name="ItemFrame" Background="Transparent" Padding="{TemplateBinding Padding}" CornerRadius="4"><ContentPresenter/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="ItemFrame" Property="Background" Value="#2B4657"/></Trigger>
    <Trigger Property="IsSelected" Value="True"><Setter TargetName="ItemFrame" Property="Background" Value="#21473F"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="{x:Type ComboBox}">
  <Setter Property="Background" Value="#111D2B"/><Setter Property="Foreground" Value="#E5EDF7"/><Setter Property="BorderBrush" Value="#35465D"/>
  <Setter Property="MinHeight" Value="32"/><Setter Property="MaxDropDownHeight" Value="330"/>
  <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBox}">
   <Grid>
    <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
     <ToggleButton.Template><ControlTemplate TargetType="{x:Type ToggleButton}">
      <Border x:Name="ToggleFrame" CornerRadius="5" Background="#111D2B" BorderBrush="#35465D" BorderThickness="1">
       <Path Data="M 0 0 L 4 4 L 8 0" Stroke="#97B0C8" StrokeThickness="1.5" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/>
      </Border>
      <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ToggleFrame" Property="BorderBrush" Value="#58DFBD"/></Trigger></ControlTemplate.Triggers>
     </ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter x:Name="Selection" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" Margin="10,5,30,5" VerticalAlignment="Center" TextElement.Foreground="#E5EDF7"/>
    <TextBox x:Name="PART_EditableTextBox" Visibility="Hidden" Background="#111D2B" Foreground="#E5EDF7" CaretBrush="#58DFBD" BorderThickness="0" Padding="0" Margin="10,4,30,4" VerticalContentAlignment="Center" IsReadOnly="{TemplateBinding IsReadOnly}"/>
    <Popup x:Name="PART_Popup" AllowsTransparency="True" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" Focusable="False" PopupAnimation="Fade">
     <Border Background="#172535" BorderBrush="#3D526B" BorderThickness="1" CornerRadius="6" Padding="4" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}">
      <ScrollViewer MaxHeight="330" CanContentScroll="True" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
     </Border>
    </Popup>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsEditable" Value="True"><Setter TargetName="Selection" Property="Visibility" Value="Hidden"/><Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="{x:Type TabItem}">
  <Setter Property="Padding" Value="16,9"/><Setter Property="Margin" Value="0,0,5,0"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type TabItem}">
   <Border x:Name="TabFrame" Background="#1B2B3E" CornerRadius="6,6,0,0" Padding="{TemplateBinding Padding}">
    <ContentPresenter x:Name="Caption" ContentSource="Header" TextElement.Foreground="#B8CAE0"/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property="IsSelected" Value="True"><Setter TargetName="TabFrame" Property="Background" Value="#51DAB8"/><Setter TargetName="Caption" Property="TextElement.Foreground" Value="#0F2831"/></Trigger>
    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="TabFrame" Property="Opacity" Value="0.85"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="{x:Type Expander}">
  <Setter Property="Foreground" Value="#D8E4F1"/><Setter Property="Padding" Value="4"/>
 </Style>
</ResourceDictionary>
""";
  var palette=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(markup);foreach(var key in palette.Keys)resources[key]=palette[key];
  var check=new Style(typeof(CheckBox));check.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));resources.Add(typeof(CheckBox),check);
 }
}
sealed class CardSelectionList:ListBox
{
 Point? start;
 HashSet<object> before=[];
 SelectionAdorner? adorn;
 public bool IsMarqueeSelecting{get;private set;}
 public Action? SelectionFinished{get;set;}
 protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e){
  if(FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject)!=null){base.OnPreviewMouseLeftButtonDown(e);return;}start=e.GetPosition(this);before=Keyboard.Modifiers.HasFlag(ModifierKeys.Control)?SelectedItems.Cast<object>().ToHashSet():[];base.OnPreviewMouseLeftButtonDown(e);
 }
 protected override void OnPreviewMouseMove(MouseEventArgs e){
  base.OnPreviewMouseMove(e);if(start is not Point point||e.LeftButton!=MouseButtonState.Pressed)return;var now=e.GetPosition(this);if(!IsMarqueeSelecting&&(now-point).Length<6)return;
  if(!IsMarqueeSelecting){IsMarqueeSelecting=true;CaptureMouse();adorn=new SelectionAdorner(this);AdornerLayer.GetAdornerLayer(this)?.Add(adorn);}
  var rect=new Rect(point,now);SelectRectangle(rect,before);
  if(adorn!=null){adorn.Bounds=rect;adorn.InvalidateVisual();}e.Handled=true;
 }
 public void SelectRectangle(Rect rect,IEnumerable<object> preserved){
  // 先固定命中结果，避免选择引起的滚动或布局更新改变后续卡片坐标。
  var hits=Items.Cast<object>().Where(item=>ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c&&rect.IntersectsWith(c.TransformToAncestor(this).TransformBounds(new Rect(c.RenderSize)))).ToArray();
  var keep=preserved.ToArray();bool was=IsMarqueeSelecting;IsMarqueeSelecting=true;
  try{SelectedItems.Clear();foreach(var item in keep.Concat(hits).Distinct())SelectedItems.Add(item);}
  finally{IsMarqueeSelecting=was;}
  if(!was)SelectionFinished?.Invoke();
 }
 protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e){base.OnPreviewMouseLeftButtonUp(e);EndSelection();}
 protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);EndSelection();}
 void EndSelection(){bool was=IsMarqueeSelecting;start=null;IsMarqueeSelecting=false;if(adorn!=null){AdornerLayer.GetAdornerLayer(this)?.Remove(adorn);adorn=null;}if(IsMouseCaptured)ReleaseMouseCapture();if(was)SelectionFinished?.Invoke();}
 static T? FindAncestor<T>(DependencyObject? d)where T:DependencyObject{while(d!=null){if(d is T t)return t;d=d is Visual?VisualTreeHelper.GetParent(d):null;}return null;}
}
sealed class SelectionAdorner:UIElementAdorner
{
 public Rect Bounds{get;set;}
 public SelectionAdorner(UIElement element):base(element){IsHitTestVisible=false;}
 protected override void OnRender(DrawingContext dc)=>dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50,78,225,183)),new Pen(Theme.Accent,1),Bounds);
}
abstract class UIElementAdorner(UIElement element):Adorner(element);
