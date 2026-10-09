using System;
using System.Linq;
using Godot;
using OpenRanch.Formats.Unity.Managed;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

// Feral slimes and slimes that go for the player (docs/behavior/feral-slimes.md). Feral largos and
// tarrs share the player-chasing pieces: a tarr's prefab has them switched on from the start, a
// largo's only while it is feral.

/// <summary>
/// The feral clock (<c>SlimeFeral</c>): turns the slime feral at full agitation when its prefab says so,
/// and poofs it once its feral lifetime is over, unless it is on The Ranch or in the Wilds.
/// </summary>
public sealed class FeralClock : SlimeBehaviour
{
    public override void Tick(float delta)
    {
        if (Slime.Feral is not { } feral)
            return;
        var sheltered = Catalog.OnRanchOrWilds?.Invoke(Slime.GlobalPosition) ?? true;
        if (feral.Update(Catalog.Clock.TotalHours, sheltered))
            Slime.Consume(); // openranch draws no poof effect yet
    }
}

/// <summary>
/// Going for the player (<c>GotoPlayer</c>): when switched on (a tarr always, a largo while feral), the
/// player within the search radius matters drive² × 0.95, the drive from the component's emotion,
/// floor and extra; it then chases the player like food. Gives up after the attempt time, adding
/// agitation, and ignores the player for the give-up time.
/// </summary>
public sealed class GotoPlayer : SlimeBehaviour
{
    private enum Mode { Available, Attempting, GaveUp }

    private readonly bool _always;
    private readonly float _maxJump, _attemptSeconds, _giveUpSeconds, _extraDrive, _minDrive, _maxSearch, _minDist;
    private readonly float _facingSpeed, _facingStability, _pursuitSpeed;
    private readonly int _driver;
    private Mode _mode;
    private float _modeEnds = float.PositiveInfinity, _drive, _started;

    public GotoPlayer(SerializedObject data)
    {
        float F(string field, float fallback) => data[field] is float v ? v : fallback;
        _always = data["shouldGotoPlayer"] is true;
        _maxJump = F("maxJump", 0);
        _attemptSeconds = F("attemptTime", 0);
        _giveUpSeconds = F("giveUpTime", 0);
        _extraDrive = F("extraDrive", 0);
        _minDrive = F("minDrive", 0);
        _maxSearch = F("maxSearchRad", 0);
        _minDist = F("minDist", 0);
        _facingSpeed = F("facingSpeed", 1);
        _facingStability = F("facingStability", 1);
        _pursuitSpeed = F("pursuitSpeedFactor", 1);
        _driver = data["driver"] is int d ? d : 0;
    }

    /// <summary>Whether it goes for the player now: always for a tarr, while feral for a largo.</summary>
    public bool On => _always || Slime.Sim.IsFeral;

    // SlimeEmotions' order: hunger, agitation, fear. openranch has no fear yet (UNVERIFIED.md).
    private float Feeling => _driver switch { 0 => Slime.Sim.Hunger, 1 => Slime.Sim.Agitation, _ => 0f };

    public override float Relevancy(bool grounded)
    {
        if (!On || Catalog.Player is not { } player || !GodotObject.IsInstanceValid(player))
            return 0f;
        if (Slime.Age >= _modeEnds)
        {
            if (_mode == Mode.Attempting)
            {
                _mode = Mode.GaveUp;
                _modeEnds = Slime.Age + _giveUpSeconds;
                Slime.Sim.Agitation = Math.Min(1f, Slime.Sim.Agitation + SlimeMotion.AgitationPerGiveUp);
            }
            else if (_mode == Mode.GaveUp)
            {
                _mode = Mode.Available;
                _modeEnds = float.PositiveInfinity;
            }
        }
        if (_mode == Mode.GaveUp)
            return 0f;
        _drive = Math.Max(0f, Math.Max(_minDrive, Feeling) + _extraDrive);
        var d2 = Slime.GlobalPosition.DistanceSquaredTo(player.GlobalPosition);
        // FindConsumable: it only counts when drive / distance² beats 1 / search radius².
        if (d2 < _minDist * _minDist || _drive / Math.Max(d2, 1e-4f) <= 1f / (_maxSearch * _maxSearch))
            return 0f;
        return PlayerAttack.GotoRelevancy(_drive);
    }

    public override void Selected()
    {
        _mode = Mode.Attempting;
        _modeEnds = Slime.Age + _attemptSeconds;
        _started = Slime.Age;
    }

    public override void Action(float delta)
    {
        if (!Slime.Grounded || Catalog.Player is not { } player || Slime.Behaviour<AttackPlayer>() is { Chomping: true })
            return;
        Slime.Pursue(player.GlobalPosition, player, SlimeMotion.FoodJumpStrength(_drive, _maxJump), _pursuitSpeed, _facingSpeed, _facingStability, _started);
    }
}

/// <summary>
/// Biting the player (<c>AttackPlayer</c> with <c>Chomper</c>): when switched on (a tarr always, a
/// largo while feral) and touching the player, it turns to the player and bites; the bite takes
/// <c>damagePerAttack</c> health. The next bite can come <c>timePerAttack</c> seconds after a bite ends.
/// </summary>
public sealed class AttackPlayer : SlimeBehaviour
{
    private readonly bool _always;
    private readonly int _damage;
    private readonly float _secondsBetween;
    private float _nextChomp;

    public AttackPlayer(SerializedObject data, SerializedObject? chomper)
    {
        _always = data["shouldAttackPlayer"] is true;
        _damage = data["damagePerAttack"] is int d ? d : 0;
        _secondsBetween = chomper?["timePerAttack"] is float t ? t : 0f;
    }

    public bool On => _always || Slime.Sim.IsFeral;
    public bool Chomping { get; private set; }
    /// <summary>Bites that landed on the player.</summary>
    public int Bites { get; private set; }

    public override void Tick(float delta)
    {
        if (!On || Chomping || Slime.Age < _nextChomp || Slime.CaughtBy is not null || Catalog.Player is not { } player || !GodotObject.IsInstanceValid(player))
            return;
        // The original bites on a collision with the player, and again while the player keeps walking into it.
        if (!Slime.GetCollidingBodies().Contains(player))
            return;
        Chomping = true;
        var flat = (player.GlobalPosition - Slime.GlobalPosition) with { Y = 0 };
        if (flat.LengthSquared() > 1e-4f)
            Slime.TurnToward(flat.Normalized(), 5f, 1f);
        Slime.GetTree().CreateTimer(SlimeMotion.BiteSeconds, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            Chomping = false;
            if (!GodotObject.IsInstanceValid(Slime) || Slime.Consumed)
                return;
            _nextChomp = Slime.Age + _secondsBetween;
            Bites++;
            Catalog.PlayerVitals.Damage(_damage, Slime.Id);
        };
    }
}

/// <summary>
/// A feral largo's leap and stomp (<c>FeralSlimeButtstomp</c>): standing, feral and 5 to 20 m from the
/// player, it leaps at a point two metres in front of the player, drops straight down once it stops
/// rising, and explodes where it lands with the component's power, radius and damage. Five seconds
/// before the next.
/// </summary>
public sealed class FeralStompBehaviour : SlimeBehaviour
{
    private enum Mode { Waiting, Midair, WaitForImpact, Stomping, Landed }

    private readonly float _power, _radius, _minDamage, _maxDamage;
    private Mode _mode;
    private float _nextStomp;

    public FeralStompBehaviour(SerializedObject data)
    {
        float F(string field) => data[field] is float v ? v : 0f;
        (_power, _radius, _minDamage, _maxDamage) = (F("explodePower"), F("explodeRadius"), F("minPlayerDamage"), F("maxPlayerDamage"));
    }

    /// <summary>Where the stomp is (for check reports).</summary>
    public string Phase => _mode.ToString();
    /// <summary>Stomps done (for checks).</summary>
    public int Stomps { get; private set; }
    public event Action<Explosions.Result>? Stomped;

    public override float Relevancy(bool grounded)
    {
        if (!grounded || !Slime.Sim.IsFeral || Slime.Age < _nextStomp || Catalog.Player is not { } player || !GodotObject.IsInstanceValid(player))
            return 0f;
        if (!FeralStomp.InRange(Slime.GlobalPosition.DistanceTo(player.GlobalPosition)))
            return 0f;
        return FeralStomp.MinRelevancy + (float)Slime.Random.NextDouble() * (FeralStomp.MaxRelevancy - FeralStomp.MinRelevancy);
    }

    public override bool CanRethink => _mode is Mode.Waiting or Mode.Landed;
    public override void Selected() => _mode = Mode.Waiting;

    public override void Action(float delta)
    {
        switch (_mode)
        {
            case Mode.Waiting:
                Leap();
                break;
            case Mode.Midair:
                if (Slime.LinearVelocity.Y <= 0)
                {
                    // Drops straight down at the speed it had.
                    Slime.LinearVelocity = new Vector3(0, -Slime.LinearVelocity.Length(), 0);
                    _mode = Mode.WaitForImpact;
                }
                break;
            case Mode.WaitForImpact:
                var below = Slime.Ray(Slime.GlobalPosition, Slime.GlobalPosition + Vector3.Down * (Slime.Radius + 0.1f), Actor.WorldLayer | Actor.ActorLayer);
                if (below.Count > 0 && Slime.GlobalPosition.Y - ((Vector3)below["position"]).Y >= FeralStomp.UnderneathThreshold)
                    _mode = Mode.Stomping;
                break;
            case Mode.Stomping:
                var result = Explosions.Explode(Catalog, Slime, Slime.GlobalPosition, _radius, _power, _minDamage, _maxDamage, Slime.Id);
                _mode = Mode.Landed;
                _nextStomp = Slime.Age + FeralStomp.ResetSeconds;
                Stomps++;
                Stomped?.Invoke(result);
                break;
        }
    }

    private void Leap()
    {
        if (Catalog.Player is not { } player)
            return;
        // A point two metres in front of the player (the player faces -Z in Godot).
        var aim = player.GlobalPosition - player.GlobalBasis.Z.Normalized() * FeralStomp.AimAheadOfPlayer;
        var to = aim - Slime.GlobalPosition;
        var dir = to.Normalized();
        Slime.TurnToward(dir, 1f, 5f);
        var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        var speed = FeralStomp.LeapSpeed(to.Length(), gravity);
        Slime.LinearVelocity += (dir + Vector3.Up).Normalized() * speed;
        _mode = Mode.Midair;
    }
}
