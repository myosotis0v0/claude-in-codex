using SkiaSharp;
using System.IO;

namespace MotionLab;
internal sealed partial class Renderer
{
    internal void PreviewPeripherals(string root)
    {
        var dir=Path.Combine(root,"assets/peripherals");
        using var mouse=SKBitmap.Decode(Path.Combine(dir,"mouse.png"));
        using var keyboard=SKBitmap.Decode(Path.Combine(dir,"keyboard.png"));
        var output=Path.Combine(root,"previews/peripherals");Directory.CreateDirectory(output);
        var pose=new Pose();Rig.Update(pose,0,0);
        foreach(var size in new[]{256,512}){
            using var bitmap=new SKBitmap(size,size,SKColorType.Bgra8888,SKAlphaType.Premul);
            using var c=new SKCanvas(bitmap);c.Clear(SKColors.Transparent);
            c.Scale(size/1400f);c.Translate(73,73);
            DrawHead(c,pose);
            // Device tops are occluded by the original hand mesh, not baked-in fingers.
            c.DrawBitmap(mouse,new SKRect(213,1200,418,1278),sampling,paint);
            c.DrawBitmap(keyboard,new SKRect(419,1201,1154,1278),sampling,paint);
            DrawMesh(c,3,"clothes-paws");
            using var png=bitmap.Encode(SKEncodedImageFormat.Png,100);
            using var f=File.Create(Path.Combine(output,$"assembled-{size}.png"));png.SaveTo(f);
        }
    }
}
