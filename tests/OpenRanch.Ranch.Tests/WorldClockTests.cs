namespace OpenRanch.Ranch.Tests;

public class WorldClockTests
{
    [Fact]
    public void A_new_game_starts_at_nine_on_day_one()
    {
        var ranch = new RanchState();
        Assert.Equal(1, ranch.Clock.Day);
        Assert.Equal(9.0, ranch.Clock.Hour, 9);
        Assert.Equal("Day 1, 09:00", ranch.Clock.ToString());
    }

    [Theory]
    [InlineData(0.0, 1, 0)]
    [InlineData(86399.0, 1, 1439)]
    [InlineData(86400.0, 2, 0)]
    [InlineData(3 * 86400.0 + 13.5 * 3600, 4, 810)]
    public void Day_and_minute_follow_world_time(double time, int day, int minute)
    {
        var clock = new WorldClock(time);
        Assert.Equal(day, clock.Day);
        Assert.Equal(minute, clock.MinuteOfDay);
    }

    [Theory]
    [InlineData(2.0, DayPhase.Night)]
    [InlineData(5.0, DayPhase.Dawn)]
    [InlineData(12.0, DayPhase.Day)]
    [InlineData(17.5, DayPhase.Dusk)]
    [InlineData(21.0, DayPhase.Night)]
    public void Phases_split_the_day(double hour, DayPhase phase) =>
        Assert.Equal(phase, new WorldClock(WorldClock.At(5, hour)).Phase);

    [Fact]
    public void Next_hour_is_today_if_still_ahead_and_tomorrow_otherwise()
    {
        var morning = new WorldClock(WorldClock.At(2, 5));
        Assert.Equal(WorldClock.At(2, 6), morning.NextAtHour(6));
        var evening = new WorldClock(WorldClock.At(2, 22));
        Assert.Equal(WorldClock.At(3, 6), evening.NextAtHour(6));
        var exactly = new WorldClock(WorldClock.At(2, 6));
        Assert.Equal(WorldClock.At(3, 6), exactly.NextAtHour(6));
    }

    [Fact]
    public void Sleeping_runs_the_clock_fast_until_six()
    {
        var ranch = new RanchState { WorldTime = WorldClock.At(4, 20) };
        var cycle = new DayCycle(new DayLength(RealSecondsPerDay: 1440, FastForwardRealSecondsPerDay: 5));

        cycle.Advance(ranch, 60); // a real minute is one game hour at 1,440 real seconds a day
        Assert.Equal(21.0, ranch.Clock.Hour, 6);

        cycle.Sleep(ranch);
        Assert.True(cycle.IsFastForwarding);
        cycle.Advance(ranch, 1); // a fifth of a day per real second
        Assert.Equal(WorldClock.At(4, 21) + 86400 / 5.0, ranch.WorldTime, 6);
        cycle.Advance(ranch, 10); // overshoot stops at 6:00
        Assert.Equal(WorldClock.At(5, 6), ranch.WorldTime);
        Assert.False(cycle.IsFastForwarding);
        Assert.Equal(5, ranch.Clock.Day);
    }
}
