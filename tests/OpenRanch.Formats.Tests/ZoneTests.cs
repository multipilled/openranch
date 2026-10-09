using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

// The other zones of the world scene (docs/behavior/zones.md). Expected values were read independently
// with the generic script reader (MonoBehaviourReader over the game's managed metadata).
public class ZoneTests
{
    [GameFact]
    public void Cells_carry_their_ambience_zone_and_world_space_box()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var scene = assets.File("level3")!;

        var quarry = ZoneExtractor.Extract(assets, scene, "zoneQUARRY");
        Assert.Equal(15, quarry.Cells.Count);
        Assert.Equal(14, quarry.Cells.Count(c => c.AmbianceZone == 1)); // QUARRY
        Assert.Equal(1000, Assert.Single(quarry.Cells, c => c.Path.EndsWith("cellQuarry_CrystalVolcano")).AmbianceZone); // AUX1

        // Each teleporter stands inside the box of the cell it belongs to.
        foreach (var t in quarry.Teleports)
        {
            var cell = quarry.Cells.Single(c => t.Path.StartsWith(c.Path + "/"));
            Assert.True(cell.Contains(t.World.Translation), $"{t.Path} is outside {cell.Path}");
        }

        var desert = ZoneExtractor.Extract(assets, scene, "zoneDESERT");
        Assert.Equal(25, desert.Cells.Count);
        Assert.All(desert.Cells, c => Assert.Equal(3, c.AmbianceZone)); // DESERT
        Assert.Single(desert.KillVolumes); // the sand sea

        var slimulations = ZoneExtractor.Extract(assets, scene, "zoneSLIMULATIONS");
        Assert.Equal(21, slimulations.Cells.Count);
        Assert.All(slimulations.Cells, c => Assert.Equal(9, c.AmbianceZone)); // SLIMULATIONS, from GlitchCellDirector
    }

    [GameFact]
    public void Teleport_destinations_and_debug_starts_are_listed()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var scene = assets.File("level3")!;

        var wilds = ZoneExtractor.Extract(assets, scene, "zoneWILDS");
        var start = Assert.Single(wilds.Teleports, t => t.IsDebugStart);
        Assert.Equal("Wilds Start", start.Name);
        Assert.Equal(4, wilds.Teleports.Count(t => !t.IsDebugStart && t.Name == "Wilds"));

        var ruins = ZoneExtractor.Extract(assets, scene, "zoneRUINS");
        var exit = Assert.Single(ruins.Teleports, t => t.Name == "RuinsTempleExitTeleporter");
        Assert.False(exit.Reorient);
    }

    [GameFact]
    public void Cells_split_into_their_region_roots_with_proxies()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var scene = assets.File("level3")!;

        var parts = ZoneExtractor.ExtractParts(assets, scene, "zoneREEF");
        Assert.Equal(1, parts.Zone); // ZoneDirector.Zone REEF
        Assert.Equal(16, parts.Regions.Count); // one per cell, each cell's "Sector"
        Assert.All(parts.Regions, r => Assert.NotNull(r.ProxyMesh));
        Assert.All(parts.Regions, r => Assert.Contains(r.Content.Renderers, x => x.Path.StartsWith(r.Cell.Path + "/Sector")));
        // The parts add up to the whole zone.
        var whole = ZoneExtractor.Extract(assets, scene, "zoneREEF");
        Assert.Equal(whole.Renderers.Count, parts.Always.Renderers.Count + parts.Regions.Sum(r => r.Content.Renderers.Count));
        Assert.Equal(whole.Colliders.Count, parts.Always.Colliders.Count + parts.Regions.Sum(r => r.Content.Colliders.Count));
        Assert.True(parts.Regions.Sum(r => r.Content.Renderers.Count) > parts.Always.Renderers.Count);
    }

    [GameFact]
    public void Zones_fall_into_region_sets_and_the_player_loads_200_m_around()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        var scene = assets.File("level3")!;
        var sets = RegionSets.Read(install);
        int SetOf(string root) => sets.SetOf(ZoneExtractor.ExtractParts(assets, scene, root).Zone);
        Assert.Equal("HOME", sets.NameOf(SetOf("zoneWILDS")));
        Assert.Equal("DESERT", sets.NameOf(SetOf("zoneDESERT")));
        Assert.Equal("VALLEY", sets.NameOf(SetOf("zoneMOCHI")));

        // The RegionLoader on the player rig (read with the generic script reader: 50/200/50, 200/200/200, 0.1).
        var loader = RegionLoaderSettings.Read(assets, scene);
        Assert.Equal(new System.Numerics.Vector3(200, 200, 200), loader.LoadSize);
        Assert.Equal(new System.Numerics.Vector3(50, 200, 50), loader.WakeSize);
        Assert.Equal(0.1f, loader.UnloadBuffer, 5);
    }

    [Fact]
    public void Regions_load_inside_the_box_and_unload_outside_the_larger_one()
    {
        var cell = new CellArea("c", 0, new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(10, 10, 10));
        var loader = new RegionLoader(new RegionLoaderSettings(default, new System.Numerics.Vector3(200, 200, 200), 0.1f), [cell]);
        Assert.Empty(loader.Update(new(115, 0, 0)).Load); // 5 m beyond the 100 m half size
        Assert.Equal([0], loader.Update(new(110, 0, 0)).Load); // touching
        Assert.Empty(loader.Update(new(110.5f, 0, 0)).Unload); // moved less than 1 m: no check
        Assert.Empty(loader.Update(new(119, 0, 0)).Unload); // inside the 110 m unload box
        Assert.Equal([0], loader.Update(new(121, 0, 0)).Unload);
    }

    [GameFact]
    public void Teleporters_link_by_name_and_open_by_the_original_rules()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        var scene = assets.File("level3")!;
        var sets = RegionSets.Read(install);
        var zones = new[] { "zoneRANCH", "zoneSEA", "zoneQUARRY", "zoneDESERT", "zoneRUINS" }
            .Select(z => ZoneExtractor.ExtractParts(assets, scene, z)).ToList();
        var network = new TeleportNetwork(
            zones.SelectMany(z => z.Always.Teleports.Select(t => (t, sets.SetOf(z.Zone)))),
            zones.SelectMany(z => z.Merged().TeleportSources));
        var sources = zones.SelectMany(z => z.Merged().TeleportSources).ToList();
        TeleportSourceItem Source(string destination) => sources.First(s => s.Destination == destination && !s.InEchoNoteGordo);

        // The Slime Sea's two islands link both ways; arriving holds the far side until the player steps off.
        var toMustache = Source("SeaMustacheIsland");
        var arrival = network.Enter(toMustache, new Random(1))!;
        Assert.Equal("SeaMustacheIsland", arrival.Point.Name);
        Assert.False(network.IsLinkActive(arrival.Source!));
        network.Exit(arrival.Source!);
        Assert.True(network.IsLinkActive(arrival.Source!));

        // The Ranch's Grotto teleporter leads to the Quarry's cave hub, whose own teleporter a gordo blocks.
        Assert.Null(network.Enter(Source("QuarryCaveHub"), new Random(1)));
        // The Lab's teleporter needs UNLOCK_VIKTOR_MISSIONS (300) and the Ruins' temple exit waits to be switched on.
        Assert.False(network.IsLinkActive(Source("ViktorLabReceiver")));
        network.Progress = new Dictionary<int, int> { [300] = 1 };
        Assert.True(network.IsLinkActive(Source("ViktorLabReceiver")));
        Assert.False(network.IsLinkActive(Source("DesertTempleReceiver")));
        // The Desert's receiver is in the DESERT set.
        Assert.Equal("DESERT", sets.NameOf(Assert.Single(network.Destinations("DesertTempleReceiver")).RegionSet));
    }
}
