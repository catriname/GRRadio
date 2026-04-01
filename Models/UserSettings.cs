namespace GRRadio.Models;

public class UserSettings
{
    public string Callsign { get; set; } = string.Empty;
    public string GridSquare { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MinSatElevation { get; set; } = 10;
    public List<string> SatelliteWatchlist { get; set; } = DefaultWatchlist();
    public bool AlertsEnabled { get; set; } = true;
    public bool DarkMode { get; set; } = true;

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
