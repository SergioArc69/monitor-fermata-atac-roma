using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace MonitorFermataAtacRoma;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/SergioArc69/monitor-fermata-atac-roma";
    private const string ReleasesUrl = "https://github.com/SergioArc69/monitor-fermata-atac-roma/releases";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionTextBlock.Text = version is null ? "" : $"Versione {version.Major}.{version.Minor}.{version.Build}";
    }

    private void RepositoryLink_Click(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private void ReleasesLink_Click(object sender, RoutedEventArgs e) => OpenUrl(ReleasesUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best effort: if no default browser is registered there's nothing else to do here.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
