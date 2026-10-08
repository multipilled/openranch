using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch;

/// <summary>
/// openranch's own save format for a <see cref="RanchState"/>: a versioned JSON document. The
/// layout and the rules for changing it are in docs/formats/openranch-saves.md.
/// </summary>
public static class RanchSave
{
    /// <summary>The value of the "format" member that marks an openranch ranch save.</summary>
    public const string FormatName = "openranch-ranch";

    /// <summary>The format version this build writes. Older versions are upgraded when read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The usual ending of an openranch ranch save's file name.</summary>
    public const string FileExtension = ".ranch.json";

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        // Times such as "never" are stored by the original as infinity; JSON numbers can't hold it.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    // The top level of a save. "format" and "version" come first so a reader can check them before the rest.
    private sealed record Document(string Format, int Version, RanchState Ranch);

    public static void Write(Stream stream, RanchState ranch) =>
        JsonSerializer.Serialize(stream, new Document(FormatName, CurrentVersion, ranch), Options);

    public static byte[] ToBytes(RanchState ranch)
    {
        using var stream = new MemoryStream();
        Write(stream, ranch);
        return stream.ToArray();
    }

    public static RanchState Read(Stream stream)
    {
        var root = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false })
            as JsonObject ?? throw new RanchSaveException("An openranch ranch save is a JSON object.");
        if (root["format"]?.GetValueKind() != JsonValueKind.String || root["format"]!.GetValue<string>() != FormatName)
            throw new RanchSaveException($"Not an openranch ranch save: \"format\" isn't \"{FormatName}\".");
        if (root["version"]?.GetValueKind() != JsonValueKind.Number || !root["version"]!.AsValue().TryGetValue<int>(out var version))
            throw new RanchSaveException("The save has no whole-number \"version\".");
        if (version > CurrentVersion)
            throw new NotSupportedException($"The save is format version {version}; this build of openranch reads up to version {CurrentVersion}.");
        if (version < 1)
            throw new RanchSaveException($"Unknown format version {version}.");

        Upgrade(root, version);
        var ranch = root["ranch"] as JsonObject ?? throw new RanchSaveException("The save has no \"ranch\" object.");
        return ranch.Deserialize<RanchState>(Options) ?? throw new RanchSaveException("The \"ranch\" object is empty.");
    }

    public static RanchState FromBytes(byte[] bytes) => Read(new MemoryStream(bytes, writable: false));

    public static RanchState ReadFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>
    /// Writes a save file, replacing any file at <paramref name="path"/> only once the new one is
    /// complete. Refuses to write anywhere inside the original game's save folder.
    /// </summary>
    public static void WriteFile(string path, RanchState ranch)
    {
        var full = Path.GetFullPath(path);
        if (IsInOriginalSaveFolder(full))
            throw new InvalidOperationException($"openranch never writes into the original game's save folder ({SaveFile.DefaultSaveDirectory()}).");
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, ToBytes(ranch));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    // Each format change adds a step here that rewrites the JSON of the version before it.
    private static void Upgrade(JsonObject root, int version)
    {
        while (version < CurrentVersion)
        {
            switch (version)
            {
                default:
                    throw new RanchSaveException($"No upgrade from format version {version}.");
            }
        }
        root["version"] = CurrentVersion;
    }

    /// <summary>Whether <paramref name="path"/> is the original game's save folder or anything inside it.</summary>
    public static bool IsInOriginalSaveFolder(string path)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SaveFile.DefaultSaveDirectory()));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(full, dir, comparison) || full.StartsWith(dir + Path.DirectorySeparatorChar, comparison);
    }
}

public sealed class RanchSaveException(string message) : Exception(message);
