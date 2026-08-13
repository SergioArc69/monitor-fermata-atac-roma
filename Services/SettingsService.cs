using System.IO;
using System.Text.Json;
using MonitorFermataAtacRoma.Models;

namespace MonitorFermataAtacRoma.Services;

/// <summary>Persists monitoring options across app restarts (last stop, notification prefs, line filter).</summary>
public sealed class SettingsService
{
    private readonly string _filePath;

    public SettingsService()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorFermataAtacRoma");
        Directory.CreateDirectory(cacheDir);
        _filePath = Path.Combine(cacheDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: fall back to defaults rather than blocking the app.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception)
        {
            // Best effort only; losing the saved session isn't fatal.
        }
    }
}
