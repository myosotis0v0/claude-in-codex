using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ClaudeCompanion;

public sealed record QuotaWindow(int DurationMinutes,double UsedPercent,DateTimeOffset? ResetsAt)
{
    public double Remaining=>Math.Clamp(100-UsedPercent,0,100);
    public string Label=>DurationMinutes==300?"5 小时额度":DurationMinutes==10080?"每周额度":$"{DurationMinutes} 分钟额度";
}
public sealed record UsageSnapshot(List<QuotaWindow> Windows,DateTimeOffset? UpdatedAt,string Status,bool Stale=false);

public sealed class UsageClient : IDisposable
{
    private Process? process;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly ConcurrentDictionary<int,TaskCompletionSource<JsonElement>> pending=new();
    private int requestId;
    private bool disposed;
    public UsageSnapshot Current {get;private set;}=new([],null,"尚未读取");
    public event Action<UsageSnapshot>? Changed;
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
    private void Publish(UsageSnapshot value) { Current=value; Changed?.Invoke(value); }
    private async Task StartAsync()
    {
        if(process is {HasExited:false})return;
        if(process!=null)StopProcess();
        var path=ResolveCodex() ?? throw new FileNotFoundException("Codex runtime not found");
        var info=new ProcessStartInfo(path) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add("app-server");info.ArgumentList.Add("--stdio");
        process=new Process {StartInfo=info};
        process.OutputDataReceived+=(_,e)=>ReadLine(e.Data);
        // Drain, but never persist stderr: it can contain account or project details.
        process.ErrorDataReceived+=(_,_)=>{};
        process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
        await RequestAsync("initialize",new {clientInfo=new{name="claude_in_codex",title="Claude in Codex",version=typeof(App).Assembly.GetName().Version?.ToString(3)},capabilities=new{experimentalApi=false}});
        await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
    }
    private async Task<JsonElement> RequestAsync(string method,object? parameters=null)
    {
        var id=Interlocked.Increment(ref requestId);
        var completion=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id]=completion;
        try
        {
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id,method,@params=parameters}));
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally {pending.TryRemove(id,out _);}
    }
    private void ReadLine(string? line)
    {
        if(string.IsNullOrWhiteSpace(line))return;
        try
        {
            using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
            if(root.TryGetProperty("id",out var id)&&id.TryGetInt32(out var number)&&pending.TryGetValue(number,out var completion))
            {
                if(root.TryGetProperty("error",out _))completion.TrySetException(new InvalidOperationException("App-server request rejected"));
                else if(root.TryGetProperty("result",out var result))completion.TrySetResult(result.Clone());
            }
            else if(root.TryGetProperty("method",out var method)&&method.GetString()=="account/rateLimits/updated"&&root.TryGetProperty("params",out var parameters))
                Publish(Parse(parameters));
        }
        catch(JsonException){ }
    }
    public static UsageSnapshot Parse(JsonElement result)
    {
        JsonElement limits=default;
        if(result.TryGetProperty("rateLimitsByLimitId",out var map)&&map.ValueKind==JsonValueKind.Object&&map.TryGetProperty("codex",out var codex))limits=codex;
        else if(result.TryGetProperty("rateLimits",out var legacy)&&legacy.ValueKind==JsonValueKind.Object)limits=legacy;
        var windows=new List<QuotaWindow>();
        if(limits.ValueKind==JsonValueKind.Object)
        foreach(var name in new[]{"primary","secondary"})
        {
            if(!limits.TryGetProperty(name,out var w)||w.ValueKind!=JsonValueKind.Object||!w.TryGetProperty("usedPercent",out var used)||!used.TryGetDouble(out var percent)||!double.IsFinite(percent))continue;
            var duration=w.TryGetProperty("windowDurationMins",out var durationValue)&&durationValue.TryGetInt32(out var d)?d:0;
            DateTimeOffset? reset=null;
            if(w.TryGetProperty("resetsAt",out var resetValue)&&resetValue.TryGetInt64(out var seconds))
                try {reset=DateTimeOffset.FromUnixTimeSeconds(seconds);}catch(ArgumentOutOfRangeException){}
            windows.Add(new(duration,Math.Clamp(percent,0,100),reset));
        }
        return new(windows.OrderBy(w=>w.DurationMinutes).ToList(),DateTimeOffset.Now,windows.Count>0?"已连接":"当前账号未返回额度窗口");
    }
    public async Task RefreshAsync()
    {
        if(disposed||!await gate.WaitAsync(0))return;
        try
        {
            await StartAsync();
            var result=await RequestAsync("account/rateLimits/read");
            if(!disposed)Publish(Parse(result));
        }
        catch(Exception ex)
        {
            if(!disposed)
            {
                var status=ex is FileNotFoundException?"未找到 Codex 运行时":ex is TimeoutException?"读取超时，请重试":"无法读取额度，请确认 Codex 已登录后重试";
                Publish(new(Current.Windows,Current.UpdatedAt,status,true));
                Settings.Log("Usage read: "+ex.GetType().Name);
                StopProcess();
            }
        }
        finally {gate.Release();}
    }
    private void StopProcess()
    {
        var p=process;process=null;
        if(p!=null){try{if(!p.HasExited)p.Kill(entireProcessTree:true);}catch{}p.Dispose();}
        foreach(var item in pending.Values)item.TrySetException(new IOException("App-server disconnected"));
    }
    public void Dispose(){disposed=true;StopProcess();}
}
