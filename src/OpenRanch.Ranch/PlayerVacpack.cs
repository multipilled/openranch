using OpenRanch.Simulation;

namespace OpenRanch.Ranch;

/// <summary>
/// The vacpack as the save keeps it (docs/behavior/vacpack.md, "Saved"): the player block holds the
/// slots of each vacpack mode in slot order, an empty slot being the "none" item with count 0, each
/// with the moods of the slimes in it. The selected slot is not saved. Capacity and the liquid slot
/// come from the player's upgrades (static analysis of the original's player model and vacpack).
/// </summary>
public static class PlayerVacpack
{
    /// <summary>The vacpack mode of normal play (<see cref="GameEnum.AmmoMode"/>); the other is the Nimble Valley's.</summary>
    public const string DefaultMode = "DEFAULT";

    // Each capacity upgrade sets its own per-slot limit (the original's player model).
    private static readonly string[] CapacityUpgrades = ["AMMO_1", "AMMO_2", "AMMO_3", "AMMO_4"];
    private const string LiquidSlotUpgrade = "LIQUID_SLOT";

    /// <summary>Gives <paramref name="pack"/> the player's capacity, liquid slot and saved slots for normal play.</summary>
    public static void Load(Rancher player, IGameNames names, Vacpack pack)
    {
        // Upgrades apply in the order stored, so the last capacity upgrade sets the limit.
        foreach (var upgrade in player.Upgrades.Select(u => names.Name(GameEnum.PlayerUpgrade, u)))
        {
            if (Array.IndexOf(CapacityUpgrades, upgrade) is var level and >= 0)
                pack.SetCapacityLevel(level + 1);
            else if (upgrade == LiquidSlotUpgrade)
                pack.UnlockLiquidSlot();
        }
        var slots = player.Ammo.GetValueOrDefault(names.Value(GameEnum.AmmoMode, DefaultMode)) ?? [];
        for (var i = 0; i < Vacpack.TotalSlots; i++)
            pack.Restore(i, i < slots.Count ? Slot(slots[i], names) : null);
    }

    /// <summary>The vacpack's slot as the save holds it, or null for an empty one.</summary>
    public static VacSlot? Slot(AmmoSlot saved, IGameNames names)
    {
        var id = names.Name(GameEnum.ItemId, saved.Id);
        if (id == "NONE" || saved.Count <= 0)
            return null;
        return new VacSlot(id, saved.Count)
        {
            Moods = saved.Emotions.ToDictionary(e => names.Name(GameEnum.Emotion, e.Key), e => e.Value),
        };
    }

    /// <summary>The slots to save for <paramref name="pack"/>: every slot in order, empty ones as the "none" item with count 0.</summary>
    public static List<AmmoSlot> Save(Vacpack pack, IGameNames names)
    {
        var none = names.Value(GameEnum.ItemId, "NONE");
        return pack.Slots.Select(s => s is null
            ? new AmmoSlot(none, 0)
            : new AmmoSlot(names.Value(GameEnum.ItemId, s.Id), s.Count)
            {
                Emotions = s.Moods.ToDictionary(m => names.Value(GameEnum.Emotion, m.Key), m => m.Value),
            }).ToList();
    }
}
