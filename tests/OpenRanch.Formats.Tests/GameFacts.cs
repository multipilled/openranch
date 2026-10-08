using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Formats.Tests;

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

// Tests that need saves in a supported format. OPENRANCH_SAVE_DIR overrides the game's own folder.
public sealed class SaveFactAttribute : FactAttribute
{
    public static string SaveDirectory { get; } =
        Environment.GetEnvironmentVariable("OPENRANCH_SAVE_DIR") is { Length: > 0 } dir ? dir : SaveFile.DefaultSaveDirectory();

    public static IReadOnlyList<string> SupportedSaves { get; } = FindSupportedSaves();

    public SaveFactAttribute()
    {
        if (SupportedSaves.Count == 0)
            Skip = $"No v{SaveSchemas.Game.Version} saves found in {SaveDirectory}.";
    }

    private static List<string> FindSupportedSaves()
    {
        if (!Directory.Exists(SaveDirectory))
            return [];
        var found = new List<string>();
        foreach (var path in Directory.GetFiles(SaveDirectory, "*.sav"))
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
}
