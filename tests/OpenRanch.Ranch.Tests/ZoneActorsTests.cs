using System.Numerics;

namespace OpenRanch.Ranch.Tests;

public class ZoneActorsTests
{
    // Made-up enum numbers: the real ones are read from the install.
    private sealed class FakeNames : IGameNames
    {
        private readonly Dictionary<(string, string), int> _values = new()
        {
            [(GameEnum.ItemId, "PINK_SLIME")] = 1,
            [(GameEnum.ItemId, "CARROT_VEGGIE")] = 2,
            [(GameEnum.ItemId, "HEN")] = 3,
            [(GameEnum.ItemId, "PINK_PLORT")] = 4,
            [(GameEnum.ResourceCycleState, "UNRIPE")] = 0,
            [(GameEnum.ResourceCycleState, "RIPE")] = 1,
            [(GameEnum.ResourceCycleState, "EDIBLE")] = 2,
        };

        public string Name(string label, long value) =>
            _values.FirstOrDefault(kv => kv.Key.Item1 == label && kv.Value == value).Key.Item2 ?? value.ToString();

        public int Value(string label, string name) => _values[(label, name)];
    }

    private const int Home = 0, Desert = 1;
    private static readonly ZoneRegions Zone = new(Home, [("cell", new Vector3(0, 0, 0), new Vector3(50, 50, 50))]);
    private static readonly CropSpawner Patch = new("patch", Matrix4x4.CreateTranslation(10, 0, 0), [new Vector3(10, 1, 0), new Vector3(11, 1, 0)]);

    private static Actor Item(int type, float x, float y, float z, int cycle = 0, int set = Home) =>
        new() { TypeId = type, Position = new Vec3(x, y, z), CycleState = cycle, RegionSetId = set };

    private static List<ZoneActor> Load(params Actor[] actors) =>
        ZoneActors.Of(new RanchState { Actors = actors.ToList() }, Zone, new FakeNames(), [Patch], id => id == "CARROT_VEGGIE");

    [Fact]
    public void Only_loose_things_inside_the_zone_and_its_region_set_come_back()
    {
        var loaded = Load(
            Item(1, 0, 0, 0),              // a slime: loaded with the corrals, not here
            Item(3, 5, 0, 5),              // a hen in the zone
            Item(4, 60, 0, 0),             // a plort outside every cell box
            Item(4, 5, 0, 5, set: Desert)); // inside the box, but in another world
        Assert.Equal(["HEN"], loaded.Select(a => a.Id));
        Assert.Null(loaded[0].Joint);
    }

    [Fact]
    public void Unripe_and_ripe_produce_hang_from_the_nearest_spawn_joint()
    {
        var loaded = Load(Item(2, 10.05f, 1, 0, cycle: 0), Item(2, 11, 1.02f, 0, cycle: 1), Item(2, 10, 1, 0, cycle: 2));
        Assert.Equal(new Vector3(10, 1, 0), loaded[0].Joint);
        Assert.True(loaded[0].Unripe);
        Assert.Equal(new Vector3(11, 1, 0), loaded[1].Joint);
        Assert.False(loaded[1].Unripe);
        // Edible produce lies loose, even on a joint.
        Assert.Null(loaded[2].Joint);
    }

    [Fact]
    public void Produce_with_no_joint_close_enough_falls_loose()
    {
        var loaded = Load(Item(2, 10.2f, 1, 0, cycle: 1), Item(2, 30, 1, 0, cycle: 0));
        Assert.All(loaded, a => Assert.Null(a.Joint));
        Assert.All(loaded, a => Assert.False(a.Unripe));
    }

    [Fact]
    public void Things_that_dont_grow_on_crops_never_attach()
    {
        var loaded = Load(Item(4, 10, 1, 0, cycle: 0));
        Assert.Null(loaded.Single().Joint);
    }
}
