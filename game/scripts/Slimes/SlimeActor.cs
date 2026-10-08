using System;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// A slime in the world: a ball that stays upright, gets hungry over game time, goes for food it
/// wants, eats what it touches and pops out plorts. The rules are in docs/behavior/slimes.md ("In the
/// world"); tuning comes from the prefab's components, code-only numbers from <see cref="SlimeMotion"/>.
/// </summary>
public partial class SlimeActor : Actor
{
    private enum Activity { None, Wander, Food }

    private readonly ItemCatalog _catalog;
    private readonly Random _random = new();

    // Tuning from the prefab: GotoConsumable (or a tabby's StalkConsumable) and SlimeRandomMove.
    private readonly float _searchRadius, _maxJump, _pursuitSpeed, _facingSpeed, _facingStability, _attemptSeconds, _giveUpSeconds;
    private readonly float _verticalFactor, _scootSpeed;
    // How far down the slime looks for ground: its collider's half height times 1.3 (SlimeSubbehaviourPlexer).
    private readonly float _groundDistance;

    private float _age, _nextRethink, _nextGroundCheck, _nextJump, _nextBlockCheck, _nextMood;
    private float _attemptEnds, _ignoreFoodUntil, _foodStarted;
    private bool _grounded, _blocked, _busy;
    private Activity _activity;
    private Actor? _target;
    private float _targetDrive;
    private WanderMood _mood = WanderMood.Rest;
    private Vector3 _heading = Vector3.Forward;

    public SlimeActor(ItemCatalog catalog, SlimeSpecies species, ItemPrefab prefab)
    {
        _catalog = catalog;
        Sim = new Slime(species);
        var seek = prefab.RootScript("GotoConsumable") ?? prefab.RootScript("StalkConsumable");
        float F(string field, float fallback) => seek?[field] is float v ? v : fallback;
        _searchRadius = F("maxSearchRad", 0);
        _maxJump = F("maxJump", SlimeMotion.DefaultFoodJump);
        _pursuitSpeed = F("pursuitSpeedFactor", 1);
        _facingSpeed = F("facingSpeed", 1);
        _facingStability = F("facingStability", 1);
        _attemptSeconds = F("attemptTime", SlimeMotion.DefaultAttemptSeconds);
        _giveUpSeconds = F("giveUpTime", SlimeMotion.DefaultGiveUpSeconds);
        _verticalFactor = prefab.RootFloat("SlimeRandomMove", "verticalFactor", 1);
        _scootSpeed = prefab.RootFloat("SlimeRandomMove", "scootSpeedFactor", 1);
        var halfHeight = ItemCatalog.Radius(prefab);
        _groundDistance = halfHeight * 1.3f;
    }

    /// <summary>The slime's appetite and mood.</summary>
    public Slime Sim { get; }

    /// <summary>Raised when the slime has eaten something (the food's id).</summary>
    public event Action<SlimeActor, string>? Ate;
    /// <summary>Raised for each item the slime produces after digesting.</summary>
    public event Action<SlimeActor, Actor>? Produced;

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        var dt = (float)delta;
        _age += dt;
        Sim.Advance(_catalog.Clock.HoursFor(dt));
        if (Consumed || CaughtBy is not null)
            return;

        foreach (var body in GetCollidingBodies())
            if (body is Actor food && TryEat(food))
                break;

        if (_age < SlimeMotion.StartDelaySeconds || _busy)
            return;
        if (_age >= _nextGroundCheck)
        {
            _nextGroundCheck = _age + SlimeMotion.GroundCheckSeconds;
            _grounded = Ray(GlobalPosition, GlobalPosition + Vector3.Down * _groundDistance, WorldLayer | ActorLayer).Count > 0;
        }
        if (_age >= _nextRethink)
        {
            _nextRethink = _age + SlimeMotion.RethinkSeconds;
            Rethink();
        }
        if (!_grounded)
            return;
        if (_activity == Activity.Food)
            GoForFood();
        else if (_activity == Activity.Wander)
            Wander();
    }

    // Picks whichever activity matters most: going for the best food in reach, or wandering.
    private void Rethink()
    {
        if (_activity == Activity.Food && _age >= _attemptEnds)
        {
            // Gave up on food it couldn't reach.
            Sim.Agitation = Math.Min(1f, Sim.Agitation + SlimeMotion.AgitationPerGiveUp);
            _ignoreFoodUntil = _age + _giveUpSeconds;
            _target = null;
        }

        Actor? best = null;
        float bestScore = 0, bestDrive = 0;
        if (_age >= _ignoreFoodUntil)
        {
            foreach (var item in _catalog.Live)
            {
                if (item == this || item.CaughtBy is not null || Sim.Species.FoodEffect(item.Id) is not { } effect)
                    continue;
                var drive = effect.IgnoresHunger ? 1f : Sim.Hunger;
                if (drive < Sim.Species.Eating.MinDriveToEat)
                    continue;
                var d2 = GlobalPosition.DistanceSquaredTo(item.GlobalPosition);
                if (d2 > _searchRadius * _searchRadius)
                    continue;
                var score = SlimeMotion.FoodScore(drive, d2);
                if (score > bestScore)
                    (best, bestScore, bestDrive) = (item, score, drive);
            }
        }

        if (best is not null && SlimeMotion.PrefersFood(bestDrive))
        {
            if (_activity != Activity.Food)
            {
                _attemptEnds = _age + _attemptSeconds;
                _foodStarted = _age;
            }
            _activity = Activity.Food;
            _target = best;
            _targetDrive = bestDrive;
        }
        else
        {
            _activity = Activity.Wander;
            _target = null;
        }
    }

    private void GoForFood()
    {
        if (_target is null || !IsInstanceValid(_target) || _target.Consumed || _target.CaughtBy is not null)
        {
            _target = null;
            _activity = Activity.None;
            _nextRethink = _age;
            return;
        }
        var toTarget = _target.GlobalPosition - GlobalPosition;
        var distance = toTarget.Length();
        var dir = distance > 0.001f ? toTarget / distance : Forward;
        TurnToward(dir, _facingSpeed, _facingStability);

        // Something in the way (checked at most once a second): jump toward the target.
        if (_age >= _nextBlockCheck)
        {
            _nextBlockCheck = _age + 1f;
            var flat = toTarget with { Y = 0 };
            var reach = Math.Min(_groundDistance * 5, distance);
            var hit = flat.LengthSquared() > 0 ? Ray(GlobalPosition, GlobalPosition + flat.Normalized() * reach, WorldLayer | ActorLayer) : null;
            _blocked = hit is { Count: > 0 } && hit["collider"].AsGodotObject() != _target;
        }
        if (_blocked)
        {
            if (_age >= _nextJump)
            {
                var strength = SlimeMotion.FoodJumpStrength(_targetDrive, _maxJump);
                var aim = (dir * SlimeMotion.LeanTowardTarget(distance) + Vector3.Up).Normalized();
                ApplyCentralImpulse(aim * strength * Mass);
                _nextJump = _age + SlimeMotion.SecondsBetweenJumps;
            }
        }
        else if (distance <= SlimeMotion.SteadyPursuitDistance)
        {
            ApplyCentralForce(dir * (SlimeMotion.SteadyPursuitForce * _pursuitSpeed * Mass * _catalog.FixedTimestep));
        }
        else
        {
            var pulse = SlimeMotion.Pulse(_age - _foodStarted);
            ApplyCentralForce(dir * (SlimeMotion.PulsePursuitForce * Mass * _pursuitSpeed * _catalog.FixedTimestep * pulse));
            ApplyForce(dir * (SlimeMotion.PulseRollForce * Mass * _catalog.FixedTimestep * pulse), Vector3.Down * Radius);
        }
    }

    private void Wander()
    {
        if (_age >= _nextMood)
        {
            _nextMood = _age + SlimeMotion.WanderMoodSeconds;
            _mood = SlimeMotion.PickMood(_random.NextDouble());
            var turn = (float)(_random.NextDouble() * 2 - 1) * SlimeMotion.WanderTurnRadians;
            _heading = (Forward with { Y = 0 }).Normalized().Rotated(Vector3.Up, turn);
        }
        switch (_mood)
        {
            case WanderMood.Hop when _age >= _nextJump && LinearVelocity.LengthSquared() <= SlimeMotion.MaxSpeedToHop * SlimeMotion.MaxSpeedToHop:
            {
                var half = 0.5f * SlimeMotion.WanderJump * Mass;
                float Between(float a, float b) => a + (float)_random.NextDouble() * (b - a);
                ApplyCentralImpulse(new Vector3(Between(-half, half), _verticalFactor * Between(half, SlimeMotion.WanderJump * Mass), Between(-half, half)));
                _nextJump = _age + SlimeMotion.SecondsBetweenJumps;
                break;
            }
            case WanderMood.Scoot:
            {
                TurnToward(_heading, 1f, 1f);
                var pulse = SlimeMotion.Pulse(_age);
                ApplyCentralForce(Forward * (SlimeMotion.PulsePursuitForce * Mass * _scootSpeed * _catalog.FixedTimestep * pulse));
                ApplyForce(Forward * (SlimeMotion.PulseRollForce * Mass * _catalog.FixedTimestep * pulse), Vector3.Down * Radius);
                break;
            }
        }
    }

    // Slimes face their model's front, Unity's +Z, which is Godot's -Z.
    private Vector3 Forward => -GlobalBasis.Z.Normalized();

    // Twists toward a direction, judging by where it will face a moment from now (docs/behavior/slimes.md).
    private void TurnToward(Vector3 dir, float speed, float stability)
    {
        var spin = AngularVelocity;
        var ahead = spin.LengthSquared() > 1e-8f
            ? Forward.Rotated(spin.Normalized(), spin.Length() * stability * 0.1f / Math.Max(speed, 0.001f))
            : Forward;
        ApplyTorque(ahead.Cross(dir) * (speed * speed * Mass));
    }

    // Starts a bite when the slime touches food it will eat now; the food is gone after the bite and
    // the products pop out after digesting (docs/behavior/slimes.md, "In the world: eating").
    private bool TryEat(Actor food)
    {
        if (_busy || food.Consumed || food.CaughtBy is not null || !Sim.WillEat(food.Id))
            return false;
        _busy = true;
        food.Reserve();
        GetTree().CreateTimer(SlimeMotion.BiteSeconds, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            food.Finish();
            if (!IsInstanceValid(this) || Consumed)
                return;
            var meal = Sim.Feed(food.Id);
            _busy = false;
            Ate?.Invoke(this, food.Id);
            if (meal.Produced.Count == 0)
                return;
            GetTree().CreateTimer(Slime.DigestSeconds, processAlways: false, processInPhysics: true).Timeout += () =>
            {
                if (!IsInstanceValid(this) || Consumed)
                    return;
                foreach (var id in meal.Produced)
                {
                    var at = GlobalPosition + GlobalBasis.Y.Normalized() * SlimeMotion.ProduceHeight;
                    var item = _catalog.Spawn(id, at);
                    item.LinearVelocity = GlobalBasis.Y.Normalized() * SlimeMotion.ProduceSpeed;
                    item.GrowFrom(SlimeMotion.ProduceStartScale, SlimeMotion.ProduceGrowSeconds);
                    Produced?.Invoke(this, item);
                }
            };
        };
        return true;
    }

    private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to, uint mask)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, new Godot.Collections.Array<Rid> { GetRid() });
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }
}
