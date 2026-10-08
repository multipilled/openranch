namespace OpenRanch.Simulation.Tests;

public class VacpackTests
{
    [Fact]
    public void Four_slots_hold_four_kinds_of_item()
    {
        var pack = new Vacpack();
        Assert.Equal(4, pack.UsableSlots);
        foreach (var id in new[] { "PINK_PLORT", "CARROT_VEGGIE", "POGO_FRUIT", "HEN" })
            Assert.True(pack.TryAdd(id));
        Assert.False(pack.TryAdd("ROCK_PLORT"));
        Assert.False(pack.CanAdd("ROCK_PLORT"));
        Assert.True(pack.TryAdd("PINK_PLORT")); // joins its own slot
        Assert.Equal(2, pack.Count("PINK_PLORT"));
    }

    [Fact]
    public void A_slot_stops_at_its_limit_until_upgraded()
    {
        var pack = new Vacpack();
        for (var i = 0; i < Vacpack.SlotLimits[0]; i++)
            Assert.True(pack.TryAdd("PINK_PLORT"));
        Assert.False(pack.TryAdd("PINK_PLORT"));
        Assert.Equal(20, pack.Count("PINK_PLORT"));

        pack.UpgradeCapacity();
        Assert.Equal(30, pack.MaxPerSlot);
        Assert.True(pack.TryAdd("PINK_PLORT"));
        for (var i = 0; i < 10; i++)
            pack.UpgradeCapacity();
        Assert.Equal(100, pack.MaxPerSlot);
    }

    [Fact]
    public void A_full_slot_does_not_spill_into_an_empty_one()
    {
        var pack = new Vacpack();
        for (var i = 0; i < 20; i++)
            pack.TryAdd("PINK_PLORT");
        Assert.False(pack.TryAdd("PINK_PLORT"));
        Assert.Null(pack[1]);
    }

    [Fact]
    public void Liquids_only_go_in_the_fifth_slot_once_it_is_unlocked()
    {
        var pack = new Vacpack();
        Assert.False(pack.TryAdd("WATER_LIQUID"));
        pack.UnlockLiquidSlot();
        Assert.Equal(5, pack.UsableSlots);
        Assert.True(pack.TryAdd("WATER_LIQUID"));
        Assert.Equal(Vacpack.WaterPerVac, pack[Vacpack.LiquidSlot]!.Count);
        for (var i = 0; i < 4; i++)
            Assert.True(pack.TryAdd($"ITEM{i}_PLORT"));
        Assert.False(pack.TryAdd("PINK_PLORT")); // the liquid slot won't take solids
    }

    [Fact]
    public void Shooting_empties_the_selected_slot()
    {
        var pack = new Vacpack();
        pack.TryAdd("CARROT_VEGGIE");
        pack.TryAdd("CARROT_VEGGIE");
        Assert.Equal("CARROT_VEGGIE", pack.TakeSelected());
        Assert.Equal("CARROT_VEGGIE", pack.TakeSelected());
        Assert.Null(pack.TakeSelected());
        Assert.Null(pack[0]);
        pack.SelectPrevious();
        Assert.Equal(3, pack.SelectedSlot);
    }

    [Fact]
    public void Items_the_pack_cannot_hold_are_refused()
    {
        var pack = new Vacpack(id => id != "GORDO_THING");
        Assert.False(pack.TryAdd("GORDO_THING"));
        Assert.True(pack.TryAdd("PINK_PLORT"));
    }
}
