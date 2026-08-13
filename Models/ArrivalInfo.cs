namespace MonitorFermataAtacRoma.Models;

public sealed class ArrivalInfo
{
    public required string TripId { get; init; }
    public required string RouteLabel { get; init; }
    public required string Headsign { get; init; }
    public required DateTime ArrivalTime { get; init; }
    public int DelaySeconds { get; init; }
    public bool IsRealtime { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Headsign) ? $"Linea {RouteLabel}" : $"Linea {RouteLabel} → {Headsign}";
    public string DestinationLabel => string.IsNullOrEmpty(Headsign) ? "—" : Headsign;

    public TimeSpan TimeUntilArrival => ArrivalTime - DateTime.Now;

    public string MinutesLabel
    {
        get
        {
            var minutes = (int)Math.Round(TimeUntilArrival.TotalMinutes);
            if (minutes <= 0) return "in arrivo";
            if (minutes == 1) return "1 min";
            return $"{minutes} min";
        }
    }

    public string DelayLabel
    {
        get
        {
            if (!IsRealtime) return "orario";
            if (DelaySeconds == 0) return "in orario";
            var minutes = (int)Math.Round(DelaySeconds / 60.0);
            return minutes > 0 ? $"+{minutes} min" : $"{minutes} min";
        }
    }
}
