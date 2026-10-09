namespace OpenRanch.Simulation;

/// <summary>
/// A slime that only lives during some hours of the day, from its prefab's
/// <c>DestroyOutsideHoursOfDay</c> (phosphor slimes: 18:00 to 6:00). Hours are 0-24.
/// </summary>
/// <param name="StartHour">The window opens (<c>startHour</c>).</param>
/// <param name="EndHour">The window closes (<c>endHour</c>); before the start hour means it wraps past midnight.</param>
/// <param name="MinEndureHours">It lasts at least this long outside the window (<c>minEndureHoursOutsideWindow</c>)...</param>
/// <param name="MaxEndureHours">...and at most this long (<c>maxEndureHoursOutsideWindow</c>).</param>
public sealed record HoursWindow(float StartHour, float EndHour, float MinEndureHours, float MaxEndureHours);

/// <summary>
/// When such a slime vanishes. Rules in docs/behavior/slime-traits.md ("Night only"); from static
/// analysis of DestroyOutsideHoursOfDay and TimeDirector. Times are total game hours
/// (<see cref="GameClock.TotalHours"/>; hour 0 = midnight before day 1).
/// </summary>
public static class OutsideHours
{
    /// <summary>A world clock that hasn't started yet reads as 9:00 on day 1 (the new-game start). Code-only.</summary>
    public const double ClockStartHours = 9;

    /// <summary>
    /// The total game hour the slime vanishes at, with its clock started at <paramref name="nowHours"/>
    /// (when it appears, or when it leaves the last cave it was in). <paramref name="roll"/> is a random
    /// number in [0, 1) that picks how long it endures. Outside its window with the endured time also
    /// outside, it vanishes once that time has passed; otherwise (inside the window, or the endured time
    /// would reach into it) it vanishes that long after the window next closes.
    /// </summary>
    public static double ShutdownAt(double nowHours, HoursWindow window, double roll)
    {
        var started = Math.Max(nowHours, ClockStartHours);
        var hour = (float)(started % 24);
        var endure = window.MinEndureHours + (float)roll * (window.MaxEndureHours - window.MinEndureHours);
        var later = hour + endure;
        if (later > 24f)
            later %= 24f;
        bool Outside(float h) => window.EndHour >= window.StartHour
            ? h < window.StartHour || h > window.EndHour
            : h > window.EndHour && h < window.StartHour;
        return Outside(hour) && Outside(later)
            ? started + endure
            : NextHour(nowHours, window.EndHour) + endure;
    }

    /// <summary>The next time strictly after <paramref name="nowHours"/> that the clock reads <paramref name="hour"/> (now itself counts as passed).</summary>
    public static double NextHour(double nowHours, float hour)
    {
        var dayFraction = (float)((nowHours % 24 + 24) % 24 / 24);
        var target = hour / 24f;
        var days = dayFraction < target ? target - dayFraction : target - dayFraction + 1f;
        return nowHours + days * 24.0;
    }
}
