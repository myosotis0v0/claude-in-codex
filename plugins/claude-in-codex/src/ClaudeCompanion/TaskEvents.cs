using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeCompanion;

// Hooks never persist prompt/reply text, transcript paths, cwd or credentials.
internal sealed record TaskSignal(string Session,string Turn,string Event,long Ticks);
internal sealed record TaskActivity(int Count,bool Seen,bool Expired,long Starts=0,long Completions=0,long StartedTicks=0,long CompletedTicks=0);
internal sealed class TaskEvents
{
    internal static string EventDirectory=>Path.Combine(Settings.DirectoryPath,"task-events");
    private sealed record SessionCursor(TaskSignal Signal,long StartedTicks);
    private readonly Dictionary<string,SessionCursor> sessions=[];
    private readonly Dictionary<string,long> retiredTurns=[];
    private readonly Dictionary<string,TaskSignal> terminalTurns=[];
    private readonly Dictionary<string,long> sessionEnds=[];
    private readonly DateTimeOffset started=DateTimeOffset.UtcNow;
    private bool seen,expired;
    private long starts,completions,startedTicks,completedTicks;
    private void Completed(long ticks){completions++;completedTicks=Math.Max(completedTicks,ticks);}
    internal static TaskSignal? Parse(JsonElement input,DateTimeOffset now)
    {
        if(input.ValueKind!=JsonValueKind.Object)return null;
        string Field(string key)=>input.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        var kind=Field("hook_event_name");var session=Field("session_id");var turn=Field("turn_id");
        if(kind is not("UserPromptSubmit" or "Stop" or "Interrupt" or "SessionEnd")||session.Length==0||Field("agent_id").Length>0)return null;
        if(kind!="SessionEnd"&&turn.Length==0)return null;
        static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24];
        return new(Hash(session),turn.Length>0?Hash(turn):"",kind,now.UtcTicks);
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
    internal void Accept(TaskSignal signal)
    {
        static bool Identifier(string? id)=>id?.Length==24&&id.All(Uri.IsHexDigit);
        if(signal.Event is not("UserPromptSubmit" or "Stop" or "Interrupt" or "SessionEnd")||!Identifier(signal.Session)||
            (signal.Event!="SessionEnd"&&!Identifier(signal.Turn)))return;
        if(signal.Ticks<started.AddHours(-2).UtcTicks||signal.Ticks>DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks)return;
        seen=true;
        if(signal.Event=="SessionEnd")
        {
            sessionEnds[signal.Session]=Math.Max(sessionEnds.GetValueOrDefault(signal.Session),signal.Ticks);
            if(sessions.TryGetValue(signal.Session,out var current)&&current.StartedTicks<=signal.Ticks)
                sessions[signal.Session]=current with{Signal=current.Signal with{Event="SessionEnd",Ticks=signal.Ticks}};
            return;
        }
        if(sessionEnds.GetValueOrDefault(signal.Session)>=signal.Ticks)return;
        var id=signal.Session+":"+signal.Turn;
        if(signal.Event!="UserPromptSubmit")
        {
            if(!terminalTurns.TryGetValue(id,out var terminal)||terminal.Ticks<=signal.Ticks)terminalTurns[id]=signal;
            // Unknown/older turn endings are tombstones, not permission to stop the current turn.
            if(sessions.TryGetValue(signal.Session,out var current)&&current.Signal.Turn==signal.Turn&&current.Signal.Ticks<=signal.Ticks)
            {
                if(current.Signal.Event=="UserPromptSubmit"&&signal.Event=="Stop")Completed(signal.Ticks);
                sessions[signal.Session]=current with{Signal=signal};
            }
            return;
        }
        if(retiredTurns.ContainsKey(id))return;
        var newStart=true;
        if(sessions.TryGetValue(signal.Session,out var previous))
        {
            if(signal.Ticks<previous.StartedTicks)return;
            newStart=previous.Signal.Turn!=signal.Turn||previous.Signal.Event!="UserPromptSubmit"&&signal.Ticks>previous.Signal.Ticks;
            if(previous.Signal.Turn!=signal.Turn)
            {
                if(signal.Ticks==previous.StartedTicks)return;
                retiredTurns[signal.Session+":"+previous.Signal.Turn]=signal.Ticks;
            }
        }
        // A stop may be delivered before its start; keep its terminal result without resurrecting work.
        var resolved=terminalTurns.TryGetValue(id,out var end)&&end.Ticks>=signal.Ticks?end:signal;
        sessions[signal.Session]=new(resolved,signal.Ticks);
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
        return new(sessions.Values.Count(v=>v.Signal.Event=="UserPromptSubmit"),seen,expired,starts,completions,startedTicks,completedTicks);
    }
    internal TaskActivity Drain(string? folder=null)
    {
        try
        {
            folder??=EventDirectory;
            if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.json").Order().Take(256))
            {
                try{if(new FileInfo(file).Length<=1024){var signal=JsonSerializer.Deserialize<TaskSignal>(File.ReadAllText(file));if(signal!=null)Accept(signal);}File.Delete(file);}
                catch(JsonException){try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        return Snapshot(DateTimeOffset.UtcNow);
    }
}
