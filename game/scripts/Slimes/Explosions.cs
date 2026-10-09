using System;
using System.Linq;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Explosions in the world (docs/behavior/slime-abilities.md, "Explosions"): every item whose body
/// reaches into the radius is pushed away from the centre, once, and a player in reach is pushed and
/// hurt. The formulas are <see cref="Blast"/>'s.
/// </summary>
public static class Explosions
{
    /// <summary>What one explosion did: how many items it pushed and how much health the player lost (0 when out of reach).</summary>
    public readonly record struct Result(string Source, Vector3 Center, float Radius, float Power, int Pushed, int PlayerDamage);

    public static Result Explode(ItemCatalog catalog, Node3D source, Vector3 center, float radius, float power, float minDamage, float maxDamage, string sourceId)
    {
        var pushed = 0;
        foreach (var actor in catalog.Live.ToList())
        {
            if (actor == source || actor.Freeze)
                continue;
            var offset = actor.GlobalPosition - center;
            var distance = offset.Length();
            // The original gathers every collider overlapping the sphere, so a body counts once any
            // part of it is inside.
            if (distance - actor.Radius > radius)
                continue;
            var dir = distance > 1e-4f ? offset / distance : Vector3.Up;
            // A one-off force in the original, so one physics step's worth of it (its fixed step).
            actor.ApplyCentralImpulse(dir * (Blast.BodyPush(power, radius, distance) * catalog.FixedTimestep));
            pushed++;
        }

        var damage = 0;
        if (catalog.Player is { } player && IsInstanceValid(player))
        {
            var offset = player.GlobalPosition - center;
            var distance = offset.Length();
            if (distance <= radius)
            {
                damage = Blast.PlayerDamage(minDamage, maxDamage, radius, distance);
                catalog.PlayerVitals.Damage(damage, sourceId);
                // openranch's stand-in for the original controller's push (UNVERIFIED.md, "Player knockback").
                var dir = distance > 1e-4f ? offset / distance : Vector3.Up;
                player.Velocity += dir * (Blast.PlayerPush(power, radius, distance) / catalog.FixedTimestep);
            }
        }
        var result = new Result(sourceId, center, radius, power, pushed, damage);
        catalog.ReportExplosion(result);
        return result;
    }

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);
}
