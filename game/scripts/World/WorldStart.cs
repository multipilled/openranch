using System;
using System.Globalization;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.Player;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Where the player starts in the joined world (docs/behavior/zones.md, "Where the player arrives"), in order:
/// --camera x,y,z,yaw,pitch (in whichever region set's cells hold the point); --spawn NAME (that teleporter
/// destination, in its zone's set); a save's player, in their saved region set where they stood, when that set's cells
/// hold the place; otherwise the player rig's own place in the scene, on The Ranch.
/// </summary>
public static class WorldStart
{
    public static void Place(WorldMap map, PlayerController player, PlayerRig rig, Rancher? saved, string? spawnName, string? camera)
    {
        if (saved is not null)
            map.Network.Progress = saved.Progress;

        if (camera is not null)
        {
            var v = camera.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var at = new N.Vector3(v[0], v[1], v[2]);
            map.Start(player, at, map.SetAt(at) ?? map.Sets.Home, v.Length > 3 ? v[3] : 0, v.Length > 4 ? v[4] : 0);
            return;
        }

        if (spawnName is not null && map.Destinations.FirstOrDefault(d => d.Point.Name == spawnName) is { Point: not null } spawn)
        {
            var (pitch, yaw) = WorldMap.UnityEuler(spawn.Point.World);
            map.Start(player, spawn.Point.World.Translation, spawn.Set, spawn.Point.Reorient ? yaw : 0, spawn.Point.Reorient ? pitch : 0);
            GD.Print($"World: player starts at {spawnName} in {map.Sets.NameOf(spawn.Set)}");
            return;
        }

        // The save keeps the player's feet, their view as Euler angles (pitch in x, yaw in y; docs/behavior/player-camera.md)
        // and the region set they were in (PlayerModel.currRegionSetId).
        if (saved is not null)
        {
            var feet = new N.Vector3(saved.Position.X, saved.Position.Y, saved.Position.Z);
            if (map.CellsOf(saved.RegionSetId).Any(c => c.Contains(feet)))
            {
                var pitch = saved.Rotation.X > 180 ? saved.Rotation.X - 360 : saved.Rotation.X;
                map.Start(player, feet, saved.RegionSetId, saved.Rotation.Y, pitch);
                GD.Print($"World: player starts where the save left them, ({feet.X:F1}, {feet.Y:F1}, {feet.Z:F1}) in {map.Sets.NameOf(saved.RegionSetId)}");
                return;
            }
            GD.Print($"World: the save's player ({feet.X:F1}, {feet.Y:F1}, {feet.Z:F1}) is in no cell of set {saved.RegionSetId}; starting on The Ranch");
        }
        map.Start(player, rig.Spawn, map.SetAt(rig.Spawn) ?? map.Sets.Home);
    }
}
