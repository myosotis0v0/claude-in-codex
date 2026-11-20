using SkiaSharp;

namespace MotionLab;

internal static class MenuHairBuild
{
    internal static void Run(string repository)
    {
        var root=Path.Combine(repository,"plugins/claude-in-codex/assets/motion");
        var source=Path.Combine(root,"layers");var output=Path.Combine(root,MenuHair.DirectoryName);
        // Trace the upper edge of the original inner ink so that stroke belongs to the front surface.
        (float X,float Y)[] trace=[(0,632),(55,611),(90,537),(120,475),(155,418),(200,351),(245,293),
            (285,251),(330,204),(390,155),(440,126),(500,107),(540,107),(580,118),(610,136),(627,150),
            (645,141),(680,118),(735,111),(790,129),(845,167),(895,221),(945,294),(995,394),
            (1040,494),(1090,570),(1145,621),(1254,634)];
        const int size=1254;
        using var builder=new SKPathBuilder();builder.MoveTo(trace[0].X,trace[0].Y);
        for(int i=0;i<trace.Length-1;i++)
        {
            var a=trace[i];var b=trace[i+1];var prev=trace[Math.Max(0,i-1)];var next=trace[Math.Min(trace.Length-1,i+2)];
            float run=(b.X-a.X)/3;
            builder.CubicTo(a.X+run,a.Y+run*(b.Y-prev.Y)/(b.X-prev.X),
                b.X-run,b.Y-run*(next.Y-a.Y)/(next.X-a.X),b.X,b.Y);
        }
        builder.LineTo(size,size);builder.LineTo(0,size);builder.Close();using var inner=builder.Detach();
        var sample=new SKSamplingOptions(SKFilterMode.Nearest);
        using var backing=Assets.Load(Path.Combine(source,"hair-backing.png"));
        using var crown=new SKBitmap(size,size);using var rear=new SKCanvas(crown);
        rear.DrawBitmap(backing,0,0,sample);
        rear.Save();rear.ClipPath(inner,SKClipOperation.Difference,true);
        foreach(var name in new[]{"hair-front-left","hair-front-center","hair-front-right","hair-cap"})
        {
            using var bitmap=Assets.Load(Path.Combine(source,name+".png"));
            rear.DrawBitmap(bitmap,0,0,sample);
            using var cut=new SKBitmap(size,size);using var front=new SKCanvas(cut);
            front.ClipPath(inner,SKClipOperation.Intersect,true);
            front.DrawBitmap(bitmap,0,0,sample);front.Flush();
            Assets.Save(cut,Path.Combine(output,name+".png"));
        }
        rear.Restore();
        // Overlap the existing painted backing below the visible crown instead of leaving a cut edge.
        Fade(rear,436,560,SKColors.White,SKColors.Transparent);
        rear.Flush();Assets.Save(crown,Path.Combine(output,"hair-crown-rear.png"));
        using var lower=new SKBitmap(size,size);using var lowerCanvas=new SKCanvas(lower);
        lowerCanvas.DrawBitmap(backing,0,0,sample);
        Fade(lowerCanvas,382,454,SKColors.Transparent,SKColors.White);
        lowerCanvas.Flush();Assets.Save(lower,Path.Combine(output,"hair-backing.png"));
    }

    private static void Fade(SKCanvas canvas,int top,int bottom,SKColor first,SKColor last)
    {
        using var shader=SKShader.CreateLinearGradient(new(0,top),new(0,bottom),[first,last],SKShaderTileMode.Clamp);
        using var matte=new SKPaint{Shader=shader,BlendMode=SKBlendMode.DstIn};canvas.DrawPaint(matte);
    }
}
