using System.Text.RegularExpressions;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Fetches confirmed QSO statistics from ARRL's Logbook of the World (LoTW).
/// Queries the lotwreport ADIF endpoint with the user's credentials.
/// </summary>
public class LoTwStatsService(HttpClient http)
{
    private const string BaseUrl = "https://lotw.arrl.org/lotwuser/lotwreport.adi";

    private LoTwStats? _cache;
    private string    _cachedUser  = "";
    private DateTime  _cacheUntil  = DateTime.MinValue;

    // ── Public API ────────────────────────────────────────────────

    public async Task<LoTwStats?> GetStatsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        if (_cache is not null && username == _cachedUser && DateTime.UtcNow < _cacheUntil)
            return _cache;

        try
        {
            var dxccTask = FetchDxccCountAsync(username, password);
            var wasTask  = FetchWasCountAsync(username, password);
            var qsoTask  = FetchTotalConfirmedAsync(username, password);

            await Task.WhenAll(dxccTask, wasTask, qsoTask);

            _cache = new LoTwStats
            {
                ConfirmedDxcc = dxccTask.Result,
                ConfirmedWas  = wasTask.Result,
                ConfirmedQsos = qsoTask.Result
            };
            _cachedUser  = username;
            _cacheUntil  = DateTime.UtcNow.AddMinutes(60);
            return _cache;
        }
        catch
        {
            return null;
        }
    }

    // ── Internals ─────────────────────────────────────────────────

    /// Count unique confirmed DXCC entities by fetching DXCC-grouped confirmed QSOs.
    private async Task<int> FetchDxccCountAsync(string user, string pass)
    {
        var url  = BuildUrl(user, pass, "&qso_dxcc=1&qso_qsl=yes&qso_qslsince=1970-01-01");
        var adif = await FetchAdifAsync(url);
        if (adif is null) return 0;

        // Each confirmed DXCC entity appears as a QSO record with a DXCC field.
        // Count unique DXCC values.
        var entities = Regex.Matches(adif, @"<DXCC:\d+>([^<]+)", RegexOptions.IgnoreCase)
                            .Select(m => m.Groups[1].Value.Trim())
                            .Where(v => !string.IsNullOrEmpty(v))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entities.Count;
    }

    /// Count unique confirmed US states (WAS = 0–50).
    private async Task<int> FetchWasCountAsync(string user, string pass)
    {
        // Filter to QSOs with a STATE field, confirmed, US entities (DXCC 291)
        var url  = BuildUrl(user, pass, "&qso_state=1&qso_qsl=yes&qso_qslsince=1970-01-01&qso_dxcc=291");
        var adif = await FetchAdifAsync(url);
        if (adif is null) return 0;

        var states = Regex.Matches(adif, @"<STATE:\d+>([^<]+)", RegexOptions.IgnoreCase)
                          .Select(m => m.Groups[1].Value.Trim().ToUpperInvariant())
                          .Where(v => v.Length is 2)
                          .ToHashSet();
        return Math.Min(states.Count, 50);
    }

    /// Count total confirmed QSOs (all-time).
    private async Task<int> FetchTotalConfirmedAsync(string user, string pass)
    {
        var url  = BuildUrl(user, pass, "&qso_query=1&qso_qsl=yes&qso_qslsince=1970-01-01");
        var adif = await FetchAdifAsync(url);
        if (adif is null) return 0;

        return Regex.Matches(adif, @"<EOR>", RegexOptions.IgnoreCase).Count;
    }

    private async Task<string?> FetchAdifAsync(string url)
    {
        try
        {
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            // LoTW returns an error line at the top if credentials fail
            if (body.Contains("ARRL LoTW")) return null;  // HTML error page
            return body;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildUrl(string user, string pass, string extra) =>
        $"{BaseUrl}?login={Uri.EscapeDataString(user)}&password={Uri.EscapeDataString(pass)}&qso_query=1{extra}";
}
