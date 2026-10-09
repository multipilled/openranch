using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// A crop prefab (a veggie patch or a fruit tree) from the LookupDirector's resource spawner list:
/// its <c>SpawnResource</c> id, what it draws, and its spawn joints (where produce hangs while it
/// grows), relative to its root.
/// </summary>
public sealed record CropPrefab(int Id, string Name, PrefabTree Tree, IReadOnlyList<Matrix4x4> JointsToRoot)
{
    /// <summary>Its <c>SpawnResource</c> script (what grows and how often) and the file it is in.</summary>
    public CropScript? Script { get; init; }
}

/// <summary>
/// A crop's <c>SpawnResource</c> script and the file it is in (for <see cref="ProduceData.SpawnRules"/>), and
/// whether the same object carries a <c>SpawnResourceForceFirstRipeness</c> (its first batch grows ripe).
/// </summary>
public sealed record CropScript(SerializedFile File, SerializedObject Data, bool ForceFirstRipeness);

/// <summary>
/// A crop standing in the world, planted on a plot or placed in the scene: where it stands (Unity
/// world matrix), its spawn joints' world positions, and the prefab it was made from when it is a
/// plot's crop.
/// </summary>
public sealed record CropSpawner(string Path, Matrix4x4 World, IReadOnlyList<Vector3> Joints, CropPrefab? Prefab = null)
{
    /// <summary>Its <c>SpawnResource</c> script: the prefab's for a plot's crop, the scene's for the scene's own.</summary>
    public CropScript? Script { get; init; }

    /// <summary>The position the game registers the spawner at (its transform's position).</summary>
    public Vector3 Position => World.Translation;
}

/// <summary>
/// Crops for a loaded ranch. A plot whose save names a crop (<see cref="Plot.AttachedResource"/>)
/// gets that crop's prefab, looked up by id in the LookupDirector's <c>resourceSpawnerPrefabs</c>,
/// at the plot root's position and rotation with the prefab root's own scale: static analysis of
/// <c>LandPlot.SetModel</c>, which does this for any plot type whose saved crop isn't "none". The
/// scene's own crops (the Overgrowth's patches and trees) are found by their <c>SpawnResource</c>
/// scripts. See docs/behavior/plots.md, "Crops".
/// </summary>
public sealed class PlotCrops
{
    private PlotCrops(IReadOnlyDictionary<int, CropPrefab> prefabs) => Prefabs = prefabs;

    /// <summary>Crop prefabs by <see cref="GameEnum.SpawnResource"/> id.</summary>
    public IReadOnlyDictionary<int, CropPrefab> Prefabs { get; }

    public static PlotCrops Read(GameScripts scripts, SerializedFile scene)
    {
        var assets = scripts.Assets;
        var prefabs = new Dictionary<int, CropPrefab>();
        var lookup = scripts.OfClass("LookupDirector").FirstOrDefault(l => l.Ref.File == scene);
        if (lookup.Data?.Data?["resourceSpawnerPrefabs"] is not PPtr listPtr || scripts.Follow(lookup.Ref.File, listPtr) is not { } list)
            throw new InvalidDataException("The LookupDirector's resource spawner list wasn't found.");
        foreach (var item in list.Data.Data!.List("items").OfType<PPtr>())
        {
            if (assets.Resolve(list.Ref.File, item) is not { ClassId: UnityClassId.GameObject } root
                || SpawnResourceOf(scripts, root) is not { } spawner)
                continue;
            var id = Convert.ToInt32(spawner.Data["id"]);
            // The director keeps the last prefab listed for an id.
            var tree = PrefabTree.Read(scripts, root);
            var joints = JointObjects(scripts, root.File, spawner.Data).Where(tree.Objects.ContainsKey).Select(j => tree.Objects[j].ToRoot).ToList();
            prefabs[id] = new CropPrefab(id, tree.Name, tree, joints) { Script = new CropScript(spawner.Ref.File, spawner.Data, ForcesFirstRipeness(scripts, root)) };
        }
        return new PlotCrops(prefabs);
    }

    /// <summary>The crop planted on <paramref name="plot"/>, or null when it has none (or none the install knows).</summary>
    public CropSpawner? On(PlacedPlot plot)
    {
        if (!Prefabs.TryGetValue(plot.Plot.AttachedResource, out var prefab))
            return null;
        Matrix4x4.Decompose(plot.Site.PlotWorld, out _, out var rotation, out var position);
        Matrix4x4.Decompose(prefab.Tree.RootLocal, out var scale, out _, out _);
        var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        return new CropSpawner($"{plot.Site.Id}/{prefab.Name}", world, prefab.JointsToRoot.Select(j => (j * world).Translation).ToList(), prefab) { Script = prefab.Script };
    }

    /// <summary>What the plots' crops draw and collide with, in world coordinates, under "site/crop prefab/...".</summary>
    public static (List<RenderItem> Renderers, List<ColliderItem> Colliders) Build(IEnumerable<CropSpawner> crops)
    {
        var renderers = new List<RenderItem>();
        var colliders = new List<ColliderItem>();
        foreach (var crop in crops.Where(c => c.Prefab is not null))
        {
            renderers.AddRange(crop.Prefab!.Tree.RenderersOn().Select(r => r with { Path = $"{crop.Path}/{r.Path}", World = r.World * crop.World }));
            colliders.AddRange(crop.Prefab.Tree.CollidersOn().Select(c => c with { Path = $"{crop.Path}/{c.Path}", World = c.World * crop.World }));
        }
        return (renderers, colliders);
    }

    /// <summary>
    /// The crops the scene itself places under <paramref name="rootName"/> (active objects, not under
    /// one of the <paramref name="hidden"/> objects).
    /// </summary>
    public static List<CropSpawner> InScene(GameScripts scripts, SerializedFile scene, string rootName, IReadOnlySet<long> hidden)
    {
        var assets = scripts.Assets;
        var found = new List<CropSpawner>();
        if (!ZoneExtractor.RootObjects(assets, scene).TryGetValue(rootName, out var root))
            return found;
        var worlds = new Dictionary<long, Matrix4x4>();
        var spawners = new List<(string Path, long Go, SerializedObject Data, bool ForceFirst)>();
        var stack = new Stack<(AssetRef Transform, Matrix4x4 Parent, string Path)>();
        stack.Push((root, Matrix4x4.Identity, ""));
        while (stack.Count > 0)
        {
            var (transformRef, parent, parentPath) = stack.Pop();
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive || hidden.Contains(t.GameObject.PathId))
                continue;
            var world = t.LocalMatrix * parent;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;
            worlds[t.GameObject.PathId] = world;
            foreach (var c in go.Components)
                if (scripts.Follow(scene, c) is { Data: { ScriptClass: "SpawnResource", Data: { } data } })
                    spawners.Add((path, t.GameObject.PathId, data, go.Components.Any(o => scripts.Follow(scene, o) is { Data.ScriptClass: "SpawnResourceForceFirstRipeness" })));
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    stack.Push((childRef, world, path));
        }
        foreach (var (path, go, data, forceFirst) in spawners)
        {
            var joints = JointObjects(scripts, scene, data).Where(worlds.ContainsKey).Select(j => worlds[j].Translation).ToList();
            found.Add(new CropSpawner(path, worlds[go], joints) { Script = new CropScript(scene, data, forceFirst) });
        }
        return found;
    }

    private static bool ForcesFirstRipeness(GameScripts scripts, AssetRef gameObject) =>
        scripts.Assets.Read(gameObject, GameObjectData.Read).Components.Any(c => scripts.Follow(gameObject.File, c) is { Data.ScriptClass: "SpawnResourceForceFirstRipeness" });

    private static (AssetRef Ref, SerializedObject Data)? SpawnResourceOf(GameScripts scripts, AssetRef gameObject)
    {
        foreach (var c in scripts.Assets.Read(gameObject, GameObjectData.Read).Components)
            if (scripts.Follow(gameObject.File, c) is { Data: { ScriptClass: "SpawnResource", Data: { } data } } s)
                return (s.Ref, data);
        return null;
    }

    // The game objects of a SpawnResource's spawn joints (its SpawnJoints field lists joint components).
    private static IEnumerable<long> JointObjects(GameScripts scripts, SerializedFile file, SerializedObject spawner) =>
        spawner.List("SpawnJoints").OfType<PPtr>()
            .Select(p => scripts.Assets.Resolve(file, p)).OfType<AssetRef>()
            .Select(j => PrefabTree.OwnerOf(scripts.Assets, j));
}
