using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Key = System.Windows.Input.Key;
using MonitorFermataAtacRoma.Models;
using MonitorFermataAtacRoma.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MonitorFermataAtacRoma;

public partial class MainWindow : Window
{
    private const string DefaultStopId = "73953"; // BONA

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NotifyThreshold = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly GtfsStaticData _staticData;
    private readonly GtfsRealtimeService _realtimeService;
    private readonly DispatcherTimer _timer;
    private readonly Task _staticDataLoadTask;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ObservableCollection<string> _availableLines = new();

    private string? _monitoredStopId;
    private string? _lastNotifiedKey;
    private IReadOnlyList<ArrivalInfo> _lastArrivals = Array.Empty<ArrivalInfo>();
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _staticData = new GtfsStaticData(_httpClient);
        _realtimeService = new GtfsRealtimeService(_httpClient, _staticData);

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += async (_, _) => await RefreshArrivalsAsync();

        _notifyIcon = CreateNotifyIcon();

        LineFilterListBox.ItemsSource = _availableLines;

        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();

        _staticDataLoadTask = LoadStaticDataAsync();
        StopIdTextBox.Text = DefaultStopId;
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var iconUri = new Uri("pack://application:,,,/Assets/bus.ico");
        var resourceStream = System.Windows.Application.GetResourceStream(iconUri)!.Stream;
        var icon = new Drawing.Icon(resourceStream);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Apri", null, (_, _) => ShowFromTray());
        menu.Items.Add("Esci", null, (_, _) => { _isExiting = true; System.Windows.Application.Current.Shutdown(); });

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Monitor Fermata ATAC Roma",
            Visible = true,
            ContextMenuStrip = menu,
        };
        notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        return notifyIcon;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            _notifyIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        _notifyIcon.ShowBalloonTip(3000, "Monitor Fermata ATAC Roma",
            "L'app continua a girare nella system tray. Usa il menu dell'icona per uscire.", Forms.ToolTipIcon.Info);
    }

    private async Task LoadStaticDataAsync()
    {
        StatusTextBlock.Text = "Scaricamento dati GTFS statici (linee/fermate)...";
        try
        {
            await _staticData.LoadAsync();
            StatusTextBlock.Text = "Dati caricati. Inserisci un codice fermata e premi Monitora.";
            UpdateStopNameHint();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Errore nel caricamento dei dati statici: {ex.Message}";
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) => await StartMonitoringAsync();

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _monitoredStopId = null;
        _lastNotifiedKey = null;
        _lastArrivals = Array.Empty<ArrivalInfo>();
        _availableLines.Clear();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StopIdTextBox.IsEnabled = true;
        StatusTextBlock.Text = "Monitoraggio fermato.";
    }

    private async void StopIdTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await StartMonitoringAsync();
    }

    private void StopIdTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateStopNameHint();

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e) =>
        AutoStartService.SetEnabled(AutoStartCheckBox.IsChecked == true);

    private void LineFilterCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyFilterAndDisplay();

    private void LineFilterListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyFilterAndDisplay();

    private void UpdateStopNameHint()
    {
        var stopId = StopIdTextBox.Text.Trim();
        if (stopId.Length == 0)
        {
            StopNameHintTextBlock.Text = "";
        }
        else if (!_staticDataLoadTask.IsCompleted)
        {
            StopNameHintTextBlock.Text = "(caricamento nomi fermate...)";
        }
        else if (_staticData.TryGetStopName(stopId, out var stopName))
        {
            StopNameHintTextBlock.Text = stopName;
        }
        else
        {
            StopNameHintTextBlock.Text = "(fermata non trovata)";
        }
    }

    private async Task StartMonitoringAsync()
    {
        var stopId = StopIdTextBox.Text.Trim();
        if (stopId.Length == 0)
        {
            StatusTextBlock.Text = "Inserisci un codice fermata valido.";
            return;
        }

        StartButton.IsEnabled = false;
        StopIdTextBox.IsEnabled = false;
        _monitoredStopId = stopId;
        _lastNotifiedKey = null;
        _availableLines.Clear();

        if (!_staticDataLoadTask.IsCompleted)
            StatusTextBlock.Text = "Attendo il caricamento dei dati GTFS statici...";
        await _staticDataLoadTask;

        if (_staticData.TryGetStopName(stopId, out var stopName))
            Title = $"Monitor Fermata ATAC Roma — {stopName} ({stopId})";
        else
            StatusTextBlock.Text = $"Attenzione: codice fermata '{stopId}' non trovato nel GTFS statico (proseguo comunque con i dati realtime).";

        StopButton.IsEnabled = true;
        _timer.Start();
        await RefreshArrivalsAsync();
    }

    private async Task RefreshArrivalsAsync()
    {
        if (_monitoredStopId is null) return;

        try
        {
            _lastArrivals = await _realtimeService.GetArrivalsForStopAsync(_monitoredStopId);

            foreach (var line in _lastArrivals.Select(a => a.RouteLabel).Distinct())
                AddAvailableLine(line);

            ApplyFilterAndDisplay();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Errore durante l'aggiornamento ({DateTime.Now:HH:mm:ss}): {ex.Message}";
        }
    }

    private void AddAvailableLine(string label)
    {
        if (_availableLines.Contains(label)) return;
        var index = 0;
        while (index < _availableLines.Count && string.Compare(_availableLines[index], label, StringComparison.OrdinalIgnoreCase) < 0)
            index++;
        _availableLines.Insert(index, label);
    }

    private void ApplyFilterAndDisplay()
    {
        var selectedLines = LineFilterListBox.SelectedItems.Cast<string>().ToHashSet();
        var filterActive = LineFilterCheckBox.IsChecked == true && selectedLines.Count > 0;

        var arrivals = filterActive
            ? _lastArrivals.Where(a => selectedLines.Contains(a.RouteLabel)).ToList()
            : _lastArrivals;

        var view = new ListCollectionView(arrivals.ToList())
        {
            GroupDescriptions = { new PropertyGroupDescription(nameof(ArrivalInfo.RouteLabel)) },
            SortDescriptions =
            {
                new SortDescription(nameof(ArrivalInfo.RouteLabel), ListSortDirection.Ascending),
                new SortDescription(nameof(ArrivalInfo.ArrivalTime), ListSortDirection.Ascending),
            },
        };
        ArrivalsGrid.ItemsSource = view;
        StatusTextBlock.Text = arrivals.Count > 0
            ? $"Ultimo aggiornamento: {DateTime.Now:HH:mm:ss} — {arrivals.Count} corse in arrivo."
            : $"Ultimo aggiornamento: {DateTime.Now:HH:mm:ss} — nessuna corsa in arrivo trovata per questa fermata.";

        if (NotifyCheckBox.IsChecked == true && arrivals.Count > 0)
            MaybeNotifyNextArrival(arrivals[0]);
    }

    private void MaybeNotifyNextArrival(ArrivalInfo next)
    {
        if (next.TimeUntilArrival <= TimeSpan.Zero || next.TimeUntilArrival > NotifyThreshold) return;

        // Bucketing on the estimated time (rounded down to the previous even minute) means small
        // second-to-second prediction jitter doesn't re-trigger a notification, but a real change
        // in the estimate (e.g. traffic) that crosses into a new bucket still does.
        var bucket = RoundDownToEvenMinute(next.ArrivalTime);
        var key = $"{next.TripId}|{bucket:O}";
        if (key == _lastNotifiedKey) return;

        _lastNotifiedKey = key;
        var minutes = Math.Max(1, (int)Math.Round(next.TimeUntilArrival.TotalMinutes));
        _notifyIcon.ShowBalloonTip(5000, "Autobus in arrivo",
            $"{next.DisplayName} arriva tra {minutes} min ({next.ArrivalTime:HH:mm:ss}).", Forms.ToolTipIcon.Info);
    }

    private static DateTime RoundDownToEvenMinute(DateTime time)
    {
        var evenMinute = time.Minute - (time.Minute % 2);
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, evenMinute, 0);
    }
}
