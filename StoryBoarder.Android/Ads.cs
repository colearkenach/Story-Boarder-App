using Android.App;
using Google.Android.Gms.Ads;
using StoryBoarder.Views;
using Xamarin.Google.UserMesssagingPlatform; // sic: the binding's namespace has three s's

namespace StoryBoarder.Android;

/// <summary>
/// AdMob setup. Asks for consent where the law requires it (EEA, UK, Switzerland) with Google's consent form,
/// then starts the ads SDK and loads the banner. Users who had to consent can change their choice later from
/// the File menu ("Ad privacy choices").
/// </summary>
internal static class Ads
{
    /// <summary>
    /// The banner ad unit (an ID with a "/"; the app ID with a "~" goes in AndroidManifest.xml). Debug builds use
    /// Google's sample unit, which only serves test ads, so testing on your own phone never loads or clicks real
    /// ads (AdMob can suspend an account for that). Release builds, the ones uploaded to Google Play, use yours.
    /// </summary>
#if DEBUG
    public const string BannerUnitId = "ca-app-pub-3940256099942544/9214589741";
#else
    public const string BannerUnitId = "ca-app-pub-5545029026903875/5031662256";
#endif

    /// <summary>
    /// Your own phones and tablets, which always get test ads, even in Release builds with the real ad unit.
    /// To add one, run the app on it and look in the device log for "setTestDeviceIds(Arrays.asList("…"))".
    /// </summary>
    private static readonly string[] TestDeviceIds =
    [
        "AFE3D92A56E62581A4ECF2CC6C73043F", // Pixel Fold
    ];

    private static readonly List<WeakReference<AdBanner>> Banners = new();
    private static IConsentInformation? _consent;
    private static bool _started;

    /// <summary>Asks for consent if needed, then starts ads. Only for users without Premium; safe to call again.</summary>
    public static void Start(Activity activity)
    {
        if (_consent is not null) return;
        _consent = UserMessagingPlatform.GetConsentInformation(activity);
        var parameters = new ConsentRequestParameters.Builder().Build();
        _consent.RequestConsentInfoUpdate(activity, parameters,
            new ConsentInfoUpdated(() => UserMessagingPlatform.LoadAndShowConsentFormIfRequired(activity,
                new ConsentFormDismissed(_ => OnConsentGathered(activity)))),
            new ConsentInfoUpdateFailed(_ => OnConsentGathered(activity)));
        // Consent given in an earlier session lets ads start without waiting for the update.
        if (_consent.CanRequestAds()) StartAds(activity);
    }

    private static void OnConsentGathered(Activity activity)
    {
        if (_consent is null) return;
        var privacyRequired = ConsentInformationPrivacyOptionsRequirementStatus.Required.Equals(_consent.PrivacyOptionsRequirementStatus);
        MainView.PrivacyOptions = privacyRequired ? ShowPrivacyOptions : null;
        if (_consent.CanRequestAds()) StartAds(activity);
    }

    private static void ShowPrivacyOptions()
    {
        if (MainActivity.Current is { } activity)
            UserMessagingPlatform.ShowPrivacyOptionsForm(activity, new ConsentFormDismissed(_ => { }));
    }

    private static void StartAds(Activity activity)
    {
        if (_started) return;
        _started = true;
        MobileAds.RequestConfiguration = new RequestConfiguration.Builder().SetTestDeviceIds(TestDeviceIds).Build();
        // Google recommends initializing off the main thread; ads can be requested straight away.
        var context = activity.ApplicationContext!;
        Task.Run(() => MobileAds.Initialize(context));
        foreach (var banner in LiveBanners()) banner.Load();
    }

    public static void Register(AdBanner banner)
    {
        Banners.Add(new WeakReference<AdBanner>(banner));
        if (_started) banner.Load();
    }

    public static void Pause()
    {
        foreach (var banner in LiveBanners()) banner.Pause();
    }

    public static void Resume()
    {
        foreach (var banner in LiveBanners()) banner.Resume();
    }

    private static List<AdBanner> LiveBanners()
    {
        Banners.RemoveAll(b => !b.TryGetTarget(out _));
        return Banners.Select(b => b.TryGetTarget(out var banner) ? banner : null).OfType<AdBanner>().ToList();
    }

    private sealed class ConsentInfoUpdated(Action onSuccess)
        : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateSuccessListener
    {
        public void OnConsentInfoUpdateSuccess() => onSuccess();
    }

    private sealed class ConsentInfoUpdateFailed(Action<FormError> onFailure)
        : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateFailureListener
    {
        public void OnConsentInfoUpdateFailure(FormError error) => onFailure(error);
    }

    private sealed class ConsentFormDismissed(Action<FormError?> onDismissed)
        : Java.Lang.Object, IConsentFormOnConsentFormDismissedListener
    {
        public void OnConsentFormDismissed(FormError? error) => onDismissed(error);
    }
}
