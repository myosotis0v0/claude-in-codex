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
        check(start.Starts==1&&feedback.Observe(start,now)&&feedback.StateAt(now)=="thinking","new accepted turn starts sustained thinking");
        events.Accept(Signal("UserPromptSubmit"));events.Accept(Signal("UserPromptSubmit",1));
        check(events.Snapshot(now).Starts==1,"duplicate active-turn starts cannot extend or retrigger the reaction");
        check(!feedback.Observe(events.Snapshot(now),now.AddSeconds(2))&&feedback.StateAt(now.AddSeconds(3.01))=="thinking","polling does not restart or end sustained thinking");
        check(events.Snapshot(now.AddSeconds(10)).Count==1&&feedback.StateAt(now.AddSeconds(10))=="thinking","active work continues thinking beyond the old three-second limit");
        check(feedback.StateAt(now.AddHours(1))=="thinking"&&feedback.MotionTimeAt(now.AddSeconds(8))<MotionLab.Motions.Duration,"long work loops its thinking motion without compressing playback");
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
            var canceled=new TaskEvents();var canceledFeedback=new TaskFeedback();canceled.Accept(Signal("UserPromptSubmit"));canceledFeedback.Observe(canceled.Snapshot(now),now);canceled.Accept(Signal(terminal,10));
            check(canceled.Snapshot(now).Completions==0,terminal+": not reported as successful completion");
            canceledFeedback.Observe(canceled.Snapshot(now),now.AddSeconds(1));check(canceledFeedback.StateAt(now.AddSeconds(1))=="",terminal+": clears thinking without a completion animation");
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
        var parallelFeedback=new TaskFeedback();parallelFeedback.Observe(parallel.Snapshot(now),now);
        check(parallelFeedback.StateAt(now)=="complete"&&parallelFeedback.StateAt(now.AddSeconds(3))=="thinking","completion returns to sustained thinking while another chat remains active");
        parallel.Accept(Signal("Stop",5,session:"two"));parallelFeedback.Observe(parallel.Snapshot(now),now.AddSeconds(4));
        check(parallelFeedback.StateAt(now.AddSeconds(4))=="complete"&&parallelFeedback.StateAt(now.AddSeconds(7))=="","last chat completion returns to idle");
        var fast=new TaskEvents();fast.Accept(Signal("UserPromptSubmit"));fast.Accept(Signal("Stop",1));var fastFeedback=new TaskFeedback();
        check(fastFeedback.Observe(fast.Snapshot(now),now)&&fastFeedback.StateAt(now)=="complete","start and Stop in one poll prefer the terminal cue");
        var stale=new TaskFeedback();
        check(stale.Observe(start,now.AddSeconds(31))&&stale.StateAt(now.AddSeconds(31))=="thinking","an older still-active turn restores thinking after relaunch");
        check(!new TaskFeedback().Observe(new(0,true,false,0,1,0,now.AddSeconds(-31).UtcTicks),now),"old completion backlog is not replayed");
        var ordering=new TaskFeedback();ordering.Observe(new(1,true,false,1,0,now.AddSeconds(2).UtcTicks),now.AddSeconds(2));
        check(!ordering.Observe(new(1,true,false,1,1,now.AddSeconds(2).UtcTicks,now.UtcTicks),now.AddSeconds(3)),"late older completion cannot displace a newer start cue");
        var controller=new MotionController();controller.React("wink");controller.ObserveTasks(start,now);
        for(var i=0;i<40;i++)controller.Step(1/60d,new Vector(.8,-.6),true,now.AddSeconds(i/60d));
        check(controller.State=="wink"&&controller.Current.GazeX>.79&&controller.Current.GazeY<-.59,"task start preserves the active greeting and live gaze");
        for(var i=40;i<130;i++)controller.Step(1/60d,new Vector(.8,-.6),true,now.AddSeconds(i/60d));
        check(controller.State=="thinking","still-valid task feedback becomes visible after the greeting finishes");
        controller.Step(0,new Vector(),true,now.AddMinutes(1));
        check(controller.State=="thinking","active thinking survives hidden/collapsed time without advancing animation springs");
        var greetingEnd=new MotionController();greetingEnd.React("wink");greetingEnd.ObserveTasks(start,now);
        greetingEnd.ObserveTasks(new(0,true,false,1,1,now.UtcTicks,now.AddMilliseconds(500).UtcTicks),now.AddMilliseconds(500));
        for(var i=0;i<60;i++)greetingEnd.Step(1/60d,new Vector(),false,now.AddSeconds(i/60d));
        check(greetingEnd.State=="wink","normal completion received during greeting cannot truncate the wink");
        for(var i=60;i<130;i++)greetingEnd.Step(1/60d,new Vector(),false,now.AddSeconds(i/60d));
        check(greetingEnd.State=="complete","completion metadata remains available after the protected wink");
        controller.ObserveTasks(new(0,true,false,1,1,now.UtcTicks,now.AddSeconds(5).UtcTicks),now.AddSeconds(5));
        for(var i=0;i<50;i++)controller.Step(1/60d,new Vector(-.8,.6),true,now.AddSeconds(5+i/60d));
        check(controller.State=="complete"&&controller.Current.Mouth=="happy"&&controller.Current.GazeX<-.79,"completion has a happy pop and live gaze");
        controller.Step(1/60d,new Vector(),true,now.AddSeconds(8));check(controller.State=="idle","completion returns to idle automatically without another event");
        var blink=new MotionController();blink.ObserveTasks(start,now);
        for(var i=0;i<72;i++)blink.Step(1/60d,new Vector(),false,now.AddSeconds((i+1)/60d));
        check(Math.Abs(blink.Current.EyeLeft-MotionLab.Motions.Evaluate("idle",1.2).EyeLeft)<.001,"sustained thinking preserves normal paired blink timing");
        BubbleChecks(check,now);
    }
    private static void BubbleChecks(Action<bool,string> check,DateTimeOffset now)
    {
        var bubbles=new TaskBubbles();
        TaskActivity Activity(params TaskSessionActivity[] sessions)=>new(sessions.Count(s=>s.Active),true,false,Sessions:sessions);
        var one=new TaskSessionActivity("one","a",true,0);var two=new TaskSessionActivity("two","a",true,0);
        bubbles.Observe(Activity(one,two),now);var first=bubbles.Frames(now.AddSeconds(1));
        check(first.Count==2&&first.All(b=>b.State=="thinking"),"two active chats have two independent thinking bubbles");
        var slot=first.Single(b=>b.Session=="two").Slot;
        one=one with{Active=false,CompletedTicks=now.AddSeconds(1).UtcTicks};bubbles.Observe(Activity(one,two),now.AddSeconds(1));
        var mixed=bubbles.Frames(now.AddSeconds(1.5));
        check(mixed.Single(b=>b.Session=="one").State=="complete"&&mixed.Single(b=>b.Session=="two").State=="thinking","only the completed chat bubble plays its check animation");
        bubbles.Observe(Activity(one,two),now.AddSeconds(2));
        check(bubbles.Frames(now.AddSeconds(2.9)).Single(b=>b.Session=="one").Opacity<.3,"completed bubble fades out independently without polling extending it");
        bubbles.Observe(Activity(one,two),now.AddSeconds(4));var remaining=bubbles.Frames(now.AddSeconds(4));
        check(remaining.Count==1&&remaining[0].Session=="two"&&remaining[0].Slot==slot,"finishing a chat does not renumber the remaining bubble slot");
        two=two with{Active=false,CompletedTicks=now.AddSeconds(4).UtcTicks};bubbles.Observe(Activity(one,two),now.AddSeconds(4));
        check(bubbles.Frames(now.AddSeconds(5)).Single().State=="complete"&&bubbles.Frames(now.AddSeconds(7)).Count==0,"last completion disappears after its own two-second animation");
        var interrupted=new TaskBubbles();interrupted.Observe(Activity(new TaskSessionActivity("three","a",true,0)),now);
        interrupted.Observe(Activity(new TaskSessionActivity("three","a",false,0)),now.AddSeconds(1));
        check(interrupted.Frames(now.AddSeconds(1)).Count==0,"interrupt or expiry removes a bubble without inventing completion");
        var stale=new TaskBubbles();stale.Observe(Activity(new TaskSessionActivity("old","a",false,now.AddSeconds(-31).UtcTicks)),now);
        check(stale.Frames(now).Count==0,"historical completions do not reappear as bubbles");
        var concurrent=new TaskBubbles();concurrent.Observe(Activity(one with{CompletedTicks=now.UtcTicks},two with{CompletedTicks=now.UtcTicks}),now);
        check(concurrent.Frames(now.AddSeconds(.5)).Count==2&&concurrent.Frames(now.AddSeconds(.5)).All(b=>b.State=="complete"),"completions received in one poll each get a separate animation");
        var nextRound=new TaskBubbles();nextRound.Observe(Activity(new TaskSessionActivity("one","b",true,now.UtcTicks)),now);
        check(nextRound.Frames(now.AddSeconds(.5)).Single().State=="complete"&&nextRound.Frames(now.AddSeconds(3)).Single().State=="thinking","a quick next round resumes thinking after the previous completion on the same chat");
        foreach(var count in new[]{1,2,3,6,12,30})foreach(var titled in new[]{false,true})
        {
            var frames=Enumerable.Range(0,count).Select(i=>new TaskBubbleFrame(i.ToString(),"thinking",i,1,0,titled?"\u957f\u5bf9\u8bdd\u540d\u79f0\u6d4b\u8bd5":"")).ToArray();
            var area=new Rect(0,0,192,92);var layout=ActivityBubble.Layout(frames,area);
            check(layout.Count==count&&layout.All(b=>area.Contains(b.Bounds)),"all bubbles remain in the headroom: "+count+", names="+titled);
            check(!layout.Where((a,i)=>layout.Skip(i+1).Any(b=>a.Bounds.IntersectsWith(b.Bounds))).Any(),"bubble bounds do not overlap: "+count+", names="+titled);
        }
        check(ActivityBubble.Label("1234567")=="123456..."&&ActivityBubble.Label("123456")=="123456","titles have a fixed six-element limit and literal ellipsis");
        check(ActivityBubble.Label("\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600x")==string.Concat(Enumerable.Repeat("\U0001F600",6))+"...","title truncation preserves Unicode graphemes");
        using var metadata=JsonDocument.Parse("{\"data\":[{\"id\":\"one\",\"name\":\"NAME_SENTINEL\",\"preview\":\"PRIVATE_PROMPT\"},{\"id\":\"two\",\"name\":null},{\"id\":\"other\",\"name\":\"unrelated\"}]}");
        var titles=UsageClient.ParseThreadTitles(metadata.RootElement,new HashSet<string>{TaskEvents.HashIdentifier("one"),TaskEvents.HashIdentifier("two")});
        check(titles.Count==1&&titles[TaskEvents.HashIdentifier("one")]=="NAME_SENTINEL","title lookup selects only requested anonymous ids and explicit names, never prompt previews");
    }
    internal static void Preview()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("An isolated test output is required.");
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        Run(Check);using var theme=new ThemeManager();theme.Apply("dark");
        var pet=new PetView{ArtworkSize=224,RenderInset=new Vector(14,100)};Check(pet.EnableMotion(),"feedback preview loads actual packaged material");
        pet.SetThreadTitles(new Dictionary<string,string>{{"one","\u52a8\u753b\u7814\u7a76\u4e0e\u4fee\u590d"},{"two","\u6784\u5efa\u8d44\u6e90"}});
        var now=DateTimeOffset.UtcNow;pet.Measure(new Size(238,324));pet.Arrange(new Rect(0,0,238,324));
        try
        {
            var states=new List<object>();
            for(var i=0;i<240;i++)
            {
                var time=now.AddSeconds(i/30d);
                if(i==0)pet.Controller!.ObserveTasks(new(2,true,false,2,0,now.UtcTicks,Sessions:[new("one","a",true,0),new("two","a",true,0)]),time);
                if(i==90)pet.Controller!.ObserveTasks(new(1,true,false,2,1,now.UtcTicks,time.UtcTicks,Sessions:[new("one","a",false,time.UtcTicks),new("two","a",true,0)]),time);
                if(i==150)pet.Controller!.ObserveTasks(new(0,true,false,2,2,now.UtcTicks,time.UtcTicks,Sessions:[new("one","a",false,now.AddSeconds(3).UtcTicks),new("two","a",false,time.UtcTicks)]),time);
                pet.Look(new Vector(.45*Math.Sin(i/40d),-.15),true);pet.Animate(1/30d,true,time);
                var state=pet.Controller!.State;
                Check(state==(i<90?"thinking":i<210?"complete":"idle"),$"timeline frame {i}: {state}");
                var image=Diagnostics.Render(pet,238,324);Diagnostics.Save(image,Path.Combine("frames",$"{i:D3}.png"));
                if(i==110)Diagnostics.Save(image,"bubbles-mixed.png");states.Add(new{Frame=i,State=state});
            }
            File.WriteAllText(Path.Combine(output,"feedback-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,States=states,Frames=240,Fps=30,
                Scope="Synthetic events and injected wall clock, actual packaged controller and WPF offscreen composition. No desktop control, settings changes or host delivery proof."},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{pet.DisposeMotion();}
    }
}
