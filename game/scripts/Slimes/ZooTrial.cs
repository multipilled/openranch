using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// One check in its own pen of the slime zoo (<see cref="SlimeZoo"/>): a gordo to feed until it
/// bursts, an ability to see fire, a behaviour to see happen. The zoo builds the pen, calls
/// <see cref="Build"/> once and <see cref="Step"/> every physics step, and passes once every trial
/// is <see cref="Done"/> and none <see cref="Failed"/>. The set-ups (pens, foods, starting moods) are
/// openranch's own test scenes; what the trial checks comes from the game's data and rules.
/// </summary>
public abstract class ZooTrial
{
    protected ZooTrial(string name) => Name = name;

    public string Name { get; }
    public bool Done { get; protected set; }
    public bool Failed { get; protected set; }
    /// <summary>One line on what happened, for the report.</summary>
    public string Outcome { get; protected set; } = "not started";

    protected SlimeZoo Zoo { get; private set; } = null!;
    protected ItemCatalog Catalog => Zoo.Catalog;
    protected Vector3 Center { get; private set; }
    public Vector3 PenCenter => Center;
    /// <summary>Seconds since the zoo was built.</summary>
    protected double Time => Zoo.Elapsed;

    public void Start(SlimeZoo zoo, Vector3 center)
    {
        Zoo = zoo;
        Center = center;
        Build();
    }

    protected abstract void Build();
    public virtual void Step(double delta) { }

    protected void Pass(string outcome)
    {
        if (Done)
            return;
        Done = true;
        Outcome = outcome;
    }

    protected void Fail(string outcome)
    {
        if (Done)
            return;
        Done = true;
        Failed = true;
        Outcome = outcome;
    }

    /// <summary>The trial's line when the zoo times out before it is done.</summary>
    public virtual string TimedOut() => Outcome;

    protected Actor Place(string id, Vector3 onFloor) => Zoo.Place(id, onFloor);

    protected static string Counted(IEnumerable<string> ids) =>
        string.Join(", ", ids.GroupBy(i => i).OrderBy(g => g.Key, System.StringComparer.Ordinal).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));
}

/// <summary>
/// A gordo fed its diet's food (a favourite when it has one) at its mouth until it bursts: it must
/// eat exactly its data's target count, burst two seconds after the last meal, and drop its data's
/// rewards plus its own slime in every spawn point left over (13 in all).
/// </summary>
public sealed class GordoTrial : ZooTrial
{
    private const double FeedEverySeconds = 0.1; // openranch's test pace
    private readonly string _gordo;
    private GordoActor? _actor;
    private string? _food;
    private int _meals, _counted;
    private double _nextFeed, _fullAt = -1;

    public GordoTrial(string gordo) : base(gordo) => _gordo = gordo;

    protected override void Build()
    {
        _actor = Catalog.SpawnGordo(_gordo, Center);
        var diet = _actor.Sim;
        _food = Catalog.Species(diet.Info.Slime)?.Foods
            .Where(f => f.Becomes is null && f.Food != SlimeSpecies.AlwaysWantedFood && !Items.IsSlime(f.Food) && Catalog.Prefabs.Has(f.Food) && diet.WillEat(f.Food))
            .OrderBy(f => f.IsFavorite ? 0 : 1).ThenBy(f => f.Food, System.StringComparer.Ordinal)
            .Select(f => f.Food).FirstOrDefault();
        _actor.Ate += (_, _, count) => { _meals++; _counted += count; };
        _actor.Straining += _ => _fullAt = Time;
        _actor.Burst += (g, spawned) => Check(g, spawned.Select(a => a.Id).ToList());
        if (_food is null)
            Fail($"no food with a prefab in {diet.Info.Slime}'s diet");
        Outcome = $"feeding {_food}";
    }

    public override void Step(double delta)
    {
        if (Done || _actor is null || _food is null || !GodotObject.IsInstanceValid(_actor) || _actor.Sim.Full || Time < _nextFeed)
            return;
        _nextFeed = Time + FeedEverySeconds;
        var food = Catalog.Spawn(_food, _actor.Mouth);
        food.LinearVelocity = Vector3.Zero;
    }

    private void Check(GordoActor gordo, List<string> spawned)
    {
        var info = gordo.Sim.Info;
        var expected = info.Rewards.Concat(Enumerable.Repeat(info.FillSlime ?? "", Gordo.SpawnPoints - info.Rewards.Count)).ToList();
        var delay = Time - _fullAt;
        var line = $"ate {_counted}/{info.TargetCount} in {_meals} meals of {_food}, burst {delay:F1} s after the last; " +
                   $"dropped [{Counted(spawned)}] expected [{Counted(expected)}]";
        var ok = _counted >= info.TargetCount && _counted - info.TargetCount < Math.Max(1, _counted / Math.Max(1, _meals))
                 && Math.Abs(delay - Gordo.BurstDelaySeconds) < 0.2
                 && Counted(spawned) == Counted(expected);
        if (ok)
            Pass(line);
        else
            Fail(line);
    }

    public override string TimedOut() => $"ate {_counted}/{_actor?.Sim.TargetCount} in {_meals} meals of {_food}; never burst";
}
