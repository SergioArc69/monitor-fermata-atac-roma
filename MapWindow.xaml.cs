using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using MonitorFermataAtacRoma.Services;
using Geo = Windows.Devices.Geolocation;

namespace MonitorFermataAtacRoma;

public partial class MapWindow : Window
{
    private static readonly TimeSpan BusRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly GtfsStaticData _staticData;
    private readonly GtfsRealtimeService? _realtimeService;
    private readonly VehiclePositionsService? _vehiclePositions;
    private readonly string? _monitoredStopId;
    private readonly DispatcherTimer? _busRefreshTimer;

    public string? SelectedStopId { get; private set; }

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

    private async void MapWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await MapWebView.EnsureCoreWebView2Async();
        MapWebView.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            SelectedStopId = args.TryGetWebMessageAsString();
            DialogResult = true;
        };

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

            await ExecuteScriptAsync($"initMap({Fmt(lat)}, {Fmt(lon)}, 16);");
            await ExecuteScriptAsync($"addStopMarker('{_monitoredStopId}', {Fmt(lat)}, {Fmt(lon)}, {JsString(stopName)}, false);");
            await RefreshBusPositionsAsync();
            _busRefreshTimer?.Start();
        }
        else
        {
            await ShowNearbyStopsAsync();
        }
    }

    private async Task ShowNearbyStopsAsync()
    {
        var location = await GetCurrentLocationAsync();
        var (lat, lon) = location ?? (41.9028, 12.4964); // fallback: Roma centro

        InstructionTextBlock.Text = location is not null
            ? "Fermate vicino alla tua posizione: clicca su una fermata per selezionarla."
            : "Posizione non disponibile: mostro le fermate del centro di Roma. Clicca su una fermata per selezionarla.";

        await ExecuteScriptAsync($"initMap({Fmt(lat)}, {Fmt(lon)}, 16);");
        await ExecuteScriptAsync($"addMeMarker({Fmt(lat)}, {Fmt(lon)});");

        foreach (var stop in _staticData.GetNearbyStops(lat, lon, 40))
            await ExecuteScriptAsync(
                $"addStopMarker('{stop.StopId}', {Fmt(stop.Lat)}, {Fmt(stop.Lon)}, {JsString(stop.StopName)}, true);");
    }

    private async Task RefreshBusPositionsAsync()
    {
        if (_vehiclePositions is null || _realtimeService is null || _monitoredStopId is null) return;

        try
        {
            // Re-fetch live arrivals every tick (rather than reusing a snapshot from when the dialog
            // opened) so a bus that has already passed the stop naturally drops out of tracking instead
            // of lingering on the map with a frozen, increasingly wrong ETA.
            var arrivals = await _realtimeService.GetArrivalsForStopAsync(_monitoredStopId);
            var arrivalByTripId = arrivals.GroupBy(a => a.TripId).ToDictionary(g => g.Key, g => g.First());

            await ExecuteScriptAsync("clearBusMarkers();");

            if (arrivalByTripId.Count == 0)
            {
                BusStatusList.ItemsSource = Array.Empty<string>();
                return;
            }

            var positions = await _vehiclePositions.GetPositionsForTripsAsync(arrivalByTripId.Keys.ToHashSet());

            var statusChips = new List<string>();
            foreach (var p in positions)
            {
                var label = string.IsNullOrEmpty(p.VehicleLabel) ? "?" : p.VehicleLabel;
                var etaLabel = "";
                if (arrivalByTripId.TryGetValue(p.TripId, out var arrival))
                {
                    var prefix = arrival.MinutesLabel == "in arrivo" ? "" : "tra ";
                    etaLabel = $"{prefix}{arrival.MinutesLabel} ({arrival.ArrivalTime:HH:mm:ss})";
                }
                var statusLabel = p.IsStopped ? "fermo" : "in movimento";

                await ExecuteScriptAsync(
                    $"addBusMarker('{p.TripId}', {Fmt(p.Lat)}, {Fmt(p.Lon)}, {JsString(label)}, {(p.IsStopped ? "true" : "false")}, {JsString(etaLabel)});");

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

    private Task ExecuteScriptAsync(string script) => MapWebView.CoreWebView2.ExecuteScriptAsync(script);

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
          </style>
        </head>
        <body>
          <div id="map"></div>
          <script>
            let map, stopMarkers = {}, busMarkers = {}, meMarker = null;

            function initMap(lat, lon, zoom) {
              map = L.map('map').setView([lat, lon], zoom);
              L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; OpenStreetMap contributors'
              }).addTo(map);
            }

            function addStopMarker(id, lat, lon, label, selectable) {
              const marker = L.marker([lat, lon]).addTo(map).bindPopup(label);
              if (selectable) {
                marker.on('click', () => window.chrome.webview.postMessage(id));
              }
              stopMarkers[id] = marker;
            }

            function addMeMarker(lat, lon) {
              if (meMarker) map.removeLayer(meMarker);
              meMarker = L.circleMarker([lat, lon], { radius: 8, color: '#1a73e8', fillColor: '#1a73e8', fillOpacity: 0.9 })
                .addTo(map).bindPopup('La tua posizione');
            }

            function addBusMarker(id, lat, lon, label, isStopped, eta) {
              const statusClass = isStopped ? 'stopped' : 'moving';
              const icon = L.divIcon({ className: 'bus-icon ' + statusClass, html: '🚌', iconSize: [24, 24] });
              let popup = label + ' — ' + (isStopped ? 'fermo' : 'in movimento');
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
