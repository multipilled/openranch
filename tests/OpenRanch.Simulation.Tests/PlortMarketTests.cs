using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

public class PlortMarketTests
{
    // Made-up numbers; the real ones come from the install.
    private static MarketData Data(float recovery = 0.25f) =>
        new([new MarketEntryData("TEST_PLORT", 10, 20), new MarketEntryData("OTHER_PLORT", 8, 40)], 0.005f, recovery, 5);

    [Fact]
    public void A_new_game_starts_half_saturated()
    {
        var market = new PlortMarket(Data());
        Assert.Equal(10f, market.Saturation("TEST_PLORT"));
        Assert.Equal(15, market.Price("TEST_PLORT")); // 10 * (1 + 0.5)
        Assert.Null(market.Price("PINK_SLIME"));
        Assert.Null(market.Sell("PINK_SLIME"));
    }

    [Fact]
    public void Selling_lowers_the_price_from_the_next_day()
    {
        var market = new PlortMarket(Data(recovery: 0));
        Assert.Equal(15 * 10, market.Sell("TEST_PLORT", 10));
        Assert.Equal(15, market.Price("TEST_PLORT")); // unchanged until midnight
        market.StartDay(1);
        Assert.Equal(10, market.Price("TEST_PLORT")); // fully saturated: base price
        Assert.Equal(-5, market.PriceChange("TEST_PLORT"));
        Assert.Equal(12, market.Price("OTHER_PLORT")); // unsold plorts keep their price
    }

    [Fact]
    public void Price_never_falls_below_base_or_rises_above_double()
    {
        Assert.Equal(1f, PlortMarket.Demand(1000, 20));
        Assert.Equal(2f, PlortMarket.Demand(0, 20));
        Assert.Equal(1.5f, PlortMarket.Demand(10, 20));
    }

    [Fact]
    public void Saturation_recovers_each_day()
    {
        var market = new PlortMarket(Data(recovery: 0.25f));
        market.Sell("TEST_PLORT", 30); // 10 + 30 = 40
        market.StartDay(1);
        Assert.Equal(30f, market.Saturation("TEST_PLORT"), 3);
        market.StartDay(2);
        Assert.Equal(22.5f, market.Saturation("TEST_PLORT"), 3);
        for (var day = 3; day < 40; day++)
            market.StartDay(day);
        Assert.Equal(20, market.Price("TEST_PLORT")); // nearly nothing sold lately: double base
    }

    [Fact]
    public void Market_closes_just_after_midnight()
    {
        var market = new PlortMarket(Data());
        Assert.True(market.IsClosed(0.05f)); // 3 minutes past midnight
        Assert.False(market.IsClosed(0.1f)); // 6 minutes past
        Assert.False(new PlortMarket(Data(), dynamic: false).IsClosed(0f));
    }

    [Fact]
    public void Fixed_markets_pay_one_and_a_half_times_base()
    {
        var market = new PlortMarket(Data(), dynamic: false);
        market.Sell("TEST_PLORT", 100);
        market.StartDay(1);
        Assert.Equal(15, market.Price("TEST_PLORT"));
    }

    [Fact]
    public void Wandering_mood_stays_in_the_original_spread_and_moves_slowly()
    {
        var mood = new WanderingMood(1234);
        for (var day = 0; day < 200; day++)
        {
            Assert.InRange(mood.Market(day), WanderingMood.Low, WanderingMood.High);
            Assert.InRange(mood.Item(day, "PINK_PLORT"), WanderingMood.Low, WanderingMood.High);
            Assert.True(Math.Abs(mood.Market(day + 1) - mood.Market(day)) < 0.25f);
        }
    }

    [Fact]
    public void A_loaded_market_prices_from_the_saved_saturation_without_recovering_first()
    {
        var market = new PlortMarket(Data(recovery: 0.25f));
        market.Load(new Dictionary<string, float> { ["TEST_PLORT"] = 20, ["NOT_SOLD_HERE"] = 3 });
        market.Open(36);
        Assert.Equal(36, market.Day);
        Assert.Equal(20f, market.Saturation("TEST_PLORT"));
        Assert.Equal(10, market.Price("TEST_PLORT")); // fully saturated: base price
        Assert.Equal(0, market.PriceChange("TEST_PLORT")); // yesterday's price counts as the base value
        Assert.Equal(20f, market.Saturations["OTHER_PLORT"]); // not in the save: as a new game
        // The next midnight recovers as usual.
        market.StartDay(37);
        Assert.Equal(15f, market.Saturation("TEST_PLORT"), 3);
    }
}
