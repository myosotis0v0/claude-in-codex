using System.Text.Json;

namespace MotionLab;

internal static class LayerDemo
{
    private static float Smooth(float v){v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
    private static Pose Blend(Pose a,Pose b,float w)
    {
        float L(float x,float y)=>x+(y-x)*w;
        return new Pose{HeadX=L(a.HeadX,b.HeadX),HeadY=L(a.HeadY,b.HeadY),HeadAngle=L(a.HeadAngle,b.HeadAngle),
            GazeX=L(a.GazeX,b.GazeX),GazeY=L(a.GazeY,b.GazeY),EyeLeft=L(a.EyeLeft,b.EyeLeft),EyeRight=L(a.EyeRight,b.EyeRight),
            SmileEyes=L(a.SmileEyes,b.SmileEyes),BrowY=L(a.BrowY,b.BrowY),BrowLeft=L(a.BrowLeft,b.BrowLeft),BrowRight=L(a.BrowRight,b.BrowRight),
            Blush=L(a.Blush,b.Blush),Breath=L(a.Breath,b.Breath),Wind=L(a.Wind,b.Wind),AmbientWind=L(a.AmbientWind,b.AmbientWind),
            Mouth=w>.001?b.Mouth:a.Mouth,MouthMix=L(a.MouthMix,b.MouthMix),MouthOpen=L(a.MouthOpen,b.MouthOpen)};
    }
    private static Pose PoseAt(double seconds)
    {
        var t=(float)seconds;var idle=Motions.Evaluate("idle",seconds);
        var thinking=Smooth((t-1)/.35f)*(1-Smooth((t-3.2f)/.35f));
        var talking=Smooth((t-3.2f)/.35f)*(1-Smooth((t-5.5f)/.35f));
        return Blend(Blend(idle,Motions.Evaluate("thinking",seconds),thinking),Motions.Evaluate("talk",seconds),talking);
    }
    internal static void Run(Renderer renderer,string output)
    {
        foreach(var enabled in new[]{false,true})
        {
            var folder=Path.Combine(output,enabled?"demo-on":"demo-off");Directory.CreateDirectory(folder);
            var spring=new Spring(38,10);var tip=new Spring(25,8);
            (Pose Pose,float Root,float Tip) Step(double t)
            {
                var pose=PoseAt(t) with{AmbientWind=enabled?MotionLayers.Ambient(t):0};
                var first=spring.Step(pose.Wind+pose.AmbientWind-pose.HeadAngle*.85f-pose.HeadX*.05f,1/120f);
                return(pose,first,tip.Step(first,1/120f));
            }
            for(var cycle=0;cycle<5;cycle++)for(var i=0;i<720;i++)Step(i/120d);
            for(var frame=0;frame<180;frame++)
            {
                var state=Step(frame/30d);for(var sub=1;sub<4;sub++)state=Step(frame/30d+sub/120d);
                using var bitmap=renderer.Render(state.Pose,state.Root,state.Tip);Assets.Save(bitmap,Path.Combine(folder,$"{frame:D3}.png"));
            }
        }
        File.WriteAllText(Path.Combine(output,"demo-meta.json"),JsonSerializer.Serialize(new{Fps=30,Duration=6,Sequence=new[]{"idle","thinking","talk","idle"},
            Difference="Only ambient wind changes between the two sides; expressions/head movement and persistent spring instances are identical."},Assets.Json));
    }
}
