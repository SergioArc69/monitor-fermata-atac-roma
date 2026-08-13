using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MonitorFermataAtacRoma.Services;

public sealed record RouteInfo(string ShortName, string LongName);
public sealed record TripInfo(string RouteId, string Headsign);

/// <summary>
/// Loads and caches the static GTFS feed (routes/trips/stops) just to translate
/// the bare ids coming from GTFS-RT into human-readable line names and stop names.
/// stop_times.txt (huge, >150MB for Rome) is intentionally never parsed: GTFS-RT
/// already carries per-trip stop time updates, so it isn't needed.
/// </summary>
public sealed class GtfsStaticData
{
    private const string StaticFeedUrl = "https://romamobilita.it/sites/default/files/rome_static_gtfs.zip";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly string _cacheFilePath;
    private readonly HttpClient _httpClient;

    private Dictionary<string, RouteInfo> _routes = new();
    private Dictionary<string, TripInfo> _trips = new();
    private Dictionary<string, string> _stopNames = new();

    public GtfsStaticData(HttpClient httpClient)
    {
        _httpClient = httpClient;
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorFermataAtacRoma");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "rome_static_gtfs.zip");
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await EnsureCachedZipAsync(ct);

        using var zip = ZipFile.OpenRead(_cacheFilePath);

        _routes = ParseCsvEntry<RouteInfo>(zip, "routes.txt", fields => fields.TryGetValue("route_id", out var id)
            ? (id, new RouteInfo(fields.GetValueOrDefault("route_short_name", ""), fields.GetValueOrDefault("route_long_name", "")))
            : null);

        _trips = ParseCsvEntry<TripInfo>(zip, "trips.txt", fields => fields.TryGetValue("trip_id", out var id)
            ? (id, new TripInfo(fields.GetValueOrDefault("route_id", ""), fields.GetValueOrDefault("trip_headsign", "")))
            : null);

        _stopNames = ParseCsvEntry<string>(zip, "stops.txt", fields => fields.TryGetValue("stop_id", out var id)
            ? (id, fields.GetValueOrDefault("stop_name", ""))
            : null);
    }

    public bool TryGetStopName(string stopId, out string stopName) => _stopNames.TryGetValue(stopId, out stopName!);

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

    private async Task EnsureCachedZipAsync(CancellationToken ct)
    {
        var isStale = !File.Exists(_cacheFilePath) ||
                      DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) > CacheLifetime;
        if (!isStale) return;

        using var response = await _httpClient.GetAsync(StaticFeedUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var tempPath = _cacheFilePath + ".tmp";
        await using (var fileStream = File.Create(tempPath))
        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
        {
            await httpStream.CopyToAsync(fileStream, ct);
        }
        File.Move(tempPath, _cacheFilePath, overwrite: true);
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
