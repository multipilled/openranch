using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Ranch;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// Produce hanging from a crop's spawn joint: held still where it hangs. Unripe produce is a third of
/// its size and can't be vacuumed; ripe produce lets go once the vacpack has pulled at it for its
/// prefab's release time (<c>ResourceCycle.releasePrepTime</c>), then falls under physics (static
/// analysis of <c>ResourceCycle</c>). Ripening over time is not modelled yet.
/// </summary>
public partial class CropHold : Node
{
    private sealed class Held(Slimes.Actor actor, bool unripe, float releaseSeconds)
    {
        public Slimes.Actor Actor { get; } = actor;
        public bool Unripe { get; } = unripe;
        public float ReleaseSeconds { get; } = releaseSeconds;
        public double PulledFor { get; set; }
    }

    private readonly List<Held> _held = [];

    public CropHold() => Name = "CropHold";

    /// <summary>The produce put on joints (picked or not).</summary>
    public int Count { get; private set; }

    /// <summary>How many of <see cref="Count"/> were unripe.</summary>
    public int UnripeCount { get; private set; }

    /// <summary>The produce still hanging from its joint.</summary>
    public IEnumerable<Slimes.Actor> Hanging => _held.Where(h => GodotObject.IsInstanceValid(h.Actor) && !h.Actor.Consumed).Select(h => h.Actor);

    public void Add(Slimes.Actor actor, bool unripe, float releaseSeconds)
    {
        actor.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        actor.Freeze = true;
        if (unripe)
        {
            actor.Vacuumable = false;
            actor.Visual.Scale = Vector3.One * ZoneActors.UnripeScale;
            foreach (var shape in actor.GetChildren().OfType<CollisionShape3D>())
                shape.Transform = shape.Transform.Scaled(Vector3.One * ZoneActors.UnripeScale);
        }
        _held.Add(new Held(actor, unripe, releaseSeconds));
        Count++;
        if (unripe)
            UnripeCount++;
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (var h in _held.ToList())
        {
            if (!GodotObject.IsInstanceValid(h.Actor) || h.Actor.Consumed)
            {
                _held.Remove(h);
                continue;
            }
            if (h.Unripe)
                continue;
            h.PulledFor = h.Actor.CaughtBy is null ? 0 : h.PulledFor + delta;
            if (h.Actor.CaughtBy is not null && h.PulledFor >= h.ReleaseSeconds)
            {
                h.Actor.Freeze = false;
                _held.Remove(h);
            }
        }
    }
}
