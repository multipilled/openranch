namespace OpenRanch.Simulation;

/// <summary>
/// Game time: how many real seconds a game day lasts (read from the install's TimeDirector), the
/// day number and the hour. See docs/behavior/slimes.md, "Game time".
/// </summary>
public sealed class GameClock
{
    public GameClock(float secondsPerGameDay, double startHour = 12)
    {
        if (secondsPerGameDay <= 0)
            throw new ArgumentOutOfRangeException(nameof(secondsPerGameDay));
        SecondsPerGameDay = secondsPerGameDay;
        TotalHours = startHour;
    }

    public float SecondsPerGameDay { get; }
    /// <summary>Game hours since the start of day 0.</summary>
    public double TotalHours { get; private set; }
    public int Day => (int)Math.Floor(TotalHours / 24);
    public float HourOfDay => (float)(TotalHours - Day * 24.0);

    /// <summary>Game hours that pass in <paramref name="realSeconds"/>.</summary>
    public double HoursFor(double realSeconds) => realSeconds * 24.0 / SecondsPerGameDay;

    /// <summary>Lets real time pass; returns the game hours that passed and whether a new day began.</summary>
    public (double Hours, bool NewDay) Advance(double realSeconds)
    {
        var day = Day;
        var hours = HoursFor(realSeconds);
        TotalHours += hours;
        return (hours, Day != day);
    }
}
