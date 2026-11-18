using ClaudeCompanion;
using MotionLab;
using SkiaSharp;
using System.IO;
using System.Windows;

// Offline only: drive the real controller with a deterministic cursor and synthetic events.
var root=Path.GetFullPath(args[0]);
using var renderer=new Renderer(Path.Combine(root,"plugins/claude-in-codex/assets/motion"));
const int size=512, fps=30;
var epoch=DateTimeOffset.UnixEpoch.AddDays(1);
foreach(var id in new[]{"thinking","complete","pat","sleepy","wink","menu"})
{
    var dir=Path.Combine(root,"artifacts/acting-study",id);Directory.CreateDirectory(dir);
    var ctl=new MotionController();
    if(id=="sleepy")for(int i=0;i<2700;i++)ctl.Step(1d/fps,new(),true,epoch);
    var hair=new Spring(38,10);var tip=new Spring(25,8);var front=new FrontHairDynamics();
    var count=id switch{"wink" or "complete"=>72,"menu"=>54,_=>180};
    for(int f=0;f<count;f++)
    {
        var t=f/(double)fps;var now=epoch.AddSeconds(t);
        if(f==0)switch(id){
            case "thinking":ctl.ObserveTasks(new(1,true,false,1,0,now.UtcTicks),now);break;
            case "complete":ctl.ObserveTasks(new(0,true,false,0,1,0,now.UtcTicks),now);break;
            case "menu":ctl.LookAtMenu(new(-.25,-.95));break;
            case "sleepy":break;
            default:ctl.React(id);break;
        }
        var gaze=new Vector(.48*Math.Sin(t*1.1),.18*Math.Sin(t*1.7));
        var basePose=ctl.Step(1d/fps,gaze,true,now);
        if(f==2 && ctl.State!=id)throw new InvalidDataException($"Expected {id}, got {ctl.State}");
        var (p,drop)=Acting.Apply(id,basePose,t);
        for(var s=0;s<4;s++){
            var force=p.Wind+p.AmbientWind-p.HeadAngle*.85f-p.HeadX*.05f;
            tip.Step(hair.Step(force,1f/120),1f/120);front.Step(force,1f/120);
        }
        renderer.StudyId="";
        using var a=renderer.Render(basePose,ctl.Hair,ctl.Tip,size,front:ctl.FrontHair);
        renderer.StudyId=id;renderer.StudyTime=t;renderer.Drop=drop;
        using var b=renderer.Render(p,hair.Position,tip.Position,size,front:front.Current);
        using var sheet=new SKBitmap(size*2,size);using var canvas=new SKCanvas(sheet);
        var sample=new SKSamplingOptions(SKFilterMode.Nearest);
        canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(a,0,0,sample);canvas.DrawBitmap(b,size,0,sample);
        using var data=sheet.Encode(SKEncodedImageFormat.Png,100);
        using var file=File.Create(Path.Combine(dir,$"{f:D3}.png"));data.SaveTo(file);
    }
    Console.WriteLine($"{id}: {count} paired frames");
}

// Assembly marker for the shared diagnostic types; the study never starts the WPF app.
namespace ClaudeCompanion { internal sealed class App {} }
