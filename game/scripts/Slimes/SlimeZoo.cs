using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Milestone 5's slime zoo (--slime-zoo): every slime and largo in the install, each in its own
/// walled pen on a cleared floor over The Ranch's corral, hungry, with a food from its diet (its
/// favourite when it has one). One more pen holds a pink slime and a rock plort: the pink has to
/// become the pink-rock largo, which is then given a tabby plort and has to become a tarr.
/// Without --screenshot it is a check: it passes once every slime that makes plorts has eaten its
/// food and made the plorts its diet says, and the largo and tarr have formed; then it quits.
/// --zoo-focus ID points the camera at that slime's pen instead of the whole zoo.
/// Beside the eating pens, bigger pens hold one <see cref="ZooTrial"/> each: every gordo fed until it
/// bursts, and the abilities and behaviours of docs/behavior/slime-abilities.md. --zoo-part NAMES (a
/// comma-separated list of "eat", "gordos" and the trial groups of <see cref="BehaviourTrials"/>) runs only those parts.
/// The floor, pen size and wall height are openranch's own test set-up, not the original's.
/// </summary>
public partial class SlimeZoo : Node3D
{
    private const double RefeedSeconds = 60, EveningHour = 20;
    private const float PenSize = 4f, TrialPenSize = 14f, WallHeight = 40f, WallThickness = 0.2f, TrialWallThickness = 1f, TimeoutSeconds = 300;
    private const string FormingSlime = "PINK_SLIME", FirstPlort = "ROCK_PLORT", ThirdPlort = "TABBY_PLORT";

    private sealed class Pen
    {
        public required string Slime;
        public required Vector3 Center;
        public string? Food;
        public List<string> Expected = [];
        public bool Ate;
        public List<string> Made = [];
        public SlimeActor? Actor;
        public double FedAt;
        public List<Actor> Plorts = [];
        public bool Cleared;
        public bool Done => Food is null || (Ate && Expected.GroupBy(e => e).All(g => Made.Count(m => m == g.Key) >= g.Count()));
    }

    private readonly M2World _m2;
    private readonly PlayerController _player;
    private readonly bool _check;
    private readonly string? _focus;
    private readonly List<Pen> _pens = [];
    private readonly List<ZooTrial> _trials = [];
    private readonly HashSet<string>? _parts;
    private Pen? _formingPen;
    private string? _largo, _tarr;
    private double _time, _builtAt = -1, _largoAt, _tarrAt;
    // The trial the player stands in for now (trials that need the player take turns).
    private ZooTrial? _playerHolder;
    private Vector3 _heldAt;

    public SlimeZoo(M2World m2, PlayerController player, string[] args)
    {
        Name = "SlimeZoo";
        _m2 = m2;
        _player = player;
        _check = Array.IndexOf(args, "--screenshot") < 0;
        _focus = Array.IndexOf(args, "--zoo-focus") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        _parts = Array.IndexOf(args, "--zoo-part") is var j and >= 0 && j + 1 < args.Length
            ? args[j + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        _m2.Catalog.Player = player;
        _m2.Catalog.Wallet ??= _m2.Wallet;
    }

    public ItemCatalog Catalog => _m2.Catalog;
    /// <summary>Seconds since the zoo was built.</summary>
    public double Elapsed => _builtAt < 0 ? 0 : _time - _builtAt;

    private bool Runs(string part) => _parts is null || _parts.Contains(part);

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_builtAt < 0)
        {
            if (_time > 0.3) // let the physics server take in the world's shapes first
                Build();
            return;
        }
        foreach (var trial in _trials.Where(t => !t.Done))
            trial.Step(delta);
        Refeed();
        if (_playerHolder is { Done: true })
            _playerHolder = null;
        // Pinned in place every step: a kinematic body moved by setting its position keeps that jump as
        // its velocity and flings whatever lands on it (a feral largo left at 1000+ m/s).
        if (_playerHolder is not null)
        {
            _player.GlobalPosition = _heldAt;
            _player.Velocity = Vector3.Zero;
        }
        if (!_check)
            return;
        var eatDone = !Runs("eat") || (_pens.All(p => p.Done) && _largo is not null && _tarr is not null);
        var passed = eatDone && _trials.All(t => t.Done && !t.Failed);
        var over = eatDone && _trials.All(t => t.Done);
        if (passed || over || _time - _builtAt > TimeoutSeconds)
        {
            GD.Print(Report(passed));
            GetTree().Quit(passed ? 0 : 1);
            SetPhysicsProcess(false);
        }
    }

    private void Build()
    {
        _builtAt = _time;
        // Evening: night-only slimes (phosphors and their largos) last until dawn, so the eating pens
        // can check them; the phosphor trial moves the clock to dawn itself. openranch's test set-up.
        if (WorldTime is { } world)
        {
            world.Set(OpenRanch.Ranch.WorldClock.At(world.Clock.Day, EveningHour));
            Catalog.Clock.Set(world.Ranch.WorldTime / OpenRanch.Ranch.WorldClock.SecondsPerHour); // at once, before any slime looks
        }
        var slimes = Runs("eat")
            ? Catalog.Slimes.Slimes
                .Where(s => s.Eating is not null && Catalog.Prefabs.Has(s.Id) && Catalog.Species(s.Id) is not null)
                .OrderBy(s => s.IsLargo).ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList()
            : [];
        var count = Runs("eat") ? slimes.Count + 1 : 0;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        var rows = (count + columns - 1) / columns;
        _trials.AddRange(MakeTrials());
        var trialColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_trials.Count)));
        var trialRows = (_trials.Count + trialColumns - 1) / trialColumns;

        // Over the ranch's corral, on a floor just above the highest ground under it.
        var center = _m2.Sites.Corrals.FirstOrDefault() is { } corral
            ? UnityConvert.Transform(corral.World).Origin
            : _player.GlobalPosition;
        var (eatWidth, eatDepth) = (count == 0 ? 0 : columns * PenSize, rows * PenSize);
        var (trialWidth, trialDepth) = (_trials.Count == 0 ? 0 : trialColumns * TrialPenSize, trialRows * TrialPenSize);
        var (width, depth) = (eatWidth + trialWidth, Math.Max(eatDepth, trialDepth));
        var top = float.NegativeInfinity;
        for (var x = -width / 2; x <= width / 2; x += 1f)
            for (var z = -depth / 2; z <= depth / 2; z += 1f)
            {
                var from = center + new Vector3(x, 150, z);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 300, Actor.WorldLayer));
                if (hit.Count > 0)
                    top = Math.Max(top, ((Vector3)hit["position"]).Y);
            }
        center.Y = float.IsFinite(top) ? top + 0.05f : center.Y;
        BuildFloor(center, width, depth);
        var eatCorner = center + new Vector3(-width / 2, 0, -depth / 2);
        var trialCorner = eatCorner + new Vector3(eatWidth, 0, 0);
        if (count > 0)
            BuildWalls(eatCorner, PenSize, columns, rows, WallThickness);
        if (_trials.Count > 0)
            BuildWalls(trialCorner, TrialPenSize, trialColumns, trialRows, TrialWallThickness); // thick: leaping slimes tunnel through thin ones

        Vector3 PenCenter(int i) => eatCorner + new Vector3((i % columns + 0.5f) * PenSize, 0, (i / columns + 0.5f) * PenSize);
        for (var i = 0; i < slimes.Count; i++)
        {
            var pen = new Pen { Slime = slimes[i].Id, Center = PenCenter(i) };
            var species = Catalog.Species(pen.Slime)!;
            if (FoodFor(species) is { } food)
            {
                pen.Food = food.Food;
                pen.Expected = food.Produces.SelectMany(p => Enumerable.Repeat(p, food.CountEach)).ToList();
                Place(food.Food, pen.Center + new Vector3(1.1f, 0, 0));
            }
            var slime = (SlimeActor)Place(pen.Slime, pen.Center + new Vector3(-0.7f, 0, 0));
            pen.Actor = slime;
            pen.FedAt = _time;
            slime.Sim.Hunger = 1; // hungry from the start, so the check doesn't wait hours of game time
            slime.Ate += (_, eaten) => pen.Ate |= eaten == pen.Food;
            slime.Produced += (_, item) => { pen.Made.Add(item.Id); pen.Plorts.Add(item); };
            _pens.Add(pen);
        }

        for (var i = 0; i < _trials.Count; i++)
            _trials[i].Start(this, trialCorner + new Vector3((i % trialColumns + 0.5f) * TrialPenSize, 0, (i / trialColumns + 0.5f) * TrialPenSize));

        AimCamera(center, width, depth);
        GD.Print($"slime-zoo: {slimes.Count} slimes ({slimes.Count(s => s.IsLargo)} largos) in {columns} x {rows} pens, " +
                 $"{_trials.Count} trials, floor at {center.Y:F1}, {_pens.Count(p => p.Food is not null)} with food");
        if (!Runs("eat"))
            return;

        // The largo and tarr pen: not hungry, so the plorts are all it goes for.
        _formingPen = new Pen { Slime = FormingSlime, Center = PenCenter(slimes.Count) };
        var forming = (SlimeActor)Place(FormingSlime, _formingPen.Center + new Vector3(-0.8f, 0, 0));
        forming.Sim.Hunger = 0;
        Place(FirstPlort, _formingPen.Center + new Vector3(0.8f, 0, 0));
        forming.Transformed += (_, largo) =>
        {
            _largo = largo.Id;
            _largoAt = _time - _builtAt;
            var away = (_formingPen.Center - largo.GlobalPosition) with { Y = 0 };
            away = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Right;
            Place(ThirdPlort, _formingPen.Center + away * 1.4f);
            largo.Transformed += (_, tarr) => (_tarr, _tarrAt) = (tarr.Id, _time - _builtAt);
        };
    }

    // openranch's test harness: a slime that hasn't eaten its food within a minute (stalkers in a 4 m pen
    // can bat it out of reach, or carry it off) gets a fresh one dropped on it.
    private void Refeed()
    {
        // Finished pens lose their plorts, so stalkers in the trial pens don't spend the run after them
        // (plorts carry extra drive for every slime). openranch's own test housekeeping.
        foreach (var pen in _pens.Where(p => p.Done && !p.Cleared))
        {
            pen.Cleared = true;
            foreach (var plort in pen.Plorts.Where(p => IsInstanceValid(p) && !p.Consumed && p.CaughtBy is null))
                plort.Consume();
        }
        foreach (var pen in _pens)
        {
            if (pen.Ate || pen.Food is null || pen.Actor is not { } s || !IsInstanceValid(s) || s.Consumed || _time - pen.FedAt < RefeedSeconds)
                continue;
            pen.FedAt = _time;
            var food = Catalog.Spawn(pen.Food, s.GlobalPosition + Vector3.Up * (s.Radius + 0.6f));
            food.LinearVelocity = Vector3.Zero;
        }
    }

    // The trials for the parts asked for (all of them by default).
    private IEnumerable<ZooTrial> MakeTrials()
    {
        if (Runs("gordos"))
            foreach (var gordo in Catalog.Gordos.Gordos.Where(g => Catalog.Prefabs.Has(g.Id)).OrderBy(g => g.Id, StringComparer.Ordinal))
                yield return new GordoTrial(gordo.Id);
        foreach (var trial in BehaviourTrials.All(Catalog))
            if (Runs(trial.Group))
                yield return trial;
    }

    // Its favourite food when it has one, otherwise the first of its foods by name; never a slime or
    // the always-wanted tofu, and only foods that have a prefab to spawn.
    private FoodEffect? FoodFor(SlimeSpecies species)
    {
        var foods = species.Foods
            .Where(f => f.Becomes is null && f.Food != SlimeSpecies.AlwaysWantedFood && !Items.IsSlime(f.Food) && Catalog.Prefabs.Has(f.Food))
            .OrderBy(f => f.Food, StringComparer.Ordinal)
            .ToList();
        return foods.FirstOrDefault(f => f.IsFavorite) ?? foods.FirstOrDefault();
    }

    /// <summary>
    /// Lends the player to <paramref name="trial"/>, standing at <paramref name="onFloor"/> and held still,
    /// unless another trial has it; then returns false and the trial asks again later. The player is
    /// handed back when the trial is done or calls <see cref="ReleasePlayer"/>.
    /// </summary>
    public bool HoldPlayer(ZooTrial trial, Vector3 onFloor)
    {
        if (_playerHolder is not null && _playerHolder != trial)
            return false;
        if (_playerHolder is null)
        {
            _heldAt = onFloor + Vector3.Up * 0.05f;
            _player.GlobalPosition = _heldAt;
            _player.Velocity = Vector3.Zero;
        }
        _playerHolder = trial;
        return true;
    }

    public void ReleasePlayer(ZooTrial trial)
    {
        if (_playerHolder == trial)
            _playerHolder = null;
    }

    /// <summary>Whether everything but <paramref name="trial"/> is done (eating pens and the other trials).</summary>
    public bool OthersDone(ZooTrial trial) =>
        (!Runs("eat") || (_pens.All(p => p.Done) && _largo is not null && _tarr is not null)) && _trials.All(t => t == trial || t.Done);

    /// <summary>The world's clock, for trials that need another time of day.</summary>
    public WorldTime? WorldTime => _worldTime ??= Find<WorldTime>(GetTree().Root);
    private WorldTime? _worldTime;

    private static T? Find<T>(Node node) where T : class
    {
        if (node is T found)
            return found;
        foreach (var child in node.GetChildren())
            if (Find<T>(child) is { } inner)
                return inner;
        return null;
    }

    /// <summary>Puts an item on the zoo's floor at <paramref name="onFloor"/>, resting on it.</summary>
    public Actor Place(string id, Vector3 onFloor)
    {
        var actor = Catalog.Spawn(id, onFloor);
        actor.GlobalPosition = onFloor + Vector3.Up * (actor.Radius + 0.05f);
        return actor;
    }

    // A solid floor everything stands on.
    private void BuildFloor(Vector3 center, float width, float depth)
    {
        var floor = new StaticBody3D { Name = "ZooFloor", CollisionLayer = Actor.WorldLayer, CollisionMask = 0 };
        var size = new Vector3(width + 2, 1, depth + 2);
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        floor.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.6f, 0.5f), Roughness = 1 },
        });
        floor.Position = center + Vector3.Down * 0.5f;
        AddChild(floor);
    }

    // Items-only walls between pens of one grid, from its corner (lowest x and z).
    private void BuildWalls(Vector3 corner, float penSize, int columns, int rows, float thickness)
    {
        var (width, depth) = (columns * penSize, rows * penSize);
        var walls = new StaticBody3D { Name = "ZooWalls", CollisionLayer = Actor.PenWallLayer, CollisionMask = 0 };
        void Wall(Vector3 at, Vector3 wallSize) =>
            walls.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = wallSize }, Position = at + Vector3.Up * (WallHeight / 2) });
        for (var c = 0; c <= columns; c++)
            Wall(corner + new Vector3(c * penSize, 0, depth / 2), new Vector3(thickness, WallHeight, depth));
        for (var r = 0; r <= rows; r++)
            Wall(corner + new Vector3(width / 2, 0, r * penSize), new Vector3(width, WallHeight, thickness));
        AddChild(walls);
    }

    // Holds the player still above the zoo, looking down at it (or at one pen with --zoo-focus).
    private void AimCamera(Vector3 center, float width, float depth)
    {
        _player.SetPhysicsProcess(false);
        Vector3 eye, target;
        if (_focus is not null && _pens.FirstOrDefault(p => p.Slime == _focus) is { } pen)
            (eye, target) = (pen.Center + new Vector3(1.2f, 4f, -3.6f), pen.Center + Vector3.Up * 0.5f); // slimes face -Z
        else if (_focus is not null && _trials.FirstOrDefault(t => t.Name == _focus) is { } trial)
            (eye, target) = (trial.PenCenter + new Vector3(3f, 9f, -10f), trial.PenCenter + Vector3.Up * 1.5f);
        else
            (eye, target) = (center + new Vector3(0, Math.Max(width, depth) * 0.75f, depth * 0.85f), center);
        _player.GlobalPosition = eye - (_player.Camera.GlobalPosition - _player.GlobalPosition);
        for (var i = 0; i < 2; i++) // the camera sits a little off the body's centre, so refine once
        {
            var d = target - _player.Camera.GlobalPosition;
            _player.Look(Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z)), Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())));
        }
    }

    private string Report(bool passed)
    {
        var b = new StringBuilder();
        var fed = _pens.Where(p => p.Food is not null).ToList();
        b.AppendLine(CultureInfo.InvariantCulture, $"slime-zoo after {_time - _builtAt:F1} s: {fed.Count(p => p.Done)}/{fed.Count} fed slimes ate and made their plorts");
        foreach (var p in _pens)
        {
            var state = p.Food is null ? "no food in its diet with a prefab" : p.Done ? "ok" : "NOT DONE";
            b.AppendLine($"  {p.Slime,-24} {p.Food ?? "-",-16} ate={p.Ate,-5} made [{string.Join(",", p.Made)}] expected [{string.Join(",", p.Expected)}] {state}");
        }
        b.AppendLine(CultureInfo.InvariantCulture, $"  largo: {FormingSlime} + {FirstPlort} -> {_largo ?? "none"}{(_largo is null ? "" : $" at {_largoAt:F1} s")}");
        b.AppendLine(CultureInfo.InvariantCulture, $"  tarr: {_largo ?? "largo"} + {ThirdPlort} -> {_tarr ?? "none"}{(_tarr is null ? "" : $" at {_tarrAt:F1} s")}");
        b.AppendLine(CultureInfo.InvariantCulture, $"slime-zoo trials: {_trials.Count(t => t.Done && !t.Failed)}/{_trials.Count} passed");
        foreach (var t in _trials)
            b.AppendLine($"  {t.Name,-24} {(t.Done ? t.Failed ? "FAIL" : "ok" : "NOT DONE"),-8} {(t.Done ? t.Outcome : t.TimedOut())}");
        b.Append(passed ? "slime-zoo PASS" : "slime-zoo FAIL");
        return b.ToString();
    }
}
