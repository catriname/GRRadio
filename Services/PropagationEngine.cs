using GRRadio.Models;

namespace GRRadio.Services;

/// <summary>
/// Scores HF bands based on solar conditions, time of day, and observer location.
/// Ported and enhanced from K5GRR's propagation logic.
/// </summary>
public static class PropagationEngine
{
    // Band definitions: name, frequency range, is-night, is-high
    private static readonly BandDef[] Bands =
    [
        new("6m",  "50.0–54.0 MHz",   false, true),
        new("10m", "28.0–29.7 MHz",   false, true),
        new("12m", "24.890–24.990 MHz", false, true),
        new("15m", "21.0–21.45 MHz",  false, true),
        new("17m", "18.068–18.168 MHz", false, false),
        new("20m", "14.0–14.35 MHz",  false, false),
        new("30m", "10.1–10.15 MHz",  false, false),
        new("40m", "7.0–7.3 MHz",     true,  false),
        new("80m", "3.5–4.0 MHz",     true,  false),
    ];

    private record BandDef(string Name, string Freq, bool IsNight, bool IsHigh);

    public static List<BandCondition> Evaluate(SolarData solar, DateTime utcNow, double lat, double lon)
    {
        var timeBlock = GetTimeBlock(utcNow, lat, lon);
        return Bands.Select(b => ScoreBand(b, solar, timeBlock, lat, lon)).ToList();
    }

    /// <summary>
    /// Generates 3-hour interval forecasts for the next 3 days.
    /// </summary>
    public static List<BandForecast> BuildForecast(
        List<ForecastEntry> forecastEntries, double lat, double lon)
    {
        return forecastEntries.Select(fe =>
        {
            var syntheticSolar = new SolarData
            {
                SolarFluxIndex = fe.PredictedSfi,
                KIndex         = fe.PredictedKIndex,
                SolarWindSpeed = 400,
                BzComponent    = 0
            };
            return new BandForecast
            {
                Hour       = fe.Time,
                Conditions = Evaluate(syntheticSolar, fe.Time, lat, lon)
            };
        }).ToList();
    }

    // ── Time Block ────────────────────────────────────────────────────────────

    private enum TimeBlock { PreSunrise, Morning, Midday, Afternoon, PostSunset, Night }

    private static TimeBlock GetTimeBlock(DateTime utc, double lat, double lon)
    {
        // Work in solar offset from solar noon to avoid UTC midnight-crossing bugs.
        // solarOffset = 0 at noon, negative = AM, positive = PM.
        var (noon, ha) = GetSolarNoonAndHalfDay(utc.Date, lat, lon);

        double hour   = utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0;
        double offset = hour - noon;
        // Normalize to [-12, 12]
        if (offset >  12) offset -= 24;
        if (offset < -12) offset += 24;

        double sunrise = -ha;   // hours before noon
        double sunset  =  ha;   // hours after noon

        if (offset < sunrise - 1)    return TimeBlock.Night;
        if (offset < sunrise)        return TimeBlock.PreSunrise;
        if (offset < sunrise + 4)    return TimeBlock.Morning;
        if (offset < sunset  - 3)    return TimeBlock.Midday;
        if (offset < sunset)         return TimeBlock.Afternoon;
        if (offset < sunset  + 1.5)  return TimeBlock.PostSunset;
        return TimeBlock.Night;
    }

    // Returns UTC hour of solar noon and half-daylight-length in hours.
    private static (double Noon, double HalfDay) GetSolarNoonAndHalfDay(DateTime date, double lat, double lon)
    {
        const double Deg2Rad = Math.PI / 180;
        int doy = date.DayOfYear;
        double b   = (360.0 / 365) * (doy - 81) * Deg2Rad;
        double et  = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b);
        double dec = 23.45 * Math.Sin(b) * Deg2Rad;
        double ha  = Math.Acos(Math.Clamp(-Math.Tan(lat * Deg2Rad) * Math.Tan(dec), -1, 1)) / Deg2Rad;
        double noon = 12.0 - lon / 15.0 - et / 60.0;
        return (noon, ha / 15.0);
    }

    // ── Band Scoring ──────────────────────────────────────────────────────────

    private static BandCondition ScoreBand(BandDef band, SolarData solar, TimeBlock block, double lat, double lon)
    {
        var sfi = solar.SolarFluxIndex;
        var kp  = solar.KIndex;

        return band.Name switch
        {
            "6m"  => Score6m(band, sfi, kp, block),
            "10m" => Score10m(band, sfi, kp, block),
            "12m" => Score12m(band, sfi, kp, block),
            "15m" => Score15m(band, sfi, kp, block),
            "17m" => Score17m(band, sfi, kp, block),
            "20m" => Score20m(band, sfi, kp, block),
            "30m" => Score30m(band, sfi, kp, block),
            "40m" => Score40m(band, sfi, kp, block),
            "80m" => Score80m(band, sfi, kp, block),
            _ => Closed(band)
        };
    }

    // ── Individual Band Logic ─────────────────────────────────────────────────

    private static BandCondition Score6m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        // 6m: primarily sporadic-E (May–Aug) or high solar F2
        if (sfi > 180 && kp < 3 && t is TimeBlock.Midday or TimeBlock.Afternoon)
            return Make(b, BandRating.Good, "F2 opening possible", ["EU", "SA", "NA"], "SSB/FT8");
        if (IsSummerMonth() && t is TimeBlock.Morning or TimeBlock.Midday)
            return Make(b, BandRating.Fair, "Sporadic-E season", ["NA", "EU"], "SSB/FT8");
        return Make(b, BandRating.Closed, "Closed – needs high SFI or Sporadic-E", [], "");
    }

    private static BandCondition Score10m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (sfi > 150 && kp < 3 && t == TimeBlock.Midday)
            return Make(b, BandRating.Excellent, "Worldwide openings", ["EU", "AF", "SA", "JA", "VK"], "SSB/FT8");
        if (sfi > 130 && kp < 4 && t is TimeBlock.Morning or TimeBlock.Afternoon)
            return Make(b, BandRating.Good, "Long paths open", ["EU", "SA", "JA"], "SSB/FT8");
        if (sfi > 110 && kp < 5)
            return Make(b, BandRating.Fair, "Short skip possible", ["NA", "SA"], "FT8");
        return Make(b, BandRating.Closed, "Closed – low solar flux", [], "");
    }

    private static BandCondition Score12m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (sfi > 140 && kp < 3 && t is TimeBlock.Morning or TimeBlock.Midday or TimeBlock.Afternoon)
            return Make(b, BandRating.Good, "DX paths open", ["EU", "AF", "SA"], "SSB/FT8");
        if (sfi > 120 && kp < 4)
            return Make(b, BandRating.Fair, "Some DX possible", ["SA", "NA"], "FT8");
        return Make(b, BandRating.Poor, "Marginal conditions", [], "FT8");
    }

    private static BandCondition Score15m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (sfi > 120 && kp < 3 && t is TimeBlock.Morning or TimeBlock.Midday)
            return Make(b, BandRating.Excellent, "Trans-Atlantic open", ["EU", "AF", "SA"], "SSB/FT8/CW");
        if (sfi > 100 && kp < 4 && t is TimeBlock.Morning or TimeBlock.Midday or TimeBlock.Afternoon)
            return Make(b, BandRating.Good, "Regional DX open", ["NA", "SA", "EU"], "SSB/FT8");
        if (t is TimeBlock.Night or TimeBlock.PreSunrise)
            return Make(b, BandRating.Closed, "Closed at night", [], "");
        return Make(b, BandRating.Fair, "Marginal – try FT8", ["NA", "SA"], "FT8");
    }

    private static BandCondition Score17m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (kp < 3 && t is TimeBlock.Morning or TimeBlock.Midday or TimeBlock.Afternoon)
            return Make(b, BandRating.Good, "Reliable DX band", ["EU", "SA", "JA", "AF"], "SSB/FT8/CW");
        if (kp < 5 && t is TimeBlock.Morning or TimeBlock.Midday)
            return Make(b, BandRating.Fair, "Some DX possible", ["NA", "SA", "EU"], "FT8");
        if (t is TimeBlock.Night)
            return Make(b, BandRating.Fair, "Gray-line DX possible", ["JA", "VK"], "FT8/CW");
        return Make(b, BandRating.Poor, "Disturbed – try later", [], "FT8");
    }

    private static BandCondition Score20m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (kp < 3)
        {
            if (t is TimeBlock.Morning or TimeBlock.Midday)
                return Make(b, BandRating.Excellent, "King of DX bands – wide open", ["EU", "AF", "SA", "JA", "VK"], "SSB/FT8/CW");
            if (t is TimeBlock.Night)
                return Make(b, BandRating.Good, "Long path to Asia/Pacific", ["JA", "VK", "AS"], "FT8/CW");
        }
        if (kp < 5)
            return Make(b, BandRating.Good, "Open with some absorption", ["EU", "NA", "SA"], "FT8/CW");
        return Make(b, BandRating.Fair, "Geomagnetic disturbance – try low power modes", ["NA", "SA"], "FT8");
    }

    private static BandCondition Score30m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        // 30m: DIGITAL ONLY (ITU regulations)
        if (kp < 4)
        {
            if (t is TimeBlock.Night or TimeBlock.PreSunrise)
                return Make(b, BandRating.Good, "Excellent overnight DX", ["EU", "AF", "JA"], "FT8/JS8 (Digital only)");
            return Make(b, BandRating.Fair, "Reliable – low noise at night", ["NA", "SA"], "FT8 (Digital only)");
        }
        return Make(b, BandRating.Fair, "Storm – try NVIS paths", ["NA"], "FT8 (Digital only)");
    }

    private static BandCondition Score40m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (t is TimeBlock.Night or TimeBlock.PreSunrise)
        {
            if (kp < 3)
                return Make(b, BandRating.Excellent, "Wide-open DX at night", ["EU", "AF", "JA", "VK"], "SSB/CW/FT8");
            return Make(b, BandRating.Good, "Good night DX with some noise", ["EU", "NA"], "CW/FT8");
        }
        if (t is TimeBlock.PostSunset)
            return Make(b, BandRating.Good, "Opening as night falls", ["NA", "SA", "EU"], "SSB/CW/FT8");
        // Daytime
        return Make(b, BandRating.Fair, "NVIS – regional contacts only", ["NA regional"], "SSB/CW");
    }

    private static BandCondition Score80m(BandDef b, double sfi, double kp, TimeBlock t)
    {
        if (t is TimeBlock.Night or TimeBlock.PreSunrise)
        {
            if (kp < 3)
                return Make(b, BandRating.Good, "Regional DX overnight", ["NA", "EU"], "CW/FT8");
            return Make(b, BandRating.Fair, "Noisy – high K-index", ["NA local"], "CW");
        }
        if (t is TimeBlock.PostSunset or TimeBlock.Morning)
            return Make(b, BandRating.Fair, "Regional – opening/closing", ["NA"], "SSB/CW");
        return Make(b, BandRating.Poor, "Daytime – local only", ["Local"], "SSB");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BandCondition Make(BandDef b, BandRating rating, string summary,
        List<string> regions, string mode) => new()
    {
        Band          = b.Name,
        FrequencyRange = b.Freq,
        Rating        = rating,
        Summary       = summary,
        GoodRegions   = regions,
        BestMode      = mode,
        IsNightBand   = b.IsNight,
        IsHighBand    = b.IsHigh
    };

    private static BandCondition Closed(BandDef b) =>
        Make(b, BandRating.Closed, "No prediction available", [], "");

    private static bool IsSummerMonth()
    {
        var m = DateTime.UtcNow.Month;
        return m is >= 5 and <= 8;
    }
}
