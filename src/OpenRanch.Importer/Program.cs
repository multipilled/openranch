using System.Globalization;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Importer;

public static class Program
{
    private const string Usage = """
        openranch-import: reads your own copy of Slime Rancher. It never changes game files or saves.

        Commands:
          inventory [--game DIR]           Count every asset in the game's data files by type
          saves [--dir DIR] [--verify]     List your saves; --verify re-writes each supported save in memory
                                           and checks the bytes match the original exactly

        The game is found through OPENRANCH_GAME_DIR or your Steam libraries.
        Saves default to %USERPROFILE%\AppData\LocalLow\Monomi Park\Slime Rancher.
        """;

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return args.FirstOrDefault() switch
            {
                "inventory" => Inventory(args[1..]),
                "saves" => Saves(args[1..]),
                _ => PrintUsage(),
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or SaveFormatException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine(Usage);
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static GameInstall RequireInstall(string[] args)
    {
        var dir = Option(args, "--game");
        var install = dir is null ? GameInstall.Find() : GameInstall.Open(dir);
        return install ?? throw new IOException(
            $"Slime Rancher wasn't found. Pass --game <folder> or set {GameInstall.GameDirVariable}.");
    }

    private static int Inventory(string[] args)
    {
        var install = RequireInstall(args);
        var census = AssetCensus.Take(install);
        Console.WriteLine($"Game: {install.RootDirectory}");
        Console.WriteLine($"Unity {census.Files[0].UnityVersion}, serialized format {census.Files[0].FormatVersion}");
        Console.WriteLine();
        Console.WriteLine($"{"File",-28} {"Objects",10} {"Size (MB)",10}");
        foreach (var file in census.Files)
            Console.WriteLine($"{Path.GetFileName(file.Path),-28} {file.Objects.Count,10:N0} {new FileInfo(file.Path).Length / 1048576.0,10:N1}");
        Console.WriteLine();
        Console.WriteLine($"{census.Files.Count} files, {census.TotalObjects:N0} objects");
        Console.WriteLine();
        Console.WriteLine($"{"Type",-26} {"Count",10}");
        foreach (var (classId, count) in census.CountsByClass.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
            Console.WriteLine($"{UnityClassId.NameOf(classId),-26} {count,10:N0}");
        return 0;
    }

    private static int Saves(string[] args)
    {
        var dir = Option(args, "--dir") ?? SaveFile.DefaultSaveDirectory();
        var verify = args.Contains("--verify");
        if (!Directory.Exists(dir))
            throw new IOException($"No save folder at {dir}.");

        var files = Directory.GetFiles(dir, "*.sav").Order(StringComparer.Ordinal).ToList();
        int supported = 0, matched = 0;
        foreach (var path in files)
        {
            // Saves are read into memory once and never opened for writing.
            var original = File.ReadAllBytes(path);
            var name = Path.GetFileName(path);
            using var stream = new MemoryStream(original, writable: false);
            var (tag, version) = SaveFile.PeekHeader(stream);
            if (tag != SaveFile.GameTag || !SaveFile.SupportedVersions.Contains(version))
            {
                Console.WriteLine($"{name}: {tag} v{version}, not supported yet");
                continue;
            }

            supported++;
            var game = SaveFile.Read(stream);
            var info = SaveFile.Describe(game);
            var line = $"{name}: v{version} \"{info.DisplayName}\" game {info.GameVersion}, day {info.Day}, " +
                       $"{info.Currency:N0} newbucks, {info.ActorCount} actors, {info.PlotCount} plots";
            if (verify)
            {
                var rewritten = SaveFile.ToBytes(game);
                var same = rewritten.AsSpan().SequenceEqual(original);
                if (same)
                    matched++;
                line += same ? ", re-writes byte for byte" : $", MISMATCH {Mismatch(original, rewritten)}";
            }
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine($"{files.Count} saves, {supported} in a supported format" +
                          (verify ? $", {matched} of {supported} re-write byte for byte" : ""));
        return verify && matched != supported ? 1 : 0;
    }

    private static string Mismatch(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            if (a[i] != b[i])
                return $"at byte {i}";
        return $"in length ({a.Length} vs {b.Length} bytes)";
    }
}
