using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class CompletionDraft
{
    internal static void Run(string repository)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v2");
        var output=Path.Combine(repository,"previews","front-hair-v2");Directory.CreateDirectory(output);
        var parts=new[]{("face-complete","FACE / FOREHEAD / TEMPLES"),("front-left-complete","VIEWER LEFT / COMPLETED"),
            ("front-center-complete","CENTER / COMPLETED"),("front-right-complete","VIEWER RIGHT / COMPLETED"),
            ("hair-foundation-complete","CROWN / BACKING / SOLID FILL")};
        using var board=Assets.Empty(936,728);using var canvas=new SKCanvas(board);canvas.Clear(new SKColor(32,32,34));
        using var label=new SKPaint{Color=new SKColor(232,232,236),IsAntialias=true};
        using var font=new SKFont(SKTypeface.FromFamilyName("Segoe UI"),13);
        canvas.DrawText("FRONT HAIR COMPLETION SOURCES / NOT YET REGISTERED OR SKINNED",18,24,SKTextAlign.Left,font,label);
        var records=new List<object>();
        for(var i=0;i<6;i++)
        {
            var original=i==5;var name=original?"CURRENT HAIR / REFERENCE":parts[i].Item2;
            var file=original?Path.Combine(repository,"plugins","claude-in-codex","assets","motion","layers","hair.png"):
                Path.Combine(root,"generated",parts[i].Item1+".png");
            using var bitmap=Assets.Load(file);var bounds=Bounds(bitmap);var x=16+i%3*308;var y=44+i/3*336;
            using var tile=new SKPaint();
            for(var yy=0;yy<288;yy+=12)for(var xx=0;xx<288;xx+=12)
            {
                tile.Color=(xx/12+yy/12)%2==0?new SKColor(55,55,59):new SKColor(44,44,48);
                canvas.DrawRect(x+xx,y+yy,12,12,tile);
            }
            var scale=Math.Min(270f/bounds.Width,270f/bounds.Height);
            var dest=new SKRect(x+(288-bounds.Width*scale)/2,y+(288-bounds.Height*scale)/2,
                x+(288+bounds.Width*scale)/2,y+(288+bounds.Height*scale)/2);
            canvas.DrawBitmap(bitmap,bounds,dest,new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.DrawText(name,x,y+310,SKTextAlign.Left,font,label);
            if(!original)
            {
                var holes=Holes(bitmap);var transparent=bitmap.GetPixel(0,0).Alpha==0&&bitmap.GetPixel(bitmap.Width-1,bitmap.Height-1).Alpha==0;
                if(!transparent||bounds.Width<80||bounds.Height<80)throw new InvalidDataException("Invalid completion source: "+file);
                records.Add(new{Part=parts[i].Item1,File=Path.GetRelativePath(root,file).Replace('\\','/'),bitmap.Width,bitmap.Height,
                    Bounds=new[]{bounds.Left,bounds.Top,bounds.Right,bounds.Bottom},TransparentCorners=transparent,
                    EnclosedTransparentPixelsAtAlpha128=holes,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))});
            }
        }
        Assets.Save(board,Path.Combine(output,"completion-sources.png"));
        File.WriteAllText(Path.Combine(root,"source-validation.json"),JsonSerializer.Serialize(new{Stage="completion source drafts",Parts=records,
            RegisteredToOriginal=false,VisibleContourPreservationVerified=false,Skinned=false,Installed=false,
            Limits="Independent generated components are complete silhouettes, but generation changed scale/contours. A contact sheet fits each part for inspection, not a neutral assembled pose. No claim of seam-free deformation or unchanged visible pixels."},Assets.Json));
    }
    private static SKRectI Bounds(SKBitmap b)
    {
        var l=b.Width;var t=b.Height;var r=0;var bottom=0;
        for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(b.GetPixel(x,y).Alpha>=128)
        {l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);bottom=Math.Max(bottom,y+1);}
        return new(l,t,r,bottom);
    }
    private static int Holes(SKBitmap b)
    {
        var n=b.Width*b.Height;var empty=new bool[n];var seen=new bool[n];var queue=new Queue<int>();
        for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)empty[y*b.Width+x]=b.GetPixel(x,y).Alpha<128;
        void Add(int p){if(empty[p]&&!seen[p]){seen[p]=true;queue.Enqueue(p);}}
        for(var x=0;x<b.Width;x++){Add(x);Add((b.Height-1)*b.Width+x);}
        for(var y=0;y<b.Height;y++){Add(y*b.Width);Add(y*b.Width+b.Width-1);}
        while(queue.TryDequeue(out var p))
        {var x=p%b.Width;var y=p/b.Width;if(x>0)Add(p-1);if(x<b.Width-1)Add(p+1);if(y>0)Add(p-b.Width);if(y<b.Height-1)Add(p+b.Width);}
        return Enumerable.Range(0,n).Count(i=>empty[i]&&!seen[i]);
    }
}
