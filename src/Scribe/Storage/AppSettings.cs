using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scribe.Storage;

/// <summary>
/// Small user preferences file. Kept in AppData rather than in the notebook
/// folder so that syncing notebooks between machines does not drag one
/// machine's window layout along with them.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("workspaceRoot")]
    public string? WorkspaceRoot { get; set; }

    [JsonPropertyName("lastPageFile")]
    public string? LastPageFile { get; set; }

    [JsonPropertyName("touchDraws")]
    public bool TouchDraws { get; set; }

    /// <summary>Dark is the default; writing at night is the common case.</summary>
    [JsonPropertyName("darkMode")]
    public bool DarkMode { get; set; } = true;

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Scribe", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (s is not null) return s;
            }
        }
        catch
        {
            // Corrupt settings should never stop the app from opening.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing preferences is not worth interrupting the user over.
        }
    }
}
