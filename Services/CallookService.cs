using GRRadio.Models;
using System.Text.Json.Nodes;

namespace GRRadio.Services;

/// <summary>
/// Looks up US amateur radio license data from callook.info (free, no auth).
/// Returns null for non-US or unlisted callsigns.
/// </summary>
public class CallookService(HttpClient http)
{
    private readonly Dictionary<string, CallookData?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<CallookData?> LookupAsync(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return null;

        callsign = callsign.Trim().ToUpperInvariant();

        if (_cache.TryGetValue(callsign, out var cached))
            return cached;

        try
        {
            var json = await http.GetStringAsync($"https://callook.info/{callsign}/json");
            var node = JsonNode.Parse(json);

            if (node?["status"]?.GetValue<string>() is not "VALID")
            {
                _cache[callsign] = null;
                return null;
            }

            var data = new CallookData
            {
                Callsign   = node["current"]?["callsign"]?.GetValue<string>() ?? callsign,
                OperClass  = node["current"]?["operClass"]?.GetValue<string>() ?? "",
                Name       = node["name"]?.GetValue<string>() ?? "",
                GrantDate  = node["otherInfo"]?["grantDate"]?.GetValue<string>() ?? "",
                GridSquare = node["location"]?["gridsquare"]?.GetValue<string>() ?? "",
                Country    = node["address"]?["country"]?.GetValue<string>() ?? ""
            };

            _cache[callsign] = data;
            return data;
        }
        catch
        {
            _cache[callsign] = null;
            return null;
        }
    }
}
