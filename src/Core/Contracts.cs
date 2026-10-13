namespace CardFileMaker;
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public void Validate() { if (Width < 1 || Height < 1 || Width > 8192 || Height > 8192 || Pixels.Length != checked(Width * Height * 4)) throw new InvalidDataException("图像尺寸或像素数据无效。"); }
}
public sealed record AssetSpec(string Key, string Label, string Prefix, string Suffix, int Width, int Height, int Mips, string TexturePrefix)
{
    public string BundleName(int id) => $"{Prefix}{id:D6}{Suffix}".ToLowerInvariant();
    public string TextureName(int id) => $"{TexturePrefix}{id:D6}{Suffix.ToUpperInvariant()}";
}
public static class Assets
{
    public static readonly AssetSpec[] All = [
        new("card","完整卡面","ui_card_","",768,1052,1,"UI_Card_"),
        new("cardSmall","小卡面","ui_card_","_s",256,356,1,"UI_Card_"),
        new("battle","战斗立绘","ui_card_chara_","",1024,1024,11,"UI_Card_Chara_"),
        new("portrait","竖版人物图","ui_card_chara_","_p",768,1052,1,"UI_Card_Chara_"),
        new("icon","头像","ui_card_icon_","",256,256,1,"UI_Card_Icon_"),
        new("iconSmall","小头像","ui_card_icon_","_s",100,100,1,"UI_Card_Icon_")
    ];
}
public sealed record BossSettings(int CardId, int CharaId, string CardName, string CharacterName, string Attribute, int BossLevel, string OptName);
public sealed record SongChoice(string XmlPath, int Id, string Title)
{
    public override string ToString() => $"{Id} · {Title}";
}
public sealed record ExportRequest(string GamePackagePath, string SourceOptPath, string OutputParentPath, BossSettings Settings, IReadOnlyList<SongChoice> Songs, IReadOnlyDictionary<string, RgbaImage> Images, bool ConfirmLargeFiles = false);
public sealed record ExportResult(string OutputPath, string ReportPath, IReadOnlyList<string> Warnings);
public sealed class CatalogResult
{
    public HashSet<int> CardIds { get; } = [];
    public HashSet<int> CharaIds { get; } = [];
    public HashSet<int> SkillIds { get; } = [];
    public HashSet<int> VersionIds { get; } = [];
    public HashSet<string> AssetNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Cabs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; } = [];
    public int NextCardId(int start = 900001) { while (CardIds.Contains(start) && start <= 999999) start++; return start <= 999999 ? start : throw new InvalidOperationException("候选卡片 ID 已用尽。"); }
    public int NextCharaId(int start = 900001) { while (CharaIds.Contains(start) && start <= 999999) start++; return start <= 999999 ? start : throw new InvalidOperationException("候选角色 ID 已用尽。"); }
}
public sealed class LargeFileException(IReadOnlyList<string> paths) : Exception("发现达到 100 MB 的源文件，需要确认后才可复制。")
{
    public IReadOnlyList<string> Paths { get; } = paths;
}
public sealed record ManifestEntry(int Id, string Name, int[] Dependencies);
public sealed record BundleInspection(string Cab, string BundleName, string TextureName, string ContainerPath, int Width, int Height, int Format, int Mips, byte[] RgbaTopDown);
public readonly record struct CropTransform(double Scale, double X, double Y);
public static class CropGeometry
{
    public static CropTransform Center(int sw,int sh,int tw,int th) => new(1,Math.Floor((tw-sw)/2.0),Math.Floor((th-sh)/2.0));
    public static CropTransform Fit(int sw, int sh, int tw, int th, bool cover)
    {
        double scale = cover ? Math.Max((double)tw / sw, (double)th / sh) : Math.Min((double)tw / sw, (double)th / sh);
        return new(scale, (tw - sw * scale) / 2, (th - sh * scale) / 2);
    }
}
