using GRRadio.Models;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GRRadio.Services;

public class SolarWeatherService(IHttpClientFactory httpFactory)
{
    private HttpClient Http => httpFactory.CreateClient("solarweather");

    private SolarData? _currentData;
    private List<ForecastEntry>? _forecast;
    private DateTime _forecastFetchedAt = DateTime.MinValue;
    private List<SpaceWeatherAlert>? _alerts;
    private DateTime _alertsFetchedAt = DateTime.MinValue;

    private static readonly TimeSpan CurrentTtl  = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan AlertsTtl   = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ForecastTtl = TimeSpan.FromHours(3);

    // ── Current Conditions ────────────────────────────────────────────────────

    public void InvalidateCache()
    {
        _currentData       = null;
        _forecast          = null;
        _forecastFetchedAt = DateTime.MinValue;
        _alerts            = null;
        _alertsFetchedAt   = DateTime.MinValue;
    }

    public async Task<SolarData> RefreshAsync()
    {
        InvalidateCache();
        return await GetCurrentAsync();
    }

    public async Task<SolarData> GetCurrentAsync()
    {
        if (_currentData is not null && DateTime.UtcNow - _currentData.FetchedAt < CurrentTtl)
            return _currentData;

        var sfi  = await FetchSolarFluxAsync();
        var kIdx = await FetchKIndexAsync();
        var (windSpeeds, bzValues) = await FetchSolarWindFullAsync();

        _currentData = new SolarData
        {
            SolarFluxIndex = sfi,
            KIndex         = kIdx,
            SolarWindSpeed = windSpeeds.Length > 0 ? windSpeeds[^1] : 400,
            BzComponent    = bzValues.Length > 0 ? bzValues[^1] : 0,
            FetchedAt      = DateTime.UtcNow
        };

        return _currentData;
    }

    private async Task<double> FetchSolarFluxAsync()
    {
        try
        {
            var url = "https://services.swpc.noaa.gov/json/solar-cycle/observed-solar-cycle-indices.json";
            var json = await Http.GetStringAsync(url);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null || arr.Count == 0) return 100;
            var last = arr[^1];
            return last?["f10.7"]?.GetValue<double>() ?? 100;
        }
        catch { return 100; }
    }

    private async Task<double> FetchKIndexAsync()
    {
        try
        {
            var url = "https://services.swpc.noaa.gov/json/planetary_k_index_1m.json";
            var json = await Http.GetStringAsync(url);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null || arr.Count == 0) return 2;
            var last = arr[^1];
            return last?["kp_index"]?.GetValue<double>() ?? 2;
        }
        catch { return 2; }
    }

    private async Task<(double[] speeds, double[] bz)> FetchSolarWindFullAsync()
    {
        try
        {
            var url = "https://services.swpc.noaa.gov/json/rtsw/rtsw_mag_1m.json";
            var json = await Http.GetStringAsync(url);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return ([], []);

            var speeds = arr
                .Select(n => n?["speed"]?.GetValue<double>() ?? 0)
                .Where(v => v > 0)
                .ToArray();

            var bz = arr
                .Select(n => n?["bz_gsm"]?.GetValue<double>() ?? 0)
                .ToArray();

            return (speeds, bz);
        }
        catch { return ([], []); }
    }

    // ── 3-Day Forecast ────────────────────────────────────────────────────────

    public async Task<List<ForecastEntry>> GetForecastAsync()
    {
        if (_forecast is not null && DateTime.UtcNow - _forecastFetchedAt < ForecastTtl)
            return _forecast;

        _forecast          = await FetchForecastAsync();
        _forecastFetchedAt = DateTime.UtcNow;
        return _forecast;
    }

    private async Task<List<ForecastEntry>> FetchForecastAsync()
    {
        var entries = new List<ForecastEntry>();

        try
        {
            // NOAA 3-day forecast text
            var url = "https://services.swpc.noaa.gov/text/3-day-forecast.txt";
            var text = await Http.GetStringAsync(url);
            entries.AddRange(Parse3DayForecast(text));
        }
        catch { /* fall through to fallback */ }

        // Fallback: generate synthetic forecast from current conditions
        if (entries.Count == 0 && _currentData is not null)
            entries.AddRange(GenerateSyntheticForecast(_currentData));

        return entries;
    }

    private static List<ForecastEntry> Parse3DayForecast(string text)
    {
        var entries = new List<ForecastEntry>();
        var lines = text.Split('\n');

        // Look for Radio Blackout and Geomagnetic Storm sections
        double[] kForecast = [2, 2, 2]; // day1, day2, day3
        double sfi = 100;

        // Parse "Geomagnetic Activity Forecast" K-values
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Solar flux line: "10.7 cm Radio Flux:  123  125  127"
            if (line.Contains("10.7 cm") && line.Contains("Flux"))
            {
                var nums = Regex.Matches(line, @"\b(\d{2,3})\b");
                for (int d = 0; d < Math.Min(3, nums.Count); d++)
                    if (double.TryParse(nums[d].Value, out var v))
                        sfi = d == 0 ? v : sfi; // just take day1 for now — we'll use it for all

                double[] sfis = new double[3];
                for (int d = 0; d < Math.Min(3, nums.Count); d++)
                    if (double.TryParse(nums[d].Value, out var sv)) sfis[d] = sv;

                for (int d = 0; d < 3; d++)
                {
                    var dt = DateTime.UtcNow.Date.AddDays(d);
                    for (int h = 0; h < 24; h += 3)
                    {
                        var existing = entries.FirstOrDefault(e => e.Time == dt.AddHours(h));
                        if (existing is not null)
                            existing.PredictedSfi = sfis[d] > 0 ? sfis[d] : 100;
                    }
                }
            }

            // K-index forecast: lines like "Kp  3  3  4  3  3  2  2  2"  (3-hr periods)
            if (Regex.IsMatch(line, @"^Kp\s+\d") || (line.Contains("Kp Index:") && !line.Contains("Active")))
            {
                var nums = Regex.Matches(line, @"\b([0-9])\b");
                for (int k = 0; k < Math.Min(8, nums.Count); k++)
                {
                    if (double.TryParse(nums[k].Value, out var kv))
                    {
                        var dayIdx = k / 8;
                        if (dayIdx < 3) kForecast[dayIdx] = Math.Max(kForecast[dayIdx], kv);
                    }
                }
            }
        }

        // Build 3-hour interval entries for 3 days
        for (int day = 0; day < 3; day++)
        {
            for (int h = 0; h < 24; h += 3)
            {
                entries.Add(new ForecastEntry
                {
                    Time             = DateTime.UtcNow.Date.AddDays(day).AddHours(h),
                    PredictedSfi     = sfi,
                    PredictedKIndex  = kForecast[day],
                    Period           = $"day{day + 1}"
                });
            }
        }

        return entries;
    }

    private static List<ForecastEntry> GenerateSyntheticForecast(SolarData current)
    {
        var entries = new List<ForecastEntry>();
        for (int day = 0; day < 3; day++)
        {
            for (int h = 0; h < 24; h += 3)
            {
                // Simple decay model: conditions tend to normalize over time
                var kNoise = (new Random().NextDouble() - 0.5) * 1.5;
                entries.Add(new ForecastEntry
                {
                    Time            = DateTime.UtcNow.Date.AddDays(day).AddHours(h),
                    PredictedSfi    = current.SolarFluxIndex + (new Random().NextDouble() - 0.5) * 10,
                    PredictedKIndex = Math.Max(0, Math.Min(9, current.KIndex + kNoise + (day * -0.3))),
                    Period          = $"day{day + 1}"
                });
            }
        }
        return entries;
    }

    // ── Alerts ────────────────────────────────────────────────────────────────

    public async Task<List<SpaceWeatherAlert>> GetAlertsAsync()
    {
        if (_alerts is not null && DateTime.UtcNow - _alertsFetchedAt < AlertsTtl)
            return _alerts;

        _alerts          = await FetchAlertsAsync();
        _alertsFetchedAt = DateTime.UtcNow;
        return _alerts;
    }

    private async Task<List<SpaceWeatherAlert>> FetchAlertsAsync()
    {
        try
        {
            var url = "https://services.swpc.noaa.gov/products/alerts.json";
            var json = await Http.GetStringAsync(url);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return [];

            return arr
                .Where(n => n is not null)
                .Select(n => new SpaceWeatherAlert
                {
                    ProductId = n!["product_id"]?.GetValue<string>() ?? "",
                    IssueTime = n["issue_datetime"]?.GetValue<string>() ?? "",
                    Message   = n["message"]?.GetValue<string>() ?? "",
                    Severity  = ParseAlertSeverity(n["product_id"]?.GetValue<string>() ?? "")
                })
                .Where(a => !string.IsNullOrEmpty(a.Message))
                .Take(20)
                .ToList();
        }
        catch { return []; }
    }

    private static AlertSeverity ParseAlertSeverity(string productId) =>
        productId.ToUpperInvariant() switch
        {
            var s when s.Contains("WARNING") => AlertSeverity.Warning,
            var s when s.Contains("WATCH")   => AlertSeverity.Watch,
            var s when s.Contains("ALERT")   => AlertSeverity.Alert,
            _                                 => AlertSeverity.Summary
        };
}
