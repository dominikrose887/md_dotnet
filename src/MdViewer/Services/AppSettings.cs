using System.IO;
using System.Text.Json;

namespace MdViewer.Services;

public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdViewer",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool IsDarkTheme { get; set; }
    public bool IsSidebarVisible { get; set; }
    public bool IsFocusMode { get; set; }
    public string? LastFolder { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public List<string> RecentFolders { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Ignore settings persistence failures
        }
    }

    public void AddRecentFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, fullPath);

        const int maxRecent = 10;
        if (RecentFiles.Count > maxRecent)
            RecentFiles.RemoveRange(maxRecent, RecentFiles.Count - maxRecent);
    }

    public void AddRecentFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        RecentFolders.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, fullPath);

        const int maxRecent = 8;
        if (RecentFolders.Count > maxRecent)
            RecentFolders.RemoveRange(maxRecent, RecentFolders.Count - maxRecent);

        LastFolder = fullPath;
    }
}
