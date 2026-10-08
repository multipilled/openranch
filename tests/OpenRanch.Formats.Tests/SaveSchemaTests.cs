using System.Text;
using OpenRanch.Formats.Saves;
using static OpenRanch.Formats.Saves.SaveCodec;

namespace OpenRanch.Formats.Tests;

public class SaveSchemaTests
{
    private static readonly SaveSchema Inner = new("IN", 1, new SaveSchema.Field("value", I32));

    private static readonly SaveSchema Sample = new("SAMPLE", 3,
        new SaveSchema.Field("name", Str),
        new SaveSchema.Section(),
        new SaveSchema.Field("scores", MapOf(Str, F32)),
        new SaveSchema.Field("levels", PairsOf(I32, F32)),
        new SaveSchema.Field("maybe", Opt(F64)),
        new SaveSchema.Field("child", OptBlock(Inner)),
        new SaveSchema.Field("children", ListOf(BlockOf(Inner))),
        new SaveSchema.Field("lock", RecordOf(("on", Bool), ("until", F64))),
        new SaveSchema.Field("at", Stamp),
        new SaveSchema.Gate("hasExtra", new SaveSchema.Field("extra", U64)));

    private static SaveBlock Child(int value)
    {
        var block = new SaveBlock("IN", 1);
        block.Fields.Add(new("value", value));
        return block;
    }

    private static SaveBlock MakeSample(bool withExtra)
    {
        var lockRecord = new SaveRecord();
        lockRecord.Fields.Add(new("on", true));
        lockRecord.Fields.Add(new("until", 12.5));

        var block = new SaveBlock("SAMPLE", 3);
        block.Fields.Add(new("name", "Ranch"));
        block.Fields.Add(new("scores", new SaveMap { new("a", 1.5f), new("b", -2f) }));
        block.Fields.Add(new("levels", new SaveMap { new(7, 0.25f) }));
        block.Fields.Add(new("maybe", null));
        block.Fields.Add(new("child", Child(42)));
        block.Fields.Add(new("children", new List<object?> { Child(1), Child(2) }));
        block.Fields.Add(new("lock", lockRecord));
        block.Fields.Add(new("at", new SaveTimestamp(638000000000000000, 60)));
        block.Fields.Add(new("hasExtra", withExtra));
        if (withExtra)
            block.Fields.Add(new("extra", 99UL));
        return block;
    }

    private static byte[] Write(SaveSchema schema, SaveBlock block)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            schema.Write(writer, block);
        return stream.ToArray();
    }

    private static SaveBlock Read(SaveSchema schema, byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        var block = schema.Read(reader);
        Assert.Equal(bytes.Length, reader.BaseStream.Position);
        return block;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Blocks_round_trip_exactly(bool withExtra)
    {
        var bytes = Write(Sample, MakeSample(withExtra));
        var again = Write(Sample, Read(Sample, bytes));
        Assert.Equal(bytes, again);
    }

    [Fact]
    public void Block_framing_is_tag_version_and_markers()
    {
        var bytes = Write(Inner, Child(5));
        // "IN" with a one-byte length, uint32 version 1, 0x3000, int32 5, 0x4000.
        byte[] expected = [2, (byte)'I', (byte)'N', 1, 0, 0, 0, 0x00, 0x30, 5, 0, 0, 0, 0x00, 0x40];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Maps_put_an_element_marker_after_each_pair_but_pairs_do_not()
    {
        var map = Write(new SaveSchema("M", 1, new SaveSchema.Field("m", MapOf(I32, I32))), WithField("M", "m", new SaveMap { new(1, 2) }));
        var pairs = Write(new SaveSchema("M", 1, new SaveSchema.Field("m", PairsOf(I32, I32))), WithField("M", "m", new SaveMap { new(1, 2) }));
        Assert.Equal(pairs.Length + 2, map.Length);
    }

    [Fact]
    public void Wrong_tag_is_reported()
    {
        var bytes = Write(Inner, Child(5));
        var other = new SaveSchema("OUT", 1, new SaveSchema.Field("value", I32));
        Assert.Throws<SaveFormatException>(() => Read(other, bytes));
    }

    [Fact]
    public void Values_read_back_with_their_types()
    {
        var block = Read(Sample, Write(Sample, MakeSample(withExtra: true)));
        Assert.Equal("Ranch", block.Get<string>("name"));
        Assert.Null(block["maybe"]);
        Assert.Equal(42, block.Block("child").Get<int>("value"));
        Assert.Equal(2, block.List("children").Count);
        Assert.Equal(-2f, block.Map("scores")[1].Value);
        Assert.Equal(99UL, block.Get<ulong>("extra"));
    }

    private static SaveBlock WithField(string tag, string name, object? value)
    {
        var block = new SaveBlock(tag, 1);
        block.Fields.Add(new(name, value));
        return block;
    }
}
