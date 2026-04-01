namespace GRRadio.Models;

public class BandCondition
{
    public string Band { get; set; } = string.Empty;           // "20m", "40m", etc.
    public string FrequencyRange { get; set; } = string.Empty; // "14.000–14.350 MHz"
    public BandRating Rating { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> GoodRegions { get; set; } = [];
    public string BestMode { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public bool IsNightBand { get; set; }
    public bool IsHighBand { get; set; }
}

public class BandForecast
{
    public DateTime Hour { get; set; }
    public List<BandCondition> Conditions { get; set; } = [];
    public string TimeLabel => Hour.ToLocalTime().ToString("ddd HH:mm");
    public string DayLabel => Hour.ToLocalTime().ToString("ddd MMM d");
}

public enum BandRating
{
    Closed,
    Poor,
    Fair,
    Good,
    Excellent
}

public static class BandRatingExtensions
{
    public static string ToLabel(this BandRating r) => r switch
    {
        BandRating.Excellent => "Excellent",
        BandRating.Good => "Good",
        BandRating.Fair => "Fair",
        BandRating.Poor => "Poor",
        BandRating.Closed => "Closed",
        _ => "Unknown"
    };

    public static string ToCssClass(this BandRating r) => r switch
    {
        BandRating.Excellent => "rating-excellent",
        BandRating.Good => "rating-good",
        BandRating.Fair => "rating-fair",
        BandRating.Poor => "rating-poor",
        BandRating.Closed => "rating-closed",
        _ => "rating-closed"
    };

    public static string ToIcon(this BandRating r) => r switch
    {
        BandRating.Excellent => "★★★",
        BandRating.Good => "★★☆",
        BandRating.Fair => "★☆☆",
        BandRating.Poor => "▼",
        BandRating.Closed => "✕",
        _ => "?"
    };
}
