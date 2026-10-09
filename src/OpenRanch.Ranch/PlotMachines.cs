using OpenRanch.Formats.Unity;
using OpenRanch.Simulation;

namespace OpenRanch.Ranch;

/// <summary>
/// A store of a plot (a silo, the corral feeder's food, the plort collector's plorts): its kind
/// (<see cref="GameEnum.SiloStorage"/>, <c>SiloStorage.type</c>), its slots (<c>numSlots</c>) and how
/// many of one item a slot holds (<c>maxAmmo</c>). The slots live in <see cref="Plot.Silo"/> under the
/// kind. See docs/behavior/plot-machines.md.
/// </summary>
public sealed record StoreRules(int Kind, string KindName, int Slots, int MaxPerSlot)
{
    /// <summary>
    /// Whether the store's kind takes an item (<c>StorageTypeExtensions.GetContents</c>): non-slimes take
    /// anything but slimes and largos, plorts take plorts, food takes food and chicks, crafting takes
    /// plorts and crafting materials, elder takes the elder hen and rooster; quicksilver plorts go in none.
    /// </summary>
    public bool Takes(string itemId)
    {
        if (itemId == "QUICKSILVER_PLORT")
            return false;
        var kind = Items.KindOf(itemId);
        return KindName switch
        {
            "PLORT" => kind == ItemKind.Plort,
            "FOOD" => kind is ItemKind.Veggie or ItemKind.Fruit or ItemKind.Meat or ItemKind.Tofu or ItemKind.Chick,
            "CRAFTING" => kind is ItemKind.Plort or ItemKind.Craft,
            "ELDER" => itemId is "ELDER_HEN" or "ELDER_ROOSTER",
            "NON_SLIMES" => kind is not (ItemKind.Slime or ItemKind.Largo or ItemKind.Gordo or ItemKind.Liquid),
            _ => false,
        };
    }

    /// <summary>
    /// Puts one item in (<c>Ammo.MaybeAddToSlot</c>): into the first slot already holding it, unless that
    /// slot is full (then refused, even with an empty slot free); otherwise into the first empty slot.
    /// <paramref name="slot"/> puts it into that slot only (a silo's catcher, <c>MaybeAddToSpecificSlot</c>).
    /// </summary>
    public bool TryAdd(List<AmmoSlot> slots, int itemId, string itemName, int? slot = null)
    {
        if (!Takes(itemName))
            return false;
        while (slots.Count < Slots)
            slots.Add(new AmmoSlot(0, 0));
        var range = slot is { } only ? new[] { only } : Enumerable.Range(0, Slots).ToArray();
        foreach (var i in range)
            if (slots[i].Count > 0 && slots[i].Id == itemId)
            {
                if (slots[i].Count >= MaxPerSlot)
                    return false;
                slots[i] = slots[i] with { Count = slots[i].Count + 1 };
                return true;
            }
        foreach (var i in range)
            if (slots[i].Count == 0)
            {
                slots[i] = new AmmoSlot(itemId, 1);
                return true;
            }
        return false;
    }

    /// <summary>Takes one item out of a slot; returns its id, or null when the slot is empty.</summary>
    public static int? TakeOne(List<AmmoSlot> slots, int slot)
    {
        if (slot >= slots.Count || slots[slot].Count <= 0)
            return null;
        var id = slots[slot].Id;
        slots[slot] = slots[slot].Count == 1 ? new AmmoSlot(0, 0) : slots[slot] with { Count = slots[slot].Count - 1 };
        return id;
    }
}

/// <summary>
/// A corral's auto-feeder (<c>SlimeFeeder</c>): every cycle it queues <paramref name="ItemsPerFeeding"/>
/// drops; a cycle lasts <paramref name="HoursBySpeed"/> of its speed (<see cref="GameEnum.FeedSpeed"/>).
/// Queued drops come out one at a time from the first slot of its food store. See docs/behavior/plot-machines.md.
/// </summary>
public sealed record FeederRules(IReadOnlyDictionary<int, float> HoursBySpeed, int ItemsPerFeeding)
{
    /// <summary>Real seconds between drops (<c>SlimeFeeder.EJECT_RATE</c>).</summary>
    public const float EjectSeconds = 0.5f;

    public float Hours(int speed) => HoursBySpeed.TryGetValue(speed, out var h) ? h : HoursBySpeed.Values.FirstOrDefault();

    /// <summary>A feeder switched on for the first time feeds after one cycle (<c>SlimeFeeder.InitModel</c>).</summary>
    public FeederState Start(int speed, double now) =>
        new(Math.Max(now, WorldClock.NewGameStart) + Hours(speed) * 3600.0, 0, speed);

    /// <summary>Every cycle that has ended by <paramref name="now"/> queues its drops (<c>SlimeFeeder.Update</c>).</summary>
    public FeederState CatchUp(FeederState feeder, double now)
    {
        var next = feeder.NextTime;
        var pending = feeder.PendingCount;
        var step = Hours(feeder.Speed) * 3600.0;
        while (step > 0 && now >= next)
        {
            pending += ItemsPerFeeding;
            next += step;
        }
        return feeder with { NextTime = next, PendingCount = pending };
    }
}

/// <summary>
/// A corral's plort collector (<c>PlortCollector</c>): every <paramref name="PeriodHours"/> game hours it
/// sweeps the plorts lying in its area into its plort store. See docs/behavior/plot-machines.md.
/// </summary>
public sealed record CollectorRules(float PeriodHours)
{
    /// <summary>A collector switched on for the first time sweeps after one period (<c>PlortCollector.InitModel</c>).</summary>
    public double Start(double now) => Math.Max(now, WorldClock.NewGameStart) + PeriodHours * 3600.0;

    /// <summary>Whether it sweeps now, and when it sweeps next (<c>collectorNextTime += period</c>, once per sweep).</summary>
    public (bool Sweep, double Next) Tick(double next, double now) => now >= next ? (true, next + PeriodHours * 3600.0) : (false, next);
}

/// <summary>
/// An incinerator (<c>Incinerate</c>) and its ash trough (<c>FillableAshSource</c>): it burns anything but
/// <see cref="Fireproof"/>; with the trough on, each food burnt adds <paramref name="AshPerItem"/> ash, up
/// to <paramref name="MaxAsh"/>. A new incinerator's trough starts full. See docs/behavior/plot-machines.md.
/// </summary>
public sealed record AshRules(float AshPerItem, float MaxAsh)
{
    /// <summary>What the fire doesn't burn (<c>Incinerate.CanBeIncinerated</c>).</summary>
    public static readonly IReadOnlySet<string> Fireproof = new HashSet<string>(StringComparer.Ordinal) { "FIRE_PLORT", "FIRE_SLIME", "CHARCOAL_BRICK_TOY" };

    public static bool Burns(string itemId) => !Fireproof.Contains(itemId);

    /// <summary>The trough after burning <paramref name="itemId"/> (<c>Incinerate.ProcessIncinerateResults</c>): food adds ash while the trough is on.</summary>
    public float Burn(float ash, string itemId, bool trough) =>
        trough && Items.KindOf(itemId) is ItemKind.Veggie or ItemKind.Fruit or ItemKind.Meat or ItemKind.Tofu ? Math.Min(ash + AshPerItem, MaxAsh) : ash;
}

/// <summary>
/// A silo catcher of a plot (<c>SiloCatcher</c>): the trigger it catches in, the store and slot it fills
/// (<c>slotIdx</c>) and its <c>type</c> (<c>SiloCatcher.Type</c> name): every type but output-only takes
/// items in; the silo types give them out to a vacpack pulling at them.
/// </summary>
public sealed record CatcherPart(PrefabTrigger Trigger, StoreRules Store, int Slot, string Type = "SILO_DEFAULT")
{
    /// <summary>
    /// The silo button that picks this catcher's slot (<c>SiloStorageActivator.activatorIdx</c>), or -1, and
    /// the slots it cycles through (its <c>siloSlotUIs</c>' <c>slotIdx</c>): the catcher fills the slot the
    /// plot's saved selection for that button names (<see cref="Plot.SiloSlotSelections"/>; static analysis
    /// of <c>SiloStorageActivator.OnActiveSlotChanged</c>).
    /// </summary>
    public int Button { get; init; } = -1;
    public IReadOnlyList<int> ButtonSlots { get; init; } = [];

    /// <summary>The slot this catcher fills and gives out of now.</summary>
    public int SlotFor(IReadOnlyList<int> selections) =>
        Button >= 0 && ButtonSlots.Count > 0 ? ButtonSlots[(Button < selections.Count ? selections[Button] : 0) % ButtonSlots.Count] : Slot;

    public bool Input => Type != "SILO_OUTPUT_ONLY";
    public bool Output => Type is "SILO_DEFAULT" or "SILO_OUTPUT_ONLY";
    /// <summary>Real seconds between items given out (<c>SiloCatcher</c>'s 0.25, before its speed-up).</summary>
    public const float OutputSeconds = 0.25f;
    /// <summary>The widest angle from the catcher's front a vacpack can pull from (<c>SiloCatcher</c>'s 45 degrees).</summary>
    public const float OutputAngleDegrees = 45f;
}

/// <summary>
/// The machines of each plot type, read from the plot prefabs (<see cref="PlotLayout.Prefabs"/>): the
/// stores (<c>SiloStorage</c>), the silo catchers, the corral's feeder and plort collector and the
/// incinerator's ash trough. Every number is the prefab's own, except the feeder's hours per speed,
/// which the game keeps in code (<c>SlimeFeeder.hoursByFeedSpeed</c>: slow 9, normal 6, fast 3; Unity
/// doesn't save dictionaries). See docs/behavior/plot-machines.md.
/// </summary>
public sealed class PlotMachineData
{
    /// <summary>The feeder's hours per cycle by speed name, from <c>SlimeFeeder</c>'s code.</summary>
    public static readonly IReadOnlyDictionary<string, float> FeederHoursByName = new Dictionary<string, float> { ["Slow"] = 9, ["Normal"] = 6, ["Fast"] = 3 };

    public Dictionary<int, List<StoreRules>> Stores { get; } = [];
    public Dictionary<int, List<CatcherPart>> Catchers { get; } = [];
    public Dictionary<int, FeederRules> Feeders { get; } = [];
    public Dictionary<int, (CollectorRules Rules, PrefabTrigger? Area)> Collectors { get; } = [];
    public Dictionary<int, AshRules> Ash { get; } = [];

    /// <param name="scripts">Reads the silo buttons' slot lists (their UI objects aren't part of the prefab tree's walk).</param>
    public static PlotMachineData Read(PlotLayout layout, IGameNames names, OpenRanch.Formats.Game.GameScripts? scripts = null)
    {
        var data = new PlotMachineData();
        var hours = FeederHoursByName.ToDictionary(kv => names.Value(GameEnum.FeedSpeed, kv.Key), kv => kv.Value);
        foreach (var (type, prefab) in layout.Prefabs)
        {
            if (prefab.Tree is not { } tree)
                continue;
            var stores = new List<StoreRules>();
            var storeByObject = new Dictionary<long, StoreRules>();
            foreach (var s in tree.Scripts.Where(s => s.Class == "SiloStorage" && s.Enabled))
            {
                var kind = Convert.ToInt32(s.Data["type"]);
                var store = new StoreRules(kind, names.Name(GameEnum.SiloStorage, kind), Convert.ToInt32(s.Data["numSlots"]), Convert.ToInt32(s.Data["maxAmmo"]));
                stores.Add(store);
                storeByObject.TryAdd(s.Object, store);
            }
            if (stores.Count > 0)
                data.Stores[type] = stores;
            // A catcher fills the store of the nearest object above it carrying one (SiloCatcher finds its parent's SiloStorage).
            StoreRules? StoreAbove(long obj) => tree.ChainOf(obj).Reverse().Select(o => storeByObject.GetValueOrDefault(o)).FirstOrDefault(s => s is not null);
            var catchers = tree.Scripts.Where(s => s.Class == "SiloCatcher" && s.Enabled)
                .SelectMany(s => tree.Triggers.Where(t => t.Region.Class == "SiloCatcher" && t.Object == s.Object)
                    .Select(t => (t, store: StoreAbove(s.Object), slot: Convert.ToInt32(s.Data["slotIdx"]), kind: names.Name("SiloCatcher.Type", Convert.ToInt64(s.Data["type"])))))
                .Where(c => c.store is not null).Select(c =>
                {
                    var catcherScript = tree.Scripts.First(s => s.Class == "SiloCatcher" && s.Object == c.t.Object);
                    var button = tree.Scripts.FirstOrDefault(b => b.Class == "SiloStorageActivator" && b.Data["siloCatcher"] is PPtr p && p.PathId == catcherScript.Component);
                    var slots = button is null ? new List<int>() : button.Data.List("siloSlotUIs").OfType<PPtr>()
                        .Select(p => tree.Scripts.FirstOrDefault(u => u.Component == p.PathId)?.Data["slotIdx"] ?? scripts?.Follow(button.File, p)?.Data.Data?["slotIdx"])
                        .OfType<object>().Select(Convert.ToInt32).ToList();
                    return new CatcherPart(c.t, c.store!, c.slot, c.kind)
                    {
                        Button = button is null ? -1 : Convert.ToInt32(button.Data["activatorIdx"]),
                        ButtonSlots = slots,
                    };
                }).ToList();
            if (catchers.Count > 0)
                data.Catchers[type] = catchers;
            if (tree.Script("SlimeFeeder") is { } feeder)
                data.Feeders[type] = new FeederRules(hours, Convert.ToInt32(feeder.Data["itemsPerFeeding"]));
            if (tree.Script("PlortCollector") is { } collector)
                data.Collectors[type] = (new CollectorRules(Convert.ToSingle(collector.Data["collectPeriod"])),
                    tree.Triggers.FirstOrDefault(t => t.Region.Class == "TrackCollisions"));
            if (tree.Script("Incinerate") is { } fire && tree.Script("FillableAshSource") is { } trough)
                data.Ash[type] = new AshRules(Convert.ToSingle(fire.Data["ashPerIncineration"]), Convert.ToSingle(trough.Data["maxUnits"]));
        }
        return data;
    }
}
