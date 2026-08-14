using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MonitorFermataAtacRoma.Models;

namespace MonitorFermataAtacRoma.Services;

public sealed record RouteInfo(string ShortName, string LongName, int RouteType);
public sealed record TripInfo(string RouteId, string Headsign, string ServiceId);
public sealed record StopInfo(string Name, double Lat, double Lon);

/// <summary>
/// Loads and caches the static GTFS feed (routes/trips/stops) just to translate
/// the bare ids coming from GTFS-RT into human-readable line names and stop names.
/// stop_times.txt (huge, >150MB for Rome) is never fully loaded into memory: it's only streamed
/// on demand, and just for one stop at a time, as a scheduled-times fallback when GTFS-RT has
/// nothing to report (see <see cref="GetScheduledArrivals"/>).
/// </summary>
public sealed class GtfsStaticData
{
    private const string StaticFeedUrl = "https://romamobilita.it/sites/default/files/rome_static_gtfs.zip";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly string _cacheFilePath;
    private readonly string _lastModifiedFilePath;
    private readonly HttpClient _httpClient;

    private Dictionary<string, RouteInfo> _routes = new();
    private Dictionary<string, TripInfo> _trips = new();
    private Dictionary<string, StopInfo> _stops = new();
    private HashSet<(string ServiceId, int Date)> _activeServiceDates = new();
    private Dictionary<string, HashSet<int>> _stopRouteTypes = new();
    private Dictionary<string, List<StopSuggestion>> _routeStops = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<int, string> RouteTypeNames = new()
    {
        [0] = "Tram",
        [1] = "Metro",
        [2] = "Treno",
        [3] = "Bus",
        [4] = "Traghetto",
        [5] = "Tram a fune",
        [6] = "Funivia",
        [7] = "Funicolare",
    };

    public GtfsStaticData(HttpClient httpClient)
    {
        _httpClient = httpClient;
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorFermataAtacRoma");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "rome_static_gtfs.zip");
        _lastModifiedFilePath = Path.Combine(cacheDir, "rome_static_gtfs.lastmodified");
    }

    public async Task LoadAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        await EnsureCachedZipAsync(forceRefresh, ct);

        using var zip = ZipFile.OpenRead(_cacheFilePath);

        _routes = ParseCsvEntry<RouteInfo>(zip, "routes.txt", fields => fields.TryGetValue("route_id", out var id)
            ? (id, new RouteInfo(
                fields.GetValueOrDefault("route_short_name", ""),
                fields.GetValueOrDefault("route_long_name", ""),
                int.TryParse(fields.GetValueOrDefault("route_type", ""), out var routeType) ? routeType : -1))
            : null);

        _trips = ParseCsvEntry<TripInfo>(zip, "trips.txt", fields => fields.TryGetValue("trip_id", out var id)
            ? (id, new TripInfo(
                fields.GetValueOrDefault("route_id", ""),
                fields.GetValueOrDefault("trip_headsign", ""),
                fields.GetValueOrDefault("service_id", "")))
            : null);

        _stops = ParseCsvEntry<StopInfo>(zip, "stops.txt", fields => fields.TryGetValue("stop_id", out var id)
            ? (id, new StopInfo(
                fields.GetValueOrDefault("stop_name", ""),
                ParseCoordinate(fields.GetValueOrDefault("stop_lat", "")),
                ParseCoordinate(fields.GetValueOrDefault("stop_lon", ""))))
            : null);

        _activeServiceDates = ParseActiveServiceDates(zip);
        // Stale after a refresh; rebuilt by BuildStopIndexes.
        _stopRouteTypes = new Dictionary<string, HashSet<int>>();
        _routeStops = new Dictionary<string, List<StopSuggestion>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans stop_times.txt once to learn, for every stop, which transport modes serve it
    /// (bus/metro/tram/...) and, for every line, which stops it visits (in route order) — two small
    /// aggregated indexes built from a single pass, rather than keeping the whole file loaded. Meant to
    /// be kicked off in the background: it's a multi-second full scan, and optional — <see cref="TryGetStopModes"/>
    /// and <see cref="GetStopsForRoute"/> simply return nothing until this has completed.
    /// </summary>
    public void BuildStopIndexes()
    {
        if (!File.Exists(_cacheFilePath)) return;

        using var zip = ZipFile.OpenRead(_cacheFilePath);
        var entry = zip.GetEntry("stop_times.txt");
        if (entry is null) return;

        using var reader = new StreamReader(entry.Open());
        var headerLine = reader.ReadLine();
        if (headerLine is null) return;

        var headers = ParseCsvLine(headerLine);
        var tripIdx = headers.IndexOf("trip_id");
        var stopIdx = headers.IndexOf("stop_id");
        var seqIdx = headers.IndexOf("stop_sequence");
        if (tripIdx < 0 || stopIdx < 0) return;

        var modesIndex = new Dictionary<string, HashSet<int>>();
        // routeLabel -> stopId -> lowest stop_sequence seen, so the final list roughly follows the
        // physical order of the route instead of being alphabetical.
        var routeStopSequence = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            var fields = ParseCsvLine(line);
            var maxIdx = Math.Max(tripIdx, stopIdx);
            if (fields.Count <= maxIdx) continue;

            if (!_trips.TryGetValue(fields[tripIdx], out var trip)) continue;
            if (!_routes.TryGetValue(trip.RouteId, out var route) || route.RouteType < 0) continue;

            var stopId = fields[stopIdx];

            if (!modesIndex.TryGetValue(stopId, out var types))
            {
                types = new HashSet<int>();
                modesIndex[stopId] = types;
            }
            types.Add(route.RouteType);

            var routeLabel = ResolveRouteLabel(trip.RouteId);
            if (!routeStopSequence.TryGetValue(routeLabel, out var stopsForRoute))
            {
                stopsForRoute = new Dictionary<string, int>();
                routeStopSequence[routeLabel] = stopsForRoute;
            }

            var sequence = seqIdx >= 0 && fields.Count > seqIdx && int.TryParse(fields[seqIdx], out var seq) ? seq : int.MaxValue;
            if (!stopsForRoute.TryGetValue(stopId, out var existingSequence) || sequence < existingSequence)
                stopsForRoute[stopId] = sequence;
        }

        _stopRouteTypes = modesIndex;
        _routeStops = new Dictionary<string, List<StopSuggestion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (routeLabel, stopsForRoute) in routeStopSequence)
        {
            _routeStops[routeLabel] = stopsForRoute
                .OrderBy(kv => kv.Value)
                .Select(kv => new StopSuggestion(kv.Key, _stops.TryGetValue(kv.Key, out var stop) ? stop.Name : ""))
                .ToList();
        }
    }

    /// <summary>Human-readable transport mode(s) for a stop (e.g. "Bus", "Metro/Bus"), once <see cref="BuildStopIndexes"/> has run.</summary>
    public bool TryGetStopModes(string stopId, out string modesLabel)
    {
        if (_stopRouteTypes.TryGetValue(stopId, out var types) && types.Count > 0)
        {
            modesLabel = string.Join("/", types
                .OrderBy(t => t)
                .Select(t => RouteTypeNames.GetValueOrDefault(t, "Altro"))
                .Distinct());
            return true;
        }
        modesLabel = "";
        return false;
    }

    /// <summary>The stops served by a line (route order), once <see cref="BuildStopIndexes"/> has run. Empty if the line is unknown or the index isn't ready yet.</summary>
    public IReadOnlyList<StopSuggestion> GetStopsForRoute(string routeLabel) =>
        _routeStops.TryGetValue(routeLabel, out var stops) ? stops : Array.Empty<StopSuggestion>();

    public bool TryGetStopName(string stopId, out string stopName)
    {
        if (_stops.TryGetValue(stopId, out var stop))
        {
            stopName = stop.Name;
            return true;
        }
        stopName = "";
        return false;
    }

    public bool TryGetStopLocation(string stopId, out double lat, out double lon)
    {
        if (_stops.TryGetValue(stopId, out var stop) && (stop.Lat != 0 || stop.Lon != 0))
        {
            lat = stop.Lat;
            lon = stop.Lon;
            return true;
        }
        lat = 0;
        lon = 0;
        return false;
    }

    /// <summary>
    /// Matches the query against both the stop code and the stop name: codes aren't always numeric
    /// (e.g. metro/rail interchanges like "BP16" for Tiburtina FS), so a name-only search would miss them.
    /// </summary>
    /// <summary>
    /// An exact (case-insensitive) match against a known line number takes priority and returns that
    /// line's stops in route order — e.g. typing "53" lists every stop line 53 serves. Otherwise falls
    /// back to the regular code-or-name substring search.
    /// </summary>
    public IReadOnlyList<StopSuggestion> SearchStops(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<StopSuggestion>();

        if (_routeStops.TryGetValue(query, out var routeStops) && routeStops.Count > 0)
            return routeStops.Take(maxResults).ToList();

        return _stops
            .Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         kv.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(kv => new StopSuggestion(kv.Key, kv.Value.Name))
            .ToList();
    }

    public IReadOnlyList<NearbyStop> GetNearbyStops(double lat, double lon, int maxResults)
    {
        return _stops
            .Where(kv => kv.Value.Lat != 0 || kv.Value.Lon != 0)
            .Select(kv => new NearbyStop(kv.Key, kv.Value.Name, kv.Value.Lat, kv.Value.Lon,
                HaversineMeters(lat, lon, kv.Value.Lat, kv.Value.Lon)))
            .OrderBy(s => s.DistanceMeters)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// All stops within the given map viewport, capped at <paramref name="maxResults"/> (closest to the
    /// viewport center first) so zooming out over a wide area doesn't dump thousands of markers on the map.
    /// </summary>
    public IReadOnlyList<NearbyStop> GetStopsInBounds(double north, double south, double east, double west, int maxResults)
    {
        var centerLat = (north + south) / 2;
        var centerLon = (east + west) / 2;

        return _stops
            .Where(kv => (kv.Value.Lat != 0 || kv.Value.Lon != 0) &&
                         kv.Value.Lat <= north && kv.Value.Lat >= south &&
                         kv.Value.Lon <= east && kv.Value.Lon >= west)
            .Select(kv => new NearbyStop(kv.Key, kv.Value.Name, kv.Value.Lat, kv.Value.Lon,
                HaversineMeters(centerLat, centerLon, kv.Value.Lat, kv.Value.Lon)))
            .OrderBy(s => s.DistanceMeters)
            .Take(maxResults)
            .ToList();
    }

    private static double ParseCoordinate(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6371000;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public (string RouteLabel, string Headsign) DescribeTrip(string tripId, string fallbackRouteId)
    {
        if (_trips.TryGetValue(tripId, out var trip))
        {
            var routeId = string.IsNullOrEmpty(trip.RouteId) ? fallbackRouteId : trip.RouteId;
            return (ResolveRouteLabel(routeId), trip.Headsign);
        }

        return (ResolveRouteLabel(fallbackRouteId), "");
    }

    private string ResolveRouteLabel(string routeId) =>
        _routes.TryGetValue(routeId, out var route)
            ? (string.IsNullOrEmpty(route.ShortName) ? route.LongName : route.ShortName)
            : routeId;

    /// <summary>
    /// Falls back to the static timetable when GTFS-RT has nothing to say for this stop (e.g. quiet
    /// periods, or gaps in realtime coverage). Streams stop_times.txt (150+MB) directly from the cached
    /// zip rather than keeping it indexed in memory, since this only runs occasionally and on-demand for
    /// a single stop — a full scan takes a couple of seconds, which is fine for a rare fallback.
    /// </summary>
    public IReadOnlyList<ScheduledArrival> GetScheduledArrivals(string stopId, int maxResults)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var todayKey = today.Year * 10000 + today.Month * 100 + today.Day;

        if (!File.Exists(_cacheFilePath)) return Array.Empty<ScheduledArrival>();

        using var zip = ZipFile.OpenRead(_cacheFilePath);
        var entry = zip.GetEntry("stop_times.txt");
        if (entry is null) return Array.Empty<ScheduledArrival>();

        using var reader = new StreamReader(entry.Open());
        var headerLine = reader.ReadLine();
        if (headerLine is null) return Array.Empty<ScheduledArrival>();

        var headers = ParseCsvLine(headerLine);
        var tripIdx = headers.IndexOf("trip_id");
        var stopIdx = headers.IndexOf("stop_id");
        var arrivalIdx = headers.IndexOf("arrival_time");
        if (tripIdx < 0 || stopIdx < 0 || arrivalIdx < 0) return Array.Empty<ScheduledArrival>();

        var results = new List<ScheduledArrival>();

        while (reader.ReadLine() is { } line)
        {
            // Cheap pre-check before paying for a full CSV split: most rows are for other stops.
            if (!line.Contains(stopId, StringComparison.Ordinal)) continue;

            var fields = ParseCsvLine(line);
            var maxIdx = Math.Max(tripIdx, Math.Max(stopIdx, arrivalIdx));
            if (fields.Count <= maxIdx || fields[stopIdx] != stopId) continue;

            var tripId = fields[tripIdx];
            if (!_trips.TryGetValue(tripId, out var trip)) continue;
            if (!_activeServiceDates.Contains((trip.ServiceId, todayKey))) continue;

            if (!TryParseGtfsTimeOfDay(fields[arrivalIdx], out var timeOfDay)) continue;
            var arrivalTime = now.Date.Add(timeOfDay); // GTFS allows hours >= 24 for past-midnight trips
            if (arrivalTime < now.AddMinutes(-1)) continue;

            var (routeLabel, headsign) = DescribeTrip(tripId, trip.RouteId);
            results.Add(new ScheduledArrival(tripId, routeLabel, headsign, arrivalTime));
        }

        return results.OrderBy(r => r.ArrivalTime).Take(maxResults).ToList();
    }

    private static bool TryParseGtfsTimeOfDay(string value, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.Zero;
        var parts = value.Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var s))
            return false;
        timeOfDay = new TimeSpan(h, m, s);
        return true;
    }

    private static HashSet<(string ServiceId, int Date)> ParseActiveServiceDates(ZipArchive zip)
    {
        var result = new HashSet<(string, int)>();
        var entry = zip.GetEntry("calendar_dates.txt");
        if (entry is null) return result;

        using var reader = new StreamReader(entry.Open());
        var headerLine = reader.ReadLine();
        if (headerLine is null) return result;

        var headers = ParseCsvLine(headerLine);
        var serviceIdx = headers.IndexOf("service_id");
        var dateIdx = headers.IndexOf("date");
        var exceptionIdx = headers.IndexOf("exception_type");
        if (serviceIdx < 0 || dateIdx < 0) return result;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var fields = ParseCsvLine(line);
            var maxIdx = Math.Max(serviceIdx, dateIdx);
            if (fields.Count <= maxIdx) continue;

            // exception_type: 1 = service added on this date, 2 = removed. Rome's feed only ever adds
            // (there's no base calendar.txt weekly pattern to subtract from), but honor it if present.
            if (exceptionIdx >= 0 && fields.Count > exceptionIdx && fields[exceptionIdx] == "2") continue;
            if (!int.TryParse(fields[dateIdx], out var date)) continue;

            result.Add((fields[serviceIdx], date));
        }

        return result;
    }

    private async Task EnsureCachedZipAsync(bool forceRefresh, CancellationToken ct)
    {
        var cacheExists = File.Exists(_cacheFilePath);
        var isExpired = !cacheExists || DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) > CacheLifetime;
        if (!forceRefresh && !isExpired) return;

        // Ask the server whether the file actually changed before downloading 35+MB again: if the
        // agency hasn't republished the feed, this costs a near-empty round trip instead of a full
        // re-download, both on the normal 7-day cache expiry and on a manual "aggiorna" click.
        using var request = new HttpRequestMessage(HttpMethod.Get, StaticFeedUrl);
        if (cacheExists && TryReadLastModified() is { } lastModified)
            request.Headers.IfModifiedSince = lastModified;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            File.SetLastWriteTimeUtc(_cacheFilePath, DateTime.UtcNow); // reset the freshness clock
            return;
        }

        response.EnsureSuccessStatusCode();

        var tempPath = _cacheFilePath + ".tmp";
        await using (var fileStream = File.Create(tempPath))
        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
        {
            await httpStream.CopyToAsync(fileStream, ct);
        }
        File.Move(tempPath, _cacheFilePath, overwrite: true);

        if (response.Content.Headers.LastModified is { } newLastModified)
            File.WriteAllText(_lastModifiedFilePath, newLastModified.ToString("R"));
        else if (File.Exists(_lastModifiedFilePath))
            File.Delete(_lastModifiedFilePath);
    }

    private DateTimeOffset? TryReadLastModified()
    {
        try
        {
            if (File.Exists(_lastModifiedFilePath) &&
                DateTimeOffset.TryParse(File.ReadAllText(_lastModifiedFilePath), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var value))
                return value;
        }
        catch (Exception)
        {
            // Missing/corrupt metadata just means we fall back to an unconditional request.
        }
        return null;
    }

    private static Dictionary<string, T> ParseCsvEntry<T>(ZipArchive zip, string entryName, Func<Dictionary<string, string>, (string Key, T Value)?> select)
        where T : notnull
    {
        var result = new Dictionary<string, T>();
        var entry = zip.GetEntry(entryName) ?? throw new FileNotFoundException($"'{entryName}' non trovato nel GTFS statico.");

        using var reader = new StreamReader(entry.Open());
        var headerLine = reader.ReadLine();
        if (headerLine is null) return result;
        var headers = ParseCsvLine(headerLine);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var values = ParseCsvLine(line);
            var fields = new Dictionary<string, string>(headers.Count);
            for (var i = 0; i < headers.Count && i < values.Count; i++)
                fields[headers[i]] = values[i];

            var mapped = select(fields);
            if (mapped is { } kv) result[kv.Key] = kv.Value;
        }

        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
