namespace OpenRanch.Simulation.Tests;

public class SlimeTraitsTests
{
    [Fact]
    public void Calm_slimes_wait_longer_between_hovers_and_rolls()
    {
        Assert.Equal(SlimeTraits.HoverMaxDelay, SlimeTraits.Delay(SlimeTraits.HoverMinDelay, SlimeTraits.HoverMaxDelay, 0f, 0.5));
        Assert.Equal(SlimeTraits.RollMinDelay, SlimeTraits.Delay(SlimeTraits.RollMinDelay, SlimeTraits.RollMaxDelay, 1f, 0.5));
        Assert.Equal(9f, SlimeTraits.Delay(3, 15, 0.5f, 0.5), 4);
        Assert.Equal(9f + 12 * 0.1f, SlimeTraits.Delay(3, 15, 0.5f, 1.0), 4); // jitter at most 0.1 of the range
    }

    [Fact]
    public void Hover_lift_fades_to_nothing_at_hover_height()
    {
        Assert.Equal(SlimeTraits.HoverLift, SlimeTraits.HoverLiftAt(0));
        Assert.Equal(0f, SlimeTraits.HoverLiftAt(SlimeTraits.HoverHeight), 4);
    }
}
