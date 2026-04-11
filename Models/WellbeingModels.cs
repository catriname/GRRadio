namespace GRRadio.Models;

public class CallookData
{
    public string Callsign    { get; set; } = "";
    public string OperClass   { get; set; } = "";   // EXTRA, GENERAL, TECHNICIAN, ADVANCED, NOVICE
    public string Name        { get; set; } = "";
    public string GrantDate   { get; set; } = "";   // "MM/DD/YYYY" from Callook
    public string GridSquare  { get; set; } = "";
    public string Country     { get; set; } = "";

    public string ClassShort => OperClass switch
    {
        "EXTRA"            => "Extra",
        "GENERAL"          => "General",
        "TECHNICIAN"       => "Technician",
        "TECHNICIAN PLUS"  => "Tech+",
        "ADVANCED"         => "Advanced",
        "NOVICE"           => "Novice",
        _                  => OperClass
    };

    public string ClassCss => OperClass switch
    {
        "EXTRA"   => "license-extra",
        "GENERAL" => "license-general",
        _         => "license-tech"
    };

    public int YearsLicensed
    {
        get
        {
            if (DateTime.TryParseExact(GrantDate, "MM/dd/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return (int)((DateTime.Now - dt).TotalDays / 365.25);
            return 0;
        }
    }
}

public class GreetingInsight
{
    public string Icon      { get; set; } = "";
    public string BadgeText { get; set; } = "";  // Rendered as a styled tag before Text
    public string BadgeCss  { get; set; } = "";  // CSS class for the badge
    public string Text      { get; set; } = "";
    public string SubText   { get; set; } = "";
}

public class QrzStats
{
    public int       TotalQsos { get; set; }
    public string?   LastCall  { get; set; }
    public string?   LastBand  { get; set; }
    public string?   LastMode  { get; set; }
    public DateTime? LastDate  { get; set; }
}

public class PskSpot
{
    public string ReceiverCallsign { get; set; } = "";
    public string ReceiverLocator  { get; set; } = "";
    public string SenderLocator    { get; set; } = "";
    public double Frequency        { get; set; }    // Hz
    public string Mode             { get; set; } = "";
    public int    SNR              { get; set; }
    public long   Timestamp        { get; set; }    // Unix seconds
    public double DistanceKm       { get; set; }

    public string Band => Frequency switch
    {
        < 2_200_000  => "160m",
        < 4_000_000  => "80m",
        < 7_500_000  => "40m",
        < 11_000_000 => "30m",
        < 15_000_000 => "20m",
        < 19_000_000 => "17m",
        < 22_000_000 => "15m",
        < 25_000_000 => "12m",
        < 30_000_000 => "10m",
        < 54_000_000 => "6m",
        _            => "VHF+"
    };

    public string TimeAgo
    {
        get
        {
            var ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Timestamp;
            return ago switch
            {
                < 120   => "just now",
                < 3600  => $"{ago / 60}m ago",
                < 86400 => $"{ago / 3600}h ago",
                _       => $"{ago / 86400}d ago"
            };
        }
    }

    public string DistanceDisplay => DistanceKm >= 1000
        ? $"{DistanceKm / 1000:F1}k km"
        : $"{DistanceKm:F0} km";
}
