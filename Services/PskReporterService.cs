using GRRadio.Models;
using Microsoft.JSInterop;
using System.Text.Json;

namespace GRRadio.Services;

/// <summary>
/// Queries PSK Reporter for recent reception reports of a given callsign.
/// PSKReporter blocks server-side HTTP; uses JS JSONP interop (fetchPskReporter)
/// when IJSRuntime is supplied.
/// </summary>
public class PskReporterService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    // Cache keyed on callsign + hoursBack so 72h and 2160h are stored separately
    private readonly Dictionary<string, (List<PskSpot> Spots, DateTime FetchedAt)> _cache = [];

    public async Task<List<PskSpot>> GetRecentSpotsAsync(string callsign, int hoursBack = 24, IJSRuntime? js = null)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return [];

        callsign = callsign.Trim().ToUpperInvariant();
        var key  = $"{callsign}:{hoursBack}";

        if (_cache.TryGetValue(key, out var entry) &&
            DateTime.UtcNow - entry.FetchedAt < CacheDuration)
            return entry.Spots;

        var spots = await FetchAsync(callsign, hoursBack, js);
        _cache[key] = (spots, DateTime.UtcNow);
        return spots;
    }

    /// <summary>
    /// Returns the single spot with the greatest distance from the sender's grid square,
    /// computed from an already-fetched spot list (avoids a second PSKReporter request).
    /// </summary>
    public PskSpot? GetFarthestSpotAsync(string callsign, string senderGrid, List<PskSpot> spots)
    {
        var candidates = spots.Where(s => s.ReceiverLocator.Length >= 4).ToList();
        if (candidates.Count == 0)
            return null;

        var (sLat, sLon) = SettingsService.MaidenheadToLatLon(senderGrid);

        foreach (var spot in candidates)
        {
            var (rLat, rLon) = SettingsService.MaidenheadToLatLon(spot.ReceiverLocator);
            spot.DistanceKm = HaversineKm(sLat, sLon, rLat, rLon);
        }

        return candidates.MaxBy(s => s.DistanceKm);
    }

    // ── Diagnostics ────────────────────────────────────────────────────────────

    public string LastError { get; private set; } = "";

    // ── Fetch ──────────────────────────────────────────────────────────────────

    private async Task<List<PskSpot>> FetchAsync(string callsign, int hoursBack, IJSRuntime? js)
    {
        try
        {
            if (js is null)
            {
                LastError = "JS interop unavailable — PSKReporter requires browser context";
                return [];
            }

            // Receive the full PSKReporter response object so we can inspect all keys
            var data = await js.InvokeAsync<JsonElement>("fetchPskReporter", callsign, hoursBack);

            // Try receptionReport first, then senderSearch, then activeCallsign
            JsonElement reportsEl;
            string sourceKey = "";
            if (data.TryGetProperty("receptionReport", out reportsEl) && reportsEl.GetArrayLength() > 0)
                sourceKey = "receptionReport";
            else if (data.TryGetProperty("senderSearch", out reportsEl) && reportsEl.GetArrayLength() > 0)
                sourceKey = "senderSearch";
            else if (data.TryGetProperty("activeCallsign", out reportsEl) && reportsEl.GetArrayLength() > 0)
                sourceKey = "activeCallsign";
            else
            {
                var keys = string.Join(", ", data.EnumerateObject()
                    .Select(p => $"{p.Name}{(p.Value.ValueKind == JsonValueKind.Array ? ":" + p.Value.GetArrayLength() : "")}"));
                LastError = $"no spots. keys={keys}";
                return [];
            }

            var spots = new List<PskSpot>();
            foreach (var r in reportsEl.EnumerateArray())
            {
                if (sourceKey == "senderSearch")
                {
                    // senderSearch: aggregate entry — has timestamp but no individual receiver info
                    long ts = r.TryGetProperty("recentFlowStartSeconds", out var tsEl) ? tsEl.GetInt64() : 0;
                    if (ts > 0)
                        spots.Add(new PskSpot { Timestamp = ts });
                }
                else
                {
                    // receptionReport / activeCallsign: individual spots
                    var receiverCs = r.TryGetProperty("receiverCallsign", out var rc) ? rc.GetString() ?? ""
                                   : r.TryGetProperty("callsign",         out var cs) ? cs.GetString() ?? "" : "";
                    var receiverLoc = r.TryGetProperty("receiverLocator", out var rl) ? rl.GetString() ?? ""
                                    : r.TryGetProperty("locator",         out var lo) ? lo.GetString() ?? "" : "";

                    spots.Add(new PskSpot
                    {
                        ReceiverCallsign = receiverCs,
                        ReceiverLocator  = receiverLoc,
                        SenderLocator    = r.TryGetProperty("senderLocator",    out var sl) ? sl.GetString() ?? "" : "",
                        Frequency        = r.TryGetProperty("frequency",        out var fr) ? fr.GetDouble() : 0,
                        Mode             = r.TryGetProperty("mode",             out var mo) ? mo.GetString() ?? "" : "",
                        SNR              = r.TryGetProperty("sNR",              out var snr) ? (int)snr.GetDouble() : 0,
                        Timestamp        = r.TryGetProperty("flowStartSeconds", out var ts) ? ts.GetInt64() : 0
                    });
                }
            }

            LastError = spots.Count == 0 ? "no spots parsed" : "";
            return spots;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return [];
        }
    }

    // ── Haversine ──────────────────────────────────────────────────────────────

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
