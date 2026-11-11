using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using StoryBoarder.Views;

namespace StoryBoarder.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // A banner under the board; it stays out of the way (zero height) until an ad has loaded.
        MainView.AdBannerFactory = () => new AdBanner();
        return base.CustomizeAppBuilder(builder);
    }
}
