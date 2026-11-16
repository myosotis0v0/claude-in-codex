using System.IO;
using System.Threading;
using System.Windows;

namespace ClaudeCompanion;

public partial class App : Application
{
    private Mutex? singleton;
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
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Settings.Log("Unhandled: " + args.Exception.GetType().Name);
            args.Handled = true;
            Shutdown(1);
        };
        MainWindow = new CompanionWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        singleton?.Dispose();
        base.OnExit(e);
    }
}
