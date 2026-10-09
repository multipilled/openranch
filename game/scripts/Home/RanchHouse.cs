using System;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;
using OpenRanch.Ranch;

namespace OpenRanch.Game.Home;

/// <summary>
/// The ranch house's door (docs/behavior/day-cycle.md, "Sleeping"): the world scene puts a UI activator
/// whose screen is the ranch house's (RanchHouseUI) on the house's "interactTrigger" box. Looking at it
/// from within the player rig's reach (UIDetector.interactDistance) and pressing the interact key
/// opens the ranch house screen (<see cref="Screen"/>, game/scripts/RanchEconomy/RanchHouseScreen.cs),
/// whose sleep button sleeps: the world clock runs at the sleeping pace (ffSecsPerGameDay) to the next
/// 6:00 and the player is held still until then.
/// Command-line option after "--": --sleep-check sleeps from the current hour (use --hour 20) through
/// the door and the screen's sleep button, checks the wake-up time and the pace, prints a report and quits.
/// </summary>
/// <summary>The ranch house screen the door opens.</summary>
public interface IHouseScreen
{
    bool IsOpen { get; }
    void Open();
    /// <summary>Presses the screen's sleep button.</summary>
    void PressSleep();
}

public partial class RanchHouse : Node3D
{
    /// <summary>The input action that uses what the player looks at (openranch's key: E; UNVERIFIED.md, "Interact key").</summary>
    public const string InteractAction = "interact";
    private const Key InteractKey = Key.E;

    // A physics layer of its own for things the player can use, so the door stops rays, not bodies.
    public const uint InteractLayer = 1u << 20;

    private readonly PlayerController _player;
    private readonly WorldTime _clock;
    private readonly Slimes.M2World? _m2;
    private readonly float _reach;
    private readonly Area3D _door;
    private readonly SleepCheck? _check;

    private RanchHouse(PlayerController player, WorldTime clock, Slimes.M2World? m2, Transform3D door, Vector3 center, Vector3 size, float reach, bool check)
    {
        Name = "RanchHouse";
        // Waking is noticed, and the check runs, while the open ranch house screen pauses the game.
        ProcessMode = ProcessModeEnum.Always;
        _player = player;
        _clock = clock;
        _m2 = m2;
        _reach = reach;
        _door = new Area3D { Name = "Door", Transform = door, CollisionLayer = InteractLayer, CollisionMask = 0, Monitoring = false, Monitorable = true };
        _door.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
        AddChild(_door);
        if (check)
            _check = new SleepCheck(this);
    }

    /// <summary>The screen the door opens; a plain one (no save button) is made on first use when none was given.</summary>
    public IHouseScreen? Screen { get; set; }

    /// <summary>The world clock's reading now.</summary>
    public WorldClock Clock => _clock.Clock;

    /// <summary>Whether the world clock is running at the sleeping pace.</summary>
    public bool Sleeping => _clock.Cycle.IsFastForwarding;

    /// <summary>Raised when the player has slept: from and to which world time.</summary>
    public event Action<double, double>? Slept;

    /// <summary>
    /// Finds the ranch house's door in the zone, or returns null (zones without one). <paramref name="scripts"/>
    /// is an open reader of the install's scripts.
    /// </summary>
    public static RanchHouse? Create(GameScripts scripts, string zoneName, PlayerController player, WorldTime clock, Slimes.M2World? m2, string[] args)
    {
        var scene = scripts.Assets.File("level3");
        if (scene is null)
            return null;
        // The door is the UI activator whose screen prefab is the ranch house's (static analysis:
        // UIActivator.uiPrefab, RanchHouseUI on the prefab).
        var door = SceneScripts.Find(scripts, scene, zoneName, "UIActivator").FirstOrDefault(s =>
            s.Data["uiPrefab"] is PPtr p && scripts.Assets.Resolve(scene, p) is { } r && scripts.Assets.Read(r, GameObjectData.Read).Name == "RanchHouseUI");
        if (door?.Colliders.FirstOrDefault(c => c.Shape == ColliderShape.Box) is not { } box)
            return null;
        // How far the player reaches: the rig's UIDetector (static analysis: a ray from the middle of the view, this long).
        var reach = scripts.OfClass("UIDetector").Select(d => d.Data.Data?["interactDistance"]).OfType<float>().FirstOrDefault(d => d > 0);
        if (reach <= 0)
            return null;
        var world = UnityConvert.Transform(door.World);
        var scale = world.Basis.Scale.Abs();
        var center = UnityConvert.Position(box.Center) * scale;
        // Unity takes a box collider's size as its absolute value.
        var size = UnityConvert.Position(box.Size).Abs() * scale;
        var house = new RanchHouse(player, clock, m2, new Transform3D(world.Basis.Orthonormalized(), world.Origin), center, size, reach,
            Array.IndexOf(args, "--sleep-check") >= 0);
        GD.Print($"Ranch house door at {UnityConvert.Position(door.World.Translation)}, reach {reach} m");
        return house;
    }

    public override void _Ready()
    {
        if (InputMap.HasAction(InteractAction))
            return;
        InputMap.AddAction(InteractAction);
        InputMap.ActionAddEvent(InteractAction, new InputEventKey { PhysicalKeycode = InteractKey });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // The original acts when the interact button is released (UIDetector).
        if (e.IsActionReleased(InteractAction) && Screen?.IsOpen != true && !GetTree().Paused)
            Interact();
    }

    /// <summary>Whether the middle of the player's view meets the door within reach, with nothing in between.</summary>
    public bool LookingAtDoor()
    {
        var camera = _player.Camera.GlobalTransform;
        var from = camera.Origin;
        var to = from - camera.Basis.Z.Normalized() * _reach;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Slimes.Actor.WorldLayer | InteractLayer, [_player.GetRid()]);
        query.CollideWithAreas = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["collider"].AsGodotObject() == _door;
    }

    /// <summary>Uses the door if the player looks at it: opens the ranch house screen. Returns whether it did.</summary>
    public bool Interact()
    {
        if (Sleeping || !LookingAtDoor())
            return false;
        if (Screen is null)
        {
            var screen = new RanchEconomy.RanchHouseScreen(this, null);
            AddChild(screen);
            Screen = screen;
        }
        Screen.Open();
        return true;
    }

    /// <summary>
    /// Sleeps: the clock runs fast to the next morning (DayCycle.Sleep) and the player is held still
    /// until it gets there (static analysis: the ranch house's sleep button locks the player until the
    /// next dawn while the time director fast-forwards).
    /// </summary>
    public void Sleep()
    {
        var from = _clock.Ranch.WorldTime;
        _clock.Cycle.Sleep(_clock.Ranch);
        _player.SetPhysicsProcess(false);
        _sleptFrom = from;
        GD.Print($"Sleeping from {_clock.Clock} until {new WorldClock(_clock.Cycle.FastForwardUntil!.Value)}");
    }

    private double? _sleptFrom;

    public override void _Process(double delta)
    {
        if (_sleptFrom is { } from && !Sleeping)
        {
            _sleptFrom = null;
            _player.SetPhysicsProcess(true);
            GD.Print($"Woke up at {_clock.Clock}");
            Slept?.Invoke(from, _clock.Ranch.WorldTime);
        }
        _check?.Step(delta);
    }

    // The headless check: aims the player at the door, uses it and watches the clock run to morning.
    private sealed class SleepCheck(RanchHouse house)
    {
        private const double SettleSeconds = 1;
        private double _time, _sleepStartedAt, _maxSpeed;
        private double _startWorld, _expectedWake;
        private bool _started, _frozen = true;
        private readonly StringBuilder _report = new("sleep-check:\n");
        private bool _passed = true;

        private void Check(bool ok, string what)
        {
            _passed &= ok;
            _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        }

        public void Step(double delta)
        {
            _time += delta;
            var clock = house._clock;
            if (!_started)
            {
                if (_time < SettleSeconds)
                    return;
                _started = true;
                _startWorld = clock.Ranch.WorldTime;
                _expectedWake = clock.Clock.WakeTime;
                Check(AimAndUse(), $"the door: the player looks at it from {house._reach * 0.75f:F2} m and uses it (reach {house._reach} m)");
                if (!house.Sleeping)
                {
                    Finish();
                    return;
                }
                _sleepStartedAt = _time;
                return;
            }
            if (house.Sleeping)
            {
                _frozen &= !house._player.IsPhysicsProcessing();
                if (house._m2 is { } m2)
                    _maxSpeed = Math.Max(_maxSpeed, m2.Clock.Speed);
                return;
            }

            var slept = _time - _sleepStartedAt;
            var length = clock.Cycle.Length;
            var expected = (_expectedWake - _startWorld) / WorldClock.SecondsPerDay * length.FastForwardRealSecondsPerDay / clock.Speed;
            var start = new WorldClock(_startWorld);
            Check(clock.Ranch.WorldTime == _expectedWake && Math.Abs(clock.Clock.Hour - WorldClock.DawnHour) < 1e-9
                  && clock.Clock.Day == start.Day + (start.Hour >= WorldClock.DawnHour ? 1 : 0),
                $"woke at {clock.Clock} (world time {clock.Ranch.WorldTime:F0}), slept from {start} ({_startWorld:F0}); the next {WorldClock.DawnHour}:00 is {_expectedWake:F0}");
            Check(Math.Abs(slept - expected) <= 3 * delta + 0.01 * expected,
                $"pace: {slept:F3} real s asleep, {expected:F3} s at {length.FastForwardRealSecondsPerDay} real s per game day (the install's ffSecsPerGameDay)");
            Check(_frozen, "the player was held still while sleeping");
            if (house._m2 is { } m)
            {
                var wanted = clock.Speed * m.Clock.SecondsPerGameDay / length.FastForwardRealSecondsPerDay;
                Check(Math.Abs(_maxSpeed - wanted) < 1e-6 * wanted,
                    $"slimes and plorts slept at the same pace: game clock {_maxSpeed:F1}x its normal pace, the world clock {wanted:F1}x");
            }
            Finish();
        }

        // Stands the player in front of the door, a little inside reach, looking at its middle; tries both faces.
        private bool AimAndUse()
        {
            var player = house._player;
            player.SetPhysicsProcess(false);
            var door = house._door.GlobalTransform;
            var middle = door * ((CollisionShape3D)house._door.GetChild(0)).Position;
            var eye = player.Camera.Position.Y;
            foreach (var side in new[] { 1f, -1f })
            {
                var normal = (door.Basis.Z * side).Normalized();
                var cameraAt = middle + normal * (house._reach * 0.75f);
                player.Position = cameraAt - Vector3.Up * eye;
                var dir = (middle - cameraAt).Normalized();
                player.Look(Mathf.RadToDeg(Mathf.Atan2(-dir.X, -dir.Z)), Mathf.RadToDeg(Mathf.Asin(dir.Y)));
                if (house.Interact())
                {
                    house.Screen!.PressSleep();
                    return true;
                }
            }
            player.SetPhysicsProcess(true);
            return false;
        }

        private void Finish()
        {
            _report.Append(_passed ? "sleep-check PASS" : "sleep-check FAIL");
            GD.Print(_report.ToString());
            house.GetTree().Quit(_passed ? 0 : 1);
            house.SetProcess(false);
        }
    }
}
