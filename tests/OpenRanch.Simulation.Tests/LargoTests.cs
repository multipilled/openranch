using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

// Largo and tarr rules on made-up slimes; the real definitions come from the install (InstalledLargoTests).
public class LargoTests
{
    private static readonly SlimeEatingData Eating = new(0.333f, 0.333f, 0.15f, 0.3f, 0);
    private static readonly EmotionTuning Hunger = new(0.5f, 1f, 0.05f), Agitation = new(0f, 0f, 0.333f);
    private static readonly string[] Ids =
        ["CARROT_VEGGIE", "HEN", "SPICY_TOFU", "A_PLORT", "B_PLORT", "C_PLORT", "HONEY_PLORT", "QUICKSILVER_PLORT", "GOLD_PLORT",
         "A_SLIME", "B_SLIME", "A_B_LARGO", "TARR_SLIME", "GOLD_SLIME"];

    private static SlimeInfo Base(string id, string group, string plort) =>
        new(id, id, id, false, true, new SlimeDietData([group], [], ["SPICY_TOFU"], [plort], 2), Eating, Hunger, Agitation);

    private static readonly SlimeInfo A = Base("A_SLIME", "VEGGIES", "A_PLORT"), B = Base("B_SLIME", "MEAT", "B_PLORT");
    private static readonly SlimeInfo AB = new("AB", "AB", "A_B_LARGO", true, false,
        new SlimeDietData(["VEGGIES", "MEAT"], [], ["SPICY_TOFU"], ["A_PLORT", "B_PLORT"], 2), Eating, Hunger, Agitation, ["A_SLIME", "B_SLIME"]);
    private static readonly SlimeData All = SlimeData.FromInfos([A, B, AB]);
    private static readonly Largos Index = new(All);

    [Fact]
    public void Largos_are_found_by_their_plorts_in_either_order()
    {
        Assert.Equal("A_B_LARGO", Index.ForPlorts("A_PLORT", "B_PLORT"));
        Assert.Equal("A_B_LARGO", Index.ForPlorts("B_PLORT", "A_PLORT"));
        Assert.Null(Index.ForPlorts("A_PLORT", "C_PLORT"));
    }

    [Fact]
    public void A_base_slime_turns_into_the_largo_of_the_plort_it_eats()
    {
        var a = SlimeSpecies.From(A, Ids, Index);
        Assert.Equal("A_B_LARGO", a.FoodEffect("B_PLORT")!.Becomes);
        Assert.False(a.Eats("A_PLORT")); // its own plort
        Assert.False(a.Eats("C_PLORT")); // no largo of those two
        Assert.False(a.Eats("QUICKSILVER_PLORT"));

        var slime = new Slime(a) { Hunger = 0, Agitation = 0 };
        Assert.True(slime.WillEat("B_PLORT")); // wanted at any mood: agitation is raised to the 0.5 floor
        var meal = slime.Feed("B_PLORT");
        Assert.Equal("A_B_LARGO", meal.Becomes);
        Assert.Empty(meal.Produced);
    }

    [Fact]
    public void A_largo_turns_into_a_tarr_on_a_third_plort()
    {
        var ab = SlimeSpecies.From(AB, Ids, Index);
        Assert.Equal(Largos.TarrSlime, ab.FoodEffect("C_PLORT")!.Becomes);
        Assert.Equal(Largos.TarrSlime, ab.FoodEffect("HONEY_PLORT")!.Becomes);
        Assert.False(ab.Eats("A_PLORT"));
        Assert.False(ab.Eats("B_PLORT"));
        Assert.False(ab.Eats("GOLD_PLORT")); // not in the plort food group
        Assert.Equal(1f, new Slime(ab).Drive(ab.FoodEffect("HONEY_PLORT")!)); // 0.5 floor + 0.5 extra
    }

    [Fact]
    public void A_largo_makes_both_plorts_and_counts_two_meals()
    {
        var slime = new Slime(SlimeSpecies.From(AB, Ids, Index)) { Hunger = 1, Agitation = 0.5f };
        var meal = slime.Feed("CARROT_VEGGIE");
        Assert.Equal(["A_PLORT", "B_PLORT"], meal.Produced);
        Assert.Equal(1 - 2 * 0.333f, slime.Hunger, 4);
        Assert.Equal(0.5f - 2 * 0.15f, slime.Agitation, 4);
    }

    [Fact]
    public void Slimes_that_cant_largofy_ignore_plorts()
    {
        var gold = new SlimeInfo("G", "G", "GOLD_SLIME", false, false, new SlimeDietData(["GINGER"], [], [], ["GOLD_PLORT"], 5), Eating, Hunger, Agitation);
        Assert.Empty(SlimeSpecies.From(gold, Ids, Index).Transforms);
    }

    [Fact]
    public void Tarrs_eat_slimes_and_largos_but_not_tarrs_or_gold()
    {
        Assert.True(Items.InFoodGroup("A_B_LARGO", "NONTARRGOLD_SLIMES"));
        Assert.True(Items.InFoodGroup("A_SLIME", "NONTARRGOLD_SLIMES"));
        Assert.False(Items.InFoodGroup("TARR_SLIME", "NONTARRGOLD_SLIMES"));
        Assert.False(Items.InFoodGroup("GOLD_SLIME", "NONTARRGOLD_SLIMES"));
    }

    [Fact]
    public void A_diet_that_produces_nothing_eats_without_making_anything()
    {
        var lucky = new SlimeInfo("L", "L", "LUCKY_SLIME", false, false, new SlimeDietData(["MEAT"], [], [], ["NONE"], 2), Eating, Hunger, Agitation);
        var slime = new Slime(SlimeSpecies.From(lucky, Ids, Index)) { Hunger = 1 };
        var meal = slime.Feed("HEN");
        Assert.Equal("HEN", meal.Food);
        Assert.Empty(meal.Produced);
        Assert.Equal(1 - 0.333f, slime.Hunger, 4);

        var empty = new SlimeInfo("F", "F", "FIRE_SLIME", false, false, new SlimeDietData([], [], [], [], 2), Eating, Hunger, Agitation);
        Assert.Empty(SlimeSpecies.From(empty, Ids, Index).Foods);
    }
}
