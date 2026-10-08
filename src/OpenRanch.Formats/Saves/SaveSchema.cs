namespace OpenRanch.Formats.Saves;

/// <summary>
/// The layout of one kind of block: its tag, its version and its fields in stored order.
/// A block is written as: tag (length-prefixed UTF-8), version (uint32), <see cref="BeginMarker"/>,
/// the fields, <see cref="EndMarker"/>.
/// </summary>
public sealed class SaveSchema
{
    public const short SectionMarker = 0x1000;
    public const short ElementMarker = 0x2000;
    public const short BeginMarker = 0x3000;
    public const short EndMarker = 0x4000;

    // Lists and maps in real saves stay far below this; a larger count means the reader is misaligned.
    private const int MaxCount = 10_000_000;

    public SaveSchema(string tag, uint version, params Item[] items)
    {
        Tag = tag;
        Version = version;
        Items = items;
    }

    public string Tag { get; }
    public uint Version { get; }
    public IReadOnlyList<Item> Items { get; }

    public abstract record Item;

    /// <summary>A named value.</summary>
    public sealed record Field(string Name, SaveCodec Codec) : Item;

    /// <summary>A section marker between groups of fields; it carries no value.</summary>
    public sealed record Section : Item;

    /// <summary>A bool field; the items inside are stored only when it is true.</summary>
    public sealed record Gate(string Name, params Item[] Items) : Item;

    public SaveBlock Read(BinaryReader reader)
    {
        var start = reader.BaseStream.Position;
        var tag = reader.ReadString();
        var version = reader.ReadUInt32();
        if (tag != Tag || version != Version)
            throw new SaveFormatException($"Expected block {Tag} v{Version} at byte {start}, found {tag} v{version}.");
        Expect(reader, BeginMarker, $"start of {Tag}");
        var block = new SaveBlock(tag, version);
        ReadItems(reader, Items, block);
        Expect(reader, EndMarker, $"end of {Tag}");
        return block;
    }

    public void Write(BinaryWriter writer, SaveBlock block)
    {
        writer.Write(Tag);
        writer.Write(Version);
        writer.Write(BeginMarker);
        WriteItems(writer, Items, block);
        writer.Write(EndMarker);
    }

    private static void ReadItems(BinaryReader reader, IEnumerable<Item> items, SaveRecord into)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case Field f:
                    into.Fields.Add(new(f.Name, f.Codec.Read(reader)));
                    break;
                case Section:
                    Expect(reader, SectionMarker, "section marker");
                    break;
                case Gate g:
                    var open = reader.ReadBoolean();
                    into.Fields.Add(new(g.Name, open));
                    if (open)
                        ReadItems(reader, g.Items, into);
                    break;
            }
        }
    }

    private static void WriteItems(BinaryWriter writer, IEnumerable<Item> items, SaveRecord from)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case Field f:
                    f.Codec.Write(writer, from[f.Name]);
                    break;
                case Section:
                    writer.Write(SectionMarker);
                    break;
                case Gate g:
                    var open = (bool)from[g.Name]!;
                    writer.Write(open);
                    if (open)
                        WriteItems(writer, g.Items, from);
                    break;
            }
        }
    }

    internal static void Expect(BinaryReader reader, short marker, string what)
    {
        var at = reader.BaseStream.Position;
        var value = reader.ReadInt16();
        if (value != marker)
            throw new SaveFormatException($"Expected {what} (0x{marker:X4}) at byte {at}, found 0x{value:X4}.");
    }

    internal static int ReadCount(BinaryReader reader)
    {
        var at = reader.BaseStream.Position;
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxCount)
            throw new SaveFormatException($"Implausible item count {count} at byte {at}.");
        return count;
    }
}

public sealed class SaveFormatException(string message) : Exception(message);
