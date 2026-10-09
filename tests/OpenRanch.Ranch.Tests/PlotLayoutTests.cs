using OpenRanch.Formats.Game;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

// Plot sites and prefabs read from the install, and the Game2 ranch's plots standing on them.
public class PlotLayoutTests(ITestOutputHelper output)
{
    [Game2Fact]
    public void Game2_plots_stand_on_the_ranchs_sites()
    {
        if (GameFactAttribute.Install is null)
            return;
        using var scripts = new GameScripts(GameFactAttribute.Install);
        var layout = PlotLayout.Read(scripts, scripts.Assets.File("level3")!, "zoneRANCH");
        var names = InstalledNames.Get;

        // One prefab per plot type except the enum's "none": empty and the six buildings.
        Assert.Equal(7, layout.Prefabs.Count);
        Assert.All(layout.Prefabs.Values, p => Assert.NotEmpty(p.Renderers));
        Assert.NotEmpty(layout.Prefabs[names.Value(GameEnum.PlotType, "CORRAL")].Regions);

        var (_, save) = Saves.Newest(Game2FactAttribute.GameName)!.Value;
        var ranch = SaveImport.FromSave(save);
        Assert.Equal(26, layout.Sites.Count);
        Assert.Equal(layout.Sites.Count, layout.Sites.Select(s => s.Id).Distinct().Count());
        Assert.All(layout.Sites, s => Assert.NotNull(ranch.FindPlot(s.Id)));

        var placed = layout.Place(ranch);
        Assert.Equal(layout.Sites.Count, placed.Count);
        Assert.All(placed, p => Assert.Equal(ranch.FindPlot(p.Site.Id)!.Type, p.Prefab.Type));

        var (hidden, renderers, colliders) = layout.Build(placed);
        Assert.Equal(layout.Sites.Count, hidden.Count);
        Assert.Equal(placed.Sum(p => p.Prefab.Renderers.Count), renderers.Count);
        Assert.Equal(placed.Sum(p => p.Prefab.Colliders.Count), colliders.Count);

        foreach (var g in placed.GroupBy(p => p.Prefab.Type).OrderBy(g => g.Key))
            output.WriteLine($"{names.PlotType(g.Key)}: {g.Count()} ({g.First().Prefab.Name}, {g.First().Prefab.Renderers.Count} meshes, " +
                             $"{g.First().Prefab.Colliders.Count} colliders, {g.First().Prefab.Regions.Count} regions)");
        output.WriteLine($"Sites in other areas: {ranch.Plots.Count - layout.Sites.Count}");
    }

    [Game2Fact]
    public void Game2_barriers_open_with_the_saves_doors_and_expansions()
    {
        if (GameFactAttribute.Install is null)
            return;
        using var scripts = new GameScripts(GameFactAttribute.Install);
        var names = InstalledNames.Get;
        var barriers = ExpansionBarriers.Read(scripts, scripts.Assets.File("level3")!, "zoneRANCH", names);
        Assert.Equal(10, barriers.Count);
        // The Docks', the Grotto's, the Overgrowth's and the Lab's door (a LabAccessDoor).
        Assert.Equal(4, barriers.Count(b => b.DoorId is not null));
        Assert.All(barriers, b => Assert.True(b.DoorId is not null || b.Expansions.Count > 0, b.Path));

        var (_, save) = Saves.Newest(Game2FactAttribute.GameName)!.Value;
        var ranch = SaveImport.FromSave(save);
        foreach (var b in barriers)
            output.WriteLine($"{(ExpansionBarriers.IsOpen(b, ranch, names) ? "open  " : "closed")} {b.Path} door={b.DoorId} progress=[{string.Join(",", b.DoorProgress)}]");
        // A door the save lists as open lifts its barrier; one it lists as locked doesn't.
        Assert.All(barriers.Where(b => b.DoorId is not null && ranch.AccessDoors.ContainsKey(b.DoorId)), b =>
            Assert.Equal(ranch.AccessDoors[b.DoorId!] == names.Value(GameEnum.AccessDoorState, "OPEN"), ExpansionBarriers.IsOpen(b, ranch, names)));
    }
}
