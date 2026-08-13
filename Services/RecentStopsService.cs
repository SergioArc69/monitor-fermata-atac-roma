using System.IO;
using System.Text.Json;

namespace MonitorFermataAtacRoma.Services;

/// <summary>Keeps the last N monitored stop ids (most-recent-first), persisted to disk.</summary>
public sealed class RecentStopsService
{
    private const int MaxItems = 5;

    private readonly string _filePath;
    private List<string> _stopIds;

    public RecentStopsService()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorFermataAtacRoma");
        Directory.CreateDirectory(cacheDir);
        _filePath = Path.Combine(cacheDir, "recent_stops.json");
        _stopIds = Load();
    }

    public IReadOnlyList<string> StopIds => _stopIds;

    public void Touch(string stopId)
    {
        _stopIds.Remove(stopId);
        _stopIds.Insert(0, stopId);
        if (_stopIds.Count > MaxItems)
            _stopIds.RemoveRange(MaxItems, _stopIds.Count - MaxItems);
        Save();
    }

    private List<string> Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath)) ?? new List<string>();
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start fresh rather than blocking the app.
        }
        return new List<string>();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_stopIds));
        }
        catch (Exception)
        {
            // Best effort only; losing the MRU list isn't fatal.
        }
    }
}
