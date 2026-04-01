namespace GRRadio.Models;

public class SolarData
{
    public double SolarFluxIndex { get; set; }
    public double KIndex { get; set; }
    public double AIndex { get; set; }
    public double SolarWindSpeed { get; set; }
    public double BzComponent { get; set; }
    public int SunspotNumber { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public SolarConditionLevel OverallCondition => KIndex switch
    {
        < 2 => SolarConditionLevel.Excellent,
        < 3 => SolarConditionLevel.Good,
        < 5 => SolarConditionLevel.Fair,
        < 7 => SolarConditionLevel.Poor,
        _ => SolarConditionLevel.Storm
    };

    public bool IsStale => (DateTime.UtcNow - FetchedAt).TotalMinutes > 15;
}

public class ForecastEntry
{
    public DateTime Time { get; set; }
    public double PredictedSfi { get; set; }
    public double PredictedKIndex { get; set; }
    public string Period { get; set; } = string.Empty; // "day1", "day2", "day3"
    public bool IsAlert => PredictedKIndex >= 5;
}

public class SpaceWeatherAlert
{
    public string ProductId { get; set; } = string.Empty;
    public string IssueTime { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }

    public string ShortMessage => Message.Length > 120 ? Message[..120] + "…" : Message;
}

public enum SolarConditionLevel { Excellent, Good, Fair, Poor, Storm }
public enum AlertSeverity { Watch, Warning, Alert, Summary }
