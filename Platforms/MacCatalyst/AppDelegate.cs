using Foundation;
using Plugin.LocalNotification;

namespace GRRadio;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary launchOptions)
	{
		var result = base.FinishedLaunching(application, launchOptions);
		_ = LocalNotificationCenter.Current.RequestNotificationPermission();
		return result;
	}
}
