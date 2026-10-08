using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Text;

/// <summary>
/// The paths of the game's <c>Resources</c> folder, as kept by Unity's ResourceManager object in
/// <c>globalgamemanagers</c>: each lower-case path without extension (for example
/// <c>i18n/en/actor</c>) points at an object in one of the asset files. Several objects can share
/// one path when they differ in type.
/// </summary>
public sealed class ResourceIndex
{
    /// <summary>Unity's class id for the ResourceManager.</summary>
    public const int ResourceManagerClassId = 147;

    public SerializedFile File { get; }
    public IReadOnlyList<(string Path, PPtr Object)> Entries { get; }

    private ResourceIndex(SerializedFile file, List<(string, PPtr)> entries)
    {
        File = file;
        Entries = entries;
    }

    public static ResourceIndex Read(AssetSet assets)
    {
        var file = assets.File("globalgamemanagers") ?? throw new IOException("globalgamemanagers is missing.");
        var info = file.Objects.FirstOrDefault(o => o.ClassId == ResourceManagerClassId);
        if (info.ClassId != ResourceManagerClassId)
            throw new InvalidDataException("globalgamemanagers has no ResourceManager.");
        var r = assets.Reader(new AssetRef(file, info));
        // The container comes first; the dependency list after it isn't needed.
        var entries = r.ReadArray(x => (x.ReadAlignedString(), PPtr.Read(x)));
        return new ResourceIndex(file, entries);
    }

    /// <summary>Entries whose path starts with <paramref name="prefix"/> (case-insensitive, '/' separated).</summary>
    public IEnumerable<(string Path, AssetRef Asset)> Under(AssetSet assets, string prefix)
    {
        foreach (var (path, ptr) in Entries)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && assets.Resolve(File, ptr) is { } asset)
                yield return (path, asset);
    }
}
