using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CardFileMaker;
public sealed class CropState
{
    public AssetSpec Spec { get; }
    public BitmapSource? Image {get;set;}
    public string SourceName {get;set;}="";
    public CropTransform Transform {get;set;}
    public CropState(AssetSpec spec) {Spec=spec;}
    public int EffectiveWidth=>Spec.Key=="battle"?768:Spec.Width;
    public void Fit(bool cover) {if(Image!=null)Transform=CropGeometry.Fit(Image.PixelWidth,Image.PixelHeight,EffectiveWidth,Spec.Height,cover);}
    public void Center(){if(Image!=null)Transform=CropGeometry.Center(Image.PixelWidth,Image.PixelHeight,EffectiveWidth,Spec.Height);}
    public BitmapSource Render()
    {
        if(Image==null)throw new InvalidOperationException("请先为 "+Spec.Label+" 导入图片。");
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var dc=visual.RenderOpen()){
            dc.PushClip(new RectangleGeometry(new Rect(0,0,EffectiveWidth,Spec.Height)));
            dc.DrawImage(Image,new Rect(Transform.X,Transform.Y,Image.PixelWidth*Transform.Scale,Image.PixelHeight*Transform.Scale));dc.Pop();
        }
        var bmp=new RenderTargetBitmap(Spec.Width,Spec.Height,96,96,PixelFormats.Pbgra32);bmp.Render(visual);bmp.Freeze();return bmp;
    }
    public BitmapSource Thumbnail(){
        if(Image==null)throw new InvalidOperationException("尚无素材。");
        double factor=112.0/Spec.Width;var v=new DrawingVisual();RenderOptions.SetBitmapScalingMode(v,BitmapScalingMode.HighQuality);
        using(var dc=v.RenderOpen()){
            dc.PushClip(new RectangleGeometry(new Rect(0,0,EffectiveWidth*factor,Spec.Height*factor)));
            dc.DrawImage(Image,new Rect(Transform.X*factor,Transform.Y*factor,Image.PixelWidth*Transform.Scale*factor,Image.PixelHeight*Transform.Scale*factor));dc.Pop();
        }
        var b=new RenderTargetBitmap(112,(int)Math.Ceiling(Spec.Height*factor),96,96,PixelFormats.Pbgra32);b.Render(v);b.Freeze();return b;
    }
    public RgbaImage Rgba()
    {
        var bmp=new FormatConvertedBitmap(Render(),PixelFormats.Bgra32,null,0);var b=new byte[Spec.Width*Spec.Height*4];bmp.CopyPixels(b,Spec.Width*4,0);
        for(int i=0;i<b.Length;i+=4)(b[i],b[i+2])=(b[i+2],b[i]);
        return new(Spec.Width,Spec.Height,b);
    }
}
public sealed class CropCanvas : FrameworkElement
{
    public CropState? State {get;set;}
    public Action? Changed {get;set;}
    Point? last;
    Rect DrawArea {
        get {
            if(State==null)return Rect.Empty;double f=Math.Max(.01,Math.Min((ActualWidth-48)/State.Spec.Width,(ActualHeight-48)/State.Spec.Height));
            double w=State.Spec.Width*f,h=State.Spec.Height*f;return new((ActualWidth-w)/2,(ActualHeight-h)/2,w,h);
        }
    }
    public CropCanvas(){RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);ClipToBounds=true;Focusable=true;Cursor=Cursors.Hand;MinHeight=180;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18,24,32)),null,new Rect(0,0,ActualWidth,ActualHeight));
        if(State==null)return;var a=DrawArea;dc.PushClip(new RectangleGeometry(a));
        dc.DrawRectangle(Brushes.WhiteSmoke,null,a);
        for(double y=a.Y;y<a.Bottom;y+=16)for(double x=a.X;x<a.Right;x+=16)if(((int)((x-a.X)/16)+(int)((y-a.Y)/16))%2==0)dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(211,218,225)),null,new Rect(x,y,16,16));
        double f=a.Width/State.Spec.Width;
        if(State.Image!=null)dc.DrawImage(State.Image,new Rect(a.X+State.Transform.X*f,a.Y+State.Transform.Y*f,State.Image.PixelWidth*State.Transform.Scale*f,State.Image.PixelHeight*State.Transform.Scale*f));
        if(State.Spec.Key=="battle"){
            var x=a.X+768*f;dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(225,24,37,47)),null,new Rect(x,a.Y,a.Right-x,a.Height));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(58,220,184)),2),new Point(x,a.Y),new Point(x,a.Bottom));
        }
        dc.Pop();dc.DrawRectangle(null,new Pen(new SolidColorBrush(Color.FromRgb(83,116,138)),1),a);
        var effectiveW=State.EffectiveWidth*f;
        var guides=new Pen(new SolidColorBrush(Color.FromArgb(85,255,255,255)),1){DashStyle=DashStyles.Dash};
        dc.DrawLine(guides,new(a.X+effectiveW/2,a.Y),new(a.X+effectiveW/2,a.Bottom));
        dc.DrawLine(guides,new(a.X,a.Y+a.Height/2),new(a.X+effectiveW,a.Y+a.Height/2));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();if(State?.Image!=null){last=e.GetPosition(this);CaptureMouse();e.Handled=true;}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(last is not Point old||State?.Image==null)return;var p=e.GetPosition(this);double f=DrawArea.Width/State.Spec.Width;State.Transform=State.Transform with{X=State.Transform.X+(p.X-old.X)/f,Y=State.Transform.Y+(p.Y-old.Y)/f};last=p;InvalidateVisual();Changed?.Invoke();}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);last=null;ReleaseMouseCapture();}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);last=null;}
    protected override void OnMouseWheel(MouseWheelEventArgs e){base.OnMouseWheel(e);if(State?.Image==null)return;var a=DrawArea;double f=a.Width/State.Spec.Width;var p=e.GetPosition(this);Zoom(Math.Pow(1.12,e.Delta/120.0),(p.X-a.X)/f,(p.Y-a.Y)/f);e.Handled=true;}
    public void Zoom(double ratio,double? cx=null,double? cy=null)
    {
        if(State?.Image==null)return;var t=State.Transform;double scale=Math.Clamp(t.Scale*ratio,.005,100);ratio=scale/t.Scale;double x=cx??State.EffectiveWidth/2.0,y=cy??State.Spec.Height/2.0;
        State.Transform=new(scale,x-(x-t.X)*ratio,y-(y-t.Y)*ratio);InvalidateVisual();Changed?.Invoke();
    }
}
