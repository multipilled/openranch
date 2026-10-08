using System.Buffers.Binary;
using System.Text;

namespace OpenRanch.Formats.Unity;

/// <summary>A forward reader over a byte buffer with switchable endianness and 4-byte alignment.</summary>
public sealed class EndianReader
{
    private readonly byte[] _data;

    public EndianReader(byte[] data, int position = 0, bool bigEndian = false)
    {
        _data = data;
        Position = position;
        BigEndian = bigEndian;
    }

    public int Position { get; set; }
    public bool BigEndian { get; set; }
    public int Length => _data.Length;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (Position + count > _data.Length)
            throw new InvalidDataException($"Read of {count} bytes at {Position} runs past the end ({_data.Length}).");
        var span = _data.AsSpan(Position, count);
        Position += count;
        return span;
    }

    public byte ReadByte() => Take(1)[0];
    public bool ReadBool() => ReadByte() != 0;
    public short ReadInt16() => BigEndian ? BinaryPrimitives.ReadInt16BigEndian(Take(2)) : BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUInt16() => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(Take(2)) : BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt32() => BigEndian ? BinaryPrimitives.ReadInt32BigEndian(Take(4)) : BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt32() => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(Take(4)) : BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadInt64() => BigEndian ? BinaryPrimitives.ReadInt64BigEndian(Take(8)) : BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public float ReadSingle() => BigEndian ? BinaryPrimitives.ReadSingleBigEndian(Take(4)) : BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public void Skip(int count) => Take(count);

    public void Align(int alignment = 4)
    {
        var rem = Position % alignment;
        if (rem != 0)
            Position += alignment - rem;
    }

    /// <summary>Reads a zero-terminated UTF-8 string.</summary>
    public string ReadCString()
    {
        var end = Array.IndexOf(_data, (byte)0, Position);
        if (end < 0)
            throw new InvalidDataException($"Unterminated string at {Position}.");
        var s = Encoding.UTF8.GetString(_data, Position, end - Position);
        Position = end + 1;
        return s;
    }

    /// <summary>Reads Unity's serialized string: an int32 byte length, the UTF-8 bytes, then 4-byte alignment.</summary>
    public string ReadAlignedString()
    {
        var length = ReadInt32();
        if (length < 0 || Position + length > _data.Length)
            throw new InvalidDataException($"Bad string length {length} at {Position - 4}.");
        var s = Encoding.UTF8.GetString(Take(length));
        Align();
        return s;
    }
}
