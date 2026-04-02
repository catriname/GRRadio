using GRRadio.Models;
using System.Text.Json;

namespace GRRadio.Services;

public class SettingsService
{
    private const string SettingsKey = "grradio_settings";
    private UserSettings? _cached;

    public UserSettings Load()
    {
        if (_cached is not null)
            return _cached;

        var json = Preferences.Default.Get(SettingsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            _cached = new UserSettings();
            return _cached;
        }

        try
        {
            _cached = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            _cached = new UserSettings();
        }

        return _cached;
    }

    public event Action? OnChanged;

    public void Save(UserSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings);
        Preferences.Default.Set(SettingsKey, json);
        OnChanged?.Invoke();
    }

    public void UpdateGridFromLatLon(UserSettings settings, double lat, double lon)
    {
        settings.Latitude = lat;
        settings.Longitude = lon;
        settings.GridSquare = LatLonToMaidenhead(lat, lon);
        Save(settings);
    }

    // Converts lat/lon to 6-character Maidenhead grid square
    public static string LatLonToMaidenhead(double lat, double lon)
    {
        lon += 180;
        lat += 90;

        char f1 = (char)('A' + (int)(lon / 20));
        char f2 = (char)('A' + (int)(lat / 10));
        char f3 = (char)('0' + (int)((lon % 20) / 2));
        char f4 = (char)('0' + (int)(lat % 10));
        char f5 = (char)('A' + (int)((lon % 2) / (2.0 / 24)));
        char f6 = (char)('A' + (int)((lat % 1) / (1.0 / 24)));

        return $"{f1}{f2}{f3}{f4}{char.ToLower(f5)}{char.ToLower(f6)}";
    }

    // Converts Maidenhead grid square to lat/lon center point
    public static (double Lat, double Lon) MaidenheadToLatLon(string grid)
    {
        if (string.IsNullOrWhiteSpace(grid) || grid.Length < 4)
            return (0, 0);

        grid = grid.ToUpper();
        double lon = (grid[0] - 'A') * 20 - 180;
        double lat = (grid[1] - 'A') * 10 - 90;
        lon += (grid[2] - '0') * 2;
        lat += (grid[3] - '0');

        if (grid.Length >= 6)
        {
            lon += (grid[4] - 'A') * (2.0 / 24);
            lat += (grid[5] - 'A') * (1.0 / 24);
            // Center of subsquare
            lon += 1.0 / 24;
            lat += 0.5 / 24;
        }
        else
        {
            lon += 1;
            lat += 0.5;
        }

        return (lat, lon);
    }
}
