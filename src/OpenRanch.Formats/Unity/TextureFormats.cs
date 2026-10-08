namespace OpenRanch.Formats.Unity;

/// <summary>Unity's texture format numbers for the formats Slime Rancher uses.</summary>
public static class TextureFormats
{
    public const int Alpha8 = 1;
    public const int Rgb24 = 3;
    public const int Rgba32 = 4;
    public const int Argb32 = 5;
    public const int Rgb565 = 7;
    public const int Dxt1 = 10;
    public const int Dxt5 = 12;
    public const int Rgba4444 = 13;
    public const int Bgra32 = 14;
    public const int RgbaHalf = 17;
    public const int Bc6H = 24;
    public const int Bc7 = 25;
    public const int Bc4 = 26;
    public const int Bc5 = 27;
    public const int Dxt1Crunched = 28;
    public const int Dxt5Crunched = 29;

    /// <summary>
    /// Returns texture data a GPU can sample: crunched textures are decoded to plain DXT blocks,
    /// everything else is passed through. The data holds all mip levels, largest first.
    /// </summary>
    public static (int Format, byte[] Data, int MipCount) GpuData(Texture2DData texture, byte[] raw)
    {
        switch (texture.Format)
        {
            case Dxt1Crunched:
            case Dxt5Crunched:
                var blocks = Crunch.DecodeAllLevels(raw, out var header);
                return (texture.Format == Dxt1Crunched ? Dxt1 : Dxt5, blocks, header.Levels);
            default:
                return (texture.Format, raw, texture.MipCount);
        }
    }

    public static string NameOf(int format) => format switch
    {
        Alpha8 => "Alpha8",
        Rgb24 => "RGB24",
        Rgba32 => "RGBA32",
        Argb32 => "ARGB32",
        Rgb565 => "RGB565",
        Dxt1 => "DXT1",
        Dxt5 => "DXT5",
        Rgba4444 => "RGBA4444",
        Bgra32 => "BGRA32",
        RgbaHalf => "RGBAHalf",
        Bc6H => "BC6H",
        Bc7 => "BC7",
        Bc4 => "BC4",
        Bc5 => "BC5",
        Dxt1Crunched => "DXT1 crunched",
        Dxt5Crunched => "DXT5 crunched",
        _ => $"format {format}",
    };
}
