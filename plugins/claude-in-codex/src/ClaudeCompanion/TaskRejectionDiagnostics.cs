using System.IO;
using System.Text.Json;

namespace ClaudeCompanion;

internal static class TaskRejectionDiagnostics
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow.AddSeconds(-1);
        TaskSignal Signal(string kind,int ms=0,string turn="a")=>new(TaskEvents.HashIdentifier("session"),TaskEvents.HashIdentifier(turn),kind,now.AddMilliseconds(ms).UtcTicks);
        string Folder(string name)=>Path.Combine(root,"rejection-"+name);
        void Expect(string name,TaskRejection expected,params TaskSignal[] signals)
        {
            var folder=Folder(name);var events=new TaskEvents();var reference=new TaskEvents();
            foreach(var signal in signals){events.Accept(signal,folder);reference.Accept(signal);}
            var actual=events.Snapshot(DateTimeOffset.UtcNow);var expectedState=reference.Snapshot(DateTimeOffset.UtcNow);
            check(actual==expectedState with{Sessions=actual.Sessions}&&actual.Sessions!.SequenceEqual(expectedState.Sessions!),name+": diagnostics preserve task state");
            var lines=File.ReadAllLines(Path.Combine(folder,TaskRejections.FileName));
            using var last=JsonDocument.Parse(lines.Last());
            check(last.RootElement.GetProperty("Reason").GetString()==expected.ToString(),name+": categorical rejection recorded");
            check(lines.All(l=>!l.Contains("PRIVATE_")&&!l.Contains("session\"")),name+": raw input is not copied into the journal");
        }
        Expect("event",TaskRejection.UnknownEvent,Signal("PRIVATE_UNKNOWN_EVENT"));
        Expect("session",TaskRejection.InvalidSession,Signal("Stop") with{Session="PRIVATE_SESSION"});
        Expect("turn",TaskRejection.InvalidTurn,Signal("Stop") with{Turn="PRIVATE_TURN"});
        Expect("stale",TaskRejection.StaleTimestamp,Signal("Stop") with{Ticks=now.AddHours(-3).UtcTicks});
        Expect("future",TaskRejection.FutureTimestamp,Signal("Stop") with{Ticks=now.AddMinutes(2).UtcTicks});
        Expect("ended",TaskRejection.SessionAlreadyEnded,Signal("SessionEnd",10),Signal("UserPromptSubmit"));
        Expect("retired",TaskRejection.RetiredTurn,Signal("UserPromptSubmit"),Signal("UserPromptSubmit",10,"b"),Signal("UserPromptSubmit",20));
        Expect("older-start",TaskRejection.OlderStart,Signal("UserPromptSubmit",10),Signal("UserPromptSubmit",0,"b"));
        Expect("equal-start",TaskRejection.ConflictingStartTimestamp,Signal("UserPromptSubmit"),Signal("UserPromptSubmit",0,"b"));
        Expect("unknown-end",TaskRejection.TerminalForUnknownSession,Signal("Stop"));
        Expect("different-end",TaskRejection.TerminalForDifferentTurn,Signal("UserPromptSubmit"),Signal("Stop",10,"b"));
        Expect("older-end",TaskRejection.OlderTerminal,Signal("UserPromptSubmit",10),Signal("Stop"));
        Expect("older-session-end",TaskRejection.OlderSessionEnd,Signal("UserPromptSubmit",10),Signal("SessionEnd"));
        var good=Folder("success");var normal=new TaskEvents();normal.Accept(Signal("UserPromptSubmit"),good);normal.Accept(Signal("Stop",20),good);
        check(!Directory.Exists(good),"normal start and Stop produce no success journal or directory");
        var sanitized=Folder("sanitized");
        TaskRejections.Write(sanitized,TaskRejection.UnknownEvent,new("PRIVATE_SESSION","PRIVATE_TURN","PRIVATE_EVENT",0),new("PRIVATE_SESSION","PRIVATE_CURRENT","PRIVATE_CURRENT_EVENT",0));
        check(!File.ReadAllText(Path.Combine(sanitized,TaskRejections.FileName)).Contains("PRIVATE_"),"journal boundary strips even forged raw identifiers, current state and event text");
        var poisoned=Folder("poisoned-wire");Directory.CreateDirectory(poisoned);
        File.WriteAllText(Path.Combine(poisoned,"1.json"),"PRIVATE_BROKEN_JSON");
        File.WriteAllText(Path.Combine(poisoned,"2.json"),"null");
        File.WriteAllText(Path.Combine(poisoned,"3.json"),new string('x',1025));
        new TaskEvents().Drain(poisoned);
        var reasons=File.ReadAllLines(Path.Combine(poisoned,TaskRejections.FileName)).Select(l=>{using var d=JsonDocument.Parse(l);return d.RootElement.GetProperty("Reason").GetString();}).ToArray();
        check(reasons.SequenceEqual(new[]{"MalformedQueueJson","NullQueueSignal","OversizedQueueFile"}),"unreadable, null and oversized queue records have distinct reasons");
        check(!Directory.EnumerateFiles(poisoned,"*.json").Any(),"poisoned queue records are still removed");
        var blocked=Folder("unwritable");File.WriteAllText(blocked,"not a directory");var guarded=new TaskEvents();
        guarded.Accept(Signal("UserPromptSubmit"),blocked);guarded.Accept(Signal("Stop",10,"b"),blocked);guarded.Accept(Signal("Stop",20),blocked);
        check(guarded.Snapshot(DateTimeOffset.UtcNow).Count==0,"journal storage failure cannot affect normal state transitions");
        var rotated=Folder("rotation");
        for(var i=0;i<450;i++)TaskRejections.Write(rotated,TaskRejection.OlderStart,Signal("UserPromptSubmit",i));
        check(File.Exists(Path.Combine(rotated,TaskRejections.PreviousName)),"journal rotates after reaching its capacity");
        check(Directory.GetFiles(rotated).Length==2&&Directory.GetFiles(rotated).All(f=>new FileInfo(f).Length<=TaskRejections.MaximumBytes),"current plus previous journal are bounded to 128 KiB total");
        check(Directory.GetFiles(rotated).SelectMany(File.ReadAllLines).All(l=>{using var d=JsonDocument.Parse(l);return d.RootElement.GetProperty("Stage").GetString()=="Receiver";}),"rotation retains complete JSON lines");
        var concurrent=Folder("concurrent");Parallel.For(0,8,i=>TaskRejections.Write(concurrent,TaskRejection.OlderStart,Signal("Stop",i)));
        check(File.ReadAllLines(Path.Combine(concurrent,TaskRejections.FileName)).Length==8,"concurrent receiver writers preserve all eight complete records");
    }
}
