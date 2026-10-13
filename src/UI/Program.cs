using System.Windows;
namespace CardFileMaker;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
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
}
