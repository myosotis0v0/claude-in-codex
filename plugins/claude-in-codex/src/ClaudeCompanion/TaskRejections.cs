using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ClaudeCompanion;

internal enum TaskRejection
{
    UnknownEvent,InvalidSession,InvalidTurn,Subagent,MalformedPayload,InvalidUtf8,PayloadReadFailure,DeliveryFailure,
    StaleTimestamp,FutureTimestamp,SessionAlreadyEnded,RetiredTurn,OlderStart,ConflictingStartTimestamp,
    TerminalForUnknownSession,TerminalForDifferentTurn,OlderTerminal,OlderSessionEnd,
    MalformedQueueJson,NullQueueSignal,OversizedQueueFile,QueueReadFailed,QueueDeleteFailed,QueueScanFailed
}

// Only categorical failures and validated anonymous metadata cross this boundary.
internal static class TaskRejections
{
    internal const int MaximumBytes=65536;
    internal const string FileName="rejections.jsonl",PreviousName="rejections.previous.jsonl";
    internal static bool Identifier([NotNullWhen(true)] string? value)=>value?.Length==24&&value.All(Uri.IsHexDigit);
    internal static bool Event(string? value)=>value is "UserPromptSubmit" or "Stop" or "Interrupt" or "SessionEnd";
    internal static void Write(string? folder,TaskRejection reason,TaskSignal? signal=null,TaskSignal? current=null)
    {
        if(folder==null)return;
        try
        {
            var full=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant();
            var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..24];
            using var mutex=new Mutex(false,@"Local\ClaudeInCodex.TaskRejections."+key);var held=false;
            try
            {
                try{held=mutex.WaitOne(50);}catch(AbandonedMutexException){held=true;}
                if(!held)return;
                Directory.CreateDirectory(folder);
                var line=JsonSerializer.Serialize(new{
                    Utc=DateTime.UtcNow.ToString("O"),Stage="Receiver",Reason=reason.ToString(),
                    Event=Event(signal?.Event)?signal!.Event:"",Session=Identifier(signal?.Session)?signal!.Session:"",
                    Turn=Identifier(signal?.Turn)?signal!.Turn:"",SignalTicks=signal?.Ticks??0,
                    CurrentTurn=Identifier(current?.Turn)?current!.Turn:"",CurrentEvent=Event(current?.Event)?current!.Event:"",
                    CurrentTicks=current?.Ticks??0,Pid=Environment.ProcessId,Version=typeof(App).Assembly.GetName().Version?.ToString(3)??""})+"\n";
                var path=Path.Combine(folder,FileName);var previous=Path.Combine(folder,PreviousName);
                if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(line)>MaximumBytes)
                    File.Move(path,previous,true);
                File.AppendAllText(path,line,new UTF8Encoding(false));
            }
            finally{if(held)mutex.ReleaseMutex();}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or WaitHandleCannotBeOpenedException)
        { /* Diagnostics must never change event handling or disclose exception text. */ }
    }
}
