using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace PhoneTrackpad.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public static bool LandscapeModeRequested { get; set; }

    const SystemUiFlags FullscreenFlags =
        SystemUiFlags.Fullscreen |
        SystemUiFlags.HideNavigation |
        SystemUiFlags.ImmersiveSticky |
        SystemUiFlags.LayoutStable |
        SystemUiFlags.LayoutFullscreen |
        SystemUiFlags.LayoutHideNavigation;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        LandscapeModeRequested = false;
        base.OnCreate(savedInstanceState);
        ApplyRequestedOrientation();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplyRequestedOrientation();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyRequestedOrientation();
            Android.Views.View? decorView = Window?.DecorView;
            if (decorView is not null)
            {
#pragma warning disable CA1422
                decorView.SystemUiFlags = FullscreenFlags;
#pragma warning restore CA1422
            }
        }
    }

    void ApplyRequestedOrientation() =>
        RequestedOrientation = LandscapeModeRequested
            ? ScreenOrientation.Landscape
            : ScreenOrientation.Portrait;
}
