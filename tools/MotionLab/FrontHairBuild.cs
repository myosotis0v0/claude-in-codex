using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class FrontHairBuild
{
    private const int N=1254;
    private const int JawStart=950;
    private static readonly string[] Names=["hair-cap","hair-front-left","hair-front-center","hair-front-right"];
    private static readonly (float Y,float X)[] Left=[(300,527),(350,508),(400,491),(450,479),(500,473),(550,473),(600,480),(650,493),(700,516),(750,550),(800,591),(850,620)];
    private static readonly (float Y,float X)[] Right=[(300,725),(350,745),(400,758),(450,770),(500,775),(550,773),(600,765),(650,752),(700,737),(750,716),(800,716),(850,730)];
    private static float Sample((float Y,float X)[] table,int y)
    {
        for(var i=1;i<table.Length;i++)if(y<=table[i].Y)
            return table[i-1].X+(table[i].X-table[i-1].X)*(y-table[i-1].Y)/(table[i].Y-table[i-1].Y);
        return table[^1].X;
    }
    private static int[] Ownership(SKBitmap hair)
    {
        var owner=new int[N*N];
        for(var y=300;y<N;y++)
        {
            var l=Sample(Left,y);var r=Sample(Right,y);
            // Below the attached root, transparent gaps provide an unambiguous component boundary.
            if(y>=530&&y<855)
            {
                var xs=Enumerable.Range(Math.Max(0,(int)l-36),Math.Min(N-1,(int)r+36)-Math.Max(0,(int)l-36)+1)
                    .Where(x=>hair.GetPixel(x,y).Alpha>0).ToArray();
                if(xs.Length>0)
                {
                    if(y<700)
                    {
                        var runs=new List<(int Start,int End)>();var start=xs[0];var end=start;
                        foreach(var x in xs.Skip(1)){if(x>end+1){runs.Add((start,end));start=x;}end=x;}runs.Add((start,end));
                        var middle=runs.MaxBy(run=>run.End-run.Start);l=middle.Start;r=middle.End;
                    }
                    else {l=xs[0];r=xs[^1];}
                }
            }
            for(var x=0;x<N;x++)owner[y*N+x]=y<855&&x>=l&&x<=r?2:x<627?1:3;
        }
        return owner;
    }
    private static SKRectI Bounds(SKBitmap b)
    {
        var l=N;var t=N;var r=0;var bottom=0;
        for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(b.GetPixel(x,y).Alpha>128)
        {l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);bottom=Math.Max(bottom,y+1);}
        return new(l,t,r,bottom);
    }
    private static SKBitmap Register(SKBitmap input,SKRect destination)
    {
        var result=Assets.Empty();using var c=new SKCanvas(result);
        c.DrawBitmap(input,Bounds(input),destination,new SKSamplingOptions(SKCubicResampler.Mitchell));return result;
    }
    private static SKBitmap RegisterRoot(SKBitmap image,SKBitmap visible)
    {
        static (int L,int R) Row(SKBitmap b,int y)
        {
            var l=N;var r=-1;for(var x=0;x<N;x++)if(b.GetPixel(x,y).Alpha>=128){l=Math.Min(l,x);r=x;}
            return(l,r);
        }
        var oldJoin=Row(image,300);var newJoin=Row(visible,300);var top=Bounds(image).Top;
        var result=Assets.Empty();using var canvas=new SKCanvas(result);
        for(var y=top;y<300;y++)
        {
            var old=Row(image,y);if(old.R<=old.L)continue;
            var t=Math.Clamp((y-top)/(float)(300-top),0,1);t=t*t*(3-2*t);
            var l=old.L+(newJoin.L-oldJoin.L)*t;var r=old.R+1+(newJoin.R-oldJoin.R)*t;
            canvas.DrawBitmap(image,new SKRect(old.L,y,old.R+1,y+1),new SKRect(l,y,r,y+1),new SKSamplingOptions(SKFilterMode.Linear));
        }
        return result;
    }
    private static SKBitmap JoinOriginalJaw(SKBitmap upper,SKBitmap original)
    {
        static (float L,float R) Row(SKBitmap b,int y)
        {
            var l=N;var r=-1;for(var x=0;x<N;x++)if(b.GetPixel(x,y).Alpha>=128){l=Math.Min(l,x);r=x;}
            return(l,r+1);
        }
        var a=Row(upper,880);var a0=Row(upper,872);var b=Row(original,JawStart);var b1=Row(original,JawStart+8);
        static float Hermite(float a,float b,float ma,float mb,float t)=>
            (2*t*t*t-3*t*t+1)*a+(t*t*t-2*t*t+t)*ma*(JawStart-880)+(-2*t*t*t+3*t*t)*b+(t*t*t-t*t)*mb*(JawStart-880);
        var result=Assets.Empty();using var canvas=new SKCanvas(result);
        using var copy=new SKPaint{BlendMode=SKBlendMode.Src};
        canvas.DrawBitmap(upper,new SKRect(0,0,N,880),new SKRect(0,0,N,880),new SKSamplingOptions(SKFilterMode.Nearest),copy);
        for(var y=880;y<JawStart;y++)
        {
            var t=(y-880)/(float)(JawStart-880);var l=Hermite(a.L,b.L,(a.L-a0.L)/8,(b1.L-b.L)/8,t);var r=Hermite(a.R,b.R,(a.R-a0.R)/8,(b1.R-b.R)/8,t);
            var srcUpper=Row(upper,y);var srcOld=Row(original,y);var blend=Math.Clamp((y-(JawStart-20))/20f,0,1);blend=blend*blend*(3-2*blend);
            var alpha=(byte)Math.Round(blend*255);
            using var first=new SKPaint{BlendMode=SKBlendMode.Src,Color=SKColors.White.WithAlpha((byte)(255-alpha))};
            using var second=new SKPaint{BlendMode=SKBlendMode.Plus,Color=SKColors.White.WithAlpha(alpha)};
            canvas.DrawBitmap(upper,new SKRect(srcUpper.L-2,y,srcUpper.R+2,y+1),new SKRect(l-2,y,r+2,y+1),new SKSamplingOptions(SKFilterMode.Linear),first);
            if(alpha>0)canvas.DrawBitmap(original,new SKRect(srcOld.L-2,y,srcOld.R+2,y+1),new SKRect(l-2,y,r+2,y+1),new SKSamplingOptions(SKFilterMode.Linear),second);
        }
        canvas.DrawBitmap(original,new SKRect(0,JawStart,N,N),new SKRect(0,JawStart,N,N),new SKSamplingOptions(SKFilterMode.Nearest),copy);
        return result;
    }
    internal static int[] InteriorDistance(SKBitmap image,byte threshold=255)
    {
        var distance=new int[N*N];Array.Fill(distance,10000);var queue=new Queue<int>();
        for(var y=0;y<N;y++)for(var x=0;x<N;x++)if(image.GetPixel(x,y).Alpha<threshold||x==0||y==0||x==N-1||y==N-1)
        {var p=y*N+x;distance[p]=0;queue.Enqueue(p);}
        while(queue.TryDequeue(out var p))
        {
            var x=p%N;var y=p/N;
            void Visit(int next){if(distance[next]>distance[p]+1){distance[next]=distance[p]+1;queue.Enqueue(next);}}
            if(x>0)Visit(p-1);if(x<N-1)Visit(p+1);if(y>0)Visit(p-N);if(y<N-1)Visit(p+N);
        }
        return distance;
    }
    internal static void Build(string repository)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v2");var generated=Path.Combine(root,"generated");
        var output=Path.Combine(root,"layers");Directory.CreateDirectory(output);
        var source=Path.Combine(repository,"assets","claude-motion-v1","layers");
        foreach(var file in Directory.EnumerateFiles(source,"*.png"))File.Copy(file,Path.Combine(output,Path.GetFileName(file)),true);
        using var hair=Assets.Load(Path.Combine(source,"hair.png"));using var oldFace=Assets.Load(Path.Combine(source,"face.png"));
        var owner=Ownership(hair);
        var interior=InteriorDistance(hair);var faceInterior=InteriorDistance(oldFace);
        var visible=Names.Select(_=>Assets.Empty()).ToArray();var complete=Names.Select(_=>Assets.Empty()).ToArray();
        using var genLeft=Assets.Load(Path.Combine(generated,"front-left-complete.png"));
        using var genCenter=Assets.Load(Path.Combine(generated,"front-center-complete.png"));
        using var genRight=Assets.Load(Path.Combine(generated,"front-right-complete.png"));
        using var genFace=Assets.Load(Path.Combine(generated,"face-jaw-preserved.png"));
        using var genBacking=Assets.Load(Path.Combine(generated,"hair-foundation-complete.png"));
        using var left=Register(genLeft,new SKRect(8,215,554,1220));
        using var center=Register(genCenter,new SKRect(470,190,779,851));
        using var right=Register(genRight,new SKRect(710,205,1150,1220));
        using var face=JoinOriginalJaw(genFace,oldFace);
        var newFaceInterior=InteriorDistance(face,128);
        using var backing=Register(genBacking,new SKRect(8,39,1230,1218));
        using var faceComplete=Assets.Empty();using var backComplete=Assets.Empty();using var segmentation=Assets.Empty();
        var fills=new int[4];var faceFill=0;
        for(var y=0;y<N;y++)for(var x=0;x<N;x++)
        {
            var p=y*N+x;var original=hair.GetPixel(x,y);var f=oldFace.GetPixel(x,y);var part=owner[p];
            visible[part].SetPixel(x,y,original);
            // Only later, fully opaque original layers may occlude newly painted overlap pixels at rest.
            var canCover=interior[p]>=8;
            for(var id=1;id<=3;id++)
            {
                var g=(id==1?left:id==2?center:right).GetPixel(x,y);
                var later=part==0||(id!=2&&part==2);
                if(canCover&&later&&g.Alpha>0){complete[id].SetPixel(x,y,g);fills[id]++;}
            }
            // A complete egg-shaped head must not inherit the hair's occlusion matte or the old cheek notches.
            var added=face.GetPixel(x,y);
            if(y>=JawStart)added=f;
            else if(newFaceInterior[p]>=2||added.Alpha>235)added=added.WithAlpha(255);
            else if(added.Alpha<24)added=SKColors.Transparent;
            if(added.Alpha>0)
            {
                var t=y>=JawStart?1:Math.Clamp((faceInterior[p]-32)/24f,0,1);
                if(f.Alpha==255&&added.Alpha==255&&f.Red>220&&f.Green>170)
                    added=new SKColor((byte)Math.Round(added.Red+(f.Red-added.Red)*t),(byte)Math.Round(added.Green+(f.Green-added.Green)*t),(byte)Math.Round(added.Blue+(f.Blue-added.Blue)*t),255);
                faceComplete.SetPixel(x,y,added);if(f.Alpha==0)faceFill++;
            }
            // Solid support is restricted to already opaque occluders; it cannot inflate the outer alpha edge.
            if((interior[p]>=8||newFaceInterior[p]>=8)&&backing.GetPixel(x,y).Alpha>0)
                backComplete.SetPixel(x,y,backing.GetPixel(x,y));
            var color=part switch{1=>new SKColor(247,97,92),2=>new SKColor(80,198,210),3=>new SKColor(161,210,98),_=>new SKColor(224,184,86)};
            segmentation.SetPixel(x,y,color.WithAlpha(original.Alpha));
        }
        // Register each completed rounded root to the exact width of its original visible lock at the hidden join.
        for(var id=1;id<=3;id++)
        {
            using var rootPart=RegisterRoot(id==1?left:id==2?center:right,visible[id]);
            for(var y=0;y<300;y++)for(var x=0;x<N;x++)
            {
                var c=rootPart.GetPixel(x,y);complete[id].SetPixel(x,y,interior[y*N+x]>=8?c:SKColors.Transparent);
            }
        }
        // The root is pinned, so a source-colored underlap removes the resampling seam without revealing foreign strokes.
        for(var y=275;y<300;y++)for(var x=0;x<N;x++)if(interior[y*N+x]>=8)
        {
            var part=x<Sample(Left,300)?1:x>Sample(Right,300)?3:2;
            var c=complete[part].GetPixel(x,y);
            if(c.Alpha>0)complete[part].SetPixel(x,y,hair.GetPixel(x,y).WithAlpha(c.Alpha));
        }
        // Attached locks need opaque material behind their shared center edge, not complementary alpha masks.
        for(var y=300;y<570;y++)
        {
            var row=Enumerable.Range(0,N).Where(x=>owner[y*N+x]==2&&hair.GetPixel(x,y).Alpha>0).ToArray();
            if(row.Length==0)continue;var l=row[0];var r=row[^1];
            foreach(var side in new[]{1,3})
            {
                var neighbor=side==1?l-1:r+1;if(neighbor<0||neighbor>=N||hair.GetPixel(neighbor,y).Alpha!=255)continue;
                var color=hair.GetPixel(Math.Clamp(side==1?l-14:r+14,0,N-1),y);
                if(color.Alpha<255)continue;
                for(var d=0;d<18;d++)
                {
                    var x=side==1?l+d:r-d;
                    if(owner[y*N+x]==2&&hair.GetPixel(x,y).Alpha==255)complete[side].SetPixel(x,y,color);
                }
            }
        }
        var checks=new List<string>();
        void Check(bool pass,string description){if(!pass)throw new InvalidDataException(description);checks.Add(description);}
        for(var id=0;id<4;id++)
        {
            using(var c=new SKCanvas(complete[id]))c.DrawBitmap(visible[id],0,0,new SKSamplingOptions(SKFilterMode.Nearest));
            Assets.Save(visible[id],Path.Combine(root,"extracted",Names[id]+".png"));Assets.Save(complete[id],Path.Combine(output,Names[id]+".png"));
        }
        ChinNeckRepair.SmoothChin(faceComplete);
        using var neck=Assets.Load(Path.Combine(output,"neck.png"));ChinNeckRepair.CompleteNeck(neck);
        ChinNeckRepair.Validate(repository,faceComplete,oldFace,neck);
        Assets.Save(neck,Path.Combine(output,"neck.png"));
        Assets.Save(faceComplete,Path.Combine(output,"face.png"));Assets.Save(backComplete,Path.Combine(output,"hair-backing.png"));
        Assets.Save(segmentation,Path.Combine(repository,"artifacts","front-hair-v2","ownership.png"));
        // The same painter order is used by the rigged renderer: face, sides, center, then the fixed crown/root cap.
        using var reference=Assets.Empty();using(var c=new SKCanvas(reference))
        {c.DrawBitmap(faceComplete,0,0,new SKSamplingOptions(SKFilterMode.Nearest));c.DrawBitmap(hair,0,0,new SKSamplingOptions(SKFilterMode.Nearest));}
        using var joined=Assets.Empty();using(var c=new SKCanvas(joined))
        {
            c.DrawBitmap(backComplete,0,0,new SKSamplingOptions(SKFilterMode.Nearest));c.DrawBitmap(faceComplete,0,0,new SKSamplingOptions(SKFilterMode.Nearest));
            foreach(var id in new[]{1,3,2,0})c.DrawBitmap(complete[id],0,0,new SKSamplingOptions(SKFilterMode.Nearest));
        }
        var differences=0;var maxDifference=0;using var diff=Assets.Empty();
        for(var y=0;y<N;y++)for(var x=0;x<N;x++)
        {
            var a=reference.GetPixel(x,y);var b=joined.GetPixel(x,y);
            var d=new[]{Math.Abs(a.Red-b.Red),Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue),Math.Abs(a.Alpha-b.Alpha)}.Max();
            maxDifference=Math.Max(maxDifference,d);if(d>1){differences++;diff.SetPixel(x,y,new SKColor(255,40,100));}
        }
        Assets.Save(reference,Path.Combine(repository,"artifacts","front-hair-v2","reference-flat.png"));
        Assets.Save(joined,Path.Combine(repository,"artifacts","front-hair-v2","joined-flat.png"));
        Assets.Save(diff,Path.Combine(repository,"artifacts","front-hair-v2","difference-flat.png"));
        Check(fills.Skip(1).All(n=>n>100),"each front part has real completed overlap pixels, not extraction only");
        Check(faceFill>10000,"face has completed forehead/temple support behind original hair");
        var jawDifferences=0;
        for(var y=JawStart;y<N;y++)for(var x=0;x<N;x++)if(!ChinNeckRepair.ChinPatch(x,y)&&faceComplete.GetPixel(x,y)!=oldFace.GetPixel(x,y))jawDifferences++;
        Check(jawDifferences==0,$"original lower jaw at y>={JawStart} is preserved outside the explicitly repaired shallow central dent");
        FrontHairAudit.CheckComponents(root);
        Check(true,"completed front parts have no detached opaque foreign-fringe fragments");
        File.WriteAllText(Path.Combine(root,"registration-report.json"),JsonSerializer.Serialize(new{Canvas=N,Checks=checks,
            NeutralDifferentPixels=differences,NeutralMaximumChannelDifference=maxDifference,CompletedOverlapPixels=fills,CompletedFacePixels=faceFill,
            HairComparisonFace="same completed full head on both sides; cheek fold and shallow central chin dent are intentionally corrected",
            ProtectedOriginalJawFromY=JawStart,ProtectedJawDifferentPixels=jawDifferences,
            GatePassed=differences==0,Order=new[]{"hair-backing","face","hair-front-left","hair-front-right","hair-front-center","hair-cap"},
            Scope="Native pixel-space rest reassembly only. Deformation, sampled rendering, isolated silhouettes and exposed completion still need validation."},Assets.Json));
        foreach(var b in visible)b.Dispose();foreach(var b in complete)b.Dispose();
    }
    internal static void Inspect(string repository)
    {
        using var image=Assets.Load(Path.Combine(repository,"assets","claude-motion-v1","layers","hair.png"));
        using var canvas=new SKCanvas(image);
        using var pen=new SKPaint{Color=new SKColor(50,210,220,110),StrokeWidth=1,IsAntialias=true};
        using var text=new SKPaint{Color=SKColors.White,IsAntialias=true};using var font=new SKFont(SKTypeface.FromFamilyName("Segoe UI"),17);
        for(var n=0;n<1254;n+=100)
        {
            canvas.DrawLine(n,0,n,1254,pen);canvas.DrawLine(0,n,1254,n,pen);
            canvas.DrawText(n.ToString(),n+3,22,SKTextAlign.Left,font,text);canvas.DrawText(n.ToString(),3,n+20,SKTextAlign.Left,font,text);
        }
        Assets.Save(image,Path.Combine(repository,"artifacts","front-hair-v2","source-grid.png"));
    }
}
