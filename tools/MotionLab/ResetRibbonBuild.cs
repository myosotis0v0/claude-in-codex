using SkiaSharp;

namespace MotionLab;

internal static class ResetRibbonBuild
{
    internal static void Run(string repository)
    {
        var root=Path.Combine(repository,"plugins/claude-in-codex/assets/motion/layers");
        using var image=Assets.Load(Path.Combine(root,"accessory.png"));
        using var result=new SKBitmap(image.Info);using var canvas=new SKCanvas(result);
        var pixel=new SKSamplingOptions(SKFilterMode.Nearest);
        canvas.DrawBitmap(image,0,0,pixel);
        using var mark=new SKBitmap(224,224);using(var stamp=new SKCanvas(mark))
        {
            stamp.Clear(SKColors.Transparent);stamp.Translate(112,112);stamp.Scale(9.5f);stamp.Translate(-12,-12);
            using var ink=new SKPaint{IsAntialias=true,Color=SKColor.Parse("#553023"),Style=SKPaintStyle.Stroke,StrokeWidth=3.5f,StrokeCap=SKStrokeCap.Round};
            stamp.DrawArc(new SKRect(4.5f,4.5f,19.5f,19.5f),-27,287,false,ink);
            using var arrow=new SKPathBuilder();arrow.MoveTo(18f,4.9f);arrow.LineTo(9.5f,.6f);arrow.LineTo(9.5f,9.1f);arrow.Close();
            ink.Style=SKPaintStyle.Fill;using var path=arrow.Detach();stamp.DrawPath(path,ink);stamp.Flush();
        }
        // The approved 01 stamp follows the four corners of the existing cream tail.
        var a=new SKPoint(1090,999);var b=new SKPoint(1141,984);var c=new SKPoint(1166,1037);var d=new SKPoint(1094,1059);
        var v=b-c;var w=d-c;var delta=a-b+c-d;var denominator=v.X*w.Y-w.X*v.Y;
        var g=(delta.X*w.Y-w.X*delta.Y)/denominator;var h=(v.X*delta.Y-delta.X*v.Y)/denominator;
        var n=mark.Width;
        var projection=new SKMatrix{ScaleX=(b.X-a.X+g*b.X)/n,SkewX=(d.X-a.X+h*d.X)/n,TransX=a.X,
            SkewY=(b.Y-a.Y+g*b.Y)/n,ScaleY=(d.Y-a.Y+h*d.Y)/n,TransY=a.Y,Persp0=g/n,Persp1=h/n,Persp2=1};
        canvas.Concat(projection);canvas.DrawBitmap(mark,0,0,new SKSamplingOptions(SKCubicResampler.Mitchell));canvas.Flush();
        for(int y=973;y<1070;y++)for(int x=1078;x<1178;x++)
        {
            var original=image.GetPixel(x,y);var value=result.GetPixel(x,y);
            if(original.Alpha!=value.Alpha||value!=original&&(original.Red<185||original.Green<145||original.Blue<120))
                throw new InvalidOperationException($"Stamp extends outside the pale ribbon at {x},{y}.");
        }
        Assets.Save(result,Path.Combine(root,"accessory-reset.png"));
    }
}
