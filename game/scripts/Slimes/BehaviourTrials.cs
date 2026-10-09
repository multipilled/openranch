using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// The slime zoo's trials for feral slimes, abilities and feeding habits (one pen each). Each names
/// its --zoo-part group. Slime choices and starting moods are openranch's test set-ups; what is
/// checked comes from the prefab data and the rules in docs/behavior.
/// </summary>
public static class BehaviourTrials
{
    public static IEnumerable<ZooTrial> All(ItemCatalog catalog)
    {
        yield return new FeralTrial();
        foreach (var trial in AbilityTrials.All(catalog))
            yield return trial;
        // Last: it waits for everything else, then moves the clock to dawn.
        yield return new PhosphorTrial();
    }

    /// <summary>A food from the slime's diet that has a prefab (its favourite first), or null.</summary>
    public static string? DietFood(ItemCatalog catalog, string slime) =>
        catalog.Species(slime)?.Foods
            .Where(f => f.Becomes is null && f.Food != SlimeSpecies.AlwaysWantedFood && !Items.IsSlime(f.Food) && catalog.Prefabs.Has(f.Food))
            .OrderBy(f => f.IsFavorite ? 0 : 1).ThenBy(f => f.Food, StringComparer.Ordinal)
            .Select(f => f.Food).FirstOrDefault();
}

/// <summary>
/// A feral largo with the player in its pen (docs/behavior/feral-slimes.md): it must be feral, eat
/// even when full, leap and stomp at the player from 5-20 m (an explosion with its prefab's
/// FeralSlimeButtstomp numbers), chase and bite the player for AttackPlayer.damagePerAttack, and stop
/// being feral once it eats.
/// </summary>
public sealed class FeralTrial : ZooTrial
{
    private const string Largo = "PINK_HONEY_LARGO"; // a largo with no traits of its own to get in the way
    private SlimeActor? _slime;
    private string? _food;
    private int _stomps, _stompDamage, _biteDamage, _bites;
    private readonly List<int> _stompHits = [];
    private string? _fell, _lastDoing, _spike;
    private Vector3 _lastPos, _lastVelocity;
    private float _healthAtStart;
    private bool _wasFeral, _fullYetEats, _fed;
    private readonly List<string> _notes = [];

    public FeralTrial() : base("FERAL", "feral") { }

    protected override void Build()
    {
        _slime = (SlimeActor)Place(Largo, Center + new Vector3(-4.5f, 0, -4.5f));
        if (_slime.Feral is not { } feral || _slime.Behaviour<FeralStompBehaviour>() is not { } stomp || _slime.Behaviour<AttackPlayer>() is not { } attack)
        {
            Fail($"{Largo} has no feral parts");
            return;
        }
        _slime.Sim.Hunger = 0;
        // Calm first, so going for the player (driven by agitation) doesn't matter and it stomps; then
        // angry, so it chases and bites (GotoPlayer's 0.95 mostly beats the stomp's 0.3-1).
        _slime.Sim.Agitation = 0;
        feral.SetFeral(Catalog.Clock.TotalHours);
        _wasFeral = _slime.Sim.IsFeral;
        _food = BehaviourTrials.DietFood(Catalog, Largo);
        _fullYetEats = _food is not null && _slime.Sim.WillEat(_food) && !new Slime(_slime.Sim.Species) { Hunger = 0 }.WillEat(_food);
        stomp.Stomped += r => { _stomps++; _stompDamage += r.PlayerDamage; _stompHits.Add(r.PlayerDamage); };
        Catalog.PlayerVitals.Damaged += (loss, source) =>
        {
            if (source == Largo && attack.Bites > _bites)
                (_bites, _biteDamage) = (attack.Bites, _biteDamage + loss);
        };
        _slime.Ate += (_, _) => _fed = true;
        Outcome = "waiting for the player";
    }

    public override void Step(double delta)
    {
        if (Done || _slime is null)
            return;
        if (!GodotObject.IsInstanceValid(_slime) || _slime.Consumed)
        {
            Fail("the feral largo is gone");
            return;
        }
        // Diagnostics: where and how it left the floor, if it does.
        if (_fell is null && _slime.GlobalPosition.Y < Center.Y - 3)
            _fell = $"fell off at {Time:F1} s from {_lastPos - Center:F1} (pen-relative) doing {_lastDoing}, velocity {_lastVelocity:F1}, stomps {_stomps}";
        if (_spike is null && _slime.LinearVelocity.Length() > 40)
            _spike = $"speed {_slime.LinearVelocity.Length():F0} at {Time:F2} s (was {_lastVelocity.Length():F1}) doing {_slime.Doing}/{_slime.Behaviour<FeralStompBehaviour>()?.Phase}, " +
                     $"touching [{string.Join(",", _slime.GetCollidingBodies().Select(b => b.Name.ToString()))}], at {_slime.GlobalPosition - Center:F1}";
        (_lastPos, _lastDoing, _lastVelocity) = (_slime.GlobalPosition, _slime.Doing, _slime.LinearVelocity);
        if (_stomps == 0 || _bites == 0)
        {
            if (Zoo.HoldPlayer(this, Center + new Vector3(4.5f, 0, 4.5f)) && _healthAtStart == 0)
                _healthAtStart = Catalog.PlayerVitals.Health;
            if (_stomps > 0)
                _slime.Sim.Agitation = 1;
            Outcome = $"feral={_slime.Sim.IsFeral} stomps={_stomps} bites={_bites}";
            return;
        }
        if (_food is not null && !_fed && !_notes.Contains("fed"))
        {
            Zoo.ReleasePlayer(this);
            _notes.Add("fed");
            Place(_food, _slime.GlobalPosition + Vector3.Up * (_slime.Radius + 0.6f)).LinearVelocity = Vector3.Zero;
            return;
        }
        if (!_fed)
            return;
        var stomp = _slime.Prefab.RootScript("FeralSlimeButtstomp")!;
        var attackDamage = _slime.Prefab.RootScript("AttackPlayer")!["damagePerAttack"] is int d ? d : -1;
        float F(string field) => stomp[field] is float v ? v : float.NaN;
        var line = $"{Largo}: feral={_wasFeral}, eats {_food} when full={_fullYetEats}, {_stomps} stomp(s) hurt {_stompDamage} " +
                   $"(range {F("minPlayerDamage")}-{F("maxPlayerDamage")} each), {_bites} bite(s) hurt {_biteDamage} ({attackDamage} each), " +
                   $"feral after eating={_slime.Sim.IsFeral}";
        // A stomp that lands out of the player's reach does no harm; one in reach does min..max.
        var stompOk = _stompHits.All(h => h == 0 || (h >= F("minPlayerDamage") && h <= F("maxPlayerDamage")));
        if (_wasFeral && _fullYetEats && stompOk && _biteDamage == attackDamage * _bites && !_slime.Sim.IsFeral)
            Pass(line);
        else
            Fail(line);
    }

    public override string TimedOut() => _slime is null || !GodotObject.IsInstanceValid(_slime) ? Outcome
        : $"{Outcome}; doing {_slime.Doing}, agitation {_slime.Sim.Agitation:F2}, grounded={_slime.Grounded}, " +
          $"{(Catalog.Player is { } p ? _slime.GlobalPosition.DistanceTo(p.GlobalPosition) : -1):F1} m from the player, " +
          $"{_slime.GlobalPosition.DistanceTo(Center):F1} m from the pen centre, stomp damage [{string.Join(",", _stompHits)}]; {_fell ?? "never fell"}; {_spike ?? "no speed spike"}";
}

/// <summary>
/// Phosphors at dawn (docs/behavior/slime-traits.md, "Night only"): once everything else in the zoo is
/// done, the world clock jumps to 5:40 the next morning and runs six times faster. One phosphor stands
/// in the open, one in a cave (half the pen counts as a cave, behind an items-only divider). The open
/// one must vanish at 6:00 plus its data's endurance; the cave one must stay until it leaves the cave,
/// then vanish its endurance later.
/// </summary>
public sealed class PhosphorTrial : ZooTrial
{
    private const string Phosphor = "PHOSPHOR_SLIME";
    private const double JumpToHour = 5 + 40 / 60.0, Speed = 6; // openranch's test pace
    private SlimeActor? _open, _caved;
    private bool _jumped, _caveOpen = true;
    private double _openGone = -1, _cavedLeft = -1, _cavedGone = -1, _previousSpeed = 1, _openShutdown, _cavedShutdownInCave;
    private HoursWindow? _window;

    public PhosphorTrial() : base("PHOSPHOR", "phosphor") { }

    protected override void Build() => Outcome = "waiting for the rest of the zoo";

    public override void Step(double delta)
    {
        if (Done)
            return;
        var now = Catalog.Clock.TotalHours;
        if (!_jumped)
        {
            if (!Zoo.OthersDone(this))
                return;
            if (Zoo.WorldTime is not { } world)
            {
                Fail("no world clock to move");
                return;
            }
            _jumped = true;
            _previousSpeed = world.Speed;
            world.Set(OpenRanch.Ranch.WorldClock.At(world.Clock.Day + 1, JumpToHour));
            world.Speed = Speed;
            Outcome = "clock moved to 5:40";
            return;
        }
        if (_open is null)
        {
            // The clock moved last step; the slimes appear now, at night.
            var divider = new StaticBody3D { Name = "PhosphorDivider", CollisionLayer = Actor.PenWallLayer, CollisionMask = 0 };
            divider.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.2f, 40, 14) }, Position = Center + Vector3.Up * 20 });
            Zoo.AddChild(divider);
            var cave = Center.X;
            var previous = Catalog.InCave;
            Catalog.InCave = p => (_caveOpen && p.X > cave && Math.Abs(p.Z - Center.Z) < 7) || (previous?.Invoke(p) ?? false);
            _open = (SlimeActor)Place(Phosphor, Center + new Vector3(-3.5f, 0, 0));
            _caved = (SlimeActor)Place(Phosphor, Center + new Vector3(3.5f, 0, 0));
            _window = _open.Behaviour<NightOnly>()?.Window;
            if (_window is null)
            {
                Fail($"{Phosphor} has no DestroyOutsideHoursOfDay");
                return;
            }
            _open.Behaviour<NightOnly>()!.Vanishing += h => _openGone = h;
            _caved.Behaviour<NightOnly>()!.Vanishing += h => _cavedGone = h;
            return;
        }
        if (_openShutdown == 0 && GodotObject.IsInstanceValid(_open))
        {
            _openShutdown = _open.Behaviour<NightOnly>()!.ShutdownAt;
            _cavedShutdownInCave = _caved!.Behaviour<NightOnly>()!.ShutdownAt;
        }
        if (_openGone >= 0 && _cavedLeft < 0)
        {
            _caveOpen = false; // the cave one leaves its cave
            _cavedLeft = now;
        }
        Outcome = $"{Hour(now)}: open {(_openGone < 0 ? "here" : "gone at " + Hour(_openGone))}, cave {(_cavedGone < 0 ? "here" : "gone at " + Hour(_cavedGone))}";
        if (_cavedGone < 0)
            return;

        if (Zoo.WorldTime is { } w)
            w.Speed = _previousSpeed;
        var dawn = OutsideHours.NextHour(_openGone - 1, _window!.EndHour);
        var (min, max) = (_window.MinEndureHours, _window.MaxEndureHours);
        const double step = 0.01; // a physics step at this pace is well under a hundredth of an hour
        var openOk = _openGone >= dawn + min - step && _openGone <= dawn + max + step;
        var cavedAfter = _cavedGone - _cavedLeft;
        var caveOk = double.IsPositiveInfinity(_cavedShutdownInCave) && _cavedLeft >= _openGone && cavedAfter >= min - step && cavedAfter <= max + step;
        var line = $"{Phosphor}: window {_window.StartHour}-{_window.EndHour}, endures {min}-{max} h; open-air one vanished at {Hour(_openGone)} " +
                   $"(dawn + {_openGone - dawn:F3} h), the cave one stayed (no clock in the cave), left at {Hour(_cavedLeft)} and vanished {cavedAfter:F3} h later";
        if (openOk && caveOk)
            Pass(line);
        else
            Fail(line);
    }

    private static string Hour(double total)
    {
        var h = total % 24;
        return $"{(int)h}:{(int)((h - (int)h) * 60):00}";
    }
}
