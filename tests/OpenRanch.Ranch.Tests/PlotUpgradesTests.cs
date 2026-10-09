using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

public class PlotUpgradesTests(ITestOutputHelper output)
{
    // Made-up enum numbers and object ids: the real ones are read from the install.
    private sealed class FakeNames : IGameNames
    {
        private readonly Dictionary<string, int> _upgrades = new()
        {
            ["WALLS"] = 1, ["MUSIC_BOX"] = 2, ["STORAGE2"] = 3, ["STORAGE3"] = 4, ["STORAGE4"] = 5, ["SPRINKLER"] = 7, ["AIR_NET"] = 11,
        };

        public string Name(string label, long value) =>
            label == GameEnum.PlotUpgrade ? _upgrades.FirstOrDefault(kv => kv.Value == value).Key ?? value.ToString() : value.ToString();

        public int Value(string label, string name) => _upgrades[name];
    }

    private static readonly FakeNames Names = new();

    private static PlotUpgrader Upgrader(string cls, params (string Field, long[] Objects)[] fields) =>
        new(cls, fields.ToDictionary(f => f.Field, f => (IReadOnlyList<long>)f.Objects));

    private static readonly PlotUpgrader[] Corral =
    [
        Upgrader("WallUpgrader", ("standardWalls", [10]), ("upgradeWalls", [11])),
        Upgrader("MusicBoxUpgrader", ("musicBox", [20])),
        Upgrader("AirNetUpgrader", ("airNets", [30, 31])),
    ];

    [Fact]
    public void Walls_swap_the_standard_walls_for_the_high_ones()
    {
        var result = PlotUpgrades.Apply(Corral, [1], Names);
        Assert.False(result.Switched[10]);
        Assert.True(result.Switched[11]);
        Assert.Equal(["WALLS"], result.Applied);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public void A_list_field_switches_every_object_it_points_at()
    {
        var result = PlotUpgrades.Apply(Corral, [11, 2], Names);
        Assert.True(result.Switched[30]);
        Assert.True(result.Switched[31]);
        Assert.True(result.Switched[20]);
        Assert.False(result.Switched.ContainsKey(10));
    }

    [Fact]
    public void Upgrades_no_upgrader_of_the_plot_knows_change_nothing()
    {
        // A sprinkler on a corral, and a number the game's enum doesn't name.
        var result = PlotUpgrades.Apply(Corral, [7, 23], Names);
        Assert.Empty(result.Switched);
        Assert.Equal(["SPRINKLER", "23"], result.Ignored);
    }

    [Fact]
    public void Silo_storage_upgrades_leave_the_replacement_piece_of_the_highest()
    {
        var silo = new[]
        {
            Upgrader("StorageUpgrader", ("storageAdd2", [2]), ("storageAdd3", [3]), ("storageAdd4", [4]),
                ("storageOnly1", [41]), ("storageOnly2", [42]), ("storageOnly3And4", [43])),
        };
        var two = PlotUpgrades.Apply(silo, [3], Names).Switched;
        Assert.Equal([true, false, true, false], new[] { two[2], two[41], two[42], two[43] });
        Assert.False(two.ContainsKey(3));

        var all = PlotUpgrades.Apply(silo, [3, 4, 5], Names).Switched;
        Assert.Equal([true, true, true, false, false, true], new[] { all[2], all[3], all[4], all[41], all[42], all[43] });
        output.WriteLine(string.Join(", ", all.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    // The install's plot prefabs: every upgrade the game sells has an upgrader on some plot, and every
    // field the rules use points at objects of that plot's prefab.
    [GameFact]
    public void The_installs_plot_prefabs_carry_an_upgrader_for_every_upgrade()
    {
        using var scripts = new OpenRanch.Formats.Game.GameScripts(GameFactAttribute.Install!);
        var layout = PlotLayout.Read(scripts, scripts.Assets.File("level3")!, "zoneRANCH");
        var names = InstalledNames.Get;
        var covered = new HashSet<string>();
        foreach (var prefab in layout.Prefabs.Values.OrderBy(p => p.Type))
        {
            foreach (var u in prefab.Upgraders)
            {
                Assert.All(u.Targets.Values.SelectMany(t => t), id => Assert.True(prefab.Tree!.Objects.ContainsKey(id), $"{prefab.Name}: {u.Class} -> {id}"));
                foreach (var upgrade in PlotUpgrades.UpgradesOf(u.Class))
                {
                    var switches = PlotUpgrades.Switches([u], upgrade).ToList();
                    Assert.NotEmpty(switches);
                    covered.Add(upgrade);
                    output.WriteLine($"{prefab.Name} {upgrade}: " + string.Join(", ",
                        switches.Select(s => $"{prefab.Tree!.Objects[s.Object].Path} {(s.On ? "on" : "off")}")));
                }
            }
        }
        var sold = names.Get(GameEnum.PlotUpgrade).Values.Keys.Where(n => n != "NONE").ToList();
        Assert.Equal(sold.Order(), covered.Order());
    }

    [Fact]
    public void Every_upgrader_class_reacts_to_at_least_one_upgrade()
    {
        Assert.Equal(15, PlotUpgrades.Classes.Count());
        Assert.All(PlotUpgrades.Classes, c => Assert.NotEmpty(PlotUpgrades.UpgradesOf(c)));
    }
}
