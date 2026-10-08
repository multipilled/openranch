namespace OpenRanch.Simulation.Tests;

public class SlimeMotionTests
{
    [Fact]
    public void Food_jumps_grow_from_forty_percent_at_the_hungry_cutoff_to_full_at_full_hunger()
    {
        Assert.Equal(4f, SlimeMotion.FoodJumpStrength(0.3f, 10f), 4);
        Assert.Equal(4f, SlimeMotion.FoodJumpStrength(Slime.HungryCutoff, 10f), 4);
        Assert.Equal(10f, SlimeMotion.FoodJumpStrength(1f, 10f), 4);
        // Halfway from the cutoff to full hunger adds a quarter of the remaining 60%.
        var halfway = (Slime.HungryCutoff + 1f) / 2f;
        Assert.Equal(10f * (0.4f + 0.6f * 0.25f), SlimeMotion.FoodJumpStrength(halfway, 10f), 3);
    }

    [Fact]
    public void Food_beats_wandering_once_drive_squared_outweighs_it()
    {
        Assert.False(SlimeMotion.PrefersFood(0.4f)); // 0.152 < 0.2
        Assert.True(SlimeMotion.PrefersFood(0.5f)); // 0.2375 > 0.2
        Assert.True(SlimeMotion.PrefersFood(1f));
    }

    [Fact]
    public void Nearer_food_scores_higher_at_the_same_drive()
    {
        Assert.True(SlimeMotion.FoodScore(0.8f, 4f) > SlimeMotion.FoodScore(0.8f, 25f));
        Assert.Equal(0.5f, SlimeMotion.LeanTowardTarget(15f));
        Assert.Equal(1f, SlimeMotion.LeanTowardTarget(60f));
    }

    [Fact]
    public void Wander_moods_follow_their_weights()
    {
        Assert.Equal(WanderMood.Rest, SlimeMotion.PickMood(0.1));
        Assert.Equal(WanderMood.Scoot, SlimeMotion.PickMood(0.3));
        Assert.Equal(WanderMood.Hop, SlimeMotion.PickMood(0.6));
        Assert.Equal(WanderMood.Hop, SlimeMotion.PickMood(0.999));
    }

    [Fact]
    public void Clock_turns_real_seconds_into_game_hours_and_days()
    {
        var clock = new GameClock(secondsPerGameDay: 1440, startHour: 23);
        Assert.Equal(1.0, clock.HoursFor(60), 6); // one real minute is one game hour
        var (hours, newDay) = clock.Advance(90);
        Assert.Equal(1.5, hours, 6);
        Assert.True(newDay);
        Assert.Equal(1, clock.Day);
        Assert.Equal(0.5f, clock.HourOfDay, 4);
    }

    [Fact]
    public void Wallet_adds_and_spends()
    {
        var wallet = new Wallet();
        var changes = new List<int>();
        wallet.Changed += changes.Add;
        wallet.Add(36);
        Assert.False(wallet.TrySpend(40));
        Assert.True(wallet.TrySpend(30));
        Assert.Equal(6, wallet.Coins);
        Assert.Equal([36, -30], changes);
    }
}
