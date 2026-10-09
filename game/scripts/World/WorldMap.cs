using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.Player;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// The whole world scene as one walkable world (docs/behavior/zones.md, "Joining the zones"). Every zone root is read
/// once and kept; the zones share one coordinate space, so they are built side by side without offsets. Only one region
/// set is live at a time (the player's): its zones' always-there objects stand, each of its cells loads and unloads by
/// its region box as the player moves (<see cref="RegionLoader"/>), and an unloaded cell shows its low-detail proxy.
/// Teleporters send the player to their destination by name, switching the region set when the destination is in
/// another one; kill volumes of the live set send a player who falls in back to the set's wake-up point.
/// </summary>
public partial class WorldMap : Node3D
{
    // A bit of the player's collision layer that only teleporter triggers look for (openranch's own, not the game's).
    private const uint TriggerBit = 1u << 19;
    // PlayerDeathHandler.ResetPlayer: the player is moved one second after dying.
    private const double DeathDelaySeconds = 1;

    private sealed class Zone(ZoneParts parts, int set)
    {
        public ZoneParts Parts { get; set; } = parts;
        public int Set { get; } = set;
        public Node3D? Node;
    }

    private sealed class Cell(Zone zone, RegionPart part)
    {
        public Zone Zone { get; } = zone;
        public RegionPart Part { get; } = part;
        public Node3D? Built;
        public Node3D? Proxy;
        public bool Loaded;
    }

    private readonly List<Zone> _zones;
    private readonly RegionSets _sets;
    private readonly RegionLoaderSettings _loaderSettings;
    private readonly WorldAssets _assets;
    private readonly PhysicsLayers _layers;
    private readonly WorldLighting _lighting;
    private readonly TimedObjects _timed;
    private readonly Dictionary<int, List<Cell>> _cellsBySet = new();
    private readonly Queue<Cell> _toBuild = new();
    // The game picks among a name's destinations with its shared random generator; a fixed seed keeps runs repeatable.
    private readonly Random _random = new(0);
    private List<Cell> _cells = [];
    private RegionLoader? _loader;
    private List<TriggerVolume> _kills = [];
    private PlayerController? _player;
    private TeleportNetwork.Destination? _pendingTeleport;
    private double _deathTimer = -1;
    private Node? _actors;
    private int _hibernateFrame;

    private WorldMap(List<Zone> zones, RegionSets sets, RegionLoaderSettings loader, WorldAssets assets, PhysicsLayers layers,
        WorldLighting lighting, TimedObjects timed)
    {
        Name = "World";
        _zones = zones;
        _sets = sets;
        _loaderSettings = loader;
        _assets = assets;
        _layers = layers;
        _lighting = lighting;
        _timed = timed;
        foreach (var zone in zones)
        {
            if (!_cellsBySet.TryGetValue(zone.Set, out var list))
                _cellsBySet[zone.Set] = list = [];
            list.AddRange(zone.Parts.Regions.Select(r => new Cell(zone, r)));
        }
        Network = new TeleportNetwork(zones.SelectMany(z => z.Parts.Always.Teleports.Select(t => (t, z.Set))),
            zones.SelectMany(z => z.Parts.Merged().TeleportSources));
    }

    /// <summary>The live region set (RegionRegistry.RegionSetId), -1 before <see cref="Start"/>.</summary>
    public int CurrentSet { get; private set; } = -1;

    public RegionSets Sets => _sets;
    public TeleportNetwork Network { get; }

    /// <summary>How long reading every zone root took, in milliseconds.</summary>
    public long ReadMilliseconds { get; private init; }

    /// <summary>Cells built so far, and the time spent building them (ms), for the report.</summary>
    public int CellsBuilt { get; private set; }
    public long BuildMilliseconds { get; private set; }

    /// <summary>Load every cell of the live set at once, as if the player stood everywhere (for checks).</summary>
    public bool LoadAll { get; set; }

    /// <summary>Called when the live region set changes (the new set).</summary>
    public event Action<int>? SetChanged;

    /// <summary>Called when cells load or unload (cell paths loaded, unloaded).</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? CellsChanged;

    /// <summary>Called after a teleport (source destination name, where the player landed).</summary>
    public event Action<TeleportNetwork.Destination>? Teleported;

    /// <summary>
    /// Reads every zone root of the world scene. <paramref name="saveFor"/> gives, for a zone with land plot sites, the
    /// world state with the scene plots the save replaced hidden and a function that adds the save's plots, or null.
    /// </summary>
    public static WorldMap Create(GameInstall install, AssetSet assets, SerializedFile scene, WorldState state,
        Func<string, (WorldState State, Func<ZoneExtract, ZoneExtract> Apply)?>? saveFor, PhysicsLayers layers, WorldAssets world,
        WorldLighting lighting, TimedObjects timed)
    {
        var clock = Stopwatch.StartNew();
        var sets = RegionSets.Read(install);
        var zones = new List<Zone>();
        foreach (var name in ZoneExtractor.RootObjects(assets, scene).Keys.Where(n => n.StartsWith("zone", StringComparison.Ordinal)).OrderBy(n => n))
        {
            var parts = ZoneExtractor.ExtractParts(assets, scene, name, state);
            if (parts.Zone < 0)
                continue;
            // A save's plots stand on the zone's sites in place of the scene's own (SaveLoad/SavedRanch.cs).
            if (parts.PlotSites > 0 && saveFor?.Invoke(name) is { } saved)
            {
                parts = ZoneExtractor.ExtractParts(assets, scene, name, saved.State);
                parts = parts with { Always = saved.Apply(parts.Always) };
            }
            zones.Add(new Zone(parts, sets.SetOf(parts.Zone)));
        }
        return new WorldMap(zones, sets, RegionLoaderSettings.Read(assets, scene), world, layers, lighting, timed)
        {
            ReadMilliseconds = clock.ElapsedMilliseconds,
        };
    }

    /// <summary>A zone with every cell's objects in one list (saved plots included), e.g. The Ranch for milestones 2 and 3.</summary>
    public ZoneExtract Merged(string zoneName) => _zones.First(z => z.Parts.Name == zoneName).Parts.Merged();

    /// <summary>Every zone root of a region set, merged, for checks over the joined world.</summary>
    public ZoneExtract MergedSet(int set)
    {
        var merged = _zones.Where(z => z.Set == set).Select(z => z.Parts.Merged()).ToList();
        return merged[0] with
        {
            Name = _sets.NameOf(set),
            Renderers = merged.SelectMany(z => z.Renderers).ToList(),
            Colliders = merged.SelectMany(z => z.Colliders).ToList(),
            Caves = merged.SelectMany(z => z.Caves).ToList(),
            Lights = merged.SelectMany(z => z.Lights).ToList(),
            Timed = merged.SelectMany(z => z.TimedGroups).ToList(),
            CellAreas = merged.SelectMany(z => z.Cells).ToList(),
            TeleportPoints = merged.SelectMany(z => z.Teleports).ToList(),
            Kills = merged.SelectMany(z => z.KillVolumes).ToList(),
            Sources = merged.SelectMany(z => z.TeleportSources).ToList(),
        };
    }

    /// <summary>The zone names of a region set.</summary>
    public IEnumerable<string> ZonesOf(int set) => _zones.Where(z => z.Set == set).Select(z => z.Parts.Name);

    /// <summary>The cells of a region set.</summary>
    public IEnumerable<CellArea> CellsOf(int set) => _cellsBySet.GetValueOrDefault(set)?.Select(c => c.Part.Cell) ?? [];

    /// <summary>Every teleport destination with its zone's region set (debug start points included).</summary>
    public IEnumerable<(TeleportPoint Point, int Set)> Destinations => _zones.SelectMany(z => z.Parts.Always.Teleports.Select(t => (t, z.Set)));

    /// <summary>Every teleporter entrance of the world, with the region set of its zone.</summary>
    public IEnumerable<TeleportSourceItem> Sources => _zones.SelectMany(z => z.Parts.Merged().TeleportSources);

    /// <summary>The region set whose cells hold <paramref name="unity"/> (the first in set order), or null.</summary>
    public int? SetAt(N.Vector3 unity) =>
        _cellsBySet.OrderBy(kv => kv.Key).FirstOrDefault(kv => kv.Value.Any(c => c.Part.Cell.Contains(unity))).Value?[0].Zone.Set;

    /// <summary>The cells loaded now (paths).</summary>
    public IEnumerable<string> LoadedCells => _cells.Where(c => c.Loaded).Select(c => c.Part.Cell.Path);

    /// <summary>Whether a point lies in a loaded, built cell of the live set.</summary>
    public bool IsLoadedAt(N.Vector3 unity) => _cells.Any(c => c.Loaded && c.Built is not null && c.Part.Cell.Contains(unity));

    /// <summary>
    /// Makes <paramref name="set"/> live and puts the player at <paramref name="unity"/> (feet, Unity coordinates), turned
    /// by <paramref name="yawDegrees"/> and <paramref name="pitchDegrees"/> (Unity's, null to keep the player's view). The
    /// cells around the player are built before this returns.
    /// </summary>
    public void Start(PlayerController player, N.Vector3 unity, int set, float? yawDegrees = null, float pitchDegrees = 0)
    {
        _player = player;
        player.CollisionLayer |= TriggerBit;
        MovePlayer(unity, set, yawDegrees, pitchDegrees);
    }

    /// <summary>Loads and unloads cells for where the player stands now (after <see cref="LoadAll"/> changes), building at once.</summary>
    public void Restream()
    {
        if (_player is not null)
            Stream(UnityPosition(_player.GlobalPosition), force: true, now: true);
    }

    /// <summary>Freezes the actors under <paramref name="actors"/> that stand outside every loaded cell (see Hibernate).</summary>
    public void Manage(Node actors) => _actors = actors;

    private void MovePlayer(N.Vector3 unity, int set, float? yawDegrees, float pitchDegrees)
    {
        if (set != CurrentSet)
            EnterSet(set);
        Stream(unity, force: true, now: true);
        var player = _player!;
        player.GlobalPosition = UnityConvert.Position(unity);
        player.Velocity = Vector3.Zero;
        // Unity yaw turns clockwise seen from above and positive pitch looks down; Godot is the reverse.
        if (yawDegrees is { } yaw)
            player.Look(-yaw, -pitchDegrees);
    }

    // Makes a region set live: the old set's objects leave the scene tree (kept built), the new set's always-there
    // objects and every cell's proxy go in, and the lighting follows the new set's cells and caves.
    private void EnterSet(int set)
    {
        var clock = Stopwatch.StartNew();
        foreach (var cell in _cells)
        {
            Detach(cell.Built);
            Detach(cell.Proxy);
            cell.Loaded = false;
        }
        foreach (var zone in _zones.Where(z => z.Set == CurrentSet))
            Detach(zone.Node);
        foreach (var source in _zones.Where(z => z.Set == CurrentSet).SelectMany(z => z.Parts.Merged().TeleportSources))
            Network.Disable(source);
        _toBuild.Clear();

        var previous = CurrentSet;
        CurrentSet = set;
        _cells = _cellsBySet.GetValueOrDefault(set) ?? [];
        _loader = new RegionLoader(_loaderSettings, _cells.Select(c => c.Part.Cell).ToList());
        var zones = _zones.Where(z => z.Set == set).ToList();
        foreach (var zone in zones)
        {
            zone.Node ??= BuildPart(zone.Parts.Always, zone.Parts.Name);
            AddChild(zone.Node);
        }
        foreach (var cell in _cells)
        {
            cell.Proxy ??= BuildProxy(cell.Part);
            if (cell.Proxy is not null)
                AddChild(cell.Proxy);
        }
        // Only kill volumes the player's layer meets (the Ranch's "NonPlayerKillVolume"s are on one it doesn't).
        _kills = zones.SelectMany(z => z.Parts.Always.KillVolumes).Where(k => _layers.Collide(PhysicsLayers.PlayerLayer, (int)k.Layer)).ToList();
        _lighting.SetArea(_cells.Select(c => c.Part.Cell).ToList(), zones.SelectMany(z => z.Parts.Always.Caves).ToList());
        GD.Print($"World: region set {_sets.NameOf(set)} live ({string.Join(", ", zones.Select(z => z.Parts.Name))}): {_cells.Count} cells, " +
                 $"{_kills.Count} kill volumes, set up in {clock.ElapsedMilliseconds} ms" + (previous >= 0 ? $" (was {_sets.NameOf(previous)})" : ""));
        SetChanged?.Invoke(set);
    }

    private static void Detach(Node? node) => node?.GetParent()?.RemoveChild(node);

    // Builds one part of the world: meshes, collision, lamps, objects tied to the time of day and teleporter triggers.
    private Node3D BuildPart(ZoneExtract part, string name)
    {
        var clock = Stopwatch.StartNew();
        var built = ZoneBuilder.Build(part with { Name = name.Split('/').Last() }, _assets, _layers);
        foreach (var lamp in part.Lights)
            _lighting.AddLamp(lamp, built.Root);
        _timed.Add(part, _assets, _layers, _lighting, built.Root);
        foreach (var source in part.TeleportSources)
            built.Root.AddChild(SourceTrigger(source));
        BuildMilliseconds += clock.ElapsedMilliseconds;
        return built.Root;
    }

    // The region's low-detail stand-in, at the cell object's place (Region.CreateProxy: no shadows).
    private Node3D? BuildProxy(RegionPart part)
    {
        if (part.ProxyMesh is not { } meshRef)
            return null;
        var mirrored = UnityConvert.IsMirrored(part.World);
        var subMeshes = _assets.Mesh(meshRef).SubMeshes.Count;
        var parts = Enumerable.Range(0, subMeshes).Select(i => (i, part.ProxyMaterials.Count == 0 ? (AssetRef?)null
            : part.ProxyMaterials[Math.Min(i, part.ProxyMaterials.Count - 1)]));
        var mesh = _assets.BuildMesh(meshRef, parts, mirrored);
        var transform = UnityConvert.Transform(part.World);
        // Mirrored meshes have reversed winding baked in; a multimesh keeps Godot from flipping culling again.
        if (mirrored)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 1 };
            multi.SetInstanceTransform(0, transform);
            return new MultiMeshInstance3D { Name = part.Cell.Path.Split('/').Last() + " Proxy", Multimesh = multi,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }
        return new MeshInstance3D { Name = part.Cell.Path.Split('/').Last() + " Proxy", Mesh = mesh, Transform = transform,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private Area3D SourceTrigger(TeleportSourceItem source)
    {
        var area = new Area3D
        {
            Name = "Teleport " + source.Destination,
            CollisionLayer = 0,
            CollisionMask = TriggerBit,
            Monitorable = false,
        };
        if (ZoneBuilder.MakeShape(source.Trigger, _assets) is { } shape)
            area.AddChild(shape);
        area.BodyEntered += body =>
        {
            if (body == _player && _pendingTeleport is null && _deathTimer < 0 && Network.Enter(source, _random) is { } destination)
                _pendingTeleport = destination;
        };
        area.BodyExited += body =>
        {
            if (body == _player)
                Network.Exit(source);
        };
        return area;
    }

    // RegionLoader: load and unload cells around the player. A cell that loads is built at once when the player is
    // being placed (now), otherwise queued and built one per frame while its proxy still shows.
    private void Stream(N.Vector3 unity, bool force = false, bool now = false)
    {
        if (_loader is null)
            return;
        var (load, unload) = LoadAll ? (_loader.LoadAll(), new List<int>()) : _loader.Update(unity, force);
        if (load.Count == 0 && unload.Count == 0)
            return;
        foreach (var i in unload)
        {
            var cell = _cells[i];
            cell.Loaded = false;
            Detach(cell.Built);
            if (cell.Proxy is not null && cell.Proxy.GetParent() is null)
                AddChild(cell.Proxy);
            foreach (var source in cell.Part.Content.TeleportSources)
                Network.Disable(source);
        }
        foreach (var i in load)
        {
            var cell = _cells[i];
            cell.Loaded = true;
            if (cell.Built is not null || now)
                Show(cell);
            else
                _toBuild.Enqueue(cell);
        }
        CellsChanged?.Invoke(load.Select(i => _cells[i].Part.Cell.Path).ToList(), unload.Select(i => _cells[i].Part.Cell.Path).ToList());
    }

    private void Show(Cell cell)
    {
        if (cell.Built is null)
        {
            cell.Built = BuildPart(cell.Part.Content, cell.Part.Cell.Path);
            CellsBuilt++;
        }
        if (cell.Built.GetParent() is null)
            AddChild(cell.Built);
        Detach(cell.Proxy);
    }

    public override void _Process(double delta)
    {
        // One queued cell per frame keeps a hitch to one cell's build.
        while (_toBuild.Count > 0)
        {
            var cell = _toBuild.Dequeue();
            if (!cell.Loaded)
                continue;
            Show(cell);
            break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
            return;
        var feet = UnityPosition(_player.GlobalPosition);

        if (_pendingTeleport is { } destination)
        {
            _pendingTeleport = null;
            Teleport(destination);
            return;
        }

        // KillOnTrigger: the player's body entering a kill volume dies (DeathHandler.Kill), and a second later wakes at
        // the wake-up point of their region set (PlayerDeathHandler.ResetPlayer). The feet stand in for the body.
        if (_deathTimer < 0 && _kills.Any(k => k.Contains(feet)))
        {
            _deathTimer = DeathDelaySeconds;
            GD.Print($"World: the player fell into a kill volume at ({feet.X:F1}, {feet.Y:F1}, {feet.Z:F1})");
        }
        else if (_deathTimer >= 0 && (_deathTimer -= delta) < 0)
            WakeUp();

        Stream(feet);
        if (_actors is not null && ++_hibernateFrame % 10 == 0)
            Hibernate();
    }

    /// <summary>Sends the player to a destination, as the teleporter would (TeleportablePlayer.TeleportTo).</summary>
    public void Teleport(TeleportNetwork.Destination destination)
    {
        var p = destination.Point;
        var (pitch, yaw) = UnityEuler(p.World);
        MovePlayer(p.World.Translation, destination.RegionSet, p.Reorient ? yaw : null, p.Reorient ? pitch : 0);
        GD.Print($"World: teleported to {p.Name} ({p.World.Translation.X:F1}, {p.World.Translation.Y:F1}, {p.World.Translation.Z:F1}) " +
                 $"in {_sets.NameOf(destination.RegionSet)}");
        Teleported?.Invoke(destination);
    }

    private void WakeUp()
    {
        // SceneContext.GetWakeUpDestination: the live set's own wake-up point, or the HOME set's.
        var wakeUps = _zones.SelectMany(z => z.Parts.WakeUps.Select(w => (Wake: w, z.Set))).ToList();
        var (wake, set) = wakeUps.FirstOrDefault(w => w.Wake.DeathRegionSet == CurrentSet);
        if (wake is null)
            (wake, set) = wakeUps.FirstOrDefault(w => w.Wake.DeathRegionSet == _sets.Home);
        if (wake is null)
            return;
        var (pitch, yaw) = UnityEuler(wake.World);
        MovePlayer(wake.World.Translation, set, yaw, pitch);
        GD.Print($"World: the player woke at {wake.Path} in {_sets.NameOf(set)}");
    }

    // Actors (slimes, food, plorts) outside every loaded cell of the live set are frozen: off the physics world and not
    // processed, until their cell loads. The original hibernates its region members this way when their regions are
    // unloaded or in another set (RegionMember.UpdateHibernation); its smaller wake box around the player isn't modelled.
    private void Hibernate()
    {
        foreach (var child in _actors!.GetChildren())
        {
            if (child is not Node3D actor)
                continue;
            var awake = IsLoadedAt(UnityPosition(actor.GlobalPosition));
            var mode = awake ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            if (actor.ProcessMode != mode)
                actor.ProcessMode = mode;
        }
    }

    /// <summary>Unity's Euler angles of a world matrix, pitch (x) and yaw (y) in degrees (rotation order Z, X, Y).</summary>
    public static (float Pitch, float Yaw) UnityEuler(N.Matrix4x4 m)
    {
        var scale = new N.Vector3(new N.Vector3(m.M31, m.M32, m.M33).Length());
        var x = MathF.Asin(Math.Clamp(-m.M32 / scale.X, -1, 1));
        var y = MathF.Atan2(m.M31, m.M33);
        return (x * 180 / MathF.PI, y * 180 / MathF.PI);
    }

    private static N.Vector3 UnityPosition(Vector3 godot) => new(godot.X, godot.Y, -godot.Z);
}
