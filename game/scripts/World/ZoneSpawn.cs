using System;
using System.Linq;
using OpenRanch.Formats.Scene;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Where the player starts in an area built with --zone (docs/behavior/zones.md): the developers' named
/// start point for the zone (a DebugTeleportDestination such as "Wilds Start") if it has one, otherwise the
/// first teleporter destination in scene order that the player arrives at by the zone's own teleporter (not an
/// echo note gordo's, which only opens once that gordo is popped), otherwise any destination. The player lands
/// on the destination's position and, when it reorients, faces its way (TeleportablePlayer.TeleportTo).
/// </summary>
public static class ZoneSpawn
{
    // The name every echo note gordo's teleporter gives its destination in the world scene.
    private const string EchoNoteGordo = "echoNoteGordo_source";

    public sealed record Spawn(TeleportPoint Point, N.Vector3 Position, float YawDegrees);

    public static Spawn? Pick(ZoneExtract zone)
    {
        var point = zone.Teleports.FirstOrDefault(t => t.IsDebugStart)
                    ?? zone.Teleports.FirstOrDefault(t => t.Name != EchoNoteGordo)
                    ?? zone.Teleports.FirstOrDefault();
        if (point is null)
            return null;
        // Unity yaw: the turn about Y of the object's forward (+Z) axis, clockwise seen from above.
        var forward = N.Vector3.TransformNormal(N.Vector3.UnitZ, point.World);
        var yaw = point.Reorient ? MathF.Atan2(forward.X, forward.Z) * 180 / MathF.PI : 0;
        return new Spawn(point, point.World.Translation, yaw);
    }
}
