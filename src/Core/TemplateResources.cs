using System.Reflection;
namespace CardFileMaker;

/// <summary>读取通用占位模板，也支持构建者指定本地模板目录。</summary>
public static class TemplateResources
{
    static readonly string[] Required = ["Card.xml", "Chara.xml", "Music.xml", "DataConfig.xml",
        "card.bin", "cardSmall.bin", "icon.bin", "iconSmall.bin", "battle.bin", "portrait.bin"];
    static Assembly Assembly => typeof(TemplateResources).Assembly;
    static string ResourceName(string name) => "CardFileMaker.Templates." + name;
    public static IReadOnlyList<string> Missing => Required.Where(name =>
        !Assembly.GetManifestResourceNames().Contains(ResourceName(name), StringComparer.Ordinal)).ToArray();
    public static Stream Open(string name)
    {
        if (!Required.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("未知模板名称。", nameof(name));
        return Assembly.GetManifestResourceStream(ResourceName(name)) ?? throw new FileNotFoundException(
            "缺少本地模板：" + name + "。请使用 CfmTemplateDirectory 指定模板目录并重新构建；详见 README。", name);
    }
    public static string ReadText(string name) { using var stream = Open(name); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
}
