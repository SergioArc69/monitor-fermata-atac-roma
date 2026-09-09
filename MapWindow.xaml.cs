using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MonitorFermataAtacRoma.Models;
using MonitorFermataAtacRoma.Services;
using Geo = Windows.Devices.Geolocation;

namespace MonitorFermataAtacRoma;

public partial class MapWindow : Window
{
    private static readonly TimeSpan BusRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly GtfsStaticData _staticData;
    private readonly GtfsRealtimeService? _realtimeService;
    private readonly VehiclePositionsService? _vehiclePositions;
    private string? _monitoredStopId;
    private readonly DispatcherTimer? _busRefreshTimer;

    // The upstream GTFS-RT feed drops a stop's stop_time_update entry almost as soon as the bus
    // passes it — it does NOT keep reporting it for minutes afterwards. So to keep recently-passed
    // buses on the map for RecentlyPassedLookbackMinutes, we have to remember their last-seen
    // arrival ourselves rather than expecting the feed to still have it on a later poll.
    private readonly Dictionary<string, ArrivalInfo> _recentArrivalsByTripId = new();

    public string? SelectedStopId { get; private set; }

    /// <summary>True if the user used the "Ferma monitoraggio" button while this dialog was open —
    /// the caller should stop monitoring in the main window too, not just switch this map to browse-mode.</summary>
    public bool MonitoringWasStopped { get; private set; }

    /// <param name="monitoredStopId">
    /// Null: browse-mode, shows stops near the current location and lets the user pick one.
    /// Non-null: monitor-mode, centers on this stop and overlays live bus positions, re-fetched from
    /// <paramref name="realtimeService"/> on every refresh so buses that have already passed the stop
    /// drop off the map instead of lingering with a stale ETA.
    /// </param>
    public MapWindow(GtfsStaticData staticData, GtfsRealtimeService? realtimeService,
        VehiclePositionsService? vehiclePositions, string? monitoredStopId)
    {
        InitializeComponent();

        _staticData = staticData;
        _realtimeService = realtimeService;
        _vehiclePositions = vehiclePositions;
        _monitoredStopId = monitoredStopId;

        if (_monitoredStopId is not null)
        {
            _busRefreshTimer = new DispatcherTimer { Interval = BusRefreshInterval };
            _busRefreshTimer.Tick += async (_, _) => await RefreshBusPositionsAsync();
        }

        Loaded += MapWindow_Loaded;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MapWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _busRefreshTimer?.Stop();

    private async void StopMonitoringButton_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoringButton.IsEnabled = false;
        try
        {
            _busRefreshTimer?.Stop();
            _monitoredStopId = null;
            MonitoringWasStopped = true;
            BusStatusList.ItemsSource = Array.Empty<string>();

            await ExecuteScriptAsync("clearBusMarkers(); clearStopMarkers();");
            StopMonitoringButton.Visibility = Visibility.Collapsed;

            await ShowNearbyStopsAsync();
        }
        finally
        {
            StopMonitoringButton.IsEnabled = true;
        }
    }

    private async void MapWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // async void: any unhandled exception here would crash the whole app, not just this dialog
        // (this is exactly how a missing WebView2Loader.dll took the app down). Never let that happen.
        try
        {
            // WebView2 defaults to a user data folder next to the exe. When installed under
            // Program Files, a non-admin user can't write there (E_ACCESSDENIED) — point it at
            // %LOCALAPPDATA% instead, alongside the app's other cached data.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MonitorFermataAtacRoma", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await MapWebView.EnsureCoreWebView2Async(environment);
            MapWebView.CoreWebView2.WebMessageReceived += async (_, args) => await OnWebMessageReceivedAsync(args);

            var navigationCompleted = new TaskCompletionSource();
            void OnNavigationCompleted(object? s, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs a) =>
                navigationCompleted.TrySetResult();
            MapWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            MapWebView.NavigateToString(MapHtml);
            await navigationCompleted.Task;
            MapWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

            if (_monitoredStopId is not null && _staticData.TryGetStopLocation(_monitoredStopId, out var lat, out var lon))
            {
                _staticData.TryGetStopName(_monitoredStopId, out var stopName);
                InstructionTextBlock.Text = "Fermata monitorata, con la posizione dei bus in transito (aggiornata ogni 15 secondi).";
                StopMonitoringButton.Visibility = Visibility.Visible;

                await ExecuteScriptAsync($"initMap({Fmt(lat)}, {Fmt(lon)}, 16);");
                await ExecuteScriptAsync($"addStopMarker('{_monitoredStopId}', {Fmt(lat)}, {Fmt(lon)}, {JsString(BuildStopTooltip(_monitoredStopId, stopName))}, false);");
                await RefreshBusPositionsAsync();
                _busRefreshTimer?.Start();
            }
            else
            {
                await ShowNearbyStopsAsync();
            }
        }
        catch (Exception ex)
        {
            InstructionTextBlock.Text =
                "Impossibile caricare la mappa: verifica che Microsoft Edge WebView2 Runtime sia installato " +
                "(https://developer.microsoft.com/microsoft-edge/webview2/).\n" +
                $"Dettaglio: {ex.Message}";
        }
    }

    private static readonly (double Lat, double Lon) RomeCenter = (41.9028, 12.4964);
    private const double MaxDistanceFromRomeCenterKm = 50;

    private async Task ShowNearbyStopsAsync()
    {
        var location = await GetCurrentLocationAsync();
        if (location is not null && DistanceKm(location.Value.Lat, location.Value.Lon, RomeCenter.Lat, RomeCenter.Lon) > MaxDistanceFromRomeCenterKm)
        {
            // Too far from the area served by Roma Mobilità: there are certainly no stops to show there.
            location = null;
        }
        var (lat, lon) = location ?? RomeCenter;

        InstructionTextBlock.Text = location is not null
            ? "Fermate vicino alla tua posizione: clicca su una fermata per selezionarla, oppure sposta o zooma la mappa per cercarne altre."
            : "Posizione non disponibile: mostro le fermate del centro di Roma. Clicca su una fermata per selezionarla, oppure sposta o zooma la mappa per cercarne altre.";

        await ExecuteScriptAsync($"initMap({Fmt(lat)}, {Fmt(lon)}, 16);");
        await ExecuteScriptAsync($"addMeMarker({Fmt(lat)}, {Fmt(lon)});");
        await ExecuteScriptAsync("enableViewportStopSearch();");
        await ExecuteScriptAsync("notifyViewportChanged();"); // triggers the first stop search, via the same path as pan/zoom
    }

    private const int MaxVisibleStops = 150;

    /// <summary>Replaces the selectable stop markers with the ones inside the current map viewport.</summary>
    private async Task RefreshStopsInViewportAsync(double north, double south, double east, double west)
    {
        await ExecuteScriptAsync("clearStopMarkers();");
        foreach (var stop in _staticData.GetStopsInBounds(north, south, east, west, MaxVisibleStops))
            await ExecuteScriptAsync(
                $"addStopMarker('{stop.StopId}', {Fmt(stop.Lat)}, {Fmt(stop.Lon)}, {JsString(BuildStopTooltip(stop.StopId, stop.StopName))}, true);");
    }

    /// <summary>"&lt;b&gt;code&lt;/b&gt; — name" plus the transport mode(s) on a second line, once known.</summary>
    private string BuildStopTooltip(string stopId, string stopName)
    {
        var tooltip = $"<b>{System.Net.WebUtility.HtmlEncode(stopId)}</b> — {System.Net.WebUtility.HtmlEncode(stopName)}";
        if (_staticData.TryGetStopModes(stopId, out var modes))
            tooltip += $"<br><i>{System.Net.WebUtility.HtmlEncode(modes)}</i>";
        return tooltip;
    }

    private async Task OnWebMessageReceivedAsync(CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (json is null) return;

            using var message = JsonDocument.Parse(json);
            var type = message.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "select":
                    SelectedStopId = message.RootElement.GetProperty("stopId").GetString();
                    DialogResult = true;
                    break;

                case "viewportChanged":
                    var north = message.RootElement.GetProperty("north").GetDouble();
                    var south = message.RootElement.GetProperty("south").GetDouble();
                    var east = message.RootElement.GetProperty("east").GetDouble();
                    var west = message.RootElement.GetProperty("west").GetDouble();
                    await RefreshStopsInViewportAsync(north, south, east, west);
                    break;
            }
        }
        catch (Exception)
        {
            // Malformed/unexpected message from the page: not worth surfacing to the user.
        }
    }

    /// <summary>Buses that already passed the stop stay visible on the map, in blue, for this long afterwards.</summary>
    private const int RecentlyPassedLookbackMinutes = 10;

    private async Task RefreshBusPositionsAsync()
    {
        if (_vehiclePositions is null || _realtimeService is null || _monitoredStopId is null) return;

        try
        {
            // Re-fetch live arrivals every tick (rather than reusing a snapshot from when the dialog
            // opened) and merge them into our own short-term memory, since the feed itself drops a
            // stop's entry almost as soon as the bus passes it — see _recentArrivalsByTripId.
            var freshArrivals = await _realtimeService.GetArrivalsForStopAsync(_monitoredStopId);
            foreach (var arrival in freshArrivals)
                _recentArrivalsByTripId[arrival.TripId] = arrival;

            var cutoff = DateTime.Now.AddMinutes(-RecentlyPassedLookbackMinutes);
            foreach (var tripId in _recentArrivalsByTripId.Where(kv => kv.Value.ArrivalTime < cutoff).Select(kv => kv.Key).ToList())
                _recentArrivalsByTripId.Remove(tripId);

            await ExecuteScriptAsync("clearBusMarkers();");

            if (_recentArrivalsByTripId.Count == 0)
            {
                BusStatusList.ItemsSource = Array.Empty<string>();
                return;
            }

            var arrivalByTripId = _recentArrivalsByTripId;
            var positions = await _vehiclePositions.GetPositionsForTripsAsync(arrivalByTripId.Keys.ToHashSet());

            var now = DateTime.Now;
            var statusChips = new List<string>();
            foreach (var p in positions)
            {
                var vehicleLabel = string.IsNullOrEmpty(p.VehicleLabel) ? "?" : p.VehicleLabel;
                var hasPassed = arrivalByTripId.TryGetValue(p.TripId, out var arrival) && arrival.ArrivalTime < now;
                // Prefix with the route so it's clear which line each bus belongs to at stops served by several.
                var label = arrival is not null ? $"[{arrival.RouteLabel}] {vehicleLabel}" : vehicleLabel;
                var etaLabel = "";
                if (arrival is not null)
                {
                    if (hasPassed)
                    {
                        var minutesAgo = Math.Max(0, (int)Math.Round((now - arrival.ArrivalTime).TotalMinutes));
                        var passedLabel = minutesAgo <= 0 ? "poco fa" : $"{minutesAgo} min fa";
                        etaLabel = $"{passedLabel} ({arrival.ArrivalTime:HH:mm:ss})";
                    }
                    else
                    {
                        var prefix = arrival.MinutesLabel == "in arrivo" ? "" : "tra ";
                        etaLabel = $"{prefix}{arrival.MinutesLabel} ({arrival.ArrivalTime:HH:mm:ss})";
                    }
                }
                var statusLabel = hasPassed ? "già passato" : p.IsStopped ? "fermo" : "in movimento";

                await ExecuteScriptAsync(
                    $"addBusMarker('{p.TripId}', {Fmt(p.Lat)}, {Fmt(p.Lon)}, {JsString(label)}, {(p.IsStopped ? "true" : "false")}, {JsString(etaLabel)}, {(hasPassed ? "true" : "false")});");

                statusChips.Add(string.IsNullOrEmpty(etaLabel)
                    ? $"🚌 {label}: {statusLabel}"
                    : $"🚌 {label}: {statusLabel} — {etaLabel}");
            }

            BusStatusList.ItemsSource = statusChips;
        }
        catch (Exception)
        {
            // Best-effort live overlay: a transient feed hiccup shouldn't break the dialog.
        }
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = double.DegreesToRadians(lat2 - lat1);
        var dLon = double.DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(double.DegreesToRadians(lat1)) * Math.Cos(double.DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    private static async Task<(double Lat, double Lon)?> GetCurrentLocationAsync()
    {
        try
        {
            var access = await Geo.Geolocator.RequestAccessAsync();
            if (access != Geo.GeolocationAccessStatus.Allowed) return null;

            var geolocator = new Geo.Geolocator { DesiredAccuracy = Geo.PositionAccuracy.Default };
            var position = await geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
            return (position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// CoreWebView2 can go null while the dialog is closing (e.g. a bus-refresh tick already in
    /// flight resumes after an await, right as the WebView2 control is torn down) — no-op instead
    /// of throwing a NullReferenceException in that case.
    /// </summary>
    private Task ExecuteScriptAsync(string script) =>
        MapWebView.CoreWebView2?.ExecuteScriptAsync(script) ?? Task.CompletedTask;

    private static string Fmt(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string JsString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private const string MapHtml = """
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8" />
          <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
          <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
          <style>
            html, body, #map { height: 100%; margin: 0; padding: 0; }
            .bus-icon { font-size: 20px; text-align: center; line-height: 24px; filter: drop-shadow(0 0 2px white); }
            .bus-icon.stopped { filter: drop-shadow(0 0 3px #d32f2f) drop-shadow(0 0 3px #d32f2f); }
            .bus-icon.moving { filter: drop-shadow(0 0 3px #2e7d32) drop-shadow(0 0 3px #2e7d32); }
            .bus-icon.passed { filter: drop-shadow(0 0 3px #1a73e8) drop-shadow(0 0 3px #1a73e8); opacity: 0.8; }
          </style>
        </head>
        <body>
          <div id="map"></div>
          <script>
            let map, stopMarkers = {}, busMarkers = {}, meMarker = null;
            let viewportDebounceTimer = null;
            let viewportHandler = null;

            // Idempotent: the dialog can switch from monitor-mode back to browse-mode (and vice
            // versa) without ever tearing down the WebView, so a second call just recenters.
            function initMap(lat, lon, zoom) {
              if (!map) {
                map = L.map('map').setView([lat, lon], zoom);
                L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                  maxZoom: 19,
                  attribution: '&copy; OpenStreetMap contributors'
                }).addTo(map);
              } else {
                map.setView([lat, lon], zoom);
              }
            }

            function notifyViewportChanged() {
              const bounds = map.getBounds();
              window.chrome.webview.postMessage(JSON.stringify({
                type: 'viewportChanged',
                north: bounds.getNorth(), south: bounds.getSouth(),
                east: bounds.getEast(), west: bounds.getWest()
              }));
            }

            // Browse-mode only: re-search stops within the visible area after panning/zooming settles,
            // instead of leaving the initial fixed set of markers stale forever.
            function enableViewportStopSearch() {
              if (viewportHandler) map.off('moveend', viewportHandler);
              viewportHandler = () => {
                clearTimeout(viewportDebounceTimer);
                viewportDebounceTimer = setTimeout(notifyViewportChanged, 500);
              };
              map.on('moveend', viewportHandler);
            }

            function addStopMarker(id, lat, lon, label, selectable) {
              const marker = L.marker([lat, lon]).addTo(map)
                .bindTooltip(label, { direction: 'top', offset: [0, -30] });
              if (selectable) {
                marker.on('click', () => window.chrome.webview.postMessage(JSON.stringify({ type: 'select', stopId: id })));
              }
              stopMarkers[id] = marker;
            }

            function clearStopMarkers() {
              for (const id in stopMarkers) map.removeLayer(stopMarkers[id]);
              stopMarkers = {};
            }

            function addMeMarker(lat, lon) {
              if (meMarker) map.removeLayer(meMarker);
              meMarker = L.circleMarker([lat, lon], { radius: 8, color: '#1a73e8', fillColor: '#1a73e8', fillOpacity: 0.9 })
                .addTo(map).bindPopup('La tua posizione');
            }

            function addBusMarker(id, lat, lon, label, isStopped, eta, hasPassed) {
              const statusClass = hasPassed ? 'passed' : (isStopped ? 'stopped' : 'moving');
              const icon = L.divIcon({ className: 'bus-icon ' + statusClass, html: '🚌', iconSize: [24, 24] });
              let popup = label + ' — ' + (hasPassed ? 'già passato' : (isStopped ? 'fermo' : 'in movimento'));
              if (eta) popup += '<br>' + eta;
              busMarkers[id] = L.marker([lat, lon], { icon }).addTo(map).bindPopup(popup);
            }

            function clearBusMarkers() {
              for (const id in busMarkers) map.removeLayer(busMarkers[id]);
              busMarkers = {};
            }
          </script>
        </body>
        </html>
        """;
}
