using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Builds items (slimes, plorts, food) from their prefabs in the player's install and keeps them
/// under one parent node. Models, materials and slime species are made once per item id.
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<string, Node3D> _visuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SlimeSpecies?> _species = new(StringComparer.Ordinal);
    private readonly Dictionary<(AssetRef?, AssetRef, string), Material> _materials = new();

    public ItemCatalog(GameScripts scripts, ItemPrefabs prefabs, SlimeData slimes, GameClock clock, Node3D actors)
    {
        Scripts = scripts;
        Prefabs = prefabs;
        Slimes = slimes;
        Clock = clock;
        Actors = actors;
        World = new WorldAssets(scripts.Assets);
        ItemIds = scripts.IdentifiableIds.Values.Keys.ToList();
    }

    public GameScripts Scripts { get; }
    public ItemPrefabs Prefabs { get; }
    public SlimeData Slimes { get; }
    public GameClock Clock { get; }
    public WorldAssets World { get; }
    public IReadOnlyList<string> ItemIds { get; }
    /// <summary>The node every item lives under.</summary>
    public Node3D Actors { get; }

    /// <summary>Raised for every item put into the world.</summary>
    public event Action<Actor>? Spawned;

    /// <summary>The items in the world that haven't been taken out of the game.</summary>
    public IEnumerable<Actor> Live => Actors.GetChildren().OfType<Actor>().Where(a => !a.Consumed && GodotObject.IsInstanceValid(a));

    /// <summary>The slime species for an id, or null when it isn't a slime with eating settings.</summary>
    public SlimeSpecies? Species(string id)
    {
        if (!_species.TryGetValue(id, out var species))
        {
            species = Items.KindOf(id) == ItemKind.Slime && Slimes.TryGet(id, out var info) && info.Eating is not null
                ? SlimeSpecies.From(info, ItemIds)
                : null;
            _species[id] = species;
        }
        return species;
    }

    /// <summary>Puts a new item into the world at <paramref name="position"/> (Godot coordinates).</summary>
    public Actor Spawn(string id, Vector3 position, float yawRadians = 0)
    {
        var prefab = Prefabs.Get(id);
        Actor actor = Species(id) is { } species ? new SlimeActor(this, species, prefab) : new Actor();
        actor.Id = id;
        actor.Name = id;
        // Mass and drags from the prefab's Rigidbody.
        actor.Mass = Math.Max(0.001f, prefab.Body?.Mass ?? 1f);
        actor.LinearDamp = prefab.Body?.Drag ?? 0f;
        actor.AngularDamp = prefab.Body?.AngularDrag ?? 0f;
        actor.Vacuumable = prefab.RootScript("Vacuumable") is not null && prefab.VacuumSize == 0;
        actor.Radius = Radius(prefab);
        actor.Visual = BuildVisual(prefab);
        actor.AddChild(actor.Visual);
        foreach (var c in prefab.Colliders.Where(c => !c.Collider.IsTrigger))
            if (ColliderShapes.Make(c.Collider, UnityConvert.Transform(c.ToRoot)) is { } shape)
                actor.AddChild(shape);
        actor.Position = position;
        actor.Rotation = new Vector3(0, yawRadians, 0);
        Actors.AddChild(actor);
        Spawned?.Invoke(actor);
        return actor;
    }

    /// <summary>The radius of the item's solid colliders, the size the vacpack uses to place a shot item.</summary>
    public static float Radius(ItemPrefab prefab)
    {
        var radius = 0f;
        foreach (var c in prefab.Colliders.Where(c => !c.Collider.IsTrigger))
        {
            var scale = UnityConvert.Transform(c.ToRoot).Basis.Scale.Abs();
            var s = Math.Max(scale.X, Math.Max(scale.Y, scale.Z));
            var r = c.Collider.Shape switch
            {
                ColliderShape.Sphere => c.Collider.Radius,
                ColliderShape.Capsule => Math.Max(c.Collider.Radius, c.Collider.Height / 2),
                ColliderShape.Box => c.Collider.Size.Length() / 2,
                _ => 0f,
            };
            radius = Math.Max(radius, r * s + c.Collider.Center.Length() * s);
        }
        return radius;
    }

    private Node3D BuildVisual(ItemPrefab prefab)
    {
        if (!_visuals.TryGetValue(prefab.Id, out var template))
        {
            template = new Node3D { Name = "Visual" };
            foreach (var part in prefab.Meshes)
            {
                var subMeshes = World.Mesh(part.Mesh).SubMeshes.Count;
                for (var s = 0; s < subMeshes; s++)
                {
                    var material = part.Materials.Count == 0 ? null : part.Materials[Math.Min(s, part.Materials.Count - 1)];
                    var mesh = World.BuildMesh(part.Mesh, [(s, material)], mirrored: false);
                    if (mesh.GetSurfaceCount() == 0)
                        continue;
                    mesh.SurfaceSetMaterial(0, MaterialFor(material, part.Mesh, part.FaceLayers));
                    template.AddChild(new MeshInstance3D { Mesh = mesh, Transform = UnityConvert.Transform(part.ToRoot) });
                }
            }
            _visuals[prefab.Id] = template;
        }
        return (Node3D)template.Duplicate();
    }

    // Slime bodies and plorts get the gradient stand-in; everything else goes through the world's
    // material conversion. Face layers (eyes, mouth) are drawn as further passes over the body.
    private Material MaterialFor(AssetRef? materialRef, AssetRef meshRef, IReadOnlyList<AssetRef> faceLayers)
    {
        var key = (materialRef, meshRef, string.Join(",", faceLayers.Select(f => f.PathId)));
        if (_materials.TryGetValue(key, out var cached))
            return cached;

        var assets = Scripts.Assets;
        Material result;
        var data = materialRef is { ClassId: UnityClassId.Material } m ? assets.Read(m, MaterialData.Read) : null;
        if (data is not null && SlimeLooks.IsGradient(data))
        {
            var mesh = assets.Read(meshRef, MeshData.Read);
            var stripes = data.Texture("_StripeTexture") is { } slot ? World.Texture(assets.Resolve(materialRef!.Value.File, slot.Texture)) : null;
            result = SlimeLooks.Body(data, mesh.BoundsCenter.Y - mesh.BoundsExtent.Y, mesh.BoundsCenter.Y + mesh.BoundsExtent.Y, stripes);
        }
        else
        {
            result = World.Material(materialRef);
            if (faceLayers.Count > 0)
                result = (Material)result.Duplicate();
        }

        var last = result;
        foreach (var layer in faceLayers)
        {
            var face = assets.Read(layer, MaterialData.Read);
            var atlas = face.Texture("_FaceAtlas") is { } slot ? World.Texture(assets.Resolve(layer.File, slot.Texture)) : null;
            if (SlimeLooks.Face(face, atlas) is not { } pass)
                continue;
            last.NextPass = pass;
            last = pass;
        }
        _materials[key] = result;
        return result;
    }
}
