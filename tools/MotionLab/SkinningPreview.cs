using SkiaSharp;
using System.Text.Json;

namespace MotionLab;

internal static class SkinningPreview
{
    private static SKColor BoneColor(int bone)=>bone switch
    {
        0=>new(155,163,175),1=>new(87,183,224),12=>new(134,205,145),
        13 or 15 or 17=>new(255,144,105),14 or 16 or 18=>new(255,204,89),
        9=>new(89,201,184),10=>new(242,151,172),11=>new(197,139,201),_=>new(242,151,172)
    };
    private static SKColor WeightColor(Influence[] weights)
    {
        float r=0,g=0,b=0;foreach(var w in weights){var color=BoneColor(w.Bone);r+=color.Red*w.Weight;g+=color.Green*w.Weight;b+=color.Blue*w.Weight;}
        return new((byte)Math.Round(r),(byte)Math.Round(g),(byte)Math.Round(b));
    }
    internal static void Run(string repository)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v4");using var renderer=new Renderer(root);
        var entries=new (string Name,string Label,Mesh? Mesh)[]{("hair-cap","头顶发盖",null),
            ("hair-front-left","左前发",renderer.FrontMeshes[0]),("hair-front-center","中前发",renderer.FrontMeshes[1]),("hair-front-right","右前发",renderer.FrontMeshes[2]),
            ("rear-hair","后发底层",renderer.Meshes[4]),("hair-curtain","后发帘",renderer.RearCurtain),
            ("hair-backing","后发承托",renderer.FrontMeshes[3]),("accessory","花饰与缎带",renderer.Meshes[1]),
            ("face","脸部",null),("neck","脖子",renderer.Meshes[2]),("clothes-paws","衣服与手掌",renderer.Meshes[3])};
        using var sheet=Assets.Empty(1440,1250);using var canvas=new SKCanvas(sheet);canvas.Clear(new SKColor(32,32,34));
        using var typeface=SKTypeface.FromFamilyName("Microsoft YaHei");using var title=new SKFont(typeface,24);using var font=new SKFont(typeface,18);
        using var text=new SKPaint{Color=new SKColor(238,238,238),IsAntialias=true};
        canvas.DrawText("部件蒙皮",24,36,SKTextAlign.Left,title,text);
        var legends=new[]{(1,"头部"),(0,"身体"),(13,"发束中段"),(14,"发梢"),(12,"呼吸"),(11,"花饰"),(9,"缎带根部"),(10,"缎带末端")};
        foreach(var (entry,i) in legends.Select((p,i)=>(p,i)))
        {
            using var color=new SKPaint{Color=BoneColor(entry.Item1),IsAntialias=true};var x=24+i*176;
            canvas.DrawRect(x,58,13,13,color);canvas.DrawText(entry.Item2,x+22,72,SKTextAlign.Left,font,text);
        }
        var meshes=new List<object>();
        foreach(var (entry,i) in entries.Select((entry,i)=>(entry,i)))
        {
            var x=24+i%4*354;var y=104+i/4*370;
            using var mask=Assets.Load(Path.Combine(root,"layers",entry.Name+".png"));using var map=Assets.Empty();using var c=new SKCanvas(map);
            c.Clear(SKColors.Transparent);
            if(entry.Mesh is {} mesh)
            {
                if(mesh.Weights.Any(w=>Math.Abs(w.Sum(i=>i.Weight)-1)>.0001f))throw new InvalidDataException(entry.Name+": non-normalized weights");
                var points=mesh.Rest.Select(p=>new SKPoint(p.X,p.Y)).ToArray();var colors=mesh.Weights.Select(WeightColor).ToArray();
                using var vertices=SKVertices.CreateCopy(SKVertexMode.Triangles,points,null,colors,mesh.Indices);
                using var paint=new SKPaint{IsAntialias=true,Color=SKColors.White};c.DrawVertices(vertices,SKBlendMode.Modulate,paint);
                using var line=new SKPaint{Color=new SKColor(255,255,255,135),StrokeWidth=2,IsAntialias=true};
                for(var j=0;j<mesh.Indices.Length;j+=3)
                {
                    var a=points[mesh.Indices[j]];var b=points[mesh.Indices[j+1]];var d=points[mesh.Indices[j+2]];
                    c.DrawLine(a,b,line);c.DrawLine(b,d,line);c.DrawLine(d,a,line);
                }
                meshes.Add(new{entry.Name,Vertices=mesh.Rest.Length,Triangles=mesh.Indices.Length/3});
            }
            else {using var fill=new SKPaint{Color=BoneColor(1)};c.DrawRect(0,0,1254,1254,fill);}
            using(var alpha=new SKPaint{BlendMode=SKBlendMode.DstIn})c.DrawBitmap(mask,0,0,new SKSamplingOptions(SKFilterMode.Nearest),alpha);
            var left=1254;var top=1254;var right=0;var bottom=0;
            for(var py=0;py<1254;py++)for(var px=0;px<1254;px++)if(mask.GetPixel(px,py).Alpha>0)
            {left=Math.Min(left,px);top=Math.Min(top,py);right=Math.Max(right,px+1);bottom=Math.Max(bottom,py+1);}
            var scale=Math.Min(300f/(right-left),300f/(bottom-top));var width=(right-left)*scale;var height=(bottom-top)*scale;
            canvas.DrawBitmap(map,new SKRect(left,top,right,bottom),new SKRect(x+(320-width)/2,y+(320-height)/2,x+(320+width)/2,y+(320+height)/2),new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.DrawText(entry.Label,x+4,y+346,SKTextAlign.Left,font,text);
        }
        // Eye/brow/mouth features are head-bound parameterized parts, not triangle-skinned meshes.
        using(var features=Assets.Empty())using(var c=new SKCanvas(features))
        {
            using var iris=Assets.Load(Path.Combine(root,"layers","eye.png"));
            foreach(var x in new[]{388,728})
            {c.Save();c.ClipRect(new SKRect(x-20,789,x+175,900));c.DrawBitmap(iris,x,655,new SKSamplingOptions(SKFilterMode.Nearest));c.Restore();}
            foreach(var name in new[]{"brow-left","brow-right","mouth-neutral"})
            {using var image=Assets.Load(Path.Combine(root,"layers",name+".png"));c.DrawBitmap(image,0,0,new SKSamplingOptions(SKFilterMode.Nearest));}
            using(var fill=new SKPaint{Color=BoneColor(1),BlendMode=SKBlendMode.SrcIn})c.DrawRect(0,0,1254,1254,fill);
            canvas.DrawBitmap(features,new SKRect(1086,844,1406,1164),new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.DrawText("眼睛、眉毛与嘴型",1090,1190,SKTextAlign.Left,font,text);
        }
        Assets.Save(sheet,Path.Combine(repository,"previews","material-skinning-v4.png"));
        File.WriteAllText(Path.Combine(repository,"artifacts","expression-preview","skinning-report.json"),JsonSerializer.Serialize(new{Success=true,Meshes=meshes,
            Scope="Actual runtime mesh topology and normalized weights clipped to current V4 part alpha. Head-bound rigid and parameterized parts have solid head color, not invented mesh topology."},Assets.Json));
        Console.WriteLine("Skinning diagram: actual runtime weights and topology rendered.");
    }
}
