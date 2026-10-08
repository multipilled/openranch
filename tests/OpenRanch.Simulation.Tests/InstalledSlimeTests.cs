namespace OpenRanch.Simulation.Tests;

// Milestone 2's finish line: feed a slime, catch its plort, sell it at the price read from the install.
public class InstalledSlimeTests
{
    [GameFact]
    public void Pink_slime_eats_a_carrot_and_its_plort_sells_at_the_install_price()
    {
        var pink = new Slime(InstalledData.Species("PINK_SLIME"));
        var vacpack = new Vacpack();
        var market = new PlortMarket(InstalledData.Market);

        Assert.True(pink.Species.Eats("CARROT_VEGGIE"));
        pink.Advance(hours: 6); // let it get properly hungry
        Assert.True(pink.WantsToEat);

        var meal = pink.Feed("CARROT_VEGGIE");
        Assert.Equal(["PINK_PLORT"], meal.Produced);

        Assert.True(vacpack.TryAdd(meal.Produced[0]));
        var plort = vacpack.TakeSelected();
        Assert.Equal("PINK_PLORT", plort);

        var entry = InstalledData.Market.Get("PINK_PLORT");
        Assert.True(entry.BaseValue > 0);
        var demand = PlortMarket.Demand(market.Saturation("PINK_PLORT"), entry.FullSaturation);
        var expected = (int)Math.Round(entry.BaseValue * demand, MidpointRounding.ToEven);
        Assert.Equal(expected, market.Sell(plort!));

        // At full saturation the market pays exactly the base price from the install.
        market.StartDay(1);
        market.SetSaturation("PINK_PLORT", entry.FullSaturation);
        market.StartDay(2);
        Assert.True(market.Saturation("PINK_PLORT") < entry.FullSaturation); // it recovered overnight
        var fresh = new PlortMarket(InstalledData.Market);
        fresh.SetSaturation("PINK_PLORT", entry.FullSaturation / (1 - InstalledData.Market.SaturationRecovery));
        fresh.StartDay(1); // overnight recovery brings it down to exactly full
        Assert.Equal(entry.FullSaturation, fresh.Saturation("PINK_PLORT"), 3);
        Assert.Equal((int)Math.Round(entry.BaseValue, MidpointRounding.ToEven), fresh.Price("PINK_PLORT"));
    }

    [GameFact]
    public void Pink_tabby_and_rock_slimes_follow_their_diets()
    {
        var pink = InstalledData.Species("PINK_SLIME");
        var tabby = InstalledData.Species("TABBY_SLIME");
        var rock = InstalledData.Species("ROCK_SLIME");

        // Pink slimes eat every ordinary food group; tabbies eat meat; rocks eat veggies.
        Assert.True(pink.Eats("CARROT_VEGGIE") && pink.Eats("POGO_FRUIT") && pink.Eats("HEN"));
        Assert.True(tabby.Eats("HEN") && !tabby.Eats("CARROT_VEGGIE"));
        Assert.True(rock.Eats("CARROT_VEGGIE") && !rock.Eats("HEN"));
        Assert.DoesNotContain(pink.Foods, f => f.IsFavorite);

        foreach (var species in new[] { tabby, rock })
        {
            var info = InstalledData.Slimes.Get(species.Id);
            var favorite = Assert.Single(info.Diet.Favorites);
            var slime = new Slime(species);
            var meal = slime.Feed(favorite);
            Assert.True(meal.WasFavorite);
            Assert.Equal(info.Diet.FavoriteProductionCount, meal.Produced.Count);
            Assert.All(meal.Produced, p => Assert.True(InstalledData.Market.Get(p).BaseValue > 0));
        }
    }

    [GameFact]
    public void Market_buys_the_plorts_of_all_three_slimes()
    {
        foreach (var id in new[] { "PINK_PLORT", "TABBY_PLORT", "ROCK_PLORT" })
        {
            var e = InstalledData.Market.Get(id);
            Assert.True(e.BaseValue > 0 && e.FullSaturation > 0, id);
        }
        Assert.InRange(InstalledData.Market.SaturationRecovery, 0f, 1f);
    }

    [GameFact]
    public void A_fed_slime_stops_eating_until_hunger_returns()
    {
        var rock = new Slime(InstalledData.Species("ROCK_SLIME")) { Hunger = 0.5f };
        Assert.NotEmpty(rock.Feed("CARROT_VEGGIE").Produced);
        Assert.False(rock.WillEat("CARROT_VEGGIE")); // 0.5 minus one meal is under the eating threshold
        Assert.Same(Meal.None, rock.Feed("CARROT_VEGGIE"));
        rock.Advance(hours: 24);
        Assert.Equal(rock.Species.Hunger.Rest, rock.Hunger);
        Assert.True(rock.WillEat("CARROT_VEGGIE"));
    }
}
