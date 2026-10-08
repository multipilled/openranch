using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>Looks up the game's names for enum numbers and back. <see cref="GameEnums"/> reads them from the install.</summary>
public interface IGameNames
{
    /// <summary>The name of <paramref name="value"/> in the enum <paramref name="label"/> (a <see cref="GameEnum"/> label), or the number as text when it has none.</summary>
    string Name(string label, long value);

    /// <summary>The number of the value named <paramref name="name"/> in the enum <paramref name="label"/>.</summary>
    int Value(string label, string name);
}

/// <summary>
/// Turns the enum numbers kept in <see cref="RanchState"/> into the game's names (and back) by
/// reading the enums from the player's own install, through assembly metadata. No game code is
/// loaded or run, and no table of names lives in openranch.
/// </summary>
public sealed class GameEnums : IGameNames, IDisposable
{
    private readonly ManagedTypes _types;
    private readonly bool _ownsTypes;
    private readonly Dictionary<string, EnumValues> _cache = new(StringComparer.Ordinal);

    public GameEnums(GameInstall install) : this(new ManagedTypes(install.ManagedDirectory), ownsTypes: true)
    {
    }

    public GameEnums(ManagedTypes types) : this(types, ownsTypes: false)
    {
    }

    private GameEnums(ManagedTypes types, bool ownsTypes)
    {
        _types = types;
        _ownsTypes = ownsTypes;
    }

    /// <summary>
    /// The values of one enum, by its label in <see cref="GameEnum"/>: "Outer.Inner" for an enum
    /// nested in a class (the usual case), or "Namespace.Name".
    /// </summary>
    public EnumValues Get(string label)
    {
        if (_cache.TryGetValue(label, out var cached))
            return cached;
        var type = _types.Find(GameScripts.GameAssembly, "", label.Replace('.', '/'));
        if (type is null && label.LastIndexOf('.') is var dot and > 0)
            type = _types.Find(GameScripts.GameAssembly, label[..dot], label[(dot + 1)..]);
        if (type is null)
            throw new KeyNotFoundException($"The enum {label} wasn't found in {GameScripts.GameAssembly}.");
        return _cache[label] = EnumValues.Read(type);
    }

    /// <summary>The name of <paramref name="value"/> in the enum <paramref name="label"/>, or the number as text when it has none.</summary>
    public string Name(string label, long value) => Get(label).NameOf(value);

    /// <summary>The number of the value named <paramref name="name"/> in the enum <paramref name="label"/>.</summary>
    public int Value(string label, string name) => checked((int)Get(label).ValueOf(name));

    public string Item(int id) => Name(GameEnum.ItemId, id);
    public string PlotType(int type) => Name(GameEnum.PlotType, type);
    public string PlotUpgrade(int upgrade) => Name(GameEnum.PlotUpgrade, upgrade);

    public void Dispose()
    {
        if (_ownsTypes)
            _types.Dispose();
    }
}
