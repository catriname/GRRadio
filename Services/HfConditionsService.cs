using System.Xml.Linq;
using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Fetches current HF band condition ratings from the N0NBH/HamQSL solar XML feed,
/// which publishes real propagation assessments for 80m-40m, 30m-17m, and 15m-10m.
/// </summary>
public class HfConditionsService(HttpClient http)
{
    private const string FeedUrl = "https://www.hamqsl.com/solarxml.php";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    // Raw band groups from the XML: group name → (day rating, night rating)
    private Dictionary<string, (string Day, string Night)>? _rawGroups;
    private double _sfi = 100;
    private double _kp  = 2;
    private DateTime _fetchedAt = DateTime.MinValue;

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task<List<BandCondition>> GetBandConditionsAsync(double lat, double lon)
    {
        if (_rawGroups is null || DateTime.UtcNow - _fetchedAt > CacheDuration)
            await FetchAsync();

        bool isDay = IsLocalDay(DateTime.UtcNow, lat, lon);
        return BuildConditions(isDay);
    }

    // ── Fetch & Parse ──────────────────────────────────────────────────────────

    private async Task FetchAsync()
    {
        try
        {
            var xml = await http.GetStringAsync(FeedUrl);
            var doc = XDocument.Parse(xml);
            var sd  = doc.Root!.Element("solardata")!;

            if (double.TryParse(sd.Element("solarflux")?.Value, out var sfi)) _sfi = sfi;
            if (double.TryParse(sd.Element("kindex")?.Value,    out var kp))  _kp  = kp;

            var calc = sd.Element("calculatedconditions");
            if (calc is not null)
            {
                _rawGroups = calc.Elements("band")
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

            _fetchedAt = DateTime.UtcNow;
        }
        catch
        {
            // Fallback to neutral conditions so the app still renders
            _rawGroups ??= new Dictionary<string, (string, string)>
            {
                ["80m-40m"] = ("Fair", "Good"),
                ["30m-17m"] = ("Fair", "Fair"),
                ["15m-10m"] = ("Fair", "Poor"),
            };
        }
    }

    // ── Condition Builder ──────────────────────────────────────────────────────

    private List<BandCondition> BuildConditions(bool isDay)
    {
        var t = isDay ? "day" : "night";

        string Raw(string group) =>
            _rawGroups!.TryGetValue(group, out var v)
                ? (isDay ? v.Day : v.Night)
                : "Fair";

        // HamQSL groups: 15m-10m covers the high bands, 30m-17m the middle, 80m-40m the low bands.
        // 20m behaves closer to 30m-17m at its frequency (14 MHz sits just below the group).
        var high   = Raw("15m-10m");
        var mid    = Raw("30m-17m");
        var low    = Raw("80m-40m");

        return
        [
            MakeBand("6m",  "50.0–54.0 MHz",     false, true,  Get6mRating(isDay)),
            MakeBand("10m", "28.0–29.7 MHz",      false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("12m", "24.890–24.990 MHz",  false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("15m", "21.0–21.45 MHz",     false, true,  ParseRating(high, canExcellent: true)),
            MakeBand("17m", "18.068–18.168 MHz",  false, false, ParseRating(mid)),
            MakeBand("20m", "14.0–14.35 MHz",     false, false, ParseRating(mid,  canExcellent: true)),
            MakeBand("30m", "10.1–10.15 MHz",     false, false, ParseRating(mid)),
            MakeBand("40m", "7.0–7.3 MHz",        true,  false, ParseRating(low)),
            MakeBand("80m", "3.5–4.0 MHz",        true,  false, ParseRating(low)),
        ];
    }

    private BandRating Get6mRating(bool isDay)
    {
        if (!isDay) return BandRating.Closed;
        if (_sfi > 180 && _kp < 3) return BandRating.Good;
        if (IsSummerMonth())        return BandRating.Fair;
        return BandRating.Closed;
    }

    /// <summary>
    /// Maps HamQSL's text rating to our enum.
    /// "Good" is promoted to Excellent when SFI and Kp support it on bands that can go Excellent.
    /// </summary>
    private BandRating ParseRating(string condition, bool canExcellent = false) => condition switch
    {
        "Good"        => canExcellent && _sfi > 150 && _kp < 2 ? BandRating.Excellent : BandRating.Good,
        "Fair"        => BandRating.Fair,
        "Poor"        => BandRating.Poor,
        "Band Closed" => BandRating.Closed,
        _             => BandRating.Fair,
    };

    // ── Band Card Info ─────────────────────────────────────────────────────────

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

    // ── Solar Time Helpers ─────────────────────────────────────────────────────

    /// <summary>Returns true if the sun is above the horizon at lat/lon right now.</summary>
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

    private static bool IsSummerMonth()
    {
        var m = DateTime.UtcNow.Month;
        return m is >= 5 and <= 8;
    }
}
