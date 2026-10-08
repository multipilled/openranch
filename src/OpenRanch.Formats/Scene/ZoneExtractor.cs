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

public sealed record ZoneExtract(string Name, IReadOnlyList<RenderItem> Renderers, IReadOnlyList<ColliderItem> Colliders, ZoneStats Stats);

/// <summary>
/// Walks one root object of a scene (for example "zoneRANCH" in the world scene) and lists what it
/// draws and what it collides with. Inactive objects, lower levels of detail and objects that the game
/// hides at runtime outside gadget mode (see docs/behavior/world-visibility.md) are left out.
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

    public static ZoneExtract Extract(AssetSet assets, SerializedFile scene, string rootName)
    {
        var roots = RootObjects(assets, scene);
        if (!roots.TryGetValue(rootName, out var root))
            throw new KeyNotFoundException($"No root object named '{rootName}' in {Path.GetFileName(scene.Path)}.");

        var renderers = new List<RenderItem>();
        var colliders = new List<ColliderItem>();
        var lowerLods = new HashSet<long>();
        var hiddenObjects = new HashSet<long>();
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
                        if (HiddenOutsideGadgetMode(assets, script) is { } hidden)
                            hiddenObjects.Add(hidden);
                        break;
                }
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    CollectLods(childRef);
        }
        CollectLods(root);

        void Walk(AssetRef transformRef, Matrix4x4 parentWorld, string parentPath)
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

            MeshFilterData? filter = null;
            foreach (var c in go.Components)
                if (assets.Resolve(scene, c) is { ClassId: UnityClassId.MeshFilter } f)
                    filter = assets.Read(f, MeshFilterData.Read);

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
                        renderers.Add(r.IsStaticBatched
                            ? new RenderItem(path, Matrix4x4.Identity, mesh, r.StaticBatchFirstSubMesh, r.StaticBatchSubMeshCount,
                                materials, true, r.CastShadows, go.Layer)
                            : new RenderItem(path, world, mesh, 0, -1, materials, false, r.CastShadows, go.Layer));
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
                            continue;
                        }
                        var mesh = col.Shape == ColliderShape.Mesh ? assets.Resolve(scene, col.Mesh) : null;
                        if (col.Shape == ColliderShape.Mesh && mesh is null)
                            continue;
                        colliders.Add(new ColliderItem(path, world, col, mesh, go.Layer));
                        break;
                    }
                }
            }

            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    Walk(childRef, world, path);
        }
        Walk(root, Matrix4x4.Identity, "");

        return new ZoneExtract(rootName, renderers, colliders,
            new ZoneStats(nodes, inactive, renderers.Count, lowerLods.Count, colliders.Count, triggers));
    }

    /// <summary>
    /// Gadget build sites carry a script that keeps their markers switched off unless the player is in
    /// gadget mode. Returns the object it hides when not in gadget mode, if this component is one.
    /// </summary>
    private static long? HiddenOutsideGadgetMode(AssetSet assets, AssetRef behaviour)
    {
        var r = assets.Reader(behaviour);
        var (_, enabled, script, _) = Unity.Managed.MonoBehaviourReader.ReadHeader(r);
        if (!enabled || assets.Resolve(behaviour.File, script) is not { } scriptRef)
            return null;
        if (assets.Read(scriptRef, Unity.Managed.MonoBehaviourReader.ReadMonoScript).ClassName != "DeactivateBasedOnGadgetMode")
            return null;
        var target = PPtr.Read(r);
        var showOnlyOutsideGadgetMode = r.ReadBool();
        return !showOnlyOutsideGadgetMode && target.FileId == 0 && !target.IsNull ? target.PathId : null;
    }
}
