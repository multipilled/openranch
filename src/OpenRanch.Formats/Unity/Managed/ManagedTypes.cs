using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OpenRanch.Formats.Unity.Managed;

/// <summary>A type definition in one of the game's managed assemblies.</summary>
public sealed class ManagedType
{
    internal ManagedType(ManagedTypes owner, string assembly, MetadataReader reader, TypeDefinitionHandle handle)
    {
        Owner = owner;
        Assembly = assembly;
        Reader = reader;
        Handle = handle;
        Definition = reader.GetTypeDefinition(handle);
        Name = reader.GetString(Definition.Name);
        Namespace = reader.GetString(Definition.Namespace);
        FullName = Definition.IsNested
            ? owner.Get(assembly, reader, Definition.GetDeclaringType()).FullName + "/" + Name
            : string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
    }

    internal ManagedTypes Owner { get; }
    public string Assembly { get; }
    internal MetadataReader Reader { get; }
    internal TypeDefinitionHandle Handle { get; }
    internal TypeDefinition Definition { get; }
    public string Name { get; }
    public string Namespace { get; }
    public string FullName { get; }

    public bool IsSerializableFlag => (Definition.Attributes & TypeAttributes.Serializable) != 0;
    public bool IsAbstract => (Definition.Attributes & TypeAttributes.Abstract) != 0;
    public bool IsGenericDefinition => Definition.GetGenericParameters().Count > 0;

    private ManagedType? _base;
    private bool _baseResolved;

    public ManagedType? BaseType
    {
        get
        {
            if (!_baseResolved)
            {
                _baseResolved = true;
                var b = Definition.BaseType;
                if (!b.IsNil)
                    _base = Owner.Resolve(Reader, b, Assembly);
            }
            return _base;
        }
    }

    /// <summary>
    /// The base type and, when it is a closed generic such as DefinitionList&lt;GadgetDefinition&gt;,
    /// its type arguments, resolved in this type's own generic context.
    /// </summary>
    public (ManagedType? Type, ImmutableArray<TypeShape> Arguments) BaseWithArguments(ImmutableArray<TypeShape> context)
    {
        var b = Definition.BaseType;
        if (b.IsNil)
            return (null, ImmutableArray<TypeShape>.Empty);
        if (b.Kind == HandleKind.TypeSpecification)
        {
            var shape = Reader.GetTypeSpecification((TypeSpecificationHandle)b).DecodeSignature(new ShapeProvider(), context);
            if (shape is GenericShape { Definition: NamedShape n } g)
                return (Owner.Resolve(n.Reader, n.Handle, Assembly), g.Arguments);
        }
        return (BaseType, ImmutableArray<TypeShape>.Empty);
    }

    public bool IsEnum => BaseType?.FullName == "System.Enum";
    public bool IsValueType => BaseType?.FullName is "System.ValueType" or "System.Enum";

    public bool DerivesFrom(string fullName)
    {
        for (var t = this; t is not null; t = t.BaseType)
            if (t.FullName == fullName)
                return true;
        return false;
    }

    /// <summary>The underlying primitive of an enum (the type of its instance field value__).</summary>
    public PrimitiveTypeCode EnumUnderlying()
    {
        foreach (var fh in Definition.GetFields())
        {
            var f = Reader.GetFieldDefinition(fh);
            if ((f.Attributes & FieldAttributes.Static) != 0)
                continue;
            if (f.DecodeSignature(new ShapeProvider(), ImmutableArray<TypeShape>.Empty) is PrimitiveShape p)
                return p.Code;
        }
        return PrimitiveTypeCode.Int32;
    }

    public override string ToString() => FullName;
}

/// <summary>A field's declared type, as read from its signature.</summary>
public abstract record TypeShape;
public sealed record PrimitiveShape(PrimitiveTypeCode Code) : TypeShape;
public sealed record NamedShape(MetadataReader Reader, EntityHandle Handle) : TypeShape;
public sealed record SzArrayShape(TypeShape Element) : TypeShape;
public sealed record GenericShape(TypeShape Definition, ImmutableArray<TypeShape> Arguments) : TypeShape;
public sealed record OtherShape(string Why) : TypeShape;

internal sealed class ShapeProvider : ISignatureTypeProvider<TypeShape, object?>
{
    public TypeShape GetPrimitiveType(PrimitiveTypeCode typeCode) => new PrimitiveShape(typeCode);
    public TypeShape GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => new NamedShape(reader, handle);
    public TypeShape GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => new NamedShape(reader, handle);
    public TypeShape GetSZArrayType(TypeShape elementType) => new SzArrayShape(elementType);
    public TypeShape GetGenericInstantiation(TypeShape genericType, ImmutableArray<TypeShape> typeArguments) => new GenericShape(genericType, typeArguments);
    public TypeShape GetArrayType(TypeShape elementType, System.Reflection.Metadata.ArrayShape shape) => new OtherShape("multi-dimensional array");
    public TypeShape GetByReferenceType(TypeShape elementType) => new OtherShape("by-ref");
    public TypeShape GetPointerType(TypeShape elementType) => new OtherShape("pointer");
    public TypeShape GetPinnedType(TypeShape elementType) => elementType;
    public TypeShape GetFunctionPointerType(MethodSignature<TypeShape> signature) => new OtherShape("function pointer");
    public TypeShape GetGenericMethodParameter(object? genericContext, int index) => new OtherShape("generic parameter");

    // The context is the type arguments of the class being read, when it is a closed generic base.
    public TypeShape GetGenericTypeParameter(object? genericContext, int index) =>
        genericContext is ImmutableArray<TypeShape> args && index < args.Length ? args[index] : new OtherShape("generic parameter");
    public TypeShape GetModifiedType(TypeShape modifier, TypeShape unmodifiedType, bool isRequired) => unmodifiedType;
    public TypeShape GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

/// <summary>
/// The game's managed assemblies (the player's own Managed folder), opened read-only through
/// metadata. No game code is loaded or run.
/// </summary>
public sealed class ManagedTypes : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, (PEReader Pe, MetadataReader Reader)?> _assemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MetadataReader, TypeDefinitionHandle), ManagedType> _types = new();
    private readonly Dictionary<string, ManagedType?> _byName = new();

    public ManagedTypes(string managedDirectory) => _directory = managedDirectory;

    private MetadataReader? Assembly(string name)
    {
        name = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (!_assemblies.TryGetValue(name, out var entry))
        {
            var path = Path.Combine(_directory, name + ".dll");
            if (File.Exists(path))
            {
                var pe = new PEReader(File.OpenRead(path));
                entry = (pe, pe.GetMetadataReader());
            }
            _assemblies[name] = entry;
        }
        return entry?.Reader;
    }

    internal ManagedType Get(string assembly, MetadataReader reader, TypeDefinitionHandle handle)
    {
        if (!_types.TryGetValue((reader, handle), out var type))
            _types[(reader, handle)] = type = new ManagedType(this, assembly, reader, handle);
        return type;
    }

    private string AssemblyNameOf(MetadataReader reader) => reader.GetString(reader.GetAssemblyDefinition().Name);

    /// <summary>Finds a top-level or nested type ("Outer/Inner") by assembly, namespace and name.</summary>
    public ManagedType? Find(string assembly, string ns, string name)
    {
        var key = $"{assembly}|{ns}|{name}";
        if (_byName.TryGetValue(key, out var cached))
            return cached;
        ManagedType? found = null;
        var reader = Assembly(assembly);
        if (reader is not null)
        {
            var parts = name.Split('/');
            foreach (var h in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(h);
                if (td.IsNested || reader.GetString(td.Name) != parts[0] || reader.GetString(td.Namespace) != ns)
                    continue;
                var current = h;
                foreach (var nested in parts.Skip(1))
                {
                    var match = reader.GetTypeDefinition(current).GetNestedTypes()
                        .FirstOrDefault(n => reader.GetString(reader.GetTypeDefinition(n).Name) == nested);
                    if (match.IsNil)
                    {
                        current = default;
                        break;
                    }
                    current = match;
                }
                if (!current.IsNil)
                    found = Get(AssemblyNameOf(reader), reader, current);
                break;
            }
            // Type forwarding (for example UnityEngine.dll forwarding to its modules).
            if (found is null)
            {
                foreach (var eh in reader.ExportedTypes)
                {
                    var et = reader.GetExportedType(eh);
                    if (reader.GetString(et.Name) == name && reader.GetString(et.Namespace) == ns
                        && et.Implementation.Kind == HandleKind.AssemblyReference)
                    {
                        var target = reader.GetAssemblyReference((AssemblyReferenceHandle)et.Implementation);
                        found = Find(reader.GetString(target.Name), ns, name);
                        break;
                    }
                }
            }
        }
        _byName[key] = found;
        return found;
    }

    /// <summary>Resolves a type definition or reference seen in <paramref name="reader"/>.</summary>
    public ManagedType? Resolve(MetadataReader reader, EntityHandle handle, string? assembly = null)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return Get(assembly ?? AssemblyNameOf(reader), reader, (TypeDefinitionHandle)handle);
            case HandleKind.TypeReference:
            {
                var tr = reader.GetTypeReference((TypeReferenceHandle)handle);
                var name = reader.GetString(tr.Name);
                var ns = reader.GetString(tr.Namespace);
                switch (tr.ResolutionScope.Kind)
                {
                    case HandleKind.AssemblyReference:
                        var ar = reader.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope);
                        return Find(reader.GetString(ar.Name), ns, name);
                    case HandleKind.TypeReference:
                        var outer = Resolve(reader, tr.ResolutionScope, assembly);
                        if (outer is null)
                            return null;
                        var nested = outer.Definition.GetNestedTypes()
                            .FirstOrDefault(n => outer.Reader.GetString(outer.Reader.GetTypeDefinition(n).Name) == name);
                        return nested.IsNil ? null : Get(outer.Assembly, outer.Reader, nested);
                    case HandleKind.ModuleDefinition:
                        return Find(assembly ?? AssemblyNameOf(reader), ns, name);
                    default:
                        return null;
                }
            }
            case HandleKind.TypeSpecification:
                var shape = reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(new ShapeProvider(), null);
                return shape is GenericShape { Definition: NamedShape n } ? Resolve(n.Reader, n.Handle, assembly) : null;
            default:
                return null;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _assemblies.Values)
            entry?.Pe.Dispose();
        _assemblies.Clear();
    }
}
