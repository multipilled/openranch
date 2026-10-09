using OpenRanch.Simulation;

namespace OpenRanch.Ranch.Tests;

public class PlayerVacpackTests
{
    // Made-up enum numbers: the real ones are read from the install.
    private sealed class FakeNames : IGameNames
    {
        private readonly Dictionary<(string, string), int> _values = new()
        {
            [(GameEnum.ItemId, "NONE")] = 0,
            [(GameEnum.ItemId, "PINK_SLIME")] = 1,
            [(GameEnum.ItemId, "CARROT_VEGGIE")] = 2,
            [(GameEnum.ItemId, "WATER_LIQUID")] = 3,
            [(GameEnum.AmmoMode, "DEFAULT")] = 0,
            [(GameEnum.AmmoMode, "NIMBLE_VALLEY")] = 1,
            [(GameEnum.Emotion, "HUNGER")] = 0,
            [(GameEnum.Emotion, "AGITATION")] = 1,
            [(GameEnum.Emotion, "FEAR")] = 2,
            [(GameEnum.PlayerUpgrade, "AMMO_1")] = 6,
            [(GameEnum.PlayerUpgrade, "AMMO_2")] = 7,
            [(GameEnum.PlayerUpgrade, "LIQUID_SLOT")] = 13,
        };

        public string Name(string label, long value) =>
            _values.FirstOrDefault(kv => kv.Key.Item1 == label && kv.Value == value).Key.Item2 ?? value.ToString();

        public int Value(string label, string name) => _values[(label, name)];
    }

    private static readonly FakeNames Names = new();

    private static Rancher Player() => new()
    {
        Upgrades = [6, 13, 7],
        Ammo =
        {
            [0] =
            [
                new AmmoSlot(1, 35) { Emotions = { [0] = 0.25f, [1] = 0.5f, [2] = 0.125f } },
                new AmmoSlot(0, 0),
                new AmmoSlot(2, 12),
                new AmmoSlot(0, 0),
                new AmmoSlot(3, 40),
            ],
            [1] = [new AmmoSlot(0, 0)],
        },
    };

    [Fact]
    public void Loading_fills_the_slots_in_order_with_the_upgrades_capacity_and_liquid_slot()
    {
        var pack = new Vacpack();
        PlayerVacpack.Load(Player(), Names, pack);
        Assert.Equal(40, pack.MaxPerSlot);
        Assert.Equal(5, pack.UsableSlots);
        Assert.Equal(new VacSlot("PINK_SLIME", 35).Id, pack[0]!.Id);
        Assert.Equal(35, pack[0]!.Count);
        Assert.Equal(0.5f, pack[0]!.Moods["AGITATION"]);
        Assert.Null(pack[1]);
        Assert.Equal(12, pack[2]!.Count);
        Assert.Equal("WATER_LIQUID", pack[4]!.Id);
    }

    [Fact]
    public void A_loaded_vacpack_saves_back_as_it_was_stored()
    {
        var player = Player();
        var pack = new Vacpack();
        PlayerVacpack.Load(player, Names, pack);
        var saved = PlayerVacpack.Save(pack, Names);
        Assert.Equal(player.Ammo[0].Count, saved.Count);
        for (var i = 0; i < saved.Count; i++)
        {
            Assert.Equal(player.Ammo[0][i].Id, saved[i].Id);
            Assert.Equal(player.Ammo[0][i].Count, saved[i].Count);
            Assert.Equal(player.Ammo[0][i].Emotions, saved[i].Emotions);
        }
    }

    [Fact]
    public void A_slime_sucked_up_in_play_is_written_with_its_moods()
    {
        var loaded = new RanchState { Player = Player() };
        var pack = new Vacpack();
        PlayerVacpack.Load(loaded.Player, Names, pack);
        Assert.True(pack.TryAdd("PINK_SLIME", new Dictionary<string, float> { ["HUNGER"] = 1f, ["AGITATION"] = 0.5f }));
        var ranch = RanchWriter.Merge(loaded, new LiveRanch
        {
            Money = loaded.Player.Money,
            WorldTime = loaded.WorldTime,
            Ammo = new Dictionary<int, IReadOnlyList<AmmoSlot>> { [0] = PlayerVacpack.Save(pack, Names) },
        });
        var slot = ranch.Player.Ammo[0][0];
        Assert.Equal(36, slot.Count);
        Assert.Equal(0.25f + (1f - 0.25f) / 36, slot.Emotions[0], 5);
        // Fear isn't modelled: it keeps the slot's.
        Assert.Equal(0.125f, slot.Emotions[2]);
        // The other vacpack mode passes through.
        Assert.Single(ranch.Player.Ammo[1]);
        Assert.Equal(35, loaded.Player.Ammo[0][0].Count);
    }
}
