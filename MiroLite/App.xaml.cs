using System.Windows;

namespace MiroLite;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Show unexpected errors instead of letting the app vanish with unsaved work.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Miro Lite", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
