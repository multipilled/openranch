namespace OpenRanch.Simulation.Tests;

// The code-only ability and feeding-habit rules (Abilities.cs, FeedingHabits.cs).
public class AbilityTests
{
    [Fact]
    public void Delays_run_from_the_calm_maximum_to_the_agitated_minimum()
    {
        Assert.Equal(BoomSlime.MaxDelay, AbilityDelay.Pick(BoomSlime.MinDelay, BoomSlime.MaxDelay, 0f, 0.5), 3);
        Assert.Equal(BoomSlime.MinDelay, AbilityDelay.Pick(BoomSlime.MinDelay, BoomSlime.MaxDelay, 1f, 0.5), 3);
        Assert.Equal(RadSlime.MinDelay, AbilityDelay.Pick(RadSlime.MinDelay, RadSlime.MaxDelay, 1f, 0.5), 3);
        // Quantum delays are a straight line by agitation, no jitter.
        Assert.Equal(30f, QuantumSlime.Delay(5, 30, 0), 3);
        Assert.Equal(17.5f, QuantumSlime.Delay(5, 30, 0.5f), 3);
        Assert.Equal(5f, QuantumSlime.Delay(5, 30, 1), 3);
    }

    [Fact]
    public void The_rad_aura_swells_slowly_and_shrinks_fast()
    {
        var scale = 1f;
        for (var i = 0; i < 50; i++)
            scale = RadSlime.StepScale(scale, RadSlime.ExpandFactor, 0.02f);
        Assert.Equal(1f + 50 * 0.02f / 3f, scale, 4); // 1/3 of a unit per second
        for (var i = 0; i < 200; i++)
            scale = RadSlime.StepScale(scale, RadSlime.ExpandFactor, 0.02f);
        Assert.Equal(1.5f, scale, 4);
        scale = RadSlime.StepScale(scale, 1f, 0.02f);
        Assert.Equal(1.5f - 0.02f / 3f, scale, 4); // back down just as slowly
        Assert.Equal(0.9f, RadSlime.StepScale(1f, 0f, 0.02f), 4); // below normal (calmed): 5 units per second
    }

    [Fact]
    public void Crystal_spike_counts_scale_with_mass()
    {
        Assert.Equal((2, 4), CrystalSlime.SmallSpikes(0.5f));
        Assert.Equal((4, 7), CrystalSlime.SmallSpikes(1f));
    }

    [Fact]
    public void Quantum_vibration_starts_past_the_cutoff()
    {
        Assert.Equal(0f, QuantumSlime.Vibration(0.2f, 0.2f));
        Assert.Equal(0.5f, QuantumSlime.Vibration(0.6f, 0.2f), 4);
        Assert.Equal(1f, QuantumSlime.Vibration(1f, 0.2f), 4);
    }

    [Fact]
    public void Dervish_lift_fades_to_nothing_at_its_height()
    {
        Assert.Equal(600f, DervishSlime.LiftAt(0, false));
        Assert.Equal(0f, DervishSlime.LiftAt(5, false), 3);
        Assert.Equal(300f, DervishSlime.LiftAt(4.5f, true), 3);
    }

    [Fact]
    public void Pollen_grows_only_past_the_start_agitation()
    {
        Assert.Equal(0f, TangleSlime.CloudTarget(0.5f, 0.75f));
        Assert.Equal(0.5f, TangleSlime.CloudTarget(0.875f, 0.75f), 4);
        Assert.Equal(1f, TangleSlime.CloudTarget(1f, 0.75f), 4);
    }

    [Fact]
    public void Glints_come_sooner_and_farther_when_agitated()
    {
        Assert.Equal(0.5f, MosaicSlime.AdjustHours(0.5f, 0), 4);
        Assert.Equal(0.1f, MosaicSlime.AdjustHours(0.5f, 1), 4);
        Assert.Equal(7.5f, MosaicSlime.SpawnRadius(0), 4);
        Assert.Equal(30f, MosaicSlime.SpawnRadius(1), 4);
    }

    [Fact]
    public void Stalkers_leap_by_distance_and_carry_harder_with_a_load()
    {
        Assert.Equal(MathF.Sqrt(6 * 9.81f) * 1.2f, Stalking.LeapSpeed(6, 9.81f), 4);
        Assert.Equal(0.81f, Stalking.Relevancy(0.9f), 4);
        Assert.Equal(6f * 1.3f / 1f, Gathering.CarryJump(6, 0.3f, 1f), 4);
    }

    [Fact]
    public void Gold_slimes_drop_plorts_for_most_things_but_ginger()
    {
        Assert.True(GoldSlime.CausesPlort("CARROT_VEGGIE"));
        Assert.True(GoldSlime.CausesPlort("PINK_PLORT"));
        Assert.True(GoldSlime.CausesPlort("HEN"));
        Assert.False(GoldSlime.CausesPlort("GINGER_VEGGIE"));
        Assert.False(GoldSlime.CausesPlort("GOLD_PLORT"));
        Assert.False(GoldSlime.CausesPlort("PINK_SLIME"));
    }

    [Fact]
    public void Lucky_coins_double_up_to_six_bundles()
    {
        Assert.Equal(2, LuckySlime.Bundles(0));
        Assert.Equal(4, LuckySlime.Bundles(2));
        Assert.Equal(6, LuckySlime.Bundles(4));
        Assert.Equal(6, LuckySlime.Bundles(6));
    }

    [Fact]
    public void Grazers_head_home_harder_as_their_time_runs_out()
    {
        Assert.Equal(0f, Grazing.GotoRelevancy(0.5, 0.5f), 4);
        Assert.Equal(0.5f, Grazing.GotoRelevancy(0.25, 0.5f), 4);
        Assert.Equal(1f, Grazing.GotoRelevancy(0, 0.5f), 4);
        Assert.True(Grazing.TooDense(5, 4, 0, 8));
        Assert.True(Grazing.TooDense(4, 4, 9, 8));
        Assert.False(Grazing.TooDense(4, 4, 8, 8));
    }
}
