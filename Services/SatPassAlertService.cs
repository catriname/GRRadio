using GRRadio.Models;
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;

namespace GRRadio.Services;

public enum SatAlertState { None, SinglePass, AllPasses }

public class SatPassAlertService(SettingsService settings)
{
    private const string ChannelId = "sat_alerts";
    private const string SoundName = "satellite_alert";

    // ── Public API ────────────────────────────────────────────────

    public SatAlertState GetState(SatellitePass pass)
    {
        var s = settings.Load();
        if (s.AlertedSatellites.Contains(pass.NoradId)) return SatAlertState.AllPasses;
        if (s.AlertedPassIds.Contains(pass.PassId))           return SatAlertState.SinglePass;
        return SatAlertState.None;
    }

    public async Task CycleAsync(SatellitePass pass, IList<SatellitePass> allPasses)
    {
        var s     = settings.Load();
        var state = GetState(pass);
        var mins  = s.SatPassAlertMinutes;

        switch (state)
        {
            case SatAlertState.None:
                s.AlertedPassIds.Add(pass.PassId);
                await ScheduleAsync(pass, mins);
                break;

            case SatAlertState.SinglePass:
                // Upgrade: cancel single, schedule all upcoming passes for this satellite
                s.AlertedPassIds.Remove(pass.PassId);
                await CancelAsync(pass.NotificationId);

                s.AlertedSatellites.Add(pass.NoradId);
                foreach (var p in UpcomingFor(allPasses, pass.NoradId))
                {
                    s.AlertedPassIds.Add(p.PassId);
                    await ScheduleAsync(p, mins);
                }
                break;

            case SatAlertState.AllPasses:
                // Remove all alerts for this satellite
                s.AlertedSatellites.Remove(pass.NoradId);
                foreach (var p in allPasses.Where(p => p.NoradId == pass.NoradId))
                {
                    if (s.AlertedPassIds.Remove(p.PassId))
                        await CancelAsync(p.NotificationId);
                }
                break;
        }

        settings.Save(s);
    }

    // Call this after passes are recalculated to reschedule anything still in the future
    public async Task RescheduleAllAsync(IList<SatellitePass> passes)
    {
        var s    = settings.Load();
        var mins = s.SatPassAlertMinutes;

        foreach (var pass in passes.Where(p => p.IsUpcoming && s.AlertedPassIds.Contains(p.PassId)))
            await ScheduleAsync(pass, mins);
    }

    // ── Internals ─────────────────────────────────────────────────

    private static IEnumerable<SatellitePass> UpcomingFor(IList<SatellitePass> passes, int noradId) =>
        passes.Where(p => p.NoradId == noradId && p.IsUpcoming);

    private static async Task ScheduleAsync(SatellitePass pass, int minutesBefore)
    {
        var fireTime = DateTime.SpecifyKind(pass.AosTime, DateTimeKind.Utc)
                               .ToLocalTime()
                               .AddMinutes(-minutesBefore);

        if (fireTime <= DateTime.Now) return;

        var request = new NotificationRequest
        {
            NotificationId = pass.NotificationId,
            Title          = $"{pass.SatelliteName} overhead soon",
            Description    = $"AOS in {minutesBefore} min · {pass.MaxElevationStr} max · {pass.AosTimeLocal}",
            Sound          = SoundName,
            Schedule       = new NotificationRequestSchedule
            {
                NotifyTime = fireTime,
                RepeatType = NotificationRepeat.No
            },
            Android = new AndroidOptions
            {
                ChannelId = ChannelId,
                Priority  = AndroidPriority.High
            },
            iOS = new Plugin.LocalNotification.iOSOption.iOSOptions
            {
                PlayForegroundSound = true
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    private static Task CancelAsync(int notificationId)
    {
        LocalNotificationCenter.Current.Cancel(notificationId);
        return Task.CompletedTask;
    }
}
