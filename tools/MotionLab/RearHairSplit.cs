using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class RearHairSplit
{
    private static readonly (int X,int Y)[] Left=[(0,960),(100,960),(128,918),(160,942),(185,957),(205,958),(220,954),(240,970),(260,984),(280,995),(300,1004),(330,1009),(370,1009),(410,1008),(500,1040),(1253,1040)];
    private static readonly (int X,int Y)[] Right=[(0,1040),(810,1040),(840,940),(850,940),(862,1018),(870,1008),(890,980),(910,960),(930,936),(950,925),(970,905),(990,885),(1010,845),(1030,800),(1050,745),(1100,700),(1253,700)];
    private static int Guide((int X,int Y)[] points,int x)
    {
        for(var i=1;i<points.Length;i++)if(x<=points[i].X)
        {var a=points[i-1];var b=points[i];return (int)Math.Round(a.Y+(b.Y-a.Y)*(x-a.X)/(double)(b.X-a.X));}
        return points[^1].Y;
    }
    private static int Boundary(SKBitmap image,(int X,int Y)[] guide,int x)
    {
        var target=Guide(guide,x);var best=target;var distance=13;
        for(var y=target-12;y<=target+12;y++)
        {
            var ink=image.GetPixel(x,y);var below=image.GetPixel(x,y+1);
            if(ink.Alpha>128&&ink.Red<130&&(below.Alpha<24||below.Red>160)&&Math.Abs(y-target)<distance)
            {best=y+2;distance=Math.Abs(y-target);}
        }
        return best;
    }
    internal static void Build(string repository)
    {
        var source=Path.Combine(repository,"assets","claude-motion-v2","layers");
        var root=Path.Combine(repository,"assets","claude-motion-v3");var output=Path.Combine(root,"layers");Directory.CreateDirectory(output);
        foreach(var file in Directory.EnumerateFiles(source,"*.png"))File.Copy(file,Path.Combine(output,Path.GetFileName(file)),true);
        using var curtain=Assets.Empty();using var backing=Assets.Load(Path.Combine(source,"hair-backing.png"));using var face=Assets.Load(Path.Combine(source,"face.png"));
        var moved=new List<object>();
        foreach(var (name,guide) in new[]{("hair-front-left",Left),("hair-front-right",Right)})
        {
            using var original=Assets.Load(Path.Combine(source,name+".png"));using var front=original.Copy();
            var inside=FrontHairBuild.InteriorDistance(original);var count=0;var overlap=0;
            for(var x=0;x<1254;x++)
            {
                var cut=Boundary(original,guide,x);
                // The pointed locks overlapping the cheek are foreground; do not transfer their antialiased tips behind the face.
                for(var y=cut;y<1080;y++)if(original.GetPixel(x,y).Alpha>0&&face.GetPixel(x,y).Alpha>0)cut=y+1;
                for(var y=0;y<1254;y++)
                {
                    var pixel=original.GetPixel(x,y);
                    if(y>=cut)
                    {
                        if(pixel.Alpha>0){curtain.SetPixel(x,y,pixel);count++;}
                        front.SetPixel(x,y,SKColors.Transparent);
                    }
                    else if(y>=cut-48&&inside[y*1254+x]>=8)
                    {
                        var fill=backing.GetPixel(x,y);
                        if(fill.Alpha<255)fill=original.GetPixel(x,Math.Min(cut+8,1253));
                        if(fill.Alpha==255){curtain.SetPixel(x,y,fill);overlap++;}
                    }
                }
            }
            Assets.Save(front,Path.Combine(output,name+".png"));moved.Add(new{Name=name,MovedPixels=count,HiddenOverlapPixels=overlap});
        }
        Assets.Save(curtain,Path.Combine(output,"hair-curtain.png"));
        using var oldFlat=Flat(source,false);using var newFlat=Flat(output,true);var different=0;var max=0;
        for(var y=0;y<1254;y++)for(var x=0;x<1254;x++)
        {
            var a=oldFlat.GetPixel(x,y);var b=newFlat.GetPixel(x,y);
            var d=new[]{Math.Abs(a.Red-b.Red),Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue),Math.Abs(a.Alpha-b.Alpha)}.Max();
            if(d>1)different++;max=Math.Max(max,d);
        }
        var artifact=Path.Combine(repository,"artifacts","rear-hair-flow");
        Assets.Save(oldFlat,Path.Combine(artifact,"rest-before.png"));Assets.Save(newFlat,Path.Combine(artifact,"rest-after.png"));
        FrontHairAudit.CheckComponents(root);
        File.WriteAllText(Path.Combine(root,"split-report.json"),JsonSerializer.Serialize(new{Success=different==0,NativeRestDifferentPixels=different,MaximumChannelDifference=max,Moved=moved,
            Scope="Curved anatomical boundary; old visible pixels moved to an independent rear curtain, with hidden overlap completed from existing registered material. Face, neck, eyes, center bang, flower and paws unchanged."},Assets.Json));
        Console.WriteLine($"Rear curtain split: {different} rest-pose differences, max channel {max}");
        if(different!=0)throw new InvalidDataException("Rear ownership transfer changed the protected neutral appearance.");
    }
    private static SKBitmap Flat(string layers,bool split)
    {
        var result=Assets.Empty();using var canvas=new SKCanvas(result);
        var order=new List<string>{"rear-hair","neck","hair-backing"};if(split)order.Add("hair-curtain");
        order.AddRange(["face","hair-front-left","hair-front-right","hair-front-center","hair-cap","accessory","clothes-paws"]);
        foreach(var name in order){using var image=Assets.Load(Path.Combine(layers,name+".png"));canvas.DrawBitmap(image,0,0,new SKSamplingOptions(SKFilterMode.Nearest));}
        return result;
    }
}
