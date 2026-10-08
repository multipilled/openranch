using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Text;

/// <summary>
/// The game's translated text: every message bundle in every language, read from the TextAssets
/// that the resource index lists under <c>i18n/&lt;language&gt;/&lt;bundle&gt;</c>.
/// </summary>
public sealed class GameText
{
    public const string Folder = "i18n";
    public const string DefaultLanguage = "en";
    public const string GlobalBundle = "global";

    private readonly Dictionary<(string Language, string Name), TextBundle> _bundles;

    public IReadOnlyList<string> Languages { get; }
    public IReadOnlyList<TextBundle> Bundles { get; }

    private GameText(List<TextBundle> bundles)
    {
        Bundles = bundles;
        _bundles = bundles.ToDictionary(b => (b.Language, b.Name));
        Languages = bundles.Select(b => b.Language).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    public static GameText Load(AssetSet assets)
    {
        var index = ResourceIndex.Read(assets);
        var bundles = new List<TextBundle>();
        foreach (var (path, asset) in index.Under(assets, Folder + "/"))
        {
            if (asset.ClassId != UnityClassId.TextAsset)
                continue;
            // i18n/<language>/<bundle>; a bundle name may itself contain '/'.
            var rest = path[(Folder.Length + 1)..];
            var slash = rest.IndexOf('/');
            if (slash <= 0 || slash == rest.Length - 1)
                continue;
            var text = assets.Read(asset, TextAssetData.Read);
            bundles.Add(TextBundle.Parse(rest[..slash].ToLowerInvariant(), rest[(slash + 1)..], text.Text));
        }
        bundles.Sort((a, b) => (a.Language, a.Name).CompareTo((b.Language, b.Name)));
        return new GameText(bundles);
    }

    public IEnumerable<string> BundleNames(string language) =>
        Bundles.Where(b => b.Language == language).Select(b => b.Name);

    /// <summary>
    /// The bundle for a language, falling back as the game does: the full language tag (zh-hans),
    /// then its first part (zh), then English.
    /// </summary>
    public TextBundle? Bundle(string language, string name)
    {
        foreach (var lang in Fallbacks(language))
            if (_bundles.TryGetValue((lang, name), out var bundle))
                return bundle;
        return null;
    }

    private static IEnumerable<string> Fallbacks(string language)
    {
        var lang = language.ToLowerInvariant();
        yield return lang;
        var dash = lang.IndexOf('-');
        if (dash > 0)
            yield return lang[..dash];
        yield return DefaultLanguage;
    }

    /// <summary>
    /// Looks a key up in a bundle, then in the bundle it names as its parent, and finally in the
    /// global bundle. Returns null when no bundle on the way has the key.
    /// </summary>
    public string? Get(string language, string bundle, string key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? name = bundle;
        while (name is not null && seen.Add(name))
        {
            var b = Bundle(language, name);
            if (b?[key] is { } value)
                return value;
            name = b?.Parent ?? (name == GlobalBundle ? null : GlobalBundle);
        }
        return null;
    }

    /// <summary>Finds which bundles of a language hold a key, for tools that only know the key.</summary>
    public IEnumerable<(TextBundle Bundle, string Value)> Find(string language, string key) =>
        Bundles.Where(b => b.Language == language.ToLowerInvariant() && b.Entries.ContainsKey(key)).Select(b => (b, b[key]!));
}
