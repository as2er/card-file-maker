using CardFileMaker;
public static class UnlockPatchTests
{
    public static void Run(Action<string, Action> check)
    {
        check("无法读取程序集时不能报告未安装全解", () =>
        {
            var type = typeof(CardProject).Assembly.GetType("CardFileMaker.UnlockPatchInspector") ?? throw new Exception("缺少只读补丁检查器");
            var result = type.GetMethod("InspectAssembly")!.Invoke(null, new object[] { "不存在的游戏程序集.dll" })!;
            if (!result.ToString()!.Contains("无法判断")) throw new Exception("无法读取被误认为干净环境");
        });
        check("普通工具程序集不能被判为未打补丁的游戏",()=>{
            var report=UnlockPatchInspector.InspectAssembly(typeof(CardProject).Assembly.Location);
            if(report.Status!="无法判断")throw new Exception("非游戏程序集被误判");
        });
        string game=Environment.GetEnvironmentVariable("CFM_TEST_GAME")??"";
        if(Directory.Exists(game))check("只读识别已安装全解并与备份判断对照",()=>{
            string managed=Path.Combine(Catalog.ResolvePackage(game),"mu3_Data","Managed");
            var active=UnlockPatchInspector.InspectAssembly(Path.Combine(managed,"Assembly-CSharp.dll"));
            var baseline=UnlockPatchInspector.InspectAssembly(Path.Combine(managed,"Assembly-CSharp.0.dll"));
            Console.WriteLine(active);Console.WriteLine("基线候选："+baseline);
            if(active.Status!="检测到全解影响"||baseline.Status!="未发现已知全解特征")throw new Exception("实际程序与基线识别结果不符");
            if(!active.Findings.Any(x=>x.Contains("锁定 getter"))||!active.Findings.Any(x=>x.Contains("Memory 登录"))||!active.Findings.Any(x=>x.Contains("2099")))throw new Exception("遗漏已验证的全解特征");
        });
    }
}
