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
        _slime.Sim.Agitation = 1; // an angry feral, so going for the player matters
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
        if (_stompHits.All(h => h == 0) || _bites == 0)
        {
            if (Zoo.HoldPlayer(this, Center + new Vector3(4.5f, 0, 4.5f)) && _healthAtStart == 0)
                _healthAtStart = Catalog.PlayerVitals.Health;
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
        var stompOk = _stompHits.Any(h => h > 0) && _stompHits.All(h => h == 0 || (h >= F("minPlayerDamage") && h <= F("maxPlayerDamage")));
        if (_wasFeral && _fullYetEats && stompOk && _biteDamage == attackDamage * _bites && !_slime.Sim.IsFeral)
            Pass(line);
        else
            Fail(line);
    }

    public override string TimedOut() => Outcome;
}
