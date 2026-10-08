using System.Reflection;
using System.Reflection.Metadata;

namespace OpenRanch.Formats.Unity.Managed;

/// <summary>
/// The named values of an enum in one of the game's assemblies, read from metadata constants.
/// Serialized fields store enums as plain numbers; this turns them back into names such as
/// "PINK_PLORT". No game code is loaded or run.
/// </summary>
public sealed class EnumValues
{
    private readonly Dictionary<long, string> _names = new();
    private readonly Dictionary<string, long> _values = new(StringComparer.Ordinal);

    public string TypeName { get; }

    private EnumValues(string typeName) => TypeName = typeName;

    public IReadOnlyDictionary<long, string> Names => _names;
    public IReadOnlyDictionary<string, long> Values => _values;

    /// <summary>The name for a stored number, or the number itself when it has no name.</summary>
    public string NameOf(long value) => _names.TryGetValue(value, out var name) ? name : value.ToString();

    public long ValueOf(string name) =>
        _values.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"{TypeName} has no value {name}.");

    public static EnumValues Read(ManagedType type)
    {
        if (!type.IsEnum)
            throw new ArgumentException($"{type.FullName} is not an enum.", nameof(type));
        var result = new EnumValues(type.FullName);
        var reader = type.Reader;
        foreach (var fh in type.Definition.GetFields())
        {
            var field = reader.GetFieldDefinition(fh);
            if ((field.Attributes & FieldAttributes.Literal) == 0)
                continue;
            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
                continue;
            var constant = reader.GetConstant(constantHandle);
            var blob = reader.GetBlobReader(constant.Value);
            long value = constant.TypeCode switch
            {
                ConstantTypeCode.Byte => blob.ReadByte(),
                ConstantTypeCode.SByte => blob.ReadSByte(),
                ConstantTypeCode.Int16 => blob.ReadInt16(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                ConstantTypeCode.Int32 => blob.ReadInt32(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                ConstantTypeCode.Int64 => blob.ReadInt64(),
                ConstantTypeCode.UInt64 => (long)blob.ReadUInt64(),
                _ => throw new InvalidDataException($"Unexpected enum constant {constant.TypeCode} in {type.FullName}."),
            };
            var name = reader.GetString(field.Name);
            result._names.TryAdd(value, name);
            result._values[name] = value;
        }
        return result;
    }

    /// <summary>Reads an enum by assembly, namespace and name ("Outer/Inner" for nested types).</summary>
    public static EnumValues Read(ManagedTypes types, string assembly, string ns, string name) =>
        Read(types.Find(assembly, ns, name) ?? throw new KeyNotFoundException($"{ns}.{name} wasn't found in {assembly}."));
}
