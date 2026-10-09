using OpenRanch.Formats.Game;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

public class PlotMachinesTests(ITestOutputHelper output)
{
    private const double Hour = 3600, Day2 = 86_400 + 6 * Hour;
    // Made-up enum numbers: the real ones are read from the install.
    private const int Carrot = 1, Beet = 2, PinkPlort = 3;

    [Fact]
    public void A_store_fills_the_slot_holding_the_item_first_and_refuses_when_that_slot_is_full()
    {
        var food = new StoreRules(2, "FOOD", Slots: 2, MaxPerSlot: 2);
        var slots = new List<AmmoSlot>();
        Assert.True(food.TryAdd(slots, Carrot, "CARROT_VEGGIE"));
        Assert.True(food.TryAdd(slots, Carrot, "CARROT_VEGGIE"));
        Assert.False(food.TryAdd(slots, Carrot, "CARROT_VEGGIE")); // its slot is full; the empty one isn't used
        Assert.True(food.TryAdd(slots, Beet, "BEET_VEGGIE"));
        Assert.False(food.TryAdd(slots, PinkPlort, "PINK_PLORT"));
        Assert.Equal([new AmmoSlot(Carrot, 2), new AmmoSlot(Beet, 1)], slots);
        Assert.Equal(Carrot, StoreRules.TakeOne(slots, 0));
        Assert.Equal(Beet, StoreRules.TakeOne(slots, 1));
        Assert.Null(StoreRules.TakeOne(slots, 1));
        // A catcher fills its own slot only.
        Assert.True(new StoreRules(0, "PLORT", 4, 100).TryAdd(slots = [], PinkPlort, "PINK_PLORT", slot: 2));
        Assert.Equal(PinkPlort, slots[2].Id);
        Assert.False(new StoreRules(0, "PLORT", 4, 100).TryAdd(slots, PinkPlort, "QUICKSILVER_PLORT"));
    }

    [Fact]
    public void A_feeder_queues_a_batch_every_cycle_and_a_collector_sweeps_every_period()
    {
        var feeder = new FeederRules(new Dictionary<int, float> { [0] = 6, [1] = 9, [2] = 3 }, 6);
        var started = feeder.Start(0, Day2);
        Assert.Equal(new FeederState(Day2 + 6 * Hour, 0, 0), started);
        Assert.Equal(new FeederState(Day2 + 18 * Hour, 12, 0), feeder.CatchUp(started, Day2 + 13 * Hour));
        Assert.Equal(Day2 + 9 * Hour, feeder.Start(1, Day2).NextTime);

        var collector = new CollectorRules(1);
        Assert.Equal(Day2 + Hour, collector.Start(Day2));
        Assert.Equal((false, Day2 + Hour), collector.Tick(Day2 + Hour, Day2 + 0.5 * Hour));
        Assert.Equal((true, Day2 + 2 * Hour), collector.Tick(Day2 + Hour, Day2 + 1.5 * Hour));
    }

    [Fact]
    public void The_incinerator_burns_all_but_fire_and_turns_food_into_ash()
    {
        var ash = new AshRules(1, 20);
        Assert.False(AshRules.Burns("FIRE_PLORT"));
        Assert.True(AshRules.Burns("PINK_SLIME"));
        Assert.Equal(5, ash.Burn(4, "CARROT_VEGGIE", trough: true));
        Assert.Equal(4, ash.Burn(4, "CARROT_VEGGIE", trough: false));
        Assert.Equal(4, ash.Burn(4, "PINK_PLORT", trough: true));
        Assert.Equal(20, ash.Burn(20, "HEN", trough: true));
    }

    [GameFact]
    public void Install_plot_machines()
    {
        using var scripts = new GameScripts(GameFactAttribute.Install!);
        using var names = new GameEnums(GameFactAttribute.Install!);
        var layout = PlotLayout.Read(scripts, scripts.Assets.File("level3")!, "zoneRANCH");
        var data = PlotMachineData.Read(layout, names);
        foreach (var (type, stores) in data.Stores)
            output.WriteLine($"{names.PlotType(type)} stores: {string.Join(", ", stores.Select(s => $"{s.KindName} {s.Slots}x{s.MaxPerSlot}"))}");
        foreach (var (type, catchers) in data.Catchers)
            output.WriteLine($"{names.PlotType(type)} catchers: {string.Join(", ", catchers.Select(c => $"{c.Store.KindName} slot {c.Slot}"))}");
        foreach (var (type, f) in data.Feeders)
            output.WriteLine($"{names.PlotType(type)} feeder: {f.ItemsPerFeeding} per feeding");
        foreach (var (type, c) in data.Collectors)
            output.WriteLine($"{names.PlotType(type)} collector: every {c.Rules.PeriodHours} h, area {(c.Area is null ? "none" : c.Area.Region.Size.ToString())}");
        foreach (var (type, a) in data.Ash)
            output.WriteLine($"{names.PlotType(type)} ash: {a.AshPerItem} per food, at most {a.MaxAsh}");
        var corral = names.Value(GameEnum.PlotType, "CORRAL");
        var silo = names.Value(GameEnum.PlotType, "SILO");
        var incinerator = names.Value(GameEnum.PlotType, "INCINERATOR");
        Assert.True(data.Feeders.ContainsKey(corral));
        Assert.True(data.Collectors.ContainsKey(corral));
        Assert.True(data.Catchers.ContainsKey(silo));
        Assert.True(data.Ash.ContainsKey(incinerator));
    }
}
