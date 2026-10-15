using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace CardFileMaker;
public static class ProjectCodec
{
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public static byte[] Encode(CardProject project)
    {
        Validate(project);
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
            var seen=new HashSet<string>();
            foreach(var crop in project.Cards.SelectMany(x=>x.Crops.Values)){
                crop.ImageId=crop.Png.Length==0?"":Convert.ToHexString(SHA256.HashData(crop.Png)).ToLowerInvariant();
                if(crop.Png.Length>80_000_000)throw new InvalidDataException("项目图片过大。");
                if(crop.ImageId!=""&&seen.Add(crop.ImageId)){using var stream=zip.CreateEntry("images/"+crop.ImageId+".png",CompressionLevel.Fastest).Open();stream.Write(crop.Png);}
            }
            byte[] manifestBytes=JsonSerializer.SerializeToUtf8Bytes(project,Options);
            if(manifestBytes.Length>8_000_000)throw new InvalidDataException("项目配置超过 8 MB，请将章节拆分到多个项目后保存。现有项目文件未被覆盖。");
            using var manifest=zip.CreateEntry("project.json",CompressionLevel.Optimal).Open();manifest.Write(manifestBytes);
        }return output.ToArray();
    }
    public static CardProject Decode(byte[] bytes)
    {
        using var source=new MemoryStream(bytes,false);using var zip=new ZipArchive(source,ZipArchiveMode.Read);
        if(zip.Entries.Select(x=>x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=zip.Entries.Count)throw new InvalidDataException("项目包含重复条目。");
        var entry=zip.GetEntry("project.json")??throw new InvalidDataException("不是 Card File Maker 项目。");
        if(entry.Length>8_000_000)throw new InvalidDataException("项目配置过大。");
        using var stream=entry.Open();var result=JsonSerializer.Deserialize<CardProject>(stream)??throw new InvalidDataException("项目为空。");
        Validate(result);var cache=new Dictionary<string,byte[]>();long total=0;
        foreach(var crop in result.Cards.SelectMany(x=>x.Crops.Values)){
            if(string.IsNullOrEmpty(crop.ImageId))continue;
            if(crop.ImageId.Length!=64||!crop.ImageId.All(Uri.IsHexDigit))throw new InvalidDataException("图片引用无效。");
            if(!cache.TryGetValue(crop.ImageId,out var png)){
                var image=zip.GetEntry("images/"+crop.ImageId+".png")??throw new InvalidDataException("项目缺少图片："+crop.SourceName);
                total+=image.Length;
                if(image.Length>80_000_000||total>1_000_000_000)throw new InvalidDataException("项目图片数据超过安全读取大小。");
                using var input=image.Open();using var memory=new MemoryStream();input.CopyTo(memory);png=memory.ToArray();
                if(!Convert.ToHexString(SHA256.HashData(png)).Equals(crop.ImageId,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("项目图片校验失败。");
                cache[crop.ImageId]=png;
            }crop.Png=png;
        }return result;
    }
    static void Validate(CardProject p){
        ChapterXml.ValidateStorage(p.Chapters);
        if(!Guid.TryParseExact(p.ProjectKey,"N",out _))throw new InvalidDataException("项目标识无效。");
        if(p.FormatVersion!=1||p.Cards==null||p.Cards.Count>1000)throw new InvalidDataException("不支持的项目格式或卡片数量。");
        if(p.Cards.Select(x=>x.CardId).Distinct().Count()!=p.Cards.Count||p.Cards.Select(x=>x.Key).Distinct().Count()!=p.Cards.Count)throw new InvalidDataException("项目内卡片 ID 或标识重复。");
        foreach(var c in p.Cards){
            CardValidation.Validate(c);
            if(c.Crops==null||c.Crops.Keys.Any(x=>!Assets.All.Any(a=>a.Key==x)))throw new InvalidDataException("未知的裁剪类型。");
            foreach(var crop in c.Crops.Values)if(crop==null||!double.IsFinite(crop.Transform.Scale)||!double.IsFinite(crop.Transform.X)||!double.IsFinite(crop.Transform.Y)||crop.Transform.Scale<0)throw new InvalidDataException("裁剪参数无效。");
        }
    }
}
public static class CardValidation
{
    public static void Validate(CardDraft c){
        if(c.CardId<1||c.CardId>999999||c.CharaId<1||c.CharaId>999999)throw new InvalidDataException("卡片和角色 ID 必须在 1～999999 之间。");
        if(!Guid.TryParseExact(c.Key,"N",out _))throw new InvalidDataException("卡片项目标识无效。");
        if(string.IsNullOrWhiteSpace(c.CardName)||string.IsNullOrWhiteSpace(c.CharacterName)||c.CardName.Length>100||c.CharacterName.Length>100)throw new InvalidDataException("卡片和角色名称需为 1～100 字。");
        if(c.Attribute is not ("Fire" or "Aqua" or "Leaf"))throw new InvalidDataException("卡片属性无效。");
        if(c.Rarity is not ("N" or "R" or "SR" or "SSR"))throw new InvalidDataException("卡片稀有度无效。");
        if(c.SkillId<0||c.UpgradedSkillId<0||c.SchoolId<0)throw new InvalidDataException("学校和技能 ID 不能为负数。");
    }
}
