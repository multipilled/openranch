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
}
