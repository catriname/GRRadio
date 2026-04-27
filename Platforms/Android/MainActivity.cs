using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;

namespace GRRadio;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        LocalNotificationCenter.CreateNotificationChannels(new List<NotificationChannelRequest>
        {
            new()
            {
                Id          = "sat_alerts_v2",
                Name        = "Satellite Pass Alerts",
                Description = "Alerts before satellite passes begin",
                Sound       = "satellite_alert",
                Importance  = AndroidImportance.High
            }
        });

        _ = LocalNotificationCenter.Current.RequestNotificationPermission();
    }
}
