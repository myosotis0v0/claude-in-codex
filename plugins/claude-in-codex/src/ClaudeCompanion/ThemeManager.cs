using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace ClaudeCompanion;

public sealed class ThemeManager : IDisposable
{
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
        string[] keys = ["PanelBrush","TextBrush","MutedBrush","BorderBrush","SubtleBrush"];
        string[] colors = light ? ["#FFFFFF","#202124","#65666D","#DBDCE0","#F3F4F5"] : ["#202124","#F4F4F5","#ABADB3","#49494E","#2D2E32"];
        for (var i=0;i<keys.Length;i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Applied?.Invoke();
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= Changed;
}
