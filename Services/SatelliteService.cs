using GRRadio.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GRRadio.Services;

public class SatelliteService(HttpClient http)
{
    private List<TleParsed>? _tleCache;
    private DateTime _tleFetchedAt = DateTime.MinValue;
    private List<SstvSatelliteStatus>? _sstvCache;
    private DateTime _sstvFetchedAt = DateTime.MinValue;

    private const string AmsatTleUrl   = "https://www.amsat.org/tle/current/dailytle.txt";
    private const string SstvStatusUrl = "https://amsat.org/status/api/v1/sat_info.php";

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
            var text = await http.GetStringAsync(AmsatTleUrl);
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

    public async Task<List<string>> GetSatelliteNamesAsync()
    {
        var tles = await GetTlesAsync();
        return tles.Select(t => t.Name).OrderBy(n => n).ToList();
    }

    // ── Pass Prediction ───────────────────────────────────────────────────────

    public async Task<List<SatellitePass>> GetPassesAsync(
        List<string> watchlist,
        double lat, double lon, double altKm,
        int minElevDeg,
        int hoursAhead = 72)
    {
        var tles = await GetTlesAsync();
        var sstvStatus = await GetSstvStatusAsync();
        var passes = new List<SatellitePass>();

        var watchSet = watchlist
            .Select(w => w.Trim().ToUpperInvariant())
            .ToHashSet();

        var candidates = tles.Where(t =>
            watchSet.Contains(t.Name.ToUpperInvariant()) ||
            watchlist.Any(w => t.Name.ToUpperInvariant().Contains(w.ToUpperInvariant()))
        ).ToList();

        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddHours(hoursAhead);

        foreach (var tle in candidates)
        {
            var satPasses = PredictPasses(tle, lat, lon, altKm, minElevDeg, startTime, endTime);

            // Annotate SSTV status
            var sstv = sstvStatus.FirstOrDefault(s =>
                tle.Name.Contains(s.SatelliteName, StringComparison.OrdinalIgnoreCase));

            foreach (var p in satPasses)
            {
                p.IsSstv = sstv is not null;
                p.SstvStatus = sstv?.Status;
            }

            passes.AddRange(satPasses);
        }

        return [.. passes.OrderBy(p => p.AosTime)];
    }

    private static List<SatellitePass> PredictPasses(
        TleParsed tle, double lat, double lon, double altKm,
        int minElevDeg, DateTime start, DateTime end)
    {
        var passes = new List<SatellitePass>();

        // Step 1: coarse scan every 15 seconds to find rises above horizon
        var step = TimeSpan.FromSeconds(15);
        double prevEl = -90;
        bool inPass = false;
        DateTime? aosTime = null;
        double maxEl = 0;
        DateTime maxElTime = start;
        double aosAz = 0;

        var t = start;
        while (t < end)
        {
            var eci = Sgp4.Propagate(tle, t);
            if (eci is null) { t += step; continue; }

            var (az, el, _) = Sgp4.GetLookAngles(eci, lat, lon, altKm, t);

            if (!inPass && el > 0)
            {
                // Refine AOS time (binary search between t-step and t)
                aosTime = RefineEvent(tle, lat, lon, altKm, t - step, t, true);
                inPass = true;
                aosAz = az;
                maxEl = el;
                maxElTime = t;
            }
            else if (inPass && el > maxEl)
            {
                maxEl = el;
                maxElTime = t;
            }
            else if (inPass && el < 0)
            {
                // Refine LOS time
                var losTime = RefineEvent(tle, lat, lon, altKm, t - step, t, false);

                if (maxEl >= minElevDeg && aosTime.HasValue)
                {
                    // Get LOS azimuth
                    var losEci = Sgp4.Propagate(tle, losTime);
                    var (losAz, _, __) = losEci is not null
                        ? Sgp4.GetLookAngles(losEci, lat, lon, altKm, losTime)
                        : (0, 0, 0);

                    // Get TCA azimuth
                    var tcaEci = Sgp4.Propagate(tle, maxElTime);
                    var (tcaAz, _, ___) = tcaEci is not null
                        ? Sgp4.GetLookAngles(tcaEci, lat, lon, altKm, maxElTime)
                        : (0, 0, 0);

                    passes.Add(new SatellitePass
                    {
                        SatelliteName = tle.Name,
                        AosTime       = DateTime.SpecifyKind(aosTime.Value, DateTimeKind.Utc),
                        TcaTime       = DateTime.SpecifyKind(maxElTime,     DateTimeKind.Utc),
                        LosTime       = DateTime.SpecifyKind(losTime,       DateTimeKind.Utc),
                        MaxElevation  = Math.Round(maxEl, 1),
                        AosAzimuth    = aosAz,
                        TcaAzimuth    = tcaAz,
                        LosAzimuth    = losAz
                    });
                }

                inPass = false;
                maxEl = 0;
                aosTime = null;
            }

            prevEl = el;
            t += step;
        }

        return passes;
    }

    private static DateTime RefineEvent(
        TleParsed tle, double lat, double lon, double altKm,
        DateTime t1, DateTime t2, bool risingEdge)
    {
        for (int i = 0; i < 8; i++)
        {
            var mid = t1 + (t2 - t1) / 2;
            var eci = Sgp4.Propagate(tle, mid);
            if (eci is null) return mid;

            var (_, el, _) = Sgp4.GetLookAngles(eci, lat, lon, altKm, mid);
            bool aboveHorizon = el > 0;

            if (risingEdge)
                if (aboveHorizon) t2 = mid; else t1 = mid;
            else
                if (aboveHorizon) t1 = mid; else t2 = mid;
        }
        return t1 + (t2 - t1) / 2;
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
            var json = await http.GetStringAsync(SstvStatusUrl);
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
