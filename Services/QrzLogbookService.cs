using System.Text.RegularExpressions;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Accesses the QRZ Logbook API (requires user-supplied API key).
/// Endpoint: https://logbook.qrz.com/api  (POST, form-encoded)
/// </summary>
public class QrzLogbookService(HttpClient http)
{
    private const string ApiUrl = "https://logbook.qrz.com/api";

    private QrzStats? _cache;
    private string    _cachedKey  = "";
    private DateTime  _cacheUntil = DateTime.MinValue;

    // ── Public API ────────────────────────────────────────────────

    public async Task<QrzStats?> GetStatsAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        if (_cache is not null && apiKey == _cachedKey && DateTime.UtcNow < _cacheUntil)
            return _cache;

        try
        {
            var count   = await FetchCountAsync(apiKey);
            var lastQso = await FetchLastQsoAsync(apiKey);

            _cache = new QrzStats
            {
                TotalQsos = count,
                LastCall  = lastQso?.call,
                LastBand  = lastQso?.band,
                LastMode  = lastQso?.mode,
                LastDate  = lastQso?.date
            };
            _cachedKey  = apiKey;
            _cacheUntil = DateTime.UtcNow.AddMinutes(30);
            return _cache;
        }
        catch
        {
            return null;
        }
    }

    /// Validates the API key and returns a human-readable result.
    public async Task<(bool Valid, string Message)> ValidateAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Enter an API key first.");
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["KEY"]    = apiKey,
                ["ACTION"] = "STATUS"
            });
            var resp = await http.PostAsync(ApiUrl, form);
            var body = await resp.Content.ReadAsStringAsync();

            if (body.Contains("RESULT=AUTH", StringComparison.OrdinalIgnoreCase))
                return (false, "Invalid API key — check QRZ.com → Logbook → Settings");
            if (body.Contains("RESULT=FAIL", StringComparison.OrdinalIgnoreCase))
                return (false, "QRZ returned an error — try again");
            if (body.Contains("RESULT=OK", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(body, @"COUNT=(\d+)", RegexOptions.IgnoreCase);
                var count = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                _cacheUntil = DateTime.MinValue;  // force refresh on next load
                return (true, count > 0 ? $"Connected — {count:N0} QSOs in logbook" : "Connected to QRZ Logbook");
            }
            return (false, "Unexpected response from QRZ");
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    // ── Internals ─────────────────────────────────────────────────

    /// STATUS action — returns the total QSO count for the API key.
    private async Task<int> FetchCountAsync(string apiKey)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["KEY"]    = apiKey,
            ["ACTION"] = "STATUS"
        });

        var resp = await http.PostAsync(ApiUrl, form);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();

        if (!body.Contains("RESULT=OK", StringComparison.OrdinalIgnoreCase)) return 0;

        var m = Regex.Match(body, @"COUNT=(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// FETCH action — returns the most recent QSO from the logbook.
    private async Task<(string call, string band, string mode, DateTime? date)?> FetchLastQsoAsync(string apiKey)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["KEY"]    = apiKey,
            ["ACTION"] = "FETCH"
        });

        var resp = await http.PostAsync(ApiUrl, form);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();

        if (!body.Contains("RESULT=OK", StringComparison.OrdinalIgnoreCase)) return null;

        var adifIdx = body.IndexOf("ADIF=", StringComparison.OrdinalIgnoreCase);
        if (adifIdx < 0) return null;
        var adif = body[(adifIdx + 5)..];

        var records = adif.Split("<EOR>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (records.Length == 0) return null;

        // QRZ returns records newest-first, so first record is most recent
        var rec     = records[0];
        var call    = AdifField(rec, "CALL");
        var band    = AdifField(rec, "BAND");
        var mode    = AdifField(rec, "MODE");
        var dateStr = AdifField(rec, "QSO_DATE");

        if (string.IsNullOrEmpty(call)) return null;

        DateTime? date = null;
        if (dateStr is { Length: 8 } &&
            DateTime.TryParseExact(dateStr, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            date = dt;

        return (call, band ?? "?", mode ?? "?", date);
    }

    // ── ADIF field extractor ──────────────────────────────────────

    private static string? AdifField(string record, string name) =>
        Regex.Match(record, $@"<{Regex.Escape(name)}:\d+>([^<]*)", RegexOptions.IgnoreCase) is { Success: true } m
            ? m.Groups[1].Value.Trim()
            : null;
}
