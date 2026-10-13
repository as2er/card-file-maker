namespace CardFileMaker;
public static class PathSafety
{
    public static bool Within(string path,string parent) {
        var p=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var r=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return p.Equals(r,StringComparison.OrdinalIgnoreCase)||p.StartsWith(r+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    public static IEnumerable<string> Files(string root) {
        foreach(var p in Directory.EnumerateFileSystemEntries(root)) {
            var attrs=File.GetAttributes(p);
            if(attrs.HasFlag(FileAttributes.ReparsePoint))throw new IOException("目录含链接，无法保证安全复制："+p);
            if(attrs.HasFlag(FileAttributes.Directory)){foreach(var f in Files(p))yield return f;}else yield return p;
        }
    }
    public static void RejectLinkedAncestors(string path) {
        for(var p=new DirectoryInfo(Path.GetFullPath(path));p!=null;p=p.Parent)
            if(p.Exists&&p.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("路径不能经过目录链接："+p.FullName);
    }
}
