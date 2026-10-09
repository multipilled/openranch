namespace OpenRanch.Simulation;

/// <summary>
/// Game time: how many real seconds a game day lasts (read from the install's TimeDirector), the
/// day number and the hour. See docs/behavior/slimes.md, "Game time". In the world it follows the
/// one world clock (<see cref="Follow"/>), so the clock's speed and sleeping reach everything that
/// runs on game time; on its own it runs at the install's pace (<see cref="Advance"/>).
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

    /// <summary>How many times faster than the install's pace game time runs now (the world clock's speed, sleeping).</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Game hours that pass in <paramref name="realSeconds"/> at the current <see cref="Speed"/>.</summary>
    public double HoursFor(double realSeconds) => realSeconds * 24.0 / SecondsPerGameDay * Speed;

    /// <summary>Lets real time pass; returns the game hours that passed and whether a new day began.</summary>
    public (double Hours, bool NewDay) Advance(double realSeconds) => Follow(TotalHours + HoursFor(realSeconds));

    /// <summary>Moves to <paramref name="totalHours"/>, as the world clock reads now; returns the game hours that passed and whether a new day began.</summary>
    public (double Hours, bool NewDay) Follow(double totalHours)
    {
        var day = Day;
        var hours = totalHours - TotalHours;
        TotalHours = totalHours;
        return (hours, Day != day);
    }

    /// <summary>Puts the clock at <paramref name="totalHours"/> without counting the jump as time passing (a ranch being opened).</summary>
    public void Set(double totalHours) => TotalHours = totalHours;
}
