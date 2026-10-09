namespace OpenRanch.Ranch;

/// <summary>One stored slot of a plot: the item and how many.</summary>
public sealed record StoredSlot(string Item, int Count);

/// <summary>
/// What a loaded plot holds besides its building and upgrades, by the game's names: its crop and
/// when it dies, the auto-feeder and plort collector timers, the contents of its stores (a silo's
/// slots, the plorts a corral's collector gathered), the slots its silo buttons select and an
/// incinerator's ash. Read from <see cref="Plot"/>; see docs/behavior/plots.md, "What a save keeps per plot".
/// </summary>
public sealed record PlotContents(
    string SiteId,
    string Type,
    string Crop,
    double CropDeathTime,
    FeederState Feeder,
    double CollectorNextTime,
    IReadOnlyDictionary<string, IReadOnlyList<StoredSlot>> Storage,
    IReadOnlyList<int> SlotSelections,
    float Ash)
{
    /// <summary>Everything stored, summed by item over all stores and slots (empty slots left out).</summary>
    public IReadOnlyDictionary<string, int> StoredByItem =>
        Storage.Values.SelectMany(s => s).Where(s => s.Count > 0)
            .GroupBy(s => s.Item).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));

    public static PlotContents Of(Plot plot, IGameNames names) => new(
        plot.Id,
        names.Name(GameEnum.PlotType, plot.Type),
        names.Name(GameEnum.SpawnResource, plot.AttachedResource),
        plot.AttachedDeathTime,
        plot.Feeder,
        plot.CollectorNextTime,
        plot.Silo.ToDictionary(
            kv => names.Name(GameEnum.SiloStorage, kv.Key),
            kv => (IReadOnlyList<StoredSlot>)kv.Value.Select(s => new StoredSlot(names.Name(GameEnum.ItemId, s.Id), s.Count)).ToList()),
        plot.SiloSlotSelections.ToList(),
        plot.AshUnits);
}
