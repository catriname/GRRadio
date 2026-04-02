using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace GRRadio;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Allow WebView to receive env(safe-area-inset-*) for status/nav bar padding
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);
    }
}
