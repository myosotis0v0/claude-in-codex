using System.Diagnostics;
using System.IO;
using System.Text;
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
        duplicate.Accept(Signal("UserPromptSubmit",1));Check(Count(duplicate)==1,"same-id continuation after Stop can become active again");
        duplicate.Accept(Signal("Stop"));Check(Count(duplicate)==1,"old same-id Stop cannot end a newer continuation");
        duplicate.Accept(Signal("Interrupt",2));Check(Count(duplicate)==0,"current same-id continuation can be interrupted");
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
        string Payload(string kind,string turn="a")=>JsonSerializer.Serialize(new{hook_event_name=kind,session_id="private-session-\u6d4b\u8bd5",turn_id=turn,
            prompt="PRIVATE_PROMPT_SENTINEL",last_assistant_message="PRIVATE_REPLY_SENTINEL",transcript_path="PRIVATE_TRANSCRIPT_SENTINEL",cwd="PRIVATE_CWD_SENTINEL",api_key="PRIVATE_KEY_SENTINEL"});
        string Write(string payload,string name,bool missingOutput=false,bool commandWrapper=false)
        {
            var directory=Path.Combine(fixtures,name);Directory.CreateDirectory(directory);
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass"})info.ArgumentList.Add(arg);
            if(commandWrapper)
            {
                info.ArgumentList.Add("-Command");info.ArgumentList.Add("& (Join-Path $env:PLUGIN_ROOT 'scripts/task-event.ps1') -Test");
                info.Environment["PLUGIN_ROOT"]=root;
            }
            else foreach(var arg in new[]{"-File",script,"-Test"})info.ArgumentList.Add(arg);
            if(missingOutput)info.Environment.Remove("CLAUDE_TEST_OUTPUT");else info.Environment["CLAUDE_TEST_OUTPUT"]=directory;
            using var p=new Process{StartInfo=info};var timer=Stopwatch.StartNew();p.Start();
            var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            try
            {
                p.StandardInput.Write(payload);p.StandardInput.Close();
                if(!p.WaitForExit(15000))throw new TimeoutException("Hook test timed out.");
                Check(p.ExitCode==0&&stdout.GetAwaiter().GetResult().Trim()=="{}"&&stderr.GetAwaiter().GetResult().Length==0,name+": advisory empty JSON, no stderr, exit zero");
                timings.Add(timer.Elapsed.TotalMilliseconds);return directory;
            }
            finally{if(!p.HasExited){p.Kill(entireProcessTree:true);p.WaitForExit();}}
        }
        var delivery=Write(Payload("UserPromptSubmit"),"delivery");
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
        }
        var noOutput=Write(Payload("Stop"),"missing-test-output",true);
        Check(!Directory.EnumerateFiles(noOutput).Any(),"test mode without an explicit output fails closed without using the real event spool");
        var wrapper=Write(Payload("Stop"),"command-wrapper",commandWrapper:true);
        Check(Directory.GetFiles(wrapper,"*.json").Length==1,"configured -Command/PLUGIN_ROOT wrapper invokes the packaged lightweight script correctly");
        var poison=Path.Combine(fixtures,"poison");Directory.CreateDirectory(poison);File.WriteAllText(Path.Combine(poison,"invalid.json"),"broken");
        Check(new TaskEvents().Drain(poison).Count==0&&!Directory.EnumerateFiles(poison).Any(),"malformed complete queue files are discarded, not retried forever");
        File.WriteAllText(Path.Combine(output,"task-events-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,WriterMilliseconds=timings,
            Scope="Synthetic in-memory events and real Windows PowerShell hook subprocesses in isolated directories. No windows, cursor/keyboard, account, user settings, sessions or transcripts accessed. Real host hook delivery remains manual."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static IEnumerable<TaskSignal[]> Permutations(TaskSignal[] signals)
    {
        if(signals.Length==0){yield return [];yield break;}
        for(var i=0;i<signals.Length;i++)foreach(var rest in Permutations(signals.Where((_,j)=>j!=i).ToArray()))yield return [signals[i],..rest];
    }
}
