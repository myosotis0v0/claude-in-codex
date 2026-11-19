using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeCompanion;

// Hooks never persist prompt/reply text, transcript paths, cwd or credentials.
internal sealed record TaskSignal(string Session,string Turn,string Event,long Ticks,string? Agent=null);
internal sealed record TaskSessionActivity(string Session,string Turn,bool Active,long CompletedTicks,long StartedTicks=0,bool Compacting=false,int Children=0);
internal sealed record TaskActivity(int Count,bool Seen,bool Expired,long Starts=0,long Completions=0,long StartedTicks=0,long CompletedTicks=0,
    IReadOnlyList<TaskSessionActivity>? Sessions=null);
internal sealed class TaskEvents
{
    internal static string EventDirectory=>Path.Combine(Settings.DirectoryPath,"task-events");
    private sealed record SessionCursor(TaskSignal Signal,long StartedTicks,long CompletedTicks=0);
    private readonly Dictionary<string,SessionCursor> sessions=[];
    private readonly Dictionary<string,long> retiredTurns=[];
    private readonly Dictionary<string,TaskSignal> terminalTurns=[];
    private readonly Dictionary<string,long> sessionEnds=[];
    // Detail events never enter the main turn lifecycle. Child turn IDs need not equal the parent's.
    private readonly Dictionary<(string Session,string Id),TaskSignal> children=[],compactions=[];
    private readonly DateTimeOffset started=DateTimeOffset.UtcNow;
    private bool seen,expired;
    private long starts,completions,startedTicks,completedTicks;
    private void Completed(long ticks){completions++;completedTicks=Math.Max(completedTicks,ticks);}
    internal static string HashIdentifier(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    internal static TaskSignal? Parse(JsonElement input,DateTimeOffset now)
    {
        if(input.ValueKind!=JsonValueKind.Object)return null;
        string Field(string key)=>input.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        var kind=Field("hook_event_name");var session=Field("session_id");var turn=Field("turn_id");
        var agent=Field("agent_id");var child=kind is "SubagentStart" or "SubagentStop";
        if(!TaskRejections.Event(kind)||session.Length is 0 or >4096||turn.Length>4096||child!=(agent.Length>0)||agent.Length>4096)return null;
        if(input.TryGetProperty("agent_id",out var who)&&who.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))return null;
        if(kind!="SessionEnd"&&turn.Length==0)return null;
        return new(HashIdentifier(session),turn.Length>0?HashIdentifier(turn):"",kind,now.UtcTicks,child?HashIdentifier(agent):null);
    }
    internal static void Emit(Stream input,string? folder=null)
    {
        try
        {
            using var json=JsonDocument.Parse(input);var signal=Parse(json.RootElement,DateTimeOffset.UtcNow);if(signal==null)return;
            folder??=EventDirectory;Directory.CreateDirectory(folder);
            var final=Path.Combine(folder,$"{signal.Ticks}-{Guid.NewGuid():N}.json");
            File.WriteAllText(final+".tmp",JsonSerializer.Serialize(signal));File.Move(final+".tmp",final);
        }
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    internal void Accept(TaskSignal signal)=>Accept(signal,null);
    internal void Accept(TaskSignal signal,string? diagnosticFolder)
    {
        sessions.TryGetValue(signal.Session??"",out var observed);
        void Reject(TaskRejection reason)=>TaskRejections.Write(diagnosticFolder,reason,signal,observed?.Signal);
        if(!TaskRejections.Event(signal.Event)){Reject(TaskRejection.UnknownEvent);return;}
        if(!TaskRejections.Identifier(signal.Session)){Reject(TaskRejection.InvalidSession);return;}
        if(signal.Event!="SessionEnd"&&!TaskRejections.Identifier(signal.Turn)){Reject(TaskRejection.InvalidTurn);return;}
        var child=signal.Event is "SubagentStart" or "SubagentStop";
        if(child?!TaskRejections.Identifier(signal.Agent):signal.Agent!=null){Reject(TaskRejection.InvalidAgent);return;}
        if(signal.Ticks<started.AddHours(-2).UtcTicks){Reject(TaskRejection.StaleTimestamp);return;}
        if(signal.Ticks>DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks){Reject(TaskRejection.FutureTimestamp);return;}
        if(child||signal.Event is "PreCompact" or "PostCompact"){
            var table=child?children:compactions;var key=(signal.Session,child?signal.Agent!:signal.Turn);
            if(!table.TryGetValue(key,out var prior)||signal.Ticks>prior.Ticks||
                signal.Ticks==prior.Ticks&&signal.Event is ("SubagentStop" or "PostCompact"))table[key]=signal;
            return;
        }
        seen=true;
        if(signal.Event=="SessionEnd")
        {
            sessionEnds[signal.Session]=Math.Max(sessionEnds.GetValueOrDefault(signal.Session),signal.Ticks);
            if(sessions.TryGetValue(signal.Session,out var current)&&current.StartedTicks<=signal.Ticks)
                sessions[signal.Session]=current with{Signal=current.Signal with{Event="SessionEnd",Ticks=signal.Ticks}};
            else if(current!=null)Reject(TaskRejection.OlderSessionEnd);
            return;
        }
        if(sessionEnds.GetValueOrDefault(signal.Session)>=signal.Ticks){Reject(TaskRejection.SessionAlreadyEnded);return;}
        var id=signal.Session+":"+signal.Turn;
        if(signal.Event!="UserPromptSubmit")
        {
            if(!terminalTurns.TryGetValue(id,out var terminal)||terminal.Ticks<=signal.Ticks)terminalTurns[id]=signal;
            // Unknown/older turn endings are tombstones, not permission to stop the current turn.
            if(sessions.TryGetValue(signal.Session,out var current)&&current.Signal.Turn==signal.Turn&&current.Signal.Ticks<=signal.Ticks)
            {
                var completed=current.Signal.Event=="UserPromptSubmit"&&signal.Event=="Stop";
                if(completed)Completed(signal.Ticks);
                sessions[signal.Session]=current with{Signal=signal,CompletedTicks=completed?signal.Ticks:current.CompletedTicks};
            }
            else Reject(current==null?TaskRejection.TerminalForUnknownSession:
                current.Signal.Turn!=signal.Turn?TaskRejection.TerminalForDifferentTurn:TaskRejection.OlderTerminal);
            return;
        }
        if(retiredTurns.ContainsKey(id)){Reject(TaskRejection.RetiredTurn);return;}
        var newStart=true;
        if(sessions.TryGetValue(signal.Session,out var previous))
        {
            if(signal.Ticks<previous.StartedTicks){Reject(TaskRejection.OlderStart);return;}
            newStart=previous.Signal.Turn!=signal.Turn||previous.Signal.Event!="UserPromptSubmit"&&signal.Ticks>previous.Signal.Ticks;
            if(previous.Signal.Turn!=signal.Turn)
            {
                if(signal.Ticks==previous.StartedTicks){Reject(TaskRejection.ConflictingStartTimestamp);return;}
                retiredTurns[signal.Session+":"+previous.Signal.Turn]=signal.Ticks;
            }
        }
        // A stop may be delivered before its start; keep its terminal result without resurrecting work.
        var resolved=terminalTurns.TryGetValue(id,out var end)&&end.Ticks>=signal.Ticks?end:signal;
        sessions[signal.Session]=new(resolved,newStart?signal.Ticks:previous!.StartedTicks,resolved.Event=="Stop"?resolved.Ticks:previous?.CompletedTicks??0);
        if(newStart)
        {
            if(resolved.Event=="UserPromptSubmit"){starts++;startedTicks=Math.Max(startedTicks,signal.Ticks);}
            else if(resolved.Event=="Stop")Completed(resolved.Ticks);
        }
        if(resolved.Event=="UserPromptSubmit")expired=false;
    }
    internal TaskActivity Snapshot(DateTimeOffset now)
    {
        foreach(var key in sessions.Where(p=>now.UtcTicks-p.Value.Signal.Ticks>TimeSpan.FromHours(2).Ticks).Select(p=>p.Key).ToArray())
        {expired|=sessions[key].Signal.Event=="UserPromptSubmit";sessions.Remove(key);}
        foreach(var key in retiredTurns.Where(p=>now.UtcTicks-p.Value>TimeSpan.FromHours(2).Ticks).Select(p=>p.Key).ToArray())retiredTurns.Remove(key);
        foreach(var key in terminalTurns.Where(p=>now.UtcTicks-p.Value.Ticks>TimeSpan.FromHours(2).Ticks).Select(p=>p.Key).ToArray())terminalTurns.Remove(key);
        foreach(var key in sessionEnds.Where(p=>now.UtcTicks-p.Value>TimeSpan.FromHours(2).Ticks).Select(p=>p.Key).ToArray())sessionEnds.Remove(key);
        foreach(var table in new[]{children,compactions})
            foreach(var k in table.Where(p=>now.UtcTicks-p.Value.Ticks>TimeSpan.FromHours(2).Ticks).Select(p=>p.Key).ToArray())table.Remove(k);
        var frames=sessions.Select(pair=>{
            var (session,cursor)=pair;var active=cursor.Signal.Event=="UserPromptSubmit";var turn=cursor.Signal.Turn;
            var compact=active&&compactions.TryGetValue((session,turn),out var c)&&c.Event=="PreCompact"&&c.Ticks>=cursor.StartedTicks;
            int count=active?children.Count(p=>p.Key.Session==session&&p.Value.Event=="SubagentStart"&&p.Value.Ticks>=cursor.StartedTicks):0;
            return new TaskSessionActivity(session,turn,active,cursor.CompletedTicks,cursor.StartedTicks,compact,count);
        }).ToArray();
        return new(frames.Count(f=>f.Active),seen,expired,starts,completions,startedTicks,completedTicks,frames);
    }
    internal TaskActivity Drain(string? folder=null)
    {
        try
        {
            folder??=EventDirectory;
            if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.json").Order().Take(256))
            {
                try
                {
                    if(new FileInfo(file).Length<=1024)
                    {
                        var signal=JsonSerializer.Deserialize<TaskSignal>(File.ReadAllText(file));
                        if(signal!=null)Accept(signal,folder);else TaskRejections.Write(folder,TaskRejection.NullQueueSignal);
                    }
                    else TaskRejections.Write(folder,TaskRejection.OversizedQueueFile);
                    Delete();
                }
                catch(JsonException){TaskRejections.Write(folder,TaskRejection.MalformedQueueJson);Delete();}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {TaskRejections.Write(folder,TaskRejection.QueueReadFailed);}
                void Delete()
                {
                    try{File.Delete(file);}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){TaskRejections.Write(folder,TaskRejection.QueueDeleteFailed);}
                }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {TaskRejections.Write(folder,TaskRejection.QueueScanFailed);}
        return Snapshot(DateTimeOffset.UtcNow);
    }
}
