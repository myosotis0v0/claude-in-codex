using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace ClaudeCompanion;

public sealed class ThemeManager : IDisposable
{
    private static readonly string[] BrushKeys=["PanelBrush","TextBrush","MutedBrush","BorderBrush","SubtleBrush","HoverBrush","AccentBrush","AccentFillBrush","AccentSoftBrush","ThumbBrush","TrackBrush"];
    public string Mode { get; private set; } = "system";
    public event Action? Applied;
    public ThemeManager() => SystemEvents.UserPreferenceChanged += Changed;
    private void Changed(object sender, UserPreferenceChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => Apply(Mode));
    public void Apply(string mode)
    {
        Mode = mode;
        var light = mode == "light";
        if (mode == "system")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); light = (int)(key?.GetValue("AppsUseLightTheme") ?? 1) != 0; }
            catch { light = false; }
        }
        string[] colors = light
            ? ["#FFFFFF","#252527","#68686F","#E2E2E6","#F4F4F6","#EAEAEE","#BD5108","#F58A32","#FFF0E3","#FFFFFF","#D8D8DD"]
            : ["#202022","#F4F4F5","#B1B1B8","#424247","#2B2B2F","#36363B","#FF9B4A","#F58A32","#3B2B20","#FFFFFF","#49494F"];
        for (var i=0;i<BrushKeys.Length;i++)
        {
            var color=(Color)ColorConverter.ConvertFromString(colors[i]);
            // Mutate shared brushes so a detached or open Popup updates without rebuilding its tree.
            if(Application.Current.Resources[BrushKeys[i]] is SolidColorBrush brush&&!brush.IsFrozen)brush.Color=color;
            else Application.Current.Resources[BrushKeys[i]]=new SolidColorBrush(color);
        }
        Applied?.Invoke();
    }
    internal static void ApplyScope(FrameworkElement scope)
    {
        // Popup content is a separate resource tree, including while closed or rendered offscreen.
        foreach(var key in BrushKeys)scope.Resources[key]=Application.Current.Resources[key];
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= Changed;
}
