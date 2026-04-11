using GRRadio.Models;
using System.Text.Json.Nodes;

namespace GRRadio.Services;

/// <summary>
/// Queries PSK Reporter for recent reception reports of a given callsign.
/// No authentication required. Used to find the farthest station that heard you.
/// </summary>
public class PskReporterService(HttpClient http)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private string _cachedCall = "";
    private List<PskSpot>? _cachedSpots;
    private DateTime _fetchedAt = DateTime.MinValue;

    public async Task<List<PskSpot>> GetRecentSpotsAsync(string callsign, int hoursBack = 24)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return [];

        callsign = callsign.Trim().ToUpperInvariant();

        if (_cachedCall == callsign &&
            _cachedSpots is not null &&
            DateTime.UtcNow - _fetchedAt < CacheDuration)
            return _cachedSpots;

        _cachedCall  = callsign;
        _cachedSpots = await FetchAsync(callsign, hoursBack);
        _fetchedAt   = DateTime.UtcNow;
        return _cachedSpots;
    }

    /// <summary>
    /// Returns the single spot with the greatest distance from the sender's grid square.
    /// </summary>
    public async Task<PskSpot?> GetFarthestSpotAsync(string callsign, string senderGrid, int hoursBack = 24)
    {
        var spots = await GetRecentSpotsAsync(callsign, hoursBack);
        if (spots.Count == 0)
            return null;

        var (sLat, sLon) = SettingsService.MaidenheadToLatLon(senderGrid);

        foreach (var spot in spots)
        {
            var (rLat, rLon) = SettingsService.MaidenheadToLatLon(spot.ReceiverLocator);
            spot.DistanceKm = HaversineKm(sLat, sLon, rLat, rLon);
        }

        return spots.MaxBy(s => s.DistanceKm);
    }

    // ── Fetch ──────────────────────────────────────────────────────────────────

    private async Task<List<PskSpot>> FetchAsync(string callsign, int hoursBack)
    {
        try
        {
            var seconds = hoursBack * -3600;
            var url = $"https://retrieve.pskreporter.info/query" +
                      $"?senderCallsign={callsign}" +
                      $"&flowStartSeconds={seconds}" +
                      $"&rronly=1" +
                      $"&format=json";

            var json   = await http.GetStringAsync(url);
            var node   = JsonNode.Parse(json);
            var reports = node?["receptionReports"]?.AsArray();

            if (reports is null)
                return [];

            var spots = new List<PskSpot>();
            foreach (var r in reports)
            {
                if (r is null) continue;

                var locator = r["receiverLocator"]?.GetValue<string>() ?? "";
                if (locator.Length < 4) continue; // need at least 4-char grid

                spots.Add(new PskSpot
                {
                    ReceiverCallsign = r["receiverCallsign"]?.GetValue<string>() ?? "",
                    ReceiverLocator  = locator,
                    SenderLocator    = r["senderLocator"]?.GetValue<string>() ?? "",
                    Frequency        = r["frequency"]?.GetValue<double>() ?? 0,
                    Mode             = r["mode"]?.GetValue<string>() ?? "",
                    SNR              = (int)(r["sNR"]?.GetValue<double>() ?? 0),
                    Timestamp        = r["flowStartSeconds"]?.GetValue<long>() ?? 0
                });
            }

            return spots;
        }
        catch
        {
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
