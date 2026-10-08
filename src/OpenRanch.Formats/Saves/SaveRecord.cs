namespace OpenRanch.Formats.Saves;

/// <summary>A group of named values read from a save, in the order they are stored.</summary>
public class SaveRecord
{
    public List<SaveField> Fields { get; } = new();

    public object? this[string name] =>
        Fields.FirstOrDefault(f => f.Name == name) is { Name: not null } field
            ? field.Value
            : throw new KeyNotFoundException($"No field '{name}'.");

    public bool Has(string name) => Fields.Any(f => f.Name == name);

    public SaveBlock Block(string name) => (SaveBlock)this[name]!;
    public List<object?> List(string name) => (List<object?>)this[name]!;
    public SaveMap Map(string name) => (SaveMap)this[name]!;
    public T Get<T>(string name) => (T)this[name]!;
}

/// <summary>
/// A framed block: every block in a save starts with a short tag and a version number
/// (for example "SRGAME" version 12), then its fields between two marker values.
/// </summary>
public sealed class SaveBlock : SaveRecord
{
    public SaveBlock(string tag, uint version)
    {
        Tag = tag;
        Version = version;
    }

    public string Tag { get; }
    public uint Version { get; }

    public override string ToString() => $"{Tag} v{Version}";
}

public readonly record struct SaveField(string Name, object? Value);

/// <summary>Key/value pairs in stored order. Keys may be numbers, strings or blocks (positions).</summary>
public sealed class SaveMap : List<KeyValuePair<object, object?>>
{
}

/// <summary>A wall-clock time stored as .NET ticks plus a UTC offset in minutes.</summary>
public readonly record struct SaveTimestamp(long Ticks, double OffsetMinutes)
{
    public DateTimeOffset ToDateTimeOffset() => new(Ticks, TimeSpan.FromMinutes(OffsetMinutes));
}
