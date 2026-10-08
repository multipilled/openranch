using System.Collections.Concurrent;
using OpenRanch.Formats.Game;

namespace OpenRanch.Formats.Unity;

/// <summary>An object located in a specific serialized file.</summary>
public readonly record struct AssetRef(SerializedFile File, ObjectInfo Info)
{
    public long PathId => Info.PathId;
    public int ClassId => Info.ClassId;
}

/// <summary>
/// All serialized files of an install, opened lazily, with references resolved across files.
/// Reads are thread-safe; file handles are kept open while the set lives.
/// </summary>
public sealed class AssetSet : IDisposable
{
    private readonly string _dataDirectory;
    private readonly ConcurrentDictionary<string, SerializedFile?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Dictionary<long, ObjectInfo>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string, long), object> _parsed = new();

    public AssetSet(GameInstall install) => _dataDirectory = install.DataDirectory;

    public AssetSet(string dataDirectory) => _dataDirectory = dataDirectory;

    /// <summary>Opens a serialized file by the name used in external references, e.g. "sharedassets2.assets".</summary>
    public SerializedFile? File(string name)
    {
        return _files.GetOrAdd(name, n =>
        {
            var path = PathFor(n);
            return System.IO.File.Exists(path) ? SerializedFile.Open(path) : null;
        });
    }

    private string PathFor(string externalName)
    {
        // Built-in resources are referenced as "Library/unity default resources" or "Resources/unity_builtin_extra".
        var name = externalName.Replace('\\', '/');
        var file = name[(name.LastIndexOf('/') + 1)..];
        if (name.StartsWith("Library/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_dataDirectory, "Resources", file);
        return Path.Combine(_dataDirectory, file);
    }

    private Dictionary<long, ObjectInfo> Index(SerializedFile file) =>
        _indexes.GetOrAdd(file.Path, _ => file.Objects.ToDictionary(o => o.PathId));

    public AssetRef? Find(SerializedFile file, long pathId) =>
        Index(file).TryGetValue(pathId, out var info) ? new AssetRef(file, info) : null;

    /// <summary>Resolves a reference stored in <paramref name="from"/>.</summary>
    public AssetRef? Resolve(SerializedFile from, PPtr ptr)
    {
        if (ptr.IsNull)
            return null;
        var file = ptr.FileId == 0 ? from : File(from.Externals[ptr.FileId - 1].PathName);
        return file is null ? null : Find(file, ptr.PathId);
    }

    public byte[] ReadBytes(AssetRef asset)
    {
        var stream = _streams.GetOrAdd(asset.File.Path, p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read));
        var bytes = new byte[asset.Info.ByteSize];
        lock (stream)
        {
            stream.Position = asset.Info.ByteStart;
            stream.ReadExactly(bytes);
        }
        return bytes;
    }

    public EndianReader Reader(AssetRef asset) => new(ReadBytes(asset), 0, asset.File.BigEndianData);

    /// <summary>Reads and caches an object with the given reader.</summary>
    public T Read<T>(AssetRef asset, Func<EndianReader, T> read) where T : class =>
        (T)_parsed.GetOrAdd((asset.File.Path, asset.PathId), _ => read(Reader(asset)));

    public T? Read<T>(SerializedFile from, PPtr ptr, Func<EndianReader, T> read) where T : class =>
        Resolve(from, ptr) is { } asset ? Read(asset, read) : null;

    /// <summary>Reads bulk data kept in a .resS or .resource file next to the serialized files.</summary>
    public byte[] ReadStreamed(StreamedData data)
    {
        var path = Path.Combine(_dataDirectory, Path.GetFileName(data.Path.Replace('\\', '/')));
        var stream = _streams.GetOrAdd(path, p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read));
        var bytes = new byte[data.Size];
        lock (stream)
        {
            stream.Position = (long)data.Offset;
            stream.ReadExactly(bytes);
        }
        return bytes;
    }

    public MeshData? Mesh(SerializedFile from, PPtr ptr) => Read(from, ptr, MeshData.Read);

    public DecodedMesh DecodeMesh(MeshData mesh) =>
        mesh.Decode(mesh.VertexData.Length > 0 || mesh.Stream.IsEmpty ? mesh.VertexData : ReadStreamed(mesh.Stream));

    public byte[] TextureBytes(Texture2DData texture) =>
        texture.ImageData.Length > 0 || texture.Stream.IsEmpty ? texture.ImageData : ReadStreamed(texture.Stream);

    public void Dispose()
    {
        foreach (var stream in _streams.Values)
            stream.Dispose();
        _streams.Clear();
    }
}
