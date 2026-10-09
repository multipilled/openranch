using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// A slime in the world: a ball that stays upright, gets hungry over game time, goes for food it
/// wants, eats what it touches and pops out plorts, or turns into a largo or tarr when it eats another
/// kind of plort. The rules are in docs/behavior/slimes.md ("In the world") and largos.md; tuning comes
/// from the prefab's components, code-only numbers from <see cref="SlimeMotion"/>. Species traits
/// (hovering, rolling) come from the prefab's trait components (docs/behavior/slime-traits.md).
/// </summary>
public partial class SlimeActor : Actor
{
    private enum Activity { None, Wander, Food, Hover, Roll, Behaviour }

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

    // Traits: whether the prefab carries SlimeHover / RockSlimeRoll, and their timers.
    private readonly bool _hovers, _rolls;
    // The prefab root's scale (2 for largos and tarrs); a hover ends on a touch this high up.
    private readonly float _scale;
    // How much a bite hurts a slime it eats (SlimeEat.damagePerAttack).
    private readonly int _damagePerBite;
    private float _nextHover, _nextRoll, _traitEnds, _rollStarts;
    private bool _hoverCancelled;
    private Vector3 _hoverDrift, _rollAxis, _rollForward;

    // The prefab's other behaviour pieces (feral, abilities, feeding habits) and the one in charge.
    private readonly List<SlimeBehaviour> _behaviours = [];
    private SlimeBehaviour? _behaviour;
    // Where food seeking, hovering, rolling and wandering sit among the prefab's components (ties go to the earlier).
    private readonly int _foodOrder, _hoverOrder, _rollOrder, _wanderOrder;
    private readonly bool _stalks;

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

        _hovers = prefab.RootScript("SlimeHover") is not null;
        _rolls = prefab.RootScript("RockSlimeRoll") is not null;
        _scale = prefab.Colliders.FirstOrDefault(c => c.Path == prefab.Name) is { } root
            ? new System.Numerics.Vector3(root.ToRoot.M21, root.ToRoot.M22, root.ToRoot.M23).Length()
            : 1f;
        Health = prefab.RootScript("SlimeHealth")?["maxHealth"] is int health ? health : 0;
        _damagePerBite = prefab.RootScript("SlimeEat")?["damagePerAttack"] is int damage ? damage : 0;
        // The first hover and roll come after a delay picked the same way as later ones.
        _nextHover = SlimeTraits.Delay(SlimeTraits.HoverMinDelay, SlimeTraits.HoverMaxDelay, Sim.Agitation, _random.NextDouble());
        _nextRoll = SlimeTraits.Delay(SlimeTraits.RollMinDelay, SlimeTraits.RollMaxDelay, Sim.Agitation, _random.NextDouble());

        Prefab = prefab;
        _foodOrder = SlimeBehaviours.OrderOf(prefab, "GotoConsumable");
        _stalks = prefab.RootScript("StalkConsumable") is not null;
        _hoverOrder = SlimeBehaviours.OrderOf(prefab, "SlimeHover");
        _rollOrder = SlimeBehaviours.OrderOf(prefab, "RockSlimeRoll");
        _wanderOrder = SlimeBehaviours.OrderOf(prefab, "SlimeRandomMove");
        // SlimeFeral removes itself from normal-sized slimes, so only largos can be feral (static analysis).
        if (prefab.RootScript("SlimeFeral") is { } feral && prefab.VacuumSize != 0)
            Feral = new Feral(new FeralSettings(
                feral["dynamicToFeral"] is true, feral["dynamicFromFeral"] is true,
                feral["feralLifetimeHours"] is float hours ? hours : 0f), Sim);
        foreach (var behaviour in SlimeBehaviours.For(catalog, prefab, this))
        {
            _behaviours.Add(behaviour);
            behaviour.Attach(this);
        }
    }

    public ItemCatalog Catalog => _catalog;
    public ItemPrefab Prefab { get; }
    /// <summary>Its feral state, for slimes that can go feral (largos); null otherwise.</summary>
    public Feral? Feral { get; }
    /// <summary>The behaviour pieces from its prefab (<see cref="SlimeBehaviours"/>).</summary>
    public IReadOnlyList<SlimeBehaviour> Behaviours => _behaviours;
    public T? Behaviour<T>() where T : SlimeBehaviour => _behaviours.OfType<T>().FirstOrDefault();
    /// <summary>Seconds since it appeared.</summary>
    public float Age => _age;
    /// <summary>Standing on something (checked a few times a second).</summary>
    public bool Grounded => _grounded;
    /// <summary>The prefab root's scale (2 for largos).</summary>
    public float PrefabScale => _scale;
    public Random Random => _random;

    /// <summary>The slime's appetite and mood.</summary>
    public Slime Sim { get; }

    /// <summary>Health left (the prefab's SlimeHealth.maxHealth to start); a tarr's bites take it away.</summary>
    public int Health { get; set; }

    /// <summary>Raised when the slime has turned into another (a largo or a tarr): the old slime, then the new one.</summary>
    public event Action<SlimeActor, SlimeActor>? Transformed;

    /// <summary>Raised when the slime has eaten something (the food's id).</summary>
    public event Action<SlimeActor, string>? Ate;
    /// <summary>Raised for each item the slime produces after digesting.</summary>
    public event Action<SlimeActor, Actor>? Produced;

    public override void _Ready()
    {
        base._Ready();
        BodyEntered += body =>
        {
            foreach (var b in _behaviours)
                b.Touched(body);
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        var dt = (float)delta;
        _age += dt;
        Sim.Advance(_catalog.Clock.HoursFor(dt));
        if (Consumed)
            return;
        foreach (var b in _behaviours)
        {
            b.Tick(dt);
            if (Consumed)
                return;
        }
        if (CaughtBy is not null)
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
        // A hover or roll holds the slime until it is over (or a hover is cut short).
        var holding = _activity switch
        {
            Activity.Hover => !_hoverCancelled && _age < _traitEnds,
            Activity.Roll => _age < _traitEnds,
            Activity.Behaviour => _behaviour is { CanRethink: false },
            _ => false,
        };
        if (_age >= _nextRethink && !holding)
        {
            _nextRethink = _age + SlimeMotion.RethinkSeconds;
            Rethink();
        }
        // Traits and behaviour pieces do their own ground checks; wandering and food only push while standing on something.
        if (_activity == Activity.Behaviour)
            _behaviour?.Action(dt);
        else if (_activity == Activity.Hover)
            Hover();
        else if (_activity == Activity.Roll)
            Roll();
        else if (!_grounded)
            return;
        else if (_activity == Activity.Food)
            GoForFood();
        else if (_activity == Activity.Wander)
            Wander();
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        // A hover ends when the slime's top touches something solid that isn't an item.
        if (_activity != Activity.Hover)
            return;
        for (var i = 0; i < state.GetContactCount(); i++)
            if (state.GetContactColliderObject(i) is not RigidBody3D
                && state.GetContactColliderPosition(i).Y > GlobalPosition.Y + SlimeTraits.HoverCeilingHeight * _scale)
                _hoverCancelled = true;
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
                if (item == this || item.CaughtBy is not null || !item.Edible || Sim.Species.FoodEffect(item.Id) is not { } effect)
                    continue;
                var drive = Sim.Drive(effect);
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

        // Hovering and rolling matter a fixed amount when due. A tie goes to the trait (openranch's
        // choice: the original takes whichever behaviour comes first on the prefab).
        var hoverDue = _hovers && _age >= _nextHover;
        var rollDue = _rolls && _age >= _nextRoll && _grounded;
        var foodRelevancy = best is null ? 0f : SlimeMotion.FoodRelevancy(bestDrive);
        // Everything competes like the original's sub-behaviours: in the prefab's component order, only
        // a strictly higher relevancy takes the lead, so ties go to the earlier component.
        var choice = Activity.Wander;
        SlimeBehaviour? piece = null;
        var (lead, leadOrder) = (SlimeMotion.WanderRelevancy, _wanderOrder);
        void Offer(float relevancy, int order, Activity activity, SlimeBehaviour? behaviour = null)
        {
            if (relevancy > lead || (relevancy == lead && order < leadOrder))
                (lead, leadOrder, choice, piece) = (relevancy, order, activity, behaviour);
        }
        // StalkConsumable replaces going straight for food (it forbids GotoConsumable).
        if (best is not null && !_stalks)
            Offer(foodRelevancy, _foodOrder, Activity.Food);
        if (hoverDue)
            Offer(SlimeTraits.TraitRelevancy, _hoverOrder, Activity.Hover);
        if (rollDue)
            Offer(SlimeTraits.TraitRelevancy, _rollOrder, Activity.Roll);
        foreach (var b in _behaviours)
            Offer(b.Relevancy(_grounded), b.Order, Activity.Behaviour, b);

        var previous = _activity == Activity.Behaviour ? _behaviour : null;
        if (piece is not null)
        {
            if (piece != previous)
            {
                previous?.Deselected();
                _behaviour = piece;
                _activity = Activity.Behaviour;
                piece.Selected();
            }
            _target = null;
            return;
        }
        previous?.Deselected();
        _behaviour = null;
        if (choice is Activity.Hover or Activity.Roll)
        {
            if (choice == Activity.Hover)
                StartHover();
            else
                StartRoll();
            _target = null;
        }
        else if (choice == Activity.Food)
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
        if (_target is null || !IsInstanceValid(_target) || _target.Consumed || _target.CaughtBy is not null || !_target.Edible)
        {
            _target = null;
            _activity = Activity.None;
            _nextRethink = _age;
            return;
        }
        Pursue(_target.GlobalPosition, _target, SlimeMotion.FoodJumpStrength(_targetDrive, _maxJump), _pursuitSpeed, _facingSpeed, _facingStability, _foodStarted);
    }

    /// <summary>
    /// One physics step of going after something (food, the player): turns toward it, jumps at it when
    /// something is in the way, otherwise pushes along (docs/behavior/slimes.md, "Going for food").
    /// <paramref name="startedAt"/> is when the chase began (its pulses keep time from then).
    /// </summary>
    public void Pursue(Vector3 targetPosition, GodotObject? target, float jumpStrength, float pursuitSpeed, float facingSpeed, float facingStability, float startedAt)
    {
        var toTarget = targetPosition - GlobalPosition;
        var distance = toTarget.Length();
        var dir = distance > 0.001f ? toTarget / distance : Forward;
        TurnToward(dir, facingSpeed, facingStability);

        // Something in the way (checked at most once a second): jump toward the target.
        if (_age >= _nextBlockCheck)
        {
            _nextBlockCheck = _age + 1f;
            var flat = toTarget with { Y = 0 };
            var reach = Math.Min(_groundDistance * 5, distance);
            var hit = flat.LengthSquared() > 0 ? Ray(GlobalPosition, GlobalPosition + flat.Normalized() * reach, WorldLayer | ActorLayer) : null;
            _blocked = hit is { Count: > 0 } && hit["collider"].AsGodotObject() != target;
        }
        if (_blocked)
        {
            if (_age >= _nextJump)
            {
                var aim = (dir * SlimeMotion.LeanTowardTarget(distance) + Vector3.Up).Normalized();
                ApplyCentralImpulse(aim * jumpStrength * Mass);
                _nextJump = _age + SlimeMotion.SecondsBetweenJumps;
            }
        }
        else if (distance <= SlimeMotion.SteadyPursuitDistance)
        {
            ApplyCentralForce(dir * (SlimeMotion.SteadyPursuitForce * pursuitSpeed * Mass * _catalog.FixedTimestep));
        }
        else
        {
            var pulse = SlimeMotion.Pulse(_age - startedAt);
            ApplyCentralForce(dir * (SlimeMotion.PulsePursuitForce * Mass * pursuitSpeed * _catalog.FixedTimestep * pulse));
            ApplyForce(dir * (SlimeMotion.PulseRollForce * Mass * _catalog.FixedTimestep * pulse), Vector3.Down * Radius);
        }
    }

    private void StartHover()
    {
        _activity = Activity.Hover;
        _hoverCancelled = false;
        _traitEnds = _age + SlimeTraits.HoverSeconds;
        _nextHover = _traitEnds + SlimeTraits.Delay(SlimeTraits.HoverMinDelay, SlimeTraits.HoverMaxDelay, Sim.Agitation, _random.NextDouble());
        _hoverDrift = new Vector3((float)_random.NextDouble() * 2 - 1, 0, (float)_random.NextDouble() * 2 - 1);
    }

    // Lifts toward hover height above whatever is below, and drifts (docs/behavior/slime-traits.md).
    private void Hover()
    {
        if (_hoverCancelled)
            return;
        var below = Ray(GlobalPosition, GlobalPosition + Vector3.Down * SlimeTraits.HoverHeight, WorldLayer | ActorLayer);
        if (below.Count > 0)
        {
            var height = GlobalPosition.DistanceTo((Vector3)below["position"]);
            ApplyCentralForce(Vector3.Up * (SlimeTraits.HoverLiftAt(height) * Mass * _catalog.FixedTimestep));
        }
        ApplyCentralForce(_hoverDrift * (SlimeTraits.HoverDrift * Mass * _catalog.FixedTimestep));
    }

    private void StartRoll()
    {
        _activity = Activity.Roll;
        // It rolls about its own flat right-hand axis; forward is that axis turned a quarter left
        // (Unity's right is Godot's +X).
        _rollAxis = GlobalBasis.X with { Y = 0 };
        _rollAxis = _rollAxis.LengthSquared() > 1e-6f ? _rollAxis.Normalized() : Vector3.Right;
        _rollForward = Vector3.Up.Cross(_rollAxis);
        _rollStarts = _age + SlimeTraits.RollSpinSeconds;
        _traitEnds = _rollStarts + SlimeTraits.RollSeconds;
        _nextRoll = _traitEnds + SlimeTraits.Delay(SlimeTraits.RollMinDelay, SlimeTraits.RollMaxDelay, Sim.Agitation, _random.NextDouble());
    }

    private void Roll()
    {
        if (!_grounded || _age <= _rollStarts)
            return;
        // Spin about the axis so the ball rolls the way it is pushed.
        ApplyTorque(_rollForward.Cross(Vector3.Down) * (SlimeTraits.RollTorque * Mass * _catalog.FixedTimestep));
        ApplyCentralForce(_rollForward * (SlimeTraits.RollForce * Mass * _catalog.FixedTimestep));
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
    public Vector3 Forward => -GlobalBasis.Z.Normalized();

    // Twists toward a direction, judging by where it will face a moment from now (docs/behavior/slimes.md).
    public void TurnToward(Vector3 dir, float speed, float stability)
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
        if (_busy || food.Consumed || food.CaughtBy is not null || !food.Edible || !Sim.WillEat(food.Id))
            return false;
        _busy = true;
        // A slime with health (bitten by a tarr) is only swallowed once a bite takes the last of it.
        var victim = food as SlimeActor;
        var swallowed = victim is null || victim.Health <= 0 || (victim.Health -= _damagePerBite) <= 0;
        if (swallowed)
            food.Reserve();
        GetTree().CreateTimer(SlimeMotion.BiteSeconds, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            if (swallowed)
                food.Finish();
            if (!IsInstanceValid(this) || Consumed)
                return;
            var meal = Sim.Feed(food.Id, swallowed);
            _busy = false;
            Feral?.DidEat();
            Ate?.Invoke(this, food.Id);
            if (meal.Becomes is { } becomes)
            {
                TransformInto(becomes);
                return;
            }
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

    // Becomes another slime on the spot (docs/behavior/largos.md): the new one keeps this one's
    // feelings and springs from this one's size to its own.
    private void TransformInto(string id)
    {
        if (_catalog.Species(id) is null)
        {
            GD.PushWarning($"{Id} would become {id}, which has no slime settings.");
            return;
        }
        var heading = GlobalBasis.Z with { Y = 0 };
        var yaw = heading.LengthSquared() > 1e-6f ? Mathf.Atan2(heading.X, heading.Z) : 0f;
        var next = (SlimeActor)_catalog.Spawn(id, GlobalPosition, yaw);
        // openranch's colliders don't grow with the model, so a bigger slime starts lifted clear of the ground.
        next.GlobalPosition += Vector3.Up * Math.Max(0, next.Radius - Radius);
        next.Sim.Hunger = Sim.Hunger;
        next.Sim.Agitation = Sim.Agitation;
        next.Visual.Scale = Vector3.One * (Radius / Math.Max(next.Radius, 0.001f));
        next.CreateTween().TweenProperty(next.Visual, "scale", Vector3.One, Largos.TransformScaleSeconds)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        // FeralizeOnLargoTransformed: a slime with it turns feral as it forms (hunter largos).
        if (next.Prefab.RootScript("FeralizeOnLargoTransformed") is not null)
            next.Feral?.SetFeral(_catalog.Clock.TotalHours);
        Consume();
        Transformed?.Invoke(this, next);
    }

    public Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to, uint mask)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, new Godot.Collections.Array<Rid> { GetRid() });
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }
}
