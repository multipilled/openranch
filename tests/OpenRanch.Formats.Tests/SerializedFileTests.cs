using System.Buffers.Binary;
using System.Text;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

public class SerializedFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"openranch-test-{Guid.NewGuid():N}.assets");

    public void Dispose() => File.Delete(_path);

    // Builds a minimal format-21 file: two types (Texture2D, GameObject), three objects, no externals.
    private static byte[] BuildFile()
    {
        var meta = new MemoryStream();
        var m = new BinaryWriter(meta);
        m.Write(Encoding.ASCII.GetBytes("2019.4.29f1\0"));
        m.Write(19); // target platform
        m.Write(false); // no type trees
        m.Write(2); // type count
        foreach (var classId in new[] { UnityClassId.Texture2D, UnityClassId.GameObject })
        {
            m.Write(classId);
            m.Write(false); // stripped
            m.Write((short)-1); // script type index
            m.Write(new byte[16]); // layout hash
        }

        byte[] texture = NamedPayload("grass_tile");
        byte[] other = NamedPayload("water");
        byte[] gameObject = new byte[12];

        var objects = new (long PathId, byte[] Data, int Type)[] { (1, texture, 0), (2, gameObject, 1), (3, other, 0) };
        m.Write(objects.Length);
        long headerSize = 20;
        uint offset = 0;
        foreach (var (pathId, data, type) in objects)
        {
            while ((headerSize + meta.Length) % 4 != 0)
                m.Write((byte)0);
            m.Write(pathId);
            m.Write(offset);
            m.Write((uint)data.Length);
            m.Write(type);
            offset += (uint)((data.Length + 7) & ~7);
        }
        m.Write(0); // script types
        m.Write(0); // externals
        m.Write((byte)0); // user information
        m.Flush();

        var metadata = meta.ToArray();
        var dataOffset = (uint)((headerSize + metadata.Length + 15) & ~15);
        var file = new byte[dataOffset + offset];
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0), (uint)metadata.Length);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)file.Length);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), 21);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), dataOffset);
        metadata.CopyTo(file, headerSize);
        uint at = 0;
        foreach (var (_, data, _) in objects)
        {
            data.CopyTo(file, dataOffset + at);
            at += (uint)((data.Length + 7) & ~7);
        }
        return file;
    }

    private static byte[] NamedPayload(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var payload = new byte[4 + ((bytes.Length + 3) & ~3) + 8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, bytes.Length);
        bytes.CopyTo(payload, 4);
        return payload;
    }

    [Fact]
    public void Reads_header_types_and_objects()
    {
        File.WriteAllBytes(_path, BuildFile());
        Assert.True(SerializedFile.LooksLikeSerializedFile(_path));

        var file = SerializedFile.Open(_path);
        Assert.Equal(21u, file.FormatVersion);
        Assert.Equal("2019.4.29f1", file.UnityVersion);
        Assert.False(file.HasTypeTrees);
        Assert.Equal(3, file.Objects.Count);
        Assert.Equal([UnityClassId.Texture2D, UnityClassId.GameObject, UnityClassId.Texture2D], file.Objects.Select(o => o.ClassId));
        Assert.Equal([1L, 2L, 3L], file.Objects.Select(o => o.PathId));
    }

    [Fact]
    public void Reads_names_of_named_assets_only()
    {
        File.WriteAllBytes(_path, BuildFile());
        var file = SerializedFile.Open(_path);
        Assert.Equal("grass_tile", file.TryReadName(file.Objects[0]));
        Assert.Null(file.TryReadName(file.Objects[1]));
        Assert.Equal("water", file.TryReadName(file.Objects[2]));
    }

    [Fact]
    public void Rejects_files_that_are_not_serialized_files()
    {
        File.WriteAllBytes(_path, Encoding.ASCII.GetBytes(new string('x', 100)));
        Assert.False(SerializedFile.LooksLikeSerializedFile(_path));
    }
}
