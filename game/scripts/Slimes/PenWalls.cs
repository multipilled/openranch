using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.World;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Corral walls: every solid collider of the area on a physics layer that stops items ("Actor") but
/// not the player, such as the corral barriers on "Pen Walls", becomes an items-only wall. Which
/// layers collide comes from the game's layer collision matrix (docs/behavior/corrals.md).
/// </summary>
public static class PenWalls
{
    public static StaticBody3D Build(ZoneExtract zone, PhysicsLayers layers)
    {
        var body = new StaticBody3D { Name = "PenWalls", CollisionLayer = Actor.PenWallLayer, CollisionMask = 0 };
        var actorLayer = Enumerable.Range(0, layers.Names.Count).FirstOrDefault(i => layers.Names[i] == "Actor", -1);
        if (actorLayer < 0)
            return body;
        foreach (var c in zone.Colliders)
        {
            var layer = (int)c.Layer;
            if (!layers.Collide(actorLayer, layer) || layers.Collide(PhysicsLayers.PlayerLayer, layer))
                continue;
            if (ColliderShapes.Make(c.Collider, UnityConvert.Transform(c.World)) is { } shape)
                body.AddChild(shape);
        }
        return body;
    }
}
