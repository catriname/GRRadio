using GRRadio.Models;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GRRadio.Services;

public class SatelliteService(IHttpClientFactory httpFactory)
{
    private HttpClient Http => httpFactory.CreateClient("satellite");

    private List<TleParsed>? _tleCache;
    private DateTime _tleFetchedAt = DateTime.MinValue;
    private List<SstvSatelliteStatus>? _sstvCache;
    private DateTime _sstvFetchedAt = DateTime.MinValue;
    private List<SatellitePass>? _passCache;
    private DateTime _passFetchedAt = DateTime.MinValue;
    private string _passCacheKey = string.Empty;

    private const string TleUrl = "http://www.csntechnologies.net/SAT/csnbare.txt";
    private const string SstvStatusUrl = "https://amsat.org/status/api/v1/sat_info.php";

    // ── Cache ─────────────────────────────────────────────────────────────────

    public void InvalidateCache()
    {
        _tleCache      = null;
        _tleFetchedAt  = DateTime.MinValue;
        _sstvCache     = null;
        _sstvFetchedAt = DateTime.MinValue;
        _passCache     = null;
        _passFetchedAt = DateTime.MinValue;
        _passCacheKey  = string.Empty;
    }

    // ── TLE Loading ───────────────────────────────────────────────────────────

    public async Task<List<TleParsed>> GetTlesAsync()
    {
        if (_tleCache is not null && (DateTime.UtcNow - _tleFetchedAt).TotalHours < 12)
            return _tleCache;

        _tleCache = await FetchTlesAsync();
        _tleFetchedAt = DateTime.UtcNow;
        return _tleCache;
    }

    private async Task<List<TleParsed>> FetchTlesAsync()
    {
        try
        {
            var text = await Http.GetStringAsync(TleUrl);
            return ParseTleText(text);
        }
        catch
        {
            return _tleCache ?? [];
        }
    }

    private static List<TleParsed> ParseTleText(string text)
    {
        var parsed = new List<TleParsed>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i + 2 < lines.Length; i++)
        {
            var l0 = lines[i].Trim();
            var l1 = lines[i + 1].Trim();
            var l2 = lines[i + 2].Trim();

            if (l1.StartsWith('1') && l2.StartsWith('2') && l1.Length >= 69 && l2.Length >= 69)
            {
                var tle = TleParsed.Parse(l0, l1, l2);
                if (tle is not null)
                    parsed.Add(tle);
                i += 2;
            }
        }

        return parsed;
    }

    public async Task<List<(int NoradId, string Name)>> GetSatellitesAsync()
    {
        var tles = await GetTlesAsync();
        return tles.Select(t => (t.NoradId, t.Name)).OrderBy(t => t.Name).ToList();
    }

    // ── Pass Prediction ───────────────────────────────────────────────────────

    public async Task<List<SatellitePass>> GetPassesAsync(
        IJSRuntime js,
        HashSet<int> noradIds,
        double lat, double lon, double altKm,
        int minElevDeg,
        int hoursAhead = 72,
        bool forceRefresh = false)
    {
        var cacheKey = $"{string.Join(",", noradIds.OrderBy(id => id))}|{lat:F4}|{lon:F4}|{minElevDeg}|{hoursAhead}";
        if (!forceRefresh && _passCache is not null && _passCacheKey == cacheKey
            && (DateTime.UtcNow - _passFetchedAt).TotalHours < 1)
            return _passCache;

        var tles       = await GetTlesAsync();
        var sstvStatus = await GetSstvStatusAsync();

        var candidates = tles
            .Where(t => noradIds.Contains(t.NoradId))
            .Select(t => new { t.NoradId, satelliteName = t.Name, t.Line1, t.Line2 })
            .ToArray();

        var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var endMs   = DateTimeOffset.UtcNow.AddHours(hoursAhead).ToUnixTimeMilliseconds();

        var jsPasses = await js.InvokeAsync<List<JsPass>>(
            "predictPasses", candidates, lat, lon, altKm, startMs, endMs, minElevDeg);

        var passes = jsPasses.Select(p =>
        {
            var sstv = sstvStatus.FirstOrDefault(s =>
                p.SatelliteName.Contains(s.SatelliteName, StringComparison.OrdinalIgnoreCase));
            return new SatellitePass
            {
                NoradId       = p.NoradId,
                SatelliteName = p.SatelliteName,
                AosTime       = DateTimeOffset.FromUnixTimeMilliseconds(p.AosTime).UtcDateTime,
                TcaTime       = DateTimeOffset.FromUnixTimeMilliseconds(p.TcaTime).UtcDateTime,
                LosTime       = DateTimeOffset.FromUnixTimeMilliseconds(p.LosTime).UtcDateTime,
                MaxElevation  = p.MaxElevation,
                AosAzimuth    = p.AosAzimuth,
                TcaAzimuth    = p.TcaAzimuth,
                LosAzimuth    = p.LosAzimuth,
                IsSstv        = sstv is not null,
                SstvStatus    = sstv?.Status
            };
        }).ToList();

        _passCache     = passes;
        _passFetchedAt = DateTime.UtcNow;
        _passCacheKey  = cacheKey;
        return _passCache;
    }

    private sealed class JsPass
    {
        public int    NoradId       { get; set; }
        public string SatelliteName { get; set; } = "";
        public long   AosTime       { get; set; }
        public long   TcaTime       { get; set; }
        public long   LosTime       { get; set; }
        public double MaxElevation  { get; set; }
        public double AosAzimuth    { get; set; }
        public double TcaAzimuth    { get; set; }
        public double LosAzimuth    { get; set; }
    }

    // ── SSTV Status ───────────────────────────────────────────────────────────

    public async Task<List<SstvSatelliteStatus>> GetSstvStatusAsync()
    {
        if (_sstvCache is not null && (DateTime.UtcNow - _sstvFetchedAt).TotalMinutes < 30)
            return _sstvCache;

        _sstvCache = await FetchSstvStatusAsync();
        _sstvFetchedAt = DateTime.UtcNow;
        return _sstvCache;
    }

    private async Task<List<SstvSatelliteStatus>> FetchSstvStatusAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(SstvStatusUrl);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return [];

            return arr
                .Where(n => n is not null)
                .Select(n => new SstvSatelliteStatus
                {
                    SatelliteName = n!["name"]?.GetValue<string>() ?? "",
                    Status        = n["status"]?.GetValue<string>() ?? "unknown",
                    Frequency     = n["downlink_freq"]?.GetValue<string>() ?? "",
                    Mode          = n["mode"]?.GetValue<string>() ?? "SSTV"
                })
                .Where(s => !string.IsNullOrEmpty(s.SatelliteName))
                .ToList();
        }
        catch { return []; }
    }
}
