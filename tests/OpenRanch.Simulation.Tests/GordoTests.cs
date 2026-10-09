using System.Numerics;
using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

// Gordo rules on a made-up gordo; the real ones come from the install (InstalledGordoTests).
public class GordoTests
{
    private static readonly SlimeEatingData Eating = new(0.333f, 0.333f, 0.15f, 0.3f, 0);
    private static readonly EmotionTuning Hunger = new(0.5f, 1f, 0.05f), Agitation = new(0f, 0f, 0.333f);
    private static readonly SlimeInfo Pink = new("Pink", "Pink", "PINK_SLIME", false, true,
        new SlimeDietData(["VEGGIES"], ["CARROT_VEGGIE"], [], ["PINK_PLORT"], 2), Eating, Hunger, Agitation);
    private static readonly SlimeSpecies Diet = SlimeSpecies.From(Pink, ["CARROT_VEGGIE", "BEET_VEGGIE", "HEN", "PINK_PLORT", "ROCK_PLORT"]);

    private static Gordo Make(int target = 5, IReadOnlyList<string>? rewards = null) =>
        new(new GordoInfo("PINK_GORDO", "gordoPink", "PINK_SLIME", target, 1.5f, rewards ?? ["KEY", "CRATE_REEF_01"], "PINK_SLIME",
            new Dictionary<int, IReadOnlyList<string>>()), Diet);

    [Fact]
    public void It_eats_only_its_slimes_diet_and_a_favourite_counts_double()
    {
        var gordo = Make();
        Assert.Equal(0, gordo.Feed("HEN"));
        Assert.Equal(0, gordo.Feed("ROCK_PLORT"));
        Assert.Equal(1, gordo.Feed("BEET_VEGGIE"));
        Assert.Equal(2, gordo.Feed("CARROT_VEGGIE"));
        Assert.Equal(3, gordo.EatenCount);
        Assert.False(gordo.Full);
    }

    [Fact]
    public void Once_full_it_eats_no_more()
    {
        var gordo = Make(target: 2);
        gordo.Feed("BEET_VEGGIE");
        gordo.Feed("CARROT_VEGGIE");
        Assert.True(gordo.Full);
        Assert.False(gordo.WillEat("BEET_VEGGIE"));
        Assert.Equal(3, gordo.EatenCount); // a favourite can take it past the target
    }

    [Fact]
    public void It_grows_to_its_growth_factor_and_wobbles_past_seventy_percent()
    {
        var gordo = Make(target: 10);
        Assert.Equal(1f, gordo.Scale);
        for (var i = 0; i < 7; i++)
            gordo.Feed("BEET_VEGGIE");
        Assert.Equal(0f, gordo.Wobble);
        Assert.Equal(1.35f, gordo.Scale, 4);
        for (var i = 0; i < 3; i++)
            gordo.Feed("BEET_VEGGIE");
        Assert.Equal(1.5f, gordo.Scale, 4);
        Assert.Equal(1f, gordo.Wobble, 4);
    }

    [Fact]
    public void There_are_thirteen_spawn_points()
    {
        var points = Gordo.SpawnOffsets;
        Assert.Equal(13, points.Count);
        Assert.Equal(Vector3.Zero, points[0]);
        Assert.All(points.Skip(1).Take(6), p => { Assert.Equal(0f, p.Y); Assert.Equal(1f, p.Length(), 4); });
        Assert.All(points.Skip(7).Take(3), p => Assert.Equal(0.866f, p.Y));
        Assert.All(points.Skip(10), p => Assert.Equal(-0.866f, p.Y));
        Assert.Equal(13, points.Distinct().Count());
    }

    [Fact]
    public void Bursting_drops_the_rewards_first_then_fills_with_its_slime()
    {
        var gordo = Make();
        var spawns = gordo.Burst(new Random(1));
        Assert.True(gordo.HasBurst);
        Assert.Equal(13, spawns.Count);
        Assert.Equal("KEY", spawns[0].Id);
        Assert.Equal(new Vector3(0, Gordo.SpawnHeight, 0), spawns[0].Offset);
        Assert.Equal("CRATE_REEF_01", spawns[1].Id);
        Assert.Equal(11, spawns.Count(s => s.Id == "PINK_SLIME"));
        Assert.Equal(13, spawns.Select(s => s.Offset).Distinct().Count());
        // Every other point is a unit offset times the spawn radius, around a point 1.7 m up.
        Assert.All(spawns.Skip(1), s => Assert.Equal(Gordo.SpawnRadius, Vector3.Distance(s.Offset, new Vector3(0, Gordo.SpawnHeight, 0)), 3));
    }

    [Fact]
    public void Explosions_push_harder_near_the_centre_and_hurt_more()
    {
        // Within 2 m everything gets the same push.
        Assert.Equal(Blast.BodyPush(600, 7, 0), Blast.BodyPush(600, 7, 2));
        Assert.Equal(600 * (5f / 7) * (5f / 7), Blast.BodyPush(600, 7, 1), 3);
        Assert.True(Blast.BodyPush(600, 7, 5) < Blast.BodyPush(600, 7, 3));
        Assert.Equal(45, Blast.PlayerDamage(15, 45, 7, 0));
        Assert.Equal(15, Blast.PlayerDamage(15, 45, 7, 7));
        Assert.Equal(30, Blast.PlayerDamage(15, 45, 7, 3.5f));
        Assert.Equal(0.6f, Blast.PlayerPush(600, 7, 0), 4);
    }

    [Fact]
    public void Radiation_over_the_maximum_turns_into_health_loss_in_tens()
    {
        var player = new PlayerVitals();
        Assert.Equal(0, player.AddRads(95));
        Assert.Equal(0, player.AddRads(9));
        Assert.Equal(10, player.AddRads(7)); // 111: one step of ten over 100
        Assert.Equal(101, player.Rads, 3);
        Assert.False(player.Damage(30, "test"));
        Assert.True(player.Damage(70, "test"));
        Assert.Equal(0, player.Health);
    }
}
