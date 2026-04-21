using System.Text.Json.Nodes;
using System.Xml.Linq;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Fetches HF band conditions.
/// Primary:  grrwx.atomicbliss.dev API (physics + spot blending, per-location)
/// Fallback: N0NBH/HamQSL solar XML feed
/// </summary>
public class HfConditionsService(IHttpClientFactory httpFactory)
{
    private const string GrrwxUrl      = "https://grrwx.atomicbliss.dev/api/conditions";
    private const string HamQslUrl     = "https://www.hamqsl.com/solarxml.php";
    private const string ClientName    = "hfconditions";
    private static readonly TimeSpan PrimaryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(60);

    private List<BandCondition>? _cached;
    private double _cachedLat = double.NaN;
    private double _cachedLon = double.NaN;
    private DateTime _fetchedAt = DateTime.MinValue;

    // ── Public API ─────────────────────────────────────────────────────────────

    public void InvalidateCache()
    {
        _cached    = null;
        _fetchedAt = DateTime.MinValue;
    }

    public async Task<List<BandCondition>> GetBandConditionsAsync(double lat, double lon)
    {
        bool locationChanged = Math.Abs(lat - _cachedLat) > 0.5 || Math.Abs(lon - _cachedLon) > 0.5;

        if (_cached is not null && !locationChanged && DateTime.UtcNow - _fetchedAt < CacheDuration)
            return _cached;

        var result = await TryGrrwxAsync(lat, lon)
                  ?? await TryHamQslAsync(lat, lon);

        _cached    = result;
        _cachedLat = lat;
        _cachedLon = lon;
        _fetchedAt = DateTime.UtcNow;
        return result;
    }

    // ── Primary: grrwx.atomicbliss.dev ────────────────────────────────────────

    private async Task<List<BandCondition>?> TryGrrwxAsync(double lat, double lon)
    {
        try
        {
            using var cts  = new CancellationTokenSource(PrimaryTimeout);
            var url        = $"{GrrwxUrl}?lat={lat:F4}&lon={lon:F4}";
            var client     = httpFactory.CreateClient(ClientName);
            var json       = await client.GetStringAsync(url, cts.Token);
            var root = JsonNode.Parse(json);
            var bands = root?["bands"]?.AsArray();
            if (bands is null || bands.Count == 0) return null;

            var conditions = new List<BandCondition>();
            foreach (var b in bands)
            {
                if (b is null) continue;
                var band   = b["band"]?.GetValue<string>() ?? "";
                var freq   = b["frequencyRange"]?.GetValue<string>() ?? "";
                var rating = ParseGrrwxRating(b["rating"]?.GetValue<string>());
                var mode   = b["bestMode"]?.GetValue<string>() ?? "";
                var hint   = b["timeHint"]?.GetValue<string>();
                var notes  = b["notes"]?.GetValue<string>();
                var paths  = b["openPaths"]?.AsArray()
                              .Select(p => p?.GetValue<string>() ?? "")
                              .Where(p => p != "")
                              .ToList() ?? [];

                var summary = notes ?? hint ?? SummaryFromRating(band, rating);

                conditions.Add(new BandCondition
                {
                    Band           = band,
                    FrequencyRange = freq,
                    Rating         = rating,
                    Summary        = summary,
                    GoodRegions    = paths,
                    BestMode       = mode,
                    TimeOfDay      = hint ?? "",
                    IsNightBand    = band is "40m" or "60m" or "80m" or "160m",
                    IsHighBand     = band is "6m"  or "10m" or "12m" or "15m",
                });
            }

            return conditions.Count > 0 ? conditions : null;
        }
        catch
        {
            return null;
        }
    }

    private static BandRating ParseGrrwxRating(string? s) => s switch
    {
        "Excellent" => BandRating.Excellent,
        "Good"      => BandRating.Good,
        "Fair"      => BandRating.Fair,
        "Poor"      => BandRating.Poor,
        _           => BandRating.Closed,
    };

    // ── Fallback: HamQSL XML ───────────────────────────────────────────────────

    private async Task<List<BandCondition>> TryHamQslAsync(double lat, double lon)
    {
        double sfi = 100, kp = 2;
        Dictionary<string, (string Day, string Night)> rawGroups = new()
        {
            ["80m-40m"] = ("Fair", "Good"),
            ["30m-17m"] = ("Fair", "Fair"),
            ["15m-10m"] = ("Fair", "Poor"),
        };

        try
        {
            var xml = await httpFactory.CreateClient(ClientName).GetStringAsync(HamQslUrl);
            var doc = XDocument.Parse(xml);
            var sd  = doc.Root!.Element("solardata")!;

            if (double.TryParse(sd.Element("solarflux")?.Value, out var s)) sfi = s;
            if (double.TryParse(sd.Element("kindex")?.Value,    out var k)) kp  = k;

            var calc = sd.Element("calculatedconditions");
            if (calc is not null)
            {
                rawGroups = calc.Elements("band")
                    .GroupBy(e => e.Attribute("name")?.Value ?? "")
                    .Where(g => g.Key != "")
                    .ToDictionary(
                        g => g.Key,
                        g => (
                            Day:   g.FirstOrDefault(e => e.Attribute("time")?.Value == "day")?.Value   ?? "Fair",
                            Night: g.FirstOrDefault(e => e.Attribute("time")?.Value == "night")?.Value ?? "Fair"
                        )
                    );
            }
        }
        catch { /* use defaults */ }

        bool isDay = IsLocalDay(DateTime.UtcNow, lat, lon);

        string Raw(string group) =>
            rawGroups.TryGetValue(group, out var v) ? (isDay ? v.Day : v.Night) : "Fair";

        BandRating ParseRating(string condition, bool canExcellent = false) => condition switch
        {
            "Good"        => canExcellent && sfi > 150 && kp < 2 ? BandRating.Excellent : BandRating.Good,
            "Fair"        => BandRating.Fair,
            "Poor"        => BandRating.Poor,
            "Band Closed" => BandRating.Closed,
            _             => BandRating.Fair,
        };

        BandRating Get6m()
        {
            if (!isDay) return BandRating.Closed;
            if (sfi > 180 && kp < 3) return BandRating.Good;
            if (DateTime.UtcNow.Month is >= 5 and <= 8) return BandRating.Fair;
            return BandRating.Closed;
        }

        var high = Raw("15m-10m");
        var mid  = Raw("30m-17m");
        var low  = Raw("80m-40m");

        return
        [
            MakeBand("6m",  "50.0–54.0 MHz",            false, true,  Get6m()),
            MakeBand("10m", "28.0–29.7 MHz",             false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("12m", "24.890–24.990 MHz",         false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("15m", "21.0–21.45 MHz",            false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("17m", "18.068–18.168 MHz",         false, false, ParseRating(mid)),
            MakeBand("20m", "14.0–14.35 MHz",            false, false, ParseRating(mid, canExcellent: true)),
            MakeBand("30m", "10.1–10.15 MHz",            false, false, ParseRating(mid)),
            MakeBand("40m", "7.0–7.3 MHz",               true,  false, ParseRating(low)),
            MakeBand("80m", "3.5–4.0 MHz",               true,  false, ParseRating(low)),
        ];
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BandCondition MakeBand(
        string name, string freq, bool isNight, bool isHigh, BandRating rating)
    {
        var (summary, regions, mode) = BandDetails(name, rating);
        return new BandCondition
        {
            Band           = name,
            FrequencyRange = freq,
            Rating         = rating,
            Summary        = summary,
            GoodRegions    = regions,
            BestMode       = mode,
            IsNightBand    = isNight,
            IsHighBand     = isHigh,
        };
    }

    private static string SummaryFromRating(string band, BandRating rating)
    {
        var (summary, _, _) = BandDetails(band, rating);
        return summary;
    }

    private static (string Summary, List<string> Regions, string Mode) BandDetails(
        string band, BandRating rating) => band switch
    {
        "6m"  => rating switch
        {
            BandRating.Good => ("F2 or E-skip opening", ["NA", "EU"], "SSB/FT8"),
            BandRating.Fair => ("Sporadic-E possible", ["NA"], "FT8"),
            _               => ("Closed — check back later", [], ""),
        },
        "10m" => rating switch
        {
            BandRating.Excellent => ("Worldwide openings", ["EU", "AF", "SA", "JA", "VK"], "SSB/FT8"),
            BandRating.Good      => ("Long paths open", ["EU", "SA", "JA"], "SSB/FT8"),
            BandRating.Fair      => ("Short skip possible", ["NA", "SA"], "FT8"),
            BandRating.Poor      => ("Marginal — digital modes only", [], "FT8"),
            _                    => ("Closed — low solar flux", [], ""),
        },
        "12m" => rating switch
        {
            BandRating.Excellent => ("Strong DX openings", ["EU", "AF", "SA"], "SSB/FT8"),
            BandRating.Good      => ("DX paths open", ["EU", "SA"], "SSB/FT8"),
            BandRating.Fair      => ("Some DX possible", ["SA", "NA"], "FT8"),
            _                    => ("Marginal conditions", [], "FT8"),
        },
        "15m" => rating switch
        {
            BandRating.Excellent => ("Trans-Atlantic wide open", ["EU", "AF", "SA"], "SSB/FT8/CW"),
            BandRating.Good      => ("Regional DX open", ["NA", "SA", "EU"], "SSB/FT8"),
            BandRating.Fair      => ("Marginal — try FT8", ["NA", "SA"], "FT8"),
            _                    => ("Closed at night/low SFI", [], ""),
        },
        "17m" => rating switch
        {
            BandRating.Good => ("Reliable DX band", ["EU", "SA", "JA", "AF"], "SSB/FT8/CW"),
            BandRating.Fair => ("Some DX possible", ["NA", "SA", "EU"], "FT8"),
            BandRating.Poor => ("Disturbed — try later", [], "FT8"),
            _               => ("Closed", [], ""),
        },
        "20m" => rating switch
        {
            BandRating.Excellent => ("King of DX — wide open", ["EU", "AF", "SA", "JA", "VK"], "SSB/FT8/CW"),
            BandRating.Good      => ("Open with good DX", ["EU", "NA", "SA"], "SSB/FT8/CW"),
            BandRating.Fair      => ("Reliable with some absorption", ["NA", "SA"], "FT8/CW"),
            BandRating.Poor      => ("Disturbed — weak signals", ["NA"], "FT8"),
            _                    => ("Very disturbed", [], "FT8"),
        },
        "30m" => rating switch
        {
            BandRating.Good => ("Excellent overnight DX", ["EU", "AF", "JA"], "FT8/JS8 (Digital only)"),
            BandRating.Fair => ("Reliable — low noise floor", ["NA", "SA"], "FT8 (Digital only)"),
            _               => ("Storm — NVIS paths only", ["NA"], "FT8 (Digital only)"),
        },
        "40m" => rating switch
        {
            BandRating.Excellent => ("Wide-open DX at night", ["EU", "AF", "JA", "VK"], "SSB/CW/FT8"),
            BandRating.Good      => ("Good DX — some noise", ["EU", "NA"], "CW/FT8"),
            BandRating.Fair      => ("Regional — NVIS paths", ["NA regional"], "SSB/CW"),
            _                    => ("Noisy — local only", ["Local"], "SSB"),
        },
        "80m" => rating switch
        {
            BandRating.Good => ("Regional DX overnight", ["NA", "EU"], "CW/FT8"),
            BandRating.Fair => ("Regional — noisy", ["NA"], "CW/FT8"),
            _               => ("Daytime — local only", ["Local"], "SSB"),
        },
        _ => ("No data", [], ""),
    };

    private static bool IsLocalDay(DateTime utc, double lat, double lon)
    {
        const double Deg2Rad = Math.PI / 180;
        int    doy  = utc.DayOfYear;
        double b    = (360.0 / 365) * (doy - 81) * Deg2Rad;
        double et   = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b);
        double dec  = 23.45 * Math.Sin(b) * Deg2Rad;
        double ha   = Math.Acos(Math.Clamp(-Math.Tan(lat * Deg2Rad) * Math.Tan(dec), -1, 1)) / Deg2Rad;
        double noon = 12.0 - lon / 15.0 - et / 60.0;
        double hour = utc.Hour + utc.Minute / 60.0;
        double offset = hour - noon;
        if (offset >  12) offset -= 24;
        if (offset < -12) offset += 24;
        return Math.Abs(offset) < ha / 15.0;
    }
}
