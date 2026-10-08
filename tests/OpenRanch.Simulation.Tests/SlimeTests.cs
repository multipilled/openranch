using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

public class SlimeTests
{
    // Made-up tuning; the real numbers come from the install.
    private static SlimeSpecies Species() => SlimeSpecies.From(
        new SlimeInfo("Test", "Test", "TEST_SLIME", false, true,
            new SlimeDietData(["VEGGIES"], ["BEET_VEGGIE"], ["SPICY_TOFU"], ["TEST_PLORT"], 2),
            new SlimeEatingData(0.333f, 0.333f, 0.15f, 0.3f, 0),
            new EmotionTuning(0.5f, 1f, 0.05f),
            new EmotionTuning(0f, 0f, 0.333f)),
        ["CARROT_VEGGIE", "BEET_VEGGIE", "GINGER_VEGGIE", "POGO_FRUIT", "HEN", "SPICY_TOFU", "PINK_PLORT"]);

    [Fact]
    public void Diet_expands_food_groups_and_extra_foods()
    {
        var s = Species();
        Assert.True(s.Eats("CARROT_VEGGIE"));
        Assert.True(s.Eats("GINGER_VEGGIE"));
        Assert.True(s.Eats("SPICY_TOFU"));
        Assert.False(s.Eats("POGO_FRUIT"));
        Assert.False(s.Eats("HEN"));
        Assert.False(s.Eats("PINK_PLORT"));
    }

    [Fact]
    public void Favorites_make_more_plorts()
    {
        var slime = new Slime(Species()) { Hunger = 1 };
        Assert.Equal(["TEST_PLORT"], slime.Feed("CARROT_VEGGIE").Produced);
        slime.Hunger = 1;
        var meal = slime.Feed("BEET_VEGGIE");
        Assert.True(meal.WasFavorite);
        Assert.Equal(["TEST_PLORT", "TEST_PLORT"], meal.Produced);
    }

    [Fact]
    public void Hunger_drifts_up_over_game_hours()
    {
        var slime = new Slime(Species());
        Assert.Equal(0.5f, slime.Hunger);
        slime.Advance(4);
        Assert.Equal(0.7f, slime.Hunger, 3);
        Assert.True(slime.IsHungry);
        slime.Advance(100);
        Assert.Equal(1f, slime.Hunger);
        Assert.True(slime.IsStarving);
    }

    [Fact]
    public void Starving_slimes_get_agitated_and_meals_calm_them()
    {
        var slime = new Slime(Species()) { Hunger = 1 };
        slime.Advance(10);
        Assert.True(slime.Agitation > 0.5f);
        var before = slime.Agitation;
        slime.Feed("BEET_VEGGIE");
        Assert.Equal(before - 0.3f, slime.Agitation, 3);
    }

    [Fact]
    public void Full_slimes_refuse_food_but_not_spicy_tofu()
    {
        var slime = new Slime(Species()) { Hunger = 0.1f };
        Assert.False(slime.WantsToEat);
        Assert.False(slime.WillEat("CARROT_VEGGIE"));
        Assert.Same(Meal.None, slime.Feed("CARROT_VEGGIE"));
        Assert.True(slime.WillEat("SPICY_TOFU"));
        Assert.NotEmpty(slime.Feed("SPICY_TOFU").Produced);
        Assert.Equal(0.1f, slime.Hunger); // spicy tofu doesn't count against hunger
    }

    [Fact]
    public void Item_kinds_follow_their_names()
    {
        Assert.Equal(ItemKind.Meat, Items.KindOf("STONY_HEN"));
        Assert.Equal(ItemKind.Meat, Items.KindOf("ELDER_ROOSTER"));
        Assert.Equal(ItemKind.Chick, Items.KindOf("CHICK"));
        Assert.True(Items.InFoodGroup("PINK_PLORT", "PLORTS"));
        Assert.False(Items.InFoodGroup("GOLD_PLORT", "PLORTS"));
        Assert.True(Items.InFoodGroup("PINK_SLIME", "NONTARRGOLD_SLIMES"));
        Assert.False(Items.InFoodGroup("TARR_SLIME", "NONTARRGOLD_SLIMES"));
    }
}
