using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace CardFileMaker;

public sealed record UnlockPatchReport(string Status,IReadOnlyList<string> Findings)
{
    public override string ToString()=>Status+"\n"+string.Join("\n",Findings);
}

/// <summary>只解析 PE 元数据与 IL，不加载或执行游戏程序集。</summary>
public static class UnlockPatchInspector
{
    static readonly Dictionary<short,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(x=>x.FieldType==typeof(OpCode)).Select(x=>(OpCode)x.GetValue(null)!).GroupBy(x=>x.Value).ToDictionary(x=>x.Key,x=>x.First());
    public static UnlockPatchReport Inspect(string game) {
        try {
            string package=Catalog.ResolvePackage(game),managed=Path.Combine(package,"mu3_Data","Managed");
            var report=InspectAssembly(Path.Combine(managed,"Assembly-CSharp.dll"));var findings=report.Findings.ToList();
            foreach(string name in new[]{"UnlockAllMusic","UnlockMemoryChapters","UnlockGameEvents"})if(File.Exists(Path.Combine(managed,"Assembly-CSharp."+name+".mm.dll")))findings.Add("发现补丁文件："+name+"；文件存在本身不代表正在生效。");
            if(File.Exists(Path.Combine(managed,"Assembly-CSharp.0.dll")))findings.Add("存在 Assembly-CSharp.0.dll 对照候选；CFM 不会自动替换，也不据此认定整份文件为官方原版。");
            findings.Add("账号已有记录、服务器全开放和未识别补丁仍需在隔离环境中排除。");return new(report.Status,findings);
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException) { return new("无法判断",[e.Message]); }
    }
    public static UnlockPatchReport InspectAssembly(string path) {
        try {
            using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
            if(!pe.HasMetadata)return new("无法判断",["文件没有可读取的 .NET 元数据。"]) ;
            var metadata=pe.GetMetadataReader();var findings=new List<string>();bool music=false,memory=false,events=false,examined=false;
            foreach(var handle in metadata.TypeDefinitions) {
                var type=metadata.GetTypeDefinition(handle);string name=metadata.GetString(type.Name),ns=metadata.GetString(type.Namespace);
                if(ns!="MU3.DataStudio"&&ns!="MU3.Client")continue;
                if(name is not ("MusicData" or "PacketGetUserMemoryChapter" or "PacketGetGameEvent"))continue;
                foreach(var mh in type.GetMethods()) {
                    var method=metadata.GetMethodDefinition(mh);string methodName=metadata.GetString(method.Name);
                    if(method.RelativeVirtualAddress==0)continue;
                    if(name=="MusicData"&&methodName=="get_IsLockedAtTheBeginning") {
                        examined=true;var bytes=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()??[];
                        if(ReturnsZero(bytes)) { music=true;findings.Add("已确认：歌曲初始锁定 getter 固定返回 false，XML 锁定值被覆盖。"); }
                    }
                    if(name=="MusicData"&&methodName=="get_CostToUnlock") {
                        var bytes=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()??[];
                        if(ReturnsZero(bytes)) { music=true;findings.Add("已确认：歌曲解锁费用 getter 固定返回 0。"); }
                    }
                    if(methodName!="proc")continue;
                    var references=ReadReferences(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()??[],metadata);
                    if(name=="PacketGetUserMemoryChapter"&&references.Any(x=>x.Contains("MemoryChapterDataList",StringComparison.OrdinalIgnoreCase)||x=="get_allMemoryChapterData")) {
                        memory=true;findings.Add("发现已知影响：Memory 登录响应处理枚举本地章节，可能补齐服务端未返回的章节记录。");
                    }
                    if(name=="PacketGetGameEvent"&&references.Any(x=>x.Contains("2099",StringComparison.Ordinal))) { events=true;findings.Add("发现已知影响：活动响应处理包含延长至 2099 年的逻辑。"); }
                }
            }
            if(!examined)return new("无法判断",["未找到预期的 SDDT MusicData 判断方法，不能认定为未安装补丁。"]);
            if(!music&&!memory&&!events)findings.Add("未匹配本工具已识别的全解特征；这不证明程序集完全未修改。");
            return new(music||memory||events?"检测到全解影响":"未发现已知全解特征",findings);
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException) { return new("无法判断",[e.Message]); }
    }
    static bool ReturnsZero(byte[] bytes)=>bytes.SequenceEqual(new byte[]{0x16,0x2a})||bytes.SequenceEqual(new byte[]{0x00,0x16,0x0a,0x2b,0x00,0x06,0x2a});
    static List<string> ReadReferences(byte[] bytes,MetadataReader reader) {
        var result=new List<string>();int offset=0;
        while(offset<bytes.Length) {
            short value=bytes[offset++];if(value==0xfe){if(offset>=bytes.Length)throw new BadImageFormatException("IL 截断。");value=unchecked((short)(0xfe00|bytes[offset++]));}
            if(!Codes.TryGetValue(value,out var code))throw new BadImageFormatException("未知 IL 指令。");
            int size=code.OperandType switch { OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>offset+4<=bytes.Length?checked(4+4*BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4))):throw new BadImageFormatException(),_=>4 };
            if(size<0||offset+size>bytes.Length)throw new BadImageFormatException("IL 操作数截断。");
            if(code.OperandType==OperandType.InlineString)result.Add(reader.GetUserString(MetadataTokens.UserStringHandle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4))&0xffffff)));
            else if(code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok) {
                var handle=MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4)));
                switch(handle.Kind) {
                    case HandleKind.MethodDefinition: result.Add(reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name));break;
                    case HandleKind.MemberReference: result.Add(reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name));break;
                    case HandleKind.FieldDefinition: result.Add(reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name));break;
                }
            }
            offset+=size;
        }return result;
    }
}
