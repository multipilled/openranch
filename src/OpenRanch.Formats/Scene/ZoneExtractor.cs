using System.Numerics;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Scene;

/// <summary>
/// Something to draw: a mesh (or a slice of a static-batch mesh) with its materials, placed in the world.
/// Coordinates are Unity's (left-handed, Y up); the game converts them.
/// </summary>
public sealed record RenderItem(
    string Path,
    Matrix4x4 World,
    AssetRef Mesh,
    int FirstSubMesh,
    int SubMeshCount,
    IReadOnlyList<AssetRef?> Materials,
    bool StaticBatched,
    bool CastShadows,
    uint Layer);

/// <summary>A solid collider placed in the world. Mesh is set for mesh colliders.</summary>
public sealed record ColliderItem(string Path, Matrix4x4 World, ColliderData Collider, AssetRef? Mesh, uint Layer);

public sealed record ZoneStats(int Nodes, int SkippedInactive, int Renderers, int SkippedLowerLods, int Colliders, int Triggers);

/// <summary>
/// The game state that decides which optional objects show: per-type progress counters (ranch
/// upgrades and so on, as stored in a save's player progress), the hour of the day, and gadget mode.
/// <see cref="Hidden"/> lists scene game objects (path ids in the scene file) that the game has
/// replaced or removed, such as the plot standing on a land plot site when a save built another.
/// With <see cref="RunningClock"/> set, objects tied to the time of day are kept whatever the hour and
/// listed in <see cref="ZoneExtract.Timed"/>, for a world clock to switch them as it runs.
/// </summary>
public sealed record WorldState(IReadOnlyDictionary<int, int> Progress, float Hour, bool GadgetMode = false,
    IReadOnlySet<long>? Hidden = null, bool RunningClock = false)
{
    /// <summary>A new game at midday: no progress yet.</summary>
    public static WorldState NewGame { get; } = new(new Dictionary<int, int>(), 12f);

    public int ProgressOf(int type) => Progress.TryGetValue(type, out var v) ? v : 0;
}

/// <summary>
/// One area's renderers, solid colliders, cave volumes and lamps. <see cref="Timed"/> holds what belongs
/// to objects tied to the time of day, kept out of the other lists (only with <see cref="WorldState.RunningClock"/>).
/// <see cref="Cells"/>, <see cref="Teleports"/> and <see cref="KillVolumes"/> are described in docs/behavior/zones.md.
/// </summary>
public sealed record ZoneExtract(string Name, IReadOnlyList<RenderItem> Renderers, IReadOnlyList<ColliderItem> Colliders, ZoneStats Stats,
    IReadOnlyList<CaveVolume> Caves, IReadOnlyList<LightItem> Lights, IReadOnlyList<TimedGroup>? Timed = null,
    IReadOnlyList<CellArea>? CellAreas = null, IReadOnlyList<TeleportPoint>? TeleportPoints = null, IReadOnlyList<TriggerVolume>? Kills = null)
{
    public IReadOnlyList<TimedGroup> TimedGroups => Timed ?? [];
    public IReadOnlyList<CellArea> Cells => CellAreas ?? [];
    public IReadOnlyList<TeleportPoint> Teleports => TeleportPoints ?? [];
    public IReadOnlyList<TriggerVolume> KillVolumes => Kills ?? [];
}

/// <summary>
/// A world cell: its region's box in world space (a <c>Region</c> script's <c>bounds</c>) and the ambience
/// zone its <c>CellDirector</c> names (AmbianceDirector.Zone: 0 DEFAULT, 1 QUARRY, 2 MOSS, 3 DESERT, 4 RUINS,
/// 5 WILDS, ...). The player is in every cell whose box holds its position (Unity's Bounds.Contains, edges included).
/// </summary>
public sealed record CellArea(string Path, int AmbianceZone, Vector3 Center, Vector3 Extent)
{
    public bool Contains(Vector3 point)
    {
        var d = Vector3.Abs(point - Center);
        return d.X <= Extent.X && d.Y <= Extent.Y && d.Z <= Extent.Z;
    }
}

/// <summary>
/// A place the game teleports the player to: a <c>TeleportDestination</c> (the player lands on the object's
/// position and, with <see cref="Reorient"/>, takes its rotation) or a <c>DebugTeleportDestination</c>, the
/// developers' named start point for a zone (<see cref="IsDebugStart"/>).
/// </summary>
public sealed record TeleportPoint(string Name, string Path, Matrix4x4 World, bool Reorient, bool IsDebugStart);

/// <summary>A trigger volume, such as a <c>KillOnTrigger</c> under a sea, in the same terms as <see cref="CaveVolume"/>.</summary>
public sealed record TriggerVolume(string Path, Matrix4x4 World, ColliderShape Shape, Vector3 LocalMin, Vector3 LocalMax, float Radius)
{
    public bool Contains(Vector3 point) =>
        new CaveVolume(Path, World, Shape, LocalMin, LocalMax, Radius, 0, false, []).Contains(point);
}

/// <summary>
/// A point or spot light placed in the world (Unity light types: 0 spot, 2 point). Lights listed by a
/// cave trigger have <see cref="CaveController"/> set: they are off outside that cave and fade up
/// inside it (docs/behavior/day-and-night.md).
/// </summary>
public sealed record LightItem(string Path, Matrix4x4 World, int Type, Vector4 Color, float Intensity, float Range, float SpotAngle,
    bool Enabled, long? CaveController);

/// <summary>
/// A trigger volume that switches the lighting to a cave zone's ambience while the player is inside
/// (see docs/behavior/day-and-night.md). <see cref="LocalMin"/> and <see cref="LocalMax"/> bound the
/// volume in the trigger's own space; <see cref="World"/> places it.
/// </summary>
public sealed record CaveVolume(string Path, Matrix4x4 World, ColliderShape Shape, Vector3 LocalMin, Vector3 LocalMax, float Radius,
    int Zone, bool AffectsLighting, IReadOnlyList<long> LightControllers)
{
    public bool Contains(Vector3 point)
    {
        if (!Matrix4x4.Invert(World, out var toLocal))
            return false;
        var p = Vector3.Transform(point, toLocal);
        if (Shape == ColliderShape.Sphere)
            return Vector3.Distance(p, (LocalMin + LocalMax) / 2) <= Radius;
        return p.X >= LocalMin.X && p.Y >= LocalMin.Y && p.Z >= LocalMin.Z
               && p.X <= LocalMax.X && p.Y <= LocalMax.Y && p.Z <= LocalMax.Z;
    }
}

/// <summary>
/// Walks one root object of a scene (for example "zoneRANCH" in the world scene) and lists what it
/// draws and what it collides with. Inactive objects, lower levels of detail and objects that the game's
/// scripts switch off for the given <see cref="WorldState"/> (see docs/behavior/world-visibility.md)
/// are left out.
/// </summary>
public static class ZoneExtractor
{
    public static IReadOnlyDictionary<string, AssetRef> RootObjects(AssetSet assets, SerializedFile scene)
    {
        var roots = new Dictionary<string, AssetRef>();
        foreach (var info in scene.Objects.Where(o => o.ClassId == UnityClassId.Transform))
        {
            var asset = new AssetRef(scene, info);
            var t = assets.Read(asset, TransformData.Read);
            if (!t.Father.IsNull)
                continue;
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is not null)
                roots.TryAdd(go.Name, asset);
        }
        return roots;
    }

    public static ZoneExtract Extract(AssetSet assets, SerializedFile scene, string rootName, WorldState? state = null)
    {
        state ??= WorldState.NewGame;
        var roots = RootObjects(assets, scene);
        if (!roots.TryGetValue(rootName, out var root))
            throw new KeyNotFoundException($"No root object named '{rootName}' in {Path.GetFileName(scene.Path)}.");

        var renderers = new List<RenderItem>();
        var colliders = new List<ColliderItem>();
        var caves = new List<CaveVolume>();
        var lights = new List<LightItem>();
        var cells = new List<CellArea>();
        var teleports = new List<TeleportPoint>();
        var killVolumes = new List<TriggerVolume>();
        var lowerLods = new HashSet<long>();
        var hiddenObjects = new HashSet<long>(state.Hidden ?? new HashSet<long>());
        // Objects tied to the time of day, with their windows, by game object id (only with a running clock).
        var windows = new Dictionary<long, List<TimeWindow>>();
        var timed = new Dictionary<IReadOnlyList<TimeWindow>, TimedGroup>(ReferenceEqualityComparer.Instance);
        int nodes = 0, inactive = 0, triggers = 0;

        // First pass: renderers that only show at lower levels of detail, and objects hidden at runtime.
        void CollectLods(AssetRef transformRef)
        {
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive)
                return;
            foreach (var c in go.Components)
            {
                switch (assets.Resolve(scene, c))
                {
                    case { ClassId: UnityClassId.LodGroup } lodRef:
                        var lod = assets.Read(lodRef, LodGroupData.Read);
                        foreach (var level in lod.Lods.Skip(1))
                            foreach (var r in level.Renderers)
                                lowerLods.Add(r.PathId);
                        break;
                    case { ClassId: UnityClassId.MonoBehaviour } script:
                        HideBy(assets, script, t.GameObject, state, hiddenObjects, windows);
                        break;
                }
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    CollectLods(childRef);
        }
        CollectLods(root);

        void Walk(AssetRef transformRef, Matrix4x4 parentWorld, string parentPath, IReadOnlyList<TimeWindow> openDuring)
        {
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null)
                return;
            nodes++;
            if (!go.IsActive || t.GameObject.FileId == 0 && hiddenObjects.Contains(t.GameObject.PathId))
            {
                inactive++;
                return;
            }

            var world = t.LocalMatrix * parentWorld;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;
            if (t.GameObject.FileId == 0 && windows.TryGetValue(t.GameObject.PathId, out var own))
            {
                openDuring = openDuring.Concat(own).ToList();
                timed[openDuring] = new TimedGroup(path, openDuring, [], [], []);
            }
            var group = openDuring.Count == 0 ? null : timed[openDuring];

            MeshFilterData? filter = null;
            CaveSettingsData? cave = null;
            long? caveLightController = null;
            int? ambianceZone = null;
            (Vector3 Center, Vector3 Extent)? regionBounds = null;
            var kills = false;
            foreach (var c in go.Components)
            {
                if (assets.Resolve(scene, c) is { ClassId: UnityClassId.MeshFilter } f)
                    filter = assets.Read(f, MeshFilterData.Read);
                else if (assets.Resolve(scene, c) is { ClassId: UnityClassId.MonoBehaviour } script)
                {
                    switch (ScriptName(assets, script))
                    {
                        case "CaveTrigger":
                            cave ??= CaveSettings(assets, script);
                            break;
                        case "CaveLightController":
                            caveLightController = script.PathId;
                            break;
                        case "CellDirector":
                            ambianceZone = CellAmbianceZone(assets, script);
                            break;
                        case "Region":
                            regionBounds = RegionBounds(assets, script);
                            break;
                        case "KillOnTrigger":
                            kills = true;
                            break;
                        case "TeleportDestination":
                            teleports.Add(TeleportDestination(assets, script, path, world));
                            break;
                        case "DebugTeleportDestination":
                            teleports.Add(new TeleportPoint(DebugTeleportName(assets, script), path, world, true, true));
                            break;
                    }
                }
            }
            // The Region script finds its CellDirector on the same object (static analysis: Region.Awake).
            if (ambianceZone is { } az && regionBounds is { } rb)
                cells.Add(new CellArea(path, az, rb.Center, rb.Extent));

            foreach (var c in go.Components)
            {
                if (assets.Resolve(scene, c) is not { } comp)
                    continue;
                switch (comp.ClassId)
                {
                    case UnityClassId.MeshRenderer when filter is not null:
                    {
                        var r = assets.Read(comp, RendererData.Read);
                        if (!r.Enabled || lowerLods.Contains(comp.PathId))
                            continue;
                        if (assets.Resolve(scene, filter.Mesh) is not { } mesh)
                            continue;
                        var materials = r.Materials.Select(m => assets.Resolve(scene, m)).ToList();
                        (group?.Renderers ?? renderers).Add(r.IsStaticBatched
                            ? new RenderItem(path, Matrix4x4.Identity, mesh, r.StaticBatchFirstSubMesh, r.StaticBatchSubMeshCount,
                                materials, true, r.CastShadows, go.Layer)
                            : new RenderItem(path, world, mesh, 0, -1, materials, false, r.CastShadows, go.Layer));
                        break;
                    }
                    case UnityClassId.Light:
                    {
                        var light = ReadLight(assets, comp);
                        if (light.Type is 0 or 2 && (light.Enabled || caveLightController is not null))
                            (group?.Lights ?? lights).Add(light with { Path = path, World = world, CaveController = caveLightController });
                        break;
                    }
                    case UnityClassId.BoxCollider or UnityClassId.SphereCollider or UnityClassId.CapsuleCollider or UnityClassId.MeshCollider:
                    {
                        var col = assets.Read(comp, x => ColliderData.Read(x, comp.ClassId));
                        if (!col.Enabled)
                            continue;
                        if (col.IsTrigger)
                        {
                            triggers++;
                            if (cave is { } cs && CaveBounds(assets, scene, col) is { } bounds)
                                caves.Add(new CaveVolume(path, world, col.Shape, bounds.Min, bounds.Max, col.Radius, cs.Zone, cs.AffectsLighting,
                                    cs.Lights));
                            if (kills && CaveBounds(assets, scene, col) is { } killBounds)
                                killVolumes.Add(new TriggerVolume(path, world, col.Shape, killBounds.Min, killBounds.Max, col.Radius));
                            continue;
                        }
                        var mesh = col.Shape == ColliderShape.Mesh ? assets.Resolve(scene, col.Mesh) : null;
                        if (col.Shape == ColliderShape.Mesh && mesh is null)
                            continue;
                        (group?.Colliders ?? colliders).Add(new ColliderItem(path, world, col, mesh, go.Layer));
                        break;
                    }
                }
            }

            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    Walk(childRef, world, path, openDuring);
        }
        Walk(root, Matrix4x4.Identity, "", []);

        return new ZoneExtract(rootName, renderers, colliders,
            new ZoneStats(nodes, inactive, renderers.Count, lowerLods.Count, colliders.Count, triggers), caves, lights, timed.Values.ToList(),
            cells, teleports, killVolumes);
    }

    // CellDirector's serialized fields up to its ambience zone (from the game's managed metadata): eight
    // int32 spawn limits, two floats (despawnFactor, avgSpawnTimeGameHours), three bools padded to 4
    // (isRanch, isHomeRanch, isWilds), then ambianceZone (AmbianceDirector.Zone).
    private static int CellAmbianceZone(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        r.Skip(8 * 4 + 2 * 4 + 3 * 4);
        return r.ReadInt32();
    }

    // Region: overrideBounds (bool, padded to 4), then bounds (centre, extent), already in world space:
    // the game registers them as stored (static analysis: Region.OnEnable, RegionRegistry.RegisterRegion).
    private static (Vector3 Center, Vector3 Extent) RegionBounds(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        r.Skip(4);
        return (r.ReadVector3(), r.ReadVector3());
    }

    // TeleportDestination: destLoc and arriveFX references, teleportDestinationName, reorient. The game moves
    // the player to the object's own position and, with reorient set, its rotation (static analysis:
    // TeleportNetwork, TeleportDestination.GetPosition/GetEulerAngles).
    private static TeleportPoint TeleportDestination(AssetSet assets, AssetRef behaviour, string path, Matrix4x4 world)
    {
        var r = assets.Reader(behaviour);
        Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        r.Skip(12 + 12);
        var name = r.ReadAlignedString();
        return new TeleportPoint(name, path, world, r.ReadBool(), false);
    }

    // DebugTeleportDestination: one string, the name the game's debug menu shows (e.g. "Wilds Start").
    private static string DebugTeleportName(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        return r.ReadAlignedString();
    }

    private sealed record CaveSettingsData(int Zone, bool AffectsLighting, IReadOnlyList<long> Lights);

    private static string? ScriptName(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        var (_, enabled, script, _) = Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        return enabled && assets.Resolve(behaviour.File, script) is { } scriptRef
            ? assets.Read(scriptRef, Unity.Managed.MonoBehaviourReader.ReadMonoScript).ClassName
            : null;
    }

    /// <summary>Reads a cave trigger script: its lights, whether it changes the lighting, and its zone.</summary>
    private static CaveSettingsData CaveSettings(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        var count = r.ReadInt32();
        var lights = new List<long>();
        for (var i = 0; i < count; i++)
        {
            var light = PPtr.Read(r);
            if (light.FileId == 0 && !light.IsNull)
                lights.Add(light.PathId);
        }
        var affectsLighting = r.ReadBool();
        r.Align();
        return new CaveSettingsData(r.ReadInt32(), affectsLighting, lights);
    }

    // Light component fields up to the spot angle, in stored order.
    private static LightItem ReadLight(AssetSet assets, AssetRef light)
    {
        var r = assets.Reader(light);
        r.Skip(12); // game object
        var enabled = r.ReadBool();
        r.Align();
        var type = r.ReadInt32();
        r.ReadInt32(); // shape
        var color = r.ReadVector4();
        var intensity = r.ReadSingle();
        var range = r.ReadSingle();
        var spotAngle = r.ReadSingle();
        return new LightItem("", Matrix4x4.Identity, type, color, intensity, range, spotAngle, enabled, null);
    }

    // A trigger collider's extent in its own space: exact for boxes and spheres, the bounding box of
    // the capsule or mesh otherwise.
    private static (Vector3 Min, Vector3 Max)? CaveBounds(AssetSet assets, SerializedFile scene, ColliderData col)
    {
        switch (col.Shape)
        {
            case ColliderShape.Box:
                return (col.Center - col.Size / 2, col.Center + col.Size / 2);
            case ColliderShape.Sphere:
                return (col.Center - new Vector3(col.Radius), col.Center + new Vector3(col.Radius));
            case ColliderShape.Capsule:
            {
                var half = new Vector3(col.Radius);
                var axis = col.Direction switch { 0 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => Vector3.UnitY };
                half += axis * MathF.Max(0, col.Height / 2 - col.Radius);
                return (col.Center - half, col.Center + half);
            }
            case ColliderShape.Mesh when assets.Resolve(scene, col.Mesh) is { } meshRef:
            {
                var mesh = assets.Read(meshRef, MeshData.Read);
                return (mesh.BoundsCenter - mesh.BoundsExtent, mesh.BoundsCenter + mesh.BoundsExtent);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Applies the scripts that switch world objects on and off, as they would be when the game starts
    /// in <paramref name="state"/>. Each hidden object's id is added to <paramref name="hidden"/>.
    /// </summary>
    private static void HideBy(AssetSet assets, AssetRef behaviour, PPtr owner, WorldState state, HashSet<long> hidden,
        Dictionary<long, List<TimeWindow>> windows)
    {
        var r = assets.Reader(behaviour);
        var (_, enabled, script, _) = Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        if (!enabled || assets.Resolve(behaviour.File, script) is not { } scriptRef)
            return;
        void Hide(PPtr target)
        {
            if (target.FileId == 0 && !target.IsNull)
                hidden.Add(target.PathId);
        }
        switch (assets.Read(scriptRef, Unity.Managed.MonoBehaviourReader.ReadMonoScript).ClassName)
        {
            // Build-site markers: the target shows only in gadget mode, or only outside it with the flag set.
            case "DeactivateBasedOnGadgetMode":
            {
                var target = PPtr.Read(r);
                var activeOutsideGadgetMode = r.ReadBool();
                if (state.GadgetMode == activeOutsideGadgetMode)
                    Hide(target);
                break;
            }
            // Ranch upgrades and similar: the object itself shows only while a progress counter is in range.
            case "ActivateOnProgressRange":
            {
                var type = r.ReadInt32();
                var min = r.ReadInt32();
                var max = r.ReadInt32();
                var progress = state.ProgressOf(type);
                if (progress < min || progress > max)
                    Hide(owner);
                break;
            }
            // Lamps and night decorations: the listed objects show only between two hours, wrapping past midnight.
            // With a running clock they are kept and listed with their window instead.
            case "EnableOnlyDuringTimeWindow":
            {
                var window = new TimeWindow(r.ReadSingle(), r.ReadSingle());
                var count = r.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    var target = PPtr.Read(r);
                    if (state.RunningClock)
                    {
                        if (target.FileId == 0 && !target.IsNull)
                            (windows.TryGetValue(target.PathId, out var list) ? list : windows[target.PathId] = []).Add(window);
                    }
                    else if (!window.IsOpen(state.Hour))
                        Hide(target);
                }
                break;
            }
        }
    }
}
