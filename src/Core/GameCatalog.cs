using System.Buffers.Binary;
using System.Text;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace CardFileMaker;

public sealed record ReferenceChoice(int Id, string Name, string Group = "官方游戏包", string Source = "", string Detail = "")
{
    public override string ToString() => $"{Id} · {Name}";
}

public sealed class GameCatalog
{
    private const string OfficialGroup = "官方游戏包";
    private const string CustomGroup = "自制 opt";
    private static readonly string[] MusicReferences = ["StageID", "Genre", "FirstChapterName", "MusicRightsName", "MusicSourceName", "ArtistName"];
    private readonly Dictionary<string, IReadOnlyList<ReferenceChoice>> references = new(StringComparer.Ordinal);

    readonly List<(string Path,string Group)> previewAssetRoots=[];
    public List<BossCardChoice> Cards { get; } = [];
    public List<ReferenceChoice> Characters { get; } = [];
    public CatalogResult Ids { get; private set; } = new();
    public Dictionary<int, List<int>> SkillPairs { get; } = [];
    public List<string> Warnings { get; } = [];

    public IReadOnlyList<ReferenceChoice> Get(string field) => references.TryGetValue(field, out var values) ? values : Array.Empty<ReferenceChoice>();

    public static GameCatalog Load(string gamePath, string optPath, Action<string>? log = null, bool scanCabs = true)
    {
        string package = Catalog.ResolvePackage(gamePath);
        PathSafety.RejectLinkedAncestors(package);
        string? selectedOpt = string.IsNullOrWhiteSpace(optPath) ? null : NormalizePath(optPath);
        if (selectedOpt is not null)
        {
            if (!Directory.Exists(selectedOpt)) throw new DirectoryNotFoundException("所选自制 opt 不存在：" + selectedOpt);
            PathSafety.RejectLinkedAncestors(selectedOpt);
        }

        var result = new GameCatalog();
        var choices = new Dictionary<string, Dictionary<(string Group, int Id), ReferenceChoice>>(StringComparer.Ordinal);
        var cards = new Dictionary<(string Group, int Id), BossCardChoice>();
        var characters = new Dictionary<(string Group, int Id), ReferenceChoice>();
        var skillReferences = new List<(int Skill, int UpSkill, string Path)>();
        var roots = new List<PackageRoot>();
        AddOfficialRoots(Path.Combine(package, "mu3_Data", "StreamingAssets", "GameData"), "GameData");
        AddOfficialRoots(Path.Combine(package, "option"), "option");
        if (selectedOpt is not null) roots.Add(new(selectedOpt, CustomGroup, Path.GetFileName(selectedOpt)));

        result.previewAssetRoots.Add((Path.Combine(package,"mu3_Data","StreamingAssets","assets"),OfficialGroup));
        result.previewAssetRoots.AddRange(roots.Select(x=>(Path:Path.Combine(x.Path,"assets"),x.Group)).Where(x=>Directory.Exists(x.Path)));
        foreach (var source in roots)
        {
            log?.Invoke($"读取{source.Group}：{source.Source}");
            PathSafety.RejectLinkedAncestors(source.Path);
            foreach (string path in DataFiles(source.Path, "version", "Version.xml"))
            {
                if (ReadData(path, "VersionData") is not { } root || ReadReference(root, "Name", path, true) is not { } name) continue;
                Put("VersionID", new(name.Id, name.Name, source.Group, source.Source, root.Element("Title")?.Value ?? ""));
            }
            foreach (string path in DataFiles(source.Path, "skill", "Skill.xml"))
            {
                if (ReadData(path, "SkillData") is not { } root || ReadReference(root, "Name", path, true) is not { } name) continue;
                Put("SkillID", new(name.Id, name.Name, source.Group, source.Source, root.Element("Info")?.Element("str")?.Value ?? ""));
            }
            foreach (string path in DataFiles(source.Path, "chara", "Chara.xml"))
            {
                if (ReadData(path, "CharaData") is not { } root || ReadReference(root, "Name", path, true) is not { } name) continue;
                characters[(source.Group, name.Id)] = new(name.Id, name.Name, source.Group, source.Source, root.Element("FlavorText")?.Value ?? "");
            }
            foreach (string path in DataFiles(source.Path, "music", "Music.xml"))
            {
                if (ReadData(path, "MusicData") is not { } root || ReadReference(root, "Name", path, true) is not { } name) continue;
                foreach (string field in MusicReferences)
                    if (ReadReference(root, field, path, true) is { } value)
                        Put(field, new(value.Id, value.Name, source.Group, source.Source, "歌曲：" + name.Name));
            }
            foreach (string path in DataFiles(source.Path, "card", "Card.xml"))
            {
                if (ReadData(path, "CardData") is not { } root || ReadReference(root, "Name", path, true) is not { } name) continue;
                var chara = ReadReference(root, "CharaID", path, true);
                string attribute = root.Element("Attribute")?.Value ?? "";
                if (attribute is not ("Fire" or "Aqua" or "Leaf")) Warn("卡片属性无效，保留原值：" + path);
                cards[(source.Group, name.Id)] = new(name.Id, name.Name, attribute, source.Source,
                    chara?.Id ?? 0, root.Element("CharaID")?.Element("str")?.Value ?? "", source.Group, path);
                if (ReadReference(root, "School", path, true) is { } school)
                    Put("School", new(school.Id, school.Name, source.Group, source.Source, "卡片：" + name.Name));
                var skill = ReadReference(root, "SkillID", path, true);
                var upSkill = ReadReference(root, "ChoKaikaSkillID", path, true);
                if (skill is not null && upSkill is not null) skillReferences.Add((skill.Value.Id, upSkill.Value.Id, path));
            }
        }

        foreach (var preset in new[] { (Id: 0, Name: "-"), (Id: 1, Name: "奏坂学園"), (Id: 999, Name: "プロモーション") })
            if (!choices.TryGetValue("School", out var schools) || !schools.Values.Any(x => x.Id == preset.Id))
                Put("School", new(preset.Id, preset.Name, "预设", "内置预设", "游戏卡片未提供此学校引用时的预设"));

        result.Cards.AddRange(cards.Values.OrderBy(x => GroupOrder(x.Group)).ThenBy(x => x.Id));
        result.Characters.AddRange(characters.Values.OrderBy(x => GroupOrder(x.Group)).ThenBy(x => x.Id));
        foreach (var field in choices)
            result.references[field.Key] = field.Value.Values.OrderBy(x => GroupOrder(x.Group)).ThenBy(x => x.Id).ToArray();

        foreach (var card in result.Cards)
            if (!characters.ContainsKey((card.Group, card.CharaId)) && !characters.ContainsKey((OfficialGroup, card.CharaId)))
                Warn($"卡片 {card.Id} 引用的角色 {card.CharaId}（{card.CharaName}）没有 Chara.xml 定义：{card.CardPath}");

        var skillIds = result.Get("SkillID").Select(x => x.Id).ToHashSet();
        foreach (var pair in skillReferences)
        {
            if (!skillIds.Contains(pair.Skill) || !skillIds.Contains(pair.UpSkill))
            {
                Warn($"卡片技能配对 {pair.Skill} → {pair.UpSkill} 缺少 Skill.xml 定义，未加入配对候选：{pair.Path}");
                continue;
            }
            if (!result.SkillPairs.TryGetValue(pair.Skill, out var targets)) result.SkillPairs[pair.Skill] = targets = [];
            if (!targets.Contains(pair.UpSkill)) targets.Add(pair.UpSkill);
        }
        foreach (var targets in result.SkillPairs.Values) targets.Sort();

        // 查重结果保留所有包的标识；扫描失败时必须终止，避免把不完整集合交给导出流程。
        result.Ids = Catalog.Scan(package, selectedOpt ?? "", log, scanCabs);
        foreach (string warning in result.Ids.Warnings) Warn(warning);
        log?.Invoke($"游戏目录读取完成：{result.Cards.Count:N0} 张卡片、{result.Characters.Count:N0} 个角色、{result.Warnings.Count:N0} 条诊断。");
        return result;

        void AddOfficialRoots(string parent, string label)
        {
            if (!Directory.Exists(parent)) return;
            PathSafety.RejectLinkedAncestors(parent);
            foreach (string path in Directory.EnumerateDirectories(parent, "A???")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal))
            {
                string normalized = NormalizePath(path);
                if (selectedOpt is not null && normalized.Equals(selectedOpt, StringComparison.OrdinalIgnoreCase)) continue;
                roots.Add(new(normalized, OfficialGroup, label + "/" + Path.GetFileName(normalized)));
            }
        }

        void Put(string field, ReferenceChoice value)
        {
            if (!choices.TryGetValue(field, out var map)) choices[field] = map = [];
            map[(value.Group, value.Id)] = value;
        }

        XElement? ReadData(string path, string expectedRoot)
        {
            try
            {
                var root = Catalog.ReadXml(path).Root;
                if (root is null || root.Name != expectedRoot)
                {
                    Warn($"数据根节点应为 {expectedRoot}，已跳过：{path}");
                    return null;
                }
                return root;
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                Warn($"无法读取数据，已跳过：{path}；{ex.Message}");
                return null;
            }
        }

        (int Id, string Name)? ReadReference(XElement root, string field, string path, bool required)
        {
            var reference = root.Element(field);
            if (reference is null)
            {
                if (required) Warn($"缺少 {field} 引用：{path}");
                return null;
            }
            if (!int.TryParse(reference.Element("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
            {
                Warn($"{field} 引用 ID 无效：{path}");
                return null;
            }
            string? name = reference.Element("str")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                Warn($"{field} 引用名称为空，显示实际 ID：{path}");
                name = id.ToString(CultureInfo.InvariantCulture);
            }
            return (id, name);
        }

        void Warn(string message)
        {
            result.Warnings.Add(message);
            log?.Invoke("诊断：" + message);
        }
    }

    public string? ResolveCardPreview(BossCardChoice card)
    {
        if(card.Id<1||card.Id>999999)return null;
        var preferred=string.IsNullOrEmpty(card.CardPath)?null:Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(card.CardPath)));
        var roots=(preferred==null?Enumerable.Empty<string>():new[]{Path.Combine(preferred,"assets")})
            .Concat(previewAssetRoots.Where(x=>card.Group!=OfficialGroup||x.Group==OfficialGroup).Reverse().Select(x=>x.Path)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var root in roots)foreach(var name in new[]{ $"ui_card_{card.Id:D6}_s",$"ui_card_{card.Id:D6}" }) {
            var path=Path.Combine(root,name);if(File.Exists(path)){PathSafety.RejectLinkedAncestors(root);RejectFileLink(path);return path;}
        }
        return null;
    }
    public static (int Width,int Height,int Format,byte[] Data) ReadPreviewTexture(string path,Func<byte[],int,byte[]>? lzmaDecoder=null)=>PreviewReader.Read(path,lzmaDecoder);

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
    private static int GroupOrder(string group) => group == OfficialGroup ? 0 : group == CustomGroup ? 1 : 2;
    private sealed record PackageRoot(string Path, string Group, string Source);

    private static IEnumerable<string> DataFiles(string packageRoot, string category, string fileName)
    {
        string categoryRoot = Path.Combine(packageRoot, category);
        if (!Directory.Exists(categoryRoot)) yield break;
        PathSafety.RejectLinkedAncestors(categoryRoot);
        string direct = Path.Combine(categoryRoot, fileName);
        if (File.Exists(direct))
        {
            RejectFileLink(direct);
            yield return direct;
        }
        foreach (string directory in Directory.EnumerateDirectories(categoryRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(directory);
            if (!name.StartsWith(category, StringComparison.OrdinalIgnoreCase) || name.Length == category.Length || !name.AsSpan(category.Length).ContainsOnlyDigits()) continue;
            PathSafety.RejectLinkedAncestors(directory);
            string file = Path.Combine(directory, fileName);
            if (!File.Exists(file)) continue;
            RejectFileLink(file);
            yield return file;
        }
    }

    private static void RejectFileLink(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("数据文件不能为链接：" + path);
    }
}

internal static class CatalogNameExtensions
{
    internal static bool ContainsOnlyDigits(this ReadOnlySpan<char> text)
    {
        foreach (char value in text) if (value is < '0' or > '9') return false;
        return !text.IsEmpty;
    }
}

/// <summary>只读卡面纹理解析；压缩像素由界面层的开源解码库处理。</summary>
internal static class PreviewReader
{
    private sealed record Obj(long Id,int Offset,int Size,int Type,int Table);
    private sealed record Serial(byte[] Bytes,int DataOffset,int[] Types,List<Obj> Objects);
    private sealed record Block(int Raw,int Packed,int Flags);
    private sealed record Directory(string Name,long Offset,long Size);
    private sealed record Bundle(List<Block> Blocks,List<Directory> Nodes,long DataStart);
    public static (int Width,int Height,int Format,byte[] Data) Read(string path,Func<byte[],int,byte[]>? lzmaDecoder)
    {
        PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("卡面预览文件过大或为链接。");
        using var stream=File.OpenRead(path);var bundle=ReadBundle(stream,lzmaDecoder);
        if(bundle.Blocks.Sum(x=>(long)x.Raw)>64*1024*1024)throw new InvalidDataException("预览资源解压后过大。");
        stream.Position=bundle.DataStart;using var data=new MemoryStream();
        foreach(var block in bundle.Blocks)data.Write(Decode(ReadExact(stream,block.Packed),block.Raw,block.Flags,lzmaDecoder));
        byte[] all=data.ToArray();
        byte[] Node(Directory node) {
            if(node.Offset<0||node.Size<0||node.Offset>all.Length-node.Size)throw new InvalidDataException("资源节点越界。");
            return all.AsSpan(checked((int)node.Offset),checked((int)node.Size)).ToArray();
        }
        var main=bundle.Nodes.FirstOrDefault(x=>x.Name.StartsWith("CAB-",StringComparison.Ordinal)&&!x.Name.EndsWith(".resS",StringComparison.OrdinalIgnoreCase))??throw new InvalidDataException("没有纹理节点。");
        var serial=ParseSerial(Node(main));var texture=serial.Objects.Single(x=>serial.Types[x.Type]==28);
        using var tr=new BinaryReader(new MemoryStream(serial.Bytes,serial.DataOffset+texture.Offset,texture.Size,false),Encoding.UTF8);
        ReadString(tr);int w=tr.ReadInt32(),h=tr.ReadInt32();tr.ReadInt32();int format=tr.ReadInt32();tr.ReadInt32();tr.ReadBytes(2);Align(tr,4);tr.ReadBytes(32);int length=tr.ReadInt32();
        if(w<1||h<1||w>8192||h>8192||(long)w*h>16000000||length<0||length>64*1024*1024)throw new InvalidDataException("纹理尺寸无效。");
        byte[] pixels;
        if(length>0){pixels=tr.ReadBytes(length);if(pixels.Length!=length)throw new InvalidDataException("纹理截断。");}
        else {
            Align(tr,4);uint offset=tr.ReadUInt32(),size=tr.ReadUInt32();string resource=ReadString(tr).Replace('\\','/').Split('/').Last();
            var node=bundle.Nodes.FirstOrDefault(x=>x.Name.Equals(resource,StringComparison.OrdinalIgnoreCase))??throw new InvalidDataException("缺少纹理数据流。");
            byte[] resourceBytes=Node(node);if(offset>resourceBytes.Length||(long)offset+size>resourceBytes.Length)throw new InvalidDataException("纹理数据流越界。");
            pixels=resourceBytes.AsSpan((int)offset,(int)size).ToArray();
        }
        int required=format switch {4=>checked(w*h*4),10=>checked(((w+3)/4)*((h+3)/4)*8),12 or 25=>checked(((w+3)/4)*((h+3)/4)*16),_=>throw new InvalidDataException("暂不支持此纹理格式："+format)};
        if(pixels.Length<required)throw new InvalidDataException("纹理像素数据不足。");
        return(w,h,format,pixels.AsSpan(0,required).ToArray());
    }
    private static Serial ParseSerial(byte[] b)
    {
        if(b.Length<20||BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(8))!=17||b[16]!=0)throw new InvalidDataException("仅支持 little-endian serialized v17。");
        int data=BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(12));if(data<20||data>b.Length)throw new InvalidDataException("序列化数据偏移无效。");
        using var r=new BinaryReader(new MemoryStream(b),Encoding.UTF8);r.BaseStream.Position=20;ReadZ(r);r.ReadInt32();bool tree=r.ReadBoolean();int count=Count(r.ReadInt32());var types=new int[count];
        for(int i=0;i<count;i++) { int cl=r.ReadInt32();types[i]=cl;r.ReadBoolean();r.ReadInt16();if(cl==114)r.ReadBytes(16);r.ReadBytes(16);if(tree){int nodes=Count(r.ReadInt32()),strings=Count(r.ReadInt32());r.BaseStream.Position=checked(r.BaseStream.Position+nodes*24L+strings);} }
        count=Count(r.ReadInt32());var objects=new List<Obj>();for(int i=0;i<count;i++){Align(r,4);int table=checked((int)r.BaseStream.Position);long id=r.ReadInt64();int off=r.ReadInt32(),size=r.ReadInt32(),type=r.ReadInt32();if(type<0||type>=types.Length||off<0||size<0||(long)data+off+size>b.Length)throw new InvalidDataException("对象表无效。");objects.Add(new(id,off,size,type,table));}return new(b,data,types,objects);
    }
    private static Bundle ReadBundle(Stream s,Func<byte[],int,byte[]>? lzmaDecoder)
    {
        using var r=new BinaryReader(s,Encoding.UTF8,true);if(ReadZ(r)!="UnityFS")throw new InvalidDataException("不是 UnityFS 资源。");int version=BE32(r);if(version!=6)throw new InvalidDataException("仅支持 UnityFS v6。");ReadZ(r);ReadZ(r);long size=BE64(r);int packed=BE32(r),raw=BE32(r),flags=BE32(r);long header=s.Position;
        if(size< header||size>s.Length||packed<0||packed>64*1024*1024||raw<0||raw>64*1024*1024)throw new InvalidDataException("UnityFS 块信息大小无效。");
        if((flags&128)!=0)s.Position=size-packed;byte[] info=Decode(ReadExact(s,packed),raw,flags,lzmaDecoder);long start=(flags&128)!=0?header:header+packed;
        using var ir=new BinaryReader(new MemoryStream(info),Encoding.UTF8);ir.ReadBytes(16);int n=Count(BE32(ir));var blocks=new List<Block>();for(int i=0;i<n;i++){int u=BE32(ir),c=BE32(ir);int f=(ir.ReadByte()<<8)|ir.ReadByte();if(u<0||c<0)throw new InvalidDataException("数据块大小无效。");blocks.Add(new(u,c,f));}
        n=Count(BE32(ir));var nodes=new List<Directory>();for(int i=0;i<n;i++){long o=BE64(ir),z=BE64(ir);BE32(ir);nodes.Add(new(ReadZ(ir),o,z));}if((flags&512)!=0)start=(start+15)&~15L;return new(blocks,nodes,start);
    }
    private static byte[] Decode(byte[] src,int length,int flags,Func<byte[],int,byte[]>? lzmaDecoder)
    {
        int compression=flags&63;if(compression==0){if(src.Length!=length)throw new InvalidDataException("未压缩数据长度不符。");return src;}
        if(compression==1&&lzmaDecoder!=null){var decoded=lzmaDecoder(src,length);if(decoded.Length!=length)throw new InvalidDataException("LZMA 输出长度不符。");return decoded;}
        if(compression!=2&&compression!=3)throw new InvalidDataException($"不支持预览资源压缩类型 {compression}。");
        byte[] dst=new byte[length];int p=0,o=0;
        int Extended(int n){if(n==15){int v;do{if(p>=src.Length)throw new InvalidDataException("LZ4 长度截断。");v=src[p++];n=checked(n+v);}while(v==255);}return n;}
        while(p<src.Length){int token=src[p++],literal=Extended(token>>4);if(literal>src.Length-p||literal>dst.Length-o)throw new InvalidDataException("LZ4 字面数据越界。");src.AsSpan(p,literal).CopyTo(dst.AsSpan(o));p+=literal;o+=literal;if(p==src.Length)break;if(p+2>src.Length)throw new InvalidDataException("LZ4 偏移截断。");int distance=src[p]|src[p+1]<<8;p+=2;if(distance==0||distance>o)throw new InvalidDataException("LZ4 匹配偏移无效。");int match=checked(Extended(token&15)+4);if(match>dst.Length-o)throw new InvalidDataException("LZ4 匹配越界。");for(int j=0;j<match;j++)dst[o+j]=dst[o+j-distance];o+=match;}
        if(o!=length)throw new InvalidDataException("LZ4 输出长度不符。");return dst;
    }
    private static int Count(int n){if(n<0||n>1000000)throw new InvalidDataException("数组计数无效。");return n;}
    private static void Align(BinaryReader r,int n)=>r.BaseStream.Position=(r.BaseStream.Position+n-1)&~(n-1L);
    private static string ReadString(BinaryReader r){int n=Count(r.ReadInt32());byte[] b=r.ReadBytes(n);if(b.Length!=n)throw new InvalidDataException("字符串截断。");Align(r,4);return Encoding.UTF8.GetString(b);}
    private static string ReadZ(BinaryReader r){using var m=new MemoryStream();for(int i=0;i<1048576;i++){byte c=r.ReadByte();if(c==0)return Encoding.UTF8.GetString(m.ToArray());m.WriteByte(c);}throw new InvalidDataException("字符串过长。");}
    private static byte[] ReadExact(Stream s,int n){byte[] b=new byte[n];s.ReadExactly(b);return b;}
    private static int BE32(BinaryReader r)=>BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
    private static long BE64(BinaryReader r)=>BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
}
