using System.Text.Json.Nodes;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Queries the POTA (Parks on the Air) public API — free, no auth required.
/// https://api.pota.app
/// </summary>
public class PoTaService(IHttpClientFactory httpFactory)
{
    private HttpClient Http => httpFactory.CreateClient("pota");

    private readonly Dictionary<string, DestinationInfo?> _parkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _activatorCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Park Lookup ────────────────────────────────────────────────────────────

    public async Task<DestinationInfo?> GetParkAsync(string reference)
    {
        reference = reference.Trim().ToUpperInvariant();

        if (_parkCache.TryGetValue(reference, out var cached))
            return cached;

        try
        {
            var json = await Http.GetStringAsync($"https://api.pota.app/park/{reference}");
            var n    = JsonNode.Parse(json);
            if (n is null) { _parkCache[reference] = null; return null; }

            var info = new DestinationInfo
            {
                PotaReference = n["reference"]?.GetValue<string>()    ?? reference,
                Name          = n["name"]?.GetValue<string>()         ?? reference,
                LocationName  = n["locationName"]?.GetValue<string>() ?? "",
                Grid          = n["grid6"]?.GetValue<string>()        ?? n["grid4"]?.GetValue<string>() ?? ""
            };

            if (double.TryParse(n["latitude"]?.GetValue<string>()  ?? n["latitude"]?.ToString(),  out var lat)) info.Latitude  = lat;
            if (double.TryParse(n["longitude"]?.GetValue<string>() ?? n["longitude"]?.ToString(), out var lon)) info.Longitude = lon;

            // If grid is empty but we have coords, derive it
            if (string.IsNullOrEmpty(info.Grid) && (info.Latitude != 0 || info.Longitude != 0))
                info.Grid = SettingsService.LatLonToMaidenhead(info.Latitude, info.Longitude)[..4];

            _parkCache[reference] = info;
            return info;
        }
        catch
        {
            _parkCache[reference] = null;
            return null;
        }
    }

    // ── Activator Stats ────────────────────────────────────────────────────────

    /// <summary>Returns how many times the given callsign has activated a POTA park.</summary>
    public async Task<int> GetActivationCountAsync(string callsign)
    {
        callsign = callsign.Trim().ToUpperInvariant();
        if (_activatorCache.TryGetValue(callsign, out var count)) return count;

        try
        {
            var json = await Http.GetStringAsync($"https://api.pota.app/stats/user/{callsign}");
            var n    = JsonNode.Parse(json);

            count = n?["activator"]?["activations"]?.GetValue<int>()
                 ?? n?["activations"]?.GetValue<int>()
                 ?? 0;

            _activatorCache[callsign] = count;
            return count;
        }
        catch
        {
            _activatorCache[callsign] = 0;
            return 0;
        }
    }
}
