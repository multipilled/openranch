using System.Linq;
using OpenRanch.Ranch;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// A plot's menu (docs/behavior/plots.md, "Menus and prices"): what the plot type's menu sells, in menu
/// order and at the install's prices: the buildings on an empty plot, demolishing on a built one, its
/// upgrades (those owned or not yet on sale shown but disabled, with the reason) and, on a garden with a
/// crop, clearing it. Each button's key is the menu item's name in the install (for example "corral",
/// "walls", "demolish").
/// </summary>
public partial class PlotMenu : MenuScreen
{
    private readonly RanchPlots _plots;

    public PlotMenu(RanchPlots plots) : base("PlotMenu") => _plots = plots;

    /// <summary>The site whose menu is open.</summary>
    public string? SiteId { get; private set; }

    public void Open(string siteId)
    {
        SiteId = siteId;
        Refresh();
    }

    private void Refresh()
    {
        var names = _plots.Names;
        var plot = _plots.Ranch.FindPlot(SiteId!)!;
        ShowScreen($"{names.PlotType(plot.Type)} ({SiteId}), {_plots.Purse.Money} newbucks");
        ClearItems();
        if (_plots.Catalog.Menu(plot.Type) is not { } menu)
            return;
        var empty = names.Value(GameEnum.PlotType, "EMPTY");
        foreach (var offer in menu.Replacements)
            AddItem(offer.MenuItem, plot.Type == empty ? $"Build {names.PlotType(offer.Type)}: {offer.Cost}" : $"Demolish: {offer.Cost}",
                _plots.Purse.Money >= offer.Cost, () => Done(_plots.Build(SiteId!, offer.Type), offer.MenuItem));
        foreach (var offer in menu.Upgrades)
        {
            var can = _plots.Rules.CanUpgrade(_plots.Ranch, SiteId!, offer.Upgrade);
            var note = can == PlotPurchase.Done ? "" : $" ({can})";
            AddItem(offer.MenuItem, $"{names.PlotUpgrade(offer.Upgrade)}: {offer.Cost}{note}", can == PlotPurchase.Done && _plots.Purse.Money >= offer.Cost,
                () => Done(_plots.Upgrade(SiteId!, offer.Upgrade), offer.MenuItem));
        }
        if (menu.OtherCosts.TryGetValue(PlotRules.ClearCropItem, out var clear) && plot.AttachedResource != names.Value(GameEnum.SpawnResource, "NONE"))
            AddItem(PlotRules.ClearCropItem, $"Clear {names.Name(GameEnum.SpawnResource, plot.AttachedResource)}: {clear}", _plots.Purse.Money >= clear,
                () => Done(_plots.ClearCrop(SiteId!), PlotRules.ClearCropItem));
    }

    private void Done(PlotPurchase result, string item)
    {
        Refresh();
        SetStatus(result == PlotPurchase.Done ? $"Bought {item}." : $"{item}: {result}");
        LastResult = result;
    }

    /// <summary>What the last button pressed did.</summary>
    public PlotPurchase? LastResult { get; private set; }

    /// <summary>The menu's buttons that are pressable now.</summary>
    public string[] Offered => Keys.ToArray();
}
