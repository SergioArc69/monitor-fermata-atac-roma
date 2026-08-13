namespace MonitorFermataAtacRoma.Models;

public sealed class AppSettings
{
    public bool WasMonitoring { get; set; }
    public string? StopId { get; set; }
    public bool NotifyEnabled { get; set; } = true;
    public int NotifyMinutes { get; set; } = 5;
    public string NotifyFrom { get; set; } = "00:00";
    public string NotifyTo { get; set; } = "23:59";
    public bool LineFilterEnabled { get; set; }
    public List<string> SelectedLines { get; set; } = new();
}
