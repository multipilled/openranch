namespace OpenRanch.Ranch;

/// <summary>Part of the day, as the clock icon shows it. See docs/behavior/day-cycle.md.</summary>
public enum DayPhase
{
    Night,
    Dawn,
    Day,
    Dusk,
}

/// <summary>
/// Reads the world clock: world time counts game seconds from midnight before day 1. The rules are
/// in docs/behavior/day-cycle.md.
/// </summary>
public readonly record struct WorldClock(double WorldTime)
{
    public const double SecondsPerMinute = 60;
    public const double SecondsPerHour = 3600;
    public const double SecondsPerDay = 86400;

    /// <summary>A new game starts at 9:00 on day 1 (code-only fact).</summary>
    public const double NewGameStart = 9 * SecondsPerHour;

    /// <summary>Sleeping ends at the next 6:00.</summary>
    public const double DawnHour = 6;

    // Where the clock icon changes, as fractions of the day (code-only): night until 0.2, dawn until
    // 0.3, day until 0.7, dusk until 0.8, then night again.
    public const double NightEnds = 0.2, DawnEnds = 0.3, DayEnds = 0.7, DuskEnds = 0.8;

    /// <summary>The day number shown on the clock, counting from 1.</summary>
    public int Day => 1 + (int)Math.Floor(WorldTime / SecondsPerDay);

    /// <summary>How far through the current day: 0 at midnight, 0.5 at noon.</summary>
    public double DayFraction => Mod(WorldTime, SecondsPerDay) / SecondsPerDay;

    /// <summary>Hours since midnight, with the fraction (13.5 is 13:30).</summary>
    public double Hour => DayFraction * 24;

    /// <summary>Whole minutes since midnight, as the clock shows them.</summary>
    public int MinuteOfDay => (int)Math.Floor(Mod(WorldTime, SecondsPerDay) / SecondsPerMinute);

    public DayPhase Phase => DayFraction switch
    {
        < NightEnds or > DuskEnds => DayPhase.Night,
        > DawnEnds and < DayEnds => DayPhase.Day,
        > 0.5 => DayPhase.Dusk,
        _ => DayPhase.Dawn,
    };

    /// <summary>The world time at <paramref name="hour"/> on day <paramref name="day"/>.</summary>
    public static double At(int day, double hour) => (day - 1) * SecondsPerDay + hour * SecondsPerHour;

    /// <summary>
    /// The next time the clock reads <paramref name="hour"/>, strictly after now: later today if that
    /// hour is still ahead, otherwise tomorrow.
    /// </summary>
    public double NextAtHour(double hour)
    {
        var dayStart = WorldTime - Mod(WorldTime, SecondsPerDay);
        var today = dayStart + hour * SecondsPerHour;
        return today > WorldTime ? today : today + SecondsPerDay;
    }

    /// <summary>When sleeping from now ends: the next 6:00, to the nearest whole second.</summary>
    public double WakeTime => Math.Round(NextAtHour(DawnHour), MidpointRounding.AwayFromZero);

    public override string ToString() => $"Day {Day}, {MinuteOfDay / 60:00}:{MinuteOfDay % 60:00}";

    private static double Mod(double a, double b) => a - b * Math.Floor(a / b);
}

/// <summary>
/// Moves the world clock in real time. How many real seconds a game day lasts, normally and while
/// sleeping, is read from the install (<see cref="DayLength.Read"/>).
/// </summary>
public sealed class DayCycle(DayLength length)
{
    public DayLength Length { get; } = length;

    /// <summary>While sleeping, the world time the fast-forward stops at.</summary>
    public double? FastForwardUntil { get; private set; }

    public bool IsFastForwarding => FastForwardUntil.HasValue;

    /// <summary>Starts sleeping: the clock runs fast until the next morning (see <see cref="WorldClock.WakeTime"/>).</summary>
    public void Sleep(RanchState ranch) => FastForwardUntil = ranch.Clock.WakeTime;

    /// <summary>Moves the clock on by <paramref name="realSeconds"/> of real time.</summary>
    public void Advance(RanchState ranch, double realSeconds)
    {
        if (realSeconds <= 0)
            return;
        if (FastForwardUntil is { } until)
        {
            ranch.WorldTime += realSeconds * WorldClock.SecondsPerDay / Length.FastForwardRealSecondsPerDay;
            if (ranch.WorldTime >= until)
            {
                ranch.WorldTime = until;
                FastForwardUntil = null;
            }
        }
        else
        {
            ranch.WorldTime += realSeconds * WorldClock.SecondsPerDay / Length.RealSecondsPerDay;
        }
    }
}

/// <summary>Real seconds per game day, normally and while sleeping.</summary>
public sealed record DayLength(double RealSecondsPerDay, double FastForwardRealSecondsPerDay)
{
    /// <summary>
    /// Reads the day length from the install: the time director script (<c>TimeDirector</c>) in the
    /// game's data holds <c>secsPerGameDay</c> and <c>ffSecsPerGameDay</c>.
    /// </summary>
    public static DayLength Read(OpenRanch.Formats.Game.GameScripts scripts)
    {
        var (_, director) = scripts.OfClass("TimeDirector").FirstOrDefault();
        if (director?.Data is not { } data)
            throw new InvalidDataException("The install has no TimeDirector script data.");
        return new DayLength(Convert.ToDouble(data["secsPerGameDay"]), Convert.ToDouble(data["ffSecsPerGameDay"]));
    }
}
