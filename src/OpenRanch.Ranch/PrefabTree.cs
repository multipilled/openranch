using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Ranch;

/// <summary>A game object of a prefab: its path below the root, whether it is switched on in the prefab itself, and where it sits relative to the root.</summary>
public sealed record PrefabObject(long PathId, string Path, bool ActiveSelf, Matrix4x4 ToRoot);

/// <summary>A renderer or collider of a prefab and the game objects from the root down to its own (path ids), which all have to be on for it to show.</summary>
public sealed record PrefabPart<T>(T Item, IReadOnlyList<long> Chain);

/// <summary>A trigger box of a prefab (a corral's inside), placed relative to the prefab's root, and the objects it hangs under.</summary>
public sealed record PrefabRegion(PlotRegion Region, IReadOnlyList<long> Chain);

/// <summary>
/// Everything a prefab draws and collides with, the objects that are switched off included, so that
/// switching objects on or off later (as plot upgrades do) changes what is built. Walked the way the
/// zone extractor walks the scene: the most detailed level of each LODGroup, enabled renderers and
/// colliders; trigger boxes of CorralRegion scripts are kept as regions. The root's own transform is
/// left out: whatever places the prefab places its root.
/// </summary>
public sealed class PrefabTree
{
    private readonly Dictionary<long, IReadOnlyList<long>> _chains;

    private PrefabTree(string name, AssetRef root, Matrix4x4 rootLocal, Dictionary<long, PrefabObject> objects,
        Dictionary<long, IReadOnlyList<long>> chains, List<PrefabPart<RenderItem>> renderers, List<PrefabPart<ColliderItem>> colliders,
        List<PrefabRegion> regions)
    {
        _chains = chains;
        Name = name;
        Root = root;
        RootLocal = rootLocal;
        Objects = objects;
        Renderers = renderers;
        Colliders = colliders;
        Regions = regions;
    }

    public string Name { get; }
    /// <summary>The root game object.</summary>
    public AssetRef Root { get; }
    /// <summary>The root's own local transform in the prefab (its scale survives instantiation).</summary>
    public Matrix4x4 RootLocal { get; }
    /// <summary>Every game object of the prefab by path id, switched on or not.</summary>
    public IReadOnlyDictionary<long, PrefabObject> Objects { get; }
    public IReadOnlyList<PrefabPart<RenderItem>> Renderers { get; }
    public IReadOnlyList<PrefabPart<ColliderItem>> Colliders { get; }
    public IReadOnlyList<PrefabRegion> Regions { get; }

    /// <summary>
    /// Whether every object of <paramref name="chain"/> is on, with the objects in
    /// <paramref name="switched"/> set to the given state instead of the prefab's own.
    /// </summary>
    public bool IsOn(IReadOnlyList<long> chain, IReadOnlyDictionary<long, bool>? switched = null)
    {
        foreach (var id in chain)
        {
            var on = switched is not null && switched.TryGetValue(id, out var s) ? s : Objects[id].ActiveSelf;
            if (!on)
                return false;
        }
        return true;
    }

    /// <summary>Whether the object <paramref name="pathId"/> and everything above it are on.</summary>
    public bool IsOn(long pathId, IReadOnlyDictionary<long, bool>? switched = null) => IsOn(ChainOf(pathId), switched);

    /// <summary>The path ids from the root down to <paramref name="pathId"/>.</summary>
    public IReadOnlyList<long> ChainOf(long pathId) => _chains[pathId];

    public IEnumerable<RenderItem> RenderersOn(IReadOnlyDictionary<long, bool>? switched = null) =>
        Renderers.Where(p => IsOn(p.Chain, switched)).Select(p => p.Item);

    public IEnumerable<ColliderItem> CollidersOn(IReadOnlyDictionary<long, bool>? switched = null) =>
        Colliders.Where(p => IsOn(p.Chain, switched)).Select(p => p.Item);

    public IEnumerable<PlotRegion> RegionsOn(IReadOnlyDictionary<long, bool>? switched = null) =>
        Regions.Where(p => IsOn(p.Chain, switched)).Select(p => p.Region);

    public static PrefabTree Read(GameScripts scripts, AssetRef root)
    {
        var assets = scripts.Assets;
        var objects = new Dictionary<long, PrefabObject>();
        var chains = new Dictionary<long, IReadOnlyList<long>>();
        var renderers = new List<PrefabPart<RenderItem>>();
        var colliders = new List<PrefabPart<ColliderItem>>();
        var regions = new List<PrefabRegion>();
        var lowerLods = new HashSet<long>();
        var walked = new List<(AssetRef Go, GameObjectData Data, Matrix4x4 ToRoot, string Path, IReadOnlyList<long> Chain)>();
        var rootLocal = Matrix4x4.Identity;

        var rootGo = assets.Read(root, GameObjectData.Read);
        var stack = new Stack<(AssetRef, Matrix4x4, string, IReadOnlyList<long>, bool)>();
        stack.Push((root, Matrix4x4.Identity, rootGo.Name, [], true));
        while (stack.Count > 0)
        {
            var (goRef, parent, path, parentChain, isRoot) = stack.Pop();
            var go = assets.Read(goRef, GameObjectData.Read);
            var transformRef = go.Components.Select(c => assets.Resolve(goRef.File, c)).FirstOrDefault(c => c?.ClassId == UnityClassId.Transform);
            if (transformRef is null)
                continue;
            var t = assets.Read(transformRef.Value, TransformData.Read);
            if (isRoot)
                rootLocal = t.LocalMatrix;
            var toRoot = isRoot ? Matrix4x4.Identity : t.LocalMatrix * parent;
            var chain = parentChain.Append(goRef.PathId).ToList();
            objects[goRef.PathId] = new PrefabObject(goRef.PathId, path, go.IsActive, toRoot);
            chains[goRef.PathId] = chain;
            walked.Add((goRef, go, toRoot, path, chain));
            foreach (var childPtr in t.Children)
            {
                if (assets.Resolve(goRef.File, childPtr) is not { } childT
                    || assets.Resolve(childT.File, assets.Read(childT, TransformData.Read).GameObject) is not { } childGo)
                    continue;
                stack.Push((childGo, toRoot, path + "/" + assets.Read(childGo, GameObjectData.Read).Name, chain, false));
            }
            foreach (var c in go.Components)
                if (assets.Resolve(goRef.File, c) is { ClassId: UnityClassId.LodGroup } lod)
                    foreach (var level in assets.Read(lod, LodGroupData.Read).Lods.Skip(1))
                        foreach (var r in level.Renderers)
                            lowerLods.Add(r.PathId);
        }

        foreach (var (goRef, go, toRoot, path, chain) in walked)
        {
            var file = goRef.File;
            var components = go.Components.Select(c => assets.Resolve(file, c)).OfType<AssetRef>().ToList();
            var filter = components.Where(c => c.ClassId == UnityClassId.MeshFilter).Select(c => assets.Read(c, MeshFilterData.Read)).FirstOrDefault();
            var isCorralRegion = components.Any(c => c.ClassId == UnityClassId.MonoBehaviour && scripts.Reader.Read(c) is { Enabled: true, ScriptClass: "CorralRegion" });
            foreach (var comp in components)
            {
                switch (comp.ClassId)
                {
                    case UnityClassId.MeshRenderer when filter is not null:
                    {
                        var r = assets.Read(comp, RendererData.Read);
                        if (!r.Enabled || lowerLods.Contains(comp.PathId) || assets.Resolve(file, filter.Mesh) is not { } mesh)
                            continue;
                        var materials = r.Materials.Select(m => assets.Resolve(file, m)).ToList();
                        renderers.Add(new(new RenderItem(path, toRoot, mesh, 0, -1, materials, false, r.CastShadows, go.Layer), chain));
                        break;
                    }
                    case UnityClassId.BoxCollider or UnityClassId.SphereCollider or UnityClassId.CapsuleCollider or UnityClassId.MeshCollider:
                    {
                        var col = assets.Read(comp, x => ColliderData.Read(x, comp.ClassId));
                        if (!col.Enabled)
                            continue;
                        if (col.IsTrigger)
                        {
                            if (isCorralRegion && col.Shape == ColliderShape.Box)
                                regions.Add(new(new PlotRegion("CorralRegion", toRoot, col.Center, col.Size), chain));
                            continue;
                        }
                        var mesh = col.Shape == ColliderShape.Mesh ? assets.Resolve(file, col.Mesh) : null;
                        if (col.Shape == ColliderShape.Mesh && mesh is null)
                            continue;
                        colliders.Add(new(new ColliderItem(path, toRoot, col, mesh, go.Layer), chain));
                        break;
                    }
                }
            }
        }
        return new PrefabTree(rootGo.Name, root, rootLocal, objects, chains, renderers, colliders, regions);
    }

    /// <summary>The game object a component (a joint, a script) sits on: every component's data starts with a reference to it.</summary>
    public static long OwnerOf(AssetSet assets, AssetRef component) => PPtr.Read(assets.Reader(component)).PathId;
}
