namespace OpenRanch.Simulation;

/// <summary>One vacpack slot: the item it holds and how many.</summary>
public sealed record VacSlot(string Id, int Count);

/// <summary>
/// The player's vacpack inventory: four slots for solid items from the start, and a fifth, liquid-only
/// slot that an upgrade opens. Each slot holds one kind of item up to the current per-slot limit.
/// The rules and numbers are in docs/behavior/vacpack.md.
/// </summary>
public sealed class Vacpack
{
    /// <summary>Slots the vacpack has in all, counting the liquid slot.</summary>
    public const int TotalSlots = 5;
    /// <summary>Slots usable before the liquid slot upgrade.</summary>
    public const int StartingSlots = 4;
    /// <summary>The index of the liquid-only slot.</summary>
    public const int LiquidSlot = 4;
    /// <summary>Per-slot limit with no upgrades, then after each of the four capacity upgrades.</summary>
    public static readonly IReadOnlyList<int> SlotLimits = [20, 30, 40, 50, 100];
    /// <summary>How much one suck of water fills; everything else is one per item.</summary>
    public const int WaterPerVac = 5;

    private readonly VacSlot?[] _slots = new VacSlot?[TotalSlots];
    private readonly Func<string, bool> _canHold;
    private int _capacityLevel;

    /// <param name="canHold">Which items can be sucked up at all (the game keeps a list of vaccable items). Defaults to anything.</param>
    public Vacpack(Func<string, bool>? canHold = null) => _canHold = canHold ?? (_ => true);

    public int UsableSlots { get; private set; } = StartingSlots;
    public int SelectedSlot { get; private set; }
    public int MaxPerSlot => SlotLimits[_capacityLevel];
    public int CapacityLevel => _capacityLevel;

    public VacSlot? this[int slot] => _slots[slot];
    public IReadOnlyList<VacSlot?> Slots => _slots;

    /// <summary>Applies the next capacity upgrade, if any are left.</summary>
    public void UpgradeCapacity() => _capacityLevel = Math.Min(_capacityLevel + 1, SlotLimits.Count - 1);

    /// <summary>Opens the fifth, liquid-only slot.</summary>
    public void UnlockLiquidSlot() => UsableSlots = TotalSlots;

    /// <summary>Whether <paramref name="id"/> is allowed in <paramref name="slot"/>: liquids only in the liquid slot, and nothing liquid elsewhere.</summary>
    public static bool SlotAccepts(int slot, string id) => (slot == LiquidSlot) == Items.IsLiquid(id);

    public int Count(string id) => _slots.Take(UsableSlots).FirstOrDefault(s => s?.Id == id)?.Count ?? 0;

    /// <summary>Whether sucking up one <paramref name="id"/> now would fit.</summary>
    public bool CanAdd(string id)
    {
        if (!_canHold(id))
            return false;
        for (var i = 0; i < UsableSlots; i++)
        {
            if (_slots[i] is { } s)
            {
                if (s.Id == id)
                    return s.Count < MaxPerSlot;
            }
        }
        for (var i = 0; i < UsableSlots; i++)
            if (_slots[i] is null && SlotAccepts(i, id))
                return true;
        return false;
    }

    /// <summary>
    /// Sucks up one <paramref name="id"/>. It joins the slot already holding that item, or else the first
    /// empty slot that accepts it. Returns false (and nothing changes) when there's no room.
    /// </summary>
    public bool TryAdd(string id)
    {
        if (!_canHold(id))
            return false;
        var amount = id == "WATER_LIQUID" ? WaterPerVac : 1;
        for (var i = 0; i < UsableSlots; i++)
        {
            if (_slots[i] is { } s && s.Id == id)
            {
                if (s.Count >= MaxPerSlot)
                    return false;
                _slots[i] = s with { Count = Math.Min(MaxPerSlot, s.Count + amount) };
                return true;
            }
        }
        for (var i = 0; i < UsableSlots; i++)
        {
            if (_slots[i] is null && SlotAccepts(i, id))
            {
                _slots[i] = new VacSlot(id, Math.Min(MaxPerSlot, amount));
                return true;
            }
        }
        return false;
    }

    public void Select(int slot)
    {
        if (slot >= 0 && slot < UsableSlots)
            SelectedSlot = slot;
    }

    public void SelectNext() => SelectedSlot = (SelectedSlot + 1) % UsableSlots;
    public void SelectPrevious() => SelectedSlot = (SelectedSlot + UsableSlots - 1) % UsableSlots;

    /// <summary>Shoots one item out of the selected slot. Returns its id, or null when the slot is empty.</summary>
    public string? TakeSelected() => Take(SelectedSlot);

    public string? Take(int slot)
    {
        if (_slots[slot] is not { } s)
            return null;
        _slots[slot] = s.Count > 1 ? s with { Count = s.Count - 1 } : null;
        return s.Id;
    }

    public void Clear(int slot) => _slots[slot] = null;
}
