namespace OpenRanch.Ranch;

/// <summary>What happened when the player tried to buy something at a plot.</summary>
public enum PlotPurchase
{
    Done,
    NoSuchPlot,
    /// <summary>This plot's menu doesn't sell it.</summary>
    NotOffered,
    AlreadyOwned,
    /// <summary>It is the next step of a series (silo storage) and the step before isn't owned yet.</summary>
    NeedsEarlierUpgrade,
    /// <summary>It only goes on sale after rewards from another rancher.</summary>
    NotUnlocked,
    NotEnoughMoney,
}

/// <summary>
/// Buying at a plot: building on an empty plot, demolishing, and upgrades. Prices and what each
/// plot type sells come from the install (<see cref="PlotCatalog"/>); the few rules that live only in
/// the game's code are named here. See docs/behavior/plots.md.
/// </summary>
public sealed class PlotRules
{
    /// <summary>
    /// Upgrades that go on sale only after rewards from another rancher: the upgrade, the progress
    /// counter, and the level needed (static analysis: the purchase conditions in GardenUI and CoopUI).
    /// </summary>
    public static readonly IReadOnlyList<(string Upgrade, string Progress, int Level)> RewardUpgrades =
    [
        ("MIRACLE_MIX", "OGDEN_REWARDS", 1),
        ("DELUXE_GARDEN", "OGDEN_REWARDS", 2),
        ("DELUXE_COOP", "MOCHI_REWARDS", 2),
    ];

    private readonly PlotCatalog _catalog;
    private readonly IGameNames _names;
    private readonly Dictionary<int, (int Progress, int Level)> _rewardLocks = new();

    public PlotRules(PlotCatalog catalog, IGameNames names)
    {
        _catalog = catalog;
        _names = names;
        foreach (var (upgrade, progress, level) in RewardUpgrades)
            _rewardLocks[names.Value(GameEnum.PlotUpgrade, upgrade)] = (names.Value(GameEnum.Progress, progress), level);
    }

    public PlotCatalog Catalog => _catalog;

    /// <summary>
    /// Replaces what stands on the plot with <paramref name="type"/>, as the plot's menu sells it:
    /// building on an empty plot, or demolishing a built one back to empty. What is built starts
    /// fresh: no upgrades, nothing stored, nothing planted.
    /// </summary>
    public PlotPurchase Replace(RanchState ranch, string plotId, int type)
    {
        if (ranch.FindPlot(plotId) is not { } plot)
            return PlotPurchase.NoSuchPlot;
        if (_catalog.Menu(plot.Type)?.Replacements.FirstOrDefault(r => r.Type == type) is not { } offer)
            return PlotPurchase.NotOffered;
        if (!Pay(ranch, offer.Cost))
            return PlotPurchase.NotEnoughMoney;
        ranch.Plots[ranch.Plots.IndexOf(plot)] = new Plot { Id = plot.Id, Type = type };
        return PlotPurchase.Done;
    }

    /// <summary>Whether <paramref name="upgrade"/> could be bought for the plot now, money aside.</summary>
    public PlotPurchase CanUpgrade(RanchState ranch, string plotId, int upgrade)
    {
        if (ranch.FindPlot(plotId) is not { } plot)
            return PlotPurchase.NoSuchPlot;
        if (_catalog.Upgrade(plot.Type, upgrade) is null)
            return PlotPurchase.NotOffered;
        if (plot.HasUpgrade(upgrade))
            return PlotPurchase.AlreadyOwned;
        if (EarlierStep(plot.Type, upgrade) is { } earlier && !plot.HasUpgrade(earlier))
            return PlotPurchase.NeedsEarlierUpgrade;
        if (_rewardLocks.TryGetValue(upgrade, out var needs) && ranch.Player.ProgressOf(needs.Progress) < needs.Level)
            return PlotPurchase.NotUnlocked;
        return PlotPurchase.Done;
    }

    public PlotPurchase Upgrade(RanchState ranch, string plotId, int upgrade)
    {
        var allowed = CanUpgrade(ranch, plotId, upgrade);
        if (allowed != PlotPurchase.Done)
            return allowed;
        var plot = ranch.FindPlot(plotId)!;
        if (!Pay(ranch, _catalog.Upgrade(plot.Type, upgrade)!.Cost))
            return PlotPurchase.NotEnoughMoney;
        plot.Upgrades.Add(upgrade);
        return PlotPurchase.Done;
    }

    // Upgrades named alike but for a final number (STORAGE2, STORAGE3, STORAGE4) are steps of one
    // series and are bought in order: each needs the one numbered one lower, if the menu sells it
    // (static analysis: SiloUI only offers each storage upgrade once the one before is owned).
    private int? EarlierStep(int type, int upgrade)
    {
        var name = _names.Name(GameEnum.PlotUpgrade, upgrade);
        var digits = name.Length - name.AsSpan().TrimEnd("0123456789").Length;
        if (digits == 0 || !int.TryParse(name[^digits..], out var step))
            return null;
        var earlierName = name[..^digits] + (step - 1);
        return _catalog.Menu(type)?.Upgrades
            .Select(u => (int?)u.Upgrade)
            .FirstOrDefault(u => _names.Name(GameEnum.PlotUpgrade, u!.Value) == earlierName);
    }

    private static bool Pay(RanchState ranch, int cost)
    {
        if (ranch.Player.Money < cost)
            return false;
        ranch.Player.Money -= cost;
        return true;
    }
}
