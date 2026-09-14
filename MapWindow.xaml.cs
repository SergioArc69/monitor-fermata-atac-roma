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
    private static readonly TimeSpan BusRefreshInterval = TimeSpan.FromSeconds(20);

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
                InstructionTextBlock.Text = "Fermata monitorata, con la posizione dei bus in transito (aggiornata ogni 20 secondi).";
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

    /// <summary>
    /// A bus is only treated as "already passed" once it has BOTH dropped out of the TripUpdates
    /// feed for this stop (producers remove a stop the moment the vehicle passes it) AND its last
    /// predicted arrival is comfortably in the past. Time alone isn't enough: a late bus that's
    /// still approaching keeps a stale-looking prediction while its real position shows otherwise.
    /// </summary>
    private const int PassedConfirmGraceSeconds = 90;

    private bool _busRefreshInFlight;

    private async Task RefreshBusPositionsAsync()
    {
        if (_vehiclePositions is null || _realtimeService is null || _monitoredStopId is null) return;

        // A slow previous refresh still resolving its fetches would otherwise interleave with this
        // one (Tick can fire again while the handler is awaiting) and leak duplicate markers.
        if (_busRefreshInFlight) return;
        _busRefreshInFlight = true;
        var stopId = _monitoredStopId;

        try
        {
            // Re-fetch live arrivals every tick (rather than reusing a snapshot from when the dialog
            // opened) and merge them into our own short-term memory, since the feed itself drops a
            // stop's entry almost as soon as the bus passes it — see _recentArrivalsByTripId.
            var freshArrivals = await _realtimeService.GetArrivalsForStopAsync(stopId);
            if (_monitoredStopId != stopId) return;

            // Trips still listed for this stop haven't passed it yet, however late they're running.
            var stillApproaching = freshArrivals.Select(a => a.TripId).ToHashSet();
            foreach (var arrival in freshArrivals)
                _recentArrivalsByTripId[arrival.TripId] = arrival;

            var cutoff = DateTime.Now.AddMinutes(-RecentlyPassedLookbackMinutes);
            foreach (var tripId in _recentArrivalsByTripId.Where(kv => kv.Value.ArrivalTime < cutoff).Select(kv => kv.Key).ToList())
                _recentArrivalsByTripId.Remove(tripId);

            if (_recentArrivalsByTripId.Count == 0)
            {
                await ExecuteScriptAsync("clearBusMarkers();");
                BusStatusList.ItemsSource = Array.Empty<string>();
                return;
            }

            var arrivalByTripId = _recentArrivalsByTripId;
            var positions = await _vehiclePositions.GetPositionsForTripsAsync(arrivalByTripId.Keys.ToHashSet());
            if (_monitoredStopId != stopId) return;

            // For buses currently stopped, look up the realtime predicted departure from the stop
            // they're sitting at, so the chip can show when they're expected to move on.
            var stoppedTripStops = positions
                .Where(p => p.IsStopped && !string.IsNullOrEmpty(p.CurrentStopId))
                .GroupBy(p => p.TripId)
                .ToDictionary(g => g.Key, g => g.First().CurrentStopId);
            var predictedDepartures = await _realtimeService.GetPredictedDeparturesAtStopsAsync(stoppedTripStops);
            if (_monitoredStopId != stopId) return;

            // Replace this tick's snapshot in one go, only after every fetch has resolved — clearing
            // earlier would leave the map empty during the fetch window.
            await ExecuteScriptAsync("clearBusMarkers();");

            var now = DateTime.Now;
            var statusChips = new List<string>();
            foreach (var p in positions)
            {
                var vehicleLabel = string.IsNullOrEmpty(p.VehicleLabel) ? "?" : p.VehicleLabel;
                arrivalByTripId.TryGetValue(p.TripId, out var arrival);
                var hasPassed = arrival is not null
                    && !stillApproaching.Contains(p.TripId)
                    && arrival.ArrivalTime < now.AddSeconds(-PassedConfirmGraceSeconds);
                // Prefix with the route so it's clear which line each bus belongs to at stops served by several.
                var label = arrival is not null ? $"[{arrival.RouteLabel}] {vehicleLabel}" : vehicleLabel;

                DateTime? departureTime = !hasPassed && p.IsStopped && predictedDepartures.TryGetValue(p.TripId, out var dep)
                    ? dep
                    : null;

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
                        // When a departure time (|→) is also shown, mark the arrival time with →| to tell the two apart.
                        var arrivalMark = departureTime is not null ? "→| " : "";
                        etaLabel = $"{prefix}{arrival.MinutesLabel} ({arrivalMark}{arrival.ArrivalTime:HH:mm:ss})";
                    }
                }

                var statusClass = hasPassed ? "passed" : p.IsStopped ? "stopped" : "moving";
                var statusLabel = hasPassed ? "già passato" : p.IsStopped ? "fermo" : "in movimento";
                if (departureTime is not null) statusLabel = $"fermo ({departureTime:HH:mm:ss} |→)";

                await ExecuteScriptAsync(
                    $"addBusMarker('{p.TripId}', {Fmt(p.Lat)}, {Fmt(p.Lon)}, {JsString(label)}, {JsString(statusClass)}, {JsString(statusLabel)}, {JsString(etaLabel)});");

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
        finally
        {
            _busRefreshInFlight = false;
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
          <link rel="stylesheet" href="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css" />
          <script src="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js"></script>
          <style>
            html, body, #map { height: 100%; margin: 0; padding: 0; }
            .stop-marker { font-size: 20px; cursor: default; filter: drop-shadow(0 0 2px white); }
            .stop-marker.selectable { cursor: pointer; }
            .me-marker {
              width: 16px; height: 16px; border-radius: 50%;
              background: #1a73e8; border: 2px solid white; box-shadow: 0 0 2px rgba(0,0,0,.5);
            }
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
            // Note: MapLibre (unlike Leaflet) takes [lon, lat], not [lat, lon].
            function initMap(lat, lon, zoom) {
              if (!map) {
                // tile.openstreetmap.org is a volunteer-run server that blocks third-party app
                // traffic (a WebView2-hosted page has no real referrer, read as "bulk" usage under
                // its tile usage policy — see https://operations.osmfoundation.org/policies/tiles/).
                // Wikimedia's raster tiles turned out to be rate-limited in practice, and CARTO's
                // free basemaps now require an API key — OpenFreeMap is free, unlimited, and needs
                // no key, but only serves vector tiles, hence MapLibre GL JS instead of Leaflet.
                map = new maplibregl.Map({
                  container: 'map',
                  style: 'https://tiles.openfreemap.org/styles/bright',
                  center: [lon, lat],
                  zoom: zoom,
                  attributionControl: { compact: true, customAttribution: '© OpenStreetMap contributors · © OpenFreeMap' }
                });
                map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-left');

                // The "bright" style references a few POI icons (gate, office, swimming_pool, ...)
                // that aren't in the sprite sheet it's paired with — cosmetic gaps upstream.
                // setMissingStyleImageResolver is awaited *before* MapLibre treats the image as
                // missing, so resolving it here (unlike handling 'styleimagemissing', which fires
                // only after the "could not be loaded" warning is already logged) avoids the
                // console warning entirely, not just the visual gap.
                map.setMissingStyleImageResolver((id) => {
                  if (map.hasImage(id)) return;
                  map.addImage(id, { width: 1, height: 1, data: new Uint8Array([0, 0, 0, 0]) });
                });
              } else {
                map.jumpTo({ center: [lon, lat], zoom: zoom });
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
              const el = document.createElement('div');
              el.className = 'stop-marker' + (selectable ? ' selectable' : '');
              el.textContent = selectable ? '📍' : '🚏';
              // Leaflet's bindTooltip showed the label on hover (not click); replicate that with a
              // popup toggled on mouseenter/mouseleave instead of MapLibre's default click-to-open.
              const tooltip = new maplibregl.Popup({ offset: 14, closeButton: false, closeOnClick: false }).setHTML(label);
              el.addEventListener('mouseenter', () => tooltip.setLngLat([lon, lat]).addTo(map));
              el.addEventListener('mouseleave', () => tooltip.remove());
              if (selectable) {
                el.addEventListener('click', () => window.chrome.webview.postMessage(JSON.stringify({ type: 'select', stopId: id })));
              }
              stopMarkers[id] = new maplibregl.Marker({ element: el }).setLngLat([lon, lat]).addTo(map);
            }

            function clearStopMarkers() {
              for (const id in stopMarkers) stopMarkers[id].remove();
              stopMarkers = {};
            }

            function addMeMarker(lat, lon) {
              if (meMarker) meMarker.remove();
              const el = document.createElement('div');
              el.className = 'me-marker';
              const popup = new maplibregl.Popup({ offset: 10 }).setHTML('La tua posizione');
              meMarker = new maplibregl.Marker({ element: el }).setLngLat([lon, lat]).setPopup(popup).addTo(map);
            }

            function addBusMarker(id, lat, lon, label, statusClass, statusLabel, eta) {
              if (busMarkers[id]) busMarkers[id].remove(); // never leave the previous position's marker behind
              const el = document.createElement('div');
              el.className = 'bus-icon ' + statusClass;
              el.textContent = '🚌';
              let popupHtml = label + ' — ' + statusLabel;
              if (eta) popupHtml += '<br>' + eta;
              const popup = new maplibregl.Popup({ offset: 12 }).setHTML(popupHtml);
              busMarkers[id] = new maplibregl.Marker({ element: el }).setLngLat([lon, lat]).setPopup(popup).addTo(map);
            }

            function clearBusMarkers() {
              for (const id in busMarkers) busMarkers[id].remove();
              busMarkers = {};
            }
          </script>
        </body>
        </html>
        """;
}
