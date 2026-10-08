using System;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.Market;
using OpenRanch.Game.Player;
using OpenRanch.Game.Vacpack;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Milestone 2 in the world: items built from the game's prefabs, slimes that eat and make plorts,
/// the vacpack on the player, corral walls, the plort market at the ranch and a HUD. Command-line
/// options after "--": --no-slimes leaves the corral empty; --m2-check runs <see cref="M2Check"/>;
/// --m2-watch N logs what the corral's slimes eat and make for N seconds, then quits.
/// </summary>
public partial class M2World : Node3D
{
    private readonly M2Check? _check;
    private readonly double _watchSeconds;
    private double _time;

    private M2World(GameInstall install, ZoneExtract zone, PhysicsLayers layers, PlayerController player, float startHour, string[] args)
    {
        Name = "M2";
        Scripts = new GameScripts(install);
        var prefabs = ItemPrefabs.Read(Scripts);
        var slimes = SlimeData.Read(Scripts);
        MarketData = MarketData.Read(Scripts);
        var scene = Scripts.Assets.File("level3") ?? throw new System.IO.IOException("level3 is missing.");
        Sites = RanchSites.Read(Scripts, scene, zone.Name);
        var vacuum = VacuumTuning.Read(Scripts, scene);
        Clock = new GameClock(GameTimeData.SecondsPerGameDay(Scripts), startHour);

        var actors = new Node3D { Name = "Actors" };
        AddChild(actors);
        Catalog = new ItemCatalog(Scripts, prefabs, slimes, Clock, actors);
        // openranch's own price drift with the original's spread (UNVERIFIED.md, "Daily plort price noise").
        Market = new PlortMarket(MarketData, new WanderingMood(seed: 0));
        Wallet = new Wallet();
        Pack = new Simulation.Vacpack(id => prefabs.Has(id) && prefabs.Get(id) is { VacuumSize: 0 } p && p.RootScript("Vacuumable") is not null);

        AddChild(PenWalls.Build(zone, layers));
        foreach (var site in Sites.Markets)
        {
            var stand = new MarketStand(site, Market, Wallet, Clock);
            Stands = Stands.Append(stand).ToArray();
            AddChild(stand);
        }
        Tool = new VacpackTool(vacuum, Catalog, player, Pack);
        player.Camera.AddChild(Tool);
        AddChild(new Hud(Wallet, Pack));

        if (Array.IndexOf(args, "--m2-check") >= 0)
            _check = new M2Check(this, player);
        else if (Array.IndexOf(args, "--no-slimes") < 0)
            Callable.From(SpawnStarters).CallDeferred();
        if (Array.IndexOf(args, "--m2-watch") is var w and >= 0 && w + 1 < args.Length && double.TryParse(args[w + 1],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            _watchSeconds = seconds;
            Catalog.Spawned += actor =>
            {
                if (actor is SlimeActor slime)
                    slime.Ate += (s, food) => GD.Print($"m2-watch {_time,6:F1} s: {s.Id} ate {food}, hunger now {s.Sim.Hunger:F2}");
            };
            Catalog.Spawned += actor => GD.Print($"m2-watch {_time,6:F1} s: {actor.Id} appeared at {actor.Position.X:F1}, {actor.Position.Y:F1}, {actor.Position.Z:F1}");
        }
    }

    public GameScripts Scripts { get; }
    public MarketData MarketData { get; }
    public RanchSites Sites { get; }
    public GameClock Clock { get; }
    public ItemCatalog Catalog { get; }
    public PlortMarket Market { get; }
    public Wallet Wallet { get; }
    public Simulation.Vacpack Pack { get; }
    public VacpackTool Tool { get; }
    public MarketStand[] Stands { get; private set; } = [];

    /// <summary>Builds milestone 2's world, or returns null (with a warning) when the install's data can't be read.</summary>
    public static M2World? Create(GameInstall install, ZoneExtract zone, PhysicsLayers layers, PlayerController player, float startHour, string[] args)
    {
        try
        {
            return new M2World(install, zone, layers, player, startHour, args);
        }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.Collections.Generic.KeyNotFoundException)
        {
            GD.PushWarning($"Slimes, vacpack and market are off: {e.Message}");
            return null;
        }
    }

    // A few slimes and some food in the ranch's corral, to watch them hop, eat and make plorts.
    // openranch's own demo set; a new game in the original starts with an empty corral.
    private void SpawnStarters()
    {
        if (Sites.Corrals.FirstOrDefault() is not { } corral)
            return;
        var place = UnityConvert.Transform(corral.World);
        Vector3 At(float x, float z) => place * new Vector3(x, 0, z);
        var slimes = new[] { ("PINK_SLIME", -0.3f, -0.3f), ("PINK_SLIME", 0.3f, -0.3f), ("ROCK_SLIME", -0.3f, 0.3f), ("TABBY_SLIME", 0.3f, 0.3f) };
        foreach (var (id, x, z) in slimes)
            Catalog.Spawn(id, At(x, z));
        foreach (var (x, z) in new[] { (0f, 0.1f), (0.1f, -0.1f), (-0.1f, 0f) })
            Catalog.Spawn("CARROT_VEGGIE", At(x, z));
        Catalog.Spawn("HEN", At(0.2f, 0.1f));
    }

    public override void _ExitTree()
    {
        Catalog.FreeTemplates();
        Scripts.Dispose();
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_watchSeconds > 0 && _time >= _watchSeconds)
        {
            foreach (var actor in Catalog.Live)
                GD.Print($"m2-watch end: {actor.Id} at {actor.GlobalPosition.X:F1}, {actor.GlobalPosition.Y:F1}, {actor.GlobalPosition.Z:F1}" +
                         (actor is SlimeActor s ? $", hunger {s.Sim.Hunger:F2}" : ""));
            GetTree().Quit();
            SetPhysicsProcess(false);
            return;
        }
        var (_, newDay) = Clock.Advance(delta);
        if (newDay)
            Market.StartDay(Clock.Day);
        if (_check is not null && _check.Step(delta))
        {
            GD.Print(_check.Report);
            GetTree().Quit(_check.Passed ? 0 : 1);
            SetPhysicsProcess(false);
        }
    }
}
