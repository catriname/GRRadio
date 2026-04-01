namespace GRRadio.Models;

public class DailyReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string Greeting { get; set; } = string.Empty;
    public string MotivationalPhrase { get; set; } = string.Empty;
    public List<BandHighlight> BandHighlights { get; set; } = [];
    public List<SatPassHighlight> UpcomingPasses { get; set; } = [];
    public string WeatherSummary { get; set; } = string.Empty;

    public bool HasBandHighlights => BandHighlights.Count > 0;
    public bool HasUpcomingPasses => UpcomingPasses.Count > 0;
    public string GeneratedAtLabel => GeneratedAt.ToString("ddd, MMM d · HH:mm");
}

public class BandHighlight
{
    public string Band { get; set; } = string.Empty;
    public BandRating Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> GoodRegions { get; set; } = [];
    public string BestMode { get; set; } = string.Empty;
}

public class SatPassHighlight
{
    public string SatelliteName { get; set; } = string.Empty;
    public DateTime AosTime { get; set; }
    public double MaxElevation { get; set; }
    public string PassQuality { get; set; } = string.Empty;
    public string PassQualityCss { get; set; } = string.Empty;
    public string AosDirection { get; set; } = string.Empty;
    public string LosDirection { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
