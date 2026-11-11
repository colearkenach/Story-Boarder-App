using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace StoryBoarder.Android;

[Activity(
    Label = "Story Boarder",
    Theme = "@style/StoryBoarderTheme",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    // Rotating, resizing (split screen, foldables) and dark mode don't restart the app.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.UiMode | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The activity on screen, which ads and the consent form are shown in.</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
        Ads.Start(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Ads.Resume();
    }

    protected override void OnPause()
    {
        Ads.Pause();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (Current == this) Current = null;
        base.OnDestroy();
    }
}
