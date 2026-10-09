using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Simulation.Tests;

// The install's gordos: the LookupDirector's gordo list and the gordos placed in the world scene.
public class InstalledGordoTests
{
    private static readonly Lazy<(GameScripts Scripts, ItemPrefabs Prefabs, GordoData Gordos)> Data = new(() =>
    {
        var scripts = new GameScripts(GameFactAttribute.Install!);
        return (scripts, ItemPrefabs.Read(scripts), GordoData.Read(scripts));
    });

    [GameFact]
    public void Every_gordo_kind_has_a_target_rewards_and_its_own_slime()
    {
        var gordos = Data.Value.Gordos.Gordos;
        Assert.Equal(14, gordos.Count);
        foreach (var g in gordos)
        {
            Assert.True(Data.Value.Prefabs.Has(g.Id), g.Id);
            Assert.Equal(g.Id.Replace("_GORDO", "_SLIME"), g.Slime);
            Assert.Equal(g.Slime, g.FillSlime);
            Assert.Equal(1.5f, g.GrowthFactor);
            Assert.InRange(g.Rewards.Count, 1, 12);
            var expected = g.Id switch { "PINK_GORDO" => 30, "GOLD_GORDO" => 15, _ => 50 };
            Assert.Equal(expected, g.TargetCount);
        }
    }

    [GameFact]
    public void Placed_gordos_keep_their_own_rewards()
    {
        var scripts = Data.Value.Scripts;
        var scene = scripts.Assets.File("level3")!;
        var placed = new[] { "zoneREEF", "zoneQUARRY", "zoneMOSS", "zoneRUINS", "zoneDESERT" }
            .SelectMany(root => GordoData.Placed(scripts, scene, root)).ToList();
        Assert.Equal(27, placed.Count);
        var ringIsland = Assert.Single(placed, p => p.Path.EndsWith("cellReef_RingIsland/Sector/Slimes/gordoPink", StringComparison.Ordinal));
        Assert.Equal(["KEY", "CRATE_REEF_01", "CRATE_REEF_01"], ringIsland.Gordo.Rewards);
        var mirror = Assert.Single(placed, p => p.Path.EndsWith("cellQuarry_MirrorIsland/Sector/Slimes/gordoRock", StringComparison.Ordinal));
        Assert.Equal(["CRATE_QUARRY_01", "CRATE_QUARRY_01", "CRATE_QUARRY_01", "ROCK_SLIME", "ROCK_SLIME", "ROCK_SLIME"], mirror.Gordo.Rewards);
        // Game mode 3's override swaps the desert crate for a third quarry crate on the Ash Isle crystal gordo.
        var crystal = Assert.Single(placed, p => p.Gordo.Id == "CRYSTAL_GORDO");
        Assert.Equal(["CRATE_QUARRY_01", "CRATE_QUARRY_01", "CRATE_QUARRY_01", "KEY"], crystal.Gordo.RewardOverrides[3]);
    }

    [GameFact]
    public void Gordo_prefabs_have_a_body_a_mouth_and_full_size_models()
    {
        var pink = Data.Value.Prefabs.Get("PINK_GORDO");
        var body = Assert.Single(pink.Colliders, c => c.Collider is { Shape: ColliderShape.Mesh, IsTrigger: false });
        Assert.True(body.Collider.Convex);
        Assert.NotNull(body.Mesh);
        var mouth = Assert.Single(pink.Colliders, c => c.Collider.IsTrigger);
        Assert.Contains(pink.Scripts, s => s.Class == "GordoEatTrigger" && s.Path == mouth.Path);
        // The pink gordo's model hangs at a quarter scale, but its bones put it at the root's scale of 4.
        var model = Assert.Single(pink.Meshes, m => m.Skinned && m.Path.EndsWith("slime_gordo", StringComparison.Ordinal));
        Assert.Equal(4f, model.ToRoot.M11, 3);
        var boom = Data.Value.Prefabs.Get("BOOM_GORDO").RootScript("BoomGordoEat")!;
        Assert.Equal(1800f, boom["explodePower"]);
        Assert.Equal(21f, boom["explodeRadius"]);
        Assert.Equal(30f, boom["minPlayerDamage"]);
        Assert.Equal(90f, boom["maxPlayerDamage"]);
    }

    [GameFact]
    public void Keys_float_where_they_appear()
    {
        var key = Data.Value.Prefabs.Get("KEY");
        Assert.True(key.Body is { IsKinematic: true, UseGravity: false });
        Assert.True(Data.Value.Prefabs.Get("CRATE_REEF_01").Body is { IsKinematic: false, UseGravity: true });
    }
}
