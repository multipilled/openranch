using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity.Managed;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

// Slime abilities (docs/behavior/slime-abilities.md). Numbers on the prefab are read from it; the
// code-only ones are in src/OpenRanch.Simulation/Abilities.cs. Largos carry both parents' components,
// so they get both abilities. Effects (particles, sounds, faces) aren't drawn yet.

/// <summary>Boom slimes (<c>BoomSlimeExplode</c>): every 10-45 s (sooner when agitated) it grimaces 1.5 s, explodes, and recovers 5 s.</summary>
public sealed class BoomExplode : SlimeBehaviour
{
    private enum State { Idle, Preparing, Recovering }

    private readonly float _power, _radius, _minDamage, _maxDamage;
    private State _state;
    private float _next, _until;

    public BoomExplode(SerializedObject data) =>
        (_power, _radius, _minDamage, _maxDamage) = (F(data, "explodePower"), F(data, "explodeRadius"), F(data, "minPlayerDamage"), F(data, "maxPlayerDamage"));

    public Explosions.Result? Last { get; private set; }

    protected override void Ready()
    {
        var first = Delay();
        _next = first * (BoomSlime.FirstDelayMinFraction + (float)Slime.Random.NextDouble() * (1f - BoomSlime.FirstDelayMinFraction));
    }

    private float Delay() => AbilityDelay.Pick(BoomSlime.MinDelay, BoomSlime.MaxDelay, Slime.Sim.Agitation, Slime.Random.NextDouble());

    /// <summary>Makes it due now (for checks).</summary>
    public void DueNow() => _next = Slime.Age;

    public override float Relevancy(bool grounded) => _state == State.Idle && Slime.Age > _next ? BoomSlime.Relevancy : 0f;
    public override bool CanRethink => _state == State.Idle;

    public override void Selected()
    {
        _state = State.Preparing;
        _until = Slime.Age + BoomSlime.PrepSeconds;
    }

    public override void Tick(float delta)
    {
        if (_state == State.Preparing && Slime.Age >= _until)
        {
            var result = Explosions.Explode(Catalog, Slime, Slime.GlobalPosition, _radius, _power, _minDamage, _maxDamage, Slime.Id);
            Last = result;
            _next = Slime.Age + Delay();
            _state = State.Recovering;
            _until = Slime.Age + BoomSlime.RecoverySeconds;
            Report($"exploded (power {_power}, radius {_radius}): pushed {result.Pushed}, player lost {result.PlayerDamage}");
        }
        else if (_state == State.Recovering && Slime.Age >= _until)
            _state = State.Idle;
    }
}

/// <summary>
/// Rad slimes' aura (<c>RadSource</c> on the prefab's rad trigger, <c>RadSlimeExpand</c>): a player inside
/// the aura soaks up its radiation per second; radiation over the maximum turns into health loss. Every
/// 30-180 s the aura swells to 1.5× over 3 s, stays 10 s, and shrinks back.
/// </summary>
public sealed class RadAura : SlimeBehaviour
{
    private readonly float _radPerSecond;
    private readonly PrefabCollider? _trigger;
    private SphereShape3D? _sphere;
    private float _baseRadius, _scale = 1f, _next, _phaseEnds;
    private bool _expanding, _expanded, _playerInside;

    public RadAura(ItemPrefab prefab)
    {
        var source = prefab.Scripts.FirstOrDefault(s => s.Class == "RadSource");
        _radPerSecond = F(source?.Data, "radPerSecond");
        _trigger = source is null ? null : prefab.Colliders.FirstOrDefault(c => c.Path == source.Path && c.Collider.IsTrigger);
    }

    public float Scale => _scale;
    /// <summary>The aura's radius now, in metres.</summary>
    public float Radius => _baseRadius * _scale;
    public float RadPerSecond => _radPerSecond;
    public float RadsGiven { get; private set; }

    protected override void Ready()
    {
        _next = Slime.Age + Delay();
        if (_trigger is null || ColliderShapes.Make(_trigger.Collider, UnityConvert.Transform(_trigger.ToRoot)) is not { Shape: SphereShape3D sphere } shape)
            return;
        _sphere = sphere;
        _baseRadius = sphere.Radius;
        var area = new Area3D { Name = "RadAura", CollisionLayer = 0, CollisionMask = Actor.WorldLayer, Monitorable = false };
        area.AddChild(shape);
        area.BodyEntered += b => { if (b == Catalog.Player) _playerInside = true; };
        area.BodyExited += b => { if (b == Catalog.Player) _playerInside = false; };
        Slime.AddChild(area);
    }

    private float Delay() => AbilityDelay.Pick(RadSlime.MinDelay, RadSlime.MaxDelay, Slime.Sim.Agitation, Slime.Random.NextDouble());

    public void DueNow() => _next = Slime.Age;

    public override float Relevancy(bool grounded) => !_expanding && !_expanded && Slime.Age > _next ? RadSlime.Relevancy : 0f;
    public override bool CanRethink => !_expanding;

    public override void Selected()
    {
        _expanding = true;
        _phaseEnds = Slime.Age + RadSlime.ExpandingSeconds;
    }

    public override void Tick(float delta)
    {
        if (_expanding && Slime.Age >= _phaseEnds)
            (_expanding, _expanded, _phaseEnds) = (false, true, Slime.Age + RadSlime.ExpandedSeconds);
        else if (_expanded && Slime.Age >= _phaseEnds)
        {
            _expanded = false;
            _next = Slime.Age + Delay();
            Report($"aura swelled to {_scale:F2}x (radius {Radius:F2} m, normally {_baseRadius:F2} m)");
        }
        _scale = RadSlime.StepScale(_scale, _expanding || _expanded ? RadSlime.ExpandFactor : 1f, delta);
        if (_sphere is not null)
            _sphere.Radius = _baseRadius * _scale;
        if (_playerInside && Catalog.Player is { } player && GodotObject.IsInstanceValid(player))
        {
            var rads = _radPerSecond * delta;
            RadsGiven += rads;
            if (Catalog.PlayerVitals.AddRads(rads) is > 0 and var loss)
                Catalog.PlayerVitals.Damage(loss, Slime.Id);
        }
    }
}

/// <summary>
/// Crystal slimes (<c>CrystalSlimeLaunch</c>): every 0.05-0.25 game hours, standing, it curls up for a game
/// minute, launches itself up and forward and rolls, and leaves crystal spikes: one big where it was and a
/// ring of small ones. Spikes (their prefabs from the slime's appearance) hurt the player on touch and
/// crumble after their lifetime.
/// </summary>
public sealed class CrystalLaunch : SlimeBehaviour
{
    private enum State { Idle, Preparing, Launched }

    private readonly ItemPrefab? _large, _small;
    private State _state;
    private double _next, _until;
    private Vector3 _rollForward;

    public CrystalLaunch(ItemPrefabs prefabs, string slimeId)
    {
        _large = prefabs.AppearancePrefab(slimeId, "CrystalAppearance", "largeCrystalPrefab");
        _small = prefabs.AppearancePrefab(slimeId, "CrystalAppearance", "smallCrystalPrefab");
    }

    public int SpikesMade { get; private set; }
    public List<CrystalSpike> Spikes { get; } = [];

    private double Now => Catalog.Clock.TotalHours;
    protected override void Ready() => _next = Catalog.Clock.TotalHours + Delay();
    private float Delay() => AbilityDelay.Pick(CrystalSlime.MinDelayHours, CrystalSlime.MaxDelayHours, Slime.Sim.Agitation, Slime.Random.NextDouble());
    public void DueNow() => _next = Now;

    public override float Relevancy(bool grounded)
    {
        if (_state != State.Idle || !grounded || Now < _next)
            return 0f;
        var right = Slime.GlobalBasis.X with { Y = 0 };
        right = right.LengthSquared() > 1e-6f ? right.Normalized() : Vector3.Right;
        _rollForward = Vector3.Up.Cross(right);
        return CrystalSlime.Relevancy;
    }

    public override bool CanRethink => _state == State.Idle;

    public override void Selected()
    {
        _state = State.Preparing;
        _until = Now + CrystalSlime.PrepHours;
    }

    public override void Deselected() => _state = State.Idle;

    public override void Action(float delta)
    {
        if (_state == State.Preparing && Now >= _until && Slime.Grounded)
        {
            Launch();
            _state = State.Launched;
            _until = Now + CrystalSlime.LaunchedHours;
        }
        else if (_state == State.Launched)
        {
            Slime.ApplyTorque(_rollForward.Cross(Vector3.Down) * (CrystalSlime.RollTorque * Slime.Mass * Catalog.FixedTimestep));
            if (Now >= _until)
            {
                _next = Now + Delay();
                _state = State.Idle;
            }
        }
    }

    private void Launch()
    {
        // A one-step force: 200 up (not scaled by mass) and 40 x mass forward.
        Slime.ApplyCentralImpulse((Vector3.Up * CrystalSlime.LaunchUp + _rollForward * CrystalSlime.LaunchForward * Slime.Mass) * Catalog.FixedTimestep);
        var at = Slime.GlobalPosition;
        var made = Spike(_large, at) ? 1 : 0;
        var (min, max) = CrystalSlime.SmallSpikes(Slime.Mass);
        var count = Slime.Random.Next(min, Math.Max(min + 1, max));
        Slime.GetTree().CreateTimer(CrystalSlime.SmallSpikeDelay, processAlways: false, processInPhysics: true).Timeout += () =>
        {
            var angle = (float)(Slime.Random.NextDouble() * Math.PI * 2);
            var small = 0;
            for (var i = 0; i < count; i++, angle += MathF.PI * 2 / count)
                if (Spike(_small, at + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * CrystalSlime.SpikeRing))
                    small++;
            Report($"launched and left {made} big + {small} small spikes ({count} tried for mass {Slime.Mass:F2})");
        };
    }

    private bool Spike(ItemPrefab? prefab, Vector3 spot)
    {
        if (prefab is null || !GodotObject.IsInstanceValid(Slime))
            return false;
        var hit = Slime.Ray(spot, spot + Vector3.Down * CrystalSlime.SpikeDropRay, Actor.WorldLayer);
        if (hit.Count == 0)
            return false;
        var spike = new CrystalSpike(Catalog, prefab, Slime.Random.NextDouble() * 360);
        Slime.GetParent().AddChild(spike);
        spike.GlobalPosition = (Vector3)hit["position"];
        Spikes.Add(spike);
        SpikesMade++;
        return true;
    }
}

/// <summary>A crystal spike (<c>CrystalSpikesLifecycle</c>): hurts the player it touches by <c>damagePerHit</c>, gone after <c>lifetime</c> game hours.</summary>
public partial class CrystalSpike : Area3D
{
    private readonly ItemCatalog _catalog;
    private readonly double _dies;
    private readonly int _damage;

    public CrystalSpike(ItemCatalog catalog, ItemPrefab prefab, double yawDegrees)
    {
        _catalog = catalog;
        var life = prefab.RootScript("CrystalSpikesLifecycle");
        _damage = life?["damagePerHit"] is int d ? d : 0;
        // The lifetime counts from the new-game start if the clock hasn't reached it (HoursFromNowOrStart).
        _dies = Math.Max(catalog.Clock.TotalHours, OutsideHours.ClockStartHours) + (life?["lifetime"] is float h ? h : 0f);
        Name = prefab.Name;
        CollisionLayer = 0;
        CollisionMask = Actor.WorldLayer;
        Monitorable = false;
        RotationDegrees = new Vector3(0, (float)yawDegrees, 0);
        AddChild(catalog.VisualFor(prefab));
        foreach (var c in prefab.Colliders.Where(c => c.Collider.IsTrigger))
            if (ColliderShapes.Make(c.Collider, UnityConvert.Transform(c.ToRoot)) is { } shape)
                AddChild(shape);
        BodyEntered += b =>
        {
            if (b != _catalog.Player)
                return;
            Hits++;
            _catalog.PlayerVitals.Damage(_damage, prefab.Name);
        };
    }

    public int Damage => _damage;
    public double DiesAt => _dies;
    public int Hits { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (_catalog.Clock.TotalHours >= _dies)
            QueueFree();
    }
}

/// <summary>
/// Quantum slimes (<c>GenerateQuantumQubit</c>, <c>QuantumSlimeSuperposition</c>, <c>QuantumVibration</c>):
/// it leaves ghost copies (qubits) on clear ground within its search radius, up to its maximum (a new one
/// ends the oldest), every 5-20 s (sooner when agitated); while agitated past its cutoff it vibrates, and
/// every 5-30 s it jumps into one of its qubits and the rest fade.
/// </summary>
public sealed class QuantumGhosts : SlimeBehaviour
{
    private readonly float _searchRadius, _minGen, _maxGen, _minJump, _maxJump, _cutoff;
    private readonly int _maxQubits;
    private readonly List<Node3D> _qubits = [];
    private readonly ItemPrefab _prefab;
    private float _nextGeneration, _nextJump;
    private bool _jumped;

    public QuantumGhosts(ItemPrefab prefab)
    {
        _prefab = prefab;
        var gen = prefab.RootScript("GenerateQuantumQubit");
        var sup = prefab.RootScript("QuantumSlimeSuperposition");
        _searchRadius = F(gen, "QubitSearchRadius");
        _maxQubits = (int)F(gen, "MaxQubits");
        (_minGen, _maxGen) = (F(gen, "MinGenerationDelay"), F(gen, "MaxGenerationDelay"));
        (_minJump, _maxJump) = (F(sup, "MinSuperposeDelay"), F(sup, "MaxSuperposeDelay"));
        _cutoff = F(prefab.RootScript("QuantumVibration"), "AgitationCutoff");
    }

    public IReadOnlyList<Node3D> Qubits => _qubits;
    public int QubitsMade { get; private set; }
    public float SearchRadius => _searchRadius;
    public int MaxQubits => _maxQubits;
    public bool Vibrating => Slime.Sim.Agitation > _cutoff;

    protected override void Ready() => _nextJump = Slime.Age + _maxJump;

    public override void Tick(float delta)
    {
        if (Slime.Age < _nextGeneration || !Slime.Grounded || Slime.CaughtBy is not null)
            return;
        // openranch's simplification: up to five tries at once each time it is due (UNVERIFIED.md).
        for (var attempt = 0; attempt < QuantumSlime.MaxGenerationAttempts; attempt++)
            if (TryPlace())
                break;
        _nextGeneration = Slime.Age + QuantumSlime.Delay(_minGen, _maxGen, Slime.Sim.Agitation);
    }

    private bool TryPlace()
    {
        var angle = Slime.Random.NextDouble() * Math.PI * 2;
        var r = _searchRadius * MathF.Sqrt((float)Slime.Random.NextDouble());
        var spot = Slime.GlobalPosition + new Vector3((float)Math.Cos(angle) * r, 0, (float)Math.Sin(angle) * r);
        var hit = Slime.Ray(spot + Vector3.Up * 50, spot + Vector3.Down * 50, Actor.WorldLayer);
        if (hit.Count == 0)
            return false;
        var at = (Vector3)hit["position"] + Vector3.Up * QuantumSlime.QubitRadius;
        if (!Clear(at))
            return false;
        var qubit = new Node3D { Name = "Qubit" };
        var look = Catalog.VisualFor(_prefab);
        foreach (var g in look.FindChildren("*", nameof(GeometryInstance3D), true, false).OfType<GeometryInstance3D>())
            g.Transparency = 0.6f; // a ghostly copy; openranch's stand-in for the qubit appearance
        qubit.AddChild(look);
        Slime.GetParent().AddChild(qubit);
        qubit.GlobalPosition = at;
        _qubits.Add(qubit);
        QubitsMade++;
        if (_qubits.Count > _maxQubits)
        {
            _qubits[0].QueueFree();
            _qubits.RemoveAt(0);
        }
        return true;
    }

    private bool Clear(Vector3 at)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = QuantumSlime.ClearRadius },
            Transform = new Transform3D(Basis.Identity, at),
            CollisionMask = Actor.WorldLayer | Actor.ActorLayer,
            Exclude = [Slime.GetRid()],
        };
        return Slime.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    public void DueNow() => _nextJump = Slime.Age;

    public override float Relevancy(bool grounded) =>
        Vibrating && Slime.Age > _nextJump && _qubits.Count > 0 && Slime.CaughtBy is null ? QuantumSlime.Relevancy : 0f;

    public override bool CanRethink => _jumped || Slime.CaughtBy is not null;
    public override void Selected() => _jumped = false;
    public override void Deselected() => _nextJump = Slime.Age + QuantumSlime.Delay(_minJump, _maxJump, Slime.Sim.Agitation);

    public override void Action(float delta)
    {
        if (_jumped)
            return;
        var free = _qubits.Where(q => GodotObject.IsInstanceValid(q) && Clear(q.GlobalPosition)).ToList();
        if (free.Count == 0)
            return;
        var to = free[Slime.Random.Next(free.Count)];
        var from = Slime.GlobalPosition;
        Slime.GlobalPosition = to.GlobalPosition;
        _jumped = true;
        foreach (var q in _qubits)
            q.QueueFree();
        _qubits.Clear();
        Report($"jumped {from.DistanceTo(Slime.GlobalPosition):F1} m into a qubit (search radius {_searchRadius}, max {_maxQubits})");
    }
}

/// <summary>
/// Dervish slimes (<c>DervishSlimeSpin</c>): a stronger spin-hover (lift 600, up to 5 m, a largo 9 m) every
/// 10-25 s like a phosphor's hover; at 0.95 agitation or more each spin also lets loose a whirlwind (its
/// prefab from the slime's appearance) that lasts its <c>DestroyAfterTime</c> and pulls things up; at most six.
/// </summary>
public sealed class DervishSpin : SlimeBehaviour
{
    private readonly ItemPrefab? _whirlwind;
    private readonly bool _largo;
    private readonly Queue<Whirlwind> _whirlwinds = new();
    private float _next, _ends;
    private Vector3 _drift;

    public DervishSpin(ItemPrefabs prefabs, ItemPrefab prefab)
    {
        _whirlwind = prefabs.AppearancePrefab(prefab.Id, "TornadoAppearance", "fullWhirlwindPrefab")
                     ?? prefabs.Referenced(prefab.Id, "DervishSlimeSpin", "fullWhirlwindPrefab");
        _largo = prefab.VacuumSize != 0;
    }

    public int Whirlwinds => _whirlwinds.Count(w => GodotObject.IsInstanceValid(w));
    public Whirlwind? LastWhirlwind { get; private set; }

    protected override void Ready() => _next = Slime.Age + Delay();
    private float Delay() => SlimeTraits.Delay(SlimeTraits.HoverMinDelay, SlimeTraits.HoverMaxDelay, Slime.Sim.Agitation, Slime.Random.NextDouble());
    public void DueNow() => _next = Slime.Age;

    public override float Relevancy(bool grounded) => Slime.Age >= _next ? SlimeTraits.TraitRelevancy : 0f;
    public override bool CanRethink => Slime.Age >= _ends;

    public override void Selected()
    {
        _ends = Slime.Age + SlimeTraits.HoverSeconds;
        _next = _ends + Delay();
        _drift = new Vector3((float)Slime.Random.NextDouble() * 2 - 1, 0, (float)Slime.Random.NextDouble() * 2 - 1);
        var whirl = "";
        if (Slime.Sim.Agitation >= DervishSlime.WhirlwindAgitation && _whirlwind is not null)
        {
            var w = new Whirlwind(Catalog, _whirlwind);
            Slime.GetParent().AddChild(w);
            w.GlobalPosition = Slime.GlobalPosition;
            _whirlwinds.Enqueue(w);
            LastWhirlwind = w;
            while (_whirlwinds.Count > DervishSlime.MaxWhirlwinds)
                if (_whirlwinds.Dequeue() is { } old && GodotObject.IsInstanceValid(old))
                    old.QueueFree();
            whirl = $", whirlwind lasting {w.LifetimeHours} h";
        }
        Report($"spun up (lift {DervishSlime.HoverLift}, height {(_largo ? DervishSlime.LargoHoverHeight : DervishSlime.HoverHeight)} m){whirl}");
    }

    public override void Action(float delta)
    {
        if (Slime.Age >= _ends)
            return;
        var top = _largo ? DervishSlime.LargoHoverHeight : DervishSlime.HoverHeight;
        var below = Slime.Ray(Slime.GlobalPosition, Slime.GlobalPosition + Vector3.Down * top, Actor.WorldLayer | Actor.ActorLayer);
        if (below.Count > 0)
        {
            var height = Slime.GlobalPosition.DistanceTo((Vector3)below["position"]);
            Slime.ApplyCentralForce(Vector3.Up * (DervishSlime.LiftAt(height, _largo) * Slime.Mass * Catalog.FixedTimestep));
        }
        Slime.ApplyCentralForce(_drift * (SlimeTraits.HoverDrift * Slime.Mass * Catalog.FixedTimestep));
    }
}

/// <summary>
/// A dervish whirlwind: its prefab's trigger capsules, gone after <c>DestroyAfterTime.lifeTimeHours</c>.
/// What it does to things inside (the original's <c>ActorVortexer</c>: up to its height at its height
/// speed, then thrown out) is approximated: items inside are lifted and spun (UNVERIFIED.md).
/// </summary>
public partial class Whirlwind : Area3D
{
    private readonly ItemCatalog _catalog;
    private readonly double _dies;
    private readonly float _heightSpeed, _height;

    public Whirlwind(ItemCatalog catalog, ItemPrefab prefab)
    {
        _catalog = catalog;
        LifetimeHours = prefab.RootScript("DestroyAfterTime")?["lifeTimeHours"] is float h ? h : 0f;
        _dies = catalog.Clock.TotalHours + LifetimeHours;
        var vortex = prefab.RootScript("ActorVortexer");
        _heightSpeed = vortex?["heightSpeed"] is float s ? s : 0f;
        _height = vortex?["tornadoHeight"] is float t ? t : 0f;
        Name = prefab.Name;
        CollisionLayer = 0;
        CollisionMask = Actor.ActorLayer;
        Monitorable = false;
        foreach (var c in prefab.Colliders.Where(c => c.Collider.IsTrigger))
            if (ColliderShapes.Make(c.Collider, UnityConvert.Transform(c.ToRoot)) is { } shape)
                AddChild(shape);
    }

    public float LifetimeHours { get; }
    public int Caught { get; private set; }
    private readonly HashSet<ulong> _seen = [];

    public override void _PhysicsProcess(double delta)
    {
        if (_catalog.Clock.TotalHours >= _dies)
        {
            QueueFree();
            return;
        }
        foreach (var body in GetOverlappingBodies().OfType<Actor>())
        {
            if (body.Freeze)
                continue;
            if (_seen.Add(body.GetInstanceId()))
                Caught++;
            var off = (body.GlobalPosition - GlobalPosition) with { Y = 0 };
            var spin = off.LengthSquared() > 1e-4f ? Vector3.Up.Cross(off.Normalized()) : Vector3.Zero;
            var up = body.GlobalPosition.Y - GlobalPosition.Y < _height ? _heightSpeed : 0f;
            body.LinearVelocity = body.LinearVelocity.Lerp(spin * 4f + Vector3.Up * up - off * 0.5f, 0.1f);
        }
    }
}

/// <summary>
/// Tangle slimes' pollen (<c>PollenCloudController</c>): above the start agitation a pollen cloud grows on
/// it at its rate per game hour toward a size set by how agitated it is; at 95% it is let go (the prefab's
/// cloud actor), drifting forward 1 m/s, gone after its <c>PollenCloudDestructor.gameHrsToLive</c>.
/// </summary>
public sealed class PollenCloud : SlimeBehaviour
{
    private readonly float _ratePerHour, _start, _maxScale;
    private readonly ItemPrefab? _cloud;
    private float _size;

    public PollenCloud(ItemPrefabs prefabs, ItemPrefab prefab)
    {
        var data = prefab.RootScript("PollenCloudController");
        (_ratePerHour, _start, _maxScale) = (F(data, "pctGrowthPerGameHour"), F(data, "startGrowthAgitation"), F(data, "maxCloudScale"));
        _cloud = prefabs.Referenced(prefab.Id, "PollenCloudController", "cloudActorPrefab");
    }

    public float Size => _size;
    public float StartAgitation => _start;

    public override void Tick(float delta)
    {
        var target = TangleSlime.CloudTarget(Slime.Sim.Agitation, _start);
        var step = (float)(_ratePerHour * Catalog.Clock.HoursFor(delta));
        _size = target > _size ? Math.Min(target, _size + step) : Math.Max(target, _size - step);
        if (_size < TangleSlime.ReleaseFraction || _cloud is null)
            return;
        _size = 0;
        var cloud = new DriftingCloud(Catalog, _cloud, Slime.Forward * TangleSlime.CloudSpeed);
        Slime.GetParent().AddChild(cloud);
        cloud.GlobalPosition = Slime.GlobalPosition;
        Report($"let a pollen cloud go (starts at agitation {_start}, grows {_ratePerHour}/game hour, max scale {_maxScale}, lives {cloud.LifetimeHours} h)");
    }
}

/// <summary>A released pollen cloud: drifts at a steady velocity and is gone after its lifetime in game hours.</summary>
public partial class DriftingCloud : Node3D
{
    private readonly ItemCatalog _catalog;
    private readonly Vector3 _velocity;
    private readonly double _dies;

    public DriftingCloud(ItemCatalog catalog, ItemPrefab prefab, Vector3 velocity)
    {
        _catalog = catalog;
        _velocity = velocity;
        LifetimeHours = prefab.RootScript("PollenCloudDestructor")?["gameHrsToLive"] is float h ? h : 0f;
        _dies = catalog.Clock.TotalHours + LifetimeHours;
        Name = prefab.Name;
        AddChild(catalog.VisualFor(prefab));
    }

    public float LifetimeHours { get; }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _velocity * (float)delta;
        if (_catalog.Clock.TotalHours >= _dies)
            QueueFree();
    }
}

/// <summary>
/// Tangle slimes' vines (<c>GroundVine</c>): standing, and its cooldown over, it picks the food it wants
/// most within the vine's search radius (mattering drive² × 0.95); a vine grows under the food and lifts
/// it 3-4 m, then brings it 2-2.5 m up in front of the slime's mouth, and lets go so the slime eats it.
/// </summary>
public sealed class GroundVine : SlimeBehaviour
{
    private enum Phase { Idle, Grow, Bring, Done }

    private readonly float _maxSearch, _minDist, _cooldown;
    private Phase _phase;
    private float _next, _phaseEnds;
    private Actor? _target;

    public GroundVine(SerializedObject data) =>
        (_maxSearch, _minDist, _cooldown) = (F(data, "maxSearchRad"), F(data, "minDist"), F(data, "cooldown"));

    public string? LastFood { get; private set; }

    protected override void Ready() => _next = Slime.Age + _cooldown;

    public override float Relevancy(bool grounded)
    {
        if (Slime.Age < _next || !grounded || Slime.CaughtBy is not null)
            return 0f;
        var (food, drive) = FoodSearch.Nearest(Slime, _maxSearch, _minDist);
        _target = food;
        return food is null ? 0f : SlimeMotion.FoodRelevancy(drive);
    }

    public override bool CanRethink => _phase is Phase.Idle or Phase.Done;

    public override void Selected()
    {
        if (_target is null)
            return;
        _phase = Phase.Grow;
        var height = TangleSlime.MinExtraHeight + (float)Slime.Random.NextDouble() * (TangleSlime.MaxExtraHeight - TangleSlime.MinExtraHeight);
        _phaseEnds = Slime.Age + TangleSlime.FullVineSeconds * height / TangleSlime.FullVineHeight;
        _target.Freeze = true;
        LastFood = _target.Id;
    }

    public override void Deselected()
    {
        _next = Slime.Age + _cooldown;
        if (_target is { } t && GodotObject.IsInstanceValid(t) && !t.Consumed)
            t.Freeze = false;
        _target = null;
        _phase = Phase.Idle;
    }

    public override void Action(float delta)
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target) || _target.Consumed)
        {
            _phase = Phase.Done;
            return;
        }
        if (Slime.Age < _phaseEnds)
            return;
        if (_phase == Phase.Grow)
        {
            // Brought up in front of its mouth; openranch drops it on the slime (UNVERIFIED.md, vine path).
            var eatHeight = TangleSlime.MinEatHeight + (float)Slime.Random.NextDouble() * (TangleSlime.MaxEatHeight - TangleSlime.MinEatHeight);
            _target.GlobalPosition = Slime.GlobalPosition + Vector3.Up * Math.Min(eatHeight, Slime.Radius + 0.6f);
            _phase = Phase.Bring;
            _phaseEnds = Slime.Age + TangleSlime.FullVineSeconds * eatHeight / TangleSlime.FullVineHeight;
        }
        else if (_phase == Phase.Bring)
        {
            _target.Freeze = false;
            _target.LinearVelocity = Vector3.Zero;
            Report($"vined {_target.Id} (search radius {_maxSearch} m, cooldown {_cooldown} s)");
            _phase = Phase.Done;
        }
    }
}

/// <summary>
/// Hunter slimes' cloaking (<c>SlimeStealth</c>): invisible for 5 s after appearing, then seen unless
/// stalking; opacity moves 2 per second toward its target; always fully seen while held by the vacpack.
/// </summary>
public sealed class Stealth : SlimeBehaviour
{
    private float _opacity, _target = 1f, _initUntil;
    private bool _wasCloaked;

    public float Opacity => _opacity;
    public bool Cloaked => _opacity < 1f;
    public void SetStealth(bool on) => _target = on ? 0f : 1f;

    protected override void Ready() => _initUntil = HunterSlime.InitialStealthSeconds;

    public override void Tick(float delta)
    {
        var target = Slime.Age < _initUntil ? 0f : _target;
        _opacity = target > _opacity ? Math.Min(target, _opacity + HunterSlime.OpacityPerSecond * delta) : Math.Max(target, _opacity - HunterSlime.OpacityPerSecond * delta);
        var shown = Slime.CaughtBy is not null ? 1f : _opacity;
        foreach (var g in Slime.Visual.FindChildren("*", nameof(GeometryInstance3D), true, false).OfType<GeometryInstance3D>())
            g.Transparency = 1f - shown;
        if (Cloaked != _wasCloaked)
        {
            _wasCloaked = Cloaked;
            Report(Cloaked ? "cloaked" : $"decloaked after {Slime.Age:F1} s");
        }
    }
}

/// <summary>
/// Mosaic slimes' glints (<c>GlintController</c>): every 10 game minutes it checks; a new glint (its prefab
/// from the slime's appearance) appears each half game hour, sooner when agitated (× (1 − 0.8 × agitation)),
/// 7.5-30 m around it (farther when agitated), never below it, and fades after its phases.
/// </summary>
public sealed class Glints : SlimeBehaviour
{
    private readonly ItemPrefab? _glint;
    private readonly List<(Node3D Node, double Dies)> _live = [];
    private double _nextCheck, _nextSpawn;

    public Glints(ItemPrefabs prefabs, string slimeId) => _glint = prefabs.AppearancePrefab(slimeId, "GlintAppearance", "readyGlintPrefab");

    public int Made { get; private set; }
    public IEnumerable<Node3D> Live => _live.Select(l => l.Node).Where(n => GodotObject.IsInstanceValid(n));

    protected override void Ready() => _nextCheck = _nextSpawn = Catalog.Clock.TotalHours;
    public void DueNow() => _nextCheck = _nextSpawn = Catalog.Clock.TotalHours;

    public override void Tick(float delta)
    {
        var now = Catalog.Clock.TotalHours;
        for (var i = _live.Count - 1; i >= 0; i--)
            if (now >= _live[i].Dies)
            {
                if (GodotObject.IsInstanceValid(_live[i].Node))
                    _live[i].Node.QueueFree();
                _live.RemoveAt(i);
            }
        if (now < _nextCheck || _glint is null)
            return;
        _nextCheck = now + MosaicSlime.UpdateHours;
        var agitation = Slime.Sim.Agitation;
        while (_nextSpawn <= now)
        {
            _nextSpawn += MosaicSlime.AdjustHours(MosaicSlime.SpawnHours, agitation);
            var dir = new Vector3((float)Slime.Random.NextDouble() * 2 - 1, (float)Slime.Random.NextDouble() * 2 - 1, (float)Slime.Random.NextDouble() * 2 - 1);
            var offset = dir.LimitLength(1f) * (float)Slime.Random.NextDouble() * MosaicSlime.SpawnRadius(agitation);
            offset.Y = Math.Abs(offset.Y);
            var glint = new Node3D { Name = "Glint" };
            glint.AddChild(Catalog.VisualFor(_glint));
            Slime.GetParent().AddChild(glint);
            glint.GlobalPosition = Slime.GlobalPosition + offset;
            // Suspended, then ready, then free (UNVERIFIED.md: openranch keeps one glint for all three).
            var life = MosaicSlime.AdjustHours(1f, agitation) + MosaicSlime.AdjustHours(0.5f, agitation) + 0.5f;
            _live.Add((glint, now + life));
            Made++;
            Report($"glint {offset.Length():F1} m away (spawn radius {MosaicSlime.SpawnRadius(agitation):F1} m at agitation {agitation:F2})");
        }
    }
}

/// <summary>Finding the food a slime wants most near it, the way the original's food searches score it (drive ÷ distance²).</summary>
public static class FoodSearch
{
    public static (Actor? Food, float Drive) Nearest(SlimeActor slime, float maxRadius, float minDist, Func<Actor, bool>? also = null)
    {
        Actor? best = null;
        float bestScore = 1f / (maxRadius * maxRadius), bestDrive = 0f;
        foreach (var item in slime.Catalog.Live)
        {
            if (item == slime || item.CaughtBy is not null || !item.Edible || item.Freeze || slime.Sim.Species.FoodEffect(item.Id) is not { } effect)
                continue;
            if (also is not null && !also(item))
                continue;
            var d2 = slime.GlobalPosition.DistanceSquaredTo(item.GlobalPosition);
            if (d2 < minDist * minDist)
                continue;
            var drive = slime.Sim.Drive(effect);
            var score = drive / Math.Max(d2, 1e-4f);
            if (score > bestScore)
                (best, bestScore, bestDrive) = (item, score, drive);
        }
        return (best, bestDrive);
    }
}
