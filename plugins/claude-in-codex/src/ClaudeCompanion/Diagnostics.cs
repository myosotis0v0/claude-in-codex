using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace ClaudeCompanion;

internal static class Diagnostics
{
    private static string Output=>Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??Path.Combine(Settings.DirectoryPath,"diagnostics");
    private static readonly List<string> Checks=[];
    private static void Check(bool condition,string description)
    {
        if(!condition)throw new InvalidOperationException(description);
        Checks.Add(description);
    }
    public static async Task RunAsync(bool live,bool windowTest)
    {
        Directory.CreateDirectory(Output);
        using(var json=JsonDocument.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"windowDurationMins\":10080,\"usedPercent\":22,\"resetsAt\":1800000000},\"secondary\":{\"windowDurationMins\":300,\"usedPercent\":109}}}}"))
        {
            var snapshot=UsageClient.Parse(json.RootElement);
            Check(snapshot.Windows.Count==2,"two quota windows parsed");
            Check(snapshot.Windows[0].DurationMinutes==300,"windows sorted by duration, not primary ordering");
            Check(snapshot.Windows[0].Remaining==0,"remaining percentage clamped");
            Check(snapshot.Windows[1].Remaining==78,"remaining calculated from used percentage");
            Check(snapshot.Windows[1].ResetsAt?.ToUnixTimeSeconds()==1800000000,"reset uses Unix seconds");
        }
        using(var json=JsonDocument.Parse("{\"rateLimits\":null}"))Check(UsageClient.Parse(json.RootElement).Windows.Count==0,"missing quota produces unknown, not simulated values");
        Check(PluginLifetime.ParseEnabled("[plugins.\"claude-in-codex@claude-in-codex-local\"]\nenabled = true")==true,"quoted plugin TOML enabled flag parsed");
        Check(PluginLifetime.ParseEnabled("[plugins.\"claude-in-codex@claude-in-codex-local\"]\nenabled = false")==false,"plugin disable flag closes companion");
        Check(PluginLifetime.ParseEnabled("[plugins.other]\nenabled = false") is null,"unrelated plugin setting does not dismiss companion");
        foreach(var scale in new[]{1d,1.25,1.5,2})foreach(var size in new[]{128d,224,360})
        {
            var settings=new Settings{Size=size,OffsetX=12,OffsetY=12};
            var rect=new Native.Rect{Right=1920,Bottom=1080};
            var placement=Native.Place(rect,-1920,0,scale,settings);
            Check(placement.X>=-1920&&placement.X+placement.Size<=0&&placement.Y>=0&&placement.Y+placement.Size<=1080,$"DPI placement {scale} size {size} negative monitor origin");
            settings.OffsetX=-9000;settings.OffsetY=9000;
            placement=Native.Place(rect,-1920,0,scale,settings);
            Check(placement.X+placement.Size<=0&&placement.Y>=0,$"offscreen offset clamped {scale} size {size}");
        }
        var pet=new PetView();pet.Measure(new Size(256,256));pet.Arrange(new Rect(0,0,256,256));
        var center=Render(pet,256,256);Save(center,"runtime-center.png");
        pet.Look(new Vector(-1,0),true);Save(Render(pet,256,256),"runtime-left.png");
        pet.Look(new Vector(1,0),true);var right=Render(pet,256,256);Save(right,"runtime-right.png");
        var centerPixels=new byte[256*256*4];center.CopyPixels(centerPixels,256*4,0);
        var rightPixels=new byte[centerPixels.Length];right.CopyPixels(rightPixels,256*4,0);
        var changed=0;var changedOutsideEyes=0;var orange=0;
        for(var y=0;y<256;y++)for(var x=0;x<256;x++)
        {
            var p=(y*256+x)*4;
            if(centerPixels[p+2]>220&&centerPixels[p+1]>90&&centerPixels[p+1]<180&&centerPixels[p]<80)orange++;
            if(!centerPixels.AsSpan(p,4).SequenceEqual(rightPixels.AsSpan(p,4)))
            {
                changed++;
                if(y<158||y>191||x<70||x>185)changedOutsideEyes++;
            }
        }
        Check(orange>100,"sprite nonblank and orange artwork rendered");
        Check(changed>20,"runtime gaze moves visible eyes");
        Check(changedOutsideEyes==0,"runtime gaze does not alter hair, lashes, or body outside eye areas");
        Check(!pet.Hit(0,0)&&pet.Hit(128,128),"transparent corner passes through, face hit-tests");
        pet.ShowEyes=false;pet.InvalidateVisual();
        var baselinePath=Path.Combine(AppContext.BaseDirectory,"assets","base.png");
        var sourceImage=new System.Windows.Controls.Image {Source=new BitmapImage(new Uri(baselinePath)),Stretch=Stretch.Fill};
        RenderOptions.SetBitmapScalingMode(sourceImage,BitmapScalingMode.HighQuality);
        var differences=0;
        foreach(var size in new[]{224,256,280,336})
        {
            var split=Render(pet,size,size);var single=Render(sourceImage,size,size);
            var a=new byte[size*size*4];var b=new byte[a.Length];split.CopyPixels(a,size*4,0);single.CopyPixels(b,size*4,0);
            differences+=a.Zip(b).Count(pair=>Math.Abs(pair.First-pair.Second)>1);
        }
        Check(differences==0,$"base compositing has no split-mask seams (different channels: {differences})");
        pet.ShowEyes=true;
        UsageSnapshot? actual=null;
        if(live)
        {
            using var client=new UsageClient();await client.RefreshAsync();actual=client.Current;
            Check(actual.Windows.Count>0&&!actual.Stale,"live app-server returned real account quota");
        }
        using(var theme=new ThemeManager())
        {
            // Offscreen rendering only: no window movement, input, or OS theme change.
            var window=new CompanionWindow(diagnostic:true);
            await window.DiagnosticPanel(actual??new([],null,"测试：未连接真实用量"),"light");
            Save(Render(window.DiagnosticLauncher,36,36),"launcher-light.png");
            await window.DiagnosticPanel(actual??new([],null,"测试：未连接真实用量"),"dark");
            Save(Render(window.DiagnosticLauncher,36,36),"launcher-dark.png");
            Check(window.DiagnosticLauncherVisible,"initial state retains the launcher");
            window.Expand();Check(!window.DiagnosticLauncherVisible,"expand switches from launcher to character");
            window.DiagnosticClosePet();Check(window.DiagnosticLauncherVisible&&!window.DiagnosticClosed,"actual close button collapses to launcher without terminating");
            window.Expand();window.DiagnosticClosePet();Check(window.DiagnosticLauncherVisible&&!window.DiagnosticClosed,"repeated open/close retains launcher");
            window.DisposeForDiagnostics();
        }
        var pids=Native.CodexPids();var target=Native.FindWindow(pids);
        var windowsReport=new {Found=target!=0,Visible=target!=0&&Native.IsWindowVisible(target),Minimized=target!=0&&Native.IsIconic(target),Dpi=target==0?0:Native.GetDpiForWindow(target)};
        if(windowTest)await WindowTests();
        File.WriteAllText(Path.Combine(Output,"report.json"),JsonSerializer.Serialize(new{Success=true,Checks,Windows=windowsReport,RealUsage=actual,Scope=windowTest?"WPF rendering and dedicated native fixture; no mouse/keyboard input.":"Offscreen WPF rendering and read-only discovery only; no windows shown or controlled."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task WindowTests()
    {
        var host=new Window{Title="Claude companion test fixture",Width=720,Height=540,Left=50,Top=50,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.DimGray};
        CompanionWindow? companion=null;
        try
        {
            host.Show();var hwnd=new WindowInteropHelper(host).Handle;
            companion=new CompanionWindow(()=>[Environment.ProcessId],_=>hwnd,true);
            companion.Show();await Task.Delay(400);
            var own=new WindowInteropHelper(companion).Handle;
            Check(companion.IsVisible,"native fixture: companion shows with host");
            Native.GetWindowRect(own,out var before);
            Native.GetWindowRect(hwnd,out var hostBefore);
            Native.SetWindowPos(hwnd,0,hostBefore.Left+70,hostBefore.Top+45,hostBefore.Width,hostBefore.Height,0x14);
            await Task.Delay(400);Native.GetWindowRect(own,out var after);
            Check(after.Left-before.Left==70&&after.Top-before.Top==45,"native fixture: companion follows exact physical-pixel host movement");
            Native.GetWindowRect(hwnd,out var hostMoved);
            Native.SetWindowPos(hwnd,0,hostMoved.Left,hostMoved.Top,hostMoved.Width+90,hostMoved.Height+40,0x14);
            await Task.Delay(400);Native.GetWindowRect(own,out var resized);
            Check(resized.Left-after.Left==90&&resized.Top-after.Top==40,"native fixture: bottom-right anchor follows resize");
            Check(companion.DiagnosticClick(),"panel opens through character click handler");
            Check(!companion.DiagnosticLock(),"lock command enables dragging");
            Check(companion.DiagnosticLock(),"lock command disables dragging");
            host.WindowState=WindowState.Minimized;await Task.Delay(400);
            Check(!companion.IsVisible,"native fixture: minimize hides companion");
            host.WindowState=WindowState.Normal;await Task.Delay(400);
            Check(companion.IsVisible,"native fixture: restore shows companion");
            host.Hide();await Task.Delay(400);Check(!companion.IsVisible,"native fixture: hidden host hides companion");
            host.Show();await Task.Delay(400);Check(companion.IsVisible,"native fixture: hidden host restoration shows companion");
            Check((Native.GetStyle(own,-20).ToInt64()&8)==0&&Native.GetWindow(own,4)==hwnd,"native fixture: owned window, not globally topmost");
            host.Close();await Task.Delay(400);Check(companion.DiagnosticClosed,"native fixture: owner closure closes companion");
        }
        finally {companion?.Close();host.Close();}
    }
    internal static RenderTargetBitmap Render(FrameworkElement element,int width,int height)
    {
        element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);return bitmap;
    }
    internal static void Save(BitmapSource bitmap,string file)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(Output,file));encoder.Save(stream);
    }
    public static void WriteFailure(Exception ex)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"report.json"),JsonSerializer.Serialize(new{Success=false,Checks,Error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
