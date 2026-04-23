namespace GRRadio.Models;

public class TleEntry
{
    public string Name { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
}

public class SatellitePass
{
    public string SatelliteName { get; set; } = string.Empty;
    public DateTime AosTime { get; set; }      // Acquisition of signal
    public DateTime TcaTime { get; set; }      // Time of closest approach
    public DateTime LosTime { get; set; }      // Loss of signal
    public double MaxElevation { get; set; }   // degrees
    public double AosAzimuth { get; set; }
    public double TcaAzimuth { get; set; }
    public double LosAzimuth { get; set; }
    public bool IsSstv { get; set; }
    public string? SstvStatus { get; set; }

    public TimeSpan Duration => LosTime - AosTime;
    public bool IsActive => DateTime.UtcNow >= AosTime && DateTime.UtcNow <= LosTime;
    public bool IsUpcoming => AosTime > DateTime.UtcNow;
    public TimeSpan TimeUntilAos => AosTime - DateTime.UtcNow;

    public string AosTimeLocal => DateTime.SpecifyKind(AosTime, DateTimeKind.Utc).ToLocalTime().ToString("ddd HH:mm");
    public string LosTimeLocal => DateTime.SpecifyKind(LosTime, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
    public string MaxElevationStr => $"{MaxElevation:F0}°";

    public string PassQuality => MaxElevation switch
    {
        >= 60 => "Overhead",
        >= 30 => "High",
        >= 15 => "Medium",
        _ => "Low"
    };

    public string PassQualityCss => MaxElevation switch
    {
        >= 60 => "rating-excellent",
        >= 30 => "rating-good",
        >= 15 => "rating-fair",
        _ => "rating-poor"
    };

    public string CompassDirection(double azimuth) => ((int)(azimuth / 22.5) % 16) switch
    {
        0 => "N", 1 => "NNE", 2 => "NE", 3 => "ENE",
        4 => "E", 5 => "ESE", 6 => "SE", 7 => "SSE",
        8 => "S", 9 => "SSW", 10 => "SW", 11 => "WSW",
        12 => "W", 13 => "WNW", 14 => "NW", 15 => "NNW",
        _ => "N"
    };

    public string AosDirection => CompassDirection(AosAzimuth);
    public string LosDirection => CompassDirection(LosAzimuth);

    public string PassId => $"{SatelliteName}_{AosTime:yyyyMMddHHmm}";
    public int NotificationId => Math.Abs(PassId.Aggregate(0, (h, c) => h * 31 + c)) % 100_000;
}

public class SstvSatelliteStatus
{
    public string SatelliteName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public bool IsActive => Status.Equals("active", StringComparison.OrdinalIgnoreCase);
}
