using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Scene;

/// <summary>
/// The region sets of the world (RegionRegistry.RegionSetId: HOME, DESERT, VALLEY, VIKTOR_LAB, SLIMULATIONS) and which
/// set each zone's cells belong to. Only one set is live at a time: the one the player is in. The numbers come from the
/// install's enums; which zone goes in which set is code-only (static analysis: ZoneDirector.GetRegionSetId, called by
/// Region.Awake with the zone of the ZoneDirector above the region). See docs/behavior/zones.md.
/// </summary>
public sealed class RegionSets
{
    // ZoneDirector.GetRegionSetId, by enum name.
    private static readonly (string Set, string[] Zones)[] SetOfZone =
    [
        ("HOME", ["RANCH", "REEF", "QUARRY", "MOSS", "SEA", "RUINS", "RUINS_TRANSITION", "WILDS", "OGDEN_RANCH"]),
        ("DESERT", ["DESERT"]),
        ("VALLEY", ["VALLEY", "MOCHI_RANCH"]),
        ("VIKTOR_LAB", ["VIKTOR_LAB"]),
        ("SLIMULATIONS", ["SLIMULATIONS"]),
    ];

    private readonly Dictionary<int, int> _setOfZone = new();
    private readonly EnumValues _sets;

    private RegionSets(EnumValues zones, EnumValues sets)
    {
        _sets = sets;
        foreach (var (set, names) in SetOfZone)
            foreach (var zone in names)
                _setOfZone[(int)zones.ValueOf(zone)] = (int)sets.ValueOf(set);
        Home = (int)sets.ValueOf("HOME");
    }

    /// <summary>The HOME set: The Ranch and the zones reached on foot from it, the Wilds and Ogden's retreat.</summary>
    public int Home { get; }

    public static RegionSets Read(GameInstall install)
    {
        using var types = new ManagedTypes(install.ManagedDirectory);
        return new RegionSets(EnumValues.Read(types, GameScripts.GameAssembly, "", "ZoneDirector/Zone"),
            EnumValues.Read(types, GameScripts.GameAssembly, "MonomiPark.SlimeRancher.Regions", "RegionRegistry/RegionSetId"));
    }

    /// <summary>The region set of a zone id (ZoneDirector.Zone).</summary>
    public int SetOf(int zone) => _setOfZone.TryGetValue(zone, out var set) ? set
        : throw new ArgumentException($"Zone {zone} has no region set.", nameof(zone));

    public string NameOf(int set) => _sets.NameOf(set);
}

/// <summary>
/// The player's region loader (<c>RegionLoader</c> on the player rig): the box around the player, by full size, inside
/// which regions load (<see cref="LoadSize"/>) and actors wake (<see cref="WakeSize"/>), and how much larger the box a
/// loaded region must leave before it unloads (<see cref="UnloadBuffer"/>, a fraction of the size).
/// </summary>
public sealed record RegionLoaderSettings(Vector3 WakeSize, Vector3 LoadSize, float UnloadBuffer)
{
    // RegionLoader's field defaults, used only when the rig has no loader (static analysis: RegionLoader).
    public static RegionLoaderSettings Default { get; } = new(new Vector3(20, 10, 20), new Vector3(20, 10, 20), 0.1f);

    /// <summary>Reads the loader on the world scene's player rig: WakeSize, LoadSize, UnloadBuffer in stored order.</summary>
    public static RegionLoaderSettings Read(AssetSet assets, SerializedFile scene, string rigName = "SimplePlayer")
    {
        if (!ZoneExtractor.RootObjects(assets, scene).TryGetValue(rigName, out var root))
            return Default;
        var t = assets.Read(root, TransformData.Read);
        var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
        foreach (var c in go?.Components ?? [])
        {
            if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.MonoBehaviour } behaviour)
                continue;
            var r = assets.Reader(behaviour);
            var (_, _, script, _) = MonoBehaviourReader.ReadHeader(r);
            if (assets.Resolve(scene, script) is not { } scriptRef
                || assets.Read(scriptRef, MonoBehaviourReader.ReadMonoScript).ClassName != "RegionLoader")
                continue;
            return new RegionLoaderSettings(r.ReadVector3(), r.ReadVector3(), r.ReadSingle());
        }
        return Default;
    }
}

/// <summary>
/// Which cells are loaded as the player moves (static analysis: RegionLoader.UpdateProxied): a cell loads once its
/// region box overlaps the load box centred on the player, and unloads once it no longer overlaps that box grown by the
/// unload buffer. The check only runs after the player has moved at least 1 m since the last one (REGION_UPDATE_DIST),
/// or when forced (a teleport, a change of region set).
/// </summary>
public sealed class RegionLoader
{
    // RegionLoader.REGION_UPDATE_DIST, metres.
    public const float UpdateDistance = 1f;

    private readonly RegionLoaderSettings _settings;
    private readonly IReadOnlyList<CellArea> _cells;
    private readonly HashSet<int> _loaded = [];
    private Vector3? _lastCheck;

    public RegionLoader(RegionLoaderSettings settings, IReadOnlyList<CellArea> cells)
    {
        _settings = settings;
        _cells = cells;
    }

    /// <summary>The indices (into the cell list) of the loaded cells.</summary>
    public IReadOnlySet<int> Loaded => _loaded;

    /// <summary>
    /// Moves the loader to <paramref name="position"/> (Unity coordinates) and returns the cells that load and unload
    /// there, or nothing when the player hasn't moved far enough to check again.
    /// </summary>
    public (List<int> Load, List<int> Unload) Update(Vector3 position, bool force = false)
    {
        if (!force && _lastCheck is { } last && Vector3.DistanceSquared(last, position) < UpdateDistance * UpdateDistance)
            return ([], []);
        _lastCheck = position;
        var load = new List<int>();
        var unload = new List<int>();
        var half = _settings.LoadSize / 2;
        var outer = half * (1 + _settings.UnloadBuffer);
        for (var i = 0; i < _cells.Count; i++)
        {
            if (_loaded.Contains(i))
            {
                if (!Overlaps(_cells[i], position, outer))
                {
                    _loaded.Remove(i);
                    unload.Add(i);
                }
            }
            else if (Overlaps(_cells[i], position, half))
            {
                _loaded.Add(i);
                load.Add(i);
            }
        }
        return (load, unload);
    }

    /// <summary>Loads every cell at once (for checks that need the whole set).</summary>
    public List<int> LoadAll()
    {
        var load = Enumerable.Range(0, _cells.Count).Where(i => _loaded.Add(i)).ToList();
        return load;
    }

    // Unity's Bounds.Intersects: the boxes touch or overlap on every axis.
    public static bool Overlaps(CellArea cell, Vector3 center, Vector3 halfSize)
    {
        var d = Vector3.Abs(cell.Center - center);
        var reach = cell.Extent + halfSize;
        return d.X <= reach.X && d.Y <= reach.Y && d.Z <= reach.Z;
    }
}

/// <summary>
/// The teleport network: destinations by name and the rules that open and shut links (static analysis:
/// TeleportNetwork, TeleportSource, TeleportDestination). A source sends the player to one of the destinations named by
/// its <see cref="TeleportSourceItem.Destination"/> whose link is active, picked at random. A destination that is also a
/// source (a two-way teleporter) is active only while its own source's link is, and after an arrival there its source
/// waits until the player has left its trigger, so the player isn't sent straight back.
/// </summary>
public sealed class TeleportNetwork
{
    /// <summary>A destination with the region set of its zone (TeleportDestination.regionSetId, from its Region).</summary>
    public sealed record Destination(TeleportPoint Point, int RegionSet, TeleportSourceItem? Source);

    private readonly Dictionary<string, List<Destination>> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<long> _waitForTriggerExit = [];
    private readonly HashSet<long> _externallyActivated = [];

    /// <param name="destinations">Every destination of the world with its region set (debug start points are left out).</param>
    /// <param name="sources">Every source of the world, to pair two-way teleporters (same object).</param>
    public TeleportNetwork(IEnumerable<(TeleportPoint Point, int RegionSet)> destinations, IEnumerable<TeleportSourceItem> sources)
    {
        var sourceByObject = new Dictionary<long, TeleportSourceItem>();
        foreach (var s in sources)
            sourceByObject.TryAdd(s.ObjectId, s);
        foreach (var (point, set) in destinations.Where(d => !d.Point.IsDebugStart))
        {
            if (!_byName.TryGetValue(point.Name, out var list))
                _byName[point.Name] = list = [];
            list.Add(new Destination(point, set, sourceByObject.GetValueOrDefault(point.ObjectId)));
        }
    }

    /// <summary>The player's progress counters (a save's, or none in a new game).</summary>
    public IReadOnlyDictionary<int, int> Progress { get; set; } = new Dictionary<int, int>();

    /// <summary>Blocker objects (game object path ids) no longer in the way, such as popped gordos.</summary>
    public HashSet<long> ClearedBlockers { get; } = [];

    public IReadOnlyList<Destination> Destinations(string name) => _byName.TryGetValue(name, out var list) ? list : [];

    /// <summary>TeleportSource.IsLinkActive: whether this source would send the player anywhere at all, on its own side.</summary>
    public bool IsLinkActive(TeleportSourceItem source)
    {
        if (_waitForTriggerExit.Contains(source.ObjectId))
            return false;
        if ((source.WaitForExternalActivation || source.InEchoNoteGordo) && !_externallyActivated.Contains(source.ObjectId))
            return false;
        if (source.Blocker is { } blocker && source.BlockerActive && !ClearedBlockers.Contains(blocker))
            return false;
        // ProgressModel.HasProgress: the counter is above 0. -1 is ProgressType.NONE.
        if (source.ActivationProgress != -1 && !(Progress.TryGetValue(source.ActivationProgress, out var p) && p > 0))
            return false;
        // A quicksilver generator only blocks while it runs; none run before the player starts one.
        return true;
    }

    /// <summary>TeleportDestination.IsLinkActive: its own source's link, or always for an arrival-only destination.</summary>
    public bool IsLinkActive(Destination destination) => destination.Source is not { } s || IsLinkActive(s);

    /// <summary>TeleportNetwork.IsLinkFullyActive: the source is open and at least one destination of its name is.</summary>
    public bool IsLinkFullyActive(TeleportSourceItem source) =>
        IsLinkActive(source) && Destinations(source.Destination).Any(IsLinkActive);

    /// <summary>
    /// The player touched <paramref name="source"/>'s trigger: the destination they go to (one of the active ones of its
    /// name, picked with <paramref name="random"/>), or null when the link is shut. Marks the destination's own source to
    /// wait for the player to leave its trigger.
    /// </summary>
    public Destination? Enter(TeleportSourceItem source, Random random)
    {
        if (!IsLinkFullyActive(source))
            return null;
        var open = Destinations(source.Destination).Where(IsLinkActive).ToList();
        var picked = open[random.Next(open.Count)];
        if (picked.Source is { } back)
            _waitForTriggerExit.Add(back.ObjectId);
        return picked;
    }

    /// <summary>The player left <paramref name="source"/>'s trigger (TeleportSource.OnTriggerExit).</summary>
    public void Exit(TeleportSourceItem source) => _waitForTriggerExit.Remove(source.ObjectId);

    /// <summary>A source switched off or unloaded forgets that it was waiting (TeleportSource.OnDisable).</summary>
    public void Disable(TeleportSourceItem source) => _waitForTriggerExit.Remove(source.ObjectId);

    /// <summary>Opens a source that waits for something else to switch it on (TeleportSource.ExternalActivate).</summary>
    public void ExternalActivate(TeleportSourceItem source) => _externallyActivated.Add(source.ObjectId);
}
