using System.Text.Json;
namespace CardFileMaker;

public enum PathPurpose { GameDirectory, OptDirectory, ImageImport, ProjectOpen, ProjectSave, MusicImport, MusicSave, StagePreviewImport }

/// <summary>每个选择入口独立记录成功使用的目录，历史损坏或不可写不阻止主操作。</summary>
public sealed class PathHistoryStore
{
    readonly string path;
    readonly Action<string>? log;
    readonly object gate=new();
    Dictionary<string,string> values=new(StringComparer.Ordinal);
    public PathHistoryStore(string programDirectory,Action<string>? log=null)
    {
        path=Path.Combine(AutoSaveStore.DirectoryFor(programDirectory),"path-history.json");this.log=log;
        try { values=Read(); } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { log?.Invoke("路径历史无法读取，使用默认目录："+e.Message); }
    }
    Dictionary<string,string> Read()
    {
        PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
        if(!File.Exists(path))return new(StringComparer.Ordinal);
        if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(path).Length>65536)throw new IOException("路径历史文件无效。");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new(StringComparer.Ordinal);
    }
    public string Get(PathPurpose purpose,string? fallback=null)
    {
        lock(gate) {
            if(values.TryGetValue(purpose.ToString(),out var previous)&&Directory.Exists(previous))return previous;
            return Directory.Exists(fallback)?Path.GetFullPath(fallback!):"";
        }
    }
    public void Remember(PathPurpose purpose,string directory)
    {
        if(!Enum.IsDefined(purpose)||!Directory.Exists(directory))return;
        lock(gate) {
            values[purpose.ToString()]=Path.GetFullPath(directory);
            try {
                PathSafety.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
                if(File.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("路径历史文件不能是链接。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Dictionary<string,string> merged;try{merged=Read();}catch(JsonException){merged=new(StringComparer.Ordinal);}
                foreach(var pair in values)merged[pair.Key]=pair.Value;
                string pending=path+".pending-"+Guid.NewGuid().ToString("N");
                using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    JsonSerializer.Serialize(stream,merged,new JsonSerializerOptions{WriteIndented=true});stream.Flush(true);
                }
                File.Move(pending,path,true);values=merged;
            } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { log?.Invoke("路径历史无法保存，本次操作仍已完成："+e.Message); }
        }
    }
}
