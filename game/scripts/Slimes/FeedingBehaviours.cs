using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity.Managed;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

// Feeding habits (docs/behavior/feeding-habits.md): stalking and pouncing, carrying food, gold and
// lucky slimes, fire slimes on ash, puddle slimes in water. Code-only numbers are in
// src/OpenRanch.Simulation/FeedingHabits.cs; tuning comes from the prefab.

/// <summary>
/// Tabbies, sabers and hunters (<c>StalkConsumable</c>, which replaces going straight for food): the food
/// it wants most, or the player, matters drive². Farther than 8 m it creeps up for up to 3 s (then waits
/// 3 s); within 8 m it wiggles 2 s and pounces, a leap of √(distance × gravity) × 1.2; a saber
/// (<c>doesParkour</c>) first feints sideways at a random 55-80° and pounces off whatever it lands on.
/// No stalking for 15 s after a pounce. Hunters cloak while stalking.
/// </summary>
public sealed class StalkPounce : SlimeBehaviour
{
    private enum Mode { None, Approach, Wait, Prep, Pounce, Feint, Pivot }

    private readonly float _maxSearch, _minDist, _pursuit, _facingSpeed, _facingStability, _feintPower, _feintMin, _feintMax;
    private readonly bool _parkour;
    private Mode _mode;
    private float _modeEnds, _nextStalk;
    private Node3D? _target;
    private bool _feinting, _pivotNow, _fromPivot;

    public StalkPounce(SerializedObject data)
    {
        (_maxSearch, _minDist, _pursuit) = (F(data, "maxSearchRad"), F(data, "minDist"), F(data, "pursuitSpeedFactor", 1));
        (_facingSpeed, _facingStability) = (F(data, "facingSpeed", 1), F(data, "facingStability", 1));
        (_feintPower, _feintMin, _feintMax) = (F(data, "feintPowerMultiplier", 1), F(data, "feintMinAngle"), F(data, "feintMaxAngle"));
        _parkour = data["doesParkour"] is true;
    }

    public bool DoesParkour => _parkour;
    public string? LastTarget { get; private set; }
    public float LastLeapSpeed { get; private set; }
    public int Feints { get; private set; }

    public override float Relevancy(bool grounded)
    {
        if (Slime.Age < _nextStalk)
            return 0f;
        var (food, drive) = FoodSearch.Nearest(Slime, _maxSearch, _minDist);
        _target = food;
        var best = food is null ? 0f : drive / Math.Max(Slime.GlobalPosition.DistanceSquaredTo(food.GlobalPosition), 1e-4f);
        // The player is prey too: a drive of 1 + (-0.1).
        if (Catalog.Player is { } player && GodotObject.IsInstanceValid(player))
        {
            var playerDrive = Math.Max(0f, 1f + Stalking.PlayerExtraDrive);
            var d2 = Slime.GlobalPosition.DistanceSquaredTo(player.GlobalPosition);
            var score = playerDrive / Math.Max(d2, 1e-4f);
            if (d2 >= _minDist * _minDist && score > Math.Max(best, 1f / (_maxSearch * _maxSearch)))
                (_target, drive) = (player, playerDrive);
        }
        return _target is null ? 0f : Stalking.Relevancy(drive);
    }

    public override bool CanRethink => _mode == Mode.None;

    public override void Selected()
    {
        Slime.Behaviour<Stealth>()?.SetStealth(true);
        _mode = Mode.None;
    }

    public override void Deselected()
    {
        Slime.Behaviour<Stealth>()?.SetStealth(false);
        _mode = Mode.None;
    }

    public override void Touched(Node body)
    {
        if (_feinting && _mode == Mode.Pivot)
            _pivotNow = true;
    }

    private Vector3 TargetPos => _target!.GlobalPosition;
    private bool TargetGone => _target is null || !GodotObject.IsInstanceValid(_target) || _target is Actor { Consumed: true } or Actor { CaughtBy: not null };

    public override void Action(float delta)
    {
        if (TargetGone)
        {
            _mode = Mode.None;
            return;
        }
        var age = Slime.Age;
        var to = TargetPos - Slime.GlobalPosition;
        switch (_mode)
        {
            case Mode.None:
                var close = to.LengthSquared() < Stalking.PounceDistance * Stalking.PounceDistance;
                _mode = close ? Mode.Prep : Mode.Approach;
                _modeEnds = age + (close ? Stalking.PrepSeconds : Stalking.ApproachSeconds);
                break;
            case Mode.Approach:
                if (Slime.Grounded)
                {
                    var dir = to.Normalized();
                    Slime.TurnToward(dir, _facingSpeed, _facingStability);
                    var up = Slime.LinearVelocity.LengthSquared() < Stalking.SlowSpeedSquared ? Stalking.CreepUpSlow : Stalking.CreepUpFast;
                    var aim = (dir * Stalking.CreepAhead + Vector3.Up * up).Normalized();
                    var step = Catalog.FixedTimestep;
                    Slime.ApplyCentralForce(aim * (Stalking.CreepPush * Slime.Mass * _pursuit * step));
                    Slime.ApplyForce(aim * (Stalking.CreepBasePush * Slime.Mass * step), Vector3.Down * Slime.Radius);
                    if (to.LengthSquared() < Stalking.PounceDistance * Stalking.PounceDistance)
                        (_mode, _modeEnds) = (Mode.Prep, age + Stalking.PrepSeconds);
                }
                if (_mode == Mode.Approach && age > _modeEnds)
                    (_mode, _modeEnds) = (Mode.Wait, age + Stalking.WaitSeconds);
                break;
            case Mode.Wait:
                if (age > _modeEnds)
                    _mode = Mode.None;
                break;
            case Mode.Prep:
                if (age > _modeEnds)
                {
                    _feinting = false;
                    _fromPivot = false;
                    (_mode, _modeEnds) = (_parkour ? Mode.Feint : Mode.Pounce, age + (_parkour ? Stalking.FeintSeconds : Stalking.PounceSeconds));
                }
                break;
            case Mode.Pounce:
                if (Slime.Grounded || _fromPivot)
                {
                    if (_fromPivot)
                        Slime.LinearVelocity = Vector3.Zero;
                    Leap(to.LengthSquared(), to.Normalized(), to.Normalized());
                    LastTarget = _target is Actor a ? a.Id : "PLAYER";
                    Report($"pounced at {LastTarget} from {to.Length():F1} m (leap {LastLeapSpeed:F1} m/s{(_fromPivot ? ", off a feint" : "")})");
                    _target = null;
                    _mode = Mode.None;
                    _fromPivot = false;
                    _nextStalk = age + Stalking.ResetSeconds;
                }
                else if (age > _modeEnds)
                    _mode = Mode.None;
                break;
            case Mode.Feint:
                if (Slime.Grounded)
                {
                    var angle = _feintMin + (float)Slime.Random.NextDouble() * (_feintMax - _feintMin);
                    if (Slime.Random.Next(2) == 0)
                        angle = -angle;
                    var dir = to.Normalized();
                    Leap(to.LengthSquared() * _feintPower * _feintPower, dir.Rotated(Vector3.Up, Mathf.DegToRad(angle)), dir);
                    Feints++;
                    _feinting = true;
                    (_mode, _modeEnds) = (Mode.Pivot, age + Stalking.PivotSeconds);
                }
                else if (age > _modeEnds)
                    (_mode, _feinting) = (Mode.None, false);
                break;
            case Mode.Pivot:
                if (_pivotNow)
                {
                    _pivotNow = false;
                    _fromPivot = true;
                    (_mode, _modeEnds) = (Mode.Pounce, age + Stalking.PounceSeconds);
                }
                else if (age > _modeEnds)
                    (_mode, _feinting) = (Mode.None, false);
                break;
        }
    }

    private void Leap(float distanceSquared, Vector3 jumpDir, Vector3 faceDir)
    {
        Slime.TurnToward(faceDir, _facingSpeed, _facingStability);
        var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        LastLeapSpeed = Stalking.LeapSpeed(MathF.Sqrt(distanceSquared), gravity);
        Slime.LinearVelocity += (jumpDir + Vector3.Up).Normalized() * LastLeapSpeed;
    }
}

/// <summary>
/// Tabbies and sabers carrying food (<c>GatherIdentifiableItems</c>): a fruit or veggie within reach (any
/// kind its <c>itemClasses</c> name) with some other food at least <c>minGatherDist</c> from it makes
/// carrying matter a random 0.3-0.5. It goes to the item, picks it up in its mouth, hops toward the
/// other food (harder for the load) and drops it within 3 m. Ten seconds without progress and it gives
/// up; afterwards it rests <c>pauseBetweenGathers</c> seconds.
/// </summary>
public sealed class Gather : SlimeBehaviour
{
    private readonly float _maxSearch, _maxJump, _pause, _minGather, _pursuit, _facingSpeed, _facingStability;
    private readonly HashSet<ItemKind> _kinds = [];
    private Actor? _item;
    private Actor? _goal;
    private bool _carrying;
    private float _giveUp, _disallowUntil, _started;
    private Vector3 _pickedAt;

    public Gather(SerializedObject data)
    {
        (_maxSearch, _maxJump, _pause, _minGather) = (F(data, "maxSearchRad"), F(data, "maxJump"), F(data, "pauseBetweenGathers"), F(data, "minGatherDist"));
        (_pursuit, _facingSpeed, _facingStability) = (F(data, "pursuitSpeedFactor", 1), F(data, "facingSpeed", 1), F(data, "facingStability", 1));
        // Identifiable item classes: 0 veggies, 1 fruit (the two the tabby and saber prefabs list).
        foreach (var c in data.List("itemClasses"))
            _kinds.Add(Convert.ToInt32(c) == 0 ? ItemKind.Veggie : ItemKind.Fruit);
    }

    public string? LastCarried { get; private set; }
    public float LastCarryDistance { get; private set; }

    private bool Wanted(Actor a) => _kinds.Contains(a.Kind) && !a.Freeze && a.CaughtBy is null && !a.Consumed;

    public override float Relevancy(bool grounded)
    {
        if (_carrying)
            return Gathering.MaxRelevancy;
        _item = null;
        if (Slime.Age < _disallowUntil)
            return 0f;
        _item = Catalog.Live.Where(Wanted)
            .Where(a => a.GlobalPosition.DistanceTo(Slime.GlobalPosition) <= _maxSearch)
            .OrderBy(a => a.GlobalPosition.DistanceSquaredTo(Slime.GlobalPosition)).FirstOrDefault();
        _goal = _item is null ? null : GoalFor(_item);
        if (_goal is null)
            _item = null;
        return _item is null ? 0f : Gathering.MinRelevancy + (float)Slime.Random.NextDouble() * (Gathering.MaxRelevancy - Gathering.MinRelevancy);
    }

    // The nearest other food of a different kind, between minGatherDist and the search radius from the item.
    private Actor? GoalFor(Actor item) => Catalog.Live
        .Where(a => a != item && a != Slime && a.Edible && _kinds.Contains(a.Kind) && a.Id != item.Id)
        .Select(a => (a, d: a.GlobalPosition.DistanceTo(item.GlobalPosition)))
        .Where(x => x.d >= _minGather && x.d <= _maxSearch)
        .OrderBy(x => x.d).Select(x => x.a).FirstOrDefault();

    public override bool CanRethink => !_carrying;

    public override void Selected()
    {
        _giveUp = Slime.Age + Gathering.GiveUpSeconds;
        _started = Slime.Age;
    }

    public override void Deselected()
    {
        Drop();
        _disallowUntil = Slime.Age + _pause;
    }

    private void Drop()
    {
        if (_carrying && _item is { } i && GodotObject.IsInstanceValid(i) && !i.Consumed)
            i.Freeze = false;
        _carrying = false;
    }

    public override void Action(float delta)
    {
        if (_item is null || !GodotObject.IsInstanceValid(_item) || _item.Consumed || _goal is null || !GodotObject.IsInstanceValid(_goal) || Slime.Age > _giveUp || Slime.CaughtBy is not null)
        {
            Drop();
            _item = null;
            return;
        }
        if (!_carrying)
        {
            if (Slime.Grounded)
                Slime.Pursue(_item.GlobalPosition, _item, _maxJump, _pursuit, _facingSpeed, _facingStability, _started);
            if (Slime.GlobalPosition.DistanceTo(_item.GlobalPosition) <= Gathering.PickUpDistance * Slime.PrefabScale + Slime.Radius)
            {
                _carrying = true;
                _item.Freeze = true;
                _pickedAt = _item.GlobalPosition;
                _giveUp = Slime.Age + Gathering.GiveUpSeconds;
            }
            return;
        }
        // In its mouth: just in front of it.
        _item.GlobalPosition = Slime.GlobalPosition + Slime.Forward * (Slime.Radius + _item.Radius) + Vector3.Up * 0.1f;
        if (Slime.GlobalPosition.DistanceSquaredTo(_goal.GlobalPosition) <= Gathering.DropDistance * Gathering.DropDistance)
        {
            LastCarried = _item.Id;
            LastCarryDistance = _pickedAt.DistanceTo(_item.GlobalPosition);
            Drop();
            Report($"carried {LastCarried} {LastCarryDistance:F1} m to {_goal.Id}");
            _item = null;
            return;
        }
        if (Slime.Grounded)
            Slime.Pursue(_goal.GlobalPosition, _goal, Gathering.CarryJump(_maxJump, _item.Mass, Slime.Mass), _pursuit, _facingSpeed, _facingStability, _started);
    }
}

/// <summary>
/// Gold slimes (<c>GoldSlimeProducePlorts</c>, <c>GoldSlimeFlee</c>/<c>SlimeFlee</c>): hit by a food, chick or
/// plort (not ginger, not a gold plort) it drops a gold plort behind itself (the prefab's plort), hops,
/// and runs from the player; touching the player also sets it running. Running matters 1: it turns away
/// and pushes off; if something blocks its way it vanishes.
/// </summary>
public sealed class GoldRunner : SlimeBehaviour
{
    private readonly string? _plort;
    private readonly float _fleeSpeed, _facingSpeed, _facingStability;
    private readonly HashSet<ulong> _hitBy = [];
    private Vector3? _fleeDir;

    public GoldRunner(ItemCatalog catalog, ItemPrefab prefab)
    {
        _plort = catalog.ItemIdOf(catalog.Prefabs.Referenced(prefab.Id, "GoldSlimeProducePlorts", "plortPrefab"));
        var flee = prefab.RootScript("GoldSlimeFlee");
        (_fleeSpeed, _facingSpeed, _facingStability) = (F(flee, "fleeSpeedFactor", 1), F(flee, "facingSpeed", 1), F(flee, "facingStability", 1));
    }

    public string? Plort => _plort;
    public int PlortsMade { get; private set; }
    public bool Fleeing => _fleeDir is not null;
    public bool Vanished { get; private set; }

    public override void Touched(Node body)
    {
        if (body == Catalog.Player)
        {
            StartFleeing();
            return;
        }
        if (body is not Actor a || !GoldSlime.CausesPlort(a.Id) || !_hitBy.Add(a.GetInstanceId()))
            return;
        if ((a.LinearVelocity - Slime.LinearVelocity).Length() <= GoldSlime.HitThreshold)
            return;
        if (_plort is not null)
        {
            var plort = Catalog.Spawn(_plort, Slime.GlobalPosition - Slime.Forward);
            plort.GrowFrom(SlimeMotion.ProduceStartScale, SlimeMotion.ProduceGrowSeconds);
            PlortsMade++;
        }
        Slime.ApplyCentralImpulse(Vector3.Up * GoldSlime.JumpOnHit * Catalog.FixedTimestep);
        Report($"hit by {a.Id}: dropped {_plort} and ran");
        StartFleeing();
    }

    private void StartFleeing()
    {
        if (Catalog.Player is not { } player || !GodotObject.IsInstanceValid(player))
            return;
        var away = (Slime.GlobalPosition - player.GlobalPosition) with { Y = 0 };
        _fleeDir = away.LengthSquared() > 1e-4f ? away.Normalized() : Slime.Forward;
    }

    public override float Relevancy(bool grounded) => _fleeDir is null ? 0f : OpenRanch.Simulation.Fleeing.Relevancy;

    public override void Action(float delta)
    {
        if (_fleeDir is not { } dir)
            return;
        Slime.TurnToward(dir, _facingSpeed, _facingStability);
        var ahead = Slime.Ray(Slime.GlobalPosition, Slime.GlobalPosition + dir * (Slime.Radius + 0.5f), Actor.WorldLayer | Actor.PenWallLayer);
        if (ahead.Count > 0)
        {
            Vanished = true;
            Report("ran into something and vanished");
            Slime.Consume();
            return;
        }
        var step = Catalog.FixedTimestep;
        Slime.ApplyCentralForce(dir * (OpenRanch.Simulation.Fleeing.Push * Slime.Mass * _fleeSpeed * step));
        Slime.ApplyForce(dir * (OpenRanch.Simulation.Fleeing.BasePush * Slime.Mass * step), Vector3.Down * Slime.Radius);
    }
}

/// <summary>
/// Lucky slimes (<c>LuckySlimeProduceCoins</c>, <c>LuckySlimeFlee</c>): hit by a chicken or chick, it gobbles it,
/// hops after 0.35 s and drops coin bundles 0.1 s apart (2 the first time, then double, up to 6), each
/// worth its prefab's <c>ConvertToCurrency.amount</c>; once hit or seen by the player it vanishes 10 game
/// minutes later.
/// </summary>
public sealed class LuckyCoins : SlimeBehaviour
{
    private readonly int _perBundle;
    private readonly float _bundleDelay;
    private readonly float _seeRadius;
    private readonly HashSet<ulong> _hitBy = [];
    private int _lastBundles;
    private double? _vanishAt;

    public LuckyCoins(ItemCatalog catalog, ItemPrefab prefab)
    {
        var coins = catalog.Prefabs.Referenced(prefab.Id, "LuckySlimeProduceCoins", "coinsPrefab")?.RootScript("ConvertToCurrency");
        _perBundle = coins?["amount"] is int a ? a : 0;
        _bundleDelay = F(coins, "delay");
        // The player "sees" it on entering its root trigger, if it has one (openranch: nothing otherwise).
        _seeRadius = prefab.Colliders.FirstOrDefault(c => c.Path == prefab.Name && c.Collider.IsTrigger)?.Collider.Radius ?? 0f;
    }

    public int PerBundle => _perBundle;
    public int CoinsGiven { get; private set; }
    public double? VanishAt => _vanishAt;

    public override void Touched(Node body)
    {
        if (body is not Actor a || Items.KindOf(a.Id) is not (ItemKind.Meat or ItemKind.Chick) || a.Consumed || !_hitBy.Add(a.GetInstanceId()))
            return;
        if ((a.LinearVelocity - Slime.LinearVelocity).Length() <= LuckySlime.HitThreshold)
            return;
        a.Consume(); // gobbled whatever its mood
        var bundles = _lastBundles = LuckySlime.Bundles(_lastBundles);
        Slime.GetTree().CreateTimer(LuckySlime.HopDelay, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(Slime))
                return;
            float Side() => ((float)Slime.Random.NextDouble() * 2 - 1) * LuckySlime.HopSideways;
            Slime.ApplyCentralImpulse(new Vector3(Side(), LuckySlime.HopUp, Side()) * Catalog.FixedTimestep);
            for (var i = 0; i < bundles; i++)
                Slime.GetTree().CreateTimer(i * LuckySlime.SecondsBetweenCoins + _bundleDelay, processAlways: false, processInPhysics: true).Timeout += () =>
                {
                    CoinsGiven += _perBundle;
                    Catalog.Wallet?.Add(_perBundle);
                };
            Report($"ate {a.Id}: {bundles} coin bundles of {_perBundle}");
        };
        StartVanishing();
    }

    private void StartVanishing() => _vanishAt ??= Catalog.Clock.TotalHours + LuckySlime.VanishHours;

    public override void Tick(float delta)
    {
        if (_seeRadius > 0 && Catalog.Player is { } p && GodotObject.IsInstanceValid(p) && p.GlobalPosition.DistanceTo(Slime.GlobalPosition) <= _seeRadius)
            StartVanishing();
        if (_vanishAt is { } at && Catalog.Clock.TotalHours >= at)
            Slime.Consume();
    }
}

/// <summary>
/// Fire slimes on ash and puddle slimes in water (<c>SlimeEatAsh</c>/<c>SlimeEatWater</c>,
/// <c>GotoAsh</c>/<c>GotoWater</c>, <c>DestroyOnTouching</c>): touching its patch and hungry, every
/// <c>eatRate</c> seconds it takes a bite, gets less hungry and calmer, and 2 s later makes a plort (the
/// prefab's) unless, for puddles, it is too crowded. Standing on anything else for its
/// <c>hoursOfContactAllowed</c> makes it poof; heading back to its patch (within 30 m) matters more the
/// closer that is.
/// </summary>
public sealed class Grazer : SlimeBehaviour
{
    private readonly string _kind;
    private readonly string? _plort;
    private readonly float _eatRate, _hoursAllowed, _slimeReach, _plortReach;
    private readonly int _maxSlimes, _maxPlorts;
    private float _nextBite, _nextDensity;
    private bool _tooDense;
    private double _poofAt = double.PositiveInfinity;
    private FeedingPatch? _goal;

    public Grazer(ItemCatalog catalog, ItemPrefab prefab, string kind)
    {
        _kind = kind;
        var eat = prefab.RootScript(kind == FeedingPatch.Ash ? "SlimeEatAsh" : "SlimeEatWater");
        _plort = catalog.ItemIdOf(catalog.Prefabs.Referenced(prefab.Id, kind == FeedingPatch.Ash ? "SlimeEatAsh" : "SlimeEatWater", "plort"));
        _eatRate = F(eat, "eatRate");
        (_slimeReach, _maxSlimes) = (F(eat, "slimeDensityDistance"), (int)F(eat, "maxSlimeDensity"));
        (_plortReach, _maxPlorts) = (F(eat, "plortDensityDistance"), (int)F(eat, "maxPlortDensity"));
        _hoursAllowed = F(prefab.RootScript("DestroyOnTouching"), "hoursOfContactAllowed");
    }

    public string Kind => _kind;
    public string? Plort => _plort;
    public float EatRate => _eatRate;
    public int Bites { get; private set; }
    public int PlortsMade { get; private set; }
    public bool TooDense => _tooDense;
    public double PoofAt => _poofAt;
    public float HoursAllowed => _hoursAllowed;

    private FeedingPatch? Touching() => Catalog.Patches.FirstOrDefault(p => p.Kind == _kind && p.Touches(Slime.GlobalPosition, Slime.Radius + 0.1f));

    public override void Tick(float delta)
    {
        var now = Catalog.Clock.TotalHours;
        var patch = Touching();
        // Its poof clock runs while it stands on anything but its patch.
        var unsafeGround = patch is null && Slime.Grounded;
        if (unsafeGround && double.IsPositiveInfinity(_poofAt) && _hoursAllowed > 0)
            _poofAt = Math.Max(now, OutsideHours.ClockStartHours) + _hoursAllowed;
        else if (!unsafeGround)
            _poofAt = double.PositiveInfinity;
        if (now >= _poofAt)
        {
            Report($"poofed after {_hoursAllowed} h away from {_kind}");
            Slime.Consume();
            return;
        }

        if (_kind == FeedingPatch.Water && Slime.Age >= _nextDensity)
        {
            _nextDensity = Slime.Age + Grazing.DensityCheckSeconds;
            var slimes = Catalog.Live.Count(a => a is SlimeActor && a != Slime && a.GlobalPosition.DistanceTo(Slime.GlobalPosition) <= _slimeReach);
            var plorts = Catalog.Live.Count(a => a.Id == _plort && a.GlobalPosition.DistanceTo(Slime.GlobalPosition) <= _plortReach);
            _tooDense = Grazing.TooDense(slimes, _maxSlimes, plorts, _maxPlorts);
        }
        if (patch is null || Slime.Age < _nextBite || Slime.Sim.Hunger <= Slime.Sim.Species.Eating.MinDriveToEat || !patch.Take())
            return;
        Bites++;
        _nextBite = Slime.Age + _eatRate;
        var eating = Slime.Sim.Species.Eating;
        Slime.Sim.Hunger = Math.Clamp(Slime.Sim.Hunger - eating.DrivePerEat, 0f, 1f);
        Slime.Sim.Agitation = Math.Clamp(Slime.Sim.Agitation - eating.AgitationPerEat, 0f, 1f);
        if (_tooDense || _plort is null)
        {
            Report($"took a bite of {_kind} (too crowded for a plort)");
            return;
        }
        Slime.GetTree().CreateTimer(Grazing.ProduceDelay, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(Slime) || Slime.Consumed)
                return;
            var up = Slime.GlobalBasis.Y.Normalized();
            var plort = Catalog.Spawn(_plort, Slime.GlobalPosition + up * Grazing.ProduceHeight);
            plort.LinearVelocity = up * Grazing.ProduceSpeed;
            plort.GrowFrom(0.001f, Grazing.ProduceGrowSeconds);
            PlortsMade++;
            Report($"ate {_kind} (every {_eatRate} s) and made {_plort}");
        };
    }

    public override float Relevancy(bool grounded)
    {
        _goal = null;
        if (Touching() is not null || double.IsPositiveInfinity(_poofAt))
            return 0f;
        _goal = Catalog.Patches.Where(p => p.Kind == _kind && p.Center.DistanceTo(Slime.GlobalPosition) < Grazing.SearchRadius)
            .OrderBy(p => p.Center.DistanceSquaredTo(Slime.GlobalPosition)).FirstOrDefault();
        return _goal is null ? 0f : Grazing.GotoRelevancy(_poofAt - Catalog.Clock.TotalHours, _hoursAllowed);
    }

    public override void Action(float delta)
    {
        if (_goal is not null && Slime.Grounded)
            Slime.Pursue(_goal.Center, null, 0f, 1f, 5f, 1f, 0f);
    }
}

/// <summary>Rock, crystal and quicksilver slimes (and their largos) hurt the player on touch (<c>DamagePlayerOnTouch</c>): <c>damagePerTouch</c> at most every <c>repeatTime</c> seconds.</summary>
public sealed class DamageOnTouch : SlimeBehaviour
{
    private readonly int _damage;
    private readonly float _repeat;
    private float _next = 0.1f; // a tenth of a second's amnesty after appearing

    public DamageOnTouch(SerializedObject data) => (_damage, _repeat) = (data["damagePerTouch"] is int d ? d : 0, F(data, "repeatTime"));

    public override void Tick(float delta)
    {
        if (Slime.Age < _next || Catalog.Player is not { } player || !GodotObject.IsInstanceValid(player) || !Slime.GetCollidingBodies().Contains(player))
            return;
        Catalog.PlayerVitals.Damage(_damage, Slime.Id);
        _next = Slime.Age + _repeat;
        Report($"hurt the player {_damage} (again after {_repeat} s)");
    }
}
