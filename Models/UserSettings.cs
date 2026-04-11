namespace GRRadio.Models;

public class UserSettings
{
    public string Callsign { get; set; } = string.Empty;
    public string GridSquare { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MinSatElevation { get; set; } = 10;
    public List<string> SatelliteWatchlist { get; set; } = DefaultWatchlist();
    public List<string> NewsSubreddits { get; set; } = ["hamradio", "amateurradio"];
    public bool AlertsEnabled { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public bool DailyReportEnabled { get; set; } = false;
    public bool SatellitesEnabled { get; set; } = false;
    public List<TravelDestination> TravelDestinations { get; set; } = [];

    // Logbook & Awards (optional — only used if non-empty)
    public string QrzApiKey    { get; set; } = string.Empty;
    public string LoTwUsername { get; set; } = string.Empty;
    public string LoTwPassword { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Callsign) && !string.IsNullOrWhiteSpace(GridSquare);

    private static List<string> DefaultWatchlist() =>
    [
        "ISS (ZARYA)",
        "ISS",
        "RS-44",
        "AO-91",
        "AO-92",
        "SO-50",
        "PO-101",
        "TEVEL-1",
        "TEVEL-2",
        "AO-7",
        "FO-29",
        "XW-2A",
        "CAS-4A",
        "CAS-4B",
        "LILACSAT-2"
    ];
}
