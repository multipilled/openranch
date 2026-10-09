using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

// Milestone 5: every slime's diet and the largo and tarr rules, read from the install.
public class InstalledLargoTests
{
    private static readonly Lazy<Largos> Index = new(() => new Largos(InstalledData.Slimes));

    private static SlimeSpecies Species(string id) => SlimeSpecies.From(InstalledData.Slimes.Get(id), InstalledData.ItemIds, Index.Value);

    private static IEnumerable<SlimeInfo> Defined => InstalledData.Slimes.Slimes.Where(s => s.Eating is not null);

    // Base slimes that make plorts from food.
    private static IEnumerable<SlimeInfo> PlortMakers =>
        Defined.Where(s => !s.IsLargo && s.Diet.Produces.Any(Items.IsPlort) && s.Diet.FoodGroups.Count + s.Diet.AdditionalFoods.Count > 0);

    [GameFact]
    public void Every_largo_has_two_base_slimes_that_can_become_largos_and_a_unique_plort_pair()
    {
        var largos = Defined.Where(s => s.IsLargo).ToList();
        Assert.NotEmpty(largos);
        foreach (var largo in largos)
        {
            Assert.Equal(2, largo.BaseSlimes!.Count);
            Assert.All(largo.BaseSlimes, b => Assert.True(InstalledData.Slimes.Get(b).CanLargofy, $"{largo.Id}: {b}"));
            var plorts = largo.BaseSlimes.Select(b => InstalledData.Slimes.Get(b).Diet.Produces[0]).ToList();
            Assert.Equal(largo.Id, Index.Value.ForPlorts(plorts[0], plorts[1]));
        }
        Assert.Equal(largos.Count, Index.Value.Count);
    }

    [GameFact]
    public void A_largos_stored_diet_is_both_parents_diets_together()
    {
        foreach (var largo in Defined.Where(s => s.IsLargo))
        {
            var (a, b) = (InstalledData.Slimes.Get(largo.BaseSlimes![0]).Diet, InstalledData.Slimes.Get(largo.BaseSlimes[1]).Diet);
            Assert.Equal(a.FoodGroups.Union(b.FoodGroups).Order(), largo.Diet.FoodGroups.Order());
            Assert.Equal(a.Favorites.Union(b.Favorites).Order(), largo.Diet.Favorites.Order());
            Assert.Equal(a.AdditionalFoods.Union(b.AdditionalFoods).Order(), largo.Diet.AdditionalFoods.Order());
            Assert.Equal(a.Produces.Union(b.Produces), largo.Diet.Produces);
            Assert.Equal(a.FavoriteProductionCount, largo.Diet.FavoriteProductionCount);
        }
    }

    [GameFact]
    public void Every_base_slime_eats_its_diet_and_makes_its_plort()
    {
        var makers = PlortMakers.ToList();
        Assert.True(makers.Count >= 15, $"only {makers.Count} plort makers");
        foreach (var info in makers)
        {
            var species = Species(info.Id);
            var plorts = info.Diet.Produces.Where(Items.IsPlort).ToList();
            var foods = species.Foods.Where(f => f.Becomes is null).ToList();
            Assert.NotEmpty(foods);
            foreach (var food in foods)
            {
                var meal = new Slime(species) { Hunger = 1 }.Feed(food.Food);
                Assert.Equal(food.Food, meal.Food);
                var count = info.Diet.Favorites.Contains(food.Food) ? info.Diet.FavoriteProductionCount : 1;
                Assert.Equal(plorts.SelectMany(p => Enumerable.Repeat(p, count)), meal.Produced);
            }
        }
    }

    [GameFact]
    public void A_base_slime_becomes_a_largo_from_every_partner_plort_and_nothing_else()
    {
        foreach (var info in PlortMakers.Where(s => s.CanLargofy))
        {
            var species = Species(info.Id);
            var partners = Defined.Count(l => l.IsLargo && l.BaseSlimes!.Contains(info.Id));
            Assert.Equal(partners, species.Transforms.Count());
            foreach (var t in species.Transforms)
            {
                var largo = InstalledData.Slimes.Get(t.Becomes!);
                Assert.True(largo.IsLargo);
                Assert.Contains(info.Id, largo.BaseSlimes!);
                Assert.Contains(t.Food, largo.Diet.Produces);
            }
            Assert.False(species.Eats(info.Diet.Produces[0]));
        }
        Assert.Equal("PINK_ROCK_LARGO", new Slime(Species("PINK_SLIME")).Feed("ROCK_PLORT").Becomes);
        Assert.Equal("PINK_ROCK_LARGO", new Slime(Species("ROCK_SLIME")).Feed("PINK_PLORT").Becomes);
        Assert.False(Species("PINK_SLIME").Eats("QUICKSILVER_PLORT"));
        Assert.False(Species("PINK_SLIME").Eats("GOLD_PLORT"));
    }

    [GameFact]
    public void A_largo_becomes_a_tarr_from_any_third_plort()
    {
        foreach (var info in Defined.Where(s => s.IsLargo))
        {
            var species = Species(info.Id);
            Assert.NotEmpty(species.Transforms);
            Assert.All(species.Transforms, t => Assert.Equal(Largos.TarrSlime, t.Becomes));
            Assert.All(info.Diet.Produces, p => Assert.False(species.Eats(p)));
        }
        var largo = new Slime(Species("PINK_ROCK_LARGO")) { Hunger = 1 };
        Assert.Equal(["PINK_PLORT", "ROCK_PLORT"], largo.Feed("CARROT_VEGGIE").Produced);
        Assert.Equal(Largos.TarrSlime, largo.Feed("TABBY_PLORT").Becomes);
    }

    [GameFact]
    public void Tarrs_eat_slimes_and_largos_and_never_transform()
    {
        var tarr = Species("TARR_SLIME");
        Assert.True(tarr.Eats("PINK_SLIME") && tarr.Eats("PINK_ROCK_LARGO"));
        Assert.False(tarr.Eats("TARR_SLIME") || tarr.Eats("GOLD_SLIME") || tarr.Eats("LUCKY_SLIME"));
        Assert.Empty(tarr.Transforms);
        Assert.Equal(["TARR_SLIME"], new Slime(tarr) { Hunger = 1 }.Feed("PINK_SLIME").Produced);
    }

    [GameFact]
    public void Slimes_without_products_eat_nothing_and_the_lucky_slime_eats_without_making_anything()
    {
        foreach (var id in new[] { "FIRE_SLIME", "GLITCH_SLIME", "PUDDLE_SLIME", "QUICKSILVER_SLIME" })
            Assert.Empty(Species(id).Foods);
        var lucky = new Slime(Species("LUCKY_SLIME")) { Hunger = 1 };
        Assert.Empty(lucky.Feed("HEN").Produced);
        Assert.True(lucky.Hunger < 1);
    }
}
