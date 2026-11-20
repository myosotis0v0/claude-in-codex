using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeCompanion;

public sealed record QuotaWindow(int DurationMinutes,double UsedPercent,DateTimeOffset? ResetsAt,string? Slot=null)
{
    public double Remaining=>Math.Clamp(100-UsedPercent,0,100);
    public string Label=>DurationMinutes==300?"5 小时额度":DurationMinutes==10080?"每周额度":$"{DurationMinutes} 分钟额度";
}
public sealed record UsageSnapshot(List<QuotaWindow> Windows,DateTimeOffset? UpdatedAt,string Status,bool Stale=false,ResetCredits? ResetCredits=null);

public sealed class UsageClient : IDisposable
{
    private volatile Process? process;
    private readonly object lifecycle=new(),snapshotGate=new();
    private readonly SemaphoreSlim connectGate=new(1,1),writeGate=new(1,1),titlesGate=new(1,1);
    private sealed record Pending(Process Source,TaskCompletionSource<JsonElement> Completion);
    private readonly ConcurrentDictionary<int,Pending> pending=new();
    private int requestId,accountRevision;
    private volatile bool disposed;
    private readonly Func<ProcessStartInfo>? processFixture;
    private DateTimeOffset nextTitlesRead;
    internal event Action<IReadOnlyDictionary<string,string>>? ThreadTitlesChanged;
    private volatile UsageSnapshot current=new([],null,"尚未读取");
    public UsageSnapshot Current=>current;
    public event Action<UsageSnapshot>? Changed;
    public UsageClient(){}
    internal UsageClient(Func<ProcessStartInfo> processFixture)=>this.processFixture=processFixture;
    public static string? ResolveCodex()
    {
        var explicitPath=Environment.GetEnvironmentVariable("CLAUDE_CODEX_PATH");
        if(!string.IsNullOrWhiteSpace(explicitPath)&&File.Exists(explicitPath))return explicitPath;
        foreach(var name in new[]{"ChatGPT","Codex"}) foreach(var p in Process.GetProcessesByName(name))
            using(p)
            {
                if(!Native.IsCodex(p))continue;
                try
                {
                    var path=Path.Combine(Path.GetDirectoryName(p.MainModule!.FileName)!,"resources","codex.exe");
                    if(File.Exists(path))return path;
                }
                catch { }
            }
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root))return Directory.EnumerateFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return null;
    }
    private void Publish(Func<UsageSnapshot,UsageSnapshot> update)
    {
        lock(snapshotGate)
        {
            if(disposed)return;
            var next=update(Current);
            if(ReferenceEquals(next,Current))return;
            current=next;Changed?.Invoke(next);
        }
    }
    private async Task<Process> StartAsync()
    {
        await connectGate.WaitAsync().ConfigureAwait(false);
        Process? started=null;
        try
        {
            lock(lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed,this);
                if(process is {HasExited:false} running)return running;
                StopProcess();
                var info=processFixture?.Invoke()??CodexStartInfo();
                info.UseShellExecute=false;info.CreateNoWindow=true;
                info.RedirectStandardInput=info.RedirectStandardOutput=info.RedirectStandardError=true;
                info.StandardInputEncoding=info.StandardOutputEncoding=info.StandardErrorEncoding=new UTF8Encoding(false);
                var child=new Process {StartInfo=info};started=process=child;
                child.OutputDataReceived+=(_,e)=>ReadLine(child,e.Data);
                // Drain, but never persist stderr: it can contain account or project details.
                child.ErrorDataReceived+=(_,_)=>{};
                child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
            }
            await RequestAsync(started,"initialize",new {clientInfo=new{name="claude_in_codex",title="Claude in Codex",version=typeof(App).Assembly.GetName().Version?.ToString(3)},capabilities=new{experimentalApi=false}}).ConfigureAwait(false);
            await SendAsync(started,"{\"method\":\"initialized\",\"params\":{}}").ConfigureAwait(false);
            return started;
        }
        catch{if(started!=null)StopProcess(started);throw;}
        finally{connectGate.Release();}
    }
    private static ProcessStartInfo CodexStartInfo()
    {
        var path=ResolveCodex()??throw new FileNotFoundException("Codex runtime not found");
        var info=new ProcessStartInfo(path);info.ArgumentList.Add("app-server");info.ArgumentList.Add("--stdio");return info;
    }
    private async Task SendAsync(Process source,string line)
    {
        await writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(disposed||process!=source)throw new IOException("App-server disconnected");
            await source.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally{writeGate.Release();}
    }
    private async Task<JsonElement> RequestAsync(Process source,string method,object? parameters=null)
    {
        var id=Interlocked.Increment(ref requestId);
        var completion=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id]=new(source,completion);
        try
        {
            await SendAsync(source,JsonSerializer.Serialize(new{id,method,@params=parameters})).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        finally {pending.TryRemove(id,out _);}
    }
    private void ReadLine(Process source,string? line)
    {
        if(line==null){FailPending(source);return;}
        if(string.IsNullOrWhiteSpace(line))return;
        try
        {
            using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return;
            if(root.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.Number&&id.TryGetInt32(out var number)&&
                pending.TryGetValue(number,out var request)&&request.Source==source)
            {
                if(root.TryGetProperty("error",out _))request.Completion.TrySetException(new InvalidOperationException("App-server request rejected"));
                else if(root.TryGetProperty("result",out var result))request.Completion.TrySetResult(result.Clone());
            }
            else if(process==source&&root.TryGetProperty("method",out var method)&&method.ValueKind==JsonValueKind.String)
            {
                if(method.GetString()=="account/rateLimits/updated"&&root.TryGetProperty("params",out var parameters))
                    Publish(before=>MergeUpdate(before,parameters));
                else if(method.GetString()=="account/updated")Publish(_=>{accountRevision++;return new([],null,"尚未读取");});
            }
        }
        catch(JsonException){ }
    }
    public static UsageSnapshot Parse(JsonElement result)
    {
        JsonElement limits=default;
        if(result.ValueKind==JsonValueKind.Object&&result.TryGetProperty("rateLimitsByLimitId",out var map)&&map.ValueKind==JsonValueKind.Object&&map.TryGetProperty("codex",out var codex))limits=codex;
        else if(result.ValueKind==JsonValueKind.Object&&result.TryGetProperty("rateLimits",out var legacy)&&legacy.ValueKind==JsonValueKind.Object)limits=legacy;
        var windows=ReadWindows(limits,[]);
        return new(windows,DateTimeOffset.Now,windows.Count>0?"已连接":"当前账号未返回额度窗口",ResetCredits:ResetCredits.Parse(result));
    }
    internal static UsageSnapshot MergeUpdate(UsageSnapshot before,JsonElement update)
    {
        if(before.UpdatedAt==null||update.ValueKind!=JsonValueKind.Object||!update.TryGetProperty("rateLimits",out var limits)||limits.ValueKind!=JsonValueKind.Object||
            limits.TryGetProperty("limitId",out var id)&&id.ValueKind==JsonValueKind.String&&id.GetString()!="codex")return before;
        var windows=ReadWindows(limits,before.Windows);
        return windows.SequenceEqual(before.Windows)?before:before with{Windows=windows};
    }
    private static List<QuotaWindow> ReadWindows(JsonElement limits,List<QuotaWindow> previous)
    {
        var windows=new List<QuotaWindow>();
        if(limits.ValueKind==JsonValueKind.Object)
        foreach(var name in new[]{"primary","secondary"})
        {
            var old=previous.Find(item=>item.Slot==name);
            if(!limits.TryGetProperty(name,out var w)||w.ValueKind!=JsonValueKind.Object||!w.TryGetProperty("usedPercent",out var used)||
                used.ValueKind!=JsonValueKind.Number||!used.TryGetDouble(out var percent)||!double.IsFinite(percent))
            {if(old!=null)windows.Add(old);continue;}
            var duration=w.TryGetProperty("windowDurationMins",out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var d)?d:old?.DurationMinutes??0;
            var reset=ResetCredits.ReadDate(w,"resetsAt")??old?.ResetsAt;
            windows.Add(new(duration,Math.Clamp(percent,0,100),reset,name));
        }
        return windows.OrderBy(w=>w.DurationMinutes).ToList();
    }
    public async Task RefreshAsync()
    {
        if(disposed)return;
        Process? source=null;
        try
        {
            source=await StartAsync().ConfigureAwait(false);
            // The server can announce the initial account while the first read is in flight.
            for(int attempt=0;attempt<2;attempt++)
            {
                int revision;lock(snapshotGate)revision=accountRevision;
                var result=await RequestAsync(source,"account/rateLimits/read").ConfigureAwait(false);
                bool accepted=false;
                Publish(before=>{
                    if(process!=source||accountRevision!=revision)return before;
                    accepted=true;return Parse(result);
                });
                if(accepted||process!=source||disposed)return;
            }
        }
        catch(Exception ex)
        {
            if(!disposed)
            {
                var status=ex is FileNotFoundException?"未找到 Codex 运行时":ex is TimeoutException?"读取超时，请重试":"无法读取额度，请确认 Codex 已登录后重试";
                Publish(before=>before with{Status=status,Stale=true});
                Settings.Log("Usage read: "+ex.GetType().Name);
                if(source!=null&&ex is IOException or TimeoutException)StopProcess(source);
            }
        }
    }
    internal static Dictionary<string,string> ParseThreadTitles(JsonElement result,IReadOnlySet<string> wanted)
    {
        var titles=new Dictionary<string,string>();
        if(!result.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array)return titles;
        foreach(var thread in data.EnumerateArray())
            if(thread.ValueKind==JsonValueKind.Object&&thread.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String&&
                thread.TryGetProperty("name",out var name)&&name.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(name.GetString()))
            {
                var hash=TaskEvents.HashIdentifier(id.GetString()!);
                if(wanted.Contains(hash))titles[hash]=name.GetString()!;
            }
        return titles;
    }
    internal async Task RefreshThreadTitlesAsync(IReadOnlySet<string> wanted)
    {
        if(disposed||wanted.Count==0||DateTimeOffset.UtcNow<nextTitlesRead||!await titlesGate.WaitAsync(0))return;
        nextTitlesRead=DateTimeOffset.UtcNow.AddSeconds(10);
        try
        {
            var source=await StartAsync();var titles=new Dictionary<string,string>();string? cursor=null;
            for(var page=0;page<3;page++)
            {
                var result=await RequestAsync(source,"thread/list",new{limit=100,sortKey="updated_at",currentAccountOnly=true,cursor});
                foreach(var title in ParseThreadTitles(result,wanted))titles[title.Key]=title.Value;
                if(titles.Count==wanted.Count||!result.TryGetProperty("nextCursor",out var next)||next.ValueKind!=JsonValueKind.String)break;
                cursor=next.GetString();if(string.IsNullOrEmpty(cursor))break;
            }
            if(!disposed)ThreadTitlesChanged?.Invoke(titles);
        }
        catch(Exception){ /* Optional labels never affect quota, task state or hook delivery. */ }
        finally {titlesGate.Release();}
    }
    private void StopProcess(Process? expected=null)
    {
        Process? p;
        lock(lifecycle)
        {
            if(expected!=null&&process!=expected)return;
            p=process;process=null;
        }
        if(p!=null){try{if(!p.HasExited)p.Kill(entireProcessTree:true);}catch{}p.Dispose();}
        if(p!=null)FailPending(p);
    }
    private void FailPending(Process source)
    {foreach(var item in pending.Values)if(item.Source==source)item.Completion.TrySetException(new IOException("App-server disconnected"));}
    public void Dispose(){disposed=true;StopProcess();}
}
