using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.Player;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Headless checks of the joined world (World/WorldMap.cs), run from the command line:
///   --world-check            walks (and jetpacks where the way climbs) from The Ranch through the Dry Reef into the
///                            Indigo Quarry's cave hub (--walk-to x,y,z elsewhere), on a route planned beforehand with
///                            every cell loaded (World/RoutePlanner.cs) and then walked with cells streaming, reporting the
///                            cells that load and unload and the ambience zone on the way; then walks into a teleporter
///                            (--teleport-via NAME, default SeaMustacheIsland: the Slime Sea's mini island teleporter) and
///                            checks the player lands on its destination and stays there; quits with 0 on PASS
///   --ground-map x0,z0,x1,z1,step[,top]   prints the ground height under a rectangle (Unity x, z) after loading every cell, then quits
/// The route and its end are test input, not game data.
/// </summary>
public partial class WorldCheck : Node
{
    // Where the walk ends: the Indigo Quarry's cave hub teleporter pad (its TeleportDestination "QuarryCaveHub").
    private static readonly N.Vector3 DefaultEnd = new(228.2f, 5.8f, 147.0f);
    private const double LegTimeout = 25;
    // Waypoints every this many planner steps.
    private const int WaypointStride = 4;
    private const string DefaultTeleport = "SeaMustacheIsland";

    private readonly WorldMap _map;
    private readonly PlayerController _player;
    private readonly WorldLighting _lighting;
    private readonly string[] _args;
    private readonly StringBuilder _report = new();
    private List<N.Vector3> _path = [];
    private readonly N.Vector3 _end;
    private readonly bool _walk;
    private readonly string? _groundMap;
    private readonly bool _listCells;
    private int _leg;
    private double _legTime, _stuckTime, _jetTime, _time;
    private int _loads, _unloads, _ambienceChanges;
    private bool _jetpacked;
    private int _zone = -1;
    private float _lastDistance = float.MaxValue;
    private Phase _phase = Phase.Settle;
    private int _settleFrames = 30;
    private bool _passed = true;
    private readonly List<string> _zonesVisited = [];
    private TeleportSourceItem? _teleportSource;
    private TeleportNetwork.Destination? _teleportedTo;
    private double _teleportTime;

    private enum Phase { Settle, Plan, Unload, Walk, TeleportApproach, TeleportStay, Done }

    public WorldCheck(WorldMap map, PlayerController player, WorldLighting lighting, string[] args)
    {
        Name = "WorldCheck";
        _map = map;
        _player = player;
        _lighting = lighting;
        _args = args;
        _walk = Array.IndexOf(args, "--world-check") >= 0;
        _groundMap = Arg("--ground-map");
        _listCells = Array.IndexOf(args, "--list-cells") >= 0;
        _end = Arg("--walk-to") is { } end && end.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray() is { Length: 3 } e
            ? new N.Vector3(e[0], e[1], e[2]) : DefaultEnd;
        if (!_walk && _groundMap is null && !_listCells)
        {
            SetPhysicsProcess(false);
            return;
        }
        map.CellsChanged += (loaded, unloaded) =>
        {
            _loads += loaded.Count;
            _unloads += unloaded.Count;
            if (_phase != Phase.Settle)
                Log($"cells loaded [{string.Join(", ", loaded.Select(Short))}] unloaded [{string.Join(", ", unloaded.Select(Short))}]");
        };
        map.Teleported += destination => _teleportedTo = destination;
    }

    private string? Arg(string name) => Array.IndexOf(_args, name) is var i and >= 0 && i + 1 < _args.Length ? _args[i + 1] : null;

    private static string Short(string path) => path.Split('/').Last();

    private void Log(string line)
    {
        var p = Unity(_player.GlobalPosition);
        var text = $"{_time,6:F1} s ({p.X:F0}, {p.Y:F0}, {p.Z:F0}): {line}";
        _report.AppendLine("  " + text);
        GD.Print("world-check " + text);
    }

    private static N.Vector3 Unity(Vector3 g) => new(g.X, g.Y, -g.Z);

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_phase == Phase.Settle)
        {
            if (--_settleFrames > 0)
                return;
            // Every cell of the set, for the ground map or the route.
            _map.LoadAll = true;
            _map.Restream();
            _settleFrames = 10;
            _phase = Phase.Plan;
            return;
        }
        if (_phase == Phase.Plan)
        {
            if (--_settleFrames > 0)
                return;
            if (_listCells)
                PrintCells();
            if (_groundMap is not null || !_walk)
            {
                if (_groundMap is not null)
                    PrintGroundMap(_groundMap);
                GetTree().Quit();
                return;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var from = Unity(_player.GlobalPosition);
            var shape = (CapsuleShape3D)_player.GetChildren().OfType<CollisionShape3D>().First().Shape;
            GD.Print($"world-check: planning with the player capsule, radius {shape.Radius} m, height {shape.Height} m");
            var route = RoutePlanner.Plan(_player.GetWorld3D().DirectSpaceState, _player.GetRid(),
                shape, from, _end, 170, 2,
                Mathf.Cos(_player.FloorMaxAngle), _map.MergedSet(_map.CurrentSet).KillVolumes);
            if (route is null)
            {
                Fail($"no route from ({from.X:F0}, {from.Y:F0}, {from.Z:F0}) to ({_end.X:F0}, {_end.Y:F0}, {_end.Z:F0})");
                Finish();
                return;
            }
            _path = route.Where((_, i) => i % WaypointStride == 0).Skip(1).Append(route[^1]).ToList();
            Log($"route planned in {clock.ElapsedMilliseconds} ms: {route.Count} steps, {_path.Count} waypoints, " +
                $"{route.Zip(route.Skip(1), N.Vector3.Distance).Sum():F0} m");
            // Back to cells loading around the player only.
            _map.LoadAll = false;
            _map.Restream();
            _settleFrames = 10;
            _phase = Phase.Unload;
            return;
        }
        if (_phase == Phase.Unload)
        {
            if (--_settleFrames > 0)
                return;
            _zone = _lighting.OutsideZone;
            Log($"start in {_map.Sets.NameOf(_map.CurrentSet)}, {_map.LoadedCells.Count()} cells loaded, ambience zone {_zone}");
            _phase = Phase.Walk;
            _loads = _unloads = 0;
        }

        if (_lighting.OutsideZone != _zone)
        {
            Log($"ambience zone {_zone} -> {_lighting.OutsideZone}");
            _zone = _lighting.OutsideZone;
            _ambienceChanges++;
        }

        switch (_phase)
        {
            case Phase.Walk:
                Walk(delta);
                break;
            case Phase.TeleportApproach:
                Input.ActionPress("move_forward");
                if (_teleportedTo is not null)
                {
                    Input.ActionRelease("move_forward");
                    _teleportTime = 0;
                    _phase = Phase.TeleportStay;
                }
                else if ((_legTime += delta) > 6)
                {
                    Fail($"walked into the teleporter to {_teleportSource!.Destination} but nothing happened");
                    Finish();
                }
                break;
            case Phase.TeleportStay:
                // The far side's own teleporter must not send the player straight back.
                if ((_teleportTime += delta) < 2)
                    return;
                var landed = Unity(_player.GlobalPosition);
                var target = _teleportedTo!.Point.World.Translation;
                var off = N.Vector3.Distance(new N.Vector3(landed.X, 0, landed.Z), new N.Vector3(target.X, 0, target.Z));
                var ok = _teleportedTo.Point.Name == _teleportSource!.Destination && off < 1.5f;
                Log($"teleporter to {_teleportSource.Destination}: landed on {_teleportedTo.Point.Name} at ({target.X:F1}, {target.Y:F1}, {target.Z:F1}) " +
                    $"in {_map.Sets.NameOf(_teleportedTo.RegionSet)}; 2 s later {off:F2} m from it" + (ok ? "" : " -> wrong"));
                if (!ok)
                    Fail("the teleporter didn't land the player on its destination");
                Finish();
                break;
        }
    }

    private void Walk(double delta)
    {
        if (_leg >= _path.Count)
        {
            Input.ActionRelease("move_forward");
            Input.ActionRelease("jump");
            var at = Unity(_player.GlobalPosition);
            var cell = _map.CellsOf(_map.CurrentSet).Where(c => c.Contains(at)).Select(c => Short(c.Path)).ToList();
            var off = N.Vector3.Distance(at, _end);
            Log($"end of the path, {off:F1} m from its end, in [{string.Join(", ", cell)}]; zones walked through: {string.Join(" -> ", _zonesVisited)}");
            if (off > 4)
                Fail("the walk didn't reach its end");
            if (!_zonesVisited.Contains("zoneREEF"))
                Fail("the walk never went through the Dry Reef");
            if (_loads == 0 || _unloads == 0)
                Fail("no cell loaded or unloaded on the way");
            if (!_jetpacked)
                Fail("the jetpack was never used");
            if (_ambienceChanges == 0 && Arg("--walk-to") is null)
                Fail("the ambience never changed");
            StartTeleport();
            return;
        }

        var here = Unity(_player.GlobalPosition);
        if (_map.CellsOf(_map.CurrentSet).FirstOrDefault(c => c.Contains(here)) is { } inCell
            && inCell.Path.Split('/')[0] is var zoneName && (_zonesVisited.Count == 0 || _zonesVisited[^1] != zoneName))
            _zonesVisited.Add(zoneName);
        var waypoint = _path[_leg];
        var (x, z) = (waypoint.X, waypoint.Z);
        var target = new Vector3(x, 0, -z);
        var flat = new Vector3(_player.GlobalPosition.X, 0, _player.GlobalPosition.Z);
        var distance = flat.DistanceTo(target);
        if (distance < 1.5f && Math.Abs(here.Y - waypoint.Y) < 2.5f)
        {
            _leg++;
            _legTime = _stuckTime = 0;
            _lastDistance = float.MaxValue;
            return;
        }
        if ((_legTime += delta) > LegTimeout)
        {
            Fail($"stuck on the way to waypoint {_leg + 1} ({x:F0}, {waypoint.Y:F0}, {z:F0}), {distance:F0} m short");
            _leg = _path.Count;
            return;
        }
        // Face the waypoint and walk; when the player stops closing in, use the jetpack to climb over.
        var to = target - flat;
        _player.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0);
        Input.ActionPress("move_forward");
        Input.ActionPress("sprint");
        _stuckTime = distance > _lastDistance - 0.02f ? _stuckTime + delta : 0;
        _lastDistance = Math.Min(_lastDistance, distance);
        // The planner's climbs need the jetpack: start it when the next waypoint is well above the feet.
        var climb = waypoint.Y - here.Y > 1.2f && distance < 6;
        if ((_stuckTime > 0.4 || climb) && _jetTime <= 0)
        {
            // Long enough to rise above the waypoint at the jetpack's top speed, from a full tank: on the ground, wait for
            // the energy first (Player/PlayerController.cs).
            var rise = Math.Max(waypoint.Y - here.Y, 3) + 2;
            var seconds = Math.Min(0.4 + rise / _player.JetpackMaxRise, _player.MaxEnergy / _player.EnergyUsePerSecond);
            if (_player.IsOnFloor() && _player.Energy < seconds * _player.EnergyUsePerSecond)
            {
                Input.ActionRelease("move_forward");
                return;
            }
            _jetTime = seconds;
            Log($"{(climb ? "climbing" : "blocked")} {distance:F0} m before waypoint {_leg + 1} of {_path.Count}: jetpack for {seconds:F1} s");
        }
        if (_jetTime > 0)
        {
            _jetTime -= delta;
            // Jump, then hold: the jetpack takes over once the jump stops rising (Player/PlayerController.cs).
            Input.ActionPress("jump");
            if (!_player.IsOnFloor())
                _jetpacked = true;
            if (_jetTime <= 0)
                Input.ActionRelease("jump");
        }
    }

    private void StartTeleport()
    {
        Input.ActionRelease("sprint");
        var name = Arg("--teleport-via") ?? DefaultTeleport;
        _teleportSource = _map.Sources.FirstOrDefault(s => s.Destination == name && _map.Network.IsLinkFullyActive(s));
        if (_teleportSource is null)
        {
            Fail($"no open teleporter leads to {name}");
            Finish();
            return;
        }
        // Stand 4 m out along the teleporter's own forward axis (the way a player arriving there faces), turn round and walk in.
        var pad = _teleportSource.Trigger.World.Translation;
        var forward = N.Vector3.Normalize(N.Vector3.TransformNormal(N.Vector3.UnitZ, _teleportSource.Trigger.World) with { Y = 0 });
        var start = pad + forward * 4 + new N.Vector3(0, 0.5f, 0);
        var set = _map.SetAt(pad) ?? _map.CurrentSet;
        var yaw = MathF.Atan2(-forward.X, -forward.Z) * 180 / MathF.PI;
        _map.Start(_player, start, set, yaw);
        _teleportedTo = null;
        _legTime = 0;
        Log($"walking into the teleporter at {_teleportSource.Path} (to {name})");
        _phase = Phase.TeleportApproach;
    }

    private void Fail(string why)
    {
        _passed = false;
        Log("FAIL: " + why);
    }

    private void Finish()
    {
        Input.ActionRelease("move_forward");
        Input.ActionRelease("jump");
        Input.ActionRelease("sprint");
        _phase = Phase.Done;
        GD.Print($"world-check: {_loads} cell loads, {_unloads} unloads, {_ambienceChanges} ambience changes, {_map.CellsBuilt} cells built in " +
                 $"{_map.BuildMilliseconds} ms; " +
                 $"memory {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB private, " +
                 $"{GC.GetTotalMemory(false) / 1048576} MB managed -> {(_passed ? "PASS" : "FAIL")}");
        GetTree().Quit(_passed ? 0 : 1);
    }

    // Every cell of the live set (ambience zone, box min and max) and every teleport destination and source, in Unity terms.
    private void PrintCells()
    {
        foreach (var c in _map.CellsOf(_map.CurrentSet))
        {
            var (lo, hi) = (c.Center - c.Extent, c.Center + c.Extent);
            GD.Print($"cell {c.Path} ambience {c.AmbianceZone} box ({lo.X:F0}, {lo.Y:F0}, {lo.Z:F0}) .. ({hi.X:F0}, {hi.Y:F0}, {hi.Z:F0})");
        }
        foreach (var (p, set) in _map.Destinations)
            GD.Print($"destination {p.Name} set {set} at ({p.World.Translation.X:F1}, {p.World.Translation.Y:F1}, {p.World.Translation.Z:F1}) {p.Path}");
        foreach (var s in _map.Sources)
            GD.Print($"source to {s.Destination} at ({s.Trigger.World.Translation.X:F1}, {s.Trigger.World.Translation.Y:F1}, {s.Trigger.World.Translation.Z:F1}) " +
                     $"open {_map.Network.IsLinkFullyActive(s)} {s.Path}");
    }

    // Ground heights on a grid (rays straight down), with every cell of the live set loaded: '.' no ground, '~' kill
    // volume below, digits the height band (10 m per step from the lowest), for picking a route by eye.
    private void PrintGroundMap(string spec)
    {
        var v = spec.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var (x0, z0, x1, z1, step) = (v[0], v[1], v[2], v[3], v[4]);
        var top = v.Length > 5 ? v[5] : 1400; // start the rays below roofs to see cave and tunnel floors
        var space = _player.GetWorld3D().DirectSpaceState;
        var rows = new List<string>();
        for (var z = z1; z >= z0; z -= step)
        {
            var row = new StringBuilder($"{z,6:F0} ");
            for (var x = x0; x <= x1; x += step)
            {
                var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, top, -z), new Vector3(x, -200, -z));
                query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
                var hit = space.IntersectRay(query);
                if (hit.Count == 0)
                {
                    row.Append('.');
                    continue;
                }
                var y = ((Vector3)hit["position"]).Y;
                var n = ((Vector3)hit["normal"]).Y;
                row.Append(n < 0.7f ? '#' : (char)('0' + Math.Clamp((int)Math.Floor((y + 20) / 10), 0, 9)));
            }
            rows.Add(row.ToString());
        }
        GD.Print($"ground-map x {x0}..{x1}, z {z1} (top) .. {z0}, every {step} m; digit = (height + 20) / 10, # = too steep, . = none");
        foreach (var row in rows)
            GD.Print(row);
    }
}
