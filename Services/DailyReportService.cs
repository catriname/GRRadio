using GRRadio.Models;

namespace GRRadio.Services;

public class DailyReportService(PhraseService phraseService)
{
    // Enthusiastic band messages keyed by "band_rating"
    private static readonly Dictionary<string, string[]> BandMessages = new()
    {
        ["6m_excellent"] =
        [
            "THE MAGIC BAND IS WIDE OPEN! Drop everything and get on 6m!",
            "6m is doing its thing — this is what we live for!",
            "Magic band magic! 6m is wide open right now. Work the world!"
        ],
        ["6m_good"] =
        [
            "The magic band is stirring — 6m showing signs of life!",
            "6m is flickering open. Keep an ear on it — could go wide any moment.",
            "Magic band activity on 6m! Something special might be coming."
        ],
        ["10m_excellent"] =
        [
            "10m is going WORLDWIDE today! Solar flux is delivering — get on!",
            "Ten meters is wide open. The whole planet is within reach right now.",
            "10m is singing! Time to work some serious DX. The solar gods are smiling."
        ],
        ["10m_good"] =
        [
            "10m is open and active — solid DX opportunities today.",
            "Good openings on 10m. Fire up the radio, conditions are cooperating.",
            "10m is cooperating — nice DX window open today."
        ],
        ["15m_excellent"] =
        [
            "15m is wide open — DX is calling your name loud and clear!",
            "Fifteen meters going off! Excellent conditions reaching across the globe."
        ],
        ["15m_good"] =
        [
            "15m is in good shape — reliable DX band doing its job today.",
            "Solid conditions on 15m. Good choice for afternoon DX."
        ],
        ["17m_excellent"] =
        [
            "17m is wide open — that WARC band extra reach is paying off today!",
            "No contests on 17m, just wide-open DX. Perfect conditions."
        ],
        ["17m_good"] =
        [
            "17m is in good shape today — WARC band conditions looking solid.",
            "Good DX on 17m today. Nice, quiet WARC band working well."
        ],
        ["20m_excellent"] =
        [
            "The WORKHORSE BAND IS SINGING! 20m wide open for serious DX.",
            "20m is absolutely rocking — king of the HF bands is on the throne today!",
            "King of DX is holding court on 20m. Wide open. Get on now."
        ],
        ["20m_good"] =
        [
            "20m is doing its reliable thing — solid conditions, solid contacts.",
            "The workhorse is pulling its weight today. Good 20m conditions all day.",
            "Steady as she goes on 20m — reliable DX window is open."
        ],
        ["30m_excellent"] =
        [
            "30m is excellent — fire up the digital modes and work the world!",
            "CW and digital on 30m looking great. This quiet WARC band is alive."
        ],
        ["30m_good"] =
        [
            "30m is behaving well today. Good CW and digital conditions.",
            "Nice conditions on 30m — great band for a quiet, focused session."
        ],
        ["40m_excellent"] =
        [
            "40m is ALIVE tonight! Classic nighttime DX — get on now!",
            "Forty meters is BOOMING. The night owls are winning tonight.",
            "40m lit up like a Christmas tree — regional and DX contacts waiting."
        ],
        ["40m_good"] =
        [
            "Good conditions on 40m — solid regional and some DX possible.",
            "40m is cooperating nicely tonight. Good evening band conditions.",
            "The evening band is open. 40m is ready when you are."
        ],
        ["80m_excellent"] =
        [
            "80m is a POWERHOUSE tonight — regional comms are excellent!",
            "The low bands are alive! 80m wide open for regional work and rag chewing.",
            "80m is booming — great for nets, rag chews, and local DX tonight."
        ],
        ["80m_good"] =
        [
            "Good conditions on 80m tonight. Solid regional band doing its thing.",
            "80m is showing up for the evening. Good regional conditions."
        ],
        ["12m_excellent"] =
        [
            "12m is open! Time to work some rare WARC band DX.",
            "Twelve meters is alive — solar conditions really delivering today!"
        ]
    };

    public async Task<DailyReport> GenerateAsync(
        UserSettings settings,
        SolarData? solar,
        List<BandCondition> bands,
        List<SatellitePass> passes)
    {
        var now = DateTime.Now;
        var phrase = await phraseService.GetRandomPhraseAsync();

        return new DailyReport
        {
            GeneratedAt      = now,
            Greeting         = BuildGreeting(settings.Callsign, now),
            MotivationalPhrase = phrase.Text,
            BandHighlights   = BuildBandHighlights(bands),
            UpcomingPasses   = BuildPassHighlights(passes, now),
            WeatherSummary   = BuildWeatherSummary(solar)
        };
    }

    private static string BuildGreeting(string callsign, DateTime now)
    {
        var name = string.IsNullOrWhiteSpace(callsign) ? "operator" : callsign;
        var greeting = now.Hour switch
        {
            >= 5 and < 12  => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            >= 17 and < 21 => "Good evening",
            _              => "Good night"
        };
        return $"{greeting}, {name}.";
    }

    private static List<BandHighlight> BuildBandHighlights(List<BandCondition> bands) =>
        bands
            .Where(b => b.Rating >= BandRating.Good)
            .OrderByDescending(b => (int)b.Rating)
            .Take(4)
            .Select(b => new BandHighlight
            {
                Band        = b.Band,
                Rating      = b.Rating,
                Message     = GetBandMessage(b.Band, b.Rating),
                GoodRegions = b.GoodRegions,
                BestMode    = b.BestMode
            })
            .ToList();

    private static string GetBandMessage(string band, BandRating rating)
    {
        var key = $"{band}_{rating.ToString().ToLower()}";
        if (BandMessages.TryGetValue(key, out var msgs))
            return msgs[Random.Shared.Next(msgs.Length)];

        return rating switch
        {
            BandRating.Excellent => $"{band} is wide open — excellent conditions!",
            BandRating.Good      => $"{band} is in good shape today.",
            _                    => $"{band}: {rating.ToLabel()}"
        };
    }

    private static List<SatPassHighlight> BuildPassHighlights(List<SatellitePass> passes, DateTime now)
    {
        var utcNow = now.ToUniversalTime();
        return passes
            .Where(p => p.AosTime >= utcNow && p.AosTime <= utcNow.AddHours(12))
            .OrderBy(p => p.AosTime)
            .Take(3)
            .Select(p => new SatPassHighlight
            {
                SatelliteName = p.SatelliteName,
                AosTime       = p.AosTime.ToLocalTime(),
                MaxElevation  = p.MaxElevation,
                PassQuality   = p.PassQuality,
                PassQualityCss = p.PassQualityCss,
                AosDirection  = p.AosDirection,
                LosDirection  = p.LosDirection,
                Description   = BuildPassDescription(p)
            })
            .ToList();
    }

    private static string BuildPassDescription(SatellitePass p)
    {
        var timeUntil = p.TimeUntilAos;
        var when = timeUntil.TotalMinutes < 60
            ? $"in {(int)timeUntil.TotalMinutes} min"
            : $"at {p.AosTime.ToLocalTime():HH:mm}";
        return $"{p.PassQuality} pass {when} — max {p.MaxElevation:F0}° from {p.AosDirection} to {p.LosDirection}";
    }

    private static string BuildWeatherSummary(SolarData? solar)
    {
        if (solar is null) return "Space weather data unavailable.";

        var sfiDesc = solar.SolarFluxIndex switch
        {
            > 200 => "very high solar flux",
            > 150 => "elevated solar flux",
            > 100 => "moderate solar flux",
            _     => "low solar flux"
        };

        var kpDesc = solar.KIndex switch
        {
            < 2 => "very quiet geomagnetic field",
            < 4 => "calm geomagnetic conditions",
            < 6 => "unsettled — some HF degradation possible",
            _   => "disturbed — expect degraded HF conditions"
        };

        return $"SFI {solar.SolarFluxIndex:F0} ({sfiDesc}) · Kp {solar.KIndex:F1} ({kpDesc}).";
    }
}
