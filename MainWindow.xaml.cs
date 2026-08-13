using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Key = System.Windows.Input.Key;
using MonitorFermataAtacRoma.Models;
using MonitorFermataAtacRoma.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MonitorFermataAtacRoma;

public partial class MainWindow : Window
{
    private const int MaxNameSearchResults = 50;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly GtfsStaticData _staticData;
    private readonly GtfsRealtimeService _realtimeService;
    private readonly VehiclePositionsService _vehiclePositionsService;
    private readonly RecentStopsService _recentStops = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _timer;
    private readonly Task _staticDataLoadTask;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ObservableCollection<string> _availableLines = new();

    private readonly Forms.NumericUpDown _notifyMinutesUpDown = new() { Minimum = 1, Maximum = 60, Value = 5 };
    private readonly Forms.DateTimePicker _notifyFromPicker = CreateTimePicker();
    private readonly Forms.DateTimePicker _notifyToPicker = CreateTimePicker();

    private string? _monitoredStopId;
    private string? _lastNotifiedKey;
    private IReadOnlyList<ArrivalInfo> _lastArrivals = Array.Empty<ArrivalInfo>();
    private List<string>? _pendingLineSelection;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _staticData = new GtfsStaticData(_httpClient);
        _realtimeService = new GtfsRealtimeService(_httpClient, _staticData);
        _vehiclePositionsService = new VehiclePositionsService(_httpClient);

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += async (_, _) => await RefreshArrivalsAsync();

        _notifyIcon = CreateNotifyIcon();

        NotifyMinutesHost.Child = _notifyMinutesUpDown;
        NotifyFromHost.Child = _notifyFromPicker;
        NotifyToHost.Child = _notifyToPicker;
        _notifyFromPicker.Value = DateTime.Today;
        _notifyToPicker.Value = DateTime.Today.AddHours(23).AddMinutes(59);

        LineFilterListBox.ItemsSource = _availableLines;

        StopIdComboBox.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler(StopIdComboBox_TextChanged));

        _staticDataLoadTask = LoadStaticDataAsync();
        _ = RestoreLastSessionAsync();
    }

    private static Forms.DateTimePicker CreateTimePicker() => new()
    {
        Format = Forms.DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
    };

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var iconUri = new Uri("pack://application:,,,/Assets/bus.ico");
        var resourceStream = System.Windows.Application.GetResourceStream(iconUri)!.Stream;
        var icon = new Drawing.Icon(resourceStream);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Apri", null, (_, _) => ShowFromTray());
        menu.Items.Add("Esci", null, (_, _) => ExitApplication());

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

    // Window.MinWidth/MinHeight alone are unreliable on some DPI setups (a known WPF issue: the OS
    // resize limits end up computed from the wrong DPI scale). Enforcing them directly via the native
    // WM_GETMINMAXINFO message is the standard, DPI-correct workaround.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            hwndSource.AddHook(EnforceMinimumSizeHook);
    }

    private IntPtr EnforceMinimumSizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.PtMinTrackSize.X = (int)Math.Round(MinWidth * dpiScale);
        minMaxInfo.PtMinTrackSize.Y = (int)Math.Round(MinHeight * dpiScale);
        Marshal.StructureToPtr(minMaxInfo, lParam, true);

        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint PtReserved;
        public NativePoint PtMaxSize;
        public NativePoint PtMaxPosition;
        public NativePoint PtMinTrackSize;
        public NativePoint PtMaxTrackSize;
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

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        SaveCurrentSession();
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void SaveCurrentSession()
    {
        _settingsService.Save(new AppSettings
        {
            WasMonitoring = _monitoredStopId is not null,
            StopId = _monitoredStopId ?? StopIdComboBox.Text.Trim(),
            NotifyEnabled = NotifyCheckBox.IsChecked == true,
            NotifyMinutes = (int)_notifyMinutesUpDown.Value,
            NotifyFrom = _notifyFromPicker.Value.ToString("HH:mm"),
            NotifyTo = _notifyToPicker.Value.ToString("HH:mm"),
            LineFilterEnabled = LineFilterCheckBox.IsChecked == true,
            SelectedLines = LineFilterListBox.SelectedItems.Cast<string>().ToList(),
        });
    }

    private async Task RestoreLastSessionAsync()
    {
        await _staticDataLoadTask;

        var settings = _settingsService.Load();
        if (!settings.WasMonitoring || string.IsNullOrWhiteSpace(settings.StopId)) return;

        NotifyCheckBox.IsChecked = settings.NotifyEnabled;
        _notifyMinutesUpDown.Value = Math.Clamp(settings.NotifyMinutes, (int)_notifyMinutesUpDown.Minimum, (int)_notifyMinutesUpDown.Maximum);
        if (DateTime.TryParseExact(settings.NotifyFrom, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var from))
            _notifyFromPicker.Value = from;
        if (DateTime.TryParseExact(settings.NotifyTo, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var to))
            _notifyToPicker.Value = to;
        LineFilterCheckBox.IsChecked = settings.LineFilterEnabled;
        _pendingLineSelection = settings.SelectedLines;

        StopIdComboBox.Text = settings.StopId;
        await StartMonitoringAsync();
    }

    private async Task LoadStaticDataAsync()
    {
        StatusTextBlock.Text = "Scaricamento dati GTFS statici (linee/fermate)...";
        try
        {
            await _staticData.LoadAsync();
            StatusTextBlock.Text = "Dati caricati. Digita il nome o il codice di una fermata e premi Monitora.";
            UpdateStopNameHint();
            UpdateSuggestions();
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
        StopIdComboBox.IsEnabled = true;
        StatusTextBlock.Text = "Monitoraggio fermato.";
    }

    private async void StopIdComboBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await StartMonitoringAsync();
    }

    private void StopIdComboBox_GotFocus(object sender, RoutedEventArgs e) => UpdateSuggestions();

    private void StopIdComboBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateStopNameHint();
        UpdateSuggestions();
    }

    private void StopIdComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StopIdComboBox.SelectedItem is not StopSuggestion suggestion) return;

        // WPF syncs Text from SelectedItem right after this handler returns, which would
        // overwrite whatever we set here (e.g. back to the item's ToString()). Deferring to
        // the next dispatcher cycle lets our assignment win.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StopIdComboBox.IsDropDownOpen = false;
            StopIdComboBox.Text = suggestion.StopId;
        }), DispatcherPriority.ContextIdle);
    }

    private async void MapButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_staticDataLoadTask.IsCompleted)
        {
            StatusTextBlock.Text = "Attendo il caricamento dei dati GTFS statici prima di aprire la mappa...";
            await _staticDataLoadTask;
        }

        var dialog = _monitoredStopId is not null
            ? new MapWindow(_staticData, _realtimeService, _vehiclePositionsService, _monitoredStopId)
            : new MapWindow(_staticData, null, null, null);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && dialog.SelectedStopId is not null)
            StopIdComboBox.Text = dialog.SelectedStopId;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_staticData) { Owner = this };
        dialog.ShowDialog();

        // Names/routes may have just been refreshed from the dialog.
        UpdateStopNameHint();
        UpdateSuggestions();
    }

    private void LineFilterCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyFilterAndDisplay();

    private void LineFilterListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyFilterAndDisplay();

    private void UpdateStopNameHint()
    {
        var stopId = StopIdComboBox.Text.Trim();
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
            StopNameHintTextBlock.Text = "";
        }
    }

    /// <summary>Refreshes the dropdown: MRU when the field is empty or numeric, name search otherwise.</summary>
    private void UpdateSuggestions()
    {
        var text = StopIdComboBox.Text.Trim();
        List<StopSuggestion> items;

        if (text.Length == 0)
        {
            items = BuildRecentSuggestions();
        }
        else if (text.All(char.IsDigit))
        {
            items = BuildRecentSuggestions().Where(s => s.StopId.StartsWith(text, StringComparison.Ordinal)).ToList();
        }
        else if (_staticDataLoadTask.IsCompleted)
        {
            items = _staticData.SearchStopsByName(text, MaxNameSearchResults).ToList();
        }
        else
        {
            items = new List<StopSuggestion>();
        }

        StopIdComboBox.ItemsSource = items;
        StopIdComboBox.IsDropDownOpen = items.Count > 0 && StopIdComboBox.IsKeyboardFocusWithin;
    }

    private List<StopSuggestion> BuildRecentSuggestions() =>
        _recentStops.StopIds
            .Select(id => new StopSuggestion(id, _staticData.TryGetStopName(id, out var name) ? name : ""))
            .ToList();

    private async Task StartMonitoringAsync()
    {
        var stopId = StopIdComboBox.Text.Trim();
        if (stopId.Length == 0)
        {
            StatusTextBlock.Text = "Inserisci un codice fermata valido.";
            return;
        }
        if (!stopId.All(char.IsDigit))
        {
            StatusTextBlock.Text = "Il codice fermata deve essere numerico: seleziona una fermata dai suggerimenti oppure digita il codice.";
            return;
        }

        StartButton.IsEnabled = false;
        StopIdComboBox.IsEnabled = false;
        _monitoredStopId = stopId;
        _lastNotifiedKey = null;
        _availableLines.Clear();
        _recentStops.Touch(stopId);

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

            ApplyPendingLineSelection();
            ApplyFilterAndDisplay();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Errore durante l'aggiornamento ({DateTime.Now:HH:mm:ss}): {ex.Message}";
        }
    }

    /// <summary>Re-selects the line filter saved from the previous session, once its lines show up in the feed.</summary>
    private void ApplyPendingLineSelection()
    {
        if (_pendingLineSelection is null) return;

        foreach (var line in _pendingLineSelection)
            if (_availableLines.Contains(line) && !LineFilterListBox.SelectedItems.Contains(line))
                LineFilterListBox.SelectedItems.Add(line);

        _pendingLineSelection = null;
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
        RefreshStarColumnWidths();
        StatusTextBlock.Text = arrivals.Count > 0
            ? $"Ultimo aggiornamento: {DateTime.Now:HH:mm:ss} — {arrivals.Count} corse in arrivo."
            : $"Ultimo aggiornamento: {DateTime.Now:HH:mm:ss} — nessuna corsa in arrivo trovata per questa fermata.";

        if (NotifyCheckBox.IsChecked == true && arrivals.Count > 0)
            MaybeNotifyNextArrival(arrivals[0]);
    }

    /// <summary>
    /// WPF DataGrid quirk: star-sized columns (here "Destinazione") don't redistribute their width
    /// when ItemsSource is assigned for the first time — only a later layout-invalidating event, like
    /// resizing the window, does. Toggling the width forces the recomputation.
    /// </summary>
    /// <remarks>
    /// On a restored session at startup, this can run before the window's very first layout pass has
    /// happened at all (if the GTFS cache is warm, the whole restore chain up to the network call can
    /// complete synchronously, racing ahead of the queued Render-priority layout). Deferring to
    /// ContextIdle — lower priority than layout/render — guarantees the DataGrid has been arranged at
    /// least once before we try to nudge its column widths.
    /// </remarks>
    private void RefreshStarColumnWidths()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var column in ArrivalsGrid.Columns)
            {
                if (!column.Width.IsStar) continue;
                var width = column.Width;
                column.Width = new System.Windows.Controls.DataGridLength(0, System.Windows.Controls.DataGridLengthUnitType.Pixel);
                column.Width = width;
            }
        }), DispatcherPriority.ContextIdle);
    }

    private void MaybeNotifyNextArrival(ArrivalInfo next)
    {
        var notifyThreshold = TimeSpan.FromMinutes((double)_notifyMinutesUpDown.Value);
        if (next.TimeUntilArrival <= TimeSpan.Zero || next.TimeUntilArrival > notifyThreshold) return;
        if (!IsWithinNotifyWindow()) return;

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

    private bool IsWithinNotifyWindow()
    {
        var from = _notifyFromPicker.Value.TimeOfDay;
        var to = _notifyToPicker.Value.TimeOfDay;
        var now = DateTime.Now.TimeOfDay;

        return from <= to
            ? now >= from && now <= to
            : now >= from || now <= to; // overnight range, e.g. 22:00-06:00
    }
}
