using System.Buffers.Binary;
using OpenRanch.Formats.Audio;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

public class AudioTests
{
    [Fact]
    public void Wav_header_gives_channels_rate_and_length()
    {
        var wav = Wav(channels: 2, rate: 22050, frames: 1000);
        var info = SoundFileInfo.ReadWav(wav);
        Assert.Equal((2, 22050, 1000L), (info.Channels, info.Frequency, info.SampleCount));
    }

    [Fact]
    public void Non_fsb_data_is_refused()
    {
        Assert.False(SoundDecoder.LooksLikeFsb5("RIFF"u8));
        Assert.Throws<InvalidDataException>(() => SoundDecoder.Decode(new byte[64]));
    }

    [GameFact]
    public void Every_audio_clip_reads_to_its_exact_end_and_points_at_a_sound_bank()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        var sounds = GameSounds.Load(assets, install);
        Assert.Equal(953, sounds.Clips.Count);

        var resourceSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var magic = new byte[4];
        foreach (var clip in sounds.Clips)
        {
            var r = assets.Reader(clip.Asset);
            AudioClipData.Read(r);
            Assert.Equal(r.Length, r.Position);

            var c = clip.Clip;
            Assert.True(c.Channels is 1 or 2, $"{c.Name}: {c.Channels} channels");
            Assert.True(c.Frequency is >= 8000 and <= 96000, $"{c.Name}: {c.Frequency} Hz");
            Assert.True(c.Length > 0, $"{c.Name}: length {c.Length}");
            Assert.False(c.Resource.IsEmpty, $"{c.Name} has no sound data");
            Assert.Equal(0, c.SubsoundIndex);

            var path = Path.Combine(install.DataDirectory, Path.GetFileName(c.Resource.Path));
            if (!resourceSizes.TryGetValue(path, out var size))
                resourceSizes[path] = size = new FileInfo(path).Length;
            Assert.True(c.Resource.Offset + c.Resource.Size <= (ulong)size, $"{c.Name} runs past the end of its file");
            using var stream = File.OpenRead(path);
            stream.Position = (long)c.Resource.Offset;
            stream.ReadExactly(magic);
            Assert.True(SoundDecoder.LooksLikeFsb5(magic), $"{c.Name} doesn't start with an FSB5 header");
        }
        Assert.Equal(3, resourceSizes.Count);
        Assert.Equal(sounds.Clips.Count, sounds.Clips.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count());
    }

    // Converts a spread of clips (every 25th, so all three resource files and both short effects
    // and long music are covered) and checks each file's structure against the clip.
    [GameFact]
    public void Sampled_clips_convert_to_well_formed_files()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        var sounds = GameSounds.Load(assets, install);
        var formats = new HashSet<SoundFileFormat>();
        for (var i = 0; i < sounds.Clips.Count; i += 25)
        {
            var clip = sounds.Clips[i];
            var sound = sounds.Decode(clip);
            var info = SoundFileInfo.Read(sound.Format, sound.Data);
            formats.Add(sound.Format);
            Assert.Equal(clip.Clip.Channels, info.Channels);
            Assert.Equal(clip.Clip.Frequency, info.Frequency);
            Assert.InRange(info.Seconds, clip.Clip.Length - 0.05, clip.Clip.Length + 0.05 + clip.Clip.Length * 0.01);
        }
        Assert.Contains(SoundFileFormat.Ogg, formats);
    }

    [GameFact]
    public void A_damaged_ogg_page_fails_its_checksum()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        var sounds = GameSounds.Load(assets, install);
        var sound = sounds.Clips.Select(sounds.Decode).First(s => s.Format == SoundFileFormat.Ogg);
        var damaged = (byte[])sound.Data.Clone();
        damaged[^10] ^= 0x5A;
        Assert.Throws<InvalidDataException>(() => SoundFileInfo.ReadOgg(damaged));
    }

    private static byte[] Wav(int channels, int rate, int frames)
    {
        var dataSize = frames * channels * 2;
        var wav = new byte[44 + dataSize];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataSize);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), rate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataSize);
        return wav;
    }
}
