namespace MonitorFermataAtacRoma.Models;

public sealed record VehiclePosition(string TripId, string VehicleLabel, double Lat, double Lon, bool IsStopped);
