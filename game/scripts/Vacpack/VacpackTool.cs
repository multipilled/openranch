using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Game.Slimes;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Vacpack;

/// <summary>
/// The vacpack on the player's camera: hold the vac button to reel in items inside the suction cone
/// and store them in <see cref="Simulation.Vacpack"/>, hold the shoot button to fire the selected
/// slot. Tuning and the cone's shape come from the player rig's WeaponVacuum component
/// (docs/behavior/vacpack.md). Keys: right mouse vacuums, left mouse shoots, 1-4 or the wheel pick a slot.
/// </summary>
public partial class VacpackTool : Node3D
{
    // Vacuumable: a captured item shrinks to a tenth of its size over 0.2 s; an item let go because
    // the vacpack is full can't be caught again for a second.
    private const float CaptureShrinkSeconds = 0.2f, CaptureShrinkScale = 0.1f, ReleaseDelaySeconds = 1f;
    // WeaponVacuum: a shot item grows from a fifth of its size to full size in a tenth of a second.
    private const float ShotStartScale = 0.2f, ShotGrowSeconds = 0.1f;

    private readonly VacuumTuning _tuning;
    private readonly ItemCatalog _catalog;
    private readonly CharacterBody3D _player;
    private readonly (Vector3 A, Vector3 B, float Radius)[] _cone;
    private readonly HashSet<Actor> _caught = new();
    private readonly Dictionary<Actor, float> _releasedAt = new();
    private float _time, _nextShot;

    public VacpackTool(VacuumTuning tuning, ItemCatalog catalog, CharacterBody3D player, Simulation.Vacpack pack)
    {
        Name = "Vacpack";
        _tuning = tuning;
        _catalog = catalog;
        _player = player;
        Pack = pack;
        // The nozzle and cone are stored in the camera's space (Unity coordinates).
        Transform = UnityConvert.Transform(tuning.NozzleToCamera);
        _cone = tuning.Cone.Select(c => (UnityConvert.Position(c.A), UnityConvert.Position(c.B), c.Radius)).ToArray();
    }

    public Simulation.Vacpack Pack { get; }
    /// <summary>When true, <see cref="VacHeld"/> and <see cref="ShootHeld"/> are set by a script instead of the mouse.</summary>
    public bool ScriptControlled { get; set; }
    public bool VacHeld { get; set; }
    public bool ShootHeld { get; set; }

    /// <summary>Raised when an item joins the vacpack.</summary>
    public event Action<string>? Captured;
    /// <summary>Raised when an item is shot out.</summary>
    public event Action<Actor>? Shot;

    public override void _Ready()
    {
        EnsureMouseAction("vac", MouseButton.Right);
        EnsureMouseAction("shoot", MouseButton.Left);
        for (var i = 1; i <= Simulation.Vacpack.StartingSlots; i++)
            EnsureKeyAction($"slot_{i}", Key.Key0 + i);
    }

    private static void EnsureMouseAction(string name, MouseButton button)
    {
        if (InputMap.HasAction(name))
            return;
        InputMap.AddAction(name);
        InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void EnsureKeyAction(string name, Key key)
    {
        if (InputMap.HasAction(name))
            return;
        InputMap.AddAction(name);
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (ScriptControlled)
            return;
        for (var i = 1; i <= Simulation.Vacpack.StartingSlots; i++)
            if (e.IsActionPressed($"slot_{i}"))
                Pack.Select(i - 1);
        if (e is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelDown)
                Pack.SelectNext();
            else if (wheel.ButtonIndex == MouseButton.WheelUp)
                Pack.SelectPrevious();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        if (!ScriptControlled)
        {
            var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
            VacHeld = captured && Input.IsActionPressed("vac");
            ShootHeld = captured && Input.IsActionPressed("shoot");
        }

        if (VacHeld)
        {
            CatchItemsInCone();
            ReelIn();
        }
        else
        {
            ReleaseAll();
        }

        if (ShootHeld)
        {
            if (_time >= _nextShot)
            {
                var moods = Pack[Pack.SelectedSlot]?.Moods;
                if (Pack.TakeSelected() is { } id)
                    ShootOut(id, moods);
                _nextShot = _time + _tuning.ShootCooldown;
            }
        }
        else
        {
            _nextShot = Math.Min(_nextShot, _time); // the first shot of a press goes at once
        }
    }

    /// <summary>Whether a world position is inside the suction cone and within reach.</summary>
    public bool InCone(Vector3 worldPosition)
    {
        var camera = GetParent<Node3D>();
        var local = camera.GlobalTransform.AffineInverse() * worldPosition;
        if (worldPosition.DistanceTo(GlobalPosition) > _tuning.MaxVacDist)
            return false;
        foreach (var (a, b, r) in _cone)
        {
            var ab = b - a;
            var t = ab.LengthSquared() > 0 ? Mathf.Clamp((local - a).Dot(ab) / ab.LengthSquared(), 0, 1) : 0;
            if (local.DistanceSquaredTo(a + ab * t) <= r * r)
                return true;
        }
        return false;
    }

    private void CatchItemsInCone()
    {
        var space = GetWorld3D().DirectSpaceState;
        foreach (var item in _catalog.Live)
        {
            if (!item.Vacuumable || item.CaughtBy is not null || item.Launched
                || _releasedAt.TryGetValue(item, out var at) && _time - at < ReleaseDelaySeconds)
                continue;
            if (!InCone(item.GlobalPosition))
                continue;
            // The nozzle must see the item: nothing solid of the world in between.
            var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, item.GlobalPosition, Actor.WorldLayer,
                new Godot.Collections.Array<Rid> { _player.GetRid() });
            if (space.IntersectRay(query).Count > 0)
                continue;
            item.CaughtBy = this;
            item.GravityScale = 0;
            item.AddCollisionExceptionWith(_player);
            _caught.Add(item);
        }
    }

    // Caught items move toward the nozzle, faster when close; within capture distance they shrink
    // into the vacpack, or are let go when there's no room.
    private void ReelIn()
    {
        foreach (var item in _caught.ToList())
        {
            if (!IsInstanceValid(item) || item.Consumed)
            {
                _caught.Remove(item);
                continue;
            }
            var toNozzle = GlobalPosition - item.GlobalPosition;
            var distance = toNozzle.Length();
            if (distance <= _tuning.CaptureDist)
            {
                _caught.Remove(item);
                if (!Pack.CanAdd(item.Id))
                {
                    Release(item);
                    continue;
                }
                var id = item.Id;
                var moods = item is SlimeActor slime ? Moods(slime) : null;
                item.Reserve();
                item.ShrinkTo(CaptureShrinkScale, CaptureShrinkSeconds, () =>
                {
                    if (Pack.TryAdd(id, moods))
                        Captured?.Invoke(id);
                    item.Finish();
                });
                continue;
            }
            var speed = Mathf.Lerp(_tuning.MaxJointSpeed, _tuning.MinJointSpeed, Mathf.Clamp(distance / _tuning.MaxVacDist, 0, 1));
            item.LinearVelocity = toNozzle / distance * speed + _player.Velocity;
        }
    }

    private void ReleaseAll()
    {
        foreach (var item in _caught.ToList())
            if (IsInstanceValid(item))
                Release(item);
        _caught.Clear();
    }

    private void Release(Actor item)
    {
        _caught.Remove(item);
        item.CaughtBy = null;
        item.GravityScale = 1;
        item.RemoveCollisionExceptionWith(_player);
        _releasedAt[item] = _time;
    }

    // A shot item appears just ahead of the nozzle (further out when aiming down) and flies along the
    // nozzle's direction at the eject speed plus the player's own speed.
    /// <summary>The moods a slime takes into the vacpack, by the game's emotion names (only hunger and agitation are modelled).</summary>
    public static Dictionary<string, float> Moods(SlimeActor slime) => new() { [Hunger] = slime.Sim.Hunger, [Agitation] = slime.Sim.Agitation };

    private const string Hunger = "HUNGER", Agitation = "AGITATION";

    // A slime shot out takes the slot's averaged moods (static analysis of WeaponVacuum's shooting).
    private void ShootOut(string id, IReadOnlyDictionary<string, float>? moods)
    {
        var dir = GlobalBasis.Y.Normalized();
        var radius = ItemCatalog.Radius(_catalog.Prefabs.Get(id));
        var start = GlobalPosition + dir * (radius * 0.2f + (dir.Y >= 0 ? 0 : -0.5f * dir.Y));
        var item = _catalog.Spawn(id, start);
        // It turns to face the player (slimes' fronts are Godot's -Z, which LookAt points at the target).
        var facing = _player.GlobalPosition with { Y = item.GlobalPosition.Y };
        if (facing.DistanceSquaredTo(item.GlobalPosition) > 0.01f)
            item.LookAt(facing, Vector3.Up);
        item.LinearVelocity = dir * _tuning.EjectSpeed + _player.Velocity;
        item.GrowFrom(ShotStartScale, ShotGrowSeconds);
        item.Launch(_player);
        if (item is SlimeActor slime && moods is not null)
        {
            if (moods.TryGetValue(Hunger, out var hunger))
                slime.Sim.Hunger = Mathf.Clamp(hunger, 0, 1);
            if (moods.TryGetValue(Agitation, out var agitation))
                slime.Sim.Agitation = Mathf.Clamp(agitation, 0, 1);
        }
        Shot?.Invoke(item);
    }
}
