using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Google.Android.Gms.Ads;

namespace StoryBoarder.Android;

/// <summary>
/// An AdMob adaptive banner (a native Android view) in the Avalonia layout, under the board. It takes no space
/// until ads are allowed, and gives the space back if no ad comes.
/// </summary>
internal sealed class AdBanner : NativeControlHost
{
    private AdView? _adView;
    private bool _loading;

    public AdBanner()
    {
        Height = 0;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var activity = MainActivity.Current ?? throw new InvalidOperationException("No activity to show ads in.");
        var metrics = activity.Resources!.DisplayMetrics!;
        var widthDp = (int)(metrics.WidthPixels / metrics.Density);
        _adView = new AdView(activity)
        {
            AdUnitId = Ads.BannerUnitId,
            AdSize = AdSize.GetLargeAnchoredAdaptiveBannerAdSize(activity, widthDp),
            AdListener = new Listener(this),
        };
        Ads.Register(this);
        return new AndroidViewControlHandle(_adView);
    }

    // Destroy the ad view as soon as the banner leaves the screen (e.g. the user just bought Premium): by the time
    // Avalonia calls DestroyNativeControlCore it has already released the view, and it can't be destroyed then.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _adView?.Destroy();
        _adView = null;
        base.OnDetachedFromVisualTree(e);
    }

    public void Load()
    {
        if (_adView?.AdSize is not { } size || _loading) return;
        _loading = true;
        // Make room first: AdMob won't show an ad in a view too small for it.
        Height = size.Height;
        _adView.LoadAd(new AdRequest.Builder().Build());
    }

    public void Pause() => _adView?.Pause();
    public void Resume() => _adView?.Resume();

    private sealed class Listener(AdBanner banner) : AdListener
    {
        public override void OnAdFailedToLoad(LoadAdError error) =>
            // No ad (no fill, offline...): hand the space back to the board. AdMob retries on its own refresh.
            Dispatcher.UIThread.Post(() => banner.Height = 0);

        public override void OnAdLoaded() =>
            Dispatcher.UIThread.Post(() =>
            {
                if (banner._adView?.AdSize is { } size) banner.Height = size.Height;
            });
    }
}
