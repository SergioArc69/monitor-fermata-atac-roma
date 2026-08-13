namespace MonitorFermataAtacRoma.Models;

public sealed record NearbyStop(string StopId, string StopName, double Lat, double Lon, double DistanceMeters);
