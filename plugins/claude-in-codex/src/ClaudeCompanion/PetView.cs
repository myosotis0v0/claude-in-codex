using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeCompanion;

public sealed class PetView : FrameworkElement
{
    private readonly BitmapSource foreground, underlay, eye;
    private readonly List<(ImageBrush Mask, Point Origin)> eyes = [];
    private readonly BitmapSource hitImage;
    private Vector gaze;
    public PetView()
    {
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        var root = Path.Combine(AppContext.BaseDirectory,"assets");
        foreground=Load(Path.Combine(root,"foreground.png"));
        underlay=Load(Path.Combine(root,"skin-underlay.png"));
        eye=Load(Path.Combine(root,"eye.png"));
        hitImage=new FormatConvertedBitmap(Load(Path.Combine(root,"base.png")),PixelFormats.Bgra32,null,0);
        hitImage.Freeze();
        using var rig=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"rig.json")));
        foreach(var item in rig.RootElement.GetProperty("eyes").EnumerateArray())
        {
            var mask=LoadMask(Path.Combine(root,item.GetProperty("mask").GetString()!));
            var origin=item.GetProperty("origin");
            var brush=new ImageBrush(mask) { Stretch=Stretch.Fill, ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,1254,1254), TileMode=TileMode.None };
            brush.Freeze();
            eyes.Add((brush,new Point(origin[0].GetDouble(),origin[1].GetDouble())));
        }
    }
    private static BitmapSource Load(string path)
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
        target=new Vector(Math.Clamp(target.X,-1,1)*18,Math.Clamp(target.Y,-1,1)*8);
        var next=instant?target:gaze+(target-gaze)*0.25;
        if ((next-gaze).Length<0.05) return;
        gaze=next; InvalidateVisual();
    }
    public bool Hit(double x,double y)
    {
        if (ActualWidth<=0 || ActualHeight<=0) return false;
        var px=Math.Clamp((int)(x/ActualWidth*1254),0,1253); var py=Math.Clamp((int)(y/ActualHeight*1254),0,1253);
        var pixel=new byte[4]; hitImage.CopyPixels(new Int32Rect(px,py,1,1),pixel,4,0); return pixel[3]>32;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(ActualWidth/1254,ActualHeight/1254));
        var rect=new Rect(0,0,1254,1254);
        dc.DrawImage(underlay,rect);
        foreach(var item in eyes)
        {
            dc.PushOpacityMask(item.Mask);
            dc.DrawImage(eye,new Rect(item.Origin.X+gaze.X,item.Origin.Y+gaze.Y,148,244));
            dc.Pop();
        }
        dc.DrawImage(foreground,rect); dc.Pop();
    }
}
