using System.IO;
using System.Threading;
using System.Windows;

namespace ClaudeCompanion;

public partial class App : Application
{
    private Mutex? singleton;
    private EventWaitHandle? expandEvent;
    private RegisteredWaitHandle? expandWait;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try { await Diagnostics.RunAsync(e.Args.Contains("--live"),e.Args.Contains("--window-test")); Shutdown(0); }
            catch (Exception ex) { Diagnostics.WriteFailure(ex); Shutdown(1); }
            return;
        }
        singleton = new Mutex(true, @"Local\ClaudeInCodex.Companion", out var first);
        if (!first)
        {
            if(e.Args.Contains("--expand"))try{using var existing=EventWaitHandle.OpenExisting(@"Local\ClaudeInCodex.Expand");existing.Set();}catch(WaitHandleCannotBeOpenedException){}
            Shutdown(); return;
        }
        expandEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\ClaudeInCodex.Expand");
        expandWait=ThreadPool.RegisterWaitForSingleObject(expandEvent,(_,_)=>Dispatcher.BeginInvoke(()=>
        {
            if(MainWindow is CompanionWindow companion)companion.Expand();
        }),null,Timeout.Infinite,false);
        DispatcherUnhandledException += (_, args) =>
        {
            Settings.Log("Unhandled: " + args.Exception.GetType().Name);
            args.Handled = true;
            Shutdown(1);
        };
        MainWindow = new CompanionWindow();
        MainWindow.Show();
        if(e.Args.Contains("--expand"))_ = Dispatcher.BeginInvoke(()=>((CompanionWindow)MainWindow).Expand());
    }
    protected override void OnExit(ExitEventArgs e)
    {
        singleton?.Dispose();
        expandWait?.Unregister(null);expandEvent?.Dispose();
        base.OnExit(e);
    }
}
