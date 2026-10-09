namespace OpenRanch.Simulation.Tests;

// Feral rules on a made-up slime; the real settings come from the install (InstalledAbilityTests).
public class FeralTests
{
    private static Slime NewSlime() => new(InstalledFree.Species());

    [Fact]
    public void Turning_feral_starts_its_clock_and_it_eats_however_full()
    {
        var slime = NewSlime();
        slime.Hunger = 0;
        Assert.False(slime.WillEat("CARROT_VEGGIE"));
        var feral = new Feral(new FeralSettings(false, true, 3), slime);
        feral.SetFeral(10);
        Assert.True(slime.IsFeral);
        Assert.True(slime.WantsToEat);
        Assert.True(slime.WillEat("CARROT_VEGGIE"));
        Assert.False(slime.WillEat("ROCK_PLORT")); // still only its diet
        Assert.Equal(13, feral.ExpiresAt);
    }

    [Fact]
    public void Only_slimes_that_say_so_turn_feral_at_full_agitation()
    {
        var calm = NewSlime();
        var hunter = new Feral(new FeralSettings(true, true, 3), calm);
        calm.Agitation = 0.99f;
        hunter.Update(0, true);
        Assert.False(calm.IsFeral);
        calm.Agitation = Feral.AgitationTrigger;
        hunter.Update(0, true);
        Assert.True(calm.IsFeral);

        var other = NewSlime();
        other.Agitation = 1;
        new Feral(new FeralSettings(false, true, 3), other).Update(0, true);
        Assert.False(other.IsFeral);
    }

    [Fact]
    public void Out_of_time_it_poofs_unless_on_the_ranch_or_in_the_wilds()
    {
        var slime = NewSlime();
        var feral = new Feral(new FeralSettings(false, true, 3), slime);
        feral.SetFeral(0);
        Assert.False(feral.Update(2.9, false));
        Assert.False(feral.Update(3, true));
        Assert.Equal(4, feral.ExpiresAt);
        Assert.True(feral.Update(4, false));
    }

    [Fact]
    public void Eating_calms_it_when_its_prefab_says_so_and_clearing_can_calm_agitation()
    {
        var slime = NewSlime();
        var feral = new Feral(new FeralSettings(false, false, 3), slime);
        feral.SetFeral(0);
        feral.DidEat();
        Assert.True(slime.IsFeral);
        slime.Agitation = 0.8f;
        feral.Clear(calm: true);
        Assert.False(slime.IsFeral);
        Assert.Equal(0.3f, slime.Agitation, 4);
        Assert.Equal(double.PositiveInfinity, feral.ExpiresAt);

        var calmed = new Feral(new FeralSettings(false, true, 3), slime);
        calmed.SetFeral(0);
        calmed.DidEat();
        Assert.False(slime.IsFeral);
    }

    [Fact]
    public void The_stomp_leaps_by_distance_and_gravity_within_its_range()
    {
        Assert.False(FeralStomp.InRange(4.9f));
        Assert.True(FeralStomp.InRange(5f));
        Assert.True(FeralStomp.InRange(20f));
        Assert.False(FeralStomp.InRange(20.1f));
        Assert.Equal(MathF.Sqrt(10 * 9.81f) * 1.2f * 1.4f, FeralStomp.LeapSpeed(10, 9.81f), 4);
        Assert.Equal(0.95f * 0.25f, PlayerAttack.GotoRelevancy(0.5f), 5);
    }
}

// A plain pink-like species for rule tests that don't need the install.
internal static class InstalledFree
{
    private static readonly OpenRanch.Formats.Game.SlimeEatingData Eating = new(0.333f, 0.333f, 0.15f, 0.3f, 0);
    private static readonly OpenRanch.Formats.Game.EmotionTuning Hunger = new(0.5f, 1f, 0.05f), Agitation = new(0f, 0f, 0.333f);

    public static SlimeSpecies Species() => SlimeSpecies.From(
        new OpenRanch.Formats.Game.SlimeInfo("Pink", "Pink", "PINK_SLIME", false, true,
            new OpenRanch.Formats.Game.SlimeDietData(["VEGGIES"], ["CARROT_VEGGIE"], [], ["PINK_PLORT"], 2), Eating, Hunger, Agitation),
        ["CARROT_VEGGIE", "BEET_VEGGIE", "PINK_PLORT", "ROCK_PLORT"]);
}
