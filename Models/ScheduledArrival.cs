namespace MonitorFermataAtacRoma.Models;

public sealed record ScheduledArrival(string TripId, string RouteLabel, string Headsign, DateTime ArrivalTime);
