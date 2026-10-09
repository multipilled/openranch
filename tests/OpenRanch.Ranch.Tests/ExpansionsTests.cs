using OpenRanch.Formats.Game;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

public class ExpansionsTests(ITestOutputHelper output)
{
    // Made-up enum numbers and prices: the real ones are read from the install.
    private sealed class FakeNames : IGameNames
    {
        private readonly Dictionary<(string, string), int> _values = new()
        {
            [(GameEnum.AccessDoorState, "LOCKED")] = 0,
            [(GameEnum.AccessDoorState, "CLOSED")] = 1,
            [(GameEnum.AccessDoorState, "OPEN")] = 2,
        };

        public string Name(string label, long value) =>
            _values.FirstOrDefault(kv => kv.Key.Item1 == label && kv.Value == value).Key.Item2 ?? value.ToString();

        public int Value(string label, string name) => _values[(label, name)];
    }

    private const int Locked = 0, Closed = 1, Open = 2;
    private const int UnlockLab = 3, UnlockDocks = 5, SlimeDoors = 1000;
    private static readonly FakeNames Names = new();

    private static readonly Expansions Doors = new(
    [
        new ExpansionDoor("lab", "LabAccessDoor", 10_000, 4009, [UnlockLab], ["labBack"], 1),
        new ExpansionDoor("labBack", "AccessDoor", 0, 0, [UnlockLab], [], 2),
        new ExpansionDoor("docks", "AccessDoor", 5000, 4012, [UnlockDocks], [], 3),
        new ExpansionDoor("gate1", "AccessDoor", 0, 0, [SlimeDoors], [], 4),
        new ExpansionDoor("gate2", "AccessDoor", 0, 0, [SlimeDoors], [], 5),
    ]);

    [Fact]
    public void Buying_opens_the_door_counts_progress_and_closes_linked_doors()
    {
        var ranch = new RanchState { Player = { Money = 12_000 } };
        Assert.Equal(ExpansionPurchase.Done, Doors.Buy(ranch, "lab", Names));
        Assert.Equal(2000, ranch.Player.Money);
        Assert.Equal(Open, ranch.AccessDoors["lab"]);
        Assert.Equal(Closed, ranch.AccessDoors["labBack"]);
        // Two unlocked doors list UNLOCK_LAB: the counter is the count, not a sum of additions.
        Assert.Equal(2, ranch.Player.Progress[UnlockLab]);
        Assert.Equal(ExpansionPurchase.AlreadyUnlocked, Doors.Buy(ranch, "lab", Names));
        Assert.Equal(ExpansionPurchase.NotEnoughMoney, Doors.Buy(ranch, "docks", Names));
        Assert.Equal(Locked, Expansions.StateOf(ranch, "docks", Names));
        Assert.Equal(ExpansionPurchase.NotForSale, Doors.Buy(ranch, "gate1", Names));
        Assert.Equal(ExpansionPurchase.NoSuchDoor, Doors.Buy(ranch, "zzz", Names));
        Assert.Equal(["lab", "docks"], Doors.ForSale.Select(d => d.Id));
    }

    [GameFact]
    public void The_installs_four_expansions_are_for_sale()
    {
        using var scripts = new GameScripts(GameFactAttribute.Install!);
        var names = InstalledNames.Get;
        var doors = Expansions.Read(scripts, scripts.Assets.File("level3")!);
        foreach (var d in doors.Doors.Values.OrderBy(d => d.Id))
            output.WriteLine($"{d.Id} {d.Class} cost {d.Cost} region {d.LockedRegionId} progress [{string.Join(",", d.Progress.Select(p => names.Name(GameEnum.Progress, p)))}]" +
                             (d.LinkedDoorIds.Count > 0 ? $" linked {string.Join(",", d.LinkedDoorIds)}" : ""));
        var sale = doors.ForSale.ToDictionary(d => names.Name(GameEnum.Progress, d.Progress.Single()), d => d.Cost);
        Assert.Equal(["UNLOCK_DOCKS", "UNLOCK_GROTTO", "UNLOCK_LAB", "UNLOCK_OVERGROWTH"], sale.Keys.Order());
        Assert.All(sale.Values, c => Assert.True(c > 0));
        Assert.True(NewRanch.InitialMoney(scripts) >= 0);
        output.WriteLine($"A new game starts with {NewRanch.InitialMoney(scripts)} newbucks");
    }

    // The counting rule reproduces what the original saved: for every progress counter a door lists,
    // the save's counter equals the number of its doors that aren't locked.
    [SaveFact]
    public void Recounting_the_saves_doors_gives_their_progress()
    {
        if (GameFactAttribute.Install is null)
            return;
        using var scripts = new GameScripts(GameFactAttribute.Install);
        var names = InstalledNames.Get;
        var doors = Expansions.Read(scripts, scripts.Assets.File("level3")!);
        foreach (var path in Saves.Supported)
        {
            var saved = SaveImport.FromSave(SaveImport.ReadSave(path));
            var ranch = SaveImport.FromSave(SaveImport.ReadSave(path));
            var types = doors.Doors.Values.SelectMany(d => d.Progress).ToHashSet();
            foreach (var t in types)
                ranch.Player.Progress.Remove(t);
            foreach (var door in doors.Doors.Values)
                doors.Recount(ranch, door, names);
            foreach (var t in types)
                Assert.True(saved.Player.ProgressOf(t) == ranch.Player.ProgressOf(t),
                    $"{Path.GetFileName(path)} {names.Name(GameEnum.Progress, t)}: saved {saved.Player.ProgressOf(t)}, counted {ranch.Player.ProgressOf(t)}");
        }
    }
}
