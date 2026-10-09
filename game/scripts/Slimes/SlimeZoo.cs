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
/// The floor, pen size and wall height are openranch's own test set-up, not the original's.
/// </summary>
public partial class SlimeZoo : Node3D
{
    private const float PenSize = 4f, WallHeight = 40f, WallThickness = 0.2f, TimeoutSeconds = 180;
    private const string FormingSlime = "PINK_SLIME", FirstPlort = "ROCK_PLORT", ThirdPlort = "TABBY_PLORT";

    private sealed class Pen
    {
        public required string Slime;
        public required Vector3 Center;
        public string? Food;
        public List<string> Expected = [];
        public bool Ate;
        public List<string> Made = [];
        public bool Done => Food is null || (Ate && Expected.GroupBy(e => e).All(g => Made.Count(m => m == g.Key) >= g.Count()));
    }

    private readonly M2World _m2;
    private readonly PlayerController _player;
    private readonly bool _check;
    private readonly string? _focus;
    private readonly List<Pen> _pens = [];
    private Pen? _formingPen;
    private string? _largo, _tarr;
    private double _time, _builtAt = -1, _largoAt, _tarrAt;

    public SlimeZoo(M2World m2, PlayerController player, string[] args)
    {
        Name = "SlimeZoo";
        _m2 = m2;
        _player = player;
        _check = Array.IndexOf(args, "--screenshot") < 0;
        _focus = Array.IndexOf(args, "--zoo-focus") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private ItemCatalog Catalog => _m2.Catalog;

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_builtAt < 0)
        {
            if (_time > 0.3) // let the physics server take in the world's shapes first
                Build();
            return;
        }
        if (!_check)
            return;
        var passed = _pens.All(p => p.Done) && _largo is not null && _tarr is not null;
        if (passed || _time - _builtAt > TimeoutSeconds)
        {
            GD.Print(Report(passed));
            GetTree().Quit(passed ? 0 : 1);
            SetPhysicsProcess(false);
        }
    }

    private void Build()
    {
        _builtAt = _time;
        var slimes = Catalog.Slimes.Slimes
            .Where(s => s.Eating is not null && Catalog.Prefabs.Has(s.Id) && Catalog.Species(s.Id) is not null)
            .OrderBy(s => s.IsLargo).ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        var count = slimes.Count + 1;
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;

        // Over the ranch's corral, on a floor just above the highest ground under it.
        var center = _m2.Sites.Corrals.FirstOrDefault() is { } corral
            ? UnityConvert.Transform(corral.World).Origin
            : _player.GlobalPosition;
        var (width, depth) = (columns * PenSize, rows * PenSize);
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
        BuildFloorAndWalls(center, width, depth, columns, rows);

        Vector3 PenCenter(int i) => center + new Vector3((i % columns + 0.5f) * PenSize - width / 2, 0, (i / columns + 0.5f) * PenSize - depth / 2);
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
            slime.Sim.Hunger = 1; // hungry from the start, so the check doesn't wait hours of game time
            slime.Ate += (_, eaten) => pen.Ate |= eaten == pen.Food;
            slime.Produced += (_, item) => pen.Made.Add(item.Id);
            _pens.Add(pen);
        }

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

        AimCamera(center, width, depth);
        GD.Print($"slime-zoo: {slimes.Count} slimes ({slimes.Count(s => s.IsLargo)} largos) in {columns} x {rows} pens, " +
                 $"floor at {center.Y:F1}, {_pens.Count(p => p.Food is not null)} with food");
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

    private Actor Place(string id, Vector3 onFloor)
    {
        var actor = Catalog.Spawn(id, onFloor);
        actor.GlobalPosition = onFloor + Vector3.Up * (actor.Radius + 0.05f);
        return actor;
    }

    // A solid floor everything stands on and items-only walls between the pens.
    private void BuildFloorAndWalls(Vector3 center, float width, float depth, int columns, int rows)
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

        var walls = new StaticBody3D { Name = "ZooWalls", CollisionLayer = Actor.PenWallLayer, CollisionMask = 0 };
        void Wall(Vector3 at, Vector3 wallSize) =>
            walls.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = wallSize }, Position = at + Vector3.Up * (WallHeight / 2) });
        for (var c = 0; c <= columns; c++)
            Wall(center + new Vector3(c * PenSize - width / 2, 0, 0), new Vector3(WallThickness, WallHeight, depth));
        for (var r = 0; r <= rows; r++)
            Wall(center + new Vector3(0, 0, r * PenSize - depth / 2), new Vector3(width, WallHeight, WallThickness));
        AddChild(walls);
    }

    // Holds the player still above the zoo, looking down at it (or at one pen with --zoo-focus).
    private void AimCamera(Vector3 center, float width, float depth)
    {
        _player.SetPhysicsProcess(false);
        Vector3 eye, target;
        if (_focus is not null && _pens.FirstOrDefault(p => p.Slime == _focus) is { } pen)
            (eye, target) = (pen.Center + new Vector3(0, 3.2f, 4.2f), pen.Center + Vector3.Up * 0.5f);
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
        b.Append(passed ? "slime-zoo PASS" : "slime-zoo FAIL");
        return b.ToString();
    }
}
