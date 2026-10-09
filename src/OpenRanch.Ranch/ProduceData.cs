using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// The scripts that make things grow, read from the install's item prefabs (the LookupDirector's
/// <c>identifiablePrefabs</c>): each produce's <c>ResourceCycle</c> times, each hen's <c>Reproduce</c>
/// and each chick's <c>TransformAfterTime</c>; and a crop's <c>SpawnResource</c> rules, with the prefabs
/// they name turned into item ids. See docs/behavior/produce.md and coops.md.
/// </summary>
public sealed class ProduceData
{
    private readonly GameScripts _scripts;
    private readonly Dictionary<string, AssetRef> _roots = new(StringComparer.Ordinal);
    private readonly Dictionary<(string File, long PathId), string> _idsByObject = [];
    private readonly Dictionary<string, Dictionary<string, SerializedObject>> _rootScripts = new(StringComparer.Ordinal);

    private ProduceData(GameScripts scripts) => _scripts = scripts;

    public static ProduceData Read(GameScripts scripts)
    {
        var data = new ProduceData(scripts);
        var (lookupRef, lookup) = scripts.OfClass("LookupDirector").FirstOrDefault();
        if (lookup?.Data?["identifiablePrefabs"] is not PPtr listPtr || scripts.Follow(lookupRef.File, listPtr) is not { } list)
            throw new InvalidDataException("The LookupDirector's prefab list wasn't found.");
        foreach (var item in list.Data.Data!.List("items").OfType<PPtr>())
            if (scripts.Assets.Resolve(list.Ref.File, item) is { ClassId: UnityClassId.GameObject } go && data.IdentifiableOf(go) is { } id)
            {
                data._roots.TryAdd(id, go);
                data._idsByObject.TryAdd((go.File.Path, go.PathId), id);
            }
        return data;
    }

    /// <summary>The item ids with a prefab.</summary>
    public IEnumerable<string> Ids => _roots.Keys;

    /// <summary>A script on the item prefab's root object, by class; null when it has none.</summary>
    public SerializedObject? RootScript(string itemId, string scriptClass)
    {
        if (!_roots.TryGetValue(itemId, out var root))
            return null;
        if (!_rootScripts.TryGetValue(itemId, out var scripts))
        {
            _rootScripts[itemId] = scripts = new Dictionary<string, SerializedObject>(StringComparer.Ordinal);
            foreach (var c in _scripts.Assets.Read(root, GameObjectData.Read).Components)
                if (_scripts.Follow(root.File, c) is { Data: { ScriptClass: { } cls, Data: { } d } })
                    scripts.TryAdd(cls, d);
        }
        return scripts.GetValueOrDefault(scriptClass);
    }

    /// <summary>The stage times of produce (its <c>ResourceCycle</c>), or null for an item that doesn't grow.</summary>
    public ProduceTimes? Times(string itemId) =>
        RootScript(itemId, "ResourceCycle") is { } s
            ? new ProduceTimes(F(s, "unripeGameHours"), F(s, "ripeGameHours"), F(s, "edibleGameHours"), F(s, "rottenGameHours"), F(s, "releasePrepTime"))
            : null;

    /// <summary>A hen's laying rules (its <c>Reproduce</c>), or null.</summary>
    public ReproduceRules? Reproduce(string itemId)
    {
        if (RootScript(itemId, "Reproduce") is not { } s || !_roots.TryGetValue(itemId, out var root))
            return null;
        var child = s["childPrefab"] is PPtr c ? IdOf(root.File, c) : null;
        if (child is null)
            return null;
        return new ReproduceRules(
            s["nearMateId"] is PPtr mate ? IdOf(root.File, mate) : null, F(s, "maxDistToMate"),
            s.List("densityIds").OfType<PPtr>().Select(p => IdOf(root.File, p)).OfType<string>().ToList(), F(s, "densityDist"),
            I(s, "maxDensity"), child, F(s, "minReproduceGameHours"), F(s, "maxReproduceGameHours"), F(s, "deluxeDensityFactor"));
    }

    /// <summary>What an item turns into after a time (its <c>TransformAfterTime</c>), or null.</summary>
    public TransformRules? Transform(string itemId)
    {
        if (RootScript(itemId, "TransformAfterTime") is not { } s || !_roots.TryGetValue(itemId, out var root))
            return null;
        var options = s.List("options").OfType<SerializedObject>()
            .Select(o => (Id: o["targetPrefab"] is PPtr p ? IdOf(root.File, p) : null, Weight: F(o, "weight")))
            .Where(o => o.Id is not null).Select(o => (o.Id!, o.Weight)).ToList();
        return new TransformRules(F(s, "delayGameHours"), options);
    }

    /// <summary>A crop's <c>SpawnResource</c> rules; <paramref name="file"/> is the file the script is in.</summary>
    public CropSpawnRules SpawnRules(SerializedFile file, SerializedObject s, bool forceFirstRipeness = false)
    {
        List<string> Items(string field) => s.List(field).OfType<PPtr>().Select(p => IdOf(file, p)).OfType<string>().ToList();
        return new CropSpawnRules(Items("ObjectsToSpawn"), Items("BonusObjectsToSpawn"),
            F(s, "MinObjectsSpawned"), F(s, "MaxObjectsSpawned"), F(s, "MinNutrientObjectsSpawned"),
            F(s, "MinSpawnIntervalGameHours"), F(s, "MaxSpawnIntervalGameHours"), F(s, "BonusChance"), I(s, "minBonusSelections"),
            I(s, "MaxActiveSpawns"), I(s, "MaxTotalSpawns"), s["forceDestroyLeftoversOnSpawn"] is true,forceFirstRipeness);
    }

    /// <summary>
    /// The item id a reference names: an item prefab's game object, or an <c>Identifiable</c> script on
    /// one (the hens' mate and crowd fields hold the script).
    /// </summary>
    public string? IdOf(SerializedFile from, PPtr ptr)
    {
        if (ptr.IsNull || _scripts.Assets.Resolve(from, ptr) is not { } target)
            return null;
        if (target.ClassId == UnityClassId.GameObject)
            return _idsByObject.GetValueOrDefault((target.File.Path, target.PathId)) ?? IdentifiableOf(target);
        if (_scripts.Follow(from, ptr) is { Data: { ScriptClass: "Identifiable", Data: { } d } })
            return _scripts.IdentifiableIds.NameOf(Convert.ToInt64(d["id"]));
        return null;
    }

    private string? IdentifiableOf(AssetRef gameObject)
    {
        foreach (var c in _scripts.Assets.Read(gameObject, GameObjectData.Read).Components)
            if (_scripts.Follow(gameObject.File, c) is { Data: { ScriptClass: "Identifiable", Data: { } d } })
                return _scripts.IdentifiableIds.NameOf(Convert.ToInt64(d["id"]));
        return null;
    }

    private static float F(SerializedObject s, string field) => s[field] switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        _ => throw new InvalidDataException($"{s.TypeName}.{field} wasn't found."),
    };

    private static int I(SerializedObject s, string field) => s[field] is { } v ? Convert.ToInt32(v) : throw new InvalidDataException($"{s.TypeName}.{field} wasn't found.");
}
