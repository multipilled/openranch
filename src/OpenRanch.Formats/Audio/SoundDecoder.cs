using Fmod5Sharp;
using Fmod5Sharp.FmodTypes;

namespace OpenRanch.Formats.Audio;

/// <summary>The file a clip becomes: Ogg Vorbis when the bank holds Vorbis, otherwise 16-bit WAV.</summary>
public enum SoundFileFormat
{
    Ogg,
    Wav,
}

/// <summary>A clip turned into a standard sound file, ready for an engine to load from memory.</summary>
/// <param name="BankSounds">How many sounds the clip's bank holds (Unity writes one per clip).</param>
public sealed record DecodedSound(SoundFileFormat Format, byte[] Data, string Codec, int Channels, int Frequency, long SampleCount,
    int BankSounds = 1)
{
    public string Extension => Format == SoundFileFormat.Ogg ? "ogg" : "wav";
    public double Seconds => Frequency > 0 ? (double)SampleCount / Frequency : 0;
}

/// <summary>
/// Turns an FMOD 5 sound bank (FSB5), as Unity stores it for each AudioClip, into an Ogg Vorbis or
/// WAV file. Vorbis banks are re-wrapped into Ogg pages without re-encoding; ADPCM and PCM banks
/// become WAV. Nothing is played. Uses Fmod5Sharp (MIT).
/// </summary>
public static class SoundDecoder
{
    private static readonly byte[] Magic = "FSB5"u8.ToArray();

    public static bool LooksLikeFsb5(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual(Magic);

    /// <summary>Converts one subsound of the bank. Throws <see cref="InvalidDataException"/> when it can't.</summary>
    public static DecodedSound Decode(byte[] fsb, int subsoundIndex = 0)
    {
        if (!LooksLikeFsb5(fsb))
            throw new InvalidDataException("The sound data is not an FMOD 5 sound bank.");

        FmodSoundBank bank;
        try
        {
            bank = FsbLoader.LoadFsbFromByteArray(fsb);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"The FMOD bank couldn't be read: {e.Message}", e);
        }

        var codec = bank.Header.AudioType;
        if (subsoundIndex < 0 || subsoundIndex >= bank.Samples.Count)
            throw new InvalidDataException($"The bank has {bank.Samples.Count} sounds; subsound {subsoundIndex} was asked for.");
        var sample = bank.Samples[subsoundIndex];

        byte[]? data;
        string? extension;
        try
        {
            if (!sample.RebuildAsStandardFileFormat(out data, out extension) || data is null || data.Length == 0)
                throw new InvalidDataException($"{codec} sound couldn't be rebuilt.");
        }
        catch (Exception e) when (e is not (InvalidDataException or OutOfMemoryException))
        {
            throw new InvalidDataException($"{codec} sound couldn't be rebuilt: {e.Message}", e);
        }

        var format = extension switch
        {
            "ogg" => SoundFileFormat.Ogg,
            "wav" => SoundFileFormat.Wav,
            _ => throw new NotSupportedException($"{codec} sound rebuilt as unexpected .{extension}."),
        };
        var meta = sample.Metadata;
        return new DecodedSound(format, data, codec.ToString(), (int)meta.Channels, meta.Frequency, meta.SampleCount, bank.Samples.Count);
    }
}
