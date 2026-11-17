using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ClaudeCompanion;

internal static class TaskFeedbackDiagnostics
{
    internal static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        TaskSignal Signal(string kind,int ms=0,string turn="a",string session="one")
        {
            using var input=JsonDocument.Parse(JsonSerializer.Serialize(new{hook_event_name=kind,session_id=session,turn_id=turn}));
            return TaskEvents.Parse(input.RootElement,now.AddMilliseconds(ms))!;
        }
        var events=new TaskEvents();var feedback=new TaskFeedback();
        events.Accept(Signal("UserPromptSubmit"));var start=events.Snapshot(now);
        check(start.Starts==1&&feedback.Observe(start,now)&&feedback.StateAt(now)=="thinking","new accepted turn emits a one-shot thinking cue");
        events.Accept(Signal("UserPromptSubmit"));events.Accept(Signal("UserPromptSubmit",1));
        check(events.Snapshot(now).Starts==1,"duplicate active-turn starts cannot extend or retrigger the reaction");
        check(!feedback.Observe(events.Snapshot(now),now.AddSeconds(2))&&feedback.StateAt(now.AddSeconds(3.01))=="","polling a stale active task never prolongs the three-second cue");
        check(events.Snapshot(now.AddSeconds(10)).Count==1&&feedback.StateAt(now.AddSeconds(10))=="","missing Stop may leave an observed active count but cannot leave a thinking animation");
        events.Accept(Signal("UserPromptSubmit",2000,"b"));var next=events.Snapshot(now);
        check(next.Count==1&&next.Starts==2&&feedback.Observe(next,now.AddSeconds(4))&&feedback.StateAt(now.AddSeconds(4))=="thinking","a new round retriggers even when the stale aggregate count never changed");
        events.Accept(Signal("Stop",2100));
        check(events.Snapshot(now).Completions==0,"late Stop from a retired round cannot manufacture completion");
        events.Accept(Signal("Stop",5000,"b"));var end=events.Snapshot(now);
        check(end.Completions==1&&feedback.Observe(end,now.AddSeconds(5))&&feedback.StateAt(now.AddSeconds(5))=="complete","matching Stop interrupts thinking with the completion reaction");
        events.Accept(Signal("Stop",5000,"b"));events.Accept(Signal("Stop",5100,"b"));
        check(events.Snapshot(now).Completions==1&&!feedback.Observe(events.Snapshot(now),now.AddSeconds(6))&&feedback.StateAt(now.AddSeconds(7.01))=="","duplicate Stops and repeated polling cannot extend the two-second completion");
        events.Accept(Signal("UserPromptSubmit",5200,"b"));
        check(events.Snapshot(now).Starts==3,"newer same-id continuation remains a separate start cue");
        foreach(var terminal in new[]{"Interrupt","SessionEnd"})
        {
            var canceled=new TaskEvents();canceled.Accept(Signal("UserPromptSubmit"));canceled.Accept(Signal(terminal,10));
            check(canceled.Snapshot(now).Completions==0,terminal+": not reported as successful completion");
        }
        var expired=new TaskEvents();expired.Accept(Signal("UserPromptSubmit"));
        check(expired.Snapshot(now.AddHours(3)).Completions==0,"expiry is not a completion event");
        var unknown=new TaskEvents();unknown.Accept(Signal("Stop",5,"unknown"));
        check(unknown.Snapshot(now).Completions==0,"unmatched Stop does not animate completion");
        var reverse=new TaskEvents();reverse.Accept(Signal("Stop",10));reverse.Accept(Signal("UserPromptSubmit"));var reversed=reverse.Snapshot(now);
        check(reversed.Starts==0&&reversed.Completions==1,"end-before-start delivery resolves to completion without reviving thinking");
        reverse.Accept(Signal("UserPromptSubmit"));check(reverse.Snapshot(now).Completions==1,"repeated reversed start does not duplicate completion");
        var parallel=new TaskEvents();parallel.Accept(Signal("UserPromptSubmit"));parallel.Accept(Signal("UserPromptSubmit",1,session:"two"));parallel.Accept(Signal("Stop",2));
        check(parallel.Snapshot(now).Count==1&&parallel.Snapshot(now).Completions==1,"one finished round emits completion even with another active session");
        var fast=new TaskEvents();fast.Accept(Signal("UserPromptSubmit"));fast.Accept(Signal("Stop",1));var fastFeedback=new TaskFeedback();
        check(fastFeedback.Observe(fast.Snapshot(now),now)&&fastFeedback.StateAt(now)=="complete","start and Stop in one poll prefer the terminal cue");
        var stale=new TaskFeedback();
        check(!stale.Observe(start,now.AddSeconds(31))&&stale.StateAt(now.AddSeconds(31))=="","old backlog is consumed without replaying historical thinking");
        check(!new TaskFeedback().Observe(new(0,true,false,0,1,0,now.AddSeconds(-31).UtcTicks),now),"old completion backlog is not replayed");
        var ordering=new TaskFeedback();ordering.Observe(new(1,true,false,1,0,now.AddSeconds(2).UtcTicks),now.AddSeconds(2));
        check(!ordering.Observe(new(1,true,false,1,1,now.AddSeconds(2).UtcTicks,now.UtcTicks),now.AddSeconds(3)),"late older completion cannot displace a newer start cue");
        var controller=new MotionController();controller.React("wink");controller.ObserveTasks(start,now);
        for(var i=0;i<40;i++)controller.Step(1/60d,new Vector(.8,-.6),true,now.AddSeconds(i/60d));
        check(controller.State=="thinking"&&controller.Current.GazeX>.79&&controller.Current.GazeY<-.59,"start reaction supersedes greeting while preserving live gaze");
        controller.Step(0,new Vector(),true,now.AddMinutes(1));
        check(controller.State=="idle","wall-clock expiry clears the cue after hidden/collapsed time without advancing animation springs");
        controller.ObserveTasks(new(0,true,false,1,1,now.UtcTicks,now.AddSeconds(5).UtcTicks),now.AddSeconds(5));
        for(var i=0;i<50;i++)controller.Step(1/60d,new Vector(-.8,.6),true,now.AddSeconds(5+i/60d));
        check(controller.State=="complete"&&controller.Current.Mouth=="smile"&&controller.Current.GazeX<-.79,"completion has a small smile, gentle nod and live gaze");
        controller.Step(1/60d,new Vector(),true,now.AddSeconds(8));check(controller.State=="idle","completion returns to idle automatically without another event");
        var blink=new MotionController();blink.ObserveTasks(start,now);
        for(var i=0;i<72;i++)blink.Step(1/60d,new Vector(),false,now.AddSeconds((i+1)/60d));
        check(Math.Abs(blink.Current.EyeLeft-MotionLab.Motions.Evaluate("idle",1.2).EyeLeft)<.001,"compressing the start pose does not speed up normal paired blinks");
    }
    internal static void Preview()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("An isolated test output is required.");
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        Run(Check);using var theme=new ThemeManager();theme.Apply("dark");
        var pet=new PetView{ArtworkSize=224,RenderInset=new Vector(14,14)};Check(pet.EnableMotion(),"feedback preview loads actual packaged material");
        var now=DateTimeOffset.UtcNow;pet.Measure(new Size(238,238));pet.Arrange(new Rect(0,0,238,238));
        try
        {
            var states=new List<object>();
            for(var i=0;i<240;i++)
            {
                var time=now.AddSeconds(i/30d);
                if(i==0)pet.Controller!.ObserveTasks(new(1,true,false,1,0,now.UtcTicks),time);
                if(i==150)pet.Controller!.ObserveTasks(new(0,true,false,1,1,now.UtcTicks,time.UtcTicks),time);
                pet.Look(new Vector(.45*Math.Sin(i/40d),-.15),true);pet.Animate(1/30d,true,time);
                var state=pet.Controller!.State;
                Check(state==(i<90?"thinking":i<150?"idle":i<210?"complete":"idle"),$"timeline frame {i}: {state}");
                Diagnostics.Save(Diagnostics.Render(pet,238,238),Path.Combine("frames",$"{i:D3}.png"));states.Add(new{Frame=i,State=state});
            }
            File.WriteAllText(Path.Combine(output,"feedback-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,States=states,Frames=240,Fps=30,
                Scope="Synthetic events and injected wall clock, actual packaged controller and WPF offscreen composition. No desktop control, settings changes or host delivery proof."},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{pet.DisposeMotion();}
    }
}
