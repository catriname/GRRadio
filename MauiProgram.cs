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

		// GRRadio services — all singletons so in-memory caches survive tab navigation
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<UIStateService>();
		builder.Services.AddSingleton<BluetoothKissService>();
		builder.Services.AddSingleton<ChatHistoryService>();
		builder.Services.AddSingleton<PhraseService>();
		builder.Services.AddSingleton<DailyReportService>();

		builder.Services.AddSingleton<NewsService>();
		builder.Services.AddHttpClient("news", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio news aggregator; by amateur radio operator)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddSingleton<ClassifiedService>();
		builder.Services.AddHttpClient("classified", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio classifieds)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});

		builder.Services.AddSingleton<SolarWeatherService>();
		builder.Services.AddHttpClient("solarweather", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddSingleton<SatelliteService>();
		builder.Services.AddHttpClient("satellite", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});

		builder.Services.AddSingleton<HfConditionsService>();
		builder.Services.AddHttpClient("hfconditions", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddSingleton<CallookService>();
		builder.Services.AddHttpClient("callook", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio operator app)");
			client.Timeout = TimeSpan.FromSeconds(10);
		});

		builder.Services.AddSingleton<PskReporterService>();

		builder.Services.AddSingleton<PoTaService>();
		builder.Services.AddHttpClient("pota", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio POTA app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddSingleton<TravelDestinationService>();
		builder.Services.AddHttpClient("travel", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio travel app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddSingleton<QrzLogbookService>();
		builder.Services.AddHttpClient("qrzlogbook", client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio logbook app)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
