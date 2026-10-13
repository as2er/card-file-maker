using System.Windows.Media.Imaging;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
namespace CardFileMaker;

public sealed class MusicEditorPanel:UserControl
{
 readonly Func<IReadOnlyList<BossCardChoice>> getCards;
 readonly Action<string> log;
 readonly PathHistoryStore pathHistory;
 public Func<BossCardChoice,BitmapSource?>? DraftPreview {get;set;}
 Image stagePreview=new(),cardPreview=new();
 TextBlock stagePreviewHint=new(),cardPreviewHint=new(),stagePreviewName=new(),cardPreviewName=new();
 Button bindStageButton=new();
 int previewGeneration;
 string StagePreviewPath(int id)=>Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"stage-previews",$"stage{id:D4}.png");

 readonly TabControl tabs=new();
 readonly Grid fields=new();
 readonly TextBox raw=new(){AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
 string outputDirectory="";
 readonly Dictionary<string,ComboBox> references=new();
 readonly Dictionary<string,TextBox> textFields=new();
 readonly Dictionary<string,ComboBox> booleanFields=new();
 readonly List<(ComboBox Whole,ComboBox Fraction,TextBox Path)> difficultyFields=new();
 readonly List<TextBlock> difficultyTitles=new();
 MusicDocument document=MusicDocument.CreateNew();
 GameCatalog? catalog;
 List<BossCardChoice> allCards=[];
 ComboBox sourceGroups=ChoiceInput(),characters=ChoiceInput(),cards=ChoiceInput(),attribute=ChoiceInput();
 TextBlock bossHint=new();
 ScrollViewer? songScroll;
 FrameworkElement? bossAnchor;
 bool loading,rawDirty,dirty;
 string optDirectory="";
 static readonly string[] Groups=["官方游戏包","自制 opt","当前项目"];
 static readonly string[] Difficulties=["BASIC · 初级","ADVANCED · 高级","EXPERT · 专家","MASTER · 大师","LUNATIC · 特殊"];
 static readonly string[] ReferenceFields=["MusicRightsName","MusicSourceName","Genre","FirstChapterName","VersionID","StageID"];
 static readonly AttributeChoice[] Attributes=[new("Fire","Fire（火）"),new("Aqua","Aqua（水）"),new("Leaf","Leaf（叶）")];
 sealed record AttributeChoice(string Value,string Label){public override string ToString()=>Label;}

 public string OutputDirectory {get=>outputDirectory;set=>outputDirectory=value??"";}
 public string OptDirectory {get=>optDirectory;set{optDirectory=value??"";if(!loading)TryRefreshCards();}}
 public bool HasUnsavedChanges=>dirty;
 public bool IsRawDraft=>rawDirty;
 public Action? ReloadRequested {get;set;}
 public string? SourcePath=>document.SourcePath;
  public string? SourceHash=>document.SourceHash;
 public MusicEditorPanel(Func<IReadOnlyList<BossCardChoice>> getCards,Action<string> log,PathHistoryStore? pathHistory=null)
 {
  this.pathHistory=pathHistory??new PathHistoryStore(AppContext.BaseDirectory,log);
  this.getCards=getCards;this.log=log;Foreground=Brushes.White;Background=Theme.Background;
  tabs.Background=Theme.Background;raw.Background=Theme.Dark;raw.Foreground=Brushes.White;
  var dock=new DockPanel();Content=dock;
  var top=new StackPanel{Margin=new Thickness(0,0,0,3)};DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
  var buttons=new WrapPanel();top.Children.Add(buttons);
  AddButton(buttons,"空白新建",()=>{if(AllowDiscard())NewDocument();});
  AddButton(buttons,"导入 Music.xml",()=>{if(!AllowDiscard())return;var picker=new OpenFileDialog{Filter="Music XML|*.xml",InitialDirectory=this.pathHistory.Get(PathPurpose.MusicImport,AutoSaveStore.DirectoryFor(AppContext.BaseDirectory))};if(picker.ShowDialog()==true){LoadXml(picker.FileName);this.pathHistory.Remember(PathPurpose.MusicImport,Path.GetDirectoryName(picker.FileName)!);}});
  AddButton(buttons,"刷新引用",()=>{if(ReloadRequested is { } reload)reload();else{RefreshReferences();TryRefreshCards();}});
  AddButton(buttons,"保存",Save);
   AddButton(buttons,"覆盖原 XML",SaveOriginal);
  var intro=Theme.Hint("音源和 ogkr 谱面需另行准备；额外难度、奖励及未知字段可在原始 XML 中编辑。");intro.Margin=new Thickness(2,3,0,3);top.Children.Add(intro);
  fields.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});fields.ColumnDefinitions.Add(new(){Width=new GridLength(12)});fields.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  tabs.Items.Add(new TabItem{Header="歌曲与谱面",Foreground=Brushes.Black,Content=fields});
  var rawDock=new DockPanel();var rawTop=new StackPanel();DockPanel.SetDock(rawTop,Dock.Top);rawDock.Children.Add(rawTop);
  rawTop.Children.Add(Theme.Hint("应用 XML 时会固定发布日期，并将 0/1 布尔值转为 true/false。未完成的原始 XML 会保留在草稿中。"));
  var rawButtons=new WrapPanel();AddButton(rawButtons,"应用原始 XML",()=>{ParseRaw();tabs.SelectedIndex=0;});rawTop.Children.Add(rawButtons);rawDock.Children.Add(raw);
  tabs.Items.Add(new TabItem{Header="高级原始 XML",Foreground=Brushes.Black,Content=rawDock});dock.Children.Add(tabs);
  raw.TextChanged+=(_,_)=>{if(!loading){rawDirty=true;dirty=true;}};
  tabs.SelectionChanged+=(_,e)=>
  {
   if(e.Source!=tabs||loading)return;
   if(tabs.SelectedIndex!=0||!rawDirty)return;
   try{ParseRaw();}catch(Exception ex){WithLoading(()=>tabs.SelectedIndex=1);MessageBox.Show(ex.Message,"Music XML",MessageBoxButton.OK,MessageBoxImage.Warning);}
  };
  IsVisibleChanged+=(_,_)=>{if(IsVisible)TryRefreshCards();};NewDocument();
 }
 static TextBox CreateTextInput()=>new(){MinWidth=80,Background=Theme.Dark,Foreground=Brushes.White,VerticalContentAlignment=VerticalAlignment.Center};
 static ComboBox ChoiceInput(bool editable=false)=>new(){IsEditable=editable,IsTextSearchEnabled=!editable,MinWidth=90,MaxDropDownHeight=330,Foreground=Brushes.White,VerticalContentAlignment=VerticalAlignment.Center};
 void WithLoading(Action action){var previous=loading;loading=true;try{action();}finally{loading=previous;}}
 void AddButton(Panel panel,string text,Action action)
 {
  var button=Theme.Button(text);button.Click+=(_,_)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Music 编辑器",MessageBoxButton.OK,MessageBoxImage.Warning);}};panel.Children.Add(button);
 }
 bool AllowDiscard()=>!dirty||MessageBox.Show("当前 Music 有未保存的修改，是否放弃？","Music",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
 public void NewDocument(){document=MusicDocument.CreateNew();dirty=false;rawDirty=false;Render();TryRefreshCards(true);}
 public void LoadXml(string path){document=MusicDocument.Load(path);dirty=false;rawDirty=false;Render();TryRefreshCards(true);log("已导入 Music："+Path.GetFullPath(path));}
 public void MarkSaved()=>dirty=false;
 public void SetCatalog(GameCatalog value){catalog=value;RefreshReferences();TryRefreshCards(true);}
 public string CaptureXml()=>rawDirty?raw.Text:document.Xml.ToString();
  public void RestoreXml(string xml,string? sourcePath,bool preserveRaw=false,string? sourceHash=null)
 {
   if(preserveRaw){RestoreRawDraft(xml,sourcePath);document.SourceHash=sourceHash;return;}
  try
  {
   document=Parse(xml,sourcePath);rawDirty=false;Render();TryRefreshCards(true);
  }
  catch(Exception ex) when(ex is XmlException or InvalidDataException)
  {
   RestoreRawDraft(xml,sourcePath);
  }
   document.SourceHash=sourceHash;dirty=true;
 }
 void RestoreRawDraft(string xml,string? sourcePath)
 {
  document=MusicDocument.CreateNew();document.SourcePath=sourcePath;Render();
  WithLoading(()=>{raw.Text=xml;tabs.SelectedIndex=1;});rawDirty=true;dirty=true;TryRefreshCards();
 }
 static MusicDocument Parse(string xml,string? sourcePath)
 {
  using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
  return new MusicDocument(XDocument.Load(reader)){SourcePath=sourcePath};
 }
  void ParseRaw(){var parsed=Parse(raw.Text,document.SourcePath);parsed.SourceHash=document.SourceHash;if(parsed.Get("Name/str")!=document.Get("Name/str"))parsed.Set("Name/str",parsed.Get("Name/str"));document=parsed;rawDirty=false;Render();TryRefreshCards(true);}
 public XDocument Snapshot(){if(rawDirty)ParseRaw();return new XDocument(document.Xml);}
 void Changed(){dirty=true;if(!rawDirty)WithLoading(()=>raw.Text=document.Xml.ToString());}

 void Render()
 {
  WithLoading(()=>
  {
   fields.Children.Clear();references.Clear();textFields.Clear();booleanFields.Clear();difficultyFields.Clear();difficultyTitles.Clear();
   var left=new StackPanel();var right=new StackPanel();
   var leftScroll=new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   var rightScroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   var leftHost=new DockPanel();var previewArea=new StackPanel{Margin=new Thickness(0,8,0,0)};AddPreviewArea(previewArea);DockPanel.SetDock(previewArea,Dock.Bottom);leftHost.Children.Add(previewArea);leftHost.Children.Add(leftScroll);
   songScroll=leftScroll;fields.Children.Add(leftHost);Grid.SetColumn(rightScroll,2);fields.Children.Add(rightScroll);
   var song=Section(left,"歌曲信息");
   AddText(song,"歌曲 ID","Name/id");AddText(song,"数据目录名","dataName",true);AddText(song,"歌曲名称","Name/str");
   AddText(song,"艺术家","ArtistName/str");AddReference(song,"音源","MusicSourceName",true);
   var referenceHint=Theme.Hint("艺术家直接填写名称；音源输入“ID · 名称”。");referenceHint.Margin=new Thickness(104,2,0,3);song.Children.Add(referenceHint);
   AddReference(song,"分类","Genre");
   var metadata=Foldout(song,"排序与补充资料");AddText(metadata,"排序名称","NameForSort");AddReference(metadata,"版权","MusicRightsName");AddReference(metadata,"首次章节","FirstChapterName");
   var sortRow=new Grid();sortRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});sortRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
   var sort=CreateText("SortOrder");sortRow.Children.Add(sort);var random=Theme.Button("随机排序号");random.Click+=(_,_)=>RandomizeSort();Grid.SetColumn(random,1);sortRow.Children.Add(random);random.Padding=new Thickness(8,4,8,4);random.Margin=new Thickness(6,0,0,0);AddRow(metadata,"排序号",sortRow);
   var boss=Section(left,"舞台与 Boss");bossAnchor=boss;AddReference(boss,"舞台","StageID");
   sourceGroups=ChoiceInput();sourceGroups.ItemsSource=Groups;sourceGroups.SelectedItem="当前项目";
   characters=ChoiceInput();cards=ChoiceInput();attribute=ChoiceInput();attribute.ItemsSource=Attributes;
   attribute.SelectedItem=Attributes.FirstOrDefault(x=>x.Value==document.Get("WaveAttribute/AttributeType"));
   AddRow(boss,"角色数据来源",sourceGroups);AddRow(boss,"角色",characters);AddRow(boss,"Boss 卡片",cards);AddRow(boss,"波次属性",attribute);
   bossHint=Theme.Hint("");bossHint.Margin=new Thickness(104,3,0,5);boss.Children.Add(bossHint);
   sourceGroups.SelectionChanged+=(_,_)=>{if(loading)return;ClearBoss();RefreshBossSelectors(false);};
   characters.SelectionChanged+=(_,_)=>{if(loading)return;ClearBoss();RefreshCardSelector();};
   cards.SelectionChanged+=(_,_)=>{if(!loading&&cards.SelectedItem is BossCardChoice choice)SelectBoss(choice);};
   attribute.SelectionChanged+=(_,_)=>{if(!loading&&attribute.SelectedItem is AttributeChoice choice){document.SetWaveAttribute(choice.Value);Changed();}};
   AddText(boss,"Boss 等级","BossLevel");AddText(boss,"锁定 HP 系数","BossLockHpCoef");AddText(boss,"语音编号","BossVoiceNo");
   var state=Foldout(left,"版本与解锁设置",true);AddReference(state,"版本","VersionID");AddText(state,"发布日期","ReleaseVersion",true);
   AddBoolean(state,"初始拥有","PossessingFromTheBeginning");AddBoolean(state,"初始锁定","IsLockedAtTheBeginning");AddBoolean(state,"奖励曲","IsBonusTrack");AddBoolean(state,"EP 释放","EpReleaseFlag");
   AddText(state,"解锁花费","CostToUnlock");AddText(state,"完成奖励","FinishBonus");
   var modes=Section(right,"难度模式");var modeRow=new Grid();modeRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});modeRow.ColumnDefinitions.Add(new(){Width=new GridLength(14)});modeRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
   var lunaticMode=new StackPanel();var remasterMode=new StackPanel();modeRow.Children.Add(lunaticMode);Grid.SetColumn(remasterMode,2);modeRow.Children.Add(remasterMode);modes.Children.Add(modeRow);
   AddBoolean(lunaticMode,"LUNATIC 模式","IsLunatic");AddBoolean(remasterMode,"Re:MASTER 模式","IsReMaster");
   for(var index=0;index<5;index++)AddDifficulty(right,index);
   if(document.Xml.Root!.Element("FumenData")!.Elements("FumenData").Count()>5)right.Children.Add(Theme.Hint("此文件含额外难度，已保留；请在高级原始 XML 中编辑。"));
   UpdateDifficultyMode();RefreshReferences();raw.Text=document.Xml.ToString();rawDirty=false;
  });
  UpdatePreviews();
 }
 static StackPanel Section(Panel parent,string title)
 {
  var body=new StackPanel();var heading=Theme.Text(title,13,Theme.Accent);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,5);body.Children.Add(heading);
  var border=Theme.Panel(body);border.Padding=new Thickness(10);border.Margin=new Thickness(0,0,0,8);parent.Children.Add(border);return body;
 }
 static StackPanel Foldout(Panel parent,string title,bool boxed=false)
 {
  var body=new StackPanel{Margin=new Thickness(0,5,0,0)};
  var expander=new Expander{Header=title,Foreground=Theme.Muted,Content=body,IsExpanded=false,Margin=new Thickness(0,4,0,0),Padding=new Thickness(0)};
  if(boxed){var border=Theme.Panel(expander);border.Padding=new Thickness(10,7,10,7);border.Margin=new Thickness(0,0,0,8);parent.Children.Add(border);}else parent.Children.Add(expander);
  return body;
 }
 public void ShowBossForPreview()
 {
  if(rawDirty||songScroll==null||bossAnchor==null)return;
  tabs.SelectedIndex=0;UpdateLayout();var position=bossAnchor.TransformToAncestor(songScroll).Transform(new Point(0,0));songScroll.ScrollToVerticalOffset(Math.Max(0,songScroll.VerticalOffset+position.Y-8));UpdateLayout();
 }
 static void AddRow(Panel parent,string label,UIElement input)
 {
  var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new(){Width=new GridLength(104)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var caption=Theme.Text(label,12);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new Thickness(0,0,8,0);row.Children.Add(caption);Grid.SetColumn(input,1);row.Children.Add(input);parent.Children.Add(row);
 }
 void AddText(Panel parent,string label,string path,bool readOnly=false)=>AddRow(parent,label,CreateText(path,readOnly));
 TextBox CreateText(string path,bool readOnly=false)
 {
  var input=CreateTextInput();input.Text=document.Get(path);input.IsReadOnly=readOnly;textFields[path]=input;
  input.TextChanged+=(_,_)=>
  {
   if(loading||readOnly)return;document.Set(path,input.Text);
   if(path=="Name/str"&&textFields.TryGetValue("NameForSort",out var nameSort))WithLoading(()=>nameSort.Text=document.Get("NameForSort"));
   if(path=="Name/id"&&textFields.TryGetValue("dataName",out var dataName))WithLoading(()=>dataName.Text=document.Get("dataName"));
   Changed();
  };return input;
 }
 void AddBoolean(Panel parent,string label,string path)
 {
  var input=ChoiceInput();input.ItemsSource=new[]{"true","false"};input.SelectedItem=document.Get(path);booleanFields[path]=input;
  if(path=="IsBonusTrack")input.ToolTip="附加曲目标记。本游戏包中的官方样本为角色独唱版；独立于难度、初始拥有和锁定状态。";
  input.SelectionChanged+=(_,_)=>{
   if(loading||input.SelectedItem is not string value)return;
   if(path is "IsLunatic" or "IsReMaster"){
    var mode=value=="true"?(path=="IsLunatic"?MusicChartMode.Lunatic:MusicChartMode.ReMaster):MusicChartMode.Normal;
    document.SetChartMode(mode);UpdateDifficultyMode();
   }else document.Set(path,value);
   Changed();
  };AddRow(parent,label,input);
 }
 void UpdateDifficultyMode()
 {
  document.NormalizeInactiveConstants();
  WithLoading(()=>{
   if(booleanFields.TryGetValue("IsLunatic",out var lunatic))lunatic.SelectedItem=document.ChartMode==MusicChartMode.Lunatic?"true":"false";
   if(booleanFields.TryGetValue("IsReMaster",out var remaster))remaster.SelectedItem=document.ChartMode==MusicChartMode.ReMaster?"true":"false";
   for(int i=0;i<difficultyFields.Count;i++){
    var inputs=difficultyFields[i];bool enabled=document.IsChartEnabled(i);
    inputs.Whole.IsEnabled=enabled;inputs.Fraction.IsEnabled=enabled;inputs.Path.IsEnabled=enabled;
    if(!enabled){inputs.Whole.SelectedItem=0;inputs.Fraction.SelectedItem=0;}
    difficultyTitles[i].Text=i==4&&document.ChartMode==MusicChartMode.ReMaster?"Re:MASTER · 特殊":Difficulties[i];
    difficultyTitles[i].Opacity=enabled?1:.5;
    inputs.Path.ToolTip=enabled?"填写谱面文件的相对路径，例如 9001_00.ogkr 或 charts/9001_00.ogkr。":"当前模式不使用此难度，定数为 0 / 0。谱面名称保留在草稿，保存 XML 时留空。";
   }
  });
 }
 void AddReference(Panel parent,string label,string path,bool editable=false)
 {
  var input=ChoiceInput(editable);references[path]=input;
  if(editable)input.ToolTip="选择已有引用，或输入 ID · 名称";
  input.SelectionChanged+=(_,_)=>
  {
   if(loading||input.SelectedItem is not ReferenceChoice choice)return;
   document.Set(path+"/id",choice.Id.ToString(CultureInfo.InvariantCulture));document.Set(path+"/str",choice.Name);Changed();if(path=="StageID")UpdatePreviews();
  };
  if(editable)input.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,e)=>
  {
   if(loading||e.OriginalSource is not TextBox editor)return;
   var text=editor.Text;var split=text.IndexOf('·');
   if(split>=0){document.Set(path+"/id",text[..split].Trim());document.Set(path+"/str",text[(split+1)..].Trim());}
   else if(int.TryParse(text.Trim(),out var typedId)){document.Set(path+"/id",text.Trim());document.Set(path+"/str","");}
   else{document.Set(path+"/str",text.Trim());}

   Changed();
  }));
  AddRow(parent,label,input);
 }
 void RefreshReferences()
 {
  WithLoading(()=>
  {
   foreach(var field in ReferenceFields)
   {
    if(!references.TryGetValue(field,out var input))continue;
    var typed=input.IsEditable?input.Text:null;var id=document.Get(field+"/id");var name=document.Get(field+"/str");
    var choices=(catalog?.Get(field)??Array.Empty<ReferenceChoice>()).ToList();
    if(int.TryParse(id,out var value)&&!choices.Any(x=>x.Id==value&&x.Name==name))choices.Add(new ReferenceChoice(value,name,"当前 XML"));
    input.ItemsSource=choices;input.SelectedItem=choices.FirstOrDefault(x=>x.Id.ToString(CultureInfo.InvariantCulture)==id&&x.Name==name);
    if(input.IsEditable)input.Text=string.IsNullOrEmpty(typed)?(string.IsNullOrWhiteSpace(id)?name:id+" · "+name):typed;
   }
  });
 }
 XElement EnsureChart(int index)
 {
  var root=document.Xml.Root!;var holder=root.Element("FumenData");if(holder==null){holder=new XElement("FumenData");root.Add(holder);}
  while(holder.Elements("FumenData").Count()<=index)holder.Add(new XElement("FumenData",new XElement("FumenConstIntegerPart","0"),new XElement("FumenConstFractionalPart","0"),new XElement("FumenFile",new XElement("path",""))));
  return holder.Elements("FumenData").ElementAt(index);
 }
 void AddDifficulty(Panel parent,int index)
 {
  var section=new StackPanel();var border=Theme.Panel(section);border.Padding=new Thickness(10,8,10,8);border.Margin=new Thickness(0,0,0,7);parent.Children.Add(border);
  var chart=EnsureChart(index);var whole=ChoiceInput();var fraction=ChoiceInput();whole.MinWidth=56;fraction.MinWidth=56;
  whole.ToolTip="定数整数：0–20";fraction.ToolTip="定数小数：0、10、20、30、40、50、60、70、80、90";
  whole.ItemsSource=Enumerable.Range(0,21).ToArray();fraction.ItemsSource=Enumerable.Range(0,10).Select(x=>x*10).ToArray();
  if(int.TryParse(chart.Element("FumenConstIntegerPart")?.Value,out var w)&&w>=0&&w<=20)whole.SelectedItem=w;
  if(int.TryParse(chart.Element("FumenConstFractionalPart")?.Value,out var f)&&f>=0&&f<=90&&f%10==0)fraction.SelectedItem=f;
  var heading=new Grid{Margin=new Thickness(0,0,0,4)};heading.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=new GridLength(1.1,GridUnitType.Star)});
  var title=Theme.Text(Difficulties[index],12,Theme.Accent);title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);difficultyTitles.Add(title);
  var constants=new Grid();constants.ColumnDefinitions.Add(new(){Width=new GridLength(32)});constants.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});constants.ColumnDefinitions.Add(new(){Width=new GridLength(14)});constants.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var caption=Theme.Text("定数",11,Theme.Muted);caption.VerticalAlignment=VerticalAlignment.Center;constants.Children.Add(caption);Grid.SetColumn(whole,1);constants.Children.Add(whole);
  var separator=Theme.Text("/",11,Theme.Muted);separator.HorizontalAlignment=HorizontalAlignment.Center;separator.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(separator,2);constants.Children.Add(separator);Grid.SetColumn(fraction,3);constants.Children.Add(fraction);
  Grid.SetColumn(constants,1);heading.Children.Add(constants);section.Children.Add(heading);
  whole.SelectionChanged+=(_,_)=>{if(!loading&&document.IsChartEnabled(index)&&whole.SelectedItem is int value){SetChartValue(chart,"FumenConstIntegerPart",value.ToString(CultureInfo.InvariantCulture));Changed();}};
  fraction.SelectionChanged+=(_,_)=>{if(!loading&&document.IsChartEnabled(index)&&fraction.SelectedItem is int value){SetChartValue(chart,"FumenConstFractionalPart",value.ToString(CultureInfo.InvariantCulture));Changed();}};
  var path=CreateTextInput();path.Text=chart.Element("FumenFile")?.Element("path")?.Value??"";path.ToolTip="填写谱面文件的相对路径，例如 9001_00.ogkr 或 charts/9001_00.ogkr。";
  path.TextChanged+=(_,_)=>{if(loading||!document.IsChartEnabled(index))return;var file=chart.Element("FumenFile");if(file==null){file=new XElement("FumenFile");chart.Add(file);}SetChartValue(file,"path",path.Text);Changed();};AddRow(section,"谱面名称",path);
  difficultyFields.Add((whole,fraction,path));
 }
 static void SetChartValue(XElement chart,string name,string value){var node=chart.Element(name);if(node==null)chart.Add(new XElement(name,value));else node.Value=value;}
 void RandomizeSort()=>textFields["SortOrder"].Text=Random.Shared.Next(200000,1000000).ToString(CultureInfo.InvariantCulture);
 void SelectBoss(BossCardChoice choice)
 {
  var before=document.Xml.ToString();document.SelectBoss(choice);WithLoading(()=>attribute.SelectedItem=Attributes.FirstOrDefault(x=>x.Value==choice.Attribute));if(document.Xml.ToString()!=before)Changed();
  bossHint.Text="已同步所有波次属性："+choice.Attribute;UpdatePreviews();
 }
 void ClearBoss(){document.Set("BossCard/id","");document.Set("BossCard/str","");Changed();UpdatePreviews();}
 void TryRefreshCards(bool followDocument=false)
 {
  try
  {
   allCards=getCards().Concat(catalog?.Cards??Enumerable.Empty<BossCardChoice>()).DistinctBy(x=>(x.Group,x.Id)).ToList();RefreshBossSelectors(followDocument);
   if(followDocument&&!rawDirty&&cards.SelectedItem is BossCardChoice matched)SelectBoss(matched);
  }
  catch(Exception ex){log("刷新 Music 卡片失败："+ex.Message);}
 }
 void RefreshBossSelectors(bool followDocument)
 {
  WithLoading(()=>
  {
   var selectedCharacter=(characters.SelectedItem as ReferenceChoice)?.Id;
   var current=allCards.FirstOrDefault(x=>x.Id.ToString(CultureInfo.InvariantCulture)==document.Get("BossCard/id")&&x.Name==document.Get("BossCard/str"));
   if(followDocument&&current!=null&&Groups.Contains(current.Group)){sourceGroups.SelectedItem=current.Group;selectedCharacter=current.CharaId;}
   var group=sourceGroups.SelectedItem as string??"当前项目";var available=allCards.Where(x=>x.Group==group&&x.CharaId>0).ToList();
   var choices=available.Select(CharacterFor).OfType<ReferenceChoice>().DistinctBy(x=>x.Id).OrderBy(x=>x.Id).ToList();
   characters.ItemsSource=choices;characters.SelectedItem=choices.FirstOrDefault(x=>x.Id==selectedCharacter);
   RefreshCardSelector(followDocument?current:null);
  });
 }
 ReferenceChoice? CharacterFor(BossCardChoice card)
 {
  if(card.CharaId<=0)return null;
  if(card.Group=="当前项目"||catalog==null)return new ReferenceChoice(card.CharaId,card.CharaName,card.Group,card.Source);
  var defined=catalog.Characters.FirstOrDefault(x=>x.Id==card.CharaId&&x.Group==card.Group)
   ??(card.Group=="自制 opt"?catalog.Characters.FirstOrDefault(x=>x.Id==card.CharaId&&x.Group=="官方游戏包"):null);
  return defined==null?null:defined with{Group=card.Group};
 }
 void RefreshCardSelector(BossCardChoice? preferred=null)
 {
  WithLoading(()=>
  {
   var selected=preferred??cards.SelectedItem as BossCardChoice;var group=sourceGroups.SelectedItem as string??"当前项目";var character=characters.SelectedItem as ReferenceChoice;
   var available=allCards.Where(x=>x.Group==group&&character!=null&&x.CharaId==character.Id).OrderBy(x=>x.Id).ToList();
   cards.ItemsSource=available;cards.SelectedItem=available.FirstOrDefault(x=>selected!=null&&x.Id==selected.Id&&x.Group==selected.Group);
   bossHint.Text=character==null?"请先选择此来源中具有关联卡片的角色。":available.Count==0?"此角色没有可用于 Music 的 Boss 卡片。":"选择 Boss 卡片后自动同步所有波次属性。";
  });
  UpdatePreviews();
 }
 void AddPreviewArea(Panel parent)
 {
  var area=new Grid{Margin=new Thickness(0,6,0,10)};
  area.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});area.ColumnDefinitions.Add(new(){Width=new GridLength(12)});area.ColumnDefinitions.Add(new(){Width=new GridLength(132)});
  Border Tile(string title,out Image picture,out TextBlock hint,out TextBlock caption) {
   var stack=new StackPanel();stack.Children.Add(Theme.Text(title,12,Theme.Muted));
   var frame=new Grid{Height=160,Background=Theme.Dark,Margin=new Thickness(0,5,0,5),ClipToBounds=true};
   picture=new Image{Stretch=Stretch.Uniform,Margin=new Thickness(4)};hint=Theme.Hint("请选择");hint.TextAlignment=TextAlignment.Center;hint.HorizontalAlignment=HorizontalAlignment.Center;hint.VerticalAlignment=VerticalAlignment.Center;hint.Margin=new Thickness(8);
   frame.Children.Add(picture);frame.Children.Add(hint);stack.Children.Add(frame);caption=Theme.Text("—",12);caption.TextTrimming=TextTrimming.CharacterEllipsis;stack.Children.Add(caption);
   var captured=picture;frame.MouseLeftButtonUp+=(_,_)=>{if(captured.Source!=null)ShowLargePreview(title,captured.Source);};frame.Cursor=System.Windows.Input.Cursors.Hand;frame.ToolTip="点击查看大图";
   return new Border{Background=Theme.Dark,BorderBrush=Theme.PanelBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(8),Child=stack};
  }
  var stage=Tile("舞台预览",out stagePreview,out stagePreviewHint,out stagePreviewName);var card=Tile("Boss 卡面",out cardPreview,out cardPreviewHint,out cardPreviewName);
  area.Children.Add(stage);Grid.SetColumn(card,2);area.Children.Add(card);parent.Children.Add(area);
  bindStageButton=Theme.Button("为当前舞台设置截图");bindStageButton.HorizontalAlignment=HorizontalAlignment.Left;bindStageButton.Margin=new Thickness(0,0,0,8);bindStageButton.Click+=(_,_)=>{
   try{BindStagePreview();}catch(Exception ex){MessageBox.Show(ex.Message,"舞台预览",MessageBoxButton.OK,MessageBoxImage.Warning);}
  };parent.Children.Add(bindStageButton);
 }
 void ShowLargePreview(string title,ImageSource source)
 {
  var window=new Window{Title=title,Width=900,Height=650,MinWidth=420,MinHeight=300,Background=Theme.Background,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,
   Content=new Image{Source=source,Stretch=Stretch.Uniform,Margin=new Thickness(18)}};
  window.ShowDialog();
 }
 void BindStagePreview()
 {
  if(!int.TryParse(document.Get("StageID/id"),out var id)||id<=0)return;
  var picker=new OpenFileDialog{Title="选择当前舞台的游戏截图",Filter="图片|*.png;*.jpg;*.jpeg;*.bmp",InitialDirectory=pathHistory.Get(PathPurpose.StagePreviewImport,AutoSaveStore.DirectoryFor(AppContext.BaseDirectory))};
  if(picker.ShowDialog()!=true)return;
  var image=ImageFiles.Read(picker.FileName);if(Math.Max(image.PixelWidth,image.PixelHeight)>1600){var factor=1600.0/Math.Max(image.PixelWidth,image.PixelHeight);image=new TransformedBitmap(image,new ScaleTransform(factor,factor));image.Freeze();}
  string path=StagePreviewPath(id);PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
  if(File.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("舞台预览不能为文件链接。");
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,ImageFiles.Png(image));
  pathHistory.Remember(PathPurpose.StagePreviewImport,Path.GetDirectoryName(picker.FileName)!);UpdatePreviews();log("已设置舞台预览："+document.Get("StageID/str"));
 }
 async void UpdatePreviews()
 {
  int generation=++previewGeneration;
  stagePreview.Source=null;cardPreview.Source=null;stagePreviewHint.Visibility=Visibility.Visible;cardPreviewHint.Visibility=Visibility.Visible;
  bool hasStage=int.TryParse(document.Get("StageID/id"),out var stageId)&&stageId>0;
  stagePreviewName.Text=hasStage?stageId+" · "+document.Get("StageID/str"):"未选择舞台";stagePreviewHint.Text=hasStage?"暂无内置舞台预览\n可设置自己的截图":"选择舞台后显示预览";bindStageButton.IsEnabled=hasStage;
  var choice=cards.SelectedItem as BossCardChoice;
  if(choice==null||choice.Id.ToString(CultureInfo.InvariantCulture)!=document.Get("BossCard/id"))choice=allCards.FirstOrDefault(x=>x.Id.ToString(CultureInfo.InvariantCulture)==document.Get("BossCard/id")&&x.Name==document.Get("BossCard/str"));
  cardPreviewName.Text=choice==null?"未选择卡片":choice.Id+" · "+choice.Name;cardPreviewHint.Text=choice==null?"选择 Boss 后显示卡面":"正在读取卡面…";
  try {
   string? stagePath=hasStage?StagePreviewPath(stageId):null;
   if(stagePath!=null&&File.Exists(stagePath)){
    var stage=await Task.Run(()=>{PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(stagePath)!);if(File.GetAttributes(stagePath).HasFlag(FileAttributes.ReparsePoint))throw new IOException("舞台预览为链接。");return ImageFiles.Read(stagePath);});
    if(generation!=previewGeneration)return;stagePreview.Source=stage;stagePreviewHint.Visibility=Visibility.Collapsed;
   }
   else if(hasStage){
    var stage=await Task.Run(()=>StagePreviewArchive.Read(stageId));
    if(generation!=previewGeneration)return;stagePreview.Source=stage;if(stage!=null)stagePreviewHint.Visibility=Visibility.Collapsed;
   }
  }catch(Exception ex){if(generation==previewGeneration){stagePreviewHint.Text="舞台截图无法读取";log("舞台预览："+ex.Message);}}
  if(generation!=previewGeneration||choice==null)return;
  try {
   BitmapSource? picture=choice.Group=="当前项目"?DraftPreview?.Invoke(choice):null;
   if(picture==null&&choice.Group!="当前项目"){
    var asset=catalog?.ResolveCardPreview(choice);
    if(asset!=null)picture=await Task.Run(()=>DecodeCardPreview(asset));
   }
   if(generation!=previewGeneration)return;
   cardPreview.Source=picture;cardPreviewHint.Visibility=picture==null?Visibility.Visible:Visibility.Collapsed;cardPreviewHint.Text="暂无可用卡面";
  }catch(Exception ex){if(generation==previewGeneration){cardPreviewHint.Text="卡面无法预览";log("卡面预览："+ex.Message);}}
 }
 static byte[] DecodeLzma(byte[] source,int length)
 {
  if(source.Length<5||length<0||length>64*1024*1024)throw new InvalidDataException("LZMA 数据或长度无效。");
  uint dictionary=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(1,4));
  if(dictionary>64*1024*1024)throw new InvalidDataException("LZMA 字典过大。");
  using var input=new MemoryStream(source,5,source.Length-5,false);
  using var decoder=new SharpCompress.Compressors.LZMA.LzmaStream(source.AsSpan(0,5).ToArray(),input,source.Length-5,length);
  byte[] decoded=new byte[length];decoder.ReadExactly(decoded);return decoded;
 }
 internal async Task VerifyLoadedPreviews()
 {
  if(!int.TryParse(document.Get("StageID/id"),out var id)||!StagePreviewArchive.StageIds.Contains(id))throw new InvalidOperationException("测试歌曲没有内置舞台。");
  for(int i=0;i<100&&(stagePreview.Source==null||cardPreview.Source==null);i++)await Task.Delay(50);
  if(stagePreview.Source==null||cardPreview.Source==null)throw new InvalidOperationException("实际舞台或 Boss 预览未显示："+stagePreviewHint.Text+" / "+cardPreviewHint.Text);
 }
 internal static BitmapSource DecodeCardPreview(string path)
 {
  var texture=GameCatalog.ReadPreviewTexture(path,DecodeLzma);byte[] pixels;
  if(texture.Format==4)pixels=texture.Data;
  else {
   var format=texture.Format switch {10=>CompressionFormat.Bc1,12=>CompressionFormat.Bc3,25=>CompressionFormat.Bc7,_=>throw new InvalidDataException("不支持的卡面格式")};
   var colors=new BcDecoder().DecodeRaw(texture.Data,texture.Width,texture.Height,format);pixels=new byte[texture.Width*texture.Height*4];
   for(int i=0;i<colors.Length;i++){pixels[i*4]=colors[i].r;pixels[i*4+1]=colors[i].g;pixels[i*4+2]=colors[i].b;pixels[i*4+3]=colors[i].a;}
  }
  var output=new byte[pixels.Length];int stride=texture.Width*4;
  for(int y=0;y<texture.Height;y++)for(int x=0;x<texture.Width;x++){int src=(texture.Height-1-y)*stride+x*4,dst=y*stride+x*4;output[dst]=pixels[src+2];output[dst+1]=pixels[src+1];output[dst+2]=pixels[src];output[dst+3]=pixels[src+3];}
  var image=BitmapSource.Create(texture.Width,texture.Height,96,96,PixelFormats.Bgra32,null,output,stride);image.Freeze();return image;
 }
 public static void VerifyRefreshIsolation()
 {
  var logs=new List<string>();var fail=false;
  var panel=new MusicEditorPanel(()=>{if(fail)throw new IOException("回归检查模拟卡片目录错误");return new[]{new BossCardChoice(123,"回归卡片","Fire","项目",1,"回归角色")};},logs.Add);
  const string invalid="<MusicData><Name>";panel.raw.Text=invalid;panel.OptDirectory="";panel.SetCatalog(new GameCatalog());
  if(panel.CaptureXml()!=invalid||!panel.HasUnsavedChanges)throw new InvalidOperationException("刷新改写了未保存原始 XML");
  try{panel.Snapshot();throw new InvalidOperationException("非法 XML 未阻止提交");}catch(XmlException){}
  fail=true;panel.OptDirectory="";
  if(panel.raw.Text!=invalid||!logs.Any(x=>x.Contains("回归检查模拟")))throw new InvalidOperationException("目录刷新未隔离错误");
  panel.RestoreXml(invalid,"C:\\test\\Music.xml");
  if(panel.CaptureXml()!=invalid||panel.SourcePath!="C:\\test\\Music.xml")throw new InvalidOperationException("未完成 XML 草稿恢复失败");
 }
 public static void VerifyEditorWorkflow()
 {
  var choices=new[]{new BossCardChoice(11,"官方卡","Fire","official",101,"官方角色","官方游戏包"),new BossCardChoice(22,"自制卡","Aqua","opt",202,"自制角色","自制 opt"),new BossCardChoice(33,"项目卡","Leaf","project",303,"项目角色","当前项目")};
  var panel=new MusicEditorPanel(()=>choices,_=>{});
  var loadedCatalog=new GameCatalog();loadedCatalog.Cards.AddRange(choices);
  loadedCatalog.Cards.Add(new BossCardChoice(44,"孤立卡","Fire","official",404,"缺失定义角色","官方游戏包"));
  loadedCatalog.Cards.Add(new BossCardChoice(23,"沿用官方角色的opt卡","Fire","opt",101,"卡内角色名","自制 opt"));
  loadedCatalog.Characters.AddRange(new[]{new ReferenceChoice(101,"官方角色","官方游戏包"),new ReferenceChoice(202,"自制角色","自制 opt"),new ReferenceChoice(909,"无关联卡角色","自制 opt")});
  panel.SetCatalog(loadedCatalog);
  panel.sourceGroups.SelectedItem="官方游戏包";
  if(panel.characters.Items.Cast<ReferenceChoice>().Any(x=>x.Id==404))throw new InvalidOperationException("目录缺少定义的角色被编造为候选");
  panel.sourceGroups.SelectedItem="自制 opt";
  if(!panel.characters.Items.Cast<ReferenceChoice>().Any(x=>x.Id==101&&x.Name=="官方角色"&&x.Group=="自制 opt")||panel.characters.Items.Cast<ReferenceChoice>().Any(x=>x.Id==909))throw new InvalidOperationException("opt沿用官方角色或无卡角色筛选错误");
  foreach(var field in new[]{"MusicSourceName"})
  {
   var input=panel.references[field];input.ApplyTemplate();
   if(input.Template.FindName("PART_EditableTextBox",input) is not TextBox editor)throw new InvalidOperationException("引用输入框模板不可用");
   editor.Text="9001 · 新建"+field;
   if(panel.document.Get(field+"/id")!="9001"||panel.document.Get(field+"/str")!="新建"+field)throw new InvalidOperationException("无法手填新引用");
  }
  panel.textFields["ArtistName/str"].Text="新建ArtistName";panel.RefreshReferences();
  if(panel.document.Get("ArtistName/str")!="新建ArtistName"||panel.document.Get("MusicSourceName/str")!="新建MusicSourceName")throw new InvalidOperationException("目录刷新丢失手填引用");
  panel.document.Xml.Root!.Add(new XElement("WaveAttribute",new XElement("AttributeType","Fire")));
  panel.textFields["Name/str"].Text="初次名称";panel.textFields["NameForSort"].Text="手动排序";panel.textFields["Name/str"].Text="再次名称";
  if(panel.textFields["NameForSort"].Text!="再次名称"||panel.document.Get("NameForSort")!="再次名称")throw new InvalidOperationException("名称单向同步失败");
  if(panel.booleanFields.Values.Any(x=>x.IsEditable)||panel.difficultyFields.Any(x=>x.Whole.IsEditable||x.Fraction.IsEditable))throw new InvalidOperationException("约束字段仍可任意输入");
  if(panel.references.Any(x=>x.Value.IsEditable!=(x.Key is "MusicSourceName")))throw new InvalidOperationException("引用编辑权限错误");
  if(panel.characters.IsEditable||panel.cards.IsEditable)throw new InvalidOperationException("角色或卡片允许任意输入");
  foreach(var group in Groups)
  {
   panel.sourceGroups.SelectedItem=group;var expected=choices.Single(x=>x.Group==group);
   if(panel.characters.Items.Cast<ReferenceChoice>().Any(x=>x.Group!=group))throw new InvalidOperationException("角色来源混合");
   panel.characters.SelectedItem=panel.characters.Items.Cast<ReferenceChoice>().Single(x=>x.Id==expected.CharaId);
   if(panel.cards.Items.Count!=1||panel.cards.Items.Cast<BossCardChoice>().Any(x=>x.Group!=group||x.CharaId!=expected.CharaId))throw new InvalidOperationException("Boss 角色筛选错误");
   panel.cards.SelectedIndex=0;
   if(panel.document.Get("BossCard/id")!=expected.Id.ToString()||panel.document.Xml.Root!.Elements("WaveAttribute").Any(x=>x.Element("AttributeType")?.Value!=expected.Attribute)||((AttributeChoice?)panel.attribute.SelectedItem)?.Value!=expected.Attribute||!panel.raw.Text.Contains("<AttributeType>"+expected.Attribute+"</AttributeType>"))throw new InvalidOperationException("Boss 选择未同步字段和原始 XML");
  }
  for(var i=0;i<200;i++){panel.RandomizeSort();var value=int.Parse(panel.document.Get("SortOrder"));if(value<200000||value>999999)throw new InvalidOperationException("随机排序号越界");}
  panel.raw.Text="<MusicData><IsLunatic>1</IsLunatic><ReleaseVersion>bad</ReleaseVersion></MusicData>";panel.Snapshot();
  if(panel.document.Get("ReleaseVersion")!=MusicDocument.FixedReleaseDate||(panel.booleanFields["IsLunatic"].SelectedItem as string)!="true")throw new InvalidOperationException("原始 XML 应用未归一化");
  if(panel.booleanFields.Any(x=>x.Value.SelectedItem is not string))throw new InvalidOperationException("旧版Music缺失布尔字段导致下拉为空");
  var imported=MusicDocument.CreateNew();imported.Set("BossCard/id","22");imported.Set("BossCard/str","自制卡");imported.SetWaveAttribute("Leaf");imported.Xml.Root!.Add(new XElement("WaveAttribute",new XElement("AttributeType","Fire")));
  var delayed=new MusicEditorPanel(()=>Array.Empty<BossCardChoice>(),_=>{});delayed.RestoreXml(imported.Xml.ToString(),null);delayed.SetCatalog(loadedCatalog);
  if(delayed.document.Xml.Root!.Elements("WaveAttribute").Any(x=>x.Element("AttributeType")?.Value!="Aqua")||((AttributeChoice?)delayed.attribute.SelectedItem)?.Value!="Aqua"||delayed.raw.Text.Contains("<AttributeType>Leaf</AttributeType>"))throw new InvalidOperationException("读取目录后没有按真实卡片覆盖导入属性");
  delayed.raw.Text="<MusicData><Name>";delayed.SetCatalog(loadedCatalog);
  if(delayed.CaptureXml()!="<MusicData><Name>")throw new InvalidOperationException("自动同步Boss覆盖了未完成原始XML");
  const string validRaw="<MusicData>\n    <Name><id>9012</id><str>未应用草稿</str></Name>\n  <IsLunatic>1</IsLunatic>\n</MusicData>";
  var rawDraft=new MusicEditorPanel(()=>Array.Empty<BossCardChoice>(),_=>{});rawDraft.RestoreXml(validRaw,"C:\\draft\\Music.xml",true);
  if(!rawDraft.IsRawDraft||rawDraft.CaptureXml()!=validRaw||rawDraft.tabs.SelectedIndex!=1||rawDraft.SourcePath!="C:\\draft\\Music.xml")throw new InvalidOperationException("有效但未应用的原始XML没有原样恢复");
  rawDraft.SetCatalog(loadedCatalog);
  if(!rawDraft.IsRawDraft||rawDraft.CaptureXml()!=validRaw)throw new InvalidOperationException("目录刷新应用或重排了原始XML草稿");
  rawDraft.Snapshot();
  if(rawDraft.IsRawDraft||rawDraft.document.Get("IsLunatic")!="true")throw new InvalidOperationException("主动应用原始XML草稿失败");
  var edited=new MusicEditorPanel(()=>Array.Empty<BossCardChoice>(),_=>{});
  edited.textFields["Name/id"].Text="009876";
  if(edited.textFields["dataName"].Text!="music9876"||!edited.textFields["dataName"].IsReadOnly)throw new InvalidOperationException("目录名没有只读跟随歌曲ID");
  if(edited.references.ContainsKey("ArtistName"))throw new InvalidOperationException("艺术家仍为下拉框");
  edited.textFields["ArtistName/str"].Text="手动艺术家名字";
  if(edited.document.Get("ArtistName/str")!="手动艺术家名字"||int.Parse(edited.document.Get("ArtistName/id"))!=9876)throw new InvalidOperationException("纯文字艺术家没有获得ID");
  edited.RefreshReferences();if(edited.document.Get("ArtistName/str")!="手动艺术家名字")throw new InvalidOperationException("刷新丢失手动艺术家");
  for(int i=0;i<4;i++){edited.difficultyFields[i].Whole.SelectedItem=12;edited.difficultyFields[i].Fraction.SelectedItem=80;edited.difficultyFields[i].Path.Text=$"9876_0{i}.ogkr";}
  edited.booleanFields["IsLunatic"].SelectedItem="true";
  if(edited.document.ChartMode!=MusicChartMode.Lunatic||(string)edited.booleanFields["IsReMaster"].SelectedItem!="false")throw new InvalidOperationException("LUNATIC界面未互斥");
  if(edited.difficultyFields.Take(4).Any(x=>x.Whole.IsEnabled||x.Fraction.IsEnabled||x.Path.IsEnabled||(int)x.Whole.SelectedItem!=0||(int)x.Fraction.SelectedItem!=0)||!edited.difficultyFields[4].Whole.IsEnabled)throw new InvalidOperationException("LUNATIC禁用栏错误");
  edited.difficultyFields[4].Whole.SelectedItem=14;edited.difficultyFields[4].Fraction.SelectedItem=70;edited.difficultyFields[4].Path.Text="9876_10.ogkr";
  edited.booleanFields["IsReMaster"].SelectedItem="true";
  if(edited.document.Get("IsLunatic")!="true"||edited.document.Get("IsReMaster")!="true"||(string)edited.booleanFields["IsLunatic"].SelectedItem!="false"||!edited.difficultyTitles[4].Text.StartsWith("Re:MASTER"))throw new InvalidOperationException("ReMASTER界面或官方XML编码错误");
  edited.booleanFields["IsReMaster"].SelectedItem="false";
  if(edited.document.ChartMode!=MusicChartMode.Normal||edited.difficultyFields[4].Path.IsEnabled||edited.difficultyFields.Take(4).Any(x=>!x.Path.IsEnabled)||(int)edited.difficultyFields[4].Whole.SelectedItem!=0||(int)edited.difficultyFields[4].Fraction.SelectedItem!=0)throw new InvalidOperationException("普通模式禁用栏错误");
  var official=new MusicEditorPanel(()=>Array.Empty<BossCardChoice>(),_=>{});
  official.RestoreXml("<MusicData><Name><id>8164</id></Name><IsLunatic>true</IsLunatic><IsReMaster>true</IsReMaster></MusicData>",null);
  if((string)official.booleanFields["IsLunatic"].SelectedItem!="false"||(string)official.booleanFields["IsReMaster"].SelectedItem!="true"||official.document.Get("IsLunatic")!="true")throw new InvalidOperationException("导入官方双true未还原为界面ReMASTER");
  panel.MarkSaved();if(panel.HasUnsavedChanges)throw new InvalidOperationException("保存状态未清除");
 }
 void Save()
 {
  Snapshot();var errors=document.Validate();if(errors.Count>0)throw new InvalidDataException(string.Join(Environment.NewLine,errors));
  var picker=new OpenFolderDialog{Title="选择保存文件夹（直接保存 Music.xml）"};
  picker.InitialDirectory=Directory.Exists(OutputDirectory)?OutputDirectory:pathHistory.Get(PathPurpose.MusicSave,AutoSaveStore.DirectoryFor(AppContext.BaseDirectory));
  if(picker.ShowDialog()!=true)return;
   SaveTarget(document.TargetPath(picker.FolderName));
 }
  void SaveOriginal(){
   if(document.SourcePath==null)throw new InvalidOperationException("请先导入 Music.xml，或使用“保存”选择文件夹。");
   Snapshot();SaveTarget(document.SourcePath);
  }
  void SaveTarget(string target){
   var plan=document.PrepareSave(target);
   string message=plan.OriginalHash==null?"将创建：\n"+plan.TargetPath:"将覆盖：\n"+plan.TargetPath+"\n原文件备份到：\n"+plan.BackupDirectory;
   if(MessageBox.Show(message+"\n影响：保存歌曲配置，不生成音源或 ogkr。是否同意？","确认保存",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   FinishSave(plan);
  }
  void FinishSave(PreparedMusicSave plan){
   document.CommitSave(plan,true);OutputDirectory=Path.GetDirectoryName(plan.TargetPath)!;pathHistory.Remember(PathPurpose.MusicSave,OutputDirectory);Render();RefreshBossSelectors(true);MarkSaved();log("已保存 Music："+plan.TargetPath+(plan.OriginalHash==null?"":"；备份："+plan.BackupDirectory));
  }

}
