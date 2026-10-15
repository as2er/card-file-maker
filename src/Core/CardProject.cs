namespace CardFileMaker;
public sealed class CardDraft
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public int CardId { get; set; } = 900001;
    public int CharaId { get; set; } = 900001;
    public string CardName { get; set; } = "自定义 Boss";
    public string CharacterName { get; set; } = "自定义角色";
    public bool ReuseExistingChara { get; set; }
    public bool QuickCrop { get; set; }
    public string SchoolName { get; set; } = "プロモーション";
    public string SkillName { get; set; } = "ボスアタック +10";
    public string UpgradedSkillName { get; set; } = "ボスアタック +12";
    public string Attribute { get; set; } = "Fire";
    public string Rarity { get; set; } = "R";
    public string NickName { get; set; } = "";
    public int SchoolId { get; set; } = 999;
    public int SkillId { get; set; } = 100005;
    public int UpgradedSkillId { get; set; } = 100046;
    public Dictionary<string, SavedCrop> Crops { get; set; } = [];
    // 复制可编辑字段；已编码的 PNG 字节仅供读取，避免重复占用内存。
    public CardDraft Snapshot()
    {
        var copy=(CardDraft)MemberwiseClone();
        copy.Crops=Crops.ToDictionary(x=>x.Key,x=>new SavedCrop{Png=x.Value.Png,ImageId=x.Value.ImageId,SourceName=x.Value.SourceName,Transform=x.Value.Transform});
        return copy;
    }

}
public sealed class SavedCrop
{
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] Png { get; set; } = [];
    public string ImageId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public CropTransform Transform { get; set; }
}
public sealed class CardProject
{
    public int FormatVersion { get; set; } = 1;
    public string ProjectKey { get; set; } = Guid.NewGuid().ToString("N");
    public string MusicXml { get; set; } = "";
    public bool MusicRawDraft { get; set; }
    public string? MusicSourcePath { get; set; }
    public string? MusicSourceHash { get; set; }
    public string GameDirectory { get; set; } = "";
    public string OptDirectory { get; set; } = "";
    public string MusicOutputDirectory { get; set; } = "";
    public List<CardDraft> Cards { get; set; } = [];
    public List<ChapterDraft> Chapters { get; set; } = [];
}
public sealed record CardExportItem(CardDraft Card, IReadOnlyDictionary<string,RgbaImage> Images);
public sealed record BatchExportRequest(string GameDirectory,string OptDirectory,IReadOnlyList<CardExportItem> Cards);
public sealed record PlannedFile(string Path,byte[] Bytes,string? OriginalHash);
public sealed class PreparedBatch
{
    public string OutputDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = "";
    public string BackupRoot { get; init; } = Path.Combine(AutoSaveStore.DirectoryFor(AppContext.BaseDirectory),"card-maker-backups");
    public List<PlannedFile> Files { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
