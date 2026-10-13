namespace CardFileMaker;

/// <summary>每个项目独立的恢复文件，不覆盖手动保存的项目。</summary>
public static class AutoSaveStore
{
    public static readonly TimeSpan Interval=TimeSpan.FromMinutes(5);
    public static string DirectoryFor(string programDirectory)=>Path.Combine(Path.GetFullPath(programDirectory),"save");
    public static string Save(CardProject project,string directory)
    {
        if(!Guid.TryParseExact(project.ProjectKey,"N",out _))throw new InvalidDataException("项目标识无效。");
        directory=Path.GetFullPath(directory);
        PathSafety.RejectLinkedAncestors(directory);
        string target=Path.Combine(directory,"autosave-"+project.ProjectKey+".cfmproj");
        string staging=target+".pending-"+Guid.NewGuid().ToString("N");
        foreach(var p in new[]{target,staging})
            if(File.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("自动保存目标不能是链接："+p);
        if(File.Exists(target)&&ProjectCodec.Decode(File.ReadAllBytes(target)).ProjectKey!=project.ProjectKey)
            throw new IOException("自动保存路径已被其它项目占用："+target);
        if(File.Exists(staging))throw new IOException("存在未完成的自动保存，请保留并检查："+staging);
        var data=ProjectCodec.Encode(project);
        if(data.Length>=100_000_000)throw new InvalidDataException("自动保存达到 100 MB，已暂停。请使用手动保存并确认文件大小。");
        WriteProjectFile(target,data,FileHash(target),staging,null);
        return target;
    }

    public static string? FileHash(string path)=>File.Exists(path)?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))):null;
    public static void WriteProjectFile(string target,byte[] bytes,string? expectedHash,string staging,string? backup)
    {
        ProjectCodec.Decode(bytes);
        target=Path.GetFullPath(target);string dir=Path.GetDirectoryName(target)!;
        PathSafety.RejectLinkedAncestors(dir);
        foreach(string p in new[]{target,staging}.Concat(backup==null?Array.Empty<string>():new[]{backup}))
        {
            if(Path.GetDirectoryName(Path.GetFullPath(p))!=dir)throw new IOException("项目临时文件及备份必须位于目标目录。");
            if(File.Exists(p)&&File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))throw new IOException("项目目标不能为文件链接："+p);
        }
        if(FileHash(target)!=expectedHash||File.Exists(staging)||(backup!=null&&File.Exists(backup)))throw new IOException("保存预览后目标发生变化，请重新保存。");
        Directory.CreateDirectory(dir);
        using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
        ProjectCodec.Decode(File.ReadAllBytes(staging));
        if(FileHash(target)!=expectedHash)throw new IOException("写入期间目标被修改，已保留临时项目："+staging);
        if(expectedHash!=null&&backup!=null)File.Replace(staging,target,backup);
        else File.Move(staging,target,expectedHash!=null);
    }
}
