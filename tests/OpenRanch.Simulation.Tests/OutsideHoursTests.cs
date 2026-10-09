namespace OpenRanch.Simulation.Tests;

// The night-only clock on the phosphor's window (18-6); the install's values are checked in
// InstalledAbilityTests.
public class OutsideHoursTests
{
    private static readonly HoursWindow Night = new(18, 6, 0.5f, 0.55f);

    [Fact]
    public void At_night_it_lasts_until_dawn_plus_its_endurance()
    {
        // Day 2, 22:00 (total hour 46): dawn is 54.
        Assert.Equal(54.5, OutsideHours.ShutdownAt(46, Night, 0), 4);
        Assert.Equal(54.55, OutsideHours.ShutdownAt(46, Night, 1), 4);
        // Just before dawn, the same.
        Assert.Equal(30.5, OutsideHours.ShutdownAt(29.9, Night, 0), 4);
    }

    [Fact]
    public void In_the_day_it_lasts_only_its_endurance()
    {
        Assert.Equal(36.5, OutsideHours.ShutdownAt(36, Night, 0), 4); // noon, day 2
    }

    [Fact]
    public void Late_afternoon_reaching_into_the_night_waits_for_the_next_dawn()
    {
        // 17:45 + 0.5 h is past 18:00, so it lives through the night.
        Assert.Equal(54.5, OutsideHours.ShutdownAt(41.75, Night, 0), 4);
    }

    [Fact]
    public void A_clock_before_the_new_game_start_counts_from_nine()
    {
        Assert.Equal(9.5, OutsideHours.ShutdownAt(0, Night, 0), 4);
    }

    [Fact]
    public void The_next_hour_is_strictly_later()
    {
        Assert.Equal(30, OutsideHours.NextHour(29.5, 6), 4);
        Assert.Equal(54, OutsideHours.NextHour(30, 6), 4);
        Assert.Equal(54, OutsideHours.NextHour(31, 6), 4);
    }
}
