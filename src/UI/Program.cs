using System.Windows;
namespace CardFileMaker;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--check-chapter-ui") {
            try {
                string output=System.IO.Path.GetFullPath(args[1]);System.IO.Directory.CreateDirectory(output);
                var diagnosticApp=new Application();
                var panel=new ChapterEditorPanel(_=>{},new PathHistoryStore(output),()=>(MusicDocument.CreateNew().Xml.ToString(),null,null),()=>("",""));Theme.Install(panel.Resources);
                var music=MusicDocument.CreateNew();music.Set("Name/id","912345");music.Set("Name/str","自定义 Boss 挑战");
                var song=ChapterSong.FromXml(music.Xml.ToString());song.Challenge.Enabled=true;
                panel.Restore([new ChapterDraft{Songs=[song],Name="自定义章节 · 示例"}]);
                var saved=panel.Snapshot();panel.Restore(saved);if(panel.Snapshot()[0].Songs[0].Challenge.Score!=970000)throw new InvalidOperationException("页面恢复丢失分数条件。");
                var tabs=Nodes<System.Windows.Controls.TabControl>(panel).First();tabs.SelectedIndex=1;
                System.Windows.Controls.ComboBox ChallengeSwitch()=>Nodes<System.Windows.Controls.StackPanel>(panel).First(x=>x.Children.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.StartsWith("启用分数挑战"))).Children.OfType<System.Windows.Controls.ComboBox>().Single();
                ChallengeSwitch().SelectedItem="false";ChallengeSwitch().SelectedItem="true";
                var scoreInput=Nodes<System.Windows.Controls.StackPanel>(panel).First(x=>x.Children.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.StartsWith("最低分数"))).Children.OfType<System.Windows.Controls.TextBox>().Single();scoreInput.Text="987654";
                if(!panel.Snapshot()[0].Songs[0].Challenge.Enabled||panel.Snapshot()[0].Songs[0].Challenge.Score!=987654)throw new InvalidOperationException("挑战开关或分数输入未同步到项目。");
                panel.Width=1420;panel.Height=830;
                for(int page=0;page<tabs.Items.Count;page++) {
                    tabs.SelectedIndex=page;panel.Measure(new Size(1420,830));panel.Arrange(new Rect(0,0,1420,830));panel.UpdateLayout();
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1420,830,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(panel);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(System.IO.Path.Combine(output,$"chapter-page-{page+1}.png")))encoder.Save(file);
                }
                // 同一进程验证原 Music 页面，整个诊断过程不显示窗口。
                MusicEditorPanel.VerifyRefreshIsolation();MusicEditorPanel.VerifyEditorWorkflow();
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,"result.txt"),"章节页面布局、重复恢复与原 Music 编辑器交互检查通过。");diagnosticApp.Shutdown();Environment.ExitCode=0;
            }catch(Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetFullPath(args[1]),"result.txt"),e.ToString());Environment.ExitCode=2;}
            return;
        }
        var missing=TemplateResources.Missing;
        if(args.Contains("--check-templates")) {
            Console.WriteLine(missing.Count==0?"模板完整。":"缺少模板："+string.Join("、",missing));
            Environment.ExitCode=missing.Count==0?0:2;
            return;
        }
        if(missing.Count>0) {
            MessageBox.Show("模板文件未完整嵌入构建。缺少：\n"+string.Join("、",missing)+
                "\n\n请使用 CfmTemplateDirectory 指定自己的本地模板目录并重新构建，详见 README。",
                "需要本地模板",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"发生错误",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
        app.Run(new MainWindow(args));
    }
    static IEnumerable<T> Nodes<T>(DependencyObject parent) where T:DependencyObject {
        if(parent is T node)yield return node;
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())foreach(var item in Nodes<T>(child))yield return item;
    }
}
