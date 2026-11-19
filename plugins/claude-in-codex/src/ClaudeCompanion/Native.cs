using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeCompanion;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public int Width=>Right-Left; public int Height=>Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    internal delegate bool EnumProc(nint hwnd,nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback,nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd,ref Point point);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern uint GetMessagePos();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetStyle(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern nint SetStyle(nint hwnd,int index,nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
    internal static bool Cloaked(nint hwnd) => DwmGetWindowAttribute(hwnd,14,out var value,4)==0 && value!=0;
    internal static bool IsCodex(Process process)
    {
        try
        {
            var path=process.MainModule?.FileName ?? "";
            return (path.Contains(@"\WindowsApps\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase) && path.EndsWith(@"\app\ChatGPT.exe",StringComparison.OrdinalIgnoreCase))
                || (path.Contains(@"\OpenAI\Codex\",StringComparison.OrdinalIgnoreCase) && (path.EndsWith("Codex.exe",StringComparison.OrdinalIgnoreCase)||path.EndsWith("ChatGPT.exe",StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }
    internal static HashSet<int> CodexPids()
    {
        var result=new HashSet<int>();
        foreach(var name in new[]{"ChatGPT","Codex"}) foreach(var p in Process.GetProcessesByName(name))
            using(p) { if(IsCodex(p)) result.Add(p.Id); }
        return result;
    }
    internal static nint FindWindow(HashSet<int> pids)
    {
        nint best=0; long area=0;
        var foreground=GetForegroundWindow();
        EnumWindows((hwnd,_)=>
        {
            GetWindowThreadProcessId(hwnd,out var pid);
            if(!pids.Contains((int)pid)||GetWindow(hwnd,4)!=0||!GetClientRect(hwnd,out var rect)||rect.Width<400||rect.Height<250) return true;
            if(IsWindowVisible(hwnd)&&!Cloaked(hwnd))
            {
                if(hwnd==foreground) { best=hwnd; return false; }
                var a=(long)rect.Width*rect.Height;
                if(a>area){area=a;best=hwnd;}
            }
            return true;
        },0);
        return best;
    }
    internal static (int X,int Y,int Size) Place(Rect client,int originX,int originY,double dpi,Settings settings)
    {
        var size=Math.Max(1,(int)Math.Round(Math.Min(settings.Size*dpi,Math.Min(client.Width,client.Height))));
        var x=originX+client.Width-size-(int)Math.Round(settings.OffsetX*dpi);
        var y=originY+client.Height-size-(int)Math.Round(settings.OffsetY*dpi);
        return (Math.Clamp(x,originX,originX+Math.Max(0,client.Width-size)),Math.Clamp(y,originY,originY+Math.Max(0,client.Height-size)),size);
    }
}
