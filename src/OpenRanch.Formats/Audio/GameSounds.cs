using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Audio;

/// <summary>An AudioClip and the serialized file it was found in.</summary>
public sealed record SoundClip(AssetRef Asset, AudioClipData Clip)
{
    public string Name => Clip.Name;
}

/// <summary>
/// Every AudioClip in an install. Reading the list touches only the small clip objects; sound data
/// is read from the <c>.resource</c> files and converted only when <see cref="Decode"/> is called.
/// </summary>
public sealed class GameSounds
{
    private readonly AssetSet _assets;
    private readonly Dictionary<string, List<SoundClip>> _byName;

    public IReadOnlyList<SoundClip> Clips { get; }

    private GameSounds(AssetSet assets, List<SoundClip> clips)
    {
        _assets = assets;
        Clips = clips;
        _byName = clips.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public static GameSounds Load(AssetSet assets, string dataDirectory)
    {
        var clips = new List<SoundClip>();
        foreach (var path in AssetCensus.SerializedFilePaths(dataDirectory))
        {
            var file = assets.File(Path.GetFileName(path));
            if (file is null)
                continue;
            foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.AudioClip))
            {
                var asset = new AssetRef(file, info);
                clips.Add(new SoundClip(asset, assets.Read(asset, AudioClipData.Read)));
            }
        }
        return new GameSounds(assets, clips);
    }

    public static GameSounds Load(AssetSet assets, GameInstall install) => Load(assets, install.DataDirectory);

    /// <summary>Clips with this name. Names are not unique across the game's files.</summary>
    public IReadOnlyList<SoundClip> Named(string name) => _byName.TryGetValue(name, out var list) ? list : [];

    /// <summary>The FSB5 bank behind a clip, as stored in its <c>.resource</c> file.</summary>
    public byte[] ReadBank(SoundClip clip)
    {
        if (clip.Clip.Resource.IsEmpty)
            throw new InvalidDataException($"Audio clip '{clip.Name}' has no sound data.");
        return _assets.ReadStreamed(clip.Clip.Resource);
    }

    public DecodedSound Decode(SoundClip clip) => SoundDecoder.Decode(ReadBank(clip), clip.Clip.SubsoundIndex);
}
