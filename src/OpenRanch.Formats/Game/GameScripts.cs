using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Game;

/// <summary>
/// Opens the player's install for reading script data: the asset files, the game's assemblies (for
/// layouts and enum names) and a script-component reader. Dispose it when done.
/// </summary>
public sealed class GameScripts : IDisposable
{
    public const string GameAssembly = "Assembly-CSharp";

    private readonly Dictionary<string, EnumValues> _enums = new(StringComparer.Ordinal);
    private List<(AssetRef Ref, MonoBehaviourData Data)>? _all;

    public GameScripts(GameInstall install)
    {
        Install = install;
        Assets = new AssetSet(install);
        Types = new ManagedTypes(install.ManagedDirectory);
        Reader = new MonoBehaviourReader(Assets, Types);
    }

    public GameInstall Install { get; }
    public AssetSet Assets { get; }
    public ManagedTypes Types { get; }
    public MonoBehaviourReader Reader { get; }

    /// <summary>The game's item ids (slimes, plorts, foods and so on) by number and name.</summary>
    public EnumValues IdentifiableIds => Enum("", "Identifiable/Id");

    public EnumValues Enum(string ns, string name)
    {
        var key = ns + "|" + name;
        if (!_enums.TryGetValue(key, out var values))
            _enums[key] = values = EnumValues.Read(Types, GameAssembly, ns, name);
        return values;
    }

    /// <summary>Every script component in the install, read once and kept.</summary>
    public IReadOnlyList<(AssetRef Ref, MonoBehaviourData Data)> All()
    {
        if (_all is null)
        {
            _all = [];
            foreach (var path in AssetCensus.SerializedFilePaths(Install.DataDirectory))
            {
                var file = Assets.File(Path.GetFileName(path))!;
                foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour))
                {
                    var asset = new AssetRef(file, info);
                    var data = Reader.Read(asset);
                    if (data.ScriptClass is not null && data.Data is not null)
                        _all.Add((asset, data));
                }
            }
        }
        return _all;
    }

    /// <summary>Script components of one class, e.g. "SlimeDefinition", from every file.</summary>
    public IEnumerable<(AssetRef Ref, MonoBehaviourData Data)> OfClass(string scriptClass) =>
        All().Where(m => m.Data.ScriptClass == scriptClass);

    /// <summary>Reads the script component a reference points at, if it is one.</summary>
    public (AssetRef Ref, MonoBehaviourData Data)? Follow(SerializedFile from, PPtr ptr)
    {
        if (ptr.IsNull || Assets.Resolve(from, ptr) is not { } target || target.ClassId != UnityClassId.MonoBehaviour)
            return null;
        var data = Reader.Read(target);
        return data.Data is null ? null : (target, data);
    }

    public void Dispose()
    {
        Types.Dispose();
        Assets.Dispose();
    }
}
