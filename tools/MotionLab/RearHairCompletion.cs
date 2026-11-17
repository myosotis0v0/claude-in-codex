using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class RearHairCompletion
{
    internal const int ProtectedBottom=1140,JoinBottom=1198,Height=1500;
    internal static void Build(string repository)
    {
        using var original=Assets.Load(Path.Combine(repository,"assets/claude-motion-v3/layers/rear-hair.png"));
        using var generated=Assets.Load(Path.Combine(repository,"assets/claude-motion-v4/generated/rear-hair-extension.png"));
        using var scaled=Assets.Empty(Assets.Size,Height);
        var scale=Assets.Size/(float)generated.Width;
        using(var c=new SKCanvas(scaled))c.DrawBitmap(generated,new SKRect(0,0,Assets.Size,generated.Height*scale),new SKSamplingOptions(SKCubicResampler.Mitchell));
        static int Top(SKBitmap image,int x)
        {for(var y=0;y<image.Height;y++)if(image.GetPixel(x,y).Alpha>200)return y;return -1;}
        var offsets=Enumerable.Range(0,Assets.Size/8).Select(i=>i*8).Select(x=>(Old:Top(original,x),New:Top(scaled,x))).Where(p=>p.Old>=0&&p.New>=0).Select(p=>p.Old-p.New).Order().ToArray();
        var offset=offsets[offsets.Length/2];
        using var aligned=Assets.Empty(Assets.Size,Height);
        using(var c=new SKCanvas(aligned))c.DrawBitmap(scaled,0,offset,new SKSamplingOptions(SKFilterMode.Nearest));
        using var result=Assets.Empty(Assets.Size,Height);
        for(var y=0;y<Height;y++)for(var x=0;x<Assets.Size;x++)
        {
            var old=y<original.Height?original.GetPixel(x,y):SKColors.Transparent;
            if(y<ProtectedBottom){result.SetPixel(x,y,old);continue;}
            var next=aligned.GetPixel(x,y);var t=Math.Clamp((y-ProtectedBottom)/(float)(JoinBottom-ProtectedBottom),0,1);t=t*t*(3-2*t);
            var a=old.Alpha*(1-t);var b=next.Alpha*t;var alpha=a+b;
            byte Channel(byte p,byte q)=>(byte)Math.Clamp((int)Math.Round((p*a+q*b)/alpha),0,255);
            result.SetPixel(x,y,alpha<.5f?SKColors.Transparent:new SKColor(Channel(old.Red,next.Red),Channel(old.Green,next.Green),Channel(old.Blue,next.Blue),(byte)Math.Round(alpha)));
        }
        var changed=0;for(var y=0;y<ProtectedBottom;y++)for(var x=0;x<Assets.Size;x++)if(result.GetPixel(x,y)!=original.GetPixel(x,y))changed++;
        if(changed!=0)throw new InvalidDataException("Protected upper rear hair changed.");
        var path=Path.Combine(repository,"assets/claude-motion-v4/layers/rear-hair.png");Assets.Save(result,path);
        File.Copy(path,Path.Combine(repository,"plugins/claude-in-codex/assets/motion/layers/rear-hair.png"),true);
        var output=Path.Combine(repository,"artifacts/rear-hair-extension");
        Assets.Save(aligned,Path.Combine(output,"registered-generation.png"));
        Assets.Save(result,Path.Combine(output,"completed-layer.png"));
        File.WriteAllText(Path.Combine(output,"material-report.json"),JsonSerializer.Serialize(new{Success=true,ProtectedDifferentPixels=changed,ProtectedBottom,JoinBottom,Width=Assets.Size,Height,GeneratedRegistrationScale=scale,GeneratedRegistrationY=offset,
            Source="Built-in image generation; only generated lower hair is composited. No repeated rows, texture extrapolation or procedural strand drawing."},Assets.Json));
        Console.WriteLine($"Generated rear hair registered at y={offset}; protected differences={changed}; output={Assets.Size}x{Height}");
    }
}
