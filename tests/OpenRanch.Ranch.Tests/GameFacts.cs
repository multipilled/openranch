using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch.Tests;

// Tests that need the real game. CI has none, so they are skipped unless the game is found
// through OPENRANCH_GAME_DIR or a Steam library on this machine.
public sealed class GameFactAttribute : FactAttribute
{
    public static GameInstall? Install { get; } = GameInstall.Find();

    public GameFactAttribute()
    {
        if (Install is null)
            Skip = $"Install Slime Rancher or set {GameInstall.GameDirVariable} to run this test.";
    }
}

// The player's saves, found in the game's save folder or OPENRANCH_SAVE_DIR. They are only ever
// copied into memory and read; nothing is written there.
public static class Saves
{
    public static string Directory { get; } =
        Environment.GetEnvironmentVariable("OPENRANCH_SAVE_DIR") is { Length: > 0 } dir ? dir : SaveFile.DefaultSaveDirectory();

    /// <summary>Every version 12 save in the folder.</summary>
    public static IReadOnlyList<string> Supported { get; } = FindSupported();

    private static List<string> FindSupported()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        var found = new List<string>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.sav").Order(StringComparer.Ordinal))
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
            try
            {
                var (tag, version) = SaveFile.PeekHeader(stream);
                if (tag == SaveFile.GameTag && version == SaveSchemas.Game.Version)
                    found.Add(path);
            }
            catch (EndOfStreamException)
            {
            }
        }
        return found;
    }

    /// <summary>The save of <paramref name="gameName"/> with the highest save counter, read into memory, or null.</summary>
    public static (string Path, SaveBlock Save)? Newest(string gameName) =>
        Supported
            .Where(p => Path.GetFileName(p).StartsWith(gameName + "_", StringComparison.Ordinal))
            .Select(p => (Path: p, Save: SaveImport.ReadSave(p)))
            .OrderByDescending(s => s.Save.Block("summary").Get<ulong>("saveNumber"))
            .Cast<(string, SaveBlock)?>()
            .FirstOrDefault();
}

public sealed class SaveFactAttribute : FactAttribute
{
    public SaveFactAttribute()
    {
        if (Saves.Supported.Count == 0)
            Skip = $"No v{SaveSchemas.Game.Version} saves found in {Saves.Directory}.";
    }
}

// The ranch the milestone is checked against: the development PC's 2022 Game2.
public sealed class Game2FactAttribute : FactAttribute
{
    public const string GameName = "20220508105930_Game2";

    public Game2FactAttribute()
    {
        if (!Saves.Supported.Any(p => Path.GetFileName(p).StartsWith(GameName + "_", StringComparison.Ordinal)))
            Skip = $"No saves of {GameName} in {Saves.Directory}.";
    }
}

// Enum names from the install, opened once for every test that needs them.
public static class InstalledNames
{
    private static readonly Lazy<GameEnums> Enums = new(() => new GameEnums(GameFactAttribute.Install!));

    public static GameEnums Get => Enums.Value;
}

// Script data from the install (reading every script component takes a while), read once.
public static class InstalledData
{
    private static readonly Lazy<(DayLength Day, PlotCatalog Plots)> Data = new(() =>
    {
        using var scripts = new GameScripts(GameFactAttribute.Install!);
        return (DayLength.Read(scripts), PlotCatalog.Read(scripts));
    });

    public static DayLength DayLength => Data.Value.Day;
    public static PlotCatalog Plots => Data.Value.Plots;
}
