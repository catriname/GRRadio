namespace GRRadio.Models;

public class UserSettings
{
    public string Callsign { get; set; } = string.Empty;
    public string GridSquare { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MinSatElevation { get; set; } = 10;
    public List<string> SatelliteWatchlist { get; set; } = [];
    public List<string> NewsSubreddits { get; set; } = ["hamradio", "amateurradio"];
    public bool AlertsEnabled { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public bool DailyReportEnabled { get; set; } = false;
    public bool SatellitesEnabled { get; set; } = false;
    public List<TravelDestination> TravelDestinations { get; set; } = [];

    // Logbook & Awards (optional — only used if non-empty)
    public string QrzApiKey { get; set; } = string.Empty;

    // Last known PSK Reporter spot (persisted so it survives service outages)
    public long   LastSpotTimestamp { get; set; }   // Unix seconds; 0 = never seen
    public string LastSpotCallsign  { get; set; } = string.Empty;
    public string LastSpotBand      { get; set; } = string.Empty;
    public string LastSpotMode      { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Callsign) && !string.IsNullOrWhiteSpace(GridSquare);
}
