using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Simulation.Tests;

// Reads the prefabs and ranch parts that milestone 2 puts in the world, from the install.
public class InstalledPrefabTests
{
    private static readonly Lazy<(GameScripts Scripts, ItemPrefabs Prefabs)> Data = new(() =>
    {
        var scripts = new GameScripts(GameFactAttribute.Install!);
        return (scripts, ItemPrefabs.Read(scripts));
    });

    [GameFact]
    public void Slime_prefabs_get_their_body_and_face_from_the_appearance()
    {
        foreach (var id in new[] { "PINK_SLIME", "TABBY_SLIME", "ROCK_SLIME" })
        {
            var slime = Data.Value.Prefabs.Get(id);
            Assert.True(slime.Body!.Mass > 0, id);
            Assert.Contains(slime.Colliders, c => c.Collider is { Shape: ColliderShape.Sphere, IsTrigger: false });
            var body = Assert.Single(slime.Meshes, m => m.FaceLayers.Count == 2);
            Assert.True(body.Skinned);
            Assert.True(slime.Palette is not null, id);
            // Tabbies stalk their food (StalkConsumable); the others go straight for it (GotoConsumable).
            Assert.True((slime.RootScript("GotoConsumable") ?? slime.RootScript("StalkConsumable")) is not null, id + " food seeking");
            Assert.True(slime.RootScript("SlimeRandomMove") is not null, id + " SlimeRandomMove");
        }
        // Tabbies have ears and a tail; rocks have spines and a rock ball.
        Assert.True(Data.Value.Prefabs.Get("TABBY_SLIME").Meshes.Count >= 2);
        Assert.True(Data.Value.Prefabs.Get("ROCK_SLIME").Meshes.Count >= 3);
    }

    [GameFact]
    public void Plorts_and_food_have_models_bodies_and_colliders()
    {
        foreach (var id in new[] { "PINK_PLORT", "TABBY_PLORT", "ROCK_PLORT", "CARROT_VEGGIE", "HEN" })
        {
            var item = Data.Value.Prefabs.Get(id);
            Assert.NotEmpty(item.Meshes);
            Assert.Contains(item.Colliders, c => !c.Collider.IsTrigger);
            Assert.True(item.Body!.Mass > 0, id);
            // Blob shadows (drawn with Unity's default material) are left out.
            Assert.DoesNotContain(item.Meshes, m => m.Path.EndsWith("Shadow", StringComparison.Ordinal));
        }
        Assert.True(Data.Value.Prefabs.Get("PINK_PLORT").RootFloat("DestroyPlortAfterTime", "lifeTimeHours", 0) > 0);
    }

    [GameFact]
    public void The_ranch_has_a_plort_market_a_corral_and_a_vacpack_cone()
    {
        var scripts = Data.Value.Scripts;
        var scene = scripts.Assets.File("level3")!;
        var sites = RanchSites.Read(scripts, scene, "zoneRANCH");
        Assert.Contains(sites.Markets, m => m.Path.Contains("techMarket") && m.Radius > 0);
        var corral = Assert.Single(sites.Corrals, c => c.Path.Contains("patchCorral"));
        var middle = System.Numerics.Vector3.Transform(corral.Center, corral.World);
        Assert.True(corral.Contains(middle) && !corral.Contains(middle + new System.Numerics.Vector3(8, 0, 0)));

        var vac = VacuumTuning.Read(scripts, scene);
        Assert.True(vac.MaxVacDist > vac.CaptureDist && vac.CaptureDist > 0);
        Assert.True(vac.EjectSpeed > 0 && vac.ShootCooldown > 0);
        Assert.True(vac.Cone.Count >= 2);
        // The cone points where the camera looks (Unity's +Z) and contains a point a few metres ahead.
        Assert.True(vac.InCone(new System.Numerics.Vector3(0, 0, 5)));
        Assert.False(vac.InCone(new System.Numerics.Vector3(0, 0, -5)));

        Assert.True(GameTimeData.SecondsPerGameDay(scripts) > 0);
    }
}
