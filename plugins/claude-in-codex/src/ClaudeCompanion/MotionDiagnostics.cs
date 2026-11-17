using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MotionLab;

namespace ClaudeCompanion;

internal static class MotionDiagnostics
{
    internal static void Run()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("Set CLAUDE_TEST_OUTPUT to a workspace test directory.");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        var now=DateTimeOffset.UtcNow;
        using var theme=new ThemeManager();theme.Apply("dark");
        TaskSignal Parse(string kind,string session="a",string turn="1",string extra="")
        {
            using var json=JsonDocument.Parse($"{{\"hook_event_name\":\"{kind}\",\"session_id\":\"{session}\",\"turn_id\":\"{turn}\",\"prompt\":\"private sentinel\"{extra}}}");
            return TaskEvents.Parse(json.RootElement,now)!;
        }
        var start=Parse("UserPromptSubmit");var stop=Parse("Stop");
        Check(start.Session!="a"&&start.Turn!="1"&&!JsonSerializer.Serialize(start).Contains("private sentinel"),"only hashed ids and event metadata leave the hook parser");
        Check(Parse("UserPromptSubmit",extra:",\"agent_id\":\"child\"")==null,"subagent events excluded");
        Check(Parse("OtherEvent")==null,"unknown hooks ignored");
        Check(Parse("Stop",turn:"")==null,"missing turn cannot finish another turn");
        var events=new TaskEvents();events.Accept(start);events.Accept(start);
        Check(events.Snapshot(now).Count==1,"duplicate start idempotent");
        events.Accept(Parse("UserPromptSubmit","b"));Check(events.Snapshot(now).Count==2,"parallel sessions tracked independently");
        events.Accept(stop);events.Accept(start);Check(events.Snapshot(now).Count==1,"late start cannot resurrect a stopped turn");
        events.Accept(start with{Ticks=now.AddMilliseconds(1).UtcTicks});Check(events.Snapshot(now).Count==2,"new continuation after Stop may reuse the same turn id");
        events.Accept(stop with{Ticks=now.AddMilliseconds(2).UtcTicks});
        events.Accept(Parse("Interrupt","b"));Check(events.Snapshot(now).Count==0,"interrupt clears only matching active turn");
        var closed=new TaskEvents();closed.Accept(Parse("SessionEnd"));closed.Accept(start);
        Check(closed.Snapshot(now).Count==0,"out-of-order session end tombstone wins");
        var expiry=new TaskEvents();expiry.Accept(start);var expired=expiry.Snapshot(now.AddHours(3));
        Check(expired.Count==0&&expired.Expired,"missing stop expires as unknown, not success");
        var spool=Path.Combine(output,"event-spool");Directory.CreateDirectory(spool);
        using(var input=new MemoryStream(Encoding.UTF8.GetBytes("{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"test\",\"turn_id\":\"test\",\"prompt\":\"private sentinel\",\"transcript_path\":\"secret\"}")))TaskEvents.Emit(input,spool);
        Check(Directory.EnumerateFiles(spool).All(p=>!File.ReadAllText(p).Contains("private sentinel")&&!File.ReadAllText(p).Contains("secret")),"on-disk bridge contains no prompt or transcript path");
        Check(new TaskEvents().Drain(spool).Count==1&&!Directory.EnumerateFiles(spool).Any(),"atomic spool event consumed and removed");
        var controller=new MotionController();
        TaskFeedbackDiagnostics.Run(Check);
        for(var i=0;i<60;i++)controller.Step(1/30d,new Vector(.8,.3),true);
        var time=controller.Clock;var hair=controller.Hair;
        controller.Step(0,new Vector(.8,.3),true);Check(controller.Clock==time&&controller.Hair==hair,"paused animation does not advance springs or ambient phase");
        controller.Step(60,new Vector(),true);Check(controller.Clock-time<.081,"resume clamps hidden-time catch-up");
        controller.ObserveTasks(new(1,true,false,1,0,now.UtcTicks),now);
        controller.Step(1/30d,new Vector(),true,now);Check(controller.State=="thinking","new task event selects a bounded thinking reaction");
        controller.React("pat");controller.Step(1/30d,new Vector(),true,now);Check(controller.State=="pat","head-pat may interrupt the start reaction");
        for(var i=0;i<190;i++)controller.Step(1/30d,new Vector(),true,now.AddSeconds(7));
        Check(controller.State=="idle","reaction cannot return to expired thinking even without a Stop");
        for(var i=0;i<2800;i++)controller.Step(1/30d,new Vector(),false,now.AddSeconds(100));
        Check(controller.State=="sleepy","long idle uses sleepy pose");controller.Touch();controller.Step(1/30d,new Vector(),false,now.AddSeconds(100));
        Check(controller.State=="idle","interaction wakes long idle");
        Check(new Settings().AmbientWind,"ambient hair defaults on, separate from instant gaze");
        var wink=new MotionController();wink.React("wink");
        for(var i=0;i<60;i++)wink.Step(1/60d,new Vector(),false);
        var winkMid=Motions.Evaluate("wink",3);
        Check(wink.State=="wink"&&Math.Abs(wink.Current.EyeRight-winkMid.EyeRight)<.001&&Math.Abs(wink.Current.HeadAngle-winkMid.HeadAngle)<.001,
            "one second of wink matches the former three-second pose (3x speed)");
        for(var i=0;i<12;i++)wink.Step(1/60d,new Vector(),false);
        Check(Math.Abs(wink.Current.EyeLeft-Motions.Evaluate("idle",1.2).EyeLeft)<.001,
            "ordinary blink on the non-winking eye is not sped up during the greeting");
        for(var i=0;i<50;i++)wink.Step(1/60d,new Vector(),false);
        Check(wink.State=="idle"&&wink.Clock<2.04,"greeting wink returns to idle after two seconds instead of six");
        var workingWink=new MotionController();workingWink.React("wink");workingWink.ObserveTasks(new(1,true,false,1,0,now.UtcTicks),now);
        for(var i=0;i<122;i++)workingWink.Step(1/60d,new Vector(),true,now.AddSeconds(i/60d));
        Check(workingWink.State=="thinking","new start reaction is not swallowed by the expansion greeting");
        var idleBlink=new MotionController();
        for(var i=0;i<72;i++)idleBlink.Step(1/60d,new Vector(),false);
        var expectedBlink=Motions.Evaluate("idle",1.2);
        Check(Math.Abs(idleBlink.Current.EyeLeft-expectedBlink.EyeLeft)<.001&&Math.Abs(idleBlink.Current.EyeRight-expectedBlink.EyeRight)<.001,
            "ordinary paired blink timing remains unchanged");
        var patTiming=new MotionController();patTiming.React("pat");
        for(var i=0;i<182;i++)patTiming.Step(1/60d,new Vector(),false);
        Check(patTiming.State=="pat","held closed-eye pat expression is not accelerated with the greeting");
        foreach(var state in new[]{"idle","thinking","sleepy","focus"})
        {
            var left=new MotionController();var right=new MotionController();
            left.React(state);right.React(state);
            for(var i=0;i<90;i++){left.Step(1/30d,new Vector(-.8,-.4),true);right.Step(1/30d,new Vector(.8,.4),true);}
            Check(left.Current.GazeX<-.79&&right.Current.GazeX>.79&&left.Current.GazeY<-.39&&right.Current.GazeY>.39,
                $"sustained {state} pose retains full live horizontal/vertical cursor gaze");
        }
        var pet=new PetView{ArtworkSize=224,RenderInset=new Vector(14,14)};
        System.Windows.Controls.ToolTipService.SetIsEnabled(pet,false);
        Check(pet.HoverTipDisabled,"pet has no tooltip and tooltip service is explicitly disabled");
        Check(pet.EnableMotion(),"runtime layered renderer loads actual packaged assets");
        try
        {
            pet.Measure(new Size(238,238));pet.Arrange(new Rect(0,0,238,238));
            var frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);
            for(var i=0;i<180;i++)
            {
                pet.Look(new Vector(Math.Sin(i/30d),Math.Cos(i/40d)*.6),true);
                if(i==30)pet.Controller!.ObserveTasks(new(1,true,false,1,0,now.AddSeconds(1).UtcTicks),now.AddSeconds(1));
                if(i==150)pet.Controller!.ObserveTasks(new(0,true,false,1,1,now.AddSeconds(1).UtcTicks,now.AddSeconds(5).UtcTicks),now.AddSeconds(5));
                pet.Animate(1/30d,true,now.AddSeconds(i/30d));
                var image=Diagnostics.Render(pet,238,238);Diagnostics.Save(image,Path.Combine("frames",$"{i:D3}.png"));
                if(i==90)Diagnostics.Save(image,"thinking-bubble.png");
            }
            Check(!pet.Hit(0,0)&&pet.Hit(130,160),"rendered alpha hit testing includes moving face but not transparent overscan");
            Check(pet.Limits.Inward==18,"approved inward gaze limit unchanged");
            var bitmap=Diagnostics.Render(pet,238,238);var pixels=new byte[238*238*4];bitmap.CopyPixels(pixels,238*4,0);
            Check(Enumerable.Range(0,238).Max(x=>pixels[((237*238+x)*4)+3])>32,"rendered paws still reach the bottom without preview safety-margin shrink");
        }
        finally{pet.DisposeMotion();}
        var timings=new List<object>();
        using(var surface=new MotionSurface())
        {
            if(surface.Renderer.RearCurtain is {} curtain)
            {
                Check(curtain.RearFlow&&surface.Renderer.Meshes[4].RearFlow&&surface.Renderer.FrontMeshes[3].RearFlow,
                    "installed rear-curtain material selects coherent rear-only flow for all three rear layers");
                Check(surface.Renderer.FrontMeshes.Take(3).All(m=>!m.RearFlow)&&curtain.Weights.All(w=>w.Length==1&&w[0].Bone==1&&w[0].Weight==1),
                    "true front locks retain their rig; every rear vertex is fully attached to the head");
                var rig=surface.Renderer.Rig;var pose=new Pose{HeadX=17,HeadY=-11,HeadAngle=4};
                rig.Update(pose,0,0);var actual=curtain.Deform(rig);
                Check(curtain.Rest.Select((p,i)=>System.Numerics.Vector2.Distance(new(actual[i].X,actual[i].Y),System.Numerics.Vector2.Transform(p,rig.Skin[1]))).Max()<.001f,
                    "installed rear curtain inherits full head translation and rotation even at the tips");
                rig.Update(pose,6.5f,6.5f);actual=curtain.Deform(rig);System.Numerics.Matrix3x2.Invert(rig.Skin[1],out var inverse);
                Check(curtain.Rest.Select((p,i)=>System.Numerics.Vector2.Distance(
                    System.Numerics.Vector2.Transform(new(actual[i].X,actual[i].Y),inverse),p+new System.Numerics.Vector2(rig.RearOffset*RearHairFlow.Amount(p.Y),0))).Max()<.001f,
                    "installed rear wind bends in head-local space before the parent transform");
            }
            Check(surface.Renderer.TextureBytes<(surface.Renderer.HasFrontHair?22_000_000:14_000_000),
                surface.Renderer.HasFrontHair?"split front hair profile decoded textures below 22 MB":"legacy cropped decoded textures below 14 MB");
            foreach(var size in new[]{192,224,336,448,720})
            {
                var pad=(int)Math.Ceiling(size*73d/1254);var ms=new List<double>();
                for(var i=0;i<100;i++)
                {
                    surface.Controller.Step(1/30d,new Vector(.5,.3),true);
                    var sw=Stopwatch.StartNew();surface.Draw(size+pad,size+pad,size,pad,pad);sw.Stop();if(i>=10)ms.Add(sw.Elapsed.TotalMilliseconds);
                }
                ms.Sort();timings.Add(new{Pixels=size,MedianMs=ms[ms.Count/2],P95Ms=ms[(int)(ms.Count*.95)]});
            }
        }
        File.WriteAllText(Path.Combine(output,"motion-runtime-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Timings=timings,
            Scope="Offscreen production WPF/CPU render and synthetic official-hook payloads. No windows shown, mouse/keyboard used, account or chat data read. Real desktop hook delivery still needs user verification."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
