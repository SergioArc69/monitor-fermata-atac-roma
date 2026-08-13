using System.Net.Http;
using MonitorFermataAtacRoma.Models;
using TransitRealtime;

namespace MonitorFermataAtacRoma.Services;

public sealed class GtfsRealtimeService
{
    private const string TripUpdatesUrl = "https://romamobilita.it/sites/default/files/rome_rtgtfs_trip_updates_feed.pb";

    private readonly HttpClient _httpClient;
    private readonly GtfsStaticData _staticData;

    public GtfsRealtimeService(HttpClient httpClient, GtfsStaticData staticData)
    {
        _httpClient = httpClient;
        _staticData = staticData;
    }

    public async Task<IReadOnlyList<ArrivalInfo>> GetArrivalsForStopAsync(string stopId, CancellationToken ct = default)
    {
        var bytes = await _httpClient.GetByteArrayAsync(TripUpdatesUrl, ct);
        var feed = FeedMessage.Parser.ParseFrom(bytes);

        var arrivals = new List<ArrivalInfo>();

        foreach (var entity in feed.Entity)
        {
            if (entity.TripUpdate is null) continue;
            var tripUpdate = entity.TripUpdate;

            foreach (var stopTimeUpdate in tripUpdate.StopTimeUpdate)
            {
                if (stopTimeUpdate.StopId != stopId) continue;

                var stopTimeEvent = stopTimeUpdate.Arrival ?? stopTimeUpdate.Departure;
                if (stopTimeEvent is null || !stopTimeEvent.HasTime) continue;

                var arrivalTime = DateTimeOffset.FromUnixTimeSeconds(stopTimeEvent.Time).ToLocalTime().DateTime;
                var delaySeconds = stopTimeEvent.HasDelay ? stopTimeEvent.Delay : 0;

                var (routeLabel, headsign) = _staticData.DescribeTrip(tripUpdate.Trip.TripId, tripUpdate.Trip.RouteId);

                arrivals.Add(new ArrivalInfo
                {
                    TripId = tripUpdate.Trip.TripId,
                    RouteLabel = routeLabel,
                    Headsign = headsign,
                    ArrivalTime = arrivalTime,
                    DelaySeconds = delaySeconds,
                    IsRealtime = true,
                });
            }
        }

        return arrivals
            .Where(a => a.ArrivalTime > DateTime.Now.AddMinutes(-1))
            .OrderBy(a => a.ArrivalTime)
            .ToList();
    }
}
