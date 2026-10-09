using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Ranch;

/// <summary>
/// One upgrader script on a plot prefab's root: its class and, for each of its game object fields,
/// the objects it points at (path ids; a list field can point at several).
/// </summary>
public sealed record PlotUpgrader(string Class, IReadOnlyDictionary<string, IReadOnlyList<long>> Targets);

/// <summary>What a plot's saved upgrades switch: objects turned on or off, and which upgrades did something.</summary>
public sealed record UpgradeResult(IReadOnlyDictionary<long, bool> Switched, IReadOnlyList<string> Applied, IReadOnlyList<string> Ignored);

/// <summary>
/// Plot upgrades as the game shows them: each plot prefab's root carries upgrader scripts whose
/// fields point at child objects (high walls, the music box, the air nets and so on, read from the
/// install). Bought upgrades switch those objects on or off. Which field an upgrade switches, and to
/// what, lives only in the scripts' code; the table below states it, from static analysis of the 15
/// upgrader scripts (<c>WallUpgrader</c>, <c>MusicBoxUpgrader</c>, <c>SolarShieldUpgrader</c>,
/// <c>AirNetUpgrader</c>, <c>PlortCollectorUpgrader</c>, <c>FeederUpgrader</c>,
/// <c>StorageUpgrader</c>, <c>SprinklerUpgrader</c>, <c>ScareslimeUpgrader</c>,
/// <c>MineralSoilUpgrader</c>, <c>MiracleMixUpgrader</c>, <c>DeluxeGardenUpgrader</c>,
/// <c>DeluxeCoopUpgrader</c>, <c>VitamizerUpgrader</c>, <c>AshTroughUpgrader</c>) and of
/// <c>LandPlot</c>, which on load runs every upgrader of the root, in component order, over the saved
/// upgrades in saved order. An upgrade no upgrader of the plot knows (a corral's sprinkler, values
/// the install's enum doesn't name) changes nothing. See docs/behavior/plots.md, "Upgrades in the world".
/// </summary>
public static class PlotUpgrades
{
    private static (string Field, bool On) On(string field) => (field, true);
    private static (string Field, bool On) Off(string field) => (field, false);

    // Upgrader class -> (upgrade name in LandPlot.Upgrade, the fields it switches).
    private static readonly Dictionary<string, (string Upgrade, (string Field, bool On)[] Switches)[]> Rules = new(StringComparer.Ordinal)
    {
        ["WallUpgrader"] = [("WALLS", [Off("standardWalls"), On("upgradeWalls")])],
        ["MusicBoxUpgrader"] = [("MUSIC_BOX", [On("musicBox")])],
        ["SolarShieldUpgrader"] = [("SOLAR_SHIELD", [On("shields")])],
        ["AirNetUpgrader"] = [("AIR_NET", [On("airNets")])],
        ["PlortCollectorUpgrader"] = [("PLORT_COLLECTOR", [On("collector")])],
        ["FeederUpgrader"] = [("FEEDER", [On("feeder")])],
        // The silo: each storage upgrade adds its own addition and swaps the replacement piece.
        ["StorageUpgrader"] =
        [
            ("STORAGE2", [On("storageAdd2"), Off("storageOnly1"), On("storageOnly2"), Off("storageOnly3And4")]),
            ("STORAGE3", [On("storageAdd3"), Off("storageOnly1"), Off("storageOnly2"), On("storageOnly3And4")]),
            ("STORAGE4", [On("storageAdd4"), Off("storageOnly1"), Off("storageOnly2"), On("storageOnly3And4")]),
        ],
        ["SprinklerUpgrader"] = [("SPRINKLER", [On("sprinkler")])],
        ["ScareslimeUpgrader"] = [("SCARESLIME", [On("scareslime")])],
        ["MineralSoilUpgrader"] = [("SOIL", [On("soil")])],
        ["MiracleMixUpgrader"] = [("MIRACLE_MIX", [On("miracleMix"), Off("normSoil")])],
        // Also replants a crop as its deluxe kind when bought; on load nothing is planted yet when it runs.
        ["DeluxeGardenUpgrader"] = [("DELUXE_GARDEN", [On("deluxeStuff")])],
        // Also makes the coop's regions deluxe (behaviour, not modelled yet).
        ["DeluxeCoopUpgrader"] = [("DELUXE_COOP", [On("deluxeStuff")])],
        ["VitamizerUpgrader"] = [("VITAMIZER", [On("vitamizer")])],
        ["AshTroughUpgrader"] = [("ASH_TROUGH", [On("ashTrough")])],
    };

    /// <summary>The upgrader classes openranch knows.</summary>
    public static IEnumerable<string> Classes => Rules.Keys;

    /// <summary>The upgrade names an upgrader class reacts to.</summary>
    public static IEnumerable<string> UpgradesOf(string upgraderClass) =>
        Rules.TryGetValue(upgraderClass, out var rules) ? rules.Select(r => r.Upgrade) : [];

    /// <summary>The objects <paramref name="upgrade"/> switches on a plot with these upgraders, and to what.</summary>
    public static IEnumerable<(long Object, bool On)> Switches(IEnumerable<PlotUpgrader> upgraders, string upgrade)
    {
        foreach (var upgrader in upgraders)
            if (Rules.TryGetValue(upgrader.Class, out var rules))
                foreach (var rule in rules.Where(r => r.Upgrade == upgrade))
                    foreach (var (field, on) in rule.Switches)
                        foreach (var target in upgrader.Targets.GetValueOrDefault(field) ?? [])
                            yield return (target, on);
    }

    /// <summary>What <paramref name="upgrades"/> (saved <see cref="GameEnum.PlotUpgrade"/> values, in saved order) switch on a plot with these upgraders.</summary>
    public static UpgradeResult Apply(IReadOnlyList<PlotUpgrader> upgraders, IEnumerable<int> upgrades, IGameNames names)
    {
        var saved = upgrades.Select(u => names.Name(GameEnum.PlotUpgrade, u)).ToList();
        var switched = new Dictionary<long, bool>();
        var applied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var upgrader in upgraders)
        {
            foreach (var upgrade in saved)
            {
                foreach (var (target, on) in Switches([upgrader], upgrade))
                {
                    switched[target] = on;
                    applied.Add(upgrade);
                }
            }
        }
        return new UpgradeResult(switched, saved.Where(applied.Contains).ToList(), saved.Where(u => !applied.Contains(u)).ToList());
    }

    /// <summary>The upgrader scripts on a prefab's root game object, in component order, with their game object fields.</summary>
    public static List<PlotUpgrader> Read(GameScripts scripts, AssetRef root)
    {
        var assets = scripts.Assets;
        var list = new List<PlotUpgrader>();
        foreach (var c in assets.Read(root, GameObjectData.Read).Components)
        {
            if (scripts.Follow(root.File, c) is not { Data: { ScriptClass: { } cls, Data: { } data } } || !Rules.ContainsKey(cls))
                continue;
            var targets = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
            foreach (var (field, value) in data.Fields)
            {
                var pointers = value switch
                {
                    PPtr p => [p],
                    List<object?> l => l.OfType<PPtr>().ToList(),
                    _ => new List<PPtr>(),
                };
                var objects = pointers.Select(p => assets.Resolve(root.File, p)).OfType<AssetRef>()
                    .Where(r => r.ClassId == UnityClassId.GameObject).Select(r => r.PathId).ToList();
                if (objects.Count > 0)
                    targets[field] = objects;
            }
            list.Add(new PlotUpgrader(cls, targets));
        }
        return list;
    }
}
