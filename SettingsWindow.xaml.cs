using System.Windows;
using MonitorFermataAtacRoma.Services;

namespace MonitorFermataAtacRoma;

public partial class SettingsWindow : Window
{
    private readonly GtfsStaticData _staticData;

    public SettingsWindow(GtfsStaticData staticData)
    {
        InitializeComponent();
        _staticData = staticData;
        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e) =>
        AutoStartService.SetEnabled(AutoStartCheckBox.IsChecked == true);

    private async void RefreshDataButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDataButton.IsEnabled = false;
        StatusTextBlock.Text = "Scaricamento dati linee e fermate in corso...";
        try
        {
            await _staticData.LoadAsync(forceRefresh: true);
            StatusTextBlock.Text = "Dati aggiornati.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Errore durante l'aggiornamento: {ex.Message}";
        }
        finally
        {
            RefreshDataButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();
}
