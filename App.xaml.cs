using System.Windows;
using System.Windows.Threading;

namespace MonitorFermataAtacRoma;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            $"Si è verificato un errore imprevisto:\n\n{e.Exception.Message}\n\nL'app proverà a continuare.",
            "Monitor Fermata ATAC Roma", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
