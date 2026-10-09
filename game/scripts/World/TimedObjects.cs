using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;

namespace OpenRanch.Game.World;

/// <summary>
/// The world objects tied to the time of day (lamps, night glows and their day-time counterparts), each
/// built as its own node and switched on and off as the world clock passes its hours
/// (docs/behavior/world-visibility.md). A hidden object neither draws, lights nor collides.
/// </summary>
public partial class TimedObjects : Node3D
{
    private readonly List<(TimedGroup Group, Node3D Node, Node? Body)> _groups = new();
    private float? _hour;

    private TimedObjects()
    {
        Name = "TimedObjects";
    }

    /// <summary>Each object with the hours it shows, and its node.</summary>
    public IEnumerable<(TimedGroup Group, Node3D Node, Node? Body)> Groups => _groups;

    /// <summary>None yet: the parts of a streamed world are added as they are built (<see cref="Add"/>).</summary>
    public static TimedObjects Empty(float hour)
    {
        var result = new TimedObjects();
        result.SetHour(hour);
        return result;
    }

    public static TimedObjects Build(ZoneExtract zone, WorldAssets assets, PhysicsLayers layers, WorldLighting lighting, float hour)
    {
        var result = new TimedObjects();
        var i = 0;
        foreach (var group in zone.TimedGroups)
        {
            var built = ZoneBuilder.Build(zone with
            {
                Name = $"{i++}: {group.Path.Split('/').Last()}",
                Renderers = group.Renderers,
                Colliders = group.Colliders,
            }, assets, layers);
            foreach (var lamp in group.Lights)
                lighting.AddLamp(lamp, built.Root);
            result.AddChild(built.Root);
            result._groups.Add((group, built.Root, built.Root.GetNodeOrNull("Collision")));
        }
        result.SetHour(hour);
        return result;
    }

    /// <summary>
    /// Adds a part of the world's objects tied to the time of day, built under <paramref name="parent"/> (a cell that
    /// loads and unloads, World/WorldMap.cs), shown or hidden for the hour the others show.
    /// </summary>
    public void Add(ZoneExtract part, WorldAssets assets, PhysicsLayers layers, WorldLighting lighting, Node parent)
    {
        foreach (var group in part.TimedGroups)
        {
            var built = ZoneBuilder.Build(part with
            {
                Name = $"{_groups.Count}: {group.Path.Split('/').Last()}",
                Renderers = group.Renderers,
                Colliders = group.Colliders,
            }, assets, layers);
            foreach (var lamp in group.Lights)
                lighting.AddLamp(lamp, built.Root);
            parent.AddChild(built.Root);
            var body = built.Root.GetNodeOrNull("Collision");
            _groups.Add((group, built.Root, body));
            if (_hour is { } hour)
            {
                built.Root.Visible = group.IsShown(hour);
                if (body is not null && !built.Root.Visible)
                    body.ProcessMode = ProcessModeEnum.Disabled;
            }
        }
    }

    /// <summary>Shows the objects whose hours include <paramref name="hour"/> (0 to 24) and hides the rest.</summary>
    public void SetHour(float hour)
    {
        if (hour == _hour)
            return;
        _hour = hour;
        foreach (var (group, node, body) in _groups)
        {
            var shown = group.IsShown(hour);
            if (node.Visible == shown)
                continue;
            node.Visible = shown;
            // A disabled body leaves the physics world, so a hidden object doesn't collide.
            if (body is not null)
                body.ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        }
    }

    /// <summary>Whether the object's node shows and, if it has colliders, they are in the physics world.</summary>
    public static bool IsOn(Node3D node, Node? body) => node.Visible && (body is null || body.ProcessMode != ProcessModeEnum.Disabled);
}
