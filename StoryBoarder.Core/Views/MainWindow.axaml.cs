using Avalonia.Controls;
using Avalonia.Threading;

namespace StoryBoarder.Views;

/// <summary>The desktop window: hosts the board view, opens a file given on the command line, asks before closing.</summary>
public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Deactivated += (_, _) => View.OnHostDeactivated();
        // Double-clicking a .storyboard file starts the app with its path.
        if (args.Length > 0 && File.Exists(args[0]))
            Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = View.OpenPathAsync(args[0]), DispatcherPriority.Loaded);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel) return;
        e.Cancel = true;
        if (!await View.ConfirmDiscardChangesAsync()) return;
        _closeConfirmed = true;
        Close();
    }
}
