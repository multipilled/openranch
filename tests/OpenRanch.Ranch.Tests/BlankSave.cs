using System.Reflection;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch.Tests;

// Builds an all-default block for any of the save layouts in SaveSchemas (zero numbers, empty
// strings, lists and maps, absent optional values), so import tests can run without real saves.
// Tests then set the fields they care about.
public static class BlankSave
{
    private static readonly Dictionary<string, SaveSchema> Schemas = typeof(SaveSchemas)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(SaveSchema))
        .Select(f => (SaveSchema)f.GetValue(null)!)
        .ToDictionary(s => $"{s.Tag} v{s.Version}");

    public static SaveBlock Game() => Of(SaveSchemas.Game);

    public static SaveBlock Of(SaveSchema schema)
    {
        var block = new SaveBlock(schema.Tag, schema.Version);
        AddItems(block, schema.Items);
        return block;
    }

    private static void AddItems(SaveRecord into, IEnumerable<SaveSchema.Item> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case SaveSchema.Field f:
                    into.Fields.Add(new(f.Name, Default(f.Codec.Describe())));
                    break;
                case SaveSchema.Gate g:
                    into.Fields.Add(new(g.Name, false));
                    break;
            }
        }
    }

    // Codec descriptions: "int", "list of X", "map of K to V", "X?" (optional), "TAG vN" (a block), or an enum label.
    private static object? Default(string codec)
    {
        if (codec.StartsWith("list of ", StringComparison.Ordinal))
            return new List<object?>();
        if (codec.StartsWith("map of ", StringComparison.Ordinal))
            return new SaveMap();
        if (codec.EndsWith('?'))
            return null;
        if (Schemas.TryGetValue(codec, out var schema))
            return Of(schema);
        return codec switch
        {
            "uint" => 0u,
            "long" => 0L,
            "ulong" => 0UL,
            "float" => 0f,
            "double" => 0.0,
            "bool" => false,
            "string" => "",
            "timestamp" => new SaveTimestamp(0, 0),
            "record" => throw new NotSupportedException("Unframed records only appear inside maps."),
            _ => 0, // int, or an enum stored as int
        };
    }

    /// <summary>Replaces the value of a field that is already there.</summary>
    public static T Set<T>(this T record, string name, object? value) where T : SaveRecord
    {
        var index = record.Fields.FindIndex(f => f.Name == name);
        if (index < 0)
            throw new KeyNotFoundException($"No field '{name}' in {record}.");
        record.Fields[index] = new(name, value);
        return record;
    }

    public static SaveBlock Vector(float x, float y, float z) =>
        Of(SaveSchemas.Vector3).Set("x", x).Set("y", y).Set("z", z);
}
