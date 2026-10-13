using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CardFileMaker;

/// <summary>按需读取内嵌舞台预览；每张 PNG 独立验证并解密，不写出临时图片。</summary>
internal static class StagePreviewArchive
{
    const string ResourceName="CardFileMaker.StagePreviews";
    static readonly byte[] Key=Convert.FromHexString("34E108845EC84922D5F1A20EAB52CF643E92D279C6DB7AF0FD009AE723854FC1");
    sealed record Entry(long Offset,int Length,byte[] Nonce,byte[] Tag);
    static readonly Lazy<IReadOnlyDictionary<int,Entry>> Index=new(ReadIndex);
    internal static IReadOnlyList<int> StageIds=>Index.Value.Keys.Order().ToArray();
    static Stream? Open()=>typeof(StagePreviewArchive).Assembly.GetManifestResourceStream(ResourceName);
    static IReadOnlyDictionary<int,Entry> ReadIndex()
    {
        using var stream=Open();var entries=new Dictionary<int,Entry>();if(stream==null)return entries;
        using var reader=new BinaryReader(stream,Encoding.UTF8);
        if(Encoding.ASCII.GetString(reader.ReadBytes(8))!="CFMSPV01")throw new InvalidDataException("内置舞台预览格式无效。");
        int count=reader.ReadInt32();if(count<0||count>10000)throw new InvalidDataException("舞台预览数量无效。");
        long tableEnd=12+count*44L;
        for(int i=0;i<count;i++)
        {
            int id=reader.ReadInt32();long offset=reader.ReadInt64();int length=reader.ReadInt32();byte[] nonce=reader.ReadBytes(12),tag=reader.ReadBytes(16);
            if(id<1||length<1||length>16*1024*1024||offset<tableEnd||offset>stream.Length-length||nonce.Length!=12||tag.Length!=16||!entries.TryAdd(id,new(offset,length,nonce,tag)))throw new InvalidDataException("舞台预览索引无效。");
        }
        return entries;
    }
    internal static byte[]? ReadPng(int id)
    {
        if(!Index.Value.TryGetValue(id,out var entry))return null;
        using var stream=Open()??throw new InvalidDataException("内置舞台预览丢失。");stream.Position=entry.Offset;
        var encrypted=new byte[entry.Length];stream.ReadExactly(encrypted);var png=new byte[entry.Length];
        using var aes=new AesGcm(Key,16);aes.Decrypt(entry.Nonce,encrypted,entry.Tag,png,BitConverter.GetBytes(id));return png;
    }
    internal static BitmapSource? Read(int id)
    {
        var bytes=ReadPng(id);if(bytes==null)return null;
        using var stream=new MemoryStream(bytes,false);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    // 本地构建辅助入口；源图由构建者自行准备，不随公开源码提供。
    internal static void Pack(string directory,string destination)
    {
        var files=Directory.EnumerateFiles(directory,"stage*.png").Select(path=>(Path:path,Id:int.Parse(Path.GetFileNameWithoutExtension(path).AsSpan(5)))).OrderBy(x=>x.Id).ToArray();
        if(files.Length==0||files.Select(x=>x.Id).Distinct().Count()!=files.Length)throw new InvalidDataException("舞台源图清单无效。");
        long estimated=12+files.Length*44L+files.Sum(x=>new FileInfo(x.Path).Length);
        if(estimated>=100_000_000)throw new InvalidDataException("素材包达到 100 MB，请先确认输出范围。");
        using var output=new FileStream(destination,FileMode.Create,FileAccess.ReadWrite,FileShare.None);using var writer=new BinaryWriter(output,Encoding.UTF8,true);
        writer.Write(Encoding.ASCII.GetBytes("CFMSPV01"));writer.Write(files.Length);output.SetLength(12+files.Length*44L);
        for(int i=0;i<files.Length;i++)
        {
            var file=files[i];var png=File.ReadAllBytes(file.Path);if(png.Length<8||png.Length>16*1024*1024||!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("舞台源图不是有效 PNG。");
            byte[] nonce=RandomNumberGenerator.GetBytes(12),tag=new byte[16],encrypted=new byte[png.Length];
            using(var aes=new AesGcm(Key,16))aes.Encrypt(nonce,png,encrypted,tag,BitConverter.GetBytes(file.Id));
            long offset=output.Length;output.Position=offset;writer.Write(encrypted);
            output.Position=12+i*44L;writer.Write(file.Id);writer.Write(offset);writer.Write(encrypted.Length);writer.Write(nonce);writer.Write(tag);
        }
        output.Flush(true);
    }
}
