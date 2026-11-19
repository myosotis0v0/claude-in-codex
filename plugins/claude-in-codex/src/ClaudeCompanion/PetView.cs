using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionLab;

namespace ClaudeCompanion;

public sealed class PetView : FrameworkElement
{
    private MotionSurface? motion;
    private LatestMotionRenderer? background;
    private MotionPresenter? presenter;
    private MotionController? backgroundController;
    private DeskHands? backgroundHands;
    private MotionFrame? requestedFrame;
    private DateTimeOffset frameMoment=DateTimeOffset.UtcNow;
    private IReadOnlyDictionary<string,string> threadTitles=new Dictionary<string,string>();
    internal void SetThreadTitles(IReadOnlyDictionary<string,string> titles){threadTitles=titles;InvalidateVisual();}
    internal bool HasMotion=>motion!=null||background!=null;
    internal double ArtworkSize {get;set;}=224;
    internal Vector RenderInset {get;set;}
    internal MotionController? Controller=>backgroundController??motion?.Controller;
    internal DeskHands? Hands=>backgroundHands??motion?.Renderer.Hands;
    internal int DisplayedFrames=>presenter?.DisplayedFrames??0;
    internal bool EnableMotion(bool useBackground=false)
    {
        try
        {
            if(useBackground)
            {
                var profile=CharacterRig.Load(MotionRaster.Assets,required:true);
                backgroundController=new MotionController(profile:profile);backgroundHands=new DeskHands();
                presenter=new MotionPresenter();
                background=new LatestMotionRenderer(profile,Dispatcher,FramesChanged);
            }
            else motion=new MotionSurface();
            return true;
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {Settings.Log("Motion fallback: "+ex.GetType().Name);return false;}
    }
    private void FramesChanged()
    {
        if(background?.GetFailure() is {} failure)
        {
            Settings.Log("Motion fallback: "+failure.GetType().Name);DisposeMotion();
        }
        InvalidateVisual();
    }
    internal void DisposeMotion()
    {
        background?.Dispose();background=null;
        presenter?.Dispose();presenter=null;
        backgroundController=null;backgroundHands=null;requestedFrame=null;
        motion?.Dispose();motion=null;
    }
    private void DiscardFrames()
    {
        background?.Discard();presenter?.Discard();requestedFrame=null;
    }
    internal void Animate(double delta,bool wind,DateTimeOffset? now=null)
    {
        frameMoment=now??DateTimeOffset.UtcNow;
        var pose=Controller?.Step(delta,gaze,wind,frameMoment);
        if(Hands is {Enabled:true} h)h.Step((float)Math.Clamp(delta,0,.08),lockMouse:pose?.CelId=="complete");
        if(HasMotion)InvalidateVisual();
    }
    internal Point? BubblePointer {get;set;}
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
        IsVisibleChanged+=(_,e)=>{if(e.NewValue is false)DiscardFrames();else InvalidateVisual();};
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
        if(HasMotion)
        {
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            return presenter?.Hit(new Point(x,y),Geometry(dpi),new Rect(0,0,ActualWidth,ActualHeight))??motion!.Hit(x,y,dpi);
        }
        if (ActualWidth<=0 || ActualHeight<=0) return false;
        var sx=(x-CanvasOffset.X)/ActualWidth*1254;var sy=(y-CanvasOffset.Y)/ActualHeight*1254;
        if(sx<0||sy<0||sx>=1254||sy>=1254)return false;
        var px=(int)sx;var py=(int)sy;
        var pixel=new byte[4]; hitImage.CopyPixels(new Int32Rect(px,py,1,1),pixel,4,0); return pixel[3]>32;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(HasMotion)
        {
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            if(ActualWidth<=0||ActualHeight<=0)return;
            DrawAnimated(dc,Geometry(dpi),new Rect(0,0,ActualWidth,ActualHeight));
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
    private MotionGeometry Geometry(double dpi)=>new(Math.Max(1,(int)Math.Ceiling(ActualWidth*dpi)),
        Math.Max(1,(int)Math.Ceiling(ActualHeight*dpi)),(float)(ArtworkSize*dpi),(float)(RenderInset.X*dpi),(float)(RenderInset.Y*dpi));
    internal void DrawAnimated(DrawingContext dc,MotionGeometry geometry,Rect bounds)
    {
        var controller=Controller!;
        var frame=DrawCharacter(geometry);
        DrawMotionLayers(dc,frame,bounds,new Point(),controller.Clock,0,TryFindResource("PanelBrush") as Brush??Brushes.Black,
            bubbles:controller.Bubbles(frameMoment).Select(b=>b with{Title=threadTitles.GetValueOrDefault(b.Session)??""}).ToArray(),
            area:new Rect(RenderInset.X,Math.Max(1,RenderInset.Y-94),ArtworkSize,92),pointer:BubblePointer,now:frameMoment,
            characterBounds:presenter?.Bounds(geometry,bounds));
    }
    internal WriteableBitmap? DrawCharacter(MotionGeometry geometry)
    {
        if(background==null)return motion?.Draw(geometry.Width,geometry.Height,geometry.ArtworkPixels,geometry.InsetX,geometry.InsetY);
        var controller=backgroundController!;
        var next=new MotionFrame(geometry,controller.Current,controller.Hair,controller.Tip,controller.FrontHair,
            controller.State=="head-touch"?controller.HeadTouch.Snapshot():null,backgroundHands!.Snapshot());
        if(requestedFrame!=next)
        {
            if(requestedFrame is {} previous&&previous.Geometry!=geometry)background.Discard();
            background.Submit(next);requestedFrame=next;
        }
        presenter!.Offer(background.TakeLatest());
        return presenter.Present(geometry);
    }
    internal static void DrawMotionLayers(DrawingContext dc,ImageSource? character,Rect bounds,Point bubbleOrigin,double clock,double opacity,Brush background,string state="thinking",
        IReadOnlyList<TaskBubbleFrame>? bubbles=null,Rect? area=null,Point? pointer=null,DateTimeOffset? now=null,Rect? characterBounds=null)
    {
        if(character!=null)dc.DrawImage(character,characterBounds??bounds);
        (TaskBubbleFrame Bubble,Rect Bounds)? hovered=null;
        if(bubbles==null)ActivityBubble.Draw(dc,bubbleOrigin,clock,opacity,background,state);
        else foreach(var (bubble,rect) in ActivityBubble.Layout(bubbles,area??bounds))
        {
            dc.PushTransform(new TranslateTransform(rect.X,rect.Y));dc.PushTransform(new ScaleTransform(rect.Width/ActivityBubble.Width(bubble),rect.Height/25));
            ActivityBubble.Draw(dc,new Point(),clock+bubble.Slot*.7,bubble.Opacity,background,bubble.State,bubble.Progress,bubble.Title,bubble.Children);
            dc.Pop();dc.Pop();
            if(pointer is {} p&&rect.Contains(p))hovered=(bubble,rect);
        }
        if(hovered is {} hit)ActivityBubble.DrawElapsed(dc,ActivityBubble.Elapsed(hit.Bubble,now??DateTimeOffset.UtcNow),hit.Bounds,bounds,background);
    }
}
