namespace OpenRanch.Formats.Unity;

/// <summary>One object stored in a serialized file.</summary>
public readonly record struct ObjectInfo(long PathId, long ByteStart, uint ByteSize, int ClassId, short ScriptTypeIndex);

/// <summary>A type entry from the file's type table.</summary>
public readonly record struct SerializedType(int ClassId, bool IsStripped, short ScriptTypeIndex, byte[]? ScriptId);

/// <summary>Another serialized file that objects in this one point into.</summary>
public readonly record struct ExternalReference(Guid Guid, int Type, string PathName);

/// <summary>
/// Reads the header and object table of a Unity serialized file (the format behind
/// <c>levelN</c>, <c>*.assets</c> and <c>globalgamemanagers</c>). Object payloads are read on demand.
/// Covers serialized-file formats 9 to 22; Slime Rancher uses 21.
/// </summary>
public sealed class SerializedFile
{
    public string Path { get; }
    public uint FormatVersion { get; }
    public string UnityVersion { get; }
    public int TargetPlatform { get; }
    public bool HasTypeTrees { get; }
    public bool BigEndianData { get; }
    public long DataOffset { get; }
    public long FileSize { get; }
    public IReadOnlyList<SerializedType> Types { get; }
    public IReadOnlyList<ObjectInfo> Objects { get; }
    public IReadOnlyList<ExternalReference> Externals { get; }

    private SerializedFile(string path, uint format, string unityVersion, int platform, bool typeTrees, bool bigEndian,
        long dataOffset, long fileSize, List<SerializedType> types, List<ObjectInfo> objects, List<ExternalReference> externals)
    {
        Path = path;
        FormatVersion = format;
        UnityVersion = unityVersion;
        TargetPlatform = platform;
        HasTypeTrees = typeTrees;
        BigEndianData = bigEndian;
        DataOffset = dataOffset;
        FileSize = fileSize;
        Types = types;
        Objects = objects;
        Externals = externals;
    }

    /// <summary>True when the first bytes look like a serialized file header.</summary>
    public static bool LooksLikeSerializedFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 48)
                return false;
            var head = new byte[48];
            stream.ReadExactly(head);
            var r = new EndianReader(head, 0, bigEndian: true);
            r.Skip(8);
            var format = r.ReadUInt32();
            return format is >= 9 and <= 30;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static SerializedFile Open(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[48];
        stream.ReadExactly(head, 0, (int)Math.Min(48, stream.Length));
        var hr = new EndianReader(head, 0, bigEndian: true);

        long metadataSize = hr.ReadUInt32();
        long fileSize = hr.ReadUInt32();
        var format = hr.ReadUInt32();
        long dataOffset = hr.ReadUInt32();
        if (format < 9)
            throw new NotSupportedException($"Serialized file format {format} is older than this reader supports.");
        var bigEndian = hr.ReadByte() != 0;
        hr.Skip(3);
        if (format >= 22)
        {
            metadataSize = hr.ReadUInt32();
            fileSize = hr.ReadInt64();
            dataOffset = hr.ReadInt64();
            hr.Skip(8);
        }

        // The metadata follows the header; read header and metadata together so alignment uses file offsets.
        var metaEnd = hr.Position + metadataSize;
        var buffer = new byte[metaEnd];
        stream.Position = 0;
        stream.ReadExactly(buffer);
        var r = new EndianReader(buffer, hr.Position, bigEndian);

        var unityVersion = r.ReadCString();
        var platform = r.ReadInt32();
        var typeTrees = r.ReadBool();

        var types = new List<SerializedType>();
        var typeCount = r.ReadInt32();
        for (var i = 0; i < typeCount; i++)
            types.Add(ReadType(r, format, typeTrees));

        var objects = new List<ObjectInfo>();
        var objectCount = r.ReadInt32();
        for (var i = 0; i < objectCount; i++)
        {
            if (format >= 14)
                r.Align();
            var pathId = format >= 14 ? r.ReadInt64() : r.ReadInt32();
            var byteStart = format >= 22 ? r.ReadInt64() : r.ReadUInt32();
            var byteSize = r.ReadUInt32();
            var typeId = r.ReadInt32();
            int classId;
            short scriptIndex = -1;
            if (format >= 17)
            {
                var type = types[typeId];
                classId = type.ClassId;
                scriptIndex = type.ScriptTypeIndex;
            }
            else
            {
                classId = typeId;
                if (format < 16)
                    r.ReadUInt16();
                if (format >= 11)
                    scriptIndex = r.ReadInt16();
                if (format is 15 or 16)
                    r.ReadByte();
            }
            objects.Add(new ObjectInfo(pathId, dataOffset + byteStart, byteSize, classId, scriptIndex));
        }

        if (format >= 11)
        {
            var scriptCount = r.ReadInt32();
            for (var i = 0; i < scriptCount; i++)
            {
                r.ReadInt32();
                if (format >= 14)
                {
                    r.Align();
                    r.ReadInt64();
                }
                else
                {
                    r.ReadInt32();
                }
            }
        }

        var externals = new List<ExternalReference>();
        var externalCount = r.ReadInt32();
        for (var i = 0; i < externalCount; i++)
        {
            r.ReadCString();
            var guid = new Guid(r.ReadBytes(16));
            var type = r.ReadInt32();
            externals.Add(new ExternalReference(guid, type, r.ReadCString()));
        }

        return new SerializedFile(path, format, unityVersion, platform, typeTrees, bigEndian,
            dataOffset, fileSize, types, objects, externals);
    }

    private static SerializedType ReadType(EndianReader r, uint format, bool typeTrees)
    {
        var classId = r.ReadInt32();
        var stripped = format >= 16 && r.ReadBool();
        short scriptIndex = format >= 17 ? r.ReadInt16() : (short)-1;
        byte[]? scriptId = null;
        if ((format >= 16 && classId == UnityClassId.MonoBehaviour) || (format < 16 && classId < 0))
            scriptId = r.ReadBytes(16);
        r.Skip(16); // hash of the type's layout
        if (typeTrees)
            SkipTypeTree(r, format);
        return new SerializedType(classId, stripped, scriptIndex, scriptId);
    }

    private static void SkipTypeTree(EndianReader r, uint format)
    {
        if (format < 12 && format != 10)
            throw new NotSupportedException("Type trees in serialized formats before 12 aren't supported.");
        var nodeCount = r.ReadInt32();
        var stringBytes = r.ReadInt32();
        r.Skip(nodeCount * (format >= 19 ? 32 : 24) + stringBytes);
        if (format >= 21)
        {
            var dependencies = r.ReadInt32();
            r.Skip(dependencies * 4);
        }
    }

    /// <summary>Reads one object's raw bytes.</summary>
    public byte[] ReadObjectBytes(ObjectInfo obj)
    {
        using var stream = File.OpenRead(Path);
        stream.Position = obj.ByteStart;
        var bytes = new byte[obj.ByteSize];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Returns the <c>m_Name</c> of an object whose class stores its name first (meshes, textures,
    /// materials, sounds, text, animation clips and other named assets), or null for other classes.
    /// </summary>
    public string? TryReadName(ObjectInfo obj, Stream? shared = null)
    {
        if (!UnityClassId.StoresNameFirst(obj.ClassId) || obj.ByteSize < 4)
            return null;
        var head = new byte[Math.Min(obj.ByteSize, 1024u)];
        var stream = shared ?? File.OpenRead(Path);
        try
        {
            stream.Position = obj.ByteStart;
            stream.ReadExactly(head);
        }
        finally
        {
            if (shared is null)
                stream.Dispose();
        }
        var r = new EndianReader(head, 0, BigEndianData);
        try
        {
            return r.ReadAlignedString();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
