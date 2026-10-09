using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Things growing on the world clock (docs/behavior/produce.md, coops.md): every crop in the zone (the
/// plots' and the scene's own) grows batches of produce on its free joints (<see cref="CropSpawnClock"/>);
/// produce ripens, lets go of its joint, rots and goes away (<see cref="ProduceCycle"/>); hens lay chicks
/// and chicks grow up (<see cref="ReproduceRules"/>, <see cref="TransformRules"/>). Every value comes from
/// the install's scripts (<see cref="ProduceData"/>). The clocks start from the save (produce stages, hens'
/// and chicks' times, the crops' spawn times) and are written back by the save writer.
/// </summary>
public partial class RanchProduce : Node
{
    /// <summary>A crop in play: its spawner, its clock and what hangs on each of its joints.</summary>
    public sealed class Crop(CropSpawner spawner, CropSpawnClock clock, string? siteId, ProduceTimes first)
    {
        public CropSpawner Spawner { get; } = spawner;
        public CropSpawnClock Clock { get; } = clock;
        /// <summary>The plot site it stands on, or null for the scene's own crops.</summary>
        public string? SiteId { get; } = siteId;
        public ProduceTimes First { get; } = first;
        public Slimes.Actor?[] Joints { get; } = new Slimes.Actor?[spawner.Joints.Count];
        /// <summary>Every batch grown in play: when it was due and the produce it hung.</summary>
        public List<(double Due, List<Slimes.Actor> Produce)> Batches { get; } = [];
    }

    /// <summary>Produce on the clock: its stages and the crop joint it hangs from, if any.</summary>
    public sealed class Growing(ProduceCycle cycle)
    {
        public ProduceCycle Cycle { get; } = cycle;
        public Crop? Crop { get; set; }
        public int Joint { get; set; } = -1;
    }

    private readonly Economy _economy;
    private readonly ProduceData _data;
    private readonly IDraws _draws = new RandomDraws(new Random());
    private readonly Dictionary<string, Crop> _crops = new(StringComparer.Ordinal);
    private readonly Dictionary<Slimes.Actor, Growing> _produce = [];
    private readonly Dictionary<Slimes.Actor, double> _hens = [];
    private readonly Dictionary<Slimes.Actor, double> _chicks = [];
    private readonly Dictionary<string, ProduceTimes?> _times = new(StringComparer.Ordinal);
    private readonly int _sprinkler, _soil, _miracleMix, _deluxeCoop, _coop;
    private readonly Material _rotten;
    private double _lastTime = double.NaN;
    private bool _started, _spawning;

    public RanchProduce(Economy economy)
    {
        Name = "Produce";
        _economy = economy;
        // Growing follows the world clock, which also runs while the ranch house screen pauses the game for sleep.
        ProcessMode = ProcessModeEnum.Always;
        _data = ProduceData.Read(economy.M2.Scripts);
        var names = economy.Names;
        _sprinkler = names.Value(GameEnum.PlotUpgrade, "SPRINKLER");
        _soil = names.Value(GameEnum.PlotUpgrade, "SOIL");
        _miracleMix = names.Value(GameEnum.PlotUpgrade, "MIRACLE_MIX");
        _deluxeCoop = names.Value(GameEnum.PlotUpgrade, "DELUXE_COOP");
        _coop = names.Value(GameEnum.PlotType, "COOP");
        // The original swaps in the prefab's rottenMat; openranch darkens the produce instead (UNVERIFIED.md, "Rotten produce look").
        _rotten = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.18f, 0.08f, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
    }

    /// <summary>The install's growing rules.</summary>
    public ProduceData Data => _data;
    public IReadOnlyDictionary<string, Crop> Crops => _crops;
    public IReadOnlyDictionary<Slimes.Actor, Growing> Produce => _produce;
    public IEnumerable<Slimes.Actor> Hens => _hens.Keys;
    public IEnumerable<Slimes.Actor> Chicks => _chicks.Keys;

    /// <summary>Chicks laid in play: the hen, the chick and when.</summary>
    public List<(Slimes.Actor Hen, Slimes.Actor Chick, double At)> Laid { get; } = [];

    /// <summary>Chicks that grew up in play: what they turned into and when.</summary>
    public List<(string From, Slimes.Actor Into, double At)> GrewUp { get; } = [];

    private double Now => _economy.Clock.Ranch.WorldTime;
    private SaveLoad.CropHold? Hold => _economy.Saved.Hold;

    public ProduceTimes? TimesOf(string id)
    {
        if (!_times.TryGetValue(id, out var t))
            _times[id] = t = _data.Times(id);
        return t;
    }

    public override void _PhysicsProcess(double delta)
    {
        // The loader puts the save's actors into the world a frame after the economy is made.
        if (!_started)
        {
            if (Hold is null)
                return;
            Start();
        }
        var now = Now;
        var worldDelta = double.IsNaN(_lastTime) ? 0 : Math.Max(0, now - _lastTime);
        _lastTime = now;
        foreach (var crop in _crops.Values.ToList())
            UpdateCrop(crop, now, worldDelta);
        foreach (var (actor, growing) in _produce.ToList())
            UpdateProduce(actor, growing, now, worldDelta);
        UpdateHens(now);
        UpdateChicks(now);
    }

    private void Start()
    {
        _started = true;
        var now = Now;
        var names = _economy.Names;
        Hold!.Picked += actor =>
        {
            if (_produce.TryGetValue(actor, out var g))
                Apply(actor, g, g.Cycle.Advance(Now, release: true));
        };
        foreach (var crop in _economy.Plots.PlantedCrops.Concat(_economy.Saved.Crops.Where(c => c.Prefab is null)))
            AddCrop(crop, now);
        _economy.Plots.Changed += SiteChanged;

        // The save's actors: produce keeps its stage, hens and chicks their times.
        foreach (var (saved, actor) in _economy.Saved.SpawnedLoose)
        {
            if (!GodotObject.IsInstanceValid(actor))
                continue;
            if (TimesOf(saved.Id) is { } times)
            {
                var cycle = new ProduceCycle(times, _draws);
                var growing = new Growing(cycle);
                if (saved.Joint is { } joint && FindJoint(joint) is { } found)
                {
                    var (crop, i) = found;
                    crop.Joints[i] = actor;
                    growing.Crop = crop;
                    growing.Joint = i;
                }
                var stage = Enum.TryParse<ProduceStage>(names.Name(GameEnum.ResourceCycleState, saved.Saved.CycleState), true, out var st) ? st : ProduceStage.Edible;
                if (saved.Saved.CycleProgressTime == 0 || (growing.Crop is null && stage is ProduceStage.Unripe or ProduceStage.Ripe))
                {
                    // Off its joint, unripe or ripe produce is edible from now (ResourceCycle.SetInitState).
                    cycle.Stage = ProduceStage.Edible;
                    cycle.ProgressTime = now + cycle.Vary(times.EdibleHours) * 3600.0;
                }
                else
                {
                    cycle.Stage = stage;
                    cycle.ProgressTime = saved.Saved.CycleProgressTime;
                }
                _produce[actor] = growing;
                if (stage == ProduceStage.Rotten)
                    LookRotten(actor);
            }
            Track(actor, saved.Saved.ReproduceTime, saved.Saved.TransformTime, now);
        }
        // Actors made in play from now on (shot from the vacpack, laid, grown up) start their own clocks.
        _economy.M2.Catalog.Spawned += actor =>
        {
            if (_spawning)
                return;
            if (TimesOf(actor.Id) is { } t)
            {
                var cycle = new ProduceCycle(t, _draws);
                cycle.InitLoose(Now);
                _produce[actor] = new Growing(cycle);
            }
            Track(actor, 0, 0, Now);
        };
        GD.Print($"Produce: {_crops.Count} crops ({_crops.Values.Count(c => c.SiteId is not null)} on plots), {_produce.Count} produce, " +
                 $"{_hens.Count} hens, {_chicks.Count} chicks");
    }

    // A hen keeps her saved laying time and a chick its growing-up time; new ones start theirs (Reproduce/TransformAfterTime.InitModel).
    private void Track(Slimes.Actor actor, double reproduceTime, double transformTime, double now)
    {
        if (_data.Reproduce(actor.Id) is { } hen)
            _hens[actor] = reproduceTime != 0 ? reproduceTime : Math.Max(now, ProduceCycle.Start) + hen.Period(_draws) * 3600.0;
        if (_data.Transform(actor.Id) is { } chick)
            _chicks[actor] = transformTime != 0 ? transformTime : Math.Max(now, ProduceCycle.Start) + chick.DelayHours * 3600.0;
    }

    private (Crop, int)? FindJoint(N.Vector3 joint)
    {
        foreach (var crop in _crops.Values)
            for (var i = 0; i < crop.Spawner.Joints.Count; i++)
                if (N.Vector3.DistanceSquared(crop.Spawner.Joints[i], joint) < 1e-6f)
                    return (crop, i);
        return null;
    }

    private void AddCrop(CropSpawner spawner, double now)
    {
        if (spawner.Script is not { } script)
            return;
        var rules = _data.SpawnRules(script.File, script.Data, script.ForceFirstRipeness);
        if (rules.Produce.Count == 0 || TimesOf(rules.Produce[0]) is not { } first)
            return;
        var siteId = spawner.Prefab is null ? null : spawner.Path.Split('/')[0];
        var clock = new CropSpawnClock(rules, first, _draws, onPlot: siteId is not null);
        // The save keeps each crop's clock by its position (SavedGame.SetSpawnTimes: within 0.1 m).
        var saved = _economy.Saved.Ranch.World.ResourceSpawners
            .FirstOrDefault(t => N.Vector3.DistanceSquared(new N.Vector3(t.Position.X, t.Position.Y, t.Position.Z), spawner.Position) < 0.01f);
        if (saved is not null)
        {
            clock.NextSpawnTime = saved.NextSpawnTime;
            clock.StoredWater = saved.Water;
        }
        clock.Init(now);
        _crops[spawner.Path] = new Crop(spawner, clock, siteId, first);
    }

    // A site rebuilt: its crop may be new or gone. Produce hanging on a crop that is gone comes off:
    // unripe produce goes with it, ripe produce falls (ResourceCycle.RegistryUpdate without a joint).
    private void SiteChanged(string siteId)
    {
        var now = Now;
        var planted = _economy.Plots.Get(siteId)?.Crop;
        foreach (var crop in _crops.Values.Where(c => c.SiteId == siteId && c.Spawner.Path != planted?.Path).ToList())
        {
            _crops.Remove(crop.Spawner.Path);
            foreach (var actor in crop.Joints.OfType<Slimes.Actor>().Where(GodotObject.IsInstanceValid))
            {
                if (_produce.TryGetValue(actor, out var g) && g.Cycle.Stage == ProduceStage.Unripe)
                    Remove(actor);
                else
                {
                    Hold?.Release(actor);
                    if (_produce.TryGetValue(actor, out var r))
                        r.Crop = null;
                }
            }
        }
        if (planted is not null && !_crops.ContainsKey(planted.Path))
            AddCrop(planted, now);
    }

    private bool HasUpgrade(Crop crop, int upgrade) =>
        crop.SiteId is not null && _economy.Plots.Ranch.FindPlot(crop.SiteId)?.HasUpgrade(upgrade) == true;

    private void UpdateCrop(Crop crop, double now, double worldDelta)
    {
        var sprinkler = HasUpgrade(crop, _sprinkler);
        foreach (var request in crop.Clock.Update(now, worldDelta, sprinkler, HasUpgrade(crop, _miracleMix)))
        {
            var active = crop.Joints.Count(a => a is not null && GodotObject.IsInstanceValid(a) && !a.Consumed);
            var batch = crop.Clock.Batch(request, now, HasUpgrade(crop, _soil), active, TimesOf);
            crop.Batches.Add((request.SpawnAt ?? now, Grow(crop, request, batch, now)));
        }
    }

    // SpawnResource.Spawn: each item on a free joint picked at random; with forceDestroyLeftoversOnSpawn
    // what still hangs there is removed first.
    private List<Slimes.Actor> Grow(Crop crop, SpawnRequest request, List<string> batch, double now)
    {
        var grown = new List<Slimes.Actor>();
        var free = new List<int>();
        for (var i = 0; i < crop.Joints.Length; i++)
        {
            var on = crop.Joints[i];
            if (on is null || !GodotObject.IsInstanceValid(on) || on.Consumed || !(Hold?.Holds(on) ?? false))
                free.Add(i);
            else if (crop.Clock.Rules.ForceDestroyLeftovers)
            {
                Remove(on);
                free.Add(i);
            }
        }
        foreach (var id in batch)
        {
            if (free.Count == 0 || TimesOf(id) is not { } times || !_economy.M2.Catalog.Prefabs.Has(id))
                break;
            var k = _draws.Range(0, free.Count);
            var joint = free[k];
            free.RemoveAt(k);
            _spawning = true;
            var actor = _economy.M2.Catalog.Spawn(id, UnityConvert.Position(crop.Spawner.Joints[joint]));
            _spawning = false;
            Hold!.Hang(actor, unripe: true, times.ReleasePrepSeconds);
            var cycle = new ProduceCycle(times, _draws);
            cycle.Attach(now);
            if (request.SpawnAt is { } began)
                cycle.History[0] = (ProduceStage.Unripe, began);
            var growing = new Growing(cycle) { Crop = crop, Joint = joint };
            crop.Joints[joint] = actor;
            _produce[actor] = growing;
            if (CropSpawnClock.UnripeEnds(request, times, now) is { } ends)
                Apply(actor, growing, cycle.ProgressTo(ends, now));
            grown.Add(actor);
        }
        return grown;
    }

    private void UpdateProduce(Slimes.Actor actor, Growing growing, double now, double worldDelta)
    {
        if (!GodotObject.IsInstanceValid(actor) || actor.Consumed)
        {
            Forget(actor);
            return;
        }
        // Produce on a watered crop's joint ripens faster (the spawner's ripeness delegate).
        if (growing.Crop is { } crop && growing.Cycle.Stage is ProduceStage.Unripe or ProduceStage.Ripe)
            growing.Cycle.Hasten(crop.Clock.RipenessPerSecond(HasUpgrade(crop, _sprinkler)), worldDelta);
        Apply(actor, growing, growing.Cycle.Advance(now));
    }

    private void Apply(Slimes.Actor actor, Growing growing, List<ProduceStage> entered)
    {
        foreach (var stage in entered)
            switch (stage)
            {
                case ProduceStage.Ripe:
                    Hold?.Ripen(actor);
                    break;
                case ProduceStage.Edible:
                    Hold?.Release(actor);
                    FreeJoint(growing);
                    // MakeEdible ejects it from the joint with a little push and a turn.
                    actor.ApplyCentralImpulse(Vector3.Up * 0.5f);
                    break;
                case ProduceStage.Rotten:
                    actor.Edible = false;
                    LookRotten(actor);
                    break;
                case ProduceStage.Gone:
                    Remove(actor);
                    return;
            }
    }

    private void LookRotten(Slimes.Actor actor)
    {
        actor.Edible = false;
        foreach (var mesh in actor.Visual.FindChildren("*", nameof(GeometryInstance3D), true, false).OfType<GeometryInstance3D>())
            mesh.MaterialOverlay = _rotten;
    }

    private void FreeJoint(Growing growing)
    {
        if (growing.Crop is { } crop && growing.Joint >= 0)
            crop.Joints[growing.Joint] = null;
        growing.Crop = null;
        growing.Joint = -1;
    }

    private void Remove(Slimes.Actor actor)
    {
        Forget(actor);
        if (GodotObject.IsInstanceValid(actor) && !actor.Consumed)
            actor.Consume();
    }

    private void Forget(Slimes.Actor actor)
    {
        if (_produce.Remove(actor, out var g))
            FreeJoint(g);
        Hold?.Drop(actor);
    }

    // --- Coops ---

    private void UpdateHens(double now)
    {
        foreach (var (hen, next) in _hens.ToList())
        {
            if (!GodotObject.IsInstanceValid(hen) || hen.Consumed)
            {
                _hens.Remove(hen);
                continue;
            }
            if (now < next || _data.Reproduce(hen.Id) is not { } rules)
                continue;
            var at = hen.GlobalPosition;
            var live = _economy.M2.Catalog.Live.ToList();
            var mateNear = rules.MateId is { } mate && live.Any(a => a.Id == mate && a.GlobalPosition.DistanceTo(at) <= rules.MaxDistToMate);
            var crowd = live.Count(a => rules.DensityIds.Contains(a.Id) && a.GlobalPosition.DistanceTo(at) <= rules.DensityDist);
            var coop = _economy.Plots.Sites.FirstOrDefault(s => s.Placed.Plot.Type == _coop && _economy.Plots.InTrigger(s, "CoopRegion", at));
            var vitamizer = _economy.Plots.Sites.Any(s => _economy.Plots.InTrigger(s, "VitamizerRegion", at));
            var lays = rules.Lays(mateNear, crowd, coop is not null, coop?.Placed.Plot.HasUpgrade(_deluxeCoop) == true, vitamizer, _draws);
            for (var i = 0; i < lays && _economy.M2.Catalog.Prefabs.Has(rules.ChildId); i++)
            {
                var chick = Spawn(rules.ChildId, at);
                _chicks[chick] = Math.Max(now, ProduceCycle.Start) + (_data.Transform(rules.ChildId)?.DelayHours ?? 0) * 3600.0;
                Laid.Add((hen, chick, now));
            }
            _hens[hen] = now + rules.Period(_draws) * 3600.0;
        }
    }

    private void UpdateChicks(double now)
    {
        foreach (var (chick, at) in _chicks.ToList())
        {
            if (!GodotObject.IsInstanceValid(chick) || chick.Consumed)
            {
                _chicks.Remove(chick);
                continue;
            }
            if (now < at || _data.Transform(chick.Id) is not { } rules || rules.Pick(_draws) is not { } into || !_economy.M2.Catalog.Prefabs.Has(into))
                continue;
            _chicks.Remove(chick);
            var position = chick.GlobalPosition;
            chick.Consume();
            var grown = Spawn(into, position);
            Track(grown, 0, 0, now);
            GrewUp.Add((chick.Id, grown, now));
        }
    }

    private Slimes.Actor Spawn(string id, Vector3 at)
    {
        _spawning = true;
        try
        {
            return _economy.M2.Catalog.Spawn(id, at);
        }
        finally
        {
            _spawning = false;
        }
    }

    // --- Saving ---

    /// <summary>The clocks to save for an actor: produce's stage, a hen's laying time, a chick's growing-up time.</summary>
    public ActorTimers? TimersOf(Slimes.Actor actor)
    {
        int? state = null;
        double? progress = null;
        if (_produce.TryGetValue(actor, out var g) && g.Cycle.Stage != ProduceStage.Gone)
        {
            state = _economy.Names.Value(GameEnum.ResourceCycleState, g.Cycle.Stage.ToString().ToUpperInvariant());
            progress = g.Cycle.ProgressTime;
        }
        double? lay = _hens.TryGetValue(actor, out var l) ? l : null;
        double? grow = _chicks.TryGetValue(actor, out var c) ? c : null;
        return state is null && lay is null && grow is null ? null : new ActorTimers(state, progress, lay, grow);
    }

    /// <summary>Rotten produce leaves the game's records when it rots (ResourceCycle.SetRotten), so it isn't saved.</summary>
    public bool Unsaved(Slimes.Actor actor) => _produce.TryGetValue(actor, out var g) && g.Cycle.Stage is ProduceStage.Rotten or ProduceStage.Gone;

    /// <summary>Every crop's clock, by its position; crops not in play keep the saved clock.</summary>
    public IReadOnlyList<CropTimes> CropClocks()
    {
        var live = _crops.Values.Select(c => new CropTimes(new Vec3(c.Spawner.Position.X, c.Spawner.Position.Y, c.Spawner.Position.Z), c.Clock.NextSpawnTime, c.Clock.StoredWater)).ToList();
        var kept = _economy.Saved.Ranch.World.ResourceSpawners.Where(t => !live.Any(l =>
            N.Vector3.DistanceSquared(new N.Vector3(t.Position.X, t.Position.Y, t.Position.Z), new N.Vector3(l.Position.X, l.Position.Y, l.Position.Z)) < 0.01f));
        return [.. kept, .. live];
    }
}
