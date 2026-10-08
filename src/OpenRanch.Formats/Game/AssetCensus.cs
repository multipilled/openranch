using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Game;

/// <summary>Counts every object in the game's serialized files by Unity class.</summary>
public sealed class AssetCensus
{
    public IReadOnlyList<SerializedFile> Files { get; }
    public IReadOnlyDictionary<int, int> CountsByClass { get; }
    public long TotalObjects => CountsByClass.Values.Sum(c => (long)c);

    private AssetCensus(List<SerializedFile> files, Dictionary<int, int> counts)
    {
        Files = files;
        CountsByClass = counts;
    }

    public int Count(int classId) => CountsByClass.TryGetValue(classId, out var n) ? n : 0;

    /// <summary>The serialized files in a data folder: levels, .assets files and globalgamemanagers.</summary>
    public static IEnumerable<string> SerializedFilePaths(string dataDirectory) =>
        Directory.EnumerateFiles(dataDirectory)
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return name == "globalgamemanagers" || name.EndsWith(".assets", StringComparison.Ordinal)
                    || (name.StartsWith("level", StringComparison.Ordinal) && name.Length > 5 && name[5..].All(char.IsAsciiDigit));
            })
            .Order(StringComparer.Ordinal);

    public static AssetCensus Take(GameInstall install)
    {
        var files = SerializedFilePaths(install.DataDirectory).Select(SerializedFile.Open).ToList();
        var counts = new Dictionary<int, int>();
        foreach (var file in files)
            foreach (var obj in file.Objects)
                counts[obj.ClassId] = counts.GetValueOrDefault(obj.ClassId) + 1;
        return new AssetCensus(files, counts);
    }
}
