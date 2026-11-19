using SkiaSharp;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MotionLab;

internal static class RearHairStudy
{
    internal static void Inspect(string repository)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v2","layers");
        using var sheet=Assets.Empty(1254,660);using var canvas=new SKCanvas(sheet);canvas.Clear(new SKColor(34,34,36));
        using var text=new SKPaint{Color=SKColors.White,IsAntialias=true};using var font=new SKFont(SKTypeface.FromFamilyName("Segoe UI"),14);
        using var grid=new SKPaint{Color=new SKColor(50,200,220,135),StrokeWidth=1};
        // Separate aligned detail crops avoid rescaling the source coordinate labels.
        using var left=Assets.Load(Path.Combine(root,"hair-front-left.png"));using var right=Assets.Load(Path.Combine(root,"hair-front-right.png"));
        canvas.Clear(new SKColor(34,34,36));
        canvas.DrawBitmap(left,new SKRect(0,750,500,1080),new SKRect(0,0,600,396),new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.DrawBitmap(right,new SKRect(780,750,1254,1080),new SKRect(630,0,1199,396),new SKSamplingOptions(SKFilterMode.Nearest));
        foreach(var side in new[]{0,1})for(var x=side==0?0:800;x<(side==0?500:1254);x+=50)
        {var px=side==0?x*1.2f:630+(x-780)*1.2f;canvas.DrawLine(px,0,px,396,grid);canvas.DrawText(x.ToString(),px+2,18,SKTextAlign.Left,font,text);}
        for(var y=800;y<1080;y+=50)
        {var py=(y-750)*1.2f;canvas.DrawLine(0,py,1254,py,grid);canvas.DrawText(y.ToString(),602,py,SKTextAlign.Center,font,text);}
        Assets.Save(sheet,Path.Combine(repository,"artifacts","rear-hair-flow","source-detail.png"));
    }
    private static SKBitmap Resolve(SKBitmap high)
    {
        var result=Assets.Empty(high.Width/2,high.Height/2);using var c=new SKCanvas(result);
        c.DrawBitmap(high,new SKRect(0,0,result.Width,result.Height),new SKSamplingOptions(SKFilterMode.Linear));return result;
    }
    internal static void Run(string repository)
    {
        var oldRoot=Path.Combine(repository,"assets","claude-motion-v2");var root=Path.Combine(repository,"assets","claude-motion-v3");
        var output=Path.Combine(repository,"artifacts","rear-hair-flow");Directory.CreateDirectory(output);
        using var old=new Renderer(oldRoot);using var current=new Renderer(root);
        var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new InvalidDataException(name);checks.Add(name);}
        Check(current.RearCurtain!=null&&!old.Meshes[4].RearFlow,"new material opts into rear flow; V2 reference remains on its original implementation");
        Check(current.Meshes[4].RearFlow&&current.FrontMeshes[3].RearFlow&&current.RearCurtain!.RearFlow,"rear underlay, backing and transferred curtain share one deformation field");
        Check(current.FrontMeshes.Take(3).All(m=>!m.RearFlow),"all three true foreground locks retain independent front skinning");
        using var splitReport=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"split-report.json")));
        Check(splitReport.RootElement.GetProperty("Success").GetBoolean(),"native rest-pose reassembly is pixel-identical after ownership transfer");
        foreach(var file in Directory.EnumerateFiles(Path.Combine(oldRoot,"layers"),"*.png").Where(f=>Path.GetFileName(f) is not("hair-front-left.png" or "hair-front-right.png")))
            Check(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"layers",Path.GetFileName(file))))),Path.GetFileName(file)+": unchanged asset bytes");
        foreach(var name in new[]{"hair-front-left","hair-front-right"})
        {
            using var front=Assets.Load(Path.Combine(root,"layers",name+".png"));
            Check(Enumerable.Range(1080,174).All(y=>Enumerable.Range(0,1254).All(x=>front.GetPixel(x,y).Alpha==0)),name+": no long rear curtain pixels remain below the cheek locks");
        }
        foreach(var size in new[]{200,336,512})
        {
            using var a=old.Render(new Pose(),0,0,size*2);using var b=current.Render(new Pose(),0,0,size*2);
            using var before=Resolve(a);using var after=Resolve(b);double sum=0;var changed=0;
            for(var y=0;y<size;y++)for(var x=0;x<size;x++)
            {
                var l=before.GetPixel(x,y);var r=after.GetPixel(x,y);
                var d=new[]{Math.Abs(l.Red-r.Red),Math.Abs(l.Green-r.Green),Math.Abs(l.Blue-r.Blue),Math.Abs(l.Alpha-r.Alpha)}.Max();sum+=d;if(d>2)changed++;
            }
            Check(sum/(size*size)<.1&&changed<size*size*.004,$"rest-pose sampled appearance is equivalent at {size}px (mean={sum/(size*size):F4}, changed={changed})");
            Assets.Save(before,Path.Combine(output,$"neutral-before-{size}.png"));Assets.Save(after,Path.Combine(output,$"neutral-after-{size}.png"));
        }
        var rearMeshes=new[]{current.Meshes[4],current.FrontMeshes[3],current.RearCurtain!};
        Check(rearMeshes.All(m=>m.Weights.All(w=>w.All(i=>i.Bone is 0 or 1))),"rear meshes no longer contain any foreground lock bone influence");
        foreach(var sign in new[]{-1f,1f})
        {
            current.Rig.Update(new Pose(),6.5f*sign,6.5f*sign);
            foreach(var mesh in rearMeshes)
            {
                var points=mesh.Deform(current.Rig);var last=0f;
                foreach(var row in mesh.Rest.Select((p,i)=>(p,i)).GroupBy(v=>v.p.Y).OrderBy(g=>g.Key))
                {
                    var offsets=row.Select(v=>points[v.i].X-v.p.X).ToArray();var amount=Math.Abs(offsets[0]);
                    Check(offsets.Max()-offsets.Min()<.0002f&&row.All(v=>Math.Abs(points[v.i].Y-v.p.Y)<.0002f),mesh.Name+$": coherent horizontal-only row at y={row.Key:F1}, sign={sign}");
                    Check(amount+.0002f>=last,mesh.Name+$": displacement increases toward tips at y={row.Key:F1}, sign={sign}");last=amount;
                    if(row.Key<=current.Rig.RearBand.Start)Check(amount<.0002f,mesh.Name+": upper root fixed");
                }
                Check(last>13.9f,mesh.Name+": bottom no longer returns to a stationary root");
            }
        }
        var flips=0;var frontDifferences=0;var extremes=0;
        foreach(var angle in new[]{-4f,0,4})foreach(var y in new[]{-16f,0,11})foreach(var wind in new[]{-6.5f,6.5f})foreach(var sign in new[]{-1f,1f})
        {
            var pose=new Pose{HeadAngle=angle,HeadY=y};var front=new FrontHairPose(2.5f*sign,-2.5f*sign,1.4f*sign,-1.4f*sign,2.2f*sign,-2.2f*sign);
            current.Rig.Update(pose,wind,wind,front);old.Rig.Update(pose,wind,wind,front);extremes++;
            foreach(var mesh in rearMeshes.Concat(current.FrontMeshes.Take(3)))
            {
                var p=mesh.Deform(current.Rig);
                for(var i=0;i<mesh.Indices.Length;i+=3)
                {var a=p[mesh.Indices[i]];var b=p[mesh.Indices[i+1]];var c=p[mesh.Indices[i+2]];if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<=0)flips++;}
            }
            for(var i=0;i<3;i++)
            {
                var a=current.FrontMeshes[i].Deform(current.Rig);var b=old.FrontMeshes[i].Deform(old.Rig);
                if(!a.SequenceEqual(b))frontDifferences++;
            }
        }
        Check(flips==0,$"{extremes} wind/head/front extremes have no inverted triangles");
        Check(frontDifferences==0,"front vertex deformation is unchanged across every extreme; correction is ownership, not an upper-front weight adjustment");
        var dynamics=new FrontHairDynamics();var hair=new Spring(38,10);var tip=new Spring(25,8);
        (FrontHairPose Front,float Sway,float Tip) Step(double t)
        {var force=MotionLayers.Ambient(t);var h=hair.Step(force,1/120f);return(dynamics.Step(force,1/120f),h,tip.Step(h,1/120f));}
        for(var cycle=0;cycle<4;cycle++)for(var i=0;i<720;i++)Step(i/120d);
        var holes=0;var changedPaws=0;var maxOffset=0f;
        for(var frame=0;frame<180;frame++)
        {
            var state=Step(frame/30d);for(var sub=1;sub<4;sub++)state=Step(frame/30d+sub/120d);
            foreach(var (renderer,name) in new[]{(old,"before"),(current,"after")})
            {
                using var high=renderer.Render(new Pose(),state.Sway,state.Tip,672,front:state.Front);using var image=Resolve(high);
                Assets.Save(image,Path.Combine(output,"frames",name,$"{frame:D3}.png"));
                if(name=="after")
                {
                    maxOffset=Math.Max(maxOffset,Math.Abs(current.Rig.RearOffset));
                    foreach(var p in new[]{new Vector2(625,1050),new(625,1075),new(250,1100),new(990,1100),new(200,1180),new(1050,1180)})
                        if(high.GetPixel((int)((p.X+73)*672/1400),(int)((p.Y+73)*672/1400)).Alpha<245)holes++;
                }
            }
            using var a=Assets.Load(Path.Combine(output,"frames","before",$"{frame:D3}.png"));using var b=Assets.Load(Path.Combine(output,"frames","after",$"{frame:D3}.png"));
            foreach(var p in new[]{(90,302),(241,302),(168,248)})if(a.GetPixel(p.Item1,p.Item2)!=b.GetPixel(p.Item1,p.Item2))changedPaws++;
        }
        Check(holes==0,"180 default-amplitude frames retain opaque sampled chin/neck/curtain interior");
        Check(changedPaws==0,"paws and central face samples stay unchanged across the paired default-wind preview");
        Check(maxOffset>8&&maxOffset<=RearHairFlow.MaximumOffset,"default wind moves the rear tips visibly within the source-margin guard");
        File.WriteAllText(Path.Combine(root,"flow-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Frames=180,Fps=30,Seconds=6,ExtremeCases=extremes,
            MaximumDefaultTipOffset=maxOffset,MaximumTipOffset=RearHairFlow.MaximumOffset,TextureBytes=current.TextureBytes,
            Scope="Same default ambient input, persistent springs and front poses on both sides; 2x offscreen rasterization. No desktop control, no installation or claim of real native interaction."},Assets.Json));
        Console.WriteLine($"Rear flow: {checks.Count} checks, {extremes} extremes, 180 paired frames");
    }
}
