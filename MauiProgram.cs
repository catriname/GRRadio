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
		builder.Services.AddHttpClient<NewsService>(client =>
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
		builder.Services.AddHttpClient<HfConditionsService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio propagation app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});
		builder.Services.AddHttpClient<CallookService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio operator app)");
			client.Timeout = TimeSpan.FromSeconds(10);
		});
		builder.Services.AddHttpClient<PskReporterService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio operator app)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});
		builder.Services.AddHttpClient<PoTaService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio POTA app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});
		builder.Services.AddHttpClient<TravelDestinationService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio travel app)");
			client.Timeout = TimeSpan.FromSeconds(15);
		});
		builder.Services.AddHttpClient<QrzLogbookService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio logbook app)");
			client.Timeout = TimeSpan.FromSeconds(20);
		});
		builder.Services.AddHttpClient<LoTwStatsService>(client =>
		{
			client.DefaultRequestHeaders.Add("User-Agent", "GRRadio/1.0 (ham radio logbook app)");
			client.Timeout = TimeSpan.FromSeconds(30);  // LoTW can be slow
		});

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
