using System.Text.RegularExpressions;

namespace GRRadio.Models;

public class TravelDestination
{
    public string Id    { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Input { get; set; } = "";  // Raw user entry: "K-1234", "Acadia", "Japan"

    // True if input looks like a POTA reference (K-1234, VK-0123, etc.)
    public bool IsPota => Regex.IsMatch(Input.Trim(), @"^[A-Z]{1,3}-\d{4,6}$", RegexOptions.IgnoreCase);
    public string PotaRef => Input.Trim().ToUpperInvariant();
}

public class DestinationInfo
{
    public TravelDestination Source   { get; set; } = new();
    public string Name                { get; set; } = "";
    public string Description         { get; set; } = "";
    public string? ImageUrl           { get; set; }
    public string Grid                { get; set; } = "";
    public double Latitude            { get; set; }
    public double Longitude           { get; set; }
    public string LocationName        { get; set; } = "";   // State / country
    public string PotaReference       { get; set; } = "";
    public int    ActivatorCount      { get; set; }         // User's own POTA activations
    public double DistanceKm          { get; set; }

    public string DistanceDisplay => DistanceKm >= 1000
        ? $"{DistanceKm / 1000:F1}k km"
        : $"{DistanceKm:F0} km";

    public bool IsDay  { get; set; }
    public string SunriseUtc { get; set; } = "";
    public string SunsetUtc  { get; set; } = "";
}
