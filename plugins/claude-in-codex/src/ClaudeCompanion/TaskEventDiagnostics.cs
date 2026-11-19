using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeCompanion;

internal static class TaskEventDiagnostics
{
    internal static void Run()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("An isolated test output is required.");
        Directory.CreateDirectory(output);var fixtures=Path.Combine(output,"cases-"+Guid.NewGuid().ToString("N"));var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        var now=DateTimeOffset.UtcNow.AddSeconds(-2);
        TaskSignal Signal(string kind,int ms=0,string turn="a",string session="one")
        {
            using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{hook_event_name=kind,session_id=session,turn_id=turn}));
            return TaskEvents.Parse(json.RootElement,now.AddMilliseconds(ms))!;
        }
        int Count(TaskEvents e)=>e.Snapshot(DateTimeOffset.UtcNow).Count;
        var a=Signal("UserPromptSubmit");var b=Signal("UserPromptSubmit",10,"b");var stopB=Signal("Stop",30,"b");
        var lost=new TaskEvents();lost.Accept(a);lost.Accept(b);
        Check(Count(lost)==1,"new turn replaces a missing old Stop within the same session");
        lost.Accept(stopB);Check(Count(lost)==0,"finishing the current turn clears the entire session despite a missing old Stop");
        lost.Accept(a with{Ticks=now.AddMilliseconds(40).UtcTicks});
        Check(Count(lost)==0,"a retired old turn cannot return as active even with a later delivery timestamp");
        var late=new TaskEvents();late.Accept(a);late.Accept(b);late.Accept(Signal("Stop",20));
        Check(Count(late)==1,"late old Stop cannot close the new current turn");
        late.Accept(Signal("Interrupt",25));Check(Count(late)==1,"late old interrupt cannot close the new current turn");
        late.Accept(stopB);Check(Count(late)==0,"current Stop still closes after late old terminal events");
        var reverse=new TaskEvents();reverse.Accept(stopB);reverse.Accept(b);
        Check(Count(reverse)==0,"Stop delivered before its matching start remains terminal");
        var stopBeforeNew=new TaskEvents();stopBeforeNew.Accept(a);stopBeforeNew.Accept(Signal("Stop",20));stopBeforeNew.Accept(b);
        Check(Count(stopBeforeNew)==1,"a late old Stop timestamp does not discard a chronologically newer start delivered afterward");
        var duplicate=new TaskEvents();duplicate.Accept(a);duplicate.Accept(a);
        Check(Count(duplicate)==1,"duplicate start is idempotent");
        duplicate.Accept(Signal("Stop"));duplicate.Accept(a);
        Check(Count(duplicate)==0,"terminal wins equal-timestamp duplicates");
        duplicate.Accept(Signal("UserPromptSubmit",1));Check(Count(duplicate)==0,"same-id submit after Stop cannot reactivate the finished round");
        duplicate.Accept(Signal("Interrupt",2));
        duplicate.Accept(Signal("UserPromptSubmit",3));Check(Count(duplicate)==0,"late interrupt cannot remove an existing Stop seal");
        duplicate.Accept(Signal("UserPromptSubmit",4,"new"));Check(Count(duplicate)==1,"a real next round in the same chat can still start");
        var concurrent=new TaskEvents();concurrent.Accept(a);concurrent.Accept(Signal("UserPromptSubmit",1,session:"two"));
        Check(Count(concurrent)==2,"two sessions are counted independently");
        concurrent.Accept(Signal("Stop",2));Check(Count(concurrent)==1,"finishing one session does not end the other");
        concurrent.Accept(Signal("SessionEnd",3,session:"two"));Check(Count(concurrent)==0,"session end clears only its matching session");
        var ended=new TaskEvents();ended.Accept(Signal("SessionEnd",15));ended.Accept(a);
        Check(Count(ended)==0,"session end tombstone rejects older starts arriving late");
        ended.Accept(Signal("UserPromptSubmit",20,"b"));ended.Accept(Signal("SessionEnd",15));
        Check(Count(ended)==1,"older session end cannot close work started after it");
        ended.Accept(Signal("SessionEnd",25));Check(Count(ended)==0,"new session end clears its current turn");
        var unknown=new TaskEvents();unknown.Accept(a);unknown.Accept(Signal("Stop",5,"unknown"));
        Check(Count(unknown)==1,"unknown turn end does not guess that the current turn finished");
        var longWork=new TaskEvents();longWork.Accept(a);
        Check(longWork.Snapshot(now.AddMinutes(60)).Count==1,"a silent one-hour task is not ended by a newly shortened timeout");
        var expired=longWork.Snapshot(now.AddHours(3));Check(expired.Count==0&&expired.Expired,"existing two-hour fallback is unknown/expired rather than successful completion");
        foreach(var order in Permutations(new[]{a,b,Signal("Stop",20),stopB}))
        {
            var events=new TaskEvents();foreach(var signal in order)events.Accept(signal);
            Check(Count(events)==0,"all 24 start/end delivery orders converge to completed: "+string.Join(",",order.Select(s=>s.Event+":"+(s.Turn==a.Turn?"a":"b"))));
        }
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
        var script=Path.Combine(root,"scripts","task-event.ps1");
        Check(File.Exists(script),"test uses the packaged hook writer next to the tested runtime");
        var scriptText=File.ReadAllText(script);
        Check(!scriptText.Contains("ClaudeCompanion.exe")&&!scriptText.Contains("Start-Process"),"writer never starts WPF or a child executable");
        var timings=new List<double>();
        var wireJson=new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        const string privateSession="private-session-\u6d4b\u8bd5";
        string Payload(string kind,string turn="a",string session=privateSession,string reply="PRIVATE_REPLY_SENTINEL \u5df2\u5b8c\u6210\u3002")=>JsonSerializer.Serialize(new{hook_event_name=kind,session_id=session,turn_id=turn,
            prompt=kind=="UserPromptSubmit"?"PRIVATE_PROMPT_SENTINEL \u7ee7\u7eed\u6d4b\u8bd5":null,last_assistant_message=kind=="Stop"?reply:null,
            transcript_path="PRIVATE_TRANSCRIPT_SENTINEL",cwd="PRIVATE_CWD_SENTINEL",api_key="PRIVATE_KEY_SENTINEL"},wireJson);
        Check(Payload("Stop").Contains("\u5df2\u5b8c\u6210\u3002"),"fixtures carry literal Unicode rather than ASCII JSON escapes");
        string Write(string payload,string name,bool missingOutput=false,bool commandWrapper=false,int? consoleCodePage=null,bool invalidUtf8=false)
        {
            var directory=Path.Combine(fixtures,name);Directory.CreateDirectory(directory);
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,StandardInputEncoding=new UTF8Encoding(false),RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass"})info.ArgumentList.Add(arg);
            if(commandWrapper)
            {
                var encoding=consoleCodePage is int page?$"[Console]::InputEncoding=[Text.Encoding]::GetEncoding({page}); ":"";
                info.ArgumentList.Add("-Command");info.ArgumentList.Add(encoding+"& (Join-Path $env:PLUGIN_ROOT 'scripts/task-event.ps1') -Test");
                info.Environment["PLUGIN_ROOT"]=root;
            }
            else foreach(var arg in new[]{"-File",script,"-Test"})info.ArgumentList.Add(arg);
            if(missingOutput)info.Environment.Remove("CLAUDE_TEST_OUTPUT");else info.Environment["CLAUDE_TEST_OUTPUT"]=directory;
            using var p=new Process{StartInfo=info};var timer=Stopwatch.StartNew();p.Start();
            var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            try
            {
                if(invalidUtf8)p.StandardInput.BaseStream.WriteByte(0xff);else p.StandardInput.Write(payload);
                p.StandardInput.Close();
                if(!p.WaitForExit(15000))throw new TimeoutException("Hook test timed out.");
                Check(p.ExitCode==0&&stdout.GetAwaiter().GetResult().Trim()=="{}"&&stderr.GetAwaiter().GetResult().Length==0,name+": advisory empty JSON, no stderr, exit zero");
                timings.Add(timer.Elapsed.TotalMilliseconds);return directory;
            }
            finally{if(!p.HasExited){p.Kill(entireProcessTree:true);p.WaitForExit();}}
        }
        var delivery=Write(Payload("UserPromptSubmit"),"delivery");
        var detailEvents=new TaskEvents();
        var bubbles=new TaskBubbles();
        var childKey=TaskEvents.HashIdentifier("child");
        var childStart=a with{Turn=TaskEvents.HashIdentifier("child-turn"),Event="SubagentStart",Ticks=a.Ticks+20,Agent=childKey};
        var childStop=childStart with{Event="SubagentStop",Ticks=a.Ticks+90};
        var pre=a with{Event="PreCompact",Ticks=a.Ticks+30};
        var post=pre with{Event="PostCompact",Ticks=a.Ticks+80};
        var two=Signal("UserPromptSubmit",0,"a","two");
        foreach(var s in new[]{childStart,pre,a,two,childStart,childStart with{Agent=TaskEvents.HashIdentifier("second")},childStart with{Session=two.Session}})detailEvents.Accept(s);
        var view=detailEvents.Snapshot(now);bubbles.Observe(view,now);
        var f=bubbles.Frames(now.AddSeconds(1));var one=f.Single(v=>v.Session==a.Session);
        Check(one.State=="compact"&&one.Children==2&&f.Single(v=>v.Session==two.Session) is {State:"thinking",Children:1},"pre-start metadata, duplicate agent and concurrent sessions stay isolated");
        detailEvents.Accept(post);detailEvents.Accept(pre);detailEvents.Accept(childStop);detailEvents.Accept(childStart);
        detailEvents.Accept(childStop);detailEvents.Accept(a with{Ticks=a.Ticks+50});
        view=detailEvents.Snapshot(now);
        Check(view.Completions==0&&view.Count==2&&view.Sessions!.Single(s=>s.Session==a.Session) is {Compacting:false,Children:1},"out-of-order compact and child stops never complete the parent");
        Check(view.Sessions!.Single(s=>s.Session==a.Session).StartedTicks==a.Ticks,"duplicate active start does not reset elapsed time");
        detailEvents.Accept(a with{Event="Stop",Ticks=a.Ticks+TimeSpan.TicksPerSecond*6});
        detailEvents.Accept(childStop with{Ticks=a.Ticks+TimeSpan.TicksPerSecond*7});
        var finishedAt=now.AddSeconds(7);view=detailEvents.Snapshot(finishedAt);bubbles.Observe(view,finishedAt);
        var done=bubbles.Frames(finishedAt.AddMilliseconds(100)).Single(i=>i.Session==a.Session);
        Check(view.Completions==1&&view.Count==1&&done.State=="complete"&&done.Children==0&&ActivityBubble.Elapsed(done,now.AddMinutes(1))=="耗时 0:06","only main Stop signals completion and freezes duration");
        detailEvents.Accept(b with{Ticks=finishedAt.UtcTicks});view=detailEvents.Snapshot(finishedAt);
        Check(view.Sessions!.Single(s=>s.Session==a.Session) is {Compacting:false,Children:0},"old details do not leak into a new round");
        var other=f.Single(v=>v.Session==two.Session);
        Check(ActivityBubble.Elapsed(other,now.AddSeconds(65))=="耗时 1:05","per-turn observed duration");
        CheckBubbleHover(one,now.AddSeconds(65),Check);
        using(var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"hooks","hooks.json")))){
            foreach(var kind in new[]{"PreCompact","PostCompact","SubagentStart","SubagentStop"}){
                Check(config.RootElement.GetProperty("hooks").GetProperty(kind)[0].GetProperty("hooks")[0].GetProperty("command").GetString()!.Contains("task-event.ps1"),kind+" packaged hook");
                var isChild=kind.StartsWith("Subagent");
                var payload=JsonSerializer.Serialize(new{hook_event_name=kind,session_id=privateSession,turn_id="a",agent_id=isChild?"PRIVATE_AGENT":null,last_assistant_message="PRIVATE_REPLY"});
                var dir=Write(payload,"detail-"+kind);var json=File.ReadAllText(Directory.GetFiles(dir,"*.json").Single());
                var signal=JsonSerializer.Deserialize<TaskSignal>(json)!;
                Check(!json.Contains("PRIVATE")&&!json.Contains(privateSession)&&signal.Event==kind&&signal.Agent==(isChild?TaskEvents.HashIdentifier("PRIVATE_AGENT"):null),kind+" writer emits anonymous metadata only");
            }
        }
        var delivered=Directory.GetFiles(delivery,"*.json");Check(delivered.Length==1,"writer atomically publishes one complete event without WPF");
        Check(!Directory.EnumerateFiles(delivery,"*.tmp").Any(),"successful atomic delivery leaves no partial temporary file");
        using(var doc=JsonDocument.Parse(File.ReadAllText(delivered[0])))
            Check(doc.RootElement.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(new[]{"Event","Session","Ticks","Turn"}),"wire contains only four allowlisted metadata fields");
        var actual=JsonSerializer.Deserialize<TaskSignal>(File.ReadAllText(delivered[0]))!;
        using(var input=JsonDocument.Parse(Payload("UserPromptSubmit")))
        {var expected=TaskEvents.Parse(input.RootElement,DateTimeOffset.UtcNow)!;Check(actual.Session==expected.Session&&actual.Turn==expected.Turn,"PowerShell and C# agree on UTF-8 anonymous identifiers");}
        var text=string.Join("",Directory.GetFiles(delivery).Select(File.ReadAllText));
        Check(!text.Contains("PRIVATE_")&&!text.Contains("private-session"),"no prompt, reply, path, credential sentinel or raw session id persisted");
        var deliveredState=new TaskEvents();Check(deliveredState.Drain(delivery).Count==1&&!Directory.EnumerateFiles(delivery,"*.json").Any(),"production receiver consumes lightweight writer output and removes delivered files");
        Check(deliveredState.Snapshot(DateTimeOffset.UtcNow).Starts==1,"actual hook delivery also publishes one bounded start-cue version");
        // Replay through the actual PowerShell transport, intentionally omitting the first turn's Stop.
        var integration=Write(Payload("UserPromptSubmit"),"integration");
        Write(Payload("UserPromptSubmit","b"),"integration");Write(Payload("Stop","b"),"integration");
        var integrated=new TaskEvents().Drain(integration);
        Check(integrated.Count==0,"end-to-end writer/receiver: replaced old turn does not stick after the new turn completes");
        Check(integrated.Starts==2&&integrated.Completions==1,"actual writer/receiver publishes per-round edges despite unchanged aggregate count");
        foreach(var kind in new[]{"Interrupt","SessionEnd"})
        {
            var dir=Write(Payload(kind),kind);var record=JsonSerializer.Deserialize<TaskSignal>(File.ReadAllText(Directory.GetFiles(dir,"*.json").Single()))!;
            Check(record.Event==kind,kind+": lightweight writer delivers the terminal event");
        }
        foreach(var item in new[]{(Name:"malformed",Payload:"PRIVATE_BAD_JSON"),(Name:"unknown-event",Payload:Payload("Unknown")),
            (Name:"missing-turn",Payload:"{\"hook_event_name\":\"Stop\",\"session_id\":\"one\"}"),
            (Name:"subagent",Payload:"{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"one\",\"turn_id\":\"one\",\"agent_id\":\"child\"}")})
        {
            var dir=Write(item.Payload,item.Name);Check(!Directory.EnumerateFiles(dir,"*.json").Any(),item.Name+": invalid or non-main event cannot change task state");
            Check(Directory.EnumerateFiles(dir).All(f=>!File.ReadAllText(f).Contains("PRIVATE_")),item.Name+": failure diagnostics contain no payload text");
            var expected=item.Name switch{"malformed"=>"MalformedPayload","unknown-event"=>"UnknownEvent","missing-turn"=>"InvalidTurn",_=>"Subagent"};
            using var rejection=JsonDocument.Parse(File.ReadAllLines(Path.Combine(dir,TaskRejections.FileName)).Single());
            Check(rejection.RootElement.GetProperty("Reason").GetString()==expected,item.Name+": writer records the categorical failure reason");
        }
        var noOutput=Write(Payload("Stop"),"missing-test-output",true);
        Check(!Directory.EnumerateFiles(noOutput).Any(),"test mode without an explicit output fails closed without using the real event spool");
        var wrapper=Write(Payload("Stop"),"command-wrapper",commandWrapper:true);
        Check(Directory.GetFiles(wrapper,"*.json").Length==1,"configured -Command/PLUGIN_ROOT wrapper invokes the packaged lightweight script correctly");
        foreach(var page in new[]{936,1252,65001})
        {
            var dir=Write(Payload("Stop"),"code-page-"+page,commandWrapper:true,consoleCodePage:page);
            var record=JsonSerializer.Deserialize<TaskSignal>(File.ReadAllText(Directory.GetFiles(dir,"*.json").Single()))!;
            using var input=JsonDocument.Parse(Payload("Stop"));var expected=TaskEvents.Parse(input.RootElement,DateTimeOffset.UtcNow)!;
            Check(record.Session==expected.Session&&record.Turn==expected.Turn&&!File.Exists(Path.Combine(dir,"delivery-errors.log")),"UTF-8 parsing and identifier hashes are independent of console code page "+page);
        }
        foreach(var reply in new[]{"\u5df2\u5b8c\u6210\u3002","\u4e2d\u6587\"\u5f15\u53f7\"\u548c\\\u8def\u5f84\n\u65b0\u4e00\u884c\u3002","\u5b8c\u6210\u2705 \U0001F600",new string('\u4e2d',20000)+"\u3002"})
        {
            var dir=Write(Payload("Stop",reply:reply),"utf8-"+Guid.NewGuid().ToString("N"),commandWrapper:true);
            Check(Directory.GetFiles(dir,"*.json").Length==1&&!File.Exists(Path.Combine(dir,"delivery-errors.log")),"literal UTF-8 reply, punctuation, escapes and long text publish without decoding errors");
            Check(Directory.EnumerateFiles(dir).All(f=>!File.ReadAllText(f).Contains("PRIVATE_")&&!File.ReadAllText(f).Contains(reply)),"Unicode reply content never reaches the metadata spool");
        }
        var rounds=new TaskEvents();var roundsFolder="utf8-two-sessions";
        TaskActivity Deliver(string kind,string session,int round)
        {
            var dir=Write(Payload(kind,"turn-"+round+"-\u8f6e",session),roundsFolder,commandWrapper:true);
            Check(!File.Exists(Path.Combine(dir,"delivery-errors.log")),"multi-session UTF-8 transport has no delivery error");
            return rounds.Drain(dir);
        }
        for(var round=1;round<=3;round++)
        {
            Check(Deliver("UserPromptSubmit","session-\u4e00",round).Count==1,"first chat starts independently in round "+round);
            Check(Deliver("UserPromptSubmit","session-\u4e8c",round).Count==2,"both chats are active in round "+round);
            Check(Deliver("Stop","session-\u4e00",round).Count==1,"first chat completion preserves the other chat in round "+round);
            var finished=Deliver("Stop","session-\u4e8c",round);
            Check(finished.Count==0&&finished.Starts==round*2&&finished.Completions==round*2,"both chats finish with balanced round edges in round "+round);
            Check(finished.Sessions is {Count:2}&&finished.Sessions.All(s=>!s.Active&&s.CompletedTicks>0),"anonymous per-chat snapshots retain both independent completions in round "+round);
        }
        var poison=Path.Combine(fixtures,"poison");Directory.CreateDirectory(poison);File.WriteAllText(Path.Combine(poison,"invalid.json"),"broken");
        Check(new TaskEvents().Drain(poison).Count==0&&!Directory.EnumerateFiles(poison,"*.json").Any(),"malformed complete queue files are discarded, not retried forever");
        Check(!File.Exists(Path.Combine(delivery,TaskRejections.FileName))&&!File.Exists(Path.Combine(integration,TaskRejections.FileName)),"successful writer/receiver traffic creates no journal");
        Check(!File.Exists(Path.Combine(fixtures,roundsFolder,TaskRejections.FileName)),"two-chat repeated rounds produce no success records");
        var invalid=Write("","invalid-utf8",commandWrapper:true,invalidUtf8:true);
        using(var d=JsonDocument.Parse(File.ReadAllLines(Path.Combine(invalid,TaskRejections.FileName)).Single()))
            Check(d.RootElement.GetProperty("Reason").GetString()=="InvalidUtf8","invalid UTF-8 is categorized without retaining input bytes");
        var rotation=Path.Combine(fixtures,"writer-rotation");Directory.CreateDirectory(rotation);
        var seed=File.ReadAllText(Path.Combine(fixtures,"malformed",TaskRejections.FileName)).TrimEnd('\r','\n');
        File.WriteAllText(Path.Combine(rotation,TaskRejections.FileName),seed+new string(' ',TaskRejections.MaximumBytes-Encoding.UTF8.GetByteCount(seed)-1)+"\n",new UTF8Encoding(false));
        Write(Payload("Unknown"),"writer-rotation",commandWrapper:true);
        Check(new FileInfo(Path.Combine(rotation,TaskRejections.PreviousName)).Length==TaskRejections.MaximumBytes&&new FileInfo(Path.Combine(rotation,TaskRejections.FileName)).Length<=TaskRejections.MaximumBytes,"PowerShell rotates the same bounded journal format as the receiver");
        TaskRejections.Write(rotation,TaskRejection.OlderStart,Signal("UserPromptSubmit"));
        var shared=File.ReadAllLines(Path.Combine(rotation,TaskRejections.FileName)).Select(l=>{using var d=JsonDocument.Parse(l);return d.RootElement.GetProperty("Stage").GetString();}).ToArray();
        Check(shared.SequenceEqual(new[]{"Writer","Receiver"}),"PowerShell and C# safely append complete records to the shared journal");
        TaskRejectionDiagnostics.Run(fixtures,Check);
        var fields=new[]{"Utc","Stage","Reason","Event","Session","Turn","SignalTicks","CurrentTurn","CurrentEvent","CurrentTicks","Pid","Version"}.Order().ToArray();
        foreach(var file in Directory.EnumerateFiles(fixtures,"rejections*.jsonl",SearchOption.AllDirectories))foreach(var line in File.ReadLines(file))
        {
            using var d=JsonDocument.Parse(line);var record=d.RootElement;
            Check(record.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(fields),"journal contains only allowlisted metadata fields");
            foreach(var field in new[]{"Session","Turn","CurrentTurn"})
            {var value=record.GetProperty(field).GetString();Check(value==""||TaskRejections.Identifier(value),"journal identifiers are blank or anonymous hashes");}
        }
        File.WriteAllText(Path.Combine(output,"task-events-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,WriterMilliseconds=timings,
            Scope="Synthetic in-memory events and real Windows PowerShell hook subprocesses in isolated directories. No windows, cursor/keyboard, account, user settings, sessions or transcripts accessed. Real host hook delivery remains manual."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void CheckBubbleHover(TaskBubbleFrame bubble,DateTimeOffset now,Action<bool,string> check){
        var rect=new System.Windows.Rect(0,0,192,100);
        var slot=ActivityBubble.Layout([bubble],rect).Single().Bounds;
        byte[] Paint(System.Windows.Point? pointer,TaskBubbleFrame frame){
            var visual=new System.Windows.Media.DrawingVisual();
            using(var dc=visual.RenderOpen())PetView.DrawMotionLayers(dc,new System.Windows.Media.DrawingImage(),rect,default,0,1,System.Windows.Media.Brushes.Black,bubbles:[frame],area:rect,pointer:pointer,now:now);
            var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(192,100,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(visual);var data=new byte[192*100*4];bmp.CopyPixels(data,192*4,0);return data;
        }
        var normal=Paint(null,bubble);
        check(normal.SequenceEqual(Paint(new(-5,-5),bubble))&&!normal.SequenceEqual(Paint(new(slot.Left+5,slot.Top+5),bubble)),"elapsed overlay renders only on the hovered bubble");
        check(!normal.SequenceEqual(Paint(null,bubble with{State="thinking"}))&&!normal.SequenceEqual(Paint(null,bubble with{Children=0})),"compact icon and child count each change the rendered bubble");
    }
    private static IEnumerable<TaskSignal[]> Permutations(TaskSignal[] signals)
    {
        if(signals.Length==0){yield return [];yield break;}
        for(var i=0;i<signals.Length;i++)foreach(var rest in Permutations(signals.Where((_,j)=>j!=i).ToArray()))yield return [signals[i],..rest];
    }
}
