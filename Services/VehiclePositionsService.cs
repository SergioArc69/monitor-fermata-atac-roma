using System.Net.Http;
using TransitRealtime;
using AppVehiclePosition = MonitorFermataAtacRoma.Models.VehiclePosition;

namespace MonitorFermataAtacRoma.Services;

public sealed class VehiclePositionsService
{
    private const string VehiclePositionsUrl = "https://romamobilita.it/sites/default/files/rome_rtgtfs_vehicle_positions_feed.pb";

    private readonly HttpClient _httpClient;

    public VehiclePositionsService(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>Returns the live position of each vehicle currently running one of the given trips.</summary>
    public async Task<IReadOnlyList<AppVehiclePosition>> GetPositionsForTripsAsync(IReadOnlySet<string> tripIds, CancellationToken ct = default)
    {
        if (tripIds.Count == 0) return Array.Empty<AppVehiclePosition>();

        var bytes = await _httpClient.GetByteArrayAsync(VehiclePositionsUrl, ct);
        var feed = FeedMessage.Parser.ParseFrom(bytes);

        var positions = new List<AppVehiclePosition>();
        foreach (var entity in feed.Entity)
        {
            var vehicle = entity.Vehicle;
            if (vehicle?.Trip is null || vehicle.Position is null) continue;
            if (!tripIds.Contains(vehicle.Trip.TripId)) continue;

            var isStopped = vehicle.HasCurrentStatus && vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt;
            var vehicleLabel = string.IsNullOrEmpty(vehicle.Vehicle?.Label) ? vehicle.Vehicle?.Id ?? "" : vehicle.Vehicle.Label;

            positions.Add(new AppVehiclePosition(vehicle.Trip.TripId, vehicleLabel, vehicle.Position.Latitude, vehicle.Position.Longitude, isStopped));
        }

        return positions;
    }
}
