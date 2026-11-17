using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeCompanion;

public sealed class PetView : FrameworkElement
{
    private MotionSurface? motion;
    private DateTimeOffset frameMoment=DateTimeOffset.UtcNow;
    private IReadOnlyDictionary<string,string> threadTitles=new Dictionary<string,string>();
    internal void SetThreadTitles(IReadOnlyDictionary<string,string> titles){threadTitles=titles;InvalidateVisual();}
    internal bool HasMotion=>motion!=null;
    internal double ArtworkSize {get;set;}=224;
    internal Vector RenderInset {get;set;}
    internal MotionController? Controller=>motion?.Controller;
    internal bool EnableMotion()
    {
        try{motion=new MotionSurface();return true;}
        catch(Exception ex) when(ex is IOException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {Settings.Log("Motion fallback: "+ex.GetType().Name);return false;}
    }
    internal void DisposeMotion(){motion?.Dispose();motion=null;}
    internal void Animate(double delta,bool wind,DateTimeOffset? now=null)
    {
        frameMoment=now??DateTimeOffset.UtcNow;
        motion?.Controller.Step(delta,gaze,wind,frameMoment);
        if(motion!=null)InvalidateVisual();
    }
    internal bool HeadHit(Point p)=>p.Y<RenderInset.Y+ArtworkSize*.76&&Hit(p.X,p.Y);
    internal bool HoverTipDisabled=>ToolTip==null&&!System.Windows.Controls.ToolTipService.GetIsEnabled(this);
    private readonly BitmapSource artwork, eye;
    private readonly LashRig lashes;
    private readonly List<(ImageBrush Mask, Point Origin, bool ViewerLeft)> eyes = [];
    private readonly BitmapSource hitImage;
    private Vector gaze;
    private Vector leftOffset, rightOffset;
    private readonly GazeLimits gazeLimits;
    internal GazeLimits Limits=>gazeLimits;
    internal Vector LeftOffset=>leftOffset;
    internal Vector RightOffset=>rightOffset;
    private readonly Vector edgeInset;
    internal Vector CanvasOffset=>new(edgeInset.X*ActualWidth/1254,edgeInset.Y*ActualHeight/1254);
    internal bool ShowEyes {get;set;}=true;
    internal bool AnimateLashes {get;set;}=true;
    public PetView()
    {
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        var root = Path.Combine(AppContext.BaseDirectory,"assets");
        artwork=Load(Path.Combine(root,"base.png"));
        eye=Load(Path.Combine(root,"eye.png"));
        lashes=new LashRig(root);
        hitImage=new FormatConvertedBitmap(Load(Path.Combine(root,"base.png")),PixelFormats.Bgra32,null,0);
        hitImage.Freeze();
        var alphaPixels=new byte[hitImage.PixelWidth*hitImage.PixelHeight*4];hitImage.CopyPixels(alphaPixels,hitImage.PixelWidth*4,0);
        var right=0;var bottom=0;
        for(var y=0;y<hitImage.PixelHeight;y++)for(var x=0;x<hitImage.PixelWidth;x++)
            if(alphaPixels[(y*hitImage.PixelWidth+x)*4+3]>32){right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
        edgeInset=new Vector(hitImage.PixelWidth-right,hitImage.PixelHeight-bottom);
        ClipToBounds=true;
        using var rig=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"rig.json")));
        var maxOffset=rig.RootElement.GetProperty("maxOffset");
        gazeLimits=new GazeLimits(maxOffset[0].GetDouble(),rig.RootElement.GetProperty("inwardOffset").GetDouble(),maxOffset[1].GetDouble());
        foreach(var item in rig.RootElement.GetProperty("eyes").EnumerateArray())
        {
            var mask=LoadMask(Path.Combine(root,item.GetProperty("mask").GetString()!));
            var origin=item.GetProperty("origin");
            var brush=new ImageBrush(mask) { Stretch=Stretch.Fill, ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,1254,1254), TileMode=TileMode.None };
            brush.Freeze();
            eyes.Add((brush,new Point(origin[0].GetDouble(),origin[1].GetDouble()),item.GetProperty("name").GetString()=="left"));
        }
    }
    internal static BitmapSource Load(string path)
    {
        var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.UriSource=new Uri(path); image.EndInit(); image.Freeze(); return image;
    }
    private static BitmapSource LoadMask(string path)
    {
        var source=new FormatConvertedBitmap(Load(path),PixelFormats.Gray8,null,0);
        var bytes=new byte[source.PixelWidth*source.PixelHeight]; source.CopyPixels(bytes,source.PixelWidth,0);
        var rgba=new byte[bytes.Length*4];
        for(var i=0;i<bytes.Length;i++) { rgba[i*4]=255; rgba[i*4+1]=255; rgba[i*4+2]=255; rgba[i*4+3]=bytes[i]; }
        var image=BitmapSource.Create(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Bgra32,null,rgba,source.PixelWidth*4); image.Freeze(); return image;
    }
    public void Look(Vector target,bool instant=false)
    {
        target=GazeMotion.Map(target);
        var next=instant?target:gaze+(target-gaze)*0.25;
        if ((next-gaze).Length*Math.Max(gazeLimits.Outward,gazeLimits.Vertical)<0.05) return;
        gaze=next;
        SetEyeOffsets(GazeMotion.Offset(gaze,true,gazeLimits),GazeMotion.Offset(gaze,false,gazeLimits));
    }
    internal void SetEyeOffsets(Vector left,Vector right)
    {
        leftOffset=left;rightOffset=right;InvalidateVisual();
    }
    public bool Hit(double x,double y)
    {
        if(motion!=null)return motion.Hit(x,y,VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (ActualWidth<=0 || ActualHeight<=0) return false;
        var sx=(x-CanvasOffset.X)/ActualWidth*1254;var sy=(y-CanvasOffset.Y)/ActualHeight*1254;
        if(sx<0||sy<0||sx>=1254||sy>=1254)return false;
        var px=(int)sx;var py=(int)sy;
        var pixel=new byte[4]; hitImage.CopyPixels(new Int32Rect(px,py,1,1),pixel,4,0); return pixel[3]>32;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(motion!=null)
        {
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            var frame=motion.Draw((int)Math.Ceiling(ActualWidth*dpi),(int)Math.Ceiling(ActualHeight*dpi),
                (float)(ArtworkSize*dpi),(float)(RenderInset.X*dpi),(float)(RenderInset.Y*dpi));
            DrawMotionLayers(dc,frame,new Rect(0,0,ActualWidth,ActualHeight),new Point(),
                motion.Controller.Clock,0,TryFindResource("PanelBrush") as Brush??Brushes.Black,
                bubbles:motion.Controller.Bubbles(frameMoment).Select(b=>b with{Title=threadTitles.GetValueOrDefault(b.Session)??""}).ToArray(),
                area:new Rect(RenderInset.X,Math.Max(1,RenderInset.Y-94),ArtworkSize,92));
            return;
        }
        dc.PushTransform(new MatrixTransform(ActualWidth/1254,0,0,ActualHeight/1254,CanvasOffset.X,CanvasOffset.Y));
        var rect=new Rect(0,0,1254,1254);
        var leftPose=lashes.Pose(leftOffset.Y/gazeLimits.Vertical);
        var rightPose=lashes.Pose(rightOffset.Y/gazeLimits.Vertical);
        var deform=ShowEyes&&AnimateLashes&&(leftPose!=lashes.Steps||rightPose!=lashes.Steps);
        // Keep one full base pass. The skin base changes only extracted lash pixels, not rectangular face patches.
        dc.DrawImage(deform?lashes.SkinArtwork:artwork,rect);
        foreach(var item in eyes.Where(_=>ShowEyes))
        {
            var offset=item.ViewerLeft?leftOffset:rightOffset;
            var pose=deform?lashes.Get(item.ViewerLeft,item.ViewerLeft?leftPose:rightPose):default;
            dc.PushOpacityMask(deform?pose.Mask:item.Mask);
            dc.DrawImage(eye,new Rect(item.Origin.X+offset.X,item.Origin.Y+offset.Y,148,244));
            dc.Pop();
            if(deform)dc.DrawImage(pose.Lashes,lashes.Region(item.ViewerLeft));
        }
        dc.Pop();
    }
    internal static void DrawMotionLayers(DrawingContext dc,ImageSource character,Rect bounds,Point bubbleOrigin,double clock,double opacity,Brush background,string state="thinking",
        IReadOnlyList<TaskBubbleFrame>? bubbles=null,Rect? area=null)
    {
        dc.DrawImage(character,bounds);
        if(bubbles==null)ActivityBubble.Draw(dc,bubbleOrigin,clock,opacity,background,state);
        else foreach(var (bubble,rect) in ActivityBubble.Layout(bubbles,area??bounds))
        {
            dc.PushTransform(new TranslateTransform(rect.X,rect.Y));dc.PushTransform(new ScaleTransform(rect.Width/ActivityBubble.Width(bubble.Title),rect.Height/25));
            ActivityBubble.Draw(dc,new Point(),clock+bubble.Slot*.7,bubble.Opacity,background,bubble.State,bubble.Progress,bubble.Title);
            dc.Pop();dc.Pop();
        }
    }
}
