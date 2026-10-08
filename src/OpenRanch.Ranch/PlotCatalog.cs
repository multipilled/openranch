using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>An upgrade a plot's menu sells (<see cref="GameEnum.PlotUpgrade"/>) and its price.</summary>
public sealed record UpgradeOffer(int Upgrade, int Cost, string MenuItem);

/// <summary>A menu item that replaces the plot with another type (<see cref="GameEnum.PlotType"/>): building on an empty plot, or demolishing back to empty.</summary>
public sealed record ReplaceOffer(int Type, int Cost, string MenuItem);

/// <summary>What the menu of one plot type offers, in menu order.</summary>
public sealed record PlotMenu(
    int Type,
    string ScriptClass,
    IReadOnlyList<ReplaceOffer> Replacements,
    IReadOnlyList<UpgradeOffer> Upgrades,
    IReadOnlyDictionary<string, int> OtherCosts);

/// <summary>
/// The plot types and what each one's menu sells, with prices, read from the player's install.
/// Each plot prefab carries a <c>LandPlot</c> script naming its type and an activator whose menu
/// prefab holds the menu script (a <c>LandPlotUI</c>); the menu's items carry their prices, the
/// upgrade they sell, or the plot prefab they build. See docs/behavior/plots.md.
/// </summary>
public sealed class PlotCatalog
{
    private readonly Dictionary<int, PlotMenu> _menus;

    /// <summary>A catalog of the given menus (the install's come from <see cref="Read"/>).</summary>
    public PlotCatalog(IEnumerable<PlotMenu> menus) => _menus = menus.ToDictionary(m => m.Type);

    /// <summary>Menus by plot type, for every plot type that has a prefab with a menu.</summary>
    public IReadOnlyDictionary<int, PlotMenu> Menus => _menus;

    public PlotMenu? Menu(int type) => _menus.GetValueOrDefault(type);

    /// <summary>The price of building <paramref name="type"/> on an empty plot of type <paramref name="emptyType"/>, or null if its menu doesn't sell it.</summary>
    public int? BuildCost(int emptyType, int type) =>
        Menu(emptyType)?.Replacements.FirstOrDefault(r => r.Type == type)?.Cost;

    public UpgradeOffer? Upgrade(int type, int upgrade) =>
        Menu(type)?.Upgrades.FirstOrDefault(u => u.Upgrade == upgrade);

    public static PlotCatalog Read(GameScripts scripts)
    {
        var assets = scripts.Assets;
        var menus = new Dictionary<int, PlotMenu>();
        foreach (var (asset, plot) in scripts.OfClass("LandPlot"))
        {
            var type = Convert.ToInt32(plot.Data!["typeId"]);
            if (menus.ContainsKey(type) || assets.Resolve(asset.File, plot.GameObject) is not { } plotObject)
                continue;
            if (FindMenu(scripts, plotObject) is not { } menu)
                continue;
            menus[type] = ReadMenu(scripts, type, menu.Ref, menu.Data);
        }
        return new PlotCatalog(menus.Values);
    }

    // The plot's activator (any script with a "uiPrefab") somewhere in the plot's hierarchy, and the
    // LandPlotUI script on the menu prefab it opens.
    private static (AssetRef Ref, MonoBehaviourData Data)? FindMenu(GameScripts scripts, AssetRef plotObject)
    {
        foreach (var (asset, script) in Scripts(scripts, plotObject, recursive: true))
        {
            if (script.Data?["uiPrefab"] is not PPtr ui || scripts.Assets.Resolve(asset.File, ui) is not { } uiObject)
                continue;
            foreach (var candidate in Scripts(scripts, uiObject, recursive: false))
                if (candidate.Data.ScriptClass is { } cls && scripts.Types.Find(GameScripts.GameAssembly, "", cls)?.DerivesFrom("LandPlotUI") == true)
                    return candidate;
        }
        return null;
    }

    private static PlotMenu ReadMenu(GameScripts scripts, int type, AssetRef menuRef, MonoBehaviourData menu)
    {
        var replacements = new List<ReplaceOffer>();
        var upgrades = new List<UpgradeOffer>();
        var other = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, value) in menu.Data!.Fields)
        {
            if (value is not SerializedObject item || !item.Has("cost"))
                continue;
            var cost = Convert.ToInt32(item["cost"]);
            if (item["plotPrefab"] is PPtr prefab)
            {
                if (scripts.Assets.Resolve(menuRef.File, prefab) is { } target && PlotType(scripts, target) is { } targetType)
                    replacements.Add(new ReplaceOffer(targetType, cost, name));
            }
            else if (item.Has("upgrade"))
                upgrades.Add(new UpgradeOffer(Convert.ToInt32(item["upgrade"]), cost, name));
            else
                other[name] = cost;
        }
        return new PlotMenu(type, menu.ScriptClass!, replacements, upgrades, other);
    }

    private static int? PlotType(GameScripts scripts, AssetRef plotObject)
    {
        foreach (var (_, script) in Scripts(scripts, plotObject, recursive: true))
            if (script.ScriptClass == "LandPlot")
                return Convert.ToInt32(script.Data!["typeId"]);
        return null;
    }

    // The script components on a game object, and with recursive set, on all its descendants.
    private static IEnumerable<(AssetRef Ref, MonoBehaviourData Data)> Scripts(GameScripts scripts, AssetRef gameObject, bool recursive)
    {
        var assets = scripts.Assets;
        var pending = new Stack<AssetRef>();
        pending.Push(gameObject);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.ClassId != UnityClassId.GameObject)
                continue;
            var go = assets.Read(current, GameObjectData.Read);
            foreach (var component in go.Components)
            {
                if (assets.Resolve(current.File, component) is not { } c)
                    continue;
                if (c.ClassId == UnityClassId.MonoBehaviour)
                {
                    var data = scripts.Reader.Read(c);
                    if (data.Data is not null)
                        yield return (c, data);
                }
                else if (recursive && c.ClassId is UnityClassId.Transform or UnityClassId.RectTransform)
                {
                    var transform = assets.Read(c, TransformData.Read);
                    foreach (var child in transform.Children)
                        if (assets.Resolve(c.File, child) is { } childTransform)
                            if (assets.Resolve(childTransform.File, assets.Read(childTransform, TransformData.Read).GameObject) is { } childObject)
                                pending.Push(childObject);
                }
            }
        }
    }
}
