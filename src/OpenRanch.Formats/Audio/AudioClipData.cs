using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Audio;

/// <summary>How Unity loads a clip at runtime. Kept for reference; openranch converts every clip the same way.</summary>
public enum AudioLoadType
{
    DecompressOnLoad = 0,
    CompressedInMemory = 1,
    Streaming = 2,
}

/// <summary>The codec Unity was asked to use when the clip was imported.</summary>
public enum AudioCompressionFormat
{
    Pcm = 0,
    Vorbis = 1,
    Adpcm = 2,
    Mp3 = 3,
    Vag = 4,
    Hevag = 5,
    Xma = 6,
    Aac = 7,
    GcAdpcm = 8,
    Atrac9 = 9,
}

/// <summary>
/// A Unity 2019.4 AudioClip. The sound itself is an FMOD sound bank (FSB5) kept in a <c>.resource</c>
/// file next to the serialized file; <see cref="Resource"/> says where.
/// </summary>
public sealed record AudioClipData(string Name, AudioLoadType LoadType, int Channels, int Frequency, int BitsPerSample,
    float Length, bool IsTrackerFormat, bool Ambisonic, int SubsoundIndex, bool PreloadAudioData, bool LoadInBackground,
    bool Legacy3D, StreamedData Resource, AudioCompressionFormat CompressionFormat)
{
    /// <summary>Reads the clip; <paramref name="r"/> ends exactly at the end of the object.</summary>
    public static AudioClipData Read(EndianReader r)
    {
        var name = r.ReadAlignedString();
        var loadType = (AudioLoadType)r.ReadInt32();
        var channels = r.ReadInt32();
        var frequency = r.ReadInt32();
        var bits = r.ReadInt32();
        var length = r.ReadSingle();
        var tracker = r.ReadBool();
        var ambisonic = r.ReadBool();
        r.Align();
        var subsound = r.ReadInt32();
        var preload = r.ReadBool();
        var background = r.ReadBool();
        var legacy3D = r.ReadBool();
        r.Align();
        var source = r.ReadAlignedString();
        var offset = (ulong)r.ReadInt64();
        var size = (ulong)r.ReadInt64();
        if (size > uint.MaxValue)
            throw new InvalidDataException($"Audio clip '{name}' claims {size} bytes of sound data.");
        var format = (AudioCompressionFormat)r.ReadInt32();
        return new AudioClipData(name, loadType, channels, frequency, bits, length, tracker, ambisonic, subsound,
            preload, background, legacy3D, new StreamedData(offset, (uint)size, source), format);
    }
}
