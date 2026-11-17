using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class ChinNeckRepair
{
    internal static bool ChinPatch(int x,int y)=>x>=550&&x<701&&y>=1020&&y<1080;
    private static float Bottom(SKBitmap image,int x)
    {
        var y=image.Height-2;
        while(y>0&&image.GetPixel(x,y).Alpha<128)y--;
        var a=image.GetPixel(x,y).Alpha;var b=image.GetPixel(x,y+1).Alpha;
        return y+(a-127.5f)/Math.Max(1,a-b);
    }
    internal static void SmoothChin(SKBitmap face)
    {
        using var source=face.Copy();using var canvas=new SKCanvas(face);
        using var copy=new SKPaint{BlendMode=SKBlendMode.Src};
        var l=Bottom(source,550);var r=Bottom(source,700);
        var ml=(l-Bottom(source,530))/20;var mr=(Bottom(source,720)-r)/20;
        // Warp only the shallow central dent; retain the original wide jaw and its existing ink.
        for(var x=550;x<=700;x++)
        {
            var t=(x-550)/150f;var t2=t*t;var t3=t2*t;
            var target=(2*t3-3*t2+1)*l+(t3-2*t2+t)*ml*150+(-2*t3+3*t2)*r+(t3-t2)*mr*150;
            var old=Bottom(source,x);var delta=target-old;
            canvas.DrawBitmap(source,new SKRect(x,1020,x+1,old+4),new SKRect(x,1020,x+1,old+4+delta),
                new SKSamplingOptions(SKFilterMode.Linear),copy);
        }
    }
    internal static void CompleteNeck(SKBitmap neck)
    {
        // This is exposed skin above the flower, not a divided collar. Keep the outer frills intact.
        using var canvas=new SKCanvas(neck);using var path=new SKPathBuilder();
        path.MoveTo(568,1004);path.LineTo(684,1004);
        path.CubicTo(681,1038,669,1060,663,1080);path.LineTo(589,1080);
        path.CubicTo(583,1060,571,1038,568,1004);path.Close();using var shape=path.Detach();
        using var shader=SKShader.CreateLinearGradient(new SKPoint(568,0),new SKPoint(684,0),
            [new SKColor(247,225,214),new SKColor(251,232,222),new SKColor(247,224,213)],null,SKShaderTileMode.Clamp);
        using var skin=new SKPaint{IsAntialias=true,Shader=shader};canvas.DrawPath(shape,skin);
    }
    internal static object Validate(string repository,SKBitmap face,SKBitmap original,SKBitmap neck)
    {
        var outside=0;var changed=0;
        for(var y=950;y<1254;y++)for(var x=0;x<1254;x++)if(face.GetPixel(x,y)!=original.GetPixel(x,y))
        {if(ChinPatch(x,y))changed++;else outside++;}
        if(outside!=0||changed==0)throw new InvalidDataException("Jaw changes must be confined to the shallow central dent patch.");
        var bottom=Enumerable.Range(550,151).Select(x=>Bottom(face,x)).ToArray();
        var peak=Array.IndexOf(bottom,bottom.Max());
        var inward=Enumerable.Range(1,bottom.Length-2).Max(i=>Math.Max(0,Math.Min(bottom[i-1],bottom[i+1])-bottom[i]));
        if(inward>.4f||bottom.Max()>1071||bottom[75]<bottom[20])throw new InvalidDataException("Central jaw must remain flat-round without an inward notch.");
        var seamPixels=0;
        for(var y=1054;y<1080;y++)for(var x=604;x<=644;x++)
        {var c=neck.GetPixel(x,y);if(c.Alpha!=255||c.Red<220||c.Green<170)seamPixels++;}
        if(seamPixels!=0)throw new InvalidDataException("Exposed neck contains a dark center seam or missing skin.");
        using var oldNeck=Assets.Load(Path.Combine(repository,"assets","claude-motion-v1","layers","neck.png"));
        var neckOutside=0;
        for(var y=0;y<1254;y++)for(var x=0;x<1254;x++)
            if((x<567||x>684||y<1003||y>=1080)&&neck.GetPixel(x,y)!=oldNeck.GetPixel(x,y))neckOutside++;
        if(neckOutside!=0)throw new InvalidDataException("Neck changes escaped the small exposed-skin patch.");
        var report=new{Success=true,JawPatch=new[]{550,1020,701,1080},JawPixelsChanged=changed,ProtectedJawPixelsChanged=outside,
            MaximumInwardNotch=inward,MaximumJawY=bottom.Max(),PeakIndex=peak,NeckCenterSeamPixels=seamPixels,ProtectedNeckPixelsChanged=neckOutside,
            Scope="Only the central chin dent and exposed neck skin are repaired; jaw outside the patch and flower/clothes/paws are protected."};
        File.WriteAllText(Path.Combine(repository,"assets","claude-motion-v2","chin-neck-report.json"),JsonSerializer.Serialize(report,Assets.Json));
        return report;
    }
    internal static void Preview(string repository)
    {
        var output=Path.Combine(repository,"artifacts","chin-neck-fix");
        using var old=new Renderer(Path.Combine(output,"before"));
        using var current=new Renderer(Path.Combine(repository,"assets","claude-motion-v2"));
        using var sheet=Assets.Empty(1120,690);using var canvas=new SKCanvas(sheet);canvas.Clear(new SKColor(32,32,34));
        using var label=new SKPaint{Color=SKColors.White,IsAntialias=true};using var font=new SKFont(SKTypeface.FromFamilyName("Segoe UI"),20);
        foreach(var (pose,index) in new[]{new Pose(),new Pose{HeadY=-16},new Pose{HeadAngle=4}}.Select((p,i)=>(p,i)))
        foreach(var (renderer,column) in new[]{(old,0),(current,1)})
        {
            using var image=renderer.Render(pose,0,0,1120);
            canvas.DrawBitmap(image,new SKRect(320,825,800,1000),new SKRect(column*560,index*230+40,column*560+560,index*230+244),new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.DrawText($"{(column==0?"BEFORE":"AFTER")} / {(index==0?"REST":index==1?"HEAD UP":"TILT")}",column*560+18,index*230+26,SKTextAlign.Left,font,label);
        }
        Assets.Save(sheet,Path.Combine(output,"chin-neck-before-after.png"));
    }
}
