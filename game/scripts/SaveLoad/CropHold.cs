using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Ranch;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// Produce hanging from a crop's spawn joint: held still where it hangs. Unripe produce is a third of
/// its size and can't be vacuumed; ripe produce lets go once the vacpack has pulled at it for its
/// prefab's release time (<c>ResourceCycle.releasePrepTime</c>), then falls under physics (static
/// analysis of <c>ResourceCycle</c>). Slimes leave hanging produce alone, ripe or not: it only becomes
/// edible once it leaves its joint. The produce clock (RanchEconomy/RanchProduce.cs) ripens it
/// (<see cref="Ripen"/>) and lets it fall when its ripe hours are up (<see cref="Release"/>).
/// </summary>
public partial class CropHold : Node
{
    private sealed class Held(Slimes.Actor actor, bool unripe, float releaseSeconds)
    {
        public Slimes.Actor Actor { get; } = actor;
        public bool Unripe { get; set; } = unripe;
        public float ReleaseSeconds { get; } = releaseSeconds;
        public double PulledFor { get; set; }
    }

    private readonly List<Held> _held = [];

    public CropHold() => Name = "CropHold";

    /// <summary>The save's produce put on joints (picked or not).</summary>
    public int Count { get; private set; }

    /// <summary>How many of <see cref="Count"/> were unripe.</summary>
    public int UnripeCount { get; private set; }

    /// <summary>The produce still hanging from its joint (the save's and what grew since).</summary>
    public IEnumerable<Slimes.Actor> Hanging => _held.Where(h => GodotObject.IsInstanceValid(h.Actor) && !h.Actor.Consumed).Select(h => h.Actor);

    /// <summary>Produce the clock let fall because its ripe hours were up (not picked).</summary>
    public HashSet<Slimes.Actor> FellOnTime { get; } = [];

    /// <summary>Raised when the vacpack has pulled ripe produce off its joint.</summary>
    public event Action<Slimes.Actor>? Picked;

    /// <summary>Hangs the save's produce on its joint.</summary>
    public void Add(Slimes.Actor actor, bool unripe, float releaseSeconds)
    {
        Hang(actor, unripe, releaseSeconds);
        Count++;
        if (unripe)
            UnripeCount++;
    }

    /// <summary>Hangs produce on its joint (the save's, or produce a crop grows in play).</summary>
    public void Hang(Slimes.Actor actor, bool unripe, float releaseSeconds)
    {
        actor.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        actor.Freeze = true;
        actor.Edible = false;
        if (unripe)
        {
            actor.Vacuumable = false;
            actor.Visual.Scale = Vector3.One * ZoneActors.UnripeScale;
            foreach (var shape in actor.GetChildren().OfType<CollisionShape3D>())
                shape.Transform = shape.Transform.Scaled(Vector3.One * ZoneActors.UnripeScale);
        }
        _held.Add(new Held(actor, unripe, releaseSeconds));
    }

    public bool Holds(Slimes.Actor actor) => _held.Any(h => h.Actor == actor);

    /// <summary>Unripe produce ripens: it grows to full size over 4 seconds (<c>ResourceCycle</c>'s scale tween) and can be vacuumed.</summary>
    public void Ripen(Slimes.Actor actor)
    {
        if (_held.FirstOrDefault(h => h.Actor == actor) is not { Unripe: true } h)
            return;
        h.Unripe = false;
        actor.Vacuumable = true;
        foreach (var shape in actor.GetChildren().OfType<CollisionShape3D>())
            shape.Transform = shape.Transform.Scaled(Vector3.One / ZoneActors.UnripeScale);
        actor.GrowFrom(ZoneActors.UnripeScale, 4);
    }

    /// <summary>Lets go of ripe produce whose time is up: it falls and slimes may eat it.</summary>
    public void Release(Slimes.Actor actor)
    {
        if (_held.FirstOrDefault(h => h.Actor == actor) is not { } h)
            return;
        if (h.Unripe)
            Ripen(actor);
        Let(h);
        FellOnTime.Add(actor);
    }

    /// <summary>Stops holding produce (its crop is gone, or it went away).</summary>
    public void Drop(Slimes.Actor actor) => _held.RemoveAll(h => h.Actor == actor);

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
                Let(h);
                Picked?.Invoke(h.Actor);
            }
        }
    }

    // Letting go of the joint makes it edible (static analysis: ResourceCycle's edible step detaches it).
    private void Let(Held h)
    {
        h.Actor.Freeze = false;
        h.Actor.Edible = true;
        _held.Remove(h);
    }
}
