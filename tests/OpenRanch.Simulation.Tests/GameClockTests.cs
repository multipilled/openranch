namespace OpenRanch.Simulation.Tests;

public class GameClockTests
{
    [Fact]
    public void Following_the_world_clock_counts_the_hours_and_days_it_moved()
    {
        var clock = new GameClock(secondsPerGameDay: 1440);
        clock.Set(3 * 24 + 23.5);
        Assert.Equal(3, clock.Day);
        var (hours, newDay) = clock.Follow(4 * 24 + 0.25);
        Assert.Equal(0.75, hours, 9);
        Assert.True(newDay);
        Assert.Equal(0.25f, clock.HourOfDay, 4);
    }

    [Fact]
    public void Speed_scales_the_game_hours_a_real_second_is_worth()
    {
        var clock = new GameClock(secondsPerGameDay: 1440, startHour: 0);
        Assert.Equal(1.0 / 60, clock.HoursFor(1), 9);
        // Sleeping: the world clock runs at 4 real seconds per game day instead of 1440.
        clock.Speed = 1440.0 / 4;
        Assert.Equal(6, clock.HoursFor(1), 9);
        var (hours, _) = clock.Advance(1);
        Assert.Equal(6, hours, 9);
        Assert.Equal(6, clock.TotalHours, 9);
    }
}
