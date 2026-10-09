namespace OpenRanch.Ranch;

/// <summary>Opens a ranch from either kind of save file. The file is only read.</summary>
public static class RanchFiles
{
    /// <summary>
    /// Reads an openranch ranch save (a JSON document, see docs/formats/openranch-saves.md) or imports
    /// one of the original's saves (see docs/behavior/ranch-saves.md). Which one it is comes from the
    /// file's first byte: openranch's saves are JSON objects, the original's start with a binary header.
    /// </summary>
    public static RanchState Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var start = bytes.AsSpan().TrimStart([(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n', (byte)0xEF, (byte)0xBB, (byte)0xBF]);
        return start.Length > 0 && start[0] == (byte)'{'
            ? RanchSave.FromBytes(bytes)
            : SaveImport.FromSave(SaveImport.ReadSave(path));
    }
}
