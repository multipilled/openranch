using System.Buffers.Binary;

namespace OpenRanch.Formats.Audio;

/// <summary>
/// What a converted sound file says about itself, read by walking its structure: Ogg pages (with
/// their checksums) and the Vorbis identification header, or the RIFF chunks of a WAV file. Used to
/// check a conversion without playing anything.
/// </summary>
public sealed record SoundFileInfo(SoundFileFormat Format, int Channels, int Frequency, long SampleCount, int Pages)
{
    public double Seconds => Frequency > 0 ? (double)SampleCount / Frequency : 0;

    public static SoundFileInfo Read(SoundFileFormat format, ReadOnlySpan<byte> data) =>
        format == SoundFileFormat.Ogg ? ReadOgg(data) : ReadWav(data);

    /// <summary>
    /// Checks every page of a single-stream Ogg Vorbis file: capture pattern, version, checksum and
    /// sequence. The sample count is the last page's granule position.
    /// </summary>
    public static SoundFileInfo ReadOgg(ReadOnlySpan<byte> data)
    {
        var pos = 0;
        var pages = 0;
        uint serial = 0;
        long granule = 0;
        var firstPacket = new List<byte>();
        var firstPacketDone = false;
        var sawEnd = false;
        while (pos < data.Length)
        {
            if (sawEnd)
                throw new InvalidDataException($"Data after the last Ogg page at {pos}.");
            if (data.Length - pos < 27 || !data[pos..(pos + 4)].SequenceEqual("OggS"u8))
                throw new InvalidDataException($"No Ogg page at {pos}.");
            if (data[pos + 4] != 0)
                throw new InvalidDataException($"Ogg page version {data[pos + 4]} at {pos}.");
            var flags = data[pos + 5];
            var pageGranule = BinaryPrimitives.ReadInt64LittleEndian(data[(pos + 6)..]);
            var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 14)..]);
            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 18)..]);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 22)..]);
            int segments = data[pos + 26];
            var headerSize = 27 + segments;
            if (data.Length - pos < headerSize)
                throw new InvalidDataException($"Ogg page at {pos} is cut short.");
            var lacing = data.Slice(pos + 27, segments);
            var bodySize = 0;
            foreach (var l in lacing)
                bodySize += l;
            if (data.Length - pos < headerSize + bodySize)
                throw new InvalidDataException($"Ogg page at {pos} is cut short.");

            if (pages == 0)
            {
                if ((flags & 0x02) == 0)
                    throw new InvalidDataException("The first Ogg page doesn't start a stream.");
                serial = pageSerial;
            }
            else if (pageSerial != serial)
                throw new InvalidDataException($"Ogg page at {pos} belongs to another stream.");
            if (sequence != pages)
                throw new InvalidDataException($"Ogg page at {pos} has sequence {sequence}, expected {pages}.");
            if (OggCrc(data.Slice(pos, headerSize + bodySize)) != crc)
                throw new InvalidDataException($"Ogg page at {pos} fails its checksum.");

            if (!firstPacketDone)
            {
                var body = pos + headerSize;
                foreach (var l in lacing)
                {
                    firstPacket.AddRange(data.Slice(body, l));
                    body += l;
                    if (l < 255)
                    {
                        firstPacketDone = true;
                        break;
                    }
                }
            }

            if (pageGranule != -1)
                granule = pageGranule;
            sawEnd = (flags & 0x04) != 0;
            pos += headerSize + bodySize;
            pages++;
        }
        if (pages == 0)
            throw new InvalidDataException("The Ogg file is empty.");
        if (!sawEnd)
            throw new InvalidDataException("The Ogg stream has no end page.");

        // Vorbis identification header: type 1, "vorbis", version, channels, rate, bitrates, block sizes, framing.
        var id = firstPacket.ToArray();
        if (id.Length < 30 || id[0] != 1 || !id.AsSpan(1, 6).SequenceEqual("vorbis"u8))
            throw new InvalidDataException("The Ogg stream doesn't start with a Vorbis identification header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(id.AsSpan(7)) != 0)
            throw new InvalidDataException("Unknown Vorbis version.");
        int channels = id[11];
        var rate = BinaryPrimitives.ReadInt32LittleEndian(id.AsSpan(12));
        if (channels == 0 || rate <= 0 || (id[29] & 1) == 0)
            throw new InvalidDataException("Bad Vorbis identification header.");
        return new SoundFileInfo(SoundFileFormat.Ogg, channels, rate, granule, pages);
    }

    /// <summary>Reads the format and data chunks of a PCM WAV file.</summary>
    public static SoundFileInfo ReadWav(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file.");
        var pos = 12;
        int channels = 0, rate = 0, blockAlign = 0;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 4)..]);
            if (size < 0 || pos + 8 + (long)size > data.Length)
                throw new InvalidDataException($"WAV chunk at {pos} runs past the end.");
            var body = data.Slice(pos + 8, size);
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16)
                    throw new InvalidDataException("WAV format chunk is too short.");
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(body[12..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0 || blockAlign == 0)
                    throw new InvalidDataException("WAV data comes before its format.");
                return new SoundFileInfo(SoundFileFormat.Wav, channels, rate, size / blockAlign, 0);
            }
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException("WAV file has no data chunk.");
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        // Ogg's checksum: CRC-32 with polynomial 0x04C11DB7, not reflected, starting from zero.
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i << 24;
            for (var k = 0; k < 8; k++)
                r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            table[i] = r;
        }
        return table;
    }

    /// <summary>Checksum of a page, computed with its own checksum field taken as zero.</summary>
    private static uint OggCrc(ReadOnlySpan<byte> page)
    {
        uint crc = 0;
        for (var i = 0; i < page.Length; i++)
        {
            var b = i is >= 22 and < 26 ? (byte)0 : page[i];
            crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ b) & 0xFF];
        }
        return crc;
    }
}
