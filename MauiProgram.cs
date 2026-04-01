using GRRadio.Services;
using Microsoft.Extensions.Logging;

namespace GRRadio;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// GRRadio services
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<PhraseService>();
		builder.Services.AddSingleton<DailyReportService>();
		builder.Services.AddHttpClient<SolarWeatherService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});
		builder.Services.AddHttpClient<SatelliteService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
