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

    public override void OnCreate()
    {
        base.OnCreate();
        // Before any screen shows, so Premium users never see an ad (it starts from the last known answer).
        Billing.Instance.Start(this);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // A banner under the board; it stays out of the way (zero height) until an ad has loaded.
        MainView.AdBannerFactory = () => new AdBanner();
        // The one-time purchase that removes ads ("Remove ads" in the ☰ menu).
        MainView.Store = Billing.Instance;
        // Google Play requires the privacy policy to be linked from inside the app too (☰ → Privacy policy).
        // It's docs/privacy-policy.html in the repository, published with GitHub Pages.
        MainView.PrivacyPolicy = new Uri("https://colearkenach.github.io/Story-Boarder-App/privacy-policy.html");
        return base.CustomizeAppBuilder(builder);
    }
}
