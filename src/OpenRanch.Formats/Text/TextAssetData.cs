using System.Text;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Text;

/// <summary>A Unity TextAsset: a name and the bytes of the original text file.</summary>
public sealed record TextAssetData(string Name, byte[] Bytes)
{
    public static TextAssetData Read(EndianReader r) => new(r.ReadAlignedString(), r.ReadByteArray());

    /// <summary>The bytes as UTF-8, without a byte-order mark.</summary>
    public string Text
    {
        get
        {
            var span = Bytes.AsSpan();
            if (span.StartsWith(Encoding.UTF8.Preamble))
                span = span[Encoding.UTF8.Preamble.Length..];
            return Encoding.UTF8.GetString(span);
        }
    }
}
