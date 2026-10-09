using System.Buffers.Binary;
using System.Numerics;

namespace OpenRanch.Formats.Unity;

public sealed record SubMesh(uint FirstByte, uint IndexCount, int Topology, uint BaseVertex, uint FirstVertex, uint VertexCount);

public readonly record struct VertexChannel(byte Stream, byte Offset, byte Format, byte Dimension);

/// <summary>
/// A Unity 2019.4 mesh: index buffer, vertex channels and the raw vertex data (inline or in a .resS file).
/// <see cref="Decode"/> turns it into plain arrays.
/// </summary>
public sealed class MeshData
{
    public const int ChannelPosition = 0, ChannelNormal = 1, ChannelTangent = 2, ChannelColor = 3, ChannelUv0 = 4, ChannelUv1 = 5;

    public required string Name { get; init; }
    public required List<SubMesh> SubMeshes { get; init; }
    public required byte MeshCompression { get; init; }
    public required int IndexFormat { get; init; }
    public required byte[] IndexBuffer { get; init; }
    public required uint VertexCount { get; init; }
    public required List<VertexChannel> Channels { get; init; }
    public required byte[] VertexData { get; init; }
    public required StreamedData Stream { get; init; }
    public required Vector3 BoundsCenter { get; init; }
    public required Vector3 BoundsExtent { get; init; }
    public required int BindPoseCount { get; init; }
    /// <summary>
    /// Each bone's bind pose (mesh space to the bone's space), stored by Unity as e00, e01, ... e33 with
    /// eRC = row R, column C; here as System.Numerics matrices (row vectors), i.e. transposed.
    /// </summary>
    public IReadOnlyList<System.Numerics.Matrix4x4> BindPoses { get; init; } = [];

    public static MeshData Read(EndianReader r)
    {
        var name = r.ReadAlignedString();
        var subMeshes = r.ReadArray(x =>
        {
            var s = new SubMesh(x.ReadUInt32(), x.ReadUInt32(), x.ReadInt32(), x.ReadUInt32(), x.ReadUInt32(), x.ReadUInt32());
            x.Skip(24); // local bounds
            return s;
        });

        // Blend shapes: vertices, shapes, channels, full weights.
        r.ReadArray(x => { x.Skip(40); return 0; });
        r.ReadArray(x => { x.Skip(10); x.Align(); return 0; });
        r.ReadArray(x => { x.ReadAlignedString(); x.Skip(12); return 0; });
        r.ReadArray(x => x.ReadSingle());

        var bindPoseList = r.ReadArray(x =>
        {
            var e = new float[16];
            for (var i = 0; i < 16; i++)
                e[i] = x.ReadSingle();
            // e[R * 4 + C] is Unity's row R, column C; numerics wants its transpose.
            return new System.Numerics.Matrix4x4(
                e[0], e[4], e[8], e[12],
                e[1], e[5], e[9], e[13],
                e[2], e[6], e[10], e[14],
                e[3], e[7], e[11], e[15]);
        });
        var bindPoses = bindPoseList.Count;
        r.ReadArray(x => x.ReadUInt32()); // bone name hashes
        r.ReadUInt32(); // root bone name hash
        r.ReadArray(x => { x.Skip(24); return 0; }); // bone bounds
        r.ReadArray(x => x.ReadUInt32()); // variable bone count weights

        var compression = r.ReadByte();
        r.Skip(3); // readable, keep vertices, keep indices
        r.Align();
        var indexFormat = r.ReadInt32();
        var indexBuffer = r.ReadByteArray();

        var vertexCount = r.ReadUInt32();
        var channels = r.ReadArray(x => new VertexChannel(x.ReadByte(), x.ReadByte(), x.ReadByte(), (byte)(x.ReadByte() & 0x0F)));
        var vertexData = r.ReadByteArray();

        // Compressed mesh: 7 packed float vectors and packed int vectors in a fixed order, then UV info.
        SkipPackedFloat(r); SkipPackedFloat(r); SkipPackedFloat(r); SkipPackedFloat(r);
        SkipPackedInt(r); SkipPackedInt(r); SkipPackedInt(r);
        SkipPackedFloat(r);
        SkipPackedInt(r); SkipPackedInt(r);
        r.ReadUInt32();

        var center = r.ReadVector3();
        var extent = r.ReadVector3();
        r.ReadInt32(); // usage flags
        r.ReadByteArray(); // baked convex collision mesh
        r.ReadByteArray(); // baked triangle collision mesh
        r.Skip(8); // mesh metrics
        var stream = new StreamedData(r.ReadUInt32(), r.ReadUInt32(), r.ReadAlignedString());

        return new MeshData
        {
            Name = name, SubMeshes = subMeshes, MeshCompression = compression, IndexFormat = indexFormat,
            IndexBuffer = indexBuffer, VertexCount = vertexCount, Channels = channels, VertexData = vertexData,
            Stream = stream, BoundsCenter = center, BoundsExtent = extent, BindPoseCount = bindPoses,
            BindPoses = bindPoseList,
        };
    }

    private static void SkipPackedFloat(EndianReader r)
    {
        r.ReadUInt32();
        r.ReadSingle();
        r.ReadSingle();
        r.ReadByteArray();
        r.ReadByte();
        r.Align();
    }

    private static void SkipPackedInt(EndianReader r)
    {
        r.ReadUInt32();
        r.ReadByteArray();
        r.ReadByte();
        r.Align();
    }

    private static int FormatSize(byte format) => format switch
    {
        0 => 4, // float
        1 => 2, // half
        2 or 3 or 6 or 7 => 1, // unorm8, snorm8, uint8, sint8
        4 or 5 or 8 or 9 => 2, // unorm16, snorm16, uint16, sint16
        10 or 11 => 4, // uint32, sint32
        _ => throw new InvalidDataException($"Unknown vertex format {format}."),
    };

    /// <summary>Decodes vertices and indices. <paramref name="vertexData"/> is the inline data or the bytes from the stream file.</summary>
    public DecodedMesh Decode(byte[] vertexData)
    {
        if (MeshCompression != 0)
            throw new NotSupportedException($"Mesh '{Name}' uses Unity mesh compression, which isn't supported yet.");

        var count = (int)VertexCount;
        // Each stream holds its channels interleaved; streams follow each other, 16-byte aligned.
        var streamCount = Channels.Count == 0 ? 0 : Channels.Max(c => c.Stream) + 1;
        var strides = new int[streamCount];
        foreach (var c in Channels.Where(c => c.Dimension > 0))
            strides[c.Stream] = Math.Max(strides[c.Stream], c.Offset + FormatSize(c.Format) * c.Dimension);
        var starts = new int[streamCount];
        var offset = 0;
        for (var s = 0; s < streamCount; s++)
        {
            starts[s] = offset;
            offset += strides[s] * count;
            offset = (offset + 15) & ~15;
        }

        float[]? Channel(int index)
        {
            if (index >= Channels.Count || Channels[index].Dimension == 0)
                return null;
            var c = Channels[index];
            var size = FormatSize(c.Format);
            var values = new float[count * c.Dimension];
            for (var v = 0; v < count; v++)
            {
                var at = starts[c.Stream] + v * strides[c.Stream] + c.Offset;
                for (var d = 0; d < c.Dimension; d++)
                    values[v * c.Dimension + d] = ReadComponent(vertexData, at + d * size, c.Format);
            }
            return values;
        }

        var dims = new int[Math.Max(Channels.Count, 6)];
        for (var i = 0; i < Channels.Count; i++)
            dims[i] = Channels[i].Dimension;

        var indices = new int[IndexFormat == 0 ? IndexBuffer.Length / 2 : IndexBuffer.Length / 4];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = IndexFormat == 0
                ? BinaryPrimitives.ReadUInt16LittleEndian(IndexBuffer.AsSpan(i * 2))
                : (int)BinaryPrimitives.ReadUInt32LittleEndian(IndexBuffer.AsSpan(i * 4));

        return new DecodedMesh(Name, count, Channel(ChannelPosition), Channel(ChannelNormal), Channel(ChannelTangent),
            Channel(ChannelColor), dims[ChannelColor], Channel(ChannelUv0), dims[ChannelUv0], Channel(ChannelUv1), dims[ChannelUv1],
            indices, IndexFormat == 0 ? 2 : 4, SubMeshes);
    }

    private static float ReadComponent(byte[] data, int at, byte format) => format switch
    {
        0 => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at)),
        1 => (float)BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(at)),
        2 => data[at] / 255f,
        3 => Math.Max((sbyte)data[at] / 127f, -1f),
        4 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) / 65535f,
        5 => Math.Max(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at)) / 32767f, -1f),
        6 => data[at],
        7 => (sbyte)data[at],
        8 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)),
        9 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at)),
        10 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)),
        11 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)),
        _ => 0f,
    };
}

/// <summary>
/// Mesh arrays in Unity's coordinate system (left-handed, Y up). Arrays are flat: 3 floats per position,
/// 4 per tangent, colour and UV dimensions as given. Null when the mesh lacks that channel.
/// </summary>
public sealed record DecodedMesh(string Name, int VertexCount, float[]? Positions, float[]? Normals, float[]? Tangents,
    float[]? Colors, int ColorDimension, float[]? Uv0, int Uv0Dimension, float[]? Uv1, int Uv1Dimension,
    int[] Indices, int IndexSize, List<SubMesh> SubMeshes)
{
    /// <summary>The triangle indices of one sub-mesh, with its base vertex applied.</summary>
    public ReadOnlySpan<int> SubMeshIndices(int subMesh, out uint baseVertex)
    {
        var s = SubMeshes[subMesh];
        baseVertex = s.BaseVertex;
        return Indices.AsSpan((int)(s.FirstByte / IndexSize), (int)s.IndexCount);
    }
}
