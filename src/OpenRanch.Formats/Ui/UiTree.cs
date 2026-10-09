using System.Numerics;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Ui;

/// <summary>
/// A RectTransform (Unity class 224): a Transform's fields followed by the uGUI layout fields, all in Unity's
/// y-up canvas space (Unity 2019.4 layout, read from the install's level2 and level3 and consumed to the byte).
/// </summary>
public sealed record RectTransformData(TransformData Transform, Vector2 AnchorMin, Vector2 AnchorMax, Vector2 AnchoredPosition,
    Vector2 SizeDelta, Vector2 Pivot)
{
    public static RectTransformData Read(EndianReader r) =>
        new(TransformData.Read(r), r.ReadVector2(), r.ReadVector2(), r.ReadVector2(), r.ReadVector2(), r.ReadVector2());
}

/// <summary>One component of a UI object: its Unity class, and for scripts the script's class and fields.</summary>
public sealed record UiComponent(AssetRef Ref, int ClassId, string? ScriptClass, SerializedObject? Data, bool Enabled)
{
    public object? this[string field] => Data?[field];
}

/// <summary>
/// One GameObject of a uGUI hierarchy read from the install: its name, whether it is active in the data, its
/// RectTransform (null for a plain Transform), its components and its children in order.
/// </summary>
public sealed class UiNode
{
    public required string Name { get; init; }
    public required bool Active { get; init; }
    public required AssetRef GameObject { get; init; }
    public RectTransformData? Rect { get; init; }
    public TransformData? Transform { get; init; }
    public List<UiComponent> Components { get; } = [];
    public List<UiNode> Children { get; } = [];
    public UiNode? Parent { get; private set; }

    internal void Add(UiNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>The first component whose script class ends with <paramref name="scriptClass"/> (e.g. "Image", "TextMeshProUGUI").</summary>
    public UiComponent? Script(string scriptClass) =>
        Components.FirstOrDefault(c => c.ScriptClass is { } s && (s == scriptClass || s.EndsWith("." + scriptClass, StringComparison.Ordinal)));

    public bool Has(string scriptClass) => Script(scriptClass) is not null;

    /// <summary>A descendant by a slash-separated path of names below this node ("Panel/Buttons/Play").</summary>
    public UiNode? Find(string path)
    {
        var node = this;
        foreach (var part in path.Split('/'))
        {
            node = node.Children.FirstOrDefault(c => c.Name == part);
            if (node is null)
                return null;
        }
        return node;
    }

    /// <summary>The first node of this subtree (depth first, including this one) with the given name.</summary>
    public UiNode? Descendant(string name) => Walk().FirstOrDefault(n => n.Name == name);

    public IEnumerable<UiNode> Walk()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.Walk())
                yield return n;
    }

    /// <summary>The path from the tree's root, for messages.</summary>
    public string PathName => Parent is null ? Name : Parent.PathName + "/" + Name;

    public override string ToString() => PathName;
}

/// <summary>Reads uGUI hierarchies (scene canvases and prefabs) from the install's serialized files.</summary>
public sealed class UiReader
{
    private readonly AssetSet _assets;
    private readonly MonoBehaviourReader _scripts;

    public UiReader(AssetSet assets, MonoBehaviourReader scripts)
    {
        _assets = assets;
        _scripts = scripts;
    }

    public AssetSet Assets => _assets;
    public MonoBehaviourReader Scripts => _scripts;

    /// <summary>The roots of every hierarchy in <paramref name="file"/> that has a RectTransform somewhere: top-level objects.</summary>
    public IEnumerable<UiNode> Roots(SerializedFile file)
    {
        foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.RectTransform))
        {
            var asset = new AssetRef(file, info);
            var rect = _assets.Read(asset, RectTransformData.Read);
            if (rect.Transform.Father.IsNull)
                yield return Read(asset)!;
        }
    }

    /// <summary>Reads the hierarchy below a Transform or RectTransform.</summary>
    public UiNode? Read(AssetRef transform)
    {
        TransformData t;
        RectTransformData? rect = null;
        if (transform.ClassId == UnityClassId.RectTransform)
        {
            rect = _assets.Read(transform, RectTransformData.Read);
            t = rect.Transform;
        }
        else if (transform.ClassId == UnityClassId.Transform)
            t = _assets.Read(transform, TransformData.Read);
        else
            return null;

        if (_assets.Resolve(transform.File, t.GameObject) is not { } goRef)
            return null;
        var go = _assets.Read(goRef, GameObjectData.Read);
        var node = new UiNode { Name = go.Name, Active = go.IsActive, GameObject = goRef, Rect = rect, Transform = t };
        foreach (var ptr in go.Components)
        {
            if (_assets.Resolve(goRef.File, ptr) is not { } c)
                continue;
            if (c.ClassId == UnityClassId.MonoBehaviour)
            {
                var mb = _scripts.Read(c);
                node.Components.Add(new UiComponent(c, c.ClassId, mb.ScriptClass, mb.Data, mb.Enabled));
            }
            else
                node.Components.Add(new UiComponent(c, c.ClassId, null, null, true));
        }
        foreach (var childPtr in t.Children)
            if (_assets.Resolve(transform.File, childPtr) is { } child && Read(child) is { } childNode)
                node.Add(childNode);
        return node;
    }

    /// <summary>Reads the hierarchy of the GameObject a reference points at (a prefab's root, say).</summary>
    public UiNode? ReadGameObject(SerializedFile from, PPtr gameObject)
    {
        if (_assets.Resolve(from, gameObject) is not { ClassId: UnityClassId.GameObject } goRef)
            return null;
        var go = _assets.Read(goRef, GameObjectData.Read);
        foreach (var ptr in go.Components)
            if (_assets.Resolve(goRef.File, ptr) is { ClassId: UnityClassId.RectTransform or UnityClassId.Transform } t)
                return Read(t);
        return null;
    }

    /// <summary>The GameObject a script component (e.g. a prefab field pointing at a MonoBehaviour) sits on, read as a tree.</summary>
    public UiNode? ReadOwner(SerializedFile from, PPtr component)
    {
        if (_assets.Resolve(from, component) is not { } c)
            return null;
        PPtr go;
        if (c.ClassId == UnityClassId.MonoBehaviour)
            go = MonoBehaviourReader.ReadHeader(_assets.Reader(c)).GameObject;
        else if (c.ClassId == UnityClassId.GameObject)
            return ReadGameObject(from, component);
        else
            go = PPtr.Read(_assets.Reader(c));
        return ReadGameObject(c.File, go);
    }
}
