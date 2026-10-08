using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenRanch.Formats.Unity;

/// <summary>
/// CPU decoding of DXT1 (BC1), DXT5 (BC3) and BC4 blocks to RGBA8, for checks and previews.
/// The game hands compressed blocks straight to the GPU instead.
/// </summary>
public static class BlockDecoder
{
    public static byte[] ToRgba(int format, ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var blockSize = format is TextureFormats.Dxt1 or TextureFormats.Bc4 ? 8 : 16;
        Span<byte> pixels = stackalloc byte[64];
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var block = data.Slice((by * blocksX + bx) * blockSize, blockSize);
                switch (format)
                {
                    case TextureFormats.Dxt1:
                        DecodeColor(block, pixels, allowTransparent: true);
                        break;
                    case TextureFormats.Dxt5:
                        DecodeColor(block[8..], pixels, allowTransparent: false);
                        DecodeAlpha(block, pixels, 3);
                        break;
                    case TextureFormats.Bc4:
                        DecodeAlpha(block, pixels, 0);
                        for (var i = 0; i < 16; i++)
                        {
                            pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4];
                            pixels[i * 4 + 3] = 255;
                        }
                        break;
                    default:
                        throw new NotSupportedException($"No CPU decoder for {TextureFormats.NameOf(format)}.");
                }
                for (var py = 0; py < 4; py++)
                {
                    var y = by * 4 + py;
                    if (y >= height)
                        break;
                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx * 4 + px;
                        if (x >= width)
                            break;
                        pixels.Slice((py * 4 + px) * 4, 4).CopyTo(rgba.AsSpan((y * width + x) * 4, 4));
                    }
                }
            }
        }
        return rgba;
    }

    private static void DecodeColor(ReadOnlySpan<byte> block, Span<byte> pixels, bool allowTransparent)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        Span<int> palette = stackalloc int[16];
        Expand(c0, palette[..4]);
        Expand(c1, palette.Slice(4, 4));
        for (var ch = 0; ch < 3; ch++)
        {
            int a = palette[ch], b = palette[4 + ch];
            if (c0 > c1 || !allowTransparent)
            {
                palette[8 + ch] = (2 * a + b) / 3;
                palette[12 + ch] = (a + 2 * b) / 3;
            }
            else
            {
                palette[8 + ch] = (a + b) / 2;
                palette[12 + ch] = 0;
            }
        }
        palette[11] = 255;
        palette[15] = c0 > c1 || !allowTransparent ? 255 : 0;
        for (var i = 0; i < 16; i++)
        {
            var index = (int)((bits >> (i * 2)) & 3);
            for (var ch = 0; ch < 4; ch++)
                pixels[i * 4 + ch] = (byte)palette[index * 4 + ch];
        }
    }

    private static void Expand(ushort c, Span<int> rgba)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        rgba[0] = (r << 3) | (r >> 2);
        rgba[1] = (g << 2) | (g >> 4);
        rgba[2] = (b << 3) | (b >> 2);
        rgba[3] = 255;
    }

    private static void DecodeAlpha(ReadOnlySpan<byte> block, Span<byte> pixels, int channel)
    {
        int a0 = block[0], a1 = block[1];
        Span<int> values = stackalloc int[8];
        values[0] = a0;
        values[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i < 7; i++)
                values[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        }
        else
        {
            for (var i = 1; i < 5; i++)
                values[i + 1] = ((5 - i) * a0 + i * a1) / 5;
            values[6] = 0;
            values[7] = 255;
        }
        ulong bits = 0;
        for (var i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
            pixels[i * 4 + channel] = (byte)values[(int)((bits >> (3 * i)) & 7)];
    }

    /// <summary>Writes RGBA8 pixels as a PNG (no filtering), for previews.</summary>
    public static void WritePng(string path, byte[] rgba, int width, int height)
    {
        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WriteChunk(file, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            // Unity stores rows bottom-up; write them top-down so the preview looks upright.
            for (var y = height - 1; y >= 0; y--)
            {
                z.WriteByte(0);
                z.Write(rgba, y * width * 4, width * 4);
            }
        }
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var buf in new[] { a, b })
            foreach (var x in buf)
            {
                crc ^= x;
                for (var k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        return ~crc;
    }
}
