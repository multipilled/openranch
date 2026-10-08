using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

public class CrunchTests
{
    // Mip levels are made from the same image, so a correct decoder gives a level 1 that matches a
    // 2x2 average of level 0. A wrong decoder gives noise that doesn't match.
    [GameFact]
    public void Crunched_mip_levels_agree_with_each_other()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var file = assets.File("sharedassets2.assets")!;
        var checkedCount = 0;
        foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.Texture2D))
        {
            var tex = assets.Read(new AssetRef(file, info), Texture2DData.Read);
            if (tex.Format is not (TextureFormats.Dxt1Crunched or TextureFormats.Dxt5Crunched) || tex.Width < 64 || tex.Height < 64 || tex.MipCount < 2)
                continue;

            var (format, data, mips) = TextureFormats.GpuData(tex, assets.TextureBytes(tex));
            Assert.True(mips >= 2);
            var blockSize = format == TextureFormats.Dxt1 ? 8 : 16;
            var level0Size = (tex.Width / 4) * (tex.Height / 4) * blockSize;
            var level0 = BlockDecoder.ToRgba(format, data.AsSpan(0, level0Size), tex.Width, tex.Height);
            var level1 = BlockDecoder.ToRgba(format, data.AsSpan(level0Size), tex.Width / 2, tex.Height / 2);

            double diff = 0;
            for (var y = 0; y < tex.Height / 2; y++)
                for (var x = 0; x < tex.Width / 2; x++)
                    for (var c = 0; c < 3; c++)
                    {
                        int Px(int xx, int yy) => level0[(yy * tex.Width + xx) * 4 + c];
                        var avg = (Px(2 * x, 2 * y) + Px(2 * x + 1, 2 * y) + Px(2 * x, 2 * y + 1) + Px(2 * x + 1, 2 * y + 1)) / 4.0;
                        diff += Math.Abs(avg - level1[(y * (tex.Width / 2) + x) * 4 + c]);
                    }
            diff /= tex.Width / 2 * (tex.Height / 2) * 3;
            Assert.True(diff < 16, $"{tex.Name}: level 1 differs from level 0 by {diff:F1} on average");

            if (++checkedCount == 12)
                break;
        }
        Assert.Equal(12, checkedCount);
    }

    [Fact]
    public void Header_must_carry_the_crunch_signature()
    {
        var bytes = new byte[80];
        Assert.Throws<InvalidDataException>(() => Crunch.ReadHeader(bytes));
    }
}
