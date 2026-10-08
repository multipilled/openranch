using OpenRanch.Formats.Text;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

public class TextTests
{
    [Fact]
    public void Bundle_lines_are_keys_and_values()
    {
        var bundle = TextBundle.Parse("en", "test", """
            # a comment
            l.carrot = Carrot
            m.desc.carrot=  Crunchy and orange.

            l.sum = 2 \= 1 + 1
            m.two_lines = First\nSecond
            m.backslash = a&bsol;b
            m.hyphen = soft­hyphen
            l.carrot = Carrot Two
            """.Replace("\r\n", "\n"));

        Assert.Equal("Carrot Two", bundle["l.carrot"]);
        Assert.Equal("Crunchy and orange.", bundle["m.desc.carrot"]);
        Assert.Equal("2 = 1 + 1", bundle["l.sum"]);
        Assert.Equal("First\nSecond", bundle["m.two_lines"]);
        Assert.Equal("a\\b", bundle["m.backslash"]);
        Assert.Equal("soft­hyphen", bundle["m.hyphen"]);
        Assert.Equal(6, bundle.Entries.Count);
        Assert.Empty(bundle.MalformedLines);
    }

    [Fact]
    public void A_trailing_backslash_joins_lines()
    {
        var bundle = TextBundle.Parse("en", "test", "m.long = one \\\r\n\t   two\r\nl.next = x\r\n");
        Assert.Equal("one two", bundle["m.long"]);
        Assert.Equal("x", bundle["l.next"]);
    }

    [Fact]
    public void Lines_without_exactly_one_separator_are_set_aside()
    {
        var bundle = TextBundle.Parse("en", "test", "just words\na = b = c\nok = yes\n");
        Assert.Equal(2, bundle.MalformedLines.Count);
        Assert.Equal("yes", bundle["ok"]);
        Assert.Single(bundle.Entries);
    }

    [GameFact]
    public void Every_text_asset_reads_to_its_exact_end()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var file = assets.File("resources.assets")!;
        var count = 0;
        foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.TextAsset))
        {
            var r = assets.Reader(new AssetRef(file, info));
            TextAssetData.Read(r);
            Assert.Equal(r.Length, r.Position);
            count++;
        }
        Assert.True(count > 0);
    }

    [GameFact]
    public void English_text_has_known_messages()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var text = GameText.Load(assets);

        Assert.Contains("en", text.Languages);
        Assert.Equal("Pink Slime", text.Get("en", "actor", "l.pink_slime"));
        Assert.Equal("Pink Plort", text.Get("en", "actor", "l.pink_plort"));
        // A missing key in another bundle is found through the global bundle or the bundle's parent.
        Assert.All(text.BundleNames("en"), name => Assert.NotNull(text.Bundle("en", name)));
        Assert.All(text.Bundles.Where(b => b.Language == "en"), b => Assert.Empty(b.MalformedLines));
    }

    [GameFact]
    public void Every_language_has_its_bundles_in_english_too()
    {
        using var assets = new AssetSet(GameFactAttribute.Install!);
        var text = GameText.Load(assets);
        var english = text.BundleNames("en").ToHashSet();

        Assert.Equal(10, text.Languages.Count);
        foreach (var language in text.Languages)
        {
            var names = text.BundleNames(language).ToHashSet();
            Assert.True(names.IsSubsetOf(english), $"{language} has bundles English lacks");
            Assert.NotNull(text.Get(language, "actor", "l.pink_slime"));
        }
        // Only English has the build bundle; other languages get the English file as a whole.
        Assert.Equal("en", text.Bundle("de", "build")?.Language);
    }
}
