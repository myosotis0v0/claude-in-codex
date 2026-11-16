using System.IO;
using System.Text.Json;

namespace ClaudeCompanion;

public sealed class Settings
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PersistenceEnabled {get;init;}=true;
    public bool Locked { get; set; } = true;
    public double Size { get; set; } = 224;
    public double OffsetX { get; set; } = 12;
    public double OffsetY { get; set; } = 12;
    public string Theme { get; set; } = "system";
    public bool ReducedMotion { get; set; }
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeInCodex");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DirectoryPath,"settings.json"))) ?? new();
            settings.Size = Math.Clamp(double.IsFinite(settings.Size) ? settings.Size : 224, 128, 360);
            if (!double.IsFinite(settings.OffsetX)) settings.OffsetX = 12;
            if (!double.IsFinite(settings.OffsetY)) settings.OffsetY = 12;
            if (settings.Theme is not ("system" or "light" or "dark")) settings.Theme = "system";
            return settings;
        }
        catch { return new(); }
    }
    public void Save()
    {
        if(!PersistenceEnabled)return;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath,"settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) { Log("Settings save: " + ex.GetType().Name); }
    }
    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath,"companion.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Move(path,path+".previous",true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}\n");
        }
        catch { }
    }
}
