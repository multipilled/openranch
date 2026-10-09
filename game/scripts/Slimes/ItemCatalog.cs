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
        Largos = new Largos(slimes);
        FixedTimestep = GameTimeData.FixedTimestep(scripts.Assets);
    }

    /// <summary>
    /// The original's fixed physics step. Its slimes push themselves with forces sized by this step,
    /// so the same forces here use it too, whatever Godot's own step is.
    /// </summary>
    public float FixedTimestep { get; }

    public GameScripts Scripts { get; }
    public ItemPrefabs Prefabs { get; }
    public SlimeData Slimes { get; }
    public GameClock Clock { get; }
    public WorldAssets World { get; }
    public IReadOnlyList<string> ItemIds { get; }
    /// <summary>Which largo two plorts make (docs/behavior/largos.md).</summary>
    public Largos Largos { get; }
    /// <summary>The node every item lives under.</summary>
    public Node3D Actors { get; }

    /// <summary>Raised for every item put into the world.</summary>
    public event Action<Actor>? Spawned;

    /// <summary>The player's body, for slimes that chase, hurt or flee from the player (null: no player around).</summary>
    public CharacterBody3D? Player { get; set; }
    /// <summary>The player's health and radiation (a stand-in until openranch has a player health bar).</summary>
    public PlayerVitals PlayerVitals { get; } = new();
    /// <summary>
    /// Whether a point (Godot coordinates) is inside a cave trigger. The world's owner sets it from the
    /// zone's cave volumes; without it nothing counts as a cave.
    /// </summary>
    public Func<Vector3, bool>? InCave { get; set; }

    /// <summary>Raised for every explosion (boom slimes, boom gordos, feral stomps).</summary>
    public event Action<Explosions.Result>? Exploded;
    internal void ReportExplosion(Explosions.Result result) => Exploded?.Invoke(result);

    private GordoData? _gordos;
    /// <summary>The install's gordos (the LookupDirector's gordo list).</summary>
    public GordoData Gordos => _gordos ??= GordoData.Read(Scripts);

    /// <summary>Raised for every gordo put into the world.</summary>
    public event Action<GordoActor>? GordoSpawned;

    /// <summary>
    /// Puts a gordo into the world at <paramref name="position"/> (Godot coordinates). <paramref name="placed"/>
    /// is a gordo as the world scene places it (with its own rewards); otherwise the prefab list's.
    /// </summary>
    public GordoActor SpawnGordo(string id, Vector3 position, float yawRadians = 0, GordoInfo? placed = null)
    {
        var info = placed ?? Gordos.Get(id) ?? throw new KeyNotFoundException($"No gordo {id}.");
        var diet = Species(info.Slime) ?? throw new KeyNotFoundException($"{id} eats like {info.Slime}, which has no slime settings.");
        var gordo = new GordoActor(this, info, Prefabs.Get(id), diet) { Position = position, Rotation = new Vector3(0, yawRadians, 0) };
        Actors.AddChild(gordo);
        GordoSpawned?.Invoke(gordo);
        return gordo;
    }

    /// <summary>A fresh copy of an item's model (as drawn when spawned).</summary>
    public Node3D VisualFor(ItemPrefab prefab) => BuildVisual(prefab);

    /// <summary>The items in the world that haven't been taken out of the game.</summary>
    public IEnumerable<Actor> Live => Actors.GetChildren().OfType<Actor>().Where(a => !a.Consumed && GodotObject.IsInstanceValid(a));

    /// <summary>The slime species for an id (a slime or a largo), or null when it isn't a slime with eating settings.</summary>
    public SlimeSpecies? Species(string id)
    {
        if (!_species.TryGetValue(id, out var species))
        {
            species = Items.IsSlime(id) && Slimes.TryGet(id, out var info) && info.Eating is not null
                ? SlimeSpecies.From(info, ItemIds, Largos)
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
        // A kinematic body (a key) stays where it is put; gravity only pulls bodies that use it.
        if (prefab.Body is { IsKinematic: true })
        {
            actor.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            actor.Freeze = true;
        }
        if (prefab.Body is { UseGravity: false })
            actor.GravityScale = 0;
        actor.Vacuumable = prefab.RootScript("Vacuumable") is not null && prefab.VacuumSize == 0;
        actor.Radius = Radius(prefab);
        if (prefab.RootScript("KeepUpright") is { } upright)
        {
            actor.UprightStability = upright["stability"] is float st ? st : 0;
            actor.UprightSpeed = upright["speed"] is float sp ? sp : 0;
        }
        actor.PhysicsMaterialOverride = ContactMaterial(prefab.Colliders.FirstOrDefault(c => !c.Collider.IsTrigger)?.Material);
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

    // Unity's default physic material, which the world's colliders use: friction 0.6, no bounce, both averaged.
    private const float DefaultFriction = 0.6f, DefaultBounce = 0f;

    /// <summary>
    /// The item's friction and bounce against the world, combined the way Unity combines two materials
    /// (the stronger of the two combine modes wins; the world's is "average").
    /// </summary>
    public static PhysicsMaterial ContactMaterial(PhysicMaterialData? material)
    {
        static float Combine(float a, float b, int mode) => mode switch
        {
            1 => Math.Min(a, b),
            2 => a * b,
            3 => Math.Max(a, b),
            _ => (a + b) / 2,
        };
        if (material is null)
            return new PhysicsMaterial { Friction = DefaultFriction, Bounce = DefaultBounce };
        return new PhysicsMaterial
        {
            Friction = Combine(material.DynamicFriction, DefaultFriction, material.FrictionCombine),
            Bounce = Combine(material.Bounciness, DefaultBounce, material.BounceCombine),
        };
    }

    /// <summary>Frees the model templates kept for spawning (they live outside the scene tree).</summary>
    public void FreeTemplates()
    {
        foreach (var template in _visuals.Values)
            template.Free();
        _visuals.Clear();
        _materials.Clear();
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
            // Objects with HideOnStart switch their renderer off at once (a gordo's full-size outline).
            var hidden = prefab.Scripts.Where(s => s.Class == "HideOnStart").Select(s => s.Path).ToHashSet(StringComparer.Ordinal);
            foreach (var part in prefab.Meshes.Where(m => !hidden.Contains(m.Path)))
            {
                var subMeshes = World.Mesh(part.Mesh).SubMeshes.Count;
                for (var s = 0; s < subMeshes; s++)
                {
                    var material = part.Materials.Count == 0 ? null : part.Materials[Math.Min(s, part.Materials.Count - 1)];
                    if (IsTransparentEffect(material))
                        continue;
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

    // A material set to draw in Unity's transparent range (render queue above 2500), such as the rad
    // slime's aura shell (4001). openranch leaves these out for now: drawn opaque they hide the slime,
    // and its full-screen fog pass erases transparent objects anyway (UNVERIFIED.md).
    private bool IsTransparentEffect(AssetRef? materialRef) =>
        materialRef is { ClassId: UnityClassId.Material } m && Scripts.Assets.Read(m, MaterialData.Read).CustomRenderQueue > 2500;

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
