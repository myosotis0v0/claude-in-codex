using System.IO;
using System.Text.Json;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal static class RearHairDiagnostics
{
    internal static void Run()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("An isolated test output is required.");
        var baselineRoot=Environment.GetEnvironmentVariable("CLAUDE_REAR_BASELINE")??throw new InvalidOperationException("An isolated original-material directory is required.");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        var root=Path.Combine(AppContext.BaseDirectory,"assets/motion");
        using var oldLayer=SKBitmap.Decode(Path.Combine(baselineRoot,"layers/rear-hair.png"));
        using var layer=SKBitmap.Decode(Path.Combine(root,"layers/rear-hair.png"));
        Check(layer.Width==1254&&layer.Height>=1414,"generated rear hair has real pixels below the old square canvas");
        var changed=0;var max=0;
        for(var y=0;y<1140;y++)for(var x=0;x<1254;x++)
        {
            var a=oldLayer.GetPixel(x,y);var b=layer.GetPixel(x,y);
            var difference=new[]{Math.Abs(a.Red-b.Red),Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue),Math.Abs(a.Alpha-b.Alpha)}.Max();
            if(difference>0)changed++;max=Math.Max(max,difference);
        }
        Check(changed==0,"exported upper rear-hair pixels are identical to the original");
        using var before=new Renderer(baselineRoot,rearOverscan:false);using var after=new Renderer(root);
        var rear=new[]{after.Meshes[4],after.FrontMeshes[3],after.RearCurtain!};
        Check(rear.All(m=>m.BottomOverscan&&m.Rest.Max(p=>p.Y)>=1414),"all rear meshes include a genuine below-frame margin");
        Check(after.FrontMeshes.Take(3).All(m=>!m.BottomOverscan),"front hair meshes remain unchanged");
        Check(rear.All(m=>m.Weights.All(w=>w.Length==1&&w[0].Bone==1&&w[0].Weight==1)),"rear roots and extension inherit the original full head attachment");
        var oldHoles=0;var newHoles=0;var leaking=0;var samples=0;
        void Save(SKBitmap image,string name)
        {using var encoded=SKImage.FromBitmap(image);using var data=encoded.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(Path.Combine(output,name));data.SaveTo(file);}
        void Sample(Pose pose,float wind,int index)
        {
            using var a=before.Render(pose,wind,wind,336);using var b=after.Render(pose,wind,wind,336);
            foreach(var x in new[]{145,165,185,1050,1075,1100})
            {
                var px=(int)((x+73)*336/1400f);var py=(int)((1240+73)*336/1400f);
                if(a.GetPixel(px,py).Alpha<245)oldHoles++;
                if(b.GetPixel(px,py).Alpha<245)newHoles++;
            }
            for(var y=321;y<336;y++)for(var x=0;x<336;x++)if(b.GetPixel(x,y).Alpha!=a.GetPixel(x,y).Alpha)leaking++;
            foreach(var mesh in rear)
            {
                var p=mesh.Deform(after.Rig);
                for(var i=0;i<mesh.Indices.Length;i+=3)
                {
                    var u=p[mesh.Indices[i]];var v=p[mesh.Indices[i+1]];var w=p[mesh.Indices[i+2]];
                    if((v.X-u.X)*(w.Y-u.Y)-(v.Y-u.Y)*(w.X-u.X)<=0)throw new InvalidDataException("Rear extension flipped a triangle.");
                }
            }
            if(index<3){Save(a,$"before-{index}.png");Save(b,$"after-{index}.png");}
            samples++;
        }
        Sample(new Pose{HeadX=12,HeadY=-14,HeadAngle=4},6.5f,0);
        Sample(new Pose{HeadX=-12,HeadY=-14,HeadAngle=-4},-6.5f,1);
        Sample(new Pose(),0,2);
        foreach(var motion in Motions.Catalog)for(var i=0;i<24;i++)Sample(Motions.Evaluate(motion.Id,i/4d),i%2==0?-6.5f:6.5f,3);
        Check(oldHoles>0,"original material reproduces transparent lower-edge gaps under motion");
        Check(newHoles==0,"generated lower hair covers every tested lower-edge interior sample");
        Check(leaking==0,"the fixed display cut prevents extended hair leaking below the original bottom edge");
        File.WriteAllText(Path.Combine(output,"rear-hair-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Samples=samples,OldHoles=oldHoles,NewHoles=newHoles,LeakPixels=leaking,
            UpperDifferentPixels=changed,UpperMaximumChannelDifference=max,Scope="Generated artwork and actual packaged renderer, offscreen only. No mouse, keyboard, host state or installed-process changes."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
