using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StoryBoarder.Views;

namespace StoryBoarder;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new MainView();

        // Show unexpected errors instead of letting the app vanish with unsaved work.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"Story Boarder: unhandled error: {args.Exception}");
            if (MainView.Current is not { } view) return;
            args.Handled = true;
            _ = view.ShowMessageAsync($"Something went wrong.\n\n{args.Exception.Message}");
        };

        base.OnFrameworkInitializationCompleted();
    }
}
