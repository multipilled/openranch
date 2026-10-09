using OpenRanch.Formats.Game;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

// The Game2 ranch's crops, plot contents and loose actors, read with the install.
public class PlotContentsTests(ITestOutputHelper output)
{
    [Game2Fact]
    public void Game2_crops_contents_and_loose_actors_load()
    {
        if (GameFactAttribute.Install is null)
            return;
        using var scripts = new GameScripts(GameFactAttribute.Install);
        var scene = scripts.Assets.File("level3")!;
        var names = InstalledNames.Get;
        var (_, save) = Saves.Newest(Game2FactAttribute.GameName)!.Value;
        var ranch = SaveImport.FromSave(save);
        var layout = PlotLayout.Read(scripts, scene, "zoneRANCH");
        var placed = layout.Place(ranch, names);

        // Every crop a plot names has a prefab with spawn joints, and stands on its plot.
        var crops = PlotCrops.Read(scripts, scene);
        Assert.NotEmpty(crops.Prefabs);
        Assert.All(crops.Prefabs.Values, c => Assert.NotEmpty(c.JointsToRoot));
        var none = names.Value(GameEnum.SpawnResource, "NONE");
        var planted = placed.Where(p => p.Plot.AttachedResource != none).ToList();
        var onPlots = planted.Select(crops.On).ToList();
        Assert.All(onPlots, Assert.NotNull);
        Assert.All(planted.Zip(onPlots), x =>
            Assert.True(System.Numerics.Vector3.Distance(x.First.Site.PlotWorld.Translation, x.Second!.Position) < 0.01f));
        var (renderers, _) = PlotCrops.Build(onPlots!);
        Assert.NotEmpty(renderers);

        // Loose actors: everything but slimes inside the zone, and the produce that hangs from crops.
        var zone = ZoneRegions.Read(scripts, scene, "zoneRANCH", names.Value(GameEnum.RegionSet, "HOME"));
        Assert.NotEmpty(zone.Boxes);
        var prefabs = ItemPrefabs.Read(scripts);
        var spawners = onPlots.OfType<CropSpawner>().Concat(PlotCrops.InScene(scripts, scene, "zoneRANCH", new HashSet<long>())).ToList();
        var actors = ZoneActors.Of(ranch, zone, names, spawners, id => prefabs.Has(id) && prefabs.Get(id).RootScript("ResourceCycle") is not null);
        Assert.NotEmpty(actors);
        Assert.Contains(actors, a => a.Joint is not null);
        Assert.All(actors, a => Assert.True(prefabs.Has(a.Id), a.Id));

        // Contents keep the save's values.
        foreach (var p in placed)
        {
            var c = PlotContents.Of(p.Plot, names);
            Assert.Equal(p.Plot.Silo.Values.Sum(s => s.Sum(x => x.Count)), c.StoredByItem.Values.Sum());
        }

        output.WriteLine($"{crops.Prefabs.Count} crop prefabs; {planted.Count} plots planted: " +
                         string.Join(", ", planted.GroupBy(p => names.Name(GameEnum.SpawnResource, p.Plot.AttachedResource)).Select(g => $"{g.Count()} {g.Key}")));
        output.WriteLine($"{spawners.Count} crop spawners, {actors.Count} loose actors, {actors.Count(a => a.Joint is not null)} on joints ({actors.Count(a => a.Unripe)} unripe)");
        foreach (var p in placed.Where(p => p.Plot.Upgrades.Count > 0))
            output.WriteLine($"{p.Site.Id} {names.PlotType(p.Plot.Type)}: applied [{string.Join(",", p.Upgrades.Applied)}], ignored [{string.Join(",", p.Upgrades.Ignored)}], " +
                             $"{p.Renderers.Count()} meshes (prefab {p.Prefab.Renderers.Count})");
    }
}
