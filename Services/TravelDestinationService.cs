using System.Text.Json.Nodes;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Resolves a TravelDestination to a full DestinationInfo by:
///   1. Querying the POTA API for park references (K-1234 format)
///   2. Fetching a Wikipedia summary + thumbnail for the name/location
///   3. Computing ham-relevant overlays: distance, day/night, solar sunrise/sunset
/// </summary>
public class TravelDestinationService(HttpClient http, PoTaService pota)
{
    private readonly Dictionary<string, DestinationInfo?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<List<DestinationInfo>> ResolveAllAsync(
        List<TravelDestination> destinations, double homeLat, double homeLon, string callsign)
    {
        var tasks  = destinations.Select(d => ResolveAsync(d, homeLat, homeLon, callsign));
        var results = await Task.WhenAll(tasks);
        return results.OfType<DestinationInfo>().ToList();
    }

    public async Task<DestinationInfo?> ResolveAsync(
        TravelDestination dest, double homeLat, double homeLon, string callsign)
    {
        var key = dest.Input.Trim().ToUpperInvariant();
        if (_cache.TryGetValue(key, out var cached))
        {
            // Re-compute distance in case home QTH changed
            if (cached is not null)
                cached.DistanceKm = HaversineKm(homeLat, homeLon, cached.Latitude, cached.Longitude);
            return cached;
        }

        try
        {
            DestinationInfo? info;

            if (dest.IsPota)
            {
                info = await ResolvePotaAsync(dest, callsign);
            }
            else
            {
                info = await ResolveWikipediaAsync(dest);
            }

            if (info is null) { _cache[key] = null; return null; }

            // Always enrich with Wikipedia image/description if not already set
            if (string.IsNullOrEmpty(info.ImageUrl) || string.IsNullOrEmpty(info.Description))
            {
                var wikiTerm = string.IsNullOrEmpty(info.Name) ? dest.Input : info.Name;
                var wiki = await FetchWikipediaAsync(wikiTerm);
                if (wiki is not null)
                {
                    if (string.IsNullOrEmpty(info.ImageUrl))     info.ImageUrl    = wiki.ImageUrl;
                    if (string.IsNullOrEmpty(info.Description))  info.Description = wiki.Description;
                    if (info.Latitude == 0 && wiki.Latitude != 0)
                    {
                        info.Latitude  = wiki.Latitude;
                        info.Longitude = wiki.Longitude;
                        if (string.IsNullOrEmpty(info.Grid))
                            info.Grid = SettingsService.LatLonToMaidenhead(wiki.Latitude, wiki.Longitude)[..4];
                    }
                }
            }

            // Ham overlays — only if we have coordinates
            if (info.Latitude != 0 || info.Longitude != 0)
            {
                info.DistanceKm = HaversineKm(homeLat, homeLon, info.Latitude, info.Longitude);
                ComputeSolar(info);
            }

            info.Source = dest;
            _cache[key] = info;
            return info;
        }
        catch
        {
            _cache[key] = null;
            return null;
        }
    }

    // ── POTA ──────────────────────────────────────────────────────────────────

    private async Task<DestinationInfo?> ResolvePotaAsync(TravelDestination dest, string callsign)
    {
        var info = await pota.GetParkAsync(dest.PotaRef);
        if (info is null) return null;

        if (!string.IsNullOrEmpty(callsign))
            info.ActivatorCount = await pota.GetActivationCountAsync(callsign);

        return info;
    }

    // ── Wikipedia ────────────────────────────────────────────────────────────

    private async Task<DestinationInfo?> ResolveWikipediaAsync(TravelDestination dest)
    {
        var wiki = await FetchWikipediaAsync(dest.Input.Trim());
        if (wiki is null) return null;

        return new DestinationInfo
        {
            Name        = wiki.Title,
            Description = wiki.Description,
            ImageUrl    = wiki.ImageUrl,
            Latitude    = wiki.Latitude,
            Longitude   = wiki.Longitude,
            Grid        = (wiki.Latitude != 0 || wiki.Longitude != 0)
                            ? SettingsService.LatLonToMaidenhead(wiki.Latitude, wiki.Longitude)[..4]
                            : ""
        };
    }

    private record WikiResult(string Title, string Description, string? ImageUrl, double Latitude, double Longitude);

    private async Task<WikiResult?> FetchWikipediaAsync(string term)
    {
        try
        {
            var encoded = Uri.EscapeDataString(term.Replace(' ', '_'));
            var url  = $"https://en.wikipedia.org/api/rest_v1/page/summary/{encoded}";
            var json = await http.GetStringAsync(url);
            var n    = JsonNode.Parse(json);

            if (n?["type"]?.GetValue<string>() == "https://mediawiki.org/wiki/HyperSwitch/errors/not_found")
                return null;

            var title   = n?["title"]?.GetValue<string>()   ?? term;
            var extract = n?["extract"]?.GetValue<string>()  ?? "";
            // Trim description to 2 sentences max
            var description = TrimToSentences(extract, 2);
            var imageUrl = n?["thumbnail"]?["source"]?.GetValue<string>();

            double lat = 0, lon = 0;
            if (n?["coordinates"] is JsonNode coords)
            {
                double.TryParse(coords["lat"]?.ToString(), out lat);
                double.TryParse(coords["lon"]?.ToString(), out lon);
            }

            return new WikiResult(title, description, imageUrl, lat, lon);
        }
        catch { return null; }
    }

    // ── Solar overlay ─────────────────────────────────────────────────────────

    private static void ComputeSolar(DestinationInfo info)
    {
        const double Deg2Rad = Math.PI / 180;
        var utcNow = DateTime.UtcNow;
        int doy  = utcNow.DayOfYear;
        double b   = (360.0 / 365) * (doy - 81) * Deg2Rad;
        double et  = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b);
        double dec = 23.45 * Math.Sin(b) * Deg2Rad;
        double ha  = Math.Acos(Math.Clamp(-Math.Tan(info.Latitude * Deg2Rad) * Math.Tan(dec), -1, 1)) / Deg2Rad;
        double noonUtc = 12.0 - info.Longitude / 15.0 - et / 60.0;

        double sunriseUtcH = noonUtc - ha / 15.0;
        double sunsetUtcH  = noonUtc + ha / 15.0;

        // Current solar offset
        double currentH = utcNow.Hour + utcNow.Minute / 60.0;
        double offset   = currentH - noonUtc;
        if (offset >  12) offset -= 24;
        if (offset < -12) offset += 24;
        info.IsDay = Math.Abs(offset) < ha / 15.0;

        info.SunriseUtc = FormatHour(sunriseUtcH);
        info.SunsetUtc  = FormatHour(sunsetUtcH);
    }

    private static string FormatHour(double h)
    {
        h = ((h % 24) + 24) % 24;
        return $"{(int)h:D2}:{(int)((h % 1) * 60):D2}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static string TrimToSentences(string text, int maxSentences)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sentences = text.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        var count = Math.Min(maxSentences, sentences.Length);
        return string.Join(". ", sentences[..count]).TrimEnd('.') + ".";
    }
}
