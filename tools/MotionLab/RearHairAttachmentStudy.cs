using System.Numerics;
using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class RearHairAttachmentStudy
{
    internal static void Run(string repository,bool baseline)
    {
        var output=Path.Combine(repository,"artifacts","rear-hair-attachment");
        var profile=Path.Combine(repository,"assets",baseline?"claude-motion-v3":"claude-motion-v4");
        using var renderer=new Renderer(profile);
        var rear=new[]{renderer.Meshes[4],renderer.FrontMeshes[3],renderer.RearCurtain!};
        if(baseline&&!rear.Any(m=>m.Weights.Any(w=>w.Any(i=>i.Bone==0&&i.Weight>.9f))))
            throw new InvalidOperationException("Baseline capture requires the original 0.5.2 detached-tip implementation.");
        var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add(name);}
        if(!baseline)
        {
            Check(rear.All(m=>m.Weights.All(w=>w.Length==1&&w[0].Bone==1&&w[0].Weight==1)),"all rear vertices belong completely to the head, including tips");
            foreach(var pose in new[]{new Pose{HeadX=19,HeadY=-13},new Pose{HeadAngle=4},new Pose{HeadX=-15,HeadY=11,HeadAngle=-4}})
            {
                renderer.Rig.Update(pose,0,0);
                foreach(var mesh in rear)
                {
                    var actual=mesh.Deform(renderer.Rig);
                    Check(mesh.Rest.Select((p,i)=>Vector2.Distance(new(actual[i].X,actual[i].Y),Vector2.Transform(p,renderer.Rig.Skin[1]))).Max()<.001f,
                        mesh.Name+": zero-wind shape inherits full head translation and rotation without stretching");
                }
                foreach(var wind in new[]{-6.5f,6.5f})
                {
                    renderer.Rig.Update(pose,wind,wind);Matrix3x2.Invert(renderer.Rig.Skin[1],out var inverse);
                    foreach(var mesh in rear)
                    {
                        var actual=mesh.Deform(renderer.Rig);var error=0f;
                        foreach(var (p,i) in mesh.Rest.Select((p,i)=>(p,i)))
                        {
                            var local=Vector2.Transform(new(actual[i].X,actual[i].Y),inverse);
                            var expected=p+new Vector2(renderer.Rig.RearOffset*RearHairFlow.Amount(p.Y),0);
                            error=Math.Max(error,Vector2.Distance(local,expected));
                        }
                        Check(error<.001f,mesh.Name+": wind is root-fixed, tip-growing and horizontal in head-local space");
                    }
                }
            }
            var flips=0;
            foreach(var motion in Motions.Catalog)for(var frame=0;frame<60;frame++)
            {
                var pose=Motions.Evaluate(motion.Id,frame/10d);renderer.Rig.Update(pose,frame%2==0?-6.5f:6.5f,0);
                foreach(var mesh in rear)
                {
                    var p=mesh.Deform(renderer.Rig);
                    for(var i=0;i<mesh.Indices.Length;i+=3)
                    {var a=p[mesh.Indices[i]];var b=p[mesh.Indices[i+1]];var c=p[mesh.Indices[i+2]];if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<=0)flips++;}
                }
            }
            Check(flips==0,"all 14 motion states x 60 samples retain non-inverted rear triangles");
        }
        var boundary=0;var holes=0;
        foreach(var id in new[]{"idle","shake"})
        {
            var hair=new Spring(38,10);var tip=new Spring(25,8);var front=new FrontHairDynamics();
            (Pose Pose,float Sway,float Tip,FrontHairPose Front) Step(double t)
            {
                var pose=MotionLayers.Evaluate(id,t,t);var target=pose.Wind+pose.AmbientWind-pose.HeadAngle*.85f-pose.HeadX*.05f;
                var h=hair.Step(target,1/120f);return(pose,h,tip.Step(h,1/120f),front.Step(target,1/120f));
            }
            for(var cycle=0;cycle<4;cycle++)for(var sub=0;sub<720;sub++)Step(sub/120d);
            for(var frame=0;frame<180;frame++)
            {
                var state=Step(frame/30d);for(var sub=1;sub<4;sub++)state=Step(frame/30d+sub/120d);
                using var high=renderer.Render(state.Pose,state.Sway,state.Tip,672,front:state.Front);
                using var image=Assets.Empty(336,336);using(var canvas=new SKCanvas(image))
                    canvas.DrawBitmap(high,new SKRect(0,0,336,336),new SKSamplingOptions(SKFilterMode.Linear));
                Assets.Save(image,Path.Combine(output,"frames",baseline?"before":"after",id,$"{frame:D3}.png"));
                for(var p=0;p<336;p++)if(image.GetPixel(p,0).Alpha>32||image.GetPixel(p,335).Alpha>32||image.GetPixel(0,p).Alpha>32||image.GetPixel(335,p).Alpha>32)boundary++;
                foreach(var source in new[]{new Vector2(450,850),new(790,850),new(625,940),new(625,1050),new(625,1075)})
                {
                    var p=Vector2.Transform(source,renderer.Rig.Skin[1]);
                    if(high.GetPixel((int)((p.X+73)*672/1400),(int)((p.Y+73)*672/1400)).Alpha<245)holes++;
                }
            }
        }
        Check(boundary==0,"360 rendered idle/shake frames stay within existing frame margins");
        Check(holes==0,"moving face and chin/neck interior samples remain opaque");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,baseline?"baseline-report.json":"report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Frames=360,Fps=30,
            Scope="0.5.2 baseline captured before modifying the runtime; actual authored idle/shake poses and persistent default springs, 2x offscreen rendering. No desktop input or live helper control."},Assets.Json));
        Console.WriteLine($"Head attachment {(baseline?"baseline":"correction")}: {checks.Count} checks, 360 frames");
    }
    internal static void Inspect(string repository)
    {
        var output=Path.Combine(repository,"artifacts","rear-hair-attachment");
        using(var front=Assets.Load(Path.Combine(repository,"assets/claude-motion-v3/layers/hair-front-right.png")))
            for(var x=840;x<=1000;x+=10)
            {
                var samples=Enumerable.Range(870,150).Select(y=>(Y:y,C:front.GetPixel(x,y))).Where(v=>v.C.Alpha>200).ToArray();
                Console.WriteLine($"x={x}: "+string.Join(" ",samples.Where(v=>v.Y%5==0).Select(v=>$"{v.Y}:{(v.C.Red<130?"ink":"fill")}")));
            }
        foreach(var (name,path) in new[]{("original","assets/claude-v2/base.png"),("front-v3","assets/claude-motion-v3/layers/hair-front-right.png"),
            ("curtain-v3","assets/claude-motion-v3/layers/hair-curtain.png"),("face","assets/claude-motion-v3/layers/face.png"),("original-hair","assets/claude-motion-v1/layers/hair.png")})
        {
            using var source=Assets.Load(Path.Combine(repository,path));using var image=Assets.Empty(660,480);using var c=new SKCanvas(image);c.Clear(new SKColor(34,34,36));
            c.DrawBitmap(source,new SKRect(810,870,1030,1030),new SKRect(0,0,660,480),new SKSamplingOptions(SKFilterMode.Nearest));
            Assets.Save(image,Path.Combine(output,name+"-detail.png"));
        }
    }
}
