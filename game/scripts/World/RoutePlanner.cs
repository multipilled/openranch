using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Finds a way on foot (and jetpack) between two points of the built world, for scripted checks (World/WorldCheck.cs).
/// Rays straight down on a grid find every floor of each column (caves and tunnels under hills included); a floor is
/// one the player can stand on (not steeper than the rig's slope limit, room for the body above it, not in a kill
/// volume). A* then joins neighbouring floors that are a step up, a jetpack climb up, or a drop down, with no wall
/// across. Unity coordinates in and out. The limits below are openranch's test settings, not game values.
/// </summary>
public static class RoutePlanner
{
    private const float Headroom = 2.2f;
    private const float JetpackClimb = 7f;
    private const float MaxDrop = 14f;
    private const int MaxFloors = 12;

    public static List<N.Vector3>? Plan(PhysicsDirectSpaceState3D space, Rid exclude, N.Vector3 from, N.Vector3 to, float margin, float step,
        float minFloorNormalY, IReadOnlyList<TriggerVolume> kills)
    {
        var minX = Math.Min(from.X, to.X) - margin;
        var minZ = Math.Min(from.Z, to.Z) - margin;
        var nx = (int)((Math.Max(from.X, to.X) + margin - minX) / step) + 1;
        var nz = (int)((Math.Max(from.Z, to.Z) + margin - minZ) / step) + 1;
        var top = Math.Max(from.Y, to.Y) + 150;
        var bottom = Math.Min(from.Y, to.Y) - 100;
        var exclusions = new Godot.Collections.Array<Rid> { exclude };

        // Every floor of each column, lowest last.
        var floors = new float[nx * nz][];
        for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
            {
                var x = minX + ix * step;
                var z = minZ + iz * step;
                var hits = new List<(float Y, float NormalY)>();
                var y = top;
                while (hits.Count < MaxFloors)
                {
                    var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, y, -z), new Vector3(x, bottom, -z));
                    query.Exclude = exclusions;
                    query.HitBackFaces = true;
                    var hit = space.IntersectRay(query);
                    if (hit.Count == 0)
                        break;
                    var p = (Vector3)hit["position"];
                    hits.Add((p.Y, ((Vector3)hit["normal"]).Y));
                    y = p.Y - 0.05f;
                }
                var list = new List<float>();
                for (var i = 0; i < hits.Count; i++)
                {
                    var (fy, normal) = hits[i];
                    if (normal < minFloorNormalY)
                        continue;
                    if (i > 0 && hits[i - 1].Y - fy < Headroom)
                        continue;
                    var at = new N.Vector3(x, fy + 0.1f, z);
                    if (kills.Any(k => k.Contains(at)))
                        continue;
                    list.Add(fy);
                }
                floors[ix * nz + iz] = list.ToArray();
            }

        (int, int, int)? Nearest(N.Vector3 p)
        {
            (int, int, int)? best = null;
            var bestD = float.MaxValue;
            var cx = (int)MathF.Round((p.X - minX) / step);
            var cz = (int)MathF.Round((p.Z - minZ) / step);
            for (var ix = Math.Max(0, cx - 4); ix <= Math.Min(nx - 1, cx + 4); ix++)
                for (var iz = Math.Max(0, cz - 4); iz <= Math.Min(nz - 1, cz + 4); iz++)
                {
                    var f = floors[ix * nz + iz];
                    for (var k = 0; k < f.Length; k++)
                    {
                        var d = N.Vector3.DistanceSquared(p, new N.Vector3(minX + ix * step, f[k], minZ + iz * step));
                        if (d < bestD)
                        {
                            bestD = d;
                            best = (ix, iz, k);
                        }
                    }
                }
            return best;
        }

        N.Vector3 Point((int X, int Z, int K) n) => new(minX + n.X * step, floors[n.X * nz + n.Z][n.K], minZ + n.Z * step);

        if (Nearest(from) is not { } start || Nearest(to) is not { } goal)
            return null;
        var goalPoint = Point(goal);
        var open = new PriorityQueue<(int, int, int), float>();
        var cost = new Dictionary<(int, int, int), float> { [start] = 0 };
        var came = new Dictionary<(int, int, int), (int, int, int)>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var node, out _))
        {
            if (node == goal)
            {
                var path = new List<N.Vector3> { Point(node) };
                while (came.TryGetValue(node, out var prev))
                {
                    node = prev;
                    path.Add(Point(node));
                }
                path.Reverse();
                return path;
            }
            var here = Point(node);
            var g = cost[node];
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    var ix = node.Item1 + dx;
                    var iz = node.Item2 + dz;
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz)
                        continue;
                    var f = floors[ix * nz + iz];
                    for (var k = 0; k < f.Length; k++)
                    {
                        var next = (ix, iz, k);
                        var there = Point(next);
                        var flat = step * MathF.Sqrt(dx * dx + dz * dz);
                        var rise = there.Y - here.Y;
                        float edge;
                        if (rise <= flat)
                            edge = flat;
                        else if (rise <= JetpackClimb)
                            edge = flat + rise * 4;
                        else
                            continue;
                        if (rise < -MaxDrop)
                            continue;
                        if (rise < -flat)
                            edge = flat - rise * 0.5f;
                        var total = g + edge;
                        if (cost.TryGetValue(next, out var known) && known <= total)
                            continue;
                        // No wall across: a ray at chest height between the two floors.
                        var a = new Vector3(here.X, Math.Max(here.Y, there.Y) + 1.2f, -here.Z);
                        var b = new Vector3(there.X, Math.Max(here.Y, there.Y) + 1.2f, -there.Z);
                        var wall = PhysicsRayQueryParameters3D.Create(a, b);
                        wall.Exclude = exclusions;
                        if (space.IntersectRay(wall).Count > 0)
                            continue;
                        cost[next] = total;
                        came[next] = node;
                        open.Enqueue(next, total + N.Vector3.Distance(there, goalPoint));
                    }
                }
        }
        return null;
    }
}
