using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
namespace CardFileMaker;

/// <summary>Unity 5.6 的 UnityFS / serialized v17 资源封装。</summary>
public static class BundleCodec
{
    private sealed record Obj(long Id, int Offset, int Size, int Type, int Table);
    private sealed record Serial(byte[] Bytes, int DataOffset, int[] Types, List<Obj> Objects);
    private sealed record Block(int Raw, int Packed, int Flags);
    private sealed record Directory(string Name, long Offset, long Size);
    private sealed record Bundle(List<Block> Blocks, List<Directory> Nodes, long DataStart);
    private static readonly Dictionary<string, byte[]> Templates = new();
    public static byte[] Build(AssetSpec spec, int id, RgbaImage image, string cab)
    {
        image.Validate();
        if (id < 0 || id > 999999 || image.Width != spec.Width || image.Height != spec.Height) throw new InvalidDataException("资源 ID 或图像尺寸无效。");
        if (!Regex.IsMatch(cab, "^CAB-[0-9a-fA-F]{32}$")) throw new InvalidDataException("CAB 必须为 CAB- 加 32 位十六进制字符。");
        if (spec.Mips < 1 || spec.Mips > 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width,image.Height)))) throw new InvalidDataException("mipmap 数量无效。");
        var key = Assets.All.FirstOrDefault(x => x.Prefix == spec.Prefix && x.Suffix == spec.Suffix)?.Key ?? throw new InvalidDataException("不支持的资源类型。");
        byte[] template;
        lock (Templates)
        {
            if (!Templates.TryGetValue(key,out template!))
            {
                using var stream=TemplateResources.Open(key+".bin"); using var buffer=new MemoryStream();stream.CopyTo(buffer); template=buffer.ToArray(); Templates[key]=template;
            }
        }
        var s=ParseSerial(template); var texture=s.Objects.Single(x=>s.Types[x.Type]==28);
        using var payload=new MemoryStream(); using var pw=new BinaryWriter(payload,Encoding.UTF8,true);
        byte[] meta=template[..s.DataOffset];
        foreach(var o in s.Objects)
        {
            Align(pw,8); int offset=checked((int)payload.Position); byte[] data;
            if(s.Types[o.Type]==28) data=Texture(spec.TextureName(id),image,spec.Mips);
            else if(s.Types[o.Type]==142) data=AssetBundle(template.AsSpan(s.DataOffset+o.Offset,o.Size).ToArray(),spec.BundleName(id));
            else throw new InvalidDataException("模板包含不支持的对象。");
            pw.Write(data); BinaryPrimitives.WriteInt32LittleEndian(meta.AsSpan(o.Table+8),offset);BinaryPrimitives.WriteInt32LittleEndian(meta.AsSpan(o.Table+12),data.Length);
        }
        byte[] serialized=new byte[checked(meta.Length+(int)payload.Length)];meta.CopyTo(serialized,0);payload.ToArray().CopyTo(serialized,meta.Length);BinaryPrimitives.WriteInt32BigEndian(serialized.AsSpan(4),serialized.Length);
        return Wrap(serialized,cab);
    }
    public static BundleInspection Inspect(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);var bundle=ReadBundle(stream);stream.Position=bundle.DataStart;using var raw=new MemoryStream();
        foreach(var b in bundle.Blocks) { var packed=ReadExact(stream,b.Packed);raw.Write(Decode(packed,b.Raw,b.Flags)); }
        var node=bundle.Nodes.FirstOrDefault(x=>x.Name.StartsWith("CAB-",StringComparison.Ordinal))??throw new InvalidDataException("未找到 CAB 节点。");
        var all=raw.ToArray(); if(node.Offset<0||node.Size<0||node.Offset+node.Size>all.Length)throw new InvalidDataException("节点越界。");
        var s=ParseSerial(all.AsSpan(checked((int)node.Offset),checked((int)node.Size)).ToArray());var tex=s.Objects.Single(x=>s.Types[x.Type]==28);var ab=s.Objects.Single(x=>s.Types[x.Type]==142);
        using var tr=new BinaryReader(new MemoryStream(s.Bytes,s.DataOffset+tex.Offset,tex.Size,false),Encoding.UTF8);string textureName=ReadString(tr);int w=tr.ReadInt32(),h=tr.ReadInt32();tr.ReadInt32();int format=tr.ReadInt32(),mips=tr.ReadInt32();tr.ReadBytes(2);Align(tr,4);tr.ReadBytes(32);int len=tr.ReadInt32();
        if(w<1||h<1||w>8192||h>8192||format!=4||len<checked(w*h*4))throw new InvalidDataException("Inspect 仅支持有效的 RGBA32 纹理。");
        byte[] pixels=tr.ReadBytes(checked(w*h*4));if(pixels.Length!=w*h*4)throw new InvalidDataException("纹理像素截断。");Flip(pixels,w,h);
        using var ar=new BinaryReader(new MemoryStream(s.Bytes,s.DataOffset+ab.Offset,ab.Size,false),Encoding.UTF8);string name=ReadString(ar);int preload=ar.ReadInt32();ar.ReadBytes(checked(preload*12));int entries=ar.ReadInt32();if(entries<1)throw new InvalidDataException("资源容器为空。");string path=ReadString(ar);
        return new(node.Name,name,textureName,path,w,h,format,mips,pixels);
    }
    public static IReadOnlyList<string> ReadCabNames(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,4096,FileOptions.SequentialScan);
        return ReadBundle(stream).Nodes.Where(x=>x.Name.StartsWith("CAB-",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Name).ToArray();
    }
    private static byte[] Texture(string name,RgbaImage image,int mips)
    {
        using var pixels=new MemoryStream();byte[] level=(byte[])image.Pixels.Clone();int w=image.Width,h=image.Height;
        for(int m=0;m<mips;m++)
        {
            byte[] flipped=(byte[])level.Clone();Flip(flipped,w,h);pixels.Write(flipped);
            if(m+1<mips)
            {
                int nw=Math.Max(1,w/2),nh=Math.Max(1,h/2);byte[] next=new byte[nw*nh*4];
                for(int y=0;y<nh;y++)for(int x=0;x<nw;x++)for(int c=0;c<4;c++) { int sum=0,n=0;for(int yy=y*2;yy<Math.Min(y*2+2,h);yy++)for(int xx=x*2;xx<Math.Min(x*2+2,w);xx++){sum+=level[(yy*w+xx)*4+c];n++;}next[(y*nw+x)*4+c]=(byte)((sum+n/2)/n); }
                level=next;w=nw;h=nh;
            }
        }
        using var ms=new MemoryStream();using var bw=new BinaryWriter(ms,Encoding.UTF8,true);WriteString(bw,name);bw.Write(image.Width);bw.Write(image.Height);bw.Write(checked((int)pixels.Length));bw.Write(4);bw.Write(mips);bw.Write(false);bw.Write(false);Align(bw,4);
        bw.Write(1);bw.Write(2);bw.Write(mips>1?2:1);bw.Write(1);bw.Write(0f);bw.Write(1);bw.Write(0);bw.Write(1);bw.Write(checked((int)pixels.Length));bw.Write(pixels.ToArray());Align(bw,4);bw.Write(0u);bw.Write(0u);WriteString(bw,"");return ms.ToArray();
    }
    private static byte[] AssetBundle(byte[] data,string name)
    {
        using var r=new BinaryReader(new MemoryStream(data),Encoding.UTF8);using var ms=new MemoryStream();using var w=new BinaryWriter(ms,Encoding.UTF8,true);
        ReadString(r);WriteString(w,name);int n=r.ReadInt32();w.Write(n);w.Write(r.ReadBytes(checked(n*12)));n=r.ReadInt32();w.Write(n);
        for(int i=0;i<n;i++){ReadString(r);WriteString(w,"assets/assetbundles/option/"+name+".png");w.Write(r.ReadBytes(20));}
        w.Write(r.ReadBytes(24));ReadString(r);WriteString(w,name);w.Write(r.ReadBytes(checked((int)(r.BaseStream.Length-r.BaseStream.Position))));return ms.ToArray();
    }
    private static Serial ParseSerial(byte[] b)
    {
        if(b.Length<20||BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(8))!=17||b[16]!=0)throw new InvalidDataException("仅支持 little-endian serialized v17。");
        int data=BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(12));if(data<20||data>b.Length)throw new InvalidDataException("序列化数据偏移无效。");
        using var r=new BinaryReader(new MemoryStream(b),Encoding.UTF8);r.BaseStream.Position=20;ReadZ(r);r.ReadInt32();bool tree=r.ReadBoolean();int count=Count(r.ReadInt32());var types=new int[count];
        for(int i=0;i<count;i++) { int cl=r.ReadInt32();types[i]=cl;r.ReadBoolean();r.ReadInt16();if(cl==114)r.ReadBytes(16);r.ReadBytes(16);if(tree){int nodes=Count(r.ReadInt32()),strings=Count(r.ReadInt32());r.BaseStream.Position=checked(r.BaseStream.Position+nodes*24L+strings);} }
        count=Count(r.ReadInt32());var objects=new List<Obj>();for(int i=0;i<count;i++){Align(r,4);int table=checked((int)r.BaseStream.Position);long id=r.ReadInt64();int off=r.ReadInt32(),size=r.ReadInt32(),type=r.ReadInt32();if(type<0||type>=types.Length||off<0||size<0||(long)data+off+size>b.Length)throw new InvalidDataException("对象表无效。");objects.Add(new(id,off,size,type,table));}return new(b,data,types,objects);
    }
    private static Bundle ReadBundle(Stream s)
    {
        using var r=new BinaryReader(s,Encoding.UTF8,true);if(ReadZ(r)!="UnityFS")throw new InvalidDataException("不是 UnityFS 资源。");int version=BE32(r);if(version!=6)throw new InvalidDataException("仅支持 UnityFS v6。");ReadZ(r);ReadZ(r);long size=BE64(r);int packed=BE32(r),raw=BE32(r),flags=BE32(r);long header=s.Position;
        if(size< header||size>s.Length||packed<0||packed>64*1024*1024||raw<0||raw>64*1024*1024)throw new InvalidDataException("UnityFS 块信息大小无效。");
        if((flags&128)!=0)s.Position=size-packed;byte[] info=Decode(ReadExact(s,packed),raw,flags);long start=(flags&128)!=0?header:header+packed;
        using var ir=new BinaryReader(new MemoryStream(info),Encoding.UTF8);ir.ReadBytes(16);int n=Count(BE32(ir));var blocks=new List<Block>();for(int i=0;i<n;i++){int u=BE32(ir),c=BE32(ir);int f=(ir.ReadByte()<<8)|ir.ReadByte();if(u<0||c<0)throw new InvalidDataException("数据块大小无效。");blocks.Add(new(u,c,f));}
        n=Count(BE32(ir));var nodes=new List<Directory>();for(int i=0;i<n;i++){long o=BE64(ir),z=BE64(ir);BE32(ir);nodes.Add(new(ReadZ(ir),o,z));}if((flags&512)!=0)start=(start+15)&~15L;return new(blocks,nodes,start);
    }
    private static byte[] Decode(byte[] src,int length,int flags)
    {
        int compression=flags&63;if(compression==0){if(src.Length!=length)throw new InvalidDataException("未压缩数据长度不符。");return src;}
        if(compression!=2&&compression!=3)throw new InvalidDataException($"不支持压缩类型 {compression}（CAB 扫描不需要解压资源正文）。");
        byte[] dst=new byte[length];int p=0,o=0;
        int Extended(int n){if(n==15){int v;do{if(p>=src.Length)throw new InvalidDataException("LZ4 长度截断。");v=src[p++];n=checked(n+v);}while(v==255);}return n;}
        while(p<src.Length){int token=src[p++],literal=Extended(token>>4);if(literal>src.Length-p||literal>dst.Length-o)throw new InvalidDataException("LZ4 字面数据越界。");src.AsSpan(p,literal).CopyTo(dst.AsSpan(o));p+=literal;o+=literal;if(p==src.Length)break;if(p+2>src.Length)throw new InvalidDataException("LZ4 偏移截断。");int distance=src[p]|src[p+1]<<8;p+=2;if(distance==0||distance>o)throw new InvalidDataException("LZ4 匹配偏移无效。");int match=checked(Extended(token&15)+4);if(match>dst.Length-o)throw new InvalidDataException("LZ4 匹配越界。");for(int j=0;j<match;j++)dst[o+j]=dst[o+j-distance];o+=match;}
        if(o!=length)throw new InvalidDataException("LZ4 输出长度不符。");return dst;
    }
    private static byte[] Wrap(byte[] serialized,string cab)
    {
        using var info=new MemoryStream();using(var w=new BinaryWriter(info,Encoding.UTF8,true)){w.Write(new byte[16]);WBE32(w,1);WBE32(w,serialized.Length);WBE32(w,serialized.Length);w.Write(new byte[2]);WBE32(w,1);WBE64(w,0);WBE64(w,serialized.Length);WBE32(w,4);WZ(w,cab);}
        using var output=new MemoryStream();using var bw=new BinaryWriter(output,Encoding.UTF8,true);WZ(bw,"UnityFS");WBE32(bw,6);WZ(bw,"5.x.x");WZ(bw,"5.6.4f1");long sizePosition=output.Position;WBE64(bw,0);WBE32(bw,checked((int)info.Length));WBE32(bw,checked((int)info.Length));WBE32(bw,64);bw.Write(info.ToArray());bw.Write(serialized);long size=output.Length;output.Position=sizePosition;WBE64(bw,size);return output.ToArray();
    }
    private static void Flip(byte[] p,int w,int h){int stride=checked(w*4);byte[] row=new byte[stride];for(int y=0;y<h/2;y++){int a=y*stride,b=(h-1-y)*stride;Buffer.BlockCopy(p,a,row,0,stride);Buffer.BlockCopy(p,b,p,a,stride);Buffer.BlockCopy(row,0,p,b,stride);}}
    private static int Count(int n){if(n<0||n>1000000)throw new InvalidDataException("数组计数无效。");return n;}
    private static void Align(BinaryReader r,int n)=>r.BaseStream.Position=(r.BaseStream.Position+n-1)&~(n-1L);
    private static void Align(BinaryWriter w,int n){while(w.BaseStream.Position%n!=0)w.Write((byte)0);}
    private static string ReadString(BinaryReader r){int n=Count(r.ReadInt32());byte[] b=r.ReadBytes(n);if(b.Length!=n)throw new InvalidDataException("字符串截断。");Align(r,4);return Encoding.UTF8.GetString(b);}
    private static void WriteString(BinaryWriter w,string text){byte[] b=Encoding.UTF8.GetBytes(text);w.Write(b.Length);w.Write(b);Align(w,4);}
    private static string ReadZ(BinaryReader r){using var m=new MemoryStream();for(int i=0;i<1048576;i++){byte c=r.ReadByte();if(c==0)return Encoding.UTF8.GetString(m.ToArray());m.WriteByte(c);}throw new InvalidDataException("字符串过长。");}
    private static void WZ(BinaryWriter w,string text){w.Write(Encoding.UTF8.GetBytes(text));w.Write((byte)0);}
    private static byte[] ReadExact(Stream s,int n){byte[] b=new byte[n];s.ReadExactly(b);return b;}
    private static int BE32(BinaryReader r)=>BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
    private static long BE64(BinaryReader r)=>BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
    private static void WBE32(BinaryWriter w,int n){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(b,n);w.Write(b);}
    private static void WBE64(BinaryWriter w,long n){Span<byte>b=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(b,n);w.Write(b);}
}
