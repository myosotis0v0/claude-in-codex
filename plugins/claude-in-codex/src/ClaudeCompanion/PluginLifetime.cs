using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace ClaudeCompanion;

internal sealed class PluginLifetime
{
    private bool wasRegistered;
    private int absentReads;
    internal static bool? ParseEnabled(string text)
    {
        var config=TomlSerializer.Deserialize<TomlTable>(text);
        if(config?.TryGetValue("plugins",out var pluginsValue)!=true||pluginsValue is not TomlTable plugins)return null;
        if(!plugins.TryGetValue("claude-in-codex@claude-in-codex-local",out var entryValue)||entryValue is not TomlTable entry)return null;
        return entry.TryGetValue("enabled",out var value)&&value is bool enabled?enabled:null;
    }
    internal bool ShouldExit()
    {
        var home=Environment.GetEnvironmentVariable("CODEX_HOME");
        if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        try
        {
            var path=Path.Combine(home,"config.toml");
            if(!File.Exists(path))return false;
            var enabled=ParseEnabled(File.ReadAllText(path));
            if(enabled is {} state){wasRegistered=true;absentReads=0;return !state;}
            return wasRegistered&&++absentReads>=2;
        }
        catch {return false;}
    }
}
