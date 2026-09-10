namespace MonitorFermataAtacRoma.Models;

/// <param name="CurrentStopId">Stop the vehicle is currently at/approaching (GTFS-RT stop_id), "" if not reported.</param>
public sealed record VehiclePosition(string TripId, string VehicleLabel, double Lat, double Lon, bool IsStopped, string CurrentStopId);
