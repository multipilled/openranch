namespace OpenRanch.Ranch.Tests;

public class PlotRulesTests
{
    // Made-up enum numbers and prices: the real ones are read from the install.
    private sealed class FakeNames : IGameNames
    {
        private readonly Dictionary<(string, string), int> _values = new()
        {
            [(GameEnum.PlotType, "EMPTY")] = 1,
            [(GameEnum.PlotType, "CORRAL")] = 2,
            [(GameEnum.PlotType, "SILO")] = 5,
            [(GameEnum.PlotType, "GARDEN")] = 4,
            [(GameEnum.PlotUpgrade, "WALLS")] = 1,
            [(GameEnum.PlotUpgrade, "STORAGE2")] = 3,
            [(GameEnum.PlotUpgrade, "STORAGE3")] = 4,
            [(GameEnum.PlotUpgrade, "STORAGE4")] = 5,
            [(GameEnum.PlotUpgrade, "MIRACLE_MIX")] = 15,
            [(GameEnum.PlotUpgrade, "DELUXE_GARDEN")] = 16,
            [(GameEnum.PlotUpgrade, "DELUXE_COOP")] = 17,
            [(GameEnum.Progress, "OGDEN_REWARDS")] = 20,
            [(GameEnum.Progress, "MOCHI_REWARDS")] = 21,
        };

        public string Name(string label, long value) =>
            _values.FirstOrDefault(kv => kv.Key.Item1 == label && kv.Value == value).Key.Item2 ?? value.ToString();

        public int Value(string label, string name) => _values[(label, name)];
    }

    private const int Empty = 1, Corral = 2, Garden = 4, Silo = 5;
    private const int Walls = 1, Storage2 = 3, Storage3 = 4, Storage4 = 5, MiracleMix = 15, DeluxeGarden = 16;
    private const int OgdenRewards = 20;

    private static readonly PlotCatalog Catalog = new(
    [
        new PlotMenu(Empty, "EmptyPlotUI",
            [new ReplaceOffer(Corral, 250, "corral"), new ReplaceOffer(Silo, 500, "silo"), new ReplaceOffer(Garden, 250, "garden")], [], new Dictionary<string, int>()),
        new PlotMenu(Corral, "CorralUI", [new ReplaceOffer(Empty, 0, "demolish")], [new UpgradeOffer(Walls, 150, "walls")], new Dictionary<string, int>()),
        new PlotMenu(Silo, "SiloUI", [new ReplaceOffer(Empty, 0, "demolish")],
            [new UpgradeOffer(Storage2, 500, "storage2"), new UpgradeOffer(Storage3, 1000, "storage3"), new UpgradeOffer(Storage4, 1500, "storage4")],
            new Dictionary<string, int>()),
        new PlotMenu(Garden, "GardenUI", [new ReplaceOffer(Empty, 0, "demolish")],
            [new UpgradeOffer(MiracleMix, 1000, "miracleMix"), new UpgradeOffer(DeluxeGarden, 5000, "deluxe")], new Dictionary<string, int> { ["clearCrop"] = 10 }),
    ]);

    private static readonly PlotRules Rules = new(Catalog, new FakeNames());

    private static RanchState Ranch(int money) => new()
    {
        Player = { Money = money },
        Plots = [new Plot { Id = "a", Type = Empty }, new Plot { Id = "b", Type = Empty }],
    };

    [Fact]
    public void Building_costs_the_menu_price_and_starts_fresh()
    {
        var ranch = Ranch(1000);
        ranch.Plots[0].Upgrades.Add(99); // leftovers from before are not kept
        Assert.Equal(PlotPurchase.Done, Rules.Replace(ranch, "a", Corral));
        Assert.Equal(750, ranch.Player.Money);
        var plot = ranch.FindPlot("a")!;
        Assert.Equal(Corral, plot.Type);
        Assert.Empty(plot.Upgrades);
        Assert.Equal(0, ranch.Plots.IndexOf(plot));
    }

    [Fact]
    public void Only_what_the_menu_sells_can_be_bought()
    {
        var ranch = Ranch(10_000);
        Assert.Equal(PlotPurchase.NotOffered, Rules.Replace(ranch, "a", 7)); // no such build
        Assert.Equal(PlotPurchase.NotOffered, Rules.Upgrade(ranch, "a", Walls)); // empty plots have no upgrades
        Assert.Equal(PlotPurchase.NoSuchPlot, Rules.Replace(ranch, "zzz", Corral));
        Rules.Replace(ranch, "a", Corral);
        Assert.Equal(PlotPurchase.NotOffered, Rules.Replace(ranch, "a", Silo)); // a built plot only demolishes
        Assert.Equal(PlotPurchase.NotOffered, Rules.Upgrade(ranch, "a", Storage2));
        Assert.Equal(10_000 - 250, ranch.Player.Money);
    }

    [Fact]
    public void Money_must_cover_the_price()
    {
        var ranch = Ranch(249);
        Assert.Equal(PlotPurchase.NotEnoughMoney, Rules.Replace(ranch, "a", Corral));
        Assert.Equal(Empty, ranch.Plots[0].Type);
        Assert.Equal(249, ranch.Player.Money);
    }

    [Fact]
    public void Upgrades_are_bought_once()
    {
        var ranch = Ranch(1000);
        Rules.Replace(ranch, "a", Corral);
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", Walls));
        Assert.Equal(PlotPurchase.AlreadyOwned, Rules.Upgrade(ranch, "a", Walls));
        Assert.Equal([Walls], ranch.Plots[0].Upgrades);
        Assert.Equal(1000 - 250 - 150, ranch.Player.Money);
    }

    [Fact]
    public void Silo_storage_is_bought_in_order()
    {
        var ranch = Ranch(10_000);
        Rules.Replace(ranch, "a", Silo);
        Assert.Equal(PlotPurchase.NeedsEarlierUpgrade, Rules.Upgrade(ranch, "a", Storage3));
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", Storage2));
        Assert.Equal(PlotPurchase.NeedsEarlierUpgrade, Rules.Upgrade(ranch, "a", Storage4));
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", Storage3));
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", Storage4));
        Assert.Equal([Storage2, Storage3, Storage4], ranch.Plots[0].Upgrades);
    }

    [Fact]
    public void Ogden_rewards_unlock_the_garden_extras()
    {
        var ranch = Ranch(10_000);
        Rules.Replace(ranch, "a", Garden);
        Assert.Equal(PlotPurchase.NotUnlocked, Rules.Upgrade(ranch, "a", MiracleMix));
        ranch.Player.Progress[OgdenRewards] = 1;
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", MiracleMix));
        Assert.Equal(PlotPurchase.NotUnlocked, Rules.Upgrade(ranch, "a", DeluxeGarden));
        ranch.Player.Progress[OgdenRewards] = 2;
        Assert.Equal(PlotPurchase.Done, Rules.Upgrade(ranch, "a", DeluxeGarden));
    }

    [Fact]
    public void Demolishing_returns_the_plot_to_empty()
    {
        var ranch = Ranch(10_000);
        Rules.Replace(ranch, "a", Silo);
        Rules.Upgrade(ranch, "a", Storage2);
        ranch.Plots[0].Silo[1] = [new AmmoSlot(50, 10)];
        Assert.Equal(PlotPurchase.Done, Rules.Replace(ranch, "a", Empty));
        Assert.Equal(Empty, ranch.Plots[0].Type);
        Assert.Empty(ranch.Plots[0].Upgrades);
        Assert.Empty(ranch.Plots[0].Silo); // what was stored is lost
    }
}
