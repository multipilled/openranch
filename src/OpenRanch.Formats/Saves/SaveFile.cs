using System.Text;

namespace OpenRanch.Formats.Saves;

/// <summary>Short facts about a save, taken from its summary block and player data.</summary>
public sealed record SaveInfo(
    string GameName,
    string DisplayName,
    string GameVersion,
    int Currency,
    double WorldTime,
    DateTimeOffset SavedAt,
    int ActorCount,
    int PlotCount)
{
    /// <summary>The in-game day, counting from 1. A day is 86,400 game seconds.</summary>
    public int Day => (int)(WorldTime / 86400.0) + 1;
}

/// <summary>Reads and writes Slime Rancher save files (*.sav).</summary>
public static class SaveFile
{
    public const string GameTag = "SRGAME";

    /// <summary>The block versions this reader can fully read.</summary>
    public static readonly IReadOnlyList<uint> SupportedVersions = [SaveSchemas.Game.Version];

    /// <summary>Reads the tag and version at the start of a save without reading the rest.</summary>
    public static (string Tag, uint Version) PeekHeader(Stream stream)
    {
        var start = stream.Position;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var tag = reader.ReadString();
        var version = reader.ReadUInt32();
        stream.Position = start;
        return (tag, version);
    }

    public static SaveBlock Read(Stream stream)
    {
        var (tag, version) = PeekHeader(stream);
        if (tag != GameTag)
            throw new SaveFormatException($"Not a Slime Rancher save: starts with '{tag}'.");
        if (version != SaveSchemas.Game.Version)
            throw new NotSupportedException(
                $"Save format v{version} isn't supported yet (supported: v{SaveSchemas.Game.Version}, from game 1.4.x).");

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var game = SaveSchemas.Game.Read(reader);
        if (stream.CanSeek && stream.Position != stream.Length)
            throw new SaveFormatException($"{stream.Length - stream.Position} unread bytes after the save data.");
        return game;
    }

    public static SaveBlock Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static void Write(Stream stream, SaveBlock game)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        SaveSchemas.Game.Write(writer, game);
    }

    public static byte[] ToBytes(SaveBlock game)
    {
        using var stream = new MemoryStream();
        Write(stream, game);
        return stream.ToArray();
    }

    public static SaveInfo Describe(SaveBlock game)
    {
        var summary = game.Block("summary");
        return new SaveInfo(
            game.Get<string>("gameName"),
            game.Get<string>("displayName"),
            summary.Get<string>("gameVersion"),
            game.Block("player").Get<int>("currency"),
            game.Block("world").Get<double>("worldTime"),
            summary.Get<SaveTimestamp>("savedAt").ToDateTimeOffset(),
            game.List("actors").Count,
            game.Block("ranch").List("plots").Count);
    }

    /// <summary>
    /// Where the game keeps its saves on Windows:
    /// %USERPROFILE%\AppData\LocalLow\Monomi Park\Slime Rancher.
    /// </summary>
    public static string DefaultSaveDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Monomi Park", "Slime Rancher");
}
