using System.Text.RegularExpressions;

namespace OpenRanch.Formats.Game;

/// <summary>
/// A Slime Rancher install on this PC: the folder with SlimeRancher.exe and its data folder.
/// Found from OPENRANCH_GAME_DIR, or by looking through the Steam libraries.
/// </summary>
public sealed class GameInstall
{
    public const string GameDirVariable = "OPENRANCH_GAME_DIR";
    public const int SteamAppId = 433340;

    public string RootDirectory { get; }
    public string DataDirectory => Path.Combine(RootDirectory, "SlimeRancher_Data");
    public string ManagedDirectory => Path.Combine(DataDirectory, "Managed");

    private GameInstall(string root) => RootDirectory = root;

    /// <summary>Opens the install at <paramref name="directory"/>, or returns null if it isn't one.</summary>
    public static GameInstall? Open(string directory)
    {
        var root = Path.GetFullPath(directory);
        // Accept the data folder itself too.
        if (Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) == "SlimeRancher_Data")
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
        var data = Path.Combine(root, "SlimeRancher_Data");
        return File.Exists(Path.Combine(data, "globalgamemanagers")) ? new GameInstall(root) : null;
    }

    /// <summary>Finds the game, first from the environment variable, then from Steam.</summary>
    public static GameInstall? Find()
    {
        var fromEnv = Environment.GetEnvironmentVariable(GameDirVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Open(fromEnv);

        foreach (var library in SteamLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", "Slime Rancher");
            if (Directory.Exists(candidate) && Open(candidate) is { } install)
                return install;
        }
        return null;
    }

    /// <summary>Lists the Steam library folders named in Steam's libraryfolders.vdf.</summary>
    public static IEnumerable<string> SteamLibraries()
    {
        var steamRoots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            steamRoots.Add(Path.Combine(home, ".steam", "steam"));
            steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            if (seen.Add(root))
                yield return root;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(path))
                    yield return path;
            }
        }
    }
}
