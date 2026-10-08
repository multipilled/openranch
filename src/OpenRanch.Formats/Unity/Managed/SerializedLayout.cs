using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace OpenRanch.Formats.Unity.Managed;

/// <summary>How one serialized field is stored.</summary>
public abstract record FieldLayout(string Name);

/// <summary>A number or bool. Fields smaller than 4 bytes are padded to 4.</summary>
public sealed record PrimitiveLayout(string Name, PrimitiveTypeCode Code) : FieldLayout(Name);

public sealed record StringLayout(string Name) : FieldLayout(Name);

/// <summary>A reference to another Unity object.</summary>
public sealed record ReferenceLayout(string Name, string TypeName) : FieldLayout(Name);

/// <summary>An array or List: an int32 count, the items, then padding to 4.</summary>
public sealed record ArrayLayout(string Name, FieldLayout Element) : FieldLayout(Name);

/// <summary>A nested serializable class or struct, stored inline.</summary>
public sealed record GroupLayout(string Name, string TypeName, IReadOnlyList<FieldLayout> Fields) : FieldLayout(Name);

/// <summary>A Unity engine type with a fixed native layout (vectors, colours, curves, gradients).</summary>
public sealed record BuiltinLayout(string Name, string TypeName) : FieldLayout(Name);

/// <summary>
/// Works out, from a script's managed type, which fields Unity 2019.4 serializes and in what form,
/// following Unity's rules: public or [SerializeField] instance fields that aren't readonly, const or
/// [NonSerialized], of a serializable type, with base-class fields first.
/// </summary>
public sealed class SerializedLayout
{
    private const int MaxDepth = 7;

    private static readonly HashSet<string> Builtins =
    [
        "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Quaternion",
        "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Rect", "UnityEngine.Bounds", "UnityEngine.Matrix4x4",
        "UnityEngine.Vector2Int", "UnityEngine.Vector3Int", "UnityEngine.RectInt", "UnityEngine.BoundsInt",
        "UnityEngine.LayerMask", "UnityEngine.AnimationCurve", "UnityEngine.Gradient", "UnityEngine.RectOffset",
        "UnityEngine.GUIStyle",
    ];

    private readonly ManagedTypes _types;
    private readonly Dictionary<ManagedType, IReadOnlyList<FieldLayout>> _cache = new();

    public SerializedLayout(ManagedTypes types) => _types = types;

    /// <summary>The fields serialized after the MonoBehaviour or ScriptableObject header.</summary>
    public IReadOnlyList<FieldLayout> ForScript(ManagedType type) => Fields(type, 0);

    private IReadOnlyList<FieldLayout> Fields(ManagedType type, int depth)
    {
        if (depth == 0 && _cache.TryGetValue(type, out var cached))
            return cached;
        // Walk up to the Unity base class, keeping each level's type arguments for generic bases.
        var chain = new List<(ManagedType Type, ImmutableArray<TypeShape> Args)>();
        var current = (Type: (ManagedType?)type, Args: ImmutableArray<TypeShape>.Empty);
        while (current.Type is { } t && !IsUnityRoot(t.FullName) && t.FullName != "System.Object" && t.FullName != "System.ValueType")
        {
            chain.Add((t, current.Args));
            current = t.BaseWithArguments(current.Args);
        }
        chain.Reverse();

        var fields = new List<FieldLayout>();
        foreach (var (t, args) in chain)
        {
            foreach (var fh in t.Definition.GetFields())
            {
                var f = t.Reader.GetFieldDefinition(fh);
                var attrs = f.Attributes;
                if ((attrs & (FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.InitOnly | FieldAttributes.NotSerialized)) != 0)
                    continue;
                var isPublic = (attrs & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
                if (!isPublic && !HasAttribute(t.Reader, f.GetCustomAttributes(), "UnityEngine.SerializeField"))
                    continue;
                if (HasAttribute(t.Reader, f.GetCustomAttributes(), "UnityEngine.SerializeReference"))
                    continue; // managed references are stored separately; none expected in this game
                var name = t.Reader.GetString(f.Name);
                var shape = f.DecodeSignature(new ShapeProvider(), args);
                if (Layout(name, shape, t, depth, allowArray: true) is { } layout)
                    fields.Add(layout);
            }
        }
        if (depth == 0)
            _cache[type] = fields;
        return fields;
    }

    private static bool IsUnityRoot(string fullName) => fullName is "UnityEngine.MonoBehaviour" or "UnityEngine.ScriptableObject"
        or "UnityEngine.Behaviour" or "UnityEngine.Component" or "UnityEngine.Object";

    private FieldLayout? Layout(string name, TypeShape shape, ManagedType owner, int depth, bool allowArray)
    {
        switch (shape)
        {
            case PrimitiveShape p:
                return p.Code switch
                {
                    PrimitiveTypeCode.String => new StringLayout(name),
                    PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Char
                        or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32
                        or PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Single or PrimitiveTypeCode.Double
                        => new PrimitiveLayout(name, p.Code),
                    _ => null,
                };
            case SzArrayShape a when allowArray:
                return Layout("data", a.Element, owner, depth, allowArray: false) is { } element ? new ArrayLayout(name, element) : null;
            case GenericShape g when allowArray && g.Definition is NamedShape gn && g.Arguments.Length == 1
                                     && _types.Resolve(gn.Reader, gn.Handle, owner.Assembly)?.FullName == "System.Collections.Generic.List`1":
                return Layout("data", g.Arguments[0], owner, depth, allowArray: false) is { } item ? new ArrayLayout(name, item) : null;
            case NamedShape n:
            {
                var type = _types.Resolve(n.Reader, n.Handle, owner.Assembly);
                if (type is null)
                    return null;
                if (type.FullName == "System.String")
                    return new StringLayout(name);
                if (type.IsEnum)
                    return new PrimitiveLayout(name, type.EnumUnderlying());
                if (Builtins.Contains(type.FullName))
                    return new BuiltinLayout(name, type.FullName);
                if (type.DerivesFrom("UnityEngine.Object"))
                    return new ReferenceLayout(name, type.FullName);
                if (type.FullName.StartsWith("System.", StringComparison.Ordinal) || type.IsAbstract || type.IsGenericDefinition)
                    return null;
                if (!type.IsSerializableFlag || depth >= MaxDepth)
                    return null;
                return new GroupLayout(name, type.FullName, Fields(type, depth + 1));
            }
            default:
                return null;
        }
    }

    private static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string fullName)
    {
        foreach (var h in attributes)
        {
            var a = reader.GetCustomAttribute(h);
            EntityHandle typeHandle = a.Constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType(),
                _ => default,
            };
            string? name = typeHandle.Kind switch
            {
                HandleKind.TypeReference => FullRef(reader, (TypeReferenceHandle)typeHandle),
                HandleKind.TypeDefinition => FullDef(reader, (TypeDefinitionHandle)typeHandle),
                _ => null,
            };
            if (name == fullName)
                return true;
        }
        return false;

        static string FullRef(MetadataReader r, TypeReferenceHandle h)
        {
            var t = r.GetTypeReference(h);
            return r.GetString(t.Namespace) + "." + r.GetString(t.Name);
        }

        static string FullDef(MetadataReader r, TypeDefinitionHandle h)
        {
            var t = r.GetTypeDefinition(h);
            return r.GetString(t.Namespace) + "." + r.GetString(t.Name);
        }
    }
}
