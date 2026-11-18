using System.Text.Json;
using System.Numerics;
using SkiaSharp;

namespace MotionLab;

internal static class RearHairCompletion
{
    internal const int SeamY=1212,Height=1500;
    // Match the original composite at the last clean row: silhouettes, strand lines and both side gaps.
    private static readonly (int X,int SourceX,int SourceY)[] SeamGuide=[
        (0,0,1244),(16,19,1244),(31,31,1244),(58,61,1244),(71,75,1244),(85,79,1244),
        (98,89,1244),(138,135,1244),(221,221,1244),(240,239,1244),(316,334,1244),
        (513,519,1244),(712,720,1244),(775,790,1244),(926,934,1244),(1005,1032,1244),
        (1035,1061,1244),(1105,1133,1269),(1123,1153,1276),(1131,1159,1276),
        (1146,1167,1276),(1161,1178,1276),(1215,1224,1276),(1233,1237,1276),(1254,1254,1276)];
    internal static void Build(string repository)
    {
        var source=Path.Combine(repository,"assets/claude-motion-v3/layers");
        using var original=Assets.Load(Path.Combine(source,"rear-hair.png"));
        using var backing=Assets.Load(Path.Combine(source,"hair-backing.png"));
        using var curtain=Assets.Load(Path.Combine(source,"hair-curtain.png"));
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
        using var originalComposite=Assets.Empty();
        using(var c=new SKCanvas(originalComposite))
        {
            foreach(var part in new[]{original,backing,curtain})c.DrawBitmap(part,0,0,new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var result=Assets.Empty(Assets.Size,Height);using var trimmedCurtain=Assets.Empty();
        for(var x=0;x<Assets.Size;x++)
        {
            var guide=Array.FindIndex(SeamGuide,p=>p.X>x);var a=SeamGuide[guide-1];var b=SeamGuide[guide];
            var t=(x-a.X)/(float)(b.X-a.X);var srcX=a.SourceX+(b.SourceX-a.SourceX)*t;var srcY=a.SourceY+(b.SourceY-a.SourceY)*t;
            var delta=Premul(originalComposite.GetPixel(x,SeamY))-Sample(aligned,srcX,srcY);
            for(var y=0;y<=SeamY;y++){result.SetPixel(x,y,original.GetPixel(x,y));trimmedCurtain.SetPixel(x,y,curtain.GetPixel(x,y));}
            for(var y=SeamY+1;y<Height;y++)
            {
                var depth=y-SeamY;var xShift=1-Smooth(depth/120f);var match=1-Smooth(depth/36f);
                // Keep the generated strands and shading; remove only the colour/alpha jump at the join.
                var pixel=Sample(aligned,x+(srcX-x)*xShift,srcY+depth)+delta*match;
                result.SetPixel(x,y,Unpremul(pixel));
            }
        }
        foreach(var (name,bitmap) in new[]{("rear-hair",result),("hair-curtain",trimmedCurtain)})
        {
            var path=Path.Combine(repository,$"assets/claude-motion-v4/layers/{name}.png");Assets.Save(bitmap,path);
            File.Copy(path,Path.Combine(repository,$"plugins/claude-in-codex/assets/motion/layers/{name}.png"),true);
        }
        var output=Path.Combine(repository,"artifacts/rear-hair-extension");
        Assets.Save(aligned,Path.Combine(output,"registered-generation.png"));
        Assets.Save(result,Path.Combine(output,"completed-layer.png"));
        File.WriteAllText(Path.Combine(output,"material-report.json"),JsonSerializer.Serialize(new{SeamY,Width=Assets.Size,Height,GeneratedRegistrationScale=scale,GeneratedRegistrationY=offset,
            Source="Built-in image generation, aligned to the original lower strand lines and contours."},Assets.Json));
        Console.WriteLine($"Generated rear hair registered at y={offset}; clean join at row {SeamY}; output={Assets.Size}x{Height}");
    }
    private static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    private static Vector4 Premul(SKColor c)=>new(c.Red*c.Alpha/255f,c.Green*c.Alpha/255f,c.Blue*c.Alpha/255f,c.Alpha);
    private static Vector4 Sample(SKBitmap image,float x,float y)
    {
        var ix=(int)MathF.Floor(x);var iy=(int)MathF.Floor(y);
        Vector4 At(int px,int py)=>px>=0&&px<image.Width&&py>=0&&py<image.Height?Premul(image.GetPixel(px,py)):Vector4.Zero;
        return Vector4.Lerp(Vector4.Lerp(At(ix,iy),At(ix+1,iy),x-ix),Vector4.Lerp(At(ix,iy+1),At(ix+1,iy+1),x-ix),y-iy);
    }
    private static SKColor Unpremul(Vector4 value)
    {
        var alpha=Math.Clamp(value.W,0,255);
        byte Channel(float v)=>(byte)Math.Clamp((int)MathF.Round(v*255/alpha),0,255);
        return alpha<.5f?SKColors.Transparent:new(Channel(value.X),Channel(value.Y),Channel(value.Z),(byte)MathF.Round(alpha));
    }
}
