using SkiaSharp;
using System.Text.Json;

namespace MotionLab;

internal static class Assets
{
    internal const int Size=1254;
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static SKBitmap Load(string path)=>SKBitmap.Decode(path)??throw new InvalidDataException(path);
    internal static void Save(SKBitmap bitmap,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);data.SaveTo(file);
    }
    internal static SKBitmap Empty(int width=Size,int height=Size)=>new(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));
    private static SKColor Clean(SKColor c)
    {
        if(c.Alpha<24)return SKColors.Transparent;
        return c.WithAlpha(c.Alpha>235?(byte)255:c.Alpha);
    }
    internal static void Prepare(string repository,string root)
    {
        var generated=Path.Combine(root,"generated");var output=Path.Combine(root,"layers");Directory.CreateDirectory(output);
        using var original=Load(Path.Combine(repository,"plugins/claude-in-codex/assets/claude-v2/base.png"));
        using var face=Load(Path.Combine(generated,"face-underlay.png"));
        using var hair=Load(Path.Combine(generated,"hair-complete.png"));
        using var rearHair=Load(Path.Combine(generated,"rear-hair-underlay.png"));
        using var accessory=Load(Path.Combine(generated,"accessory.png"));
        using var body=Load(Path.Combine(generated,"body-underlay.png"));
        using var atlas=Load(Path.Combine(generated,"mouths.png"));
        if(new[]{face,hair,rearHair,accessory,body}.Any(b=>b.Width!=Size||b.Height!=Size))throw new InvalidDataException("Aligned 1254-square parts required.");
        using var neck=Empty();using var clothes=Empty();using var eyes=Load(Path.Combine(repository,"plugins/claude-in-codex/assets/claude-v2/eye.png"));
        var white=new bool[Size*Size];
        for(var y=980;y<Size;y++)for(var x=0;x<Size;x++)
        {
            var c=body.GetPixel(x,y);white[y*Size+x]=c.Alpha>200&&c.Red>160&&c.Green>130&&c.Blue>120&&c.Red-c.Green<65;
        }
        for(var y=0;y<Size;y++)for(var x=0;x<Size;x++)
        {
            var hc=hair.GetPixel(x,y);var fc=face.GetPixel(x,y);var ac=accessory.GetPixel(x,y);
            // Keep completed layers internally coherent; color-based source backfill can reintroduce props.
            // The wig's low U-shaped rim belongs behind the neck, not across the collar foreground.
            static float Smooth(float v){v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
            var cut=Smooth((y-990)/30f)*Smooth((x-330)/40f)*(1-Smooth((x-900)/40f));
            hair.SetPixel(x,y,Clean(hc).WithAlpha((byte)Math.Round(Clean(hc).Alpha*(1-cut))));
            rearHair.SetPixel(x,y,Clean(rearHair.GetPixel(x,y)));accessory.SetPixel(x,y,Clean(ac));
            face.SetPixel(x,y,Clean(fc));
            if(y<980)continue;
            var bc=body.GetPixel(x,y);var nearWhite=false;
            if(bc.Red<145&&bc.Alpha>200)
                for(var dy=-10;dy<=10&&!nearWhite;dy+=2)for(var dx=-10;dx<=10;dx+=2)
                {
                    var nx=x+dx;var ny=y+dy;if(nx>=0&&nx<Size&&ny>=0&&ny<Size&&white[ny*Size+nx]){nearWhite=true;break;}
                }
            var flower=(x-623)*(x-623)/(82d*82)+(y-1140)*(y-1140)/(75d*75)<1;
            var tie=x>540&&x<700&&y>1160;
            if(!white[y*Size+x]&&!nearWhite&&!flower&&!tie)continue;
            (y<1080?neck:clothes).SetPixel(x,y,Clean(bc));
        }
        Save(face,Path.Combine(output,"face.png"));Save(hair,Path.Combine(output,"hair.png"));Save(rearHair,Path.Combine(output,"rear-hair.png"));Save(accessory,Path.Combine(output,"accessory.png"));
        Save(neck,Path.Combine(output,"neck.png"));Save(clothes,Path.Combine(output,"clothes-paws.png"));Save(eyes,Path.Combine(output,"eye.png"));
        foreach(var (name,box) in new[]{("brow-left",new SKRectI(349,643,529,678)),("brow-right",new SKRectI(710,640,897,680)),("mouth-neutral",new SKRectI(594,947,672,980))})
        {
            using var part=Empty();
            for(var y=box.Top;y<box.Bottom;y++)for(var x=box.Left;x<box.Right;x++)
            {
                var c=original.GetPixel(x,y);
                var line=name=="mouth-neutral"?c.Red<205&&c.Green<140:Math.Abs(y-(name=="brow-left"?650+(x-351)*.105:670-(x-714)*.115))<5;
                if(line&&c.Red<180&&c.Green<125)part.SetPixel(x,y,Clean(c));
            }
            Save(part,Path.Combine(output,name+".png"));
        }
        string[] names=["smile","happy","surprise","a","i","u"];
        var mouths=new List<object>();
        for(var tile=0;tile<6;tile++)
        {
            var cellW=atlas.Width/3;var cellH=atlas.Height/2;var ox=tile%3*cellW;var oy=tile/3*cellH;
            var minX=cellW;var minY=cellH;var maxX=0;var maxY=0;
            for(var y=0;y<cellH;y++)for(var x=0;x<cellW;x++)
                if(atlas.GetPixel(ox+x,oy+y).Alpha>200){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
            if(maxX-minX<15||maxY-minY<3)throw new InvalidDataException($"Empty mouth tile: {names[tile]}");
            using var cut=Empty(maxX-minX+4,maxY-minY+4);
            for(var y=minY-2;y<maxY+2;y++)for(var x=minX-2;x<maxX+2;x++)
            {
                var c=atlas.GetPixel(ox+x,oy+y);
                // Reject the generator's low-alpha haze; keep a narrow antialiased solid silhouette.
                var alpha=(byte)Math.Clamp((c.Alpha-180)*255/60,0,255);
                cut.SetPixel(x-minX+2,y-minY+2,c.WithAlpha(alpha));
            }
            var file=$"mouth-{names[tile]}.png";Save(cut,Path.Combine(output,file));mouths.Add(new{Name=names[tile],File=file,Width=cut.Width,Height=cut.Height});
        }
        File.WriteAllText(Path.Combine(root,"parts.json"),JsonSerializer.Serialize(new{Canvas=new[]{Size,Size},Anchor=new[]{627,1237},
            Layers=new[]{"rear-hair","neck","face","eyes-and-lashes","brows","mouth","hair","accessory","clothes-paws"},Mouths=mouths,
            Protected=new[]{"original flat orange eyes","no sclera or lower lids","flower stays viewer right","paws remain rooted"},
            RuntimeCandidate="layers plus rig.json and motions.json only; generated sources and preview dependencies excluded"},Json));
    }
}
