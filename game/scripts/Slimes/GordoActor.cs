using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// A gordo in the world: a huge slime that doesn't move. Anything in its slime's diet that touches its
/// mouth (the prefab's eat trigger) is swallowed at once and counts towards its target; it grows as it
/// fills up, strains for two seconds once full, then bursts into its rewards and a ring of its slimes
/// and is gone. A boom gordo also explodes as it bursts. Rules in docs/behavior/gordos.md; numbers
/// from the prefab's GordoEat/GordoRewards (and BoomGordoEat), code-only ones in <see cref="Gordo"/>.
/// </summary>
public partial class GordoActor : StaticBody3D
{
    private readonly ItemCatalog _catalog;
    private readonly ItemPrefab _prefab;
    private readonly Random _random = new();
    private readonly Vector3 _startScale;
    // BoomGordoEat's explosion, when the gordo has one: power, radius, player damage range.
    private readonly (float Power, float Radius, float MinDamage, float MaxDamage)? _explosion;

    public GordoActor(ItemCatalog catalog, GordoInfo info, ItemPrefab prefab, SlimeSpecies diet)
    {
        _catalog = catalog;
        _prefab = prefab;
        Sim = new Gordo(info, diet);
        Name = info.Name;
        CollisionLayer = Actor.WorldLayer;
        CollisionMask = 0;
        if (prefab.RootScript("BoomGordoEat") is { } boom)
        {
            float F(string field) => boom[field] is float v ? v : 0f;
            _explosion = (F("explodePower"), F("explodeRadius"), F("minPlayerDamage"), F("maxPlayerDamage"));
        }
        _startScale = Vector3.One;
    }

    public Gordo Sim { get; }
    public string Id => Sim.Info.Id;

    /// <summary>Raised for every meal: the food's id and how much it counted.</summary>
    public event Action<GordoActor, string, int>? Ate;
    /// <summary>Raised once full, when it starts to strain.</summary>
    public event Action<GordoActor>? Straining;
    /// <summary>Raised when it bursts, with everything that popped out.</summary>
    public event Action<GordoActor, IReadOnlyList<Actor>>? Burst;

    /// <summary>Where its mouth is (the eat trigger's centre), in world space; food dropped here is eaten.</summary>
    public Vector3 Mouth => _mouth is { } m && IsInstanceValid(m) ? m.GlobalPosition : GlobalPosition;
    private Node3D? _mouth;

    public override void _Ready()
    {
        AddChild(_catalog.VisualFor(_prefab));
        foreach (var c in _prefab.Colliders)
        {
            var place = UnityConvert.Transform(c.ToRoot);
            if (c.Collider.IsTrigger)
            {
                // The eat trigger: anything solid entering it is offered to the gordo.
                if (_prefab.Scripts.Any(s => s.Class == "GordoEatTrigger" && s.Path == c.Path) && ColliderShapes.Make(c.Collider, place) is { } trigger)
                {
                    var area = new Area3D { Name = "EatTrigger", CollisionLayer = 0, CollisionMask = Actor.ActorLayer, Monitorable = false };
                    area.AddChild(trigger);
                    area.BodyEntered += body => { if (body is Actor food) TryEat(food); };
                    AddChild(area);
                    _mouth = new Node3D { Name = "Mouth", Position = trigger.Position };
                    AddChild(_mouth);
                }
                continue;
            }
            if (c.Collider.Shape == Formats.Unity.ColliderShape.Mesh)
            {
                if (c.Mesh is not { } mesh || _catalog.World.Faces(mesh, place) is not { Length: > 0 } faces)
                    continue;
                Shape3D shape = c.Collider.Convex
                    ? new ConvexPolygonShape3D { Points = faces }
                    : new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true };
                AddChild(new CollisionShape3D { Shape = shape });
            }
            else if (ColliderShapes.Make(c.Collider, place) is { } solid)
                AddChild(solid);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // It grows from its own size to growthFactor times that as it fills (GordoEat.Update).
        Scale = _startScale * Sim.Scale;
    }

    /// <summary>Swallows <paramref name="food"/> if it is in the gordo's diet and the gordo isn't full yet.</summary>
    public bool TryEat(Actor food)
    {
        if (food.Consumed || !Sim.WillEat(food.Id))
            return false;
        var count = Sim.Feed(food.Id);
        food.Consume();
        Ate?.Invoke(this, food.Id, count);
        if (Sim.Full)
        {
            Straining?.Invoke(this);
            GetTree().CreateTimer(Gordo.BurstDelaySeconds, processAlways: false, processInPhysics: true).Timeout += DoBurst;
        }
        return true;
    }

    private void DoBurst()
    {
        if (!IsInstanceValid(this) || Sim.HasBurst)
            return;
        var center = GlobalPosition;
        // Gone at once, so what pops out isn't stuck inside it. A boom gordo explodes first, then the
        // rewards appear (GordoEat: DidCompleteBurst runs before the rewards are given).
        CollisionLayer = 0;
        if (_explosion is { } e)
            Explosions.Explode(_catalog, this, center, e.Radius, e.Power, e.MinDamage, e.MaxDamage, Id);
        var spawned = new List<Actor>();
        foreach (var s in Sim.Burst(_random))
        {
            if (!_catalog.Prefabs.Has(s.Id))
            {
                GD.PushWarning($"{Id} would drop {s.Id}, which has no prefab.");
                continue;
            }
            // Offsets are in world axes (the gordo's turn plays no part); each spawn faces outward.
            var at = center + UnityConvert.Position(s.Offset);
            var facing = UnityConvert.Position(s.Facing);
            var actor = _catalog.Spawn(s.Id, at);
            actor.GlobalBasis = Basis.LookingAt(facing.LengthSquared() > 1e-6f ? facing : Vector3.Forward, Vector3.Up);
            float Spin() => ((float)_random.NextDouble() * 2 - 1) * Gordo.SpawnTorque;
            actor.ApplyTorqueImpulse(new Vector3(Spin(), Spin(), Spin()) * _catalog.FixedTimestep);
            spawned.Add(actor);
        }
        Burst?.Invoke(this, spawned);
        // The burst gordo is switched off for good (its save counts it as burst).
        QueueFree();
    }
}
