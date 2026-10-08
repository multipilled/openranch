using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;

namespace OpenRanch.Game;

/// <summary>
/// Checks that an area can be walked on: casts rays straight down on a grid over the area to find
/// ground, then drops the player onto a spread of ground points and checks that it comes to rest
/// standing there instead of falling through. Runs without a window (--headless).
/// </summary>
public sealed class CollisionCheck
{
    private const float GridStep = 4f;
    private const int Drops = 40;
    private const int SettleFrames = 90;

    private readonly Node3D _scene;
    private readonly PlayerController _player;
    private readonly Aabb _bounds;
    private readonly float _minWalkableNormalY;
    private int _warmup = 3;
    private List<Vector3>? _targets;
    private int _current = -1;
    private int _frame;
    private int _rays, _hits, _walkable, _landed;
    private readonly List<string> _failures = new();

    public CollisionCheck(Node3D scene, PlayerController player, ZoneExtract zone, PlayerRig rig)
    {
        _scene = scene;
        _player = player;
        _minWalkableNormalY = Mathf.Cos(Mathf.DegToRad(rig.SlopeLimitDegrees));
        var points = zone.Colliders.Select(c => UnityConvert.Transform(c.World).Origin).ToList();
        var bounds = new Aabb(points[0], Vector3.Zero);
        foreach (var p in points)
            bounds = bounds.Expand(p);
        _bounds = bounds;
        _player.SetPhysicsProcess(false);
    }

    public bool Passed { get; private set; }
    public string Report { get; private set; } = "";

    /// <summary>Advances one physics frame; returns true when the check is finished.</summary>
    public bool Step()
    {
        if (_warmup-- > 0)
            return false; // let the physics server take in the new shapes

        if (_targets is null)
        {
            _targets = CastGrid();
            _player.SetPhysicsProcess(true);
            NextDrop();
            return false;
        }

        if (_current < _targets.Count && ++_frame >= SettleFrames)
        {
            var target = _targets[_current];
            var rest = _player.GlobalPosition;
            if (_player.IsOnFloor() && Math.Abs(rest.Y - target.Y) < 1.5f)
                _landed++;
            else
                _failures.Add($"({target.X:F0}, {target.Y:F0}, {target.Z:F0}) ended at y={rest.Y:F1}");
            NextDrop();
        }

        if (_current < _targets.Count)
            return false;

        var groundShare = _rays == 0 ? 0 : 100.0 * _hits / _rays;
        Passed = _targets.Count > 0 && _landed >= _targets.Count * 0.95;
        Report = $"collision-check: {_rays} rays over {_bounds.Size.X:F0} x {_bounds.Size.Z:F0} m, {_hits} hit ground " +
                 $"({groundShare:F0}%), {_walkable} walkable; player landed on {_landed} of {_targets.Count} drops" +
                 (_failures.Count > 0 ? "; misses: " + string.Join("; ", _failures.Take(5)) : "") +
                 (Passed ? " -> PASS" : " -> FAIL");
        return true;
    }

    private void NextDrop()
    {
        _current++;
        _frame = 0;
        if (_current >= _targets!.Count)
            return;
        _player.GlobalPosition = _targets[_current] + new Vector3(0, 2.5f, 0);
        _player.Velocity = Vector3.Zero;
    }

    private List<Vector3> CastGrid()
    {
        var space = _scene.GetWorld3D().DirectSpaceState;
        var walkable = new List<Vector3>();
        var top = _bounds.End.Y + 60;
        var bottom = _bounds.Position.Y - 60;
        for (var x = _bounds.Position.X; x <= _bounds.End.X; x += GridStep)
        {
            for (var z = _bounds.Position.Z; z <= _bounds.End.Z; z += GridStep)
            {
                _rays++;
                var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, top, z), new Vector3(x, bottom, z));
                query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
                var hit = space.IntersectRay(query);
                if (hit.Count == 0)
                    continue;
                _hits++;
                var normal = (Vector3)hit["normal"];
                if (normal.Y >= _minWalkableNormalY)
                {
                    _walkable++;
                    walkable.Add((Vector3)hit["position"]);
                }
            }
        }
        // An even spread of drop points across the walkable ground.
        var step = Math.Max(1, walkable.Count / Drops);
        return walkable.Where((_, i) => i % step == 0).Take(Drops).ToList();
    }
}
