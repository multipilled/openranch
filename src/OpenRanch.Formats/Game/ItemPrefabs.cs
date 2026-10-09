using System.Numerics;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Game;

/// <summary>A prefab's Rigidbody: mass, the two drags, and whether gravity pulls it and physics moves it at all.</summary>
public sealed record RigidbodyData(float Mass, float Drag, float AngularDrag, bool UseGravity = true, bool IsKinematic = false)
{
    public static RigidbodyData Read(EndianReader r)
    {
        PPtr.Read(r); // game object
        var (mass, drag, angularDrag) = (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return new RigidbodyData(mass, drag, angularDrag, r.ReadBool(), r.ReadBool());
    }
}

/// <summary>
/// A skinned mesh renderer (Unity class 137): the shared renderer fields, then its mesh and bones.
/// Layout in docs/formats/prefabs.md.
/// </summary>
public sealed record SkinnedMeshRendererData(RendererData Renderer, PPtr Mesh, List<PPtr> Bones, PPtr RootBone)
{
    public static SkinnedMeshRendererData Read(EndianReader r)
    {
        var renderer = RendererData.Read(r);
        PPtr.Read(r); // static batch root
        PPtr.Read(r); // probe anchor
        PPtr.Read(r); // light probe volume override
        r.ReadInt32(); // sorting layer id
        r.ReadInt16(); // sorting layer
        r.ReadInt16(); // sorting order
        r.ReadInt32(); // quality
        r.ReadBool(); // update when offscreen
        r.ReadBool(); // skinned motion vectors
        r.Align();
        var mesh = PPtr.Read(r);
        var bones = r.ReadArray(PPtr.Read);
        r.ReadArray(x => x.ReadSingle()); // blend shape weights
        var root = PPtr.Read(r);
        return new SkinnedMeshRendererData(renderer, mesh, bones, root);
    }
}

/// <summary>
/// Something a prefab draws: a mesh with its materials, placed relative to the prefab's root object
/// with the root's own scale included (Unity coordinates). Skinned meshes are drawn in their bind
/// pose. <see cref="FaceLayers"/> are extra materials drawn over the same mesh (a slime's eyes and mouth).
/// </summary>
public sealed record PrefabMesh(string Path, Matrix4x4 ToRoot, AssetRef Mesh, IReadOnlyList<AssetRef?> Materials, bool Skinned,
    IReadOnlyList<AssetRef> FaceLayers);

/// <summary>A collider of a prefab, placed relative to the root like <see cref="PrefabMesh"/>. A mesh collider names its mesh (<see cref="Mesh"/>).</summary>
public sealed record PrefabCollider(string Path, Matrix4x4 ToRoot, ColliderData Collider, PhysicMaterialData? Material = null, AssetRef? Mesh = null);

/// <summary>
/// A physic material (Unity class 134): frictions, bounciness and how each is combined with the other
/// collider's (0 average, 1 minimum, 2 multiply, 3 maximum).
/// </summary>
public sealed record PhysicMaterialData(string Name, float DynamicFriction, float StaticFriction, float Bounciness, int FrictionCombine, int BounceCombine)
{
    public static PhysicMaterialData Read(EndianReader r) =>
        new(r.ReadAlignedString(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), r.ReadInt32());

    /// <summary>A collider's material: the reference right after the collider's game object.</summary>
    public static PhysicMaterialData? Of(AssetSet assets, AssetRef collider)
    {
        var r = assets.Reader(collider);
        PPtr.Read(r); // game object
        return assets.Read(collider.File, PPtr.Read(r), Read);
    }
}

/// <summary>A script component of a prefab with its serialized fields.</summary>
public sealed record PrefabScript(string Path, Matrix4x4 ToRoot, string Class, SerializedObject Data);

/// <summary>A slime appearance's colours: top, middle and bottom of the body, and the vacpack icon colour.</summary>
public sealed record SlimePalette(Vector4 Top, Vector4 Middle, Vector4 Bottom, Vector4 Ammo);

/// <summary>
/// One item's prefab as the game spawns it: physics settings, what it draws, its colliders and its
/// scripts. See docs/formats/prefabs.md.
/// </summary>
public sealed record ItemPrefab(
    string Id,
    string Name,
    RigidbodyData? Body,
    int VacuumSize,
    IReadOnlyList<PrefabMesh> Meshes,
    IReadOnlyList<PrefabCollider> Colliders,
    IReadOnlyList<PrefabScript> Scripts,
    SlimePalette? Palette)
{
    /// <summary>A script on the prefab's root object, by class name.</summary>
    public SerializedObject? RootScript(string scriptClass) =>
        Scripts.FirstOrDefault(s => s.Class == scriptClass && s.Path == Name)?.Data;

    public float RootFloat(string scriptClass, string field, float fallback) =>
        RootScript(scriptClass) is { } s && s[field] is float f ? f : fallback;
}

/// <summary>
/// The game's item prefabs (slimes, plorts, food and so on) by item id, found through the world's
/// LookupDirector. Prefabs are read when first asked for and kept.
/// </summary>
public sealed class ItemPrefabs
{
    /// <summary>The slime face shown when nothing else is going on (SlimeFace.SlimeExpression "Happy").</summary>
    public const int DefaultExpression = 15;

    private readonly GameScripts _scripts;
    private readonly Dictionary<string, AssetRef> _roots;
    private readonly Dictionary<string, ItemPrefab> _read = new(StringComparer.Ordinal);

    private ItemPrefabs(GameScripts scripts, Dictionary<string, AssetRef> roots)
    {
        _scripts = scripts;
        _roots = roots;
    }

    public IEnumerable<string> Ids => _roots.Keys;
    public bool Has(string id) => _roots.ContainsKey(id);

    public ItemPrefab Get(string id)
    {
        if (!_read.TryGetValue(id, out var prefab))
        {
            if (!_roots.TryGetValue(id, out var root))
                throw new KeyNotFoundException($"No prefab for {id}.");
            _read[id] = prefab = ReadPrefab(id, root);
        }
        return prefab;
    }

    public static ItemPrefabs Read(GameScripts scripts)
    {
        var assets = scripts.Assets;
        var ids = scripts.IdentifiableIds;
        var (lookupRef, lookup) = scripts.OfClass("LookupDirector").FirstOrDefault();
        if (lookup?.Data?["identifiablePrefabs"] is not PPtr listPtr || scripts.Follow(lookupRef.File, listPtr) is not { } list)
            throw new InvalidDataException("The LookupDirector's prefab list wasn't found.");

        var roots = new Dictionary<string, AssetRef>(StringComparer.Ordinal);
        foreach (var item in list.Data.Data!.List("items").OfType<PPtr>())
        {
            if (assets.Resolve(list.Ref.File, item) is not { ClassId: UnityClassId.GameObject } goRef)
                continue;
            var go = assets.Read(goRef, GameObjectData.Read);
            foreach (var c in go.Components)
            {
                if (scripts.Follow(goRef.File, c) is { Data.ScriptClass: "Identifiable" } identifiable)
                    roots.TryAdd(ids.NameOf(Convert.ToInt64(identifiable.Data.Data!["id"])), goRef);
            }
        }

        // Gordos are listed apart (the LookupDirector's gordoEntries), each named by its GordoIdentifiable.
        if (lookup.Data["gordoEntries"] is PPtr gordoPtr && scripts.Follow(lookupRef.File, gordoPtr) is { } gordos)
            foreach (var item in gordos.Data.Data!.List("items").OfType<PPtr>())
            {
                if (assets.Resolve(gordos.Ref.File, item) is not { ClassId: UnityClassId.GameObject } goRef)
                    continue;
                foreach (var c in assets.Read(goRef, GameObjectData.Read).Components)
                    if (scripts.Follow(goRef.File, c) is { Data.ScriptClass: "GordoIdentifiable" } identifiable)
                        roots.TryAdd(ids.NameOf(Convert.ToInt64(identifiable.Data.Data!["id"])), goRef);
            }
        return new ItemPrefabs(scripts, roots);
    }

    private ItemPrefab ReadPrefab(string id, AssetRef root)
    {
        var assets = _scripts.Assets;
        var rootGo = assets.Read(root, GameObjectData.Read);
        var meshes = new List<PrefabMesh>();
        var colliders = new List<PrefabCollider>();
        var scripts = new List<PrefabScript>();
        var placed = new Dictionary<(string, long), Matrix4x4>(); // game object -> to-root matrix
        var lowerLods = new HashSet<(string, long)>();
        RigidbodyData? body = null;
        var vacuumSize = 0;

        // Lower levels of detail are listed by LODGroups; only the most detailed level is drawn.
        foreach (var (goRef, _) in Objects(root, fullRootTransform: false))
            foreach (var c in Components(goRef).Where(c => c.ClassId == UnityClassId.LodGroup))
                foreach (var level in assets.Read(c, LodGroupData.Read).Lods.Skip(1))
                    foreach (var r in level.Renderers)
                        if (assets.Resolve(c.File, r) is { } rr)
                            lowerLods.Add((rr.File.Path, rr.PathId));

        // Bones are transforms; a skinned mesh is drawn where its bones put it (below).
        var objects = Objects(root, fullRootTransform: false).ToList();
        var transforms = new Dictionary<(string, long), Matrix4x4>();
        foreach (var (goRef, toRoot) in objects)
            if (Components(goRef).FirstOrDefault(c => c.ClassId == UnityClassId.Transform) is { PathId: not 0 } t)
                transforms[(t.File.Path, t.PathId)] = toRoot;

        foreach (var (goRef, toRoot) in objects)
        {
            placed[(goRef.File.Path, goRef.PathId)] = toRoot;
            var go = assets.Read(goRef, GameObjectData.Read);
            var path = PathTo(goRef, root);
            MeshFilterData? filter = null;
            foreach (var c in Components(goRef).Where(c => c.ClassId == UnityClassId.MeshFilter))
                filter = assets.Read(c, MeshFilterData.Read);

            foreach (var c in Components(goRef))
            {
                switch (c.ClassId)
                {
                    case UnityClassId.Rigidbody when goRef.Equals(root):
                        body = assets.Read(c, RigidbodyData.Read);
                        break;
                    case UnityClassId.MeshRenderer when filter is not null && !lowerLods.Contains((c.File.Path, c.PathId)):
                    {
                        var r = assets.Read(c, RendererData.Read);
                        var materials = r.Materials.Select(m => assets.Resolve(c.File, m)).ToList();
                        if (r.Enabled && !OnlyDefaultMaterial(materials) && assets.Resolve(c.File, filter.Mesh) is { } mesh)
                            meshes.Add(new PrefabMesh(path, toRoot, mesh, materials, false, []));
                        break;
                    }
                    case UnityClassId.SkinnedMeshRenderer when !lowerLods.Contains((c.File.Path, c.PathId)):
                    {
                        var r = assets.Read(c, SkinnedMeshRendererData.Read);
                        var materials = r.Renderer.Materials.Select(m => assets.Resolve(c.File, m)).ToList();
                        if (r.Renderer.Enabled && !OnlyDefaultMaterial(materials) && assets.Resolve(c.File, r.Mesh) is { } mesh)
                            meshes.Add(new PrefabMesh(path, BindPlacement(c.File, r, mesh, transforms) ?? toRoot, mesh, materials, true, []));
                        break;
                    }
                    case UnityClassId.BoxCollider or UnityClassId.SphereCollider or UnityClassId.CapsuleCollider or UnityClassId.MeshCollider:
                    {
                        var col = assets.Read(c, x => ColliderData.Read(x, c.ClassId));
                        if (col.Enabled)
                            colliders.Add(new PrefabCollider(path, toRoot, col, PhysicMaterialData.Of(assets, c),
                                col.Shape == ColliderShape.Mesh ? assets.Resolve(c.File, col.Mesh) : null));
                        break;
                    }
                    case UnityClassId.MonoBehaviour:
                    {
                        var mb = _scripts.Reader.Read(c);
                        if (mb is { Enabled: true, ScriptClass: { } cls, Data: { } data })
                        {
                            scripts.Add(new PrefabScript(path, toRoot, cls, data));
                            if (cls == "Vacuumable" && goRef.Equals(root))
                                vacuumSize = Convert.ToInt32(data["size"]);
                        }
                        break;
                    }
                }
            }
        }

        var palette = AddSlimeAppearance(root, scripts, placed, meshes);
        return new ItemPrefab(id, rootGo.Name, body, vacuumSize, meshes, colliders, scripts, palette);
    }

    // Where a skinned mesh sits in its bind pose: its root bone's bind pose (mesh to bone), then the
    // bone's place under the prefab root. Unity draws skinned meshes by their bones, not by the
    // renderer's own object, which may be scaled differently (the pink gordo's is a quarter size).
    private Matrix4x4? BindPlacement(SerializedFile file, SkinnedMeshRendererData r, AssetRef mesh, Dictionary<(string, long), Matrix4x4> transforms)
    {
        var assets = _scripts.Assets;
        var bindPoses = assets.Read(mesh, MeshData.Read).BindPoses;
        if (bindPoses.Count == 0 || r.Bones.Count == 0)
            return null;
        var rootRef = assets.Resolve(file, r.RootBone);
        var index = rootRef is { } rr ? r.Bones.FindIndex(b => assets.Resolve(file, b) is { } br && br.Equals(rr)) : -1;
        index = Math.Max(0, index);
        if (index >= bindPoses.Count || assets.Resolve(file, r.Bones[index]) is not { } bone
            || !transforms.TryGetValue((bone.File.Path, bone.PathId), out var boneToRoot))
            return null;
        return bindPoses[index] * boneToRoot;
    }

    // Slime prefabs carry no body model: their SlimeAppearanceApplicator names the slime definition,
    // whose default appearance lists the parts to hang on the prefab's bones.
    private SlimePalette? AddSlimeAppearance(AssetRef root, List<PrefabScript> scripts, Dictionary<(string, long), Matrix4x4> placed,
        List<PrefabMesh> meshes)
    {
        var assets = _scripts.Assets;
        var applicatorScript = scripts.FirstOrDefault(s => s.Class == "SlimeAppearanceApplicator" && s.Path == PathTo(root, root));
        if (applicatorScript is null)
            return null;
        var applicator = applicatorScript.Data;
        var file = root.File;
        if (applicator["SlimeDefinition"] is not PPtr defPtr || _scripts.Follow(file, defPtr) is not { } definition)
            return null;
        var appearances = definition.Data.Data!.List("AppearancesDefault");
        if (appearances.Count == 0 || _scripts.Follow(definition.Ref.File, (PPtr)appearances[0]!) is not { } appearance)
            return null;
        var app = appearance.Data.Data!;
        var appFile = appearance.Ref.File;

        Matrix4x4? Placement(PPtr goPtr) =>
            assets.Resolve(file, goPtr) is { } r && placed.TryGetValue((r.File.Path, r.PathId), out var m) ? m : null;
        var bones = new Dictionary<int, PPtr>();
        foreach (var mapping in applicator.List("Bones").OfType<SerializedObject>())
            bones[Convert.ToInt32(mapping["Bone"])] = (PPtr)mapping["BoneObject"]!;
        var appearanceRoot = applicator["RootAppearanceObject"] is PPtr rootPtr ? Placement(rootPtr) : null;

        // The face for the default expression: eyes and mouth materials.
        AssetRef? eyes = null, mouth = null;
        var expression = applicator["SlimeExpression"] is int e ? e : DefaultExpression;
        if (app["Face"] is PPtr facePtr && _scripts.Follow(appFile, facePtr) is { } face)
        {
            foreach (var f in face.Data.Data!.List("ExpressionFaces").OfType<SerializedObject>())
            {
                if (Convert.ToInt32(f["SlimeExpression"]) != expression)
                    continue;
                eyes = f["Eyes"] is PPtr ep ? assets.Resolve(face.Ref.File, ep) : null;
                mouth = f["Mouth"] is PPtr mp ? assets.Resolve(face.Ref.File, mp) : null;
            }
        }

        foreach (var structure in app.List("Structures").OfType<SerializedObject>())
        {
            if (structure["Element"] is not PPtr elementPtr || _scripts.Follow(appFile, elementPtr) is not { } element)
                continue;
            var defaults = structure.List("DefaultMaterials").OfType<PPtr>().Select(m => assets.Resolve(appFile, m)).ToList();
            var perObject = structure.List("ElementMaterials").OfType<SerializedObject>().ToList();
            var faceRules = structure.List("FaceRules").OfType<SerializedObject>().ToList();
            var supportsFaces = structure["SupportsFaces"] is true;
            var prefabs = element.Data.Data!.List("Prefabs").OfType<PPtr>().ToList();
            for (var i = 0; i < prefabs.Count; i++)
            {
                if (_scripts.Follow(element.Ref.File, prefabs[i]) is not { } part)
                    continue;
                var p = part.Data.Data!;
                if (Convert.ToInt32(p["LODIndex"]) != 0 && p["IgnoreLODIndex"] is not true)
                    continue;
                var parentBone = Convert.ToInt32(p["ParentBone"]);
                var parent = parentBone != 0 && bones.TryGetValue(parentBone, out var bonePtr) ? Placement(bonePtr) : appearanceRoot;
                if (parent is null || assets.Resolve(part.Ref.File, part.Data.GameObject) is not { } partGo)
                    continue;
                var materials = i < perObject.Count && perObject[i]["OverrideDefaults"] is true
                    ? perObject[i].List("Materials").OfType<PPtr>().Select(m => assets.Resolve(appFile, m)).ToList()
                    : defaults;
                var layers = new List<AssetRef>();
                if (supportsFaces && i < faceRules.Count)
                {
                    if (faceRules[i]["ShowEyes"] is true && eyes is { } ey)
                        layers.Add(ey);
                    if (faceRules[i]["ShowMouth"] is true && mouth is { } mo)
                        layers.Add(mo);
                }
                AddPart(partGo, parent.Value, materials, layers, meshes);
            }
        }

        if (app["ColorPalette"] is not SerializedObject palette)
            return null;
        Vector4 C(string name) => palette[name] is Vector4 v ? v : Vector4.One;
        return new SlimePalette(C("Top"), C("Middle"), C("Bottom"), C("Ammo"));
    }

    // Adds the renderers of an appearance part's object tree, hung from a placed parent.
    private void AddPart(AssetRef partRoot, Matrix4x4 parent, IReadOnlyList<AssetRef?> materials, IReadOnlyList<AssetRef> faceLayers, List<PrefabMesh> meshes)
    {
        var assets = _scripts.Assets;
        foreach (var (goRef, toPart) in Objects(partRoot, fullRootTransform: true))
        {
            var toRoot = toPart * parent;
            var path = "appearance/" + PathTo(goRef, partRoot);
            MeshFilterData? filter = null;
            foreach (var c in Components(goRef).Where(c => c.ClassId == UnityClassId.MeshFilter))
                filter = assets.Read(c, MeshFilterData.Read);
            foreach (var c in Components(goRef))
            {
                if (c.ClassId == UnityClassId.SkinnedMeshRenderer)
                {
                    var r = assets.Read(c, SkinnedMeshRendererData.Read);
                    if (r.Renderer.Enabled && assets.Resolve(c.File, r.Mesh) is { } mesh)
                        meshes.Add(new PrefabMesh(path, toRoot, mesh, materials, true, faceLayers));
                }
                else if (c.ClassId == UnityClassId.MeshRenderer && filter is not null)
                {
                    var r = assets.Read(c, RendererData.Read);
                    if (r.Enabled && assets.Resolve(c.File, filter.Mesh) is { } mesh)
                        meshes.Add(new PrefabMesh(path, toRoot, mesh, materials, false, faceLayers));
                }
            }
        }
    }

    // Blob shadows and other helpers draw with Unity's built-in default material only.
    private bool OnlyDefaultMaterial(IReadOnlyList<AssetRef?> materials) =>
        materials.Count > 0 && materials.All(m => m is null || _scripts.Assets.Read(m.Value, MaterialData.Read).Name == "Default-Material");

    private IEnumerable<AssetRef> Components(AssetRef goRef)
    {
        foreach (var c in _scripts.Assets.Read(goRef, GameObjectData.Read).Components)
            if (_scripts.Assets.Resolve(goRef.File, c) is { } comp)
                yield return comp;
    }

    private TransformData? TransformOf(AssetRef goRef) =>
        Components(goRef).FirstOrDefault(c => c.ClassId == UnityClassId.Transform) is { PathId: not 0 } t
            ? _scripts.Assets.Read(t, TransformData.Read)
            : null;

    /// <summary>
    /// The active objects of a prefab, root first, with each one's matrix to the root. For a whole
    /// item the root's own position and rotation are left out (an instance sets them) but its scale is
    /// kept; an appearance part keeps its full local transform (<paramref name="fullRootTransform"/>).
    /// </summary>
    private IEnumerable<(AssetRef Go, Matrix4x4 ToRoot)> Objects(AssetRef root, bool fullRootTransform)
    {
        var t = TransformOf(root);
        if (t is null)
            yield break;
        var start = fullRootTransform ? t.LocalMatrix : Matrix4x4.CreateScale(t.LocalScale);
        var stack = new Stack<(AssetRef, TransformData, Matrix4x4)>();
        stack.Push((root, t, start));
        while (stack.Count > 0)
        {
            var (goRef, transform, toRoot) = stack.Pop();
            var go = _scripts.Assets.Read(goRef, GameObjectData.Read);
            if (!go.IsActive)
                continue;
            yield return (goRef, toRoot);
            foreach (var childPtr in transform.Children)
            {
                if (_scripts.Assets.Resolve(goRef.File, childPtr) is not { } childRef)
                    continue;
                var child = _scripts.Assets.Read(childRef, TransformData.Read);
                if (_scripts.Assets.Resolve(childRef.File, child.GameObject) is { } childGo)
                    stack.Push((childGo, child, child.LocalMatrix * toRoot));
            }
        }
    }

    private string PathTo(AssetRef goRef, AssetRef root)
    {
        var names = new List<string>();
        var current = goRef;
        while (true)
        {
            names.Add(_scripts.Assets.Read(current, GameObjectData.Read).Name);
            if (current.Equals(root) || TransformOf(current) is not { } t || t.Father.IsNull)
                break;
            if (_scripts.Assets.Resolve(current.File, t.Father) is not { } fatherRef)
                break;
            var father = _scripts.Assets.Read(fatherRef, TransformData.Read);
            if (_scripts.Assets.Resolve(fatherRef.File, father.GameObject) is not { } fatherGo)
                break;
            current = fatherGo;
        }
        names.Reverse();
        return string.Join("/", names);
    }
}
