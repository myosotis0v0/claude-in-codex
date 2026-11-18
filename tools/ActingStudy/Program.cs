using ClaudeCompanion;
using MotionLab;
using SkiaSharp;
using System.IO;
using System.Windows;

// Offline only: drive the real controller with a deterministic cursor and synthetic events.
var root=Path.GetFullPath(args[0]);
using var renderer=new Renderer(Path.Combine(root,"plugins/claude-in-codex/assets/motion"));
const int size=512, fps=30;
if(args.Skip(1).Contains("peripherals")){renderer.PreviewPeripherals(root);return;}
if(args.Skip(1).Contains("menu-keyform")){MenuKeyform.Run(root,renderer,size);return;}
if(args.Skip(1).Contains("menu-pitch")){MenuPitchStudy.Run(root,renderer,size);return;}
var epoch=DateTimeOffset.UnixEpoch.AddDays(1);
foreach(var id in new[]{"thinking","complete","pat","sleepy","wink","menu"})
{
    var dir=Path.Combine(root,"artifacts/acting-study",id);Directory.CreateDirectory(dir);
    var baseline=new MotionController(false);var current=new MotionController();
    var controllers=new[]{baseline,current};
    if(id=="sleepy")foreach(var ctl in controllers)for(int i=0;i<2700;i++)ctl.Step(1d/fps,new(),true,epoch);
    var count=id switch{"wink" or "complete"=>72,"menu"=>54,_=>180};
    for(int f=0;f<count;f++)
    {
        var t=f/(double)fps;var now=epoch.AddSeconds(t);
        if(f==0)foreach(var ctl in controllers)switch(id){
            case "thinking":ctl.ObserveTasks(new(1,true,false,1,0,now.UtcTicks),now);break;
            case "complete":ctl.ObserveTasks(new(0,true,false,0,1,0,now.UtcTicks),now);break;
            case "menu":ctl.LookAtMenu(new(-.25,-.95));break;
            case "sleepy":break;
            default:ctl.React(id);break;
        }
        var gaze=new Vector(.48*Math.Sin(t*1.1),.18*Math.Sin(t*1.7));
        var basePose=baseline.Step(1d/fps,gaze,true,now);var p=current.Step(1d/fps,gaze,true,now);
        if(f==2 && (current.State!=id||p.CelId!=id))throw new InvalidDataException($"Expected {id}, got {p.CelId}");
        if(id=="sleepy"&&(p.Mouth!="yawn"||p.MouthMix!=1||p.MouthOpen is <0 or >1))
            throw new InvalidDataException($"Mixed or invalid sleepy mouth at frame {f}");
        using var a=renderer.Render(basePose,baseline.Hair,baseline.Tip,size,front:baseline.FrontHair);
        using var b=renderer.Render(p,current.Hair,current.Tip,size,front:current.FrontHair);
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
