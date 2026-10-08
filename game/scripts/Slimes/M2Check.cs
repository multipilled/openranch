using System;
using System.Linq;
using Godot;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Milestone 2's end-to-end check, run without a window (--m2-check): a hungry pink slime and a carrot
/// are put down in front of the ranch's plort market; the slime has to find the carrot, eat it and make
/// a plort; the player's vacpack sucks the plort up and shoots it into the market, which has to pay
/// today's price. Meanwhile a rock slime pushed hard at the corral wall has to stay inside the corral.
/// </summary>
public sealed class M2Check
{
    private const string Slime = "PINK_SLIME", Food = "CARROT_VEGGIE", Plort = "PINK_PLORT", Penned = "ROCK_SLIME";
    private const double FeedTimeout = 60, VacTimeout = 15, SellTimeout = 8, SettleSeconds = 1.5;

    private enum Stage { Warmup, Feed, Settle, Vacuum, Aim, Shoot, Done }

    private readonly M2World _m2;
    private readonly PlayerController _player;
    private Stage _stage = Stage.Warmup;
    private double _time, _stageStart;
    private Vector3 _hole, _out, _standAt;
    private SlimeActor? _slime, _penned;
    private Actor? _food;
    private Actor? _plort;
    private string? _ate;
    private double _ateAt;
    private int? _paid, _expectedPrice;
    private float _demand;
    private string _failure = "";

    public M2Check(M2World m2, PlayerController player)
    {
        _m2 = m2;
        _player = player;
        _m2.Tool.ScriptControlled = true;
        foreach (var stand in _m2.Stands)
            stand.Sold += (id, paid) => { if (id == Plort) _paid = paid; };
    }

    public bool Passed { get; private set; }
    public string Report { get; private set; } = "";

    /// <summary>Advances one physics frame; returns true when the check is finished.</summary>
    public bool Step(double delta)
    {
        _time += delta;
        var inStage = _time - _stageStart;
        switch (_stage)
        {
            case Stage.Warmup when inStage > 0.2: // let the physics server take in the world's shapes
                return Setup() || Next(Stage.Feed);
            case Stage.Feed:
                if (_plort is not null)
                    return Next(Stage.Settle);
                return inStage > FeedTimeout && Fail(_ate is null ? "the slime didn't eat the carrot" : "no plort came out");
            case Stage.Settle when inStage > SettleSeconds:
                Teleport(_plort!.GlobalPosition + _out * 3f);
                _m2.Tool.VacHeld = true;
                return Next(Stage.Vacuum);
            case Stage.Vacuum:
                if (_m2.Pack.Count(Plort) > 0)
                {
                    _m2.Tool.VacHeld = false;
                    Teleport(_standAt);
                    return Next(Stage.Aim);
                }
                if (IsInstanceValid(_plort))
                    LookAt(_plort!.GlobalPosition);
                return inStage > VacTimeout && Fail("the vacpack didn't pick up the plort");
            case Stage.Aim:
                LookAt(_hole);
                if (inStage < 0.5)
                    return false;
                _m2.Pack.Select(Enumerable.Range(0, _m2.Pack.UsableSlots).First(i => _m2.Pack[i]?.Id == Plort));
                _expectedPrice = _m2.Market.Price(Plort);
                _demand = PlortMarket.Demand(_m2.Market.Saturation(Plort), _m2.MarketData.Get(Plort).FullSaturation);
                _m2.Tool.ShootHeld = true;
                return Next(Stage.Shoot);
            case Stage.Shoot:
                _m2.Tool.ShootHeld = false;
                if (_paid is not null)
                    return Finish();
                return inStage > SellTimeout && Fail("the market didn't take the plort");
        }
        return false;
    }

    private static bool IsInstanceValid(GodotObject? o) => o is not null && GodotObject.IsInstanceValid(o);

    private bool Next(Stage stage)
    {
        _stage = stage;
        _stageStart = _time;
        return false;
    }

    private bool Fail(string why)
    {
        _failure = why;
        return Finish();
    }

    // Finds the market's hole and the open side in front of it, puts down the slime and its food, and
    // a rock slime in the corral running at the wall.
    private bool Setup()
    {
        var stand = _m2.Stands.FirstOrDefault();
        if (stand is null)
            return Fail("no plort market was found at the ranch");
        _hole = stand.GlobalTransform * ((CollisionShape3D)stand.GetChild(0)).Position;
        var front = -stand.GlobalBasis.Z with { Y = 0 };
        foreach (var dir in new[] { front.Normalized(), -front.Normalized() })
        {
            if (Ground(_hole + dir * 4f) is not { } stand4 || Ground(_hole + dir * 8f) is not { } feedAt)
                continue;
            // The player must see the hole from where it stands.
            if (Blocked(stand4 + Vector3.Up * 1.6f, _hole))
                continue;
            _out = dir;
            _standAt = stand4;
            var side = dir.Cross(Vector3.Up).Normalized();
            _slime = (SlimeActor)_m2.Catalog.Spawn(Slime, feedAt + Vector3.Up * 0.6f);
            _slime.Sim.Hunger = 1f;
            _food = _m2.Catalog.Spawn(Food, (Ground(feedAt + side * 2f) ?? feedAt + side * 2f) + Vector3.Up * 0.3f);
            _slime.Ate += (_, food) => { _ate ??= food; _ateAt = _time; };
            _slime.Produced += (_, item) => { if (item.Id == Plort) _plort ??= item; };
            break;
        }
        if (_slime is null)
            return Fail("no open ground in front of the plort market");

        if (_m2.Sites.Corrals.FirstOrDefault() is { } corral)
        {
            var place = UnityConvert.Transform(corral.World);
            _penned = (SlimeActor)_m2.Catalog.Spawn(Penned, place * Vector3.Zero);
            var wall = (place * new Vector3(1, 0, 0) - place * Vector3.Zero) with { Y = 0 };
            _penned.LinearVelocity = wall.Normalized() * 12f + Vector3.Up * 2f;
        }
        return false;
    }

    private bool Finish()
    {
        var penned = _penned is null ? "no corral found" : InCorral(_penned) ? "stayed in" : "got out";
        var pennedOk = _penned is not null && InCorral(_penned);
        Passed = _failure.Length == 0 && pennedOk && _paid is not null && _paid == _expectedPrice;
        var entry = _m2.MarketData.Get(Plort);
        var report = $"m2-check: {Slime} ";
        report += _ate is null ? "ate nothing" : $"ate {_ate} after {_ateAt:F1} s and made {Plort}";
        if (_paid is not null)
            report += $"; the vacpack took it and the plort market bought it for {_paid} newbucks " +
                      $"(today's price {_expectedPrice}: base {entry.BaseValue} from the install x demand {_demand:F2} " +
                      $"x mood {_expectedPrice / (entry.BaseValue * _demand):F2}); wallet {_m2.Wallet.Coins}";
        report += $"; a {Penned} thrown at the corral wall {penned}";
        if (_failure.Length > 0)
        {
            report += $"; failed: {_failure}";
            if (IsInstanceValid(_slime) && IsInstanceValid(_food))
                report += $" (slime at {_slime!.GlobalPosition:F1}, hunger {_slime.Sim.Hunger:F2}; carrot at {_food!.GlobalPosition:F1})";
        }
        Report = report + (Passed ? " -> PASS" : " -> FAIL");
        _stage = Stage.Done;
        return true;
    }

    private bool InCorral(Actor actor)
    {
        if (!IsInstanceValid(actor))
            return false;
        var p = actor.GlobalPosition;
        return _m2.Sites.Corrals.Any(c => c.Contains(new System.Numerics.Vector3(p.X, p.Y, -p.Z)));
    }

    private void Teleport(Vector3 point)
    {
        var ground = Ground(point) ?? point;
        _player.GlobalPosition = ground + Vector3.Up * 0.05f;
        _player.Velocity = Vector3.Zero;
    }

    // Turns the player and its camera to look straight at a point.
    private void LookAt(Vector3 target)
    {
        for (var i = 0; i < 2; i++) // the camera sits a little ahead of the body's centre, so refine once
        {
            var d = target - _player.Camera.GlobalPosition;
            var yaw = Mathf.Atan2(-d.X, -d.Z);
            var pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
            _player.Look(Mathf.RadToDeg(yaw), Mathf.RadToDeg(pitch));
        }
    }

    private Vector3? Ground(Vector3 near)
    {
        var space = _m2.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(near + Vector3.Up * 4f, near + Vector3.Down * 10f, Actor.WorldLayer,
            new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = space.IntersectRay(query);
        return hit.Count > 0 ? (Vector3)hit["position"] : null;
    }

    private bool Blocked(Vector3 from, Vector3 to)
    {
        var space = _m2.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Actor.WorldLayer, new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = space.IntersectRay(query);
        return hit.Count > 0 && ((Vector3)hit["position"]).DistanceTo(to) > 0.7f;
    }
}
