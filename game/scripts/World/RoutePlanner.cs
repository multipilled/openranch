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
/// volume). A* then joins neighbouring floors that are a step up, a jetpack climb up, or a drop down, where the player's
/// own capsule can rise above the higher floor, move across and fall to a lower one without touching anything. Unity coordinates in and out.
/// The limits below are openranch's test settings, not game values.
/// </summary>
public static class RoutePlanner
{
    private const float Headroom = 2.2f;
    private const float JetpackClimb = 12f;
    private const float MaxDrop = 40f;
    private const int MaxFloors = 12;
    // How far above a floor the capsule's bottom moves: over the bumps and seams between grid points that the body
    // slides over on foot or clears with a short jetpack burst.
    private const float Lift = 1.0f;

    public static List<N.Vector3>? Plan(PhysicsDirectSpaceState3D space, Rid exclude, CapsuleShape3D body, N.Vector3 from, N.Vector3 to,
        float margin, float step, float minFloorNormalY, IReadOnlyList<TriggerVolume> kills)
    {
        var sweep = new PhysicsShapeQueryParameters3D { Shape = body, Exclude = new Godot.Collections.Array<Rid> { exclude } };
        // Whether the capsule, its bottom at a (Godot), moves to b without touching anything.
        bool Clear(Vector3 a, Vector3 b)
        {
            sweep.Transform = new Transform3D(Basis.Identity, a + new Vector3(0, body.Height / 2, 0));
            sweep.Motion = b - a;
            return space.CastMotion(sweep)[0] >= 1;
        }
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
        var closest = start;
        var closestD = float.MaxValue;
        while (open.TryDequeue(out var node, out _))
        {
            if (N.Vector3.Distance(Point(node), goalPoint) is var left && left < closestD)
                (closest, closestD) = (node, left);
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
            // Neighbours up to two columns away: a hop (on foot or with a jetpack burst) over a narrow steep crest.
            for (var dx = -2; dx <= 2; dx++)
                for (var dz = -2; dz <= 2; dz++)
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
                        // Room to rise above the higher floor (a jetpack climb), then to move across to the other column.
                        var above = Math.Max(here.Y, there.Y) + Lift;
                        var lifted = new Vector3(here.X, above, -here.Z);
                        if (rise > 0 && !Clear(new Vector3(here.X, here.Y + Lift, -here.Z), lifted))
                            continue;
                        var across = new Vector3(there.X, above, -there.Z);
                        if (!Clear(lifted, across))
                            continue;
                        // A drop must fall clear onto the lower floor, not through a roof over it.
                        if (rise < 0 && !Clear(across, new Vector3(there.X, there.Y + Lift, -there.Z)))
                            continue;
                        cost[next] = total;
                        came[next] = node;
                        open.Enqueue(next, total + N.Vector3.Distance(there, goalPoint));
                    }
                }
        }
        var c = Point(closest);
        GD.Print($"route: {cost.Count} floors reached from ({Point(start).X:F0}, {Point(start).Y:F0}, {Point(start).Z:F0}); " +
                 $"closest to the goal ({goalPoint.X:F0}, {goalPoint.Y:F0}, {goalPoint.Z:F0}) was ({c.X:F0}, {c.Y:F0}, {c.Z:F0}), {closestD:F0} m away");
        // What stops the capsule around the closest floor: 4 m sweeps in eight directions, with the body each one hits.
        for (var d = 0; d < 8; d++)
        {
            var angle = d * MathF.PI / 4;
            var a = new Vector3(c.X, c.Y + Lift, -c.Z);
            var b = a + new Vector3(MathF.Cos(angle), 0, -MathF.Sin(angle)) * 4;
            sweep.Transform = new Transform3D(Basis.Identity, a + new Vector3(0, body.Height / 2, 0));
            sweep.Motion = b - a;
            var fractions = space.CastMotion(sweep);
            var what = "";
            if (fractions[0] < 1)
            {
                sweep.Transform = new Transform3D(Basis.Identity, a + new Vector3(0, body.Height / 2, 0) + sweep.Motion * fractions[1]);
                sweep.Motion = Vector3.Zero;
                var rest = space.GetRestInfo(sweep);
                if (rest.Count > 0 && GodotObject.InstanceFromId((ulong)(long)rest["collider_id"]) is Node hit)
                    what = $" by {hit.GetPath()} at ({((Vector3)rest["point"]).X:F1}, {((Vector3)rest["point"]).Y:F1}, {-((Vector3)rest["point"]).Z:F1})";
            }
            GD.Print($"route: from the closest floor towards +x turned {d * 45} degrees: {fractions[0] * 4:F1} m clear{what}");
        }
        // Where the search got to, 3 x 3 columns per letter: 'o' reached, '-' floors not reached, ' ' no floor.
        GD.Print($"route: reached map, x {minX:F0} at the left, z {minZ + (nz - 1) * step:F0} at the top, {3 * step} m per letter");
        var reached = cost.Keys.Select(k => (k.Item1 / 3, k.Item2 / 3)).ToHashSet();
        for (var bz = (nz - 1) / 3; bz >= 0; bz--)
        {
            var row = new System.Text.StringBuilder($"route: {minZ + bz * 3 * step,6:F0} ");
            for (var bx = 0; bx <= (nx - 1) / 3; bx++)
            {
                var any = false;
                for (var ix = bx * 3; ix < Math.Min(nx, bx * 3 + 3); ix++)
                    for (var iz = bz * 3; iz < Math.Min(nz, bz * 3 + 3); iz++)
                        any |= floors[ix * nz + iz].Length > 0;
                row.Append(reached.Contains((bx, bz)) ? 'o' : any ? '-' : ' ');
            }
            GD.Print(row.ToString());
        }
        return null;
    }
}
