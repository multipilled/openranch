using System;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// An item in the world built from its prefab: a slime, plort, food and so on. It rolls and bounces
/// under physics, can be sucked up by the vacpack, and passes through corral walls while it flies
/// from the vacpack until it first touches something (docs/behavior/corrals.md).
/// </summary>
public partial class Actor : RigidBody3D
{
    /// <summary>Godot physics layers: the world's solid ground, items, and corral walls that stop items only.</summary>
    public const uint WorldLayer = 1, ActorLayer = 2, PenWallLayer = 4;

    private Node3D? _shooter;

    public string Id { get; set; } = "";
    public ItemKind Kind => Items.KindOf(Id);
    /// <summary>Whether the vacpack can suck it up (it has a Vacuumable component of normal size).</summary>
    public bool Vacuumable { get; set; }
    /// <summary>The radius of its solid colliders, in metres.</summary>
    public float Radius { get; set; }
    /// <summary>The drawn model; scaled when the item grows in or shrinks away.</summary>
    public Node3D Visual { get; set; } = null!;
    /// <summary>Gone from the game (eaten, sold or sucked up) and about to be removed.</summary>
    public bool Consumed { get; private set; }
    /// <summary>Shot from the vacpack and not yet touched anything.</summary>
    public bool Launched { get; private set; }
    /// <summary>The vacpack reeling it in, if any.</summary>
    public object? CaughtBy { get; set; }

    /// <summary>Raised when the item is removed from the game.</summary>
    public event Action<Actor>? Removed;

    public override void _Ready()
    {
        CollisionLayer = ActorLayer;
        CollisionMask = WorldLayer | ActorLayer | PenWallLayer;
        ContactMonitor = true;
        MaxContactsReported = 8;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Starts flying from the vacpack: corral walls and the shooter don't stop it until it touches something.</summary>
    public void Launch(Node3D shooter)
    {
        Launched = true;
        _shooter = shooter;
        CollisionMask = WorldLayer | ActorLayer;
        if (shooter is PhysicsBody3D body)
            AddCollisionExceptionWith(body);
    }

    private void OnBodyEntered(Node body)
    {
        if (Launched && body != _shooter)
            EndLaunch();
    }

    private void EndLaunch()
    {
        Launched = false;
        CollisionMask = WorldLayer | ActorLayer | PenWallLayer;
        if (_shooter is PhysicsBody3D body && IsInstanceValid(body))
            RemoveCollisionExceptionWith(body);
        _shooter = null;
    }

    /// <summary>Takes the item out of the game at once.</summary>
    public void Consume()
    {
        if (Consumed)
            return;
        Consumed = true;
        Freeze = true;
        CollisionLayer = 0;
        CollisionMask = 0;
        Removed?.Invoke(this);
        QueueFree();
    }

    /// <summary>Marks the item as taken (so nothing else grabs it) while something finishes with it.</summary>
    public void Reserve()
    {
        Consumed = true;
        Freeze = true;
        CollisionLayer = 0;
        CollisionMask = 0;
    }

    /// <summary>Removes an item reserved with <see cref="Reserve"/>.</summary>
    public void Finish()
    {
        Removed?.Invoke(this);
        QueueFree();
    }

    /// <summary>Grows the model from <paramref name="fromScale"/> of its size to full size.</summary>
    public void GrowFrom(float fromScale, float seconds)
    {
        Visual.Scale = Vector3.One * fromScale;
        CreateTween().TweenProperty(Visual, "scale", Vector3.One, seconds);
    }

    /// <summary>Shrinks the model to <paramref name="toScale"/> of its size, then calls <paramref name="done"/>.</summary>
    public void ShrinkTo(float toScale, float seconds, Action done)
    {
        var tween = CreateTween();
        tween.TweenProperty(Visual, "scale", Vector3.One * toScale, seconds);
        tween.TweenCallback(Callable.From(done));
    }
}
