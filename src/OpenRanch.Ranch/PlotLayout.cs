using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// A land plot site in the world scene: its id (shared with saves), where a plot stands on it (the
/// world matrix of the plot object under the site, in Unity coordinates), and the plot the scene
/// itself places there (its type and its game object's path id in the scene file).
/// </summary>
public sealed record PlotSite(string Id, Matrix4x4 PlotWorld, int SceneType, long ScenePlotObject);

/// <summary>A trigger box of a plot (a corral's inside), placed relative to the plot's root.</summary>
public sealed record PlotRegion(string Class, Matrix4x4 ToRoot, Vector3 Center, Vector3 Size)
{
    /// <summary>Whether a point (Unity world coordinates) is inside the box when the plot stands at <paramref name="plotWorld"/>.</summary>
    public bool Contains(Matrix4x4 plotWorld, Vector3 point)
    {
        if (!Matrix4x4.Invert(ToRoot * plotWorld, out var toLocal))
            return false;
        var p = Vector3.Transform(point, toLocal) - Center;
        return Math.Abs(p.X) <= Size.X / 2 && Math.Abs(p.Y) <= Size.Y / 2 && Math.Abs(p.Z) <= Size.Z / 2;
    }
}

/// <summary>
/// One plot type's prefab, as the zone builder draws it: what it renders and collides with, relative
/// to the plot's root, plus its corral regions.
/// </summary>
public sealed record PlotPrefab(int Type, string Name, IReadOnlyList<RenderItem> Renderers, IReadOnlyList<ColliderItem> Colliders,
    IReadOnlyList<PlotRegion> Regions);

/// <summary>A plot as loaded from a save, standing on its site.</summary>
public sealed record PlacedPlot(PlotSite Site, Plot Plot, PlotPrefab Prefab)
{
    public bool Contains(Vector3 unityPoint) => Prefab.Regions.Any(r => r.Contains(Site.PlotWorld, unityPoint));
}

/// <summary>
/// The land plot sites of one area of the world scene and the game's plot prefabs, read from the
/// player's install, and how a saved ranch's plots stand on them. Sites are the scene's
/// <c>LandPlotLocation</c> objects; their ids come from the <c>IdDirector</c> above them, which
/// pairs each id holder with its saved id. The prefabs are the <c>LookupDirector</c>'s plot prefab
/// list, each named by the <c>LandPlot</c> script on its root. See docs/behavior/plots.md.
/// </summary>
public sealed class PlotLayout
{
    private PlotLayout(IReadOnlyList<PlotSite> sites, IReadOnlyDictionary<int, PlotPrefab> prefabs)
    {
        Sites = sites;
        Prefabs = prefabs;
    }

    /// <summary>The sites in scene order.</summary>
    public IReadOnlyList<PlotSite> Sites { get; }

    /// <summary>Plot prefabs by plot type (<see cref="GameEnum.PlotType"/>).</summary>
    public IReadOnlyDictionary<int, PlotPrefab> Prefabs { get; }

    public static PlotLayout Read(GameScripts scripts, SerializedFile scene, string rootName)
    {
        var assets = scripts.Assets;
        var ids = new Dictionary<long, string>();
        foreach (var (director, data) in scripts.OfClass("IdDirector"))
        {
            if (director.File != scene)
                continue;
            var keys = data.Data!.List("persistenceKeys");
            var values = data.Data!.List("persistenceValues");
            for (var i = 0; i < Math.Min(keys.Count, values.Count); i++)
                if (keys[i] is PPtr key && values[i] is string id && assets.Resolve(scene, key) is { } holder)
                    ids[holder.PathId] = id;
        }

        var sites = new List<PlotSite>();
        if (ZoneExtractor.RootObjects(assets, scene).TryGetValue(rootName, out var root))
            FindSites(scripts, scene, root, Matrix4x4.Identity, ids, sites);
        return new PlotLayout(sites, ReadPrefabs(scripts, scene));
    }

    /// <summary>
    /// The plots of <paramref name="ranch"/> on this area's sites, in site order. A site the save
    /// doesn't list keeps the scene's own plot; a plot type with no prefab (the enum's "none") stands
    /// for nothing and is left out.
    /// </summary>
    public IReadOnlyList<PlacedPlot> Place(RanchState ranch)
    {
        var placed = new List<PlacedPlot>();
        foreach (var site in Sites)
        {
            var plot = ranch.FindPlot(site.Id) ?? new Plot { Id = site.Id, Type = site.SceneType };
            if (Prefabs.TryGetValue(plot.Type, out var prefab))
                placed.Add(new PlacedPlot(site, plot, prefab));
        }
        return placed;
    }

    /// <summary>
    /// The scene's plot objects to hide (for <see cref="WorldState.Hidden"/>) and what the saved plots
    /// draw and collide with in their place, in world coordinates. Every site's scene plot is hidden:
    /// the plot on it is always built from its type's prefab, as the game does when it loads a save.
    /// </summary>
    public (HashSet<long> Hidden, List<RenderItem> Renderers, List<ColliderItem> Colliders) Build(IReadOnlyList<PlacedPlot> plots)
    {
        var hidden = Sites.Select(s => s.ScenePlotObject).ToHashSet();
        var renderers = new List<RenderItem>();
        var colliders = new List<ColliderItem>();
        foreach (var p in plots)
        {
            var path = $"{p.Site.Id}/{p.Prefab.Name}";
            renderers.AddRange(p.Prefab.Renderers.Select(r => r with { Path = $"{path}/{r.Path}", World = r.World * p.Site.PlotWorld }));
            colliders.AddRange(p.Prefab.Colliders.Select(c => c with { Path = $"{path}/{c.Path}", World = c.World * p.Site.PlotWorld }));
        }
        return (hidden, renderers, colliders);
    }

    private static void FindSites(GameScripts scripts, SerializedFile scene, AssetRef transformRef, Matrix4x4 parentWorld,
        Dictionary<long, string> ids, List<PlotSite> sites)
    {
        var assets = scripts.Assets;
        var t = assets.Read(transformRef, TransformData.Read);
        var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
        if (go is null || !go.IsActive)
            return;
        var world = t.LocalMatrix * parentWorld;
        foreach (var c in go.Components)
        {
            if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.MonoBehaviour } behaviour
                || scripts.Reader.Read(behaviour).ScriptClass != "LandPlotLocation" || !ids.TryGetValue(behaviour.PathId, out var id))
                continue;
            // The plot standing on the site is the child object carrying a LandPlot script.
            foreach (var childPtr in t.Children)
            {
                if (assets.Resolve(scene, childPtr) is not { } childRef)
                    continue;
                var child = assets.Read(childRef, TransformData.Read);
                if (assets.Resolve(scene, child.GameObject) is { } childGo && LandPlotType(scripts, childGo) is { } type)
                {
                    sites.Add(new PlotSite(id, child.LocalMatrix * world, type, childGo.PathId));
                    break;
                }
            }
            return;
        }
        foreach (var child in t.Children)
            if (assets.Resolve(scene, child) is { } childRef)
                FindSites(scripts, scene, childRef, world, ids, sites);
    }

    private static int? LandPlotType(GameScripts scripts, AssetRef gameObject)
    {
        foreach (var c in scripts.Assets.Read(gameObject, GameObjectData.Read).Components)
            if (scripts.Follow(gameObject.File, c) is { Data.ScriptClass: "LandPlot" } plot)
                return Convert.ToInt32(plot.Data.Data!["typeId"]);
        return null;
    }

    private static Dictionary<int, PlotPrefab> ReadPrefabs(GameScripts scripts, SerializedFile scene)
    {
        var assets = scripts.Assets;
        var prefabs = new Dictionary<int, PlotPrefab>();
        var lookup = scripts.OfClass("LookupDirector").FirstOrDefault(l => l.Ref.File == scene);
        if (lookup.Data?.Data?["plotPrefabs"] is not PPtr listPtr || scripts.Follow(lookup.Ref.File, listPtr) is not { } list)
            throw new InvalidDataException("The LookupDirector's plot prefab list wasn't found.");
        foreach (var item in list.Data.Data!.List("items").OfType<PPtr>())
        {
            if (assets.Resolve(list.Ref.File, item) is not { ClassId: UnityClassId.GameObject } root
                || LandPlotType(scripts, root) is not { } type || prefabs.ContainsKey(type))
                continue;
            prefabs[type] = ReadPrefab(scripts, type, root);
        }
        return prefabs;
    }

    // Walks a plot prefab the way the zone extractor walks the scene: active objects only, the most
    // detailed level of each LODGroup, solid colliders; trigger boxes of CorralRegion scripts are kept
    // as regions. The root's own transform is left out: the site places the plot.
    private static PlotPrefab ReadPrefab(GameScripts scripts, int type, AssetRef root)
    {
        var assets = scripts.Assets;
        var renderers = new List<RenderItem>();
        var colliders = new List<ColliderItem>();
        var regions = new List<PlotRegion>();
        var lowerLods = new HashSet<long>();
        var objects = new List<(AssetRef Go, GameObjectData Data, Matrix4x4 ToRoot, string Path)>();

        var rootGo = assets.Read(root, GameObjectData.Read);
        var stack = new Stack<(AssetRef, Matrix4x4, string, bool)>();
        stack.Push((root, Matrix4x4.Identity, rootGo.Name, true));
        while (stack.Count > 0)
        {
            var (goRef, parent, path, isRoot) = stack.Pop();
            var go = assets.Read(goRef, GameObjectData.Read);
            if (!go.IsActive)
                continue;
            var transformRef = go.Components.Select(c => assets.Resolve(goRef.File, c)).FirstOrDefault(c => c?.ClassId == UnityClassId.Transform);
            if (transformRef is null)
                continue;
            var t = assets.Read(transformRef.Value, TransformData.Read);
            var toRoot = isRoot ? Matrix4x4.Identity : t.LocalMatrix * parent;
            objects.Add((goRef, go, toRoot, path));
            foreach (var childPtr in t.Children)
            {
                if (assets.Resolve(goRef.File, childPtr) is not { } childT
                    || assets.Resolve(childT.File, assets.Read(childT, TransformData.Read).GameObject) is not { } childGo)
                    continue;
                stack.Push((childGo, toRoot, path + "/" + assets.Read(childGo, GameObjectData.Read).Name, false));
            }
            foreach (var c in go.Components)
                if (assets.Resolve(goRef.File, c) is { ClassId: UnityClassId.LodGroup } lod)
                    foreach (var level in assets.Read(lod, LodGroupData.Read).Lods.Skip(1))
                        foreach (var r in level.Renderers)
                            lowerLods.Add(r.PathId);
        }

        foreach (var (goRef, go, toRoot, path) in objects)
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
                        renderers.Add(new RenderItem(path, toRoot, mesh, 0, -1, materials, false, r.CastShadows, go.Layer));
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
                                regions.Add(new PlotRegion("CorralRegion", toRoot, col.Center, col.Size));
                            continue;
                        }
                        var mesh = col.Shape == ColliderShape.Mesh ? assets.Resolve(file, col.Mesh) : null;
                        if (col.Shape == ColliderShape.Mesh && mesh is null)
                            continue;
                        colliders.Add(new ColliderItem(path, toRoot, col, mesh, go.Layer));
                        break;
                    }
                }
            }
        }
        return new PlotPrefab(type, rootGo.Name, renderers, colliders, regions);
    }
}
